using System;
using System.Collections.Generic;
using System.Globalization;
using Archivist.Building.Collection;
using Archivist.Generation.Sheets;
using static Archivist.Harness.Report;

namespace Archivist.Harness
{
    /// <summary>
    /// The archive file. S1 the round trip, S2 the room, S3 the invariant the room exists for.
    ///
    /// <para><b>Why these run headlessly at all.</b> The save is deliberately split: the file,
    /// the stores and the format know nothing about UnityEngine, and only <c>Archive</c> — which
    /// owns the disk and the save points — is engine side. So everything a save can get wrong
    /// about <i>content</i> is testable here, in milliseconds, with no editor and no island
    /// generated.</para>
    ///
    /// <para><b>The harness compiling these files is itself the check</b> that they stay
    /// engine-free. A <c>using UnityEngine</c> in <c>ArchiveFormat</c> is a broken build here,
    /// not a comment somebody stopped believing.</para>
    ///
    /// <para><b>There is no board section any more</b> (Q4.7). S1–S7 of the old suite tested
    /// placements, assemblies, frames and lay order through the file; a board is now a view of
    /// the binders on a table, derived every time it opens, so there is nothing about it to
    /// write down and nothing to check. What survives is what the file still carries: the
    /// ledger, and where each piece of paper is.</para>
    /// </summary>
    public static class SaveAcceptance
    {
        // The table and the island every check below builds on. Fixed values rather than drawn
        // ones: nothing here is about which island it is, and a seed from Streams would make a
        // save check depend on the generator.
        const string TableA = "6f1c9d3b4a2e40f7a1b28c5d9e0f3a11";
        const string ShelfA = "b3d81e57c04a49f2ae6710d95c8b2f34";
        const ulong Seed = 0x0123456789ABCDEFUL;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static SheetId Id(int number, Office office = Office.LandSurvey, bool whole = false)
        {
            return new SheetId(Seed, office, whole, number);
        }

        /// <summary>
        /// Every place paper can be, because the four are not variations of one case: a binder
        /// on a table is parented to an anchor and keeps a jittered angle nothing can recompute,
        /// one on the floor keeps a pose the player dropped it at, one in the hands has no pose at
        /// all, and one on a shelf is named by a row and a column rather than by a place.
        /// </summary>
        static RoomSnapshot Room()
        {
            var pose = new PaperPose(1.25, 0.9, -3.5, 0.0, 137.5, 0.0);
            var other = new PaperPose(-4.0, 0.0, 2.5, 0.0, -12.25, 0.0);
            var shelved = new PaperPose(2.5, 1.35, 4.75, 0.0, 90.0, 90.0);

            var binders = new List<BinderRecord>
            {
                new BinderRecord(1, Seed, "Isle of Two Words",
                                 new List<SheetId> { Id(1), Id(2), Id(3) },
                                 PaperWhere.Table, TableA, 0, pose),

                // A second binder naming the SAME island and holding different plates — the
                // pair a merge is tried on.
                new BinderRecord(2, Seed, "Isle of Two Words",
                                 new List<SheetId> { Id(4) },
                                 PaperWhere.Table, TableA, 1, other),

                // Two offices in one folder: what a binder that was never sorted looks like.
                new BinderRecord(3, Seed, "Isle of Two Words",
                                 new List<SheetId> { Id(5), Id(1, Office.Garrison) },
                                 PaperWhere.Floor, null, -1, other),

                // Empty. It cannot arise in play (F-R19.2) and the format still has to survive
                // reading one, because a save is read by a build that may not have written it.
                new BinderRecord(4, Seed, "Isle of Two Words",
                                 new List<SheetId>(),
                                 PaperWhere.Hands, null, -1, default(PaperPose)),

                // Filed in a rack (R4.2), which is the one place named by a KEY rather than by a
                // seat: (row, column) survives the shelf's spacing being retuned, where a slot
                // index would silently shift every binder along when slotsPerRow changed. The
                // pose is still written — it is what the binder falls back to if that slot is
                // gone by the time the file is read.
                new BinderRecord(5, Seed, "Isle of Two Words",
                                 new List<SheetId> { Id(7) },
                                 PaperWhere.Shelf, null, -1, shelved, ShelfA, 2, 5),
            };

            var loose = new List<LooseSheetRecord>
            {
                new LooseSheetRecord(Id(6), PaperWhere.Floor, pose),
                new LooseSheetRecord(Id(1, Office.Hydrographic), PaperWhere.Hands, default(PaperPose)),
            };

            return new RoomSnapshot(binders, loose, 6);
        }

        static SheetLedgerStore Ledger()
        {
            var ledger = new SheetLedgerStore();
            ledger.Record(Seed, 7);
            ledger.Describe(Seed, "Isle of Two Words", 11);
            for (int i = 1; i <= 7; i++) ledger.MarkIssued(Id(i));
            ledger.MarkIssued(Id(1, Office.Hydrographic));
            ledger.MarkIssued(Id(1, Office.Garrison));
            return ledger;
        }

        static void Read(string text, out SheetLedgerStore ledger, out RoomSnapshot room,
                         out List<string> warnings)
        {
            ArchiveFormat.Contents contents = ArchiveFormat.Read(text);

            ledger = new SheetLedgerStore();
            for (int i = 0; i < contents.Islands.Count; i++)
            {
                ArchiveFormat.LedgerIsland island = contents.Islands[i];
                ledger.Record(island.Seed, island.Index);
                ledger.Describe(island.Seed, island.Name, island.TotalSheets);
                for (int s = 0; s < island.Issued.Count; s++) ledger.MarkIssued(island.Issued[s]);
            }

            room = contents.Room;
            warnings = contents.Warnings;
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

        // ---------------------------------------------------------------- S1

        /// <summary>
        /// Write, read, write again, and compare the text. The strongest check available on a
        /// format: it catches a field written and not read, a field read and not written, and
        /// any reordering, without naming a single one of them.
        /// </summary>
        public static void S1_RoundTrip()
        {
            Console.WriteLine("S1  Round trip");

            SheetLedgerStore ledger = Ledger();
            string first = ArchiveFormat.Write(ledger, Room());

            SheetLedgerStore ledger2;
            RoomSnapshot room2;
            List<string> warnings;
            Read(first, out ledger2, out room2, out warnings);

            if (warnings.Count > 0) { Fail("S1", "a clean save warned: " + warnings[0]); return; }

            string second = ArchiveFormat.Write(ledger2, room2);
            if (first != second)
            {
                Fail("S1", "write -> read -> write differs");
                Info("first:");  Console.Write(first);
                Info("second:"); Console.Write(second);
                return;
            }
            Pass("S1", "write -> read -> write is identical  (" +
                       first.Split('\n').Length + " lines)");

            IslandHolding holding;
            bool known = ledger2.TryGetHolding(Seed, out holding);

            if (!known || holding.Index != 7 || holding.Total != 11
                || holding.Name != "Isle of Two Words" || holding.Issued != 9)
            {
                Fail("S1", "the ledger did not survive: " + (known ? holding.ToString() : "no island"));
                return;
            }
            Pass("S1", "the ledger survives: index, total, a name with spaces, 9 issued");

            // Issue order is content, not incidental: the collection screen reads it, so a load
            // that shuffled it would be visible.
            if (Numbers(ledger2.IssuedSheets(Seed)) != "1,2,3,4,5,6,7,1,1")
            { Fail("S1", "issue order came back as " + Numbers(ledger2.IssuedSheets(Seed))); return; }
            Pass("S1", "sheets come back in the order they were issued");

            // A file this build cannot read is no save at all, never an empty one — the one
            // behaviour that cannot make a broken file worse. Every version-1 save is in this
            // case: the plates it names no longer exist (Q1.1).
            //
            // The version is read off the constant rather than typed, so a format that moves on
            // again does not leave this check quietly replacing nothing and passing.
            string thisVersion = "\"archive\": " + ArchiveFormat.Version.ToString(Inv);
            string older = "\"archive\": " + (ArchiveFormat.Version - 1).ToString(Inv);

            ArchiveFormat.Contents old = ArchiveFormat.Read(ReplaceFirst(first, thisVersion, older));
            if (old.Readable)
            { Fail("S1", "a file one version older was accepted by a version-"
                         + ArchiveFormat.Version + " build"); return; }
            Pass("S1", "a file from an older format is refused whole, not read half-way");
        }

        // ---------------------------------------------------------------- S2

        /// <summary>
        /// The room. Binders, what is filed in them, where each one lies, the loose paper, and
        /// the counter on the spine.
        /// </summary>
        public static void S2_Room()
        {
            Console.WriteLine("S2  The room");

            string first = ArchiveFormat.Write(Ledger(), Room());

            SheetLedgerStore back;
            RoomSnapshot room2;
            List<string> warnings;
            Read(first, out back, out room2, out warnings);

            if (warnings.Count > 0) { Fail("S2", "a clean room warned: " + warnings[0]); return; }

            if (room2.Binders.Count != 5 || room2.Sheets.Count != 2)
            { Fail("S2", "the room came back as " + room2); return; }

            BinderRecord onTableBinder = room2.Binders[0];
            if (onTableBinder.Where != PaperWhere.Table || onTableBinder.TableId != TableA
                || onTableBinder.Anchor != 0 || onTableBinder.Number != 1
                || onTableBinder.IslandName != "Isle of Two Words")
            { Fail("S2", "the binder on the table came back as " + onTableBinder); return; }

            if (onTableBinder.Pose.X != 1.25 || onTableBinder.Pose.Y != 0.9
                || onTableBinder.Pose.Z != -3.5 || onTableBinder.Pose.RotY != 137.5)
            { Fail("S2", "a binder's pose did not survive — the angle it lay at is not recomputable"); return; }

            if (Numbers(onTableBinder.Contents) != "1,2,3")
            { Fail("S2", "filing order came back as " + Numbers(onTableBinder.Contents)); return; }
            Pass("S2", "a binder keeps its number, island, table, anchor, pose and filing order");

            BinderRecord shelved = room2.Binders[4];
            if (shelved.Where != PaperWhere.Shelf || shelved.ShelfId != ShelfA
                || shelved.Row != 2 || shelved.Column != 5)
            { Fail("S2", "the binder on the shelf came back as " + shelved
                         + " r" + shelved.Row + "c" + shelved.Column); return; }

            if (shelved.Pose.Y != 1.35 || shelved.Pose.RotZ != 90.0)
            { Fail("S2", "a shelved binder's fallback pose did not survive"); return; }
            Pass("S2", "a shelved binder keeps its shelf, its row and its column");

            // A slot half-named is refused rather than dropped to the floor, for the reason the
            // table's anchor is: a binder that comes back a metre from where it was filed is a
            // binder somebody has to go and find, and silence is what makes them look.
            List<string> complained;
            RoomSnapshot partial;
            SheetLedgerStore ignored;
            Read(ReplaceFirst(first, "\"row\"", "\"rowe\""), out ignored, out partial, out complained);

            if (partial.Binders.Count != 4 || complained.Count == 0)
            { Fail("S2", "a shelf record with no row was read as if it were whole"); return; }
            Pass("S2", "a shelf record missing its row is refused, and says so");
        }

        // ---------------------------------------------------------------- S3

        /// <summary>
        /// <b>Every issued sheet is somewhere, and somewhere once.</b> The reason the room is in
        /// the file at all: the ledger says a sheet has entered the world, and until the room was
        /// saved with it nothing said where it went — so a reloaded archive claimed paper that did
        /// not exist.
        ///
        /// <para>All three ways of breaking it are checked, because the audit reports rather than
        /// repairs and a count nobody reads is a count nobody notices.</para>
        /// </summary>
        public static void S3_EveryIssuedSheetIsSomewhere()
        {
            Console.WriteLine("S3  Every issued sheet is somewhere");

            SheetLedgerStore ledger = Ledger();

            RoomAudit clean = Room().Audit(ledger);
            if (!clean.Clean) { Fail("S3", "a whole room did not account for the ledger: " + clean); return; }
            Pass("S3", "9 issued sheets, 5 binders and 2 loose: every one accounted for, once");

            // Lost: the binder holding sheets 1..3 is gone from the room.
            var missing = new List<BinderRecord>(Room().Binders);
            missing.RemoveAt(0);
            RoomAudit lost = new RoomSnapshot(missing, Room().Sheets, 6).Audit(ledger);
            if (lost.Missing != 3 || lost.Duplicated != 0 || lost.Unissued != 0)
            { Fail("S3", "a lost binder reported " + lost); return; }

            // Doubled: the same sheet filed in two binders.
            var doubled = new List<BinderRecord>(Room().Binders);
            doubled[2] = new BinderRecord(3, Seed, "Isle of Two Words",
                                          new List<SheetId> { Id(5), Id(1, Office.Garrison), Id(1) },
                                          PaperWhere.Floor, null, -1, default(PaperPose));
            RoomAudit twice = new RoomSnapshot(doubled, Room().Sheets, 6).Audit(ledger);
            if (twice.Duplicated != 1) { Fail("S3", "the same sheet in two binders reported " + twice); return; }

            // Restocked: paper the archive never handed out.
            var stocked = new List<LooseSheetRecord>(Room().Sheets);
            stocked.Add(new LooseSheetRecord(Id(9), PaperWhere.Floor, default(PaperPose)));
            RoomAudit invented = new RoomSnapshot(Room().Binders, stocked, 6).Audit(ledger);
            if (invented.Unissued != 1) { Fail("S3", "a sheet that was never issued reported " + invented); return; }

            Pass("S3", "lost, doubled and never-issued paper are each counted and named");
        }
    }
}
