using NUnit.Framework;
using Archivist.Generation;
using Archivist.Generation.Analysis;
using Archivist.Generation.Determinism;

namespace Archivist.Tests
{
    /// <summary>
    /// The seeds this assembly tests against.
    ///
    /// <para>The literal <c>8412</c> was written out about a dozen times across the harness and
    /// these tests — sometimes named <c>Collection</c>, more often bare. That is how the seed
    /// COUNTS in the numbering and contour-seam checks drifted apart without anyone noticing:
    /// nothing tied the copies together, so nothing flagged it when they stopped agreeing.</para>
    ///
    /// <para>There are exactly two homes for it, one each side of the assembly boundary: this
    /// one, and <c>Archivist.Harness.Report.Collection</c> for the headless harness. The two
    /// must hold the same value or the two suites stop talking about the same islands.</para>
    /// </summary>
    internal static class TestSeeds
    {
        public const ulong Collection = 8412UL;
    }

    /// <summary>A2, §13.2. Same seed -> identical island, across runs and unrelated code changes.</summary>
    public class DeterminismTests
    {
        const ulong Collection = TestSeeds.Collection;

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

        /// <summary>
        /// A2's island digest. Delegates to <see cref="IslandDigest"/> so this test and the
        /// headless harness assert the SAME property.
        ///
        /// <para>They did not, until now. This method used to roll its own digest covering only
        /// character, name, coastline vertices, peak spot heights, settlement names, and each
        /// survey's rotation and sheet count. It was blind to peak and settlement POSITIONS, to
        /// scale, to year, and to where any sheet actually sits — so a change that moved every
        /// village on the island left this test green while the harness's A2 went red. "A2 passes"
        /// meant two different things depending on which A2 you ran.</para>
        ///
        /// <para><see cref="IslandDigest"/> is the harness's richer version, moved into the
        /// Generation assembly where both callers can see it. Do not re-roll a local digest here;
        /// widening the field set is a deliberate re-anchoring of the published hash, not a
        /// refactor.</para>
        /// </summary>
        internal static ulong HashOf(Island isl)
        {
            return IslandDigest.Hash(isl);
        }
    }
}
