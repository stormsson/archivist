using System.Collections.Generic;
using Archivist.Building.Collection;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Where the cartography table gets its sheets from (§4.3): one island seed in, the
    /// identities the archive holds for it out.
    ///
    /// <para><b>The UI layer must NEVER reference <see cref="SheetLedger"/> directly.</b> A
    /// single <c>ledger.IssuedSheets(seed)</c> in a view answers "everything ever issued of this
    /// island" where the table asks "what is in the folders on this table" (§1, §13); the wrong
    /// call compiles and still returns sheets, so it is a silent hunt to find and only a count
    /// gives it away. Every consumer takes an <see cref="ISheetSource"/> and is handed one by
    /// the composition root.</para>
    ///
    /// <para><b>Identities, never geometry</b> — a sheet's ground lives only as long as the
    /// island object that produced it (R1.1, R1.11), so a source returning <c>Sheet</c> would
    /// have to hold an island; the walk back is
    /// <see cref="SheetLookup.TryFind(Archivist.Generation.Island, SheetId, out Archivist.Generation.Sheets.Sheet)"/>
    /// and it stays the caller's business. <b>Order is part of the contract:</b> a list that
    /// reordered itself between two openings would be unreadable. A source is asked for one
    /// seed and answers for that seed only, which is what makes C4.3 — a bound table takes no
    /// other island's sheets — true by construction rather than by a rule.</para>
    /// </summary>
    public interface ISheetSource
    {
        /// <summary>
        /// The sheets this island offers the table, in a stable order. Empty — never null — for
        /// an island the archive has nothing for. Read-only and valid for the current frame:
        /// an implementation may return a live collection, so anything held across an opening
        /// must be copied by the holder.
        /// </summary>
        IReadOnlyList<SheetId> SheetsFor(ulong islandSeed);
    }
}
