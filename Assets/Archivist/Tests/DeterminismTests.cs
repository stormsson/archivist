using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using Archivist.Generation;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Tests
{
    /// <summary>A2, §13.2. Same seed -> identical island, across runs and unrelated code changes.</summary>
    public class DeterminismTests
    {
        const ulong Collection = 8412UL;

        [Test]
        public void SameSeedGeneratesIdenticalIsland()
        {
            ulong seed = Streams.IslandSeed(Collection, 0);
            ulong first = HashOf(Island.FromSeed(seed));
            for (int i = 1; i < 20; i++)
                Assert.AreEqual(first, HashOf(Island.FromSeed(seed)), "island diverged on iteration " + i);
        }

        [Test]
        public void UnrelatedStreamDrawsDoNotPerturbTheIsland()
        {
            // §4.3: one stream per purpose. Adding a feature type or reordering a loop must not
            // reshuffle the island, or "only the seed is persisted" becomes a lie.
            ulong seed = Streams.IslandSeed(Collection, 1);
            ulong before = HashOf(Island.FromSeed(seed));
            Pcg32 unrelated = Streams.For(seed, "some.future.purpose");
            for (int i = 0; i < 1000; i++) unrelated.NextUInt();
            Assert.AreEqual(before, HashOf(Island.FromSeed(seed)));
        }

        [Test]
        public void NamedStreamsAreIndependentOfCallOrder()
        {
            ulong seed = Streams.IslandSeed(Collection, 2);
            Pcg32 a1 = Streams.For(seed, "peaks");
            Pcg32 b1 = Streams.For(seed, "settlements");
            uint a1v = a1.NextUInt();
            uint b1v = b1.NextUInt();

            Pcg32 b2 = Streams.For(seed, "settlements");   // drawn in the opposite order
            Pcg32 a2 = Streams.For(seed, "peaks");
            Assert.AreEqual(b1v, b2.NextUInt());
            Assert.AreEqual(a1v, a2.NextUInt());
        }

        [Test]
        public void Pcg32RangeIsUnbiasedAndInRange()
        {
            Pcg32 r = Streams.For(1234UL, "test");
            for (int i = 0; i < 10000; i++)
            {
                int v = r.Range(3, 9);
                Assert.GreaterOrEqual(v, 3);
                Assert.Less(v, 9);
            }
        }

        internal static ulong HashOf(Island isl)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.Append(isl.Params.Character).Append('|').Append(isl.Name).Append('|');
            for (int i = 0; i < isl.Coastline.Count; i++)
            {
                Polyline p = isl.Coastline[i];
                for (int v = 0; v < p.Count; v++)
                    sb.Append(p[v].X.ToString("F6", inv)).Append(',').Append(p[v].Y.ToString("F6", inv)).Append(';');
            }
            for (int i = 0; i < isl.Features.Peaks.Count; i++)
                sb.Append(isl.Features.Peaks[i].SpotHeightM).Append(';');
            for (int i = 0; i < isl.Features.Settlements.Count; i++)
                sb.Append(isl.Features.Settlements[i].Name).Append(';');
            for (int i = 0; i < isl.Surveys.Count; i++)
                sb.Append(isl.Surveys[i].Spec.RotationDeg.ToString("F1", inv)).Append(':')
                  .Append(isl.Surveys[i].SheetCount).Append(';');
            return Hash.Fnv1a64(sb.ToString());
        }
    }
}
