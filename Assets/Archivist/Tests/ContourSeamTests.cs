using System;
using System.Collections.Generic;
using NUnit.Framework;
using Archivist.Generation;
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
        [Test]
        public void AdjacentRectsAgreeOnTheSharedBorder()
        {
            Island isl = Island.FromSeed(Streams.IslandSeed(8412UL, 3));
            int lod = Contours.LodForScale(5000);
            double cell = Contours.CellSizeForLod(lod);
            double tol = 1e-6 * cell;

            V2 c = isl.LandBounds.Centre;
            Rect2 left  = new Rect2(c.X - 2000, c.Y - 2000, c.X, c.Y + 2000);
            Rect2 right = new Rect2(c.X, c.Y - 2000, c.X + 2000, c.Y + 2000);

            List<double> a = BorderYs(Contours.Extract(isl.Field, left,  cell, isl.Params.SeaLevel), c.X, tol);
            List<double> b = BorderYs(Contours.Extract(isl.Field, right, cell, isl.Params.SeaLevel), c.X, tol);

            if (a.Count == 0 && b.Count == 0) Assert.Ignore("no coastline crosses the border on this seed");

            a.Sort(); b.Sort();
            Assert.AreEqual(a.Count, b.Count, "different number of border crossings");
            for (int i = 0; i < a.Count; i++)
                Assert.LessOrEqual(Math.Abs(a[i] - b[i]), tol, "border vertex " + i + " disagrees");
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

        static List<double> BorderYs(IReadOnlyList<Polyline> lines, double x, double tol)
        {
            List<double> ys = new List<double>();
            for (int i = 0; i < lines.Count; i++)
                for (int v = 0; v < lines[i].Count; v++)
                    if (Math.Abs(lines[i][v].X - x) <= tol) ys.Add(lines[i][v].Y);
            return ys;
        }
    }
}
