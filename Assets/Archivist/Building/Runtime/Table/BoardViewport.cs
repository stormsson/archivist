using UnityEngine;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Where the board camera is looking: a zoom factor and a centre in board units, plus the
    /// arithmetic that keeps the two honest. It is the whole of the interactive framing of
    /// G10.1's second half — the pan that G10.1 recorded as owed and did not build.
    ///
    /// <para><b>C8.13 is now fully superseded, and this class is what supersedes it.</b> C8.13
    /// said "no zoom, no pan — the board always frames the whole board", and its reason was
    /// absolute seating: the mounting sheet's extent was the player's only clue to where a
    /// sheet belonged, so cropping it removed the one reference on screen. G1.9 took absolute
    /// correctness out of scope and G10.1 lifted the zoom half on that argument, leaving the
    /// pan half explicitly unpaid — <i>"pan is the other half of this change and has not been
    /// built"</i>. Both halves are lifted here. What is kept of C8.13 is its framing as the
    /// <b>floor</b>: <see cref="Zoom"/> 1 is exactly the view C8.13 described, and the player
    /// cannot zoom out past it, so the original composition is still a place the board returns
    /// to rather than a state it has lost.</para>
    ///
    /// <para><b>This is view state, and it is deliberately not board state.</b> Nothing here
    /// goes into <c>BoardStore</c>. That store holds player facts about paper — placements,
    /// membership, frames — and §4.2 and G4.4 shape it to be persisted; a camera is not a fact
    /// about the archive and a save that restored someone's scroll position would be saving the
    /// wrong thing. So the viewport is made in <c>BoardView.BuildCamera</c>, dies in
    /// <c>Teardown</c>, and is reset to <see cref="TableOptions.BoardZoom"/> centred on every
    /// <c>Show</c>. A player who zoomed in, closed the table and came back gets the board as the
    /// spec composes it, not as they last left it.</para>
    ///
    /// <para><b>Zoom is multiplicative and is applied about a point.</b> Two decisions, and both
    /// were taken against the obvious alternative. A linear step — <c>zoom ± 0.25</c> — is a
    /// quarter of the view at zoom 1 and a sixteenth at zoom 4, so the same wheel notch feels
    /// violent zoomed out and inert zoomed in; a constant <i>ratio</i> per notch is the same
    /// apparent step everywhere, which is what a wheel is expected to do. And zooming about the
    /// board centre rather than about the pointer makes the thing being looked at slide out of
    /// frame exactly when the player leans in on it — the gesture becomes a fight, and the
    /// player learns to zoom and then pan back, twice per look. <see cref="ZoomAbout"/> holds
    /// one board point still, which is the whole difference between a magnifier and a
    /// wrestle.</para>
    ///
    /// <para><b>The pan clamp is stated as travel, not as a rectangle.</b> The centre may move
    /// until the view's edge reaches the mounting sheet's edge and no further:
    /// <c>travel = max(0, boardHalf − viewHalf)</c> per axis. Two consequences worth stating
    /// because they are the design, not accidents. At zoom 1 both travels are zero — the view
    /// already contains the board's full height and (on any wide viewport) more than its width,
    /// so C8.13's framing is not merely the floor of the zoom but a genuinely fixed view, with
    /// nothing to pan to. And the clamp is on the <i>camera centre</i>, so a slab whose centre
    /// sits on the board's edge keeps its far half outside the view: what guarantees that half
    /// is reachable is <c>BoardSpace.ForIsland</c>'s 8% padding, which already exists to hold
    /// the coastal sheets that hang off the land (4.10 units on island 0). An extra overscroll
    /// margin was considered and rejected — it is a second tuning value doing a job the padding
    /// is already doing, and paper is only ever laid where the pointer can go, which is inside
    /// the view.</para>
    ///
    /// <para><b>No engine dependency beyond <c>Vector2</c>/<c>Mathf</c>, and no Camera.</b> This
    /// class does not know what a Camera is: <c>BoardView</c> reads <see cref="OrthographicSize"/>
    /// and <see cref="Centre"/> and writes them onto the rig it owns. That keeps the one thing
    /// worth testing — the fixed point of <see cref="ZoomAbout"/> — testable without a rig, and
    /// keeps the depth of 100 and the Table-only culling mask (C5.1) in the one place that has
    /// ever set them.</para>
    /// </summary>
    public sealed class BoardViewport
    {
        /// <summary>Below this a board dimension is degenerate — <c>Rect2.Empty</c> carries
        /// MaxValue/MinValue sentinels and <c>BoardSpace</c> hands them straight through, which
        /// would otherwise produce a negative orthographic size and a camera that renders
        /// nothing while looking, from the inspector, perfectly reasonable.</summary>
        const float SmallestBoard = 0.001f;

        readonly float boardWidth;
        readonly float boardHeight;
        readonly float homeZoom;
        readonly float minZoom;
        readonly float maxZoom;

        /// <summary>Current zoom, in the units <see cref="TableOptions.BoardZoom"/> is in: the
        /// factor the "whole board fits" half-height is divided by. 1 is C8.13's framing.
        /// </summary>
        public float Zoom { get; private set; }

        /// <summary>The board point the camera is centred on, in board units. <c>x</c> is board
        /// X and <c>y</c> is board <b>Z</b> — the same axis pun <c>BoardSpace</c> makes and for
        /// the same reason: the board lies in the XZ plane and the caller builds the Vector3.
        /// </summary>
        public Vector2 Centre { get; private set; }

        public float MinZoom { get { return minZoom; } }
        public float MaxZoom { get { return maxZoom; } }

        /// <summary>The camera's half-height, which is G10.1's formula unchanged:
        /// <c>BoardHeight * 0.5 / Zoom</c>. Divided, not multiplied — a smaller orthographic
        /// size is a closer camera.</summary>
        public float OrthographicSize { get { return boardHeight * 0.5f / Zoom; } }

        public BoardViewport(float boardWidth, float boardHeight,
                             float homeZoom, float minZoom, float maxZoom)
        {
            this.boardWidth = Mathf.Max(SmallestBoard, boardWidth);
            this.boardHeight = Mathf.Max(SmallestBoard, boardHeight);

            this.minZoom = Mathf.Max(0.1f, minZoom);
            this.maxZoom = Mathf.Max(this.minZoom, maxZoom);
            this.homeZoom = Mathf.Clamp(homeZoom, this.minZoom, this.maxZoom);

            Reset();
        }

        /// <summary>Back to <see cref="TableOptions.BoardZoom"/>, centred. Called on every
        /// <c>Show</c> — see the class comment for why a view is not a thing to restore.
        /// </summary>
        public void Reset()
        {
            Zoom = homeZoom;
            Centre = Vector2.zero;
        }

        /// <summary>
        /// Multiplies the zoom by <paramref name="factor"/> while holding
        /// <paramref name="anchorBoard"/> at the same place on screen.
        ///
        /// <para>The fixed point falls out of the orthographic projection in one line. A board
        /// point <c>a</c> sits at a screen offset proportional to <c>(a − c) / h</c>, where
        /// <c>c</c> is the centre and <c>h</c> the half-height; <c>h</c> is inversely
        /// proportional to the zoom, so holding that ratio across a change from <c>z0</c> to
        /// <c>z1</c> gives <c>c1 = a + (c0 − a) · z0/z1</c>. Aspect does not appear: both axes
        /// scale by the same factor, so the horizontal fixed point comes free.</para>
        ///
        /// <para>The centre is moved <b>only if the zoom actually changed</b>. At a stop the
        /// alternative — clamping the zoom and moving the centre by the un-clamped factor —
        /// would let the view creep toward the pointer on every further notch of a wheel the
        /// player is still turning, which reads as a slow drift with no input to blame.</para>
        /// </summary>
        public void ZoomAbout(float factor, Vector2 anchorBoard, float aspect)
        {
            float before = Zoom;
            float after = Mathf.Clamp(before * factor, minZoom, maxZoom);
            if (after == before) return;

            Zoom = after;
            Centre = anchorBoard + (Centre - anchorBoard) * (before / after);
            Clamp(aspect);
        }

        /// <summary>Slides the view by a board-unit delta and re-clamps. The caller computes the
        /// delta from the ground under the pointer, so the pan is 1:1 with the board by
        /// construction rather than by a pixels-per-unit factor computed twice.</summary>
        public void MoveBy(Vector2 deltaBoard, float aspect)
        {
            Centre += deltaBoard;
            Clamp(aspect);
        }

        /// <summary>
        /// How far the centre may travel from the board centre on each axis, given the current
        /// zoom and the viewport's aspect. Zero on an axis means the view already contains the
        /// board on that axis and there is nothing to pan to — which is the whole of zoom 1.
        /// </summary>
        public Vector2 PanLimit(float aspect)
        {
            float halfH = OrthographicSize;
            float halfW = halfH * Mathf.Max(0.0001f, aspect);

            return new Vector2(Mathf.Max(0f, boardWidth * 0.5f - halfW),
                               Mathf.Max(0f, boardHeight * 0.5f - halfH));
        }

        void Clamp(float aspect)
        {
            Vector2 travel = PanLimit(aspect);
            Centre = new Vector2(Mathf.Clamp(Centre.x, -travel.x, travel.x),
                                 Mathf.Clamp(Centre.y, -travel.y, travel.y));
        }
    }
}
