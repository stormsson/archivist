using System;
using System.Collections.Generic;
using Archivist.Generation.Field;

namespace Archivist.Generation.Geometry
{
    /// <summary>
    /// §6.1 marching squares over an <see cref="IHeightField"/>, and the §6.2 lattice rule
    /// that makes two adjacent sheets agree along their shared border.
    ///
    /// Contouring is a query, never a build step (§3): nothing here is cached.
    /// </summary>
    public static class Contours
    {
        /// <summary>One marching-squares segment, directed so that land lies on its left.</summary>
        readonly struct Seg
        {
            public readonly V2 A;
            public readonly V2 B;
            public Seg(V2 a, V2 b) { A = a; B = b; }
        }

        /// <summary>Spatial-hash bucket key. Lookup only — never enumerated (§4.1).</summary>
        readonly struct GridKey : IEquatable<GridKey>
        {
            public readonly long X;
            public readonly long Y;
            public GridKey(long x, long y) { X = x; Y = y; }
            public bool Equals(GridKey o) { return X == o.X && Y == o.Y; }
            public override bool Equals(object o) { return o is GridKey g && Equals(g); }
            public override int GetHashCode()
            {
                unchecked { return (int)(X * 73856093L) ^ (int)(Y * 19349663L); }
            }
        }

        // ---------------------------------------------------------------- LOD

        /// <summary>
        /// §6.2: cellSize = BaseCell / 2^lod. Computed by repeated halving — every value is a
        /// power of two, so this is exact and free of Math.Pow's last-ulp exposure (§4.4).
        /// </summary>
        public static double CellSizeForLod(int lod)
        {
            int l = lod;
            if (l < 0) l = 0;
            if (l > Tuning.MaxLod) l = Tuning.MaxLod;
            double cell = Tuning.BaseCell;
            for (int i = 0; i < l; i++) cell *= 0.5;
            return cell;
        }

        /// <summary>
        /// §6.2: targetGroundCell = PaperDetailMm / 1000 * scaleDenominator,
        /// lod = clamp(ceil(log2(BaseCell / targetGroundCell)), 0, MaxPaperContourLod).
        ///
        /// ceil(log2(a/b)) is the smallest L with a / 2^L &lt;= b, so this halves BaseCell in an
        /// integer loop instead of trusting the last ulp of a transcendental that feeds an
        /// int branch (§4.4).
        ///
        /// <para>The result is capped at <see cref="Tuning.MaxPaperContourLod"/>, because the
        /// paper rule alone asks for detail the FIELD does not have — see that constant for the
        /// measurements. Uncapped, a 1:2500 sheet asked for lod 7 and spent 2.5 s sampling 2.58 M
        /// cells of which 0.15% straddled the coastline, to move the line by 0.03 m.</para>
        ///
        /// <para>The cap does not weaken §6.2's seam guarantee: it is a function of the
        /// denominator alone, so two rects of the same survey still resolve to the same LOD and
        /// still sample identical lattice points along a shared border (A3).</para>
        ///
        /// <para><b>This is the PAPER path only</b> — the Editor panes, the SVG export and the
        /// acceptance checks. The raster renderer picks its LOD from the pixel instead, via
        /// <c>RenderLod.ForPixelsPerMetre</c>, and must not be capped this way.</para>
        /// </summary>
        public static int LodForScale(int scaleDenominator)
        {
            if (scaleDenominator <= 0) return Tuning.MaxPaperContourLod;

            double targetGroundCell = Tuning.PaperDetailMm / Tuning.MmPerMetre * scaleDenominator;
            if (!(targetGroundCell > 0.0)) return Tuning.MaxPaperContourLod;

            int lod = 0;
            double cell = Tuning.BaseCell;
            while (lod < Tuning.MaxPaperContourLod && cell > targetGroundCell)
            {
                cell *= 0.5;
                lod++;
            }
            return lod;
        }

        // ------------------------------------------------------------ contour

        /// <summary>
        /// §6.1 / §6.2. Snaps <paramref name="area"/> outward to the cellSize lattice measured
        /// from the domain origin (0,0), expands by one cell so lines crossing the border are
        /// correct, contours, stitches, then clips back to the original <paramref name="area"/>.
        ///
        /// Corner values come straight from <see cref="IHeightField.Height01"/>, already quantised
        /// at 2^-16 (§4.4); they are compared with &gt;= (a tie at the level counts as inside) and
        /// interpolated as-is. Never re-quantised — both sides of a shared border must interpolate
        /// identical numbers or the §6.2 guarantee is lost (A3, §13.3).
        ///
        /// Output order is a total order on the polylines (first vertex x asc, y asc, then the
        /// remaining vertices), so callers such as §10.1's "longest loop, ties by first vertex"
        /// see a stable list. No dictionary is ever enumerated to produce it (§4.1).
        /// </summary>
        public static IReadOnlyList<Polyline> Extract(IHeightField field, Rect2 area, double cellSize, double level01)
        {
            var empty = new List<Polyline>();
            if (field == null) return empty;
            if (area.IsEmpty) return empty;
            if (!(cellSize > 0.0) || double.IsInfinity(cellSize) || double.IsNaN(cellSize)) return empty;

            // §6.2: corners land on multiples of cellSize from the domain origin, then one cell
            // of margin so a line crossing the border is built from complete cells on both sides.
            Rect2 grid = area.SnapOut(cellSize).Expanded(cellSize);

            long ix0 = (long)Math.Floor(grid.MinX / cellSize + 0.5);
            long iy0 = (long)Math.Floor(grid.MinY / cellSize + 0.5);
            long nxL = (long)Math.Floor(grid.Width / cellSize + 0.5);
            long nyL = (long)Math.Floor(grid.Height / cellSize + 0.5);
            if (nxL < 1 || nyL < 1) return empty;

            int nx = (int)nxL;
            int ny = (int)nyL;

            double weldEps = cellSize * Tuning.WeldFraction;

            // Corner x's are hoisted so that two rects sharing a border compute bit-identical
            // abscissae: always (latticeIndex * cellSize), never an accumulated sum.
            double[] xs = new double[nx + 1];
            for (int i = 0; i <= nx; i++) xs[i] = (ix0 + i) * cellSize;

            // Two rolling rows: every corner is sampled exactly once (§13.8).
            double[] below = new double[nx + 1];
            double[] above = new double[nx + 1];

            double yBase = iy0 * cellSize;
            for (int i = 0; i <= nx; i++) below[i] = field.Height01(xs[i], yBase);

            var segs = new List<Seg>();

            for (int j = 0; j < ny; j++)
            {
                double ylo = (iy0 + j) * cellSize;
                double yhi = (iy0 + j + 1) * cellSize;

                for (int i = 0; i <= nx; i++) above[i] = field.Height01(xs[i], yhi);

                for (int i = 0; i < nx; i++)
                {
                    double v00 = below[i];
                    double v10 = below[i + 1];
                    double v01 = above[i];
                    double v11 = above[i + 1];

                    bool in00 = v00 >= level01;
                    bool in10 = v10 >= level01;
                    bool in11 = v11 >= level01;
                    bool in01 = v01 >= level01;

                    int code = (in00 ? 1 : 0) | (in10 ? 2 : 0) | (in11 ? 4 : 0) | (in01 ? 8 : 0);
                    if (code == 0 || code == 15) continue;

                    double xlo = xs[i];
                    double xhi = xs[i + 1];

                    // Only edges with a sign change carry a crossing. Bottom/top always interpolate
                    // left-to-right and left/right always bottom-to-top, so the crossing shared by
                    // two cells (or by two rects) is produced by the same expression on both sides.
                    V2 eB = default, eR = default, eT = default, eL = default;
                    if (in00 != in10) eB = new V2(xlo + (xhi - xlo) * Frac(v00, v10, level01), ylo);
                    if (in10 != in11) eR = new V2(xhi, ylo + (yhi - ylo) * Frac(v10, v11, level01));
                    if (in01 != in11) eT = new V2(xlo + (xhi - xlo) * Frac(v01, v11, level01), yhi);
                    if (in00 != in01) eL = new V2(xlo, ylo + (yhi - ylo) * Frac(v00, v01, level01));

                    bool centreInside = false;
                    if (code == 5 || code == 10)
                    {
                        // §6.1: saddles resolve by the sign of the cell-centre sample, always.
                        // (index + 0.5) * cellSize keeps the centre identical across rects.
                        double cx = (ix0 + i + 0.5) * cellSize;
                        double cy = (iy0 + j + 0.5) * cellSize;
                        centreInside = field.Height01(cx, cy) >= level01;
                    }

                    Emit(code, centreInside, eB, eR, eT, eL, segs);
                }

                double[] swap = below; below = above; above = swap;
            }

            List<Polyline> stitched = Stitch(segs, weldEps);

            var clipped = new List<Polyline>(stitched.Count);
            for (int i = 0; i < stitched.Count; i++) ClipToRect(stitched[i], area, weldEps, clipped);

            clipped.Sort(Compare);
            return clipped;
        }

        /// <summary>Linear interpolation parameter of level between two quantised corner values.</summary>
        static double Frac(double va, double vb, double level)
        {
            double d = vb - va;
            if (d == 0.0) return 0.5;           // unreachable for a real crossing; kept as a guard
            double t = (level - va) / d;
            if (t < 0.0) return 0.0;
            if (t > 1.0) return 1.0;
            return t;
        }

        /// <summary>
        /// The 16 marching-squares cases. Bit 1 = lower-left, 2 = lower-right, 4 = upper-right,
        /// 8 = upper-left. Every segment is directed so the inside (land) lies on its left, which
        /// makes closed loops come out counter-clockwise. Cases 5 and 10 take the centre sign.
        /// </summary>
        static void Emit(int code, bool centreInside, V2 b, V2 r, V2 t, V2 l, List<Seg> segs)
        {
            switch (code)
            {
                case 1:  segs.Add(new Seg(b, l)); break;
                case 2:  segs.Add(new Seg(r, b)); break;
                case 3:  segs.Add(new Seg(r, l)); break;
                case 4:  segs.Add(new Seg(t, r)); break;
                case 5:
                    if (centreInside) { segs.Add(new Seg(b, r)); segs.Add(new Seg(t, l)); }
                    else              { segs.Add(new Seg(b, l)); segs.Add(new Seg(t, r)); }
                    break;
                case 6:  segs.Add(new Seg(t, b)); break;
                case 7:  segs.Add(new Seg(t, l)); break;
                case 8:  segs.Add(new Seg(l, t)); break;
                case 9:  segs.Add(new Seg(b, t)); break;
                case 10:
                    if (centreInside) { segs.Add(new Seg(l, b)); segs.Add(new Seg(r, t)); }
                    else              { segs.Add(new Seg(r, b)); segs.Add(new Seg(l, t)); }
                    break;
                case 11: segs.Add(new Seg(r, t)); break;
                case 12: segs.Add(new Seg(l, r)); break;
                case 13: segs.Add(new Seg(b, r)); break;
                case 14: segs.Add(new Seg(l, b)); break;
                default: break;                                   // 0 and 15 produce nothing
            }
        }

        // ------------------------------------------------------------ stitch

        /// <summary>
        /// §6.1: weld segment endpoints at cellSize * 1e-6 into polylines, flagging closed loops.
        /// The spatial hash is a lookup structure only; chains are seeded in segment index order
        /// and each candidate is the lowest-numbered match, so nothing depends on hash order (§4.1).
        /// </summary>
        static List<Polyline> Stitch(List<Seg> segs, double weldEps)
        {
            var result = new List<Polyline>();
            int m = segs.Count;
            if (m == 0) return result;

            double epsSq = weldEps * weldEps;
            double invEps = 1.0 / weldEps;

            bool[] used = new bool[m];
            var buckets = new Dictionary<GridKey, List<int>>(m * 2);
            for (int s = 0; s < m; s++)
            {
                // A corner value exactly equal to the level puts both of that corner's crossings
                // on the corner itself, so the cell emits a zero-length segment. h01 is quantised
                // at 2^-16 and the coast level is 0.50, so this happens for real; the segment
                // carries no geometry and the neighbouring cells already weld at that corner.
                if (V2.DistSq(segs[s].A, segs[s].B) <= epsSq) { used[s] = true; continue; }
                AddEndpoint(buckets, segs[s].A, invEps, s * 2);
                AddEndpoint(buckets, segs[s].B, invEps, s * 2 + 1);
            }

            var fwd = new List<V2>();
            var back = new List<V2>();

            for (int s = 0; s < m; s++)
            {
                if (used[s]) continue;
                used[s] = true;

                fwd.Clear();
                back.Clear();
                fwd.Add(segs[s].A);
                fwd.Add(segs[s].B);

                V2 origin = segs[s].A;
                bool closed = false;

                while (true)
                {
                    V2 tail = fwd[fwd.Count - 1];
                    int ep = FindEndpoint(buckets, segs, used, tail, invEps, epsSq);
                    if (ep < 0) break;
                    int si = ep >> 1;
                    used[si] = true;
                    V2 far = (ep & 1) == 0 ? segs[si].B : segs[si].A;
                    fwd.Add(far);
                    if (V2.DistSq(far, origin) <= epsSq) { closed = true; break; }
                }

                if (!closed)
                {
                    V2 head = origin;
                    while (true)
                    {
                        int ep = FindEndpoint(buckets, segs, used, head, invEps, epsSq);
                        if (ep < 0) break;
                        int si = ep >> 1;
                        used[si] = true;
                        V2 far = (ep & 1) == 0 ? segs[si].B : segs[si].A;
                        back.Add(far);
                        head = far;
                    }
                }

                var chain = new List<V2>(back.Count + fwd.Count);
                for (int i = back.Count - 1; i >= 0; i--) chain.Add(back[i]);
                chain.AddRange(fwd);

                // A chain whose ends already coincide is a loop even if the walk ran out of
                // unused segments before it noticed.
                if (!closed && chain.Count > 2 && V2.DistSq(chain[0], chain[chain.Count - 1]) <= epsSq)
                    closed = true;

                if (closed) chain.RemoveAt(chain.Count - 1);
                if (chain.Count >= 2) result.Add(new Polyline(chain.ToArray(), closed));
            }

            return result;
        }

        static void AddEndpoint(Dictionary<GridKey, List<int>> buckets, V2 p, double invEps, int endpointId)
        {
            GridKey k = KeyOf(p, invEps);
            List<int> list;
            if (!buckets.TryGetValue(k, out list))
            {
                list = new List<int>(2);
                buckets.Add(k, list);
            }
            list.Add(endpointId);
        }

        static GridKey KeyOf(V2 p, double invEps)
        {
            return new GridKey((long)Math.Floor(p.X * invEps), (long)Math.Floor(p.Y * invEps));
        }

        /// <summary>
        /// Lowest endpoint id of an unused segment whose endpoint welds to p. The 3x3 bucket probe
        /// is exhaustive because bucket size equals the weld tolerance.
        /// </summary>
        static int FindEndpoint(Dictionary<GridKey, List<int>> buckets, List<Seg> segs, bool[] used,
                                V2 p, double invEps, double epsSq)
        {
            GridKey c = KeyOf(p, invEps);
            int best = -1;
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    List<int> list;
                    if (!buckets.TryGetValue(new GridKey(c.X + dx, c.Y + dy), out list)) continue;
                    for (int n = 0; n < list.Count; n++)
                    {
                        int ep = list[n];
                        int si = ep >> 1;
                        if (used[si]) continue;
                        V2 q = (ep & 1) == 0 ? segs[si].A : segs[si].B;
                        if (V2.DistSq(q, p) > epsSq) continue;
                        if (best < 0 || ep < best) best = ep;
                    }
                }
            }
            return best;
        }

        // -------------------------------------------------------------- clip

        /// <summary>
        /// Clip a stitched polyline back to the original rect (§6.1). A polyline wholly inside is
        /// returned untouched, closed flag and all; anything else becomes one or more open runs.
        /// A closed loop is rotated to start at a vertex outside the rect first, so the wrap
        /// segment is clipped like any other and a run is never split across the seam.
        /// </summary>
        static void ClipToRect(Polyline p, Rect2 area, double weldEps, List<Polyline> outList)
        {
            int n = p.Count;
            if (n < 2) return;

            bool allInside = true;
            int firstOutside = -1;
            for (int i = 0; i < n; i++)
            {
                if (!area.Contains(p[i]))
                {
                    allInside = false;
                    if (firstOutside < 0) firstOutside = i;
                }
            }
            if (allInside) { outList.Add(p); return; }

            int start = p.Closed ? firstOutside : 0;
            int segCount = p.Closed ? n : n - 1;
            double epsSq = weldEps * weldEps;

            var run = new List<V2>();
            for (int k = 0; k < segCount; k++)
            {
                int i = (start + k) % n;
                int j = (i + 1) % n;
                V2 a = p[i];
                V2 b = p[j];

                double t0, t1;
                if (ClipSegment(a, b, area, out t0, out t1))
                {
                    V2 ca = t0 <= 0.0 ? a : V2.Lerp(a, b, t0);
                    V2 cb = t1 >= 1.0 ? b : V2.Lerp(a, b, t1);
                    if (run.Count == 0)
                    {
                        run.Add(ca);
                        run.Add(cb);
                    }
                    else if (V2.DistSq(run[run.Count - 1], ca) <= epsSq)
                    {
                        run.Add(cb);
                    }
                    else
                    {
                        Flush(run, outList);
                        run.Add(ca);
                        run.Add(cb);
                    }
                }
                else
                {
                    Flush(run, outList);
                }
            }
            Flush(run, outList);
        }

        static void Flush(List<V2> run, List<Polyline> outList)
        {
            if (run.Count >= 2) outList.Add(new Polyline(run.ToArray(), false));
            run.Clear();
        }

        /// <summary>
        /// Liang-Barsky. The border parameter is always (bound - a) / (b - a), so a segment clipped
        /// by two rects that share that border yields the same point on both sides (A3, §13.3).
        /// </summary>
        static bool ClipSegment(V2 a, V2 b, Rect2 r, out double t0, out double t1)
        {
            t0 = 0.0;
            t1 = 1.0;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            if (!ClipParam(-dx, a.X - r.MinX, ref t0, ref t1)) return false;
            if (!ClipParam( dx, r.MaxX - a.X, ref t0, ref t1)) return false;
            if (!ClipParam(-dy, a.Y - r.MinY, ref t0, ref t1)) return false;
            if (!ClipParam( dy, r.MaxY - a.Y, ref t0, ref t1)) return false;
            return t1 > t0;
        }

        static bool ClipParam(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0.0) return q >= 0.0;
            double t = q / p;
            if (p < 0.0)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }

        // -------------------------------------------------------------- sort

        /// <summary>
        /// Total order on polylines: first vertex (x asc, y asc), then the remaining vertices, then
        /// length and closedness. The leading key is exactly the tie-break §10.1 asks for.
        /// </summary>
        static int Compare(Polyline a, Polyline b)
        {
            int na = a.Count;
            int nb = b.Count;
            int m = na < nb ? na : nb;
            for (int i = 0; i < m; i++)
            {
                V2 pa = a[i];
                V2 pb = b[i];
                if (pa.X < pb.X) return -1;
                if (pa.X > pb.X) return 1;
                if (pa.Y < pb.Y) return -1;
                if (pa.Y > pb.Y) return 1;
            }
            if (na != nb) return na < nb ? -1 : 1;
            if (a.Closed != b.Closed) return a.Closed ? 1 : -1;
            return 0;
        }
    }
}
