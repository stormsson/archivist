using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Building.Interactables;
using Archivist.Building.Sheets;
using Archivist.Generation;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using Archivist.Render;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The cartography board itself: the rig of spec §5.1 — a ground-space board (§3.1), a
    /// mounting sheet, an orthographic camera looking straight down — plus the sheets currently
    /// laid on it and one cached raster per sheet the table is allowed to offer.
    ///
    /// <para><b>This is <c>CartographyBoardBench</c> productionised, not rewritten.</b> Slice S1
    /// answered the one question it existed to ask — does a board of ground-scale sheets
    /// overlapping by a fifth read, or is it a heap — with <i>it reads</i>. Every number that
    /// produced that result is carried over unchanged, including the rotation negation below,
    /// which is the one thing in the transform that is easy to get wrong in a way that still
    /// looks plausible. <b>Nothing about the geometry is reopened here.</b> This class holds no
    /// input and no UGUI, so the same board can be driven by a test, a bench or a pointer.</para>
    ///
    /// <para><b>The sheet list arrives through <see cref="ISheetSource"/> and never through the
    /// ledger</b> (§4.3). A single <c>ledger.IssuedSheets(seed)</c> in a view makes the eventual
    /// swap to a <c>FolderSheetSource</c> a silent hunt through the UI, because the wrong call
    /// still compiles and still returns sheets. The default source is built here from
    /// <c>generator.Ledger</c> and can be replaced before <see cref="Show"/>.</para>
    ///
    /// <para><b>Rendering is off the main thread and uploads one texture per frame</b> (C5.6).
    /// Island generation is a third of a second of engine-free C# that must not happen inline,
    /// and several <c>Texture2D.Apply</c> calls in one frame are a visible hitch —
    /// <c>MapCrate</c>'s comments are the authority on both. So the island resolves on a worker,
    /// the rig appears the moment its bounds are known (C5.7), the rasters are produced one at a
    /// time into a queue, and the coroutine drains exactly one per frame. One at a time rather
    /// than as a batch is what gets the first thumbnail on screen inside A2's 500 ms.</para>
    ///
    /// <para><b>One render and one upload per sheet, and this class owns it</b> (C5.5). The
    /// <c>IslandRenderer</c> pass runs once into a cached <see cref="SheetRender"/>, the upload
    /// runs once into <c>textures</c>, and slabs are built through <c>BoardSheetView</c>'s
    /// <b>borrowing</b> overload — the one that takes a <c>Texture2D</c> and does not destroy
    /// it. Uploading twice so each object owns what it draws costs about 36 MB of duplicate
    /// VRAM across a 48-sheet board (F-S1.3); one texture with two owners and one
    /// <c>Destroy</c> is worse still, because destroying one slab would blank every thumbnail on
    /// screen. <c>ownsTexture</c> is what makes the third option possible.</para>
    ///
    /// <para><b>Nothing here is an asset.</b> Every mesh, material and texture is created at
    /// runtime with <c>HideFlags.DontSave</c> and destroyed in <see cref="Hide"/> and
    /// <c>OnDestroy</c>. A cached texture is owned by nobody else: the cabinet borrows it while
    /// the table is open and must not hold it across a close.</para>
    ///
    /// <para><b>There is no model</b> (Q4.1, Q4.7). A plate lies at its ground rect and
    /// nowhere else, so the board is derived from the binders on the table every time it opens.
    /// <see cref="Hide"/> destroys every slab, mesh and texture and keeps nothing, because
    /// there is nothing a player could have arranged that a reopening would fail to
    /// reproduce.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardView : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The scene's one source of islands. The board resolves its island through " +
                 "GetOrGenerate, so opening the same table twice costs one generation.")]
        [SerializeField] IslandGenerator generator;

        [Tooltip("Feel values (§10). Null falls back to TableOptions' Default* constants, so a " +
                 "board works in a scene with no options asset.")]
        [SerializeField] TableOptions options;

        [Tooltip("Template for board slabs — an UNLIT material (§3.4). Each slab instances it. " +
                 "Null builds a URP/Unlit instance at runtime and owns it.")]
        [SerializeField] Material unlitMaterial;

        /// <summary>
        /// Where this board's rig is built — <see cref="BoardRig.DefaultOrigin"/> unless a scene
        /// says otherwise.
        ///
        /// <para>The rig is a root object at this position rather than a child of this
        /// component, which is the bench's arrangement kept on purpose: a <c>BoardView</c> lives
        /// wherever its owner finds convenient — quite possibly on the table in the room — and
        /// parenting the board to it would drop a 120-unit island on the floor of the archive.
        /// The board's position is a property of the board, not of whoever holds it.</para>
        /// </summary>
        [SerializeField] Vector3 boardOrigin = BoardRig.DefaultOrigin;

        /// <summary>
        /// The sheets this board may offer (§4.3). <c>[SerializeReference]</c> so a future
        /// <c>FolderSheetSource</c> can be assigned in the inspector without this class knowing
        /// the type.
        ///
        /// <para>Left empty it becomes a <see cref="LedgerSheetSource"/> over
        /// <c>generator.Ledger</c>, which is C1.3's answer for this POC. Note that
        /// <c>LedgerSheetSource</c> itself is not worth assigning here even though the field
        /// would accept it: its ledger reference is <c>readonly</c> and therefore does not
        /// survive serialisation, so an inspector-assigned one would come back with a null
        /// ledger and quietly show an empty cabinet. Built in code, it cannot.</para>
        /// </summary>
        [SerializeReference] ISheetSource sheetSource;

        /// <summary>The worker's stop flag. A board can be closed while its rasters are still
        /// being produced, and a cancelled job must not keep rendering sheets nobody will
        /// upload. <c>volatile</c> because it is written on the main thread and read on the
        /// worker; it is a plain bool and not a Unity API, so both sides are legal.</summary>
        sealed class RenderJob { public volatile bool Cancelled; }

        readonly List<SheetId> available = new List<SheetId>();
        readonly Dictionary<SheetId, Sheet> sheets = new Dictionary<SheetId, Sheet>();
        readonly Dictionary<SheetId, SheetRender> renders = new Dictionary<SheetId, SheetRender>();
        readonly Dictionary<SheetId, Texture2D> textures = new Dictionary<SheetId, Texture2D>();

        readonly Dictionary<SheetId, BoardSheetView> placed = new Dictionary<SheetId, BoardSheetView>();
        readonly List<BoardSheetView> drawOrder = new List<BoardSheetView>();

        string stateId;

        GameObject boardRoot;

        /// <summary>
        /// The rig's root, or null while hidden. Public because the board's input needs a plane
        /// to raycast against and a space to convert into, and both are properties of the rig
        /// rather than of whoever is pointing at it.
        /// </summary>
        public Transform BoardRoot { get { return boardRoot != null ? boardRoot.transform : null; } }

        /// <summary>The offices with plates on this board, in <c>Offices.All</c> order, and
        /// which of them is showing. Rebuilt as plates land; -1 while there are none.</summary>
        readonly List<Office> layers = new List<Office>();
        int layerIndex = -1;

        /// <summary>
        /// Where the camera is looking (G10.1, and C8.13 superseded outright). Made in
        /// <see cref="BuildCamera"/> and destroyed with the rig, which is what makes "reset on
        /// every Show" true by construction rather than by a line someone has to remember.
        ///
        /// </summary>
        BoardViewport viewport;

        Material slabMaterial;      // owned only when unlitMaterial was null
        Material mountingMaterial;  // always owned
        Island island;
        float topInset;

        Coroutine build;
        RenderJob job;

        /// <summary>True once the rig exists. Textures may still be landing — C5.7 is explicit
        /// that opening never blocks, so "showing" and "finished rendering" are different
        /// states and only the first gates a plate.</summary>
        public bool IsShowing { get; private set; }

        /// <summary>The island this board is bound to, or 0 while hidden (C4.1's unbound
        /// table).</summary>
        public ulong IslandSeed { get; private set; }

        /// <summary>
        /// The offices this board can show, in <c>Offices.All</c> order — one per office with a
        /// plate on it, so an island surveyed by two has two layers and not three.
        /// </summary>
        public IReadOnlyList<Office> Layers { get { return layers; } }

        /// <summary>Which of <see cref="Layers"/> is showing, or -1 when the board holds no
        /// quarters at all.</summary>
        public int LayerIndex { get { return layerIndex; } }

        /// <summary>The office on screen. Meaningless when <see cref="LayerIndex"/> is -1.</summary>
        public Office ActiveLayer
        {
            get { return layerIndex >= 0 && layerIndex < layers.Count ? layers[layerIndex] : Office.Hydrographic; }
        }

        /// <summary>
        /// Shows one office's plates and hides the others (Q4.3). The chart is never hidden: it
        /// is the base everything is laid over (Q4.4), and where no quarter covers it the board
        /// is meant to show it through (Q4.6).
        ///
        /// <para><b>Visibility, not layout.</b> Nothing is moved, re-laid or re-rendered — the
        /// slabs stay exactly where their quarters put them, which is the whole point of
        /// flipping between offices: the same ground, in register, and nothing changes but the
        /// ink. Rebuilding the board per layer would also throw away rasters that cost 200 ms
        /// each.</para>
        /// </summary>
        public void ShowLayer(int index)
        {
            if (layers.Count == 0) { layerIndex = -1; return; }

            // Wraps both ways: Q at the first office lands on the last. There are two or three
            // layers, and a cycle that stopped at the ends would be a dead key half the time.
            int count = layers.Count;
            layerIndex = ((index % count) + count) % count;

            ApplyLayer();
            Raise();
        }

        /// <summary>The next office, or the previous one for a negative step (Q4.3's Q and E).
        /// </summary>
        public void CycleLayer(int step)
        {
            if (layers.Count == 0) return;
            ShowLayer(layerIndex + step);
        }

        void RebuildLayers()
        {
            Office was = ActiveLayer;
            bool had = layerIndex >= 0;

            layers.Clear();

            // Offices.All order, never the order plates happened to land in: two openings of one
            // island must put Q and E on the same offices, and rasters arrive one per frame in
            // whatever order the renderer finished them.
            for (int o = 0; o < Offices.All.Length; o++)
            {
                Office office = Offices.All[o];
                foreach (BoardSheetView view in drawOrder)
                {
                    if (view == null) continue;
                    SheetId id = view.Id;
                    if (id.WholeIsland || id.Office != office) continue;

                    layers.Add(office);
                    break;
                }
            }

            // Hold the office that was showing if it still has plates; a sheet landing must not
            // flip the board to another office under the player's hands.
            int keep = had ? layers.IndexOf(was) : -1;
            layerIndex = layers.Count == 0 ? -1 : (keep >= 0 ? keep : 0);

            ApplyLayer();
        }

        void ApplyLayer()
        {
            for (int i = 0; i < drawOrder.Count; i++)
            {
                BoardSheetView view = drawOrder[i];
                if (view == null) continue;

                SheetId id = view.Id;
                bool show = id.WholeIsland || (layerIndex >= 0 && id.Office == layers[layerIndex]);
                if (view.gameObject.activeSelf != show) view.gameObject.SetActive(show);
            }
        }

        /// <summary>The board's orthographic camera (§5.1), built disabled. <b>Enabling it is
        /// <c>TableSession</c>'s business</b>, along with whatever it does to the room's camera;
        /// a view that switched cameras itself would be a second owner of the mode switch of
        /// §8.2 and the two would disagree about which one is on.</summary>
        public Camera BoardCamera { get; private set; }

        /// <summary>
        /// How many screen pixels of the top edge the chrome covers, pushed in by whoever draws
        /// it. Narrows the camera's rect, so the framing is the rectangle the player can see.
        ///
        /// <para><b>A covered band is unreachable, not merely hidden.</b> At zoom 1 the camera's
        /// half-height is the board's, so <c>BoardViewport</c>'s travel is zero on both axes and
        /// no pan can bring the occluded strip out from under the header — the top of the island
        /// is simply gone. Rendering into the visible rectangle instead is the same argument the
        /// cabinet column was answered with, and costs nothing elsewhere:
        /// <c>Camera.aspect</c>, <c>ScreenPointToRay</c> and <c>WorldToScreenPoint</c> all follow
        /// the rect, so the viewport's arithmetic is unchanged.</para>
        ///
        /// <para>In <b>pixels</b>, because the chrome's height in pixels is a fact only the
        /// <c>CanvasScaler</c> has: the band is 96 reference pixels and the scaler's match is
        /// 0.5, so its screen height depends on the window's width as well as its height.</para>
        /// </summary>
        public float TopInsetPixels
        {
            get { return topInset; }
            set
            {
                topInset = value > 0f ? value : 0f;
                if (BoardCamera != null) ApplyCameraRect(BoardCamera);
            }
        }

        /// <summary>The ground &lt;-&gt; board transform (§3.1). Default until <see cref="Show"/>
        /// resolves an island; a drag handler converts a pointer hit with
        /// <c>Space.ToGround</c> before asking <see cref="SheetFit"/> anything, because the truth
        /// it compares against is in ground metres.</summary>
        public BoardSpace Space { get; private set; }

        /// <summary>The camera's current zoom, in <see cref="TableOptions.BoardZoom"/>'s units:
        /// 1 is C8.13's whole-board framing. <see cref="TableOptions.BoardZoom"/> while hidden,
        /// because a board with no rig has no view.</summary>
        public float ViewZoom { get { return viewport != null ? viewport.Zoom : Zoom; } }

        /// <summary>The board point the camera is centred on, in board units — <c>x</c> is board
        /// X and <c>y</c> is board <b>Z</b>, <see cref="BoardSpace"/>'s convention.</summary>
        public Vector2 ViewCentre { get { return viewport != null ? viewport.Centre : Vector2.zero; } }

        /// <summary>
        /// Slides the view by a board-unit delta, clamped so the view cannot leave the mounting
        /// sheet (see <see cref="BoardViewport"/> for the rule and for what it means at zoom 1).
        ///
        /// <para><b>A view transform and nothing else.</b> Nothing here touches a placement, a
        /// group, a frame or a tolerance: <c>SheetFit</c>'s reach is in ground metres and
        /// <c>GlowingHintRange</c> is in board units, so what fuses and what the hint covers is
        /// exactly what it was before the pan. The board model cannot tell this happened.</para>
        /// </summary>
        public void MoveView(Vector2 deltaBoard)
        {
            if (viewport == null || BoardCamera == null) return;

            viewport.MoveBy(deltaBoard, BoardCamera.aspect);
            ApplyView();
        }

        /// <summary>
        /// Multiplies the zoom by <paramref name="factor"/> about a board point that must not
        /// move on screen — the pointer's, in practice. Clamped to
        /// <see cref="TableOptions.BoardZoomMin"/>..<see cref="TableOptions.BoardZoomMax"/>.
        ///
        /// <para>Same guarantee as <see cref="MoveView"/>: this is the camera and nothing but
        /// the camera. A sheet under the cursor before a notch is the same sheet under the
        /// cursor after it, because the anchor is held fixed and because every screen-to-ground
        /// question already goes through the camera rather than around it.</para>
        /// </summary>
        public void ZoomViewAbout(float factor, Vector2 anchorBoard)
        {
            if (viewport == null || BoardCamera == null) return;

            viewport.ZoomAbout(factor, anchorBoard, BoardCamera.aspect);
            ApplyView();
        }

        /// <summary>Raised after any mutation: a sheet laid, seated or removed, a texture
        /// landing, and the board opening or closing. One event and not several because every
        /// consumer so far — the cabinet, the header, the caption — rebuilds from the whole
        /// board rather than reacting to a specific change, and a fine-grained event set that
        /// nothing subscribes to individually is API nobody can safely change later.</summary>
        public event Action Changed;

        float UnitsPerMetre { get { return options != null ? options.BoardUnitsPerMetre : TableOptions.DefaultBoardUnitsPerMetre; } }
        float Padding       { get { return options != null ? options.BoardPadding       : TableOptions.DefaultBoardPadding; } }
        float Separation    { get { return options != null ? options.SheetSeparation    : TableOptions.DefaultSheetSeparation; } }
        float Zoom          { get { return options != null ? options.BoardZoom         : TableOptions.DefaultBoardZoom; } }
        float ZoomMin       { get { return options != null ? options.BoardZoomMin      : TableOptions.DefaultBoardZoomMin; } }
        float ZoomMax       { get { return options != null ? options.BoardZoomMax      : TableOptions.DefaultBoardZoomMax; } }
        float PixelsPerMetre { get { return options != null ? options.BoardPixelsPerMetre : TableOptions.DefaultBoardPixelsPerMetre; } }

        /// <summary>
        /// Builds the board for one island: rig first, rasters after. Safe to call again — with
        /// the same seed it joins the build already running (or completes immediately if there
        /// is nothing left to do), with a different seed it tears the old board down first.
        ///
        /// <para>Always returns a coroutine so a caller can <c>yield return</c> it without
        /// having to know which of those three happened.</para>
        /// </summary>
        public Coroutine Show(ulong islandSeed)
        {
            return Show(islandSeed, null);
        }

        /// <summary>
        /// The same, for one named table (§4.1). <paramref name="boardId"/> chooses which stored
        /// board is laid back out and which one the save writes; an empty id falls back to this
        /// view's own instance, which is a board that lives as long as the session and no longer.
        ///
        /// <para>Showing a table its own board is <b>the</b> restore path: the arrangement is
        /// already in the store, and the slabs come back as their rasters land (C5.7), in lay
        /// order, poses from the store and assemblies from their frames (G3.1).</para>
        /// </summary>
        public Coroutine Show(ulong islandSeed, string boardId)
        {
            string id = Keyed(boardId);

            // Both re-open paths reset the framing, because a board is always opened as the
            // spec composes it (§3.1, C8.13's view scaled by TableOptions.BoardZoom) and never
            // as the player last left it — see BoardViewport for why a camera is not a fact
            // worth carrying. The build path gets its reset free: BuildCamera makes a new
            // viewport.
            //
            // The id is part of "already showing this": two tables bound to one island show the
            // same paper and different boards, and answering the second with the first's rig
            // would put one table's arrangement on the other.
            bool same = IslandSeed == islandSeed && id == StateId;
            if (build != null && same) { ResetView(); return build; }
            if (IsShowing && same && build == null)
            {
                ResetView();
                return StartCoroutine(AlreadyShowing());
            }

            if (build != null) { StopCoroutine(build); build = null; }
            Teardown();

            stateId = id;
            build = StartCoroutine(BuildBoard(islandSeed));
            return build;
        }

        /// <summary>Closes the board and destroys everything it made. Not a hide-in-place: a
        /// board holds N rasters and N textures, and C4.4 already says emptying a table is the
        /// deliberate act of clearing it — keeping the objects alive for a reopening that may
        /// never come would trade a third of a second against tens of megabytes.</summary>
        public void Hide()
        {
            if (build != null) { StopCoroutine(build); build = null; }
            bool was = IsShowing;
            Teardown();
            if (was) Raise();
        }

        /// <summary>The geometry behind an identity: centre, rotation, survey, paper. Resolved
        /// once when the board opens and held for as long as the island is, so this is a
        /// dictionary lookup and not a walk through
        /// <see cref="SheetLookup"/> on every drag frame.</summary>
        bool TrySheet(SheetId id, out Sheet sheet)
        {
            return sheets.TryGetValue(id, out sheet);
        }

        // ------------------------------------------------------------- identity

        /// <summary>The store's key for the board on screen — the table's id (§4.1), or this
        /// view's own instance when it was opened without one.</summary>
        string StateId
        {
            get
            {
                if (string.IsNullOrEmpty(stateId)) stateId = Keyed(null);
                return stateId;
            }
        }

        /// <summary>
        /// A usable store key, whatever the caller had. The fallback is per instance and
        /// therefore per run: a board opened with no table behind it — the <c>C</c> shortcut, a
        /// bench — is kept and saved like any other, and comes back as a different board next
        /// session because nothing in the room can point at it and say which one it was.
        /// </summary>
        string Keyed(string boardId)
        {
            return string.IsNullOrEmpty(boardId) ? "BoardView#" + GetInstanceID() : boardId;
        }

        // ---------------------------------------------------------------- build

        IEnumerator AlreadyShowing() { yield break; }

        IEnumerator BuildBoard(ulong islandSeed)
        {
            if (generator == null)
            {
                Debug.LogError("[BoardView] Not wired to an IslandGenerator.", this);
                build = null;
                yield break;
            }

            IslandSeed = islandSeed;

            // Generation is ~340 ms of pure, engine-free C# (C5.7) and must not happen inline —
            // MapCrate's comment is the authority. The generator reference is captured on the
            // main thread and only its thread-safe GetOrGenerate is touched off it; nothing in
            // here compares a UnityEngine.Object against null, which is the operation that would
            // not be safe.
            IslandGenerator source = generator;
            Task<Island> generating = Task.Run(() => source.GetOrGenerate(islandSeed));

            while (!generating.IsCompleted) yield return null;

            if (generating.IsFaulted)
            {
                Debug.LogException(generating.Exception, this);
                build = null;
                yield break;
            }

            island = generating.Result;
            Space = BoardSpace.ForIsland(island.LandBounds, Padding, UnitsPerMetre);

            BuildRig();
            CollectAvailable();

            // The rig is up and the cabinet has its rows; C5.7's "opens on the mounting sheet
            // with the cabinet filling in as textures land" starts here.
            IsShowing = true;
            Raise();

            yield return RenderAvailable();

            build = null;
        }

        void BuildRig()
        {
            boardRoot = new GameObject("BoardRoot");
            boardRoot.transform.position = boardOrigin;

            int layer = BoardRig.TableLayer;
            if (layer < 0)
                Debug.LogWarning("[BoardView] No '" + BoardRig.TableLayerName + "' layer — C5.1 " +
                                 "needs one, or the room's camera will draw the board.", this);

            mountingMaterial = BoardRig.BuildMountingSheet(boardRoot.transform, Space, layer);
            BuildCamera(boardRoot.transform, layer);
        }

        /// <summary>The board camera of §5.1: orthographic, looking down −Y.
        ///
        /// <para><b>No longer framing the whole board, and no longer fixed</b> (G10.1,
        /// superseding C8.13). C8.13 existed for absolute seating — the mounting sheet's extent
        /// was the player's only clue to where a sheet belonged — and G1.9 takes that out of
        /// scope, so the far corners carry no information, while at the old framing a Land Survey
        /// slab was 35% of the viewport height. <see cref="BoardViewport"/> holds the framing,
        /// <see cref="TableOptions.BoardZoom"/> is only where it starts, and
        /// <see cref="MoveView"/> / <see cref="ZoomViewAbout"/> are the whole of what may move
        /// it.</para>
        ///
        /// <para>The viewport is made <b>here</b>, with the rig, so it dies with the rig: a board
        /// reopening on the last player's zoom would be view state outliving its view.</para>
        /// </summary>
        void BuildCamera(Transform parent, int layer)
        {
            // depth ABOVE the room's camera, explicitly. A Camera created in code defaults to
            // depth 0, and POC04_Room's Main Camera is also depth 0 — equal depths leave the
            // render order undefined, so the room draws over the board about as often as not.
            // The symptom is the worst kind: the chrome appears (Screen Space Overlay, always
            // on top) while the main area shows the room, which reads as "the board failed to
            // build" rather than "two cameras are arguing".
            //
            // ENABLED here, by the thing that builds it: Show is a COROUTINE, so a caller that
            // switched the camera on after calling it would be reaching for a camera that does
            // not exist yet, and the enable would silently do nothing.
            Camera cam = BoardRig.BuildCamera(parent, layer, depth: 100f, enabled: true);

            BoardCamera = cam;
            ApplyCameraRect(cam);

            // After BoardCamera is set, because ApplyView writes through it — and after the
            // enable, so cam.aspect is the real viewport's rather than 1. G10.1's formula lives
            // in BoardViewport.OrthographicSize now and is unchanged: BoardHeight * 0.5 / Zoom,
            // divided and not multiplied, because orthographicSize is a half-HEIGHT and a
            // smaller number is a closer camera.
            viewport = new BoardViewport((float)Space.BoardWidth, (float)Space.BoardHeight,
                                         Zoom, ZoomMin, ZoomMax);
            ApplyView();
        }

        /// <summary>
        /// The screen rectangle the board is drawn into: everything under
        /// <see cref="TopInsetPixels"/>. Assigned only when it changes, because this is written
        /// from a per-frame push and <c>Camera.rect</c> is not a free setter.
        ///
        /// <para>The inset is capped well short of the whole window. A rect of zero height
        /// renders nothing and reports an infinite aspect, which <c>BoardViewport</c>'s clamp
        /// would then divide by — a blank board from a chrome measurement that arrived wrong is
        /// a failure with nothing on screen to name it.</para>
        /// </summary>
        void ApplyCameraRect(Camera cam)
        {
            float screenHeight = Screen.height > 0 ? Screen.height : 1;
            float top = Mathf.Clamp(topInset / screenHeight, 0f, 0.5f);

            var rect = new Rect(0f, 0f, 1f, 1f - top);
            if (cam.rect != rect) cam.rect = rect;
        }

        /// <summary>Back to <see cref="TableOptions.BoardZoom"/>, centred. Null-safe: a board
        /// whose rig has not been built yet has nothing to reset, and will be built at the home
        /// view anyway.</summary>
        void ResetView()
        {
            if (viewport == null) return;

            viewport.Reset();
            ApplyView();
        }

        /// <summary>
        /// Writes the viewport onto the camera. The only place either value is set, so there is
        /// one answer to "where is the board camera" and it is the viewport's.
        ///
        /// <para><b>Y is left exactly as it is</b> — the camera's 50 units of height over the
        /// board, which has nothing to do with the framing and everything to do with the clip
        /// planes. The same discipline <c>BoardInteractor.SetPose</c> applies to a slab's Y, for
        /// the same reason: a setter that touches an axis it does not own is a bug that only
        /// shows up in the one case nobody tested.</para>
        ///
        /// <para><b>Nothing here touches depth or the culling mask</b> (C5.1). They are set once
        /// above and a view transform has no business in either.</para>
        /// </summary>
        void ApplyView()
        {
            Camera cam = BoardCamera;
            if (cam == null || viewport == null) return;

            cam.orthographicSize = viewport.OrthographicSize;

            Transform t = cam.transform;
            Vector3 p = t.localPosition;
            t.localPosition = new Vector3(viewport.Centre.x, p.y, viewport.Centre.y);
        }

        /// <summary>
        /// Asks the source what this island offers and resolves each identity back to its
        /// geometry.
        ///
        /// <para>Taken once, when the board opens. Issuance does not stop because a table is
        /// open, so a crate opened afterwards can add sheets this list will not have — that is
        /// deliberate for now: a cabinet that grew rows under the player mid-drag is a worse
        /// failure than one that is a delivery out of date, and nothing yet asks for the other
        /// behaviour. <c>LedgerSheetSource</c> already hands back a copy for exactly this
        /// reason, so the list here cannot be mutated underneath us either way.</para>
        /// </summary>
        void CollectAvailable()
        {
            available.Clear();
            sheets.Clear();

            ISheetSource from = Source;
            if (from == null) return;

            IReadOnlyList<SheetId> ids = from.SheetsFor(IslandSeed);
            if (ids == null) return;

            for (int i = 0; i < ids.Count; i++)
            {
                SheetId id = ids[i];
                Sheet sheet;
                if (!SheetLookup.TryFind(island, id, out sheet))
                {
                    // C9.1's assertion, one layer earlier: a source naming a sheet this island
                    // does not have is a broken save or a stale ledger, and dropping it with a
                    // warning beats a null row nobody can explain.
                    Debug.LogWarning("[BoardView] " + id + " is not a sheet of this island — dropped.", this);
                    continue;
                }
                if (sheets.ContainsKey(id)) continue;

                sheets.Add(id, sheet);
                available.Add(id);
            }
        }

        /// <summary>
        /// The render budget of C5.6, in full: a worker producing rasters one at a time into a
        /// queue, and this coroutine uploading exactly one per frame.
        ///
        /// <para><b>Both halves are load-bearing.</b> Rendering inline would freeze the room for
        /// N × tens of milliseconds; uploading the batch in one frame is the visible hitch
        /// <c>MapCrate</c> already met, because <c>Texture2D.Apply</c> is a stall on the main
        /// thread whatever produced the bytes. And the producer emits per sheet rather than
        /// returning a finished list so that the first thumbnail can appear while the twentieth
        /// is still being drawn — A2 wants it inside 500 ms of the view opening, which a batch
        /// cannot promise.</para>
        /// </summary>
        IEnumerator RenderAvailable()
        {
            if (available.Count == 0) yield break;

            var queue = new ConcurrentQueue<SheetRender>();
            var pending = new List<Sheet>(available.Count);
            for (int i = 0; i < available.Count; i++) pending.Add(sheets[available[i]]);

            Island source = island;
            double pxPerMetre = PixelsPerMetre;
            RenderJob cancel = job = new RenderJob();

            Task rendering = Task.Run(() => RenderInto(source, pending, pxPerMetre, queue, cancel));

            while (!rendering.IsCompleted || !queue.IsEmpty)
            {
                SheetRender render;
                if (queue.TryDequeue(out render))
                {
                    renders[render.Id] = render;

                    Texture2D texture = UploadMap(render.Image, "T_Board_" + render.Id);
                    Texture2D old;
                    if (textures.TryGetValue(render.Id, out old)) Discard(old);
                    textures[render.Id] = texture;

                    // The board comes back here and nowhere else: a slab needs its raster, and
                    // this is the frame the raster arrives in (C5.7). A sheet nobody had laid
                    // down returns nothing to do.
                    LayOut(render.Id);

                    Raise();
                }

                // One upload, then the frame. Unconditional: with nothing in the queue this is
                // the coroutine waiting on the worker, and with something in it this is the
                // budget.
                yield return null;
            }

            if (rendering.IsFaulted) Debug.LogException(rendering.Exception, this);
        }

        /// <summary>
        /// Worker-thread half. Touches no engine API — <c>MapCrate.Render</c> is the shipping
        /// raster path (<c>RenderRequest.ForSheet</c> → <c>IslandRenderer.Render</c>) and
        /// <c>Archivist.Render</c> may not reference UnityEngine at all, which is what makes it
        /// safe here. Using the crate's own path rather than a second one is the same rule the
        /// bench followed: a board that drew its own sheets would prove only that the board
        /// works.
        /// </summary>
        static void RenderInto(Island island, List<Sheet> pending, double pixelsPerMetre,
                               ConcurrentQueue<SheetRender> done, RenderJob job)
        {
            var one = new List<Sheet>(1);

            for (int i = 0; i < pending.Count; i++)
            {
                if (job.Cancelled) return;

                one.Clear();
                one.Add(pending[i]);

                List<SheetRender> rendered = MapCrate.RenderForBoard(island, one, pixelsPerMetre);
                if (rendered.Count > 0) done.Enqueue(rendered[0]);
            }
        }

        /// <summary>
        /// The map, and only the map, as a texture — the cabinet's thumbnail (C5.5) and nothing
        /// the board itself draws.
        ///
        /// <para><b>The one vertical flip, again.</b> <see cref="ImageBuffer"/> is RGBA32,
        /// row-major, TOP-LEFT origin and <c>Texture2D</c> is BOTTOM-LEFT, so raw bytes come out
        /// upside down — easy to miss on a roughly symmetric island. A third copy of four lines
        /// on purpose: sharing would mean a helper in <c>Archivist.Render</c>, which is
        /// engine-free by design, or widening a private on a component this file does not
        /// own.</para>
        /// </summary>
        static Texture2D UploadMap(ImageBuffer map, string name)
        {
            int stride = map.Width * 4;
            var pixels = new byte[stride * map.Height];

            for (int y = 0; y < map.Height; y++)
                Buffer.BlockCopy(map.Pixels, y * stride,
                                 pixels, (map.Height - 1 - y) * stride, stride);

            var tex = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32,
                                    mipChain: true, linear: false);
            tex.name = name;
            tex.hideFlags = HideFlags.DontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 1;

            // SetPixelData, not LoadRawTextureData: with a mip chain the latter expects bytes for
            // every level, and only level 0 exists here.
            tex.SetPixelData(pixels, 0);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }

        // ------------------------------------------------------------ placement

        /// <summary>
        /// One plate onto the board as its raster lands. Its pose is its ground rect and
        /// nothing else (Q4.1), so this is the whole of the layout: there is no stored
        /// placement to read back and no gesture that could have moved it.
        ///
        /// <para>No <see cref="Raise"/>: rasters land one per frame and a board of thirteen
        /// plates would otherwise rebuild every subscriber thirteen times while opening.</para>
        /// </summary>
        void LayOut(SheetId id)
        {
            if (!IsShowing || placed.ContainsKey(id)) return;

            Sheet sheet;
            if (!TrySheet(id, out sheet)) return;
            if (Put(id, sheet.CentreGround, sheet.RotationDeg) == null) return;

            Resort();
            RebuildLayers();
        }

        /// <summary>The slab itself, built from the cached raster the first time a sheet is
        /// seen and posed every time. Without the re-sort and the event, so a subscriber never
        /// sees a board mid-change.</summary>
        BoardSheetView Put(SheetId id, V2 groundPos, double rotationDeg)
        {
            if (!IsShowing)
            {
                Debug.LogWarning("[BoardView] Nothing to lay on — the rig is not built.", this);
                return null;
            }

            BoardSheetView view;
            if (!placed.TryGetValue(id, out view))
            {
                SheetRender render;
                if (!renders.TryGetValue(id, out render))
                {
                    // C5.7: the board opens before its rasters land, so this is a real state and
                    // not a bug.
                    Debug.LogWarning("[BoardView] " + id + " has not been rendered yet.", this);
                    return null;
                }

                // The BORROWING overload: the slab is textured from this board's own cache
                // rather than uploading a second copy of pixels that already exist. One raster,
                // one upload, two readers — the slab and the cabinet thumbnail (C5.5). The slab
                // will not destroy it; Hide()/OnDestroy() here own every cached texture.
                Texture2D map;
                if (!textures.TryGetValue(id, out map) || map == null)
                {
                    Debug.LogWarning("[BoardView] " + id + " has no cached texture yet.", this);
                    return null;
                }

                view = BoardSheetView.Create(
                    render.Sheet, id, render.IslandName, map,
                    SlabMaterial, BoardRig.MapTextureProperty, UnitsPerMetre);
                view.transform.SetParent(boardRoot.transform, false);

                int layer = BoardRig.TableLayer;
                if (layer >= 0) BoardRig.SetLayerRecursive(view.gameObject, layer);

                placed.Add(id, view);
                drawOrder.Add(view);
            }

            WritePose(view, groundPos, rotationDeg);
            return view;
        }

        /// <summary>A ground pose onto a slab, and the only place this class writes one. Y is
        /// left exactly as it is: it is set by <see cref="Resort"/> from the draw index, because
        /// sheets overlap and order is a design element and not an accident (§3.3). The yaw is
        /// <see cref="BoardRig.BoardRotation"/>'s, sign included.</summary>
        void WritePose(BoardSheetView view, V2 groundPos, double rotationDeg)
        {
            V2 centre = Space.ToBoard(groundPos);
            Transform t = view.transform;

            t.localPosition = new Vector3((float)centre.X, t.localPosition.y, (float)centre.Y);
            t.localRotation = BoardRig.BoardRotation(rotationDeg);
        }

        /// <summary>§3.3's draw order, applied as Y offsets <c>SheetSeparation</c> apart.
        /// Re-run after every plate lands rather than only appending, because a plate that
        /// belongs low in the stack raises every plate already above it.</summary>
        void Resort()
        {
            drawOrder.Sort(CompareDrawOrder);

            float separation = Separation;

            for (int i = 0; i < drawOrder.Count; i++)
            {
                BoardSheetView view = drawOrder[i];
                if (view == null) continue;

                Vector3 p = view.transform.localPosition;
                view.transform.localPosition = new Vector3(p.x, i * separation, p.z);
            }
        }

        /// <summary>A total order over identity — the chart first, then office, then number.
        /// Total, so the result does not depend on <c>List.Sort</c> being stable, which it is
        /// not; over identity rather than over arrival, so two openings of one island stack the
        /// same way while rasters land in whatever order the renderer finished them
        /// (C4.7).</summary>
        static int CompareDrawOrder(BoardSheetView a, BoardSheetView b)
        {
            SheetId ia = a.Id, ib = b.Id;

            // The chart is under everything (Q4.4), whatever office borrowed it. Office first
            // would put it above the plates of every office ordered after its own, and a chart
            // covers the whole island: those layers would show nothing else.
            int byWhole = (ia.WholeIsland ? 0 : 1).CompareTo(ib.WholeIsland ? 0 : 1);
            if (byWhole != 0) return byWhole;

            int byOffice = ((int)ia.Office).CompareTo((int)ib.Office);
            if (byOffice != 0) return byOffice;

            return ia.Number.CompareTo(ib.Number);
        }

        // ------------------------------------------------------------- lifetime

        /// <summary>The source of §4.3, defaulted to the ledger for this POC (C1.3). Settable so
        /// a test or a future <c>CartographyTable</c> can hand in a <c>FolderSheetSource</c>
        /// before <see cref="Show"/>; the default is built lazily so wiring order does not
        /// matter.</summary>
        public ISheetSource Source
        {
            get
            {
                if (sheetSource == null && generator != null)
                    sheetSource = new LedgerSheetSource(generator.Ledger);
                return sheetSource;
            }
            set { sheetSource = value; }
        }

        /// <summary>The slab template of §3.4. Built here when nothing is wired so a board works
        /// in a bare scene, and owned when it is built — the serialized one is an asset and must
        /// never be destroyed.</summary>
        Material SlabMaterial
        {
            get
            {
                if (unlitMaterial != null) return unlitMaterial;
                if (slabMaterial == null) slabMaterial = BoardRig.UnlitSlab();
                return slabMaterial;
            }
        }

        void OnDestroy()
        {
            Teardown();
        }

        /// <summary>
        /// Destroys everything the board made and forgets everything it knew. Every texture in
        /// the cache goes: they carry <c>DontSave</c>, nothing else owns them, and a board that
        /// left them behind would leak N × a few hundred kilobytes per opening — which on a game
        /// with no ceiling on how many islands exist (R1.2) is not a small leak but an unbounded
        /// one.
        ///
        /// <para>The island reference is dropped but the island is not: it lives in
        /// <c>IslandCache</c>, which is the only thing allowed to decide how long an island
        /// stays (R1.11 — nothing geometric is persisted, but caching is not persisting).</para>
        /// </summary>
        void Teardown()
        {
            if (job != null) { job.Cancelled = true; job = null; }

            foreach (KeyValuePair<SheetId, Texture2D> entry in textures) Discard(entry.Value);
            textures.Clear();

            renders.Clear();
            placed.Clear();
            drawOrder.Clear();

            // The office selection goes with them, for the reason the viewport is dropped a few
            // lines down: a board closed on Garrison must not open the NEXT island on Garrison.
            // RebuildLayers holds the showing office deliberately so a landing plate cannot flip
            // the board under the player's hands — and that hold has to end at a teardown, or it
            // crosses boards. It also leaves a window where IsShowing is true and no plate has
            // landed yet, during which the board would answer with the previous island's layers.
            layers.Clear();
            layerIndex = -1;

            // Nothing but pixels dies here. There is no model to keep (Q4.7): the board is a
            // view of what is in the binders on the table, so closing it costs nothing and
            // reopening it rebuilds from the same place it built from the first time.
            available.Clear();
            sheets.Clear();

            if (boardRoot != null) Discard(boardRoot);
            boardRoot = null;
            BoardCamera = null;

            // The view goes with the camera it describes. Dropped rather than reset so that a
            // stale zoom cannot survive a seed change even for the frame between Teardown and
            // the next BuildCamera.
            viewport = null;

            Discard(mountingMaterial); mountingMaterial = null;
            Discard(slabMaterial);     slabMaterial = null;

            island = null;
            IslandSeed = 0;
            IsShowing = false;
            Space = default(BoardSpace);
        }

        void Raise()
        {
            Action changed = Changed;
            if (changed != null) changed();
        }

        /// <summary>Destroy is illegal in edit mode, and a board is routinely built and torn down
        /// there — by a bench, by a rebuild, by deleting the root in the Hierarchy. Same shape as
        /// <c>BoardSheetView.Discard</c>, for the same reason.</summary>
        static void Discard(UnityEngine.Object thing)
        {
            if (thing == null) return;

            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }
    }
}
