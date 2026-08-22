using System.Collections.Generic;
using NUnit.Framework;
using Archivist.Generation;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Sheets;

namespace Archivist.Tests
{
    /// <summary>
    /// A6 and A7 (§13.6, §13.7) are METRICS, not gates — they report so a real finding is
    /// visible rather than asserted away. These tests print and only fail on absurdity.
    /// </summary>
    public class MetricTests
    {
        [Test]
        public void ReportSheetEconomy()
        {
            Dictionary<IslandCharacter, List<int>> byChar = new Dictionary<IslandCharacter, List<int>>();
            int fallback = 0;
            for (int i = 0; i < 30; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(TestSeeds.Collection, i));
                if (!byChar.ContainsKey(isl.Params.Character)) byChar[isl.Params.Character] = new List<int>();
                byChar[isl.Params.Character].Add(isl.TotalSheets);
                Survey whole = isl.WholeIslandSurvey;
                if (whole != null && whole.Spec.Scale.Denominator > 25000) fallback++;
            }
            foreach (IslandCharacter c in new[] { IslandCharacter.Mountainous, IslandCharacter.Fjorded, IslandCharacter.Atoll })
            {
                if (!byChar.ContainsKey(c)) continue;
                List<int> v = byChar[c];
                v.Sort();
                TestContext.WriteLine(c + ": n=" + v.Count + " min " + v[0] + " median " + v[v.Count / 2] + " max " + v[v.Count - 1]);
                Assert.Greater(v[v.Count - 1], 0, c + " produced no sheets at all on any seed");
            }
            TestContext.WriteLine("whole-island 1:50000 fallback on " + fallback + "/30 seeds");
        }

        [Test]
        public void ReportSheetCountsPerOffice()
        {
            // Reports how many sheets each office cuts over 10 islands. A count only —
            // it says nothing about whether those sheets carry anything.
            //
            // A5b (D4), the thin-sheet measure that keeps A5 honest by counting sheets
            // whose content is coast/grid alone, lives in the headless harness:
            // Tools/GenHarness/Acceptance.cs, `A5_NoBlankSheets` and the `Content`
            // helper it calls. Do NOT copy that logic here — a later phase lifts it into
            // the Generation assembly as a shared SheetContent query, and this test picks
            // its implementation up from there rather than growing a third copy.
            Dictionary<Office, int> total = new Dictionary<Office, int>();
            foreach (Office o in Offices.All)
                total[o] = 0;

            for (int i = 0; i < 10; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(TestSeeds.Collection, i));
                foreach (Office o in Offices.All)
                {
                    Survey sv = isl.SurveyFor(o);
                    if (sv == null) continue;
                    total[o] += sv.SheetCount;
                }
            }
            foreach (Office o in Offices.All)
                TestContext.WriteLine(o + ": " + total[o] + " sheets over 10 islands");
        }
    }
}
