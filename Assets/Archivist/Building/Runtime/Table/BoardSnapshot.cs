using System.Collections.Generic;
using Archivist.Building.Collection;

namespace Archivist.Building.Table
{
    /// <summary>
    /// One table's board, flat, as a value — what <see cref="BoardStore"/> hands to the save and
    /// takes back from it (§4.2, C9.5).
    ///
    /// <para><b>A list of placements and not a dictionary</b>, because lay order <i>is</i> draw
    /// order (§3.3, C4.7) and a dictionary's enumeration order is not a promise. A board that
    /// came back with different paper on top would be unreadable, and sheets at ground scale
    /// overlap by a fifth.</para>
    ///
    /// <para><b>No geometry beyond one pose per loose sheet and one frame per group</b> (R1.11,
    /// G4.4). A seated placement carries no pose at all and a member carries only its group's
    /// id; both are derived on the way back in — which is what lets A6 delete every pose field
    /// out of the file by hand and still get a seated board back.</para>
    ///
    /// <para>Deliberately free of UnityEngine, like the two stores it travels between.</para>
    /// </summary>
    public sealed class BoardSnapshot
    {
        /// <summary>One sheet and where it lies, in the order it was laid down.</summary>
        public readonly struct Entry
        {
            public readonly SheetId Id;
            public readonly Placement Placement;

            public Entry(SheetId id, Placement placement)
            {
                Id = id;
                Placement = placement;
            }
        }

        /// <summary>The table this board belongs to (§4.1). Never empty — <see cref="BoardStore"/>
        /// refuses an unusable key and so does the file.</summary>
        public readonly string TableId;

        /// <summary>The island the table is bound to, or 0 while unbound (C4.1).</summary>
        public readonly ulong IslandSeed;

        /// <summary>In lay order (§3.3). Members of a parked group are absent, exactly as they
        /// are absent from the live board (G6.4, C4.5).</summary>
        public readonly IReadOnlyList<Entry> Placed;

        /// <summary>In creation order, on-table and parked alike (G6.1).</summary>
        public readonly IReadOnlyList<GroupRecord> Groups;

        /// <summary>The counter of G4.2. Saved because ids are never reused: a reload that
        /// rewound it would hand a new assembly the name of a dead one (G15.1).</summary>
        public readonly int NextGroupId;

        public BoardSnapshot(string tableId, ulong islandSeed, IReadOnlyList<Entry> placed,
                             IReadOnlyList<GroupRecord> groups, int nextGroupId)
        {
            TableId = tableId;
            IslandSeed = islandSeed;
            Placed = placed ?? new Entry[0];
            Groups = groups ?? new GroupRecord[0];
            NextGroupId = nextGroupId;
        }

        public override string ToString()
        {
            return TableId + ": " + Placed.Count + " placed, " + Groups.Count + " groups";
        }
    }

    /// <summary>
    /// What a restore had to throw away, so the caller can say so out loud. A count rather than
    /// a bool because "the save was not quite readable" and "the save was rubbish" are different
    /// facts, and <see cref="BoardStore"/> may not call <c>Debug.Log</c> itself.
    /// </summary>
    public readonly struct BoardRestoreReport
    {
        /// <summary>Sheets in the file that could not be put back: another island's paper, or a
        /// member of a group that did not survive.</summary>
        public readonly int DroppedSheets;

        /// <summary>Assemblies below two members after the ledger check, dissolved on the way in
        /// (G5.5's one exception, alongside <see cref="BoardStore.Clear"/>).</summary>
        public readonly int DissolvedGroups;

        public BoardRestoreReport(int droppedSheets, int dissolvedGroups)
        {
            DroppedSheets = droppedSheets;
            DissolvedGroups = dissolvedGroups;
        }

        public bool Clean { get { return DroppedSheets == 0 && DissolvedGroups == 0; } }

        public override string ToString()
        {
            return DroppedSheets + " sheets dropped, " + DissolvedGroups + " groups dissolved";
        }
    }
}
