using System;
using System.Collections.Generic;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// Lattice-block sampling helpers shared by the passes that rasterise the island onto the
    /// global lattice of §6.2 — <see cref="Settlements"/> and <see cref="PoiSiting"/>. These are
    /// not geometry (that lives in <see cref="Geometry.Segment"/>); they are about the block:
    /// how big it is, which offsets a disc covers on it, and how far each cell is from the coast.
    ///
    /// <para><b>Everything here is bit-reproducible code (§4.4, asserted by A2).</b> The
    /// arithmetic was moved verbatim from the two passes that carried identical copies of it.
    /// Iteration order and expression order are part of the contract, not style.</para>
    /// </summary>
    public static class Lattice
    {
        /// <summary>
        /// The lattice-index bounds of the land bbox grown by <paramref name="margin"/>, measured
        /// from the domain origin (0,0), §6.2.
        ///
        /// <para>The margin exists so that every candidate's shelter neighbourhood is fully inside
        /// the sampled block — otherwise a coastal candidate near the bbox edge would see truncated
        /// water and read as more enclosed than it is. Both callers pass
        /// <see cref="Tuning.SettlementShelterRadius"/>, and neither may shrink it.</para>
        ///
        /// <para>Note the asymmetric rounding: Floor on the low side, Ceiling on the high side, so
        /// the block always covers the grown bbox rather than clipping it. <see cref="Peaks"/> is
        /// deliberately NOT a caller — it rounds the other way (Ceiling low, Floor high) because it
        /// wants only lattice points strictly inside the bbox.</para>
        /// </summary>
        public static void Bounds(Rect2 landBounds, double cell, double margin,
                                  out int gx0, out int gx1, out int gy0, out int gy1)
        {
            gx0 = (int)Math.Floor((landBounds.MinX - margin) / cell);
            gx1 = (int)Math.Ceiling((landBounds.MaxX + margin) / cell);
            gy0 = (int)Math.Floor((landBounds.MinY - margin) / cell);
            gy1 = (int)Math.Ceiling((landBounds.MaxY + margin) / cell);
        }

        /// <summary>
        /// The lattice offsets covered by a disc of <paramref name="radius"/> on a
        /// <paramref name="cell"/>-spaced lattice, precomputed once per pass so the shelter count
        /// is a flat walk with no per-sample distance test.
        /// </summary>
        public static Offset[] Disc(double radius, double cell)
        {
            int r = (int)Math.Floor(radius / cell);
            double r2 = radius * radius;
            List<Offset> list = new List<Offset>();
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    double ox = dx * cell, oy = dy * cell;
                    if (ox * ox + oy * oy <= r2) list.Add(new Offset(dx, dy));
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// Records, for every cell of the block, the exact distance to the nearest coastline
        /// segment, leaving <c>double.MaxValue</c> wherever the nearest segment is further than
        /// <paramref name="dist"/>. Exact point-to-segment distance, but only over each segment's
        /// bbox grown by <paramref name="dist"/>, so the cost is linear in coastline length rather
        /// than O(cells x segments).
        ///
        /// <para><b>One sweep, two readings.</b> <see cref="PoiSiting"/> needs the distance itself;
        /// <see cref="Settlements"/> only needs the boolean "is this cell inside the coast band",
        /// which it used to compute with its own copy of this sweep. They are the same predicate: a
        /// cell keeps <c>double.MaxValue</c> exactly when no segment passed the
        /// <c>d2 &lt;= dist * dist</c> test, so <c>coastDist[i] != double.MaxValue</c> is
        /// bit-for-bit the flag the old <c>MarkNearCoast</c> set.</para>
        ///
        /// <para><b>The squared comparison is deliberate.</b> The band test is <c>d2 &gt; dist2</c>
        /// on the squared distance and must not become <c>Math.Sqrt(d2) &gt; dist</c>: the two are
        /// not the same branch in floating point at the edge of the band, and A2 hashes which cells
        /// were marked. The square root is taken only AFTER the test, to store the distance.</para>
        /// </summary>
        /// <param name="coastDist">Block-sized array indexed <c>ix * ny + iy</c>. Filled with
        /// <c>double.MaxValue</c> by this method before the sweep, so callers need not prefill.</param>
        public static void MarkCoastDistance(IReadOnlyList<Polyline> coast, double dist,
                                             int gx0, int gy0, int nx, int ny, double cell,
                                             double[] coastDist)
        {
            for (int i = 0; i < coastDist.Length; i++) coastDist[i] = double.MaxValue;
            if (coast == null) return;
            double dist2 = dist * dist;

            for (int c = 0; c < coast.Count; c++)
            {
                Polyline line = coast[c];
                if (line == null || line.Count < 1) continue;

                int segCount = line.Closed ? line.Count : line.Count - 1;
                for (int s = 0; s < segCount; s++)
                {
                    V2 a = line[s];
                    V2 e = line[(s + 1) % line.Count];

                    int ix0 = (int)Math.Ceiling((Math.Min(a.X, e.X) - dist) / cell) - gx0;
                    int ix1 = (int)Math.Floor((Math.Max(a.X, e.X) + dist) / cell) - gx0;
                    int iy0 = (int)Math.Ceiling((Math.Min(a.Y, e.Y) - dist) / cell) - gy0;
                    int iy1 = (int)Math.Floor((Math.Max(a.Y, e.Y) + dist) / cell) - gy0;

                    if (ix0 < 0) ix0 = 0;
                    if (iy0 < 0) iy0 = 0;
                    if (ix1 > nx - 1) ix1 = nx - 1;
                    if (iy1 > ny - 1) iy1 = ny - 1;

                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        double x = (gx0 + ix) * cell;
                        for (int iy = iy0; iy <= iy1; iy++)
                        {
                            double d2 = Segment.DistSq(new V2(x, (gy0 + iy) * cell), a, e);
                            if (d2 > dist2) continue;
                            int i = ix * ny + iy;
                            double d = Math.Sqrt(d2);
                            if (d < coastDist[i]) coastDist[i] = d;
                        }
                    }
                }
            }
        }

        /// <summary>An integer lattice offset. Deliberately not <see cref="V2"/>: these index
        /// cells, never ground space.</summary>
        public readonly struct Offset
        {
            public readonly int Dx;
            public readonly int Dy;
            public Offset(int dx, int dy) { Dx = dx; Dy = dy; }
        }
    }
}
