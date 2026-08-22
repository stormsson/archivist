using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// §10.3's cull sampler, stated once. Both cutters test a candidate sheet by walking a
    /// <c>Tuning.CullSampleGrid x Tuning.CullSampleGrid</c> (16x16) lattice of CELL CENTRES
    /// over it — so the samples are symmetric about the rect and no sample sits on an edge
    /// shared with the neighbouring sheet — and counting land samples and served samples.
    ///
    /// <para>
    /// What the two cutters do NOT share is the space they sample in: the lattice cutter
    /// (§10.2) has a survey frame and an axis-aligned rect within it, while the coast walk
    /// has a centre and a per-sheet rotation. That difference lives in the
    /// <see cref="SamplePoint"/> each caller supplies, which maps a lattice index pair to a
    /// GROUND point. The arithmetic that turns an index into a coordinate is deliberately
    /// left at the call sites: the two forms are algebraically equal but not bit-equal, and
    /// an ulp here moves a rect across the sheet lattice and changes the sheet count (§4.4).
    /// </para>
    ///
    /// <para>
    /// Land and service come from the same 256 samples, so the service test costs no extra
    /// field evaluation (D1's implementation note).
    /// </para>
    /// </summary>
    public static class RectCull
    {
        /// <summary>
        /// Ground position of lattice cell <c>(a, b)</c>, both in <c>[0, CullSampleGrid)</c>.
        /// </summary>
        public delegate V2 SamplePoint(int a, int b);

        /// <summary>
        /// Counts, over the 16x16 lattice, how many samples are land and how many of those
        /// are served for <paramref name="office"/> (§7.4). A null
        /// <paramref name="service"/> means "no service rule in force": every land sample
        /// counts as served.
        ///
        /// <para>
        /// The counters are integers, so the order the lattice is walked in cannot affect
        /// the answer; each caller keeps whichever nesting it already had.
        /// </para>
        /// </summary>
        public static void Count(IHeightField field, ServiceRule service, Office office,
                                 SamplePoint point, out int landCount, out int servedCount)
        {
            int n = Tuning.CullSampleGrid;
            landCount = 0;
            servedCount = 0;

            for (int b = 0; b < n; b++)
            {
                for (int a = 0; a < n; a++)
                {
                    V2 ground = point(a, b);

                    // Tie at exactly SeaLevel counts as land (§4.4) — the IsLand extension
                    // states that once, and Height01 is quantised (D3) so the comparison is
                    // exact either side of the threshold.
                    if (!field.IsLand(ground)) continue;
                    landCount++;

                    if (service == null || service.Served(ground, office)) servedCount++;
                }
            }
        }

        /// <summary>
        /// Fraction of the LAND samples that are served. With no land samples at all this is
        /// 0 — such a rect is pure sea, and 0 is below <see cref="Tuning.ServedThreshold"/>,
        /// so <see cref="MeetsServedThreshold"/> rejects it without a special case.
        /// </summary>
        public static double ServedFraction(int landCount, int servedCount)
        {
            return landCount > 0 ? (double)servedCount / landCount : 0.0;
        }

        /// <summary>D1's uniform service test, applied identically by both cutters.</summary>
        public static bool MeetsServedThreshold(double servedFraction)
        {
            return servedFraction >= Tuning.ServedThreshold;
        }
    }
}
