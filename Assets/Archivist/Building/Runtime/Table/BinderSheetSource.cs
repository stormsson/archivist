using System.Collections.Generic;
using Archivist.Building.Binders;
using Archivist.Building.Collection;
using Archivist.Building.Interactables;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The <see cref="ISheetSource"/> that <c>ISheetSource</c> was written to be replaced by:
    /// the cabinet lists what is in the binders lying on <b>this table</b>, and nothing else.
    ///
    /// <para><b>This is the class §4.3 called <c>FolderSheetSource</c>.</b> It is named for the
    /// object the player actually carries — a binder — because that is what the room calls it
    /// and a name nobody uses is a name that has to be translated every time it is read. The
    /// seam it arrives through is exactly the one that was left open: nothing in the UI layer
    /// changes, because nothing in the UI layer ever knew where its sheets came from.</para>
    ///
    /// <para><b>What it fixes, precisely.</b> <see cref="LedgerSheetSource"/> answers "every
    /// sheet of this island the archive has ever issued". That is a different question from
    /// "what is on this table", and the two agreed only by coincidence: a crate files its
    /// sheets into one binder and the board opened on the island the crate had just drawn, so
    /// the same list came back. The coincidence was already imperfect — <c>MapCrate</c>'s
    /// <c>looseDebugSheet</c> issues one more sheet onto the floor, so the cabinet listed a
    /// sheet that was lying under the crate rather than in the folder in your hands — and it
    /// broke completely the moment a second island was drawn. Asking the binders is not an
    /// optimisation of that answer; it is a different and correct one.</para>
    ///
    /// <para><b>It copies, because the contract says so</b> — see <c>LedgerSheetSource</c>'s
    /// longer argument, which applies here for a second reason of its own: the underlying
    /// <see cref="BinderView.Contents"/> lists are live and a binder can be taken off the
    /// table while the board is open, so a caller holding the returned list would be holding
    /// the contents of a folder that is no longer here.</para>
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
        /// the way <c>LedgerSheetSource</c> tolerates a missing ledger: an empty cabinet is a
        /// visible, diagnosable state, and an exception inside a view's build is not.</summary>
        public BinderSheetSource(CartographyTable table) { this.table = table; }

        /// <summary>
        /// Every sheet the binders on this table hold for that island, in the order they are
        /// piled and then in the order they were filed — a fresh list each call.
        ///
        /// <para>Duplicates are dropped rather than trusted away. <see cref="BinderView.Add"/>
        /// refuses a sheet already in <i>that</i> binder, but nothing stops two binders of one
        /// island holding the same sheet — the ledger's <c>MarkIssued</c> is what makes that
        /// impossible today, and it is a rule about issuance rather than about piles. A
        /// cabinet with the same sheet in it twice is a bug the player would have to count to
        /// find, so it is made impossible here instead.</para>
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
