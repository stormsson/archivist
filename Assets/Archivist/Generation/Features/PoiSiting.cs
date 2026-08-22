using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// POC-03 spec §1 — POI siting and selection.
    ///
    /// <para><b>Siting is derived from the ground, never scattered</b> (P1.3). Every kind in
    /// <see cref="PoiKind"/> has a predicate over ground the generator already knows —
    /// elevation, slope, distance to the coastline, the §7.2 shelter measure, and the peak and
    /// settlement lists. A kind whose predicate is unsatisfiable on an island simply does not
    /// occur there, which is why an atoll has arches and stacks but no cairns.</para>
    ///
    /// <para><b>No new field evaluation strategy</b> (spec §1.2). Candidates are sampled on the
    /// <see cref="Tuning.PoiLattice"/> lattice measured from the domain origin (0,0), exactly as
    /// settlements are (§6.2).</para>
    ///
    /// <para><b>Order</b> (spec §1.3). Runs AFTER peaks, settlements and rivers — POIs read them
    /// and nothing reads POIs — and before naming. The only PRNG draw is the count, from
    /// <c>Streams.For(seed, "poi")</c>. §4.3 therefore guarantees that adding this pass leaves
    /// every existing feature bit-identical (P1.5, asserted by A2).</para>
    ///
    /// <para><b>SPEC GAP CLOSED HERE — selection is kind-major, not flat.</b> Spec §1.3 step 2
    /// says "score and sort by a TOTAL ORDER — (kind index asc, x asc, y asc)" but never defines
    /// the score, and a flat greedy pass over that order is degenerate: the primary key is the
    /// kind index, so <see cref="PoiKind.SeaArch"/> exhausts the per-island cap before any other
    /// kind is reached and every island ends up carrying nothing but sea arches. That would make
    /// C6's kind distribution meaningless and contradict P1.2's two families.
    /// <see cref="Select"/> therefore walks the kinds taking at most one POI per kind per round,
    /// repeating until the cap is met or a full round adds nothing, in the per-island order
    /// <see cref="KindOrder"/> rolls from the <c>"poi.kind"</c> stream §1.3 explicitly permits.
    /// The mandated total order is untouched — it is what canonicalises the candidate list, and
    /// within a kind selection consumes it in exactly that order — but the cap is now shared
    /// across kinds instead of being eaten by the first one. This is the one place the
    /// implementation departs from a literal reading of §1.3, and it is the difference between
    /// an island carrying five sea arches and an island carrying five different things.</para>
    /// </summary>
    public static class PoiSiting
    {
        /// <summary>
        /// Spec §1.3, steps 1-5.
        /// </summary>
        /// <param name="field">The island field. Sampled on the POI lattice only.</param>
        /// <param name="landBounds">Land AABB; an empty one yields no POIs.</param>
        /// <param name="coast">Coastline polylines, for the shore kinds' distance band.</param>
        /// <param name="peaks">Already generated (spec §1.3) — RuinedTower and Cairn read them.</param>
        /// <param name="settlements">Already generated — RuinedChapel and LandmarkTree read them.</param>
        public static List<Poi> Generate(IHeightField field, Rect2 landBounds,
                                         IReadOnlyList<Polyline> coast,
                                         IReadOnlyList<Peak> peaks,
                                         IReadOnlyList<Settlement> settlements)
        {
            if (field == null) throw new ArgumentNullException("field");

            List<Poi> result = new List<Poi>();
            if (landBounds.IsEmpty) return result;

            Block block = Block.Build(field, landBounds, coast);
            if (block == null) return result;

            // --- step 1+2: candidates, in the mandated total order --------------------
            List<Candidate> candidates = Candidates(field, landBounds, block, peaks, settlements);
            candidates.Sort(CompareCandidates);

            // --- step 4: the cap, its own named stream (§4.3) --------------------------
            int minInc, maxExc;
            IslandParams.PoiRangeFor(field.Params.Character, out minInc, out maxExc);
            Pcg32 rng = Streams.For(field.Params.Seed, "poi");
            int want = rng.Range(minInc, maxExc);

            // --- step 3: greedy selection at minimum spacing ---------------------------
            List<Candidate> chosen = Select(candidates, want, KindOrder(field.Params.Seed));

            // --- step 5: ids in the canonical total order ------------------------------
            // Re-sorted rather than left in selection order so the id sequence depends only on
            // WHICH POIs were chosen, never on HOW. Tightening Select later then cannot
            // renumber a POI that both strategies would have picked.
            chosen.Sort(CompareCandidates);
            for (int i = 0; i < chosen.Count; i++)
            {
                result.Add(new Poi(new FeatureId(FeatureClass.Poi, i), chosen[i].Position, chosen[i].Kind));
            }
            return result;
        }

        // ---------------------------------------------------------------------- selection

        /// <summary>
        /// The per-island order in which kinds get first refusal, from
        /// <c>Streams.For(seed, "poi.kind")</c> — the second stream spec §1.3 explicitly
        /// permits ("one named stream <c>poi</c>, plus <c>poi.kind</c> if needed").
        ///
        /// <para><b>Why it is needed.</b> Rounds walked in bare enum order starve the table:
        /// spec §1.1 lists all seven natural oddities before all six ruins, so with a cap of
        /// three to eight POIs the ruins never get a turn. Measured over 50 islands that gave
        /// 186 oddities against 5 ruins — one family, not the two P1.2 asks for, and requirements
        /// §2's whole argument for the feature ("ruined watchtowers and cairns sit on exactly the
        /// high ground Garrison already surveys") evaporates. Rolling the order per island makes
        /// which kinds an island favours part of its character while leaving the ordering WITHIN
        /// a kind exactly the total order §1.3 mandates.</para>
        ///
        /// <para>Fisher-Yates over <see cref="PoiKinds.All"/>, descending, from a single stream —
        /// no set iteration, no hashing, one draw per kind.</para>
        /// </summary>
        static PoiKind[] KindOrder(ulong islandSeed)
        {
            PoiKind[] order = new PoiKind[PoiKinds.Count];
            for (int i = 0; i < PoiKinds.Count; i++) order[i] = PoiKinds.All[i];

            Pcg32 rng = Streams.For(islandSeed, "poi.kind");
            for (int i = PoiKinds.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                PoiKind t = order[i];
                order[i] = order[j];
                order[j] = t;
            }
            return order;
        }

        /// <summary>
        /// Kind-major greedy selection at <see cref="Tuning.PoiMinSpacing"/> — see the class
        /// remarks for why this is not a flat pass over the sorted list. Deterministic: rounds
        /// walk <paramref name="kindOrder"/>, and each kind's candidates are consumed in the
        /// total order they were sorted into.
        /// </summary>
        static List<Candidate> Select(List<Candidate> candidates, int want, PoiKind[] kindOrder)
        {
            List<Candidate> chosen = new List<Candidate>();
            if (want <= 0 || candidates.Count == 0) return chosen;

            double minSpacing2 = Tuning.PoiMinSpacing * Tuning.PoiMinSpacing;

            // Cursor per kind into the (kind-major) sorted candidate list. Because the primary
            // sort key is the kind index, each kind's candidates are one contiguous run.
            // Indexed by (int)kind, so this needs the VALUE range, not the member count —
            // the enum has a gap where Stack was.
            int[] cursor = new int[PoiKinds.IndexRange];
            int[] end = new int[PoiKinds.IndexRange];
            for (int i = 0; i < PoiKinds.IndexRange; i++) { cursor[i] = -1; end[i] = -1; }
            for (int i = 0; i < candidates.Count; i++)
            {
                int k = (int)candidates[i].Kind;
                if (cursor[k] < 0) cursor[k] = i;
                end[k] = i + 1;
            }

            bool progress = true;
            while (chosen.Count < want && progress)
            {
                progress = false;
                for (int r = 0; r < kindOrder.Length && chosen.Count < want; r++)
                {
                    int k = (int)kindOrder[r];
                    if (cursor[k] < 0) continue;

                    while (cursor[k] < end[k])
                    {
                        Candidate c = candidates[cursor[k]];
                        cursor[k]++;

                        bool tooClose = false;
                        for (int j = 0; j < chosen.Count; j++)
                        {
                            if (V2.DistSq(chosen[j].Position, c.Position) < minSpacing2)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                        if (tooClose) continue;

                        chosen.Add(c);
                        progress = true;
                        break;
                    }
                }
            }
            return chosen;
        }

        // ---------------------------------------------------------------------- candidates

        /// <summary>
        /// Spec §1.3 step 1 — every lattice point that passes any kind's predicate, once per
        /// kind it passes. A point that is both a good cairn site and a good standing-stones
        /// site enters twice, so the minimum spacing decides between them rather than an
        /// arbitrary first-match.
        /// </summary>
        static List<Candidate> Candidates(IHeightField field, Rect2 landBounds, Block block,
                                          IReadOnlyList<Peak> peaks, IReadOnlyList<Settlement> settlements)
        {
            List<Candidate> list = new List<Candidate>();

            double maxElev = field.Params.MaxElevation;
            double highestPeak = 0.0;
            if (peaks != null)
            {
                for (int i = 0; i < peaks.Count; i++)
                {
                    if (peaks[i].SpotHeightM > highestPeak) highestPeak = peaks[i].SpotHeightM;
                }
            }
            double cairnFloor = highestPeak * Tuning.PoiCairnPeakFrac;
            bool anyPeak = highestPeak > 0.0;

            double towerDist2 = Tuning.PoiTowerPeakDist * Tuning.PoiTowerPeakDist;
            double chapelDist2 = Tuning.PoiChapelSettlementDist * Tuning.PoiChapelSettlementDist;
            double treeDist2 = Tuning.PoiTreeSettlementDist * Tuning.PoiTreeSettlementDist;

            for (int ix = 1; ix < block.Nx - 1; ix++)
            {
                double x = (block.Gx0 + ix) * block.Cell;
                if (x < landBounds.MinX || x > landBounds.MaxX) continue;

                for (int iy = 1; iy < block.Ny - 1; iy++)
                {
                    double y = (block.Gy0 + iy) * block.Cell;
                    if (y < landBounds.MinY || y > landBounds.MaxY) continue;

                    int i = ix * block.Ny + iy;
                    if (!block.HasGrad[i]) continue;      // no slope sampled here, so no predicate can read one

                    V2 p = new V2(x, y);
                    bool land = block.IsLand[i];
                    double elev = block.Elev[i];
                    double grad = block.GradMag[i];
                    double coastDist = block.CoastDist[i];
                    bool nearShore = coastDist <= Tuning.PoiShoreBand;
                    bool inland = coastDist > Tuning.PoiShoreBand;

                    // --- natural oddities -------------------------------------------
                    // Shore kinds share one band and separate on steepness. All of them sit
                    // on LAND: the one offshore kind (Stack) was removed because a detail
                    // sheet centred on open water has nothing in it to place the sheet by.
                    if (nearShore && land && grad >= Tuning.PoiSeaArchGrad)
                        list.Add(new Candidate(PoiKind.SeaArch, p));

                    if (nearShore && land && grad >= Tuning.PoiSteepShoreGrad && grad < Tuning.PoiSeaArchGrad)
                        list.Add(new Candidate(PoiKind.CaveMouth, p));

                    if (land && coastDist <= Tuning.PoiBlowholeCoastDist && grad >= Tuning.PoiSteepShoreGrad)
                        list.Add(new Candidate(PoiKind.Blowhole, p));

                    if (land && inland && elev >= Tuning.PoiSpringMinElevation
                        && block.Convergence(ix, iy) >= Tuning.PoiSpringConvergence)
                        list.Add(new Candidate(PoiKind.Spring, p));

                    if (land && inland && grad < Tuning.PoiOpenGrad
                        && elev >= maxElev * Tuning.PoiErraticElevMinFrac
                        && elev <= maxElev * Tuning.PoiErraticElevMaxFrac)
                        list.Add(new Candidate(PoiKind.ErraticBoulder, p));

                    if (land && grad < Tuning.PoiOpenGrad
                        && elev >= maxElev * Tuning.PoiTreeElevMinFrac
                        && elev <= maxElev * Tuning.PoiTreeElevMaxFrac
                        && NearestSq(settlements, p, SettlementPosition) > treeDist2)
                        list.Add(new Candidate(PoiKind.LandmarkTree, p));

                    // --- ruins -------------------------------------------------------
                    if (land && anyPeak && NearestSq(peaks, p, PeakPosition) <= towerDist2)
                        list.Add(new Candidate(PoiKind.RuinedTower, p));

                    if (land && anyPeak && elev >= cairnFloor)
                        list.Add(new Candidate(PoiKind.Cairn, p));

                    if (land && grad < Tuning.PoiFlatGrad)
                        list.Add(new Candidate(PoiKind.StandingStones, p));

                    if (land && (NearestSq(settlements, p, SettlementPosition) <= chapelDist2
                                 || (nearShore && block.Shelter[i] <= Tuning.PoiHeadlandShelterMax)))
                        list.Add(new Candidate(PoiKind.RuinedChapel, p));

                    if (land && nearShore && block.Shelter[i] >= Tuning.PoiJettyShelterMin)
                        list.Add(new Candidate(PoiKind.RuinedJetty, p));

                    if (land && inland && grad >= Tuning.PoiModerateGradMin && grad <= Tuning.PoiModerateGradMax)
                        list.Add(new Candidate(PoiKind.Enclosure, p));
                }
            }
            return list;
        }

        /// <summary>Position accessors for <see cref="NearestSq{T}"/>. Static readonly so the
        /// delegates are created once for the whole process: <see cref="NearestSq{T}"/> is called
        /// twice per lattice point, and the point of the generic is to keep that loop
        /// allocation-free and boxing-free (T is a struct, so the generic is specialised).</summary>
        static readonly Func<Settlement, V2> SettlementPosition = s => s.Position;
        static readonly Func<Peak, V2> PeakPosition = p => p.Position;

        /// <summary>Squared distance to the nearest of <paramref name="items"/>;
        /// <c>double.MaxValue</c> if there are none, so "away from settlements" is satisfied on
        /// an empty island and "near a settlement" is not — likewise for peaks.</summary>
        static double NearestSq<T>(IReadOnlyList<T> items, V2 p, Func<T, V2> position)
        {
            if (items == null || items.Count == 0) return double.MaxValue;
            double best = double.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                double d = V2.DistSq(position(items[i]), p);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Spec §1.3 step 2's total order, exactly: kind index asc, then
        /// <see cref="TotalOrder.ByPosition"/> (x asc, y asc). Total, because no two candidates of
        /// one kind share a lattice point. Note the primary key ASCENDS here, unlike
        /// <see cref="Peaks"/> and <see cref="Settlements"/>, which is why only the tie-break is
        /// shared.</summary>
        static int CompareCandidates(Candidate a, Candidate b)
        {
            if (a.Kind != b.Kind) return a.Kind < b.Kind ? -1 : 1;
            return TotalOrder.ByPosition(a.Position, b.Position);
        }

        readonly struct Candidate
        {
            public readonly PoiKind Kind;
            public readonly V2 Position;
            public Candidate(PoiKind kind, V2 position) { Kind = kind; Position = position; }
        }

        // ---------------------------------------------------------------------- the block

        /// <summary>
        /// Everything the predicates read, sampled once on the POI lattice. The block is the
        /// land bbox grown by <see cref="Tuning.SettlementShelterRadius"/> and snapped to the
        /// global lattice, so every candidate's 600 m shelter disc is fully inside it — the same
        /// reasoning §7.2 uses, and the same reason it must not be shrunk.
        /// </summary>
        sealed class Block
        {
            public double Cell;
            public int Gx0;
            public int Gy0;
            public int Nx;
            public int Ny;

            public bool[] IsLand;
            public double[] Elev;

            /// <summary>Quantised |Gradient| (§4.4 / D3): the gradient itself is unquantised,
            /// the operand every predicate compares is not.</summary>
            public double[] GradMag;

            /// <summary>Quantised gradient components, for <see cref="Convergence"/>.</summary>
            public double[] GradX;
            public double[] GradY;

            /// <summary>False where no gradient was sampled — far from land and far from the
            /// shore, where no predicate can fire anyway.</summary>
            public bool[] HasGrad;

            /// <summary>Exact distance to the nearest coastline segment, or
            /// <c>double.MaxValue</c> beyond <see cref="Tuning.PoiShoreBand"/>.</summary>
            public double[] CoastDist;

            /// <summary>§7.2's shelter measure (<see cref="ShelterMeasure"/>), computed only
            /// where a shore predicate can read it; 0 elsewhere.</summary>
            public double[] Shelter;

            /// <summary>
            /// Spec §1.2's "local gradient convergence" for Spring: the discrete Laplacian of
            /// elevation across one lattice cell, scaled by <c>2 * cell</c> and left in that
            /// scale so the constant it is compared against is a plain slope difference.
            /// Positive where flow converges — a hollow or a valley head. Built from the
            /// ALREADY-QUANTISED gradient components, so the comparison is a §4.4-safe branch
            /// and no extra field evaluation is paid for it.
            /// </summary>
            public double Convergence(int ix, int iy)
            {
                int xp = (ix + 1) * Ny + iy;
                int xm = (ix - 1) * Ny + iy;
                int yp = ix * Ny + (iy + 1);
                int ym = ix * Ny + (iy - 1);
                if (!HasGrad[xp] || !HasGrad[xm] || !HasGrad[yp] || !HasGrad[ym]) return 0.0;
                return (GradX[xp] - GradX[xm]) + (GradY[yp] - GradY[ym]);
            }

            public static Block Build(IHeightField field, Rect2 landBounds, IReadOnlyList<Polyline> coast)
            {
                Block b = new Block();
                b.Cell = Tuning.PoiLattice;
                double shelterR = Tuning.SettlementShelterRadius;

                int gx1, gy1;
                Lattice.Bounds(landBounds, b.Cell, shelterR, out b.Gx0, out gx1, out b.Gy0, out gy1);

                b.Nx = gx1 - b.Gx0 + 1;
                b.Ny = gy1 - b.Gy0 + 1;
                if (b.Nx < 3 || b.Ny < 3) return null;

                int n = b.Nx * b.Ny;
                b.IsLand = new bool[n];
                b.Elev = new double[n];
                b.GradMag = new double[n];
                b.GradX = new double[n];
                b.GradY = new double[n];
                b.HasGrad = new bool[n];
                b.CoastDist = new double[n];
                b.Shelter = new double[n];

                double seaLevel = field.Params.SeaLevel;
                for (int ix = 0; ix < b.Nx; ix++)
                {
                    double x = (b.Gx0 + ix) * b.Cell;
                    for (int iy = 0; iy < b.Ny; iy++)
                    {
                        int i = ix * b.Ny + iy;
                        double e;
                        double h01 = field.Sample(x, (b.Gy0 + iy) * b.Cell, out e);
                        b.IsLand[i] = h01 >= seaLevel;      // tie at SeaLevel is land (§4.4)
                        b.Elev[i] = e;
                    }
                }

                // Exact distance to the nearest coastline segment inside the shore band,
                // double.MaxValue outside it (the sweep fills the array with MaxValue itself).
                // This is the same sweep Settlements uses for its coast proximity test — see
                // Lattice.MarkCoastDistance.
                Lattice.MarkCoastDistance(coast, Tuning.PoiShoreBand,
                                          b.Gx0, b.Gy0, b.Nx, b.Ny, b.Cell, b.CoastDist);

                // Gradients are four field evaluations each, so they are sampled only where a
                // predicate can read one: land, or next to land, or inside the shore band.
                for (int ix = 1; ix < b.Nx - 1; ix++)
                {
                    for (int iy = 1; iy < b.Ny - 1; iy++)
                    {
                        int i = ix * b.Ny + iy;
                        if (!NeedsGradient(b, ix, iy)) continue;

                        double x = (b.Gx0 + ix) * b.Cell;
                        double y = (b.Gy0 + iy) * b.Cell;
                        V2 g = field.Gradient(x, y);
                        b.GradX[i] = Q.Grad(g.X);
                        b.GradY[i] = Q.Grad(g.Y);
                        b.GradMag[i] = Q.Grad(g.Length);
                        b.HasGrad[i] = true;
                    }
                }

                MarkShelter(b);
                return b;
            }

            static bool NeedsGradient(Block b, int ix, int iy)
            {
                int i = ix * b.Ny + iy;
                if (b.IsLand[i]) return true;
                if (b.CoastDist[i] <= Tuning.PoiShoreBand) return true;
                return b.IsLand[(ix + 1) * b.Ny + iy] || b.IsLand[(ix - 1) * b.Ny + iy]
                    || b.IsLand[ix * b.Ny + iy + 1] || b.IsLand[ix * b.Ny + iy - 1];
            }

            /// <summary>Shelter is only read by the two shore ruins, so it is measured only
            /// inside the shore band.</summary>
            static void MarkShelter(Block b)
            {
                Lattice.Offset[] disc = Lattice.Disc(Tuning.SettlementShelterRadius, b.Cell);

                for (int ix = 0; ix < b.Nx; ix++)
                {
                    for (int iy = 0; iy < b.Ny; iy++)
                    {
                        int i = ix * b.Ny + iy;
                        if (b.CoastDist[i] > Tuning.PoiShoreBand) continue;

                        int total = 0;
                        int land = 0;
                        for (int k = 0; k < disc.Length; k++)
                        {
                            int jx = ix + disc[k].Dx;
                            int jy = iy + disc[k].Dy;
                            if (jx < 0 || jx >= b.Nx || jy < 0 || jy >= b.Ny) continue;
                            total++;
                            if (b.IsLand[jx * b.Ny + jy]) land++;
                        }
                        if (total == 0) continue;
                        b.Shelter[i] = ShelterMeasure.FromLandFraction((double)land / total);
                    }
                }
            }
        }
    }
}
