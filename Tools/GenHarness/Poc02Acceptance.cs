using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Archivist.Generation;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using Archivist.Render;

namespace Archivist.Harness
{
    /// <summary>
    /// POC-02 §11 acceptance. Runs headless because Archivist.Render, like Archivist.Generation,
    /// has no UnityEngine reference (T3.2) — that is the whole reason this suite can exist.
    ///
    /// <para>B1 is the primary criterion and is human-judged; it lives in the Editor's Texture
    /// tab (§9) and cannot be automated. What is here is B2 and B3 (gated), and B4 and B5
    /// (measured and reported, never gated — §11 and T4.3).</para>
    /// </summary>
    public static class Poc02Acceptance
    {
        /// <summary>Same collection seed as the POC-01 harness, so the two suites talk about the same islands.</summary>
        const ulong Collection = 8412UL;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>A render this suite refuses to attempt. A safety valve on B5's ladder, not a budget.</summary>
        const long MaxSweepPixels = 64L * 1024L * 1024L;

        /// <summary>The three characters, in a fixed order. Never a dictionary walk — §5 forbids
        /// iteration order driving output, and it would make these reports non-reproducible.</summary>
        static readonly IslandCharacter[] Characters =
            { IslandCharacter.Mountainous, IslandCharacter.Fjorded, IslandCharacter.Atoll };

        public static bool Failed;

        static void Pass(string id, string msg) { Console.WriteLine("  PASS  " + id + "  " + msg); }
        static void Fail(string id, string msg) { Console.WriteLine("  FAIL  " + id + "  " + msg); Failed = true; }
        static void Info(string msg)            { Console.WriteLine("        " + msg); }
        static void Metric(string id, string msg) { Console.WriteLine("  ----  " + id + "  " + msg); }

        static string F0(double d) { return d.ToString("F0", Inv); }
        static string F1(double d) { return d.ToString("F1", Inv); }
        static string F2(double d) { return d.ToString("F2", Inv); }
        static string F3(double d) { return d.ToString("F3", Inv); }

        // ================================================================== B2
        /// <summary>
        /// §11 B2 — determinism. Byte-identical over 100 renders of one request, unperturbed by
        /// draws from an unrelated named stream (§4.3 of the generator spec), and — the guard that
        /// keeps the other two honest — an island render and a sheet render of the same island
        /// must differ, so a renderer that emits a constant image cannot pass B2 by accident.
        /// </summary>
        public static void B2_Determinism()
        {
            Console.WriteLine("B2  Determinism");

            ulong seed = Streams.IslandSeed(Collection, 0);
            Island isl = Island.FromSeed(seed);

            // The palette and the band table must at least agree on how many bands exist, or every
            // downstream index in B3 is meaningless.
            Rgba[] palette = Palette.ForIsland(isl);
            if (palette == null || palette.Length < Bands.Count)
            {
                Fail("B2", "palette has " + (palette == null ? 0 : palette.Length)
                           + " entries but Bands.Count is " + Bands.Count);
                return;
            }
            Info("palette " + palette.Length + " entries, Bands.Count " + Bands.Count);

            // Deliberately modest: determinism is a property of the pipeline, not of the pixel
            // count, and B2 runs inside the default `all` mode. Rotated and all layers on, so the
            // §5 rotation rule and the stroke compositor are both under test.
            double rotationDeg = 23.7;                                  // 0.1-quantised, like a survey's
            Rect2 area = Rect2.FromCentreSize(isl.LandBounds.Centre.RotateDeg(-rotationDeg), 4000.0, 3000.0);
            RenderRequest req = new RenderRequest(area, rotationDeg, 0.04,
                                                  RenderTuning.SheetPxPerPaperMm, LayerMask.All);

            ulong first = IslandRenderer.Render(isl, req).ContentHash();
            for (int i = 1; i < 100; i++)
            {
                ulong h = IslandRenderer.Render(isl, req).ContentHash();
                if (h != first)
                {
                    Fail("B2", "render hash diverged on iteration " + i
                               + "  (" + h.ToString("X16") + " != " + first.ToString("X16") + ")");
                    return;
                }
            }
            Pass("B2", "100 renders of one request identical, hash " + first.ToString("X16")
                       + "  (" + req.Width + "x" + req.Height + " px, rot " + F1(rotationDeg) + " deg)");

            // Same request against a freshly generated island: the render must not depend on which
            // Island instance it was handed, only on the seed.
            ulong fresh = IslandRenderer.Render(Island.FromSeed(seed), req).ContentHash();
            if (fresh != first) Fail("B2", "a freshly generated island rendered differently: " + fresh.ToString("X16"));
            else Pass("B2", "a freshly generated island from the same seed renders bit-identically");

            // §4.3 — adding a purpose must never reshuffle an existing one. "palette" is the stream
            // §6.4 reserves for seed-derived tints; drawing from it today must change nothing.
            Pcg32 unrelated = Streams.For(seed, "unrelated.purpose");
            for (int i = 0; i < 1000; i++) unrelated.NextUInt();
            Pcg32 reserved = Streams.For(seed, "palette");
            for (int i = 0; i < 1000; i++) reserved.NextUInt();

            ulong after = IslandRenderer.Render(Island.FromSeed(seed), req).ContentHash();
            if (after != first) Fail("B2", "unrelated / reserved stream draws perturbed the render: " + after.ToString("X16"));
            else Pass("B2", "1000 draws from \"unrelated.purpose\" and from the reserved \"palette\" stream leave the render bit-identical");

            // --- the anti-constant guard -------------------------------------------------------
            RenderRequest islandReq = new RenderRequest(isl.LandBounds, 0.0, 0.03,
                                                       RenderTuning.SheetPxPerPaperMm, LayerMask.All);
            Sheet sheet;
            if (!PickSheet(isl, out sheet))
            {
                Metric("B2", "island " + isl.Name + " shipped no sheets; the island/sheet difference guard is skipped");
                return;
            }
            RenderRequest sheetReq = RenderRequest.ForSheet(sheet, 0.6, LayerMask.All);
            NoteSheetPlacement("B2", sheet, sheetReq);

            ImageBuffer islandBuf = IslandRenderer.Render(isl, islandReq);
            ImageBuffer sheetBuf  = IslandRenderer.Render(isl, sheetReq);
            ulong hi = islandBuf.ContentHash();
            ulong hs = sheetBuf.ContentHash();

            int islandColours = DistinctColours(islandBuf, 64);
            int sheetColours  = DistinctColours(sheetBuf, 64);

            if (hi == hs)
            {
                Fail("B2", "an island render and a sheet render of the same island produced the same hash "
                           + hi.ToString("X16") + " — the renderer is not reading its request");
            }
            else if (islandColours < 2 || sheetColours < 2)
            {
                Fail("B2", "a render is a single flat colour (island " + islandColours
                           + " colours, sheet " + sheetColours + ") — determinism here is vacuous");
            }
            else
            {
                Pass("B2", "island (" + islandReq.Width + "x" + islandReq.Height + ", " + islandColours
                           + " colours) and sheet (" + sheetReq.Width + "x" + sheetReq.Height + ", "
                           + sheetColours + " colours) renders of one island differ: "
                           + hi.ToString("X16") + " vs " + hs.ToString("X16"));
            }
        }

        // ================================================================== B3
        /// <summary>
        /// §11 B3 — coherence. Two rects over common ground with a different origin, rotation AND
        /// resolution must agree about where the bands are. Target: >= 99% of sampled ground points
        /// get the same colour from both images.
        ///
        /// <para><b>This can never be 100%, and the test is not written as if it could be.</b> The
        /// two rasters lay different lattices over the same <c>f(x, y)</c>, so the nearest pixel in
        /// each is a different ground point, and near a band edge two different ground points may
        /// honestly fall on opposite sides of it. That is not a defect — unlike POC-01's A3 contour
        /// seams, which share a lattice and therefore must match exactly.</para>
        ///
        /// <para>So a disagreement is only acceptable if it is <i>explained</i>: each pixel's colour
        /// must be the palette entry for the band at that pixel's own ground sample. When both are,
        /// the two samples straddle a band edge and — being within half a pixel of the query point
        /// each — that edge provably lies within one pixel. When either is not, the fill wrote a
        /// colour that does not follow from the field, and that is a real bug: it is counted,
        /// reported with an example, and it fails the check.</para>
        /// </summary>
        public static void B3_Coherence()
        {
            Console.WriteLine("B3  Coherence  (two rects: different origin, rotation and resolution)");

            Island isl = Island.FromSeed(Streams.IslandSeed(Collection, 1));
            IslandField field = isl.Field;
            double norm = IslandRenderer.Normalisation(isl);
            double seaLevel = isl.Params.SeaLevel;
            Rgba[] palette = Palette.ForIsland(isl);

            if (palette == null || palette.Length < Bands.Count)
            {
                Fail("B3", "palette has " + (palette == null ? 0 : palette.Length)
                           + " entries but Bands.Count is " + Bands.Count);
                return;
            }

            V2 c = isl.LandBounds.Centre;

            // A — north-up, coarse, centred on the land. The overview's geometry.
            double rotA = 0.0, ppmA = 0.08;
            Rect2 areaA = Rect2.FromCentreSize(c.RotateDeg(-rotA), 3000.0, 2400.0);
            RenderRequest reqA = new RenderRequest(areaA, rotA, ppmA, RenderTuning.SheetPxPerPaperMm, LayerMask.Fill);

            // B — shifted origin, rotated (0.1-quantised, §5), and 1.6x finer. A sheet's geometry.
            // Area is a FRAME-space rect, so the ground centre is rotated by -theta to place it.
            double rotB = 31.4, ppmB = 0.13;
            V2 cB = c + new V2(137.5, -211.0);
            Rect2 areaB = Rect2.FromCentreSize(cB.RotateDeg(-rotB), 2600.0, 2600.0);
            RenderRequest reqB = new RenderRequest(areaB, rotB, ppmB, RenderTuning.SheetPxPerPaperMm, LayerMask.Fill);

            // Fill only. Strokes are anti-aliased overlays that legitimately differ between two
            // rasters; B3 is about the banded fill underneath them.
            ImageBuffer bufA = IslandRenderer.Render(isl, reqA);
            ImageBuffer bufB = IslandRenderer.Render(isl, reqB);
            GroundImage giA = new GroundImage(reqA);
            GroundImage giB = new GroundImage(reqB);

            Info("A " + bufA.Width + "x" + bufA.Height + " px at " + F3(ppmA) + " px/m, rot " + F1(rotA) + " deg");
            Info("B " + bufB.Width + "x" + bufB.Height + " px at " + F3(ppmB) + " px/m, rot " + F1(rotB) + " deg, origin offset (137.5, -211.0) m");

            // A square of ground comfortably inside both footprints: B's inscribed radius is 1300 m
            // and its centre is ~250 m off c, so +/-800 m about c is safe in both, and A (3000 x 2400 m,
            // centred on c) contains it outright.
            const int Steps = 121;
            const double HalfExtent = 800.0;
            double step = (2.0 * HalfExtent) / (Steps - 1);

            // Half a pixel diagonal in each image: the furthest a nearest-pixel centre can sit from
            // the query point. Their sum bounds how far apart the two samples can be, and therefore
            // how close a band edge must be for a disagreement to be explicable.
            double reachA = 0.5 * Math.Sqrt(2.0) / ppmA;
            double reachB = 0.5 * Math.Sqrt(2.0) / ppmB;
            double maxSeparation = reachA + reachB;

            int sampled = 0, outside = 0, agree = 0;
            int explained = 0, adjacent = 0, unexplained = 0, unfaithful = 0;
            double worstSeparation = 0.0;
            string firstUnexplained = null;
            HashSet<int> bandsSeen = new HashSet<int>();

            for (int iy = 0; iy < Steps; iy++)
            {
                double gy = c.Y - HalfExtent + iy * step;
                for (int ix = 0; ix < Steps; ix++)
                {
                    double gx = c.X - HalfExtent + ix * step;
                    V2 p = new V2(gx, gy);

                    int xa, ya, xb, yb;
                    if (!NearestPixel(giA, bufA, p, out xa, out ya) ||
                        !NearestPixel(giB, bufB, p, out xb, out yb)) { outside++; continue; }
                    sampled++;

                    Rgba ca = bufA.GetPixel(xa, ya);
                    Rgba cb = bufB.GetPixel(xb, yb);

                    // The ground each raster actually sampled for that pixel, and the band there.
                    V2 pa = giA.GroundAt(xa, ya);
                    V2 pb = giB.GroundAt(xb, yb);
                    int ia = BandAt(field, pa, norm, seaLevel);
                    int ib = BandAt(field, pb, norm, seaLevel);
                    bandsSeen.Add(ia);
                    bandsSeen.Add(ib);

                    // "compare against the palette colour actually written" — a pixel whose colour
                    // is not its own band's colour is a fill bug, agreement or no agreement.
                    bool faithfulA = InRange(ia, palette.Length) && Same(ca, palette[ia]);
                    bool faithfulB = InRange(ib, palette.Length) && Same(cb, palette[ib]);
                    if (!faithfulA || !faithfulB) unfaithful++;

                    if (Same(ca, cb)) { agree++; continue; }

                    double separation = V2.Dist(pa, pb);
                    if (separation > worstSeparation) worstSeparation = separation;

                    if (faithfulA && faithfulB)
                    {
                        // Both pixels are honest; the two samples simply straddle a band edge, and
                        // both lie within half a pixel of p, so the edge is inside one pixel.
                        explained++;
                        if (Math.Abs(ia - ib) == 1) adjacent++;
                    }
                    else
                    {
                        unexplained++;
                        if (firstUnexplained == null)
                        {
                            firstUnexplained =
                                "at (" + F1(gx) + ", " + F1(gy) + ") m: A pixel " + xa + "," + ya
                                + " wrote " + Hex(ca) + " but its ground sample is band " + ia
                                + " (" + (InRange(ia, palette.Length) ? Hex(palette[ia]) : "out of range") + ");"
                                + "  B pixel " + xb + "," + yb + " wrote " + Hex(cb)
                                + " but its ground sample is band " + ib
                                + " (" + (InRange(ib, palette.Length) ? Hex(palette[ib]) : "out of range") + ")";
                        }
                    }
                }
            }

            if (sampled == 0) { Fail("B3", "no sampled ground point fell inside both images"); return; }

            double pct = 100.0 * agree / sampled;
            string headline = F2(pct) + "% of " + sampled + " sampled ground points get the same colour from both images"
                              + (outside > 0 ? "  (" + outside + " fell outside one image and were skipped)" : "");

            if (pct >= 99.0) Pass("B3", headline + "  (target >= 99%)");
            else Fail("B3", headline + "  (target >= 99%)");

            int disagreements = explained + unexplained;
            Metric("B3", disagreements + " disagreements: " + explained + " explained by the sampling offset, "
                         + unexplained + " unexplained");
            if (explained > 0)
            {
                Metric("B3", "   of the explained, " + adjacent + "/" + explained
                             + " are between adjacent bands (a straddled edge looks like this)");
            }
            Metric("B3", "   worst separation between the two sampled ground points: " + F2(worstSeparation)
                         + " m  (bound " + F2(maxSeparation) + " m = half a pixel diagonal in each image)");
            Metric("B3", "   " + bandsSeen.Count + " distinct bands appear in the overlap"
                         + (bandsSeen.Count < 3 ? "  -- WEAK: too few bands here for this to prove much" : ""));

            // These two are the real bugs the section exists to catch.
            if (unexplained > 0)
            {
                Fail("B3", unexplained + " disagreement(s) are NOT within one pixel of a band edge — a colour "
                           + "was written that does not follow from the field");
                Info(firstUnexplained);
            }
            else
            {
                Pass("B3", "every disagreement lies within one pixel of a band edge");
            }

            if (unfaithful > 0)
            {
                Fail("B3", unfaithful + "/" + sampled + " sampled pixels carry a colour that is not their own "
                           + "ground sample's palette entry");
            }
            else
            {
                Pass("B3", "every sampled pixel in both images carries exactly palette[Bands.Index(...)] for its own ground sample");
            }

            if (worstSeparation > maxSeparation + 1e-6)
            {
                Fail("B3", "a nearest pixel centre sits further from its query point than half a pixel diagonal — "
                           + "GroundImage's ground->image inverse disagrees with its forward transform");
            }
        }

        // ================================================================== B4
        /// <summary>
        /// §11 B4 — performance. Reported, never gated: until B5 settles what resolution is needed
        /// (T4.3), a budget would be a guess. Island overview and one sheet, per character, at the
        /// §10 defaults, single-threaded.
        /// </summary>
        public static void B4_Performance()
        {
            Console.WriteLine("B4  Performance  (metric, REPORTED not gated — T4.3)");
            Info("island overview at " + F2(RenderTuning.IslandPreviewPxPerMetre) + " px/m and one sheet at "
                 + F2(RenderTuning.SheetPxPerPaperMm) + " px/paper-mm, all layers, single-threaded, median of 3");

            Island[] byCharacter = SampleByCharacter(24);

            for (int i = 0; i < Characters.Length; i++)
            {
                IslandCharacter ch = Characters[i];
                Island isl = byCharacter[i];
                if (isl == null) { Metric("B4", ch + ": no island available"); continue; }

                Metric("B4", ch + "  \"" + isl.Name + "\"  land bbox " + F0(isl.LandBounds.Width)
                             + " x " + F0(isl.LandBounds.Height) + " m,  normalisation "
                             + F1(IslandRenderer.Normalisation(isl)) + " m");

                RenderRequest overview = RenderRequest.ForIsland(isl, RenderTuning.IslandPreviewPxPerMetre);
                ReportRender("B4", "   overview", isl, overview);

                Sheet sheet;
                if (!PickSheet(isl, out sheet)) { Metric("B4", "   no sheets on this island"); continue; }

                RenderRequest sheetReq = RenderRequest.ForSheet(sheet, RenderTuning.SheetPxPerPaperMm);
                NoteSheetPlacement("B4", sheet, sheetReq);
                ReportRender("B4", "   sheet " + sheet.Survey.Office + " #" + sheet.Number
                                   + " 1:" + sheet.Survey.Scale.Denominator, isl, sheetReq);
            }
        }

        static void ReportRender(string id, string label, Island isl, RenderRequest req)
        {
            long pixels = (long)req.Width * req.Height;
            if (pixels > MaxSweepPixels)
            {
                Metric(id, label + ": " + req.Width + "x" + req.Height + " px exceeds the "
                           + MaxSweepPixels.ToString(Inv) + " pixel ceiling; skipped");
                return;
            }

            double ms = MedianRenderMs(isl, req, 3);
            double mpx = pixels / 1000000.0;
            Metric(id, label + ": " + req.Width + "x" + req.Height + " px = " + F3(mpx) + " Mpx,  "
                       + F1(ms) + " ms,  " + F0(ms * 1000000.0 / pixels) + " ns/px");
        }

        // ================================================================== B5
        /// <summary>
        /// §11 B5 — the resolution sweep, and the reason it exists: open question 1 in
        /// requirements.md ("what resolution is recognisable?") is answered by <b>looking at these
        /// files</b>, not by reading the numbers. So every render is exported as a PNG, named after
        /// its request per §8, into one printed folder.
        /// </summary>
        public static void B5_ResolutionSweep(string outDir)
        {
            Console.WriteLine("B5  Resolution sweep  (metric, REPORTED — exports PNGs for eyeballing)");

            string dir = ResolveOutputDir(outDir);
            if (dir == null) { Fail("B5", "could not create an output directory; nothing exported"); return; }
            Console.WriteLine();
            Console.WriteLine("        PNG OUTPUT FOLDER:  " + dir);
            Console.WriteLine();

            Island isl = Island.FromSeed(Streams.IslandSeed(Collection, 0));
            Metric("B5", "island \"" + isl.Name + "\"  " + isl.Params.Character + "  seed " + isl.Seed.ToString("X16")
                         + "  land bbox " + F0(isl.LandBounds.Width) + " x " + F0(isl.LandBounds.Height) + " m");

            long totalBytes = 0;

            // --- overview ladder, px per ground metre -----------------------------------------
            double[] overviewLadder = { 0.05, 0.10, 0.20, 0.40 };
            Metric("B5", "overview ladder (px per ground metre), all layers, north-up:");
            for (int i = 0; i < overviewLadder.Length; i++)
            {
                double ppm = overviewLadder[i];
                RenderRequest req = RenderRequest.ForIsland(isl, ppm);
                string name = "island_s" + isl.Seed.ToString("X16") + "_px" + F3(ppm) + ".png";
                totalBytes += SweepOne("   " + F2(ppm) + " px/m ", isl, req, dir, name);
            }

            // --- sheet ladder, px per paper millimetre ----------------------------------------
            Sheet sheet;
            if (!PickSheet(isl, out sheet))
            {
                Metric("B5", "island shipped no sheets; sheet ladder skipped");
            }
            else
            {
                double[] sheetLadder = { 0.5, 1.0, 2.0, 4.0 };
                Metric("B5", "sheet ladder (px per paper mm), " + sheet.Survey.Office + " #" + sheet.Number
                             + " at 1:" + sheet.Survey.Scale.Denominator + ", rot "
                             + F1(sheet.RotationDeg) + " deg:");
                for (int i = 0; i < sheetLadder.Length; i++)
                {
                    double ppmm = sheetLadder[i];
                    RenderRequest req = RenderRequest.ForSheet(sheet, ppmm);
                    if (i == 0) NoteSheetPlacement("B5", sheet, req);
                    string name = "sheet_s" + isl.Seed.ToString("X16") + "_" + sheet.Survey.Office
                                  + "_" + sheet.Number + "_pp" + F2(ppmm) + ".png";
                    totalBytes += SweepOne("   " + F2(ppmm) + " px/mm  (" + F3(req.PixelsPerMetre) + " px/m)",
                                           isl, req, dir, name);
                }
            }

            Metric("B5", "total exported: " + F1(totalBytes / 1048576.0) + " MB  (PngWriter uses stored "
                         + "deflate blocks — §8 asks for correct, not small)");
            Console.WriteLine();
            Console.WriteLine("        Open the folder and answer open question 1 by eye:  " + dir);
            Console.WriteLine();
        }

        static long SweepOne(string label, Island isl, RenderRequest req, string dir, string fileName)
        {
            long pixels = (long)req.Width * req.Height;
            if (pixels > MaxSweepPixels)
            {
                Metric("B5", label + ": " + req.Width + "x" + req.Height + " px exceeds the "
                             + MaxSweepPixels.ToString(Inv) + " pixel ceiling; skipped");
                return 0;
            }

            Stopwatch sw = new Stopwatch();
            sw.Restart();
            ImageBuffer buf = IslandRenderer.Render(isl, req);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;

            string path = Path.Combine(dir, fileName);
            long bytes = 0;
            try
            {
                PngWriter.Write(buf, path);
                bytes = new FileInfo(path).Length;
            }
            catch (Exception ex)
            {
                Fail("B5", "PngWriter.Write failed for " + fileName + ": " + ex.Message);
            }

            Metric("B5", label + ": " + buf.Width + "x" + buf.Height + " px = "
                         + F3(pixels / 1000000.0) + " Mpx,  " + F1(ms) + " ms,  "
                         + F0(ms * 1000000.0 / pixels) + " ns/px,  " + F1(bytes / 1048576.0) + " MB  -> " + fileName);
            return bytes;
        }

        // ================================================================== describe
        /// <summary>
        /// Not an acceptance check — the thing you read when a render looks wrong and you need to
        /// know why. Normalisation (§6.2) and peak count together explain most surprises: an island
        /// with no peaks falls back to the character maximum and its whole ramp shifts.
        /// </summary>
        public static void Describe(ulong collectionSeed, int index)
        {
            Island isl = Island.FromSeed(Streams.IslandSeed(collectionSeed, index));
            RenderRequest overview = RenderRequest.ForIsland(isl, RenderTuning.IslandPreviewPxPerMetre);

            double norm = IslandRenderer.Normalisation(isl);
            int peaks = isl.Features != null ? isl.Features.Peaks.Count : 0;
            double top = peaks > 0 ? isl.Features.Peaks[0].SpotHeightM : 0.0;

            Console.WriteLine();
            Console.WriteLine("island " + index + "  " + isl.Name + "  " + isl.Params.Character
                              + "  seed " + isl.Seed.ToString("X16"));
            Console.WriteLine("  land bbox " + F0(isl.LandBounds.Width) + " x " + F0(isl.LandBounds.Height)
                              + " m   centre " + P(isl.LandBounds.Centre));
            Console.WriteLine("  peaks " + peaks
                              + (peaks > 0 ? "  highest " + F1(top) + " m" : "  (none — normalisation falls back to the character maximum, §6.2)"));
            Console.WriteLine("  normalisation used " + F1(norm) + " m"
                              + "   character max " + F1(IslandParams.MaxElevationFor(isl.Params.Character)) + " m");
            Console.WriteLine("  overview at " + F2(RenderTuning.IslandPreviewPxPerMetre) + " px/m -> "
                              + overview.Width + " x " + overview.Height + " px  ("
                              + F3((long)overview.Width * overview.Height / 1000000.0) + " Mpx)");

            Sheet sheet;
            if (PickSheet(isl, out sheet))
            {
                RenderRequest req = RenderRequest.ForSheet(sheet, RenderTuning.SheetPxPerPaperMm);
                Console.WriteLine("  sheet " + sheet.Survey.Office + " #" + sheet.Number
                                  + " 1:" + sheet.Survey.Scale.Denominator
                                  + " rot " + F1(sheet.RotationDeg) + " deg at "
                                  + F2(RenderTuning.SheetPxPerPaperMm) + " px/mm -> "
                                  + req.Width + " x " + req.Height + " px  ("
                                  + F3(req.PixelsPerMetre) + " px/m)");
                Console.WriteLine("    sheet frame-rect centre " + P(sheet.FrameRect.Centre)
                                  + "   request area centre " + P(req.Area.Centre));
                NoteSheetPlacement("desc", sheet, req);
            }
            else
            {
                Console.WriteLine("  no sheets");
            }
        }

        // ================================================================== helpers

        /// <summary>Band index for a ground point. §6.1's land test is on h01 vs SeaLevel, not on
        /// elevation vs 0, so the fill agrees exactly with the rest of the codebase (a tie at
        /// SeaLevel counts as land, §4.4).</summary>
        static int BandAt(IslandField field, V2 p, double norm, double seaLevel)
        {
            bool isLand = field.Height01(p.X, p.Y) >= seaLevel;
            return Bands.Index(field.Elevation(p.X, p.Y), norm, isLand);
        }

        /// <summary>Pixel centres sit at integer image coordinates (GroundImage's origin IS pixel
        /// centre 0,0), so the nearest pixel is a round, not a floor.</summary>
        static bool NearestPixel(GroundImage gi, ImageBuffer buf, V2 ground, out int x, out int y)
        {
            double ix, iy;
            gi.ImageAt(ground, out ix, out iy);
            x = (int)Math.Floor(ix + 0.5);
            y = (int)Math.Floor(iy + 0.5);
            return buf.InBounds(x, y);
        }

        /// <summary>V2.ToString() formats with the CURRENT culture; this machine prints decimal
        /// commas. Every number this suite emits goes through InvariantCulture.</summary>
        static string P(V2 v) { return "(" + F1(v.X) + ", " + F1(v.Y) + ")"; }

        static bool Same(Rgba a, Rgba b) { return a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A; }

        static bool InRange(int i, int count) { return i >= 0 && i < count; }

        static string Hex(Rgba c)
        {
            return "#" + c.R.ToString("X2", Inv) + c.G.ToString("X2", Inv)
                       + c.B.ToString("X2", Inv) + c.A.ToString("X2", Inv);
        }

        static int DistinctColours(ImageBuffer buf, int cap)
        {
            HashSet<uint> seen = new HashSet<uint>();
            byte[] px = buf.Pixels;
            for (int i = 0; i + 3 < px.Length && seen.Count < cap; i += 4)
            {
                seen.Add(((uint)px[i] << 24) | ((uint)px[i + 1] << 16) | ((uint)px[i + 2] << 8) | (uint)px[i + 3]);
            }
            return seen.Count;   // Count only — never iterated, so no set order reaches the output (§5).
        }

        /// <summary>Median of n timed renders, one warm-up first. Stopwatch is legal here and only
        /// here: §5's no-wall-clock rule binds Generation and Render, not the harness.</summary>
        static double MedianRenderMs(Island isl, RenderRequest req, int reps)
        {
            IslandRenderer.Render(isl, req);            // warm the noise table and the JIT
            List<double> times = new List<double>();
            Stopwatch sw = new Stopwatch();
            for (int i = 0; i < reps; i++)
            {
                sw.Restart();
                IslandRenderer.Render(isl, req);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[times.Count / 2];
        }

        /// <summary>Prefer Land Survey (1:2500, the densest sheets), then Hydrographic, then
        /// Garrison, then the whole-island survey. Fixed order, never a dictionary walk.</summary>
        static bool PickSheet(Island isl, out Sheet sheet)
        {
            sheet = default(Sheet);
            Office[] preference = { Office.LandSurvey, Office.Hydrographic, Office.Garrison };
            for (int i = 0; i < preference.Length; i++)
            {
                Survey sv = isl.SurveyFor(preference[i]);
                if (sv != null && sv.SheetCount > 0) { sheet = sv.Sheets[0]; return true; }
            }
            Survey whole = isl.WholeIslandSurvey;
            if (whole != null && whole.SheetCount > 0) { sheet = whole.Sheets[0]; return true; }
            return false;
        }

        /// <summary>
        /// One island per character, indexed to match <see cref="Characters"/>. Scans natural seeds
        /// first — a forced character is a different island from any the collection actually
        /// contains — and only forces the character if the scan does not turn one up.
        /// </summary>
        static Island[] SampleByCharacter(int scanLimit)
        {
            Island[] found = new Island[Characters.Length];
            int remaining = Characters.Length;

            for (int i = 0; i < scanLimit && remaining > 0; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                for (int c = 0; c < Characters.Length; c++)
                {
                    if (found[c] == null && isl.Params.Character == Characters[c]) { found[c] = isl; remaining--; }
                }
            }
            for (int c = 0; c < Characters.Length; c++)
            {
                if (found[c] == null)
                {
                    found[c] = Island.FromSeed(Streams.IslandSeed(Collection, 0), Characters[c]);
                    Info(Characters[c] + " did not occur in the first " + scanLimit
                         + " seeds; forced onto seed 0 instead");
                }
            }
            return found;
        }

        /// <summary>
        /// A sheet render must cover the sheet's ground, or B1 — the primary criterion — is being
        /// judged on the wrong rectangle and B5's exports are decoration. RenderRequest.Area is a
        /// FRAME-space rect (GroundImage rotates it into ground), so it should equal the sheet's
        /// own frame rect. Loud, but not gated: this is the renderer's contract, not the suite's.
        /// </summary>
        static void NoteSheetPlacement(string id, Sheet sheet, RenderRequest req)
        {
            Rect2 frame = sheet.FrameRect;
            double d = V2.Dist(frame.Centre, req.Area.Centre);
            if (d <= 1.0) return;
            Metric(id, "*** WARNING: ForSheet's area centre is " + F0(d)
                       + " m from the sheet's frame-rect centre " + P(frame.Centre)
                       + " (request area centre " + P(req.Area.Centre) + ") — this render does NOT "
                       + "cover sheet #" + sheet.Number + "'s ground ***");
        }

        /// <summary>
        /// Tools/GenHarness/out/poc02 by default, derived from the assembly location rather than
        /// the working directory, so it lands in the repo wherever the harness was launched from.
        /// Falls back to the system temp folder. Either way the path is printed.
        /// </summary>
        static string ResolveOutputDir(string requested)
        {
            string candidate = requested;
            if (string.IsNullOrEmpty(candidate))
            {
                // bin/Debug/net9.0 -> bin/Debug -> bin -> GenHarness
                candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out", "poc02"));
            }
            try
            {
                Directory.CreateDirectory(candidate);
                return Path.GetFullPath(candidate);
            }
            catch (Exception ex)
            {
                Info("could not create " + candidate + " (" + ex.Message + "); falling back to the temp folder");
            }
            try
            {
                string fallback = Path.Combine(Path.GetTempPath(), "archivist-poc02");
                Directory.CreateDirectory(fallback);
                return Path.GetFullPath(fallback);
            }
            catch (Exception ex)
            {
                Info("temp fallback failed too: " + ex.Message);
                return null;
            }
        }
    }
}
