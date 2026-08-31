namespace Archivist.Render
{
    /// <summary>
    /// §10. Every constant in one place, mirroring Tuning.cs's role for the generator.
    /// Defaults are starting points, not findings (T4.3).
    /// </summary>
    public static class RenderTuning
    {
        // --- resolution defaults (starting points only; B5 sweeps them) ---
        public const double IslandPreviewPxPerMetre = 0.10;
        public const double SheetPxPerPaperMm       = 2.7;    // ~68 dpi

        // --- stroke weights, PAPER millimetres (§7) ---
        public const double CoastWidthMm      = 0.35;

        /// <summary>Elevation contours: finer than the coast, because the coast is the one line
        /// on the sheet that is a boundary rather than a reading.</summary>
        public const double ContourWidthMm    = 0.18;

        /// <summary>The Garrison grid. Thinner still and drawn in a lighter ink: it is a
        /// reference laid over the map, not part of it (D4 / §6.4).</summary>
        public const double GridWidthMm       = 0.15;

        public const double RiverWidthMm      = 0.25;
        public const double SettlementMarkMm  = 1.2;
        public const double PeakMarkMm        = 1.6;
        public const double SoundingDotMm     = 0.5;

        /// <summary>
        /// How many of <see cref="LandBandEdges"/> carry a contour line, taken every
        /// <c>stride</c>-th edge from the lowest.
        ///
        /// <para><b>Not all seven.</b> Every level is a separate marching-squares pass over the
        /// plate, so seven levels is seven times the coastline's cost — and a plate carrying
        /// seven contours plus a coast is a grey wash, not a map. 2 gives four lines, which
        /// reads as terrain and costs four passes on the one office that draws them.</para>
        ///
        /// <para>The levels are <see cref="LandBandEdges"/> and not an interval of their own,
        /// deliberately: a contour then falls exactly where the fill changes colour, so the two
        /// halves of the same map agree by construction rather than by coincidence.</para>
        /// </summary>
        public const int    ContourLevelStride = 2;

        /// <summary>
        /// How far past an island's land bounds isolines are extracted, in metres.
        ///
        /// <para>A plate's paper covers far more ground than its island: measured, an island
        /// fills <b>17% of its chart</b> and 27% of a quarter, so most of the sheet is open sea
        /// and every corner of it was being sampled for contours that cannot exist there. Land
        /// is what carries an isoline; sea carries none.</para>
        ///
        /// <para><b>Isolines only.</b> Soundings live <i>below</i> sea level and so lie outside
        /// the land bounds by definition, and the Garrison grid must reach the paper's edge. The
        /// clip belongs to the one layer it is true of and must never move up into
        /// <c>Strokes.QueryRect</c>, which every layer reads.</para>
        ///
        /// <para>512 m, measured: compared vertex for vertex against the unclipped extraction
        /// over 20 islands x 13 plates, 512 m gave 260/260 identical and 256 m gave 259 — one
        /// islet, six vertices. Same shape of evidence as <c>Tuning.CoastlineMarginCells</c>,
        /// and the same caveat: an islet smaller than the 64 m lattice <c>ComputeLandBounds</c>
        /// samples on can fall between samples, so this is a threshold, not a proof.</para>
        /// </summary>
        public const double IsolineLandMarginM = 512.0;

        // --- land band edges, normalised t = elevation / norm (§6.3) ---
        public static readonly double[] LandBandEdges =
            { 0.02, 0.12, 0.28, 0.45, 0.62, 0.78, 0.92 };   // 8 bands

        // --- sea band edges, ABSOLUTE metres (§6.3, T2.3) ---
        // -4 is deliberately Tuning.SoundingDepth: the shallow-water colour edge and the
        // sounding cut-off are the same line.
        public static readonly double[] SeaBandEdges =
            { -120.0, -40.0, -4.0 };                        // 4 bands

        /// <summary>Minimum normalisation divisor, so a peakless island cannot divide by zero.</summary>
        public const double MinNormalisation = 1.0;

        // --- field sampling (F-02.1) ---
        // The fill samples the field on a coarse lattice and interpolates between, because
        // the sheets are hand-drawn surveys and micro differences are in fiction. Measured
        // on a 2.85 Mpx sheet: 800 ms -> 12 ms with parallelism, at 0.12-0.34% of land
        // pixels differing. See docs/poc02/findings.md.
        //
        // TWO ceilings, and both matter. The pixel step bounds error in IMAGE terms; the
        // ground ceiling bounds it in WORLD terms, so thin land does not survive at one
        // resolution and vanish at another. 24 m is ~1/100 of Tuning.FeatureScale, the
        // coastline's wiggle wavelength.
        public const int    FieldSampleStepPx   = 4;
        public const double FieldSampleCeilingM = 24.0;

        /// <summary>Below this many rows, thread setup costs more than it saves.</summary>
        public const int ParallelRowThreshold = 96;
    }
}
