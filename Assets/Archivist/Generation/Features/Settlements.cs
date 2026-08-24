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
            int gx0, gx1, gy0, gy1;
            Lattice.Bounds(landBounds, cell, shelterR, out gx0, out gx1, out gy0, out gy1);

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
            // The shared sweep records the DISTANCE to the nearest coastline segment and leaves
            // double.MaxValue outside the band. This pass only asks the boolean question "is this
            // cell inside the band", which is exactly "did the sweep record anything here" — see
            // Lattice.MarkCoastDistance for why the two readings are bit-for-bit the same test.
            double[] coastDist = new double[nx * ny];
            Lattice.MarkCoastDistance(coast, Tuning.SettlementCoastDist, gx0, gy0, nx, ny, cell, coastDist);

            // --- the shelter disc, precomputed once ---------------------------------
            Lattice.Offset[] discOffsets = Lattice.Disc(shelterR, cell);

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

                    if (!flat && coastDist[i] == double.MaxValue) continue;

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
            Pcg32 rng = Streams.For(field.Params.Seed, StreamNames.Settlements);
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
        /// <para><b>Why this shape.</b> <c>land</c> is the discrete concavity of the coastline
        /// seen from the candidate: land wrapping more than half the neighbourhood means the
        /// shore curves around the point (a cove, a bay head, a fjord); less than half means it
        /// sticks out into the water (a headland). So <c>land^2</c> is the concavity term. The
        /// bare <c>sea</c> factor requires there be water to shelter in at all — without it every
        /// inland point scores 1.0 and settlements march away from the coast.</para>
        ///
        /// <para>The product peaks at <c>land = 2/3</c>, exactly a cove, and <c>27/4</c> is the
        /// normaliser putting that maximum at 1.0 — derived from the shape, not tuned, which is
        /// why no <see cref="Tuning"/> entry is needed. Representative values: bay head 1.00,
        /// straight coast 0.84, exposed headland 0.50, deep inland and open sea 0.00.</para>
        ///
        /// <para><b>Unimodal in <c>land</c>, not monotone in <c>sea</c>.</b> The obvious reading
        /// — shelter rises with the sea fraction — inverts the geometry: a point with sea on many
        /// sides is a headland, the most exposed place on the island. This stays monotone in the
        /// thing that matters, concavity, over the whole coastal range <c>land ∈ [0, 2/3]</c>. If
        /// the map reads wrong, the exponent on <c>land</c> is the single knob: raising it moves
        /// the optimum deeper into the inlet.</para>
        ///
        /// <para>The arithmetic itself lives in <see cref="ShelterMeasure.FromLandFraction"/>
        /// so POC-03's POI siting (spec §1.2) can reuse the same measure rather than copy it.
        /// Only the land-fraction count is local to this pass.</para>
        /// </summary>
        static double Shelter(bool[] isLand, Lattice.Offset[] disc, int ix, int iy, int nx, int ny)
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

            return ShelterMeasure.FromLandFraction((double)land / total);
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

        /// <summary>§7.2 step 3's total order: score desc, then <see cref="TotalOrder.ByPosition"/>.
        /// Descending on the primary key, which is why the comparator is written out here rather
        /// than shared whole — <see cref="PoiSiting"/>'s primary key ascends.</summary>
        static int CompareCandidates(Candidate a, Candidate b)
        {
            if (a.Score != b.Score) return a.Score > b.Score ? -1 : 1;      // desc
            return TotalOrder.ByPosition(a.Position, b.Position);
        }

        readonly struct Candidate
        {
            public readonly V2 Position;
            public readonly double Score;
            public Candidate(V2 position, double score) { Position = position; Score = score; }
        }
    }
}
