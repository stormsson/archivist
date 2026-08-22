using System;

namespace Archivist.Render
{
    /// <summary>
    /// §7, as amended: the coastline is derived from the height field the fill already
    /// sampled, not extracted as a polyline and stroked.
    ///
    /// Why this replaced the vector stroke. Extracting contours at ~1 pixel per cell over a
    /// sheet costs the sheet's AREA in field evaluations — measured at ~60% of a sheet
    /// render, and the same cost POC-01 recorded as finding F4. But the fill has already
    /// evaluated the field at every pixel, and the coast IS the `h01 == SeaLevel` isoline of
    /// those very samples. So it costs no field evaluations at all.
    ///
    /// It is also strictly more correct. §7's LOD rule existed to stop the stroke drifting
    /// off the fill's water edge; here the line is defined BY that edge, so the two cannot
    /// disagree — the failure mode is gone rather than guarded against.
    ///
    /// Rivers, settlements, peaks and soundings stay vector (Strokes.cs): they are discrete
    /// features, not isolines of the field, so this trick does not apply to them.
    /// </summary>
    public static class FieldCoast
    {
        /// <summary>
        /// Signed-distance anti-aliasing. `h01 - seaLevel` is a scalar field whose zero set
        /// is the coast; dividing by the local gradient magnitude converts that value into a
        /// distance in PIXELS, which then drives the same coverage ramp the vector strokes
        /// use, so a coast edge and a river edge look identical.
        /// </summary>
        public static void Draw(float[] h01, int width, int height, double seaLevel,
                                double halfWidthPx, Rgba ink, ImageBuffer buf)
        {
            if (h01 == null || buf == null || width <= 0 || height <= 0) return;
            if (h01.Length < width * height) return;
            if (halfWidthPx <= 0 || double.IsNaN(halfWidthPx) || double.IsInfinity(halfWidthPx)) return;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int rowUp = (y > 0 ? y - 1 : y) * width;
                int rowDn = (y < height - 1 ? y + 1 : y) * width;

                for (int x = 0; x < width; x++)
                {
                    double d = h01[row + x] - seaLevel;

                    // Central differences on the raster, clamped at the border. Units are
                    // h01 per pixel, which is exactly what turns d into a pixel distance.
                    int xm = x > 0 ? x - 1 : x;
                    int xp = x < width - 1 ? x + 1 : x;
                    double gx = (h01[row + xp] - h01[row + xm]) * 0.5;
                    double gy = (h01[rowDn + x] - h01[rowUp + x]) * 0.5;

                    double g = Math.Sqrt(gx * gx + gy * gy);

                    // Flat ground far from any shore: no isoline can pass through here.
                    // Guarding on g also avoids dividing by zero on a perfectly level plateau.
                    if (g <= 0.0) continue;

                    double distPx = Math.Abs(d) / g;
                    if (distPx > halfWidthPx + 1.0) continue;      // cheap reject, most pixels

                    double coverage = halfWidthPx + 0.5 - distPx;
                    if (coverage <= 0.0) continue;
                    if (coverage > 1.0) coverage = 1.0;

                    buf.SetPixel(x, y, Rgba.Over(buf.GetPixel(x, y), ink, coverage));
                }
            }
        }
    }
}
