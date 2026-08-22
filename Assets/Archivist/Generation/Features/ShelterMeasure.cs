namespace Archivist.Generation.Features
{
    /// <summary>
    /// The <c>shelter</c> term of POC-01 §7.2, extracted so the two callers that need it —
    /// <see cref="Settlements"/> and POC-03's <see cref="PoiSiting"/> (spec §1.2, RuinedJetty
    /// and RuinedChapel) — share one definition rather than two copies of an invented formula.
    ///
    /// <para>The formula itself is unchanged and is documented in full on
    /// <see cref="Settlements"/>: §7.2 asks for "coastline concavity in a 600 m neighbourhood"
    /// and never defines it, and §6 of <c>poc-01-decisions.md</c> records it as an open tuning
    /// choice. Only the arithmetic lives here; the land fraction is measured by each caller on
    /// its own lattice block.</para>
    /// </summary>
    public static class ShelterMeasure
    {
        /// <summary>27/4 — the normaliser that puts the maximum of <c>l^2 * (1-l)</c> at 1.0.
        /// Derived from the shape, not tuned, which is why it is not a <see cref="Tuning"/>
        /// entry.</summary>
        public const double Normaliser = 6.75;

        /// <summary>
        /// <c>clamp01(27/4 * land^2 * sea)</c>. Peaks at <c>land = 2/3</c> — land on two thirds
        /// of the horizon, water on one third, which is exactly a cove. 1.00 at a bay head,
        /// 0.84 on a straight coast, 0.50 on an exposed headland, 0.00 deep inland and at open
        /// sea.
        /// </summary>
        /// <param name="landFraction">Fraction of a 600 m disc around the point that is land.</param>
        public static double FromLandFraction(double landFraction)
        {
            double l = landFraction;
            double s = 1.0 - l;
            double v = Normaliser * l * l * s;
            if (v < 0.0) return 0.0;
            return v > 1.0 ? 1.0 : v;
        }
    }
}
