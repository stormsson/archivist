using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Archivist.Building.Table;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The save file, as a thing in the scene (§9). One JSON document holds the ledger, every
    /// board and the room, and is written in one move (C9.5), because per-table files desync from the ledger and
    /// C9.1 — no board may name a sheet the ledger never issued — is only cheap to hold if both
    /// are written together.
    ///
    /// <para><b>Ledger first, boards second</b>, on the way out and on the way in (C9.1). A
    /// board entry naming an unissued sheet is dropped with a warning; under the ordering that
    /// produces these files it cannot happen, and it is checked anyway because a save outlives
    /// the reasoning that made it safe.</para>
    ///
    /// <para><b>Event-driven, never timed</b> (C9.4). <see cref="Note"/> is called at C9.2's
    /// points — the table closing, and <i>any</i> sheet or assembly released from a drag,
    /// including one released back into the cabinet — plus the points groups added (G15.2):
    /// fusing, parking, retrieving, and a binder leaving a table. Board state is a few
    /// dozen structs; the write is a couple of kilobytes and costs less than the frame it
    /// happens in. The third point is what satisfies T6, "the player may stop at any moment with
    /// nothing left hanging": an unseated sheet is a resting state (R6.5), not unfinished work,
    /// so a deliberate near-miss must not be lost to an unclean exit.</para>
    ///
    /// <para><b>Written through a temp file and moved into place</b>, so a crash mid-write
    /// cannot leave half an archive where the whole one was. The reader treats an unreadable
    /// file as no save at all rather than as an empty one, which is the one behaviour that
    /// cannot make a broken file worse.</para>
    ///
    /// <para><b>It finds itself.</b> Nothing in the scene has to be wired to it and nothing has
    /// to remember to create it: <see cref="Wake"/> runs after the scene loads, before any crate
    /// has been opened or any table touched, which is the only moment at which loading over the
    /// live stores is safe. A scene that already has one keeps it and its inspector settings.
    /// </para>
    ///
    /// <para><b>Three sections, and the order is the argument</b> (C9.1): the ledger, then the
    /// boards, then the room. Each depends only on what came before it — a board may not name a
    /// sheet the ledger never issued, and a binder may not hold one either — so a reader that
    /// stops early is short of paper, never short of the record that justifies it. The room is
    /// what makes the ledger mean something: it says <i>where</i> each issued sheet went, and
    /// without it a reloaded archive claimed paper that did not exist. See
    /// <c>docs/UI/cartography_table/persistence.md</c>.</para>
    /// </summary>
    public sealed class Archive : MonoBehaviour
    {
        [Tooltip("Under Application.persistentDataPath. One file, ledger and boards together " +
                 "(C9.5).")]
        [SerializeField] string fileName = "archive.json";

        [Tooltip("Off to play with a save on disk without writing to it — a way to inspect a " +
                 "file rather than a game rule. The file is still read.")]
        [SerializeField] bool saving = true;

        [Tooltip("Logs every write and what it held.")]
        [SerializeField] bool logSaves;

        SheetLedger ledger;
        BoardView board;
        RoomPaper paper;

        bool loaded;

        static Archive scene;

        /// <summary>The scene's archive, made if the scene has none — and only in play mode,
        /// because an editor tool that silently added a GameObject to somebody's scene would be
        /// a change they did not make.</summary>
        public static Archive InScene
        {
            get
            {
                if (scene != null) return scene;

                scene = FindFirstObjectByType<Archive>(FindObjectsInactive.Include);
                if (scene != null || !Application.isPlaying) return scene;

                var go = new GameObject("Archive");
                scene = go.AddComponent<Archive>();
                return scene;
            }
        }

        /// <summary>Where the file is. Worth logging once: on a desktop build it is not
        /// anywhere obvious, and every question about a save starts with reading it.</summary>
        public string Path
        {
            get { return System.IO.Path.Combine(Application.persistentDataPath, fileName); }
        }

        /// <summary>Every board this save covers, or null in a scene with no board view — a
        /// generator scene, say, where the ledger is the only thing worth keeping.</summary>
        public BoardStore Boards { get { return Board != null ? Board.Boards : null; } }

        /// <summary>
        /// Something worth keeping happened. Cheap enough to call from an interaction handler
        /// and safe in a scene with no archive, which is what lets the call sites say what
        /// happened rather than test whether anyone is listening.
        /// </summary>
        public static void Note()
        {
            if (!Application.isPlaying) return;

            Archive archive = InScene;
            if (archive != null) archive.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Wake()
        {
            scene = null;

            Archive archive = InScene;
            if (archive != null) archive.Load();
        }

        void Awake()
        {
            if (scene == null) scene = this;
        }

        void OnDestroy()
        {
            if (scene == this) scene = null;
        }

        /// <summary>The last save point, and the one nothing else can stand in for: quitting is
        /// how a session usually ends.</summary>
        void OnApplicationQuit()
        {
            Save();
        }

        // ---- writing -----------------------------------------------------------------------

        /// <summary>
        /// The whole archive, atomically. Silent about having nothing to save — a fresh game
        /// notes its first island the moment a crate opens, and a log line per empty write would
        /// bury the ones that matter.
        /// </summary>
        public void Save()
        {
            if (!saving || !Application.isPlaying) return;

            // Never mid-restore: the room's floor paper arrives over several frames (each is a
            // render), and a save taken between them would record the sheets that had landed and
            // forget the rest — a slow load turned into a lost one.
            if (Paper != null && Paper.Restoring) return;

            SheetLedgerStore store = Ledger != null ? Ledger.Store : null;
            BoardStore boards = Boards;
            RoomSnapshot room = Paper != null ? Paper.Capture() : null;

            var snapshots = new List<BoardSnapshot>();
            if (boards != null)
            {
                IReadOnlyList<string> tables = boards.KnownTables;
                for (int i = 0; i < tables.Count; i++)
                {
                    BoardSnapshot snapshot = boards.Snapshot(tables[i]);
                    if (snapshot != null) snapshots.Add(snapshot);
                }
            }

            if (store == null && snapshots.Count == 0 && room == null) return;

            string text = ArchiveFormat.Write(store, snapshots, room);
            string path = Path;

            try
            {
                string temp = path + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));

                // Replace, not delete-then-move: the point of the temp file is that there is
                // never a moment with no archive on disk.
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException e)
            {
                Debug.LogError("[Archive] Could not write " + path + ": " + e.Message, this);
                return;
            }
            catch (System.UnauthorizedAccessException e)
            {
                Debug.LogError("[Archive] Could not write " + path + ": " + e.Message, this);
                return;
            }

            if (logSaves)
                Debug.Log("[Archive] saved " + snapshots.Count + " board(s) and " +
                          (room != null ? room.ToString() : "no room") + " to " + path, this);
        }

        // ---- reading -----------------------------------------------------------------------

        /// <summary>
        /// The file into the live stores, once per session. Called before anything has been
        /// issued or laid down; calling it later would overwrite the game in progress with the
        /// game on disk, so the second call does nothing and says so.
        /// </summary>
        public void Load()
        {
            if (loaded)
            {
                Debug.LogWarning("[Archive] Already loaded — refusing to load over live state.", this);
                return;
            }
            loaded = true;

            string path = Path;
            if (!File.Exists(path)) return;

            string text;
            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogError("[Archive] Could not read " + path + ": " + e.Message, this);
                return;
            }

            ArchiveFormat.Contents contents = ArchiveFormat.Read(text);

            for (int i = 0; i < contents.Warnings.Count; i++)
                Debug.LogWarning("[Archive] " + contents.Warnings[i], this);

            if (!contents.Readable) return;

            RestoreLedger(contents);
            RestoreBoards(contents);
            RestoreRoom(contents);

            if (logSaves)
                Debug.Log("[Archive] loaded " + contents.Islands.Count + " island(s), " +
                          contents.Boards.Count + " board(s) and " + contents.Room +
                          " from " + path, this);
        }

        /// <summary>C9.1's first half. Replayed in the file's order, which is the order the
        /// archive met the islands and issued the sheets — an order the collection screen
        /// reads, so a load that shuffled it would be visible.</summary>
        void RestoreLedger(ArchiveFormat.Contents contents)
        {
            SheetLedgerStore store = Ledger != null ? Ledger.Store : null;
            if (store == null)
            {
                if (contents.Islands.Count > 0)
                    Debug.LogWarning("[Archive] No SheetLedger in the scene — " +
                                     contents.Islands.Count + " island(s) not restored.", this);
                return;
            }

            for (int i = 0; i < contents.Islands.Count; i++)
            {
                ArchiveFormat.LedgerIsland island = contents.Islands[i];

                store.Record(island.Seed, island.Index);
                store.Describe(island.Seed, island.Name, island.TotalSheets);

                for (int s = 0; s < island.Issued.Count; s++) store.MarkIssued(island.Issued[s]);
            }
        }

        /// <summary>
        /// C9.1's second half, and the check it exists for: <b>a board entry naming a sheet the
        /// ledger does not have issued is dropped</b>, before <see cref="BoardStore"/> ever sees
        /// it. A group that then falls below two members dissolves inside the restore, which is
        /// where the survivor's fate is decided — see <see cref="BoardStore.Restore"/>.
        /// </summary>
        void RestoreBoards(ArchiveFormat.Contents contents)
        {
            BoardStore boards = Boards;
            if (boards == null)
            {
                if (contents.Boards.Count > 0)
                    Debug.LogWarning("[Archive] No BoardView in the scene — " +
                                     contents.Boards.Count + " board(s) not restored.", this);
                return;
            }

            SheetLedgerStore store = Ledger != null ? Ledger.Store : null;

            for (int i = 0; i < contents.Boards.Count; i++)
            {
                BoardSnapshot snapshot = Issued(contents.Boards[i], store);
                BoardRestoreReport report = boards.Restore(snapshot);

                if (!report.Clean)
                    Debug.LogWarning("[Archive] Board " + snapshot.TableId + ": " + report, this);
            }
        }

        /// <summary>
        /// C9.1's third part, and the one that makes the ledger true: the paper goes back into
        /// the room. <see cref="RoomPaper"/> performs the same issuance check on what a binder
        /// claims to hold, so the file cannot restock the archive with sheets it never issued.
        /// </summary>
        void RestoreRoom(ArchiveFormat.Contents contents)
        {
            RoomSnapshot room = contents.Room;
            if (room.Empty) return;

            if (Paper == null)
            {
                Debug.LogWarning("[Archive] No RoomPaper in the scene — " + room +
                                 " not restored.", this);
                return;
            }

            Paper.Restore(room, Ledger != null ? Ledger.Store : null);

            // Said out loud rather than repaired: all three counts mean the paper and the ledger
            // disagree, and neither this class nor the file can say which of them is right.
            RoomAudit audit = room.Audit(Ledger != null ? Ledger.Store : null);
            if (!audit.Clean)
                Debug.LogWarning("[Archive] The room does not account for the ledger: " + audit, this);
        }

        /// <summary>
        /// The same board with every sheet the ledger has not issued taken out of it. A copy
        /// rather than an edit, because <see cref="BoardSnapshot"/> is a value and the file's
        /// own reading of itself is worth keeping intact for the warning above.
        ///
        /// <para>With no ledger to ask, nothing is dropped: a scene without one cannot tell an
        /// unissued sheet from an issued one, and refusing every sheet would turn a missing
        /// component into a wiped board.</para>
        /// </summary>
        static BoardSnapshot Issued(BoardSnapshot board, SheetLedgerStore ledger)
        {
            if (ledger == null || board == null) return board;

            var placed = new List<BoardSnapshot.Entry>(board.Placed.Count);
            for (int i = 0; i < board.Placed.Count; i++)
            {
                BoardSnapshot.Entry entry = board.Placed[i];
                if (ledger.IsIssued(entry.Id)) placed.Add(entry);
            }

            var groups = new List<GroupRecord>(board.Groups.Count);
            for (int i = 0; i < board.Groups.Count; i++)
            {
                GroupRecord group = board.Groups[i];

                var members = new List<SheetId>();
                for (int m = 0; group.Members != null && m < group.Members.Count; m++)
                {
                    SheetId id = group.Members[m];
                    if (ledger.IsIssued(id)) members.Add(id);
                }

                groups.Add(new GroupRecord(group.GroupId, group.RotationDeg, group.OffsetX,
                                           group.OffsetY, group.Office, group.WholeIsland,
                                           group.OnTable, members.ToArray()));
            }

            return new BoardSnapshot(board.TableId, board.IslandSeed, placed, groups,
                                     board.NextGroupId);
        }

        // ---- wiring ------------------------------------------------------------------------

        // Resolved rather than required, as TableSession does it: every reference here is a
        // scene singleton and findable, so an archive that nobody dragged anything onto works.
        // Inactive included — the board view is off until a table is opened (§5.1).

        SheetLedger Ledger
        {
            get
            {
                if (ledger == null) ledger = FindFirstObjectByType<SheetLedger>(FindObjectsInactive.Include);
                return ledger;
            }
        }

        BoardView Board
        {
            get
            {
                if (board == null) board = FindFirstObjectByType<BoardView>(FindObjectsInactive.Include);
                return board;
            }
        }

        /// <summary>The room's paper. Made if the scene has none, for the same reason this
        /// component is: it is a piece of the save, not a thing a designer places.</summary>
        RoomPaper Paper
        {
            get
            {
                if (paper != null) return paper;

                paper = FindFirstObjectByType<RoomPaper>(FindObjectsInactive.Include);
                if (paper == null && Application.isPlaying) paper = gameObject.AddComponent<RoomPaper>();
                return paper;
            }
        }
    }
}
