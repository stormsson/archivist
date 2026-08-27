using System.Collections.Generic;
using UnityEngine;
using Archivist.Building.Config;
using Archivist.Building.Collection;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The assisted snap of §8 (G7.2–G7.5), and the seated glow of C6.4 it draws with: while a
    /// sheet is dragged, the sheet and the one slab it is about to join carry a tight hot band,
    /// and a pale <b>ghost</b> — four thin bars, an empty rectangle — is drawn on the table at
    /// the exact pose the dragged sheet will occupy if it is let go.
    ///
    /// <para><b>G7.1 IS SUPERSEDED, and this file is where it happened</b> (groups_spec §8.6).
    /// <b>With <c>gameplay.assistedSnap</c> on, releasing joins wherever the ghost is
    /// showing</b> — widening capture from <c>SheetFit</c>'s <c>reach</c> (1.54 board units on
    /// island 0's Land Survey, ≈61 px at <c>BoardZoom</c> 2) to the hint range
    /// (<c>GlowingHintRange</c> × the slab's long side, 19.03 units, ≈750 px) and making
    /// <c>RotationToleranceDeg</c> irrelevant while it is on. The playtest report was "when I
    /// drag a sheet over another and the halo starts, releasing does not snap": the release was
    /// always correct, but the halo lit at 750 px and the fuse happened at 61 px, a factor of
    /// twelve. A signal shown across a radius in which letting go does nothing is a promise the
    /// game does not keep, however clearly it is drawn, so the fix was to make the promise true.
    /// The assist no longer says "these two belong together"; it says <b>"let go and it lands
    /// here"</b>. Nothing about the strict path changed: with the assist OFF,
    /// <c>TryBestFuse</c> decides exactly as it did.</para>
    ///
    /// <para><b>Rung 2 of G7.2 no longer exists.</b> Its middle state was defined by being the
    /// one in which a release did nothing, and there is no such state left, so
    /// <see cref="ShowSeated"/>'s hot steady band is drawn in place of the pale pulsing halo —
    /// one look for one meaning. G7.5's pulse is not retired with it: it moves to the ghost,
    /// where "provisional" is exactly what it means, and <see cref="AlphaAt"/> is unchanged.
    /// </para>
    ///
    /// <para><b>This file never evaluates a fit, and that part of G7.1's discipline is kept.</b>
    /// <see cref="TryGhost"/> asks two questions and no third: is this slab related
    /// (<see cref="SheetKinship.Neighbours"/>, the island question) and is it near (board-unit
    /// distance between slab centres, a pure UI question). It builds the candidate's frame from
    /// <see cref="BoardView"/>'s public answers and reports where G3.1 puts the dragged sheet
    /// under it. No tolerance is read, no <c>SheetFit</c> is called, no board state is written.
    /// <b>If an edit here reaches for <c>SheetFit</c>, that edit is wrong</b> — the decision to
    /// fuse belongs to <see cref="BoardInteractor"/>, the one class that can keep the preview and
    /// the release the same answer.</para>
    ///
    /// <para><b>The ghost's pose does NOT come from <c>TryBestFuse</c>, and someone will try
    /// it.</b> <c>TryCandidate</c> runs <c>SheetFit.Fits</c> and <c>TryBestFuse</c>
    /// <c>continue</c>s past every candidate that fails, so a frame only comes back once the
    /// sheet is <i>already inside tolerance</i> — 61 px out on island 0. A ghost that appears
    /// only after you have hit the target is a target you cannot aim with. The pose comes from
    /// the <b>hint's</b> candidate, which <see cref="Nearest"/> finds fit or no fit, at 750 px.
    /// </para>
    ///
    /// <para><b>Light rather than line.</b> Applying G7.5's alpha to the 1.02 selection outline
    /// gives a rim 0.128 units wide on island 0's Land Survey slab — 3 screen pixels at the
    /// original framing, 6 at <c>BoardZoom</c> 2 — and G7.5 takes that hairline to alpha 0.15 at
    /// every trough. No curve tuning fixes a three-pixel line. So: concentric quads bleeding
    /// outward from the paper, each faint, compositing into a gradient about 22 px wide. Every
    /// constant lives in <see cref="CabinetStyle"/> and every one is ⟨proposed⟩ — unlike the rest
    /// of that file they have no mockup behind them, so the first playtest is their authority.
    /// </para>
    ///
    /// <para><b>Nested filled quads, not a falloff texture.</b> Slab aspect varies per office —
    /// 12.85 × 19.03 for a Land Survey sheet, 8.75 × 4.25 for a Hydrographic strip, 2.75 × 2.75
    /// for an Antiquarian detail — so a texture's border would be a different width on each axis
    /// of each survey unless regenerated per candidate. The rings take their width in <b>board
    /// units</b> and solve for the scale per axis (see <see cref="Place"/>), which no single
    /// texture can do, and cost no sampling or upload. They are nested fills rather than annular
    /// rings: a point just outside the paper is covered by all <c>HaloRings</c> and a point at
    /// the outer edge by only the last, so the falloff is the accumulation and no ring has to
    /// know where the others are.</para>
    ///
    /// <para><b>Both slabs get a halo, from this one file.</b> Handing the alpha back so
    /// <c>BoardInteractor</c> could tint its selection outline gives the dragged sheet a pulsing
    /// rim and the candidate a halo — two looks for one relationship, which is what a signal
    /// meaning "these two go together" must not do. The halo is owned in pairs here, and
    /// <c>BoardInteractor</c>'s outline is a steady rim that knows nothing about either.</para>
    ///
    /// <para><b>There is only ever ONE ghost.</b> <see cref="Nearest"/> returns a single
    /// candidate. Nine faint rectangles round a Land Survey lattice is a lightshow, and the
    /// player would then have to work out which the release will pick — the question the ghost
    /// exists to answer.</para>
    ///
    /// <para><b>The split that makes the trigger clean</b> (G7.3). <i>"Are these two related?"</i>
    /// is an <b>island</b> question and goes to <see cref="SheetKinship.Neighbours"/>, which
    /// needs the sheets' real ground rects. <i>"Are they near?"</i> is <b>pure UI</b>: the
    /// distance between two slabs' centres in board units, off the transforms. The range is
    /// G7.3's <c>max(slabWidth, slabHeight) * GlowingHintRange</c> — see <see cref="TryGhost"/>
    /// for the measurement behind <c>max</c> — and reads nothing from the island either, because
    /// <see cref="BoardSheetView"/> bakes <c>Survey.SheetGroundWidth/Height × UnitsPerMetre</c>
    /// into its quad's corners and leaves <c>localScale</c> at one. At the default range of 1.0
    /// the hint lights about one sheet-step from home, identically for a 19.03-unit Land Survey
    /// slab and an 8.75-unit Hydrographic strip.</para>
    ///
    /// <para><b>Rotation is NOT tested, deliberately.</b> An unturned sheet must still be told it
    /// is near something related; telling it only once aligned defeats the assist, because by
    /// then the player has solved the problem the hint was meant to help with.</para>
    ///
    /// <para><b>G7.4: exactly two slabs carry a halo, however large either group grows.</b> When
    /// the best candidate is a member of an assembly, only that member is lit — never the union,
    /// which is what <c>BoardInteractor.PlaceOutline</c> draws for a <i>selection</i>. The halo
    /// names the <b>edge</b> you are about to join, not the mass you are joining, and so also
    /// tells you which way round you are. Nothing shaped like <c>TryUnion</c> is called here.
    /// </para>
    ///
    /// <para><b>That holds on the dragged side too, and it is a pair that is searched for.</b>
    /// An assembly is dragged by grabbing one of its sheets, and the grabbed sheet says nothing
    /// about which end of the run is approaching anything. So <see cref="NearestPair"/> ranks
    /// <i>pairs</i> across both runs and every answer here — the lit slab, the slot's pose, the
    /// member <c>BoardFusing.TryAt</c> grounds the join on — is the <b>meeting</b> member's, the
    /// same G3.6 rule the strict path applies in <c>BoardFusing.TryCandidate</c>. Grounding any
    /// of them on the selection instead makes group-to-group snapping a function of where the
    /// player clicked to pick the assembly up.</para>
    ///
    /// <para><b>Not a MonoBehaviour.</b> The caller drives it from inside its drag loop, in a
    /// known order relative to its own outline placement; an <c>Update</c> of its own would put
    /// the halo one frame out of step with the rim it sits under. It also means the class can be
    /// exercised without a scene.</para>
    ///
    /// <para><b>Every quad shares the slab's mesh and never owns it.</b>
    /// <see cref="BoardSheetView"/> destroys that mesh in <c>OnDestroy</c>, so every reference is
    /// dropped the moment a halo is cleared. All rings of both halos and all four bars of the
    /// ghost hold the same borrowed mesh — the bars are that quad squashed to a line, which is
    /// also why the ghost cannot become a copy of the map: no texture, no material to carry one.
    /// Every quad goes on the <b>Table</b> layer, because the board camera's culling mask is that
    /// layer and nothing else (C5.1) — a quad on the default layer is built, positioned, enabled
    /// and invisible. Halos are rebuilt whenever their parent is not the current root, because
    /// <c>BoardView.Hide</c> destroys the whole rig.</para>
    ///
    /// <para><b>Allocates nothing per frame.</b> Rings, renderers and materials are built once
    /// and reused; the candidate walk is an indexed loop over <see cref="BoardView.OnTable"/>.
    /// The one allocation on the path is inside <see cref="SheetKinship.Neighbours"/>, which
    /// builds two four-corner arrays per pair it tests — the ordering in <see cref="Nearest"/>
    /// exists partly to keep the number of pairs small.</para>
    ///
    /// <para><b>The alpha is blended, which the material has to be told.</b> URP/Unlit is opaque
    /// by default and discards <c>color.a</c>, so a halo on the stock material renders as flat
    /// gold — a bug that looks implemented. <see cref="MakeBlended"/> is the one place that
    /// recipe is written down; every ring material goes through it, and so does the caller's
    /// selection outline.</para>
    /// </summary>
    public sealed class SnapHint
    {
        /// <summary>C8.8 / C5.1's layer. Must match <c>BoardView</c>'s and
        /// <c>BoardInteractor</c>'s — the board camera renders only this layer.</summary>
        const string TableLayerName = "Table";

        /// <summary>Feel values (§10). Null falls back to <c>TableOptions</c>' <c>Default*</c>
        /// constants, exactly as <c>BoardInteractor</c> does, so a bench with no options asset
        /// still gets a halo placed at the right height rather than none at all. Only
        /// <c>SheetSeparation</c> is read: everything else the halo needs is on the slabs.</summary>
        readonly TableOptions options;

        /// <summary>One material per ring, <b>shared by both halos</b>. The two halos are always
        /// in the same state and always at the same alpha — that is what "in sync" means (G7.5) —
        /// so a second set would be two objects that must be kept equal, plus <c>HaloRings</c>
        /// more colour writes a frame. Null until the first <see cref="Build"/>; the array
        /// outlives the quads on purpose, because the quads are rebuilt on every reopen of the
        /// board and rebuilding the materials with them would leak a set per open.</summary>
        Material[] ringMaterials;

        /// <summary>The candidate's halo (G7.3's <c>best</c>) and the dragged sheet's. Two, not
        /// one reparented: both are lit on the same frame, which is the whole signal.</summary>
        readonly Halo targetHalo = new Halo("SnapHintHalo_Target");
        readonly Halo draggedHalo = new Halo("SnapHintHalo_Dragged");

        /// <summary>The one ghost. Not an array and not a pool: <see cref="Nearest"/> returns a
        /// single candidate, so a second slot could only ever be drawn by inventing a second
        /// answer — see the class comment on why a ghost per neighbour is a lightshow.</summary>
        readonly Slot slot = new Slot("SnapHintGhost");

        /// <summary>
        /// The dragged run — every member of the assembly in hand, or the one loose slab —
        /// refilled at the top of each search and never held across one, for the reason
        /// <c>BoardFusing</c>'s two lists give: a slab in it can be refiled by the frame after.
        ///
        /// <para><b>The whole run, and not the slab under the pointer.</b> An assembly is
        /// dragged by grabbing one of its sheets, and which sheet that is says nothing about
        /// which end of the assembly is approaching anything (G5.6 drags the run rigidly). A
        /// search grounded on the grabbed slab makes the candidate depend on where the player
        /// happened to click, so a group brought edge-on to another finds nothing unless the
        /// sheet in hand is itself within range of it and overlaps its ground. That is what the
        /// strict path has always avoided by walking both runs
        /// (<c>BoardFusing.TryCandidate</c>), and the assist now does it the same way.</para>
        /// </summary>
        readonly List<BoardSheetView> run = new List<BoardSheetView>();

        /// <summary>The ghost's bars all share one material, because they are one line bent
        /// round a rectangle and must therefore be one alpha by construction rather than by four
        /// matching writes. Null until the first <see cref="Show"/>; it outlives the bars on
        /// purpose, for the reason <see cref="ringMaterials"/> gives.</summary>
        Material ghostMaterial;

        /// <param name="options">The board's tuning asset, or null for the compiled defaults.</param>
        public SnapHint(TableOptions options)
        {
            this.options = options;
        }

        float Separation
        {
            get { return options != null ? options.SheetSeparation : TableOptions.DefaultSheetSeparation; }
        }

        /// <summary>
        /// G7.5's pulse, sampled. A raised sine so it is smooth at both ends — a triangle wave
        /// has a corner at full and at minimum, and a corner in a glow reads as a flicker. It
        /// multiplies the whole halo, so all <c>HaloRings</c> of both halos breathe as one thing.
        ///
        /// <para>It never reaches zero: <c>HintAlphaMin</c> is 0.15 because a halo that vanished
        /// would read as broken rather than as hinting.</para>
        ///
        /// <para><paramref name="time"/> is folded into one period first: the caller's clock has
        /// been running since the game started, so by hour ten it is a float in the tens of
        /// thousands whose own spacing is coarser than a millisecond. Wrapping stops the multiply
        /// that follows from amplifying that; it cannot recover precision already lost.</para>
        /// </summary>
        public static float AlphaAt(float time)
        {
            float t = Mathf.Repeat(time, CabinetStyle.HintPeriodSeconds);
            float phase = 2f * Mathf.PI * t / CabinetStyle.HintPeriodSeconds;

            return CabinetStyle.HintAlphaMin
                 + (CabinetStyle.HintAlphaMax - CabinetStyle.HintAlphaMin)
                   * 0.5f * (1f + Mathf.Sin(phase));
        }

        /// <summary>
        /// The assist's whole question, asked once: <b>is there a related slab near enough, and
        /// if so where would the dragged sheet land on joining it?</b> Returns false — with
        /// <paramref name="ghost"/> at <see cref="Ghost.None"/> — when the assist is off, when
        /// there is no related slab on the table, or when the nearest one is out of range.
        ///
        /// <para><b>This is a pure query. It draws nothing and writes nothing.</b> That is the
        /// seam that keeps the preview and the release the same answer:
        /// <c>BoardInteractor.Evaluate</c> calls it to decide <c>snapping</c>,
        /// <c>BoardInteractor.PlaceOutline</c> draws the result, and
        /// <c>BoardInteractor.Release</c> calls it again to decide the join. Three consumers,
        /// one function, no state between them — so there is no frame on which what is drawn,
        /// what <c>snapping</c> says, and what a release does can differ.</para>
        ///
        /// <para><b>The frame is the STATIONARY thing's, always</b> (G5.2). A loose candidate
        /// derives one through <see cref="BoardFrame.ForSheet"/>; a candidate that belongs to an
        /// assembly presents its group's stored frame (G3.1's second bullet, G7.4 — the ghost
        /// names the edge you are joining, and the frame behind that edge is the assembly's).
        /// The ghost pose is then G3.1 applied to the dragged sheet's own truth:
        /// <c>frame.PositionOf(truth)</c> and <c>frame.RotationOf(truth)</c>. No fit maths, no
        /// tolerance, no <c>SheetFit</c>.</para>
        ///
        /// <para><b>For a dragged GROUP the search runs over every member</b> (see
        /// <see cref="run"/>), and the ghost is the <b>meeting</b> member's landing pose — the
        /// member that came nearest something related, not the one under the pointer.
        /// <paramref name="dragged"/> is then read only for its slab size, which every member of
        /// an assembly shares: a group is one survey by construction, since every join is gated
        /// on <see cref="SheetKinship.Fusable"/> and that demands the same office, year and
        /// scale.</para>
        ///
        /// <para>One member's pose is the honest drawing of a group's landing:
        /// <c>BoardInteractor.Settle</c> moves the assembly by handing its frame to the target's,
        /// which puts every member where that frame says — and for the meeting member, that is
        /// exactly the pose drawn here. One slot still means one place the paper goes; the rest
        /// of the run follows it rigidly, which is what a frame is. Drawing a slot per member
        /// would be the lightshow the class comment rejects, and the meeting member is the one
        /// the player is aiming with.</para>
        ///
        /// <para><b>The range is <c>max</c> of the slab's two sides, and G7.3 said <c>min</c>
        /// until this class was measured</b> (§8.3 carries the correction and its table).
        /// <c>min</c> is C6.1's quantity, right for a <i>tolerance</i> — the short axis is where
        /// a near-miss first stops looking like the same sheet, which is why <c>SheetFit</c>'s
        /// reach still uses it. A <i>range</i> has to span a lattice step, and a step is
        /// <c>side × (1 − OverlapFraction)</c>, so nothing about the short side bounds it. On
        /// island 0, <c>min</c> gave a Land Survey range of 12.85 against a long step of 15.22,
        /// and a Hydrographic range of 4.25 against spacing of 6.13–7.62: sheets stayed dark at
        /// their own correct poses. Under <c>max</c> every true step is the long side × 0.8 or
        /// less, with no fudge factor.</para>
        ///
        /// <para><b>That widening now widens CAPTURE, and it still does no discriminating.</b>
        /// Whether two sheets may be paired at all is entirely
        /// <see cref="SheetKinship.Neighbours"/>'s answer — same island, office, year and scale,
        /// neither the whole-island sheet (R2.2a), overlapping true ground rects — and
        /// <c>Neighbours</c> calls <c>Fusable</c> first, so G3.4 and G-A5 gate the assisted join
        /// as they gate the strict one. The range gates board distance and nothing else, so a
        /// wider one admits a related sheet that is farther away and never an unrelated one. The
        /// assist is in fact <i>stricter</i> on kinship: <c>Fits</c> needs only <c>Fusable</c>,
        /// this needs overlapping ground too, so two correctly posed same-survey sheets four
        /// steps apart fuse by the strict path and get no ghost (§3.6's deliberate
        /// asymmetry).</para>
        /// </summary>
        public bool TryGhost(BoardView board, BoardSheetView dragged, out Ghost ghost)
        {
            ghost = Ghost.None;

            // Cheapest gate first, and the one the strict path hangs on: with the assist off
            // nothing here runs at all, so there is no candidate, no ghost, and Release falls
            // through to TryBestFuse unchanged.
            if (!GameplayOptions.AssistedSnap) return false;
            if (board == null || dragged == null) return false;

            Mesh draggedMesh = MeshOf(dragged);
            if (draggedMesh == null) return false;

            double range = RangeOf(draggedMesh) * GameplayOptions.GlowingHintRange;

            // Written as !(range > 0) rather than range <= 0 so that a NaN fails here instead of
            // passing every comparison below and pairing with whatever the loop happened to
            // reach last. GameplayOptions already refuses a NaN GlowingHintRange for the same
            // reason, and this is the belt to that pair of braces: a degenerate mesh can produce
            // one too.
            if (!(range > 0.0)) return false;

            Fill(board, dragged);

            BoardSheetView meeting;
            BoardSheetView best = NearestPair(board, (float)(range * range), out meeting);
            if (best == null) return false;

            Sheet mine;
            if (!board.TrySheet(meeting.Id, out mine)) return false;

            // The candidate's frame, by G3.1's two bullets and nothing else.
            int group = board.GroupIdOf(best.Id);
            BoardFrame frame;

            if (group != 0)
            {
                frame = board.FrameOf(group);
            }
            else
            {
                Sheet truth;
                V2 pose;
                double rotation;
                if (!board.TrySheet(best.Id, out truth)) return false;
                if (!board.TryPoseOf(best.Id, out pose, out rotation)) return false;

                frame = BoardFrame.ForSheet(truth, pose, rotation);
            }

            ghost = new Ghost(meeting.Id, best.Id, group, frame,
                              frame.PositionOf(mine), frame.RotationOf(mine));
            return true;
        }

        /// <summary>
        /// What <see cref="TryGhost"/> found: the slab that won, the frame the join would happen
        /// under, and where G3.1 puts the dragged sheet under that frame.
        ///
        /// <para><see cref="TargetGroup"/> is 0 when the candidate is loose — the same sentinel
        /// <c>Placement.GroupId</c> and <c>BoardInteractor.FuseTarget</c> use, so the caller can
        /// hand this straight into G5.1's four-outcome branch without a translation step.
        /// <see cref="Target"/> is always the real slab, group or not: it is the slab the halo
        /// is drawn on (G7.4 — the edge, not the mass) and the one the caller re-checks
        /// <see cref="SheetKinship.Fusable"/> against before committing.</para>
        ///
        /// <para><see cref="Position"/> and <see cref="RotationDeg"/> are in <b>ground</b>
        /// space, like every other pose that crosses a seam in this folder. The drawing converts
        /// through <c>BoardSpace.ToBoard</c>; the settle consumes them as they are.</para>
        ///
        /// <para><see cref="Meeting"/> is the member of the DRAGGED run the answer was computed
        /// for — G3.6's meeting member, found here by the same nearest-fusable-pair rule
        /// <c>BoardFusing.TryCandidate</c> uses on the strict path. Everything downstream is
        /// grounded on it and not on the selection: the halo (G7.4 names the edge, and the edge
        /// is this member), the slot's pose, and the error <c>BoardFusing.TryAt</c> reports. For
        /// a loose sheet it is that sheet, which is why the loose path needed no special
        /// case.</para>
        /// </summary>
        public readonly struct Ghost
        {
            /// <summary>False for <see cref="None"/> and true for everything this class
            /// returns, so a caller can hold one of these in a field and ask it rather than
            /// keeping a parallel bool that can disagree with it.</summary>
            public readonly bool Any;

            public readonly SheetId Meeting;
            public readonly SheetId Target;
            public readonly int TargetGroup;
            public readonly BoardFrame Frame;
            public readonly V2 Position;
            public readonly double RotationDeg;

            public Ghost(SheetId meeting, SheetId target, int targetGroup, BoardFrame frame,
                         V2 position, double rotationDeg)
            {
                Any = true;
                Meeting = meeting;
                Target = target;
                TargetGroup = targetGroup;
                Frame = frame;
                Position = position;
                RotationDeg = rotationDeg;
            }

            /// <summary>No candidate. <c>default(Ghost)</c> is this by construction, which is
            /// what makes an uninitialised field safe.</summary>
            public static readonly Ghost None = default(Ghost);
        }

        /// <summary>
        /// Draws the assisted state for one frame: <see cref="ShowSeated"/>'s hot band on both
        /// slabs of the pair, and the ghost slot at <paramref name="ghost"/>'s pose. Returns
        /// false, having cleared, when the rig cannot carry either — so the caller never gets
        /// half of a signal whose two halves mean different things.
        ///
        /// <para><b>The halo is the SEATED look, not a third dress:</b> with the assist on,
        /// "there is a relative here" and "releasing will join it" are one statement, and one
        /// statement gets one look.</para>
        ///
        /// <para><b>The ghost breathes and the halo does not.</b> The paper in hand and the sheet
        /// it is joining are facts, so they are drawn still; the slot is a place nothing is yet,
        /// so it is drawn moving. It is also the channel that separates assist-on from assist-off
        /// at a glance — with the assist off a fitting release gives the same hot band, no slot
        /// and no motion.</para>
        ///
        /// <para><paramref name="time"/> must be an <b>unscaled, monotonic</b> clock:
        /// <c>Time.unscaledTime</c>. Not <c>Time.time</c> — a pause or slow-motion would stop or
        /// stretch the pulse, and a slot frozen mid-fade reads as the game having hung. Not a
        /// per-drag stopwatch either: the shared phase is what makes the pulse look like a
        /// property of the board rather than an animation that restarts.</para>
        /// </summary>
        public bool Show(BoardView board, Ghost ghost, float time)
        {
            if (!ghost.Any || board == null) { Clear(); return false; }

            // Everything below is drawn on the MEETING member, which is the selection only when
            // a loose sheet is dragged. G7.4 puts the halo on the edge being joined, and the
            // slot is that member's landing pose; drawing either on the grabbed slab would name
            // one sheet in the halo and land the slot against another. THAT IS WHY NO DRAGGED
            // SLAB IS PASSED IN: the ghost already names the only member any of this is about,
            // and a parameter holding the selection is an invitation to draw on it.
            BoardSheetView meeting = Slab(board, ghost.Meeting);
            if (meeting == null) { Clear(); return false; }

            Mesh mesh = MeshOf(meeting);
            Transform root = RootOf(board);
            if (mesh == null || root == null) { Clear(); return false; }

            // The halo goes on the ghost's OWN candidate, looked up by id, rather than on
            // whatever ShowSeated's independent search would have picked. At the default
            // GlowingHintRange of 1.0 the two searches are identical and it makes no difference;
            // at any other value they are not, and a halo naming one slab while the slot lands
            // against another is precisely the kind of disagreement this change exists to
            // remove. One candidate, one halo, one slot.
            if (!Draw(board, meeting, Slab(board, ghost.Target), mesh,
                      CabinetStyle.SeatedColour, CabinetStyle.SeatedAlphaPeak,
                      CabinetStyle.SeatedBleed))
                return false;                                   // Draw() has already cleared

            EnsureGhostMaterial();

            Color c = CabinetStyle.HaloColour;
            c.a = CabinetStyle.GhostAlphaPeak * AlphaAt(time);
            ghostMaterial.color = c;

            slot.Ensure(root, ghostMaterial);
            slot.Place(board, meeting.transform, mesh, ghost, Separation);
            return true;
        }

        /// <summary>The slab carrying <paramref name="id"/>, or null — the shared walk of
        /// <see cref="BoardView.OnTable"/>, so the hint cannot resolve an id to a different slab
        /// than the search that decides the join does.</summary>
        static BoardSheetView Slab(BoardView board, SheetId id)
        {
            return BoardSlabs.ViewOf(board, id);
        }

        /// <summary>
        /// <b>The hot band</b>, and C6.4's glow: the release will fuse. Called by
        /// <c>BoardInteractor</c> whenever its own <c>snapping</c> is true — this class is
        /// <b>told</b> the answer and never recomputes it, which is the half of G7.1's
        /// discipline that survives G7.1. Returns false, having cleared, when the rig cannot
        /// carry the halo.
        ///
        /// <para>It is drawn on both routes into that state: the strict one (the sheet is inside
        /// <c>SheetFit</c>'s tolerance, assist on or off) and the assisted one (a ghost is
        /// showing, so the release will join at it). <see cref="Show"/> calls this and then adds
        /// the slot; there is no separate dress for the assisted case, because the two states
        /// now say the same thing.</para>
        ///
        /// <para><b>It differs from a mere hint in three channels, not in brightness.</b> The
        /// motion stops — stillness against breathing is the strongest cue available. The halo is
        /// tight: <c>SeatedBleed</c> 0.30 board units, about 12 px at <c>BoardZoom</c> 2, against
        /// the ~22 px of the pale halo it replaced. And <c>SeatedColour</c> is hotter and more
        /// saturated than <c>SnapGold</c>. A higher alpha of the same pale halo was rejected:
        /// that is the difference the playtest had already failed to see.</para>
        ///
        /// <para><b>Not gated on <c>assistedSnap</c>.</b> This is C6.4, which every player gets.
        /// Turning the assist off must remove the help, not the report that a release will
        /// land.</para>
        ///
        /// <para><b>The mate is found by <see cref="NearestPair"/> at a fixed range, not by
        /// asking what the fuse chose.</b> The range here is the slab's own long side —
        /// <c>GlowingHintRange</c> deliberately <i>not</i> applied — so a player who has tuned the
        /// hint's reach down, or off, still gets this drawn at full. §8.3's table proves a fitting
        /// mate is always inside it. If no mate can be identified the dragged slab is lit on its
        /// own: the caller has already said the release will land, and saying nothing would be
        /// worse than saying half.</para>
        /// </summary>
        public bool ShowSeated(BoardView board, BoardSheetView dragged)
        {
            if (board == null || dragged == null) { Clear(); return false; }

            Mesh draggedMesh = MeshOf(dragged);
            if (draggedMesh == null) { Clear(); return false; }

            double range = RangeOf(draggedMesh);

            BoardSheetView meeting = null;
            BoardSheetView best = null;

            if (range > 0.0)
            {
                Fill(board, dragged);
                best = NearestPair(board, (float)(range * range), out meeting);
            }

            // The meeting member when there is one, the grabbed slab when there is not — the
            // degenerate case this method already had, and the caller has said a release will
            // land, so lighting nothing would be worse than lighting the sheet in hand.
            BoardSheetView lit = meeting != null ? meeting : dragged;
            Mesh litMesh = lit == dragged ? draggedMesh : MeshOf(lit);
            if (litMesh == null) { Clear(); return false; }

            return Draw(board, lit, best, litMesh,
                        CabinetStyle.SeatedColour, CabinetStyle.SeatedAlphaPeak,
                        CabinetStyle.SeatedBleed);
        }

        /// <summary>G7.3's range before <c>GlowingHintRange</c> scales it: the slab's board size,
        /// off the vertices <see cref="BoardSheetView"/> baked it into. <c>localScale</c> is one on
        /// a board slab by construction (see its class comment), so the mesh bounds ARE the slab's
        /// extent and no transform has to be consulted. Going via
        /// <c>Survey.SheetGroundWidth × UnitsPerMetre</c> would give the same number and would need
        /// the options asset to do it.</summary>
        static double RangeOf(Mesh mesh)
        {
            Vector3 size = mesh.bounds.size;
            return System.Math.Max(size.x, size.z);
        }

        /// <summary>
        /// Fills <see cref="run"/> with what is being dragged: the whole assembly when
        /// <paramref name="dragged"/> belongs to one, otherwise the slab itself.
        ///
        /// <para>Falls back to the one slab when a group resolves to no slabs at all — a store
        /// invariant says it cannot, and an empty run would silently turn the search off rather
        /// than fail where anyone would look.</para>
        /// </summary>
        void Fill(BoardView board, BoardSheetView dragged)
        {
            run.Clear();

            int group = board.GroupIdOf(dragged.Id);
            if (group != 0) BoardSlabs.MembersOf(board, group, run);

            if (run.Count == 0) run.Add(dragged);
        }

        /// <summary>
        /// G7.3's loop over G3.6's pairs: across every member of <see cref="run"/> and every
        /// slab on the table, the nearest related pair within range — the slab that won, and
        /// through <paramref name="meeting"/> the member that met it. Null when nothing
        /// qualifies.
        ///
        /// <para><b>The pair, not the slab, is what is ranked</b>, which is the whole of G3.6
        /// and is exactly what <c>BoardFusing.TryCandidate</c> does on the strict path. Ranking
        /// candidates by their distance to the <i>grabbed</i> slab instead makes the answer a
        /// function of where the player clicked to pick the assembly up, which is not a fact
        /// about the board.</para>
        ///
        /// <para><b>The two cheap tests run before the expensive one, and the answer is
        /// unchanged.</b> G7.3 writes the loop as "reject non-neighbours, keep the nearest, then
        /// compare the winner against the range", which runs
        /// <see cref="SheetKinship.Neighbours"/> — a separating-axis test that allocates two
        /// corner arrays — against every slab on a 48-sheet table, every frame of every drag.
        /// Here the distance is taken first and a pair dropped when it is beyond the range or no
        /// closer than the best so far. <b>Same result, not an approximation:</b> both filters
        /// only remove pairs that cannot be the answer. Ties still keep the first and the range
        /// boundary is still inclusive.</para>
        ///
        /// <para><b>The dragged run itself is skipped</b> — a deviation from G7.3's
        /// "B != dragged". G5.6 drags an assembly rigidly, so a mate keeps a board distance of
        /// roughly zero for the whole gesture while remaining an overlapping same-survey sheet:
        /// a permanent winner. Without this the hint would be on continuously, at full range,
        /// pointing at a sheet that is already joined. Candidates in <i>other</i> groups stay
        /// eligible — that is G7.4's case, and the one this search exists to reach.</para>
        ///
        /// <para>Distance is between slab CENTRES, on X and Z of the board root's local space;
        /// both slabs are children of that root, so no hierarchy is walked. Y is ignored: it is
        /// §3.3's draw-order stack and lifted tiers, facts about painting order rather than about
        /// where anything is on the table.</para>
        /// </summary>
        BoardSheetView NearestPair(BoardView board, float rangeSq, out BoardSheetView meeting)
        {
            meeting = null;

            IReadOnlyList<BoardSheetView> onTable = board.OnTable;
            if (onTable == null) return null;

            BoardSheetView best = null;
            float bestSq = float.MaxValue;

            for (int i = 0; i < onTable.Count; i++)
            {
                BoardSheetView other = onTable[i];

                // Membership by slab and not by group id: `run` IS the set of slabs being
                // dragged, so this covers the loose sheet and the assembly in one test.
                if (other == null || run.Contains(other)) continue;

                Vector3 to = other.transform.localPosition;

                for (int m = 0; m < run.Count; m++)
                {
                    BoardSheetView mine = run[m];

                    Vector3 from = mine.transform.localPosition;
                    float dx = to.x - from.x, dz = to.z - from.z;
                    float sq = dx * dx + dz * dz;

                    if (sq > rangeSq || sq >= bestSq) continue;

                    // The island question, and the only one asked here. Same island, same
                    // office, same year, same scale, neither of them the whole-island sheet, and
                    // their true ground rects overlap (G3.4, G3.5).
                    if (!SheetKinship.Neighbours(mine.Sheet, other.Sheet)) continue;

                    bestSq = sq;
                    best = other;
                    meeting = mine;
                }
            }

            return best;
        }

        /// <summary>
        /// Puts both halos down for one frame. Returns false, having cleared, when the board rig
        /// is not in a state that can carry them — a board mid-close, a slab without a mesh — so
        /// the caller never gets half of a two-slab signal.
        ///
        /// <para><paramref name="target"/> may be null, which is <see cref="ShowSeated"/>'s
        /// degenerate case: the dragged slab is lit alone rather than nothing being lit.</para>
        /// </summary>
        bool Draw(BoardView board, BoardSheetView dragged, BoardSheetView target, Mesh draggedMesh,
                  Color colour, float peak, float bleed)
        {
            Transform root = RootOf(board);
            if (root == null) { Clear(); return false; }

            Mesh targetMesh = target != null ? MeshOf(target) : null;
            if (target != null && targetMesh == null) { Clear(); return false; }

            EnsureMaterials();

            // Rebuilt rather than kept, for BoardInteractor.PlaceOutline's reason: BoardView.Hide
            // destroys the whole rig, taking every child of the root with it, so this cannot
            // assume it survived the last close.
            draggedHalo.Ensure(root, ringMaterials);
            targetHalo.Ensure(root, ringMaterials);

            // One colour write per ring, shared by both halos, before either is placed — so the
            // two are on the same alpha by construction and not by two matching lines (G7.5).
            for (int i = 0; i < CabinetStyle.HaloRings; i++)
            {
                Color c = colour;
                c.a = peak * RingAlphaFraction(i);
                ringMaterials[i].color = c;
            }

            // G7.4: one slab's pose each, never a group's union. See the class comment.
            draggedHalo.Place(dragged.transform, draggedMesh, Separation, bleed);

            if (target != null) targetHalo.Place(target.transform, targetMesh, Separation, bleed);
            else targetHalo.Hide();

            return true;
        }

        /// <summary>
        /// Ring <paramref name="i"/>'s share of the peak alpha, innermost first: 1, 1−1/N, …, 1/N.
        ///
        /// <para>The rings are nested <i>fills</i>, so the band between ring <c>k</c> and
        /// <c>k+1</c> is covered by rings <c>k</c>…<c>N−1</c> and no others: the accumulation does
        /// the falloff. Five rings at a peak of 0.45 composite to 0.81, 0.65, 0.46, 0.25, 0.09
        /// from the paper's edge outward. The last keeps 1/N rather than reaching zero, because a
        /// ring at alpha 0 is a draw call that paints nothing.</para>
        ///
        /// <para><b>Draw order does not matter, before someone tries to sort them.</b> Straight
        /// <i>over</i> compositing is not order-independent in general, but every ring carries the
        /// <i>same</i> colour, so only the accumulated alpha remains — and
        /// <c>1 − Π(1 − aᵢ)</c> is commutative.</para>
        /// </summary>
        static float RingAlphaFraction(int i)
        {
            int rings = CabinetStyle.HaloRings;
            if (rings <= 1) return 1f;

            return 1f - (float)i / rings;
        }

        /// <summary>Hides both halos. Safe to call every frame and safe to call before anything
        /// has been built, which is why every early return above goes through it rather than
        /// testing whether there is something to hide.</summary>
        public void Clear()
        {
            draggedHalo.Hide();
            targetHalo.Hide();
            slot.Hide();
        }

        /// <summary>Destroys both halos and the shared materials. Called from the driver's
        /// <c>OnDestroy</c>; the materials are <c>DontSave</c> and would otherwise outlive the
        /// domain they were made in.</summary>
        public void Dispose()
        {
            draggedHalo.Dispose();
            targetHalo.Dispose();
            slot.Dispose();

            Discard(ghostMaterial);
            ghostMaterial = null;

            if (ringMaterials != null)
            {
                for (int i = 0; i < ringMaterials.Length; i++) Discard(ringMaterials[i]);
                ringMaterials = null;
            }
        }

        void EnsureGhostMaterial()
        {
            if (ghostMaterial != null) return;

            // Unlit, for §3.4's reason: the board is independent of the room's lighting and of
            // where its root sits, and a lit gold would go black 500 units under the floor.
            ghostMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ghostMaterial.name = "M_BoardSnapGhost";
            ghostMaterial.hideFlags = HideFlags.DontSave;

            // Not optional and not a detail: URP/Unlit is OPAQUE out of the box and discards
            // color.a outright, so the ghost would paint at full strength and read as four
            // solid gold bars — a slot that looks like an object. This has been shipped wrong
            // once already, on the halo.
            MakeBlended(ghostMaterial);
        }

        void EnsureMaterials()
        {
            if (ringMaterials != null && ringMaterials.Length == CabinetStyle.HaloRings) return;

            if (ringMaterials != null)
                for (int i = 0; i < ringMaterials.Length; i++) Discard(ringMaterials[i]);

            ringMaterials = new Material[CabinetStyle.HaloRings];

            for (int i = 0; i < ringMaterials.Length; i++)
            {
                // Unlit, for §3.4's reason: the board is independent of the room's lighting and
                // of where its root sits, and a lit gold would go black 500 units under the
                // floor.
                Material m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.name = "M_BoardSnapHalo_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                m.hideFlags = HideFlags.DontSave;

                // Not optional and not a detail: URP/Unlit is OPAQUE out of the box and discards
                // color.a outright, so every ring would paint at full strength and the halo would
                // be one flat gold rectangle. This has been shipped wrong once already.
                MakeBlended(m);

                ringMaterials[i] = m;
            }
        }

        /// <summary>
        /// Switches a URP/Unlit material to straight alpha blending, which is what a varying
        /// alpha needs to mean anything.
        ///
        /// <para><b>Public because the caller needs it too.</b> A URP/Unlit material is Opaque
        /// out of <c>new Material(Shader.Find(...))</c> and ignores <c>color.a</c>, so a correct
        /// alpha renders as a slab that is simply gold — a silent failure that looks implemented.
        /// The recipe lives in one named place rather than in several as six unexplained property
        /// writes.</para>
        ///
        /// <para>The keyword and the render queue matter as much as the blend factors: URP
        /// compiles the transparent path behind <c>_SURFACE_TYPE_TRANSPARENT</c>, and a queue at
        /// 2000 would draw the quad in the opaque pass, before the backdrop, with nothing to
        /// blend against. Depth writing off has a second job here: the halo's rings then cannot
        /// occlude or z-fight each other, so they accumulate. The slab above them is opaque and
        /// does write depth, which keeps the halo's middle hidden under the paper.</para>
        /// </summary>
        public static void MakeBlended(Material material)
        {
            if (material == null) return;

            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);                 // URP: 0 opaque, 1 transparent
            material.SetFloat("_Blend", 0f);                   // URP: 0 alpha, 1 premultiplied
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static Transform RootOf(BoardView board)
        {
            // Through the camera's parent, as BoardInteractor.BoardRoot does and for its reason:
            // BoardView does not expose the root on purpose — it sits hundreds of units from the
            // room and handing it out invites someone to parent something to it. Both the camera
            // and every slab are children of it, so the camera's parent IS the root; and if that
            // ever stops being true this returns null and the halo goes dark rather than placing
            // quads in the wrong space.
            Camera cam = board.BoardCamera;
            return cam != null ? cam.transform.parent : null;
        }

        static Mesh MeshOf(BoardSheetView view)
        {
            MeshFilter filter = view.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>Destroy is illegal in edit mode, and the board rig is routinely built and
        /// torn down there by the bench.</summary>
        static void Discard(UnityEngine.Object thing)
        {
            if (thing == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(thing);
            else UnityEngine.Object.DestroyImmediate(thing);
        }

        /// <summary>
        /// One slab's halo: <c>CabinetStyle.HaloRings</c> concentric quads under one piece of
        /// paper, all sharing that slab's mesh and the caller's per-ring materials.
        ///
        /// <para><b>The scale is solved per axis from a width in board units</b>, which is why
        /// this is not a scale constant. A slab's mesh bounds are its board size
        /// (<c>localScale</c> is one on a slab by construction), so a bleed of <c>b</c> per side
        /// wants <c>(size + 2b) / size</c> on X and Z independently. A uniform scale cannot: 1.30
        /// on island 0's Land Survey slab is 1.93 units of bleed on the short side, 2.85 on the
        /// long, and 0.41 on an Antiquarian detail sheet. Y stays at one — the shared mesh is
        /// flat.</para>
        ///
        /// <para><b>Draw order.</b> §3.3 stacks sheets <c>SheetSeparation</c> apart in Y, so each
        /// slab owns the band between its own Y and one separation below it, and anything put
        /// down here must fit inside that band or surface through the sheet stacked below. The
        /// rings take <c>HaloDropNear</c>…<c>HaloDropFar</c> of a separation, innermost nearest
        /// the paper — 0.30 to 0.70, the middle 40% of the slot. <b>The spacing does not buy
        /// layering:</b> every ring has <c>_ZWrite</c> off, so none occludes or z-fights another
        /// and their order is irrelevant (see <see cref="RingAlphaFraction"/>). Y only has to be
        /// reliably <i>below</i> the opaque slab, whose depth writes hide the halo's middle;
        /// coplanar, the depth test ties and the halo strobes over the map.</para>
        /// </summary>
        sealed class Halo
        {
            readonly string name;

            GameObject[] rings;
            MeshFilter[] filters;
            MeshRenderer[] renderers;

            public Halo(string name)
            {
                this.name = name;
            }

            /// <summary>Builds the rings if there are none, if the ring count has changed, or if
            /// they are no longer children of the current board root — <c>BoardView.Hide</c>
            /// destroys the whole rig, so nothing here may assume it survived a close.</summary>
            public void Ensure(Transform root, Material[] materials)
            {
                int count = CabinetStyle.HaloRings;

                bool intact = rings != null && rings.Length == count;
                if (intact)
                    for (int i = 0; i < count; i++)
                        if (rings[i] == null || rings[i].transform.parent != root) { intact = false; break; }

                if (intact) return;

                Dispose();

                rings = new GameObject[count];
                filters = new MeshFilter[count];
                renderers = new MeshRenderer[count];

                // The board camera's culling mask is the Table layer and nothing else (C5.1), so
                // a quad on the default layer is built, positioned, enabled — and invisible.
                int layer = LayerMask.NameToLayer(TableLayerName);

                for (int i = 0; i < count; i++)
                {
                    GameObject go = new GameObject(name + "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    go.transform.SetParent(root, false);
                    if (layer >= 0) go.layer = layer;

                    filters[i] = go.AddComponent<MeshFilter>();
                    renderers[i] = go.AddComponent<MeshRenderer>();
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderers[i].receiveShadows = false;
                    renderers[i].sharedMaterial = materials[i];
                    renderers[i].enabled = false;

                    rings[i] = go;
                }
            }

            /// <summary>Places every ring on <paramref name="slab"/>'s pose for this frame.
            /// Allocates nothing: the mesh is borrowed, the bounds are a struct, and the vectors
            /// are stack values.</summary>
            public void Place(Transform slab, Mesh mesh, float separation, float bleed)
            {
                if (rings == null || mesh == null) return;

                Vector3 size = mesh.bounds.size;
                if (!(size.x > 0f) || !(size.z > 0f)) { Hide(); return; }

                // The guard rail of CabinetStyle.HaloBleedMaxFraction: a halo may not be so wide
                // relative to the paper that it stops reading as belonging to it. It does not
                // bind on any of island 0's three surveys.
                float cap = CabinetStyle.HaloBleedMaxFraction * 0.5f * Mathf.Min(size.x, size.z);
                float outer = Mathf.Min(bleed, cap);

                int count = rings.Length;
                Vector3 p = slab.localPosition;
                Quaternion r = slab.localRotation;

                for (int i = 0; i < count; i++)
                {
                    // Ring i reaches (i+1)/N of the way out, so the innermost band starts at the
                    // paper's own edge and no ring is wasted drawing under the slab.
                    float b = outer * (i + 1) / count;

                    float t = count == 1 ? 0f : (float)i / (count - 1);
                    float drop = Mathf.Lerp(CabinetStyle.HaloDropNear, CabinetStyle.HaloDropFar, t);

                    filters[i].sharedMesh = mesh;
                    renderers[i].enabled = true;

                    Transform tr = rings[i].transform;
                    tr.localPosition = new Vector3(p.x, p.y - separation * drop, p.z);
                    tr.localRotation = r;
                    tr.localScale = new Vector3((size.x + 2f * b) / size.x, 1f,
                                                (size.z + 2f * b) / size.z);
                }
            }

            public void Hide()
            {
                if (rings == null) return;

                for (int i = 0; i < rings.Length; i++)
                {
                    if (renderers[i] != null) renderers[i].enabled = false;

                    // Dropped, not kept: the mesh belongs to a slab that may be destroyed this
                    // frame, and BoardSheetView destroys it in OnDestroy.
                    if (filters[i] != null) filters[i].sharedMesh = null;
                }
            }

            public void Dispose()
            {
                if (rings == null) return;

                for (int i = 0; i < rings.Length; i++)
                {
                    if (filters[i] != null) filters[i].sharedMesh = null;
                    Discard(rings[i]);
                }

                rings = null;
                filters = null;
                renderers = null;
            }
        }

        /// <summary>
        /// The ghost: four thin bars laid on the edges of the rectangle the dragged sheet would
        /// occupy if it were released now. An <b>empty</b> rectangle — a slot on the table, not
        /// a sheet on it.
        ///
        /// <para><b>Four bars rather than a filled quad.</b> A single fill has to be dark enough
        /// to be seen over the mounting sheet, and anything dark enough to be seen stops looking
        /// empty — it reads as a piece of paper with no map on it, which the board does not have
        /// and the player would have to learn to ignore. An outer quad with an inner one painted
        /// over its middle needs that inner quad opaque, which is the same object again. Four bars
        /// have no middle to hide. The two long bars extend by the line width at each end, so the
        /// rectangle closes rather than showing four corner gaps.</para>
        ///
        /// <para><b>Bars straddle the true edge, half in and half out.</b> The ghost is drawn AT
        /// the pose, so the line's centre is the sheet's own boundary and the bleed either side is
        /// <c>GhostLineWidth / 2</c> — 0.10 board units, 3.93 px at <c>BoardZoom</c> 2. Wholly
        /// inside would shrink the box and wholly outside would inflate it, at the one moment the
        /// player is comparing the two rectangles by eye.</para>
        ///
        /// <para>The scale is solved per axis from a width in board units, exactly as
        /// <see cref="Halo.Place"/> does and for the same reason. Y stays at one.</para>
        ///
        /// <para><b>Draw order.</b> All four bars sit at <c>GhostDrop</c> of a separation under
        /// the DRAGGED slab — inside that slab's slot in §3.3's stack, below the halo's 0.30–0.70
        /// band. <c>BoardInteractor.Lift</c> puts a dragged sheet two separations clear of the
        /// resting stack, so 0.85 of one separation down is still above every sheet on the table:
        /// the ghost cannot be buried by the paper it is pointing between, which is the failure
        /// mode of "put it at the target's tier". Being under the dragged slab means the arriving
        /// paper covers the slot, so it stops being drawn when it stops being useful.
        /// <c>_ZWrite</c> is off (see <see cref="MakeBlended"/>), so no bar occludes or z-fights
        /// another.</para>
        ///
        /// <para><b>The one case where a bar draws over other paper.</b> When a GROUP is dragged
        /// the ghost sits under the selected member, and members lifted below it in G5.6's run sit
        /// 0.15 of a separation lower, so an overlap draws on top of them. That is the assembly's
        /// own paper, at 0.80 alpha of a pale gold on a 7.87 px line, and preferable to hiding the
        /// slot under the thing moving toward it.</para>
        ///
        /// <para><b>The alpha does not ramp with distance</b>, which is a measurement rather than
        /// a preference. At the correct pose two Land Survey sheets are a full lattice step apart
        /// — 10.28, 15.22 or 18.37 units — so a ramp keyed on centre distance would be dimmest
        /// exactly where the answer is and brightest when one sheet is dragged on top of another.
        /// Centre distance measures <i>overlap</i>; this is an <i>adjacency</i> problem.</para>
        ///
        /// <para>Allocates nothing per frame.</para>
        /// </summary>
        sealed class Slot
        {
            /// <summary>Two bars across the sheet's long-edge pair and two down its short-edge
            /// pair. Four is the shape of a rectangle and is not a tuning.</summary>
            const int Bars = 4;

            readonly string name;

            GameObject[] bars;
            MeshFilter[] filters;
            MeshRenderer[] renderers;

            public Slot(string name)
            {
                this.name = name;
            }

            /// <summary>Builds the bars if there are none, or if they are no longer children of
            /// the current board root — <c>BoardView.Hide</c> destroys the whole rig, so nothing
            /// here may assume it survived a close.</summary>
            public void Ensure(Transform root, Material material)
            {
                bool intact = bars != null;
                if (intact)
                    for (int i = 0; i < Bars; i++)
                        if (bars[i] == null || bars[i].transform.parent != root) { intact = false; break; }

                if (intact) return;

                Dispose();

                bars = new GameObject[Bars];
                filters = new MeshFilter[Bars];
                renderers = new MeshRenderer[Bars];

                // The board camera's culling mask is the Table layer and nothing else (C5.1), so
                // a quad on the default layer is built, positioned, enabled — and invisible.
                int layer = LayerMask.NameToLayer(TableLayerName);

                for (int i = 0; i < Bars; i++)
                {
                    GameObject go = new GameObject(name + "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    go.transform.SetParent(root, false);
                    if (layer >= 0) go.layer = layer;

                    filters[i] = go.AddComponent<MeshFilter>();
                    renderers[i] = go.AddComponent<MeshRenderer>();
                    renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderers[i].receiveShadows = false;
                    renderers[i].sharedMaterial = material;
                    renderers[i].enabled = false;

                    bars[i] = go;
                }
            }

            /// <summary>Lays the four bars on <paramref name="ghost"/>'s pose for this frame.
            /// <paramref name="slab"/> is the dragged slab, read for its Y only — the ghost's X
            /// and Z come from the ghost's ground pose through <c>BoardSpace.ToBoard</c>, and
            /// its yaw from the same negated convention <c>BoardInteractor.SetPose</c> uses
            /// (F-S1.2: ground +X toward +Y is a Unity yaw the other way; do not "fix" one half
            /// of that pair).</summary>
            public void Place(BoardView board, Transform slab, Mesh mesh, Ghost ghost,
                              float separation)
            {
                if (bars == null || mesh == null) return;

                Vector3 size = mesh.bounds.size;
                if (!(size.x > 0f) || !(size.z > 0f)) { Hide(); return; }

                // The guard rail of CabinetStyle.GhostLineMaxFraction: four bars may not be so
                // thick, relative to the paper, that they meet in the middle. It does not bind
                // on any survey island 0 can produce a ghost for.
                float cap = CabinetStyle.GhostLineMaxFraction * 0.5f * Mathf.Min(size.x, size.z);
                float w = Mathf.Min(CabinetStyle.GhostLineWidth, cap);

                V2 b = board.Space.ToBoard(ghost.Position);
                float y = slab.localPosition.y - separation * CabinetStyle.GhostDrop;

                Vector3 centre = new Vector3((float)b.X, y, (float)b.Y);
                Quaternion rot = Quaternion.Euler(0f, -(float)ghost.RotationDeg, 0f);

                // The two Z bars run the full width plus a line width at each end, so the
                // corners close; the two X bars then span only the height between them. The
                // four corner squares are therefore covered TWICE, and with _ZWrite off that
                // composites: at GhostAlphaPeak 0.80 a corner reads 0.96 against an edge's
                // 0.80. That is left alone rather than masked — a rectangle with slightly
                // heavier corners is how a surveyor's slot would be ticked, and hiding it would
                // cost either a fifth quad or four exact-length bars that leave hairline gaps
                // at every corner instead.
                float longScaleX = (size.x + 2f * w) / size.x;
                float thinScaleZ = w / size.z;
                float thinScaleX = w / size.x;

                float halfX = size.x * 0.5f, halfZ = size.z * 0.5f;

                Bar(0, centre, rot, new Vector3(0f, 0f,  halfZ), mesh,
                    new Vector3(longScaleX, 1f, thinScaleZ));
                Bar(1, centre, rot, new Vector3(0f, 0f, -halfZ), mesh,
                    new Vector3(longScaleX, 1f, thinScaleZ));
                Bar(2, centre, rot, new Vector3( halfX, 0f, 0f), mesh,
                    new Vector3(thinScaleX, 1f, 1f));
                Bar(3, centre, rot, new Vector3(-halfX, 0f, 0f), mesh,
                    new Vector3(thinScaleX, 1f, 1f));
            }

            /// <summary><paramref name="offset"/> is in the ghost's own turned space, so it is
            /// rotated before it is added — a bar offset in board space would slide off the edge
            /// as soon as the sheet was not axis-aligned, which is every Hydrographic strip
            /// (D-H2).</summary>
            void Bar(int i, Vector3 centre, Quaternion rot, Vector3 offset, Mesh mesh,
                     Vector3 scale)
            {
                filters[i].sharedMesh = mesh;
                renderers[i].enabled = true;

                Transform t = bars[i].transform;
                t.localPosition = centre + rot * offset;
                t.localRotation = rot;
                t.localScale = scale;
            }

            public void Hide()
            {
                if (bars == null) return;

                for (int i = 0; i < Bars; i++)
                {
                    if (renderers[i] != null) renderers[i].enabled = false;

                    // Dropped, not kept: the mesh belongs to a slab that may be destroyed this
                    // frame, and BoardSheetView destroys it in OnDestroy.
                    if (filters[i] != null) filters[i].sharedMesh = null;
                }
            }

            public void Dispose()
            {
                if (bars == null) return;

                for (int i = 0; i < Bars; i++)
                {
                    if (filters[i] != null) filters[i].sharedMesh = null;
                    Discard(bars[i]);
                }

                bars = null;
                filters = null;
                renderers = null;
            }
        }
    }
}
