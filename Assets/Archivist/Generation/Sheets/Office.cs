namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// Three offices in v1, chosen because they disagree most (§5.1 of requirements), plus
    /// POC-03's fourth.
    ///
    /// <para><b>Antiquarian</b> is the antiquarian / natural-history arm of the expedition: the
    /// party that records curiosities — ruined works and natural oddities alike — rather than
    /// charting water, measuring ground or siting batteries. It is the only office that draws
    /// <see cref="Features.FeatureClass.Poi"/>, and the only one that ships detail sheets
    /// instead of a tiled survey.</para>
    ///
    /// <para><b>Deviation from POC-03 spec §3, decided by the project owner.</b> §3 / P3.1 gave
    /// POIs to the existing three offices by type. They instead get their own office, so P3.1
    /// and §3's <c>DrawsPoi(office, kind)</c> are superseded: with one POI office there is no
    /// kind-dependent row to express, and <c>Draws(office, Poi)</c> is a plain lookup again.
    /// P3.3's blind-spot asymmetry survives in a different form — the Antiquarian office draws
    /// no grid and no soundings, so its sheets are not Garrison's or Hydrographic's.</para>
    ///
    /// <para><b>Append only.</b> Several streams are indexed by <c>(int)office</c> —
    /// <c>Streams.For(seed, "year", (int)office)</c> among them — so renumbering a member
    /// rewrites existing islands.</para>
    /// </summary>
    public enum Office
    {
        Hydrographic = 0,
        LandSurvey   = 1,
        Garrison     = 2,
        Antiquarian  = 3
    }

    /// <summary>
    /// The office list, in one place. Any site that would otherwise enumerate the offices with
    /// an inline <c>new[] { Hydrographic, LandSurvey, Garrison }</c> reads this instead, so adding
    /// a fifth office cannot be silently ignored by a caller that forgot to grow its array.
    /// </summary>
    public static class Offices
    {
        /// <summary>Number of members of <see cref="Office"/>. Sizes every per-office array.</summary>
        public const int Count = 4;

        /// <summary>Every office, in enum order. A stable array, never enum reflection (§4.1).</summary>
        public static readonly Office[] All =
        {
            Office.Hydrographic, Office.LandSurvey, Office.Garrison, Office.Antiquarian
        };

        /// <summary>
        /// True for the offices that cut a tiled survey over the island. False for
        /// <see cref="Office.Antiquarian"/>, which cuts one small detail sheet per qualifying
        /// POI (POC-03 spec §2) and therefore has no rotation, no lattice and no cull.
        /// </summary>
        public static bool CutsSurvey(Office office) { return office != Office.Antiquarian; }
    }
}
