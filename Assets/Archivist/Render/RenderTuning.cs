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
        public const double RiverWidthMm      = 0.25;
        public const double SettlementMarkMm  = 1.2;
        public const double PeakMarkMm        = 1.6;
        public const double SoundingDotMm     = 0.5;

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
