using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Archivist.Building.Binders;
using Archivist.Building.Handling;
using Archivist.Building.Interactables;
using Archivist.Building.Shelving;
using Archivist.Building.Sheets;
using Archivist.Generation;
using Archivist.Generation.Sheets;
using Archivist.Render;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The paper in the room, read off the scene and put back into it (§9,
    /// <c>persistence.md</c> §2). <see cref="Archive"/> owns the file; this owns the objects.
    ///
    /// <para><b>Why it exists at all.</b> The ledger records that a sheet has entered the world.
    /// Without this, nothing recorded where it went, so a reloaded archive claimed paper that did
    /// not exist and a restored board could not be opened because the binders that bind the table
    /// were gone. The invariant it restores: <b>every issued sheet is somewhere</b> — in exactly
    /// one binder, on the floor, or in the hands.</para>
    ///
    /// <para><b>A binder costs nothing to restore and a loose sheet costs an island.</b> A binder
    /// holds identities (R1.11), so it comes back in one frame. A sheet on the floor has to be
    /// looked at, which means the island regenerated and the sheet rasterised — ~340 ms of
    /// engine-free work per island plus an upload per sheet. So the sheets come back on a
    /// coroutine, off the main thread and one upload per frame, exactly as <c>MapCrate</c> and
    /// <c>BoardView</c> do it, and the room is walkable while they arrive.</para>
    ///
    /// <para><b>It restores after the sweeps, not before.</b> <c>SheetSpawner</c> and
    /// <c>BinderSpawner</c> clear the scene of paper in <c>Awake</c>, because paper is never
    /// written into a scene; <see cref="Archive"/> loads after the scene does, which is after
    /// every <c>Awake</c>. Restoring earlier would put the save through the sweep.</para>
    ///
    /// <para><b>Nothing here decides where paper goes.</b> Every pose in the file is one the
    /// player made — a drop, a table anchor, a crate's delivery — so this writes what it is told
    /// and never asks a spawner to choose. <c>SheetSpawner.Restore</c> and
    /// <c>CartographyTable.Restore</c> exist for that reason: the ordinary paths scatter, stack
    /// and re-roll, which is right for a gesture and wrong for a memory.</para>
    /// </summary>
    public sealed class RoomPaper : MonoBehaviour
    {
        /// <summary>What a sheet is drawn at when it is restored, if the room has no crate to
        /// ask. Matches <c>MapCrate</c>'s own default; the crate's value wins when there is one,
        /// so restored paper and delivered paper are the same paper.</summary>
        public const double DefaultPixelsPerPaperMm = 1.2;

        BinderSpawner binders;
        SheetSpawner sheets;
        PlayerHands hands;
        IslandGenerator generator;

        Coroutine restoring;
        bool rebuilding;

        /// <summary>
        /// True while the floor's paper is still being drawn. <see cref="Archive"/> does not
        /// write during it: a save taken halfway through a restore would record the sheets that
        /// had arrived and forget the rest, turning a slow load into a lost one.
        /// </summary>
        public bool Restoring { get { return rebuilding || restoring != null; } }

        // ---- capture -----------------------------------------------------------------------

        /// <summary>Which shelf and which slot, while a capture is being assembled. A struct
        /// rather than a nested dictionary because three fields travel together and a
        /// <c>KeyValuePair</c> of a pair is a thing nobody can read twice.</summary>
        readonly struct Filed
        {
            public readonly string ShelfId;
            public readonly int Row;
            public readonly int Column;

            public Filed(string shelfId, int row, int column)
            {
                ShelfId = shelfId; Row = row; Column = column;
            }

            /// <summary>Not on a shelf. Named rather than <c>default</c> because the -1s are the
            /// contract <see cref="BinderRecord.Row"/> states, and <c>default</c> would quietly
            /// write zeroes — a slot that exists.</summary>
            public static Filed None { get { return new Filed(null, -1, -1); } }
        }

        /// <summary>
        /// The room as it stands. Asked of the scene rather than of a list this component keeps:
        /// paper is created by a crate, moved by two hands and destroyed by filing, and a private
        /// tally would be a second account of the room that is wrong the first time something
        /// takes a path this class has not heard of.
        /// </summary>
        public RoomSnapshot Capture()
        {
            var carried = Hands != null ? Hands.Held : null;

            var onTables = new Dictionary<BinderView, KeyValuePair<string, int>>();
            CartographyTable[] tables = FindObjectsByType<CartographyTable>(FindObjectsSortMode.None);
            for (int t = 0; t < tables.Length; t++)
            {
                IReadOnlyList<BinderView> pile = tables[t].Binders;
                for (int i = 0; i < pile.Count; i++)
                    if (pile[i] != null)
                        onTables[pile[i]] = new KeyValuePair<string, int>(tables[t].TableId, i);
            }

            // The shelves the same way, and for the same reason: asked of the furniture rather
            // than of a list kept here. A slot's occupant is its child, so this walks the room's
            // slots rather than a tally that could disagree with them.
            var onShelves = new Dictionary<BinderView, Filed>();
            Shelf[] shelves = FindObjectsByType<Shelf>(FindObjectsSortMode.None);
            for (int h = 0; h < shelves.Length; h++)
            {
                IReadOnlyList<ShelfSlot> slots = shelves[h].Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    ShelfSlot slot = slots[i];
                    if (slot == null) continue;

                    BinderView filed = slot.Occupant;
                    if (filed != null)
                        onShelves[filed] = new Filed(shelves[h].ShelfId, slot.Row, slot.Column);
                }
            }

            var records = new List<BinderRecord>();
            BinderView[] all = BinderSpawner.AllInScene();
            for (int i = 0; i < all.Length; i++)
            {
                BinderView binder = all[i];
                if (binder == null) continue;

                PaperWhere where = PaperWhere.Floor;
                string tableId = null;
                int anchor = -1;
                Filed slot = Filed.None;

                KeyValuePair<string, int> seat;
                Filed filed;
                if (ReferenceEquals(binder, carried)) where = PaperWhere.Hands;
                else if (onTables.TryGetValue(binder, out seat))
                {
                    where = PaperWhere.Table;
                    tableId = seat.Key;
                    anchor = seat.Value;
                }
                else if (onShelves.TryGetValue(binder, out filed))
                {
                    where = PaperWhere.Shelf;
                    slot = filed;
                }

                records.Add(new BinderRecord(binder.Number, binder.IslandSeed, binder.IslandName,
                                             new List<SheetId>(binder.Contents), where, tableId,
                                             anchor, PoseOf(binder.transform),
                                             slot.ShelfId, slot.Row, slot.Column));
            }

            var loose = new List<LooseSheetRecord>();
            SheetView[] paper = SheetSpawner.AllInScene();
            for (int i = 0; i < paper.Length; i++)
            {
                SheetView sheet = paper[i];
                if (sheet == null) continue;

                loose.Add(new LooseSheetRecord(
                    sheet.Id,
                    ReferenceEquals(sheet, carried) ? PaperWhere.Hands : PaperWhere.Floor,
                    PoseOf(sheet.transform)));
            }

            int next = Binders != null ? Binders.NextNumber : 1;
            return new RoomSnapshot(records, loose, next);
        }

        // ---- restore -----------------------------------------------------------------------

        /// <summary>
        /// The room back into the scene. Binders first and immediately — they are what bind a
        /// table (C4.2) and therefore what makes a restored board reachable at all — then the
        /// floor's sheets on a coroutine, because each one costs a render.
        ///
        /// <para><paramref name="ledger"/> is C9.1 reaching into the room: a binder that holds a
        /// sheet the ledger never issued has the sheet taken out of it, with a warning, exactly as
        /// a board loses a placement it cannot justify. Passing null skips the check rather than
        /// refusing every sheet — a scene with no ledger cannot tell issued from not.</para>
        /// </summary>
        public void Restore(RoomSnapshot room, SheetLedgerStore ledger)
        {
            if (room == null || room.Empty) return;

            // Every save point in the room fires on a gesture, and putting paper back is not one:
            // a binder seated on a table during a load would otherwise write a file describing
            // the room halfway through being rebuilt — with the floor's sheets, which arrive
            // frames later, missing from it.
            rebuilding = true;

            if (Binders == null)
            {
                Debug.LogWarning("[RoomPaper] No BinderSpawner in the scene — " +
                                 room.Binders.Count + " binder(s) not restored.", this);
            }
            else
            {
                for (int i = 0; i < room.Binders.Count; i++) Rebuild(room.Binders[i], ledger);
                Binders.AdoptNextNumber(room.NextBinderNumber);
            }

            if (room.Sheets.Count == 0) { rebuilding = false; return; }

            if (Sheets == null || Generator == null)
            {
                Debug.LogWarning("[RoomPaper] No SheetSpawner or IslandGenerator — " +
                                 room.Sheets.Count + " loose sheet(s) not restored.", this);
                rebuilding = false;
                return;
            }

            // The flag passes to the coroutine rather than being dropped here: the floor is not
            // back until its last sheet has been drawn.
            restoring = StartCoroutine(RestoreSheets(room.Sheets, ledger));
            rebuilding = false;
        }

        /// <summary>One binder: made with the number it had, filled with what it held, put back
        /// where it was lying.</summary>
        void Rebuild(BinderRecord record, SheetLedgerStore ledger)
        {
            if (record == null) return;

            BinderView binder = Binders.Recreate(record.Number, record.IslandSeed,
                                                 record.IslandName);
            if (binder == null) return;

            int refused = 0;
            for (int i = 0; i < record.Contents.Count; i++)
            {
                SheetId id = record.Contents[i];
                if (ledger != null && !ledger.IsIssued(id)) { refused++; continue; }
                binder.Add(id);
            }

            if (refused > 0)
                Debug.LogWarning("[RoomPaper] Binder_" + record.Number + " held " + refused +
                                 " sheet(s) the ledger never issued — dropped (C9.1).", this);

            switch (record.Where)
            {
                case PaperWhere.Hands:
                    // One pair of hands. A file naming two carried things is broken, and the
                    // second is better on the floor than gone.
                    if (Hands != null && Hands.Adopt(binder)) return;

                    Debug.LogWarning("[RoomPaper] Binder_" + record.Number + " was being carried " +
                                     "and the hands are not free — put on the floor.", this);
                    break;

                case PaperWhere.Table:
                    CartographyTable table = TableCalled(record.TableId);

                    // Two different faults with one outcome, and telling them apart is the whole
                    // value of the message: a table that is not there means the ids have moved
                    // under the save — see CartographyTable.TableId — and a table that refused
                    // means the anchor is gone or the island does not match.
                    if (table == null)
                    {
                        Debug.LogWarning("[RoomPaper] Binder_" + record.Number + " says it is on " +
                                         "table " + record.TableId + " and no table in the scene " +
                                         "has that id — put on the floor where it was standing.", this);
                        break;
                    }

                    if (table.Restore(binder, record.Anchor,
                                      Position(record.Pose), Rotation(record.Pose))) return;

                    Debug.LogWarning("[RoomPaper] Binder_" + record.Number + " was on anchor " +
                                     record.Anchor + " of table " + record.TableId + ", which " +
                                     "refused it — no such anchor, or another island's paper is " +
                                     "already on it. Put on the floor where it was standing.", table);
                    break;

                case PaperWhere.Shelf:
                    Shelf shelf = ShelfCalled(record.ShelfId);

                    // The two faults are worth telling apart for the same reason the table's are:
                    // a missing shelf means the ids have moved under the save, and a refused slot
                    // means the shelf's numbers changed and (row, column) no longer names a place.
                    // The second is the expected one — it is what the six fields cost — and the
                    // binder is left standing where it was rather than guessing at a neighbour.
                    if (shelf == null)
                    {
                        Debug.LogWarning("[RoomPaper] Binder_" + record.Number + " says it is on " +
                                         "shelf " + record.ShelfId + " and no shelf in the scene " +
                                         "has that id — put on the floor where it was standing.", this);
                        break;
                    }

                    if (shelf.Restore(binder, record.Row, record.Column)) return;

                    Debug.LogWarning("[RoomPaper] Binder_" + record.Number + " was filed in slot r" +
                                     (record.Row + 1) + "c" + (record.Column + 1) + " of shelf " +
                                     record.ShelfId + ", which refused it — the slot is gone or " +
                                     "something else is in it. Put on the floor where it was " +
                                     "standing.", shelf);
                    break;
            }

            binder.transform.SetPositionAndRotation(Position(record.Pose), Rotation(record.Pose));
            Binders.Register(binder);
        }

        /// <summary>
        /// The floor's paper, one island at a time. The island is generated and its sheets
        /// rasterised on a worker — the same engine-free path the crate uses (C5.7) — and the
        /// views are made one per frame, because each is a <c>Texture2D.Apply</c> and several in
        /// one frame is the hitch <c>MapCrate</c> already met.
        /// </summary>
        IEnumerator RestoreSheets(IReadOnlyList<LooseSheetRecord> loose, SheetLedgerStore ledger)
        {
            var byIsland = new Dictionary<ulong, List<LooseSheetRecord>>();
            var order = new List<ulong>();

            for (int i = 0; i < loose.Count; i++)
            {
                LooseSheetRecord record = loose[i];
                if (record == null) continue;
                if (ledger != null && !ledger.IsIssued(record.Id))
                {
                    Debug.LogWarning("[RoomPaper] " + record.Id + " was on the floor and the " +
                                     "ledger never issued it — dropped (C9.1).", this);
                    continue;
                }

                List<LooseSheetRecord> list;
                if (!byIsland.TryGetValue(record.Id.IslandSeed, out list))
                {
                    list = new List<LooseSheetRecord>();
                    byIsland[record.Id.IslandSeed] = list;
                    order.Add(record.Id.IslandSeed);
                }
                list.Add(record);
            }

            double ppmm = PixelsPerPaperMm;

            for (int i = 0; i < order.Count; i++)
            {
                ulong seed = order[i];
                List<LooseSheetRecord> wanted = byIsland[seed];

                IslandGenerator source = Generator;
                Task<Island> generating = Task.Run(() => source.GetOrGenerate(seed));
                while (!generating.IsCompleted) yield return null;

                if (generating.IsFaulted)
                {
                    Debug.LogException(generating.Exception, this);
                    continue;
                }

                Island island = generating.Result;

                var found = new List<Sheet>(wanted.Count);
                var kept = new List<LooseSheetRecord>(wanted.Count);
                for (int s = 0; s < wanted.Count; s++)
                {
                    Sheet sheet;
                    if (!SheetLookup.TryFind(island, wanted[s].Id, out sheet))
                    {
                        Debug.LogWarning("[RoomPaper] " + wanted[s].Id + " is not a sheet of its " +
                                         "own island — dropped.", this);
                        continue;
                    }
                    found.Add(sheet);
                    kept.Add(wanted[s]);
                }
                if (found.Count == 0) continue;

                Task<List<SheetRender>> rendering =
                    Task.Run(() => MapCrate.Render(island, found, ppmm));
                while (!rendering.IsCompleted) yield return null;

                if (rendering.IsFaulted)
                {
                    Debug.LogException(rendering.Exception, this);
                    continue;
                }

                List<SheetRender> rendered = rendering.Result;
                for (int s = 0; s < rendered.Count && s < kept.Count; s++)
                {
                    LooseSheetRecord record = kept[s];

                    SheetView view = Sheets.Restore(rendered[s], Position(record.Pose),
                                                    Rotation(record.Pose));

                    if (record.Where == PaperWhere.Hands && Hands != null && !Hands.Adopt(view))
                        Debug.LogWarning("[RoomPaper] " + record.Id + " was being carried and the " +
                                         "hands are not free — left on the floor.", this);

                    // One upload, then the frame. The room is walkable while its paper arrives.
                    yield return null;
                }
            }

            restoring = null;
        }

        // ---- odds and ends -----------------------------------------------------------------

        static PaperPose PoseOf(Transform t)
        {
            Vector3 p = t.position;
            Vector3 e = t.eulerAngles;
            return new PaperPose(p.x, p.y, p.z, e.x, e.y, e.z);
        }

        static Vector3 Position(PaperPose pose)
        {
            return new Vector3((float)pose.X, (float)pose.Y, (float)pose.Z);
        }

        static Quaternion Rotation(PaperPose pose)
        {
            return Quaternion.Euler((float)pose.RotX, (float)pose.RotY, (float)pose.RotZ);
        }

        CartographyTable TableCalled(string tableId)
        {
            if (string.IsNullOrEmpty(tableId)) return null;

            CartographyTable[] tables = FindObjectsByType<CartographyTable>(FindObjectsSortMode.None);
            for (int i = 0; i < tables.Length; i++)
                if (tables[i].TableId == tableId) return tables[i];

            return null;
        }

        /// <summary>The shelf with that id, or null. The same walk as <see cref="TableCalled"/>:
        /// a handful of shelves, once per restored binder, against holding an index that would
        /// have to be kept in step with a room nobody rebuilds twice.</summary>
        Shelf ShelfCalled(string shelfId)
        {
            if (string.IsNullOrEmpty(shelfId)) return null;

            Shelf[] shelves = FindObjectsByType<Shelf>(FindObjectsSortMode.None);
            for (int i = 0; i < shelves.Length; i++)
                if (shelves[i].ShelfId == shelfId) return shelves[i];

            return null;
        }

        /// <summary>The crate's own value, so a restored sheet is drawn exactly as a delivered
        /// one. A room with no crate keeps the same default the crate carries.</summary>
        double PixelsPerPaperMm
        {
            get
            {
                MapCrate crate = FindFirstObjectByType<MapCrate>(FindObjectsInactive.Include);
                return crate != null ? crate.PixelsPerPaperMm : DefaultPixelsPerPaperMm;
            }
        }

        // Resolved rather than required, as the rest of the room does it.

        BinderSpawner Binders
        {
            get
            {
                if (binders == null) binders = FindFirstObjectByType<BinderSpawner>(FindObjectsInactive.Include);
                return binders;
            }
        }

        SheetSpawner Sheets
        {
            get
            {
                if (sheets == null) sheets = FindFirstObjectByType<SheetSpawner>(FindObjectsInactive.Include);
                return sheets;
            }
        }

        PlayerHands Hands
        {
            get
            {
                if (hands == null) hands = FindFirstObjectByType<PlayerHands>(FindObjectsInactive.Include);
                return hands;
            }
        }

        IslandGenerator Generator
        {
            get
            {
                if (generator == null) generator = FindFirstObjectByType<IslandGenerator>(FindObjectsInactive.Include);
                return generator;
            }
        }
    }
}
