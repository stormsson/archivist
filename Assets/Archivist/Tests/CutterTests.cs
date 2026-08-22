using NUnit.Framework;
using Archivist.Generation;
using Archivist.Generation.Determinism;
using Archivist.Generation.Sheets;

namespace Archivist.Tests
{
    /// <summary>A4 (§13.4) and A5 (§13.5).</summary>
    public class CutterTests
    {
        [Test]
        public void SheetNumbersAreContiguousFromOne([Range(0, 9)] int index)
        {
            // §10.4: cull first, then number. A gap must mean "missing sheet", and R2.9 is
            // cut from v1, so every numbered sheet must exist.
            Island isl = Island.FromSeed(Streams.IslandSeed(8412UL, index));
            for (int s = 0; s < isl.Surveys.Count; s++)
            {
                Survey sv = isl.Surveys[s];
                bool[] seen = new bool[sv.SheetCount + 1];
                for (int k = 0; k < sv.Sheets.Count; k++)
                {
                    int n = sv.Sheets[k].Number;
                    Assert.GreaterOrEqual(n, 1);
                    Assert.LessOrEqual(n, sv.SheetCount);
                    Assert.IsFalse(seen[n], "duplicate sheet number " + n);
                    seen[n] = true;
                }
            }
        }

        [Test]
        public void EveryIslandCarriesAWholeIslandSheet()
        {
            // R2.2a: the entry point for the island, and in v1 the reference map too.
            for (int i = 0; i < 10; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(8412UL, i));
                Survey whole = isl.WholeIslandSurvey;
                Assert.IsNotNull(whole, "island " + i + " has no whole-island survey");
                Assert.AreEqual(1, whole.SheetCount);
                Assert.LessOrEqual(isl.LandBounds.Width,
                    whole.Spec.Scale.GroundMetres(whole.Spec.Format.MapWidthMm) + 1e-6,
                    "land bbox overruns the whole-island map area (D5 scale selection failed)");
            }
        }

        [Test]
        public void GarrisonAlwaysSurveysTrueNorth()
        {
            for (int i = 0; i < 10; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(8412UL, i));
                Survey g = isl.SurveyFor(Office.Garrison);
                if (g != null) Assert.AreEqual(0.0, g.Spec.RotationDeg, 1e-9, "grid discipline is what Garrison is");
            }
        }

        [Test]
        public void ServingSetExcludesCoast()
        {
            // D1: the coastline is island-scale, so it can never be what makes a sheet worth cutting.
            foreach (Office o in new[] { Office.Hydrographic, Office.LandSurvey, Office.Garrison })
            {
                var serving = FeatureMatrix.Serving(o);
                CollectionAssert.DoesNotContain(serving, Archivist.Generation.Features.FeatureClass.Coast);
                Assert.Greater(serving.Count, 0, o + " serves nothing");
            }
        }
    }
}
