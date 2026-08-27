using System.Collections.Generic;
using UnityEngine;
using Archivist.Building.Collection;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// G5.1's candidate search: given what is being dragged, what on the table it would join and
    /// under whose frame.
    ///
    /// <para><b>It answers; it does not act.</b> Nothing here writes a transform, a frame or a
    /// group. <c>BoardInteractor</c> asks this once per gesture frame to decide whether to glow
    /// and once more on release to decide what to settle onto, and the two calls are the same
    /// function on the same inputs — which is what makes the preview and the outcome incapable
    /// of disagreeing. <b>A third way to fuse goes in here, or in neither branch.</b></para>
    ///
    /// <para><b>Two entry points, one <see cref="Target"/>.</b> <see cref="TryBest"/> is the
    /// strict path — <c>SheetFit.Fits</c> against the tolerances, smallest position error wins.
    /// <see cref="TryAt"/> is the assisted one (G7.1 superseded), aimed at a ghost
    /// <c>SnapHint</c> has already chosen, with no fit test at all. They produce the same kind of
    /// join; the assist just stops requiring that you aimed.</para>
    ///
    /// <para>Runs every drag frame without allocating: both member walks fill lists this class
    /// reuses, and the group table is the caller's cached one.</para>
    /// </summary>
    public sealed class BoardFusing
    {
        readonly TableOptions options;

        /// <summary>The dragged run and one candidate's, refilled per call. Never held across
        /// one: a slab in either can be refiled by the frame after.</summary>
        readonly List<BoardSheetView> dragged = new List<BoardSheetView>();
        readonly List<BoardSheetView> targets = new List<BoardSheetView>();

        /// <param name="options">The board's tuning asset, or null for the compiled defaults.
        /// The same asset the interactor and the hint read, because two components judging by
        /// two different tolerances would glow at one distance and seat at another.</param>
        public BoardFusing(TableOptions options)
        {
            this.options = options;
        }

        float PositionTol
        {
            get { return options != null ? options.PositionTolerance : TableOptions.DefaultPositionTolerance; }
        }

        float RotationTol
        {
            get { return options != null ? options.RotationToleranceDeg : TableOptions.DefaultRotationToleranceDeg; }
        }

        /// <summary>
        /// One outcome of G5.1's search: what is being dragged, what it would join, and the
        /// frame the join happens under.
        ///
        /// <para><see cref="DraggedGroup"/> and <see cref="TargetGroup"/> are 0 for a loose
        /// sheet, which is the same sentinel <c>Placement.GroupId</c> uses; the four
        /// combinations of those two zeros are exactly G5.1's four rows, so the commit is a
        /// two-way branch and not a table of special cases.</para>
        ///
        /// <para><see cref="Frame"/> is always the <b>stationary</b> thing's (G5.2).
        /// <see cref="Error"/> is the position error of the member the fit was judged on (G3.6),
        /// the quantity G5.1 ranks candidates by — measured through
        /// <c>SheetFit.PositionError</c> so the ranking uses the distance the test used.</para>
        /// </summary>
        public readonly struct Target
        {
            public readonly int DraggedGroup;
            public readonly SheetId Dragged;      // meaningless when DraggedGroup != 0
            public readonly int TargetGroup;
            public readonly SheetId TargetSheet;  // meaningless when TargetGroup != 0
            public readonly BoardFrame Frame;
            public readonly double Error;

            public Target(int draggedGroup, SheetId dragged, int targetGroup, SheetId targetSheet,
                          BoardFrame frame, double error)
            {
                DraggedGroup = draggedGroup;
                Dragged = dragged;
                TargetGroup = targetGroup;
                TargetSheet = targetSheet;
                Frame = frame;
                Error = error;
            }
        }

        /// <summary>
        /// G5.1's strict search: every fusable loose sheet and group on the table, each
        /// presenting a frame (G3.1), tested with <c>SheetFit.Fits</c>, and the smallest
        /// position error wins.
        ///
        /// <para><b>One definition of "fits", not two.</b> A dragged group is judged on ONE
        /// member (G3.6) — the one nearest, in board units, to the target's nearest fusable
        /// slab — through the same <c>SheetFit.Fits</c> a lone sheet uses.
        /// <c>PositionReach</c> scales with the <i>sheet</i> (C6.1), so grounding the test at
        /// the far end of a nine-sheet assembly would apply a tolerance to a member nowhere near
        /// the seam. A group of one is literally the sheet case, which is why the loose path
        /// below is the same code with a one-entry list.</para>
        ///
        /// <para><b>Nearness is in board units, between slab centres.</b> It picks <i>which</i>
        /// member is judged and never decides whether anything fits, so it is a pure UI question
        /// (the split §7 draws for the assist) and needs no island access.</para>
        ///
        /// <para><b>Poses come from <see cref="BoardView.TryPoseOf"/>, all of them</b> — a
        /// dragged sheet's is its transform, a dragged group's is derived from the frame the
        /// interactor writes, a stationary member's from its own group's. Reading a placement's
        /// stored coordinates instead would judge a dragged sheet against where it was before
        /// the drag started.</para>
        /// </summary>
        /// <param name="groups">The board's groups, cached by the caller — they cannot change
        /// under a drag, because fusing is evaluated on release only (G1.5).</param>
        public bool TryBest(BoardView board, SheetId selected, int selectedGroup,
                            IReadOnlyList<GroupRecord> groups, out Target best)
        {
            best = default(Target);
            if (board == null) return false;

            dragged.Clear();
            if (selectedGroup != 0) BoardSlabs.MembersOf(board, selectedGroup, dragged);
            else
            {
                BoardSheetView one = BoardSlabs.ViewOf(board, selected);
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

                Target candidate;
                if (!TryCandidate(board, selected, selectedGroup,
                                  BoardFrame.ForSheet(slab.Sheet, pose, rotation),
                                  0, slab.Id, out candidate)) continue;

                if (candidate.Error >= bestError) continue;
                bestError = candidate.Error;
                best = candidate;
                any = true;
            }

            // 2. groups, which present their stored frame directly (G3.1's second bullet).
            if (groups == null) return any;

            for (int g = 0; g < groups.Count; g++)
            {
                GroupRecord group = groups[g];
                if (!group.OnTable || group.GroupId == selectedGroup) continue;

                targets.Clear();
                BoardSlabs.MembersOf(board, group.GroupId, targets);
                if (targets.Count == 0) continue;

                Target candidate;
                if (!TryCandidate(board, selected, selectedGroup,
                                  board.FrameOf(group.GroupId), group.GroupId,
                                  default(SheetId), out candidate)) continue;

                if (candidate.Error >= bestError) continue;
                bestError = candidate.Error;
                best = candidate;
                any = true;
            }

            return any;
        }

        /// <summary>G3.6 for one candidate: pick the meeting member, then G3.2 and G3.3 verbatim
        /// on it. <see cref="dragged"/> and <see cref="targets"/> are the two slab lists,
        /// already filled.</summary>
        bool TryCandidate(BoardView board, SheetId selected, int selectedGroup,
                          BoardFrame frame, int targetGroup, SheetId targetSheet,
                          out Target candidate)
        {
            candidate = default(Target);

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

            candidate = new Target(selectedGroup, selected, targetGroup, targetSheet, frame,
                                   SheetFit.PositionError(meeting.Sheet, frame, pose));
            return true;
        }

        /// <summary>
        /// G5.1's outcome for an <b>assisted</b> release: the same <see cref="Target"/> the
        /// strict path builds, aimed at the ghost's candidate instead of at whatever passed
        /// <c>SheetFit.Fits</c>.
        ///
        /// <para><b>There is no fit test here, and that is the entire change.</b> The frame is
        /// the stationary thing's (G5.2) and G5.1's four outcomes are unchanged — the two group
        /// flags are the ghost's and the caller's, so the commit's branch resolves loose+loose,
        /// loose+group, group+loose and group+group as before, and the settle plays the same
        /// smoothstep. An assisted release picks the same <i>kind</i> of join; it just stops
        /// requiring that you aimed.</para>
        ///
        /// <para><b>The meeting member is the ghost's, not a search</b> (G3.6) — the slab the
        /// ghost was computed for, which <c>SnapHint.NearestPair</c> chose by the same
        /// nearest-fusable-pair rule <see cref="TryCandidate"/> uses. Searching again here would
        /// be a second opinion about a question already answered and drawn; grounding it on the
        /// <i>selection</i> instead — the slab the player happens to be holding the assembly by
        /// — would settle onto a pose the ghost never showed.</para>
        ///
        /// <para><b><see cref="SheetKinship.Fusable"/> is re-asked.</b> <c>SnapHint.Nearest</c>
        /// already gates on <see cref="SheetKinship.Neighbours"/>, which calls it first, so G3.4
        /// and G-A5 already hold. It is asked again for the commit's reason: a fuse to the wrong
        /// assembly is not recoverable (G5.5 — nothing ever leaves a group), and two struct
        /// comparisons is cheap insurance against the one outcome that cannot be undone.</para>
        ///
        /// <para><see cref="Target.Error"/> is measured through <c>SheetFit.PositionError</c>
        /// even though nothing ranks it here, because a zero would read as "landed perfectly"
        /// and under the assist it routinely will not have been.</para>
        ///
        /// <para>Static: no tolerance is consulted and no list is walked, so there is nothing for
        /// an instance to hold.</para>
        /// </summary>
        public static bool TryAt(BoardView board, SheetId selected, int selectedGroup,
                                 SnapHint.Ghost candidate, out Target target)
        {
            target = default(Target);
            if (!candidate.Any || board == null) return false;

            BoardSheetView meeting = BoardSlabs.ViewOf(board, candidate.Meeting);
            if (meeting == null) return false;

            Sheet theirs;
            if (!board.TrySheet(candidate.Target, out theirs)) return false;
            if (!SheetKinship.Fusable(meeting.Sheet, theirs)) return false;

            V2 pose;
            double rotation;
            if (!board.TryPoseOf(meeting.Id, out pose, out rotation)) return false;

            target = new Target(selectedGroup, selected,
                                candidate.TargetGroup, candidate.Target, candidate.Frame,
                                SheetFit.PositionError(meeting.Sheet, candidate.Frame, pose));
            return true;
        }
    }
}
