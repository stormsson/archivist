namespace Archivist.Generation
{
    /// <summary>
    /// §12 of poc-01. Every constant in one place. Defaults are starting points, not findings.
    /// Nothing outside this class may invent a magic number that belongs here.
    /// </summary>
    public static class Tuning
    {
        // --- domain / field ---
        public const double DomainMetres      = 16000.0;
        public const double NominalRadiusFrac = 0.38;
        public const double NominalRadiusJitter = 0.08;   // +/- 8%
        public const double SeaLevel          = 0.50;
        public const double FeatureScale      = 2600.0;   // coastline wiggle wavelength
        public const double WarpAmp           = 0.45;
        public const int    FbmOctaves        = 5;
        public const double FbmLacunarity     = 2.0;
        public const double FbmGain           = 0.5;
        public const double MaxDepth          = 220.0;

        // --- lattice / detail ---
        public const double BaseCell          = 64.0;     // LOD lattice root
        public const double PaperDetailMm     = 0.25;     // target cell on paper
        public const int    MaxLod            = 8;
        public const double ContourStep       = 50.0;     // metres of elevation

        // --- quantisation (D3 / §4.4) ---
        public const double GradientStep      = 20.0;     // central difference h, metres

        // --- paper ---
        public const double SheetWidthMm      = 594.0;    // A1
        public const double SheetHeightMm     = 841.0;
        public const double SheetMarginMm     = 40.0;
        public const double OverlapFraction   = 0.20;

        // --- scales (§8.1, D5, F1) ---
        // Detail surveys moved 1:5000 -> 1:2500 (F1): at 1:5000 one sheet covered 9.78 km2
        // against islands holding 1-27 km2, so sheet economy sat at a median of 13 against
        // requirements §6.1's 30-60, and Land Survey's landFraction >= 0.60 was
        // geometrically unreachable on most islands. At 1:2500 the median is 30.
        public const int    DetailScaleDenominator = 2500;

        // Hydrographic works at a coarser scale than the terrain offices, and scale is
        // therefore a fourth office signal alongside style, rotation and coverage.
        // Not arbitrary: Hydrographic keeps every rect the coast crosses, so at 1:2500 it
        // produced 31 of 56 detail sheets on one island — more than half the archive
        // re-showing ground already filed three times. At 1:5000 it produces 12, and a
        // coastal reconnaissance genuinely IS small-scale work where a terrain survey is
        // not. R2.2 never tied surveys to a shared scale; R2.3 allows three or four fixed
        // values and the set is now exactly four: 2500, 5000, 25000, 50000.
        public const int    CoastalScaleDenominator = 5000;

        // --- grid (D4 / §6.4) ---
        // D4 gave two pitches: 1000 m at 1:25000 and 200 m at 1:5000. Both are exactly
        // 40 mm on paper, so the rule was always a paper-space one and the table was a
        // two-row sample of it. Stated as the rule, it extends to any scale (1:2500 ->
        // 100 m) instead of needing a new row each time a scale is added.
        public const double GridPitchPaperMm  = 40.0;

        // --- service rule (D1 / §7.4) ---
        public const double ServiceRadiusFrac = 0.25;     // u = NominalRadius / 4
        public const double ServedThreshold   = 0.50;
        public const double SoundingDepth     = -4.0;

        // --- cull (§10.3) ---
        public const double LandFractionMinLandSurvey = 0.60;
        public const double LandFractionMinGarrison   = 0.02;
        public const int    CullSampleGrid    = 16;       // 16x16 per rect

        // --- rotation (D2 / §10.1) ---
        public const double PcaIsotropyThreshold = 1.15;  // lambda1/lambda2 below this = degenerate
        public const double PcaCoastSampleFrac   = 0.25;  // step = u * this
        public const int    PcaLandMinPoints     = 64;

        // --- peaks (§7.1) ---
        public const double PeakElevationFrac = 0.35;     // of MaxElevation
        public const double PeakNmsRadius     = 400.0;
        public const int    PeakNamedCount    = 3;

        // --- settlements (§7.2) ---
        public const double SettlementLattice   = 128.0;
        public const double SettlementCoastDist = 300.0;
        public const double SettlementFlatGrad  = 0.04;   // m/m, quantised to 1e-4 first
        public const double SettlementShelterRadius = 600.0;
        public const double SettlementMinSpacing = 1200.0;
        public const double SettlementShelterWeight = 0.6;
        public const double SettlementFlatnessWeight = 0.4;

        // --- rivers (§7.3) ---
        public const double RiverStep        = 40.0;
        public const double RiverJitterRad   = 0.15;
        public const double RiverMergeDist   = 60.0;
        public const int    RiverMaxSteps    = 400;
        public const double RiverMinLength   = 800.0;

        // --- soundings (§6.3) ---
        public const double SoundingLattice  = 400.0;

        // --- peak lattice ---
        public const double PeakLattice      = 64.0;
    }
}
