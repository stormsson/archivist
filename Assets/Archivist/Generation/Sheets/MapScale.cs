namespace Archivist.Generation.Sheets
{
    /// <summary>§8.1, and D5: three fixed values, the third a whole-island fallback only.</summary>
    public readonly struct MapScale
    {
        public readonly int Denominator;

        public MapScale(int denominator) { Denominator = denominator; }

        /// <summary>Terrain detail surveys. 1:2500 since F1 — see Tuning.DetailScaleDenominator.</summary>
        public static MapScale Detail       { get { return new MapScale(Tuning.DetailScaleDenominator); } }

        /// <summary>Coastal reconnaissance. 1:5000 — see Tuning.CoastalScaleDenominator.</summary>
        public static MapScale Coastal      { get { return new MapScale(Tuning.CoastalScaleDenominator); } }

        /// <summary>
        /// Scale per office (§8.1 as amended by F1). Nothing in R2.2 ties surveys to a
        /// shared scale — that was an implementation default, not a requirement.
        /// </summary>
        public static MapScale ForOffice(Office office)
        {
            return office == Office.Hydrographic ? Coastal : Detail;
        }
        public static MapScale WholeIsland  { get { return new MapScale(25000); } }
        public static MapScale WholeIslandFallback { get { return new MapScale(50000); } }

        public double GroundMetres(double paperMm) { return paperMm / 1000.0 * Denominator; }

        /// <summary>
        /// Grid pitch for this scale (D4 / §6.4), stated as the paper-space rule D4's two
        /// values already encoded: 40 mm on the sheet, whatever the scale. Reproduces D4
        /// exactly — 1000 m at 1:25000, 200 m at 1:5000 — and gives 100 m at 1:2500.
        /// </summary>
        public double GridPitch
        {
            get { return Tuning.GridPitchPaperMm / 1000.0 * Denominator; }
        }

        public override string ToString() { return "1:" + Denominator; }
    }
}
