using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Handling;
using Archivist.Building.Interaction;
using Archivist.Building.Interactables;
using Archivist.Building.Sheets;
using Archivist.Generation;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Reproduces an exact case: this island, these sheets, on the floor, now.
    ///
    /// <para>The draw path is already deterministic — the same island index yields the same
    /// island and the same five sheets — but "deterministic" is not the same as
    /// "reproducible". A bug that shows on one sheet in ten needs that sheet named and
    /// summoned, not waited for. This lists an island's entire inventory so a case can be
    /// written down as <c>LandSurvey:7</c> and re-created on demand.</para>
    ///
    /// <para>Editor-only, and it drives the same code the crate does. Nothing here is a
    /// parallel implementation — <see cref="MapCrate.Render"/> and
    /// <see cref="SheetSpawner.Place"/> are the shipping path.</para>
    /// </summary>
    public sealed class SheetTestBench : EditorWindow
    {
        /// <summary>
        /// One opening of the crate, without the window, the keypress or play mode: a binder
        /// of island 0's sheets and the loose debug sheet beside it.
        ///
        /// <para>It is how the survival of runtime-generated meshes across a domain reload
        /// gets checked — draw, recompile, look again — and it is the only way to exercise the
        /// delivery in edit mode, since <c>MapCrate.Open</c> is a coroutine and coroutines do
        /// not run there. Everything it calls is the shipping path: <see cref="MapCrate.Fill"/>
        /// off the crate, <see cref="BinderSpawner.Create"/> and <see cref="BinderSpawner.Place"/>
        /// off the spawner. The one thing it fakes is the thread — <c>Fill</c> runs inline
        /// here, and the room stalls for as long as an island takes.</para>
        /// </summary>
        [MenuItem("Archivist/Quick · Draw Crate")]
        public static void QuickDraw()
        {
            var generator = FindFirstObjectByType<IslandGenerator>();
            var spawner = FindFirstObjectByType<SheetSpawner>();
            var binders = FindFirstObjectByType<BinderSpawner>();
            var crate = FindFirstObjectByType<MapCrate>();

            if (generator == null || spawner == null || binders == null)
            {
                Debug.LogError("[Quick] Open POC04_Room first. Needs an IslandGenerator, a " +
                               "SheetSpawner and a BinderSpawner.");
                return;
            }

            ulong seed = generator.SeedForIndex(0);
            generator.Ledger.Record(seed, 0);

            MapCrate.Opening opening = MapCrate.Fill(
                generator, seed, generator.Ledger.Snapshot(seed),
                5, true, unchecked((int)seed), 1.2);

            generator.Ledger.Describe(generator.GetOrGenerate(seed));

            Transform anchor = crate != null ? crate.transform : spawner.transform;

            // One binder per office, the way a crate delivers (Q3.1). The bench used to make
            // one folder and pour everything into it, which BinderView now refuses — and
            // refuses correctly, because that folder was three offices wearing one label.
            // One folder for the lot: a binder names an island and may hold every office of it
            // (Q3.1). The bench is not modelling how a delivery is split — the crate does that —
            // it is summoning something to look at.
            BinderView binder = binders.Create(seed, opening.IslandName);
            if (binder != null)
            {
                for (int i = 0; i < opening.Filed.Count; i++)
                {
                    SheetId id = opening.Filed[i];
                    if (generator.Ledger.MarkIssued(id)) binder.Add(id);
                }
                binders.Place(binder, anchor);
            }

            if (opening.Loose != null && generator.Ledger.MarkIssued(opening.Loose.Id))
                spawner.Place(opening.Loose, 0, 1, anchor);

            Debug.Log($"[Quick] {(binder != null ? binder.Describe() : "no binder")}" +
                      $"{(opening.Loose != null ? $" + loose {opening.Loose.Id}" : "")}");
        }

        [MenuItem("Archivist/Quick · Clear Sheets")]
        public static void QuickClear()
        {
            var spawner = FindFirstObjectByType<SheetSpawner>();
            var binders = FindFirstObjectByType<BinderSpawner>();

            if (spawner != null) spawner.ClearAll();
            if (binders != null) binders.ClearAll();

            Debug.Log("[Quick] Cleared the floor — sheets and binders. The ledger is untouched: " +
                      "clearing the floor un-issues nothing.");
        }

        [MenuItem("Archivist/Sheet Test Bench")]
        public static void Open()
        {
            GetWindow<SheetTestBench>("Sheet Bench").minSize = new Vector2(420f, 480f);
        }

        // --- island selection
        bool useRawSeed;
        string rawSeedHex = "743A6763368B6692";
        int islandIndex;

        // --- draw settings
        int sheetCount = 5;
        double pixelsPerPaperMm = 1.2;
        string sheetSpec = "LandSurvey:2, Antiquarian:1";   // 2 = NE; a survey has four (Q1.1)

        // --- state
        Island island;
        Vector2 inventoryScroll;
        Vector2 reportScroll;
        string report = "";

        void OnGUI()
        {
            IslandGenerator generator = FindFirstObjectByType<IslandGenerator>();
            SheetSpawner spawner = FindFirstObjectByType<SheetSpawner>();
            MapCrate crate = FindFirstObjectByType<MapCrate>();
            PlayerHands hands = FindFirstObjectByType<PlayerHands>();

            if (generator == null || spawner == null)
            {
                EditorGUILayout.HelpBox("Open POC04_Room. Needs an IslandGenerator and a SheetSpawner.",
                                        MessageType.Warning);
                return;
            }

            DrawIslandSection(generator);
            DrawInventorySection();
            DrawDrawSection(generator, spawner, crate);
            DrawWorldSection(spawner, hands);
            DrawDiagnosticsSection();
            DrawReport();
        }

        // ------------------------------------------------------------------

        void DrawIslandSection(IslandGenerator generator)
        {
            EditorGUILayout.LabelField("Island", EditorStyles.boldLabel);

            useRawSeed = EditorGUILayout.ToggleLeft("Use a raw seed (otherwise island index)", useRawSeed);
            using (new EditorGUI.DisabledScope(!useRawSeed))
                rawSeedHex = EditorGUILayout.TextField("Seed (hex)", rawSeedHex);
            using (new EditorGUI.DisabledScope(useRawSeed))
                islandIndex = EditorGUILayout.IntField("Island index", islandIndex);

            EditorGUILayout.LabelField("Resolved", ResolveSeed(generator).ToString("X16"));

            if (GUILayout.Button("Generate (into the cache)"))
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                island = generator.GetOrGenerate(ResolveSeed(generator));
                report = $"{island.Name} ({island.Seed:X16}) — {island.Surveys.Count} surveys, " +
                         $"{island.TotalSheets} sheets, {watch.ElapsedMilliseconds} ms\n" +
                         $"cache {generator.Cache.Count} held, {generator.Cache.Hits} hit / {generator.Cache.Misses} miss";
            }
            EditorGUILayout.Space();
        }

        void DrawInventorySection()
        {
            if (island == null) return;

            EditorGUILayout.LabelField($"Inventory — {island.Name}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Click a sheet to add it to the list below.", EditorStyles.miniLabel);

            inventoryScroll = EditorGUILayout.BeginScrollView(inventoryScroll, GUILayout.Height(140f));
            for (int s = 0; s < island.Surveys.Count; s++)
            {
                Survey survey = island.Surveys[s];
                string tag = survey.Spec.IsWholeIsland ? "Whole" : survey.Spec.Office.ToString();

                EditorGUILayout.LabelField($"{tag}  {survey.Spec.Year}  1:{survey.Spec.Scale.Denominator}  " +
                                           $"{survey.SheetCount} sheets", EditorStyles.miniBoldLabel);

                EditorGUILayout.BeginHorizontal();
                for (int i = 0; i < survey.SheetCount; i++)
                {
                    if (i > 0 && i % 12 == 0)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                    }
                    if (GUILayout.Button(survey.Sheets[i].Number.ToString(), GUILayout.Width(28f)))
                        Append($"{tag}:{survey.Sheets[i].Number}");
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.Space();
        }

        void DrawDrawSection(IslandGenerator generator, SheetSpawner spawner, MapCrate crate)
        {
            EditorGUILayout.LabelField("Draw", EditorStyles.boldLabel);

            pixelsPerPaperMm = EditorGUILayout.DoubleField("Pixels per paper mm", pixelsPerPaperMm);
            sheetCount = EditorGUILayout.IntField("Random sheet count", sheetCount);
            sheetSpec = EditorGUILayout.TextField("Named sheets", sheetSpec);
            EditorGUILayout.LabelField("Office:Number, comma separated. 'Whole' for the whole-island survey.",
                                       EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Draw random unissued"))
                Spawn(generator, spawner, crate, PickRandom(generator));
            if (GUILayout.Button("Draw named sheets"))
                Spawn(generator, spawner, crate, PickNamed(generator));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        void DrawWorldSection(SheetSpawner spawner, PlayerHands hands)
        {
            EditorGUILayout.LabelField("World", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(hands == null))
            {
                if (GUILayout.Button("Take first")) TakeFirst(spawner, hands);
                if (GUILayout.Button("Drop held")) report = hands.Drop() ? "Dropped." : "Nothing in hand.";
            }
            if (GUILayout.Button("Resolve all")) ResolveAll(spawner);
            if (GUILayout.Button("Clear"))
            {
                spawner.ClearAll();
                report = "Cleared. The ledger is untouched — clearing the floor is not un-issuing anything.";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        /// <summary>
        /// The reach numbers, worked through. A sheet lies 1.65 m below the eye, so most of
        /// the reach budget is spent going down before any of it goes forward — which is why
        /// a sheet a comfortable step away can be unreachable while a crate at the same
        /// distance is not.
        /// </summary>
        void DrawDiagnosticsSection()
        {
            var interactor = FindFirstObjectByType<PlayerInteractor>();
            if (interactor == null) return;

            EditorGUILayout.LabelField("Reach", EditorStyles.boldLabel);

            var so = new SerializedObject(interactor);
            SerializedProperty reachProp = so.FindProperty("reach");
            SerializedProperty logProp = so.FindProperty("logProbe");

            EditorGUI.BeginChangeCheck();
            float reach = EditorGUILayout.Slider("Reach (m)", reachProp.floatValue, 1f, 5f);
            bool log = EditorGUILayout.ToggleLeft("Log what the aim ray finds", logProp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                reachProp.floatValue = reach;
                logProp.boolValue = log;
                so.ApplyModifiedProperties();
            }

            float eyeHeight = interactor.Eye != null
                ? interactor.Eye.position.y - interactor.transform.position.y
                : 1.65f;

            float floorReach = reach > eyeHeight
                ? Mathf.Sqrt(reach * reach - eyeHeight * eyeHeight)
                : 0f;

            var handsObj = FindFirstObjectByType<PlayerHands>();
            if (handsObj != null)
            {
                var hso = new SerializedObject(handsObj);
                SerializedProperty handLog = hso.FindProperty("logHandling");

                EditorGUI.BeginChangeCheck();
                bool handOn = EditorGUILayout.ToggleLeft("Log takes and drops", handLog.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    handLog.boolValue = handOn;
                    hso.ApplyModifiedProperties();
                }
            }

            EditorGUILayout.HelpBox(
                $"Eye {eyeHeight:0.00} m up.  Furthest a sheet on the floor can be and still be aimed at: " +
                $"{floorReach:0.00} m horizontally.\n" +
                $"Sheets are laid out up to ~2.5 m from the crate, so anything past {floorReach:0.00} m " +
                $"is out of range no matter where it is aimed.",
                floorReach < 2.5f ? MessageType.Warning : MessageType.Info);
            EditorGUILayout.Space();
        }

        void DrawReport()
        {
            if (string.IsNullOrEmpty(report)) return;
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            EditorGUILayout.SelectableLabel(report, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------

        ulong ResolveSeed(IslandGenerator generator)
        {
            if (!useRawSeed) return generator.SeedForIndex(islandIndex);

            ulong parsed;
            string text = (rawSeedHex ?? "").Trim().Replace("0x", "");
            return ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0UL;
        }

        Island Ensure(IslandGenerator generator)
        {
            ulong seed = ResolveSeed(generator);
            if (island == null || island.Seed != seed) island = generator.GetOrGenerate(seed);

            // The bench reaches islands without reserving them, so the ledger would otherwise
            // meet one for the first time through a MarkIssued — knowing its seed but neither
            // its name nor how many sheets it has, and so unable to report progress for an
            // island the bench has been drawing from all afternoon.
            generator.Ledger.Record(seed, useRawSeed ? -1 : islandIndex);
            generator.Ledger.Describe(island);
            return island;
        }

        List<Sheet> PickRandom(IslandGenerator generator)
        {
            Island target = Ensure(generator);
            HashSet<SheetId> issued = generator.Ledger.Snapshot(target.Seed);
            return SheetPicker.PickUnissued(target, sheetCount, issued, unchecked((int)target.Seed));
        }

        List<Sheet> PickNamed(IslandGenerator generator)
        {
            Island target = Ensure(generator);
            var wanted = new List<Sheet>();
            var missing = new List<string>();

            foreach (string entry in (sheetSpec ?? "").Split(','))
            {
                string trimmed = entry.Trim();
                if (trimmed.Length == 0) continue;

                string[] parts = trimmed.Split(':');
                int number;
                if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out number))
                {
                    missing.Add(trimmed + " (unreadable)");
                    continue;
                }

                Sheet found;
                if (TryFindByName(target, parts[0].Trim(), number, out found)) wanted.Add(found);
                else missing.Add(trimmed);
            }

            if (missing.Count > 0)
                report = "Not found: " + string.Join(", ", missing.ToArray());

            return wanted;
        }

        static bool TryFindByName(Island target, string officeOrWhole, int number, out Sheet sheet)
        {
            bool whole = string.Equals(officeOrWhole, "Whole", StringComparison.OrdinalIgnoreCase);

            for (int s = 0; s < target.Surveys.Count; s++)
            {
                Survey survey = target.Surveys[s];
                if (whole)
                {
                    if (!survey.Spec.IsWholeIsland) continue;
                }
                else
                {
                    if (survey.Spec.IsWholeIsland) continue;
                    if (!string.Equals(survey.Spec.Office.ToString(), officeOrWhole,
                                       StringComparison.OrdinalIgnoreCase)) continue;
                }

                for (int i = 0; i < survey.SheetCount; i++)
                {
                    if (survey.Sheets[i].Number != number) continue;
                    sheet = survey.Sheets[i];
                    return true;
                }
            }
            sheet = default(Sheet);
            return false;
        }

        void Spawn(IslandGenerator generator, SheetSpawner spawner, MapCrate crate, List<Sheet> sheets)
        {
            if (sheets == null || sheets.Count == 0)
            {
                report = "Nothing to draw.";
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<SheetRender> batch = MapCrate.Render(island, sheets, pixelsPerPaperMm);
            long ms = watch.ElapsedMilliseconds;

            Transform anchor = crate != null ? crate.transform : spawner.transform;
            var text = new StringBuilder();
            text.AppendLine($"{island.Name} ({island.Seed:X16}) — {batch.Count} sheets in {ms} ms");

            for (int i = 0; i < batch.Count; i++)
            {
                generator.Ledger.MarkIssued(batch[i].Id);
                SheetView view = spawner.Place(batch[i], i, batch.Count, anchor);

                Sheet sheet = batch[i].Sheet;
                text.AppendLine($"  {batch[i].Id}  1:{sheet.Survey.Scale.Denominator}  " +
                                $"paper {sheet.Survey.Format.WidthMm:0}x{sheet.Survey.Format.HeightMm:0} mm  " +
                                $"at {view.transform.position.x:0.00}, {view.transform.position.z:0.00}");
            }
            report = text.ToString();
        }

        void TakeFirst(SheetSpawner spawner, PlayerHands hands)
        {
            SheetView[] all = SheetSpawner.AllInScene();
            for (int i = 0; i < all.Length; i++)
            {
                SheetView view = all[i];
                if (view == null) continue;

                report = hands.Take(view)
                    ? $"Holding {view.Id}. Move Player/Eye/HoldAnchor to tune the pose."
                    : "Hands are full.";
                return;
            }
            report = "No sheets on the floor.";
        }

        void ResolveAll(SheetSpawner spawner)
        {
            var generator = FindFirstObjectByType<IslandGenerator>();
            SheetView[] all = SheetSpawner.AllInScene();

            var text = new StringBuilder();
            text.AppendLine($"Resolving {all.Length} sheet(s) from SheetId alone:");

            for (int i = 0; i < all.Length; i++)
            {
                SheetView view = all[i];
                if (view == null) continue;

                Island found;
                Sheet sheet;
                if (!generator.TryResolve(view.Id, out found, out sheet))
                {
                    text.AppendLine($"  {view.Id}  UNRESOLVED");
                    continue;
                }
                text.AppendLine($"  {found.Name}  {sheet.Survey.Office} {sheet.Survey.Year}  " +
                                $"sheet {sheet.Number}  1:{sheet.Survey.Scale.Denominator}  " +
                                $"centre ({sheet.CentreGround.X:0}, {sheet.CentreGround.Y:0}) m");
            }
            report = text.ToString();
        }

        void Append(string entry)
        {
            sheetSpec = string.IsNullOrWhiteSpace(sheetSpec) ? entry : sheetSpec.Trim() + ", " + entry;
            Repaint();
        }
    }
}
