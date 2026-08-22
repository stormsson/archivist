using System;

namespace Archivist.Generation.Determinism
{
    /// <summary>
    /// One stream per purpose, drawn independently (§4.3). Adding a purpose or reordering
    /// a loop must never reshuffle an existing feature — asserted by A2 (§13.2).
    /// </summary>
    public static class Streams
    {
        public static Pcg32 For(ulong islandSeed, string purpose, int index = 0)
        {
            ulong streamId = Hash.Mix(Hash.Fnv1a64(purpose), unchecked((ulong)index));
            return new Pcg32(islandSeed, streamId);
        }

        /// <summary>island_seed = Mix(collection_seed, Fnv1a64(island_index)) — R1.1.</summary>
        public static ulong IslandSeed(ulong collectionSeed, int islandIndex)
        {
            return Hash.Mix(collectionSeed, Hash.Fnv1a64(Hash.FnvOffset, unchecked((ulong)islandIndex)));
        }
    }
}
