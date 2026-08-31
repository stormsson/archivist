using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Interactables;
using Archivist.Building.Sheets;
using Archivist.Building.Table;
using Archivist.Generation;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Editor
{
    /// <summary>
    /// Slice S1 of the cartography table: every sheet of one island, laid at its <b>true</b>
    /// pose on a ground-space board. No input, no cabinet, no snapping — a solved board,
    /// built so it can be looked at.
    ///
    /// <para><b>Why this existed before anything else.</b> The spec (§3.2) draws sheets at their
    /// GROUND footprint rather than their paper size, and sheets of one survey used to overlap
    /// by a fifth of their width, at a different size and angle per office. Whether that read as
    /// an island or as a heap was the one thing that could not be reasoned about, only seen —
    /// and F-S1.1 measured that it read.
    ///
    /// <b>None of that shape survives.</b> Quarters tile exactly (Q1.4), every office shares one
    /// cut at one scale (Q1.2), and nothing rotates. The bench's question is answered and its
    /// subject is gone; what it is now is a way to summon a board to look at.</para>
    ///
    /// <para>Editor-only, and it drives the shipping path: <see cref="MapCrate.Render"/> for
    /// the raster and <see cref="BoardSheetView.Create"/> for the slab. Nothing here is a
    /// parallel implementation — a bench that draws its own sheets would prove only that the
    /// bench works. The crate's <c>SheetView</c> is deliberately <i>not</i> what the board
    /// uses: that draws whole paper, margin included, while a sheet's ground rect describes
    /// its map area alone — see <see cref="BoardSheetView"/>.</para>
    ///
    /// <para><b>It ignores the ledger on purpose.</b> A board of the five sheets that happen to
    /// have been issued says nothing about whether a FULL board reads, and the full board is
    /// the thing being judged. Issuance is S3's problem.</para>
    /// </summary>
    public sealed class CartographyBoardBench : EditorWindow
    {
        const string BoardRootName = "BoardRoot (bench)";

        /// <summary>Well clear of the room, per spec C5.2. The Table layer already isolates the
        /// board from the room's camera and raycasts, so this is belt-and-braces — but it also
        /// means a stray sheet at the wrong scale cannot appear in the middle of the floor.</summary>
        static readonly Vector3 BoardOrigin = new Vector3(0f, -500f, 0f);

        int islandIndex;
        int maxSheets = 60;
        bool hydrographic = true, landSurvey = true, garrison = true, antiquarian = true;
        bool wholeIsland = true;

        [MenuItem("Archivist/Cartography Table · Bench")]
        public static void Open()
        {
            GetWindow<CartographyBoardBench>(false, "Board Bench", true).Show();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Slice S1 — a solved board", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Lays every sheet of one island at its true ground pose. Sheets of different " +
                "offices are different sizes because they cover different amounts of ground, " +
                "and sheets of one survey overlap by 20%. That is correct — the question is " +
                "whether it reads.", MessageType.None);

            islandIndex = EditorGUILayout.IntField("Island index", Mathf.Max(0, islandIndex));
            maxSheets = EditorGUILayout.IntField("Max sheets", Mathf.Max(1, maxSheets));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Offices", EditorStyles.miniBoldLabel);
            hydrographic = EditorGUILayout.Toggle("Hydrographic", hydrographic);
            landSurvey   = EditorGUILayout.Toggle("Land Survey", landSurvey);
            garrison     = EditorGUILayout.Toggle("Garrison", garrison);
            antiquarian  = EditorGUILayout.Toggle("Antiquarian", antiquarian);
            wholeIsland  = EditorGUILayout.Toggle("Whole-island sheet", wholeIsland);

            EditorGUILayout.Space();
            if (GUILayout.Button("Lay solved board", GUILayout.Height(28)))
                Lay(islandIndex, maxSheets, Wanted(), wholeIsland);

            if (GUILayout.Button("Clear board"))
                Clear();
        }

        HashSet<Office> Wanted()
        {
            var wanted = new HashSet<Office>();
            if (hydrographic) wanted.Add(Office.Hydrographic);
            if (landSurvey)   wanted.Add(Office.LandSurvey);
            if (garrison)     wanted.Add(Office.Garrison);
            if (antiquarian)  wanted.Add(Office.Antiquarian);
            return wanted;
        }

        /// <summary>
        /// The bench without the window: island 0, every office, whole-island sheet OFF.
        ///
        /// <para>Off because at 1:25000 that one sheet covers 19 x 12.85 km for a 6.9 km
        /// island — 564% of the land area, and about 2.4x the board's own width. It would
        /// blanket everything under it and hide the very thing this slice exists to look at.
        /// That it does not fit is itself a finding (spec §3.2): at true ground scale the
        /// whole-island sheet cannot be a placeable tile, and R6.8a's "the board's outline"
        /// probably means an underlay drawn beneath everything.</para>
        /// </summary>
        [MenuItem("Archivist/Cartography Table · Lay Solved Board")]
        public static void QuickLay()
        {
            var offices = new HashSet<Office>(Offices.All);
            Lay(0, 60, offices, wantWhole: false);
        }

        [MenuItem("Archivist/Cartography Table · Clear Board")]
        public static void Clear()
        {
            GameObject existing = GameObject.Find(BoardRootName);
            if (existing != null) DestroyImmediate(existing);
        }

        static void Lay(int islandIndex, int maxSheets, HashSet<Office> offices, bool wantWhole)
        {
            var generator = Object.FindFirstObjectByType<IslandGenerator>();
            if (generator == null)
            {
                Debug.LogError("[BoardBench] Open POC04_Room first — this needs the scene's IslandGenerator.");
                return;
            }

            TableOptions options = LoadOptions();
            float unitsPerMetre = options != null ? options.BoardUnitsPerMetre : TableOptions.DefaultBoardUnitsPerMetre;
            float padding       = options != null ? options.BoardPadding       : TableOptions.DefaultBoardPadding;
            float separation    = options != null ? options.SheetSeparation    : TableOptions.DefaultSheetSeparation;
            float pxPerMetre    = options != null ? options.BoardPixelsPerMetre : TableOptions.DefaultBoardPixelsPerMetre;

            ulong seed = generator.SeedForIndex(islandIndex);
            Island island = generator.GetOrGenerate(seed);

            var sheets = new List<Sheet>();
            for (int s = 0; s < island.Surveys.Count && sheets.Count < maxSheets; s++)
            {
                Survey survey = island.Surveys[s];
                if (survey.Spec.IsWholeIsland) { if (!wantWhole) continue; }
                else if (!offices.Contains(survey.Spec.Office)) continue;

                for (int i = 0; i < survey.Sheets.Count && sheets.Count < maxSheets; i++)
                    sheets.Add(survey.Sheets[i]);
            }

            if (sheets.Count == 0)
            {
                Debug.LogWarning("[BoardBench] Nothing selected.");
                return;
            }

            Clear();

            BoardSpace space = BoardSpace.ForIsland(island.LandBounds, padding, unitsPerMetre);

            var root = new GameObject(BoardRootName);
            root.transform.position = BoardOrigin;

            BuildMountingSheet(root.transform, space);

            // Rasterising happens through the crate's own path, so what lands on the board is
            // the same image that would land on the floor — only the resolution differs.
            List<SheetRender> renders = MapCrate.RenderForBoard(island, sheets, pxPerMetre);

            Material unlit = UnlitPaper();
            int layer = LayerMask.NameToLayer("Table");

            for (int i = 0; i < renders.Count; i++)
            {
                SheetRender render = renders[i];
                Sheet sheet = render.Sheet;

                // Sized in board units inside the quad's own vertices, so localScale stays
                // at one and this loop only ever sets a pose. BoardSheetView adds the C5.4
                // marker itself.
                BoardSheetView view = BoardSheetView.Create(render, unlit, "_BaseMap", unitsPerMetre);

                if (layer >= 0) SetLayerRecursive(view.gameObject, layer);

                V2 centre = space.ToBoard(sheet.CentreGround);

                // Y by draw index: sheets overlap, so order is a design element and not an
                // accident (§3.3).
                view.transform.SetParent(root.transform, false);
                view.transform.localPosition = new Vector3(
                    (float)centre.X, i * separation, (float)centre.Y);

                // Ground X maps to board X and ground Y to board Z, so a ground rotation that
                // takes +X toward +Y is a Unity yaw that takes +X toward +Z — and Unity's
                // positive yaw goes the other way. Hence the negation. Get this wrong and the
                // board looks plausible but mirrored, which is the hardest kind of wrong to see.
                view.transform.localRotation = Quaternion.Euler(0f, -(float)sheet.RotationDeg, 0f);
            }

            BuildCamera(root.transform, space);
            Frame(root.transform, space);

            Rect2 land = island.LandBounds;
            Debug.Log(
                $"[BoardBench] {island.Name} ({seed:X16}) — {renders.Count} sheets laid.\n" +
                $"  land {land.Width / 1000.0:0.0} x {land.Height / 1000.0:0.0} km" +
                $"  ->  board {space.BoardWidth:0.0} x {space.BoardHeight:0.0} units" +
                $"  @ {unitsPerMetre} units/m", root);
        }

        /// <summary>The pale surface the sheets sit on. A quad, because the board has no
        /// thickness worth modelling and a plane would import a mesh nobody can tune.</summary>
        static void BuildMountingSheet(Transform parent, BoardSpace space)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "MountingSheet";
            Object.DestroyImmediate(quad.GetComponent<Collider>());

            quad.transform.SetParent(parent, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(
                (float)space.BoardWidth, (float)space.BoardHeight, 1f);
            quad.transform.localPosition = new Vector3(0f, -0.01f, 0f);

            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.name = "M_MountingSheet";
            material.hideFlags = HideFlags.DontSave;
            material.color = new Color(0.94f, 0.94f, 0.93f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            int layer = LayerMask.NameToLayer("Table");
            if (layer >= 0) quad.layer = layer;
        }

        /// <summary>The board camera of spec §5.1. Built here so S1 leaves behind the rig S2
        /// needs, rather than a throwaway view.</summary>
        static void BuildCamera(Transform parent, BoardSpace space)
        {
            var go = new GameObject("BoardCamera");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 50f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = (float)space.BoardHeight * 0.5f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.13f, 0.10f);
            cam.enabled = false;   // S2 turns it on; S1 is looked at through the Scene view

            int layer = LayerMask.NameToLayer("Table");
            if (layer >= 0) cam.cullingMask = 1 << layer;
        }

        /// <summary>Snaps the Scene view to look straight down at the board. S1's whole
        /// deliverable is "look at it", so not making the user find it matters.</summary>
        static void Frame(Transform root, BoardSpace space)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;

            view.orthographic = true;
            view.rotation = Quaternion.Euler(90f, 0f, 0f);
            view.LookAt(root.position, view.rotation,
                        (float)Mathf.Max((float)space.BoardWidth, (float)space.BoardHeight) * 0.6f);
            view.Repaint();
        }

        /// <summary>Unlit, per spec §3.4 — the mockups are flat and evenly lit, and an unlit
        /// board is independent of the room's lighting and of where its root sits.</summary>
        static Material UnlitPaper()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.name = "M_BoardSheet";
            material.hideFlags = HideFlags.DontSave;
            return material;
        }

        static TableOptions LoadOptions()
        {
            string[] found = AssetDatabase.FindAssets("t:TableOptions");
            if (found.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<TableOptions>(AssetDatabase.GUIDToAssetPath(found[0]));
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
