using System.Collections.Generic;
using Archivist.Building.Collection;

namespace Archivist.Building.Table
{
    /// <summary>
    /// The two ways from an identity to the paper on the board: one sheet's slab, and one
    /// assembly's run of them.
    ///
    /// <para><b>A linear walk of <see cref="BoardView.OnTable"/>, not an index.</b> The list is a
    /// table's worth of sheets, and a dictionary beside it would be a second copy of a fact the
    /// list already holds — one that goes stale the first time a slab is added without it.</para>
    ///
    /// <para><b>Static, and shared, because the answer must not differ by caller.</b> The
    /// interactor lifts a run, the fuse search walks one, the outline boxes one; three walks that
    /// skipped different members would put the glow, the box and the join on three different
    /// assemblies.</para>
    /// </summary>
    public static class BoardSlabs
    {
        /// <summary>The slab carrying <paramref name="id"/>, or null — a sheet in the drawer, or
        /// one whose raster has not landed yet (C5.7), has none.</summary>
        public static BoardSheetView ViewOf(BoardView board, SheetId id)
        {
            if (board == null) return null;

            IReadOnlyList<BoardSheetView> table = board.OnTable;
            if (table == null) return null;

            for (int i = 0; i < table.Count; i++)
            {
                BoardSheetView v = table[i];
                if (v != null && v.Id.Equals(id)) return v;
            }
            return null;
        }

        /// <summary>
        /// The slabs of one assembly, in join order — G5.6's run, composed from
        /// <c>GroupRecord.Members</c> rather than from the board's lay order, which
        /// <c>BoardStore</c> deliberately does not reshuffle when sheets fuse.
        ///
        /// <para>Appends rather than clears: the caller owns the list, and every caller here
        /// reuses one across frames.</para>
        ///
        /// <para>Members with no slab are skipped rather than treated as an error: the store's
        /// invariant makes this empty or complete in practice, and skipping keeps a run
        /// contiguous if it is ever neither.</para>
        /// </summary>
        public static void MembersOf(BoardView board, int groupId, List<BoardSheetView> into)
        {
            if (board == null) return;

            GroupRecord group;
            if (!board.TryGetGroup(groupId, out group) || group.Members == null) return;

            for (int i = 0; i < group.Members.Count; i++)
            {
                BoardSheetView view = ViewOf(board, group.Members[i]);
                if (view != null) into.Add(view);
            }
        }
    }
}
