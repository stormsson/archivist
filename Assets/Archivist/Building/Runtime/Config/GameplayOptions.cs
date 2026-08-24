using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Archivist.Generation;

namespace Archivist.Building.Config
{
    /// <summary>
    /// The <c>gameplay:</c> section of <c>config/generation.yml</c>, read by this assembly and
    /// by nothing else.
    ///
    /// <para><b>Why not in <see cref="Tuning"/>, which already reads that file.</b> G8.2.
    /// <c>Archivist.Generation</c> is engine-free and its numbers define what a seed
    /// <i>means</i> — change one and every sheet in the archive describes different ground
    /// (R1.11), which is why the file carries a warning saying so and why <c>Tuning</c> carries
    /// a fingerprint. A snap assist is a comfort setting on a table in a room. Putting it in
    /// <c>Tuning</c> would drag it into that fingerprint and imply that turning it off changes
    /// an island. It does not, and the type system should say so: these two values live in a
    /// different assembly, in a different section, behind a different reader.</para>
    ///
    /// <para><b>This location is provisional.</b> <c>assistedSnap</c> is a player-facing choice
    /// and belongs in a settings screen with the rest of them (spec §9). It is in a config file
    /// because that screen does not exist yet. When it does, only this reader changes — every
    /// caller asks <see cref="AssistedSnap"/> either way.</para>
    ///
    /// <para><b>Reusing the generator's file machinery, not copying it.</b>
    /// <see cref="TuningFile.Locate"/> already answers "where is the config file" — env var
    /// first, then an upward directory walk from both the working directory and the assembly,
    /// bounded so a symlink loop cannot hang type-initialisation — and <see cref="Yaml.Read"/>
    /// already answers "what does it say". A second finder would drift out of step with the
    /// first the day someone moves the file, and a second parser would disagree with it about
    /// some line nobody has written yet. What is <i>not</i> reused is
    /// <see cref="TuningFile.Load"/>: it deliberately keys by name alone and throws the section
    /// away, because <c>Tuning</c> is the authority on where its own keys live. Here the
    /// section is the whole point, so the entries are filtered on it and a
    /// <c>GlowingHintRange</c> filed under <c>paper:</c> is simply not ours.</para>
    ///
    /// <para><b>Never throws, never blocks startup.</b> Missing file, missing key, unparseable
    /// value: the compiled default applies and a line lands in <see cref="Problems"/>. Same
    /// reasoning as <c>TuningFile</c>'s — this assembly runs inside a shipped player, where
    /// there is no config file at all and the defaults are the right answer, and "a generator
    /// that refuses to generate is worse than one that generates the documented default". The
    /// table must open whatever is on disk. <see cref="Problems"/> is a diagnostic for an
    /// editor window to show, not a warning anything logs at startup, so it can afford to be
    /// chatty about a file that is merely absent.</para>
    ///
    /// <para><b>Read once, on first touch; <see cref="Reload"/> for the editor.</b> A static
    /// constructor runs exactly once per domain and cannot be skipped by whichever of the four
    /// entry points — editor, player, acceptance harness, test runner — forgot to call an
    /// initialiser. In the editor a domain reload re-reads the file for free, so saving a
    /// changed YAML and letting Unity recompile is already enough; <see cref="Reload"/> exists
    /// for a settings window that wants the new value without one. This mirrors
    /// <c>Tuning</c>.</para>
    /// </summary>
    public static class GameplayOptions
    {
        /// <summary>The section in the file these keys must be filed under. A key of the right
        /// name in the wrong section is ignored, not adopted.</summary>
        public const string Section = "gameplay";

        public const string AssistedSnapKey = "assistedSnap";
        public const string GlowingHintRangeKey = "GlowingHintRange";

        /// <summary>Assist on by default: the table is the optional, unhurried half of the game
        /// (CLAUDE.md), and the fiddly half of laying paper out is not the part worth
        /// defending.</summary>
        public const bool DefaultAssistedSnap = true;

        /// <summary>In multiples of the dragged slab's LONGER side (G7.3).</summary>
        public const double DefaultGlowingHintRange = 1.0;

        /// <summary>Whether a dragged slab is helped into place.</summary>
        public static bool AssistedSnap { get; private set; }

        /// <summary>How far a neighbour may be and still glow, in multiples of the dragged
        /// slab's LONGER side (G7.3).</summary>
        public static double GlowingHintRange { get; private set; }

        /// <summary>The file the live values came from, or null when none was found and the
        /// compiled defaults are in force.</summary>
        public static string LoadedFrom { get; private set; }

        /// <summary>What was wrong with the file: no file, a key it never mentioned, a value
        /// that would not parse, a key given twice. Empty is the normal case.</summary>
        public static IReadOnlyList<string> Problems { get { return problems; } }

        static IReadOnlyList<string> problems = Array.Empty<string>();

        static GameplayOptions() { Reload(); }

        /// <summary>Re-reads the file. Safe to call at any time and from anywhere: it either
        /// replaces both values or leaves them at their defaults, and it cannot throw.</summary>
        public static void Reload()
        {
            var found = new List<string>();

            bool assistedSnap = DefaultAssistedSnap;
            double glowingHintRange = DefaultGlowingHintRange;

            string path = TuningFile.Locate();
            string[] lines = null;

            if (path == null)
            {
                found.Add($"no {TuningFile.RelativePath} found; gameplay defaults in force");
            }
            else
            {
                try
                {
                    lines = File.ReadAllLines(path);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    found.Add($"could not read {path}: {e.Message}");
                    path = null;
                }
            }

            var values = new Dictionary<string, string>();
            var at = new Dictionary<string, int>();

            if (lines != null)
            {
                List<Yaml.Entry> entries = Yaml.Read(lines);
                for (int i = 0; i < entries.Count; i++)
                {
                    Yaml.Entry entry = entries[i];
                    if (!string.Equals(entry.Section, Section, StringComparison.Ordinal)) continue;

                    if (values.ContainsKey(entry.Key))
                    {
                        // Said out loud for the same reason TuningFile says it: last wins, which
                        // is how a person reads the file top to bottom, but a duplicated key is
                        // how a file quietly stops meaning what its author thinks it means.
                        found.Add($"{path}:{entry.Line}: '{Section}.{entry.Key}' appears more than once; the later value wins");
                    }

                    values[entry.Key] = entry.Value;
                    at[entry.Key] = entry.Line;
                }

                assistedSnap = Bool(AssistedSnapKey, DefaultAssistedSnap, values, at, path, found);
                glowingHintRange = Number(GlowingHintRangeKey, DefaultGlowingHintRange, values, at, path, found);
            }

            AssistedSnap = assistedSnap;
            GlowingHintRange = glowingHintRange;
            LoadedFrom = lines == null ? null : path;
            problems = found;
        }

        /// <summary>
        /// One boolean, or the default and a complaint.
        ///
        /// <para>Only <c>true</c> and <c>false</c> are accepted, case-insensitively. YAML 1.1's
        /// wider vocabulary — <c>yes</c>, <c>on</c>, <c>y</c>, and the <c>no</c> that is really
        /// the country code for Norway — was considered and rejected: this file is read by a
        /// hand-written reader (<see cref="Yaml"/>) that supports one shape of YAML on purpose,
        /// and quietly accepting spellings the rest of the file never uses would invite a
        /// config file the parser cannot round-trip. A rejected spelling costs a line in
        /// <see cref="Problems"/> and the documented default, which is visible; a silently
        /// mis-read one would not be.</para>
        /// </summary>
        static bool Bool(string key, bool fallback, Dictionary<string, string> values,
                         Dictionary<string, int> at, string path, List<string> problems)
        {
            string raw;
            if (!values.TryGetValue(key, out raw))
            {
                problems.Add($"{path}: '{Section}.{key}' is not in the file; using {Text(fallback)}");
                return fallback;
            }

            if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return false;

            problems.Add($"{path}:{at[key]}: '{key}: {raw}' is not true or false; using {Text(fallback)}");
            return fallback;
        }

        /// <summary>
        /// One number, or the default and a complaint.
        ///
        /// <para><see cref="CultureInfo.InvariantCulture"/>, always. The machine this is written
        /// on formats a decimal with a comma; parsing <c>1.0</c> under that culture yields ten,
        /// and a hint range ten times too large is exactly the kind of wrong that looks like a
        /// design decision rather than a bug. The file is one file for every machine, so it has
        /// one culture.</para>
        ///
        /// <para>Non-finite and negative are refused rather than passed through: NaN parses
        /// happily and then makes every distance comparison downstream answer false, which
        /// presents as "the assist stopped working" with nothing anywhere to say why.</para>
        /// </summary>
        static double Number(string key, double fallback, Dictionary<string, string> values,
                             Dictionary<string, int> at, string path, List<string> problems)
        {
            string raw;
            if (!values.TryGetValue(key, out raw))
            {
                problems.Add($"{path}: '{Section}.{key}' is not in the file; using {Text(fallback)}");
                return fallback;
            }

            double v;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                problems.Add($"{path}:{at[key]}: '{key}: {raw}' is not a number; using {Text(fallback)}");
                return fallback;
            }

            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0.0)
            {
                problems.Add($"{path}:{at[key]}: '{key}: {raw}' is not a distance; using {Text(fallback)}");
                return fallback;
            }

            return v;
        }

        static string Text(bool value) { return value ? "true" : "false"; }

        static string Text(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
