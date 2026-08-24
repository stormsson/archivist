using System;
using System.Collections.Generic;
using System.Globalization;
using Archivist.Generation;
using UnityEditor;
using UnityEngine;

namespace Archivist.Editor
{
    /// <summary>
    /// `Archivist -> Tuning`. Every number the generator reads, in one list, editable while the
    /// editor is running.
    ///
    /// <para><b>Why it exists.</b> The 102 values in <see cref="Tuning"/> were <c>const</c>. Moving
    /// one of them — the atoll ring width, a peak NMS radius, the sheet overlap fraction — meant
    /// editing C#, waiting out a compile, waiting out a domain reload, and only then looking at the
    /// island. That is a thirty-second round trip on a change whose only test is whether the result
    /// looks right, and a thirty-second round trip is enough to stop anyone from trying the fourth
    /// value. The numbers now come from <c>config/generation.yml</c>, read once at startup and
    /// otherwise frozen; this window is the other half of that — it moves a value without a
    /// recompile, so a value can be tried rather than argued about.</para>
    ///
    /// <para><b>Why editing here works at all, and why the shipped game pays nothing for it.</b>
    /// <c>Archivist.Generation</c> compiles twice, once with UNITY_EDITOR defined and once without,
    /// and the parameters are a different kind of member in each. In the editor they are settable
    /// properties, which is what lets this window drag a number and regenerate an island in the same
    /// frame. In a player build they are <c>static readonly</c>, which the JIT folds into literals at
    /// the call site exactly as it did with <c>const</c>. That split was measured, not assumed —
    /// generating an island twice under each form:</para>
    /// <code>
    /// const                478 ms / 492 ms
    /// static readonly      472 ms / 481 ms
    /// settable property    565 ms / 534 ms
    /// </code>
    /// <para>which is roughly 13% for the settable form: a cost the editor pays in exchange for live
    /// tuning and the shipped game does not pay at all. The determinism hash was identical under all
    /// three, so the two builds cannot generate different islands from the same seed — the split is
    /// a difference of mutability, never of arithmetic.</para>
    ///
    /// <para><b>What that means for you, holding the window.</b> What you change here is live for
    /// THIS domain and nothing more. It is not written anywhere until Save. The next script
    /// recompile, the next entry into play mode, the next domain reload for any reason at all —
    /// each one re-reads the YAML from disk and takes every value back to what is on it. An
    /// afternoon of unsaved tuning is undone by touching a script. Save writes the live values back
    /// to <c>config/generation.yml</c>; that is the only thing here that writes.</para>
    ///
    /// <para><b>And the reason the fingerprint is in the header.</b> An island is a pure function of
    /// its seed AND of these numbers (R1.11). Change one and every seed in the archive means
    /// something else: the sheet identities keep their numbers — sheet 7 is still sheet 7 — while
    /// the ground underneath them moves, so a ledger written before the change describes paper that
    /// no longer exists. The fingerprint is the hash of all 102 live values, and it is on screen so
    /// that "did the ground move?" is a thing you can read rather than a thing you have to
    /// remember. Two runs agreeing on the fingerprint agree on every island.</para>
    ///
    /// <para>IMGUI rather than UI Toolkit, unlike <see cref="IslandDebugWindow"/> next door. That
    /// window paints geometry and needs Painter2D; this one is 102 rows of label and field, which is
    /// what <c>EditorGUILayout</c> is for. An earlier attempt at a UI Toolkit version rebuilt its
    /// rows to mark overrides and to re-filter on every keystroke of the search box, which was more
    /// machinery than immediate mode needs to do the same job.</para>
    ///
    /// <para>Everything drawn below is guarded against <see cref="Tuning.Parameters"/> being null or
    /// empty. It genuinely can be, mid-domain-reload, and an exception thrown out of
    /// <c>OnGUI</c> is not a one-off: it is thrown again on the next repaint and the one after, so
    /// the editor fills with the same error and the window cannot be closed by clicking it. A
    /// HelpBox saying there is nothing to show is always the better failure.</para>
    /// </summary>
    public sealed class TuningWindow : EditorWindow
    {
        /// <summary>EditorPrefs key prefix for the per-scope foldout state. One key per scope.</summary>
        const string FoldoutPrefKey = "Archivist.Tuning.Foldout.";

        /// <summary>Width of the name column, in points. Wide enough for the longest parameter name
        /// (<c>WholeIslandFallbackScaleDenominator</c>) at the default inspector font.</summary>
        const float NameWidth = 260.0f;

        /// <summary>Width of the value field. Doubles want the room; ints do not mind having it.</summary>
        const float ValueWidth = 110.0f;

        /// <summary>Width of the per-row reset button.</summary>
        const float ResetWidth = 52.0f;

        [SerializeField] string _search = string.Empty;
        [SerializeField] Vector2 _scroll;

        /// <summary>
        /// Set the moment any <c>Set</c> happens; cleared by Save and by Reload.
        ///
        /// <para>Deliberately NOT a <c>[SerializeField]</c>. A domain reload re-reads the YAML and
        /// takes every live value back to disk, so after one there is by definition nothing unsaved
        /// — and a serialized flag would survive the reload and keep claiming otherwise. A plain
        /// field is wiped by the reload, which is exactly right.</para>
        /// </summary>
        bool _dirty;

        /// <summary>Last failure reported by <see cref="Tuning.Save"/>, or null. Shown, not thrown.</summary>
        string _saveError;

        // ---- scope -> parameter index buckets, rebuilt only when the filter or the list changes ----
        List<List<int>> _buckets;
        string _bucketsSearch;
        int _bucketsCount = -1;

        [MenuItem("Archivist/Tuning")]
        public static void Open()
        {
            TuningWindow w = GetWindow<TuningWindow>();
            w.titleContent = new GUIContent("Tuning");
            w.minSize = new Vector2(560.0f, 400.0f);
            w.Show();
        }

        /// <summary>True while the live values differ from what is on disk.</summary>
        public bool IsDirty { get { return _dirty; } }

        void OnGUI()
        {
            DrawToolbar();

            IReadOnlyList<Tuning.Parameter> parameters = Tuning.Parameters;
            IReadOnlyList<string> scopes = Tuning.Scopes;

            DrawHeader();
            DrawProblems();

            if (_saveError != null)
            {
                EditorGUILayout.HelpBox("Save failed: " + _saveError, MessageType.Error);
            }

            if (parameters == null || parameters.Count == 0)
            {
                // Mid-reload, or a Tuning that failed to initialise. Say so and draw nothing else.
                EditorGUILayout.HelpBox(
                    "No parameters are available. The generator's tuning table is either still "
                    + "loading (a domain reload is in progress) or failed to initialise — check the "
                    + "console. Reload once the editor is idle.", MessageType.Warning);
                return;
            }

            RebuildBuckets(parameters, scopes);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int shown = 0;
            if (scopes != null)
            {
                for (int s = 0; s < scopes.Count; s++)
                {
                    shown += DrawScope(parameters, scopes[s], _buckets[s]);
                }
            }

            if (shown == 0 && _search.Length > 0)
            {
                EditorGUILayout.HelpBox("No parameter name contains \"" + _search + "\".", MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60.0f)))
            {
                Reload();
            }

            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50.0f)))
            {
                Save();
            }

            if (GUILayout.Button("Reset All To Defaults", EditorStyles.toolbarButton, GUILayout.Width(150.0f)))
            {
                ResetAll();
            }

            // The asterisk is the whole unsaved-state indicator. It sits in the toolbar rather than
            // only in the title because a docked window's tab is often too narrow to read.
            GUILayout.Label(_dirty ? "*  unsaved" : string.Empty, EditorStyles.miniLabel, GUILayout.Width(70.0f));

            GUILayout.FlexibleSpace();

            GUILayout.Label("filter", EditorStyles.miniLabel, GUILayout.Width(34.0f));
            string search = GUILayout.TextField(_search, SearchFieldStyle(), GUILayout.Width(200.0f));
            if (search != _search)
            {
                _search = search;
            }

            if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20.0f)))
            {
                _search = string.Empty;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// The toolbar search style, looked up by name rather than named in code.
        ///
        /// <para><c>EditorStyles.toolbarSearchField</c> has moved in and out of the public API across
        /// versions, and a missing property is a compile error rather than a cosmetic one. Asking the
        /// skin for it by name degrades to a plain toolbar text field instead, which looks slightly
        /// wrong and works exactly the same.</para>
        /// </summary>
        static GUIStyle SearchFieldStyle()
        {
            GUIStyle style = GUI.skin.FindStyle("ToolbarSearchTextField");
            if (style == null)
            {
                style = GUI.skin.FindStyle("ToolbarSeachTextField");   // Unity's own historical typo.
            }

            return style ?? EditorStyles.toolbarTextField;
        }

        /// <summary>Where the values came from, and the hash of what they are now.</summary>
        void DrawHeader()
        {
            string from = Tuning.LoadedFrom;
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(from) ? "compiled defaults — no config file found" : from,
                EditorStyles.miniLabel);

            EditorGUILayout.LabelField(
                "fingerprint " + Tuning.Fingerprint.ToString("X16", CultureInfo.InvariantCulture),
                EditorStyles.miniLabel);
        }

        void DrawProblems()
        {
            IReadOnlyList<string> problems = Tuning.Problems;
            if (problems == null || problems.Count == 0)
            {
                return;
            }

            string[] lines = new string[problems.Count];
            for (int i = 0; i < problems.Count; i++)
            {
                lines[i] = "• " + problems[i];
            }

            EditorGUILayout.HelpBox(
                "The config file was read with complaints:\n" + string.Join("\n", lines),
                MessageType.Warning);
        }

        /// <summary>One foldout and its rows. Returns how many rows were drawn.</summary>
        int DrawScope(IReadOnlyList<Tuning.Parameter> parameters, string scope, List<int> indices)
        {
            if (indices == null || indices.Count == 0)
            {
                // Either an empty scope or one the filter excluded entirely. Do not draw the header
                // for it: a column of empty foldouts is harder to read than a short list.
                return 0;
            }

            // While a filter is typed the matches are what the reader is looking at, so every scope
            // holding one opens regardless of its stored state. The stored state is not touched, so
            // clearing the filter returns the window to how it was left.
            bool filtering = _search.Length > 0;
            string key = FoldoutPrefKey + scope;
            bool stored = EditorPrefs.GetBool(key, true);
            bool open = filtering || stored;

            bool now = EditorGUILayout.Foldout(open, scope + "  (" + indices.Count + ")", true);
            if (!filtering && now != stored)
            {
                EditorPrefs.SetBool(key, now);
            }

            if (!now)
            {
                return indices.Count;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= parameters.Count)
                {
                    continue;
                }

                DrawParameter(parameters[index]);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            return indices.Count;
        }

        /// <summary>
        /// One row: name, value, the default when it has been moved off it, and a reset.
        ///
        /// <para>An overridden parameter is marked by a BOLD name and by having its default spelled
        /// out beside the field — never by colour alone. Half the point of the row is to be readable
        /// in a screenshot pasted into a findings document, and colour does not survive that any
        /// better than it survives colour-blindness.</para>
        /// </summary>
        void DrawParameter(Tuning.Parameter p)
        {
            bool overridden = !SameValue(p.Value, p.Default);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(
                p.Name,
                overridden ? EditorStyles.boldLabel : EditorStyles.label,
                GUILayout.Width(NameWidth));

            if (p.IsInteger)
            {
                // The API carries every parameter as a double, integer-typed ones included. Round
                // rather than truncate so that a field showing 5 and nudged down does not land on 4
                // through a 4.999999 that never appeared on screen.
                int current = (int)Math.Round(p.Value, MidpointRounding.AwayFromZero);
                int edited = EditorGUILayout.IntField(current, GUILayout.Width(ValueWidth));
                if (edited != current)
                {
                    Apply(p, edited);
                }
            }
            else
            {
                // Delayed: a double field committing per keystroke sees "0", then "0.0", then "0.04"
                // while 0.04 is being typed, and each of those is a value the generator would be
                // asked to regenerate on. Commit on Return or on losing focus instead.
                double edited = EditorGUILayout.DelayedDoubleField(p.Value, GUILayout.Width(ValueWidth));
                if (!SameValue(edited, p.Value))
                {
                    Apply(p, edited);
                }
            }

            if (overridden)
            {
                EditorGUILayout.LabelField(
                    "was " + p.Default.ToString("G6", CultureInfo.InvariantCulture),
                    EditorStyles.miniLabel,
                    GUILayout.Width(120.0f));
            }
            else
            {
                GUILayout.Space(120.0f);
            }

            using (new EditorGUI.DisabledScope(!overridden))
            {
                if (GUILayout.Button("reset", EditorStyles.miniButton, GUILayout.Width(ResetWidth)))
                {
                    Apply(p, p.Default);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Set a live value and remember that disk no longer agrees with it.</summary>
        void Apply(Tuning.Parameter p, double value)
        {
            p.Set(value);
            _dirty = true;
            _saveError = null;
            UpdateTitle();
        }

        void ResetAll()
        {
            IReadOnlyList<Tuning.Parameter> parameters = Tuning.Parameters;
            if (parameters == null)
            {
                return;
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                Tuning.Parameter p = parameters[i];
                if (!SameValue(p.Value, p.Default))
                {
                    p.Set(p.Default);
                    _dirty = true;
                }
            }

            _saveError = null;
            UpdateTitle();
        }

        /// <summary>
        /// Re-read the YAML. Everything unsaved is gone afterwards, which is the same thing a domain
        /// reload does silently — so this is the honest version of what the editor is about to do to
        /// you anyway, and it is worth a confirmation only because it is deliberate.
        /// </summary>
        void Reload()
        {
            if (_dirty && !EditorUtility.DisplayDialog(
                    "Tuning",
                    "Reloading re-reads the config file from disk and discards every unsaved change "
                    + "in this window.",
                    "Reload", "Cancel"))
            {
                return;
            }

            Tuning.Reload();
            _dirty = false;
            _saveError = null;
            InvalidateBuckets();
            UpdateTitle();
        }

        void Save()
        {
            string error;
            if (Tuning.Save(out error))
            {
                _dirty = false;
                _saveError = null;
            }
            else
            {
                // Stay dirty. The values are still live; only the write failed, and telling the user
                // otherwise would invite them to close the window on work that was never written.
                _saveError = string.IsNullOrEmpty(error) ? "(no reason given)" : error;
            }

            UpdateTitle();
        }

        void UpdateTitle()
        {
            titleContent = new GUIContent(_dirty ? "Tuning*" : "Tuning");
            Repaint();
        }

        /// <summary>
        /// Group the parameter indices by scope, filtered by the search box.
        ///
        /// <para>Rebuilt only when the filter text or the parameter count changes, never per repaint:
        /// this runs on every OnGUI, including the ones a mouse-move generates, and 19 lists of
        /// indices allocated sixty times a second is a garbage collection the editor does not need.
        /// Values are not part of the key, because the grouping does not depend on them.</para>
        /// </summary>
        void RebuildBuckets(IReadOnlyList<Tuning.Parameter> parameters, IReadOnlyList<string> scopes)
        {
            int scopeCount = scopes != null ? scopes.Count : 0;
            bool stale = _buckets == null
                         || _buckets.Count != scopeCount
                         || _bucketsCount != parameters.Count
                         || !string.Equals(_bucketsSearch, _search, StringComparison.Ordinal);

            if (!stale)
            {
                return;
            }

            _buckets = new List<List<int>>(scopeCount);
            for (int s = 0; s < scopeCount; s++)
            {
                _buckets.Add(new List<int>());
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                Tuning.Parameter p = parameters[i];
                if (!Matches(p.Name))
                {
                    continue;
                }

                for (int s = 0; s < scopeCount; s++)
                {
                    if (string.Equals(scopes[s], p.Scope, StringComparison.Ordinal))
                    {
                        _buckets[s].Add(i);
                        break;
                    }
                }
            }

            _bucketsCount = parameters.Count;
            _bucketsSearch = _search;
        }

        void InvalidateBuckets()
        {
            _buckets = null;
            _bucketsCount = -1;
            _bucketsSearch = null;
        }

        /// <summary>Case-insensitive substring, on the name only — the YAML key is what is searched for.</summary>
        bool Matches(string name)
        {
            if (_search.Length == 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Equality for "is this still on its default".
        ///
        /// <para>Exact <c>==</c> on doubles is right here and a tolerance would be wrong: the value
        /// either came from the compiled literal, or from a YAML parse of a number a person typed,
        /// or from a field in this window. A parsed 0.38 and a literal 0.38 are the same bits.
        /// Anything that is not bit-identical is a real override, however small, and the whole point
        /// of the bold label is to say so. NaN is folded in by hand because <c>==</c> will not.</para>
        /// </summary>
        static bool SameValue(double a, double b)
        {
            if (double.IsNaN(a) && double.IsNaN(b))
            {
                return true;
            }

            return a == b;
        }
    }
}
