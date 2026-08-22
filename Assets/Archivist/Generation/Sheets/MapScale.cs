namespace Archivist.Generation.Sheets
{
    /// <summary>§8.1, and D5. R2.3 allows three or four fixed values; the live set is four —
    /// 1250 (<see cref="PoiDetail"/>), 2500 (<see cref="Detail"/> and <see cref="Coastal"/>,
    /// which now share a denominator), 25000 (<see cref="WholeIsland"/>) and 50000
    /// (<see cref="WholeIslandFallback"/>, a fallback only).</summary>
    public readonly struct MapScale
    {
        public readonly int Denominator;

        public MapScale(int denominator) { Denominator = denominator; }

        /// <summary>Terrain detail surveys. 1:2500 since F1 — see Tuning.DetailScaleDenominator.</summary>
        public static MapScale Detail       { get { return new MapScale(Tuning.DetailScaleDenominator); } }

        /// <summary>Coastal reconnaissance. 1:2500 — see Tuning.CoastalScaleDenominator, which is
        /// the same denominator <see cref="Detail"/> uses. Hydrographic once worked at 1:5000 and
        /// scale was then a fourth office signal; it is not one any more, and the offices are told
        /// apart by style, rotation and coverage alone.</summary>
        public static MapScale Coastal      { get { return new MapScale(Tuning.CoastalScaleDenominator); } }

        /// <summary>
        /// POC-03 spec §2.1. The detail-sheet scale, and <b>the sweep knob</b> — see
        /// <see cref="Tuning.PoiScaleDenominator"/>. §2.1 gives 1:1250 and 1:2500 and says
        /// explicitly not to pick one from the table, because open question 1 says the whole
        /// design rests on this number and it can only be measured (C7). Shipped as a constant
        /// so the sweep is a one-line change.
        /// </summary>
        public static MapScale PoiDetail { get { return new MapScale(Tuning.PoiScaleDenominator); } }

        /// <summary>
        /// Scale per office (§8.1 as amended by F1). Nothing in R2.2 ties surveys to a
        /// shared scale — that was an implementation default, not a requirement.
        /// <para>POC-03's Antiquarian office works at its own, much larger scale: it maps one
        /// thing closely rather than tiling ground.</para>
        /// </summary>
        public static MapScale ForOffice(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return Coastal;
                case Office.Antiquarian:  return PoiDetail;
                default:                  return Detail;
            }
        }
        /// <summary>Whole-island index sheet. 1:25000 — see Tuning.WholeIslandScaleDenominator.</summary>
        public static MapScale WholeIsland  { get { return new MapScale(Tuning.WholeIslandScaleDenominator); } }

        /// <summary>Used when 1:25000 still will not fit the island on one sheet. 1:50000 — see
        /// Tuning.WholeIslandFallbackScaleDenominator.</summary>
        public static MapScale WholeIslandFallback { get { return new MapScale(Tuning.WholeIslandFallbackScaleDenominator); } }

        public double GroundMetres(double paperMm) { return paperMm / Tuning.MmPerMetre * Denominator; }

        /// <summary>
        /// Grid pitch for this scale (D4 / §6.4), stated as the paper-space rule D4's two
        /// values already encoded: 40 mm on the sheet, whatever the scale. Reproduces D4's own
        /// table exactly — 1000 m at 1:25000, and, illustratively, 200 m at 1:5000, a scale
        /// nothing in the project draws at any more. The live scales give 50 m at 1:1250,
        /// 100 m at 1:2500, 1000 m at 1:25000 and 2000 m at 1:50000.
        /// </summary>
        public double GridPitch
        {
            get { return Tuning.GridPitchPaperMm / Tuning.MmPerMetre * Denominator; }
        }

        public override string ToString() { return "1:" + Denominator; }
    }
}
