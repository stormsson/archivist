using System;
using System.Collections.Generic;
using System.Globalization;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Analysis
{
    /// <summary>
    /// How the two sides of a shared border are compared for A3 (§13.3 — the §6.2 lattice rule:
    /// adjacent rects contour independently and must still agree along the border they share).
    ///
    /// <para>The two existing A3s did NOT compare the same way, which is the reason this is one
    /// file. The Unity test sorted both sides and required <c>a.Count == b.Count</c> before
    /// comparing index by index; the headless harness took, for each vertex on the left, the
    /// nearest vertex on the right, and never looked at the counts at all. <b>The harness is the
    /// laxer of the two on both axes.</b> An extra crossing on one side fails the test and passes
    /// the harness; so does a left vertex that happens to sit near a right vertex already claimed
    /// by another.</para>
    ///
    /// <para>Both forms are kept and both are selected explicitly, so the difference between two
    /// call sites is visible in the call rather than buried in a copied loop.</para>
    /// </summary>
    public static class ContourSeam
    {
        /// <summary>How the two sides are matched up. See the type doc for which is stricter.</summary>
        public enum Matching
        {
            /// <summary>For each vertex on side A, the smallest offset to ANY vertex on side B.
            /// Many-to-one matches are allowed. The laxer form; what the harness has always done.</summary>
            Nearest = 0,

            /// <summary>Sort both sides and compare index by index — a one-to-one pairing.
            /// The stricter form; what the Unity test has always done.</summary>
            SortedPairwise = 1
        }

        /// <summary>The outcome of a border comparison: the verdict plus the numbers a caller
        /// needs to report, so neither the harness nor a test has to recompute them.</summary>
        public struct Comparison
        {
            /// <summary>True when every vertex on side A matched within tolerance (and, when
            /// asked for, the counts agreed).</summary>
            public bool Agree;

            /// <summary>Vertices found on each side of the border.</summary>
            public int CountA, CountB;

            /// <summary>Side-A vertices with no match within tolerance.</summary>
            public int Unmatched;

            /// <summary>Largest matched offset seen, in metres.</summary>
            public double Worst;

            /// <summary>Null when <see cref="Agree"/>; otherwise what went wrong.</summary>
            public string Why;
        }

        /// <summary>One prepared seam test: the border and the vertices each side put on it.</summary>
        public struct Border
        {
            /// <summary>Ground X of the shared border.</summary>
            public double X;

            /// <summary>Agreement tolerance, in metres: <c>1e-6</c> of the contour cell.</summary>
            public double Tol;

            /// <summary>Vertices the left and right rects placed on the border.</summary>
            public List<V2> Left, Right;

            /// <summary>Nothing crossed the border on this seed, so the test is inconclusive
            /// rather than passing. Both call sites bail out on this.</summary>
            public bool Inconclusive
            {
                get { return (Left == null || Left.Count == 0) && (Right == null || Right.Count == 0); }
            }
        }

        /// <summary>
        /// The shared A3 fixture: two rects meeting at the vertical line through the island's
        /// land-bounds centre, each contoured independently at the LOD for
        /// <paramref name="scaleDenominator"/>, and the vertices each landed on that line.
        ///
        /// <para>This preamble — the LOD, the tolerance, the two square rects — was duplicated
        /// verbatim between the harness and the Unity test. Two copies of a fixture drift: they
        /// already disagreed about how to compare the result, and nothing stopped them
        /// disagreeing about what they were comparing.</para>
        /// </summary>
        public static Border AcrossLandCentre(Island isl, int scaleDenominator, double halfExtent)
        {
            int lod = Contours.LodForScale(scaleDenominator);
            double cell = Contours.CellSizeForLod(lod);
            double tol = 1e-6 * cell;

            V2 c = isl.LandBounds.Centre;
            Rect2 left  = new Rect2(c.X - halfExtent, c.Y - halfExtent, c.X, c.Y + halfExtent);
            Rect2 right = new Rect2(c.X, c.Y - halfExtent, c.X + halfExtent, c.Y + halfExtent);

            var r = new Border();
            r.X = c.X;
            r.Tol = tol;
            r.Left  = BorderVertices(Contours.Extract(isl.Field, left,  cell, isl.Params.SeaLevel), c.X, tol);
            r.Right = BorderVertices(Contours.Extract(isl.Field, right, cell, isl.Params.SeaLevel), c.X, tol);
            return r;
        }

        /// <summary>
        /// The vertices of <paramref name="lines"/> lying on the vertical line <c>x</c>, within
        /// <paramref name="tol"/>.
        ///
        /// <para>Returned in extraction order, NOT sorted — <see cref="Compare"/> sorts its own
        /// copies when it needs to, so the caller can still report a count in the order the
        /// contourer produced.</para>
        /// </summary>
        public static List<V2> BorderVertices(IReadOnlyList<Polyline> lines, double x, double tol)
        {
            var found = new List<V2>();
            if (lines == null) return found;
            for (int i = 0; i < lines.Count; i++)
                for (int v = 0; v < lines[i].Count; v++)
                    if (Math.Abs(lines[i][v].X - x) <= tol) found.Add(lines[i][v]);
            return found;
        }

        /// <summary>
        /// Compares the Y ordinates of two sides of a shared vertical border.
        ///
        /// <para><paramref name="requireEqualCount"/> is the check the harness never had: without
        /// it, a side that emits an EXTRA crossing still passes as long as every vertex on side A
        /// found a partner. It is a parameter rather than a default so that the harness's laxity
        /// is a decision on record instead of an omission.</para>
        /// </summary>
        /// <param name="a">Border vertices from the left rect.</param>
        /// <param name="b">Border vertices from the right rect.</param>
        /// <param name="tol">Agreement tolerance in metres.</param>
        /// <param name="matching">Which pairing rule to use.</param>
        /// <param name="requireEqualCount">Fail immediately when the two sides differ in count.</param>
        public static Comparison Compare(IReadOnlyList<V2> a, IReadOnlyList<V2> b, double tol,
                                         Matching matching, bool requireEqualCount)
        {
            var r = new Comparison();
            r.CountA = a == null ? 0 : a.Count;
            r.CountB = b == null ? 0 : b.Count;
            r.Agree = true;

            if (requireEqualCount && r.CountA != r.CountB)
            {
                r.Agree = false;
                r.Why = "different number of border crossings: " + r.CountA + " vs " + r.CountB;
                return r;
            }

            if (matching == Matching.SortedPairwise)
            {
                var ya = Ys(a); var yb = Ys(b);
                ya.Sort(); yb.Sort();
                int n = Math.Min(ya.Count, yb.Count);
                for (int i = 0; i < n; i++)
                {
                    double d = Math.Abs(ya[i] - yb[i]);
                    if (d > tol)
                    {
                        r.Unmatched++;
                        if (r.Why == null) r.Why = "border vertex " + i + " disagrees by "
                                                 + d.ToString("G17", CultureInfo.InvariantCulture) + " m";
                    }
                    if (d > r.Worst) r.Worst = d;
                }
            }
            else
            {
                for (int i = 0; i < r.CountA; i++)
                {
                    double best = double.MaxValue;
                    for (int j = 0; j < r.CountB; j++)
                        best = Math.Min(best, Math.Abs(a[i].Y - b[j].Y));
                    if (best > tol) r.Unmatched++;
                    // No side-B vertex at all leaves `best` at MaxValue; clamp so the reported
                    // worst stays a number a reader can act on.
                    if (best < double.MaxValue) r.Worst = Math.Max(r.Worst, Math.Min(best, 1e9));
                }
            }

            if (r.Unmatched > 0)
            {
                r.Agree = false;
                if (r.Why == null)
                    r.Why = r.Unmatched + "/" + r.CountA + " border vertices unmatched";
            }
            return r;
        }

        static List<double> Ys(IReadOnlyList<V2> v)
        {
            var ys = new List<double>();
            if (v == null) return ys;
            for (int i = 0; i < v.Count; i++) ys.Add(v[i].Y);
            return ys;
        }
    }
}
