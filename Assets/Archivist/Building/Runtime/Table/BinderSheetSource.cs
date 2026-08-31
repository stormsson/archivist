using System.Collections.Generic;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Interactables;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The <see cref="ISheetSource"/> a table in the room uses: what is in the binders lying on
    /// <b>this table</b>, and nothing else. §4.3's <c>FolderSheetSource</c>, named for the
    /// object the player actually carries.
    ///
    /// <para><b>A different question from <see cref="LedgerSheetSource"/>'s</b> ("every sheet of
    /// this island the archive has ever issued"). The two answers coincide only while one crate
    /// has filed into one binder: a sheet issued loose onto the floor, or a second island drawn,
    /// separates them, and the wrong one is wrong in a way only a count gives away.</para>
    ///
    /// <para><b>It copies, because the contract says so</b> (<see cref="ISheetSource"/>), and
    /// for a reason of its own: <see cref="BinderView.Contents"/> is live and a binder can be
    /// taken off the table while the board is open, so a caller holding the returned list would
    /// be holding the contents of a folder that is no longer here.</para>
    ///
    /// <para><b>The island filter is a guard, not logic.</b> C4.3 means a bound table only ever
    /// holds one island's binders, and <see cref="BinderView.Add"/> means a binder only ever
    /// holds one island's sheets — so the filter can never drop anything. It is kept because
    /// this class is what makes C4.3 true for the board, and a rule enforced by an invariant
    /// somewhere else is a rule that stops being enforced when that invariant moves.</para>
    ///
    /// <para><b>Not a MonoBehaviour</b>, for the reasons <c>LedgerSheetSource</c> gives. It
    /// holds a table rather than a list of binders because the table is the thing whose
    /// contents change: a source built once when the board opens must see the pile as it is
    /// now, not as it was at the moment somebody constructed it.</para>
    /// </summary>
    public sealed class BinderSheetSource : ISheetSource
    {
        readonly CartographyTable table;

        /// <summary>The table whose pile this reads. Null is tolerated rather than thrown on,
        /// the way <c>LedgerSheetSource</c> tolerates a missing ledger: an empty board is a
        /// visible, diagnosable state, and an exception inside a view's build is not.</summary>
        public BinderSheetSource(CartographyTable table) { this.table = table; }

        /// <summary>
        /// Every sheet the binders on this table hold for that island, in the order they are
        /// piled and then in the order they were filed — a fresh list each call.
        ///
        /// <para>Duplicates are dropped rather than trusted away. <see cref="BinderView.Add"/>
        /// refuses a sheet already in <i>that</i> binder, but nothing stops two binders of one
        /// island holding the same sheet — the ledger's <c>MarkIssued</c> is what makes that
        /// impossible today, and it is a rule about issuance rather than about piles. The same
        /// sheet listed twice is a bug the player would have to count to find, so it is made
        /// impossible here instead.</para>
        /// </summary>
        public IReadOnlyList<SheetId> SheetsFor(ulong islandSeed)
        {
            var sheets = new List<SheetId>();
            if (table == null || islandSeed == 0) return sheets;

            var seen = new HashSet<SheetId>();
            IReadOnlyList<BinderView> binders = table.Binders;

            for (int i = 0; i < binders.Count; i++)
            {
                BinderView binder = binders[i];
                if (binder == null) continue;

                IReadOnlyList<SheetId> contents = binder.Contents;
                for (int j = 0; j < contents.Count; j++)
                {
                    SheetId id = contents[j];
                    if (id.IslandSeed != islandSeed) continue;
                    if (!seen.Add(id)) continue;

                    sheets.Add(id);
                }
            }

            return sheets;
        }
    }
}
