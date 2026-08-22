using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// §7.2. Settlements are discrete (§3.1): one pass per island, total-ordered before
    /// selection, stable ids. The only PRNG draw is the count, from
    /// <c>Streams.For(seed, "settlements")</c> (§4.3).
    ///
    /// <para><b>Two formulas the spec leaves open are closed here.</b> §7.2 step 2 scores
    /// <c>0.6 * shelter + 0.4 * flatness</c> but defines neither term; `shelter` is listed as
    /// still-open in §6 of <c>poc-01-decisions.md</c> ("any monotone concavity measure will do,
    /// so it is a tuning choice, not a design one"). See <see cref="Shelter"/> and
    /// <see cref="Flatness"/> — both are documented in full there and both are built only from
    /// constants that already exist in <see cref="Tuning"/>.</para>
    /// </summary>
    public static class Settlements
    {
        /// <summary>
        /// §7.2 steps 1-5. Candidates are land points on the
        /// <see cref="Tuning.SettlementLattice"/> lattice that are either within
        /// <see cref="Tuning.SettlementCoastDist"/> of a coastline polyline or flat
        /// (<c>Q.Grad(|Gradient|) &lt; Tuning.SettlementFlatGrad</c>, §4.4 / D3 — the gradient
        /// itself is unquantised, the comparison operand is not). They are scored, sorted by
        /// (score desc, x asc, y asc), and taken greedily at
        /// <see cref="Tuning.SettlementMinSpacing"/> until the drawn count is met.
        /// <para><c>Name</c> is left null; naming is the separate §9 pass.</para>
        /// </summary>
        /// <param name="coast">Coastline polylines, i.e. <c>Contours.Extract(field, ..., SeaLevel)</c>.
        /// May be empty, in which case only the flatness criterion admits candidates.</param>
        public static List<Settlement> Generate(IHeightField field, Rect2 landBounds,
                                                IReadOnlyList<Polyline> coast)
        {
            if (field == null) throw new ArgumentNullException("field");

            List<Settlement> result = new List<Settlement>();
            if (landBounds.IsEmpty) return result;

            double cell = Tuning.SettlementLattice;
            double shelterR = Tuning.SettlementShelterRadius;

            // Lattice indices from the domain origin (0,0), §6.2. The sampled block is the land
            // bbox grown by the shelter radius so that every candidate's 600 m neighbourhood is
            // fully inside the sampled block — otherwise a coastal candidate near the bbox edge
            // would see truncated water and read as more enclosed than it is.
            int gx0 = (int)Math.Floor((landBounds.MinX - shelterR) / cell);
            int gx1 = (int)Math.Ceiling((landBounds.MaxX + shelterR) / cell);
            int gy0 = (int)Math.Floor((landBounds.MinY - shelterR) / cell);
            int gy1 = (int)Math.Ceiling((landBounds.MaxY + shelterR) / cell);

            int nx = gx1 - gx0 + 1;
            int ny = gy1 - gy0 + 1;
            if (nx < 1 || ny < 1) return result;

            // --- land/sea over the block. Height01 is quantised, so this is a safe branch (§4.4).
            bool[] isLand = new bool[nx * ny];
            for (int ix = 0; ix < nx; ix++)
            {
                double x = (gx0 + ix) * cell;
                for (int iy = 0; iy < ny; iy++)
                {
                    isLand[ix * ny + iy] = field.IsLand(x, (gy0 + iy) * cell);
                }
            }

            // --- step 1a: proximity to a coastline polyline --------------------------
            bool[] nearCoast = new bool[nx * ny];
            MarkNearCoast(coast, nearCoast, gx0, gy0, nx, ny, cell, Tuning.SettlementCoastDist);

            // --- the shelter disc, precomputed once ---------------------------------
            Offset[] discOffsets = BuildDiscOffsets(shelterR, cell);

            // --- steps 1b, 2, 3: candidates, scores, total order ---------------------
            List<Candidate> candidates = new List<Candidate>();
            for (int ix = 0; ix < nx; ix++)
            {
                double x = (gx0 + ix) * cell;
                if (x < landBounds.MinX || x > landBounds.MaxX) continue;

                for (int iy = 0; iy < ny; iy++)
                {
                    double y = (gy0 + iy) * cell;
                    if (y < landBounds.MinY || y > landBounds.MaxY) continue;

                    int i = ix * ny + iy;
                    if (!isLand[i]) continue;

                    // §4.4 / D3: Gradient is computed unquantised, but |Gradient| is rounded to
                    // 1e-4 before its one branch. Q.Grad is that rounding.
                    double gradQ = Q.Grad(field.Gradient(x, y).Length);
                    bool flat = gradQ < Tuning.SettlementFlatGrad;

                    if (!flat && !nearCoast[i]) continue;

                    double shelter = Shelter(isLand, discOffsets, ix, iy, nx, ny);
                    double flatness = Flatness(gradQ);
                    double score = Tuning.SettlementShelterWeight * shelter
                                 + Tuning.SettlementFlatnessWeight * flatness;

                    candidates.Add(new Candidate(new V2(x, y), score));
                }
            }

            candidates.Sort(CompareCandidates);

            // --- step 5: the count, its own sub-stream ------------------------------
            int minInc, maxExc;
            IslandParams.SettlementRangeFor(field.Params.Character, out minInc, out maxExc);
            Pcg32 rng = Streams.For(field.Params.Seed, "settlements");
            int want = rng.Range(minInc, maxExc);

            // --- step 4: greedy selection at minimum spacing -------------------------
            double minSpacing2 = Tuning.SettlementMinSpacing * Tuning.SettlementMinSpacing;
            for (int i = 0; i < candidates.Count && result.Count < want; i++)
            {
                bool tooClose = false;
                for (int k = 0; k < result.Count; k++)
                {
                    if (V2.DistSq(result[k].Position, candidates[i].Position) < minSpacing2)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                result.Add(new Settlement(new FeatureId(FeatureClass.Settlement, result.Count),
                                          candidates[i].Position, candidates[i].Score, null));
            }
            return result;
        }

        // -------------------------------------------------------------------------------
        // SHELTER — the formula §7.2 leaves open
        // -------------------------------------------------------------------------------

        /// <summary>
        /// <b>SPEC GAP CLOSED HERE.</b> §7.2 asks for "coastline concavity in a 600 m
        /// neighbourhood" and gives no formula; §6 of <c>poc-01-decisions.md</c> records it as
        /// open and explicitly a tuning choice. The measure adopted is:
        ///
        /// <code>
        /// land = fraction of the samples in the disc of radius SettlementShelterRadius
        ///        around the candidate, taken on the same 128 m lattice, that are land
        /// sea  = 1 - land
        /// shelter = clamp01( 27/4 * land^2 * sea )
        /// </code>
        ///
        /// <para><b>Why this shape.</b> <c>land</c> is the discrete concavity of the coastline as
        /// seen from the candidate: land wrapping more than half the neighbourhood means the
        /// shore curves around the point (a cove, a bay head, a fjord), and land wrapping less
        /// than half means the point sticks out into the water (a headland). So <c>land^2</c> is
        /// the concavity term, rising with enclosure. The bare <c>sea</c> factor is the
        /// requirement that there be water to shelter in at all: without it every inland point
        /// scores 1.0 and settlements march away from the coast, which is not what §7.2 wants
        /// from a term weighted 0.6.</para>
        ///
        /// <para>The product peaks at <c>land = 2/3</c> — land on two thirds of the horizon, water
        /// on one third, which is exactly a cove — and <c>27/4</c> is the normaliser that puts
        /// that maximum at 1.0 (it is derived from the shape, not a tuned constant, which is why
        /// no new entry in <see cref="Tuning"/> is needed). Representative values: bay head 1.00,
        /// straight coast 0.84, exposed headland 0.50, deep inland and open sea both 0.00.</para>
        ///
        /// <para><b>Deviation from the sketch, stated deliberately.</b> The obvious reading —
        /// "shelter rises with the fraction of the neighbourhood that is sea" — is monotone but
        /// inverts the geometry: a point with sea on many sides is a headland, the most exposed
        /// place on the island, not a sheltered inlet. This measure is therefore unimodal in
        /// <c>land</c> rather than monotone in <c>sea</c>. It stays monotone in the thing that
        /// matters, concavity, over the whole coastal range <c>land ∈ [0, 2/3]</c>, and it is one
        /// multiply-add per sample. If the map reads wrong, the exponent on <c>land</c> is the
        /// single knob: raising it moves the optimum deeper into the inlet.</para>
        /// </summary>
        static double Shelter(bool[] isLand, Offset[] disc, int ix, int iy, int nx, int ny)
        {
            int total = 0;
            int land = 0;
            for (int k = 0; k < disc.Length; k++)
            {
                int jx = ix + disc[k].Dx;
                int jy = iy + disc[k].Dy;
                if (jx < 0 || jx >= nx || jy < 0 || jy >= ny) continue;   // defensive; block is grown by R
                total++;
                if (isLand[jx * ny + jy]) land++;
            }
            if (total == 0) return 0.0;

            double l = (double)land / total;
            double s = 1.0 - l;
            double v = 6.75 * l * l * s;          // 27/4, the normaliser for the l = 2/3 maximum
            if (v < 0.0) return 0.0;
            return v > 1.0 ? 1.0 : v;
        }

        /// <summary>
        /// <b>SPEC GAP CLOSED HERE (minor).</b> §7.2 step 2 names <c>flatness</c> without
        /// defining it either. Taken as the linear ramp on the same quantised slope that already
        /// gates the candidate set, so the two halves of §7.2 step 1 and step 2 agree on what
        /// "flat" means and no new constant is introduced:
        /// <code>flatness = clamp01(1 - Q.Grad(|Gradient|) / SettlementFlatGrad)</code>
        /// 1.0 on dead level ground, 0.0 at or beyond the 0.04 m/m (~2.3 degree) threshold.
        /// </summary>
        static double Flatness(double gradQ)
        {
            double v = 1.0 - gradQ / Tuning.SettlementFlatGrad;
            if (v < 0.0) return 0.0;
            return v > 1.0 ? 1.0 : v;
        }

        // -------------------------------------------------------------------------------

        /// <summary>
        /// Marks every lattice cell within <paramref name="dist"/> of any coastline segment.
        /// Exact point-to-segment distance, but only over each segment's bbox grown by
        /// <paramref name="dist"/>, so the cost is linear in coastline length rather than
        /// O(candidates x segments).
        /// </summary>
        static void MarkNearCoast(IReadOnlyList<Polyline> coast, bool[] nearCoast,
                                  int gx0, int gy0, int nx, int ny, double cell, double dist)
        {
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
                    V2 b = line[(s + 1) % line.Count];

                    double minX = Math.Min(a.X, b.X) - dist;
                    double maxX = Math.Max(a.X, b.X) + dist;
                    double minY = Math.Min(a.Y, b.Y) - dist;
                    double maxY = Math.Max(a.Y, b.Y) + dist;

                    int ix0 = (int)Math.Ceiling(minX / cell) - gx0;
                    int ix1 = (int)Math.Floor(maxX / cell) - gx0;
                    int iy0 = (int)Math.Ceiling(minY / cell) - gy0;
                    int iy1 = (int)Math.Floor(maxY / cell) - gy0;

                    if (ix0 < 0) ix0 = 0;
                    if (iy0 < 0) iy0 = 0;
                    if (ix1 > nx - 1) ix1 = nx - 1;
                    if (iy1 > ny - 1) iy1 = ny - 1;

                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        double x = (gx0 + ix) * cell;
                        for (int iy = iy0; iy <= iy1; iy++)
                        {
                            int i = ix * ny + iy;
                            if (nearCoast[i]) continue;
                            if (DistSqToSegment(new V2(x, (gy0 + iy) * cell), a, b) <= dist2) nearCoast[i] = true;
                        }
                    }
                }
            }
        }

        static double DistSqToSegment(V2 p, V2 a, V2 b)
        {
            V2 ab = b - a;
            double len2 = ab.LengthSq;
            if (len2 <= 0.0) return V2.DistSq(p, a);
            double t = V2.Dot(p - a, ab) / len2;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            return V2.DistSq(p, a + ab * t);
        }

        static Offset[] BuildDiscOffsets(double radius, double cell)
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

        static int CompareCandidates(Candidate a, Candidate b)
        {
            if (a.Score != b.Score) return a.Score > b.Score ? -1 : 1;      // desc
            if (a.Position.X != b.Position.X) return a.Position.X < b.Position.X ? -1 : 1;
            if (a.Position.Y != b.Position.Y) return a.Position.Y < b.Position.Y ? -1 : 1;
            return 0;
        }

        readonly struct Candidate
        {
            public readonly V2 Position;
            public readonly double Score;
            public Candidate(V2 position, double score) { Position = position; Score = score; }
        }

        readonly struct Offset
        {
            public readonly int Dx;
            public readonly int Dy;
            public Offset(int dx, int dy) { Dx = dx; Dy = dy; }
        }
    }
}
