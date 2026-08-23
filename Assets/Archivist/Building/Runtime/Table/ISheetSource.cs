using System.Collections.Generic;
using Archivist.Building.Collection;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Where the cartography table's cabinet gets its sheets from (spec §4.3). One island seed
    /// in, the identities the archive holds for it out — nothing else.
    ///
    /// <para><b>This interface exists to be replaced.</b> That is its whole purpose, and it is
    /// the only reason it is worth a file. C1.3 settles that in this POC the accordion is fed
    /// by the ledger — <c>SheetLedger.IssuedSheets(seed)</c> grouped by
    /// <see cref="SheetId.Office"/> — because the folder model does not exist yet. §13 records
    /// that folders are *deliberately absent*, not forgotten: the player's physical item is
    /// meant to be the folder, never the sheet (§1), so the day folders arrive the cabinet must
    /// list *what is in the folders laid on this table*, not *everything ever issued of this
    /// island*. Those are different answers to the same question, and the UI must not be able
    /// to tell which one it is being given.</para>
    ///
    /// <para><b>The standing rule: the UI layer must NEVER reference
    /// <see cref="SheetLedger"/> directly</b> (§4.3, stated there in bold). Not "should
    /// prefer not to" — never. A single <c>ledger.IssuedSheets(seed)</c> inside a view,
    /// a row, a drag handler or an editor inspector is enough to make the swap to
    /// <c>FolderSheetSource</c> a hunt through the UI rather than one line at the composition
    /// root, and it will be a *silent* hunt: the call still compiles, still returns sheets, and
    /// is only wrong in that it shows sheets no folder on this table contains. Every consumer
    /// takes an <see cref="ISheetSource"/> and is handed one from outside.</para>
    ///
    /// <para><b>Identities only, deliberately.</b> The contract returns
    /// <see cref="SheetId"/>, never <c>Sheet</c>, because a sheet's geometry lives only as long
    /// as the island object that produced it (R1.1, R1.11) and a source that returned geometry
    /// would have to hold an island — which is exactly the caching this design refuses. The
    /// walk from identity back to ground is
    /// <see cref="SheetLookup.TryFind(Archivist.Generation.Island, SheetId, out Archivist.Generation.Sheets.Sheet)"/>,
    /// and it stays the caller's business.</para>
    ///
    /// <para><b>Order is part of the contract</b>, for the reason <c>SheetLedgerStore</c> gives
    /// for keeping it: a cabinet that reordered itself between two openings would be
    /// unreadable. Implementations return a stable order and the same order twice; the ledger's
    /// is issuance order, oldest first. The cabinet groups by office (C7.1) but does not sort
    /// within a section, so what is returned here is what the player reads.</para>
    ///
    /// <para><b>Not filtered by island membership by the caller.</b> A source is asked for one
    /// island and returns that island's sheets; C4.3 — a bound table accepts only its own
    /// island's sheets — is then enforced by the object rather than by a rule, because there is
    /// no other seed's sheet in the list to accept.</para>
    /// </summary>
    public interface ISheetSource
    {
        /// <summary>
        /// The sheets this island offers the table, in a stable order. Empty — never null — for
        /// an island the archive has never met or has issued nothing from.
        ///
        /// <para>Callers must treat the result as a read-only view valid for the current frame
        /// only. An implementation is free to return a live collection, so anything held across
        /// an opening must be copied by the holder; see <see cref="LedgerSheetSource"/> for
        /// what the ledger's own list does.</para>
        /// </summary>
        IReadOnlyList<SheetId> SheetsFor(ulong islandSeed);
    }
}
