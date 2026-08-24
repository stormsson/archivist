using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Archivist.Building.Collection;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The hands on the cartography board: select, move, turn, snap (spec §8.3, §6). Slice S4
    /// and S5 of §11 in one component, because they are one gesture — a sheet is dragged, and
    /// whether it glows and seats on release is a property of that drag rather than of a
    /// separate mode.
    ///
    /// <para><b>The pose lives on the transform, and this is the only class that moves it.</b>
    /// C4.6 is explicit that a seated sheet stores <i>no</i> pose — its pose is
    /// <c>Sheet.CentreGround</c>/<c>RotationDeg</c> and nothing else — and <c>BoardView.Laid</c>
    /// accordingly keeps a seated flag and a lay-order index and no coordinates at all. So the
    /// slab's <c>localPosition</c>/<c>localRotation</c> <i>is</i> the model for an unseated
    /// sheet, and this class reads the current pose back out of the transform every time it
    /// needs it rather than keeping a shadow copy. A shadow copy is the classic way this goes
    /// wrong: <c>Q</c>/<c>E</c> and the corner handle and the pointer all edit "the rotation",
    /// and two of them writing to a cached double while the third writes to the transform
    /// produces a sheet that visibly jumps back a frame after every input.</para>
    ///
    /// <para><b>Dragging does not call <see cref="BoardView.Lay"/> every frame.</b> It calls it
    /// exactly once, when the drag is committed — which is what unseats a seated sheet (C6.7).
    /// After that the transform is written directly, and a release outside tolerance calls
    /// nothing at all (C6.6). <c>Lay</c> re-sorts the whole draw order and raises
    /// <c>Changed</c>, and <c>Changed</c> is what the cabinet rebuilds from; calling it at 60 Hz
    /// would rebuild a 48-row accordion every frame of every drag, for a fact — "this sheet has
    /// moved 3 mm" — that no subscriber wants at that rate. The board's model is not lied to,
    /// because there is nothing in the model to lie to: it holds the flag, and the flag does not
    /// change while the pointer is down.</para>
    ///
    /// <para><b>The ground under the pointer comes from the board PLANE, never from the collider
    /// hit.</b> <c>Physics.Raycast</c> answers C8.8's "which sheet is this" and stops there; the
    /// point it returns is on the slab's own surface, which is lifted above the plane by the
    /// draw-order stack of §3.3 and by however many tiers this class has lifted it. Placing from
    /// that point makes a sheet follow its own surface rather than the pointer — the error is
    /// zero under the current straight-down orthographic camera and would become a visible slide
    /// the day the board is tilted or given any perspective, which is exactly the kind of bug
    /// that gets attributed to the tilt rather than to this line. So the pointer's ray is
    /// intersected with the board root's XZ plane, converted into the root's local space, and
    /// inverted through <see cref="BoardSpace.ToGround"/> — because the truth
    /// <see cref="SheetFit"/> compares against is in ground metres.</para>
    ///
    /// <para><b>The rotation convention is negated, and that is correct.</b> Ground X → board X
    /// and ground Y → board Z, so a ground rotation taking +X toward +Y is a Unity yaw taking +X
    /// toward +Z, and Unity's positive yaw goes the other way:
    /// <c>Quaternion.Euler(0, -(float)rotationDeg, 0)</c>. F-S1.2 verified this by outcome — a
    /// sign error here yields a board that is individually plausible and collectively mirrored —
    /// so <see cref="RotationOf"/> negates a second time on the way back out. Do not "fix"
    /// either half; fixing one of them is how the pair gets broken.</para>
    ///
    /// <para><b>Q/E are held, not pressed, and read a 1D axis</b> — the reasoning
    /// <c>PlayerHands</c> was asked to hand over when the verb moved here (C8.15, C8.17).
    /// Turning a sheet is a continuous adjustment against an 8° tolerance, so a step size would
    /// be one more number to be wrong about; and an axis composite rather than two buttons means
    /// a stick or a shoulder pair binds later with no code change. With nothing selected they do
    /// nothing (C8.12) — they never turn the board, which has no rotation.</para>
    ///
    /// <para><b>Selection is not a lock and neither is seating.</b> Nothing here refuses, warns
    /// or colours a bad placement: a sheet released outside tolerance stays exactly where it was
    /// let go and this class calls nothing (C6.6), and a seated sheet dragged again is unseated
    /// on the spot (C6.7). R6.5 forbids error states, and a sheet that would not move would be
    /// the loudest one available.</para>
    ///
    /// <para><b>A group is the unit of interaction, and it inverts what the pose is</b> (G1.6).
    /// Everything above describes a <i>loose</i> sheet, where the transform is the model. For a
    /// member of an assembly the model is the group's <b>frame</b> and the transform is derived
    /// from it (G1.3, G4.3), so this class never writes a member's transform: it edits the frame
    /// through <see cref="BoardView.MoveGroup"/> and lets the view re-derive every member. That
    /// is not a style choice. <c>BoardStore.Lay</c> and <c>Seat</c> take a sheet <i>out</i> of
    /// its group — a placement carries one derivation or none — and on a two-member group that
    /// dissolves the assembly outright, so a drag routed through <c>Lay</c> would silently
    /// unmake the thing being dragged. Hence the rule, stated once and enforced by the shape of
    /// the code: <b>a member moves by its group's frame; nothing here calls <c>Lay</c> on a
    /// member.</b> The C6.7 unseat on the first frame of a drag is skipped for a member for the
    /// same reason and costs nothing, because grouped and seated are mutually exclusive
    /// (G4.1).</para>
    ///
    /// <para><b>The glow is now a promise about fusing, not about seating</b> (C6.4 kept, G5.1
    /// substituted underneath it). It used to ask <c>SheetFit.Fits</c> against the island's own
    /// pose; with absolute correctness out of scope (G1.9) nothing produces a seat any more, so
    /// that gold would have been an offer the game no longer honours — the exact failure the
    /// knob's release comment argues against below. It now lights when a release <i>would</i>
    /// join something, and <b>whatever makes the release join is what it reports</b>.</para>
    ///
    /// <para><b>G7.1 IS SUPERSEDED HERE: with the assist on, a release joins at the ghost.</b>
    /// The assist used to be feedback only — same tolerances on and off, same releases producing
    /// the same groups. It is not any more, and the change was taken knowingly. When
    /// <c>gameplay.assistedSnap</c> is on and <see cref="SnapHint.TryGhost"/> finds a related
    /// slab, the ghost is drawn at the pose the dragged sheet would occupy on joining, and
    /// <see cref="Release"/> settles it there whatever pose and angle it was let go at. That
    /// widens capture from <c>SheetFit</c>'s <c>reach</c> — 1.54 board units on island 0's Land
    /// Survey, ≈61 px at <c>BoardZoom</c> 2 — to the hint range, 19.03 units and ≈750 px, and
    /// makes <c>RotationToleranceDeg</c> irrelevant while the assist is on. <b>Do not restore
    /// G7.1 by reflex</b>: the playtest that provoked this was shown a signal across a radius
    /// twelve times wider than the one in which letting go did anything, and the fix chosen was
    /// to make the signal true rather than to make it smaller.</para>
    ///
    /// <para><b>With the assist OFF nothing above happens.</b> <see cref="SnapHint.TryGhost"/>
    /// returns false on its first line, there is no ghost, no halo, and <see cref="Release"/>
    /// falls through to <see cref="TryBestFuse"/> exactly as it always did — the strict
    /// <c>SheetFit.Fits</c> path is untouched, and C6.4's seated glow previews it exactly as it
    /// always did, because C6.4 is not part of the assist and every player gets it.</para>
    ///
    /// <para><b>The invariant that must survive every edit to this file:
    /// <see cref="Evaluate"/> and <see cref="Release"/> decide by the same rule, in the same
    /// order.</b> Both ask the ghost first and <see cref="TryBestFuse"/> second; <c>snapping</c>
    /// is the disjunction, and it is what the rim and the halo are drawn from. So there is no
    /// frame on which the board promises a join the release refuses, or performs one it did not
    /// promise — which was the original defect, and is the one thing here that is not
    /// negotiable. If a future edit adds a third way to fuse, it goes into both branches or into
    /// neither.</para>
    ///
    /// <para><b>Q/E and the corner handle pivot a group about a frozen point.</b> G5.4 puts the
    /// pivot at the union's bounding centre in board space, which is stable regardless of which
    /// member was clicked and exists with nothing grabbed — but that box is an AABB over the
    /// members' quads, so it changes shape as the assembly turns. Recomputing it every frame
    /// makes a turning group crawl across the board a fraction of a millimetre at a time, which
    /// reads as a drift bug rather than as a pivot. So the pivot is taken once, on the rising
    /// edge of the turn, and held for the length of the gesture.</para>
    ///
    /// <para><b>An assembly goes to the drawer whole, and comes back whole</b> (G6.4, G6.5).
    /// Released over the cabinet a group is <i>parked</i> — <see cref="BoardView.ParkGroup"/> —
    /// keeping its membership and its frame, and <see cref="PlaceGroup"/> lays it back down
    /// under the pointer with its φ intact. The first version of the release branch refiled the
    /// one member under the pointer, which is a detach with the gesture filed off (G5.5) and on
    /// a two-member group dissolves the thing being filed; the second did nothing at all, which
    /// was honest but left C6.7's "nothing is ever stuck" resting on a gesture that did not
    /// exist. Both are gone. <b>Parking is not saving</b>: <c>BoardView.Hide</c> clears the
    /// group table, so an assembly parked in the drawer does not survive closing the table —
    /// the slice that fixes that is <c>spec.md</c> §9, and it is not this one.</para>
    ///
    /// <para><b>The wheel zooms and the right button pans, and neither is a verb.</b> G10.1
    /// lifted C8.13's "no zoom" on G1.9's argument and left "no pan" standing as an unpaid cost;
    /// both are lifted now and C8.13 is superseded outright. What matters here is the line
    /// between them and everything above: <see cref="View"/> moves the <i>camera</i> and may not
    /// touch the board model. It never selects, never deselects, never unseats, never fuses,
    /// never calls <c>Lay</c>, <c>MoveGroup</c> or <c>Remove</c>. That is not a convention to be
    /// kept by care — it is why the right button was chosen: it was completely unused on this
    /// board, so a pan cannot be mistaken for any gesture that already exists, and a player who
    /// pans across a half-assembled board changes nothing about what will fuse. The tolerances
    /// are the proof: <c>SheetFit</c>'s reach is in ground metres and <c>GlowingHintRange</c> in
    /// board units, and neither has ever read a camera.</para>
    ///
    /// <para><b><see cref="View"/> runs before the left-button verbs, on purpose.</b> A pan
    /// while a sheet is held moves the ground under a stationary cursor, and because
    /// <see cref="Hold"/> re-places the sheet at <c>TryGroundUnder(screen) + grabOffsetGround</c>
    /// <i>after</i> the view has moved, the paper stays under the cursor and travels with the
    /// view. That is the behaviour that was wanted and it is the reason to order the two calls
    /// rather than an accident of them: the alternative — paper pinned to the ground while the
    /// view slides out from under it — makes a pan mid-drag a way to lose the sheet you are
    /// carrying, and it also removes the one thing panning mid-drag is <i>for</i>, which is
    /// carrying a sheet to a neighbour that was off screen when you picked it up.</para>
    ///
    /// <para><b>The cabinet's wheel wins over the board's</b> (C7.5). <c>CabinetPanel</c> owns a
    /// <c>ScrollRect</c> and a 48-row accordion that has to be scrollable, so a notch delivered
    /// over the column must scroll it and must not also zoom the board underneath. The gate is
    /// <see cref="ReleaseOverCabinet"/> — whose name is about releasing and whose <i>content</i>
    /// is "the pointer is over the cabinet", set live from the panel's pointer enter/exit
    /// through <c>TableCanvas</c>. Right-drag is gated on the same flag and for the same
    /// reason.</para>
    ///
    /// <para>Nothing here draws from a random stream and nothing here is persisted, so no island
    /// can move because of anything in this file (§10). Board poses are player facts.</para>
    /// </summary>
    public sealed class BoardInteractor : MonoBehaviour
    {
        /// <summary>C8.8. Must match <c>BoardView</c>'s — the board camera renders only this
        /// layer and only slabs on it carry a collider.</summary>
        const string TableLayerName = "Table";

        /// <summary>C6.8's "~1.02". Applied in X and Z only: the outline shares the slab's flat
        /// quad mesh, which has no Y extent to scale.</summary>
        const float OutlineScale = 1.02f;

        /// <summary>How far under the slab the outline quad sits, as a fraction of
        /// <c>SheetSeparation</c>. Coplanar quads z-fight; a fraction of the separation rather
        /// than an absolute nudge keeps the outline inside its own slab's slot in the draw-order
        /// stack, so it can never surface through the sheet stacked below it.</summary>
        const float OutlineDrop = 0.15f;

        /// <summary>Screen pixels the pointer must travel with the button down before a click
        /// (C8.9, select) becomes a drag (C6.7, unseat). Without it every click on a seated
        /// sheet would unseat it, which is C6.7 fired by the gesture C6.7 does not describe.
        /// </summary>
        const float DragThresholdPixels = 4f;

        /// <summary>Radius of the corner knob in screen pixels, and of the disc that grabs it.
        /// The grab radius is deliberately the larger: a 14 px target is a 28 px object on a
        /// mockup rendered at 1440 wide, and the thing being aimed at is a corner that moves.
        /// </summary>
        const float HandleRadiusPixels = 14f;
        const float HandleGrabRadiusPixels = 22f;

        /// <summary>
        /// One mouse wheel detent, as the Input System reports it on Windows: WHEEL_DELTA, 120.
        /// macOS and Linux report units of about 1 instead, and a trackpad reports a continuous
        /// stream of small values — the Input System does not normalise any of this, and a zoom
        /// that took the raw number would be 120 notches per click on one platform and a
        /// fraction of one on another.
        ///
        /// <para>So the reading is bucketed rather than scaled: anything at or above half a
        /// detent is treated as a Windows-style delta and divided, anything below is taken as a
        /// count of notches directly. It is a heuristic and it is written down as one; the
        /// alternative — a per-platform constant — is a number that is wrong on the platform
        /// nobody is testing on.</para>
        /// </summary>
        const float WheelDelta = 120f;
        const float WheelDeltaThreshold = 60f;

        /// <summary>Ceiling on how much zoom one frame may apply, in notches. A trackpad flick
        /// or a frame that swallowed several events can otherwise cross the whole range between
        /// two draws, which reads as the board jumping rather than as zooming.
        ///
        /// <para><b>1, not the 4 it was.</b> 4 notches is 1.15^4 — 1.75x in a single frame, and
        /// a trackpad that sustains it crosses the whole 1..4 range in three frames. The
        /// ceiling was doing nothing a player could feel, because the thing it was supposed to
        /// catch was the ordinary case on a trackpad rather than the pathological one. At 1 the
        /// worst a frame can do is one notch, and a genuine sweep of a real wheel still zooms
        /// as fast as the wheel can tick.</para></summary>
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

        static readonly Color HandleBody = new Color(0x2A / 255f, 0x1F / 255f, 0x16 / 255f);
        static readonly Color HandleRing = new Color(0xC9 / 255f, 0xA0 / 255f, 0x63 / 255f);

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
        /// <see cref="selected"/> rather than offered instead of it: <c>SelectionChanged</c>
        /// carries a <see cref="SheetId"/> and the header and the cabinet caption a sheet, so
        /// the clicked member stays the selection and the group is what the <i>verbs</i> act
        /// on.
        ///
        /// <para>Re-read on every selection change and on every board change, because a fuse
        /// makes a loose selection grouped without the pointer touching it.</para>
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
        /// <para><b>A landing gesture cannot join.</b> Fusing is suppressed for its whole length
        /// — no ghost, no seated band, no fuse on release — and the paper simply stays where it
        /// was let go. Without it the assist's widened capture (G7.1, ~19 board units) reaches
        /// most of the board, so a sheet dragged out of the drawer is swallowed by any related
        /// group it passes near and the player has no way to put paper down and look at it.
        /// Laying something out and joining it are two decisions, and the player has only made
        /// the first.</para>
        ///
        /// <para><b>Not <see cref="placing"/>, though they are set together for a sheet.</b>
        /// That flag answers a different question — "does cancelling this drag take the sheet
        /// back off the board?" — and deliberately stays false for a retrieved group, because
        /// cancelling a retrieval must not refile the assembly (see <see cref="PlaceGroup"/>).
        /// Two questions, two fields; one field answering both is how the group path would
        /// quietly acquire the wrong cancel behaviour.</para>
        /// </summary>
        bool landing;

        /// <summary>Ground metres from the pointer to the slab's centre, frozen at the moment of
        /// the grab. Without it, grabbing the corner of a 19 × 13 unit Land Survey A1 would
        /// teleport its centre under the cursor — the sheet would jump on touch, which reads as
        /// having grabbed the wrong thing.
        ///
        /// <para>For a group it is the offset to the <b>frame's</b> <c>t</c> rather than to a
        /// slab centre — the same trick applied to the one number that moves an assembly, so a
        /// nine-sheet group grabbed by a corner sheet does not leap to put its frame origin
        /// under the cursor.</para></summary>
        V2 grabOffsetGround;

        /// <summary>True when a release, right now, would join something (C6.4). Recomputed
        /// every gesture frame in <see cref="Evaluate"/> as <c>ghost || TryBestFuse</c> — the
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
        /// asks <see cref="SnapHint.TryGhost"/> again, on the same doctrine that has always
        /// governed <c>snapping</c>: the board can change under a drag — a texture lands, a row
        /// is clicked, a sheet is refiled — and a candidate from an earlier frame can name a
        /// slab that is no longer there. Re-asking costs one walk of the table, once per
        /// gesture, and it is the same function with the same inputs, so preview and outcome
        /// still cannot disagree.</para>
        /// </summary>
        SnapHint.Ghost ghost;

        // ---- the view (G10.1, C8.13 superseded). Deliberately not part of the gesture
        // state above: EndGesture does not clear these and must not, because a pan is not a
        // gesture on the board and does not end when a drag does.

        /// <summary>The right button is down and a pan is running.</summary>
        bool panning;

        /// <summary>The board point that was under the cursor when the pan started, in board
        /// units. Held fixed for the length of the drag and the view is moved each frame by
        /// whatever it takes to put it back under the cursor, rather than by accumulating a
        /// pixels-to-units conversion. Two reasons: the conversion is then computed once, by the
        /// camera, in the code that already does it (<see cref="TryGroundUnder"/>), so a pan is
        /// 1:1 with the board by construction; and it is self-correcting — a frame lost to the
        /// clamp or to a hitch leaves no accumulated error, because the target is a point and
        /// not a sum.</summary>
        Vector2 panAnchorBoard;

        // ---- the corner handle (C8.10)

        /// <summary>Q or E is currently down. Only its rising edge matters — see
        /// <see cref="Turn"/>.</summary>
        bool keyTurning;

        bool turningHandle;
        double handleTurned;      // accumulated, so a full revolution of the pointer works
        double handleLastAngle;
        double handleFromRotation;
        Vector2 handleScreen;

        /// <summary>The frame offset a group turn started from, so each frame's rotation is
        /// applied to the pose at the grab rather than compounded onto the last one. Compounding
        /// a rotation about a pivot sixty times a second accumulates the pivot's own rounding
        /// and walks the assembly off it.</summary>
        V2 handleFromOffset;

        /// <summary>G5.4's pivot: the union's bounding centre, in ground metres, frozen for the
        /// length of one turn gesture — see the class comment for why it is not recomputed.
        /// </summary>
        V2 turnPivotGround;

        GameObject handleRoot;
        RectTransform handleRect;

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
        FuseTarget settleTarget;

        // ---- the outline / glow quad (C6.8)

        GameObject outline;
        MeshFilter outlineMesh;
        MeshRenderer outlineRenderer;
        Material outlineMaterial;

        /// <summary>
        /// The assist's pulse (G7, S7) and the seated glow it leads to. It owns the halos for
        /// BOTH slabs of the pair — the candidate's and the dragged sheet's — so the whole look
        /// lives in one file and the two halves pulse from one clock and one curve (G7.5) by
        /// construction. This class keeps owning only the steady <c>SelectionGold</c> /
        /// <c>SnapGold</c> rim of C6.8 and C6.4, which no longer varies with the pulse: an
        /// earlier shape had <see cref="PlaceOutline"/> tint the rim from the alpha
        /// <see cref="SnapHint.Evaluate"/> hands back, which gave the dragged sheet a rim and the
        /// candidate a halo — two different looks for one relationship.
        ///
        /// <para><b>Deliberately not a second fit test living in here.</b> G7.1 is superseded and
        /// the assist now decides joins, but the half of its discipline that mattered is kept:
        /// <c>SnapHint</c> may not touch a tolerance and may not call <c>SheetFit</c>. It answers
        /// two questions — is this slab related, and is it near — and reports the frame that
        /// falls out of G3.1. <b>Deciding to fuse stays here</b>, in the one class that can keep
        /// <see cref="Evaluate"/> and <see cref="Release"/> the same answer.</para>
        /// </summary>
        SnapHint hint;

        /// <summary>The slabs whose Y this class has overridden for §3.3's tiers 3 and 4, so they
        /// can be put back down when they stop being selected. <c>BoardView.Resort</c> owns tiers
        /// 1 and 2 and deliberately does not implement these two. A list rather than one slab
        /// because G5.6 lifts a group's whole run together.</summary>
        readonly List<BoardSheetView> lifted = new List<BoardSheetView>();

        /// <summary>Scratch for the member walks below. Reused rather than allocated per frame:
        /// the drag path runs at 60 Hz and a group of nine is three lists a frame otherwise.
        /// Never held across a call.</summary>
        readonly List<BoardSheetView> members = new List<BoardSheetView>();
        readonly List<BoardSheetView> dragged = new List<BoardSheetView>();
        readonly List<BoardSheetView> targets = new List<BoardSheetView>();

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
        /// here</b> — the brief offered both and this is the one that does not go stale. The
        /// cabinet is built at runtime: its panel, its sections and its rows are all created in
        /// code, its width is a style constant that can change, and it re-lays itself whenever
        /// the board raises <c>Changed</c>. A rect dragged onto this component would be a second
        /// copy of a fact the cabinet already owns, and it would be wrong silently — a sheet
        /// dropped on the cabinet would land back on the board with no indication why. It is
        /// also the wrong <i>question</i>: "is the pointer over the cabinet" is answered by the
        /// UGUI raycaster that is already running, in the coordinate space it already works in,
        /// and this class has no business re-deriving it from a rect and a screen point. The
        /// cost to the cabinet is two lines in <c>OnPointerEnter</c>/<c>OnPointerExit</c>.</para>
        ///
        /// <para>Until something sets it, a slab dragged over the cabinet simply stays on the
        /// board. That is the C6.6 shape of failure — nothing happens — rather than a sheet
        /// vanishing into a drawer nobody aimed at.</para>
        ///
        /// <para><b>The name is narrower than the fact, and the fact is the useful one.</b> This
        /// is not "a release would refile" — it is set live from <c>CabinetPanel</c>'s pointer
        /// enter and exit, through <c>TableCanvas.OnPointerOverCabinet</c>, and is therefore
        /// true whenever the pointer is over the column with no button involved at all. So
        /// <see cref="View"/> reads it as what it is: the wheel over the cabinet belongs to the
        /// accordion's <c>ScrollRect</c> (C7.5) and a right-drag started there is not aimed at
        /// the board. Renaming it would touch <c>TableCanvas</c> and <c>CabinetPanel</c>, which
        /// this change does not own; recording it here is the cheaper half of the same
        /// honesty.</para>
        /// </summary>
        public bool ReleaseOverCabinet { get; set; }

        float UnitsPerMetre { get { return options != null ? options.BoardUnitsPerMetre : TableOptions.DefaultBoardUnitsPerMetre; } }
        float Separation    { get { return options != null ? options.SheetSeparation    : TableOptions.DefaultSheetSeparation; } }
        float PositionTol   { get { return options != null ? options.PositionTolerance  : TableOptions.DefaultPositionTolerance; } }
        float RotationTol   { get { return options != null ? options.RotationToleranceDeg : TableOptions.DefaultRotationToleranceDeg; } }
        float SettleSeconds { get { return options != null ? options.SettleSeconds      : TableOptions.DefaultSettleSeconds; } }
        float TurnRate      { get { return options != null ? options.SheetTurnDegreesPerSecond : TableOptions.DefaultSheetTurnDegreesPerSecond; } }
        float ZoomStep      { get { return options != null ? options.BoardZoomStep     : TableOptions.DefaultBoardZoomStep; } }
        float WheelSensitivity { get { return options != null ? options.BoardWheelSensitivity : TableOptions.DefaultBoardWheelSensitivity; } }

        /// <summary>
        /// The rig's root, and therefore the plane the pointer is projected onto and the space
        /// every slab's <c>localPosition</c> is expressed in.
        ///
        /// <para><b>Reached through the camera's parent</b> because <c>BoardView</c> does not
        /// expose it, on purpose: the root is at <c>boardOrigin</c>, hundreds of units from the
        /// room, and handing it out invites someone to parent something to it. Both the camera
        /// and every slab are made children of that root by <c>BoardView</c>, so the camera's
        /// parent <i>is</i> the root — and if that ever stops being true, this returns null and
        /// the board goes inert rather than placing sheets in the wrong space.</para>
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

            // TableOptions is an asset, not a scene object, so FindFirstObjectByType cannot see
            // it. It IS loaded whenever the board is wired to it, because BoardView serialises
            // a reference — so the loaded-object search finds the one the board is already
            // using, which is the whole point: two components on one table reading two
            // different tolerances would be a board that glows at one distance and seats at
            // another. Nothing found leaves the Default* constants, which is a working board
            // with the spec's starting feel (§10).
            if (options == null) options = FindLoadedOptions();

            // After options, because SheetSeparation is the one value the hint cannot read off
            // a slab and it needs it to drop its quad under the paper by the same fraction this
            // class uses (C6.8's OutlineDrop). Null options leave it on the Default* constants.
            hint = new SnapHint(options);

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
        /// <para><c>TableSession</c> has a near-identical search matched on Player + Table + UI,
        /// and this is deliberately not a copy of it — it asks for what it uses. Two searches
        /// with the same predicate would be two copies of one rule; two searches with honest,
        /// different predicates are two components each stating their own dependency, and the
        /// day a fourth map appears neither has to be edited.</para>
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
            // Enabled individually, the way PlayerHands and PlayerInteractor enable theirs:
            // idempotent, and independent of the order the table's components wake in.
            // TableSession still owns the map's lifetime (C8.14), which is what makes Q and E
            // go quiet with the rest of the table; enabling the action here only means the
            // board is also driveable from the bench, where no session runs. It is safe in the
            // room because every path in Update is gated on the board actually showing, and
            // because C8.12 makes an unselected Q or E a no-op anyway.
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
            if (handleRoot != null) Discard(handleRoot);
            if (outline != null) Discard(outline);
            if (outlineMaterial != null) Discard(outlineMaterial);
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

                ShowHandle(false);
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
        /// <para>Suppressed while settling: those 0.18 s belong to C6.5's easing, and a key held
        /// through them would fight the interpolation and land the sheet somewhere that is
        /// neither where it was released nor the truth it was seating to.</para>
        ///
        /// <para><b>It unseats once per press, not once per frame.</b> A seated sheet turned by
        /// Q or E is no longer at its true pose, so C6.7 applies — but <c>Lay</c> re-sorts the
        /// board and raises <c>Changed</c>, and a key held for a second would do that sixty
        /// times. The unseat is fired on the 0 → non-zero edge of the axis instead, which is
        /// the frame the sheet actually stops agreeing with the truth.
        ///
        /// <para>A <b>group</b> turns instead about G5.4's pivot, frozen on that same rising
        /// edge, and is never unseated — a member has no pose to stop agreeing with, and the
        /// <c>Lay</c> that would say so is the one call that would dissolve it.</para></para>
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
            if (turn == 0f || view == null) { keyTurning = false; return; }

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

            // No Evaluate here, either branch — Q/E deliberately does not preview, because it
            // has no release to honour (see Release). Lighting a sheet gold while a key is held
            // and then fusing nothing when the key comes up is exactly the promise the knob's
            // comment argues must not be made. G1.5 puts the join on the release of a DRAG.
        }

        /// <summary>
        /// Turns a whole assembly by <paramref name="deltaDeg"/> about a ground pivot — G5.4's
        /// verb, expressed as the one write G4.3 allows.
        ///
        /// <para>Rotating every member about <c>P</c> means
        /// <c>pose'(M) = P + R(δ)·(pose(M) − P)</c>, and substituting G3.1 collapses that to a
        /// new frame: <c>φ' = φ + δ</c> and <c>t' = P + R(δ)·(t − P)</c>. So an assembly of nine
        /// sheets turns by editing two numbers and no member is touched — which is the whole
        /// point of storing a frame rather than N poses, and the reason a half-turned group is
        /// unrepresentable.</para>
        ///
        /// <para>Board space and ground space differ by a scale and a translation and no
        /// rotation (<see cref="BoardSpace"/>), so G5.4's pivot "in board space" and this pivot
        /// in ground metres are the same point. Converting once, at the pivot, is what keeps
        /// that true.</para>
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
        /// <para>The wheel zooms about the pointer and the right button pans, both through the
        /// board point under the cursor, which is why both start with the same question and why
        /// neither computes a pixels-per-unit factor of its own. A second copy of that factor is
        /// how a zoom and a pan end up disagreeing by a fraction of a unit per frame.</para>
        ///
        /// <para>Both are refused over the cabinet: the accordion's <c>ScrollRect</c> owns the
        /// wheel there (C7.5) and a right-drag that started on the chrome is not aimed at the
        /// board. <b>Only the start</b> of a pan is gated, though — dragging out over the column
        /// mid-pan keeps panning, because interrupting a drag at a rectangle's edge is the kind
        /// of stop the player reads as a bug in the board rather than as a boundary.</para>
        ///
        /// <para>Allocates nothing: two structs, one <c>Mathf.Pow</c>, and an early return on
        /// the overwhelmingly common frame where the wheel is still and the button is up.</para>
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

            // Bucket first (the platform's units), scale second (this table's feel), clamp
            // last. WheelSensitivity is the device dial and ZoomStep the range one — see
            // TableOptions.DefaultBoardWheelSensitivity for why they are two numbers.
            float bucketed = Mathf.Abs(raw) >= WheelDeltaThreshold ? raw / WheelDelta : raw;
            float notches = Mathf.Clamp(bucketed * WheelSensitivity,
                                        -MaxNotchesPerFrame, MaxNotchesPerFrame);
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
            if (selected.HasValue && HandleHit(screen))
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

                // C6.7, and the only place it happens: the sheet is being DRAGGED now, not
                // merely clicked, so it is unseated on the spot. Lay with the pose it already
                // has, so the unseat costs no movement — the player has not asked it to move
                // yet, only started to.
                //
                // Skipped for a member, and that is not an omission: grouped and seated are
                // mutually exclusive (G4.1) so there is nothing to unseat, and this exact call
                // is the one that would take the sheet out of its group and dissolve a pair.
                // G5.4 supersedes C6.7 here — dragging a member drags the group.
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
        /// the glow is a promise about <i>releasing</i>: C6.4 lights the edges to say "let go
        /// now and it seats", and a player who turns the last two degrees with the handle,
        /// watches the sheet light up and then finds that releasing the knob does nothing has
        /// been shown a state the game will not honour. Q/E deliberately does not preview and
        /// so has nothing to honour — it has no release, being an axis rather than a grip.
        ///
        /// <para><b>Two ways to fuse, asked in the order <see cref="Evaluate"/> asks them.</b>
        /// The assist first (G7.1 superseded — a showing ghost is a join, wherever the sheet
        /// actually is), the strict <see cref="TryBestFuse"/> second. Both end in
        /// <see cref="Settle"/> with a <see cref="FuseTarget"/>, so G5.1's four outcomes, G5.2's
        /// "the dragged thing moves and the stationary frame wins" and G5.3's easing are one
        /// piece of code with one caller shape and not two parallel ones.</para>
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
                // It leaves the board keeping its membership and its frame, and comes back
                // whole through PlaceGroup. Refiling the one member under the pointer was
                // never a near-enough answer — it is a detach with the gesture filed off
                // (G5.5), and on a pair it would dissolve the assembly outright.
                //
                // This is also what makes G5.5 tolerable. C6.7's principle — "seating is not a
                // lock, and a locked sheet is the harshest error state there is" (R6.5) — is
                // honoured here rather than by a detach gesture: there is nothing the player
                // can assemble that they cannot send back to the drawer whole.
                //
                // Parked is NOT saved: BoardView.Hide clears the group table, so an assembly
                // parked here is lost when the table closes. See BoardView.ParkGroup — the
                // slice that fixes it is spec.md §9.
                if (group != 0)
                {
                    board.ParkGroup(group);
                    Deselect();
                    return;
                }

                board.Remove(id);
                Deselect();
                return;
            }

            // G5.1, evaluated on release and only on release (G1.5). The same two questions
            // Evaluate() has been asking every frame of this gesture, in the same order, so what
            // was previewed is what happens.
            FuseTarget target;

            // 1. The assist, and G7.1 superseded. If a ghost is showing, the release joins THERE
            //    — whatever pose and angle the sheet was let go at, and with no Fits() in the
            //    path. Asked again rather than read off the field Evaluate() filled: the board
            //    can move under a drag, and a candidate from an earlier frame can name a slab
            //    that has since been refiled. With the assist off TryGhost returns false on its
            //    first line and this branch does not exist.
            SnapHint.Ghost g;
            if (!wasLanding
                && hint != null && hint.TryGhost(board, view, out g) && TryFuseAt(g, out target))
            {
                Settle(target);
                return;
            }

            // 2. The strict path, unchanged. This is what every release did before the assist
            //    was given teeth, and it is still the whole of the game for a player who has the
            //    assist off.
            if (!wasLanding && TryBestFuse(out target)) { Settle(target); return; }

            // C6.6, and it is a deliberate absence of code: the sheet stays exactly where it
            // was released, and this class calls NOTHING. No error state, no colour, no
            // message (R6.5).
            //
            // Not even Lay. The sheet was unseated when the gesture began and the transform it
            // is standing on IS its pose (C4.6), so a Lay here would restate a fact the board
            // already holds — at the cost of a re-sort and a Changed, which is a 48-row cabinet
            // rebuild fired by the one outcome the spec says produces no feedback at all.
            //
            // The same is true of a group, by a different route: its frame was written through
            // on every frame of the drag (the frame IS the model, G4.3), so the board already
            // holds where it was let go and there is again nothing to say. A Lay here would be
            // worse than redundant — it would take the member under the pointer out of the
            // assembly the player has just spent the drag moving.
        }

        // -------------------------------------------------------------- the verbs

        /// <summary>
        /// Starts a drag of a sheet the player has pulled out of the cabinet (C7.4). Lays it
        /// under the pointer and hands it to the ordinary drag path, so a sheet arriving from
        /// the drawer and a sheet already on the board are moved by exactly one piece of code.
        ///
        /// <para><b>Laid at rotation 0, never at its true rotation.</b> Handing it out already
        /// turned correctly would seat two thirds of the board's sheets for free — the Land
        /// Survey's whole lattice shares one angle (F-S1.4) — and orientation is explicitly part
        /// of the placement (POC-03 P2.6, C6.3).</para>
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
        /// G6.5: takes a parked assembly out of the Groups drawer and lays it back down under
        /// the pointer, <b>preserving its frame rotation φ</b>. The counterpart of the park in
        /// <see cref="Release"/>, and the other half of C7.5's two directions applied to a
        /// group.
        ///
        /// <para><b>φ survives, and that is deliberate.</b> <see cref="BeginPlace"/> lays a
        /// single sheet at rotation 0 <i>"never at its true rotation"</i>, because resolving
        /// orientation is part of placing a sheet (POC-03 P2.6, C6.3) and handing it out already
        /// turned would seat two thirds of the board's paper for free. A group has already had
        /// its orientation resolved — that <i>is</i> what made it a group, since G3.3 is what
        /// let the members fuse — and with absolute correctness out of scope (G1.9) its φ
        /// carries no remaining puzzle. Resetting it would destroy the player's work to no
        /// end.</para>
        ///
        /// <para><b>Where it lands, and the mistake that is worth naming.</b> The assembly is
        /// restored at the frame it was parked with and then <i>translated</i> so that the
        /// union of its members sits centred on the drop point. The obvious shorter route —
        /// writing the pointer straight into the frame's <c>t</c>, the way
        /// <see cref="BeginPlace"/> writes it straight into a lone sheet's pose — is wrong, and
        /// wrong by kilometres: <c>t</c> is where the frame puts the island's <i>origin</i>, not
        /// where the paper is, so on island 0 that flings a nine-sheet assembly by the members'
        /// own ground coordinates and puts every one of them off a 5940 × 5492 m board. The
        /// union's centre is the point the gesture has actually been promising, because the row
        /// ghost the player has been dragging is a thumbnail centred on the pointer
        /// (<c>CabinetRow</c>, C7.5).</para>
        ///
        /// <para><b>Then <see cref="grabOffsetGround"/> is taken from the frame that resulted,
        /// never assumed to be zero.</b> <c>BeginPlace</c> can zero it because it puts a lone
        /// sheet's centre exactly under the cursor; for a group the offset is measured to the
        /// frame's <c>t</c> and that is metres away from anything visible, so a zero here would
        /// teleport the frame origin under the cursor on the first drag frame — the same fling,
        /// arriving one frame later. The rule is the one this field already states: the offset
        /// is frozen from the pose the thing is actually in.</para>
        ///
        /// <para><b>No <see cref="placing"/> flag.</b> A cancelled place removes the sheet it put
        /// down, and the equivalent for a group would be <c>board.Remove</c> on a member —
        /// which is a detach (G5.5) and on a pair a dissolution. A retrieved group that the
        /// player thinks better of goes back the way it came, by being dragged onto the cabinet
        /// (G6.4). Nothing is stuck, which is C6.7's principle and R6.5's.</para>
        ///
        /// <para>Retrieval is only meaningful within one opening of the table: closing it
        /// destroys the group table, parked groups included. See <c>BoardView.ParkGroup</c>.
        /// </para>
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

            // Already on the table is not this gesture — the Groups row refuses the drag for
            // one (C7.4's two states, G6.1's marking) — but a group can be fused onto while the
            // row event is in flight. Retrieving one that is already down would be a no-op that
            // then dragged it anyway, which is the right outcome, so the call is allowed to
            // answer "already there" and the drag continues from where it stands.
            if (!board.RetrieveGroup(groupId)) return;

            // The union is read from the slabs RetrieveGroup has just derived, not composed a
            // second time from the truths: TryUnion is the one implementation of "how big is
            // this assembly and where is its middle", shared with G5.4's turn pivot and the
            // corner handle, so the three of them cannot disagree about the centre.
            Union union;
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
        /// <para><b>The duration is FIXED, and does not scale with how far the paper travels.
        /// This is a decision, and the number behind it is worse than it first looks — so it is
        /// recorded rather than assumed.</b> Before the assist could fuse, a settle moved at
        /// most <c>reach</c>: 154 ground metres, 1.54 board units, ≈61 px at <c>BoardZoom</c> 2
        /// on island 0's Land Survey, because nothing outside that could fuse at all. An
        /// assisted settle travels <c>|release − ghost|</c>, and the ghost sits a whole lattice
        /// step from the candidate while the release may be anywhere within the hint range of
        /// it — including on the far side. So the bound is <b>range + step</b>, not range.
        /// Measured over every true neighbour pair of island 0 at
        /// <c>OverlapFraction</c> 0: Hydrographic 17.88 board units (703 px), Land Survey and
        /// Garrison <b>41.98 board units — 1652 px, or 9176 px/s over 0.18 s</b>. That is more
        /// than the height of a 1080p screen, and it is not the ≈750 px the hint range alone
        /// suggests.</para>
        ///
        /// <para><b>Kept fixed anyway, for three reasons, and flagged as the first thing to
        /// watch.</b> First — and this is the one that matters — <b>the ghost has already
        /// answered the question a slower move would be answering</b>. A long move reads as a
        /// teleport when the destination is a surprise; the slot has been drawn at the landing
        /// pose for the whole approach, so the player is watching the paper go somewhere they
        /// were already looking. Second, 0.18 s is eleven frames at 60 Hz and the smoothstep
        /// spends most of them at the two ends, so the motion has a visible start and a visible
        /// arrival — it is a short movement, not a jump. Third, the worst case is reached only
        /// by dragging a sheet <i>past</i> the sheet it belongs beside and letting go on the
        /// opposite side; the ordinary case measured above is 7–15 units (285–570 px), which is
        /// 1600–3200 px/s and unremarkable.</para>
        ///
        /// <para><b>What was NOT done, and why.</b> Scaling the duration needs a <b>new tuning
        /// value</b> — in fact two, a speed to divide the distance by and a ceiling to stop a
        /// board-crossing settle taking most of a second — and §10 enumerates
        /// <c>TableOptions</c>' contents "and no others". There is no existing quantity to scale
        /// by either: keeping the strict settle's speed (<c>SettleSeconds × travel / reach</c>)
        /// gives 27× on the worst case, i.e. 4.9 seconds, which is absurd. Two numbers with no
        /// measurement behind them, added on the suspicion that a movement nobody has yet
        /// watched might be too quick, is exactly the speculative tuning this project keeps out
        /// of that asset.</para>
        ///
        /// <para><b>If the playtest says teleport, this is the shape of the fix</b> — written
        /// down so it is not re-derived: <c>duration = clamp(|settleTo − settleFrom| ×
        /// UnitsPerMetre / SettleUnitsPerSecond, SettleSeconds, SettleMaxSeconds)</c>, computed
        /// once here and stored beside <c>settleElapsed</c>, with the easing in
        /// <see cref="Advance"/> untouched. At 1652 px and, say, 2500 px/s it would run 0.66 s.
        /// It is not being added now because both constants would be guesses dressed as
        /// tuning.</para>
        ///
        /// <para><b>The membership is written when the ease finishes, not when it starts.</b>
        /// Committing first would snap the sheet onto the frame — <c>BoardView</c> derives a
        /// member's transform the instant it joins — and there would be nothing left to
        /// animate. This is the shape <c>Seat</c> had, kept: the settle plays, and the last
        /// thing it does is tell the board.</para>
        /// </summary>
        void Settle(FuseTarget target)
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

                // The SHORT way round. A sheet 5° from the pose it is joining at must not spin
                // 355° to get there, and AngleDelta is the same fold G3.3's test uses — one
                // definition of "how far apart are these two angles", so the thing that decides
                // a fit and the thing that plays it out cannot disagree.
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
        /// <para><b>Re-asked, not assumed.</b> A settle is 0.18 s of a board nothing else is
        /// supposed to touch, but a texture can land, a row can be clicked and a sheet can be
        /// refiled inside it, and every one of the four calls below is a refusal rather than a
        /// throw if the world moved. A fuse that quietly does not happen is the C6.6 shape of
        /// failure; a fuse that happens to the wrong assembly is not recoverable.</para>
        ///
        /// <para><b>The frame is set exactly, not left where the easing stopped.</b> The
        /// interpolation is float work over a duration in wall-clock seconds and lands within a
        /// hair of the target; the group's actual frame is then written from the target's, so
        /// the derived poses are the frame's answer and not the animation's. G5.1 says the
        /// stationary thing's frame wins, and this is where "wins" is made literal.</para>
        /// </summary>
        void Commit()
        {
            settling = false;

            FuseTarget target = settleTarget;
            settleTarget = default(FuseTarget);

            bool draggedGroup = target.DraggedGroup != 0;
            bool targetGroup = target.TargetGroup != 0;

            if (draggedGroup)
            {
                // The dragged assembly adopts the stationary thing's frame either way (G5.2).
                board.MoveGroup(target.DraggedGroup, target.Frame);

                if (targetGroup) board.MergeGroups(target.TargetGroup, target.DraggedGroup);
                else board.AddToGroup(target.DraggedGroup, target.Target);
            }
            else if (targetGroup)
            {
                board.AddToGroup(target.TargetGroup, target.Dragged);
            }
            else
            {
                // Two loose sheets. The stationary one goes in first, so it stays under the
                // sheet just laid on it inside G5.6's run.
                board.CreateGroup(target.Target, target.Dragged, target.Frame);
            }

            // The board raised Changed on the way through, which re-read this — but Commit can
            // also be reached from Settle with SettleSeconds at 0, before the field exists to be
            // refreshed. Asking again costs a dictionary lookup and removes the ordering
            // question entirely.
            if (selected.HasValue) selectedGroup = board.GroupIdOf(selected.Value);
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
        /// <para><b>Layered on <see cref="TryGroundUnder"/> rather than duplicating it.</b> That
        /// method is already camera-agnostic in the way this needs — it takes the camera's ray,
        /// intersects the board root's own plane and inverse-transforms into root-local space,
        /// so it answers correctly for any orthographic size and any camera position, which is
        /// exactly what zoom and pan change. A second plane intersect written here would be a
        /// second answer to a question that must have one, and it is the question the whole
        /// drag layer rests on. The round trip through <c>ToGround</c> and back through
        /// <c>ToBoard</c> is a divide and a multiply by the same constant.</para>
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
            IReadOnlyList<BoardSheetView> table = board.OnTable;
            for (int i = 0; i < table.Count; i++)
            {
                BoardSheetView v = table[i];
                if (v != null && v.Id.Equals(id)) return v;
            }
            return null;
        }

        // -------------------------------------------------------------- draw order

        /// <summary>
        /// §3.3's tiers 3 (selected topmost) and 4 (dragged above that), which
        /// <c>BoardView.Resort</c> deliberately does not implement — they are properties of a
        /// pointer, not of the board, and a view that owned them would need to be told about
        /// selection to compute a position. So the drag layer lifts its own slab, in board units
        /// of <c>SheetSeparation</c>, from the top of whatever stack the board has sorted.
        ///
        /// <para>Applied every <c>LateUpdate</c> rather than once, because <c>Resort</c> runs on
        /// every board mutation — including a texture landing behind the player's back (C5.7) —
        /// and would otherwise flatten the lift mid-drag.</para>
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

        /// <summary>
        /// The slabs of one assembly, in join order — G5.6's run, composed from
        /// <c>GroupRecord.Members</c> rather than from the board's lay order, which
        /// <c>BoardStore</c> deliberately does not reshuffle when sheets fuse.
        ///
        /// <para>Members with no slab are skipped rather than treated as an error. The store's
        /// invariant says a member is on the board exactly when its group is, so this is empty
        /// or complete in practice; skipping keeps a run contiguous if it is ever
        /// neither.</para>
        /// </summary>
        void MembersOf(int groupId, List<BoardSheetView> into)
        {
            GroupRecord group;
            if (!board.TryGetGroup(groupId, out group) || group.Members == null) return;

            for (int i = 0; i < group.Members.Count; i++)
            {
                BoardSheetView view = ViewOf(group.Members[i]);
                if (view != null) into.Add(view);
            }
        }

        // ------------------------------------------------------ outline and glow

        /// <summary>
        /// C6.4, and the one place <c>snapping</c> is decided. Re-asked every frame of a gesture,
        /// which is what makes the glow a preview of the release rather than a report on it
        /// (C1.5).
        ///
        /// <para><b>It is written to mirror <see cref="Release"/> line for line</b>: ghost first,
        /// <see cref="TryBestFuse"/> second, and <c>snapping</c> is true if either answered. That
        /// ordering is not cosmetic — the assisted branch is checked first in both, so a frame on
        /// which both could fire resolves to the same target in the preview and in the outcome.
        /// If you change the rule in one of these two methods and not the other, the board starts
        /// lying, which is the defect this whole change exists to remove.</para>
        ///
        /// <para><b>Gated on a live gesture.</b> A ghost is a promise about letting go, and Q/E
        /// has no release to make one about (it is an axis, not a grip); a selected sheet sitting
        /// still is not about to land anywhere either. <see cref="Gesturing"/> is exactly the
        /// condition <see cref="Release"/> requires before it fuses at all, so the two agree by
        /// construction rather than by two matching tests.</para>
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

            FuseTarget ignored;
            snapping = TryBestFuse(out ignored);
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

        // ------------------------------------------------------------ fusing

        /// <summary>
        /// One outcome of G5.1's search: what is being dragged, what it would join, and the
        /// frame the join happens under.
        ///
        /// <para><see cref="DraggedGroup"/> and <see cref="TargetGroup"/> are 0 for a loose
        /// sheet, which is the same sentinel <c>Placement.GroupId</c> uses; the four
        /// combinations of those two zeros are exactly G5.1's four rows, so the commit is a
        /// two-way branch and not a table of special cases.</para>
        ///
        /// <para><see cref="Frame"/> is always the <b>stationary</b> thing's (G5.2): the table
        /// does not move when paper is put on it. <see cref="Error"/> is the position error of
        /// the member the fit was judged on (G3.6), which is the quantity G5.1 ranks candidates
        /// by — measured through <c>SheetFit.PositionError</c> rather than re-derived, so the
        /// ranking uses the same distance the test used.</para>
        /// </summary>
        readonly struct FuseTarget
        {
            public readonly int DraggedGroup;
            public readonly SheetId Dragged;      // meaningless when DraggedGroup != 0
            public readonly int TargetGroup;
            public readonly SheetId Target;       // meaningless when TargetGroup != 0
            public readonly BoardFrame Frame;
            public readonly double Error;

            public FuseTarget(int draggedGroup, SheetId dragged, int targetGroup, SheetId target,
                              BoardFrame frame, double error)
            {
                DraggedGroup = draggedGroup;
                Dragged = dragged;
                TargetGroup = targetGroup;
                Target = target;
                Frame = frame;
                Error = error;
            }
        }

        /// <summary>
        /// G5.1's candidate search: every fusable loose sheet and group on the table, each
        /// presenting a frame (G3.1), tested with <c>SheetFit.Fits</c>, and the smallest
        /// position error wins.
        ///
        /// <para><b>One definition of "fits", not two.</b> A dragged group is judged on ONE
        /// member (G3.6) — the one nearest, in board units, to the target's nearest fusable slab
        /// — and then goes through the same <c>SheetFit.Fits</c> a lone sheet does. That is not
        /// an approximation of a per-member test: <c>PositionReach</c> scales with the
        /// <i>sheet</i> (C6.1), so grounding the test at the far end of a nine-sheet assembly
        /// would apply a tolerance to a member nowhere near the seam. For a group of one this is
        /// literally the sheet case, which is why the loose path below is the same code with a
        /// one-entry list.</para>
        ///
        /// <para><b>Nearness is measured in board units, between slab centres.</b> It picks
        /// <i>which</i> member is judged and never decides whether anything fits, so it is a
        /// pure UI question — the same split §7 draws for the assist — and wants no island
        /// access and no truth pose.</para>
        ///
        /// <para><b>Poses come from <see cref="BoardView.TryPoseOf"/>, all of them.</b> The
        /// dragged sheet's live pose is its transform, a dragged group's is derived from the
        /// frame this class has been writing, and a stationary member's is derived from its own
        /// group's — three different answers to "where is this", and one method that knows
        /// which is which. Reading a placement's stored coordinates instead would judge a
        /// dragged sheet against where it was before the drag started.</para>
        ///
        /// <para>Runs every drag frame. No allocation: both member walks fill reused lists, the
        /// group table is cached until the board changes, and the arithmetic is
        /// <c>SheetFit</c>'s subtraction, square root and modulus.</para>
        /// </summary>
        bool TryBestFuse(out FuseTarget best)
        {
            best = default(FuseTarget);
            if (board == null || !selected.HasValue) return false;

            dragged.Clear();
            if (selectedGroup != 0) MembersOf(selectedGroup, dragged);
            else
            {
                BoardSheetView one = ViewOf(selected.Value);
                if (one == null) return false;
                dragged.Add(one);
            }
            if (dragged.Count == 0) return false;

            bool any = false;
            double bestError = double.MaxValue;

            // 1. loose sheets. A slab that belongs to a group is skipped here and reached
            //    through its group below, so no candidate is offered twice and no member is
            //    ever joined to as if it were loose — its frame is the assembly's, not its own.
            IReadOnlyList<BoardSheetView> table = board.OnTable;
            for (int i = 0; i < table.Count; i++)
            {
                BoardSheetView slab = table[i];
                if (slab == null || dragged.Contains(slab)) continue;
                if (board.GroupIdOf(slab.Id) != 0) continue;

                V2 pose;
                double rotation;
                if (!board.TryPoseOf(slab.Id, out pose, out rotation)) continue;

                targets.Clear();
                targets.Add(slab);

                FuseTarget candidate;
                if (!TryCandidate(BoardFrame.ForSheet(slab.Sheet, pose, rotation),
                                  0, slab.Id, out candidate)) continue;

                if (candidate.Error >= bestError) continue;
                bestError = candidate.Error;
                best = candidate;
                any = true;
            }

            // 2. groups, which present their stored frame directly (G3.1's second bullet).
            IReadOnlyList<GroupRecord> all = Groups();
            for (int g = 0; g < all.Count; g++)
            {
                GroupRecord group = all[g];
                if (!group.OnTable || group.GroupId == selectedGroup) continue;

                targets.Clear();
                MembersOf(group.GroupId, targets);
                if (targets.Count == 0) continue;

                FuseTarget candidate;
                if (!TryCandidate(board.FrameOf(group.GroupId), group.GroupId,
                                  default(SheetId), out candidate)) continue;

                if (candidate.Error >= bestError) continue;
                bestError = candidate.Error;
                best = candidate;
                any = true;
            }

            return any;
        }

        /// <summary>G3.6 for one candidate: pick the meeting member, then G3.2 and G3.3 verbatim
        /// on it. <c>dragged</c> and <c>targets</c> are the two slab lists, already
        /// filled.</summary>
        bool TryCandidate(BoardFrame frame, int targetGroup, SheetId target,
                          out FuseTarget candidate)
        {
            candidate = default(FuseTarget);

            BoardSheetView meeting = null;
            double nearest = double.MaxValue;

            for (int m = 0; m < dragged.Count; m++)
            {
                BoardSheetView mine = dragged[m];
                Vector3 a = mine.transform.localPosition;

                for (int t = 0; t < targets.Count; t++)
                {
                    BoardSheetView theirs = targets[t];

                    // G3.4, and the first gate because it is two comparisons on fields already
                    // in hand. It also does the whole-island refusal (R2.2a: a survey of one
                    // has no peer) without this method having to know about it.
                    if (!SheetKinship.Fusable(mine.Sheet, theirs.Sheet)) continue;

                    Vector3 b = theirs.transform.localPosition;
                    float dx = a.x - b.x, dz = a.z - b.z;
                    double d = dx * dx + dz * dz;      // squared: only the ordering is used

                    if (d >= nearest) continue;
                    nearest = d;
                    meeting = mine;
                }
            }

            if (meeting == null) return false;

            V2 pose;
            double rotation;
            if (!board.TryPoseOf(meeting.Id, out pose, out rotation)) return false;

            if (!SheetFit.Fits(meeting.Sheet, frame, pose, rotation, PositionTol, RotationTol))
                return false;

            candidate = new FuseTarget(selectedGroup, selected.Value, targetGroup, target, frame,
                                       SheetFit.PositionError(meeting.Sheet, frame, pose));
            return true;
        }

        /// <summary>
        /// G5.1's outcome for an <b>assisted</b> release: the same <see cref="FuseTarget"/> the
        /// strict path builds, aimed at the ghost's candidate instead of at whatever passed
        /// <c>SheetFit.Fits</c>.
        ///
        /// <para><b>There is no fit test here, and that is the entire change.</b> The frame is
        /// the stationary thing's (G5.2), the four outcomes of G5.1 are unchanged — the two
        /// group flags are the ghost's and this class's, so <see cref="Commit"/>'s two-way
        /// branch resolves loose+loose, loose+group, group+loose and group+group exactly as
        /// before — and <see cref="Settle"/> plays the same smoothstep over the same
        /// <c>SettleSeconds</c>, turning the short way through <c>AngleDelta</c> (G5.3). An
        /// assisted release picks the same <i>kind</i> of join; it just stops requiring that you
        /// aimed.</para>
        ///
        /// <para><b>The meeting member is the selected slab, not a search</b> (G3.6). It is the
        /// slab the ghost was computed for, so grounding the join anywhere else would settle the
        /// assembly onto a pose the player was never shown. For a loose sheet this is that
        /// sheet; for a group it is the clicked member, which is the one the ghost is drawn
        /// under.</para>
        ///
        /// <para><b><see cref="SheetKinship.Fusable"/> is re-asked, and it is not redundant
        /// bookkeeping.</b> <c>SnapHint.Nearest</c> already gates on
        /// <see cref="SheetKinship.Neighbours"/>, which calls <c>Fusable</c> first — so G3.4's
        /// same-survey rule and G-A5's whole-island refusal already hold and no widening in the
        /// assist can reach past them. This asks again for <see cref="Commit"/>'s reason: a fuse
        /// that quietly does not happen is the C6.6 shape of failure, and a fuse that happens to
        /// the wrong assembly is not recoverable (G5.5 — nothing ever leaves a group). Two
        /// struct comparisons is a cheap price for the one outcome that cannot be undone.</para>
        ///
        /// <para><see cref="FuseTarget.Error"/> is filled truthfully through
        /// <c>SheetFit.PositionError</c> even though nothing ranks it here — there is exactly
        /// one candidate. It is measured rather than left at zero because a zero would read, to
        /// anyone printing a target, as "landed perfectly", and under the assist it routinely
        /// will not have been.</para>
        /// </summary>
        bool TryFuseAt(SnapHint.Ghost candidate, out FuseTarget target)
        {
            target = default(FuseTarget);
            if (!candidate.Any || board == null || !selected.HasValue) return false;

            BoardSheetView meeting = ViewOf(selected.Value);
            if (meeting == null) return false;

            Sheet theirs;
            if (!board.TrySheet(candidate.Target, out theirs)) return false;
            if (!SheetKinship.Fusable(meeting.Sheet, theirs)) return false;

            V2 pose;
            double rotation;
            if (!board.TryPoseOf(meeting.Id, out pose, out rotation)) return false;

            target = new FuseTarget(selectedGroup, selected.Value,
                                    candidate.TargetGroup, candidate.Target, candidate.Frame,
                                    SheetFit.PositionError(meeting.Sheet, candidate.Frame, pose));
            return true;
        }

        /// <summary>
        /// C6.8, taken literally: one child quad at <see cref="OutlineScale"/> with an unlit gold
        /// material, enabled and disabled. No shader, no outline pass, no second camera.
        ///
        /// <para><b>One object, reparented, rather than one per slab.</b> Exactly one sheet is
        /// selected at a time, so N-1 of them would always be switched off; and a quad living
        /// under a slab dies with it when the sheet is refiled, which is a destroyed
        /// <c>MeshRenderer</c> to notice at the moment the player is looking somewhere else. It
        /// hangs off the board root and copies the slab's local pose instead.</para>
        ///
        /// <para><b>It shares the slab's mesh</b> — every slab is a different size (F-S1.4), so
        /// a mesh of its own would have to be rebuilt on every selection change. Shared, never
        /// owned: <c>BoardSheetView</c> destroys that mesh in its <c>OnDestroy</c>, so the
        /// reference is dropped the moment the outline is detached.</para>
        ///
        /// <para><b>It sits <i>under</i> the slab</b>, by a fraction of the separation. Coplanar
        /// quads z-fight; above the slab the 1.02 rim would be right but the middle would strobe
        /// over the map. Under it, the slab covers the middle and only the rim shows, which is
        /// what an outline is.</para>
        /// </summary>
        void PlaceOutline()
        {
            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;
            if (view == null) { DetachOutline(); return; }

            Transform root = BoardRoot;
            if (root == null) { DetachOutline(); return; }

            // Rebuilt rather than kept: BoardView.Hide destroys the whole rig, taking any child
            // of the root with it, so this cannot assume it survived the last close.
            if (outline == null || outline.transform.parent != root) BuildOutline(root);

            Mesh mesh = null;
            MeshFilter filter = view.GetComponent<MeshFilter>();
            if (filter != null) mesh = filter.sharedMesh;

            outlineMesh.sharedMesh = mesh;
            outlineRenderer.enabled = mesh != null;

            // Two states, and G7.2 used to have three. `snapping` is the whole condition: it is
            // true exactly when a release would join something, whether that is the assist's
            // ghost or SheetFit's strict tolerance, so the rim and the halo cannot be showing a
            // promise the release will not keep. THAT IS THE INVARIANT — everything drawn below
            // is a function of the same value Release() acts on.
            //
            // G7.2's middle rung is gone with G7.1. It was "related and near but a release does
            // nothing", drawn as a pale pulsing halo, and with the assist on there is no longer
            // such a state: near enough to hint IS near enough to join. So the assisted case
            // draws the SEATED band — one look for one meaning — plus the ghost, which is the
            // part that says WHERE. See SnapHint's class comment for the measurement that killed
            // the middle rung.
            //
            // THE RIM IS NOT WHAT MOVES. It was, and a ~5 px hairline turned out to be too small
            // to carry either a pulse or the difference between two states. The halo and the
            // ghost carry all of it, inside SnapHint; this rim stays on its two steady colours.
            //
            // Time.unscaledTime rather than Time.time: one shared monotonic clock means the
            // ghost breathes on G7.5's curve without anything arranging its phase, and a paused
            // or slowed game cannot freeze it half way through.
            if (snapping)
            {
                outlineMaterial.color = SnapGold;

                if (hint != null)
                {
                    if (ghost.Any) hint.Show(board, view, ghost, Time.unscaledTime);
                    else hint.ShowSeated(board, view);
                }
            }
            else
            {
                outlineMaterial.color = SelectionGold;
                if (hint != null) hint.Clear();
            }

            Union union;
            if (selectedGroup != 0 && TryUnion(selectedGroup, out union))
            {
                // G5.4: round the UNION of the members' quads, not round the clicked one. The
                // borrowed mesh is one member's quad, so it is scaled to cover the box — the
                // 1.02 rim of C6.8 applied to the assembly instead of to a sheet.
                float mw = (float)(view.Sheet.Survey.SheetGroundWidth * UnitsPerMetre);
                float mh = (float)(view.Sheet.Survey.SheetGroundHeight * UnitsPerMetre);
                if (mw <= 0f || mh <= 0f) { DetachOutline(); return; }

                outline.transform.localPosition =
                    new Vector3(union.CentreX, union.LowestY - Separation * OutlineDrop,
                                union.CentreZ);

                // Axis-aligned, and deliberately not turned with the assembly. A member's own
                // outline follows its slab because a sheet has one rotation; an assembly does
                // not — the Hydrographic coast walk gives every strip its own angle (D-H2), so
                // there is no shared angle for a tight box to be drawn at. The board-space box
                // is the same thing G5.4 measures the pivot and the corner handle from, which
                // is what keeps the three of them agreeing.
                outline.transform.localRotation = Quaternion.identity;
                outline.transform.localScale =
                    new Vector3(union.Width * OutlineScale / mw, 1f,
                                union.Height * OutlineScale / mh);
                return;
            }

            Transform t = view.transform;
            Vector3 p = t.localPosition;

            outline.transform.localPosition = new Vector3(p.x, p.y - Separation * OutlineDrop, p.z);
            outline.transform.localRotation = t.localRotation;
            outline.transform.localScale = new Vector3(OutlineScale, 1f, OutlineScale);
        }

        /// <summary>The board-space bounding box of an assembly's quads (G5.4), plus the lowest
        /// tier in the run so the outline can sit under all of it.</summary>
        readonly struct Union
        {
            public readonly float MinX, MinZ, MaxX, MaxZ, LowestY;

            public Union(float minX, float minZ, float maxX, float maxZ, float lowestY)
            {
                MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ; LowestY = lowestY;
            }

            public float Width { get { return MaxX - MinX; } }
            public float Height { get { return MaxZ - MinZ; } }
            public float CentreX { get { return (MinX + MaxX) * 0.5f; } }
            public float CentreZ { get { return (MinZ + MaxZ) * 0.5f; } }
        }

        /// <summary>
        /// The union of an assembly's quads, in the board root's local space: every member's
        /// four corners, turned by its own rotation, reduced to a box.
        ///
        /// <para><b>Corners, not <c>Renderer.bounds</c>.</b> A renderer's bounds are world-space
        /// and axis-aligned in <i>world</i>, and the rig hangs 500 units under the room on a
        /// root that is allowed to move (see <see cref="TryGroundUnder"/>); reading them would
        /// make the outline depend on where the board happens to be built. The four corners of a
        /// quad whose size is <c>SheetGroundWidth × UnitsPerMetre</c> are exact and are the same
        /// numbers <see cref="PlaceHandle"/> already works from.</para>
        ///
        /// <para>Taken from the transforms rather than from the frame, deliberately: they are
        /// derived from it and are therefore already the assembly's current pose, and going
        /// back to the truth here would mean a second application of G3.1 for a box that only
        /// has to agree with what is on screen.</para>
        /// </summary>
        bool TryUnion(int groupId, out Union union)
        {
            union = default(Union);

            members.Clear();
            MembersOf(groupId, members);
            if (members.Count == 0) return false;

            float minX = float.MaxValue, minZ = float.MaxValue, lowestY = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;

            for (int i = 0; i < members.Count; i++)
            {
                BoardSheetView slab = members[i];
                Transform t = slab.transform;

                float hw = (float)(slab.Sheet.Survey.SheetGroundWidth * UnitsPerMetre * 0.5);
                float hh = (float)(slab.Sheet.Survey.SheetGroundHeight * UnitsPerMetre * 0.5);

                Vector3 centre = t.localPosition;
                if (centre.y < lowestY) lowestY = centre.y;

                for (int c = 0; c < 4; c++)
                {
                    float sx = (c == 0 || c == 3) ? -hw : hw;
                    float sz = (c == 0 || c == 1) ? -hh : hh;

                    Vector3 corner = centre + t.localRotation * new Vector3(sx, 0f, sz);

                    if (corner.x < minX) minX = corner.x;
                    if (corner.x > maxX) maxX = corner.x;
                    if (corner.z < minZ) minZ = corner.z;
                    if (corner.z > maxZ) maxZ = corner.z;
                }
            }

            union = new Union(minX, minZ, maxX, maxZ, lowestY);
            return true;
        }

        /// <summary>G5.4's pivot in ground metres. Falls back to the frame's own offset when the
        /// group has no slabs to measure — a pivot that is merely arbitrary, rather than a turn
        /// that does not happen.</summary>
        V2 UnionCentreGround(int groupId)
        {
            Union union;
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

        void BuildOutline(Transform root)
        {
            if (outline != null) Discard(outline);

            outline = new GameObject("SelectionOutline");
            outline.transform.SetParent(root, false);

            // The board camera's culling mask is the Table layer and nothing else (C5.1), so an
            // outline on the default layer is built, positioned, enabled — and invisible.
            int layer = LayerMask.NameToLayer(TableLayerName);
            if (layer >= 0) outline.layer = layer;

            outlineMesh = outline.AddComponent<MeshFilter>();
            outlineRenderer = outline.AddComponent<MeshRenderer>();
            outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;

            if (outlineMaterial == null)
            {
                // Unlit, for §3.4's reason: the board is independent of the room's lighting and
                // of where its root sits, and a lit gold would go black 500 units under the
                // floor.
                outlineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                outlineMaterial.name = "M_BoardOutline";
                outlineMaterial.hideFlags = HideFlags.DontSave;

                // URP's Unlit is OPAQUE by default and discards color.a outright. Without this
                // the pulse of G7.5 computes a correct alpha every frame and renders as a slab
                // that is simply gold — a bug with no symptom, which is the worst kind to ship.
                // Alpha 1 through a blended material is pixel-identical to the opaque one, so
                // both steady states (C6.8's SelectionGold, C6.4's SnapGold) are unchanged.
                SnapHint.MakeBlended(outlineMaterial);
            }

            outlineRenderer.sharedMaterial = outlineMaterial;
        }

        void DetachOutline()
        {
            if (outlineRenderer != null) outlineRenderer.enabled = false;

            // The hint's quad goes dark on the same terms as this one. Both of PlaceOutline's
            // early returns land here — no selection, or no board root — and a candidate left
            // glowing for a sheet that is no longer selected is a pointer at nothing.
            if (hint != null) hint.Clear();

            // Dropped, not kept: the mesh belongs to a slab that may be destroyed this frame.
            if (outlineMesh != null) outlineMesh.sharedMesh = null;
        }

        // ------------------------------------------------------- the corner handle

        /// <summary>
        /// C8.10's first input: the knob from mockup <c>1c</c>, at the selected sheet's corner.
        ///
        /// <para><b>A fixed corner in the sheet's own space</b> — its +X/+Z one — rather than
        /// whichever corner is currently top-right on screen. It then travels round with the
        /// sheet as it turns, which is what makes it read as attached to the paper; a knob that
        /// jumped between corners as the sheet passed 45° would read as a second control.</para>
        ///
        /// <para><b>Its own overlay canvas, above <c>TableCanvas</c>.</b> The knob is chrome,
        /// so it cannot be a board slab (the board camera renders ground, and a knob that scaled
        /// with the island would be unusable on a small sheet); and it cannot go inside
        /// <c>TableCanvas</c> without this class reaching into the cabinet's object graph. A
        /// canvas of its own with a higher sorting order is the seam. No <c>CanvasScaler</c>:
        /// positions come straight from <c>WorldToScreenPoint</c> in raw pixels, and a scaler
        /// would silently apply a second transform to them.</para>
        ///
        /// <para><b>It does not raycast.</b> <see cref="HandleHit"/> tests it in screen pixels
        /// instead. A UGUI target here would have to be an <c>EventSystem</c> participant, which
        /// means drag events arriving on a different clock from the mouse polling the rest of
        /// this class does — two input paths for one gesture.</para>
        /// </summary>
        void PlaceHandle()
        {
            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;
            if (view == null) { ShowHandle(false); return; }

            Vector3 corner;

            Union union;
            if (selectedGroup != 0 && TryUnion(selectedGroup, out union))
            {
                // G5.4: at the UNION's corner. The +X/+Z one, which is the same corner a lone
                // sheet's knob sits at — it does not travel round with the paper here, because
                // the box it is a corner of is axis-aligned (see PlaceOutline), so the knob
                // instead stays where the player last let go of it.
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
            if (point.z < 0f) { ShowHandle(false); return; }

            EnsureHandle();
            handleScreen = new Vector2(point.x, point.y);
            handleRect.anchoredPosition = handleScreen;
            ShowHandle(true);
        }

        bool HandleHit(Vector2 screen)
        {
            if (handleRoot == null || !handleRoot.activeSelf) return false;
            return (screen - handleScreen).sqrMagnitude
                   <= HandleGrabRadiusPixels * HandleGrabRadiusPixels;
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

        void EnsureHandle()
        {
            if (handleRoot != null) return;

            handleRoot = new GameObject("BoardCornerHandle", typeof(Canvas));

            var canvas = handleRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the cabinet and the header: the knob belongs to a sheet on the board, and a
            // sheet dragged under the cabinet's edge must not have its handle clipped away by
            // chrome the player is not aiming at.
            canvas.sortingOrder = TableCanvas.SortingOrder + 10;

            // Unity's built-in round sprite, so no texture is generated and no asset is added.
            // A null (older editor, stripped resources) leaves a square knob, which is ugly and
            // entirely usable — the fallback keeps the verb, not the look.
            Sprite disc = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");

            // Anchored to the canvas's bottom-left corner, so anchoredPosition IS the screen
            // point WorldToScreenPoint returns — no scaler, no offset, no conversion.
            handleRect = Knob(handleRoot.transform, "Body", disc, HandleBody,
                              HandleRadiusPixels * 2f, Vector2.zero);

            // A dark disc, a gold ring, a dark centre — the mockup's rotate mark, drawn as three
            // circles rather than as a glyph. A glyph would need a font that has it, and the OS
            // faces CabinetStyle borrows are not guaranteed to. Anchored to the BODY'S CENTRE,
            // not to a corner: these are concentric, and a (0,0) anchor here would hang them off
            // the knob's bottom-left.
            Vector2 centre = new Vector2(0.5f, 0.5f);
            Knob(handleRect, "Ring", disc, HandleRing, HandleRadiusPixels * 1.05f, centre);
            Knob(handleRect, "Core", disc, HandleBody, HandleRadiusPixels * 0.62f, centre);

            ShowHandle(false);
        }

        static RectTransform Knob(Transform parent, string name, Sprite sprite, Color colour,
                                  float diameter, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false;      // see PlaceHandle

            var rt = image.rectTransform;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(diameter, diameter);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        void ShowHandle(bool visible)
        {
            if (handleRoot != null && handleRoot.activeSelf != visible)
                handleRoot.SetActive(visible);
        }

        // --------------------------------------------------------------- selection

        /// <summary>
        /// Selects from outside — the cabinet's row click (C7.6).
        ///
        /// <para><b>A sheet still in the drawer clears the board selection rather than becoming
        /// it.</b> There is no slab to outline and nothing for <c>Q</c>/<c>E</c> to turn, so the
        /// alternative is a header naming one sheet while the rotate keys move a different one
        /// that is still quietly outlined — which looks like the keys are broken. Clearing says
        /// the truth: you are looking at that sheet, and nothing on the board is under your
        /// hands.</para>
        ///
        /// <para>Ignored entirely while a gesture is running. A click cannot arrive mid-drag
        /// from the pointer, but a rebuild of the accordion can fire a row event, and moving the
        /// selection out from under a drag would leave a slab following a pointer that no longer
        /// owns it.</para>
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

        static void Discard(UnityEngine.Object thing)
        {
            if (thing == null) return;

            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }
    }
}
