using System.Collections.Generic;
using UnityEngine;
using Archivist.Generation;

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
    /// <para><b>What it is for, besides enforcing R2.10.</b> It is the archive's own account
    /// of itself: which islands have been met, what has come out of them, and how much of each
    /// is still in the crates. A screen that lists the collection — the cartography table's
    /// island column, one accordion per island — reads it and nothing else, so those
    /// questions are answered here rather than by walking the room and guessing from paper on
    /// the floor.</para>
    ///
    /// <para><b>Not yet persisted.</b> The store is rebuilt empty on every load, which is only
    /// safe because spawned sheets are never written into a scene either (see
    /// <c>SheetSpawner</c>). The two must be saved and loaded as one unit, or a sheet exists
    /// with nothing recording it and R2.10 breaks silently. The memos —
    /// <see cref="Describe(Island)"/> — need no saving either way: they are pure functions of
    /// a seed and can always be recomputed.</para>
    /// </summary>
    public sealed class SheetLedger : MonoBehaviour
    {
        readonly SheetLedgerStore store = new SheetLedgerStore();

        // ---- recording -------------------------------------------------------------------

        /// <summary>Notes an island the archive has met, before anything is issued from it.
        /// <paramref name="islandIndex"/> may be -1 when the caller does not know it.</summary>
        public void Record(ulong islandSeed, int islandIndex) { store.Record(islandSeed, islandIndex); }

        /// <summary>True if this call issued it; false if it was already out.</summary>
        public bool MarkIssued(SheetId id) { return store.MarkIssued(id); }

        /// <summary>
        /// Hands the ledger the two things it cannot afford to work out for itself — the
        /// island's name and its sheet count — from an island that is already in memory.
        ///
        /// <para>Call it wherever an island has just been generated. Both values are pure
        /// functions of the seed, so this is a memo and never a fact; the point is that
        /// <see cref="IslandHolding.IssuedPercent"/> needs a denominator and finding one from
        /// scratch costs a third of a second per island.</para>
        /// </summary>
        public void Describe(Island island)
        {
            if (island == null) return;
            store.Describe(island.Seed, island.Name, island.TotalSheets);
        }

        // ---- reading ---------------------------------------------------------------------

        public bool IsIssued(SheetId id) { return store.IsIssued(id); }

        public int IssuedCount(ulong islandSeed) { return store.IssuedCount(islandSeed); }

        /// <summary>Sheets of this island that have been issued, oldest first. Issued, not
        /// necessarily on the floor — see <see cref="SheetLedgerStore.IssuedSheets"/>.</summary>
        public IReadOnlyList<SheetId> IssuedSheets(ulong islandSeed) { return store.IssuedSheets(islandSeed); }

        /// <summary>A copy, safe to hand to a worker thread.</summary>
        public HashSet<SheetId> Snapshot(ulong islandSeed) { return store.Snapshot(islandSeed); }

        /// <summary>Every island the archive has met, in the order it met them.</summary>
        public IReadOnlyList<ulong> KnownIslands { get { return store.KnownIslands; } }

        /// <summary>Read-only in the inspector; the count is the only thing worth watching.</summary>
        public int KnownIslandCount { get { return store.KnownIslandCount; } }

        /// <summary>Sheets issued across the whole collection.</summary>
        public int TotalIssuedCount { get { return store.TotalIssuedCount; } }

        /// <summary>What the archive holds of one island — its name, what is out, what is
        /// left. False if it has never met that island.</summary>
        public bool TryGetHolding(ulong islandSeed, out IslandHolding holding)
        {
            return store.TryGetHolding(islandSeed, out holding);
        }

        /// <summary>Every island as one list, in draw order. The collection screen's rows.</summary>
        public List<IslandHolding> Holdings() { return store.Holdings(); }
    }
}
