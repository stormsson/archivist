using System;
using System.Collections.Generic;
using System.Globalization;
using Archivist.Building.Table;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The archive file, as JSON (C9.5). One document holding the ledger, every board and the
    /// room, because per-table files desync from the ledger and C9.1's invariant — no board and
    /// no binder may name a sheet the ledger never issued — is only cheap to hold if all of it is
    /// written together.
    ///
    /// <para><b>Three sections, and the order is the argument</b> (C9.1): <c>ledger</c>, then
    /// <c>boards</c>, then <c>room</c>. Each depends only on what came before it, so the document
    /// reads in the order the load happens.</para>
    ///
    /// <para><b>Order is content.</b> Islands are listed in the order the archive met them and
    /// sheets in the order they were issued (<see cref="SheetLedgerStore"/>); placements in lay
    /// order, which is draw order (§3.3, C4.7); a group's members in join order and a binder's
    /// contents in filing order. Arrays keep all of that for free and are read back top to
    /// bottom.</para>
    ///
    /// <para><b>A damaged record costs that record.</b> Text that will not parse is refused whole
    /// — it is not this format, and guessing at the rest of it would be guessing at poses — but a
    /// single entry missing a field or naming an unknown office is dropped with a warning and the
    /// rest of the save survives. A file that cannot be read at all costs the player everything;
    /// one bad entry costs them one drag.</para>
    ///
    /// <para><b>Culture cannot reach it.</b> <see cref="Json"/> writes and parses every number
    /// through the invariant culture and round-trips doubles with <c>R</c>: a board saved on a
    /// machine that writes <c>0,5</c> and read on one that does not would put paper somewhere
    /// nobody left it.</para>
    ///
    /// <para><b>Enums by name</b>, so that appending an <see cref="Office"/> cannot re-label every
    /// sheet in an old file. An unknown name drops that record with a warning.</para>
    ///
    /// <para>Deliberately free of UnityEngine, like the stores it serves — hence
    /// <see cref="Json"/> rather than <c>JsonUtility</c>, which also keeps the file a format with
    /// its own compatibility story instead of a mirror of whatever the classes look like today.
    /// </para>
    /// </summary>
    public static class ArchiveFormat
    {
        /// <summary>The format's version, the <c>archive</c> member of the root object. A reader
        /// that does not recognise it refuses the whole document rather than half-reading it.
        /// </summary>
        public const int Version = 1;

        /// <summary>One island's row of the ledger: what it is, and what has come out of it.
        /// Name and total are memos of pure functions (R1.11), saved so that listing thirty
        /// islands does not cost thirty generations.</summary>
        public sealed class LedgerIsland
        {
            public ulong Seed;
            public int Index = -1;
            public string Name;
            public int TotalSheets;
            public readonly List<SheetId> Issued = new List<SheetId>();
        }

        /// <summary>A file, read. Warnings are the records that did not survive; they are the
        /// caller's to log, because this class may not.</summary>
        public sealed class Contents
        {
            public readonly List<LedgerIsland> Islands = new List<LedgerIsland>();
            public readonly List<BoardSnapshot> Boards = new List<BoardSnapshot>();
            public readonly List<string> Warnings = new List<string>();

            /// <summary>Every piece of paper the file names.</summary>
            public readonly List<BinderRecord> Binders = new List<BinderRecord>();
            public readonly List<LooseSheetRecord> Sheets = new List<LooseSheetRecord>();
            public int NextBinderNumber = 1;

            /// <summary>The room as one value. A fresh object each call, over the live lists.</summary>
            public RoomSnapshot Room
            {
                get { return new RoomSnapshot(Binders, Sheets, NextBinderNumber); }
            }

            /// <summary>False when the text was not this format at all — as distinct from a file
            /// with a bad record in it, which is readable and warned about.</summary>
            public bool Readable = true;
        }

        // ---- writing -----------------------------------------------------------------------

        /// <summary>The whole archive as one JSON document.</summary>
        public static string Write(SheetLedgerStore ledger, IReadOnlyList<BoardSnapshot> boards,
                                   RoomSnapshot room)
        {
            var json = new Json.Writer();

            json.OpenObject();
            json.Field("archive", Version);

            json.Name("ledger").OpenArray();
            if (ledger != null)
            {
                IReadOnlyList<ulong> islands = ledger.KnownIslands;
                for (int i = 0; i < islands.Count; i++)
                {
                    ulong seed = islands[i];

                    IslandHolding holding;
                    if (!ledger.TryGetHolding(seed, out holding)) continue;

                    json.OpenObject();
                    json.Field("seed", Seed(seed));
                    json.Field("index", holding.Index);
                    json.Field("sheets", holding.Total);
                    json.Field("name", holding.Name);

                    json.Name("issued").OpenArray();
                    IReadOnlyList<SheetId> issued = ledger.IssuedSheets(seed);
                    for (int s = 0; s < issued.Count; s++) json.Value(Key(issued[s]));
                    json.CloseArray();

                    json.CloseObject();
                }
            }
            json.CloseArray();

            json.Name("boards").OpenArray();
            for (int b = 0; boards != null && b < boards.Count; b++)
            {
                BoardSnapshot board = boards[b];
                if (board == null || !IsWritableId(board.TableId)) continue;

                json.OpenObject();
                json.Field("table", board.TableId);
                json.Field("seed", Seed(board.IslandSeed));
                json.Field("nextGroup", board.NextGroupId);

                json.Name("groups").OpenArray();
                for (int g = 0; g < board.Groups.Count; g++)
                {
                    GroupRecord group = board.Groups[g];

                    json.OpenObject();
                    json.Field("id", group.GroupId);
                    json.Field("office", group.Office.ToString());
                    json.Field("whole", group.WholeIsland);
                    json.Field("onTable", group.OnTable);
                    json.Field("rotation", group.RotationDeg);
                    json.Field("x", group.OffsetX);
                    json.Field("y", group.OffsetY);

                    json.Name("members").OpenArray();
                    for (int m = 0; group.Members != null && m < group.Members.Count; m++)
                        json.Value(Key(group.Members[m]));
                    json.CloseArray();

                    json.CloseObject();
                }
                json.CloseArray();

                json.Name("placed").OpenArray();
                for (int p = 0; p < board.Placed.Count; p++)
                {
                    BoardSnapshot.Entry entry = board.Placed[p];
                    Table.Placement placement = entry.Placement;

                    // Three kinds, and only the loose one carries a pose (C4.6, G4.1). This is
                    // where R1.11 is visible in the file: a nine-sheet assembly is nine names and
                    // one frame, and a seated sheet is a name.
                    json.OpenObject();
                    json.Field("sheet", Key(entry.Id));

                    if (placement.Seated) json.Field("seated", true);
                    else if (placement.Grouped) json.Field("group", placement.GroupId);
                    else
                    {
                        json.Field("x", placement.GroundX);
                        json.Field("y", placement.GroundY);
                        json.Field("rotation", placement.RotationDeg);
                    }
                    json.CloseObject();
                }
                json.CloseArray();

                json.CloseObject();
            }
            json.CloseArray();

            // The room last, because it is the half that says where the ledger's paper actually
            // is: a reader that stops before it has an archive that over-claims, which is the
            // state this section exists to end.
            json.Name("room").OpenObject();
            json.Field("nextBinder", room != null ? room.NextBinderNumber : 1);

            json.Name("binders").OpenArray();
            for (int b = 0; room != null && b < room.Binders.Count; b++)
            {
                BinderRecord binder = room.Binders[b];
                if (binder == null) continue;

                json.OpenObject();
                json.Field("number", binder.Number);
                json.Field("seed", Seed(binder.IslandSeed));
                json.Field("island", binder.IslandName);
                WriteWhere(json, binder.Where, binder.TableId, binder.Anchor, binder.Pose);

                json.Name("holds").OpenArray();
                for (int c = 0; c < binder.Contents.Count; c++) json.Value(Key(binder.Contents[c]));
                json.CloseArray();

                json.CloseObject();
            }
            json.CloseArray();

            json.Name("sheets").OpenArray();
            for (int i = 0; room != null && i < room.Sheets.Count; i++)
            {
                LooseSheetRecord sheet = room.Sheets[i];
                if (sheet == null) continue;

                json.OpenObject();
                json.Field("sheet", Key(sheet.Id));
                WriteWhere(json, sheet.Where, null, -1, sheet.Pose);
                json.CloseObject();
            }
            json.CloseArray();

            json.CloseObject();       // room
            json.CloseObject();       // the document

            return json.ToString();
        }

        /// <summary>Where one piece of paper is: the three places of <see cref="PaperWhere"/>
        /// and, for the two that have one, the pose it is standing in. In the hands there is no
        /// pose to write — the hands are the place.</summary>
        static void WriteWhere(Json.Writer json, PaperWhere where, string tableId, int anchor,
                               PaperPose pose)
        {
            if (where == PaperWhere.Hands)
            {
                json.Field("where", "hands");
                return;
            }

            if (where == PaperWhere.Table && IsWritableId(tableId))
            {
                json.Field("where", "table");
                json.Field("table", tableId);
                json.Field("anchor", anchor);
            }
            else json.Field("where", "floor");

            json.Name("pose").OpenObject();
            json.Field("x", pose.X);
            json.Field("y", pose.Y);
            json.Field("z", pose.Z);
            json.Field("rx", pose.RotX);
            json.Field("ry", pose.RotY);
            json.Field("rz", pose.RotZ);
            json.CloseObject();
        }

        // ---- reading -----------------------------------------------------------------------

        /// <summary>
        /// A document, parsed. Never throws and never returns null: text that is not this format
        /// comes back empty with <see cref="Contents.Readable"/> false, which the caller treats
        /// as "no save" — the one behaviour that cannot make a broken file worse.
        /// </summary>
        public static Contents Read(string text)
        {
            var contents = new Contents();

            Json.Value root;
            string error;
            if (!Json.TryParse(text, out root, out error) || !root.IsObject)
            {
                contents.Warnings.Add("Not readable as JSON — not loaded: " +
                                      (error ?? "the document is not an object"));
                contents.Readable = false;
                return contents;
            }

            Json.Value version = root["archive"];
            if (!version.IsNumber || version.AsInt(0) != Version)
            {
                contents.Warnings.Add("Archive is version " +
                                      (version.IsNumber
                                          ? version.AsInt(0).ToString(CultureInfo.InvariantCulture)
                                          : "unstated") +
                                      ", this build reads " + Version + " — not loaded.");
                contents.Readable = false;
                return contents;
            }

            ReadLedger(root["ledger"], contents);
            ReadBoards(root["boards"], contents);
            ReadRoom(root["room"], contents);

            return contents;
        }

        static void ReadLedger(Json.Value ledger, Contents contents)
        {
            IReadOnlyList<Json.Value> islands = ledger.Items;
            for (int i = 0; i < islands.Count; i++)
            {
                Json.Value entry = islands[i];

                ulong seed;
                if (!TrySeed(entry["seed"].AsString(null), out seed))
                {
                    Complain(contents, "ledger[" + i + "]", "unreadable island");
                    continue;
                }

                var island = new LedgerIsland
                {
                    Seed = seed,
                    Index = entry["index"].AsInt(-1),
                    TotalSheets = entry["sheets"].AsInt(0),
                    Name = entry["name"].AsString(null),
                };

                IReadOnlyList<Json.Value> issued = entry["issued"].Items;
                for (int s = 0; s < issued.Count; s++)
                {
                    SheetId id;
                    if (!TryKey(issued[s].AsString(null), out id))
                    {
                        Complain(contents, "ledger[" + i + "].issued[" + s + "]", "bad sheet");
                        continue;
                    }
                    if (id.IslandSeed != seed)
                    {
                        Complain(contents, "ledger[" + i + "].issued[" + s + "]", "wrong island");
                        continue;
                    }
                    island.Issued.Add(id);
                }

                contents.Islands.Add(island);
            }
        }

        static void ReadBoards(Json.Value boards, Contents contents)
        {
            IReadOnlyList<Json.Value> all = boards.Items;
            for (int b = 0; b < all.Count; b++)
            {
                Json.Value entry = all[b];
                string where = "boards[" + b + "]";

                string tableId = entry["table"].AsString(null);
                ulong seed;
                if (!IsWritableId(tableId) || !TrySeed(entry["seed"].AsString(null), out seed))
                {
                    Complain(contents, where, "unreadable board");
                    continue;
                }

                var groups = new List<GroupRecord>();
                IReadOnlyList<Json.Value> groupEntries = entry["groups"].Items;
                for (int g = 0; g < groupEntries.Count; g++)
                {
                    Json.Value group = groupEntries[g];
                    string at = where + ".groups[" + g + "]";

                    Office office;
                    if (!group["id"].IsNumber) { Complain(contents, at, "bad group id"); continue; }
                    if (!Enum.TryParse(group["office"].AsString(null), out office))
                    { Complain(contents, at, "unknown office"); continue; }

                    var members = new List<SheetId>();
                    IReadOnlyList<Json.Value> memberEntries = group["members"].Items;
                    for (int m = 0; m < memberEntries.Count; m++)
                    {
                        SheetId id;
                        if (!TryKey(memberEntries[m].AsString(null), out id))
                        {
                            Complain(contents, at + ".members[" + m + "]", "bad sheet");
                            continue;
                        }
                        members.Add(id);
                    }

                    groups.Add(new GroupRecord(group["id"].AsInt(0),
                                               group["rotation"].AsDouble(0.0),
                                               group["x"].AsDouble(0.0),
                                               group["y"].AsDouble(0.0),
                                               office,
                                               group["whole"].AsBool(false),
                                               group["onTable"].AsBool(true),
                                               members.ToArray()));
                }

                var placed = new List<BoardSnapshot.Entry>();
                IReadOnlyList<Json.Value> placedEntries = entry["placed"].Items;
                for (int p = 0; p < placedEntries.Count; p++)
                {
                    Json.Value laid = placedEntries[p];
                    string at = where + ".placed[" + p + "]";

                    SheetId id;
                    if (!TryKey(laid["sheet"].AsString(null), out id))
                    {
                        Complain(contents, at, "bad sheet");
                        continue;
                    }

                    if (laid["seated"].AsBool(false))
                    {
                        placed.Add(new BoardSnapshot.Entry(id, Table.Placement.SeatedAtTruth()));
                        continue;
                    }

                    if (laid["group"].IsNumber)
                    {
                        placed.Add(new BoardSnapshot.Entry(
                            id, Table.Placement.InGroup(laid["group"].AsInt(0))));
                        continue;
                    }

                    // A pose, and all three of it. A placement missing one is a sheet that would
                    // silently move — which is exactly what A6 invites by editing the file, so it
                    // is refused rather than defaulted.
                    if (!laid["x"].IsNumber || !laid["y"].IsNumber || !laid["rotation"].IsNumber)
                    {
                        Complain(contents, at, "a laid sheet with no pose");
                        continue;
                    }

                    placed.Add(new BoardSnapshot.Entry(id, Table.Placement.Laid(
                        laid["x"].AsDouble(0.0),
                        laid["y"].AsDouble(0.0),
                        laid["rotation"].AsDouble(0.0))));
                }

                contents.Boards.Add(new BoardSnapshot(tableId, seed, placed, groups,
                                                      entry["nextGroup"].AsInt(1)));
            }
        }

        static void ReadRoom(Json.Value room, Contents contents)
        {
            if (!room.IsObject) return;

            contents.NextBinderNumber = room["nextBinder"].AsInt(1);

            IReadOnlyList<Json.Value> binders = room["binders"].Items;
            for (int b = 0; b < binders.Count; b++)
            {
                Json.Value entry = binders[b];
                string at = "room.binders[" + b + "]";

                ulong seed;
                if (!entry["number"].IsNumber || !TrySeed(entry["seed"].AsString(null), out seed))
                {
                    Complain(contents, at, "unreadable binder");
                    continue;
                }

                PaperWhere where;
                string tableId;
                int anchor;
                PaperPose pose;
                if (!TryWhere(entry, out where, out tableId, out anchor, out pose))
                {
                    Complain(contents, at, "a binder that does not say where it is");
                    continue;
                }

                var holding = new List<SheetId>();
                IReadOnlyList<Json.Value> holds = entry["holds"].Items;
                for (int h = 0; h < holds.Count; h++)
                {
                    SheetId id;
                    if (!TryKey(holds[h].AsString(null), out id))
                    {
                        Complain(contents, at + ".holds[" + h + "]", "bad sheet");
                        continue;
                    }
                    if (id.IslandSeed != seed)
                    {
                        Complain(contents, at + ".holds[" + h + "]", "wrong island");
                        continue;
                    }
                    holding.Add(id);
                }

                contents.Binders.Add(new BinderRecord(entry["number"].AsInt(0), seed,
                                                      entry["island"].AsString(null), holding,
                                                      where, tableId, anchor, pose));
            }

            IReadOnlyList<Json.Value> sheets = room["sheets"].Items;
            for (int i = 0; i < sheets.Count; i++)
            {
                Json.Value entry = sheets[i];
                string at = "room.sheets[" + i + "]";

                SheetId id;
                if (!TryKey(entry["sheet"].AsString(null), out id))
                {
                    Complain(contents, at, "bad sheet");
                    continue;
                }

                PaperWhere where;
                string tableId;
                int anchor;
                PaperPose pose;
                if (!TryWhere(entry, out where, out tableId, out anchor, out pose))
                {
                    Complain(contents, at, "a sheet that does not say where it is");
                    continue;
                }

                // A loose sheet on a table is not a state the room can be in: filing is what a
                // sheet does at a table, and it consumes the paper (D-B2).
                if (where == PaperWhere.Table)
                {
                    Complain(contents, at, "a loose sheet cannot be on a table");
                    continue;
                }

                contents.Sheets.Add(new LooseSheetRecord(id, where, pose));
            }

            // The counter cannot be behind the binders that exist, whatever the file says: two
            // Binder_4s in one room is a label that stops naming one thing.
            for (int i = 0; i < contents.Binders.Count; i++)
                if (contents.Binders[i].Number >= contents.NextBinderNumber)
                    contents.NextBinderNumber = contents.Binders[i].Number + 1;
        }

        /// <summary>The inverse of <see cref="WriteWhere"/>. A record naming a table with no id or
        /// no anchor is refused rather than dropped to the floor: a binder wrong by a metre is a
        /// binder somebody has to go and find.</summary>
        static bool TryWhere(Json.Value entry, out PaperWhere where, out string tableId,
                             out int anchor, out PaperPose pose)
        {
            where = PaperWhere.Floor;
            tableId = null;
            anchor = -1;
            pose = default(PaperPose);

            string place = entry["where"].AsString(null);
            if (place == "hands")
            {
                where = PaperWhere.Hands;
                return true;
            }

            if (place == "table")
            {
                tableId = entry["table"].AsString(null);
                if (!IsWritableId(tableId) || !entry["anchor"].IsNumber) return false;

                where = PaperWhere.Table;
                anchor = entry["anchor"].AsInt(-1);
            }
            else if (place != "floor") return false;

            Json.Value at = entry["pose"];
            if (!at.IsObject) return false;

            pose = new PaperPose(at["x"].AsDouble(0.0), at["y"].AsDouble(0.0), at["z"].AsDouble(0.0),
                                 at["rx"].AsDouble(0.0), at["ry"].AsDouble(0.0), at["rz"].AsDouble(0.0));
            return true;
        }

        static void Complain(Contents contents, string at, string why)
        {
            contents.Warnings.Add(at + ": " + why + ", skipped.");
        }

        // ---- fields ------------------------------------------------------------------------

        /// <summary>A sheet as one string, so a placement stays a flat object and a member list a
        /// list of names: seed, office, whether it is the whole-island survey, number. The four
        /// fields of <see cref="SheetId"/> and nothing derived.</summary>
        static string Key(SheetId id)
        {
            return Seed(id.IslandSeed) + "/" + id.Office + "/" + (id.WholeIsland ? "whole" : "part")
                   + "/" + id.Number.ToString(CultureInfo.InvariantCulture);
        }

        static bool TryKey(string value, out SheetId id)
        {
            id = default(SheetId);
            if (string.IsNullOrEmpty(value)) return false;

            string[] field = value.Split('/');
            if (field.Length != 4) return false;

            ulong seed;
            if (!TrySeed(field[0], out seed)) return false;

            Office office;
            if (!Enum.TryParse(field[1], out office)) return false;

            int number;
            if (!int.TryParse(field[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                return false;

            id = new SheetId(seed, office, field[2] == "whole", number);
            return true;
        }

        static string Seed(ulong seed) { return seed.ToString("X16", CultureInfo.InvariantCulture); }

        static bool TrySeed(string field, out ulong seed)
        {
            seed = 0UL;
            return !string.IsNullOrEmpty(field)
                && ulong.TryParse(field, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed);
        }

        /// <summary>A table id with whitespace in it is not one §4.1 minted. Refusing it here
        /// loses that board rather than the file.</summary>
        static bool IsWritableId(string tableId)
        {
            if (string.IsNullOrEmpty(tableId)) return false;

            for (int i = 0; i < tableId.Length; i++)
                if (char.IsWhiteSpace(tableId[i])) return false;

            return true;
        }
    }
}
