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
        public static ImageBuffer Render(Island island, RenderRequest req)
        {
            if (island == null) throw new ArgumentNullException("island");

            var buf = new ImageBuffer(req.Width, req.Height);
            var gi = new GroundImage(req);

            double norm = Normalisation(island);
            Rgba[] palette = Palette.ForIsland(island);

            // The coast is derived from the fill's own samples (§7, FieldCoast), so it needs
            // the h01 raster — allocated only when both layers are actually wanted.
            bool wantCoast = (req.Layers & LayerMask.Coast) != 0;
            bool wantFill = (req.Layers & LayerMask.Fill) != 0;
            float[] h01 = (wantCoast && wantFill) ? new float[buf.Width * buf.Height] : null;

            if (wantFill)
            {
                FillRenderer.Fill(island, req, gi, buf, palette, norm, h01);
            }

            if (h01 != null)
            {
                double halfWidthPx = GroundImage.MmToPx(RenderTuning.CoastWidthMm, req.PixelsPerPaperMm) * 0.5;
                // Ink.CoastInk is the single derivation both coast paths call, so this line
                // and the Strokes vector fallback are the same colour by construction — they
                // were NOT when each path derived its own (one rounded, one truncated).
                FieldCoast.Draw(h01, buf.Width, buf.Height, island.Params.SeaLevel,
                                halfWidthPx, Ink.CoastInk(palette), buf);
            }

            // Strokes still draws the discrete features. Coast is cleared when FieldCoast
            // handled it; without a fill there is no h01 raster, so the vector path remains
            // the fallback and Strokes keeps the layer.
            LayerMask remaining = h01 != null ? (req.Layers & ~LayerMask.Coast) : req.Layers;
            var strokeReq = new RenderRequest(req.Area, req.RotationDeg, req.PixelsPerMetre,
                                              req.PixelsPerPaperMm, remaining);
            Strokes.Draw(island, strokeReq, gi, buf, palette);
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
