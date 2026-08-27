using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Archivist.Building.Collection;
using Archivist.Building.Table;
using Archivist.Generation.Sheets;
using static Archivist.Harness.Report;

namespace Archivist.Harness
{
    /// <summary>
    /// The archive file — <c>UI/cartography_table/spec.md</c> §9 and
    /// <c>UI/cartography_table/persistence.md</c>. S1 to S9.
    ///
    /// <para><b>Why these run headlessly at all.</b> The save is deliberately split: the file,
    /// the two stores and the format know nothing about UnityEngine, and only <c>Archive</c> —
    /// which owns the disk and the save points — is engine side. So everything a save can get
    /// wrong about <i>content</i> is testable here, in milliseconds, with no editor and no
    /// island generated. What is left for a human is the two things that need a pointer: A5 (a
    /// near miss is still there after closing and reopening) and the room.</para>
    ///
    /// <para><b>The harness compiling these files is itself the check</b> that they stay
    /// engine-free. A <c>using UnityEngine</c> in <c>BoardStore</c> or <c>ArchiveFormat</c> is a
    /// broken build here, not a comment somebody stopped believing.</para>
    ///
    /// <para><b>No island is generated and none is needed.</b> A board holds identities and
    /// poses the player chose; the geometry it does not hold is exactly the point (R1.11, C4.6).
    /// A check here that regenerated an island would be testing the wrong half.</para>
    /// </summary>
    public static class SaveAcceptance
    {
        // The two tables and the island every check below builds on. Fixed values rather than
        // drawn ones: nothing here is about which island it is, and a seed from Streams would
        // make a save check depend on the generator.
        const string TableA = "6f1c9d3b4a2e40f7a1b28c5d9e0f3a11";
        const string TableB = "0a9b8c7d6e5f40312233445566778899";
        const ulong Seed = 0x0123456789ABCDEFUL;
        const ulong OtherSeed = 0xFEDCBA9876543210UL;

        static SheetId Id(int number, Office office = Office.LandSurvey, bool whole = false)
        {
            return new SheetId(Seed, office, whole, number);
        }

        /// <summary>
        /// One board with every state a placement can be in — laid, seated, in an assembly on the
        /// table, in an assembly in the drawer — plus a second table, so nothing can pass by
        /// treating the store as if it held one board.
        /// </summary>
        static BoardStore Furnished(out int onTable, out int parked)
        {
            var store = new BoardStore();

            store.Lay(TableA, Id(1), 1234.5, -678.25, 33.75);
            store.Seat(TableA, Id(2));

            onTable = store.CreateGroup(TableA, Office.LandSurvey, false, 12.5, 100.0, -200.0);
            store.AddToGroup(TableA, onTable, Id(3));
            store.AddToGroup(TableA, onTable, Id(4));

            parked = store.CreateGroup(TableA, Office.LandSurvey, false, -4.0, 5.5, 6.5);
            store.AddToGroup(TableA, parked, Id(5));
            store.AddToGroup(TableA, parked, Id(6));
            store.SetGroupOnTable(TableA, parked, false);

            store.Lay(TableB, Id(1, Office.Hydrographic), -1.0, 2.0, 0.0);
            return store;
        }

        /// <summary>
        /// A room with paper in all three places: two binders on a table, one on the floor, one
        /// in the hands, and two loose sheets — one on the floor, one being carried. Between them
        /// they hold every sheet <see cref="Ledger"/> issued, which is the invariant S9 checks.
        /// </summary>
        static RoomSnapshot Room()
        {
            var pose = new PaperPose(1.25, 0.9, -3.5, 0.0, 137.5, 0.0);
            var other = new PaperPose(-4.0, 0.0, 2.5, 0.0, -12.25, 0.0);

            var binders = new List<BinderRecord>
            {
                new BinderRecord(1, Seed, "Isle of Two Words",
                                 new List<SheetId> { Id(1), Id(2), Id(3) },
                                 PaperWhere.Table, TableA, 0, pose),
                new BinderRecord(2, Seed, "Isle of Two Words",
                                 new List<SheetId> { Id(4) },
                                 PaperWhere.Table, TableA, 1, other),
                new BinderRecord(3, Seed, "Isle of Two Words",
                                 new List<SheetId> { Id(5) },
                                 PaperWhere.Floor, null, -1, other),
                new BinderRecord(4, Seed, "Isle of Two Words",
                                 new List<SheetId>(),
                                 PaperWhere.Hands, null, -1, default(PaperPose)),
            };

            var loose = new List<LooseSheetRecord>
            {
                new LooseSheetRecord(Id(6), PaperWhere.Floor, pose),
                new LooseSheetRecord(Id(1, Office.Hydrographic), PaperWhere.Hands, default(PaperPose)),
            };

            return new RoomSnapshot(binders, loose, 5);
        }

        static SheetLedgerStore Ledger()
        {
            var ledger = new SheetLedgerStore();
            ledger.Record(Seed, 7);
            ledger.Describe(Seed, "Isle of Two Words", 11);
            for (int i = 1; i <= 6; i++) ledger.MarkIssued(Id(i));
            ledger.MarkIssued(Id(1, Office.Hydrographic));
            return ledger;
        }

        static string Write(SheetLedgerStore ledger, BoardStore store, RoomSnapshot room = null)
        {
            var boards = new List<BoardSnapshot>();
            IReadOnlyList<string> tables = store.KnownTables;
            for (int i = 0; i < tables.Count; i++)
            {
                BoardSnapshot snapshot = store.Snapshot(tables[i]);
                if (snapshot != null) boards.Add(snapshot);
            }
            return ArchiveFormat.Write(ledger, boards, room);
        }

        /// <summary>The load side of <c>Archive</c>, minus the disk: ledger first, then every
        /// board through the same ledger filter C9.1 demands. Copied rather than called because
        /// <c>Archive</c> is a MonoBehaviour; if that ever drifts from this, S4 is what says
        /// so.</summary>
        static void Read(string text, out SheetLedgerStore ledger, out BoardStore store,
                         out List<string> warnings, out BoardRestoreReport worst)
        {
            RoomSnapshot room;
            Read(text, out ledger, out store, out room, out warnings, out worst);
        }

        static void Read(string text, out SheetLedgerStore ledger, out BoardStore store,
                         out RoomSnapshot room, out List<string> warnings,
                         out BoardRestoreReport worst)
        {
            ArchiveFormat.Contents contents = ArchiveFormat.Read(text);

            ledger = new SheetLedgerStore();
            store = new BoardStore();
            room = contents.Room;
            warnings = contents.Warnings;
            worst = new BoardRestoreReport(0, 0);

            if (!contents.Readable) return;

            for (int i = 0; i < contents.Islands.Count; i++)
            {
                ArchiveFormat.LedgerIsland island = contents.Islands[i];
                ledger.Record(island.Seed, island.Index);
                ledger.Describe(island.Seed, island.Name, island.TotalSheets);
                for (int s = 0; s < island.Issued.Count; s++) ledger.MarkIssued(island.Issued[s]);
            }

            for (int i = 0; i < contents.Boards.Count; i++)
            {
                BoardRestoreReport report = store.Restore(OnlyIssued(contents.Boards[i], ledger));
                if (report.DroppedSheets + report.DissolvedGroups
                    > worst.DroppedSheets + worst.DissolvedGroups) worst = report;
            }
        }

        /// <summary>C9.1's check, as <c>Archive.Issued</c> performs it: a board entry naming a
        /// sheet the ledger does not have issued never reaches the store.</summary>
        static BoardSnapshot OnlyIssued(BoardSnapshot board, SheetLedgerStore ledger)
        {
            var placed = new List<BoardSnapshot.Entry>();
            for (int i = 0; i < board.Placed.Count; i++)
                if (ledger.IsIssued(board.Placed[i].Id)) placed.Add(board.Placed[i]);

            var groups = new List<GroupRecord>();
            for (int i = 0; i < board.Groups.Count; i++)
            {
                GroupRecord group = board.Groups[i];
                var members = new List<SheetId>();
                for (int m = 0; group.Members != null && m < group.Members.Count; m++)
                    if (ledger.IsIssued(group.Members[m])) members.Add(group.Members[m]);

                groups.Add(new GroupRecord(group.GroupId, group.RotationDeg, group.OffsetX,
                                           group.OffsetY, group.Office, group.WholeIsland,
                                           group.OnTable, members.ToArray()));
            }

            return new BoardSnapshot(board.TableId, board.IslandSeed, placed, groups,
                                     board.NextGroupId);
        }

        // ---------------------------------------------------------------- S1

        /// <summary>
        /// §9 — a board written, read and written again is the same board.
        ///
        /// <para>Comparing the two <i>texts</i> rather than walking the stores is deliberate: it
        /// is one assertion that covers every field, every order and every number at once, and a
        /// field added to the format without a reader lands here as a diff instead of as silence.
        /// </para>
        /// </summary>
        public static void S1_RoundTrip()
        {
            Console.WriteLine("S1  Round trip");

            int onTable, parked;
            BoardStore store = Furnished(out onTable, out parked);
            SheetLedgerStore ledger = Ledger();

            string first = Write(ledger, store);

            SheetLedgerStore ledger2;
            BoardStore store2;
            List<string> warnings;
            BoardRestoreReport worst;
            Read(first, out ledger2, out store2, out warnings, out worst);

            if (warnings.Count > 0) { Fail("S1", "a clean save warned: " + warnings[0]); return; }
            if (!worst.Clean)       { Fail("S1", "a clean save lost something: " + worst); return; }

            string second = Write(ledger2, store2);
            if (first != second)
            {
                Fail("S1", "write -> read -> write differs");
                Info("first:");  Console.Write(first);
                Info("second:"); Console.Write(second);
                return;
            }

            Pass("S1", "write -> read -> write is identical  (" +
                       first.Split('\n').Length + " lines, 2 boards, 2 groups)");

            // The ledger half of C9.5, checked through the same file.
            IslandHolding holding;
            bool known = ledger2.TryGetHolding(Seed, out holding);

            if (!known || holding.Index != 7 || holding.Total != 11
                || holding.Name != "Isle of Two Words" || holding.Issued != 7)
            {
                Fail("S1", "the ledger did not survive: " + (known ? holding.ToString() : "no island"));
                return;
            }
            Pass("S1", "the ledger survives with it: index, total, a name with spaces, 7 issued");

            // Every state a placement can be in, one at a time — a board that came back with the
            // right line count and the wrong kinds would pass the comparison above.
            Placement laid, seated;
            bool ok = store2.TryGetPlacement(TableA, Id(1), out laid) && !laid.Seated && !laid.Grouped
                   && laid.GroundX == 1234.5 && laid.GroundY == -678.25 && laid.RotationDeg == 33.75;
            ok &= store2.TryGetPlacement(TableA, Id(2), out seated) && seated.Seated;

            GroupRecord live, drawer;
            ok &= store2.TryGetGroup(TableA, onTable, out live) && live.OnTable && live.MemberCount == 2
               && live.RotationDeg == 12.5 && live.OffsetX == 100.0 && live.OffsetY == -200.0;
            ok &= store2.TryGetGroup(TableA, parked, out drawer) && !drawer.OnTable && drawer.MemberCount == 2;

            // G6.4: a parked group's members are in the drawer, not on the board (C4.5).
            ok &= !store2.IsOnTable(TableA, Id(5)) && !store2.IsOnTable(TableA, Id(6));
            ok &= store2.IslandOf(TableB) == Seed && store2.OnTableCount(TableB) == 1;

            if (!ok) { Fail("S1", "a placement, a group or the second board came back wrong"); return; }
            Pass("S1", "laid, seated, on-table and parked all come back as themselves");
        }

        // ---------------------------------------------------------------- S2

        /// <summary>
        /// C4.7 and G5.6 — the board comes back in the order the player built it.
        ///
        /// <para>Sheets at ground scale overlap by a fifth (C1.2), so lay order is which paper is
        /// on top. A save that reordered itself between two openings would be unreadable, and
        /// would look like nothing worse than a redraw.</para>
        /// </summary>
        public static void S2_Order()
        {
            Console.WriteLine("S2  Order is content");

            var store = new BoardStore();
            store.Lay(TableA, Id(5), 0.0, 0.0, 0.0);
            store.Lay(TableA, Id(1), 10.0, 0.0, 0.0);
            store.Lay(TableA, Id(3), 20.0, 0.0, 0.0);
            store.Lay(TableA, Id(2), 30.0, 0.0, 0.0);

            // Joined in this order, so this is the run G5.6 draws them in — and it is the
            // opposite of both their numbering and their lay order.
            int group = store.CreateGroup(TableA, Office.LandSurvey, false, 0.0, 0.0, 0.0);
            store.AddToGroup(TableA, group, Id(3));
            store.AddToGroup(TableA, group, Id(1));

            // Re-laying a sheet already down does not move it up the pile (C4.7).
            store.Lay(TableA, Id(5), 0.5, 0.5, 1.0);

            var ledger = new SheetLedgerStore();
            for (int i = 1; i <= 5; i++) ledger.MarkIssued(Id(i));

            SheetLedgerStore back;
            BoardStore store2;
            List<string> warnings;
            BoardRestoreReport worst;
            Read(Write(ledger, store), out back, out store2, out warnings, out worst);

            string before = Numbers(store.LayOrder(TableA));
            string after = Numbers(store2.LayOrder(TableA));
            if (before != after) { Fail("S2", "lay order " + before + " came back as " + after); return; }
            Pass("S2", "lay order survives, nudging included: " + after);

            GroupRecord record;
            if (!store2.TryGetGroup(TableA, group, out record)) { Fail("S2", "the group did not survive"); return; }

            string joined = Numbers(record.Members);
            if (joined != "3,1") { Fail("S2", "join order came back as " + joined + ", not 3,1"); return; }
            Pass("S2", "join order survives — G5.6's run is the one the player made: " + joined);
        }

        /// <summary>
        /// A6's edit, performed on the document: every pose member struck out of every placement,
        /// as somebody deleting them in a text editor would leave things. What survives is what
        /// was never a pose to begin with.
        /// </summary>
        static string HandEdited(string text)
        {
            var kept = new List<string>();
            bool inPlaced = false;

            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.TrimStart();

                // Placements only. A group's frame lives in the same three member names one level
                // up, and it is not a pose A6 is talking about — it is the assembly's ONLY pose,
                // and striking it out would be deleting the thing the check wants to survive.
                if (trimmed.StartsWith("\"placed\":")) inPlaced = true;
                else if (inPlaced && (trimmed == "]" || trimmed == "],")) inPlaced = false;

                if (inPlaced && (trimmed.StartsWith("\"x\":") || trimmed.StartsWith("\"y\":")
                                 || trimmed.StartsWith("\"rotation\":"))) continue;

                kept.Add(line);
            }

            // Deleting members leaves the previous one with a trailing comma. A person editing by
            // hand would fix that; the test does the same, so what is being checked is the save
            // and not the editor.
            string edited = string.Join("\n", kept);
            return edited.Replace(",\n        }", "\n        }")
                         .Replace(",\n          }", "\n          }")
                         .Replace(",\n            }", "\n            }");
        }

        static string ReplaceFirst(string text, string what, string with)
        {
            int at = text.IndexOf(what, StringComparison.Ordinal);
            return at < 0 ? text : text.Substring(0, at) + with + text.Substring(at + what.Length);
        }

        static string Numbers(IReadOnlyList<SheetId> ids)
        {
            var parts = new List<string>(ids.Count);
            for (int i = 0; i < ids.Count; i++) parts.Add(ids[i].Number.ToString(Inv));
            return string.Join(",", parts);
        }

        // ---------------------------------------------------------------- S3

        /// <summary>
        /// R1.11, C4.6, G4.4 and A6 — <b>the file holds no geometry it could have recomputed</b>.
        ///
        /// <para>A seated sheet is a name and a grouped sheet is a name and an id; a nine-sheet
        /// assembly is one frame. A6 asks that the pose fields can be deleted out of a save by
        /// hand and every seated sheet still return — so the check deletes every line that
        /// carries a pose and asserts the seated sheet comes back at all.</para>
        /// </summary>
        public static void S3_NoGeometry()
        {
            Console.WriteLine("S3  Nothing geometric is persisted");

            var store = new BoardStore();
            store.Seat(TableA, Id(1));

            int group = store.CreateGroup(TableA, Office.LandSurvey, false, 30.0, 7.0, 9.0);
            for (int i = 2; i <= 10; i++) store.AddToGroup(TableA, group, Id(i));

            var ledger = new SheetLedgerStore();
            for (int i = 1; i <= 10; i++) ledger.MarkIssued(Id(i));

            string text = Write(ledger, store);

            // Counted in the document rather than in the objects: the claim is about what
            // reaches the disk, and a reader that quietly ignored a pose would pass an
            // object-level check.
            Json.Value root;
            string bad;
            if (!Json.TryParse(text, out root, out bad)) { Fail("S3", "the save is not JSON: " + bad); return; }

            Json.Value board = root["boards"].Items[0];
            int frames = board["groups"].Count;
            int seated = 0, grouped = 0, poses = 0;

            foreach (Json.Value entry in board["placed"].Items)
            {
                if (entry["seated"].AsBool(false)) seated++;
                if (entry["group"].IsNumber) grouped++;
                if (entry["x"].IsNumber || entry["y"].IsNumber || entry["rotation"].IsNumber) poses++;
            }

            if (frames != 1 || poses != 0 || seated != 1 || grouped != 9)
            {
                Fail("S3", "a 9-sheet assembly and one seated sheet wrote " + frames + " frame(s), " +
                           poses + " pose(s), " + seated + " seated and " + grouped + " grouped");
                return;
            }
            Pass("S3", "9 members and a seated sheet cost 1 frame and 0 poses (G4.4)");

            // A6, performed the way A6 describes it: by hand, in the file. Every pose member is
            // struck out of the document — which for a seated sheet means striking out nothing,
            // because there was never one to strike.
            string edited = HandEdited(text);

            SheetLedgerStore back;
            BoardStore store2;
            List<string> warnings;
            BoardRestoreReport worst;
            Read(edited, out back, out store2, out warnings, out worst);

            Placement placement;
            if (!store2.TryGetPlacement(TableA, Id(1), out placement) || !placement.Seated)
            {
                Fail("S3", "a seated sheet did not survive the pose fields being deleted");
                return;
            }
            Pass("S3", "A6: every pose line deleted by hand, the seated sheet still returns seated");

            GroupRecord record;
            if (!store2.TryGetGroup(TableA, group, out record) || record.MemberCount != 9
                || record.RotationDeg != 30.0 || record.OffsetX != 7.0 || record.OffsetY != 9.0)
            {
                Fail("S3", "the assembly's frame did not survive with it");
                return;
            }
            Pass("S3", "and the assembly keeps its frame, which is the only pose it ever had");
        }

        // ---------------------------------------------------------------- S4

        /// <summary>
        /// C9.1 — <b>a board may not name a sheet the ledger says was never issued</b>, and an
        /// assembly that falls below two members on the way in dissolves.
        ///
        /// <para>The ordering that writes these files makes this unreachable; it is checked
        /// because a format outlives the reasoning that made it safe. The decision under test is
        /// <c>persistence.md</c> §3: the survivor of a load-time dissolution goes back to the
        /// cabinet rather than to an invented pose, because composing one needs the island and
        /// load has none.</para>
        /// </summary>
        public static void S4_LedgerFirst()
        {
            Console.WriteLine("S4  Ledger first, boards second");

            int onTable, parked;
            BoardStore store = Furnished(out onTable, out parked);
            string text = Write(Ledger(), store);

            // The same file, read against a ledger that only ever issued two of the six: sheet 1,
            // which is loose, and sheet 3, which is half of an assembly.
            ArchiveFormat.Contents contents = ArchiveFormat.Read(text);
            var thin = new SheetLedgerStore();
            thin.Record(Seed, 7);
            thin.MarkIssued(Id(1));
            thin.MarkIssued(Id(3));

            var store2 = new BoardStore();
            int dropped = 0, dissolved = 0;
            for (int i = 0; i < contents.Boards.Count; i++)
            {
                BoardRestoreReport report = store2.Restore(OnlyIssued(contents.Boards[i], thin));
                dropped += report.DroppedSheets;
                dissolved += report.DissolvedGroups;
            }

            if (dissolved != 2) { Fail("S4", "expected both assemblies to dissolve, got " + dissolved); return; }
            Pass("S4", "an assembly below two members dissolves on the way in (" + dropped +
                       " sheets dropped, " + dissolved + " groups dissolved)");

            if (!store2.IsOnTable(TableA, Id(1))) { Fail("S4", "an issued, loose sheet was dropped"); return; }
            if (store2.IsOnTable(TableA, Id(3)))  { Fail("S4", "the survivor of a dissolved pair stayed on the board"); return; }
            if (store2.GroupsOn(TableA).Count != 0) { Fail("S4", "a group survived with one member"); return; }
            Pass("S4", "the survivor goes back to the cabinet, the issued loose sheet stays where it was");

            // C4.3 / R6.8, asked of a file: another island's paper is not this board's.
            var mixed = new BoardStore();
            mixed.Lay(TableA, Id(1), 1.0, 2.0, 3.0);
            var strayLedger = new SheetLedgerStore();
            strayLedger.MarkIssued(Id(1));
            strayLedger.MarkIssued(new SheetId(OtherSeed, Office.LandSurvey, false, 1));

            BoardSnapshot one = mixed.Snapshot(TableA);
            var withStray = new List<BoardSnapshot.Entry>(one.Placed);
            withStray.Add(new BoardSnapshot.Entry(new SheetId(OtherSeed, Office.LandSurvey, false, 1),
                                                  Placement.Laid(0.0, 0.0, 0.0)));

            var store3 = new BoardStore();
            BoardRestoreReport strayReport = store3.Restore(
                new BoardSnapshot(TableA, one.IslandSeed, withStray, one.Groups, one.NextGroupId));

            if (strayReport.DroppedSheets != 1 || store3.OnTableCount(TableA) != 1)
            {
                Fail("S4", "a sheet of another island was accepted onto a bound board");
                return;
            }
            Pass("S4", "a sheet of another island is refused by the restore too (C4.3, R6.8)");
        }

        // ---------------------------------------------------------------- S5

        /// <summary>
        /// A damaged file costs what it damaged, and no more.
        ///
        /// <para>The rule the format is built on: an unreadable file is treated as <i>no</i> save
        /// rather than as an empty one, because a save that cannot be read costs the player
        /// everything and a line that cannot be read costs them one drag. Both halves are checked
        /// here, because it is the difference between the two that makes the rule.</para>
        /// </summary>
        public static void S5_Damage()
        {
            Console.WriteLine("S5  A damaged file");

            if (ArchiveFormat.Read("nonsense, and not even JSON").Readable)
            { Fail("S5", "a file that is not an archive was read as one"); return; }

            if (ArchiveFormat.Read("").Readable)
            { Fail("S5", "an empty file was read as an archive"); return; }

            if (ArchiveFormat.Read("{ \"archive\": 1, \"boards\": [ ").Readable)
            { Fail("S5", "a truncated document was read as a whole one"); return; }

            if (ArchiveFormat.Read("{ \"archive\": 99 }").Readable)
            { Fail("S5", "a future version was read by this build"); return; }

            if (ArchiveFormat.Read("{ \"ledger\": [] }").Readable)
            { Fail("S5", "a document with no version was read anyway"); return; }
            Pass("S5", "not JSON, empty, truncated, unversioned, or a version this build does " +
                       "not know: refused whole");

            int onTable, parked;
            BoardStore store = Furnished(out onTable, out parked);
            string text = Write(Ledger(), store);

            // One placement mangled by hand — its rotation struck out, the shape a careless edit
            // actually produces. The document still parses; the record does not survive.
            string mangled = text.Replace("\"rotation\": 33.75", "\"rotation\": null");
            if (mangled == text) { Fail("S5", "the fixture wrote no pose to mangle"); return; }

            SheetLedgerStore back;
            BoardStore store2;
            List<string> warnings;
            BoardRestoreReport worst;
            Read(mangled, out back, out store2, out warnings, out worst);

            if (warnings.Count != 1) { Fail("S5", "one bad record produced " + warnings.Count + " warnings"); return; }
            if (store2.IsOnTable(TableA, Id(1))) { Fail("S5", "the mangled record was read anyway"); return; }
            if (!store2.IsOnTable(TableA, Id(2)) || store2.GroupsOn(TableA).Count != 2)
            { Fail("S5", "one bad record cost more than itself"); return; }

            Pass("S5", "a damaged record inside a good document costs that record, warns once, " +
                       "and nothing else: " + warnings[0]);
        }

        // ---------------------------------------------------------------- S6

        /// <summary>
        /// A5's foundation, and the trap under it: <b>a pose is the same number after a round
        /// trip, on any machine</b>.
        ///
        /// <para>A5 asks that a sheet released just outside tolerance is still there after
        /// closing and reopening — measured, not eyeballed. That is only true if the text
        /// round-trips exactly, so the poses here are the awkward ones: a value that is not
        /// representable, a very small one, an angle at the fold. And the whole check runs again
        /// under a culture that writes <c>0,5</c>, because a save written on one machine and read
        /// on another must not move the paper.</para>
        /// </summary>
        public static void S6_Numbers()
        {
            Console.WriteLine("S6  Poses round-trip exactly, in any culture");

            double[] awkward =
            {
                0.1 + 0.2, -1e-7, 179.99999999999997, -0.0000000000001, 12345678.875
            };

            string invariant = null;
            CultureInfo original = CultureInfo.CurrentCulture;
            string[] cultures = { "en-US", "it-IT", "de-DE" };

            try
            {
                for (int c = 0; c < cultures.Length; c++)
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(cultures[c]);

                    var store = new BoardStore();
                    var ledger = new SheetLedgerStore();
                    for (int i = 0; i < awkward.Length; i++)
                    {
                        store.Lay(TableA, Id(i + 1), awkward[i], -awkward[i], awkward[i]);
                        ledger.MarkIssued(Id(i + 1));
                    }

                    string text = Write(ledger, store);

                    SheetLedgerStore back;
                    BoardStore store2;
                    List<string> warnings;
                    BoardRestoreReport worst;
                    Read(text, out back, out store2, out warnings, out worst);

                    for (int i = 0; i < awkward.Length; i++)
                    {
                        Placement placement;
                        if (store2.TryGetPlacement(TableA, Id(i + 1), out placement)
                            && placement.GroundX == awkward[i]
                            && placement.GroundY == -awkward[i]
                            && placement.RotationDeg == awkward[i]) continue;

                        Fail("S6", "under " + cultures[c] + ", " + awkward[i].ToString("R", Inv) +
                                   " did not survive the file");
                        return;
                    }

                    if (invariant == null) invariant = text;
                    else if (invariant != text)
                    {
                        Fail("S6", "the file written under " + cultures[c] + " differs from the one " +
                                   "written under " + cultures[0]);
                        return;
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }

            Pass("S6", awkward.Length + " awkward poses survive bit-for-bit under " +
                       string.Join(", ", cultures));
        }

        // ---------------------------------------------------------------- S7

        /// <summary>
        /// G4.2 and G15.1 — <b>a group id is never handed out twice</b>, across a save as well as
        /// across a session.
        ///
        /// <para>The counter is saved for one reason: a reload that rewound it would give a new
        /// assembly the name of a dead one, and every stale reference — a half-applied undo, an
        /// older file, a row the cabinet has not redrawn — would then name somebody else's paper
        /// and be confidently wrong instead of obviously wrong.</para>
        /// </summary>
        public static void S7_GroupIds()
        {
            Console.WriteLine("S7  Group ids are never reused");

            var store = new BoardStore();
            var ledger = new SheetLedgerStore();
            for (int i = 1; i <= 6; i++) ledger.MarkIssued(Id(i));

            int first = store.CreateGroup(TableA, Office.LandSurvey, false, 0.0, 0.0, 0.0);
            store.AddToGroup(TableA, first, Id(1));
            store.AddToGroup(TableA, first, Id(2));

            int second = store.CreateGroup(TableA, Office.LandSurvey, false, 0.0, 0.0, 0.0);
            store.AddToGroup(TableA, second, Id(3));
            store.AddToGroup(TableA, second, Id(4));

            // The second assembly is broken up before the save, so its id names nothing at all in
            // the file — the case a naive "highest id + 1" gets wrong.
            store.Lay(TableA, Id(3), 1.0, 1.0, 0.0);

            SheetLedgerStore back;
            BoardStore store2;
            List<string> warnings;
            BoardRestoreReport worst;
            Read(Write(ledger, store), out back, out store2, out warnings, out worst);

            int next = store2.CreateGroup(TableA, Office.LandSurvey, false, 0.0, 0.0, 0.0);
            store2.AddToGroup(TableA, next, Id(5));
            store2.AddToGroup(TableA, next, Id(6));

            if (next <= second)
            {
                Fail("S7", "after a reload the next group is " + next + ", which was already used by " +
                           (next == first ? "a live assembly" : "a dissolved one"));
                return;
            }

            GroupRecord survivor;
            if (!store2.TryGetGroup(TableA, first, out survivor) || survivor.MemberCount != 2)
            { Fail("S7", "the surviving assembly lost its id or its members"); return; }

            if (store2.TryGetGroup(TableA, second, out survivor))
            { Fail("S7", "a dissolved assembly came back from the file"); return; }

            Pass("S7", "ids " + first + " and " + second + " were used; after a reload the next is " + next);
        }

        // ---------------------------------------------------------------- S8

        /// <summary>
        /// The room — <c>persistence.md</c> §2. Binders, what is filed in them, where each one
        /// lies, the loose paper, and the counter on the spine.
        ///
        /// <para>Every place paper can be is in the fixture, because the three are not variations
        /// of one case: a binder on a table is parented to an anchor and keeps a jittered angle
        /// nothing can recompute, one on the floor keeps a pose the player dropped it at, and one
        /// in the hands has no pose at all.</para>
        /// </summary>
        public static void S8_Room()
        {
            Console.WriteLine("S8  The room");

            int onTable, parked;
            BoardStore board = Furnished(out onTable, out parked);
            SheetLedgerStore ledger = Ledger();
            RoomSnapshot room = Room();

            string first = Write(ledger, board, room);

            SheetLedgerStore back;
            BoardStore board2;
            RoomSnapshot room2;
            List<string> warnings;
            BoardRestoreReport worst;
            Read(first, out back, out board2, out room2, out warnings, out worst);

            if (warnings.Count > 0) { Fail("S8", "a clean room warned: " + warnings[0]); return; }

            string second = Write(back, board2, room2);
            if (first != second)
            {
                Fail("S8", "write -> read -> write differs");
                Info("first:");  Console.Write(first);
                Info("second:"); Console.Write(second);
                return;
            }
            Pass("S8", "write -> read -> write is identical with the room in it (" +
                       room2 + ", next binder " + room2.NextBinderNumber + ")");

            if (room2.Binders.Count != 4 || room2.Sheets.Count != 2)
            { Fail("S8", "the room came back as " + room2); return; }

            BinderRecord onTableBinder = room2.Binders[0];
            if (onTableBinder.Where != PaperWhere.Table || onTableBinder.TableId != TableA
                || onTableBinder.Anchor != 0 || onTableBinder.Number != 1
                || onTableBinder.IslandName != "Isle of Two Words")
            { Fail("S8", "the binder on the table came back as " + onTableBinder); return; }

            if (onTableBinder.Pose.X != 1.25 || onTableBinder.Pose.Y != 0.9
                || onTableBinder.Pose.Z != -3.5 || onTableBinder.Pose.RotY != 137.5)
            { Fail("S8", "a binder's pose did not survive — the angle it lay at is not recomputable"); return; }

            if (Numbers(onTableBinder.Contents) != "1,2,3")
            { Fail("S8", "filing order came back as " + Numbers(onTableBinder.Contents)); return; }
            Pass("S8", "a binder keeps its number, island, table, anchor, pose and filing order");

            if (room2.Binders[3].Where != PaperWhere.Hands || room2.Binders[2].Where != PaperWhere.Floor)
            { Fail("S8", "floor and hands did not come back as themselves"); return; }

            if (room2.Sheets[0].Where != PaperWhere.Floor || room2.Sheets[1].Where != PaperWhere.Hands)
            { Fail("S8", "the loose sheets did not come back where they were"); return; }
            Pass("S8", "on a table, on the floor and in the hands are three states and stay three");

            // A binder with no place is dropped whole rather than guessed at, and says so. The
            // third binder is the one on the floor; striking out its "where" leaves a record that
            // parses and means nothing.
            // The FIRST floor record only: the binders are written before the loose sheets, and
            // one of those is on the floor too. Mangling both would be testing two things and
            // reporting one.
            string placeless = ReplaceFirst(first, "\"where\": \"floor\"", "\"where\": \"nowhere\"");
            if (placeless == first) { Fail("S8", "the fixture wrote no floor binder"); return; }

            Read(placeless, out back, out board2, out room2, out warnings, out worst);
            if (room2.Binders.Count != 3 || warnings.Count != 1)
            { Fail("S8", "a binder with no place: " + room2 + ", " + warnings.Count + " warning(s)"); return; }
            Pass("S8", "a binder that does not say where it is: dropped, warned, and nothing else lost");
        }

        // ---------------------------------------------------------------- S9

        /// <summary>
        /// <b>Every issued sheet is somewhere, and somewhere once.</b> The reason the room is in
        /// the file at all: the ledger says a sheet has entered the world, and until the room was
        /// saved with it nothing said where it went — so a reloaded archive claimed paper that did
        /// not exist.
        ///
        /// <para>All three ways of breaking it are checked, because the audit reports rather than
        /// repairs and a count nobody reads is a count nobody notices.</para>
        /// </summary>
        public static void S9_EveryIssuedSheetIsSomewhere()
        {
            Console.WriteLine("S9  Every issued sheet is somewhere");

            SheetLedgerStore ledger = Ledger();

            RoomAudit clean = Room().Audit(ledger);
            if (!clean.Clean) { Fail("S9", "a whole room did not account for the ledger: " + clean); return; }
            Pass("S9", "7 issued sheets, 4 binders and 2 loose: every one accounted for, once");

            // Lost: the binder holding sheets 1..3 is gone from the room.
            var missing = new List<BinderRecord>(Room().Binders);
            missing.RemoveAt(0);
            RoomAudit lost = new RoomSnapshot(missing, Room().Sheets, 5).Audit(ledger);
            if (lost.Missing != 3 || lost.Duplicated != 0 || lost.Unissued != 0)
            { Fail("S9", "a lost binder reported " + lost); return; }

            // Doubled: the same sheet filed in two binders.
            var doubled = new List<BinderRecord>(Room().Binders);
            doubled[2] = new BinderRecord(3, Seed, "Isle of Two Words",
                                          new List<SheetId> { Id(5), Id(1) },
                                          PaperWhere.Floor, null, -1, default(PaperPose));
            RoomAudit twice = new RoomSnapshot(doubled, Room().Sheets, 5).Audit(ledger);
            if (twice.Duplicated != 1) { Fail("S9", "the same sheet in two binders reported " + twice); return; }

            // Restocked: paper the archive never handed out.
            var stocked = new List<LooseSheetRecord>(Room().Sheets);
            stocked.Add(new LooseSheetRecord(Id(9), PaperWhere.Floor, default(PaperPose)));
            RoomAudit invented = new RoomSnapshot(Room().Binders, stocked, 5).Audit(ledger);
            if (invented.Unissued != 1) { Fail("S9", "a sheet that was never issued reported " + invented); return; }

            Pass("S9", "lost, doubled and never-issued paper are each counted and named");
        }
    }
}
