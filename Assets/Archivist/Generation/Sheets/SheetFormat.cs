namespace Archivist.Generation.Sheets
{
    /// <summary>§8.1. Paper is real; sheet count is a consequence, not a knob.</summary>
    public readonly struct SheetFormat
    {
        public readonly double WidthMm;
        public readonly double HeightMm;
        public readonly double MarginMm;

        public SheetFormat(double widthMm, double heightMm, double marginMm)
        { WidthMm = widthMm; HeightMm = heightMm; MarginMm = marginMm; }

        public static SheetFormat A1 { get { return new SheetFormat(Tuning.SheetWidthMm, Tuning.SheetHeightMm, Tuning.SheetMarginMm); } }

        public double MapWidthMm  { get { return WidthMm  - 2 * MarginMm; } }   // 514
        public double MapHeightMm { get { return HeightMm - 2 * MarginMm; } }   // 761

        public SheetFormat Landscape { get { return new SheetFormat(HeightMm, WidthMm, MarginMm); } }
        public bool IsPortrait { get { return HeightMm >= WidthMm; } }
    }
}
