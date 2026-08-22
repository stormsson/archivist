using System;
using System.Collections.Generic;
using System.Text;
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
    /// <summary>
    /// POC-03 §4 acceptance — C2 to C6. C1 (placeable late, not early) is human-judged and needs
    /// the map table; C7 (the 1:1250 / 1:2500 sweep) is a rendering sweep and lives with B5.
    /// </summary>
    public static class Poc03Acceptance
    {
        // ---------------------------------------------------------------- C2
        /// <summary>
        /// §4 C2 — same seed, identical POIs and detail sheets. The bit-identical half of C2
        /// (P1.5) is A2's job: A2 hashes peaks, settlements, rivers and every survey sheet, and
        /// its value is unchanged by POIs because the new streams are new (§4.3).
        /// </summary>
        public static void C2_Determinism()
        {
            Console.WriteLine("C2  POI determinism");
            for (int i = 0; i < 8; i++)
            {
                ulong seed = Streams.IslandSeed(Collection, i);
                string first = HashPois(Island.FromSeed(seed));
                for (int k = 1; k < 6; k++)
                {
                    if (HashPois(Island.FromSeed(seed)) != first)
                    {
                        Fail("C2", "POIs diverged on island " + i + ", generation " + k);
                        return;
                    }
                }
            }
            Pass("C2", "8 islands x 6 generations: identical POIs and detail sheets");
        }

        static string HashPois(Island isl)
        {
            var sb = new StringBuilder();
            IReadOnlyList<Poi> pois = isl.Features.Pois;
            for (int i = 0; i < pois.Count; i++)
            {
                sb.Append(pois[i].Id).Append(',').Append(pois[i].Kind).Append(',')
                  .Append(F(pois[i].Position.X)).Append(',').Append(F(pois[i].Position.Y)).Append(';');
            }
            Survey sv = isl.SurveyFor(Office.Antiquarian);
            if (sv != null)
            {
                for (int i = 0; i < sv.Sheets.Count; i++)
                {
                    sb.Append(sv.Sheets[i].Number).Append(':').Append(F(sv.Sheets[i].RotationDeg))
                      .Append(',').Append(F(sv.Sheets[i].CentreGround.X)).Append(';');
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- C3
        /// <summary>
        /// §4 C3, <b>GATED</b> — every detail sheet contains at least one drawn feature besides
        /// its own POI (P2.4). This is C1's precondition; if it fails, C1 cannot pass.
        /// </summary>
        public static void C3_PlaceabilityFloor()
        {
            Console.WriteLine("C3  Placeability floor");
            int sheets = 0, bad = 0;
            for (int i = 0; i < 20; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                Survey sv = isl.SurveyFor(Office.Antiquarian);
                if (sv == null) continue;

                for (int k = 0; k < sv.Sheets.Count; k++)
                {
                    sheets++;
                    Poi poi = PoiAt(isl, sv.Sheets[k].CentreGround);
                    if (!isl.Service.ServedByAny(poi.Position,
                                                 FeatureMatrix.Placeability(Office.Antiquarian))) bad++;
                }
            }
            if (bad > 0) Fail("C3", bad + " of " + sheets + " detail sheets carry nothing besides their POI");
            else Pass("C3", sheets + " detail sheets all carry a drawn feature besides their own POI");
        }

        static Poi PoiAt(Island isl, V2 centre)
        {
            IReadOnlyList<Poi> pois = isl.Features.Pois;
            for (int i = 0; i < pois.Count; i++)
            {
                if (pois[i].Position.X == centre.X && pois[i].Position.Y == centre.Y) return pois[i];
            }
            return default(Poi);
        }

        // ---------------------------------------------------------------- C4
        /// <summary>
        /// §4 C4 — the survey run <c>1..N</c> and the detail run <c>1..M</c> are each contiguous
        /// with no duplicates, so a gap in either stays unambiguous (R2.10b). A4 already walks
        /// every survey; this asserts the two runs are separate and that
        /// <see cref="Sheet.IsDetail"/> partitions them.
        ///
        /// <para>The numbering rule is <see cref="SheetNumbering.Validate"/>, shared with A4 and
        /// with <c>CutterTests</c>. This call site asks for the POSITIONAL form — the STRONGER
        /// of the two: <c>Sheets[k].Number == k + 1</c>, so the run follows list order. A4 and
        /// the Unity test ask only for the set form, which a survey emitting its sheets as
        /// <c>{2, 1}</c> would still satisfy.</para>
        /// </summary>
        public static void C4_Numbering()
        {
            Console.WriteLine("C4  Detail numbering");
            int islands = 0, detail = 0, survey = 0, badSurveys = 0, badFlags = 0;
            string firstWhy = null;
            for (int i = 0; i < 20; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                islands++;
                for (int s = 0; s < isl.Surveys.Count; s++)
                {
                    Survey sv = isl.Surveys[s];
                    bool isDetailSurvey = !sv.Spec.IsWholeIsland && sv.Spec.Office == Office.Antiquarian;

                    string why;
                    bool numbered = SheetNumbering.Validate(sv, true, out why);
                    if (!numbered)
                    {
                        badSurveys++;
                        if (firstWhy == null) firstWhy = "island " + i + ", " + why;
                    }

                    for (int k = 0; k < sv.Sheets.Count; k++)
                    {
                        if (sv.Sheets[k].IsDetail != isDetailSurvey) { badFlags++; continue; }
                        if (!numbered) continue;   // a mis-numbered run counts nothing as contiguous
                        if (isDetailSurvey) detail++; else survey++;
                    }
                }
            }
            if (badSurveys > 0 || badFlags > 0)
                Fail("C4", badSurveys + " surveys mis-numbered and " + badFlags + " sheets mis-flagged across "
                           + islands + " islands" + (firstWhy == null ? "" : "  (first: " + firstWhy + ")"));
            else Pass("C4", survey + " survey sheets 1..N and " + detail + " detail sheets 1..M, both contiguous");
        }

        // ---------------------------------------------------------------- C6
        /// <summary>
        /// §4 C6 — reported, not gated. POIs per island by character, kind distribution, and how
        /// many POIs produced NO sheet because they failed C3. <b>That last number is the
        /// interesting one</b>: a high value means the siting rules and the placeability floor
        /// disagree.
        /// </summary>
        public static void C6_Density(int seeds)
        {
            Console.WriteLine("C6  POI density and distribution over " + seeds + " seeds");

            var count = new Dictionary<IslandCharacter, List<int>>();
            foreach (IslandCharacter c in Enum.GetValues(typeof(IslandCharacter))) count[c] = new List<int>();

            int[] kind = new int[PoiKinds.IndexRange];   // indexed by (int)kind; enum has a gap
            int total = 0, sheets = 0, empty = 0, ruins = 0;

            for (int i = 0; i < seeds; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(Collection, i));
                IReadOnlyList<Poi> pois = isl.Features.Pois;
                Survey sv = isl.SurveyFor(Office.Antiquarian);

                count[isl.Params.Character].Add(pois.Count);
                total += pois.Count;
                sheets += sv == null ? 0 : sv.SheetCount;
                if (pois.Count == 0) empty++;
                for (int k = 0; k < pois.Count; k++)
                {
                    kind[(int)pois[k].Kind]++;
                    if (pois[k].IsRuin) ruins++;
                }
            }

            foreach (IslandCharacter c in Enum.GetValues(typeof(IslandCharacter)))
            {
                List<int> v = count[c];
                if (v.Count == 0) continue;
                double sum = 0;
                int lo = int.MaxValue, hi = int.MinValue;
                for (int i = 0; i < v.Count; i++) { sum += v[i]; if (v[i] < lo) lo = v[i]; if (v[i] > hi) hi = v[i]; }
                Metric("C6", c + ": " + v.Count + " islands, POIs mean "
                    + (sum / v.Count).ToString("F2", Inv) + " (min " + lo + ", max " + hi + ")");
            }

            Metric("C6", total + " POIs, " + sheets + " detail sheets, "
                + (total - sheets) + " produced NO sheet (failed the placeability floor) = "
                + (total == 0 ? 0.0 : 100.0 * (total - sheets) / total).ToString("F1", Inv) + "%");
            Metric("C6", empty + " of " + seeds + " islands carry no POI at all (P1.4 permits this)");
            Metric("C6", "families: " + (total - ruins) + " natural oddities, " + ruins + " ruins");

            var sb = new StringBuilder();
            // Walk the member list, not the value range, or the removed kind's gap prints
            // as a real kind with a count of zero.
            foreach (PoiKind pk in PoiKinds.All)
            {
                int k = (int)pk;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(pk).Append(' ').Append(kind[k]);
            }
            Metric("C6", "kinds: " + sb);

            var spec = new SurveySpec(0, Office.Antiquarian, 0, MapScale.PoiDetail, 0.0,
                                      SheetFormat.DetailSheet, 0.0);
            Metric("C6", "detail sheet: " + SheetFormat.DetailSheet.WidthMm.ToString("F0", Inv) + " x "
                + SheetFormat.DetailSheet.HeightMm.ToString("F0", Inv) + " mm at " + MapScale.PoiDetail
                + " = " + spec.SheetGroundWidth.ToString("F0", Inv) + " m square of ground"
                + "  (C7 sweeps this — Tuning.PoiScaleDenominator)");
        }
    }
}
