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

        // --- falloff (§5.3 recipes) ---
        // Three recipes, not one curve with three parameter sets: R1.7 asks for character to be
        // a different SHAPE each time, so the numbers below are read one recipe at a time and
        // are not interchangeable between them. Every value is a fraction of NominalRadius, so
        // they are scale-free and an island's character survives any DomainMetres.

        /// <summary>Mountainous: land is solid to 0.35 of the nominal radius, gone by 1.00.
        /// The late start is what makes the massif compact — a plateau, then one steep ramp.</summary>
        public const double MountainousEdge0 = 0.35;
        public const double MountainousEdge1 = 1.00;

        /// <summary>Fjorded: the same ramp started earlier, with an angular cut added to r.
        /// Starting at 0.30 rather than 0.35 leaves room for the cut to bite inland without
        /// the ramp bottoming out, which would flatten inlets back into a smooth coast.</summary>
        public const double FjordedEdge0 = 0.30;
        public const double FjordedEdge1 = 1.00;

        /// <summary>Fjorded: amplitude and angular frequency of the inlet cut. 0.18 of the
        /// nominal radius is deep enough for an inlet to reach past sea level and detach
        /// islets; 6 cuts per turn gives a handful of fjords rather than a crenellated rim.</summary>
        public const double FjordedCutAmplitude = 0.18;
        public const double FjordedCutFrequency = 6.00;

        /// <summary>Atoll: the ring sits at 0.62 of the nominal radius and is 0.14 wide either
        /// side. The width must stay well under the radius or the lagoon fills in and the
        /// recipe collapses to a disc — the two-loop coastline is the whole point (§6.1).</summary>
        public const double AtollRingRadius = 0.62;
        public const double AtollRingCore   = 0.00;
        public const double AtollRingWidth  = 0.14;

        // --- lattice / detail ---
        public const double BaseCell          = 64.0;     // LOD lattice root
        public const double PaperDetailMm     = 0.25;     // target cell on paper
        public const int    MaxLod            = 8;
        public const double ContourStep       = 50.0;     // metres of elevation

        // --- contouring (§6.1 / §6.2) ---

        /// <summary>§6.1 endpoint weld tolerance, as a fraction of cellSize. Relative rather than
        /// absolute so it means the same thing at every LOD; 1e-6 of a cell is far below any
        /// crossing two neighbouring cells can legitimately disagree by, and far above the
        /// rounding of the interpolation that produced the two endpoints.</summary>
        public const double WeldFraction      = 1e-6;

        /// <summary>Millimetres per metre, for the §6.2 paper-detail conversion. A unit factor,
        /// not a tuning value — named so that paper-to-ground arithmetic reads as a conversion
        /// instead of as a bare 1000 that could be mistaken for a magic number.</summary>
        public const double MmPerMetre        = 1000.0;

        // --- quantisation (D3 / §4.4) ---
        public const double GradientStep      = 20.0;     // central difference h, metres

        // --- paper ---
        public const double SheetWidthMm      = 594.0;    // A1
        public const double SheetHeightMm     = 841.0;
        public const double SheetMarginMm     = 40.0;
        public const double OverlapFraction   = 0.20;

        // --- Hydrographic coastal strip (D-H3) ---
        // 380 x 200 mm, map 350 x 170 mm -> 875 x 425 m of ground at 1:2500.
        //
        // (History: the strip was once 841 x 297 mm — A1's long edge by A3's short edge, map
        // area 801 x 257 mm, which is the 2002 x 642 m the paragraph below refers to. That
        // paper-convention format is gone; the numbers above are the live ones.)
        //
        // The length is set by the coast, not by paper convention. Tuning.FeatureScale is
        // 2600 m — the wavelength the coastline wiggles on — and a straight rectangle can
        // only track a curve if it spans well under one wavelength. At 2002 m the strips
        // cut across every bay; at ~1/3 of a wavelength they follow it.
        public const double StripWidthMm      = 380.0;
        public const double StripHeightMm     = 200.0;
        public const double StripMarginMm     = 15.0;

        // A survey works a STRETCH of coast, not the whole ring. Without this the office
        // circumnavigates every island exhaustively, which is both implausible and the
        // reason no ground was ever left unsurveyed (R1.8 / finding F8).
        // Tuned against the 10-15 sheet target: 0.50-0.85 gave a mean of 17.2. Half a coast
        // is also the more believable expedition — a season's work, not a circumnavigation.
        // An expedition works a REGION, not a fraction of each loop independently. Applying
        // an arc per loop made the office survey 30% of the main shore while charting remote
        // skerries end to end, because a loop too small to step across gets one sheet
        // covering all of it — an expedition no one would mount. The survey is now a disc:
        // a seeded anchor on the main coast, and everything within reach of it, main shore
        // and offshore rocks alike. Radius is a fraction of the land bbox diagonal.
        public const double CoastRegionRadiusMin = 0.34;
        public const double CoastRegionRadiusMax = 0.62;

        // How far seaward of the shoreline the strip sits, as a fraction of its depth.
        // 0 centres it on the coast, which puts half of every sheet over ground this
        // office does not chart; 0.3 leaves roughly a fifth of the strip inland.
        public const double CoastSeawardBias  = 0.30;

        /// <summary>Loops shorter than this are specks, not coastline. ~190 m across.</summary>
        public const double CoastMinLoopLength = 600.0;

        /// <summary>Minimum gap between sheet centres, as a fraction of the step.</summary>
        public const double CoastMinSheetSeparation = 0.75;

        // --- scales (§8.1, D5, F1) ---
        // Detail surveys moved 1:5000 -> 1:2500 (F1): at 1:5000 one sheet covered 9.78 km2
        // against islands holding 1-27 km2, so sheet economy sat at a median of 13 against
        // requirements §6.1's 30-60, and Land Survey's landFraction >= 0.60 was
        // geometrically unreachable on most islands. At 1:2500 the median is 30.
        public const int    DetailScaleDenominator = 2500;

        // Hydrographic works at 1:2500 — the SAME scale as the terrain offices. Scale is
        // therefore NOT an office signal: style, rotation and coverage are the three that
        // distinguish a coastal reconnaissance from a terrain survey, and a reader cannot
        // tell the two apart by denominator alone.
        //
        // (History, and why 1:5000 was once chosen: Hydrographic keeps every rect the coast
        // crosses, so at 1:2500 it once produced 31 of 56 detail sheets on one island — more
        // than half the archive re-showing ground already filed three times. At 1:5000 it
        // produced 12, and a coastal reconnaissance genuinely IS small-scale work where a
        // terrain survey is not, which made scale a fourth signal alongside the other three.
        // That differentiation was lost when this value moved back to 2500; the sheet-count
        // argument against 1:2500 still stands and is unaddressed here. Changing the value
        // back is a design decision, not a comment fix.)
        //
        // R2.2 never tied surveys to a shared scale; R2.3 allows three or four fixed values.
        // The live set is four: 1250 (PoiScaleDenominator), 2500, 25000, 50000 — 5000 is no
        // longer among them and nothing in the project draws at it.
        public const int    CoastalScaleDenominator = 2500;

        // Whole-island index sheets, and the fallback when even that will not fit. Both are
        // small-scale by construction: the point is one sheet covering the entire island, so
        // the denominator follows the largest island rather than any paper-detail rule.
        // 1:50000 exists only as the fallback — it is used when 1:25000 still overflows the
        // sheet, which is why the two are named apart instead of being one clamped value.
        public const int    WholeIslandScaleDenominator = 25000;
        public const int    WholeIslandFallbackScaleDenominator = 50000;

        // --- grid (D4 / §6.4) ---
        // D4 gave two pitches: 1000 m at 1:25000 and 200 m at 1:5000. Both are exactly
        // 40 mm on paper, so the rule was always a paper-space one and the table was a
        // two-row sample of it. Stated as the rule, it extends to any scale (1:2500 ->
        // 100 m) instead of needing a new row each time a scale is added. Only D4's first
        // row is a live case; nothing draws at 1:5000 any more, so that row is now just an
        // illustration of the rule.
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

        // =====================================================================
        // POC-03 — points of interest (spec §1) and detail sheets (spec §2).
        // =====================================================================

        /// <summary>Spec §1.2: "sample candidates on the 128 m lattice, exactly as settlements
        /// do". Same value as <see cref="SettlementLattice"/>, named separately so the two can
        /// be tuned apart without one silently moving the other.</summary>
        public const double PoiLattice        = 128.0;

        /// <summary>Spec §1.3 step 3 — greedy selection spacing, "so POIs do not cluster".
        /// Comfortably wider than a detail sheet at either candidate scale (275 m at 1:1250,
        /// 550 m at 1:2500), so no two detail sheets can show the same POI twice.</summary>
        public const double PoiMinSpacing     = 800.0;

        // --- POI count per island, by character (P1.4: "a few per island, varying by
        //     character"; an island with none at all is a legitimate outcome, which is what
        //     the atoll minimum of 0 buys). Inclusive min, exclusive max, matching
        //     IslandParams.SettlementRangeFor.
        public const int PoiCountMountainousMin = 3;
        public const int PoiCountMountainousMax = 8;    // 3-7
        public const int PoiCountFjordedMin     = 3;
        public const int PoiCountFjordedMax     = 9;    // 3-8
        public const int PoiCountAtollMin       = 0;
        public const int PoiCountAtollMax       = 4;    // 0-3

        /// <summary>How close to a coastline polyline counts as "on the coast" for the shore
        /// kinds. One lattice cell — the finest band the 128 m candidate lattice can resolve
        /// without the shore kinds becoming unsatisfiable.</summary>
        public const double PoiShoreBand      = 128.0;

        /// <summary>"High local relief — steep shore" (spec §1.2). Quantised slope, m/m.</summary>
        public const double PoiSteepShoreGrad = 0.14;

        /// <summary>A sea arch wants the most vertical shore on the island, not merely a steep
        /// one, which is what separates it from a cave mouth on the same band.</summary>
        public const double PoiSeaArchGrad    = 0.22;

        /// <summary>Spec §1.2: Blowhole sits "on land within ~60 m of the coast".</summary>
        public const double PoiBlowholeCoastDist = 60.0;

        /// <summary>Springs sit above the splash zone, not on the beach.</summary>
        public const double PoiSpringMinElevation = 20.0;

        /// <summary>
        /// Spec §1.2's "local gradient convergence", as the quantised discrete Laplacian of
        /// elevation across one POI lattice cell — see <c>PoiSiting.Convergence</c>. Units are
        /// m/m summed over both axes, so this is a pure slope difference and not a curvature
        /// in m^-1. Flow converges where the value is positive; the floor keeps a spring off
        /// dead-flat ground where the sign is noise.
        /// </summary>
        public const double PoiSpringConvergence = 0.0060;

        /// <summary>"Open ground, low gradient" / "low gradient" (spec §1.2), m/m.</summary>
        public const double PoiOpenGrad       = 0.06;

        /// <summary>"Flat ground, low gradient" — standing stones want flatter than open, m/m.</summary>
        public const double PoiFlatGrad       = 0.03;

        /// <summary>"Moderate slope, inland" — an enclosure is terraced ground, m/m.</summary>
        public const double PoiModerateGradMin = 0.06;
        public const double PoiModerateGradMax = 0.20;

        /// <summary>Erratic boulders sit at mid elevation, as a fraction of MaxElevation.</summary>
        public const double PoiErraticElevMinFrac = 0.20;
        public const double PoiErraticElevMaxFrac = 0.60;

        /// <summary>A landmark tree sits low-to-mid, as a fraction of MaxElevation.</summary>
        public const double PoiTreeElevMinFrac    = 0.05;
        public const double PoiTreeElevMaxFrac    = 0.40;

        /// <summary>"Away from settlements" — a landmark tree is a landmark because nothing
        /// else near it is.</summary>
        public const double PoiTreeSettlementDist = 1000.0;

        /// <summary>Spec §1.2: RuinedChapel sits "within ~1 km of a settlement".</summary>
        public const double PoiChapelSettlementDist = 1000.0;

        /// <summary>"On or beside a peak — commanding ground". Matches
        /// <see cref="PeakNmsRadius"/>, so "beside" is one peak-suppression radius.</summary>
        public const double PoiTowerPeakDist  = 400.0;

        /// <summary>Spec §1.2: Cairn sits at or above this fraction of the island's highest
        /// peak.</summary>
        public const double PoiCairnPeakFrac  = 0.50;

        /// <summary>A headland is exposed: shelter at or below this (see
        /// <see cref="ShelterMeasure"/>; 0.50 is an exposed headland, 0.84 a straight coast).</summary>
        public const double PoiHeadlandShelterMax = 0.55;

        /// <summary>A jetty needs a sheltered coast (spec §1.2, "reuse §7.2's shelter
        /// measure"); 1.00 is a bay head, 0.84 a straight coast.</summary>
        public const double PoiJettyShelterMin = 0.88;

        // --- detail sheets (spec §2.1) ---

        /// <summary>250 x 250 mm paper, 15 mm margin -> a 220 x 220 mm map area (spec §2.1).
        /// Square, small, and unmistakably a different physical object from an A1 survey sheet
        /// or a coastal strip (P2.1).</summary>
        public const double DetailSheetWidthMm  = 250.0;
        public const double DetailSheetHeightMm = 250.0;
        public const double DetailSheetMarginMm = 15.0;

        /// <summary>
        /// <b>THE SWEEP KNOB.</b> Spec §2.1 gives 1:1250 (275 x 275 m) and 1:2500 (550 x 550 m)
        /// and says explicitly <i>do not pick one from this table</i>: open question 1 says the
        /// whole design rests on this number and that it cannot be reasoned about, only looked
        /// at (C7). It ships as a constant precisely so the sweep is a one-line change and no
        /// literal is buried in the cutter.
        ///
        /// <para>The value below is the SWEEP DEFAULT, not a finding. 1:1250 is the tighter of
        /// the two and therefore the one that actually tests the premise — a sheet roomy enough
        /// to place itself proves nothing. R2.3 permits three or four fixed scale values; the
        /// live set is 2500 / 25000 / 50000, so 1250 keeps it legal at four and 2500 keeps it
        /// at three. Either value is legal; neither is decided.</para>
        /// </summary>
        public const int PoiScaleDenominator = 1250;
    }
}
