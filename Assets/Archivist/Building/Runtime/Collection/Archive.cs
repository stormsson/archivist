using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Archivist.Building.Table;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The save file, as a thing in the scene (§9). One JSON document holds the ledger and the
    /// room, written in one move (C9.5), because separate files desync and C9.1 — nothing may
    /// name a sheet the ledger never issued — is only cheap to hold if both are written
    /// together.
    ///
    /// <para><b>The board is not in it</b> (Q4.7). A board is a view of the binders on a table,
    /// derived every time it opens, so there is no arrangement to lose and nothing a reopening
    /// would fail to reproduce. What is saved is what is <i>in</i> each binder and where each
    /// binder lies.</para>
    ///
    /// <para><b>Event-driven, never timed</b> (C9.4). <see cref="Note"/> is called when paper
    /// moves: a sheet filed into a binder, a binder placed or taken, a table closing. The write
    /// is a couple of kilobytes and costs less than the frame it happens in. That satisfies T6,
    /// "the player may stop at any moment with nothing left hanging".</para>
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
    /// <para><b>Two sections, and the order is the argument</b> (C9.1): the ledger, then the
    /// room. The second depends on the first — a binder may not hold a sheet the ledger never
    /// issued — so a reader that stops early is short of paper, never short of the record that
    /// justifies it. The room is what makes the ledger mean something: it says <i>where</i> each
    /// issued sheet went, and without it a reloaded archive claimed paper that did not
    /// exist.</para>
    /// </summary>
    public sealed class Archive : MonoBehaviour
    {
        /// <summary>The file, when nobody has said otherwise. Named here so a tool that has no
        /// <see cref="Archive"/> to ask — one that runs outside play mode — can still find
        /// it.</summary>
        public const string DefaultFileName = "archive.json";

        [Tooltip("Under Application.persistentDataPath. One file, ledger and room together " +
                 "(C9.5).")]
        [SerializeField] string fileName = DefaultFileName;

        [Tooltip("DEVELOPMENT. On: start from nothing. The save is not read and the file is " +
                 "deleted, so the archive is as empty as it was the first time anyone ran the " +
                 "game. Leave it on while the generator or the binder model is changing — a " +
                 "save written before a change names plates that no longer exist.")]
        [SerializeField] bool resetOnLoad;

        [Tooltip("Off to play with a save on disk without writing to it — a way to inspect a " +
                 "file rather than a game rule. The file is still read.")]
        [SerializeField] bool saving = true;

        [Tooltip("Logs every write and what it held.")]
        [SerializeField] bool logSaves;

        SheetLedger ledger;
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
            get { return PathOf(fileName); }
        }

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
            RoomSnapshot room = Paper != null ? Paper.Capture() : null;

            if (store == null && room == null) return;

            string text = ArchiveFormat.Write(store, room);
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
                Debug.Log("[Archive] saved " +
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

            // Reset is a LOAD-TIME act and there is no other kind. Nothing is live yet: the
            // ledger starts empty, and SheetSpawner and BinderSpawner sweep whatever a scene
            // was saved with, so "start from nothing" is exactly "do not read the file".
            //
            // There is deliberately no mid-play reset. Clearing the ledger with paper in the
            // room would leave every binder holding sheets nothing remembers issuing — which is
            // precisely what RoomSnapshot.Audit exists to catch, and it would be right.
            if (resetOnLoad)
            {
                bool had = Discard(path);
                Debug.LogWarning("[Archive] resetOnLoad is on — starting from nothing"
                                 + (had ? " (deleted " + path + ")" : ", there was no save")
                                 + ". Turn it off in the Archive component to keep a save.", this);
                return;
            }

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
            RestoreRoom(contents);

            if (logSaves)
                Debug.Log("[Archive] loaded " + contents.Islands.Count + " island(s) and " +
                          contents.Room + " from " + path, this);
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
        /// Deletes a save file. True if there was one. Never throws: a save that cannot be
        /// removed is a message, not a crash, and the caller is always something a person just
        /// asked to tidy up.
        /// </summary>
        public static bool Discard(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (IOException e)
            {
                Debug.LogError("[Archive] Could not delete " + path + ": " + e.Message);
                return false;
            }
            catch (System.UnauthorizedAccessException e)
            {
                Debug.LogError("[Archive] Could not delete " + path + ": " + e.Message);
                return false;
            }
        }

        /// <summary>Where the save lives, for a caller with no <see cref="Archive"/> to ask.
        /// </summary>
        public static string PathOf(string file)
        {
            return System.IO.Path.Combine(Application.persistentDataPath,
                                          string.IsNullOrEmpty(file) ? DefaultFileName : file);
        }

        // ---- wiring ------------------------------------------------------------------------

        // Resolved rather than required, as TableSession does it: every reference here is a
        // scene singleton and findable, so an archive that nobody dragged anything onto works.

        SheetLedger Ledger
        {
            get
            {
                if (ledger == null) ledger = FindFirstObjectByType<SheetLedger>(FindObjectsInactive.Include);
                return ledger;
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
