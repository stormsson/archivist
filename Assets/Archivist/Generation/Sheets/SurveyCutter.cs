using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// §10.1 as amended by D2 — rotation is DERIVED, never rolled, so it becomes a third
    /// readable office signal alongside coverage and (later) style, at the cost of one 2x2
    /// covariance.
    ///
    /// <para>
    /// D2 struck the "surveyed arc" from the Hydrographic row. The arc was circular: it is a
    /// product of cutting, and rotation is step 1 of cutting. Removing it costs nothing —
    /// Hydrographic's cull keeps every rect the coastline crosses, so the survey follows the
    /// whole shore and there was never an arc to speak of.
    /// </para>
    ///
    /// <para>
    /// Every angle leaves this class quantised to 0.1 deg (§4.4). An ulp in a rotation can
    /// shift a rect across the sheet lattice and change the sheet count, which is why §4.4
    /// keeps its rotation clause even after D3 moved the field's quantisation to <c>h01</c>.
    /// The quantised value is what callers must use everywhere downstream.
    /// </para>
    /// </summary>
    public static class Rotations
    {
        /// <summary>
        /// Hydrographic follows the shore: PCA of the MAIN COASTLINE LOOP, sampled by arc
        /// length at <c>u * Tuning.PcaCoastSampleFrac</c> (= u/4, about 380 m).
        ///
        /// <para>
        /// Main loop = the longest coastline polyline; ties broken by first vertex
        /// (x ascending, then y ascending). An atoll has two loops — outer shore and lagoon —
        /// and the outer one wins on length.
        /// </para>
        ///
        /// <para>
        /// SAMPLE BY ARC LENGTH, NOT BY VERTEX. Marching squares emits vertices at a density
        /// that varies with how the coast meets the lattice, so a vertex-weighted covariance
        /// is biased toward whichever stretch happens to run diagonally across cells.
        /// </para>
        ///
        /// <para>
        /// Degenerate case: <c>lambda1/lambda2 &lt; Tuning.PcaIsotropyThreshold</c> yields
        /// 0.0 deg. The guard is not optional — a round island has no long axis, the larger
        /// eigenvector is then noise, and two seeds a hair either side of isotropic would sit
        /// their whole survey 90 deg apart for no reason a player could read. An atoll is the
        /// standing case: its coast is a ring, isotropic by construction, so its Hydrographic
        /// survey comes out north-up.
        /// </para>
        /// </summary>
        /// <param name="coast">The island coastline polylines (§6.1), in ground space.</param>
        /// <param name="u">The island-scale service radius, <c>IslandParams.ServiceRadius</c>.</param>
        public static double Hydrographic(IReadOnlyList<Polyline> coast, double u, out PcaResult pca)
        {
            Polyline main = MainLoop(coast);

            List<V2> samples;
            if (main == null || u <= 0.0)
            {
                samples = new List<V2>();
            }
            else
            {
                samples = main.SampleByArcLength(u * Tuning.PcaCoastSampleFrac);
            }

            // minPoints 2: a covariance needs two distinct points to mean anything. The real
            // guard on this office is the isotropy threshold, not a point count — an island
            // always has a coastline, unlike the high ground Land Survey needs.
            pca = Pca.PrincipalAxis(samples, Tuning.PcaIsotropyThreshold, 2);

            if (pca.Degenerate) return 0.0;
            return NormaliseAxisDeg(pca.AngleDeg);
        }

        /// <summary>
        /// Land Survey follows the ridge: PCA of land points above
        /// <c>Tuning.PeakElevationFrac * MaxElevation</c>, sampled on the
        /// <c>Tuning.BaseCell</c> (64 m) lattice.
        ///
        /// <para>
        /// The lattice is snapped outward to multiples of 64 m from the domain origin, the
        /// same §6.2 rule the contours obey, so the sample set is a subset of one global
        /// lattice and does not shift with the land bbox.
        /// </para>
        ///
        /// <para>
        /// Degenerate case (isotropic, or fewer than <c>Tuning.PcaLandMinPoints</c> points):
        /// falls back to <c>hydroDeg + 90</c>, NOT to north. If both degenerate cases fell
        /// back to 0 deg, Land Survey and Garrison would share a rotation on exactly the
        /// islands where the third office signal is already weakest. Cross-grain is
        /// geometric, deterministic, distinct from both other offices, and reads as a
        /// traverse run across the island rather than along it.
        /// </para>
        ///
        /// <para>
        /// A ridge running along the island's long axis legitimately gives
        /// <c>theta_land ~= theta_hydro</c>. That is not a bug and is deliberately not
        /// pre-empted with a forced-separation rule — §13.7 reports the separation
        /// distribution over 50 seeds first (D2, "measure before hardening").
        /// </para>
        /// </summary>
        /// <param name="hydroDeg">The already-derived, already-quantised Hydrographic rotation.</param>
        public static double LandSurvey(IHeightField field, Rect2 landBounds, double hydroDeg, out PcaResult pca)
        {
            var points = new List<V2>();

            if (field != null && !landBounds.IsEmpty)
            {
                double threshold = Tuning.PeakElevationFrac * field.Params.MaxElevation;
                Rect2 lattice = landBounds.SnapOut(Tuning.BaseCell);

                // Integer stepping, never an accumulating += : the sample positions must be
                // exact multiples of BaseCell from the origin (§6.2).
                int nx = (int)Math.Round(lattice.Width / Tuning.BaseCell);
                int ny = (int)Math.Round(lattice.Height / Tuning.BaseCell);

                for (int j = 0; j <= ny; j++)
                {
                    double y = lattice.MinY + j * Tuning.BaseCell;
                    for (int i = 0; i <= nx; i++)
                    {
                        double x = lattice.MinX + i * Tuning.BaseCell;
                        if (field.Elevation(x, y) >= threshold) points.Add(new V2(x, y));
                    }
                }
            }

            pca = Pca.PrincipalAxis(points, Tuning.PcaIsotropyThreshold, Tuning.PcaLandMinPoints);

            if (pca.Degenerate) return NormaliseAxisDeg(hydroDeg + 90.0);
            return NormaliseAxisDeg(pca.AngleDeg);
        }

        /// <summary>
        /// Garrison is 0 deg, always — true north. Grid discipline is what Garrison *is*
        /// (§10.1), and §6.4's grid is defined in the true-north frame precisely because of
        /// this. There is no degenerate case.
        /// </summary>
        public static double Garrison()
        {
            return 0.0;
        }

        /// <summary>
        /// Longest coastline polyline; ties broken by first vertex (x ascending, then y
        /// ascending) so the choice never depends on the order the extractor emitted loops in.
        /// </summary>
        static Polyline MainLoop(IReadOnlyList<Polyline> coast)
        {
            if (coast == null) return null;

            Polyline best = null;
            double bestLength = 0.0;

            for (int i = 0; i < coast.Count; i++)
            {
                Polyline candidate = coast[i];
                if (candidate == null || candidate.Count < 2) continue;

                double length = candidate.Length;
                if (best == null || length > bestLength ||
                    (length == bestLength && FirstVertexPrecedes(candidate, best)))
                {
                    best = candidate;
                    bestLength = length;
                }
            }

            return best;
        }

        static bool FirstVertexPrecedes(Polyline a, Polyline b)
        {
            V2 pa = a[0];
            V2 pb = b[0];
            if (pa.X != pb.X) return pa.X < pb.X;
            return pa.Y < pb.Y;
        }

        /// <summary>
        /// A survey rotation is an AXIS, not a heading: rotating a sheet rect by 180 deg gives
        /// the same rect. Folding every angle into [0, 180) before quantising keeps
        /// <c>hydroDeg + 90</c> from producing an equivalent-but-differently-numbered frame,
        /// and makes §13.7's <c>|theta_hydro - theta_land| mod 180</c> well defined.
        /// Quantisation to 0.1 deg (§4.4) happens last, with a guard against 179.97 rounding
        /// up to a full 180.
        /// </summary>
        internal static double NormaliseAxisDeg(double deg)
        {
            double d = deg % 180.0;
            if (d < 0.0) d += 180.0;

            double q = Q.Deg(d);
            if (q >= 180.0) q -= 180.0;
            return q;
        }
    }

    /// <summary>
    /// §10 — survey cutting. A survey is one office's expedition over one island (§2):
    /// one office, one year, one scale, one rotation, one numbered set of sheets.
    ///
    /// <para>
    /// Sheet count is NOT a knob (§8.1). It falls out of paper size, scale, island size and
    /// the cull. R1.8 (coverage must be partial) and R2.10a (overlap is required) are
    /// likewise satisfied by construction here, not by tuning.
    /// </para>
    /// </summary>
    public static class SurveyCutter
    {
        /// <summary>
        /// IMPLEMENTATION CHOICE, not a spec value. <c>SurveySpec.Year</c> is label-only in
        /// v1 (no eras, §10) and neither §10 nor §12 gives a rule; D5's open list records it
        /// as "pick a per-office range when the header block is drawn". The range below is a
        /// plausible chart-office span for the setting and is drawn from its own named stream
        /// so adding it cannot perturb any existing feature (§4.3, asserted by A2).
        /// </summary>
        const int YearMinInclusive = 1860;
        const int YearMaxExclusive = 1936;

        /// <summary>
        /// §10.1 + §8.1 — everything about a survey that is fixed before any rect exists.
        /// Separated from <see cref="Cut"/> so the caller can derive the Hydrographic
        /// rotation once and hand it to all three offices: Land Survey's degenerate case
        /// falls back to <c>hydroDeg + 90</c> (D2), so Hydrographic must be planned first.
        /// </summary>
        /// <param name="coast">
        /// Accepted for signature symmetry with <see cref="Cut"/> and not read: the only
        /// thing planning needs from the coastline is the Hydrographic rotation, and that
        /// arrives already derived in <paramref name="hydroDeg"/> so it is computed once per
        /// island rather than once per office.
        /// </param>
        /// <param name="hydroDeg">
        /// The island's Hydrographic rotation, from <see cref="Rotations.Hydrographic"/>.
        /// Used directly for that office, and as Land Survey's cross-grain fallback.
        /// </param>
        public static SurveySpec PlanSurvey(IHeightField field, IReadOnlyList<Polyline> coast,
                                            Rect2 landBounds, Office office, double hydroDeg)
        {
            double rotationDeg = RotationFor(field, landBounds, office, hydroDeg);

            // Scale is per office (§8.1 as amended by F1): Hydrographic 1:5000, the terrain
            // offices 1:2500. 1:25000 and 1:50000 belong to the whole-island sheet alone;
            // see CutWholeIsland.
            MapScale scale = MapScale.ForOffice(office);

            // The coast walk always runs the sheet's LONG edge along the shore, so its
            // orientation is fixed rather than chosen: landscape, every sheet. The lattice
            // offices still pick whichever orientation covers their extent in fewer rects.
            SheetFormat format = office == Office.Hydrographic
                ? SheetFormat.A1.Landscape
                : ChooseOrientation(landBounds, rotationDeg, scale, Tuning.OverlapFraction);
            ulong islandSeed = field.Params.Seed;

            return new SurveySpec(islandSeed, office, PickYear(islandSeed, office, false),
                                  scale, rotationDeg, format, Tuning.OverlapFraction, false);
        }

        /// <summary>
        /// §10.2 — the cutting algorithm, in this exact order:
        /// <list type="number">
        /// <item>rotation theta (already fixed on <paramref name="spec"/>, §10.1);</item>
        /// <item>frame space = ground rotated by <c>-theta</c>; sheets tile axis-aligned
        ///       there and are rotated rects in ground space;</item>
        /// <item>extent = the frame-space bbox of the land bbox's four projected corners;</item>
        /// <item>lattice at <c>step = sheetSize * (1 - overlap)</c>, laid CENTRED on the
        ///       extent so leftover margin is split evenly rather than dumped on one edge;</item>
        /// <item>candidates — one rect per lattice cell, centre transformed back to ground;</item>
        /// <item>cull per office (§10.3), including the office-relative R1.5 service test,
        ///       applied to all three offices with no exemption (D1);</item>
        /// <item>order row-major in FRAME space, +u then +v, origin bottom-left;</item>
        /// <item>number 1..N.</item>
        /// </list>
        ///
        /// <para>
        /// §10.4: numbering happens AFTER the cull, never before. Number a 6x4 grid 1-24 and
        /// then drop the sea-only rects and the survey ships with permanent holes at 3, 7, 18
        /// — R2.10b then loses its meaning, because a gap must mean "missing sheet" and R2.9
        /// (incomplete surveys) is cut from v1. Asserted by A4 (§13.4).
        /// </para>
        /// </summary>
        /// <param name="service">
        /// The island's R1.5 service rule (§7.4). A null rule is treated as "served
        /// everywhere" so the cutter can be exercised in isolation; production always passes
        /// one.
        /// </param>
        public static Survey Cut(IHeightField field, IReadOnlyList<Polyline> coast, ServiceRule service,
                                 Rect2 landBounds, SurveySpec spec)
        {
            if (spec.IsWholeIsland)
            {
                return SingleSheetSurvey(spec, landBounds);
            }

            var sheets = new List<Sheet>();
            if (field == null || landBounds.IsEmpty) return new Survey(spec, sheets);

            // 1-2. Rotation and frame space.
            double theta = spec.RotationDeg;

            // 3. Extent.
            Rect2 extent = FrameExtent(landBounds, theta);

            // 4. Lattice, centred on the extent.
            double sheetW = spec.SheetGroundWidth;
            double sheetH = spec.SheetGroundHeight;
            double stepX = sheetW * (1.0 - spec.OverlapFraction);
            double stepY = sheetH * (1.0 - spec.OverlapFraction);

            int cols = CellCount(extent.Width, sheetW, spec.OverlapFraction);
            int rows = CellCount(extent.Height, sheetH, spec.OverlapFraction);

            V2 extentCentre = extent.Centre;
            double firstX = extentCentre.X - (cols - 1) * stepX * 0.5;
            double firstY = extentCentre.Y - (rows - 1) * stepY * 0.5;

            // The coastline is projected into frame space ONCE per survey, not once per rect:
            // the Hydrographic cull walks every segment of it against every candidate.
            List<V2[]> coastFrame;
            List<Rect2> coastFrameBounds;
            BuildFrameCoast(coast, theta, out coastFrame, out coastFrameBounds);

            // Garrison alone is confined to a block (§10.3). Expressed as an inclusive index
            // range so the block is, by construction, a whole number of sheets.
            int blockMinI = 0, blockMaxI = cols - 1, blockMinJ = 0, blockMaxJ = rows - 1;
            if (spec.Office == Office.Garrison)
            {
                GarrisonBlock(field, landBounds, theta, firstX, firstY, stepX, stepY,
                              sheetW, sheetH, cols, rows,
                              out blockMinI, out blockMaxI, out blockMinJ, out blockMaxJ);
            }

            // 5-7. Candidates, cull, and order. The loop nesting IS the order: +u inner,
            // +v outer, both ascending from the bottom-left of the frame-space lattice.
            var keptCentres = new List<V2>();

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    if (i < blockMinI || i > blockMaxI || j < blockMinJ || j > blockMaxJ) continue;

                    var frameCentre = new V2(firstX + i * stepX, firstY + j * stepY);
                    Rect2 frameRect = Rect2.FromCentreSize(frameCentre, sheetW, sheetH);

                    double landFraction, servedFraction;
                    SampleRect(field, service, spec.Office, frameRect, theta,
                               out landFraction, out servedFraction);

                    if (!Keeps(spec.Office, frameRect, coastFrame, coastFrameBounds,
                               landFraction, servedFraction)) continue;

                    keptCentres.Add(ToGround(frameCentre, theta));
                }
            }

            // 8. Number 1..N — after the cull, so every number exists (§10.4, A4).
            for (int n = 0; n < keptCentres.Count; n++)
            {
                sheets.Add(new Sheet(spec, n + 1, keptCentres[n]));
            }

            return new Survey(spec, sheets);
        }

        /// <summary>
        /// §10.5 as amended by D5 — the whole-island survey (R2.2a). Every island carries one:
        /// one office chosen by <c>Streams.For(islandSeed, "wholeIsland")</c>, rotation 0 deg,
        /// ONE sheet centred on the land bbox, orientation chosen to fit.
        ///
        /// <para>
        /// The scale is the SMALLEST of {1:25000, 1:50000} whose map area contains the land
        /// bbox in either orientation. D5 replaced a bare assert here that had no fallback:
        /// <c>NominalRadius</c> is 6080 m jittered +/-8%, so a mountainous island whose land
        /// approaches its nominal radius has a bbox near 13 km and overruns the 12 850 m
        /// portrait width of 1:25000. 1:25000 remains the normal answer; §13.7 reports how
        /// often the fallback fires.
        /// </para>
        ///
        /// <para>
        /// It is the entry point for the island and, in v1, doubles as the reference map.
        /// </para>
        /// </summary>
        public static Survey CutWholeIsland(IHeightField field, Rect2 landBounds, ulong islandSeed)
        {
            Pcg32 pick = Streams.For(islandSeed, "wholeIsland");
            var office = (Office)pick.Range(0, 3);

            MapScale scale;
            SheetFormat format;
            ChooseWholeIslandPaper(landBounds, out scale, out format);

            var spec = new SurveySpec(islandSeed, office, PickYear(islandSeed, office, true),
                                      scale, Rotations.Garrison(), format,
                                      Tuning.OverlapFraction, true);

            return SingleSheetSurvey(spec, landBounds);
        }

        // ---------------------------------------------------------------- rotation & paper

        static double RotationFor(IHeightField field, Rect2 landBounds, Office office, double hydroDeg)
        {
            PcaResult pca;
            switch (office)
            {
                case Office.Hydrographic:
                    // Already derived and quantised by the caller; normalising is idempotent.
                    return Rotations.NormaliseAxisDeg(hydroDeg);
                case Office.LandSurvey:
                    return Rotations.LandSurvey(field, landBounds, hydroDeg, out pca);
                default:
                    return Rotations.Garrison();
            }
        }

        /// <summary>
        /// §8.1: "orientation is chosen per survey to better fit the target region", with no
        /// tie-break stated — D5's open list defers it to implementation. CHOICE: the
        /// orientation that needs FEWER candidate rects to cover the frame-space extent, ties
        /// to portrait. Fewer rects is the only reading of "better fit" that is measurable
        /// before the cull, and it is exactly the quantity §8.1 says is not a knob.
        /// </summary>
        static SheetFormat ChooseOrientation(Rect2 landBounds, double rotationDeg,
                                             MapScale scale, double overlap)
        {
            SheetFormat portrait = SheetFormat.A1;
            SheetFormat landscape = portrait.Landscape;

            if (landBounds.IsEmpty) return portrait;

            Rect2 extent = FrameExtent(landBounds, rotationDeg);

            int portraitCells = CandidateCount(extent, scale, portrait, overlap);
            int landscapeCells = CandidateCount(extent, scale, landscape, overlap);

            return landscapeCells < portraitCells ? landscape : portrait;
        }

        static int CandidateCount(Rect2 extent, MapScale scale, SheetFormat format, double overlap)
        {
            int cols = CellCount(extent.Width, scale.GroundMetres(format.MapWidthMm), overlap);
            int rows = CellCount(extent.Height, scale.GroundMetres(format.MapHeightMm), overlap);
            return cols * rows;
        }

        /// <summary>
        /// How many sheets of ground extent <paramref name="size"/>, laid at
        /// <c>size * (1 - overlap)</c>, are needed to cover <paramref name="span"/>.
        /// n sheets cover <c>size + (n-1) * step</c>. The epsilon absorbs the case where
        /// span lands a float hair over an exact multiple, which would otherwise buy a whole
        /// extra row of sheets for nothing.
        /// </summary>
        static int CellCount(double span, double size, double overlap)
        {
            double step = size * (1.0 - overlap);
            if (step <= 0.0 || span <= size) return 1;
            return (int)Math.Ceiling((span - size) / step - 1e-9) + 1;
        }

        /// <summary>
        /// D5 / §8.1 — the smallest of {1:25000, 1:50000} whose map area contains the land
        /// bbox, trying the orientation that matches the bbox's own aspect first (landscape
        /// if the island is wider than it is tall, portrait otherwise, ties to portrait).
        /// If neither scale fits in either orientation — which the 16 km domain makes
        /// impossible, 1:50000 landscape covering 38 050 x 25 700 m — the fallback scale is
        /// used anyway rather than throwing, since a slightly clipped reference map beats a
        /// hard stop on an otherwise valid seed.
        /// </summary>
        static void ChooseWholeIslandPaper(Rect2 landBounds, out MapScale scale, out SheetFormat format)
        {
            double width = landBounds.IsEmpty ? 0.0 : landBounds.Width;
            double height = landBounds.IsEmpty ? 0.0 : landBounds.Height;

            SheetFormat portrait = SheetFormat.A1;
            SheetFormat landscape = portrait.Landscape;

            SheetFormat preferred = width > height ? landscape : portrait;
            SheetFormat alternate = width > height ? portrait : landscape;

            MapScale[] ladder = { MapScale.WholeIsland, MapScale.WholeIslandFallback };

            for (int s = 0; s < ladder.Length; s++)
            {
                if (Fits(ladder[s], preferred, width, height))
                {
                    scale = ladder[s];
                    format = preferred;
                    return;
                }
                if (Fits(ladder[s], alternate, width, height))
                {
                    scale = ladder[s];
                    format = alternate;
                    return;
                }
            }

            scale = MapScale.WholeIslandFallback;
            format = preferred;
        }

        static bool Fits(MapScale scale, SheetFormat format, double width, double height)
        {
            return scale.GroundMetres(format.MapWidthMm) >= width
                && scale.GroundMetres(format.MapHeightMm) >= height;
        }

        /// <summary>
        /// IMPLEMENTATION CHOICE (see <see cref="YearMinInclusive"/>). The whole-island survey
        /// draws from its own purpose so it does not inherit the same year as that office's
        /// detail survey — a reconnaissance sheet and a detail survey by one office would not
        /// share a date, and identical years would read as a bug.
        /// </summary>
        static int PickYear(ulong islandSeed, Office office, bool wholeIsland)
        {
            Pcg32 rng = Streams.For(islandSeed, wholeIsland ? "yearWholeIsland" : "year", (int)office);
            return rng.Range(YearMinInclusive, YearMaxExclusive);
        }

        static Survey SingleSheetSurvey(SurveySpec spec, Rect2 landBounds)
        {
            V2 centre = landBounds.IsEmpty ? V2.Zero : landBounds.Centre;
            var sheets = new List<Sheet> { new Sheet(spec, 1, centre) };
            return new Survey(spec, sheets);
        }

        // ---------------------------------------------------------------------- frame space

        /// <summary>
        /// Ground -> frame. Frame space is ground space rotated by <c>-theta</c> (§2), which
        /// is exactly what <see cref="Sheet.FrameRect"/> does; the two must never disagree.
        /// </summary>
        static V2 ToFrame(V2 ground, double rotationDeg)
        {
            return ground.RotateDeg(-rotationDeg);
        }

        /// <summary>Frame -> ground, the inverse of <see cref="ToFrame"/>.</summary>
        static V2 ToGround(V2 frame, double rotationDeg)
        {
            return frame.RotateDeg(rotationDeg);
        }

        /// <summary>
        /// §10.2 step 3 — project the four corners of a ground-space bbox into frame space and
        /// take their frame-space bbox. Conservative for a rotated rect, which is the point:
        /// the lattice must cover the land, not hug it.
        /// </summary>
        static Rect2 FrameExtent(Rect2 groundRect, double rotationDeg)
        {
            Rect2 result = Rect2.Empty;
            result = result.Encapsulate(ToFrame(new V2(groundRect.MinX, groundRect.MinY), rotationDeg));
            result = result.Encapsulate(ToFrame(new V2(groundRect.MaxX, groundRect.MinY), rotationDeg));
            result = result.Encapsulate(ToFrame(new V2(groundRect.MaxX, groundRect.MaxY), rotationDeg));
            result = result.Encapsulate(ToFrame(new V2(groundRect.MinX, groundRect.MaxY), rotationDeg));
            return result;
        }

        /// <summary>
        /// Projects the coastline into frame space once per survey. Closed loops get their
        /// first point repeated so the closing segment is walked like any other, and each
        /// polyline keeps a frame-space bbox so the per-rect test can reject whole loops with
        /// one comparison.
        /// </summary>
        static void BuildFrameCoast(IReadOnlyList<Polyline> coast, double rotationDeg,
                                    out List<V2[]> polys, out List<Rect2> bounds)
        {
            polys = new List<V2[]>();
            bounds = new List<Rect2>();
            if (coast == null) return;

            for (int p = 0; p < coast.Count; p++)
            {
                Polyline line = coast[p];
                if (line == null || line.Count < 2) continue;

                int count = line.Count + (line.Closed ? 1 : 0);
                var points = new V2[count];
                Rect2 box = Rect2.Empty;

                for (int i = 0; i < count; i++)
                {
                    points[i] = ToFrame(line[i % line.Count], rotationDeg);
                    box = box.Encapsulate(points[i]);
                }

                polys.Add(points);
                bounds.Add(box);
            }
        }

        // ----------------------------------------------------------------------------- cull

        /// <summary>
        /// §10.3 sampling. Each candidate is sampled on a
        /// <c>Tuning.CullSampleGrid x Tuning.CullSampleGrid</c> (16x16) lattice IN FRAME
        /// SPACE — cell centres, so the samples are symmetric about the rect and no sample
        /// sits on an edge shared with the neighbouring sheet.
        ///
        /// <para>
        /// <c>landFraction</c> = fraction of samples with <c>Height01 &gt;= SeaLevel</c> (the
        /// tie counts as land, §4.4). <c>servedFraction</c> = fraction of the LAND samples
        /// that are served for this office (§7.4). With no land samples at all,
        /// <c>servedFraction</c> is 0 — such a rect is pure sea and every office's
        /// landFraction floor removes it anyway.
        /// </para>
        ///
        /// <para>
        /// Both fractions come from the same 256 samples, so the service test costs no extra
        /// field evaluation (D1's implementation note).
        /// </para>
        /// </summary>
        static void SampleRect(IHeightField field, ServiceRule service, Office office,
                               Rect2 frameRect, double rotationDeg,
                               out double landFraction, out double servedFraction)
        {
            int n = Tuning.CullSampleGrid;
            int total = n * n;
            int landCount = 0;
            int servedCount = 0;

            double width = frameRect.Width;
            double height = frameRect.Height;

            for (int b = 0; b < n; b++)
            {
                double frameY = frameRect.MinY + height * (b + 0.5) / n;
                for (int a = 0; a < n; a++)
                {
                    double frameX = frameRect.MinX + width * (a + 0.5) / n;
                    V2 ground = ToGround(new V2(frameX, frameY), rotationDeg);

                    if (!field.IsLand(ground)) continue;
                    landCount++;

                    if (service == null || service.Served(ground, office)) servedCount++;
                }
            }

            landFraction = (double)landCount / total;
            servedFraction = landCount > 0 ? (double)servedCount / landCount : 0.0;
        }

        /// <summary>
        /// §10.3 as amended by D1 — the cull, UNIFORM across all three offices.
        ///
        /// <list type="table">
        /// <item><term>Hydrographic</term><description>rects any coastline polyline crosses,
        ///   and <c>servedFraction &gt;= 0.50</c>. Shape: a ring following the shore, sea on
        ///   one side.</description></item>
        /// <item><term>Land Survey</term><description><c>landFraction &gt;= 0.60</c> and
        ///   <c>servedFraction &gt;= 0.50</c>. Shape: a filled interior blob.</description></item>
        /// <item><term>Garrison</term><description>in the chosen block,
        ///   <c>landFraction &gt;= 0.02</c>, and <c>servedFraction &gt;= 0.50</c>. Shape: a
        ///   tidy rectangular block that ignores geography.</description></item>
        /// </list>
        ///
        /// <para>
        /// D1 DELETED Garrison's old service exemption and it must not be reintroduced.
        /// Because <c>servedFraction</c> is office-relative, one uniform test means something
        /// different in each row: Garrison is served everywhere by its own grid so the test is
        /// a no-op there and the 2% land floor is what removes pure-sea rects; Hydrographic is
        /// served by its soundings so a bare stretch of shore keeps its sheet; Land Survey is
        /// the only office the service test actually culls.
        /// </para>
        /// </summary>
        static bool Keeps(Office office, Rect2 frameRect, List<V2[]> coastFrame,
                          List<Rect2> coastFrameBounds, double landFraction, double servedFraction)
        {
            if (servedFraction < Tuning.ServedThreshold) return false;

            switch (office)
            {
                case Office.Hydrographic:
                    return CrossesCoast(frameRect, coastFrame, coastFrameBounds);
                case Office.LandSurvey:
                    return landFraction >= Tuning.LandFractionMinLandSurvey;
                default:
                    return landFraction >= Tuning.LandFractionMinGarrison;
            }
        }

        /// <summary>
        /// "Rects any coastline polyline crosses" (§10.3), done exactly: any coastline SEGMENT
        /// that shares a point with the closed frame-space rect. Both are in frame space, so
        /// the rect is axis-aligned and the test is a Liang-Barsky clip rather than a
        /// rotated-rect SAT.
        ///
        /// <para>
        /// This deliberately counts a segment lying wholly inside the rect, so a small island
        /// or a stack fully contained by one sheet still reads as coast. It does NOT count a
        /// rect lying wholly inside a coastline loop with no segment near it — that rect is
        /// open interior or open water, and it has no shore to survey.
        /// </para>
        /// </summary>
        static bool CrossesCoast(Rect2 frameRect, List<V2[]> polys, List<Rect2> bounds)
        {
            for (int p = 0; p < polys.Count; p++)
            {
                if (!bounds[p].Intersects(frameRect)) continue;

                V2[] points = polys[p];
                for (int i = 1; i < points.Length; i++)
                {
                    if (SegmentMeetsRect(points[i - 1], points[i], frameRect)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Liang-Barsky segment/AABB overlap. Clips the parameter interval [0,1] against the
        /// four slabs; a surviving interval means the segment meets the closed rect. Handles
        /// the axis-parallel case (<c>p == 0</c>) explicitly instead of dividing by zero.
        /// </summary>
        static bool SegmentMeetsRect(V2 a, V2 b, Rect2 r)
        {
            double t0 = 0.0;
            double t1 = 1.0;
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;

            if (!ClipParameter(-dx, a.X - r.MinX, ref t0, ref t1)) return false;
            if (!ClipParameter(dx, r.MaxX - a.X, ref t0, ref t1)) return false;
            if (!ClipParameter(-dy, a.Y - r.MinY, ref t0, ref t1)) return false;
            if (!ClipParameter(dy, r.MaxY - a.Y, ref t0, ref t1)) return false;

            return true;
        }

        static bool ClipParameter(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0.0) return q >= 0.0;      // parallel to this slab: inside iff not outside it

            double t = q / p;
            if (p < 0.0)
            {
                if (t > t1) return false;
                if (t > t0) t0 = t;
            }
            else
            {
                if (t < t0) return false;
                if (t < t1) t1 = t;
            }
            return true;
        }

        // ------------------------------------------------------------------- Garrison block

        /// <summary>
        /// §10.3 — "in the true-north frame, pick the quadrant of the land bbox containing the
        /// most land; the block is that quadrant's land bbox expanded outward to whole sheets."
        ///
        /// <para>
        /// Garrison's rotation is 0 deg, so the true-north frame and its survey frame are the
        /// same space and <see cref="FrameExtent"/> is the identity here. Written through the
        /// frame transform anyway so the block does not silently become wrong if Garrison ever
        /// rotates.
        /// </para>
        ///
        /// <para>
        /// "Expanded outward to whole sheets" is expressed as an INCLUSIVE INDEX RANGE over
        /// the lattice: every cell whose sheet rect meets the quadrant's land bbox. Because
        /// meeting the bbox is monotone in both i and j, that set is always a contiguous
        /// rectangle of indices — a tidy block, by construction rather than by rounding
        /// arithmetic that would have to re-derive the lattice phase.
        /// </para>
        /// </summary>
        static void GarrisonBlock(IHeightField field, Rect2 landBounds, double rotationDeg,
                                  double firstX, double firstY, double stepX, double stepY,
                                  double sheetW, double sheetH, int cols, int rows,
                                  out int minI, out int maxI, out int minJ, out int maxJ)
        {
            minI = 0; maxI = -1; minJ = 0; maxJ = -1;    // empty block until proven otherwise

            Rect2 quadrant = BusiestQuadrantLandBounds(field, landBounds);
            if (quadrant.IsEmpty) return;

            Rect2 target = FrameExtent(quadrant, rotationDeg);

            int loI = cols, hiI = -1, loJ = rows, hiJ = -1;

            for (int i = 0; i < cols; i++)
            {
                double centreX = firstX + i * stepX;
                if (centreX + sheetW * 0.5 < target.MinX) continue;
                if (centreX - sheetW * 0.5 > target.MaxX) continue;
                if (i < loI) loI = i;
                if (i > hiI) hiI = i;
            }

            for (int j = 0; j < rows; j++)
            {
                double centreY = firstY + j * stepY;
                if (centreY + sheetH * 0.5 < target.MinY) continue;
                if (centreY - sheetH * 0.5 > target.MaxY) continue;
                if (j < loJ) loJ = j;
                if (j > hiJ) hiJ = j;
            }

            if (hiI < loI || hiJ < loJ) return;

            minI = loI; maxI = hiI; minJ = loJ; maxJ = hiJ;
        }

        /// <summary>
        /// Splits the land bbox at its own centre and returns the land bbox of the quadrant
        /// holding the most land.
        ///
        /// <para>
        /// Land is measured by counting land samples on the <c>Tuning.BaseCell</c> (64 m)
        /// lattice snapped to the domain origin (§6.2). The four quadrants of a bbox halved at
        /// its centre have equal area, so sample counts are directly comparable. Ties go to
        /// the lowest quadrant index — SW, SE, NW, NE — which is a stated answer rather than a
        /// coin flip, and a tie is only reachable on an exactly symmetric island.
        /// </para>
        /// </summary>
        static Rect2 BusiestQuadrantLandBounds(IHeightField field, Rect2 landBounds)
        {
            if (field == null || landBounds.IsEmpty) return Rect2.Empty;

            Rect2 lattice = landBounds.SnapOut(Tuning.BaseCell);
            int nx = (int)Math.Round(lattice.Width / Tuning.BaseCell);
            int ny = (int)Math.Round(lattice.Height / Tuning.BaseCell);

            V2 split = landBounds.Centre;

            var counts = new int[4];
            var boxes = new Rect2[4];
            for (int q = 0; q < 4; q++) boxes[q] = Rect2.Empty;

            for (int j = 0; j <= ny; j++)
            {
                double y = lattice.MinY + j * Tuning.BaseCell;
                for (int i = 0; i <= nx; i++)
                {
                    double x = lattice.MinX + i * Tuning.BaseCell;
                    if (!field.IsLand(x, y)) continue;

                    // 0 = SW, 1 = SE, 2 = NW, 3 = NE.
                    int quadrant = (y >= split.Y ? 2 : 0) + (x >= split.X ? 1 : 0);
                    counts[quadrant]++;
                    boxes[quadrant] = boxes[quadrant].Encapsulate(new V2(x, y));
                }
            }

            int best = 0;
            for (int q = 1; q < 4; q++)
            {
                if (counts[q] > counts[best]) best = q;   // strict >, so ties keep the lower index
            }

            return counts[best] > 0 ? boxes[best] : Rect2.Empty;
        }
    }
}
