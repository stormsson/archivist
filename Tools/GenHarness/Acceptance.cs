using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Archivist.Generation;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Harness
{
    /// <summary>§13 acceptance. Runs headless because Generation has no UnityEngine reference (§14).</summary>
    public static class Acceptance
    {
        const ulong Collection = 8412UL;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool Failed;

        static void Pass(string id, string msg) { Console.WriteLine("  PASS  " + id + "  " + msg); }
        static void Fail(string id, string msg) { Console.WriteLine("  FAIL  " + id + "  " + msg); Failed = true; }
        static void Info(string msg)            { Console.WriteLine("        " + msg); }
        static void Metric(string id, string msg) { Console.WriteLine("  ----  " + id + "  " + msg); }

        static string F(double d) { return d.ToString("F6", Inv); }

        // ---------------------------------------------------------------- A2
        /// <summary>§13.2 — same seed, identical island, across runs.</summary>
        public static void A2_Determinism()
        {
            Console.WriteLine("A2  Determinism");
            ulong seed = Streams.IslandSeed(Collection, 0);
            ulong first = HashIsland(Island.FromSeed(seed));
            for (int i = 1; i < 100; i++)
            {
                ulong h = HashIsland(Island.FromSeed(seed));
                if (h != first) { Fail("A2", "island hash diverged on iteration " + i); return; }
            }
            Pass("A2", "100 generations identical, hash " + first.ToString("X16"));

            // Drawing from an unrelated stream must not perturb anything (§4.3).
            var noise = Streams.For(seed, "unrelated.purpose");
            for (int i = 0; i < 1000; i++) noise.NextUInt();
            if (HashIsland(Island.FromSeed(seed)) != first) Fail("A2", "unrelated stream perturbed the island");
            else Pass("A2", "unrelated sub-stream draws leave the island bit-identical");
        }

        static ulong HashIsland(Island isl)
        {
            var sb = new StringBuilder();
            sb.Append(isl.Params.Character).Append('|').Append(F(isl.Params.NominalRadius)).Append('|');
            sb.Append(isl.Name).Append('|');
            for (int i = 0; i < isl.Coastline.Count; i++)
            {
                Polyline p = isl.Coastline[i];
                sb.Append('C').Append(p.Count).Append(p.Closed ? 'c' : 'o');
                for (int v = 0; v < p.Count; v++) sb.Append(F(p[v].X)).Append(',').Append(F(p[v].Y)).Append(';');
            }
            for (int i = 0; i < isl.Features.Peaks.Count; i++)
            {
                Peak k = isl.Features.Peaks[i];
                sb.Append('P').Append(F(k.Position.X)).Append(',').Append(F(k.Position.Y))
                  .Append(',').Append(k.SpotHeightM).Append(',').Append(k.Name ?? "-").Append(';');
            }
            for (int i = 0; i < isl.Features.Settlements.Count; i++)
            {
                Settlement s = isl.Features.Settlements[i];
                sb.Append('S').Append(F(s.Position.X)).Append(',').Append(F(s.Position.Y))
                  .Append(',').Append(s.Name ?? "-").Append(';');
            }
            for (int i = 0; i < isl.Features.Rivers.Count; i++)
                sb.Append('R').Append(isl.Features.Rivers[i].Course.Count).Append(';');
            for (int i = 0; i < isl.Surveys.Count; i++)
            {
                Survey sv = isl.Surveys[i];
                sb.Append('V').Append(sv.Spec.Office).Append(',').Append(sv.Spec.Year).Append(',')
                  .Append(sv.Spec.Scale.Denominator).Append(',').Append(F(sv.Spec.RotationDeg)).Append(',')
                  .Append(sv.SheetCount).Append(';');
                for (int s = 0; s < sv.Sheets.Count; s++)
                    sb.Append(sv.Sheets[s].Number).Append(':').Append(F(sv.Sheets[s].CentreGround.X))
                      .Append(',').Append(F(sv.Sheets[s].CentreGround.Y)).Append(';');
            }
            return Hash.Fnv1a64(sb.ToString());
        }

        // ---------------------------------------------------------------- A3
        /// <summary>§13.3 — the §6.2 lattice rule. Adjacent rects must not tear.</summary>
        public static void A3_NoSeams()
        {
            Console.WriteLine("A3  No seams");
            Island isl = Island.FromSeed(Streams.IslandSeed(Collection, 3));
            int lod = Contours.LodForScale(5000);
            double cell = Contours.CellSizeForLod(lod);
            double tol = 1e-6 * cell;

            // Two rects sharing a border, each snapped to the same global lattice.
            V2 c = isl.LandBounds.Centre;
            double w = 2000, h = 2000;
            Rect2 left  = new Rect2(c.X - w, c.Y - h, c.X,     c.Y + h);
            Rect2 right = new Rect2(c.X,     c.Y - h, c.X + w, c.Y + h);

            var a = Contours.Extract(isl.Field, left,  cell, isl.Params.SeaLevel);
            var b = Contours.Extract(isl.Field, right, cell, isl.Params.SeaLevel);

            var onBorderA = VerticesNear(a, c.X, tol);
            var onBorderB = VerticesNear(b, c.X, tol);

            if (onBorderA.Count == 0 && onBorderB.Count == 0)
            {
                Metric("A3", "no coastline crosses the test border on this seed; inconclusive");
                return;
            }
            double worst = 0;
            int unmatched = 0;
            for (int i = 0; i < onBorderA.Count; i++)
            {
                double best = double.MaxValue;
                for (int j = 0; j < onBorderB.Count; j++)
                    best = Math.Min(best, Math.Abs(onBorderA[i].Y - onBorderB[j].Y));
                if (best > tol) unmatched++;
                if (best < double.MaxValue) worst = Math.Max(worst, Math.Min(best, 1e9));
            }
            if (unmatched > 0) Fail("A3", unmatched + "/" + onBorderA.Count + " border vertices unmatched, worst " + F(worst));
            else Pass("A3", onBorderA.Count + " border vertices agree within " + F(tol) + " m (worst " + F(worst) + ")");
        }

        static List<V2> VerticesNear(IReadOnlyList<Polyline> lines, double x, double tol)
        {
            var outv = new List<V2>();
            for (int i = 0; i < lines.Count; i++)
                for (int v = 0; v < lines[i].Count; v++)
                    if (Math.Abs(lines[i][v].X - x) <= tol) outv.Add(lines[i][v]);
            return outv;
        }

        // ---------------------------------------------------------------- A4
        /// <summary>§13.4 — numbers are exactly 1..N, contiguous, no duplicates (§10.4).</summary>
        public static void A4_Numbering()
        {
            Console.WriteLine("A4  Numbering");
            int checkedSurveys = 0, bad = 0;
            for (int i = 0; i < 20; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                for (int s = 0; s < isl.Surveys.Count; s++)
                {
                    Survey sv = isl.Surveys[s];
                    checkedSurveys++;
                    var seen = new bool[sv.SheetCount + 1];
                    for (int k = 0; k < sv.Sheets.Count; k++)
                    {
                        int n = sv.Sheets[k].Number;
                        if (n < 1 || n > sv.SheetCount) { bad++; break; }
                        if (seen[n]) { bad++; break; }
                        seen[n] = true;
                    }
                }
            }
            if (bad > 0) Fail("A4", bad + " of " + checkedSurveys + " surveys have non-contiguous or duplicate numbers");
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
            foreach (Office o in new[] { Office.Hydrographic, Office.LandSurvey, Office.Garrison })
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

            foreach (Office o in new[] { Office.Hydrographic, Office.LandSurvey, Office.Garrison })
            {
                double pct = perOffice[o] == 0 ? 0 : 100.0 * thin[o] / perOffice[o];
                Metric("A5b", o + ": " + pct.ToString("F1", Inv) + "% coast/grid only  (" + thin[o] + "/" + perOffice[o] + ")");
            }
        }

        struct SheetContent { public bool Any; public bool AnyBeyondCoastAndGrid; }

        static SheetContent Content(Island isl, Sheet sheet)
        {
            var r = new SheetContent();
            Office o = sheet.Survey.Office;
            Rect2 bounds = sheet.GroundBounds;

            if (FeatureMatrix.Draws(o, FeatureClass.Grid)) r.Any = true;

            if (FeatureMatrix.Draws(o, FeatureClass.Coast))
            {
                for (int i = 0; i < isl.Coastline.Count && !r.Any; i++)
                    if (isl.Coastline[i].Bounds.Intersects(bounds)) r.Any = true;
            }
            if (FeatureMatrix.Draws(o, FeatureClass.Peak))
                for (int i = 0; i < isl.Features.Peaks.Count; i++)
                    if (bounds.Contains(isl.Features.Peaks[i].Position)) { r.Any = true; r.AnyBeyondCoastAndGrid = true; }
            if (FeatureMatrix.Draws(o, FeatureClass.Settlement))
                for (int i = 0; i < isl.Features.Settlements.Count; i++)
                    if (bounds.Contains(isl.Features.Settlements[i].Position)) { r.Any = true; r.AnyBeyondCoastAndGrid = true; }
            if (FeatureMatrix.Draws(o, FeatureClass.River))
                for (int i = 0; i < isl.Features.Rivers.Count; i++)
                    if (isl.Features.Rivers[i].Course.Bounds.Intersects(bounds)) { r.Any = true; r.AnyBeyondCoastAndGrid = true; }
            if (FeatureMatrix.Draws(o, FeatureClass.Sounding))
                if (Soundings.ForRect(isl.Field, bounds).Count > 0) { r.Any = true; r.AnyBeyondCoastAndGrid = true; }
            if (FeatureMatrix.Draws(o, FeatureClass.Contour))
                if (isl.Service.ServedClass(bounds.Centre, FeatureClass.Contour)) { r.Any = true; r.AnyBeyondCoastAndGrid = true; }
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

        static bool IntersectionHasShared(Island isl, Rect2 x, Office a, Office b)
        {
            foreach (FeatureClass c in FeatureMatrix.Drawn(a))
            {
                if (!FeatureMatrix.Draws(b, c)) continue;
                switch (c)
                {
                    case FeatureClass.Coast:
                        for (int i = 0; i < isl.Coastline.Count; i++)
                            if (isl.Coastline[i].Bounds.Intersects(x)) return true;
                        break;
                    case FeatureClass.Peak:
                        for (int i = 0; i < isl.Features.Peaks.Count; i++)
                            if (x.Contains(isl.Features.Peaks[i].Position)) return true;
                        break;
                    case FeatureClass.Settlement:
                        for (int i = 0; i < isl.Features.Settlements.Count; i++)
                            if (x.Contains(isl.Features.Settlements[i].Position)) return true;
                        break;
                }
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
                Metric("A7", "Hydro/Land rotation separation: median " + separations[separations.Count / 2].ToString("F1", Inv)
                             + " deg,  " + close + "/" + separations.Count + " below 8 deg (D2)");
            }
        }

        // ---------------------------------------------------------------- A8
        /// <summary>§13.8 — island gen &lt; 250 ms, one sheet re-contoured at 1:5000 &lt; 50 ms.</summary>
        public static void A8_Performance()
        {
            Console.WriteLine("A8  Performance");
            Island warm = Island.FromSeed(Streams.IslandSeed(Collection, 99));
            var sw = new Stopwatch();

            var gen = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                sw.Restart();
                Island.FromSeed(Streams.IslandSeed(Collection, 200 + i));
                sw.Stop();
                gen.Add(sw.Elapsed.TotalMilliseconds);
            }
            gen.Sort();
            double medGen = gen[gen.Count / 2];
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

            var one = new List<double>();
            for (int i = 0; i < 10; i++)
            {
                sw.Restart();
                Contours.Extract(warm.Field, sv.Sheets[i % sv.SheetCount].GroundBounds, cell, warm.Params.SeaLevel);
                sw.Stop();
                one.Add(sw.Elapsed.TotalMilliseconds);
            }
            one.Sort();
            double medSheet = one[one.Count / 2];
            if (medSheet < 50.0) Pass("A8", "sheet re-contour at 1:" + denom + " median " + medSheet.ToString("F1", Inv) + " ms (< 50)");
            else Fail("A8", "sheet re-contour at 1:" + denom + " median " + medSheet.ToString("F1", Inv) + " ms (>= 50)");
        }

        // ---------------------------------------------------------------- summary
        public static void Describe(ulong collectionSeed, int index)
        {
            Island isl = Island.FromSeed(Streams.IslandSeed(collectionSeed, index));
            Console.WriteLine();
            Console.WriteLine("island " + index + "  " + isl.Name + "  " + isl.Params.Character
                              + "  radius " + isl.Params.NominalRadius.ToString("F0", Inv) + " m");
            Console.WriteLine("  coast loops " + isl.Coastline.Count
                              + "   peaks " + isl.Features.Peaks.Count
                              + "   settlements " + isl.Features.Settlements.Count
                              + "   rivers " + isl.Features.Rivers.Count);
            for (int i = 0; i < isl.Surveys.Count; i++)
            {
                Survey sv = isl.Surveys[i];
                Console.WriteLine("  " + (sv.Spec.IsWholeIsland ? "whole-island" : sv.Spec.Office.ToString())
                                  + "  " + sv.Spec.Year + "  " + sv.Spec.Scale
                                  + "  rot " + sv.Spec.RotationDeg.ToString("F1", Inv)
                                  + "  sheets " + sv.SheetCount);
            }
            Console.WriteLine("  total sheets " + isl.TotalSheets);
        }
    }
}
