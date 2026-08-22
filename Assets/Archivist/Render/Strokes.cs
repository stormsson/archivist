using System;
using System.Collections.Generic;
using Archivist.Generation;
using Archivist.Generation.Features;
using Archivist.Generation.Geometry;

namespace Archivist.Render
{
    /// <summary>
    /// §7 — the vector overlays, drawn after the fill and composited with coverage-based
    /// anti-aliasing. <b>Only the strokes are anti-aliased</b>; the fill's band edges stay
    /// hard, because that is what a hypsometric map looks like.
    ///
    /// <para><b>The LOD rule, and it is load-bearing.</b> The fill computes the water's edge
    /// per pixel from the analytic field. The coastline stroke is therefore extracted at a
    /// contour cell size matched to the pixel — <see cref="RenderLod.ForPixelsPerMetre"/> —
    /// not at a fixed LOD. Extract it at, say, 32 m cells while the fill samples per pixel and
    /// the line visibly floats off the water it is supposed to bound. Tying the cell size to
    /// roughly one pixel makes the two agree by construction. This is §6.2 of POC-01 applied
    /// to the raster.</para>
    ///
    /// <para><b>No labels and no numerals (T2.6).</b> Settlements and peaks are marks only; a
    /// sounding is a dot, never a depth figure. Typography is office style and is deferred.</para>
    ///
    /// <para><b>Determinism (§5).</b> Layers are drawn in one fixed order — coast, rivers,
    /// settlements, peaks, soundings — and within a layer in the source list's own order.
    /// Every source list is already totally ordered by the generator (contours by first vertex,
    /// peaks by elevation desc, soundings in lattice order), no dictionary or set is ever
    /// enumerated, and compositing is single-threaded, so the output is byte-identical run to
    /// run. Nothing here calls a transcendental: <see cref="GroundImage"/> owns the only
    /// cos/sin in the assembly (§5) and the mark geometry comes from a literal unit-circle
    /// table.</para>
    /// </summary>
    public static class Strokes
    {
        // ----------------------------------------------------------------- ink
        //
        // Art direction is UNDEFINED for POC-02 — §6.4 calls even the fill palette a
        // placeholder "to be replaced wholesale". These are placeholders too. RenderTuning.cs
        // holds no colours and may not be edited, so the constants live here.
        //
        // Coast and river ink are derived from the palette where that is sensible: a darkened
        // deep-sea colour for the coast, the shallow colour for rivers, so the overlay tracks
        // any future re-tint of the fill (Palette.ForIsland is the seam for seed tints). The
        // band indices are Bands' own — 0..3 sea, 4..11 land — and the lookup is guarded on
        // Bands.Count, so a shorter palette falls back to the constants below rather than
        // throwing. Marks and soundings are NOT palette-derived: they must read as ink over
        // whatever band they land on.

        /// <summary>Very dark blue-black — a survey pen on water.</summary>
        static readonly Rgba CoastInkFallback = Rgba.FromHex("0c1e2f");

        /// <summary>§6.4's `shallow`, so a river reads as water where it crosses land.</summary>
        static readonly Rgba RiverInkFallback = Rgba.FromHex("3f86ad");

        /// <summary>Brown-black drafting ink for the discrete marks.</summary>
        static readonly Rgba MarkInk = Rgba.FromHex("2e2318");

        /// <summary>Soundings sit on the dark sea bands, so their dot is light, not dark.</summary>
        static readonly Rgba SoundingInk = Rgba.FromHex("dfeaf1");

        /// <summary>Multiplier taking the deep-sea colour down to a coastline pen.</summary>
        const double CoastInkDarken = 0.55;

        /// <summary>§6.3 band indices, matching <see cref="Bands"/> and <see cref="Palette"/>.</summary>
        const int DeepBandIndex = 0;
        const int ShallowBandIndex = 2;

        // ------------------------------------------------------------- geometry
        //
        // §7 and §10 give a mark's SIZE but never its line weight. Rather than invent a
        // constant (RenderTuning.cs is off limits) the marks are outlined at the river weight,
        // the finest documented stroke in the table.

        const double MarkLineMm = RenderTuning.RiverWidthMm;

        /// <summary>
        /// sqrt(3)/2, the height of an equilateral triangle of unit side. A literal rather
        /// than a call, for the same reason §4.4 quantises before a threshold.
        /// </summary>
        const double EquilateralHeight = 0.8660254037844386;

        /// <summary>
        /// A unit circle as sixteen literal vertices, 22.5° apart, starting at (1,0) and
        /// running anticlockwise. Literal because §5 gives <see cref="GroundImage"/> the only
        /// cos/sin in the assembly; sixteen because the ring is about three pixels across and
        /// no eye will find the corners.
        /// </summary>
        static readonly V2[] UnitRing =
        {
            new V2( 1.0000000000000000,  0.0000000000000000),
            new V2( 0.9238795325112867,  0.3826834323650898),
            new V2( 0.7071067811865476,  0.7071067811865476),
            new V2( 0.3826834323650898,  0.9238795325112867),
            new V2( 0.0000000000000000,  1.0000000000000000),
            new V2(-0.3826834323650898,  0.9238795325112867),
            new V2(-0.7071067811865476,  0.7071067811865476),
            new V2(-0.9238795325112867,  0.3826834323650898),
            new V2(-1.0000000000000000,  0.0000000000000000),
            new V2(-0.9238795325112867, -0.3826834323650898),
            new V2(-0.7071067811865476, -0.7071067811865476),
            new V2(-0.3826834323650898, -0.9238795325112867),
            new V2( 0.0000000000000000, -1.0000000000000000),
            new V2( 0.3826834323650898, -0.9238795325112867),
            new V2( 0.7071067811865476, -0.7071067811865476),
            new V2( 0.9238795325112867, -0.3826834323650898)
        };

        /// <summary>
        /// A stroke narrower than about half a pixel dithers away to nothing. Clamping the
        /// half-width here means a low-resolution preview loses stroke WEIGHT rather than
        /// losing the feature; above one pixel wide the clamp never engages.
        /// </summary>
        const double MinHalfWidthPx = 0.35;

        /// <summary>Feature queries grow the visible rect by this many stroke widths, so a line
        /// just outside the buffer still contributes its edge to the border pixels.</summary>
        const double MarginStrokeWidths = 2.0;

        // ------------------------------------------------------------------ API

        /// <summary>
        /// §7. Draws the vector overlays over an already-filled buffer. Each layer is gated on
        /// <see cref="RenderRequest.Layers"/>; every degenerate case (no island, no features,
        /// an empty rect, a one-point polyline, a zero-length segment) is skipped rather than
        /// thrown, because a render must never crash.
        /// </summary>
        /// <param name="island">The island; its field is re-queried for the coastline and the soundings.</param>
        /// <param name="req">The request, for the layer mask and the paper scale of the widths.</param>
        /// <param name="gi">The ground&lt;-&gt;image transform (§2). The only source of coordinates here.</param>
        /// <param name="buf">The destination, composited into with <see cref="Rgba.Over"/>.</param>
        /// <param name="palette">The fill palette (§6.4); stroke ink is derived from it where sensible.</param>
        public static void Draw(Island island, RenderRequest req, GroundImage gi,
                                ImageBuffer buf, Rgba[] palette)
        {
            if (island == null || gi == null || buf == null) return;
            if (req.Layers == LayerMask.None) return;

            // Widths are in PAPER millimetres, so a feature has the same apparent weight on
            // every sheet whatever its scale (§7). Without a paper scale there is no width.
            double ppm = req.PixelsPerPaperMm;
            if (!(ppm > 0.0) || double.IsInfinity(ppm)) return;
            if (!(req.PixelsPerMetre > 0.0) || double.IsInfinity(req.PixelsPerMetre)) return;

            double coastHalf = HalfWidthPx(RenderTuning.CoastWidthMm, ppm);
            double riverHalf = HalfWidthPx(RenderTuning.RiverWidthMm, ppm);
            double markHalf = HalfWidthPx(MarkLineMm, ppm);
            double settlementRadius = GroundImage.MmToPx(RenderTuning.SettlementMarkMm, ppm) * 0.5;
            double peakBase = GroundImage.MmToPx(RenderTuning.PeakMarkMm, ppm);
            double soundingRadius = GroundImage.MmToPx(RenderTuning.SoundingDotMm, ppm) * 0.5;

            double widest = coastHalf;
            if (riverHalf > widest) widest = riverHalf;
            if (settlementRadius > widest) widest = settlementRadius;
            if (peakBase > widest) widest = peakBase;

            Rect2 groundRect = QueryRect(req, gi, buf, widest);
            if (groundRect.IsEmpty) return;

            Rgba coastInk = CoastInk(palette);
            Rgba riverInk = RiverInk(palette);

            IslandFeatures features = island.Features;

            // ---- the fixed draw order (§5). Do not reorder: the acceptance hash depends on it.
            // 1 coast, 2 rivers, 3 settlements, 4 peaks, 5 soundings.

            if ((req.Layers & LayerMask.Coast) != 0)
            {
                DrawCoast(island, req, gi, buf, groundRect, coastHalf, coastInk);
            }

            if ((req.Layers & LayerMask.Rivers) != 0 && features != null && features.Rivers != null)
            {
                IReadOnlyList<River> rivers = features.Rivers;
                for (int i = 0; i < rivers.Count; i++)
                {
                    StrokePolyline(gi, buf, rivers[i].Course, riverHalf, riverInk);
                }
            }

            if ((req.Layers & LayerMask.Settlements) != 0 && features != null && features.Settlements != null)
            {
                IReadOnlyList<Settlement> towns = features.Settlements;
                for (int i = 0; i < towns.Count; i++)
                {
                    double ix, iy;
                    gi.ImageAt(towns[i].Position, out ix, out iy);
                    DrawRing(buf, ix, iy, settlementRadius, markHalf, MarkInk);
                }
            }

            if ((req.Layers & LayerMask.Peaks) != 0 && features != null && features.Peaks != null)
            {
                IReadOnlyList<Peak> peaks = features.Peaks;
                for (int i = 0; i < peaks.Count; i++)
                {
                    double ix, iy;
                    gi.ImageAt(peaks[i].Position, out ix, out iy);
                    DrawTriangle(buf, ix, iy, peakBase, MarkInk);
                }
            }

            if ((req.Layers & LayerMask.Soundings) != 0)
            {
                // Field-derived and re-queried per rect (§6.3 of generation): emitted in
                // (x asc, y asc) lattice order, so this loop is itself deterministic.
                List<Sounding> soundings = Soundings.ForRect(island.Field, groundRect);
                for (int i = 0; i < soundings.Count; i++)
                {
                    double ix, iy;
                    gi.ImageAt(soundings[i].Position, out ix, out iy);
                    FillDot(buf, ix, iy, soundingRadius, SoundingInk);
                }
            }
        }

        // --------------------------------------------------------------- layers

        /// <summary>
        /// §7's LOD rule. The cell size follows the pixel, never a fixed LOD, so the stroke and
        /// the fill's per-pixel water edge agree by construction. Do not "optimise" this to a
        /// coarser lattice — the line will float off the water and B1 fails on sight.
        /// </summary>
        static void DrawCoast(Island island, RenderRequest req, GroundImage gi, ImageBuffer buf,
                              Rect2 groundRect, double halfWidth, Rgba ink)
        {
            if (island.Field == null) return;

            int lod = RenderLod.ForPixelsPerMetre(req.PixelsPerMetre);
            double cell = Contours.CellSizeForLod(lod);

            IReadOnlyList<Polyline> coast =
                Contours.Extract(island.Field, groundRect, cell, island.Params.SeaLevel);
            if (coast == null) return;

            // Extract returns a total order on the polylines (first vertex x asc, y asc), so
            // this walk is stable without any sorting of our own.
            for (int i = 0; i < coast.Count; i++)
            {
                StrokePolyline(gi, buf, coast[i], halfWidth, ink);
            }
        }

        /// <summary>
        /// The ground-space AABB to query features against: the four image corners taken back
        /// to ground and encapsulated, so a rotated sheet gets a rect that actually covers it,
        /// then grown by a couple of stroke widths so a line just outside still contributes its
        /// edge. At rotation 0 this reproduces <see cref="RenderRequest.Area"/> to within half
        /// a pixel, so the island overview needs no separate path.
        /// </summary>
        static Rect2 QueryRect(RenderRequest req, GroundImage gi, ImageBuffer buf, double widestHalfPx)
        {
            Rect2 r = Rect2.Empty;
            r = r.Encapsulate(gi.GroundAt(0, 0));
            r = r.Encapsulate(gi.GroundAt(buf.Width, 0));
            r = r.Encapsulate(gi.GroundAt(0, buf.Height));
            r = r.Encapsulate(gi.GroundAt(buf.Width, buf.Height));

            double marginPx = widestHalfPx * MarginStrokeWidths + 1.0;
            return r.Expanded(marginPx / req.PixelsPerMetre);
        }

        // ------------------------------------------------------------- ink rules

        static Rgba CoastInk(Rgba[] palette)
        {
            if (palette == null || palette.Length < Bands.Count) return CoastInkFallback;
            return Darken(palette[DeepBandIndex], CoastInkDarken);
        }

        static Rgba RiverInk(Rgba[] palette)
        {
            if (palette == null || palette.Length < Bands.Count) return RiverInkFallback;
            return palette[ShallowBandIndex];
        }

        static Rgba Darken(Rgba c, double factor)
        {
            return new Rgba(ScaleChannel(c.R, factor), ScaleChannel(c.G, factor),
                            ScaleChannel(c.B, factor), c.A);
        }

        static byte ScaleChannel(byte v, double factor)
        {
            double s = v * factor + 0.5;
            if (s <= 0.0) return 0;
            if (s >= 255.0) return 255;
            return (byte)s;
        }

        static double HalfWidthPx(double widthMm, double pixelsPerPaperMm)
        {
            double half = GroundImage.MmToPx(widthMm, pixelsPerPaperMm) * 0.5;
            return half < MinHalfWidthPx ? MinHalfWidthPx : half;
        }

        // ------------------------------------------------------------ primitives

        /// <summary>
        /// Ground polyline -&gt; image-space strokes. A polyline of nought or one point has no
        /// segment and draws nothing; a closed one gets its wrap segment. Vertices are taken to
        /// image space through <see cref="GroundImage.ImageAt"/> — never a transform of our own.
        /// </summary>
        static void StrokePolyline(GroundImage gi, ImageBuffer buf, Polyline line,
                                   double halfWidth, Rgba ink)
        {
            if (line == null || line.Count < 2) return;

            double ax, ay;
            gi.ImageAt(line[0], out ax, out ay);
            double firstX = ax, firstY = ay;

            for (int i = 1; i < line.Count; i++)
            {
                double bx, by;
                gi.ImageAt(line[i], out bx, out by);
                StrokeSegment(buf, ax, ay, bx, by, halfWidth, ink);
                ax = bx; ay = by;
            }

            if (line.Closed && line.Count > 2)
            {
                StrokeSegment(buf, ax, ay, firstX, firstY, halfWidth, ink);
            }
        }

        /// <summary>
        /// The anti-aliased primitive, and the only place a pixel is written.
        ///
        /// <para>Coverage is <c>clamp(halfWidth + 0.5 - distance, 0, 1)</c> against the EXACT
        /// point-to-segment distance, which gives a linear ramp one pixel wide across the
        /// stroke's edge — the analytic coverage of a straight edge, to first order, and
        /// symmetric so a line does not shift as it moves sub-pixel.</para>
        ///
        /// <para>Only the pixels in the segment's bounding box grown by the half-width are
        /// visited, so cost follows the ink laid down rather than the image area, and the box
        /// is clipped to the buffer so a polyline may run far outside it. A pixel centre sits
        /// at INTEGER image coordinates: <see cref="GroundImage"/> defines image (0,0) as the
        /// centre of pixel [0,0], not its corner.</para>
        ///
        /// <para>A zero-length segment degenerates to a dot rather than dividing by zero,
        /// which is what <see cref="FillDot"/> relies on.</para>
        /// </summary>
        static void StrokeSegment(ImageBuffer buf, double ax, double ay, double bx, double by,
                                  double halfWidth, Rgba ink)
        {
            if (!(halfWidth > 0.0)) return;
            if (double.IsNaN(ax) || double.IsNaN(ay) || double.IsNaN(bx) || double.IsNaN(by)) return;
            if (double.IsInfinity(ax) || double.IsInfinity(ay) ||
                double.IsInfinity(bx) || double.IsInfinity(by)) return;

            double pad = halfWidth + 1.0;
            double loX = (ax < bx ? ax : bx) - pad;
            double hiX = (ax > bx ? ax : bx) + pad;
            double loY = (ay < by ? ay : by) - pad;
            double hiY = (ay > by ? ay : by) + pad;

            int x0 = CeilClamp(loX, 0, buf.Width - 1);
            int x1 = FloorClamp(hiX, 0, buf.Width - 1);
            int y0 = CeilClamp(loY, 0, buf.Height - 1);
            int y1 = FloorClamp(hiY, 0, buf.Height - 1);
            if (x0 > x1 || y0 > y1) return;

            double dx = bx - ax;
            double dy = by - ay;
            double lenSq = dx * dx + dy * dy;

            for (int y = y0; y <= y1; y++)
            {
                double py = y;
                for (int x = x0; x <= x1; x++)
                {
                    double px = x;

                    double t = 0.0;
                    if (lenSq > 0.0)
                    {
                        t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
                        if (t < 0.0) t = 0.0;
                        else if (t > 1.0) t = 1.0;
                    }

                    double ex = px - (ax + dx * t);
                    double ey = py - (ay + dy * t);
                    double dist = Math.Sqrt(ex * ex + ey * ey);

                    double cov = halfWidth + 0.5 - dist;
                    if (cov <= 0.0) continue;
                    if (cov > 1.0) cov = 1.0;

                    buf.SetPixel(x, y, Rgba.Over(buf.GetPixel(x, y), ink, cov));
                }
            }
        }

        /// <summary>A filled, anti-aliased dot — a zero-length stroke of the dot's radius.</summary>
        static void FillDot(ImageBuffer buf, double cx, double cy, double radius, Rgba ink)
        {
            if (!(radius > 0.0)) return;
            StrokeSegment(buf, cx, cy, cx, cy, radius, ink);
        }

        /// <summary>
        /// A settlement mark: a circle outline of the given radius, built in IMAGE space from
        /// the literal unit ring. Image space, not ground, because §7's sizes are paper
        /// millimetres — the mark must keep its size and its orientation whatever the sheet's
        /// rotation or scale.
        /// </summary>
        static void DrawRing(ImageBuffer buf, double cx, double cy, double radius,
                             double halfWidth, Rgba ink)
        {
            if (!(radius > 0.0) || double.IsNaN(cx) || double.IsNaN(cy)) return;

            double px = cx + UnitRing[0].X * radius;
            double py = cy + UnitRing[0].Y * radius;
            double firstX = px, firstY = py;

            for (int i = 1; i < UnitRing.Length; i++)
            {
                double qx = cx + UnitRing[i].X * radius;
                double qy = cy + UnitRing[i].Y * radius;
                StrokeSegment(buf, px, py, qx, qy, halfWidth, ink);
                px = qx; py = qy;
            }
            StrokeSegment(buf, px, py, firstX, firstY, halfWidth, ink);
        }

        /// <summary>
        /// A peak mark: a small filled equilateral triangle pointing up the page, centred on
        /// the peak and sized by its base in paper millimetres. Filled rather than outlined so
        /// that at three or four pixels across it still reads as a different mark from a
        /// settlement's ring. Image space is y-DOWN (§2), so the apex takes the NEGATIVE y.
        /// </summary>
        static void DrawTriangle(ImageBuffer buf, double cx, double cy, double baseWidth, Rgba ink)
        {
            if (!(baseWidth > 0.0) || double.IsNaN(cx) || double.IsNaN(cy)) return;
            if (double.IsInfinity(cx) || double.IsInfinity(cy)) return;

            double h = baseWidth * EquilateralHeight;
            double half = baseWidth * 0.5;
            double apexY = cy - h * (2.0 / 3.0);
            double baseY = cy + h * (1.0 / 3.0);

            FillTriangle(buf,
                         cx, apexY,
                         cx - half, baseY,
                         cx + half, baseY,
                         ink);
        }

        /// <summary>
        /// Anti-aliased convex fill by signed distance: a point's distance outside the triangle
        /// is the greatest of its three outward edge distances, and coverage is the same
        /// <c>clamp(0.5 - d, 0, 1)</c> ramp the stroke uses, so a mark's edge matches a line's.
        /// Winding is normalised first, so the caller cannot hand it an inside-out triangle.
        /// A degenerate (zero-area) triangle draws nothing.
        /// </summary>
        static void FillTriangle(ImageBuffer buf, double x0, double y0, double x1, double y1,
                                 double x2, double y2, Rgba ink)
        {
            double cross = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
            if (!(cross > 0.0) && !(cross < 0.0)) return;      // zero area, or NaN
            if (cross < 0.0)
            {
                double tx = x1, ty = y1;
                x1 = x2; y1 = y2; x2 = tx; y2 = ty;
            }

            double loX = Min3(x0, x1, x2) - 1.0;
            double hiX = Max3(x0, x1, x2) + 1.0;
            double loY = Min3(y0, y1, y2) - 1.0;
            double hiY = Max3(y0, y1, y2) + 1.0;

            int bx0 = CeilClamp(loX, 0, buf.Width - 1);
            int bx1 = FloorClamp(hiX, 0, buf.Width - 1);
            int by0 = CeilClamp(loY, 0, buf.Height - 1);
            int by1 = FloorClamp(hiY, 0, buf.Height - 1);
            if (bx0 > bx1 || by0 > by1) return;

            // Outward unit normals. With a positive cross the interior lies to the LEFT of
            // each directed edge, so the outward normal of a->b is (dy, -dx) normalised.
            double[] nx = new double[3];
            double[] ny = new double[3];
            double[] ax = new double[3];
            double[] ay = new double[3];
            if (!EdgeNormal(x0, y0, x1, y1, nx, ny, ax, ay, 0)) return;
            if (!EdgeNormal(x1, y1, x2, y2, nx, ny, ax, ay, 1)) return;
            if (!EdgeNormal(x2, y2, x0, y0, nx, ny, ax, ay, 2)) return;

            for (int y = by0; y <= by1; y++)
            {
                double py = y;
                for (int x = bx0; x <= bx1; x++)
                {
                    double px = x;

                    double d = (px - ax[0]) * nx[0] + (py - ay[0]) * ny[0];
                    double d1 = (px - ax[1]) * nx[1] + (py - ay[1]) * ny[1];
                    if (d1 > d) d = d1;
                    double d2 = (px - ax[2]) * nx[2] + (py - ay[2]) * ny[2];
                    if (d2 > d) d = d2;

                    double cov = 0.5 - d;
                    if (cov <= 0.0) continue;
                    if (cov > 1.0) cov = 1.0;

                    buf.SetPixel(x, y, Rgba.Over(buf.GetPixel(x, y), ink, cov));
                }
            }
        }

        static bool EdgeNormal(double ax, double ay, double bx, double by,
                               double[] nx, double[] ny, double[] px, double[] py, int i)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (!(len > 0.0)) return false;
            nx[i] = dy / len;
            ny[i] = -dx / len;
            px[i] = ax;
            py[i] = ay;
            return true;
        }

        // ---------------------------------------------------------------- maths

        /// <summary>Smallest pixel index at or above <paramref name="v"/>, clamped.</summary>
        static int CeilClamp(double v, int lo, int hi)
        {
            if (double.IsNaN(v)) return hi + 1;                // empties the loop
            if (v <= lo) return lo;
            if (v > hi) return hi + 1;
            return (int)Math.Ceiling(v);
        }

        /// <summary>Largest pixel index at or below <paramref name="v"/>, clamped.</summary>
        static int FloorClamp(double v, int lo, int hi)
        {
            if (double.IsNaN(v)) return lo - 1;                // empties the loop
            if (v >= hi) return hi;
            if (v < lo) return lo - 1;
            return (int)Math.Floor(v);
        }

        static double Min3(double a, double b, double c)
        {
            double m = a < b ? a : b;
            return m < c ? m : c;
        }

        static double Max3(double a, double b, double c)
        {
            double m = a > b ? a : b;
            return m > c ? m : c;
        }
    }
}
