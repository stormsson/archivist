using Archivist.Generation.Features;
using Archivist.Generation.Sheets;

namespace Archivist.Render
{
    /// <summary>
    /// What one office draws, as a <see cref="LayerMask"/> (Q2.1).
    ///
    /// <para><b>One table, two consumers.</b> <c>FeatureMatrix</c> — office x
    /// <see cref="FeatureClass"/> — has always been the answer to "does this office draw that",
    /// and it was read only by the editor's vector tooling. The runtime renderer asked nobody
    /// and drew <see cref="LayerMask.All"/> on every sheet, so an office was a stamp in the
    /// margin and nothing else. This is the bridge, and it exists so the two paths cannot
    /// disagree: a class wired into <c>FeatureMatrix</c> is drawn by both or by neither.</para>
    ///
    /// <para><b><see cref="LayerMask.Fill"/> is never included</b> (Q2.2). A plate is ink on
    /// paper stock, not a colour relief map — F-S1.7 measured that the renderer was producing
    /// the second and the mockups show the first. Fill is not a feature any office surveys, so
    /// there is no row in <c>FeatureMatrix</c> that could ever turn it on.</para>
    ///
    /// <para><b>The whole-island chart is the exception</b>, and takes
    /// <see cref="ChartLayers"/>: at 1:25000 nothing but the coast survives the scale, and a
    /// chart carrying its office's full remit would be a black square. The base's job is to be
    /// an outline (Q4.4).</para>
    /// </summary>
    public static class OfficeLayers
    {
        /// <summary>The base plate: a coastline, and nothing else (Q4.4).</summary>
        public const LayerMask ChartLayers = LayerMask.Coast;

        /// <summary>The layers this office puts on a quarter plate.</summary>
        public static LayerMask For(Office office)
        {
            LayerMask layers = LayerMask.None;

            // FeatureClasses.All, never a loop over ordinals: a class added to the enum and
            // wired into FeatureMatrix must not quietly fail to reach the raster (§4.1 forbids
            // enum reflection for the same reason).
            for (int i = 0; i < FeatureClasses.All.Length; i++)
            {
                FeatureClass cls = FeatureClasses.All[i];
                if (FeatureMatrix.Draws(office, cls)) layers |= LayerFor(cls);
            }

            // Fill, for an office that washes the sea — the one thing that is style rather than
            // remit, so it comes from OfficeStyles and not from FeatureMatrix. Q2.2 forbids
            // relief banding, and OfficeStyles.WashPalette has none; see its comment.
            OfficeStyle style = OfficeStyles.For(office);
            if (style.HasWash) layers |= LayerMask.Fill;

            // An office that draws no contours does not ask for the layer, whatever the matrix
            // says: Garrison's grid is its texture and a hatching under it would be a fight.
            if (style.ContourStride <= 0) layers &= ~LayerMask.Contours;

            return layers;
        }

        /// <summary>What a sheet asks for: its office's layers, or the chart's if it is one.
        /// </summary>
        public static LayerMask For(Sheet sheet)
        {
            return sheet.Survey.IsWholeIsland ? ChartLayers : For(sheet.Survey.Office);
        }

        /// <summary>
        /// One feature class to one layer bit. <see cref="LayerMask.None"/> for a class the
        /// raster path cannot draw yet — <c>Poi</c>, which POC-03 §5 keeps out of scope — so a
        /// class arriving before its drawer does nothing rather than throwing.
        /// </summary>
        public static LayerMask LayerFor(FeatureClass cls)
        {
            switch (cls)
            {
                case FeatureClass.Coast:      return LayerMask.Coast;
                case FeatureClass.Contour:    return LayerMask.Contours;
                case FeatureClass.Peak:       return LayerMask.Peaks;
                case FeatureClass.River:      return LayerMask.Rivers;
                case FeatureClass.Settlement: return LayerMask.Settlements;
                case FeatureClass.Grid:       return LayerMask.Grid;
                case FeatureClass.Sounding:   return LayerMask.Soundings;
                default:                      return LayerMask.None;
            }
        }
    }
}
