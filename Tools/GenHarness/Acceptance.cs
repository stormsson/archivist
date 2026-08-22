using System;
using System.Collections.Generic;
using Archivist.Generation;
using Archivist.Generation.Analysis;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using static Archivist.Harness.Report;

namespace Archivist.Harness
{
    /// <summary>§13 acceptance. Runs headless because Generation has no UnityEngine reference (§14).</summary>
    public static class Acceptance
    {
        // ---------------------------------------------------------------- A2
        /// <summary>§13.2 — same seed, identical island, across runs.</summary>
        public static void A2_Determinism()
        {
            Console.WriteLine("A2  Determinism");
            // The digest itself lives in Generation (IslandDigest) so the harness and the Unity
            // test assembly cannot drift apart over which fields "identical island" covers.
            ulong seed = Streams.IslandSeed(Collection, 0);
            ulong first = IslandDigest.Hash(Island.FromSeed(seed));
            for (int i = 1; i < 100; i++)
            {
                ulong h = IslandDigest.Hash(Island.FromSeed(seed));
                if (h != first) { Fail("A2", "island hash diverged on iteration " + i); return; }
            }
            Pass("A2", "100 generations identical, hash " + first.ToString("X16"));

            // Drawing from an unrelated stream must not perturb anything (§4.3).
            var noise = Streams.For(seed, "unrelated.purpose");
            for (int i = 0; i < 1000; i++) noise.NextUInt();
            if (IslandDigest.Hash(Island.FromSeed(seed)) != first) Fail("A2", "unrelated stream perturbed the island");
            else Pass("A2", "unrelated sub-stream draws leave the island bit-identical");
        }

        // ---------------------------------------------------------------- A3
        /// <summary>
        /// §13.3 — the §6.2 lattice rule. Adjacent rects must not tear.
        ///
        /// <para>The fixture and the comparison both live in <see cref="ContourSeam"/>, shared
        /// with the Unity test that asserts the same thing. This call site is the LAXER of the
        /// two, deliberately and now visibly: nearest-neighbour matching, and no requirement
        /// that the two sides put the same NUMBER of vertices on the border. An extra crossing
        /// on one side is caught by <c>ContourSeamTests</c>, not here.</para>
        /// </summary>
        public static void A3_NoSeams()
        {
            Console.WriteLine("A3  No seams");
            Island isl = Island.FromSeed(Streams.IslandSeed(Collection, 3));

            ContourSeam.Border border = ContourSeam.AcrossLandCentre(isl, 5000, 2000.0);
            if (border.Inconclusive)
            {
                Metric("A3", "no coastline crosses the test border on this seed; inconclusive");
                return;
            }

            ContourSeam.Comparison r = ContourSeam.Compare(border.Left, border.Right, border.Tol,
                                                           ContourSeam.Matching.Nearest,
                                                           false /* count check: see the doc above */);
            if (!r.Agree)
                Fail("A3", r.Unmatched + "/" + r.CountA + " border vertices unmatched, worst " + F(r.Worst));
            else
                Pass("A3", r.CountA + " border vertices agree within " + F(border.Tol) + " m (worst " + F(r.Worst) + ")");
        }

        // ---------------------------------------------------------------- A4
        /// <summary>
        /// §13.4 — numbers are exactly 1..N, contiguous, no duplicates (§10.4).
        ///
        /// <para>The rule itself is <see cref="SheetNumbering.Validate"/>, shared with
        /// <c>CutterTests</c> and with POC-03's C4. This call site asks for the SET form:
        /// the numbers must be exactly 1..N with no duplicates, but need not follow list order.
        /// C4 asks for the stronger positional form.</para>
        /// </summary>
        public static void A4_Numbering()
        {
            Console.WriteLine("A4  Numbering");
            int checkedSurveys = 0, bad = 0;
            string firstWhy = null;
            for (int i = 0; i < 20; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                for (int s = 0; s < isl.Surveys.Count; s++)
                {
                    checkedSurveys++;
                    string why;
                    if (SheetNumbering.Validate(isl.Surveys[s], false, out why)) continue;
                    bad++;
                    if (firstWhy == null) firstWhy = "island " + i + ", " + why;
                }
            }
            if (bad > 0)
                Fail("A4", bad + " of " + checkedSurveys + " surveys have non-contiguous or duplicate numbers"
                           + "  (first: " + firstWhy + ")");
            else Pass("A4", checkedSurveys + " surveys numbered 1..N, contiguous, no duplicates");
        }

        // ---------------------------------------------------------------- A5 / A5b
        /// <summary>§13.5 — no blank sheets; and A5b, the thin-sheet metric that keeps A5 honest.</summary>
        public static void A5_NoBlankSheets()
        {
            Console.WriteLine("A5  No blank sheets  (+ A5b thin sheets)");
            int total = 0, blank = 0;
            var thin = new Dictionary<Office, int>();
            var perOffice = new Dictionary<Office, int>();
            foreach (Office o in Offices.All)
            { thin[o] = 0; perOffice[o] = 0; }

            for (int i = 0; i < 20; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                for (int s = 0; s < isl.Surveys.Count; s++)
                {
                    Survey sv = isl.Surveys[s];
                    for (int k = 0; k < sv.Sheets.Count; k++)
                    {
                        total++;
                        Office o = sv.Spec.Office;
                        if (!sv.Spec.IsWholeIsland) perOffice[o]++;
                        SheetContent content = Content(isl, sv.Sheets[k]);
                        if (!content.Any) blank++;
                        if (!sv.Spec.IsWholeIsland && !content.AnyBeyondCoastAndGrid) thin[o]++;
                    }
                }
            }
            if (blank > 0) Fail("A5", blank + " of " + total + " sheets carry nothing their office draws");
            else Pass("A5", total + " sheets all carry at least one drawn class (grid counts)");

            foreach (Office o in Offices.All)
            {
                double pct = perOffice[o] == 0 ? 0 : 100.0 * thin[o] / perOffice[o];
                Metric("A5b", o + ": " + pct.ToString("F1", Inv) + "% coast/grid only  (" + thin[o] + "/" + perOffice[o] + ")");
            }
        }

        struct SheetContent { public bool Any; public bool AnyBeyondCoastAndGrid; }

        /// <summary>Is this ground point inside the region being probed? A predicate, because the
        /// two callers of <see cref="HasClass"/> mean different regions: a SHEET is a rotated
        /// rect and tests with <see cref="Sheet.Contains"/>, while a pair-overlap is a plain
        /// axis-aligned <see cref="Rect2"/>. Passing the wrong one would silently move A5b or
        /// A6.</summary>
        delegate bool InRegion(V2 p);

        /// <summary>
        /// Every drawn class, in one list. Both probes below walk THIS array, so a class added to
        /// <see cref="FeatureClass"/> and wired into <see cref="FeatureMatrix"/> cannot be
        /// remembered in one probe and forgotten in the other — which is exactly how River, Poi,
        /// Sounding, Contour and Grid were once missing from the pair probe while the sheet probe
        /// had them.
        /// </summary>
        static readonly FeatureClass[] AllClasses =
        {
            // Cheapest first: the whole-array walk stops early once both content flags are set,
            // and Sounding re-samples a lattice, so it goes last.
            FeatureClass.Grid, FeatureClass.Peak, FeatureClass.Settlement, FeatureClass.Poi,
            FeatureClass.Contour, FeatureClass.Coast, FeatureClass.River, FeatureClass.Sounding
        };

        /// <summary>The two ways the list above can go stale, both made loud rather than silent:
        /// a class added to the enum but not to <see cref="AllClasses"/> is caught here, and one
        /// added to <see cref="AllClasses"/> but not to <see cref="HasClass"/>'s switch throws on
        /// first use. Silently answering "not present" is what this whole exercise removes.</summary>
        static Acceptance()
        {
            if (AllClasses.Length != FeatureClasses.Count)
                throw new InvalidOperationException("Acceptance.AllClasses lists " + AllClasses.Length
                    + " of " + FeatureClasses.Count + " feature classes; both probes would miss the rest");
        }

        /// <summary>
        /// Does <paramref name="c"/> put anything inside the probed region?
        ///
        /// <para><paramref name="bounds"/> is the region's AABB and <paramref name="inside"/> is
        /// the exact test. The two differ for a rotated sheet, where the AABB strictly
        /// over-covers, so anything point-valued asks <paramref name="inside"/> and
        /// <paramref name="bounds"/> survives only where an exact test would need clipping we do
        /// not have (Coast), where rotation provably cannot change the answer (Contour, sampled
        /// at the centre), or as the pre-filter for a lattice query whose results are then put
        /// through <paramref name="inside"/> anyway (Sounding).</para>
        /// </summary>
        static bool HasClass(Island isl, FeatureClass c, Rect2 bounds, InRegion inside)
        {
            switch (c)
            {
                case FeatureClass.Grid:
                    // ServiceRule serves the Garrison grid everywhere, unconditionally, so it is
                    // present in any non-empty region — this one included.
                    return true;

                case FeatureClass.Coast:
                    // Approximate, deliberately: a polyline bbox against the region bbox is
                    // conservative in BOTH directions, and doing it exactly needs
                    // segment-vs-rotated-rect clipping. Coast never sets AnyBeyondCoastAndGrid,
                    // so on the sheet probe this only loosens the weaker Any flag.
                    for (int i = 0; i < isl.Coastline.Count; i++)
                        if (isl.Coastline[i].Bounds.Intersects(bounds)) return true;
                    return false;

                case FeatureClass.Peak:
                    for (int i = 0; i < isl.Features.Peaks.Count; i++)
                        if (inside(isl.Features.Peaks[i].Position)) return true;
                    return false;

                case FeatureClass.Settlement:
                    for (int i = 0; i < isl.Features.Settlements.Count; i++)
                        if (inside(isl.Features.Settlements[i].Position)) return true;
                    return false;

                case FeatureClass.Poi:
                    for (int i = 0; i < isl.Features.Pois.Count; i++)
                        if (inside(isl.Features.Pois[i].Position)) return true;
                    return false;

                case FeatureClass.River:
                    // Per-vertex, as the Editor's IsThinSheet does: Course.Bounds vs the region
                    // bbox is a bbox-vs-bbox test and fires for rivers that never touch it.
                    for (int i = 0; i < isl.Features.Rivers.Count; i++)
                    {
                        Polyline course = isl.Features.Rivers[i].Course;
                        if (course == null) continue;
                        for (int k = 0; k < course.Count; k++)
                            if (inside(course[k])) return true;
                    }
                    return false;

                case FeatureClass.Sounding:
                    // ForRect applies the generator's real Elevation < Tuning.SoundingDepth rule
                    // on the global 400 m lattice; it only takes an AABB, so its results are
                    // filtered back onto the exact region. Every point it returns lies within
                    // the rect inclusively, so for a plain-Rect2 caller the filter is a no-op.
                    List<Sounding> sd = Soundings.ForRect(isl.Field, bounds);
                    for (int i = 0; i < sd.Count; i++)
                        if (inside(sd[i].Position)) return true;
                    return false;

                case FeatureClass.Contour:
                    // Rotation-invariant: the centre of the AABB and the centre of the rotated
                    // rect are the same point.
                    return isl.Service.ServedClass(bounds.Centre, FeatureClass.Contour);
            }
            throw new ArgumentOutOfRangeException("c", "no probe for feature class " + c
                + "; every class in AllClasses needs a case here");
        }

        /// <summary>What a SHEET carries. The region is the sheet's rotated rect, so the exact
        /// test is <see cref="Sheet.Contains"/>; <see cref="Sheet.GroundBounds"/> is only the
        /// AABB of it and covers ground the sheet never draws.</summary>
        static SheetContent Content(Island isl, Sheet sheet)
        {
            var r = new SheetContent();
            Office o = sheet.Survey.Office;
            Rect2 bounds = sheet.GroundBounds;
            InRegion inside = p => sheet.Contains(p);

            for (int i = 0; i < AllClasses.Length; i++)
            {
                if (r.Any && r.AnyBeyondCoastAndGrid) break;
                FeatureClass c = AllClasses[i];
                if (!FeatureMatrix.Draws(o, c)) continue;
                if (!HasClass(isl, c, bounds, inside)) continue;
                r.Any = true;
                // A5b (D4) counts sheets whose content is coast and grid alone: neither of those
                // two ever makes a sheet worth cutting.
                if (c != FeatureClass.Coast && c != FeatureClass.Grid) r.AnyBeyondCoastAndGrid = true;
            }
            return r;
        }

        // ---------------------------------------------------------------- A6
        /// <summary>§13.6 — overlapping sheets from different offices must share a drawn class. Target >= 90%.</summary>
        public static void A6_SharedClassCoverage()
        {
            Console.WriteLine("A6  Shared-class coverage");
            int pairs = 0, shared = 0;
            for (int i = 0; i < 10; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                var all = new List<Sheet>();
                for (int s = 0; s < isl.Surveys.Count; s++)
                    if (!isl.Surveys[s].Spec.IsWholeIsland) all.AddRange(isl.Surveys[s].Sheets);

                for (int a = 0; a < all.Count; a++)
                    for (int b = a + 1; b < all.Count; b++)
                    {
                        if (all[a].Survey.Office == all[b].Survey.Office) continue;
                        Rect2 ra = all[a].GroundBounds, rb = all[b].GroundBounds;
                        if (!ra.Intersects(rb)) continue;
                        pairs++;
                        FeatureClass cls;
                        if (!FeatureMatrix.SharesDrawnClass(all[a].Survey.Office, all[b].Survey.Office, out cls)) continue;
                        if (IntersectionHasShared(isl, ra.Intersection(rb), all[a].Survey.Office, all[b].Survey.Office)) shared++;
                    }
            }
            double pct = pairs == 0 ? 0 : 100.0 * shared / pairs;
            string msg = pct.ToString("F1", Inv) + "% of " + pairs + " overlapping cross-office pairs share a class in the intersection";
            if (pct >= 90.0) Pass("A6", msg); else Metric("A6", msg + "  (target >= 90%, reported not gated)");
        }

        /// <summary>
        /// Does the overlap region carry a class BOTH offices draw?
        ///
        /// <para><paramref name="x"/> is a Rect2 — the intersection of two sheet AABBs — not a
        /// sheet, so <see cref="Rect2.Contains"/> is the region test here and
        /// <see cref="Sheet.Contains"/> is not applicable. That makes the pair-overlap test
        /// itself approximate: two rotated sheets whose AABBs meet need not actually overlap,
        /// and their true overlap is a convex polygon rather than this rect. Exact rotated-rect
        /// intersection is deferred.</para>
        ///
        /// <para>Which classes get probed is no longer a hand-written list here: it is
        /// <see cref="AllClasses"/>, shared with <see cref="Content"/>. A class missing from one
        /// of the two used to report "shares nothing" for a pair whose only common class was the
        /// one left out.</para>
        /// </summary>
        static bool IntersectionHasShared(Island isl, Rect2 x, Office a, Office b)
        {
            InRegion inside = p => x.Contains(p);
            foreach (FeatureClass c in FeatureMatrix.Drawn(a))
            {
                if (!FeatureMatrix.Draws(b, c)) continue;
                if (HasClass(isl, c, x, inside)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- A7
        /// <summary>§13.7 — sheet economy, plus the three quantities D2 and D5 said to measure.</summary>
        public static void A7_SheetEconomy(int seeds)
        {
            Console.WriteLine("A7  Sheet economy over " + seeds + " seeds");
            var byChar = new Dictionary<IslandCharacter, List<int>>();
            var bboxByChar = new Dictionary<IslandCharacter, List<double>>();
            foreach (IslandCharacter c in Enum.GetValues(typeof(IslandCharacter)))
            { byChar[c] = new List<int>(); bboxByChar[c] = new List<double>(); }

            int fallback = 0;
            var separations = new List<double>();

            for (int i = 0; i < seeds; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                byChar[isl.Params.Character].Add(isl.TotalSheets);
                bboxByChar[isl.Params.Character].Add(Math.Max(isl.LandBounds.Width, isl.LandBounds.Height));

                Survey whole = isl.WholeIslandSurvey;
                if (whole != null && whole.Spec.Scale.Denominator > 25000) fallback++;

                Survey hy = isl.SurveyFor(Office.Hydrographic);
                Survey ld = isl.SurveyFor(Office.LandSurvey);
                if (hy != null && ld != null)
                {
                    double d = Math.Abs(hy.Spec.RotationDeg - ld.Spec.RotationDeg) % 180.0;
                    separations.Add(Math.Min(d, 180.0 - d));
                }
            }

            foreach (IslandCharacter c in new[] { IslandCharacter.Mountainous, IslandCharacter.Fjorded, IslandCharacter.Atoll })
            {
                List<int> v = byChar[c];
                if (v.Count == 0) { Metric("A7", c + ": no seeds"); continue; }
                v.Sort();
                Metric("A7", c + ": n=" + v.Count + "  sheets min " + v[0] + " median " + v[v.Count / 2] + " max " + v[v.Count - 1]
                             + "   (requirements guess 30-60)");
                List<double> bb = bboxByChar[c]; bb.Sort();
                Metric("A7", "   land bbox max extent: min " + bb[0].ToString("F0", Inv) + " m, median "
                             + bb[bb.Count / 2].ToString("F0", Inv) + " m, max " + bb[bb.Count - 1].ToString("F0", Inv) + " m");
            }
            Metric("A7", "whole-island 1:50000 fallback used on " + fallback + "/" + seeds + " seeds (D5)");
            if (separations.Count > 0)
            {
                separations.Sort();
                int close = 0;
                for (int i = 0; i < separations.Count; i++) if (separations[i] < 8.0) close++;
                // Since the coast-walk, Hydrographic's survey rotation no longer orients its
                // sheets — it is the PCA coast axis, still used as Land Survey's degenerate
                // fallback. So this measures axis separation, not how the sheets sit.
                Metric("A7", "coast-axis vs ridge-axis separation: median " + separations[separations.Count / 2].ToString("F1", Inv)
                             + " deg,  " + close + "/" + separations.Count + " below 8 deg (D2; Hydro sheets now rotate per sheet)");
            }
        }

        // ---------------------------------------------------------------- A8
        /// <summary>
        /// §13.8 — island gen &lt; 250 ms, one sheet re-contoured at the survey's own scale
        /// &lt; 50 ms.
        ///
        /// <para>Both loops now warm up before measuring, via <see cref="Timing.MedianMs"/>.
        /// They did not before, while POC-02's render timing did — so A8's first sample was
        /// paying for JIT and for whatever the contourer builds lazily, and with ten samples one
        /// cold outlier can move the median. These numbers may therefore differ from previously
        /// recorded A8 runs; that is the measurement being corrected, not the code under it.</para>
        /// </summary>
        public static void A8_Performance()
        {
            Console.WriteLine("A8  Performance");
            Island warm = Island.FromSeed(Streams.IslandSeed(Collection, 99));

            double medGen = Timing.MedianMs(10, true, i => Island.FromSeed(Streams.IslandSeed(Collection, 200 + i)));
            if (medGen < 250.0) Pass("A8", "island generation median " + medGen.ToString("F1", Inv) + " ms (< 250)");
            else Fail("A8", "island generation median " + medGen.ToString("F1", Inv) + " ms (>= 250)");

            Survey sv = warm.SurveyFor(Office.LandSurvey);
            if (sv == null || sv.SheetCount == 0) sv = warm.SurveyFor(Office.Hydrographic);
            if (sv == null || sv.SheetCount == 0) { Metric("A8", "no sheets to re-contour on the warm seed"); return; }

            // Must follow the survey's OWN scale — hardcoding 1:5000 here measured a
            // 1:2500 sheet at the wrong LOD and flattered the result.
            int denom = sv.Spec.Scale.Denominator;
            int lod = Contours.LodForScale(denom);
            double cell = Contours.CellSizeForLod(lod);

            double medSheet = Timing.MedianMs(10, true,
                i => Contours.Extract(warm.Field, sv.Sheets[i % sv.SheetCount].GroundBounds, cell, warm.Params.SeaLevel));
            if (medSheet < 50.0) Pass("A8", "sheet re-contour at 1:" + denom + " median " + medSheet.ToString("F1", Inv) + " ms (< 50)");
            else Fail("A8", "sheet re-contour at 1:" + denom + " median " + medSheet.ToString("F1", Inv) + " ms (>= 50)");
        }
    }
}
