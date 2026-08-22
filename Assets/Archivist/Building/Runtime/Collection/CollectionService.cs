using UnityEngine;
using Archivist.Generation.Determinism;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The scene's one owner of collection-wide state: the collection seed, how many islands
    /// have been drawn from it, and the <see cref="SheetLedger"/>.
    ///
    /// <para>Everything here is persistable in a few dozen bytes plus the ledger, and nothing
    /// here is geometry. That is the shape R1.11 and R3.1 ask for, made concrete.</para>
    /// </summary>
    public sealed class CollectionService : MonoBehaviour
    {
        [Tooltip("The whole collection derives from this. R1.1: island_seed = hash(collection_seed, island_index).")]
        [SerializeField] long collectionSeed = 905386350;

        [Tooltip("How many islands have been drawn. Persisted state; R1.2 puts no ceiling on it.")]
        [SerializeField] int nextIslandIndex;

        readonly SheetLedger ledger = new SheetLedger();

        public SheetLedger Ledger { get { return ledger; } }

        public ulong CollectionSeed { get { return unchecked((ulong)collectionSeed); } }

        /// <summary>The seed of the most recent island drawn, or 0 before the first.</summary>
        public ulong LastIslandSeed { get; private set; }

        /// <summary>
        /// Reserves the next island index and returns its seed — without generating anything.
        /// Generation is expensive and belongs on a worker thread; deciding *which* island is
        /// cheap and belongs here, so the ledger can be consulted before any work starts.
        /// </summary>
        public ulong ReserveNextIslandSeed()
        {
            ulong seed = SeedForIndex(nextIslandIndex);
            nextIslandIndex++;
            LastIslandSeed = seed;
            return seed;
        }

        /// <summary>
        /// R1.1 without the side effect. Editor tooling needs a seed without advancing the
        /// counter, because <c>nextIslandIndex</c> is serialised: a tool that reserved one
        /// would dirty the scene and, if saved, leave a collection that had drawn islands
        /// nobody has ever seen.
        /// </summary>
        public ulong SeedForIndex(int islandIndex)
        {
            return Streams.IslandSeed(CollectionSeed, islandIndex);
        }
    }
}
