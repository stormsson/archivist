using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// §7.1. Peaks are discrete (§3.1): generated once per island, in a deterministic
    /// order, with stable ids. Nothing here touches a PRNG — peak placement is a pure
    /// function of the field, so it cannot be reshuffled by adding a feature type (§4.3).
    /// </summary>
    public static class Peaks
    {
        /// <summary>
        /// §7.1 steps 1-6. Samples <see cref="IHeightField.Elevation"/> on the
        /// <see cref="Tuning.PeakLattice"/> lattice over <paramref name="landBounds"/>, takes
        /// 8-neighbour local maxima at or above <see cref="Tuning.PeakElevationFrac"/> of
        /// MaxElevation, sorts by the total order (elevation desc, x asc, y asc), suppresses
        /// non-maxima at <see cref="Tuning.PeakNmsRadius"/>, and keeps at most
        /// <see cref="IslandParams.PeakCapFor"/>.
        /// <para>
        /// <c>Name</c> is left null. Naming the top <see cref="Tuning.PeakNamedCount"/> is a
        /// separate pass (§9) that rewrites the list with <see cref="Peak.WithName"/>.
        /// </para>
        /// </summary>
        public static List<Peak> Generate(IHeightField field, Rect2 landBounds)
        {
            if (field == null) throw new ArgumentNullException("field");

            List<Peak> result = new List<Peak>();
            if (landBounds.IsEmpty) return result;

            double cell = Tuning.PeakLattice;

            // Lattice indices measured from the domain origin (0,0), §6.2. Peaks are an
            // island-wide pass so no sheet ever re-derives them, but sharing the one global
            // lattice keeps every sampled position in the generator identical.
            int gx0 = (int)Math.Ceiling(landBounds.MinX / cell);
            int gx1 = (int)Math.Floor(landBounds.MaxX / cell);
            int gy0 = (int)Math.Ceiling(landBounds.MinY / cell);
            int gy1 = (int)Math.Floor(landBounds.MaxY / cell);

            int nx = gx1 - gx0 + 1;
            int ny = gy1 - gy0 + 1;
            if (nx < 3 || ny < 3) return result;   // no interior cell, so no 8-neighbourhood

            // --- step 1: sample the lattice -----------------------------------------
            double[] elev = new double[nx * ny];
            for (int ix = 0; ix < nx; ix++)
            {
                double x = (gx0 + ix) * cell;
                for (int iy = 0; iy < ny; iy++)
                {
                    double y = (gy0 + iy) * cell;
                    elev[ix * ny + iy] = field.Elevation(x, y);
                }
            }

            // --- step 2: 8-neighbour local maxima above the elevation floor ----------
            double floorM = Tuning.PeakElevationFrac * field.Params.MaxElevation;
            List<Candidate> candidates = new List<Candidate>();

            for (int ix = 1; ix < nx - 1; ix++)
            {
                for (int iy = 1; iy < ny - 1; iy++)
                {
                    double e = elev[ix * ny + iy];
                    if (e < floorM) continue;

                    bool isMax = true;
                    for (int dx = -1; dx <= 1 && isMax; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int jx = ix + dx, jy = iy + dy;
                            if (!Beats(e, ix, iy, elev[jx * ny + jy], jx, jy)) { isMax = false; break; }
                        }
                    }
                    if (!isMax) continue;

                    candidates.Add(new Candidate(new V2((gx0 + ix) * cell, (gy0 + iy) * cell), e));
                }
            }

            // --- step 3: the total order --------------------------------------------
            candidates.Sort(CompareCandidates);

            // --- steps 4 and 5: NMS, then the cap ------------------------------------
            double nms2 = Tuning.PeakNmsRadius * Tuning.PeakNmsRadius;
            int cap = IslandParams.PeakCapFor(field.Params.Character);

            List<Candidate> kept = new List<Candidate>();
            for (int i = 0; i < candidates.Count && kept.Count < cap; i++)
            {
                bool suppressed = false;
                for (int k = 0; k < kept.Count; k++)
                {
                    if (V2.DistSq(kept[k].Position, candidates[i].Position) < nms2) { suppressed = true; break; }
                }
                if (!suppressed) kept.Add(candidates[i]);
            }

            // --- step 6: ids in final order, spot heights to the metre ---------------
            for (int i = 0; i < kept.Count; i++)
            {
                result.Add(new Peak(new FeatureId(FeatureClass.Peak, i),
                                    kept[i].Position,
                                    (int)Q.Metre(kept[i].Elevation),
                                    null));
            }
            return result;
        }

        /// <summary>
        /// The total order of §7.1 step 3, lifted to lattice cells so that a plateau of exactly
        /// equal samples yields exactly one maximum — the (x asc, y asc) smallest — rather than
        /// none (strict &gt;) or all of them (&gt;=). Elevation is derived from the quantised
        /// Height01 (§4.4), so exact ties are reachable and must have a stated answer.
        /// </summary>
        static bool Beats(double ea, int ixa, int iya, double eb, int ixb, int iyb)
        {
            if (ea != eb) return ea > eb;
            if (ixa != ixb) return ixa < ixb;
            return iya < iyb;
        }

        /// <summary>§7.1 step 3's total order: elevation desc, then
        /// <see cref="TotalOrder.ByPosition"/>. Descending on the primary key, which is why the
        /// comparator is written out here rather than shared whole — <see cref="PoiSiting"/>'s
        /// primary key ascends.</summary>
        static int CompareCandidates(Candidate a, Candidate b)
        {
            if (a.Elevation != b.Elevation) return a.Elevation > b.Elevation ? -1 : 1;   // desc
            return TotalOrder.ByPosition(a.Position, b.Position);
        }

        readonly struct Candidate
        {
            public readonly V2 Position;
            public readonly double Elevation;
            public Candidate(V2 position, double elevation) { Position = position; Elevation = elevation; }
        }
    }
}
