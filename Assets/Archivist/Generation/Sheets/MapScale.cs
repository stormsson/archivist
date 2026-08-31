namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// §8.1, and D5. R2.3 allows three or four fixed values; the live set is
    /// 1250 (<see cref="PoiDetail"/>, detail sheets), 5000 / 10000 / 25000 (the quarter ladder,
    /// <c>QuarterCutter.QuarterLadder</c>) and 50000 (<see cref="WholeIslandFallback"/>, for a
    /// chart that will not fit).
    ///
    /// <para><b>Per-office scale is gone.</b> <c>Detail</c>, <c>Coastal</c> and <c>ForOffice</c>
    /// chose a denominator from the office; Q1.6 chooses one per <b>island</b>, shared by every
    /// office, because that is what puts the board's layers in register. They had no callers
    /// left and are removed rather than kept as a second way to answer a settled question.</para>
    /// </summary>
    public readonly struct MapScale
    {
        public readonly int Denominator;

        public MapScale(int denominator) { Denominator = denominator; }

        /// <summary>
        /// POC-03 spec §2.1. The detail-sheet scale, and <b>the sweep knob</b> — see
        /// <see cref="Tuning.PoiScaleDenominator"/>. §2.1 gives 1:1250 and 1:2500 and says
        /// explicitly not to pick one from the table, because open question 1 says the whole
        /// design rests on this number and it can only be measured (C7). Shipped as a constant
        /// so the sweep is a one-line change.
        /// </summary>
        public static MapScale PoiDetail { get { return new MapScale(Tuning.PoiScaleDenominator); } }

        /// <summary>Whole-island index sheet. 1:25000 — see Tuning.WholeIslandScaleDenominator.</summary>
        public static MapScale WholeIsland  { get { return new MapScale(Tuning.WholeIslandScaleDenominator); } }

        /// <summary>Used when 1:25000 still will not fit the island on one sheet. 1:50000 — see
        /// Tuning.WholeIslandFallbackScaleDenominator.</summary>
        public static MapScale WholeIslandFallback { get { return new MapScale(Tuning.WholeIslandFallbackScaleDenominator); } }

        public double GroundMetres(double paperMm) { return paperMm / Tuning.MmPerMetre * Denominator; }

        /// <summary>
        /// Grid pitch for this scale (D4 / §6.4), stated as the paper-space rule D4's two
        /// values already encoded: 40 mm on the sheet, whatever the scale. Reproduces D4's own
        /// table exactly — 1000 m at 1:25000. The live scales give 50 m at 1:1250, 200 m at
        /// 1:5000, 400 m at 1:10000, 1000 m at 1:25000 and 2000 m at 1:50000.
        /// </summary>
        public double GridPitch
        {
            get { return Tuning.GridPitchPaperMm / Tuning.MmPerMetre * Denominator; }
        }

        public override string ToString() { return "1:" + Denominator; }
    }
}
