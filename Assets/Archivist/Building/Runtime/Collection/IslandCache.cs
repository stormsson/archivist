using System;
using System.Collections.Generic;
using UnityEngine;
using Archivist.Generation;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// Keeps generated islands around so the same seed is not rebuilt twice.
    ///
    /// <para>R1.11 asks for exactly this: generation cheap enough to run on demand, and the
    /// result cacheable, with only the seed persisted. Nothing here is saved — the cache is
    /// an optimisation and losing it costs time, never correctness.</para>
    ///
    /// <para><b>Why it earns its place.</b> A sheet on the floor stores only its
    /// <see cref="SheetId"/>, so answering "what ground does this cover, and what year" means
    /// regenerating its island. Without a cache that is ~340 ms per question. With one it is
    /// a dictionary lookup for every sheet of an island the player is currently handling.</para>
    ///
    /// <para><b>Thread-safe, and cheaply so.</b> Generation runs on a worker thread while the
    /// main thread may be resolving sheets, so every access is guarded — but the generating
    /// call is made OUTSIDE the lock, because holding it for a third of a second would stall
    /// whichever thread asked next. Two threads can therefore race and both generate the same
    /// island; the loser's work is discarded. That is safe rather than merely tolerable: an
    /// island is a pure function of its seed (R1.1), so both results are identical and a race
    /// costs time, never correctness.</para>
    /// </summary>
    public sealed class IslandCache : MonoBehaviour
    {
        [Tooltip("How many islands to keep. Least recently used is evicted first.")]
        [SerializeField] int capacity = 8;

        readonly Dictionary<ulong, Island> islands = new Dictionary<ulong, Island>();
        readonly List<ulong> order = new List<ulong>();   // least recent first
        readonly object gate = new object();

        int hits;
        int misses;

        public int Count { get { lock (gate) { return islands.Count; } } }
        public int Hits { get { return hits; } }
        public int Misses { get { return misses; } }

        public bool TryGet(ulong islandSeed, out Island island)
        {
            lock (gate)
            {
                if (islands.TryGetValue(islandSeed, out island))
                {
                    Touch(islandSeed);
                    hits++;
                    return true;
                }
            }
            island = null;
            misses++;
            return false;
        }

        /// <summary>
        /// The cached island for this seed, generating it with <paramref name="generate"/> if
        /// it is not held. Safe to call from any thread; <paramref name="generate"/> must be
        /// too, which <c>Island.FromSeed</c> is — <c>Archivist.Generation</c> may not even
        /// reference UnityEngine.
        /// </summary>
        public Island GetOrGenerate(ulong islandSeed, Func<ulong, Island> generate)
        {
            Island cached;
            if (TryGet(islandSeed, out cached)) return cached;

            Island made = generate(islandSeed);

            lock (gate)
            {
                Island raced;
                if (islands.TryGetValue(islandSeed, out raced)) return raced;

                islands[islandSeed] = made;
                order.Add(islandSeed);
                Evict();
                return made;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                islands.Clear();
                order.Clear();
            }
            hits = 0;
            misses = 0;
        }

        /// <summary>Caller must hold the lock.</summary>
        void Touch(ulong islandSeed)
        {
            order.Remove(islandSeed);
            order.Add(islandSeed);
        }

        /// <summary>Caller must hold the lock.</summary>
        void Evict()
        {
            while (order.Count > capacity && capacity > 0)
            {
                ulong oldest = order[0];
                order.RemoveAt(0);
                islands.Remove(oldest);
            }
        }
    }
}
