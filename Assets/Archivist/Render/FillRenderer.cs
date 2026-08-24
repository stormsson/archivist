using System;
using System.Threading.Tasks;
using Archivist.Generation;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Render
{
    /// <summary>
    /// §6.1 — the fill. One field sample position per pixel, elevation to band, band to
    /// colour (T2.1). Step 2 of the §4 pipeline: normalisation (§6.2) and palette (§6.4) are
    /// already resolved by <see cref="IslandRenderer"/> and passed in, because they are
    /// per-island and must not be recomputed per pixel or per sheet.
    ///
    /// <para><b>Order independence (T4.4).</b> Every pixel reads only the field and writes
    /// only its own four bytes; no pixel is read back, and the ground position of a pixel is
    /// computed from its own <c>(x, y)</c> rather than accumulated, so a pixel's colour cannot
    /// depend on the order pixels are visited. Rows may therefore be dispatched in any order.
    /// This code does NOT introduce parallelism itself — §11's B4 timings are single-threaded
    /// and a budget has not been set yet (T4.3) — but nothing here prevents it.</para>
    ///
    /// <para><b>No transcendentals (§5).</b> <see cref="GroundImage"/> owns the only cos/sin in
    /// the renderer, evaluated once per render from the already-0.1-quantised angle. The walk
    /// below is pure multiply/add.</para>
    ///
    /// <para><b>No supersampling in v1</b> (§6.1, requirements §5.4). If thin features alias out
    /// at low resolution the fix is 2x2 SSAA of the fill, costing 4x in field samples and nothing
    /// else in this method. It is not done up front because T4.3 makes the resolution at which
    /// the quality bar holds a finding from B5's sweep rather than a constant asserted in
    /// advance, and averaging band colours would soften the hard band edges T2.1 wants.</para>
    /// </summary>
    public static class FillRenderer
    {
        /// <summary>
        /// §6.1. Fills every pixel of <paramref name="buf"/> with its band colour.
        /// </summary>
        /// <param name="island">Source of the field and of <c>Params.SeaLevel</c>.</param>
        /// <param name="req">The request the buffer and transform were built from; used to
        /// assert the three agree about dimensions before a single sample is taken.</param>
        /// <param name="gi">The ground&lt;-&gt;image transform (§2, §5).</param>
        /// <param name="buf">Destination, RGBA32, top-left origin.</param>
        /// <param name="palette">From <c>Palette.ForIsland</c> (§6.4, T2.4).</param>
        /// <param name="norm">From <c>IslandRenderer.Normalisation</c> (§6.2).</param>
        public static void Fill(Island island, RenderRequest req, GroundImage gi,
                                ImageBuffer buf, Rgba[] palette, double norm)
        {
            Fill(island, req, gi, buf, palette, norm, null);
        }

        /// <summary>
        /// As above, additionally capturing the quantised Height01 of every pixel into
        /// <paramref name="h01Out"/> (length W*H, row-major) when it is non-null.
        ///
        /// This is what makes the coastline free (§7): the coast IS the h01 == SeaLevel
        /// isoline, so once the fill has sampled the field there is nothing left to compute.
        /// float rather than double is deliberate — h01 is quantised at 2^-16, far coarser
        /// than float's ~6e-8 resolution at 0.5, so nothing is lost and the buffer halves.
        /// </summary>
        public static void Fill(Island island, RenderRequest req, GroundImage gi,
                                ImageBuffer buf, Rgba[] palette, double norm, float[] h01Out)
        {
            if (island == null) throw new ArgumentNullException("island");
            if (gi == null) throw new ArgumentNullException("gi");
            if (buf == null) throw new ArgumentNullException("buf");
            if (palette == null) throw new ArgumentNullException("palette");
            if (palette.Length < Bands.Count)
            {
                throw new ArgumentException("palette must hold Bands.Count colours", "palette");
            }
            if (buf.Width != req.Width || buf.Height != req.Height ||
                buf.Width != gi.Width || buf.Height != gi.Height)
            {
                throw new ArgumentException("buffer, request and transform disagree about dimensions");
            }

            // Hoisted out of the inner loop. The field costs ~301 ns a sample and dominates
            // everything else here, so nothing below re-reads a property per pixel. IslandField
            // is sealed, so this is a direct call rather than an interface dispatch.
            IslandField field = island.Field;
            double seaLevel = island.Params.SeaLevel;
            int width = buf.Width;
            int height = buf.Height;

            int step = SampleStep(req.PixelsPerMetre);
            bool parallel = height >= RenderTuning.ParallelRowThreshold;

            // --- 1. sample the field on the coarse lattice ---------------------------
            // Cost lives entirely here: this is the only place the field is evaluated.
            int cw = (width + step - 1) / step + 1;
            int ch = (height + step - 1) / step + 1;
            float[] coarse = new float[cw * ch];

            Action<int> sampleRow = cy =>
            {
                int rowBase = cy * cw;
                int py = cy * step;
                for (int cx = 0; cx < cw; cx++)
                {
                    V2 g = gi.GroundAt(cx * step, py);
                    coarse[rowBase + cx] = (float)field.Height01(g.X, g.Y);
                }
            };

            if (parallel) { Parallel.For(0, ch, sampleRow); }
            else { for (int cy = 0; cy < ch; cy++) { sampleRow(cy); } }

            // --- 2. interpolate to pixels, band, write -------------------------------
            // No field access at all below this line — only lerps and a table lookup.
            double inv = 1.0 / step;

            Action<int> fillRow = y =>
            {
                int cy = y / step;
                double ty = (y - cy * step) * inv;
                int rowA = cy * cw;
                int rowB = (cy + 1 < ch ? cy + 1 : cy) * cw;

                for (int x = 0; x < width; x++)
                {
                    int cx = x / step;
                    double tx = (x - cx * step) * inv;
                    int xb = cx + 1 < cw ? cx + 1 : cx;

                    double a = coarse[rowA + cx];
                    a += (coarse[rowA + xb] - a) * tx;
                    double b = coarse[rowB + cx];
                    b += (coarse[rowB + xb] - b) * tx;
                    double h01 = a + (b - a) * ty;

                    // Land test stays Height01 >= SeaLevel (§6.1, §4.4): the tie counts as
                    // land, and every other consumer uses that test. Elevation is converted
                    // from the INTERPOLATED h01 rather than re-sampled, so band and coast
                    // agree with each other even where both are approximations.
                    bool land = h01 >= seaLevel;
                    int band = Bands.Index(field.ElevationFrom(h01), norm, land);
                    buf.SetPixel(x, y, palette[band]);

                    if (h01Out != null)
                    {
                        h01Out[y * width + x] = (float)h01;
                    }
                }
            };

            if (parallel) { Parallel.For(0, height, fillRow); }
            else { for (int y = 0; y < height; y++) { fillRow(y); } }
        }

        /// <summary>
        /// How far apart to evaluate the field, in pixels. Bounded twice: by pixels, so the
        /// error stays sub-pixel in the image; and by ground metres, so thin land cannot
        /// survive at one resolution and vanish at another. See RenderTuning and
        /// docs/poc02/findings.md for the measurements behind both numbers.
        /// </summary>
        public static int SampleStep(double pixelsPerMetre)
        {
            if (pixelsPerMetre <= 0 || double.IsNaN(pixelsPerMetre)) return 1;
            int byGround = (int)Math.Floor(RenderTuning.FieldSampleCeilingM * pixelsPerMetre);
            int step = RenderTuning.FieldSampleStepPx;
            if (byGround < step) step = byGround;
            return step < 1 ? 1 : step;
        }
    }
}
