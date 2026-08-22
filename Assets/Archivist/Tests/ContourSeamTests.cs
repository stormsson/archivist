using NUnit.Framework;
using Archivist.Generation;
using Archivist.Generation.Analysis;
using Archivist.Generation.Determinism;
using Archivist.Generation.Geometry;

namespace Archivist.Tests
{
    /// <summary>
    /// A3, §13.3. The §6.2 lattice rule is the entire basis of "the island is a function":
    /// adjacent sheets contour independently and must still agree along their shared border.
    /// </summary>
    public class ContourSeamTests
    {
        /// <summary>
        /// The fixture and the comparison both come from <see cref="ContourSeam"/>, shared with
        /// the headless harness's A3. They used to be two copies with an identical preamble and
        /// a DIFFERENT verdict: this test sorted both sides and required equal counts, the
        /// harness took nearest neighbours and never compared counts at all. An extra crossing on
        /// one side failed here and passed there.
        ///
        /// <para>This call site keeps the STRICTER form — equal counts, one-to-one pairing.</para>
        /// </summary>
        [Test]
        public void AdjacentRectsAgreeOnTheSharedBorder()
        {
            Island isl = Island.FromSeed(Streams.IslandSeed(TestSeeds.Collection, 3));

            ContourSeam.Border border = ContourSeam.AcrossLandCentre(isl, 5000, 2000.0);
            if (border.Inconclusive) Assert.Ignore("no coastline crosses the border on this seed");

            ContourSeam.Comparison r = ContourSeam.Compare(border.Left, border.Right, border.Tol,
                                                           ContourSeam.Matching.SortedPairwise,
                                                           true /* the counts must match too */);
            Assert.IsTrue(r.Agree, r.Why);
        }

        [Test]
        public void LatticeSnappingIsIndependentOfRectOrigin()
        {
            // Two rects with different origins, same LOD, must sample the same global lattice.
            Rect2 a = new Rect2(101.0, 203.0, 1101.0, 1203.0).SnapOut(64.0);
            Rect2 b = new Rect2(37.0, 55.0, 1037.0, 1055.0).SnapOut(64.0);
            Assert.AreEqual(0.0, a.MinX % 64.0, 1e-9);
            Assert.AreEqual(0.0, b.MinX % 64.0, 1e-9);
            Assert.AreEqual(0.0, a.MinY % 64.0, 1e-9);
            Assert.AreEqual(0.0, b.MinY % 64.0, 1e-9);
        }
    }
}
