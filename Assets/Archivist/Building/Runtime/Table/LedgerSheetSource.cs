using System.Collections.Generic;
using Archivist.Building.Collection;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The POC's <see cref="ISheetSource"/>: the cabinet is fed by the ledger (C1.3).
    ///
    /// <para><b>Thin on purpose, and temporary on purpose.</b> There is no logic here worth
    /// having, and that is the point — this class exists so that the one place in the codebase
    /// that says "the table's sheets are the issued sheets" is a class the composition root
    /// constructs, rather than a call site buried in a row or a drag handler. §13 lists folders
    /// as deliberately absent; when they arrive, a <c>FolderSheetSource</c> is written beside
    /// this one and the UI does not change. That swap is only one line if the rule in
    /// <see cref="ISheetSource"/> has been kept: <b>nothing in the UI layer references
    /// <see cref="SheetLedger"/> directly.</b></para>
    ///
    /// <para><b>Not a MonoBehaviour.</b> It owns no scene state and exists to be substitutable;
    /// a component would make substitution a prefab edit instead of a constructor argument, and
    /// would tempt a consumer to <c>GetComponent</c> its way to the concrete type and back to the
    /// ledger. Same split as <c>SheetLedgerStore</c>.</para>
    ///
    /// <para><b>It copies, and it has to.</b>
    /// <c>SheetLedgerStore.IssuedSheets</c> is explicit that it returns <i>the live list, not a
    /// copy — read it, do not keep it: it grows under any caller that holds it across an
    /// opening</i>. The cabinet is exactly such a caller: it is built once when the table opens
    /// and read again on every rebuild, and issuance does not stop happening because a table is
    /// open. Handing it the live list means either a stale-looking cabinet that silently gains
    /// rows, or — worse — an <c>InvalidOperationException</c> from a foreach the frame a crate
    /// issues a sheet. Second reason, and the stronger one: an interface is only as strong as
    /// its loosest implementation. A future <c>FolderSheetSource</c> filtering the folders on a
    /// table must build a fresh list anyway, so if this one alone returned a live collection,
    /// consumers would be written and tested against the aliasing rules of whichever
    /// implementation they happened to meet first. Copying makes both obey the same contract.
    /// The cost is a few dozen 20-byte structs per call, which is nothing next to the island
    /// regeneration the same screen is about to do.</para>
    ///
    /// <para><b>No cache, not even a one-entry one.</b> The obvious optimisation — remember the
    /// last seed and its list — would have to be invalidated by issuance, which means this
    /// class would have to learn when the ledger changes, which means it stops being thin.
    /// The list is short and the call is rare (an opening, a seat, a return to the drawer).</para>
    /// </summary>
    public sealed class LedgerSheetSource : ISheetSource
    {
        static readonly SheetId[] None = new SheetId[0];

        readonly SheetLedger ledger;

        /// <summary>The ledger this source reads. Null is tolerated rather than thrown on
        /// because a table in a scene without a ledger should show an empty cabinet, not stop
        /// the frame — an empty cabinet is a visible, diagnosable state (C7.1 draws no section
        /// with no sheets), while an exception in a view's build is not.</summary>
        public LedgerSheetSource(SheetLedger ledger) { this.ledger = ledger; }

        /// <summary>
        /// Every sheet of this island the archive has issued, oldest first — a fresh list each
        /// call, safe to hold, sort or filter.
        ///
        /// <para>Issued, <b>not</b> lying on the floor of the room, and not "on the table":
        /// which sheets a board has laid out is a board fact, kept by <c>BoardStore</c> (C4.5),
        /// and the cabinet crosses the two itself. Nor is it every sheet the survey <i>has</i> —
        /// the ledger only knows what has come out of the crates, which is why the cabinet's
        /// counts (C7.2) never reveal how large the survey really is.</para>
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
