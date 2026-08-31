using System;
using Archivist.Generation;
using Archivist.Generation.Features;
using Archivist.Generation.Field;

namespace Archivist.Render
{
    /// <summary>
    /// §4 — the pipeline. Resolve normalisation and palette once, fill, then stroke.
    ///
    /// Takes an Island rather than a seed: per-island normalisation (§6.2) needs the
    /// island's highest peak, so a sheet render is NOT a pure function of its own rect.
    /// Callers generate the island once (~115 ms) and render many sheets from it.
    /// </summary>
    public static class IslandRenderer
    {
        /// <summary>
        /// One render. <paramref name="samples"/> is optional and lets several plates of one
        /// quarter share the field samples they all need (Q1.2 gives every office the same four
        /// rects); a null cache renders the same picture and only pays more for it.
        ///
        /// <para><b>Defaulted, and unlike <c>RenderRequest.ScaleDenominator</c> that is safe
        /// here</b> — losing this loses speed, never a layer. F-R13.2 is the case where a
        /// defaulted value silently changed what was drawn; this one cannot.</para>
        /// </summary>
        public static ImageBuffer Render(Island island, RenderRequest req,
                                         SampleGridCache samples = null)
        {
            return Render(island, req, OfficeStyles.Neutral, samples);
        }

        /// <summary>The same, in one office's hand (R2.6, Q2.6). See <c>OfficeStyles</c> for why
        /// this carries the whole of what tells two offices apart.</summary>
        public static ImageBuffer Render(Island island, RenderRequest req, OfficeStyle style,
                                         SampleGridCache samples = null)
        {
            if (island == null) throw new ArgumentNullException("island");

            var buf = new ImageBuffer(req.Width, req.Height);
            var gi = new GroundImage(req);

            double norm = Normalisation(island);
            // A washing office brings its own two-tone palette; everyone else gets the global
            // one, which only a fill would use and which no plate turns on.
            Rgba[] palette = style.HasWash ? OfficeStyles.WashPalette(style) : Palette.ForIsland(island);

            // The coast is derived from the fill's own samples (§7, FieldCoast), so it needs
            // the h01 raster — allocated only when both layers are actually wanted.
            bool wantCoast = (req.Layers & LayerMask.Coast) != 0;
            bool wantFill = (req.Layers & LayerMask.Fill) != 0;
            float[] h01 = (wantCoast && wantFill) ? new float[buf.Width * buf.Height] : null;

            if (wantFill)
            {
                FillRenderer.Fill(island, req, gi, buf, palette, norm, h01);
            }
            else
            {
                // The unprinted sheet, in this office's stock (R2.6). A buffer starts at zero,
                // which is black — so a plate with Fill off (Q2.2) and no paper under it is ink
                // on a black rectangle.
                buf.Fill(style.Paper);
            }

            if (h01 != null)
            {
                double halfWidthPx = GroundImage.MmToPx(RenderTuning.CoastWidthMm, req.PixelsPerPaperMm) * 0.5;
                // The office's own pen, the same one Strokes' vector fallback uses, so the two
                // coast paths are one colour by construction. Not derived from the fill: with a
                // wash the fill IS the office's colour, and the coastline would vanish into the
                // water it is the edge of.
                FieldCoast.Draw(h01, buf.Width, buf.Height, island.Params.SeaLevel,
                                halfWidthPx, style.Ink, buf);
            }

            // Strokes still draws the discrete features. Coast is cleared when FieldCoast
            // handled it; without a fill there is no h01 raster, so the vector path remains
            // the fallback and Strokes keeps the layer.
            LayerMask remaining = h01 != null ? (req.Layers & ~LayerMask.Coast) : req.Layers;

            RenderRequest strokeReq = req.WithLayers(remaining);
            Strokes.Draw(island, strokeReq, gi, buf, palette, style, samples);
            return buf;
        }

        /// <summary>
        /// §6.2. Peaks sort (elevation desc, x asc, y asc), so [0] is the island maximum and
        /// it costs nothing — generation already computed it. Atolls often have no peaks at
        /// all, hence the character fallback.
        /// </summary>
        public static double Normalisation(Island island)
        {
            IslandFeatures f = island.Features;
            double norm = (f != null && f.Peaks.Count > 0)
                ? f.Peaks[0].SpotHeightM
                : IslandParams.MaxElevationFor(island.Params.Character);
            return Math.Max(norm, RenderTuning.MinNormalisation);
        }
    }
}
