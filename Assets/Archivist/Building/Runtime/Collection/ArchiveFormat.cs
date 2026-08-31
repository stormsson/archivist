using System;
using System.Collections.Generic;
using System.Globalization;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The archive file, as JSON (C9.5). One document holding the ledger and the room, because
    /// separate files desync and C9.1's invariant — no binder may name a sheet the ledger never
    /// issued — is only cheap to hold if both are written together.
    ///
    /// <para><b>Two sections, and the order is the argument</b> (C9.1): <c>ledger</c>, then
    /// <c>room</c>. The second depends on the first, so the document reads in the order the load
    /// happens. <b>There is no board section</b> (Q4.7): a board is derived from the binders on
    /// a table every time it opens, so there is nothing about it to write down.</para>
    ///
    /// <para><b>Order is content.</b> Islands are listed in the order the archive met them and
    /// sheets in the order they were issued (<see cref="SheetLedgerStore"/>); a binder's
    /// contents in filing order. Arrays keep that for free and are read back top to
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
        /// <summary>
        /// 4. Version 1 carried a <c>boards</c> section and sheet numbers that were a
        /// cull-dependent 1..N; version 2 carried a binder's office; version 3 knew only three
        /// places a binder could be. A mismatched version makes the file unreadable, which is the
        /// correct outcome — a v1 save names plates that no longer exist, and a v2 save describes
        /// a binder model that does not.
        ///
        /// <para>The office is gone because a binder no longer has one (Q3.1): it names an
        /// island and holds whatever offices' plates are in it, which is read off the contents.
        /// The field existed for the one case that could not be inferred — an empty binder — and
        /// empty binders do not exist (F-R19.2).</para>
        ///
        /// <para>The fourth place is a shelf (R4.2). A v3 save cannot describe one, so every
        /// binder in it would come back on the floor with no warning that anything was lost —
        /// refusing it outright says so once, loudly.</para>
        /// </summary>
        public const int Version = 4;

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
        public static string Write(SheetLedgerStore ledger, RoomSnapshot room)
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
                WriteWhere(json, binder);

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
                WriteFloorOrHands(json, sheet.Where, sheet.Pose);
                json.CloseObject();
            }
            json.CloseArray();

            json.CloseObject();       // room
            json.CloseObject();       // the document

            return json.ToString();
        }

        /// <summary>Where one binder is: the four places of <see cref="PaperWhere"/> and, for the
        /// three that have one, the pose it is standing in. In the hands there is no pose to write
        /// — the hands are the place.
        ///
        /// <para>A furniture record with no id, or a shelf slot with no row and column, falls back
        /// to the floor. The pose is written either way, so a binder whose furniture cannot be
        /// named still comes back where it was standing rather than at the world origin.</para>
        /// </summary>
        static void WriteWhere(Json.Writer json, BinderRecord binder)
        {
            if (binder.Where == PaperWhere.Hands)
            {
                json.Field("where", "hands");
                return;
            }

            if (binder.Where == PaperWhere.Table && IsWritableId(binder.TableId))
            {
                json.Field("where", "table");
                json.Field("table", binder.TableId);
                json.Field("anchor", binder.Anchor);
            }
            else if (binder.Where == PaperWhere.Shelf && IsWritableId(binder.ShelfId)
                     && binder.Row >= 0 && binder.Column >= 0)
            {
                json.Field("where", "shelf");
                json.Field("shelf", binder.ShelfId);
                json.Field("row", binder.Row);
                json.Field("column", binder.Column);
            }
            else json.Field("where", "floor");

            WritePose(json, binder.Pose);
        }

        /// <summary>The same, for paper that has no furniture to be on: a loose sheet is on the
        /// floor or in the hands and nowhere else (D-B2).</summary>
        static void WriteFloorOrHands(Json.Writer json, PaperWhere where, PaperPose pose)
        {
            if (where == PaperWhere.Hands)
            {
                json.Field("where", "hands");
                return;
            }

            json.Field("where", "floor");
            WritePose(json, pose);
        }

        /// <summary>The pose, as its own object. Shared by both writers so a floor pose and a
        /// shelf pose cannot drift into different shapes.</summary>
        static void WritePose(Json.Writer json, PaperPose pose)
        {
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

                PaperPlace place;
                if (!TryWhere(entry, out place))
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
                                                      place.Where, place.TableId, place.Anchor,
                                                      place.Pose, place.ShelfId,
                                                      place.Row, place.Column));
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

                PaperPlace place;
                if (!TryWhere(entry, out place))
                {
                    Complain(contents, at, "a sheet that does not say where it is");
                    continue;
                }

                // A loose sheet on furniture is not a state the room can be in: filing is what a
                // sheet does at a table, and it consumes the paper (D-B2); a rack takes binders
                // and nothing else (R4.2).
                if (place.Where == PaperWhere.Table || place.Where == PaperWhere.Shelf)
                {
                    Complain(contents, at, "a loose sheet cannot be on furniture");
                    continue;
                }

                contents.Sheets.Add(new LooseSheetRecord(id, place.Where, place.Pose));
            }

            // The counter cannot be behind the binders that exist, whatever the file says: two
            // Binder_4s in one room is a label that stops naming one thing.
            for (int i = 0; i < contents.Binders.Count; i++)
                if (contents.Binders[i].Number >= contents.NextBinderNumber)
                    contents.NextBinderNumber = contents.Binders[i].Number + 1;
        }

        /// <summary>One record's place, as read. A struct rather than six <c>out</c> parameters:
        /// the two readers want all of it, and a signature nobody can call without a scratch pad
        /// is a signature that grows a seventh field wrong.</summary>
        struct PaperPlace
        {
            public PaperWhere Where;
            public string TableId;
            public int Anchor;
            public string ShelfId;
            public int Row;
            public int Column;
            public PaperPose Pose;
        }

        /// <summary>The inverse of <see cref="WriteWhere"/>. A record naming furniture with no id,
        /// no anchor or no slot is refused rather than dropped to the floor: a binder wrong by a
        /// metre is a binder somebody has to go and find.</summary>
        static bool TryWhere(Json.Value entry, out PaperPlace read)
        {
            read = new PaperPlace
            {
                Where = PaperWhere.Floor,
                TableId = null,
                Anchor = -1,
                ShelfId = null,
                Row = -1,
                Column = -1,
                Pose = default(PaperPose)
            };

            string place = entry["where"].AsString(null);
            if (place == "hands")
            {
                read.Where = PaperWhere.Hands;
                return true;
            }

            if (place == "table")
            {
                read.TableId = entry["table"].AsString(null);
                if (!IsWritableId(read.TableId) || !entry["anchor"].IsNumber) return false;

                read.Where = PaperWhere.Table;
                read.Anchor = entry["anchor"].AsInt(-1);
            }
            else if (place == "shelf")
            {
                read.ShelfId = entry["shelf"].AsString(null);
                if (!IsWritableId(read.ShelfId)) return false;
                if (!entry["row"].IsNumber || !entry["column"].IsNumber) return false;

                read.Where = PaperWhere.Shelf;
                read.Row = entry["row"].AsInt(-1);
                read.Column = entry["column"].AsInt(-1);
                if (read.Row < 0 || read.Column < 0) return false;
            }
            else if (place != "floor") return false;

            Json.Value at = entry["pose"];
            if (!at.IsObject) return false;

            read.Pose = new PaperPose(at["x"].AsDouble(0.0), at["y"].AsDouble(0.0), at["z"].AsDouble(0.0),
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
