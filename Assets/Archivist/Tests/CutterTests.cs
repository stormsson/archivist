using NUnit.Framework;
using Archivist.Generation;
using Archivist.Generation.Analysis;
using Archivist.Generation.Determinism;
using Archivist.Generation.Sheets;

namespace Archivist.Tests
{
    /// <summary>A4 (§13.4) and A5 (§13.5).</summary>
    public class CutterTests
    {
        /// <summary>
        /// §10.4: cull first, then number. A gap must mean "missing sheet", and R2.9 is cut from
        /// v1, so every numbered sheet must exist.
        ///
        /// <para>The rule is <see cref="SheetNumbering.Validate"/>, shared with the harness's A4
        /// and with POC-03's C4 — this loop used to be a line-for-line copy of A4's, over a
        /// different number of seeds. This call site keeps the SET form (exactly 1..N, no
        /// duplicates, order unconstrained), which is what it always asserted; C4 asks for the
        /// stronger positional form.</para>
        /// </summary>
        [Test]
        public void SheetNumbersAreContiguousFromOne([Range(0, 9)] int index)
        {
            Island isl = Island.FromSeed(Streams.IslandSeed(TestSeeds.Collection, index));
            for (int s = 0; s < isl.Surveys.Count; s++)
            {
                string why;
                Assert.IsTrue(SheetNumbering.Validate(isl.Surveys[s], false, out why), why);
            }
        }

        [Test]
        public void EveryIslandCarriesAWholeIslandSheet()
        {
            // R2.2a: the entry point for the island, and in v1 the reference map too.
            for (int i = 0; i < 10; i++)
            {
                Island isl = Island.FromSeed(Streams.IslandSeed(TestSeeds.Collection, i));
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
                Island isl = Island.FromSeed(Streams.IslandSeed(TestSeeds.Collection, i));
                Survey g = isl.SurveyFor(Office.Garrison);
                if (g != null) Assert.AreEqual(0.0, g.Spec.RotationDeg, 1e-9, "grid discipline is what Garrison is");
            }
        }

        [Test]
        public void ServingSetExcludesCoast()
        {
            // D1: the coastline is island-scale, so it can never be what makes a sheet worth cutting.
            foreach (Office o in Offices.All)
            {
                var serving = FeatureMatrix.Serving(o);
                CollectionAssert.DoesNotContain(serving, Archivist.Generation.Features.FeatureClass.Coast);
                Assert.Greater(serving.Count, 0, o + " serves nothing");
            }
        }
    }
}
