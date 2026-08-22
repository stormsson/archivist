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

        /// <summary>
        /// Long and thin, for the Hydrographic coast walk. A survey of a shore wants length
        /// along the water and almost no depth inland — on an A1 at 1:5000 the sheet's DEPTH
        /// alone spanned 39% of the island, so twenty of them at varying angles buried the
        /// island under 29x its own area in paper. This is 841 x 297 mm, giving 2002 x 642 m
        /// of ground at 1:2500: about 320 m either side of the shore.
        /// </summary>
        public static SheetFormat CoastalStrip
        {
            get { return new SheetFormat(Tuning.StripWidthMm, Tuning.StripHeightMm, Tuning.StripMarginMm); }
        }

        /// <summary>
        /// POC-03 spec §2.1 — the detail sheet. 250 x 250 mm paper, 15 mm margin, so a
        /// 220 x 220 mm map area: 275 x 275 m of ground at 1:1250, 550 x 550 m at 1:2500.
        ///
        /// <para>Square and small on purpose (P2.1): "it is a different physical object from a
        /// survey sheet, and should be recognisable as one at a glance". Nothing else in the
        /// collection is square, so orientation carries no information here — which is also why
        /// the detail cutter never picks one (P2.6: the sheet has no north indication and
        /// resolving orientation is part of the placement).</para>
        /// </summary>
        public static SheetFormat DetailSheet
        {
            get
            {
                return new SheetFormat(Tuning.DetailSheetWidthMm, Tuning.DetailSheetHeightMm,
                                       Tuning.DetailSheetMarginMm);
            }
        }

        public double MapWidthMm  { get { return WidthMm  - 2 * MarginMm; } }   // 514
        public double MapHeightMm { get { return HeightMm - 2 * MarginMm; } }   // 761

        public SheetFormat Landscape { get { return new SheetFormat(HeightMm, WidthMm, MarginMm); } }
    }
}
