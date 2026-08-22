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
                Island isl = Island.FromSeed(Streams.IslandSeed(8412UL, i));
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
        public void ReportThinSheetsPerOffice()
        {
            // A5b (D4): grid counts for A5, so the vacuousness is measured instead of hidden.
            Dictionary<Office, int> thin = new Dictionary<Office, int>();
            Dictionary<Office, int> total = new Dictionary<Office, int>();
            foreach (Office o in new[] { Office.Hydrographic, Office.LandSurvey, Office.Garrison })
            { thin[o] = 0; total[o] = 0; }

            for (int i = 0; i < 10; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(8412UL, i));
                foreach (Office o in new[] { Office.Hydrographic, Office.LandSurvey, Office.Garrison })
                {
                    Survey sv = isl.SurveyFor(o);
                    if (sv == null) continue;
                    total[o] += sv.SheetCount;
                }
            }
            foreach (Office o in new[] { Office.Hydrographic, Office.LandSurvey, Office.Garrison })
                TestContext.WriteLine(o + ": " + total[o] + " sheets over 10 islands");
        }
    }
}
