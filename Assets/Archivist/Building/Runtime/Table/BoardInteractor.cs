using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Archivist.Building.Collection;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The hands on the cartography board: select, move, turn, snap (spec §8.3, §6). Slices S4
    /// and S5 of §11 live in one component because they are one gesture — whether a sheet glows
    /// and seats on release is a property of the drag, not of a separate mode.
    ///
    /// <para><b>The pose lives on the transform, and this is the only class that moves it.</b>
    /// C4.6 gives a seated sheet no stored pose, and <c>BoardView.Laid</c> holds a seated flag
    /// and a lay-order index and no coordinates, so for an unseated sheet the slab's
    /// <c>localPosition</c>/<c>localRotation</c> <i>is</i> the model. Read the pose back off the
    /// transform; never keep a shadow copy. <c>Q</c>/<c>E</c>, the corner handle and the pointer
    /// all edit "the rotation", and two of them writing a cached double while the third writes
    /// the transform makes the sheet jump back a frame after every input.</para>
    ///
    /// <para><b>Dragging calls <see cref="BoardView.Lay"/> exactly once</b>, on commit, which is
    /// what unseats a seated sheet (C6.7); after that the transform is written directly, and a
    /// release outside tolerance calls nothing (C6.6). <c>Lay</c> re-sorts the draw order and
    /// raises <c>Changed</c>, which the cabinet rebuilds from — at 60 Hz that is a 48-row
    /// accordion rebuilt every frame to report "this sheet moved 3 mm".</para>
    ///
    /// <para><b>The ground under the pointer comes from the board PLANE, never the collider
    /// hit.</b> <c>Physics.Raycast</c> answers C8.8's "which sheet is this" and stops there; the
    /// point it returns sits on the slab's own surface, lifted by the draw-order stack of §3.3.
    /// Placing from it makes a sheet follow its own surface rather than the pointer — zero error
    /// under the current straight-down orthographic camera, a visible slide the day the board is
    /// tilted, and a bug that will get blamed on the tilt. So the ray meets the board root's XZ
    /// plane and is inverted through <see cref="BoardSpace.ToGround"/>, because
    /// <see cref="SheetFit"/> compares in ground metres.</para>
    ///
    /// <para><b>The rotation convention is negated twice, and both halves are correct.</b>
    /// Ground X → board X and ground Y → board Z, so a ground rotation is a Unity yaw of the
    /// opposite sign: <c>Quaternion.Euler(0, -(float)rotationDeg, 0)</c>, and
    /// <see cref="RotationOf"/> negates again on the way back. F-S1.2 verified this by outcome —
    /// a sign error yields a board that is individually plausible and collectively mirrored.
    /// Fixing one half is how the pair gets broken.</para>
    ///
    /// <para><b>Q/E are held, not pressed, and read a 1D axis</b> (C8.15, C8.17). Turning is a
    /// continuous adjustment against an 8° tolerance, so a step size would be one more number to
    /// be wrong about, and an axis composite binds a stick or a shoulder pair later with no code
    /// change. With nothing selected they do nothing (C8.12).</para>
    ///
    /// <para><b>Selection is not a lock and neither is seating.</b> A sheet released outside
    /// tolerance stays where it was let go and this class calls nothing (C6.6); a seated sheet
    /// dragged is unseated on the spot (C6.7). R6.5 forbids error states, and a sheet that would
    /// not move would be the loudest one available.</para>
    ///
    /// <para><b>A group is the unit of interaction, and it inverts what the pose is</b> (G1.6).
    /// For a member the model is the group's <b>frame</b> and the transform is derived from it
    /// (G1.3, G4.3), so this class never writes a member's transform: it edits the frame through
    /// <see cref="BoardView.MoveGroup"/> and lets the view re-derive every member.
    /// <c>BoardStore.Lay</c> and <c>Seat</c> take a sheet <i>out</i> of its group, which on a
    /// two-member group dissolves the assembly, so a drag routed through <c>Lay</c> would
    /// silently unmake the thing being dragged. Hence: <b>a member moves by its group's frame;
    /// nothing here calls <c>Lay</c> on a member.</b> The C6.7 unseat is skipped for a member and
    /// costs nothing, because grouped and seated are mutually exclusive (G4.1).</para>
    ///
    /// <para><b>The glow promises fusing, not seating</b> (C6.4 kept, G5.1 underneath it). With
    /// absolute correctness out of scope (G1.9) nothing produces a seat, so a seating glow would
    /// be an offer the game no longer honours. It lights when a release <i>would</i> join
    /// something, and whatever makes the release join is what it reports.</para>
    ///
    /// <para><b>G7.1 is superseded here: with the assist on, a release joins at the ghost</b>
    /// (groups_spec §8.6). When <c>gameplay.assistedSnap</c> is on and
    /// <see cref="SnapHint.TryGhost"/> finds a related slab, the ghost is drawn at the pose the
    /// dragged sheet would take on joining and <see cref="Release"/> settles it there whatever
    /// pose it was let go at. That widens capture from <c>SheetFit</c>'s reach (1.54 board units
    /// on island 0's Land Survey, ≈61 px at <c>BoardZoom</c> 2) to the hint range (19.03 units,
    /// ≈750 px) and makes <c>RotationToleranceDeg</c> irrelevant while the assist is on.
    /// <b>Do not restore G7.1 by reflex</b> — the fix chosen was to make the signal true, not to
    /// make it smaller.</para>
    ///
    /// <para><b>With the assist OFF none of that happens.</b> <see cref="SnapHint.TryGhost"/>
    /// returns false on its first line and <see cref="Release"/> falls through to
    /// <see cref="TryBest"/> and the strict <c>SheetFit.Fits</c> path. C6.4 is not part of
    /// the assist and every player gets it.</para>
    ///
    /// <para><b>The invariant that must survive every edit: <see cref="Evaluate"/> and
    /// <see cref="Release"/> decide by the same rule, in the same order.</b> Both ask the ghost
    /// first and <see cref="TryBest"/> second; <c>snapping</c> is the disjunction and is what
    /// the rim and halo are drawn from. No frame may promise a join the release refuses, or
    /// perform one it did not promise. A third way to fuse goes into both branches or neither.
    /// </para>
    ///
    /// <para><b>Q/E and the corner handle pivot a group about a frozen point.</b> G5.4 puts the
    /// pivot at the union's bounding centre in board space, but that box is an AABB over the
    /// members' quads and changes shape as the assembly turns. Recomputed every frame it makes a
    /// turning group crawl across the board, which reads as drift rather than as a pivot. So it
    /// is taken once, on the rising edge of the turn, and held for the gesture.</para>
    ///
    /// <para><b>An assembly goes to the drawer whole and comes back whole</b> (G6.4, G6.5).
    /// Released over the cabinet a group is <i>parked</i> — <see cref="BoardView.ParkGroup"/> —
    /// keeping its membership and frame, and <see cref="PlaceGroup"/> lays it back under the
    /// pointer with its φ intact. Refiling the single member under the pointer would be a detach
    /// with the gesture filed off (G5.5). <b>Parking is not saving</b>: <c>BoardView.Hide</c>
    /// clears the group table, so a parked assembly does not survive closing the table — that is
    /// <c>spec.md</c> §9.</para>
    ///
    /// <para><b>The wheel zooms and the right button pans, and neither is a verb</b> (G10.1,
    /// superseding C8.13). <see cref="View"/> moves the <i>camera</i> and may not touch the board
    /// model: it never selects, deselects, unseats, fuses, or calls <c>Lay</c>, <c>MoveGroup</c>
    /// or <c>Remove</c>. The right button was chosen because it was unused on this board, so a
    /// pan cannot be mistaken for an existing gesture. The tolerances are the proof:
    /// <c>SheetFit</c>'s reach is in ground metres and <c>GlowingHintRange</c> in board units,
    /// and neither has ever read a camera.</para>
    ///
    /// <para><b><see cref="View"/> runs before the left-button verbs, on purpose.</b>
    /// <see cref="Hold"/> re-places the sheet at <c>TryGroundUnder(screen) + grabOffsetGround</c>
    /// <i>after</i> the view has moved, so paper stays under the cursor and travels with the
    /// view. The alternative — paper pinned to the ground while the view slides out from under
    /// it — makes a pan mid-drag a way to lose the sheet you are carrying, and removes the one
    /// thing panning mid-drag is for: carrying a sheet to a neighbour that was off screen.</para>
    ///
    /// <para><b>The cabinet's wheel wins over the board's</b> (C7.5). A notch over the panel's
    /// <c>ScrollRect</c> must scroll the accordion and must not also zoom the board underneath.
    /// The gate is <see cref="ReleaseOverCabinet"/> — named for releasing, but its content is
    /// "the pointer is over the cabinet", set from the panel's pointer enter/exit through
    /// <c>TableCanvas</c>. Right-drag is gated on the same flag.</para>
    ///
    /// <para>Nothing here draws from a random stream and nothing here is persisted, so no island
    /// can move because of anything in this file (§10). Board poses are player facts.</para>
    /// </summary>
    public sealed class BoardInteractor : MonoBehaviour
    {
        /// <summary>C8.8. Must match <c>BoardView</c>'s — the board camera renders only this
        /// layer and only slabs on it carry a collider.</summary>
        const string TableLayerName = "Table";

        /// <summary>Screen pixels the pointer must travel with the button down before a click
        /// (C8.9, select) becomes a drag (C6.7, unseat). Without it every click on a seated
        /// sheet would unseat it, which is C6.7 fired by the gesture C6.7 does not describe.
        /// </summary>
        const float DragThresholdPixels = 4f;

        /// <summary>Ceiling on how much zoom one frame may apply, in notches. A trackpad flick
        /// or a frame that swallowed several events can otherwise cross the whole range between
        /// two draws, which reads as the board jumping rather than as zooming.
        ///
        /// <para>1, not 4: four notches is 1.15^4 — 1.75x in a frame, and a trackpad sustaining
        /// it crosses the whole 1..4 range in three. At 1 the worst a frame can do is one notch,
        /// and a real wheel still zooms as fast as it can tick.</para></summary>
        const float MaxNotchesPerFrame = 1f;

        /// <summary>The selection outline of C8.9 / C6.8, from mockup <c>1a</c>.</summary>
        static readonly Color SelectionGold = new Color(0xC9 / 255f, 0xA0 / 255f, 0x63 / 255f);

        /// <summary>The snap glow of C6.4, from mockup <c>1c</c> — the same gold, hotter, so
        /// "inside tolerance" reads as the same affordance intensified rather than as a second
        /// unrelated colour. Both are literals here rather than fields on
        /// <c>TableOptions</c>: §10 enumerates that asset's contents and says "and no others",
        /// and these are not feel values settled by playing — the mockups are the authority on
        /// look (spec §1) and a palette that drifts from them is a bug, not a tuning.</summary>
        static readonly Color SnapGold = new Color(0xE6 / 255f, 0xA8 / 255f, 0x3E / 255f);

        [Header("Wiring")]
        [Tooltip("The board this drives. Null is resolved in Awake, as TableSession does.")]
        [SerializeField] BoardView board;

        [Tooltip("Feel values (§10). Null falls back to TableOptions' Default* constants, so " +
                 "the board is still draggable in a scene with no options asset.")]
        [SerializeField] TableOptions options;

        [Header("Input")]
        [Tooltip("The asset carrying the Table map's Turn action (C8.11, C8.14). Null is " +
                 "resolved in Awake from the loaded assets.")]
        [SerializeField] InputActionAsset inputActions;

        InputAction turnAction;

        // ---- selection and drag. There is deliberately no cached pose here: see the class
        // comment. Everything below is about the GESTURE, not about where the sheet is.

        SheetId? selected;

        /// <summary>
        /// The group the selection belongs to, or 0 when it is loose (G1.6). Derived from
        /// <see cref="selected"/> rather than replacing it: <c>SelectionChanged</c> carries a
        /// <see cref="SheetId"/> and the header captions a sheet, so the clicked member stays
        /// the selection and the group is what the <i>verbs</i> act on. Re-read on every
        /// selection and board change, because a fuse makes a loose selection grouped without
        /// the pointer touching it.
        /// </summary>
        int selectedGroup;

        /// <summary>Button is down on a slab, but the pointer has not yet moved
        /// <see cref="DragThresholdPixels"/> — so this is still a click.</summary>
        bool armed;
        Vector2 pressScreen;

        bool dragging;

        /// <summary>This drag began with <see cref="BeginPlace"/> on a sheet that was not on the
        /// board, so <see cref="CancelPlace"/> has something to undo.</summary>
        bool placing;

        /// <summary>
        /// This gesture is the one that PUT the paper on the table — a sheet pulled out of the
        /// cabinet (C7.4) or a group taken back out of the Groups drawer (G6.5). Cleared the
        /// moment that gesture ends, so the very next drag of the same paper is an ordinary one.
        ///
        /// <para><b>A landing gesture cannot join.</b> Fusing is suppressed for its whole
        /// length — no ghost, no seated band, no fuse on release. Without it the assist's
        /// widened capture (G7.1, ~19 board units) reaches most of the board, so a sheet dragged
        /// out of the drawer is swallowed by any related group it passes near and the player has
        /// no way to put paper down and look at it. Laying something out and joining it are two
        /// decisions, and the player has only made the first.</para>
        ///
        /// <para><b>Not <see cref="placing"/>, though they are set together for a sheet.</b>
        /// That flag answers "does cancelling this drag take the sheet back off the board?" and
        /// stays false for a retrieved group, because cancelling a retrieval must not refile the
        /// assembly (see <see cref="PlaceGroup"/>).</para>
        /// </summary>
        bool landing;

        /// <summary>Ground metres from the pointer to the slab's centre, frozen at the grab.
        /// Without it, grabbing the corner of a 19 × 13 unit Land Survey A1 teleports its centre
        /// under the cursor, which reads as having grabbed the wrong thing.
        ///
        /// <para>For a group it is the offset to the <b>frame's</b> <c>t</c>, the one number
        /// that moves an assembly, so a nine-sheet group grabbed by a corner sheet does not leap
        /// to put its frame origin under the cursor.</para></summary>
        V2 grabOffsetGround;

        /// <summary>True when a release, right now, would join something (C6.4). Recomputed
        /// every gesture frame in <see cref="Evaluate"/> as <c>ghost || TryBest</c> — the
        /// same disjunction, in the same order, that <see cref="Release"/> acts on. Not stored
        /// anywhere else and not consulted on release: release re-evaluates, because a glow from
        /// three frames ago is not an answer about where the sheet was let go.</summary>
        bool snapping;

        /// <summary>
        /// The assisted candidate for this frame, or <see cref="SnapHint.Ghost.None"/>. Held for
        /// <b>drawing</b> only — <see cref="PlaceOutline"/> runs in <c>LateUpdate</c> and would
        /// otherwise have to repeat the search <see cref="Evaluate"/> has just done, which is a
        /// second answer to a question that must have one.
        ///
        /// <para><b>It is deliberately NOT what the release acts on.</b> <see cref="Release"/>
        /// asks <see cref="SnapHint.TryGhost"/> again: the board can change under a drag — a
        /// texture lands, a row is clicked, a sheet is refiled — and a candidate from an earlier
        /// frame can name a slab that is gone. Re-asking costs one walk of the table per
        /// gesture, and is the same function on the same inputs, so preview and outcome still
        /// cannot disagree.</para>
        /// </summary>
        SnapHint.Ghost ghost;

        // ---- the view (G10.1, C8.13 superseded). Deliberately not part of the gesture
        // state above: EndGesture does not clear these and must not, because a pan is not a
        // gesture on the board and does not end when a drag does.

        /// <summary>The right button is down and a pan is running.</summary>
        bool panning;

        /// <summary>The board point under the cursor when the pan started, in board units. Held
        /// fixed for the drag; each frame the view moves by whatever it takes to put it back
        /// under the cursor, rather than by accumulating a pixels-to-units conversion. The
        /// conversion is then done once, by the camera, in the code that already does it
        /// (<see cref="TryGroundUnder"/>), so a pan is 1:1 by construction and self-correcting —
        /// a frame lost to a hitch leaves no accumulated error.</summary>
        Vector2 panAnchorBoard;

        // ---- the corner handle (C8.10)

        /// <summary>Q or E is currently down. Only its rising edge matters — see
        /// <see cref="Turn"/>.</summary>
        bool keyTurning;

        bool turningHandle;
        double handleTurned;      // accumulated, so a full revolution of the pointer works
        double handleLastAngle;
        double handleFromRotation;

        /// <summary>The frame offset a group turn started from, so each frame's rotation is
        /// applied to the pose at the grab rather than compounded onto the last one. Compounding
        /// a rotation about a pivot sixty times a second accumulates the pivot's own rounding
        /// and walks the assembly off it.</summary>
        V2 handleFromOffset;

        /// <summary>G5.4's pivot: the union's bounding centre, in ground metres, frozen for the
        /// length of one turn gesture — see the class comment for why it is not recomputed.
        /// </summary>
        V2 turnPivotGround;

        /// <summary>C8.10's knob. It owns the canvas and the pixel it was last drawn at;
        /// <see cref="PlaceHandle"/> decides which corner that pixel is, and the state above is
        /// what a grab on it turns.</summary>
        readonly BoardHandle handle = new BoardHandle();

        // ---- the settle (C6.5, G5.3)

        bool settling;

        /// <summary>The group being settled, or 0 when a single loose sheet is. The two are
        /// different animations of the same easing: a sheet's transform is interpolated toward
        /// its frame-relative pose, a group's <i>frame</i> is interpolated toward the target's.
        /// </summary>
        int settleGroup;

        SheetId settleId;
        Sheet settleTruth;
        V2 settleFrom;
        double settleFromRotation;
        double settleTurn;
        V2 settleTo;
        float settleElapsed;

        /// <summary>What the settle commits to when it finishes (G5.1). Held rather than applied
        /// up front so the join happens once the paper has arrived, which is the same shape
        /// <see cref="Seat"/> had — and so a settle abandoned by a fresh grab (C6.7 applied to
        /// the animation) fuses nothing.</summary>
        BoardFusing.Target settleTarget;

        // ---- the outline / glow quad (C6.8)

        /// <summary>C6.8's rim. It draws where and in whichever gold it is told;
        /// <see cref="PlaceOutline"/> decides both, because the colour is a function of the
        /// <c>snapping</c> that <see cref="Release"/> acts on.</summary>
        readonly BoardOutline outline = new BoardOutline();

        /// <summary>
        /// The assist's pulse (G7, S7) and the seated glow it leads to. It owns the halos for
        /// BOTH slabs of the pair — the candidate's and the dragged sheet's — so the look lives
        /// in one file and the two halves pulse from one clock and one curve (G7.5) by
        /// construction. This class owns only the steady <c>SelectionGold</c> / <c>SnapGold</c>
        /// rim of C6.8 and C6.4, which does not vary with the pulse.
        ///
        /// <para><b>Deliberately not a second fit test.</b> The assist decides joins now, but
        /// <c>SnapHint</c> may not touch a tolerance and may not call <c>SheetFit</c>. It answers
        /// two questions — is this slab related, is it near — and reports the frame that falls
        /// out of G3.1. <b>Deciding to fuse stays here</b>, in the one class that can keep
        /// <see cref="Evaluate"/> and <see cref="Release"/> the same answer.</para>
        /// </summary>
        SnapHint hint;

        /// <summary>
        /// G5.1's candidate search (see <see cref="BoardFusing"/>). It answers and never acts,
        /// which is what lets <see cref="Evaluate"/> and <see cref="Release"/> ask the same
        /// question a frame apart and be unable to disagree.
        ///
        /// <para><b>Asked through <see cref="TryBest"/>, always.</b> That wrapper is where the
        /// selection, the selected group and the cached group table are bound to the call — the
        /// three inputs both callers must supply identically.</para>
        /// </summary>
        BoardFusing fusing;

        /// <summary>The slabs whose Y this class has overridden for §3.3's tiers 3 and 4, so they
        /// can be put back down when they stop being selected. <c>BoardView.Resort</c> owns tiers
        /// 1 and 2 and deliberately does not implement these two. A list rather than one slab
        /// because G5.6 lifts a group's whole run together.</summary>
        readonly List<BoardSheetView> lifted = new List<BoardSheetView>();

        /// <summary>Scratch for the member walks below. Reused rather than allocated per frame:
        /// the drag path runs at 60 Hz and a group of nine is a list a frame otherwise. Never
        /// held across a call. <see cref="BoardFusing"/> keeps its own, for the same
        /// reason.</summary>
        readonly List<BoardSheetView> members = new List<BoardSheetView>();

        /// <summary>
        /// The board's groups, refreshed on <c>Changed</c> rather than re-asked per frame.
        /// <see cref="BoardView.Groups"/> builds a fresh list of values on every call and the
        /// candidate search of G5.1 runs every drag frame; groups cannot change under a drag
        /// anyway, because fusing is evaluated on release only (G1.5).
        /// </summary>
        readonly List<GroupRecord> groups = new List<GroupRecord>();
        bool groupsStale = true;

        /// <summary>The sheet the pointer has picked out, or null. Reading this is the only
        /// supported way to ask; the header and the cabinet mirror it through
        /// <see cref="SelectionChanged"/>.</summary>
        public SheetId? Selected { get { return selected; } }

        /// <summary>Raised whenever <see cref="Selected"/> changes, including to null. Fired
        /// after the field is updated, so a handler that reads <see cref="Selected"/> sees the
        /// new value — the alternative has bitten every codebase that has tried it.</summary>
        public event System.Action<SheetId?> SelectionChanged;

        /// <summary>
        /// True while the pointer is over the cabinet, set by the UI (C7.5).
        ///
        /// <para><b>A flag the cabinet sets, rather than a <c>RectTransform</c> serialised
        /// here.</b> The cabinet is built at runtime — panel, sections and rows all created in
        /// code, width a style constant, re-laid whenever the board raises <c>Changed</c> — so a
        /// serialised rect would be a second copy of a fact the cabinet owns, and would go wrong
        /// silently: a sheet dropped on the cabinet would land back on the board with no
        /// indication why. "Is the pointer over the cabinet" is already answered by the running
        /// UGUI raycaster, in the space it already works in. Until something sets it, a slab
        /// dragged over the cabinet just stays on the board — the C6.6 shape of failure.</para>
        ///
        /// <para><b>The name is narrower than the fact.</b> It is set live from
        /// <c>CabinetPanel</c>'s pointer enter/exit through
        /// <c>TableCanvas.OnPointerOverCabinet</c>, so it is true whenever the pointer is over
        /// the column with no button involved. <see cref="View"/> reads it as that: a wheel over
        /// the cabinet belongs to the accordion (C7.5, G10.4) and a right-drag started there is
        /// not aimed at the board.</para>
        /// </summary>
        public bool ReleaseOverCabinet { get; set; }

        float UnitsPerMetre { get { return options != null ? options.BoardUnitsPerMetre : TableOptions.DefaultBoardUnitsPerMetre; } }
        float Separation    { get { return options != null ? options.SheetSeparation    : TableOptions.DefaultSheetSeparation; } }
        float SettleSeconds { get { return options != null ? options.SettleSeconds      : TableOptions.DefaultSettleSeconds; } }
        float TurnRate      { get { return options != null ? options.SheetTurnDegreesPerSecond : TableOptions.DefaultSheetTurnDegreesPerSecond; } }
        float ZoomStep      { get { return options != null ? options.BoardZoomStep     : TableOptions.DefaultBoardZoomStep; } }
        float WheelSensitivity { get { return options != null ? options.WheelSensitivity  : TableOptions.DefaultWheelSensitivity; } }

        /// <summary>
        /// The rig's root, and therefore the plane the pointer is projected onto and the space
        /// every slab's <c>localPosition</c> is expressed in.
        ///
        /// <para><b>Reached through the camera's parent</b> because <c>BoardView</c> does not
        /// expose it: the root is at <c>boardOrigin</c>, hundreds of units from the room, and
        /// handing it out invites parenting something to it. <c>BoardView</c> makes both the
        /// camera and every slab children of that root, so the camera's parent <i>is</i> the
        /// root — and if that stops being true this returns null and the board goes inert rather
        /// than placing sheets in the wrong space.</para>
        /// </summary>
        Transform BoardRoot
        {
            get
            {
                Camera cam = board != null ? board.BoardCamera : null;
                return cam != null ? cam.transform.parent : null;
            }
        }

        void Awake()
        {
            // Resolved rather than required, as TableSession does and for its reason: every
            // reference here is a scene singleton, so a component dropped in with nothing
            // dragged onto it works, and one that HAS been wired keeps what it was given.
            // Inactive included — the board root is off until the table is opened (§5.1).
            if (board == null) board = FindFirstObjectByType<BoardView>(FindObjectsInactive.Include);

            // TableOptions is an asset, so FindFirstObjectByType cannot see it. BoardView
            // serialises a reference, so the loaded-object search finds the one the board is
            // already using — two components reading two different tolerances would glow at one
            // distance and seat at another. Nothing found leaves the Default* constants (§10).
            if (options == null) options = FindLoadedOptions();

            // After options, because SheetSeparation is the one value the hint cannot read off
            // a slab and it needs it to drop its quad under the paper by the same fraction this
            // class uses (BoardOutline.Drop). Null options leave it on the Default* constants.
            hint = new SnapHint(options);

            // Same asset, same reason: the search that decides a join and the hint that draws
            // one must judge by one set of tolerances.
            fusing = new BoardFusing(options);

            if (inputActions == null) inputActions = FindTableActions();
            if (inputActions == null)
            {
                Debug.LogWarning("[BoardInteractor] No InputActionAsset with a Table/Turn " +
                                 "action — Q and E will do nothing. The pointer still works.", this);
                return;
            }

            // throwIfNotFound on both: they exist in the asset today (C8.14 moved them there),
            // and a typo that silently produced a null action would present as "Q does
            // nothing", which is indistinguishable from half a dozen unrelated faults.
            InputActionMap tableMap = inputActions.FindActionMap("Table", throwIfNotFound: true);
            turnAction = tableMap.FindAction("Turn", throwIfNotFound: true);
        }

        /// <summary>
        /// The project's action asset, found by the one thing this class needs of it: a
        /// <c>Table</c> map with a <c>Turn</c> action.
        ///
        /// <para><c>TableSession</c> has a near-identical search matched on Player + Table +
        /// UI, deliberately not shared: each component asks for what it uses, so the day a fourth
        /// map appears neither has to be edited.</para>
        /// </summary>
        static TableOptions FindLoadedOptions()
        {
            return TableOptions.FindLoaded();
        }

        static InputActionAsset FindTableActions()
        {
            InputActionAsset[] all = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            for (int i = 0; i < all.Length; i++)
            {
                InputActionAsset a = all[i];
                if (a == null) continue;

                InputActionMap map = a.FindActionMap("Table");
                if (map != null && map.FindAction("Turn") != null) return a;
            }
            return null;
        }

        void OnEnable()
        {
            // Enabled individually, as PlayerHands and PlayerInteractor do: idempotent and
            // independent of wake order. TableSession still owns the map's lifetime (C8.14), so
            // Q and E go quiet with the table; enabling the action here only makes the board
            // driveable from the bench, where no session runs.
            if (turnAction != null) turnAction.Enable();

            if (board != null) board.Changed += OnBoardChanged;

            // The board can have been rebuilt while this was switched off, and Changed is the
            // only thing that marks the cache dirty.
            groupsStale = true;
        }

        void OnDisable()
        {
            if (board != null) board.Changed -= OnBoardChanged;

            // A drag abandoned mid-gesture would otherwise leave a slab lifted two tiers above
            // the stack for as long as the board is open, with nothing left running to put it
            // back.
            EndGesture();
            Deselect();
        }

        void OnDestroy()
        {
            handle.Dispose();
            outline.Dispose();
            if (hint != null) hint.Dispose();
        }

        /// <summary>A sheet can leave the board without the pointer being involved — the cabinet
        /// refiles it, or the board closes. Selection must not outlive the slab it names, or the
        /// header goes on captioning a sheet that is back in the drawer.</summary>
        void OnBoardChanged()
        {
            groupsStale = true;

            if (!selected.HasValue) return;
            if (board != null && board.IsShowing && board.IsOnTable(selected.Value))
            {
                // A fuse makes a loose selection grouped without the pointer touching it, and
                // the verbs below act on the group. Re-read rather than assume.
                selectedGroup = board.GroupIdOf(selected.Value);
                return;
            }

            EndGesture();
            Deselect();
        }

        /// <summary>The group table, re-listed only when the board says it has changed. See
        /// <see cref="groups"/>.</summary>
        IReadOnlyList<GroupRecord> Groups()
        {
            if (!groupsStale) return groups;

            groups.Clear();
            if (board != null)
            {
                IReadOnlyList<GroupRecord> live = board.Groups;
                for (int i = 0; i < live.Count; i++) groups.Add(live[i]);
            }
            groupsStale = false;
            return groups;
        }

        /// <summary>True when there is a board to interact with. Everything is gated on this
        /// rather than on the component being enabled, because this component stays enabled in
        /// the room: <c>TableSession</c> switches off the player's three components (C8.4) and
        /// knows nothing about this one.</summary>
        bool Live()
        {
            return board != null && board.IsShowing
                   && board.BoardCamera != null && BoardRoot != null;
        }

        void Update()
        {
            if (!Live())
            {
                if (selected.HasValue) { EndGesture(); Deselect(); }

                // Not in EndGesture: a pan is not a board gesture and EndGesture is called on
                // every ordinary release. It is dropped here because there is no longer a
                // camera to move, and leaving it armed would resume the pan mid-air on the next
                // opening if the button were still down.
                panning = false;

                handle.Hide();
                return;
            }

            Turn();       // C8.10/C8.11 — independent of the pointer, which is the point of it
            Advance();    // C6.5's settle
            Pointer();    // C8.8, C8.9, C6.4, C6.6, C6.7
        }

        void LateUpdate()
        {
            if (!Live()) return;

            // After Update, and after anything else that moved a slab this frame: BoardView
            // re-sorts the whole stack on every mutation, so a lift applied in Update would be
            // flattened by a texture landing mid-drag.
            Lift();
            PlaceOutline();
            PlaceHandle();
        }

        // ------------------------------------------------------------------ input

        /// <summary>
        /// C8.10's second input. Held rather than pressed, and read as a 1D axis rather than as
        /// two buttons — <c>PlayerHands</c>' reasoning, carried over with the verb (C8.17): a
        /// turn to read is a continuous adjustment, and Q and E are the two ends of one.
        ///
        /// <para>Suppressed while settling: those 0.18 s belong to C6.5's easing, and a key
        /// held through them would fight the interpolation and land the sheet at neither
        /// pose.</para>
        ///
        /// <para><b>It unseats once per press, not once per frame.</b> C6.7 applies to a turned
        /// seated sheet, but <c>Lay</c> re-sorts the board and raises <c>Changed</c>, so a key
        /// held for a second would do that sixty times. The unseat fires on the 0 → non-zero
        /// edge of the axis, the frame the sheet actually stops agreeing with the truth.
        ///
        /// <para>A <b>group</b> turns about G5.4's pivot, frozen on that same rising edge, and
        /// is never unseated — a member has no pose to stop agreeing with, and the <c>Lay</c>
        /// that would say so is the one call that would dissolve it.</para></para>
        /// </summary>
        void Turn()
        {
            if (settling || turningHandle) return;

            // A disabled or missing action reads 0, which is the same answer as "not pressed" —
            // so a scene with no action asset loses Q/E and keeps the pointer, rather than
            // throwing once a frame.
            float turn = turnAction == null ? 0f : turnAction.ReadValue<float>();

            // C8.12: with nothing selected they do nothing. Checked after the read so the edge
            // flag below cannot be left armed by a key held across a deselect.
            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;
            if (turn == 0f || view == null)
            {
                // C9.2's third point, for the one verb with no release: letting go of Q/E ends
                // an adjustment as much as letting go of the mouse does, and a turn nobody let
                // go of is saved by the table closing.
                if (keyTurning) Keep(selected);
                keyTurning = false;
                return;
            }

            // E (positive) increases the ground rotation, which is counter-clockwise on screen:
            // board +X is screen right and board +Z is screen up, and a ground rotation takes
            // +X toward +Y, which is +Z. Q is the same adjustment the other way.
            double step = turn * TurnRate * Time.deltaTime;

            if (selectedGroup != 0)
            {
                if (!keyTurning)
                {
                    keyTurning = true;
                    turnPivotGround = UnionCentreGround(selectedGroup);
                }

                TurnGroup(selectedGroup, step, turnPivotGround);
                return;
            }

            if (!keyTurning)
            {
                keyTurning = true;
                board.Lay(view.Id, GroundOf(view), RotationOf(view));   // C6.7
            }

            SetPose(view, GroundOf(view), RotationOf(view) + step);

            // No Evaluate here, either branch — Q/E does not preview, because it has no
            // release to honour (see Release). G1.5 puts the join on the release of a DRAG.
        }

        /// <summary>
        /// Turns a whole assembly by <paramref name="deltaDeg"/> about a ground pivot — G5.4's
        /// verb, expressed as the one write G4.3 allows.
        ///
        /// <para>Rotating every member about <c>P</c> means
        /// <c>pose'(M) = P + R(δ)·(pose(M) − P)</c>, and substituting G3.1 collapses that to a
        /// new frame: <c>φ' = φ + δ</c>, <c>t' = P + R(δ)·(t − P)</c>. Nine sheets turn by
        /// editing two numbers and no member is touched — the point of storing a frame rather
        /// than N poses, and the reason a half-turned group is unrepresentable.</para>
        ///
        /// <para>Board and ground space differ by a scale and a translation and no rotation
        /// (<see cref="BoardSpace"/>), so G5.4's pivot "in board space" and this one in ground
        /// metres are the same point.</para>
        /// </summary>
        void TurnGroup(int groupId, double deltaDeg, V2 pivotGround)
        {
            BoardFrame frame = board.FrameOf(groupId);
            V2 offset = pivotGround + (frame.Offset - pivotGround).RotateDeg(deltaDeg);
            board.MoveGroup(groupId, new BoardFrame(frame.RotationDeg + deltaDeg, offset));
        }

        void Pointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;      // no pointer device: Q/E above still work

            // The new Input System, not Input.mousePosition: the legacy API throws outright
            // when Active Input Handling is set to "Input System Package (New)", which is what
            // this project uses.
            Vector2 screen = mouse.position.ReadValue();

            // BEFORE the verbs, so a sheet held through a pan stays under the cursor: Hold
            // re-places it from the ground under `screen`, and that ground has just moved. See
            // the class comment — the order is the decision, not a side effect of it.
            View(mouse, screen);

            if (mouse.leftButton.wasPressedThisFrame) Press(screen);
            else if (mouse.leftButton.wasReleasedThisFrame) Release();
            else if (mouse.leftButton.isPressed) Hold(screen);
        }

        /// <summary>
        /// Zoom and pan (G10.1, C8.13 superseded outright). <b>This method may not touch the
        /// board model, and nothing it calls does</b> — see the class comment.
        ///
        /// <para>Both work through the board point under the cursor, so neither computes a
        /// pixels-per-unit factor of its own — a second copy is how a zoom and a pan end up
        /// disagreeing by a fraction of a unit per frame.</para>
        ///
        /// <para>Both are refused over the cabinet (C7.5). <b>Only the start</b> of a pan is
        /// gated: dragging out over the column mid-pan keeps panning, because stopping a drag at
        /// a rectangle's edge reads as a bug rather than as a boundary.</para>
        ///
        /// <para>Allocates nothing, and returns early on the common frame where the wheel is
        /// still and the button is up.</para>
        /// </summary>
        void View(Mouse mouse, Vector2 screen)
        {
            // Initialised because the short-circuit below leaves it unassigned when the
            // pointer is over the cabinet, and it is reused by both halves of this method
            // rather than declared twice — a pan and a zoom ask the same question.
            Vector2 anchor = Vector2.zero;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                panning = !ReleaseOverCabinet && TryBoardUnder(screen, out anchor);
                if (panning) panAnchorBoard = anchor;
            }
            else if (!mouse.rightButton.isPressed)
            {
                // Covers the release frame and every frame after it, and does not care which:
                // a button that is not down is not panning, whatever this class last thought.
                panning = false;
            }
            else if (panning && TryBoardUnder(screen, out anchor))
            {
                // Move the view by exactly the error between where the grabbed point is and
                // where it should be. Clamped inside MoveView, and the anchor is deliberately
                // NOT re-taken when it clamps — so pulling further against the edge and coming
                // back lands the board where it was, rather than having quietly slipped.
                board.MoveView(panAnchorBoard - anchor);
            }

            float raw = mouse.scroll.ReadValue().y;
            if (raw == 0f || ReleaseOverCabinet) return;

            // Bucket, scale, cap — all three in Wheel, which is also what the cabinet's
            // accordion reads, so the two wheels on this table cannot disagree about the
            // hardware. WheelSensitivity is the device dial; ZoomStep is the range one.
            float notches = Wheel.Notches(raw, WheelSensitivity, MaxNotchesPerFrame);
            if (notches == 0f) return;

            // Multiplicative, and about the pointer rather than the board centre — the two
            // decisions TableOptions.BoardZoomStep and BoardViewport.ZoomAbout each argue.
            if (TryBoardUnder(screen, out anchor))
                board.ZoomViewAbout(Mathf.Pow(ZoomStep, notches), anchor);
        }

        void Press(Vector2 screen)
        {
            // Grabbing a sheet mid-settle abandons the settle where it stands. C6.7's "seating
            // is not a lock" applies a fortiori to the animation into it.
            settling = false;

            // The knob first, and before the UGUI test: the handle is drawn with
            // raycastTarget off (it is a decoration this class hit-tests itself, not a button),
            // so the EventSystem cannot see it and would wave the press through to the slab
            // underneath.
            if (selected.HasValue && handle.Hit(screen))
            {
                BoardSheetView handled = ViewOf(selected.Value);
                if (handled != null)
                {
                    turningHandle = true;
                    handleTurned = 0.0;

                    if (selectedGroup != 0)
                    {
                        // The knob turns the assembly about G5.4's pivot, and both the pivot
                        // and the frame it started from are frozen here — see the class
                        // comment. No unseat: a member has no pose to unseat, and the Lay that
                        // would say so is the one call that dissolves a pair.
                        BoardFrame frame = board.FrameOf(selectedGroup);
                        turnPivotGround = UnionCentreGround(selectedGroup);
                        handleLastAngle = ScreenAngleAbout(GroundToWorld(turnPivotGround), screen);
                        handleFromRotation = frame.RotationDeg;
                        handleFromOffset = frame.Offset;
                        return;
                    }

                    handleLastAngle = ScreenAngle(handled, screen);
                    handleFromRotation = RotationOf(handled);

                    // C6.7 again: grabbing the knob is a deliberate act on the sheet, not a
                    // click that might come to nothing, so it unseats here rather than waiting
                    // for a threshold the way a drag does.
                    board.Lay(handled.Id, GroundOf(handled), RotationOf(handled));
                    return;
                }
            }

            // The chrome gets the click. Without this, clicking a cabinet row both scrolls the
            // accordion and clears the board selection, and pressing on the header deselects
            // the sheet the header is describing.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            BoardSheetView view = SlabUnder(screen);
            if (view == null)
            {
                // C8.9: empty board clears. Not "does nothing" — the cleared state is how the
                // player puts the header back to "None selected" without refiling anything.
                Deselect();
                return;
            }

            Select(view.Id);

            V2 ground;
            if (!TryGroundUnder(screen, out ground)) return;

            armed = true;
            pressScreen = screen;

            // G1.6: clicking any member grabs the assembly, so what the pointer is offset from
            // is the frame's t and not this slab's centre.
            grabOffsetGround = selectedGroup != 0
                ? board.FrameOf(selectedGroup).Offset - ground
                : GroundOf(view) - ground;
        }

        void Hold(Vector2 screen)
        {
            if (turningHandle)
            {
                BoardSheetView turned = selected.HasValue ? ViewOf(selected.Value) : null;
                if (turned == null) { turningHandle = false; return; }

                // Measured about G5.4's frozen pivot for an assembly and about the sheet's own
                // centre for a lone one — the two things the knob is turning.
                bool grouped = selectedGroup != 0;

                double angle = grouped
                    ? ScreenAngleAbout(GroundToWorld(turnPivotGround), screen)
                    : ScreenAngle(turned, screen);

                // Accumulated through AngleDelta rather than taken as (now - grab): the fold
                // into (-180, 180] is what makes dragging the knob a full revolution keep
                // turning instead of snapping back when it crosses the far side.
                handleTurned += SheetFit.AngleDelta(angle, handleLastAngle);
                handleLastAngle = angle;

                if (grouped)
                {
                    // Applied to the frame the grab started from, never compounded onto the
                    // last frame — see handleFromOffset.
                    V2 offset = turnPivotGround
                              + (handleFromOffset - turnPivotGround).RotateDeg(handleTurned);
                    board.MoveGroup(selectedGroup,
                                    new BoardFrame(handleFromRotation + handleTurned, offset));
                }
                else
                {
                    SetPose(turned, GroundOf(turned), handleFromRotation + handleTurned);
                }

                Evaluate();
                return;
            }

            if (!selected.HasValue) return;

            BoardSheetView view = ViewOf(selected.Value);
            if (view == null) { EndGesture(); return; }

            if (armed && !dragging)
            {
                if ((screen - pressScreen).magnitude < DragThresholdPixels) return;

                dragging = true;

                // C6.7, and the only place it happens: the sheet is being DRAGGED, so it is
                // unseated on the spot. Lay with the pose it already has, so the unseat costs no
                // movement.
                //
                // Skipped for a member, not an omission: grouped and seated are mutually
                // exclusive (G4.1), and this exact call is the one that would take the sheet out
                // of its group and dissolve a pair.
                if (selectedGroup == 0) board.Lay(view.Id, GroundOf(view), RotationOf(view));
            }

            if (!dragging) return;

            V2 ground;
            if (!TryGroundUnder(screen, out ground)) return;

            if (selectedGroup != 0)
            {
                // Exactly one frame is edited and every member follows (G5.4). φ is untouched:
                // a drag translates, it does not turn.
                BoardFrame frame = board.FrameOf(selectedGroup);
                board.MoveGroup(selectedGroup,
                                new BoardFrame(frame.RotationDeg, ground + grabOffsetGround));
            }
            else
            {
                SetPose(view, ground + grabOffsetGround, RotationOf(view));
            }

            Evaluate();
        }

        /// <summary>
        /// Letting go of the sheet. A knob turn ends here on the same terms as a drag, because
        /// the glow is a promise about <i>releasing</i> (C6.4): a player who turns the last two
        /// degrees with the handle, sees the sheet light up, and finds that letting go of the
        /// knob does nothing has been shown a state the game will not honour. Q/E has no release
        /// to honour, being an axis rather than a grip, and so does not preview.
        ///
        /// <para><b>Two ways to fuse, asked in the order <see cref="Evaluate"/> asks them:</b>
        /// the assist first (a showing ghost is a join, wherever the sheet actually is), the
        /// strict <see cref="TryBest"/> second. Both end in <see cref="Settle"/> with a
        /// <see cref="BoardFusing.Target"/>, so G5.1's four outcomes, G5.2 and G5.3's easing are one
        /// piece of code and not two parallel ones.</para>
        /// </summary>
        void Release()
        {
            // The same condition Evaluate() gates the ghost on, read from one place so the
            // preview and the release cannot disagree about whether a gesture is running.
            if (!Gesturing) { EndGesture(); return; }

            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;
            if (view == null) { EndGesture(); return; }

            SheetId id = view.Id;
            int group = selectedGroup;

            // Read before EndGesture clears it. A landing releases like any other gesture — it
            // can still be dropped on the cabinet — it simply cannot join.
            bool wasLanding = landing;

            EndGesture();

            // C7.5: dragged back to the cabinet is refiled. Checked before the snap, because a
            // sheet let go over the drawer is not being placed however well it happens to line
            // up with the ground underneath the chrome.
            if (ReleaseOverCabinet)
            {
                // G6.4: a GROUP dropped on the cabinet is PARKED, not refiled sheet by sheet.
                // It keeps its membership and frame and comes back whole through PlaceGroup;
                // refiling the one member under the pointer is a detach with the gesture filed
                // off (G5.5), and on a pair it dissolves the assembly.
                //
                // This is also what makes G5.5 tolerable — R6.5's "nothing is ever stuck" is
                // honoured by parking rather than by a detach gesture.
                //
                if (group != 0)
                {
                    board.ParkGroup(group);
                    Keep(null);
                    Deselect();
                    return;
                }

                board.Remove(id);
                Keep(null);
                Deselect();
                return;
            }

            // G5.1, evaluated on release and only on release (G1.5). The same two questions
            // Evaluate() has been asking every frame of this gesture, in the same order, so what
            // was previewed is what happens.
            BoardFusing.Target target;

            // 1. The assist, and G7.1 superseded. If a ghost is showing, the release joins THERE
            //    — whatever pose and angle the sheet was let go at, and with no Fits() in the
            //    path. Asked again rather than read off the field Evaluate() filled: the board
            //    can move under a drag, and a candidate from an earlier frame can name a slab
            //    that has since been refiled. With the assist off TryGhost returns false on its
            //    first line and this branch does not exist.
            SnapHint.Ghost g;
            if (!wasLanding
                && hint != null && hint.TryGhost(board, view, out g)
                && BoardFusing.TryAt(board, selected.Value, selectedGroup, g, out target))
            {
                Settle(target);
                return;
            }

            // 2. The strict path, unchanged. This is what every release did before the assist
            //    was given teeth, and it is still the whole of the game for a player who has the
            //    assist off.
            if (!wasLanding && TryBest(out target)) { Settle(target); return; }

            // C6.6, a deliberate absence of feedback: the sheet stays where it was released.
            // No error state, no colour, no message (R6.5).
            //
            // Not Lay, though — Lay re-sorts the draw order and raises Changed, which is a
            // 48-row cabinet rebuilt to report "this sheet moved 3 mm", fired by the one outcome
            // the spec says produces no feedback at all. Keep writes the pose the sheet already
            // has into the model silently, which is what C9.2 needs saved and C9.3 explains: a
            // near miss is a resting state (R6.5), not unfinished work, and losing it to an
            // unclean exit is the failure T6 was written against.
            Keep(id);
        }

        /// <summary>
        /// C9.2's save point, at the end of a gesture: the model catches up with the transform,
        /// and the archive is written (§9). <paramref name="id"/> is the loose sheet whose pose
        /// is now a fact, or null when the gesture moved no single sheet — a group, whose frame
        /// was written through on every drag frame (G4.3), or paper that went back to the drawer.
        ///
        /// <para>One call per gesture, never per frame (C9.4). A drag writes the file once, on
        /// release, and a board is a few dozen structs.</para>
        /// </summary>
        void Keep(SheetId? id)
        {
            if (board != null && id.HasValue) board.CommitPose(id.Value);
            Archive.Note();
        }

        // -------------------------------------------------------------- the verbs

        /// <summary>
        /// Starts a drag of a sheet the player has pulled out of the cabinet (C7.4). Lays it
        /// under the pointer and hands it to the ordinary drag path, so a sheet arriving from
        /// the drawer and a sheet already on the board are moved by exactly one piece of code.
        ///
        /// <para><b>Laid at rotation 0, never at its true rotation.</b> The Land Survey's whole
        /// lattice shares one angle (F-S1.4), so handing sheets out already turned would seat
        /// two thirds of the board's paper for free; orientation is part of the placement
        /// (POC-03 P2.6, C6.3).</para>
        /// </summary>
        public void BeginPlace(SheetId id)
        {
            if (!Live()) return;

            EndGesture();

            Vector2 screen = Vector2.zero;
            Mouse mouse = Mouse.current;
            if (mouse != null) screen = mouse.position.ReadValue();

            V2 ground;
            // A pointer that misses the plane cannot happen with a straight-down orthographic
            // camera, but the board centre is a sane place for a sheet to appear if it ever
            // does — it is on the mounting sheet and it is visible.
            if (!TryGroundUnder(screen, out ground)) ground = board.Space.GroundCentre;

            bool alreadyDown = board.IsOnTable(id);

            BoardSheetView view;
            int group = board.GroupIdOf(id);

            if (alreadyDown)
            {
                view = ViewOf(id);
                if (view == null) return;

                if (group != 0)
                {
                    // G6.2 makes a grouped sheet's office row inert, so the cabinet cannot
                    // reach this — but "cannot be reached through the UI" is how the Lay below
                    // would come to dissolve an assembly one day. The drag becomes a group
                    // drag, which is what G1.6 says it is, and nothing is laid.
                    grabOffsetGround = board.FrameOf(group).Offset - ground;
                }
                else
                {
                    // Keep the pose it has and grab it where the pointer is, exactly as Press
                    // does.
                    grabOffsetGround = GroundOf(view) - ground;
                    board.Lay(id, GroundOf(view), RotationOf(view));   // C6.7
                }
            }
            else
            {
                view = board.Lay(id, ground, 0.0);
                if (view == null) return;      // raster has not landed yet (C5.7)
                grabOffsetGround = V2.Zero;
                placing = true;
                landing = true;
            }

            SelectOnBoard(id);
            armed = true;
            dragging = true;
            pressScreen = screen;
            Evaluate();
        }

        /// <summary>
        /// G6.5: takes a parked assembly out of the Groups drawer and lays it back under the
        /// pointer, <b>preserving its frame rotation φ</b>. The counterpart of the park in
        /// <see cref="Release"/>.
        ///
        /// <para><b>φ survives, deliberately.</b> <see cref="BeginPlace"/> lays a single sheet
        /// at rotation 0 because resolving orientation is part of placing it (POC-03 P2.6,
        /// C6.3). A group has already had its orientation resolved — that is what made it a
        /// group (G3.3) — and with absolute correctness out of scope (G1.9) its φ carries no
        /// remaining puzzle. Resetting it would destroy the player's work to no end.</para>
        ///
        /// <para><b>It lands by the union's centre, not by the frame's <c>t</c>.</b> Writing the
        /// pointer straight into <c>t</c> — the way <see cref="BeginPlace"/> writes it into a
        /// lone sheet's pose — is wrong by kilometres: <c>t</c> is where the frame puts the
        /// island's <i>origin</i>, so on island 0 it flings a nine-sheet assembly by the
        /// members' own ground coordinates, off a 5940 × 5492 m board. The union's centre is
        /// what the gesture promised, because the row ghost being dragged is a thumbnail
        /// centred on the pointer (<c>CabinetRow</c>, C7.5).</para>
        ///
        /// <para><b><see cref="grabOffsetGround"/> is then measured from the resulting frame,
        /// never assumed zero.</b> <c>BeginPlace</c> can zero it because it puts a lone sheet's
        /// centre under the cursor; for a group the offset runs to the frame's <c>t</c>, metres
        /// from anything visible, so a zero would teleport the frame origin under the cursor on
        /// the first drag frame — the same fling, one frame later.</para>
        ///
        /// <para><b>No <see cref="placing"/> flag.</b> Cancelling would mean <c>board.Remove</c>
        /// on a member, which is a detach (G5.5) and on a pair a dissolution. A retrieved group
        /// goes back the way it came, by being dragged onto the cabinet (G6.4).</para>
        ///
        /// <para>Retrieval is only meaningful within one opening of the table: closing it
        /// destroys the group table, parked groups included.</para>
        /// </summary>
        public void PlaceGroup(int groupId)
        {
            if (!Live()) return;

            GroupRecord group;
            if (!board.TryGetGroup(groupId, out group)) return;
            if (group.Members == null || group.Members.Count == 0) return;

            EndGesture();

            Vector2 screen = Vector2.zero;
            Mouse mouse = Mouse.current;
            if (mouse != null) screen = mouse.position.ReadValue();

            V2 ground;
            if (!TryGroundUnder(screen, out ground)) ground = board.Space.GroundCentre;

            // The Groups row refuses the drag for a group already on the table (C7.4, G6.1),
            // but one can be fused onto while the row event is in flight. Retrieving it is then
            // a no-op that drags it anyway, which is the right outcome.
            if (!board.RetrieveGroup(groupId)) return;
            Archive.Note();                                     // G15.2

            // Read from the slabs RetrieveGroup just derived, not composed again from the
            // truths: TryUnion is the one implementation of "where is this assembly's middle",
            // shared with G5.4's turn pivot and the corner handle.
            SheetUnion union;
            if (TryUnion(groupId, out union))
            {
                V2 centre = board.Space.ToGround(new V2(union.CentreX, union.CentreZ));
                BoardFrame frame = board.FrameOf(groupId);

                // A pure translation: φ is passed through untouched (G6.5), and moving the
                // whole assembly is one frame write and no member touched (G5.4, G4.3).
                board.MoveGroup(groupId,
                                new BoardFrame(frame.RotationDeg, frame.Offset + (ground - centre)));
            }

            // G1.6: the clicked sheet is the selection and the group is what the verbs act on.
            // Nothing was clicked here, so the first member in join order stands for the
            // assembly — the same sheet the Groups row names (G6.3's "from n").
            SelectOnBoard(group.Members[0]);

            grabOffsetGround = board.FrameOf(groupId).Offset - ground;

            // G6.5 is a landing too, and the hazard is larger than a sheet's: an assembly
            // retrieved next to a related group would merge with it on release, in the gesture
            // the player used to take it OUT of the drawer.
            landing = true;

            armed = true;
            dragging = true;
            pressScreen = screen;
            Evaluate();
        }

        /// <summary>
        /// Abandons a <see cref="BeginPlace"/>. A sheet this call put on the board goes back to
        /// the drawer; a sheet that was already down keeps the pose it currently has, because
        /// cancelling a place is not an undo of wherever it happened to be before.
        ///
        /// <para>It does not undo a <see cref="PlaceGroup"/>: see there for why a group has no
        /// cancel and does not need one.</para>
        /// </summary>
        public void CancelPlace()
        {
            if (!dragging) { armed = false; placing = false; return; }

            SheetId? id = selected;
            bool remove = placing;

            EndGesture();

            if (remove && id.HasValue && board != null)
            {
                board.Remove(id.Value);
                Keep(null);
                Deselect();
                return;
            }

            // A sheet that was already down keeps the pose it is standing in and the selection
            // it has. Nothing to say to the board: it was unseated when the gesture began, and
            // the transform is the pose (C4.6).
        }

        // ------------------------------------------------------------- the settle

        /// <summary>
        /// G5.3: ease to the exact frame-relative pose over <c>SettleSeconds</c>, then join.
        ///
        /// <para>A dragged <b>sheet</b> eases its transform toward
        /// <c>frame.PositionOf(truth)</c> / <c>frame.RotationOf(truth)</c>; a dragged
        /// <b>group</b> eases its own frame toward the target's, which moves every member onto
        /// the same answer at once and is the only interpolation that cannot pull an assembly
        /// apart on the way. Same smoothstep, same duration, and the turn taken the short way
        /// through <c>AngleDelta</c> either way.</para>
        ///
        /// <para><b>The duration is FIXED and does not scale with distance</b>, which matters
        /// more since the assist: a settle used to move at most <c>reach</c> (1.54 board units,
        /// ≈61 px at <c>BoardZoom</c> 2), but an assisted settle travels <c>range + step</c> —
        /// measured over island 0's true neighbour pairs at <c>OverlapFraction</c> 0, up to
        /// 41.98 board units, 1652 px, 9176 px/s over 0.18 s. It is kept fixed because the
        /// ghost has already answered the question a slower move would answer: the slot was
        /// drawn at the landing pose for the whole approach, so the paper goes somewhere the
        /// player was already looking. The ordinary case is 7–15 units. Scaling it would need
        /// two new tuning values with no measurement behind them, and §10 enumerates
        /// <c>TableOptions</c>' contents "and no others". <b>First thing to watch in
        /// playtest.</b></para>
        ///
        /// <para><b>The membership is written when the ease finishes, not when it starts.</b>
        /// <c>BoardView</c> derives a member's transform the instant it joins, so committing
        /// first would snap the sheet onto the frame and leave nothing to animate.</para>
        /// </summary>
        void Settle(BoardFusing.Target target)
        {
            settleTarget = target;
            settleGroup = target.DraggedGroup;
            settleElapsed = 0f;
            settling = true;

            if (settleGroup != 0)
            {
                BoardFrame from = board.FrameOf(settleGroup);
                settleFrom = from.Offset;
                settleFromRotation = from.RotationDeg;
                settleTo = target.Frame.Offset;
                settleTurn = SheetFit.AngleDelta(target.Frame.RotationDeg, from.RotationDeg);
            }
            else
            {
                settleId = target.Dragged;
                if (!board.TrySheet(settleId, out settleTruth)) { settling = false; return; }

                V2 from;
                double fromRotation;
                if (!board.TryPoseOf(settleId, out from, out fromRotation))
                {
                    settling = false;
                    return;
                }

                settleFrom = from;
                settleFromRotation = fromRotation;
                settleTo = target.Frame.PositionOf(settleTruth);

                // The SHORT way round: a sheet 5° from its joining pose must not spin 355°.
                // AngleDelta is the same fold G3.3's test uses, so the thing that decides a fit
                // and the thing that plays it out cannot disagree.
                settleTurn = SheetFit.AngleDelta(target.Frame.RotationOf(settleTruth),
                                                 fromRotation);
            }

            if (SettleSeconds <= 0f) Commit();
        }

        /// <summary>
        /// C6.5's easing, kept by G5.3 — the same smoothstep <c>PlayerHands.Advance</c> uses,
        /// deliberately, so a sheet joining on the board reads as the same kind of movement as a
        /// sheet coming to the hands. Driven from <c>Update</c> rather than a coroutine because
        /// it is a small state machine and a coroutine would be a second lifetime to stop when
        /// the board closes mid-settle.
        /// </summary>
        void Advance()
        {
            if (!settling) return;

            settleElapsed += Time.deltaTime;

            float duration = SettleSeconds;
            float k = duration <= 0f ? 1f : Mathf.Clamp01(settleElapsed / duration);
            float eased = k * k * (3f - 2f * k);   // no sudden start, no sudden stop

            if (settleGroup != 0)
            {
                board.MoveGroup(settleGroup,
                                new BoardFrame(settleFromRotation + settleTurn * eased,
                                               V2.Lerp(settleFrom, settleTo, eased)));
            }
            else
            {
                BoardSheetView view = ViewOf(settleId);
                if (view == null) { settling = false; return; }

                SetPose(view,
                        V2.Lerp(settleFrom, settleTo, eased),
                        settleFromRotation + settleTurn * eased);
            }

            if (k < 1f) return;
            Commit();
        }

        /// <summary>
        /// The join itself — G5.1's four outcomes, written once the paper has arrived.
        ///
        /// <para><b>Re-asked, not assumed.</b> A texture can land, a row can be clicked and a
        /// sheet can be refiled inside the settle's 0.18 s, so each of the four calls below
        /// refuses rather than throws if the world moved. A fuse that quietly does not happen is
        /// the C6.6 shape of failure; a fuse to the wrong assembly is not recoverable.</para>
        ///
        /// <para><b>The frame is set exactly, not left where the easing stopped.</b> The
        /// interpolation is float work landing within a hair of the target; the group's frame is
        /// then written from the target's, so the derived poses are the frame's answer and not
        /// the animation's. This is where G5.2's "the stationary frame wins" is made
        /// literal.</para>
        /// </summary>
        void Commit()
        {
            settling = false;

            BoardFusing.Target target = settleTarget;
            settleTarget = default(BoardFusing.Target);

            bool draggedGroup = target.DraggedGroup != 0;
            bool targetGroup = target.TargetGroup != 0;

            if (draggedGroup)
            {
                // The dragged assembly adopts the stationary thing's frame either way (G5.2).
                board.MoveGroup(target.DraggedGroup, target.Frame);

                if (targetGroup) board.MergeGroups(target.TargetGroup, target.DraggedGroup);
                else board.AddToGroup(target.DraggedGroup, target.TargetSheet);
            }
            else if (targetGroup)
            {
                board.AddToGroup(target.TargetGroup, target.Dragged);
            }
            else
            {
                // Two loose sheets. The stationary one goes in first, so it stays under the
                // sheet just laid on it inside G5.6's run.
                board.CreateGroup(target.TargetSheet, target.Dragged, target.Frame);
            }

            // Changed re-read this on the way through, but Commit is also reachable from Settle
            // with SettleSeconds at 0, before the field exists to be refreshed. Asking again is
            // one dictionary lookup and removes the ordering question.
            if (selected.HasValue) selectedGroup = board.GroupIdOf(selected.Value);

            // The join is the release, finished (C9.2, G15.2): saving at Release would have
            // written the board as it was 0.18 s before the assembly existed.
            Keep(null);
        }

        // ------------------------------------------------------------- board space

        /// <summary>C8.8. The <c>Table</c> layer only: the room's geometry is on other layers and
        /// the board sits 500 units under it, but a mask that said "everything" would make the
        /// board's hit-testing depend on where in the world it happens to be built.</summary>
        BoardSheetView SlabUnder(Vector2 screen)
        {
            int layer = LayerMask.NameToLayer(TableLayerName);
            if (layer < 0) return null;

            RaycastHit hit;
            if (!Physics.Raycast(board.BoardCamera.ScreenPointToRay(screen), out hit,
                                 Mathf.Infinity, 1 << layer))
                return null;

            // GetComponentInParent, not GetComponent: the collider is on the slab's root today,
            // and a slab that later grows a child collider must not become unclickable.
            return hit.collider != null ? hit.collider.GetComponentInParent<BoardSheetView>() : null;
        }

        /// <summary>
        /// The ground metres under the pointer, via the board PLANE — see the class comment for
        /// why not <c>hit.point</c>.
        ///
        /// <para>The plane is the board root's own XZ plane (<c>root.up</c> through
        /// <c>root.position</c>), so a rig that is ever moved or turned still works, and the hit
        /// is converted into the root's LOCAL space before <see cref="BoardSpace.ToGround"/>
        /// because that is the space slab <c>localPosition</c>s — and therefore
        /// <c>BoardSpace.ToBoard</c>'s output — live in.</para>
        /// </summary>
        bool TryGroundUnder(Vector2 screen, out V2 ground)
        {
            ground = V2.Zero;

            Transform root = BoardRoot;
            if (root == null) return false;

            Ray ray = board.BoardCamera.ScreenPointToRay(screen);
            var plane = new Plane(root.up, root.position);

            float distance;
            if (!plane.Raycast(ray, out distance)) return false;

            Vector3 local = root.InverseTransformPoint(ray.GetPoint(distance));
            ground = board.Space.ToGround(new V2(local.x, local.z));
            return true;
        }

        /// <summary>
        /// The same point as <see cref="TryGroundUnder"/>, in board units instead of ground
        /// metres — what the view wants, because a camera is framed in board units and knows
        /// nothing about ground.
        ///
        /// <para>Layered on <see cref="TryGroundUnder"/> rather than intersecting the plane a
        /// second time: that method is already correct for any orthographic size and camera
        /// position, which is exactly what zoom and pan change. The round trip through
        /// <c>ToGround</c> and back is a divide and a multiply by one constant.</para>
        ///
        /// <para><c>y</c> of the result is board <b>Z</b>, <see cref="BoardSpace"/>'s
        /// convention.</para>
        /// </summary>
        bool TryBoardUnder(Vector2 screen, out Vector2 boardPoint)
        {
            boardPoint = Vector2.zero;

            V2 ground;
            if (!TryGroundUnder(screen, out ground)) return false;

            V2 b = board.Space.ToBoard(ground);
            boardPoint = new Vector2((float)b.X, (float)b.Y);
            return true;
        }

        /// <summary>Where a slab currently is, in ground metres. Read back out of the transform
        /// every time rather than cached — the transform is the pose (C4.6).</summary>
        V2 GroundOf(BoardSheetView view)
        {
            Vector3 p = view.transform.localPosition;
            return board.Space.ToGround(new V2(p.x, p.z));
        }

        /// <summary>The inverse of the negation in <see cref="SetPose"/>. F-S1.2 verified the
        /// sign by outcome; negating on the way in and not on the way out would compare the
        /// player's angle against its own mirror image, and the symptom would be sheets seating
        /// only at 0° and 180°.</summary>
        double RotationOf(BoardSheetView view)
        {
            return -view.transform.localEulerAngles.y;
        }

        /// <summary>
        /// Writes a ground pose onto a slab. Y is left exactly as it is: it belongs to the
        /// draw-order stack of §3.3 — tiers 1 and 2 to <c>BoardView.Resort</c>, tiers 3 and 4 to
        /// <see cref="Lift"/> — and a pose setter that touched it would drop the dragged sheet
        /// back into the pile every frame of the drag.
        /// </summary>
        void SetPose(BoardSheetView view, V2 ground, double rotationDeg)
        {
            V2 b = board.Space.ToBoard(ground);
            Transform t = view.transform;

            t.localPosition = new Vector3((float)b.X, t.localPosition.y, (float)b.Y);

            // Ground X → board X and ground Y → board Z, so a ground rotation that takes +X
            // toward +Y is a Unity yaw that takes +X toward +Z — and Unity's positive yaw goes
            // the other way. F-S1.2: verified correct, do not "fix".
            t.localRotation = Quaternion.Euler(0f, -(float)rotationDeg, 0f);
        }

        BoardSheetView ViewOf(SheetId id)
        {
            return BoardSlabs.ViewOf(board, id);
        }

        // -------------------------------------------------------------- draw order

        /// <summary>
        /// §3.3's tiers 3 (selected topmost) and 4 (dragged above that), which
        /// <c>BoardView.Resort</c> does not implement — they are properties of a pointer, not of
        /// the board. The drag layer lifts its own slab in board units of
        /// <c>SheetSeparation</c>, from the top of whatever stack the board sorted.
        ///
        /// <para>Applied every <c>LateUpdate</c>, not once: <c>Resort</c> runs on every board
        /// mutation — including a texture landing behind the player's back (C5.7) — and would
        /// otherwise flatten the lift mid-drag.</para>
        /// </summary>
        void Lift()
        {
            // G5.6: the tiers are a property of the GROUP, not of the clicked member. A run
            // lifted one sheet at a time would leave the rest of the assembly buried under the
            // paper the run was supposed to be contiguous with.
            members.Clear();
            if (selected.HasValue)
            {
                if (selectedGroup != 0) MembersOf(selectedGroup, members);
                else
                {
                    BoardSheetView one = ViewOf(selected.Value);
                    if (one != null) members.Add(one);
                }
            }

            for (int i = 0; i < lifted.Count; i++)
            {
                BoardSheetView was = lifted[i];
                if (was != null && !members.Contains(was)) Lower(was);
            }

            lifted.Clear();
            lifted.AddRange(members);
            if (members.Count == 0) return;

            int tiers = (dragging || settling) ? 2 : 1;
            float baseY = (board.OnTable.Count - 1 + tiers) * Separation;

            // The run keeps its own order above the stack, so an assembly reads the same way
            // lifted as it does lying down.
            for (int i = 0; i < members.Count; i++)
            {
                Transform t = members[i].transform;
                Vector3 p = t.localPosition;
                t.localPosition = new Vector3(p.x, baseY + i * Separation, p.z);
            }
        }

        /// <summary>Puts a no-longer-selected slab back on the tier the board sorted it into.
        /// Its index in <c>OnTable</c> is that tier by definition — <c>Resort</c> writes
        /// <c>i * separation</c> and fills the list in the same pass.</summary>
        void Lower(BoardSheetView view)
        {
            if (view == null) return;

            IReadOnlyList<BoardSheetView> table = board.OnTable;
            for (int i = 0; i < table.Count; i++)
            {
                if (table[i] != view) continue;

                Vector3 p = view.transform.localPosition;
                view.transform.localPosition = new Vector3(p.x, i * Separation, p.z);
                return;
            }
        }

        void MembersOf(int groupId, List<BoardSheetView> into)
        {
            BoardSlabs.MembersOf(board, groupId, into);
        }

        // ------------------------------------------------------ outline and glow

        /// <summary>
        /// C6.4, and the one place <c>snapping</c> is decided. Re-asked every frame of a gesture,
        /// which is what makes the glow a preview of the release rather than a report on it
        /// (C1.5).
        ///
        /// <para><b>Written to mirror <see cref="Release"/> line for line</b>: ghost first,
        /// <see cref="TryBest"/> second, <c>snapping</c> true if either answered. The
        /// ordering is not cosmetic — a frame on which both could fire must resolve to the same
        /// target in the preview and in the outcome. Change the rule in one of these two methods
        /// and not the other and the board starts lying.</para>
        ///
        /// <para><b>Gated on a live gesture.</b> A ghost is a promise about letting go, and Q/E
        /// has no release to make one about. <see cref="Gesturing"/> is exactly the condition
        /// <see cref="Release"/> requires before it fuses, so the two agree by construction
        /// rather than by two matching tests.</para>
        /// </summary>
        void Evaluate()
        {
            ghost = SnapHint.Ghost.None;

            if (Joining && hint != null && board != null && selected.HasValue)
            {
                BoardSheetView view = ViewOf(selected.Value);
                if (view != null) hint.TryGhost(board, view, out ghost);
            }

            // G7.1 superseded: a ghost IS a join. Reported first and reported as snapping, so
            // the seated band the player sees is telling the truth at 750 px and not only at 61.
            if (ghost.Any) { snapping = true; return; }

            // A landing gesture previews nothing, by the same rule that stops it fusing. The
            // strict path is asked only when a join is actually on the table.
            if (!Joining) { snapping = false; return; }

            BoardFusing.Target ignored;
            snapping = TryBest(out ignored);
        }

        /// <summary>G5.1's strict search, bound to what is selected right now. Both callers go
        /// through here so neither can pass a different dragged thing or a staler group table
        /// than the other.</summary>
        bool TryBest(out BoardFusing.Target best)
        {
            best = default(BoardFusing.Target);
            if (fusing == null || board == null || !selected.HasValue) return false;

            return fusing.TryBest(board, selected.Value, selectedGroup, Groups(), out best);
        }

        /// <summary>A drag or a knob turn is running — the two gestures that end in
        /// <see cref="Release"/>, and therefore the two on which a promise about releasing can
        /// be made. <see cref="Release"/> uses the same disjunction to decide whether there is a
        /// gesture to end at all.</summary>
        bool Gesturing { get { return dragging || turningHandle; } }

        /// <summary>A gesture that may end in a join: one is running, and it is not the gesture
        /// that put the paper on the table (<see cref="landing"/>). <see cref="Evaluate"/> gates
        /// the whole preview on this and <see cref="Release"/> gates the fuse on it, so a
        /// landing shows no promise and keeps none.</summary>
        bool Joining { get { return Gesturing && !landing; } }

        /// <summary>
        /// C6.8's rim, once per frame: which pose it takes and which of the two golds it wears.
        /// <see cref="BoardOutline"/> owns the quad; everything decided here is a function of
        /// <c>snapping</c>, the value <see cref="Release"/> acts on.
        /// </summary>
        void PlaceOutline()
        {
            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;
            if (view == null) { DetachOutline(); return; }

            Transform root = BoardRoot;
            if (root == null) { DetachOutline(); return; }

            Mesh mesh = null;
            MeshFilter filter = view.GetComponent<MeshFilter>();
            if (filter != null) mesh = filter.sharedMesh;

            // Two states. `snapping` is the whole condition: true exactly when a release would
            // join something, whether by the assist's ghost or SheetFit's strict tolerance, so
            // the rim and the halo cannot show a promise the release will not keep. THAT IS THE
            // INVARIANT — everything drawn below is a function of the value Release() acts on.
            //
            // G7.2's middle rung ("related and near but a release does nothing") is gone with
            // G7.1: near enough to hint IS near enough to join. The assisted case draws the
            // SEATED band — one look for one meaning — plus the ghost, which says WHERE.
            //
            // THE RIM IS NOT WHAT MOVES: a ~5 px hairline is too small to carry a pulse or the
            // difference between two states. The halo and the ghost carry it, inside SnapHint.
            //
            // Time.unscaledTime, not Time.time: one shared monotonic clock means the ghost
            // breathes on G7.5's curve with nothing arranging its phase, and a paused game
            // cannot freeze it half way through.
            Color colour;
            if (snapping)
            {
                colour = SnapGold;

                if (hint != null)
                {
                    if (ghost.Any) hint.Show(board, ghost, Time.unscaledTime);
                    else hint.ShowSeated(board, view);
                }
            }
            else
            {
                colour = SelectionGold;
                if (hint != null) hint.Clear();
            }

            SheetUnion union;
            if (selectedGroup != 0 && TryUnion(selectedGroup, out union))
            {
                // G5.4: round the UNION of the members' quads, not round the clicked one. The
                // borrowed mesh is one member's quad, so it is scaled to cover the box — the
                // 1.02 rim of C6.8 applied to the assembly instead of to a sheet.
                float mw = (float)(view.Sheet.Survey.SheetGroundWidth * UnitsPerMetre);
                float mh = (float)(view.Sheet.Survey.SheetGroundHeight * UnitsPerMetre);
                if (mw <= 0f || mh <= 0f) { DetachOutline(); return; }

                // Axis-aligned, deliberately not turned with the assembly — see SheetUnion. The
                // board-space box is also what G5.4 measures the pivot and the corner handle
                // from, which keeps the three agreeing.
                outline.Place(root, mesh,
                              new Vector3(union.CentreX,
                                          union.LowestY - Separation * BoardOutline.Drop,
                                          union.CentreZ),
                              Quaternion.identity,
                              new Vector3(union.Width * BoardOutline.Scale / mw, 1f,
                                          union.Height * BoardOutline.Scale / mh),
                              colour);
                return;
            }

            Transform t = view.transform;
            Vector3 p = t.localPosition;

            outline.Place(root, mesh,
                          new Vector3(p.x, p.y - Separation * BoardOutline.Drop, p.z),
                          t.localRotation,
                          new Vector3(BoardOutline.Scale, 1f, BoardOutline.Scale),
                          colour);
        }

        /// <summary>G5.4's box for a group's members — the one implementation of "where is this
        /// assembly", shared by the outline, the corner handle and the turn pivot. Fills the
        /// scratch list and hands it to <see cref="SheetUnion"/>; never held across a
        /// call.</summary>
        bool TryUnion(int groupId, out SheetUnion union)
        {
            members.Clear();
            MembersOf(groupId, members);

            return SheetUnion.TryOf(members, UnitsPerMetre, out union);
        }

        /// <summary>G5.4's pivot in ground metres. Falls back to the frame's own offset when the
        /// group has no slabs to measure — a pivot that is merely arbitrary, rather than a turn
        /// that does not happen.</summary>
        V2 UnionCentreGround(int groupId)
        {
            SheetUnion union;
            if (!TryUnion(groupId, out union)) return board.FrameOf(groupId).Offset;

            return board.Space.ToGround(new V2(union.CentreX, union.CentreZ));
        }

        /// <summary>A ground point as a world point on the board plane — for the screen-space
        /// arithmetic the corner handle does, which needs the pivot where the camera can see
        /// it.</summary>
        Vector3 GroundToWorld(V2 ground)
        {
            Transform root = BoardRoot;
            V2 b = board.Space.ToBoard(ground);
            Vector3 local = new Vector3((float)b.X, 0f, (float)b.Y);
            return root != null ? root.TransformPoint(local) : local;
        }

        void DetachOutline()
        {
            outline.Hide();

            // The hint's quads go dark on the same terms. Every one of PlaceOutline's early
            // returns lands here — no selection, no board root, a degenerate sheet — and a
            // candidate left glowing for a sheet that is no longer selected is a pointer at
            // nothing.
            if (hint != null) hint.Clear();
        }

        // ------------------------------------------------------- the corner handle

        /// <summary>
        /// C8.10's first input: where the knob of mockup <c>1c</c> sits this frame.
        /// <see cref="BoardHandle"/> draws it and hit-tests it; the corner is chosen here,
        /// because only this class knows what is selected.
        ///
        /// <para><b>A fixed corner in the sheet's own space</b> — its +X/+Z one — rather than
        /// whichever corner is currently top-right on screen. It then travels round with the
        /// sheet as it turns, which is what makes it read as attached to the paper; a knob that
        /// jumped between corners as the sheet passed 45° would read as a second control.</para>
        /// </summary>
        void PlaceHandle()
        {
            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;
            if (view == null) { handle.Hide(); return; }

            Vector3 corner;

            SheetUnion union;
            if (selectedGroup != 0 && TryUnion(selectedGroup, out union))
            {
                // G5.4: at the UNION's +X/+Z corner, the same corner a lone sheet's knob sits
                // at. It does not travel round with the paper, because that box is axis-aligned
                // (see SheetUnion).
                Transform root = BoardRoot;
                Vector3 local = new Vector3(union.MaxX, union.LowestY, union.MaxZ);
                corner = root != null ? root.TransformPoint(local) : local;
            }
            else
            {
                float hw = (float)(view.Sheet.Survey.SheetGroundWidth * UnitsPerMetre * 0.5);
                float hh = (float)(view.Sheet.Survey.SheetGroundHeight * UnitsPerMetre * 0.5);
                corner = view.transform.TransformPoint(new Vector3(hw, 0f, hh));
            }

            Vector3 point = board.BoardCamera.WorldToScreenPoint(corner);
            if (point.z < 0f) { handle.Hide(); return; }

            handle.Place(new Vector2(point.x, point.y));
        }

        /// <summary>The pointer's bearing from the sheet's CENTRE, in degrees, counter-clockwise
        /// — the same sense a ground rotation runs in, because board +X is screen right and
        /// board +Z is screen up. About the centre and not the corner, because C8.10 says the
        /// handle "sets the angle about the sheet's centre".</summary>
        double ScreenAngle(BoardSheetView view, Vector2 screen)
        {
            return ScreenAngleAbout(view.transform.position, screen);
        }

        /// <summary>The same bearing about an arbitrary world point — G5.4's union centre, when
        /// what is being turned is an assembly. One implementation, because the sheet case is
        /// the group case with one member and a knob that measured its angle from two different
        /// origins would turn at two different rates.</summary>
        double ScreenAngleAbout(Vector3 world, Vector2 screen)
        {
            Vector3 centre = board.BoardCamera.WorldToScreenPoint(world);
            return Mathf.Atan2(screen.y - centre.y, screen.x - centre.x) * Mathf.Rad2Deg;
        }

        // --------------------------------------------------------------- selection

        /// <summary>
        /// Selects from outside — the cabinet's row click (C7.6).
        ///
        /// <para><b>A sheet still in the drawer clears the board selection rather than becoming
        /// it.</b> There is no slab to outline and nothing for <c>Q</c>/<c>E</c> to turn, so the
        /// alternative is a header naming one sheet while the rotate keys move a different,
        /// still-outlined one — which looks like the keys are broken.</para>
        ///
        /// <para>Ignored while a gesture is running: a rebuild of the accordion can fire a row
        /// event, and moving the selection out from under a drag would leave a slab following a
        /// pointer that no longer owns it.</para>
        /// </summary>
        public void Select(SheetId? id)
        {
            if (dragging) return;

            if (!id.HasValue || board == null || !board.IsOnTable(id.Value)) { Deselect(); return; }

            SelectOnBoard(id.Value);
        }


        /// <summary>G1.6/G5.4: clicking any member selects the whole assembly. The clicked
        /// sheet stays the <see cref="Selected"/> value — the header captions a sheet — and
        /// <see cref="selectedGroup"/> is what the verbs act on. Re-read even when the sheet
        /// has not changed, because a fuse can make the same selection grouped.</summary>
        void SelectOnBoard(SheetId id)
        {
            selectedGroup = board != null ? board.GroupIdOf(id) : 0;

            if (selected.HasValue && selected.Value.Equals(id)) return;

            selected = id;
            snapping = false;
            ghost = SnapHint.Ghost.None;
            Raise();
        }

        void Deselect()
        {
            selectedGroup = 0;
            if (!selected.HasValue) return;

            selected = null;
            snapping = false;
            ghost = SnapHint.Ghost.None;
            Raise();
        }

        void Raise()
        {
            System.Action<SheetId?> handler = SelectionChanged;
            if (handler != null) handler(selected);
        }

        /// <summary>Drops the gesture without touching the selection or the pose. Every exit
        /// from a drag goes through here so no path can leave <c>dragging</c> true with the
        /// button up — which would be a sheet that follows the pointer for ever.</summary>
        void EndGesture()
        {
            armed = false;
            dragging = false;
            placing = false;
            landing = false;
            turningHandle = false;
            snapping = false;

            // Dropped with the gesture it belonged to: a ghost is a promise about a release
            // that is now over, and one left in this field would be drawn on the next frame
            // that happened to find `snapping` true for a different reason.
            ghost = SnapHint.Ghost.None;
        }
    }
}
