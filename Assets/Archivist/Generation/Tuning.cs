namespace Archivist.Generation
{
    /// <summary>
    /// §12 of poc-01. Every constant in one place. Defaults are starting points, not findings.
    /// Nothing outside this class may invent a magic number that belongs here.
    ///
    /// <para><b>These are the DEFAULTS, not the live values.</b> Each was a <c>const</c> until
    /// tuning became configurable; a <c>const</c> is a compile-time literal the compiler inlines
    /// into every call site, so nothing could ever configure one. The literal and the
    /// reasoning that produced it stay here, as <c>Default*</c>; the value the generator actually
    /// reads — <c>Tuning.OverlapFraction</c>, unchanged at every call site — lives in the
    /// generated <c>Tuning.Values.cs</c>, is read once from <c>config/generation.yml</c> at
    /// type-initialisation, and is frozen for the life of the process.</para>
    ///
    /// <para><b>Editing a number here changes the default, which is what applies when no config
    /// file is found</b> — a player build, a fresh clone, the acceptance harness on a machine
    /// where nobody has written one. It does not change what a machine with a config file
    /// generates. Both facts matter: the defaults must stay the documented, reasoned values, and
    /// the file must stay an override sheet rather than a second source of truth.</para>
    /// </summary>
    public static partial class Tuning
    {
        // --- domain / field ---
        public const double DefaultDomainMetres = 16000.0;
        public const double DefaultNominalRadiusFrac = 0.38;
        public const double DefaultNominalRadiusJitter = 0.08;   // +/- 8%
        public const double DefaultSeaLevel = 0.50;
        public const double DefaultFeatureScale = 2600.0;   // coastline wiggle wavelength
        public const double DefaultWarpAmp = 0.45;
        public const int    DefaultFbmOctaves = 5;
        public const double DefaultFbmLacunarity = 2.0;
        public const double DefaultFbmGain = 0.5;
        public const double DefaultMaxDepth = 220.0;

        // --- falloff (§5.3 recipes) ---
        // Three recipes, not one curve with three parameter sets: R1.7 asks for character to be
        // a different SHAPE each time, so the numbers below are read one recipe at a time and
        // are not interchangeable between them. Every value is a fraction of NominalRadius, so
        // they are scale-free and an island's character survives any DomainMetres.

        /// <summary>Mountainous: land is solid to 0.35 of the nominal radius, gone by 1.00.
        /// The late start is what makes the massif compact — a plateau, then one steep ramp.</summary>
        public const double DefaultMountainousEdge0 = 0.35;
        public const double DefaultMountainousEdge1 = 1.00;

        /// <summary>Fjorded: the same ramp started earlier, with an angular cut added to r.
        /// Starting at 0.30 rather than 0.35 leaves room for the cut to bite inland without
        /// the ramp bottoming out, which would flatten inlets back into a smooth coast.</summary>
        public const double DefaultFjordedEdge0 = 0.30;
        public const double DefaultFjordedEdge1 = 1.00;

        /// <summary>Fjorded: amplitude and angular frequency of the inlet cut. 0.18 of the
        /// nominal radius is deep enough for an inlet to reach past sea level and detach
        /// islets; 6 cuts per turn gives a handful of fjords rather than a crenellated rim.</summary>
        public const double DefaultFjordedCutAmplitude = 0.18;
        public const double DefaultFjordedCutFrequency = 6.00;

        /// <summary>Atoll: the ring sits at 0.62 of the nominal radius and is 0.14 wide either
        /// side. The width must stay well under the radius or the lagoon fills in and the
        /// recipe collapses to a disc — the two-loop coastline is the whole point (§6.1).</summary>
        public const double DefaultAtollRingRadius = 0.62;
        public const double DefaultAtollRingCore = 0.00;
        public const double DefaultAtollRingWidth = 0.14;

        // --- lattice / detail ---
        public const double DefaultBaseCell = 64.0;     // LOD lattice root
        public const double DefaultPaperDetailMm = 0.25;     // target cell on paper
        public const int    DefaultMaxLod = 8;
        public const double DefaultContourStep = 50.0;     // metres of elevation

        // --- contouring (§6.1 / §6.2) ---

        /// <summary>§6.1 endpoint weld tolerance, as a fraction of cellSize. Relative rather than
        /// absolute so it means the same thing at every LOD; 1e-6 of a cell is far below any
        /// crossing two neighbouring cells can legitimately disagree by, and far above the
        /// rounding of the interpolation that produced the two endpoints.</summary>
        public const double DefaultWeldFraction = 1e-6;

        /// <summary>Millimetres per metre, for the §6.2 paper-detail conversion. A unit factor,
        /// not a tuning value — named so that paper-to-ground arithmetic reads as a conversion
        /// instead of as a bare 1000 that could be mistaken for a magic number.</summary>
        public const double DefaultMmPerMetre = 1000.0;

        /// <summary>
        /// The finest LOD <see cref="Geometry.Contours.LodForScale"/> will ask for — cell 4 m.
        ///
        /// <para><b>The field is band-limited, and §6.2's paper rule does not know it.</b>
        /// <c>LodForScale</c> derives detail from paper alone (<see cref="PaperDetailMm"/> times
        /// the scale denominator), which assumes the coastline has structure at every scale. It
        /// does not: the field is a 5-octave fbm on a <see cref="FeatureScale"/> of 2600 m, so
        /// its finest real wavelength is ~160 m and there is nothing left to resolve below a few
        /// metres. Past that, halving the cell quadruples the work and returns the same line.</para>
        ///
        /// <para>Measured on one 1:2500 sheet (963 x 671 m), against lod 7 as ground truth:</para>
        /// <code>
        /// lod 2 (16 m)    124 verts     3 ms    max deviation 1.094 m   = 0.44  mm on paper
        /// lod 3 ( 8 m)    242 verts    11 ms                   0.241 m   = 0.096 mm
        /// lod 4 ( 4 m)    477 verts    42 ms                   0.214 m   = 0.086 mm
        /// lod 5 ( 2 m)    948 verts   165 ms                   0.214 m
        /// lod 7 (0.5 m)  3655 verts  2556 ms                   0
        /// </code>
        /// <para>Accuracy plateaus at lod 3; lod 7 costs 256x lod 3 for 0.03 m. Both are already
        /// finer than <see cref="PaperDetailMm"/> can print. Lod 4 is chosen over lod 3 for line
        /// SMOOTHNESS rather than accuracy — it doubles the vertex count, so the drawn chords are
        /// ~3 m rather than ~6 m of ground, and the two cost 42 ms against 11 ms.</para>
        ///
        /// <para><b>This must NOT be applied to <c>RenderLod.ForGroundCell</c>.</b> That path ties
        /// the cell to the PIXEL on purpose: the fill computes its water edge per pixel from the
        /// analytic field, and a coarser contour makes the stroke float off the water it edges
        /// (the warning on <c>Strokes.DrawCoast</c>). There the fine LOD buys agreement between
        /// two renderings, not coastline detail, and the argument above does not apply.</para>
        /// </summary>
        public const int    DefaultMaxPaperContourLod = 4;

        /// <summary>
        /// How far past the land bounds the island-scale coastline is extracted, in
        /// <see cref="BaseCell"/> units. 4 cells = 256 m.
        ///
        /// <para><b>Why the coastline is not extracted over the whole domain.</b> It used to be,
        /// and it was 68.6% of the cost of generating an island — 500 x 500 cells over 256 km²
        /// of which the land bounds average 42.8 km², so five sixths of the scan was open sea.
        /// <c>ComputeLandBounds</c> already runs first, so the bounds are free.</para>
        ///
        /// <para><b>Why a margin at all, and why this one.</b> <c>ComputeLandBounds</c> samples
        /// on the <see cref="BaseCell"/> lattice, so an islet smaller than that spacing can fall
        /// between samples and never enter the bounds — and its coastline loop then lies outside
        /// them. Measured against the full-domain extraction over 30 islands, vertex for vertex:
        /// 1 cell misses five, 2 cells miss one, <b>4 cells miss none</b>, and 8 and 16 cells
        /// also miss none while giving back the saving. See <c>rework1/03-findings.md</c> F-R4.3.</para>
        ///
        /// <para><b>A threshold, not a guarantee.</b> Nothing bounds how far offshore an
        /// unsampled islet can sit, so raising this cannot be proved unnecessary — but an islet
        /// outside the land bounds is not drawn on any plate (Q1.1), so it exists in the data and
        /// nowhere else.</para>
        /// </summary>
        public const int    DefaultCoastlineMarginCells = 4;

        // --- quantisation (D3 / §4.4) ---
        public const double DefaultGradientStep = 20.0;     // central difference h, metres

        // --- paper ---
        public const double DefaultSheetWidthMm = 594.0;    // A1
        public const double DefaultSheetHeightMm = 841.0;
        public const double DefaultSheetMarginMm = 40.0;


        /// <summary>The fine rung of the quarter ladder (Q1.6). A quarter of even a small
        /// island is kilometres across, and 1:2500 on an A1 covers only 1285 x 1902 m.</summary>
        public const int    DefaultQuarterScaleFineDenominator = 5000;

        /// <summary>The middle rung of the quarter ladder (Q1.6). A quarter has to fit one A1
        /// map area; three rungs — 5000, 10000, 25000 — keep R2.3's "three or four fixed
        /// values" and give a small island, an ordinary one and a large one each a scale that
        /// fills its sheet.</summary>
        public const int    DefaultQuarterScaleDenominator = 10000;

        /// <summary>Whole-island index sheets, and the fallback when even that will not fit.
        /// Both are small-scale by construction: the point is one sheet covering the entire
        /// island, so the denominator follows the largest island rather than any paper-detail
        /// rule. 1:50000 is reached only when 1:25000 still overflows the sheet, which is why
        /// the two are named apart instead of being one clamped value.</summary>
        public const int    DefaultWholeIslandScaleDenominator = 25000;
        public const int    DefaultWholeIslandFallbackScaleDenominator = 50000;

        // --- grid (D4 / §6.4) ---
        // D4's two pitches — 1000 m at 1:25000, 200 m at 1:5000 — are both exactly 40 mm on
        // paper, so the rule is a paper-space one and the table was a two-row sample of it.
        // Stated as the rule it holds at any scale, and both of D4's rows are live: 1:25000
        // is the coarse rung and 1:5000 the fine one.
        public const double DefaultGridPitchPaperMm = 40.0;

        // --- service rule (D1 / §7.4) ---
        public const double DefaultServiceRadiusFrac = 0.25;     // u = NominalRadius / 4
        public const double DefaultServedThreshold = 0.50;
        public const double DefaultSoundingDepth = -4.0;

        /// <summary>Sampling density for a rect, as an n x n lattice. Its one caller is
        /// <c>Editor/IslandDebugWindow</c>, which samples a sheet this way to say whether it is
        /// worth drawing; 16 x 16 is the right density for that.</summary>
        public const int    DefaultCullSampleGrid = 16;       // 16x16 per rect


        // --- peaks (§7.1) ---
        public const double DefaultPeakElevationFrac = 0.35;     // of MaxElevation
        public const double DefaultPeakNmsRadius = 400.0;
        public const int    DefaultPeakNamedCount = 3;

        // --- settlements (§7.2) ---
        public const double DefaultSettlementLattice = 128.0;
        public const double DefaultSettlementCoastDist = 300.0;
        public const double DefaultSettlementFlatGrad = 0.04;   // m/m, quantised to 1e-4 first
        public const double DefaultSettlementShelterRadius = 600.0;
        public const double DefaultSettlementMinSpacing = 1200.0;
        public const double DefaultSettlementShelterWeight = 0.6;
        public const double DefaultSettlementFlatnessWeight = 0.4;

        // --- rivers (§7.3) ---
        public const double DefaultRiverStep = 40.0;
        public const double DefaultRiverJitterRad = 0.15;
        public const double DefaultRiverMergeDist = 60.0;
        public const int    DefaultRiverMaxSteps = 400;
        public const double DefaultRiverMinLength = 800.0;

        // --- soundings (§6.3) ---
        public const double DefaultSoundingLattice = 400.0;

        // --- peak lattice ---
        public const double DefaultPeakLattice = 64.0;

        // =====================================================================
        // POC-03 — points of interest (spec §1) and detail sheets (spec §2).
        // =====================================================================

        /// <summary>Spec §1.2: "sample candidates on the 128 m lattice, exactly as settlements
        /// do". Same value as <see cref="SettlementLattice"/>, named separately so the two can
        /// be tuned apart without one silently moving the other.</summary>
        public const double DefaultPoiLattice = 128.0;

        /// <summary>Spec §1.3 step 3 — greedy selection spacing, "so POIs do not cluster".
        /// Comfortably wider than a detail sheet at either candidate scale (275 m at 1:1250,
        /// 550 m at 1:2500), so no two detail sheets can show the same POI twice.</summary>
        public const double DefaultPoiMinSpacing = 800.0;

        // --- POI count per island, by character (P1.4: "a few per island, varying by
        //     character"; an island with none at all is a legitimate outcome, which is what
        //     the atoll minimum of 0 buys). Inclusive min, exclusive max, matching
        //     IslandParams.SettlementRangeFor.
        public const int    DefaultPoiCountMountainousMin = 3;
        public const int    DefaultPoiCountMountainousMax = 8;    // 3-7
        public const int    DefaultPoiCountFjordedMin = 3;
        public const int    DefaultPoiCountFjordedMax = 9;    // 3-8
        public const int    DefaultPoiCountAtollMin = 0;
        public const int    DefaultPoiCountAtollMax = 4;    // 0-3

        /// <summary>How close to a coastline polyline counts as "on the coast" for the shore
        /// kinds. One lattice cell — the finest band the 128 m candidate lattice can resolve
        /// without the shore kinds becoming unsatisfiable.</summary>
        public const double DefaultPoiShoreBand = 128.0;

        /// <summary>"High local relief — steep shore" (spec §1.2). Quantised slope, m/m.</summary>
        public const double DefaultPoiSteepShoreGrad = 0.14;

        /// <summary>A sea arch wants the most vertical shore on the island, not merely a steep
        /// one, which is what separates it from a cave mouth on the same band.</summary>
        public const double DefaultPoiSeaArchGrad = 0.22;

        /// <summary>Spec §1.2: Blowhole sits "on land within ~60 m of the coast".</summary>
        public const double DefaultPoiBlowholeCoastDist = 60.0;

        /// <summary>Springs sit above the splash zone, not on the beach.</summary>
        public const double DefaultPoiSpringMinElevation = 20.0;

        /// <summary>
        /// Spec §1.2's "local gradient convergence", as the quantised discrete Laplacian of
        /// elevation across one POI lattice cell — see <c>PoiSiting.Convergence</c>. Units are
        /// m/m summed over both axes, so this is a pure slope difference and not a curvature
        /// in m^-1. Flow converges where the value is positive; the floor keeps a spring off
        /// dead-flat ground where the sign is noise.
        /// </summary>
        public const double DefaultPoiSpringConvergence = 0.0060;

        /// <summary>"Open ground, low gradient" / "low gradient" (spec §1.2), m/m.</summary>
        public const double DefaultPoiOpenGrad = 0.06;

        /// <summary>"Flat ground, low gradient" — standing stones want flatter than open, m/m.</summary>
        public const double DefaultPoiFlatGrad = 0.03;

        /// <summary>"Moderate slope, inland" — an enclosure is terraced ground, m/m.</summary>
        public const double DefaultPoiModerateGradMin = 0.06;
        public const double DefaultPoiModerateGradMax = 0.20;

        /// <summary>Erratic boulders sit at mid elevation, as a fraction of MaxElevation.</summary>
        public const double DefaultPoiErraticElevMinFrac = 0.20;
        public const double DefaultPoiErraticElevMaxFrac = 0.60;

        /// <summary>A landmark tree sits low-to-mid, as a fraction of MaxElevation.</summary>
        public const double DefaultPoiTreeElevMinFrac = 0.05;
        public const double DefaultPoiTreeElevMaxFrac = 0.40;

        /// <summary>"Away from settlements" — a landmark tree is a landmark because nothing
        /// else near it is.</summary>
        public const double DefaultPoiTreeSettlementDist = 1000.0;

        /// <summary>Spec §1.2: RuinedChapel sits "within ~1 km of a settlement".</summary>
        public const double DefaultPoiChapelSettlementDist = 1000.0;

        /// <summary>"On or beside a peak — commanding ground". Matches
        /// <see cref="PeakNmsRadius"/>, so "beside" is one peak-suppression radius.</summary>
        public const double DefaultPoiTowerPeakDist = 400.0;

        /// <summary>Spec §1.2: Cairn sits at or above this fraction of the island's highest
        /// peak.</summary>
        public const double DefaultPoiCairnPeakFrac = 0.50;

        /// <summary>A headland is exposed: shelter at or below this (see
        /// <see cref="Features.ShelterMeasure"/>; 0.50 is an exposed headland, 0.84 a straight
        /// coast).</summary>
        public const double DefaultPoiHeadlandShelterMax = 0.55;

        /// <summary>A jetty needs a sheltered coast (spec §1.2, "reuse §7.2's shelter
        /// measure"); 1.00 is a bay head, 0.84 a straight coast.</summary>
        public const double DefaultPoiJettyShelterMin = 0.88;

        // --- detail sheets (spec §2.1) ---

        /// <summary>250 x 250 mm paper, 15 mm margin -> a 220 x 220 mm map area (spec §2.1).
        /// Square, small, and unmistakably a different physical object from an A1 survey sheet
        /// or a coastal strip (P2.1).</summary>
        public const double DefaultDetailSheetWidthMm = 250.0;
        public const double DefaultDetailSheetHeightMm = 250.0;
        public const double DefaultDetailSheetMarginMm = 15.0;

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
        public const int    DefaultPoiScaleDenominator = 1250;
    }
}
