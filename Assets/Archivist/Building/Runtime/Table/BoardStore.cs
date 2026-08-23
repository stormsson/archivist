using System.Collections.Generic;
using Archivist.Building.Collection;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Where one sheet is lying on one board, as a value. Either <i>laid</i> — a pose the
    /// player chose, which nothing but this record knows — or <i>seated</i>, which is a pose
    /// nobody needs to record at all.
    ///
    /// <para><b>The halving is the whole point (C4.6, D-C7).</b> A laid sheet's pose is a
    /// player fact: it exists because the player dropped the paper there, it is not derivable
    /// from anything, and it is exactly as unrecomputable as the ledger. A <i>seated</i>
    /// sheet's pose is <c>Sheet.CentreGround</c> and <c>Sheet.RotationDeg</c> — a pure
    /// function of the island seed (R1.1). Storing it would be caching a pure function of the
    /// seed, which is the one mistake R1.11 exists to prevent, and it would rot: the day a
    /// generator constant changes, every saved board would put its seated sheets in last
    /// week's positions and be confidently wrong. So a seated placement is a flag and nothing
    /// else, and the pose is looked up.</para>
    ///
    /// <para><b>Not two structs.</b> Laid and seated were nearly separate types with a common
    /// interface, which would have made the "seated carries no pose" rule impossible to break.
    /// It also would have made the dictionary hold references and the save format hold a
    /// discriminator, for a struct with four fields. A <c>bool</c> plus the discipline written
    /// down here is cheaper, and the discipline is enforceable anyway: nothing constructs a
    /// <see cref="Placement"/> except the two factories below.</para>
    /// </summary>
    public readonly struct Placement
    {
        /// <summary>True when the sheet is at its true pose. When this is set the three pose
        /// fields are <b>meaningless</b> — not stale, not approximate, <i>meaningless</i>:
        /// they are written as zero and must never be read. Ask the island (C4.6).</summary>
        public readonly bool Seated;

        /// <summary>Ground metres, island space — the same coordinates as
        /// <c>Sheet.CentreGround</c>, not board units, so a change to
        /// <c>TableOptions.BoardUnitsPerMetre</c> cannot move a saved board.
        /// <b>Meaningless when <see cref="Seated"/>.</b></summary>
        public readonly double GroundX, GroundY;

        /// <summary>Degrees, the same convention as <c>Sheet.RotationDeg</c>.
        /// <b>Meaningless when <see cref="Seated"/>.</b></summary>
        public readonly double RotationDeg;

        Placement(bool seated, double groundX, double groundY, double rotationDeg)
        {
            Seated = seated;
            GroundX = groundX;
            GroundY = groundY;
            RotationDeg = rotationDeg;
        }

        /// <summary>A sheet the player has put down somewhere of their own choosing. Unseated
        /// is a legitimate resting state (R6.5, C6.6), not unfinished work — which is why the
        /// pose is kept exactly as released rather than nudged toward truth.</summary>
        public static Placement Laid(double groundX, double groundY, double rotationDeg)
        {
            return new Placement(false, groundX, groundY, rotationDeg);
        }

        /// <summary>
        /// A sheet at its true pose. Takes no arguments <b>on purpose</b>: there is no pose to
        /// pass, because the pose is the island's and the island is a function of its seed
        /// (C4.6). A caller that has the true coordinates to hand should still not pass them.
        ///
        /// <para>Named for what it means rather than <c>Seated()</c>, which C# will not allow
        /// beside the <see cref="Seated"/> field. The longer name is the better one anyway —
        /// it says where the pose comes from, which is the fact this type is about.</para>
        /// </summary>
        public static Placement SeatedAtTruth()
        {
            return new Placement(true, 0.0, 0.0, 0.0);
        }

        public override string ToString()
        {
            return Seated
                ? "seated (pose from the island)"
                : $"laid at ({GroundX:0.#}, {GroundY:0.#}) m, {RotationDeg:0.#}°";
        }
    }

    /// <summary>
    /// What is lying on which table. <b>Keyed by table identity, never by island</b> — which
    /// is the one decision the rest of this class follows from (C1.7, R6.8).
    ///
    /// <para>Keying by island seed was the obvious shape and it is wrong. R6.8 says one board
    /// per island, and a dictionary from seed to board says exactly that — until the room has
    /// two tables and the player wants the same island half-assembled on one and untouched on
    /// the other, or until a table is walked away from and found again with its paper still
    /// on it. A board is furniture: it is a thing in the room with a position and a lifetime,
    /// and the island it currently carries is a property of it, not the other way round.
    /// R6.8 is then kept by the <i>binding</i> (C4.3) rather than by the key, which is the
    /// stronger form — the object refuses, no rule has to be remembered.</para>
    ///
    /// <para><b>Binding is soft, and asymmetric.</b> An empty table accepts any island (C4.1);
    /// the first sheet laid fixes it (C4.2); after that only that island's sheets go on
    /// (C4.3). Coming back the other way is not automatic: only <see cref="Clear"/> unbinds
    /// (C4.4). Unbinding the moment the last sheet is picked up was tried and abandoned — a
    /// player who lifts their only sheet to re-drop it would silently hand the table back to
    /// every other island for the length of one drag, and the spec's "emptying a table is the
    /// deliberate act of clearing it" describes a folder being taken off, which this POC does
    /// not have (§13). Until folders exist, the deliberate act is the explicit call.</para>
    ///
    /// <para><b>Two states, not three (C4.5).</b> A sheet is in <c>Placed</c> or it is in the
    /// cabinet. Nothing records a removal, because a removal is not a fact about the board —
    /// it is the absence of one, and a store that remembered what had been taken off would
    /// have to be asked twice about every row the cabinet draws.</para>
    ///
    /// <para><b>Order is kept on purpose.</b> The same reason <see cref="SheetLedgerStore"/>
    /// keeps it, and here it is visible rather than merely tidy: lay order <i>is</i> the draw
    /// order of unseated sheets (§3.3), and sheets at ground scale overlap by a fifth
    /// (C1.2), so a board that reordered itself between two openings would come back with
    /// different paper on top and be unreadable. A <c>Dictionary</c>'s enumeration order is
    /// not a promise. Membership still tests through the dictionary; the list is only the
    /// order, and re-laying a sheet that is already down deliberately does <b>not</b> move it
    /// to the end — nudging a sheet is not the same act as putting it down (C4.7).</para>
    ///
    /// <para><b>Not thread-safe.</b> Everything here is driven by the pointer on the main
    /// thread. Nothing on the table renders from a worker the way the picker does, so there
    /// is no snapshot and no need for one; if one ever appears, copy
    /// <see cref="SheetLedgerStore.Snapshot"/> rather than adding a lock.</para>
    ///
    /// <para>Deliberately free of UnityEngine, mirroring <see cref="SheetLedgerStore"/>
    /// exactly — same lifetime, same shape, same serialisation story, so the day either is
    /// persisted both are, in one move (§4.2). Every field here is a primitive, a
    /// <see cref="SheetId"/> or a flat collection of them, which is what keeps that move a
    /// move and not a rewrite. <b>No persistence code lives here yet</b>: §9 is a later slice
    /// (S6) and it writes one archive file holding the ledger and every board together
    /// (C9.5), which is a decision about both stores and belongs above both of them.</para>
    /// </summary>
    public sealed class BoardStore
    {
        /// <summary>One table's board. Mutable and private, as <see cref="SheetLedgerStore"/>'s
        /// row is: the world outside gets values and copies, never something it can write
        /// back into.</summary>
        sealed class Board
        {
            public readonly string TableId;
            public ulong IslandSeed;                    // 0 while unbound (C4.1)

            public readonly Dictionary<SheetId, Placement> Placed
                = new Dictionary<SheetId, Placement>();
            public readonly List<SheetId> LayOrder = new List<SheetId>();   // draw order, §3.3

            public Board(string tableId) { TableId = tableId; }
        }

        readonly Dictionary<string, Board> boards = new Dictionary<string, Board>();
        readonly List<string> knownOrder = new List<string>();

        // ---- binding ---------------------------------------------------------------------

        /// <summary>
        /// Fixes which island this table carries (C4.2). Idempotent for the island it is
        /// already bound to.
        ///
        /// <para><b>Refuses to rebind.</b> A bound table asked for a different island keeps
        /// the one it has — silently, and deliberately not by throwing. This is called from
        /// drag handling at pointer speed, and an exception there would take out the frame
        /// for a condition the caller can test for free with <see cref="IsBound"/> and
        /// <see cref="IslandOf"/>. Changing a board's island is not a refinement of binding,
        /// it is <see cref="Clear"/> followed by a fresh bind, and saying so costs one line at
        /// the call site.</para>
        ///
        /// <para>A seed of 0 is refused too: 0 is the unbound sentinel, so "bind to 0" is a
        /// request to unbind expressed by accident. <see cref="Clear"/> is the way to say
        /// that, and it discards the placements as C4.4 requires — which is precisely the part
        /// a stray zero would skip.</para>
        /// </summary>
        public void Bind(string tableId, ulong islandSeed)
        {
            if (!IsUsableId(tableId) || islandSeed == 0UL) return;

            Board board = Ensure(tableId);
            if (board.IslandSeed == 0UL) board.IslandSeed = islandSeed;
        }

        /// <summary>Whether this table has adopted an island. An unbound table accepts any
        /// (C4.1); a bound one accepts only its own (C4.3).</summary>
        public bool IsBound(string tableId)
        {
            return IslandOf(tableId) != 0UL;
        }

        /// <summary>The island this table carries, or 0 while unbound. 0 rather than a
        /// nullable because a seed of 0 never names an island and the sentinel then survives
        /// serialisation unchanged (§4.2).</summary>
        public ulong IslandOf(string tableId)
        {
            Board board;
            return IsUsableId(tableId) && boards.TryGetValue(tableId, out board)
                ? board.IslandSeed
                : 0UL;
        }

        /// <summary>
        /// Empties the table and returns it to unbound (C4.4). Both halves, always: a table
        /// that kept its island after being cleared would look empty and still refuse every
        /// other island's paper, which is a table that is broken in a way nothing on screen
        /// can show.
        ///
        /// <para>The row itself stays in <see cref="KnownTables"/>. Clearing a table does not
        /// remove the furniture; the board is still there, waiting, and dropping the row would
        /// only mean re-adding it on the next sheet in a different position in the
        /// order.</para>
        /// </summary>
        public void Clear(string tableId)
        {
            Board board;
            if (!IsUsableId(tableId) || !boards.TryGetValue(tableId, out board)) return;

            board.IslandSeed = 0UL;
            board.Placed.Clear();
            board.LayOrder.Clear();
        }

        // ---- placing ---------------------------------------------------------------------

        /// <summary>
        /// Puts a sheet down at a pose the player chose, unseated. Binds an unbound table to
        /// this sheet's island on the way through (C4.2) — the first sheet laid is what fixes
        /// the board, and making the caller call <see cref="Bind"/> first would mean every
        /// call site repeating the rule and one of them eventually forgetting it.
        ///
        /// <para>A sheet of another island is <b>dropped</b>, not placed (C4.3, R6.8). It
        /// cannot be reached through the UI, because the cabinet only ever lists the bound
        /// island's sheets (§6.3) — but R6.8 is a promise about what a board <i>is</i>, and a
        /// promise kept only by the screen that happens to be in front of it is not kept.</para>
        ///
        /// <para>Laying a sheet that is already down replaces its pose and leaves it exactly
        /// where it is in the draw order (C4.7). A sheet being nudged, dragged and re-dropped
        /// half a metre is not being put down again, and having it jump to the top of the pile
        /// on every release would make the board shuffle itself under the player's hand. What
        /// draws on top of what while a sheet is being <i>dragged</i> is the view's business
        /// (§3.3, points 3 and 4) and is not stored here.</para>
        /// </summary>
        public void Lay(string tableId, SheetId id, double groundX, double groundY,
                        double rotationDeg)
        {
            Put(tableId, id, Placement.Laid(groundX, groundY, rotationDeg));
        }

        /// <summary>
        /// Marks a sheet as being at its true pose (C6.5).
        ///
        /// <para><b>Seating an already-laid sheet throws its coordinates away</b>, and that is
        /// the point rather than a side effect: the pose of a seated sheet is
        /// <c>Sheet.CentreGround</c> and <c>Sheet.RotationDeg</c>, a pure function of the
        /// island seed, and keeping the released coordinates beside the flag would be caching
        /// that function — the mistake R1.11 exists to prevent (C4.6, D-C7). It is also how A6
        /// is provable: delete the pose fields from a save by hand and every seated sheet must
        /// still come back to the right place. Anything that still read them would pass that
        /// test only by luck.</para>
        ///
        /// <para>Seats a sheet that is not on the table at all, rather than refusing. A sheet
        /// arriving directly at its true pose — restored from a save, or laid by the editor
        /// tooling of S1 — is a real case, and it needs no pose to arrive with.</para>
        /// </summary>
        public void Seat(string tableId, SheetId id)
        {
            Put(tableId, id, Placement.SeatedAtTruth());
        }

        /// <summary>
        /// Takes a sheet off the table and back to the cabinet (C7.5). Nothing records that it
        /// was ever here — absence <i>is</i> the cabinet (C4.5).
        ///
        /// <para>Does not unbind a board it empties; see the class comment for why that was
        /// tried and dropped.</para>
        /// </summary>
        public void Remove(string tableId, SheetId id)
        {
            Board board;
            if (!IsUsableId(tableId) || !boards.TryGetValue(tableId, out board)) return;
            if (!board.Placed.Remove(id)) return;

            board.LayOrder.Remove(id);
        }

        // ---- reading ---------------------------------------------------------------------

        /// <summary>Where a sheet is lying, if it is. False means the cabinet — there is no
        /// third answer (C4.5). Remember that <see cref="Placement.Seated"/> makes the pose
        /// fields meaningless; a caller that reads them anyway will be right until the first
        /// sheet settles.</summary>
        public bool TryGetPlacement(string tableId, SheetId id, out Placement placement)
        {
            Board board;
            if (!IsUsableId(tableId) || !boards.TryGetValue(tableId, out board))
            {
                placement = default(Placement);
                return false;
            }
            return board.Placed.TryGetValue(id, out placement);
        }

        /// <summary>Whether this sheet is out of the drawer, seated or not — the question the
        /// cabinet's two row states ask (C7.4).</summary>
        public bool IsOnTable(string tableId, SheetId id)
        {
            Board board;
            return IsUsableId(tableId)
                && boards.TryGetValue(tableId, out board)
                && board.Placed.ContainsKey(id);
        }

        /// <summary>
        /// The sheets on this table in the order they were laid down — the draw order of
        /// §3.3, oldest lowest. The live list, not a copy: read it, do not keep it, because it
        /// changes under any caller that holds it across a drag. Empty (and shared) for a
        /// table nothing has been laid on.
        ///
        /// <para>It lists <b>every</b> sheet on the table, seated ones included. The view
        /// sorts seated sheets below unseated ones itself (§3.3, points 1 and 2), because
        /// "seated sinks" is a statement about how the board is drawn while it is being
        /// assembled, not about the order the player built it in — and the player's order has
        /// to survive a sheet being seated and then picked up again (C6.7).</para>
        /// </summary>
        public IReadOnlyList<SheetId> LayOrder(string tableId)
        {
            Board board;
            return IsUsableId(tableId) && boards.TryGetValue(tableId, out board)
                ? board.LayOrder
                : Empty;
        }

        /// <summary>How much paper is on this table. 0 for a table nothing has touched.</summary>
        public int OnTableCount(string tableId)
        {
            Board board;
            return IsUsableId(tableId) && boards.TryGetValue(tableId, out board)
                ? board.Placed.Count
                : 0;
        }

        /// <summary>Every table this store has heard of, in the order it heard of them —
        /// including tables that have been cleared, which are boards with nothing on them
        /// rather than boards that stopped existing. The live list: read it, do not keep
        /// it.</summary>
        public IReadOnlyList<string> KnownTables { get { return knownOrder; } }

        // ---- internals -------------------------------------------------------------------

        static readonly SheetId[] Empty = new SheetId[0];

        /// <summary>
        /// The one path that writes a placement, so the lay-order rule and the island check
        /// exist once. Splitting them across <see cref="Lay"/> and <see cref="Seat"/> meant
        /// two copies of "append only if new", and the copies had already started to
        /// disagree.
        /// </summary>
        void Put(string tableId, SheetId id, Placement placement)
        {
            if (!IsUsableId(tableId)) return;

            Board board = Ensure(tableId);
            if (board.IslandSeed == 0UL) board.IslandSeed = id.IslandSeed;   // C4.2
            else if (board.IslandSeed != id.IslandSeed) return;              // C4.3, R6.8

            if (!board.Placed.ContainsKey(id)) board.LayOrder.Add(id);       // C4.7
            board.Placed[id] = placement;
        }

        /// <summary>
        /// Guards the key. A table's id is a GUID serialised on the prefab instance and
        /// generated on first validate (§4.1), which leaves a window in which it is null or
        /// empty — a prefab dropped in the scene and not yet validated, or one whose field was
        /// cleared by hand. Every such table would share the empty-string board, and the
        /// symptom would be two tables mirroring each other's paper with nothing on screen to
        /// explain it. Ignoring the call instead makes the failure "my table does not
        /// remember anything", which points at the id.
        /// </summary>
        static bool IsUsableId(string tableId)
        {
            return !string.IsNullOrEmpty(tableId);
        }

        Board Ensure(string tableId)
        {
            Board board;
            if (boards.TryGetValue(tableId, out board)) return board;

            board = new Board(tableId);
            boards[tableId] = board;
            knownOrder.Add(tableId);
            return board;
        }
    }
}
