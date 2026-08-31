using System.Collections.Generic;

namespace Archivist.Building.Collection
{
    /// <summary>Where a thing is standing, in world metres and degrees. Three numbers and three
    /// angles rather than a <c>Transform</c>, so the record travels into the file and into the
    /// headless suite without UnityEngine.</summary>
    public readonly struct PaperPose
    {
        public readonly double X, Y, Z;
        public readonly double RotX, RotY, RotZ;

        public PaperPose(double x, double y, double z, double rotX, double rotY, double rotZ)
        {
            X = x; Y = y; Z = z;
            RotX = rotX; RotY = rotY; RotZ = rotZ;
        }
    }

    /// <summary>Where paper can be. A sheet <i>on a board</i> is not one of them: it is not paper
    /// at all but a slab drawn from an identity, and the document itself is in the binder the
    /// board was opened from.
    ///
    /// <para><see cref="Shelf"/> is for binders only. A loose sheet is filed into a binder and
    /// stops being paper (D-B2), so nothing ever stands a sheet in a rack.</para></summary>
    public enum PaperWhere
    {
        Floor,
        Table,
        Hands,
        Shelf
    }

    /// <summary>
    /// One binder as a value: which binder it is, what is filed in it, and where it lies.
    ///
    /// <para><b>Contents are identities</b> (R1.11) — the same list a <c>BinderView</c>
    /// holds, in filing order, because a binder that reordered itself between two sessions would
    /// make the same pile of paper look like a different one.</para>
    /// </summary>
    public sealed class BinderRecord
    {
        /// <summary>Its <c>Binder_n</c> number. Saved because it is on the label and because
        /// two binders may not share one.</summary>
        public readonly int Number;

        public readonly ulong IslandSeed;

        /// <summary>A memo of a pure function, like the ledger's island name, and saved for the
        /// same reason: reading it off the label must not cost a generation.</summary>
        public readonly string IslandName;

        public readonly IReadOnlyList<SheetId> Contents;

        public readonly PaperWhere Where;

        /// <summary>Which table it is lying on, and on which anchor. Empty and -1 unless
        /// <see cref="Where"/> is <see cref="PaperWhere.Table"/>.</summary>
        public readonly string TableId;
        public readonly int Anchor;

        /// <summary>Which shelf it is filed in, and which slot of it. Empty and -1 unless
        /// <see cref="Where"/> is <see cref="PaperWhere.Shelf"/>.
        ///
        /// <para><b>Row and column, never a slot index.</b> An index is <c>row * slotsPerRow +
        /// column</c>, and those numbers are authored — changing a shelf from eight slots per row
        /// to nine would silently shift every filed binder one place along, which for a game
        /// about order being meaningful is the worst available failure. The pair survives every
        /// change to the shelf's spacing and its row count, and when a column really has gone it
        /// fails loudly instead: no such slot, warn, put the binder on the floor.</para></summary>
        public readonly string ShelfId;
        public readonly int Row;
        public readonly int Column;

        /// <summary>Its world pose, wherever it is standing — including on a table, where the
        /// anchor decides the place and a runtime jitter decides the angle (so the angle is not
        /// recomputable and must be kept). Meaningless in the hands.</summary>
        public readonly PaperPose Pose;

        public BinderRecord(int number, ulong islandSeed, string islandName,
                            IReadOnlyList<SheetId> contents, PaperWhere where,
                            string tableId, int anchor, PaperPose pose,
                            string shelfId = null, int row = -1, int column = -1)
        {
            Number = number;
            IslandSeed = islandSeed;
            IslandName = islandName;
            Contents = contents ?? new SheetId[0];
            Where = where;
            TableId = tableId;
            Anchor = anchor;
            Pose = pose;
            ShelfId = shelfId;
            Row = row;
            Column = column;
        }

        public override string ToString()
        {
            return "Binder_" + Number + ": " + Contents.Count + " sheets, " + Where.ToString().ToLowerInvariant();
        }
    }

    /// <summary>One sheet of paper lying about — a sheet that was never filed. On the floor or in
    /// the hands; a sheet on a table is filed into a binder and stops being paper (D-B2).</summary>
    public sealed class LooseSheetRecord
    {
        public readonly SheetId Id;
        public readonly PaperWhere Where;
        public readonly PaperPose Pose;

        public LooseSheetRecord(SheetId id, PaperWhere where, PaperPose pose)
        {
            Id = id;
            Where = where;
            Pose = pose;
        }

        public override string ToString() { return Id + " (" + Where.ToString().ToLowerInvariant() + ")"; }
    }

    /// <summary>
    /// Every piece of paper in the room, as a value — the half of the save the ledger has always
    /// needed (C9.5, <c>persistence.md</c> §2).
    ///
    /// <para><b>The invariant this exists to make true: every issued sheet is somewhere.</b> The
    /// ledger says a sheet has entered the world; without this record nothing said where it went,
    /// so a reloaded archive claimed paper that did not exist. A sheet is in exactly one binder,
    /// on the floor, or in the hands.</para>
    ///
    /// <para><b>No geometry beyond a pose per object</b> (R1.11). A binder is a number, an island
    /// seed and a list of identities; a loose sheet is one identity. What the paper <i>shows</i>
    /// is regenerated from the seed on the way back in, which is why a floor sheet costs an island
    /// generation to restore and a binder costs nothing.</para>
    /// </summary>
    public sealed class RoomSnapshot
    {
        public readonly IReadOnlyList<BinderRecord> Binders;
        public readonly IReadOnlyList<LooseSheetRecord> Sheets;

        /// <summary>The number the next binder will carry. Saved for the reason
        /// <c>BoardStore</c>'s group counter is: a reload that rewound it would put two
        /// <c>Binder_4</c>s in one room, and the label is how the player tells them apart.</summary>
        public readonly int NextBinderNumber;

        public RoomSnapshot(IReadOnlyList<BinderRecord> binders,
                            IReadOnlyList<LooseSheetRecord> sheets, int nextBinderNumber)
        {
            Binders = binders ?? new BinderRecord[0];
            Sheets = sheets ?? new LooseSheetRecord[0];
            NextBinderNumber = nextBinderNumber;
        }

        public bool Empty { get { return Binders.Count == 0 && Sheets.Count == 0; } }

        /// <summary>
        /// Checks the invariant this record exists for — <b>every issued sheet is somewhere, and
        /// somewhere once</b> — against the ledger that claims them.
        ///
        /// <para>It reports rather than repairs, and is called after a restore rather than before
        /// a write. All three of its counts mean a bug somewhere else: paper that was issued and
        /// then lost, paper that exists twice, or paper in the room the archive never handed out.
        /// None of them is recoverable here — a missing sheet has no pose to invent and a
        /// duplicate has no way to say which copy is the real one — so the honest thing is to say
        /// so loudly and leave the room as the file described it.</para>
        /// </summary>
        public RoomAudit Audit(SheetLedgerStore ledger)
        {
            var seen = new HashSet<SheetId>();
            int duplicated = 0, unissued = 0;

            for (int b = 0; b < Binders.Count; b++)
            {
                IReadOnlyList<SheetId> contents = Binders[b].Contents;
                for (int i = 0; i < contents.Count; i++)
                {
                    if (!seen.Add(contents[i])) duplicated++;
                    else if (ledger != null && !ledger.IsIssued(contents[i])) unissued++;
                }
            }

            for (int i = 0; i < Sheets.Count; i++)
            {
                SheetId id = Sheets[i].Id;
                if (!seen.Add(id)) duplicated++;
                else if (ledger != null && !ledger.IsIssued(id)) unissued++;
            }

            int missing = 0;
            if (ledger != null)
            {
                IReadOnlyList<ulong> islands = ledger.KnownIslands;
                for (int i = 0; i < islands.Count; i++)
                {
                    IReadOnlyList<SheetId> issued = ledger.IssuedSheets(islands[i]);
                    for (int s = 0; s < issued.Count; s++)
                        if (!seen.Contains(issued[s])) missing++;
                }
            }

            return new RoomAudit(missing, duplicated, unissued);
        }

        public override string ToString()
        {
            return Binders.Count + " binder(s), " + Sheets.Count + " loose sheet(s)";
        }
    }

    /// <summary>What <see cref="RoomSnapshot.Audit"/> found. Counts rather than a bool, because
    /// "one sheet went missing" and "the room and the ledger disagree about everything" want
    /// different reactions from whoever reads the log.</summary>
    public readonly struct RoomAudit
    {
        /// <summary>Issued, and in no binder, on no floor, in no hand.</summary>
        public readonly int Missing;

        /// <summary>In two places at once — the same document existing twice, which is what
        /// C4.5 forbids of a board and R2.10 forbids of the archive.</summary>
        public readonly int Duplicated;

        /// <summary>In the room, and the ledger never handed it out.</summary>
        public readonly int Unissued;

        public RoomAudit(int missing, int duplicated, int unissued)
        {
            Missing = missing;
            Duplicated = duplicated;
            Unissued = unissued;
        }

        public bool Clean { get { return Missing == 0 && Duplicated == 0 && Unissued == 0; } }

        public override string ToString()
        {
            return Missing + " issued sheet(s) nowhere in the room, " + Duplicated +
                   " in two places, " + Unissued + " never issued";
        }
    }
}
