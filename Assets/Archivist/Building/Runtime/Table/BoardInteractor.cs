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

        /// <summary>Button is down on a slab, but the pointer has not yet moved
        /// <see cref="DragThresholdPixels"/> — so this is still a click.</summary>
        bool armed;
        Vector2 pressScreen;

        bool dragging;

        /// <summary>This drag began with <see cref="BeginPlace"/> on a sheet that was not on the
        /// board, so <see cref="CancelPlace"/> has something to undo.</summary>
        bool placing;

        /// <summary>Ground metres from the pointer to the slab's centre, frozen at the moment of
        /// the grab. Without it, grabbing the corner of a 19 × 13 unit Land Survey A1 would
        /// teleport its centre under the cursor — the sheet would jump on touch, which reads as
        /// having grabbed the wrong thing.</summary>
        V2 grabOffsetGround;

        /// <summary>Recomputed every drag frame from <see cref="SheetFit.Fits"/> (C6.4). Not
        /// stored anywhere else and not consulted on release — release re-evaluates, because a
        /// glow from three frames ago is not an answer about where the sheet was let go.</summary>
        bool snapping;

        // ---- the corner handle (C8.10)

        /// <summary>Q or E is currently down. Only its rising edge matters — see
        /// <see cref="Turn"/>.</summary>
        bool keyTurning;

        bool turningHandle;
        double handleTurned;      // accumulated, so a full revolution of the pointer works
        double handleLastAngle;
        double handleFromRotation;
        Vector2 handleScreen;

        GameObject handleRoot;
        RectTransform handleRect;

        // ---- the settle (C6.5)

        bool settling;
        SheetId settleId;
        Sheet settleTruth;
        V2 settleFrom;
        double settleFromRotation;
        double settleTurn;
        float settleElapsed;

        // ---- the outline / glow quad (C6.8)

        GameObject outline;
        MeshFilter outlineMesh;
        MeshRenderer outlineRenderer;
        Material outlineMaterial;

        /// <summary>The slab whose Y this class has overridden for §3.3's tiers 3 and 4, so it
        /// can be put back down when it stops being selected. <c>BoardView.Resort</c> owns tiers
        /// 1 and 2 and deliberately does not implement these two.</summary>
        BoardSheetView lifted;

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
        /// </summary>
        public bool ReleaseOverCabinet { get; set; }

        float UnitsPerMetre { get { return options != null ? options.BoardUnitsPerMetre : TableOptions.DefaultBoardUnitsPerMetre; } }
        float Separation    { get { return options != null ? options.SheetSeparation    : TableOptions.DefaultSheetSeparation; } }
        float PositionTol   { get { return options != null ? options.PositionTolerance  : TableOptions.DefaultPositionTolerance; } }
        float RotationTol   { get { return options != null ? options.RotationToleranceDeg : TableOptions.DefaultRotationToleranceDeg; } }
        float SettleSeconds { get { return options != null ? options.SettleSeconds      : TableOptions.DefaultSettleSeconds; } }
        float TurnRate      { get { return options != null ? options.SheetTurnDegreesPerSecond : TableOptions.DefaultSheetTurnDegreesPerSecond; } }

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
            TableOptions[] all = Resources.FindObjectsOfTypeAll<TableOptions>();
            return all != null && all.Length > 0 ? all[0] : null;
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
        }

        /// <summary>A sheet can leave the board without the pointer being involved — the cabinet
        /// refiles it, or the board closes. Selection must not outlive the slab it names, or the
        /// header goes on captioning a sheet that is back in the drawer.</summary>
        void OnBoardChanged()
        {
            if (!selected.HasValue) return;
            if (board != null && board.IsShowing && board.IsOnTable(selected.Value)) return;

            EndGesture();
            Deselect();
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
        /// the frame the sheet actually stops agreeing with the truth.</para>
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

            if (!keyTurning)
            {
                keyTurning = true;
                board.Lay(view.Id, GroundOf(view), RotationOf(view));   // C6.7
            }

            // E (positive) increases the ground rotation, which is counter-clockwise on screen:
            // board +X is screen right and board +Z is screen up, and a ground rotation takes
            // +X toward +Y, which is +Z. Q is the same adjustment the other way.
            SetPose(view, GroundOf(view), RotationOf(view) + turn * TurnRate * Time.deltaTime);
        }

        void Pointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;      // no pointer device: Q/E above still work

            // The new Input System, not Input.mousePosition: the legacy API throws outright
            // when Active Input Handling is set to "Input System Package (New)", which is what
            // this project uses.
            Vector2 screen = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame) Press(screen);
            else if (mouse.leftButton.wasReleasedThisFrame) Release();
            else if (mouse.leftButton.isPressed) Hold(screen);
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
            grabOffsetGround = GroundOf(view) - ground;
        }

        void Hold(Vector2 screen)
        {
            if (turningHandle)
            {
                BoardSheetView turned = selected.HasValue ? ViewOf(selected.Value) : null;
                if (turned == null) { turningHandle = false; return; }

                // Accumulated through AngleDelta rather than taken as (now - grab): the fold
                // into (-180, 180] is what makes dragging the knob a full revolution keep
                // turning instead of snapping back when it crosses the far side.
                double angle = ScreenAngle(turned, screen);
                handleTurned += SheetFit.AngleDelta(angle, handleLastAngle);
                handleLastAngle = angle;

                SetPose(turned, GroundOf(turned), handleFromRotation + handleTurned);
                Evaluate(turned);
                return;
            }

            if (!selected.HasValue) return;

            BoardSheetView view = ViewOf(selected.Value);
            if (view == null) { EndGesture(); return; }

            if (armed && !dragging)
            {
                if ((screen - pressScreen).magnitude < DragThresholdPixels) return;

                // C6.7, and the only place it happens: the sheet is being DRAGGED now, not
                // merely clicked, so it is unseated on the spot. Lay with the pose it already
                // has, so the unseat costs no movement — the player has not asked it to move
                // yet, only started to.
                dragging = true;
                board.Lay(view.Id, GroundOf(view), RotationOf(view));
            }

            if (!dragging) return;

            V2 ground;
            if (!TryGroundUnder(screen, out ground)) return;

            SetPose(view, ground + grabOffsetGround, RotationOf(view));
            Evaluate(view);
        }

        /// <summary>
        /// Letting go of the sheet. A knob turn ends here on the same terms as a drag, because
        /// the glow is a promise about <i>releasing</i>: C6.4 lights the edges to say "let go
        /// now and it seats", and a player who turns the last two degrees with the handle,
        /// watches the sheet light up and then finds that releasing the knob does nothing has
        /// been shown a state the game will not honour. Q/E deliberately does not preview and
        /// so has nothing to honour — it has no release, being an axis rather than a grip.
        /// </summary>
        void Release()
        {
            bool gesture = dragging || turningHandle;
            if (!gesture) { EndGesture(); return; }

            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;
            if (view == null) { EndGesture(); return; }

            SheetId id = view.Id;
            V2 ground = GroundOf(view);
            double rotation = RotationOf(view);

            EndGesture();

            // C7.5: dragged back to the cabinet is refiled. Checked before the snap, because a
            // sheet let go over the drawer is not being placed however well it happens to line
            // up with the ground underneath the chrome.
            if (ReleaseOverCabinet)
            {
                board.Remove(id);
                Deselect();
                return;
            }

            Sheet truth;
            if (board.TrySheet(id, out truth)
                && SheetFit.Fits(truth, ground, rotation, PositionTol, RotationTol))
            {
                Settle(id, truth, ground, rotation);   // C6.5
                return;
            }

            // C6.6, and it is a deliberate absence of code: the sheet stays exactly where it
            // was released, and this class calls NOTHING. No error state, no colour, no
            // message (R6.5).
            //
            // Not even Lay. The sheet was unseated when the gesture began and the transform it
            // is standing on IS its pose (C4.6), so a Lay here would restate a fact the board
            // already holds — at the cost of a re-sort and a Changed, which is a 48-row cabinet
            // rebuild fired by the one outcome the spec says produces no feedback at all.
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
            if (alreadyDown)
            {
                view = ViewOf(id);
                if (view == null) return;

                // Keep the pose it has and grab it where the pointer is, exactly as Press does.
                grabOffsetGround = GroundOf(view) - ground;
                board.Lay(id, GroundOf(view), RotationOf(view));   // C6.7
            }
            else
            {
                view = board.Lay(id, ground, 0.0);
                if (view == null) return;      // raster has not landed yet (C5.7)
                grabOffsetGround = V2.Zero;
                placing = true;
            }

            SelectOnBoard(id);
            armed = true;
            dragging = true;
            pressScreen = screen;
            Evaluate(view);
        }

        /// <summary>
        /// Abandons a <see cref="BeginPlace"/>. A sheet this call put on the board goes back to
        /// the drawer; a sheet that was already down keeps the pose it currently has, because
        /// cancelling a place is not an undo of wherever it happened to be before.
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

        void Settle(SheetId id, Sheet truth, V2 from, double fromRotation)
        {
            settleId = id;
            settleTruth = truth;
            settleFrom = from;
            settleFromRotation = fromRotation;

            // The SHORT way round. A sheet 5° from truth must not spin 355° to seat, and
            // AngleDelta is the same fold C6.3's test uses — one definition of "how far apart
            // are these two angles", so the thing that decides a fit and the thing that plays
            // it out cannot disagree.
            settleTurn = SheetFit.AngleDelta(truth.RotationDeg, fromRotation);

            settleElapsed = 0f;
            settling = true;

            if (SettleSeconds <= 0f) Seat();
        }

        /// <summary>
        /// C6.5's easing — the same smoothstep <c>PlayerHands.Advance</c> uses, deliberately, so
        /// a sheet seating on the board reads as the same kind of movement as a sheet coming to
        /// the hands. Driven from <c>Update</c> rather than a coroutine because it is a two-field
        /// state machine and a coroutine would be a second lifetime to stop when the board closes
        /// mid-settle.
        /// </summary>
        void Advance()
        {
            if (!settling) return;

            BoardSheetView view = ViewOf(settleId);
            if (view == null) { settling = false; return; }

            settleElapsed += Time.deltaTime;

            float duration = SettleSeconds;
            float k = duration <= 0f ? 1f : Mathf.Clamp01(settleElapsed / duration);
            float eased = k * k * (3f - 2f * k);   // no sudden start, no sudden stop

            SetPose(view,
                    V2.Lerp(settleFrom, settleTruth.CentreGround, eased),
                    settleFromRotation + settleTurn * eased);

            if (k < 1f) return;
            Seat();
        }

        /// <summary>The one call that marks a sheet seated. <c>BoardView.Seat</c> re-reads the
        /// exact pose from the sheet rather than taking one from here, which is C4.6 — the
        /// easing above only has to get close enough for the last frame not to jump.</summary>
        void Seat()
        {
            settling = false;
            board.Seat(settleId);
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
            BoardSheetView view = selected.HasValue ? ViewOf(selected.Value) : null;

            if (lifted != null && lifted != view) Lower(lifted);
            lifted = view;
            if (view == null) return;

            int tiers = (dragging || settling) ? 2 : 1;
            float y = (board.OnTable.Count - 1 + tiers) * Separation;

            Vector3 p = view.transform.localPosition;
            view.transform.localPosition = new Vector3(p.x, y, p.z);
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

        // ------------------------------------------------------ outline and glow

        /// <summary>C6.4. Re-asked every frame of a drag, which is what makes the glow a preview
        /// of the release rather than a report on it (C1.5).</summary>
        void Evaluate(BoardSheetView view)
        {
            Sheet truth;
            snapping = board.TrySheet(view.Id, out truth)
                       && SheetFit.Fits(truth, GroundOf(view), RotationOf(view),
                                        PositionTol, RotationTol);
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
            outlineMaterial.color = snapping ? SnapGold : SelectionGold;

            Transform t = view.transform;
            Vector3 p = t.localPosition;

            outline.transform.localPosition = new Vector3(p.x, p.y - Separation * OutlineDrop, p.z);
            outline.transform.localRotation = t.localRotation;
            outline.transform.localScale = new Vector3(OutlineScale, 1f, OutlineScale);
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
            }

            outlineRenderer.sharedMaterial = outlineMaterial;
        }

        void DetachOutline()
        {
            if (outlineRenderer != null) outlineRenderer.enabled = false;

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

            float hw = (float)(view.Sheet.Survey.SheetGroundWidth * UnitsPerMetre * 0.5);
            float hh = (float)(view.Sheet.Survey.SheetGroundHeight * UnitsPerMetre * 0.5);

            Vector3 corner = view.transform.TransformPoint(new Vector3(hw, 0f, hh));
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
            Vector3 centre = board.BoardCamera.WorldToScreenPoint(view.transform.position);
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


        void SelectOnBoard(SheetId id)
        {
            if (selected.HasValue && selected.Value.Equals(id)) return;

            selected = id;
            snapping = false;
            Raise();
        }

        void Deselect()
        {
            if (!selected.HasValue) return;

            selected = null;
            snapping = false;
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
            turningHandle = false;
            snapping = false;
        }

        static void Discard(UnityEngine.Object thing)
        {
            if (thing == null) return;

            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }
    }
}
