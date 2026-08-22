using System.Collections.Generic;
using UnityEngine;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The ledger, as a thing in the scene. Sits under the generator because issuance is
    /// collection state, not world state: sheets come and go from the floor, and none of that
    /// changes what has been issued.
    ///
    /// <para>Thin on purpose. The logic is in <see cref="SheetLedgerStore"/>, which knows
    /// nothing about UnityEngine; this component is where persistence, inspector visibility
    /// and editor tooling will attach as the ledger grows a scope of its own.</para>
    ///
    /// <para><b>Not yet persisted.</b> The store is rebuilt empty on every load, which is only
    /// safe because spawned sheets are never written into a scene either (see
    /// <c>SheetSpawner</c>). The two must be saved and loaded as one unit, or a sheet exists
    /// with nothing recording it and R2.10 breaks silently.</para>
    /// </summary>
    public sealed class SheetLedger : MonoBehaviour
    {
        readonly SheetLedgerStore store = new SheetLedgerStore();

        /// <summary>Read-only in the inspector; the count is the only thing worth watching.</summary>
        public int KnownIslandCount { get { return store.KnownIslandCount; } }

        public bool IsIssued(SheetId id) { return store.IsIssued(id); }

        /// <summary>True if this call issued it; false if it was already out.</summary>
        public bool MarkIssued(SheetId id) { return store.MarkIssued(id); }

        public int IssuedCount(ulong islandSeed) { return store.IssuedCount(islandSeed); }

        /// <summary>A copy, safe to hand to a worker thread.</summary>
        public HashSet<SheetId> Snapshot(ulong islandSeed) { return store.Snapshot(islandSeed); }

        public IEnumerable<ulong> KnownIslands { get { return store.KnownIslands; } }
    }
}
