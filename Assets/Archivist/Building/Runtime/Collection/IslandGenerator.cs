using UnityEngine;
using Archivist.Generation;
using Archivist.Generation.Determinism;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The scene's one source of islands, and the owner of everything the collection
    /// remembers about them.
    ///
    /// <para>Two children, each a component with a scope of its own:
    /// <see cref="IslandCache"/> is where a generated island is <i>stored</i>, and
    /// <see cref="SheetLedger"/> is what has been <i>issued</i>. They are deliberately not one
    /// object: the cache is disposable and can be cleared at any moment without consequence,
    /// while losing the ledger would let a sheet be drawn twice and break R2.10.</para>
    ///
    /// <para>Everything this object holds fits in a few dozen bytes plus the ledger, and none
    /// of it is geometry. That is the shape R1.11 and R3.1 ask for, made concrete.</para>
    /// </summary>
    public sealed class IslandGenerator : MonoBehaviour
    {
        [Tooltip("The whole collection derives from this. R1.1: island_seed = hash(collection_seed, island_index).")]
        [SerializeField] long collectionSeed = 905386350;

        [Tooltip("How many islands have been drawn. Persisted state; R1.2 puts no ceiling on it.")]
        [SerializeField] int nextIslandIndex;

        [Header("Storage")]
        [SerializeField] IslandCache cache;
        [SerializeField] SheetLedger ledger;

        public IslandCache Cache { get { return cache; } }
        public SheetLedger Ledger { get { return ledger; } }

        public ulong CollectionSeed { get { return unchecked((ulong)collectionSeed); } }

        /// <summary>The seed of the most recent island drawn, or 0 before the first.</summary>
        public ulong LastIslandSeed { get; private set; }

        void Awake()
        {
            // Resolved here, on the main thread, so that GetOrGenerate never has to null-check
            // a UnityEngine.Object: that comparison calls into native code and is not safe to
            // do from the worker thread generation runs on.
            if (cache == null) cache = GetComponentInChildren<IslandCache>();
            if (ledger == null) ledger = GetComponentInChildren<SheetLedger>();

            if (cache == null || ledger == null)
            {
                Debug.LogError("[IslandGenerator] Needs an IslandCache and a SheetLedger child.", this);
                enabled = false;
            }
        }

        /// <summary>
        /// Reserves the next island index and returns its seed — without generating anything.
        /// Deciding <i>which</i> island is cheap and belongs on the main thread, so the ledger
        /// can be consulted before any work starts.
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

        /// <summary>
        /// The island for this seed, from the cache if it is held and generated into the cache
        /// if not. <b>This is the only place islands are made.</b>
        ///
        /// <para>Safe to call from a worker thread, which is where a crate calls it: it
        /// touches no Unity API, only the cache's dictionary and
        /// <see cref="Island.FromSeed"/>, and <c>Archivist.Generation</c> may not even
        /// reference UnityEngine.</para>
        /// </summary>
        public Island GetOrGenerate(ulong islandSeed)
        {
            // Lambda rather than the method group: Island.FromSeed takes an optional forced
            // character, so it does not match Func<ulong, Island> on its own.
            return cache.GetOrGenerate(islandSeed, s => Island.FromSeed(s));
        }

        /// <summary>
        /// What a sheet in the world actually is: its island, and its geometry, year, scale
        /// and paper. A spawned sheet carries only a <see cref="SheetId"/>, so this is how it
        /// answers any question about itself — cheap after the first call for that island,
        /// because the island stays in the cache.
        /// </summary>
        public bool TryResolve(SheetId id, out Island island, out Sheet sheet)
        {
            island = GetOrGenerate(id.IslandSeed);
            return SheetLookup.TryFind(island, id, out sheet);
        }
    }
}
