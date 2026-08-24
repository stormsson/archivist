using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Which two sheets are allowed to join, and which two are edge to edge (G3.4, G3.5).
    ///
    /// <para><b>Same survey only</b> (G1.2). The difference between two offices' sheets of one
    /// hillside is the game (CLAUDE.md), and fusing them erases it. Mechanically too: offices
    /// work at different scales and rotations, so co-located sheets would satisfy a relative fit
    /// whenever roughly on top of one another and one group would swallow the board.</para>
    ///
    /// <para><b>Neither test may touch the island.</b> Everything read here is already on the
    /// <see cref="Sheet"/> the caller is holding, so a candidate is rejected without a
    /// regeneration, and G5.1's loop over every slab on the table costs nothing. That is also
    /// why <see cref="Fusable"/> compares the survey's identifying fields rather than the
    /// struct: <see cref="SurveySpec"/> has no equality operator, and adding one would invite
    /// comparing <c>RotationDeg</c> and <c>OverlapFraction</c> too — derived values that say
    /// nothing extra about which survey a sheet came from, and, for the coast walk, values the
    /// sheets themselves do not honour (D-H2).</para>
    ///
    /// <para><b>The whole-island sheet never fuses</b> (G-A5). R2.2a makes it a survey of one,
    /// so it has no peer to fuse with and the refusal costs nothing; it is written out rather
    /// than left to fall out of the member count so that a caller comparing a sheet with itself
    /// cannot manufacture the one group that must not exist. This is consistent with its
    /// reservation out of the crate draw.</para>
    ///
    /// <para><b>Detail sheets are deliberately NOT refused here.</b> Four Antiquarian sheets of
    /// one island share an office, year and scale, so they are fusable, and §6 is explicit that
    /// they still never group: POI suppression keeps them apart, so two 275 m sheets essentially
    /// never overlap. Adding <c>!IsDetail</c> would look like a helpful tightening and would
    /// quietly replace that measured finding — held as G-A6 so a change to the cutter is
    /// noticed — with an assertion.</para>
    ///
    /// <para><b>G3.5's specified overlap test is wrong and is not used.</b> G3.5 says
    /// <c>A.FrameRect intersects B.FrameRect</c>, on the grounds that all sheets of one survey
    /// share one rotation — true only of the lattice offices. <b>Frame space is per sheet
    /// whenever rotation is per sheet:</b> <see cref="Sheet.FrameRect"/> rotates the centre
    /// through <c>-RotationDeg</c>, so two sheets cut at different angles give rects in two
    /// <i>different</i> spaces, and intersecting them compares coordinates that do not share an
    /// orientation. It fails in <b>both</b> directions, and would break the Hydrographic coast
    /// walk (D-H2) and the Antiquarian detail sheets — half the offices — while Land Survey and
    /// Garrison worked perfectly, which is what makes it worth writing down: invisible on the
    /// two surveys a developer reaches for first.</para>
    ///
    /// <para><b>So the test is a separating-axis test on the ground-space rects</b>, exact for
    /// all four offices and indifferent to whether rotations agree, costing two corner arrays
    /// and at most four projections on pairs that already passed <see cref="Fusable"/>. The
    /// cheap frame-space test is not kept as a lattice fast path: two overlap tests means two
    /// answers to one question, and the sometimes-right one is what gets reached for.</para>
    ///
    /// <para>Engine-free, like <see cref="SheetFit"/> and <see cref="BoardFrame"/>, so G-A4
    /// through G-A6 run in the headless harness.</para>
    /// </summary>
    public static class SheetKinship
    {
        /// <summary>
        /// G3.4: may these two ever join? Same island, same office, same year, same scale, and
        /// neither of them the whole-island sheet — which is R2.2's "one island, one office,
        /// one year, one scale" read back, i.e. the same survey.
        ///
        /// <para>Consulted on every release (G5.1) and, through <see cref="Neighbours"/>, every
        /// frame of a drag with the assist on (G7.3), so it stays comparisons on fields the
        /// caller already has in hand.</para>
        /// </summary>
        public static bool Fusable(Sheet a, Sheet b)
        {
            SurveySpec sa = a.Survey, sb = b.Survey;

            if (sa.IsWholeIsland || sb.IsWholeIsland) return false;

            return sa.IslandSeed == sb.IslandSeed
                && sa.Office == sb.Office
                && sa.Year == sb.Year
                && sa.Scale.Denominator == sb.Scale.Denominator;
        }

        /// <summary>
        /// G3.5: fusable, and the two sheets' true ground rects overlap.
        ///
        /// <para><b>Only the assist uses this</b> (§7). The fuse rule never does: a player who
        /// correctly poses two same-survey sheets four lattice steps apart gets a group with a
        /// hole in it, and that is allowed. The asymmetry is deliberate — the <i>rule</i> is
        /// about the survey, the <i>hint</i> is about edges.</para>
        ///
        /// <para><see cref="Fusable"/> is the first gate and stays cheap comparisons on struct
        /// fields, so the geometry below is only reached by pairs from one survey — at most a
        /// few hundred ordered pairs on a whole island, and far fewer on a table.</para>
        ///
        /// <para><b>Ground space, not frame space, and not <see cref="Sheet.GroundBounds"/>.</b>
        /// See the class comment for what the frame-space test gets wrong. <c>GroundBounds</c> is
        /// the AABB <i>of</i> the rotated rect, so at any rotation off a multiple of 90° it
        /// carries four corner wedges the sheet does not cover and strictly over-reports. The
        /// rotated rects themselves are the only honest answer.</para>
        /// </summary>
        public static bool Neighbours(Sheet a, Sheet b)
        {
            if (!Fusable(a, b)) return false;
            return QuadsOverlap(a.GroundCorners(), b.GroundCorners());
        }

        /// <summary>
        /// Separating-axis test for two convex quads: they overlap iff no axis separates them.
        ///
        /// <para>Four candidate axes, not eight. For a general convex polygon pair the axes are
        /// every edge normal of both; a rectangle's opposite edges are parallel, so two of its
        /// four normals are duplicates and one pair of adjacent edges gives both distinct
        /// directions. And a rectangle's edge <i>direction</i> is its adjacent edge's
        /// <i>normal</i>, so the edge vectors can be projected on directly — no perpendicular
        /// is constructed, which is one fewer place to write <c>(-y, x)</c> as <c>(y, -x)</c>.
        /// The axes are left unnormalised: scaling an axis scales both intervals equally and
        /// the comparison is unaffected, so this costs no square root and no division.</para>
        ///
        /// <para><b>Touching counts as overlapping.</b> The interval test is strict
        /// (<c>maxA &lt; minB</c> separates), so a shared edge answers yes. Lattice sheets are cut
        /// to overlap by 20% (C1.2), so an exactly-abutting pair is a degenerate case not worth
        /// being fussy about; and it matches
        /// <see cref="Archivist.Generation.Geometry.Rect2.Intersects"/>, because two overlap
        /// predicates disagreeing on the boundary is a bug waiting for whoever compares
        /// them.</para>
        ///
        /// <para><see cref="Sheet.GroundCorners"/> is the one place the corner order and rotation
        /// sense are written down, so it is called rather than reproduced. It allocates a
        /// four-element array per sheet per call — a few dozen small arrays a frame with the
        /// assist on, well inside what the drag loop already does. If a profile objects, the fix
        /// is the centre-difference form of the same test, not caching corners on a slab, which
        /// would put a second copy of the truth beside the sheet that owns it.</para>
        /// </summary>
        static bool QuadsOverlap(V2[] a, V2[] b)
        {
            return !(SeparatedOn(a, b, a[1] - a[0])
                  || SeparatedOn(a, b, a[3] - a[0])
                  || SeparatedOn(a, b, b[1] - b[0])
                  || SeparatedOn(a, b, b[3] - b[0]));
        }

        /// <summary>True if the two quads' projections onto <paramref name="axis"/> are disjoint.</summary>
        static bool SeparatedOn(V2[] a, V2[] b, V2 axis)
        {
            double aMin, aMax, bMin, bMax;
            Project(a, axis, out aMin, out aMax);
            Project(b, axis, out bMin, out bMax);
            return aMax < bMin || bMax < aMin;
        }

        static void Project(V2[] quad, V2 axis, out double min, out double max)
        {
            min = max = V2.Dot(quad[0], axis);
            for (int i = 1; i < quad.Length; i++)
            {
                double d = V2.Dot(quad[i], axis);
                if (d < min) min = d;
                else if (d > max) max = d;
            }
        }
    }
}
