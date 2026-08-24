namespace Archivist.Render
{
    /// <summary>
    /// §6.3 — elevation to band index. Sea and land share ONE flat index space so the
    /// palette is a single array and a band is a single array lookup (§6.4).
    ///
    /// <code>
    ///   0 deep      1 offshore   2 shallow    3 foreshore     &lt;- sea,  absolute metres
    ///   4 shore     5 lowland    6 rising     7 mid
    ///   8 upper     9 high      10 bare      11 summit        &lt;- land, normalised t
    /// </code>
    ///
    /// <para><b>Sea is absolute (T2.3).</b> <c>Tuning.MaxDepth</c> is a global 220 m for every
    /// character, so depth is already comparable across islands; normalising it would destroy
    /// the only globally meaningful colour axis this POC has.</para>
    ///
    /// <para><b>Land is normalised per island (T2.2).</b> <c>t = elevation / normalisation</c>,
    /// clamped to <c>[0,1]</c>, against the island's own highest peak (§6.2), so a 90 m atoll
    /// uses the whole ramp and reads as varied. The recorded cost is T2.2a: the same green is
    /// 90 m on an atoll and 600 m on a mountain.</para>
    ///
    /// <para><b>Bands are discrete (T2.1).</b> No interpolation between them, no dithering.
    /// Hard band edges are what a hypsometric map looks like — a map, not a terrain render —
    /// so this is correct rather than a limitation. Only strokes are anti-aliased (§7).</para>
    ///
    /// <para>Determinism (§5): band selection compares against an already-quantised value —
    /// <c>Height01</c> is quantised at <c>2^-16</c> upstream and <c>Elevation</c> derives from
    /// it — so a band index is exactly as reproducible as the field. Pure and stateless, hence
    /// order-independent (T4.4).</para>
    /// </summary>
    public static class Bands
    {
        /// <summary>Sea bands, deep -&gt; foreshore, occupying indices 0..3.</summary>
        public const int SeaBandCount = 4;

        /// <summary>Land bands, shore -&gt; summit, occupying indices 4..11.</summary>
        public const int LandBandCount = 8;

        /// <summary>Size of the palette index space (§6.4).</summary>
        public const int Count = SeaBandCount + LandBandCount;

        /// <summary>
        /// §6.3. Band index for one sample.
        /// </summary>
        /// <param name="elevationMetres">
        /// <c>IHeightField.Elevation</c> at the sample: metres, negative below sea.
        /// </param>
        /// <param name="normalisation">
        /// The island's normalisation divisor from <c>IslandRenderer.Normalisation</c> (§6.2).
        /// Resolved once per island and passed down; never recomputed per pixel or per sheet,
        /// or two sheets of one island could normalise differently and stop cohering.
        /// Land only — the sea path ignores it (T2.3).
        /// </param>
        /// <param name="isLand">
        /// The <c>Height01 &gt;= Params.SeaLevel</c> test, NOT <c>elevation &gt;= 0</c>. §4.4 of
        /// the generator spec states the tie at exactly <c>SeaLevel</c> counts as land, and
        /// every other consumer in the codebase uses that test; the caller supplies it so the
        /// fill agrees with the coastline stroke by construction (§6.1).
        /// </param>
        /// <returns>An index in <c>[0, Count)</c>, directly indexable into a palette.</returns>
        public static int Index(double elevationMetres, double normalisation, bool isLand)
        {
            if (!isLand)
            {
                return SeaIndex(elevationMetres);
            }
            return SeaBandCount + LandIndex(elevationMetres, normalisation);
        }

        /// <summary>
        /// §6.3 sea table, ABSOLUTE metres (T2.3): deep below -120 m, offshore to -40 m,
        /// shallow to -4 m, foreshore above that. The -4 m edge is <c>Tuning.SoundingDepth</c>,
        /// so the shallow-water colour boundary and the sounding cut-off are the same line and
        /// a Hydrographic sheet's soundings sit exactly where its water colour changes.
        /// </summary>
        static int SeaIndex(double elevationMetres)
        {
            double[] edges = RenderTuning.SeaBandEdges;
            int band = 0;
            while (band < edges.Length && elevationMetres >= edges[band])
            {
                band++;
            }
            return band;
        }

        /// <summary>
        /// §6.3 land table, normalised <c>t = elevation / normalisation</c> clamped to
        /// <c>[0,1]</c> (T2.2). The clamp is what makes the band count fixed regardless of a
        /// sample sitting a little above the island's recorded highest peak.
        /// </summary>
        static int LandIndex(double elevationMetres, double normalisation)
        {
            // Same clamp IslandRenderer.Normalisation already applies (§6.2), repeated here so
            // this stays total for any caller: a peakless island must never divide by zero, and
            // a NaN divisor must not produce a NaN index.
            double denom = normalisation >= RenderTuning.MinNormalisation
                ? normalisation
                : RenderTuning.MinNormalisation;

            double t = elevationMetres / denom;
            if (!(t > 0.0)) { t = 0.0; }        // written to also swallow NaN
            else if (t > 1.0) { t = 1.0; }

            double[] edges = RenderTuning.LandBandEdges;
            int band = 0;
            while (band < edges.Length && t >= edges[band])
            {
                band++;
            }
            return band;
        }
    }
}
