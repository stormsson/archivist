using System.Collections.Generic;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// Which sheets have entered the world, per island. <b>A different memory structure from
    /// the island itself, and it has to be.</b>
    ///
    /// <para>An <see cref="Generation.Island"/> is a pure function of its seed (R1.1) and
    /// nothing geometric is ever persisted (R1.11, R3.1) — the island is thrown away and
    /// regenerated on demand, identically, forever. So it is exactly the wrong place to
    /// record a fact about the player's collection: the moment issuance lived on the island
    /// object, it would either be lost on the next regeneration or would have to be persisted,
    /// and persisting it would make the island something other than a function of its seed.</para>
    ///
    /// <para>The ledger holds only identities. It is small, flat, trivially serialisable, and
    /// it is what makes R2.10 — every sheet in the collection is unique, no duplicates, no
    /// reprints — an enforceable rule rather than an intention. R2.10b follows directly: a
    /// slot is binary because issuance is.</para>
    ///
    /// <para>Deliberately free of UnityEngine, so it can move to a headless assembly and be
    /// covered by <c>Tools/run-acceptance.sh</c> the day it needs tests.</para>
    /// </summary>
    public sealed class SheetLedger
    {
        readonly Dictionary<ulong, HashSet<SheetId>> issued = new Dictionary<ulong, HashSet<SheetId>>();

        public bool IsIssued(SheetId id)
        {
            HashSet<SheetId> set;
            return issued.TryGetValue(id.IslandSeed, out set) && set.Contains(id);
        }

        /// <summary>True if this call issued it; false if it was already out.</summary>
        public bool MarkIssued(SheetId id)
        {
            HashSet<SheetId> set;
            if (!issued.TryGetValue(id.IslandSeed, out set))
            {
                set = new HashSet<SheetId>();
                issued[id.IslandSeed] = set;
            }
            return set.Add(id);
        }

        public int IssuedCount(ulong islandSeed)
        {
            HashSet<SheetId> set;
            return issued.TryGetValue(islandSeed, out set) ? set.Count : 0;
        }

        /// <summary>
        /// A copy, because the picker runs on a worker thread while the main thread may still
        /// be writing. Cheap: a few hundred structs at worst.
        /// </summary>
        public HashSet<SheetId> Snapshot(ulong islandSeed)
        {
            HashSet<SheetId> set;
            return issued.TryGetValue(islandSeed, out set)
                ? new HashSet<SheetId>(set)
                : new HashSet<SheetId>();
        }

        public IEnumerable<ulong> KnownIslands { get { return issued.Keys; } }

        public int KnownIslandCount { get { return issued.Count; } }
    }
}
