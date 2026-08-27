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
    /// looks plausible. <b>Nothing about the geometry is reopened here.</b> What does change is
    /// that the board starts empty: <see cref="Show"/> builds the rig and puts <i>nothing</i> on
    /// the mounting sheet, and <see cref="Lay"/>, <see cref="Seat"/> and <see cref="Remove"/>
    /// are the only ways a slab arrives or leaves. This class holds no input and no UGUI, so the
    /// same board can be driven by a test, a bench or a pointer.</para>
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
    /// <c>Destroy</c> is worse still, because the first <see cref="Remove"/> would blank every
    /// thumbnail on screen. <c>ownsTexture</c> is what makes the third option possible.</para>
    ///
    /// <para><b>Nothing here is an asset.</b> Every mesh, material and texture is created at
    /// runtime with <c>HideFlags.DontSave</c> and destroyed in <see cref="Hide"/> and
    /// <c>OnDestroy</c>. A cached texture is owned by nobody else: the cabinet borrows it while
    /// the table is open and must not hold it across a close.</para>
    ///
    /// <para><b>Groups (S3) live in a <see cref="BoardStore"/> this view owns, and the two
    /// halves of a pose are not the same kind of fact.</b> For a <i>loose</i> sheet the
    /// transform IS the pose (C4.6): the drag layer writes it every frame, deliberately does not
    /// call <see cref="Lay"/> at 60 Hz, and a release that fits nothing calls nothing (C6.6), so
    /// <c>Placement.GroundX/Y</c> is the last committed pose and not the live one. For a
    /// <i>grouped</i> sheet the frame is the model and the transform is derived (G1.3, G4.3):
    /// members carry no pose, one <see cref="MoveGroup"/> moves the assembly, and this class
    /// rewrites their transforms from G3.1. <see cref="TryPoseOf"/> is the one place that knows
    /// which of the two a sheet is, which is why nothing else may compose a frame with a
    /// truth.</para>
    ///
    /// <para><b>Why the store is here rather than in <c>CartographyTable</c>.</b> The derivation
    /// needs <c>Sheet.CentreGround</c> — the island — and <see cref="BoardStore"/> has never
    /// regenerated anything and must not start. This class already resolves the island and holds
    /// <see cref="TrySheet"/>, so it is the seam where a frame and a truth may meet. One view
    /// serves every table in the room; the board it shows is chosen by the table's own id
    /// (§4.1), handed to <see cref="Show(ulong,string)"/>.</para>
    ///
    /// <para><b>The model outlives the rig</b> (§9). <see cref="Hide"/> destroys every slab,
    /// mesh and texture and keeps the store, so an arrangement — loose poses, assemblies, parked
    /// assemblies — is still there when the table is opened again, and <see cref="Archive"/>
    /// writes it to disk at C9.2's save points. A board is emptied by clearing the table
    /// (C4.4), never by closing it.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardView : MonoBehaviour
    {
        /// <summary>URP Unlit's albedo map. §3.4: unlit, so the board is independent of the
        /// room's lighting and of where its root sits — which is what makes C5.2's offset
        /// free.</summary>
        const string MapTextureProperty = "_BaseMap";

        /// <summary>C5.1. The main camera's culling mask must exclude this layer and
        /// <see cref="BoardCamera"/>'s must contain only it.</summary>
        const string TableLayerName = "Table";

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
        /// Where the board rig is built, in world space. C5.2 puts it well clear of the room so
        /// nothing on it can be seen, hit or lit from there.
        ///
        /// <para>The rig is a root object at this position rather than a child of this
        /// component, which is the bench's arrangement kept on purpose: a <c>BoardView</c> lives
        /// wherever its owner finds convenient — quite possibly on the table in the room — and
        /// parenting the board to it would drop a 120-unit island on the floor of the archive.
        /// The board's position is a property of the board, not of whoever holds it.</para>
        /// </summary>
        [SerializeField] Vector3 boardOrigin = new Vector3(0f, -500f, 0f);

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

        /// <summary>One laid-out sheet. Seated is a board fact and lives here rather than on the
        /// slab: C4.6 is explicit that a seated sheet stores <b>no pose</b> — its pose is
        /// <c>Sheet.CentreGround</c> / <c>RotationDeg</c> and nothing else — so what has to be
        /// remembered is a flag, and the slab already knows everything else.</summary>
        sealed class Laid
        {
            public readonly BoardSheetView View;

            /// <summary>When this sheet was laid down, as a monotonic counter. §3.3 orders
            /// unseated sheets "in the order they were laid down", and <c>List.Sort</c> is not a
            /// stable sort — it will happily permute equal elements — so that order has to be a
            /// value in the comparison rather than a property of the list.</summary>
            public readonly int LaidAt;

            public bool Seated;

            /// <summary>G5.6's draw keys, recomputed by <see cref="BoardView.Resort"/> before
            /// every sort. A loose sheet is <c>(LaidAt, 0)</c> and therefore sorts exactly as it
            /// did before groups existed; a member takes its group's anchor and its own index in
            /// the join order, which is what makes an assembly a contiguous run of tiers that no
            /// other paper can be interleaved with. Kept as fields rather than computed inside
            /// the comparator because a comparator that walks the group table is O(n log n)
            /// lookups, and because <c>List.Sort</c> may call it in any order.</summary>
            public int RunAt, RunIndex;

            public Laid(BoardSheetView view, int laidAt) { View = view; LaidAt = laidAt; }
        }

        /// <summary>The worker's stop flag. A board can be closed while its rasters are still
        /// being produced, and a cancelled job must not keep rendering sheets nobody will
        /// upload. <c>volatile</c> because it is written on the main thread and read on the
        /// worker; it is a plain bool and not a Unity API, so both sides are legal.</summary>
        sealed class RenderJob { public volatile bool Cancelled; }

        readonly List<SheetId> available = new List<SheetId>();
        readonly Dictionary<SheetId, Sheet> sheets = new Dictionary<SheetId, Sheet>();
        readonly Dictionary<SheetId, SheetRender> renders = new Dictionary<SheetId, SheetRender>();
        readonly Dictionary<SheetId, Texture2D> textures = new Dictionary<SheetId, Texture2D>();

        readonly Dictionary<SheetId, Laid> placed = new Dictionary<SheetId, Laid>();
        readonly List<Laid> layOrder = new List<Laid>();
        readonly List<BoardSheetView> onTable = new List<BoardSheetView>();

        /// <summary>
        /// The group table of G4.2, and the board's placements mirrored into it so the store's
        /// one invariant — <i>a member is on the board exactly when its group is</i> — is true
        /// of a real board rather than of a store nobody drives.
        ///
        /// <para>Mirrored rather than made authoritative for the placements: C4.6 says the
        /// transform is the pose, and making the store the pose authority would mean a
        /// <see cref="Lay"/> per drag frame. The mirror is one-directional — every
        /// <i>committed</i> mutation goes both places, and the store's pose for a loose sheet is
        /// a memo. What it <b>is</b> authoritative about is membership, the frame and which group
        /// a sheet is in, none of which change at pointer speed except the frame, which
        /// <see cref="MoveGroup"/> writes directly.</para>
        /// </summary>
        readonly BoardStore state = new BoardStore();
        string stateId;

        GameObject boardRoot;

        /// <summary>
        /// Where the camera is looking (G10.1, and C8.13 superseded outright). Made in
        /// <see cref="BuildCamera"/> and destroyed with the rig, which is what makes "reset on
        /// every Show" true by construction rather than by a line someone has to remember.
        ///
        /// <para><b>Not in <see cref="state"/>, and that is deliberate.</b> <c>BoardStore</c>
        /// holds player facts about paper and §4.2/G4.4 shape it to be persisted; where someone
        /// last scrolled to is not a fact about the archive, and a save that restored it would
        /// be saving the wrong thing. See <see cref="BoardViewport"/>.</para>
        /// </summary>
        BoardViewport viewport;

        Material slabMaterial;      // owned only when unlitMaterial was null
        Material mountingMaterial;  // always owned
        Island island;

        Coroutine build;
        RenderJob job;
        int nextLaidAt;

        /// <summary>True once the rig exists. Textures may still be landing — C5.7 is explicit
        /// that opening never blocks, so "showing" and "finished rendering" are different
        /// states and only the first gates <see cref="Lay"/>.</summary>
        public bool IsShowing { get; private set; }

        /// <summary>The island this board is bound to, or 0 while hidden (C4.1's unbound
        /// table).</summary>
        public ulong IslandSeed { get; private set; }

        /// <summary>The board's orthographic camera (§5.1), built disabled. <b>Enabling it is
        /// <c>TableSession</c>'s business</b>, along with whatever it does to the room's camera;
        /// a view that switched cameras itself would be a second owner of the mode switch of
        /// §8.2 and the two would disagree about which one is on.</summary>
        public Camera BoardCamera { get; private set; }

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

        /// <summary>Every sheet the source offers for this island, in the source's order (§4.3
        /// makes that order part of the contract). Not filtered by what is on the table — a
        /// sheet appears here whether it is in the drawer or laid out; <see cref="IsOnTable"/>
        /// crosses the two, which is the cabinet's job in C7.4.</summary>
        public IReadOnlyList<SheetId> Available { get { return available; } }

        /// <summary>The slabs currently on the board, in <b>draw order</b> — lowest first, which
        /// is the order their Y offsets run in (§3.3). Seated sheets are at the bottom of this
        /// list because that is the whole visual argument that the board is being assembled.
        /// </summary>
        public IReadOnlyList<BoardSheetView> OnTable { get { return onTable; } }

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
        float PixelsPerMm   { get { return options != null ? options.BoardPixelsPerPaperMm : TableOptions.DefaultBoardPixelsPerPaperMm; } }

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

        /// <summary>The sheet's raster as a texture, or null until it has landed (C5.5, C5.7).
        /// <b>Borrowed, never owned</b> — the board destroys it in <see cref="Hide"/>, so a
        /// cabinet row must re-ask rather than cache the reference across an opening.</summary>
        public Texture2D TextureFor(SheetId id)
        {
            Texture2D texture;
            return textures.TryGetValue(id, out texture) ? texture : null;
        }

        public bool IsOnTable(SheetId id) { return placed.ContainsKey(id); }

        /// <summary>
        /// Puts a sheet on the board at an explicit ground pose, or moves it there if it is
        /// already down. Returns null if the board is not showing or the sheet's raster has not
        /// landed yet.
        ///
        /// <para>Always <b>unseated</b>, which is C6.7: seating is not a lock, and a sheet given
        /// an explicit pose has by definition just been placed by hand. A caller that wants the
        /// true pose wants <see cref="Seat"/>.</para>
        ///
        /// <para><b>Laying a member takes it out of its group</b>: a placement carries one
        /// derivation or none (G4.1) and this hands it a pose of its own. <b>The interaction
        /// layer must never reach this path for a member</b> — G1.6 makes the group the unit of
        /// interaction, so dragging a member drags the group and the write is
        /// <see cref="MoveGroup"/>. The path exists for the callers <see cref="Remove"/> names,
        /// and is safe here in a way it is not in the store, because
        /// <see cref="NoteDissolution"/> puts the survivor of a dissolved pair back where it was
        /// standing instead of at the island origin.</para>
        /// </summary>
        public BoardSheetView Lay(SheetId id, V2 groundPos, double rotationDeg)
        {
            BoardSheetView view = Put(id, groundPos, rotationDeg, seated: false);
            if (view == null) return null;

            Survivor survivor = NoteDissolution(id);
            state.Lay(StateId, id, groundPos.X, groundPos.Y, rotationDeg);
            Restore(survivor);

            Resort();
            Raise();
            return view;
        }

        /// <summary>
        /// Snaps a sheet to its true pose — <c>Sheet.CentreGround</c> and <c>RotationDeg</c> —
        /// and marks it seated (§6.1). Lays it first if it was in the drawer, so "seat this" is
        /// one call whatever state the sheet was in.
        ///
        /// <para>The pose is read from the sheet, not stored, which is C4.6 and the reason A6 can
        /// delete the pose fields out of a save by hand and still get every seated sheet
        /// back.</para>
        /// </summary>
        public void Seat(SheetId id)
        {
            Sheet sheet;
            if (!TrySheet(id, out sheet)) return;

            if (Put(id, sheet.CentreGround, sheet.RotationDeg, seated: true) == null) return;

            // Same trap as Lay, for the same reason: seated and grouped are alternatives (G4.1),
            // so this takes a member out of its group. Nothing produces a seat any more (§13,
            // G1.9), which is exactly why the guard has to be here rather than at a call site
            // that no longer exists to remember it.
            Survivor survivor = NoteDissolution(id);
            state.Seat(StateId, id);
            Restore(survivor);

            Resort();
            Raise();
        }

        /// <summary>
        /// Writes a loose sheet's transform into the model, silently — no re-sort, no
        /// <see cref="Changed"/>, nothing moved. The pose is already the sheet's (C4.6); this is
        /// what makes it durable, and it exists because C9.2 saves on every release while C6.6
        /// says a release that fits nothing produces no feedback at all. <see cref="Lay"/> would
        /// do both jobs and rebuild a 48-row cabinet to announce a 3 mm move.
        ///
        /// <para><b>Seated and grouped sheets are left alone, and that is the whole guard.</b>
        /// Neither has a pose of its own — one is the island's, one is the frame's (C4.6, G4.1)
        /// — so writing this sheet's transform into the store would unseat it or, worse, take it
        /// out of its assembly and dissolve a pair.</para>
        /// </summary>
        public void CommitPose(SheetId id)
        {
            if (!placed.ContainsKey(id)) return;
            if (GroupIdOf(id) != 0) return;

            Placement placement;
            if (state.TryGetPlacement(StateId, id, out placement) && placement.Seated) return;

            V2 ground;
            double rotationDeg;
            if (!TryPoseOf(id, out ground, out rotationDeg)) return;

            state.Lay(StateId, id, ground.X, ground.Y, rotationDeg);
        }

        /// <summary>Back to the cabinet (C7.5). The slab is destroyed rather than parked:
        /// <c>BoardSheetView</c> owns its mesh, material and texture and frees them in
        /// <c>OnDestroy</c>, and the raster it was built from stays cached, so laying the same
        /// sheet again costs an upload and not a render.</summary>
        public void Remove(SheetId id)
        {
            Laid entry;
            if (!placed.TryGetValue(id, out entry)) return;

            // Asked BEFORE the store is told, because after it the group — and therefore the
            // frame the survivor's pose is composed from — is gone.
            Survivor survivor = NoteDissolution(id);

            placed.Remove(id);
            layOrder.Remove(entry);
            if (entry.View != null) Discard(entry.View.gameObject);

            state.Remove(StateId, id);
            Restore(survivor);

            Resort();
            Raise();
        }

        /// <summary>The geometry behind an identity: centre, rotation, survey, paper. Resolved
        /// once when the board opens and held for as long as the island is, so this is a
        /// dictionary lookup and not a walk through
        /// <see cref="SheetLookup"/> on every drag frame.</summary>
        public bool TrySheet(SheetId id, out Sheet sheet)
        {
            return sheets.TryGetValue(id, out sheet);
        }

        // --------------------------------------------------------------- groups

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

        /// <summary>Every board this view has been shown, for <c>Archive</c> to write and to
        /// read back (§9). The one caller: a board is mutated through the methods above, which
        /// keep the slabs and the model saying the same thing.</summary>
        public BoardStore Boards { get { return state; } }

        /// <summary>
        /// Every assembly on this board, on-table and parked alike (G6.1) — the list the
        /// cabinet's Groups section is drawn from.
        ///
        /// <para>A fresh list of values every call, per <see cref="BoardStore"/>'s standing
        /// rule. Cheap on the numbers that exist: island 0 can hold at most three groups
        /// (§6). A caller polling this every frame should keep the last one and refresh on
        /// <see cref="Changed"/> instead, which is what the drag layer does.</para>
        /// </summary>
        public IReadOnlyList<GroupRecord> Groups { get { return state.GroupsOn(StateId); } }

        /// <summary>Which assembly this sheet belongs to, or 0 when it is loose or not on this
        /// board — the test G6.2's inert office row and G1.6's "clicking any member selects the
        /// group" are both drawn from.</summary>
        public int GroupIdOf(SheetId id) { return state.GroupIdOf(StateId, id); }

        /// <summary>One assembly as a value (G4.2). False for an id that names nothing, which
        /// includes one that has been merged away — ids are never reused, so a stale reference
        /// fails rather than naming somebody else.</summary>
        public bool TryGetGroup(int groupId, out GroupRecord group)
        {
            return state.TryGetGroup(StateId, groupId, out group);
        }

        /// <summary>
        /// The frame an assembly presents (G3.1, G4.2), or <see cref="BoardFrame.Identity"/>
        /// when there is no such group.
        ///
        /// <para><b>Identity is the honest answer to "no such group", not a fallback that
        /// hides one.</b> Identity is the absolute test the table has always run (C6.1), so a
        /// caller that ignores the missing group tests against the island's own pose — a pose
        /// the player cannot guess (§1.1) and which therefore fails, quietly and always,
        /// instead of fusing something to a wrong arrangement. The alternative, a nullable, put
        /// a null check in front of every fit test on a path that already has one gate too
        /// many.</para>
        /// </summary>
        public BoardFrame FrameOf(int groupId)
        {
            GroupRecord group;
            if (!TryGetGroup(groupId, out group)) return BoardFrame.Identity;
            return new BoardFrame(group.RotationDeg, new V2(group.OffsetX, group.OffsetY));
        }

        /// <summary>
        /// Where a sheet on this board actually is, in ground metres and degrees. False when it
        /// is in the drawer.
        ///
        /// <para><b>This is the one place G3.1's derivation lives.</b> A grouped sheet's pose is
        /// <c>frame.PositionOf(truth)</c> / <c>frame.RotationOf(truth)</c>, a loose sheet's is
        /// its transform, and telling the two apart is the whole content of this method.
        /// <see cref="BoardStore"/> cannot offer it: composing a frame with a truth needs the
        /// island. <b>A second copy of this derivation anywhere is a second place for the frame
        /// to be applied wrongly</b> — mirrored, to the wrong sheet's rotation, or once too
        /// often — and G-A2 is the only check that would catch it. Call this; do not
        /// re-derive.</para>
        ///
        /// <para><b>The transform, for a loose sheet, and not the store's copy</b> (C4.6). The
        /// drag layer moves a slab without telling the board, and a release that fits nothing
        /// tells it nothing (C6.6), so <c>Placement.GroundX/Y</c> can be a whole drag out of
        /// date and reading it would make the fuse test judge a sheet against where it used to
        /// be. A seated sheet reads the same way and gives the same answer, because
        /// <see cref="Seat"/> writes the truth onto the transform.</para>
        /// </summary>
        public bool TryPoseOf(SheetId id, out V2 groundPos, out double rotationDeg)
        {
            groundPos = V2.Zero;
            rotationDeg = 0.0;

            Laid entry;
            if (!placed.TryGetValue(id, out entry) || entry.View == null) return false;

            int groupId = GroupIdOf(id);
            if (groupId != 0)
            {
                Sheet truth;
                if (!TrySheet(id, out truth)) return false;

                BoardFrame frame = FrameOf(groupId);
                groundPos = frame.PositionOf(truth);
                rotationDeg = frame.RotationOf(truth);
                return true;
            }

            Transform t = entry.View.transform;
            Vector3 p = t.localPosition;
            groundPos = Space.ToGround(new V2(p.x, p.z));

            // The inverse of the negation in Put. F-S1.2 verified the sign by outcome; negating
            // on the way in and not on the way out compares the player's angle against its own
            // mirror image. Do not "fix" either half.
            rotationDeg = -t.localEulerAngles.y;
            return true;
        }

        /// <summary>
        /// G5.1's first case: two loose sheets fuse into a new assembly under
        /// <paramref name="frame"/>. Returns the new group's id, or 0 if it refused.
        ///
        /// <para><paramref name="stationary"/> goes in first and <paramref name="joining"/>
        /// second, which is join order and therefore draw order inside G5.6's run: the paper
        /// that was already on the table stays under the sheet just laid on it, which is §3.3's
        /// rule applied inside the assembly. G5.2 decides the frame — the stationary thing's,
        /// never the dragged one's, because the table does not move when paper is put on
        /// it.</para>
        ///
        /// <para><b>Both refusals are checked before the group is opened</b>, not after. The
        /// store creates a group empty and fills it with two calls, and a second call that
        /// failed would leave a one-member group behind that no gesture could ever have made
        /// and nothing here could dissolve.</para>
        /// </summary>
        public int CreateGroup(SheetId stationary, SheetId joining, BoardFrame frame)
        {
            if (stationary.Equals(joining)) return 0;
            if (!IsOnTable(stationary) || !IsOnTable(joining)) return 0;
            if (stationary.Office != joining.Office) return 0;
            if (stationary.WholeIsland != joining.WholeIsland) return 0;
            if (GroupIdOf(stationary) != 0 || GroupIdOf(joining) != 0) return 0;

            int id = state.CreateGroup(StateId, stationary.Office, stationary.WholeIsland,
                                       frame.RotationDeg, frame.Offset.X, frame.Offset.Y);
            if (id == 0) return 0;

            state.AddToGroup(StateId, id, stationary);
            state.AddToGroup(StateId, id, joining);

            Derive(id);
            Resort();
            Raise();
            return id;
        }

        /// <summary>G5.1's second and third cases: one loose sheet joins an existing assembly
        /// and takes its frame. The sheet must already be on the board — the store would
        /// otherwise accept one out of the drawer and lay it down as a side effect of a fuse,
        /// which is a slab arriving with no <see cref="BoardSheetView"/> behind it.</summary>
        public bool AddToGroup(int groupId, SheetId id)
        {
            if (!IsOnTable(id)) return false;
            if (!state.AddToGroup(StateId, groupId, id)) return false;

            Derive(groupId);
            Resort();
            Raise();
            return true;
        }

        /// <summary>G5.1's fourth case: one flat assembly, keeping <paramref name="keepId"/>'s
        /// frame (G5.2 — the stationary thing's) and both member lists in their own join
        /// orders, so each half keeps the run G5.6 draws it in and the seam between them is
        /// where the player made it.</summary>
        public bool MergeGroups(int keepId, int absorbId)
        {
            if (!state.MergeGroups(StateId, keepId, absorbId)) return false;

            Derive(keepId);
            Resort();
            Raise();
            return true;
        }

        /// <summary>
        /// Moves the whole assembly: one frame is written and every member's transform is
        /// re-derived from it (G5.4). False when there is no such group.
        ///
        /// <para><b>This does not raise <see cref="Changed"/></b>, the same argument the drag
        /// layer makes about <see cref="Lay"/>: it runs every frame of a group drag and settle,
        /// and <c>Changed</c> is what rebuilds a 48-row accordion. Nothing in that audience draws
        /// where a group is — the Groups row shows the survey, the count and a thumbnail (G6.3).
        /// The model is not lied to: the frame is written through immediately, and every mutation
        /// that changes what a group <i>is</i> goes through a path that does raise.</para>
        ///
        /// <para>It does not <see cref="Resort"/> either, for the second half of the same
        /// reason: a move changes no draw key, and a re-sort mid-drag would flatten the tiers
        /// the interaction layer has lifted the run to.</para>
        /// </summary>
        public bool MoveGroup(int groupId, BoardFrame frame)
        {
            if (!state.SetGroupFrame(StateId, groupId, frame.RotationDeg,
                                     frame.Offset.X, frame.Offset.Y)) return false;

            Derive(groupId);
            return true;
        }

        /// <summary>
        /// G6.4: parks the whole assembly in the cabinet. It leaves the board, keeps its
        /// membership <b>and</b> its frame, and its Groups row goes to the drawer state. False
        /// when there is no such group; true and idempotent for one already parked.
        ///
        /// <para><b>This raises <see cref="Changed"/>, and <see cref="MoveGroup"/> deliberately
        /// does not.</b> That is the whole distinction: a group's <i>pose</i> is not a fact the
        /// cabinet draws, so writing it sixty times a second says nothing to anybody; where the
        /// group <i>is</i> — table or drawer — is exactly what the Groups row and every member's
        /// office row show (G6.1, G6.2, C7.4), so parking is one of the mutations the accordion
        /// exists to redraw.</para>
        ///
        /// <para>The slabs are destroyed rather than hidden — <see cref="Remove"/>'s argument
        /// applied to nine sheets at once: the raster stays cached, so retrieving costs an
        /// upload rather than a render. Nothing here touches the group table beyond the one flag;
        /// <see cref="BoardStore.SetGroupOnTable"/> is the only thing that may take members off
        /// the board, because a member is on the board exactly when its group is.</para>
        ///
        /// <para><b>A parked assembly outlives the table being closed</b> (§9): the model
        /// survives <see cref="Teardown"/> and <c>Archive</c> writes it at this gesture, so the
        /// Groups drawer keeps what it looks like it keeps. It does not outlive the table being
        /// <i>cleared</i> — C4.4, the last binder coming off — because an assembly of one
        /// island's paper on a table now bound elsewhere is a group with no board under it.</para>
        /// </summary>
        public bool ParkGroup(int groupId)
        {
            GroupRecord group;
            if (!TryGetGroup(groupId, out group)) return false;
            if (!group.OnTable) return true;
            if (group.Members == null) return false;

            // The record's member list is a copy (BoardStore hands out values), so it survives
            // the call that empties the board of them.
            if (!state.SetGroupOnTable(StateId, groupId, false)) return false;

            for (int i = 0; i < group.Members.Count; i++)
            {
                Laid entry;
                if (!placed.TryGetValue(group.Members[i], out entry)) continue;

                placed.Remove(group.Members[i]);
                layOrder.Remove(entry);
                if (entry.View != null) Discard(entry.View.gameObject);
            }

            Resort();
            Raise();
            return true;
        }

        /// <summary>
        /// G6.5: lays a parked assembly back on the board, at the frame it was parked with.
        /// False when there is no such group; true and idempotent for one already down.
        ///
        /// <para><b>φ is preserved</b>, unlike <c>BeginPlace</c>, which lays a single sheet at
        /// rotation 0 because resolving orientation is part of placing a <i>sheet</i> (POC-03
        /// P2.6, C6.3). A group has already had its orientation resolved — that is what made it a
        /// group — and with absolute correctness out of scope (G1.9) its φ carries no remaining
        /// puzzle.</para>
        ///
        /// <para><b>Where the assembly lands is not decided here.</b> This restores it at the
        /// frame it was parked with; G6.5's "under the pointer" is a translation of that frame,
        /// which is <see cref="MoveGroup"/>. That split keeps one writer of a group's pose and
        /// leaves this method with no opinion about pointers, which it could not have anyway.
        /// </para>
        ///
        /// <para>Members are laid provisionally and then derived rather than composed here:
        /// <see cref="Put"/> needs a pose to write and <see cref="TryPoseOf"/> needs a slab to
        /// read, so the slab is made at the frame's offset and <see cref="Derive"/> immediately
        /// overwrites it through the <b>one</b> G3.1 derivation this class allows. Nothing is
        /// drawn in between.</para>
        ///
        /// <para>They come back in join order at the top of the stack — G5.6's contiguous run. A
        /// member whose raster has not landed (C5.7) is skipped with a warning and the rest still
        /// arrive: the store's invariant already says the group is on the table, and a
        /// partly-drawn assembly is recoverable where a refused retrieval is not (R6.5).</para>
        ///
        /// <para>A parked assembly is still there after the table has been closed and opened
        /// again, and after the game has been (§9). It is not there after the table has been
        /// cleared — see <see cref="ParkGroup"/>.</para>
        /// </summary>
        public bool RetrieveGroup(int groupId)
        {
            GroupRecord group;
            if (!TryGetGroup(groupId, out group)) return false;
            if (group.OnTable) return true;
            if (group.Members == null || group.Members.Count == 0) return false;

            if (!state.SetGroupOnTable(StateId, groupId, true)) return false;

            BoardFrame frame = FrameOf(groupId);
            for (int i = 0; i < group.Members.Count; i++)
                Put(group.Members[i], frame.Offset, 0.0, seated: false);

            Derive(groupId);
            Resort();
            Raise();
            return true;
        }

        /// <summary>Writes every member's transform from the group's frame — G3.1 applied, via
        /// the one derivation in <see cref="TryPoseOf"/>. Y is left alone: it belongs to the
        /// draw-order stack (§3.3) and to whatever tier the drag layer has lifted the run
        /// to.</summary>
        void Derive(int groupId)
        {
            GroupRecord group;
            if (!TryGetGroup(groupId, out group) || group.Members == null) return;

            for (int i = 0; i < group.Members.Count; i++)
            {
                SheetId id = group.Members[i];

                Laid entry;
                if (!placed.TryGetValue(id, out entry) || entry.View == null) continue;

                V2 ground;
                double rotationDeg;
                if (!TryPoseOf(id, out ground, out rotationDeg)) continue;

                WritePose(entry.View, ground, rotationDeg);
            }
        }

        /// <summary>
        /// The other member of a pair that is about to be broken up, and the pose it is standing
        /// in — captured <b>before</b> the store is told, because after it there is no frame to
        /// compose one from.
        ///
        /// <para><see cref="BoardStore.Remove"/> dissolves a group that falls below two members
        /// and leaves the survivor at the island origin, visibly and deliberately wrong, handing
        /// the fix to the caller. This is that caller and the only one: every path that can take
        /// a sheet out of a group — <see cref="Lay"/>, <see cref="Seat"/>,
        /// <see cref="Remove"/> — goes through here.</para>
        ///
        /// <para>Nothing to do for a group of three or more: the survivors keep the frame and
        /// their poses do not move.</para>
        /// </summary>
        Survivor NoteDissolution(SheetId leaving)
        {
            int groupId = GroupIdOf(leaving);
            if (groupId == 0) return default(Survivor);

            GroupRecord group;
            if (!TryGetGroup(groupId, out group) || group.MemberCount != 2) return default(Survivor);
            if (!group.Members[0].Equals(leaving) && !group.Members[1].Equals(leaving))
                return default(Survivor);

            SheetId other = group.Members[0].Equals(leaving) ? group.Members[1] : group.Members[0];

            V2 ground;
            double rotationDeg;
            if (!TryPoseOf(other, out ground, out rotationDeg)) return default(Survivor);

            return new Survivor(other, ground, rotationDeg);
        }

        /// <summary>Puts the survivor back where it was standing, as a loose sheet. The
        /// transform is written too, not because it has moved — it has not — but because the
        /// store now holds that pose and the two must be able to be compared.</summary>
        void Restore(Survivor survivor)
        {
            if (!survivor.Any) return;

            Laid entry;
            if (!placed.TryGetValue(survivor.Id, out entry) || entry.View == null) return;

            state.Lay(StateId, survivor.Id, survivor.Ground.X, survivor.Ground.Y,
                      survivor.RotationDeg);
            WritePose(entry.View, survivor.Ground, survivor.RotationDeg);
        }

        /// <summary>The sheet left behind when a pair is broken up, with the pose it had while
        /// the group still existed. A value rather than three out-parameters because it travels
        /// across the call that destroys the group it was read from.</summary>
        readonly struct Survivor
        {
            public readonly bool Any;
            public readonly SheetId Id;
            public readonly V2 Ground;
            public readonly double RotationDeg;

            public Survivor(SheetId id, V2 ground, double rotationDeg)
            {
                Any = true;
                Id = id;
                Ground = ground;
                RotationDeg = rotationDeg;
            }
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

            // C4.2 and C4.3, asked of the store instead of the furniture: a table carries one
            // island's paper. A stored board for a different island cannot be shown beside this
            // one, and leaving it bound would give the player a table that refuses the binders
            // lying on it. Clearing is C4.4's act, arrived at from the other side.
            if (state.IsBound(StateId) && state.IslandOf(StateId) != islandSeed)
            {
                Debug.LogWarning("[BoardView] Board " + StateId + " held island " +
                                 state.IslandOf(StateId).ToString("X16") + " and is being opened on " +
                                 islandSeed.ToString("X16") + " — the old arrangement is discarded.", this);
                state.Clear(StateId);
            }
            state.Bind(StateId, islandSeed);

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

            ReportUnlaid();

            build = null;
        }

        /// <summary>
        /// Says so when the board holds paper the cabinet never offered. The store keeps it —
        /// nothing is dropped and the next save still carries it — but no slab is made for a
        /// sheet with no raster, and a board that quietly showed eight of nine sheets would look
        /// like a lost placement rather than what it is.
        ///
        /// <para>The case that produces it is the room: binders and their contents are not saved
        /// (see <c>Archive</c>), so a restored board can name sheets no binder on the table
        /// holds.</para>
        /// </summary>
        void ReportUnlaid()
        {
            IReadOnlyList<SheetId> order = state.LayOrder(StateId);

            int missing = 0;
            for (int i = 0; i < order.Count; i++)
                if (!placed.ContainsKey(order[i])) missing++;

            if (missing > 0)
                Debug.LogWarning("[BoardView] " + missing + " sheet(s) of this board are not in " +
                                 "the cabinet and were not laid out. They stay in the board's " +
                                 "state and come back when their paper does.", this);
        }

        void BuildRig()
        {
            boardRoot = new GameObject("BoardRoot");
            boardRoot.transform.position = boardOrigin;

            int layer = UnityEngine.LayerMask.NameToLayer(TableLayerName);
            if (layer < 0)
                Debug.LogWarning("[BoardView] No '" + TableLayerName + "' layer — C5.1 needs one, " +
                                 "or the room's camera will draw the board.", this);

            BuildMountingSheet(boardRoot.transform, layer);
            BuildCamera(boardRoot.transform, layer);
        }

        /// <summary>The pale surface the sheets sit on. A quad, because the board has no
        /// thickness worth modelling and a plane would import a mesh nobody can tune. Its
        /// collider goes: C8.8 raycasts the Table layer for slabs, and a full-board collider
        /// would swallow every miss.</summary>
        void BuildMountingSheet(Transform parent, int layer)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "MountingSheet";
            Discard(quad.GetComponent<Collider>());

            quad.transform.SetParent(parent, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3((float)Space.BoardWidth, (float)Space.BoardHeight, 1f);
            quad.transform.localPosition = new Vector3(0f, -0.01f, 0f);

            mountingMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mountingMaterial.name = "M_MountingSheet";
            mountingMaterial.hideFlags = HideFlags.DontSave;
            mountingMaterial.color = new Color(0.94f, 0.94f, 0.93f);
            quad.GetComponent<MeshRenderer>().sharedMaterial = mountingMaterial;

            if (layer >= 0) quad.layer = layer;
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
            var go = new GameObject("BoardCamera");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 50f, 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.13f, 0.10f);

            // ABOVE the room's camera, explicitly. A Camera created in code defaults to
            // depth 0, and POC04_Room's Main Camera is also depth 0 — equal depths leave the
            // render order undefined, so the room draws over the board about as often as not.
            // The symptom is the worst kind: the cabinet appears (Screen Space Overlay, always
            // on top) while the main area shows the room, which reads as "the board failed to
            // build" rather than "two cameras are arguing".
            cam.depth = 100f;

            // ENABLED here, by the thing that builds it.
            //
            // It used to be created disabled and switched on by TableSession, one line after
            // `board.Show(seed)` — but Show is a COROUTINE, so at that point this camera did
            // not exist yet and `BoardCamera` was still null. The enable silently did nothing
            // and the board never appeared, while the cabinet (Screen Space Overlay, built
            // synchronously) did. That combination reads as "the board failed to render"
            // rather than "the camera was switched on before it was made".
            cam.enabled = true;

            if (layer >= 0) cam.cullingMask = 1 << layer;

            // The board is drawn ONLY where the board is: the screen minus the cabinet column.
            // Rendering full-bleed with the cream column laid over the right 22% made the
            // framing a lie — C8.13's floor is "the whole mounting sheet in view", and that 22%
            // sat behind an opaque panel — and it broke panning, because BoardViewport's clamp
            // (travel = max(0, boardHalf - viewHalf)) believed the hidden band was on screen and
            // refused to pan toward it.
            //
            // Narrowing the rect fixes both at the source rather than by adding an overscroll
            // margin: cam.aspect follows the rect, so BoardViewport's arithmetic is unchanged
            // and now describes the rectangle the player is actually looking at.
            // ScreenPointToRay and WorldToScreenPoint both account for a camera rect.
            //
            // The fraction is CabinetStyle's, the one place the column's width is stated (C7.1).
            // The header band is NOT subtracted: it is 96 reference pixels whose screen height
            // depends on the CanvasScaler's match, so it cannot become a viewport fraction
            // without asking the canvas — and vertical travel is non-zero above zoom 1 anyway.
            // Recorded, not fixed.
            cam.rect = new Rect(0f, 0f, 1f - CabinetStyle.CabinetWidthFraction, 1f);

            BoardCamera = cam;

            // After BoardCamera is set, because ApplyView writes through it — and after the
            // enable, so cam.aspect is the real viewport's rather than 1. G10.1's formula lives
            // in BoardViewport.OrthographicSize now and is unchanged: BoardHeight * 0.5 / Zoom,
            // divided and not multiplied, because orthographicSize is a half-HEIGHT and a
            // smaller number is a closer camera.
            viewport = new BoardViewport((float)Space.BoardWidth, (float)Space.BoardHeight,
                                         Zoom, ZoomMin, ZoomMax);
            ApplyView();
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
            double ppmm = PixelsPerMm;
            RenderJob cancel = job = new RenderJob();

            Task rendering = Task.Run(() => RenderInto(source, pending, ppmm, queue, cancel));

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
                    Relay(render.Id);

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
        static void RenderInto(Island island, List<Sheet> pending, double pixelsPerPaperMm,
                               ConcurrentQueue<SheetRender> done, RenderJob job)
        {
            var one = new List<Sheet>(1);

            for (int i = 0; i < pending.Count; i++)
            {
                if (job.Cancelled) return;

                one.Clear();
                one.Add(pending[i]);

                List<SheetRender> rendered = MapCrate.Render(island, one, pixelsPerPaperMm);
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
        /// One sheet back onto the board from the store, as its raster lands (§9). Nothing to do
        /// for a sheet that is not on this board, is already down, or belongs to a parked
        /// assembly — a parked group is in the drawer and has no slabs (G6.4).
        ///
        /// <para><b>It writes nothing back.</b> <see cref="Lay"/>, <see cref="Seat"/> and the
        /// group calls are gestures and each of them edits the model; this reads it. Laying a
        /// member here would take it out of its assembly on the way in (G4.1), and laying
        /// anything would stamp the pile with the order the rasters happened to land in instead
        /// of the order the player built (C4.7) — which is what the draw index is for.</para>
        /// </summary>
        void Relay(SheetId id)
        {
            if (!IsShowing || placed.ContainsKey(id)) return;

            Placement placement;
            if (!state.TryGetPlacement(StateId, id, out placement)) return;

            int at = LaidAtOf(id);

            if (placement.Seated)
            {
                Sheet sheet;
                if (!TrySheet(id, out sheet)) return;
                if (Put(id, sheet.CentreGround, sheet.RotationDeg, true, at) == null) return;
            }
            else if (placement.Grouped)
            {
                GroupRecord group;
                if (!TryGetGroup(placement.GroupId, out group) || !group.OnTable) return;

                // Provisionally at the frame's offset and then derived, exactly as
                // RetrieveGroup does it: Put needs a pose to write, and G3.1 has one
                // implementation.
                if (Put(id, FrameOf(placement.GroupId).Offset, 0.0, false, at) == null) return;
                Derive(placement.GroupId);
            }
            else if (Put(id, new V2(placement.GroundX, placement.GroundY),
                         placement.RotationDeg, false, at) == null)
            {
                return;
            }

            Resort();
        }

        /// <summary>Where this sheet sits in the store's lay order — the order the player built
        /// the board in (§3.3, C4.7). -1 for a sheet the board does not hold, which
        /// <see cref="Put"/> reads as "put it on top".</summary>
        int LaidAtOf(SheetId id)
        {
            IReadOnlyList<SheetId> order = state.LayOrder(StateId);
            for (int i = 0; i < order.Count; i++)
                if (order[i].Equals(id)) return i;

            return -1;
        }

        /// <summary>Lay or move, without the re-sort and the event — so <see cref="Seat"/> is one
        /// mutation and not two, and a subscriber never sees a board mid-change.
        ///
        /// <para><paramref name="laidAt"/> is the draw index for a sheet arriving from the store
        /// (§9); -1 means "as if just laid down", which is every gesture.</para></summary>
        BoardSheetView Put(SheetId id, V2 groundPos, double rotationDeg, bool seated, int laidAt = -1)
        {
            if (!IsShowing)
            {
                Debug.LogWarning("[BoardView] Lay/Seat before Show — nothing to lay on.", this);
                return null;
            }

            Laid entry;
            if (!placed.TryGetValue(id, out entry))
            {
                SheetRender render;
                if (!renders.TryGetValue(id, out render))
                {
                    // C5.7: the board opens before its rasters land, so this is a real state and
                    // not a bug. The cabinet keeps a row undraggable until TextureFor answers.
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

                BoardSheetView view = BoardSheetView.Create(
                    render.Sheet, id, render.IslandName, map,
                    SlabMaterial, MapTextureProperty, UnitsPerMetre);
                view.transform.SetParent(boardRoot.transform, false);

                int layer = UnityEngine.LayerMask.NameToLayer(TableLayerName);
                if (layer >= 0) SetLayerRecursive(view.gameObject, layer);

                int at = laidAt >= 0 ? laidAt : nextLaidAt;
                if (at >= nextLaidAt) nextLaidAt = at + 1;

                entry = new Laid(view, at);
                placed.Add(id, entry);
                layOrder.Add(entry);
            }

            entry.Seated = seated;
            WritePose(entry.View, groundPos, rotationDeg);
            return entry.View;
        }

        /// <summary>
        /// A ground pose onto a slab, and the only place this class writes one. Y is left
        /// exactly as it is: it is set by <see cref="Resort"/> from the draw index, because
        /// sheets overlap and order is a design element and not an accident (§3.3).
        ///
        /// <para>Ground X maps to board X and ground Y to board Z, so a ground rotation that
        /// takes +X toward +Y is a Unity yaw that takes +X toward +Z — and Unity's positive yaw
        /// goes the other way. Hence the negation, which <see cref="TryPoseOf"/> undoes on the
        /// way back out. Get this wrong and the board looks plausible but mirrored, which is
        /// the hardest kind of wrong to see; F-S1.2 verified the pair by outcome, so do not
        /// "fix" either half.</para>
        /// </summary>
        void WritePose(BoardSheetView view, V2 groundPos, double rotationDeg)
        {
            V2 centre = Space.ToBoard(groundPos);
            Transform t = view.transform;

            t.localPosition = new Vector3((float)centre.X, t.localPosition.y, (float)centre.Y);
            t.localRotation = Quaternion.Euler(0f, -(float)rotationDeg, 0f);
        }

        /// <summary>
        /// §3.3's draw order, applied as Y offsets <c>SheetSeparation</c> apart: seated sheets
        /// lowest, in office then sheet-number order because that is the order a solved survey
        /// reads in; unseated sheets above them, in the order they were laid down.
        ///
        /// <para>A seated sheet sinking below the unseated ones is the whole visual argument that
        /// the board is being assembled, so this runs after every mutation rather than only when
        /// something is added.</para>
        ///
        /// <para>Tiers 3 and 4 — selected topmost, dragged above that — are not applied here:
        /// they are properties of a pointer, and a view that owned them would need to be told
        /// about selection. The drag layer lifts its own slab.</para>
        /// </summary>
        void Resort()
        {
            AssignRuns();
            layOrder.Sort(CompareDrawOrder);

            onTable.Clear();
            float separation = Separation;

            for (int i = 0; i < layOrder.Count; i++)
            {
                Laid entry = layOrder[i];
                if (entry.View == null) continue;

                Vector3 p = entry.View.transform.localPosition;
                entry.View.transform.localPosition = new Vector3(p.x, i * separation, p.z);
                onTable.Add(entry.View);
            }
        }

        /// <summary>
        /// G5.6: a group's members take a <b>contiguous run</b> of tiers, in join order, so an
        /// assembly always reads as one coherent map and can never be interleaved with another
        /// group's paper.
        ///
        /// <para><b>The run sits where its oldest member sat</b> — the smallest
        /// <see cref="Laid.LaidAt"/> in the group. Anchoring on the newest lifts the whole
        /// assembly to the top of the pile every time one sheet joins it, moving paper the player
        /// never touched. This is the drawing half of <c>BoardStore</c>'s rule that a fuse does
        /// not reshuffle lay order.</para>
        ///
        /// <para>Both keys stay total. A group's anchor is one of its members' <c>LaidAt</c>
        /// values and those are unique across the board, so a loose sheet can never tie with a
        /// run — which matters because <c>List.Sort</c> is not stable and would happily permute
        /// equal elements into a different order on every mutation.</para>
        /// </summary>
        void AssignRuns()
        {
            for (int i = 0; i < layOrder.Count; i++)
            {
                Laid entry = layOrder[i];
                entry.RunAt = entry.LaidAt;
                entry.RunIndex = 0;
            }

            IReadOnlyList<GroupRecord> groups = Groups;
            for (int g = 0; g < groups.Count; g++)
            {
                GroupRecord group = groups[g];
                if (!group.OnTable || group.Members == null) continue;

                int anchor = int.MaxValue;
                for (int m = 0; m < group.Members.Count; m++)
                {
                    Laid entry;
                    if (!placed.TryGetValue(group.Members[m], out entry)) continue;
                    if (entry.LaidAt < anchor) anchor = entry.LaidAt;
                }
                if (anchor == int.MaxValue) continue;

                // Counted over the members actually present rather than over the member list,
                // so the run has no gaps even if the store's invariant is ever bent by a path
                // this class does not own.
                int index = 0;
                for (int m = 0; m < group.Members.Count; m++)
                {
                    Laid entry;
                    if (!placed.TryGetValue(group.Members[m], out entry)) continue;

                    entry.RunAt = anchor;
                    entry.RunIndex = index++;
                }
            }
        }

        /// <summary>Seated before unseated, then a total order inside each tier: seated sheets by
        /// identity — office, then the whole-island flag, then number — and unseated by
        /// <see cref="Laid.LaidAt"/>. Both keys are total, so the result does not depend on
        /// <c>List.Sort</c> being stable, which it is not. Ordering seated sheets by identity
        /// rather than by arrival is also what makes a reopened board look like the one that was
        /// closed (C4.7): a board that reordered itself between two openings would be
        /// unreadable.</summary>
        int CompareDrawOrder(Laid a, Laid b)
        {
            if (a.Seated != b.Seated) return a.Seated ? -1 : 1;

            if (a.Seated)
            {
                SheetId ia = a.View.Id, ib = b.View.Id;
                int byOffice = ((int)ia.Office).CompareTo((int)ib.Office);
                if (byOffice != 0) return byOffice;

                int byWhole = (ia.WholeIsland ? 1 : 0).CompareTo(ib.WholeIsland ? 1 : 0);
                if (byWhole != 0) return byWhole;

                return ia.Number.CompareTo(ib.Number);
            }

            // G5.6. For a loose sheet these are (LaidAt, 0), so this line is what it always was.
            int byRun = a.RunAt.CompareTo(b.RunAt);
            return byRun != 0 ? byRun : a.RunIndex.CompareTo(b.RunIndex);
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
                if (slabMaterial == null)
                {
                    slabMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    slabMaterial.name = "M_BoardSheet";
                    slabMaterial.hideFlags = HideFlags.DontSave;
                }
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
            layOrder.Clear();

            // The MODEL IS NOT TOUCHED (§9). Placements, assemblies and parked assemblies belong
            // to the table and outlive its rig; what dies here is everything made of pixels. A
            // board is emptied by clearing the table — the last binder coming off it, C4.4 — and
            // that path calls BoardStore.Clear itself.
            nextLaidAt = 0;
            onTable.Clear();
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

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
