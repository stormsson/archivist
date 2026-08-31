using System.Collections.Generic;
using Archivist.Building.Collection;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The <see cref="ISheetSource"/> over everything the ledger has ever issued for an island
    /// (C1.3). A table in the room asks its binders instead — see
    /// <see cref="BinderSheetSource"/>.
    ///
    /// <para><b>Not a MonoBehaviour.</b> It owns no scene state and exists to be substitutable;
    /// a component would make substitution a prefab edit instead of a constructor argument, and
    /// would tempt a consumer to <c>GetComponent</c> its way to the concrete type and back to the
    /// ledger. Same split as <c>SheetLedgerStore</c>.</para>
    ///
    /// <para><b>It copies, and it has to.</b> <c>SheetLedgerStore.IssuedSheets</c> returns the
    /// live list, which grows under any caller holding it across an opening — a list that
    /// silently gains entries, or an <c>InvalidOperationException</c> from a foreach the frame a
    /// crate issues a sheet. An interface is also only as strong as its loosest implementation:
    /// a source that alone returned a live collection would teach consumers aliasing rules the
    /// others do not keep. It does not cache either — a cache would have to be invalidated by
    /// issuance, and this class would stop being thin.</para>
    /// </summary>
    public sealed class LedgerSheetSource : ISheetSource
    {
        static readonly SheetId[] None = new SheetId[0];

        readonly SheetLedger ledger;

        /// <summary>The ledger this source reads. Null is tolerated rather than thrown on
        /// because a table in a scene without a ledger should come up empty, not stop the frame
        /// — an empty board is a visible, diagnosable state, an exception in a view's build is
        /// not.</summary>
        public LedgerSheetSource(SheetLedger ledger) { this.ledger = ledger; }

        /// <summary>
        /// Every sheet of this island the archive has issued, oldest first — a fresh list each
        /// call, safe to hold, sort or filter.
        ///
        /// <para>Issued — <b>not</b> what is on this table, and not every sheet the survey
        /// <i>has</i>: the ledger only knows what has come out of the crates, so a count taken
        /// from it never reveals how large the survey really is.</para>
        /// </summary>
        public IReadOnlyList<SheetId> SheetsFor(ulong islandSeed)
        {
            if (ledger == null) return None;

            IReadOnlyList<SheetId> live = ledger.IssuedSheets(islandSeed);
            if (live == null || live.Count == 0) return None;

            var copy = new List<SheetId>(live.Count);
            for (int i = 0; i < live.Count; i++) copy.Add(live[i]);
            return copy;
        }
    }
}
