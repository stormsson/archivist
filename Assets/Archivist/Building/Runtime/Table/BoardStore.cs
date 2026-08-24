using System.Collections.Generic;
using Archivist.Building.Collection;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Table
{
    /// <summary>
    /// Where one sheet is lying on one board, as a value. Either <i>laid</i> — a pose the
    /// player chose, which nothing but this record knows — or <i>seated</i>, or <i>grouped</i>,
    /// both of which are poses nobody needs to record at all.
    ///
    /// <para><b>The halving is the whole point (C4.6, D-C7).</b> A laid sheet's pose is a
    /// player fact, as unrecomputable as the ledger. A <i>seated</i> sheet's pose is
    /// <c>Sheet.CentreGround</c> and <c>Sheet.RotationDeg</c>, a pure function of the seed
    /// (R1.1); storing it would cache a pure function of the seed — the mistake R1.11 exists to
    /// prevent — and it would rot, because the day a generator constant changes every saved
    /// board would put its seated sheets in last week's positions and be confidently wrong. So
    /// a seated placement is a flag, and the pose is looked up.</para>
    ///
    /// <para><b>A third state, same discipline (G4.1).</b> <see cref="GroupId"/> is 0 for a
    /// loose sheet and names a <see cref="GroupRecord"/> otherwise; a grouped sheet's pose is
    /// derived from its group's frame — <c>pose(M) = (R(φ)·c_M + t, θ_M + φ)</c>, G3.1. The
    /// visible consequence (G4.3) is that a sheet joining a group needs <i>no corrective
    /// write</i>: it was released <i>near</i> the fit, not on it, and carrying no pose there is
    /// nothing to correct. Storing the released pose beside the group id would leave the
    /// assembly permanently and invisibly loose.</para>
    ///
    /// <para><b><see cref="Seated"/> and <see cref="GroupId"/> are mutually exclusive</b>, and
    /// the factories make the pair unrepresentable rather than discouraged. Seated means "at the
    /// island's own pose"; grouped means "at the frame's". A record claiming both would leave
    /// every reader to pick one. §13 keeps <see cref="Seated"/> in the model deliberately:
    /// nothing produces it now, and the day absolute correctness returns it is the right
    /// shape.</para>
    ///
    /// <para><b>An int, not a reference to the group.</b> This struct is a save-file row in
    /// waiting (§4.2, G4.4), and a reference would make the saved board a graph instead of a
    /// table of primitives. It also means no call site can quietly start reading the group
    /// through the placement and end up with two paths to the frame.</para>
    ///
    /// <para><b>Not four structs.</b> Separate types with a common interface would make "seated
    /// carries no pose" unbreakable, at the cost of a dictionary of references and a save-format
    /// discriminator, for a struct with four fields — and grouping would now add a third type
    /// and a third branch at every call site. The discipline is enforceable anyway: nothing
    /// constructs a <see cref="Placement"/> except the three factories below.</para>
    /// </summary>
    public readonly struct Placement
    {
        /// <summary>True when the sheet is at its true pose. When this is set the three pose
        /// fields are <b>meaningless</b> — not stale, not approximate, <i>meaningless</i>:
        /// they are written as zero and must never be read. Ask the island (C4.6).</summary>
        public readonly bool Seated;

        /// <summary>The group this sheet is part of, or 0 when it is loose (G4.1). When this
        /// is non-zero the three pose fields are <b>meaningless</b> — not stale, not
        /// approximate, <i>meaningless</i>: they are written as zero and must never be read.
        /// Ask the group's frame (G3.1). 0 is the sentinel rather than a nullable for the same
        /// reason a seed of 0 means unbound: no group is ever numbered 0, so the sentinel
        /// survives serialisation unchanged (§4.2).</summary>
        public readonly int GroupId;

        /// <summary>Ground metres, island space — the same coordinates as
        /// <c>Sheet.CentreGround</c>, not board units, so a change to
        /// <c>TableOptions.BoardUnitsPerMetre</c> cannot move a saved board.
        /// <b>Meaningless when <see cref="Seated"/> or when <see cref="GroupId"/> is
        /// set.</b></summary>
        public readonly double GroundX, GroundY;

        /// <summary>Degrees, the same convention as <c>Sheet.RotationDeg</c>.
        /// <b>Meaningless when <see cref="Seated"/> or when <see cref="GroupId"/> is
        /// set.</b></summary>
        public readonly double RotationDeg;

        Placement(bool seated, int groupId, double groundX, double groundY, double rotationDeg)
        {
            Seated = seated;
            GroupId = groupId;
            GroundX = groundX;
            GroundY = groundY;
            RotationDeg = rotationDeg;
        }

        /// <summary>Whether this sheet's pose comes from a group frame rather than from these
        /// fields (G4.1). The one test a pose reader has to make before touching
        /// <see cref="GroundX"/>, beside <see cref="Seated"/>.</summary>
        public bool Grouped { get { return GroupId != 0; } }

        /// <summary>A sheet the player has put down somewhere of their own choosing. Unseated
        /// is a legitimate resting state (R6.5, C6.6), not unfinished work — which is why the
        /// pose is kept exactly as released rather than nudged toward truth.</summary>
        public static Placement Laid(double groundX, double groundY, double rotationDeg)
        {
            return new Placement(false, 0, groundX, groundY, rotationDeg);
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
            return new Placement(true, 0, 0.0, 0.0, 0.0);
        }

        /// <summary>
        /// A member of an assembly. Takes the group and <b>no pose</b>, for the same reason
        /// <see cref="SeatedAtTruth"/> takes nothing: the pose is the frame's, composed with
        /// the sheet's own truth (G3.1), and a caller that has just computed the corrected
        /// pose to animate the settle (G5.3) must still not pass it here. What is stored is
        /// the membership; the geometry is asked for.
        ///
        /// <para><paramref name="groupId"/> must be a live id from
        /// <see cref="BoardStore.CreateGroup"/>. 0 is not a group and this does not guard against
        /// it: the only caller never has a 0 to pass, and a guard could only choose between a
        /// throw at pointer speed and a silent loose placement at the origin.</para>
        /// </summary>
        public static Placement InGroup(int groupId)
        {
            return new Placement(false, groupId, 0.0, 0.0, 0.0);
        }

        public override string ToString()
        {
            if (Grouped) return $"in group {GroupId} (pose from the frame)";
            return Seated
                ? "seated (pose from the island)"
                : $"laid at ({GroundX:0.#}, {GroundY:0.#}) m, {RotationDeg:0.#}°";
        }
    }

    /// <summary>
    /// One assembly on one board, as a value: where the whole thing sits, which survey its
    /// members belong to, whether it is out on the table or parked in the cabinet, and who is
    /// in it.
    ///
    /// <para><b>A frame, not N poses (G1.3, G4.3).</b> The three pose numbers here are the
    /// <i>only</i> geometry a group owns; every member's pose is derived from them, so a broken
    /// group is unrepresentable — the members have no poses to disagree with each other. It also
    /// keeps the group coherent across a tuning change: <c>config/generation.yml</c>'s header
    /// says <i>"sheet identities do not move — sheet 7 stays sheet 7 — but the ground under them
    /// does"</i>, and a stored frame re-derives every member onto the new ground where N stored
    /// poses would keep last week's arrangement, still <i>look</i> assembled, and fail the test
    /// that created them. Float drift is <b>not</b> among the reasons: repeated
    /// compose/decompose in doubles is ~1e-13 relative, nanometres over an island.</para>
    ///
    /// <para><b>Three doubles, not a frame type.</b> Everything in <see cref="BoardStore"/> is a
    /// primitive, a <see cref="SheetId"/> or a flat collection of them, which is what makes §9's
    /// persistence one move rather than a rewrite (§4.2). A <c>BoardFrame</c> borrowed from the
    /// interaction layer would be a type the save format has to know about, and the day it grew
    /// a cached matrix the store would have stopped being serialisable unnoticed.</para>
    ///
    /// <para><b>The survey key is two fields, lifted straight off <see cref="SheetId"/>.</b>
    /// G3.4 makes fusability <i>"same office, same whole-island flag"</i> and nothing else, so a
    /// candidate can be rejected <b>without touching the island</b> — generating one to learn
    /// that a Garrison sheet cannot join a Land Survey group would cost a third of a second for
    /// something the identity already said. A survey's list position is not stable; office plus
    /// the flag is. The island is not on this record because it is the board's (R6.8).</para>
    ///
    /// <para><b>A copy, taken when you asked.</b> Like <c>IslandHolding</c>, a snapshot and never
    /// a handle: <see cref="Members"/> is a fresh array, so a caller cannot write back into a
    /// board and does not see the assembly grow under it.</para>
    /// </summary>
    public readonly struct GroupRecord
    {
        /// <summary>The board-local id (G4.2). Never 0 — 0 means "no group" everywhere else in
        /// this file — and never reused once the group is gone.</summary>
        public readonly int GroupId;

        /// <summary>The frame's rotation φ, in degrees, in the sense G3.1 defines: the angle
        /// that takes island ground to board ground. Not a sheet's rotation.</summary>
        public readonly double RotationDeg;

        /// <summary>The frame's offset t, in <b>ground metres</b>, matching
        /// <see cref="Placement.GroundX"/> and for the same reason: board units are a display
        /// choice and must not be able to move a saved assembly.</summary>
        public readonly double OffsetX, OffsetY;

        /// <summary>Half the survey key (G3.4). Every member has this office.</summary>
        public readonly Office Office;

        /// <summary>The other half (G3.4). The whole-island survey (R2.2a) borrows an office,
        /// so the flag is what stops its sheet being confused with that office's own.</summary>
        public readonly bool WholeIsland;

        /// <summary>False for a group parked in the cabinet (G6.4). A parked group keeps its
        /// membership and its frame — parking is where it is, not what it is — and its members
        /// are not on the board, so <c>BoardStore.IsOnTable</c> says no about every one of
        /// them and C4.5's two states survive a group exactly as they survive a sheet.</summary>
        public readonly bool OnTable;

        /// <summary>
        /// Who is in it, in the order they joined. A copy.
        ///
        /// <para><b>The order matters</b> for the reason lay order matters (C4.7): G5.6 makes
        /// the draw stack per-group, with a group's members occupying a <i>contiguous</i> run
        /// of tiers <i>in their own lay order</i>, so an assembly always reads as one coherent
        /// map and can never be interleaved with another group's paper. A set would have made
        /// the run's internal order a fresh accident on every opening, and two openings of the
        /// same board would have drawn different paper on top.</para>
        /// </summary>
        public readonly IReadOnlyList<SheetId> Members;

        /// <summary>Made by <see cref="BoardStore"/>. Public for the same reason
        /// <c>IslandHolding</c>'s is: it is a value, it cannot be written back into anything,
        /// and a test that wants to build one should not have to go through a board.</summary>
        public GroupRecord(int groupId, double rotationDeg, double offsetX, double offsetY,
                           Office office, bool wholeIsland, bool onTable,
                           IReadOnlyList<SheetId> members)
        {
            GroupId = groupId;
            RotationDeg = rotationDeg;
            OffsetX = offsetX;
            OffsetY = offsetY;
            Office = office;
            WholeIsland = wholeIsland;
            OnTable = onTable;
            Members = members;
        }

        public int MemberCount { get { return Members == null ? 0 : Members.Count; } }

        /// <summary>Whether this sheet is of the survey this group is made of — G3.4's
        /// <c>fusable</c>, minus the island, which the board has already settled. The cheap
        /// half of the fit test and the one that should be asked first: it is two comparisons
        /// against an identity, and it rejects a candidate before anything regenerates an
        /// island to find its geometry.</summary>
        public bool SameSurvey(SheetId id)
        {
            return Office == id.Office && WholeIsland == id.WholeIsland;
        }

        public override string ToString()
        {
            return $"group {GroupId}: {MemberCount} × {Office}{(WholeIsland ? "-whole" : "")}"
                 + $" at ({OffsetX:0.#}, {OffsetY:0.#}) m, {RotationDeg:0.#}°"
                 + (OnTable ? "" : " (parked)");
        }
    }

    /// <summary>
    /// What is lying on which table. <b>Keyed by table identity, never by island</b> — which
    /// is the one decision the rest of this class follows from (C1.7, R6.8).
    ///
    /// <para>Keying by island seed is the obvious shape and it is wrong. R6.8 says one board
    /// per island, and a seed-to-board dictionary says exactly that — until the room has two
    /// tables and the player wants the same island half-assembled on one and untouched on the
    /// other. A board is furniture: a thing in the room with a position and a lifetime, and the
    /// island it carries is a property of it. R6.8 is then kept by the <i>binding</i> (C4.3)
    /// rather than by the key, which is the stronger form — the object refuses, and no rule has
    /// to be remembered.</para>
    ///
    /// <para><b>Binding is soft, and asymmetric.</b> An empty table accepts any island (C4.1);
    /// the first sheet laid fixes it (C4.2); after that only that island's sheets go on (C4.3).
    /// Only <see cref="Clear"/> unbinds (C4.4) — unbinding when the last sheet is picked up
    /// would hand the table back to every other island for the length of one drag.</para>
    ///
    /// <para><b>Two states, not three (C4.5).</b> A sheet is in <c>Placed</c> or in the cabinet.
    /// Nothing records a removal, because a removal is the absence of a fact and a store that
    /// remembered what had been taken off would have to be asked twice about every row the
    /// cabinet draws. <b>A group does not add a third state</b>, which is what fixes what
    /// parking means: a parked group's members leave <c>Placed</c> like any other sheet going
    /// back to the drawer (G6.4), and the invariant the group half maintains is <i>a member is
    /// on the board exactly when its group is</i>. Leaving parked members in <c>Placed</c> with
    /// a flag makes <see cref="IsOnTable"/> — the question the cabinet's two row states ask
    /// (C7.4) — answer yes about paper that is in a drawer.</para>
    ///
    /// <para><b>Membership is authoritative; the placement's copy is a memo.</b> A sheet's group
    /// is <see cref="GroupRecord.Members"/>, because a parked group has no placements at all and
    /// its membership still has to survive (G6.4). <see cref="Placement.GroupId"/> holds the
    /// same fact where the pose discipline needs it. They cannot drift apart, because exactly
    /// one path writes both.</para>
    ///
    /// <para><b>Order is kept on purpose.</b> Lay order <i>is</i> the draw order of unseated
    /// sheets (§3.3), and sheets at ground scale overlap by a fifth (C1.2), so a board that
    /// reordered itself between two openings would come back with different paper on top and be
    /// unreadable — and a <c>Dictionary</c>'s enumeration order is not a promise. Re-laying a
    /// sheet already down deliberately does <b>not</b> move it to the end: nudging a sheet is
    /// not putting it down (C4.7). Groups are ordered the same way twice over — the group table
    /// keeps creation order, each group keeps its join order, which G5.6 turns into a contiguous
    /// run of draw tiers. <b>Fusing does not reshuffle <see cref="LayOrder"/></b>: that is the
    /// order the player built the board in, and resorting on every join would make paper the
    /// player never touched change places.</para>
    ///
    /// <para><b>Not thread-safe.</b> Everything here is driven by the pointer on the main
    /// thread. If a worker ever renders from the table, copy
    /// <see cref="SheetLedgerStore.Snapshot"/> rather than adding a lock.</para>
    ///
    /// <para>Deliberately free of UnityEngine, mirroring <see cref="SheetLedgerStore"/> — same
    /// lifetime, shape and serialisation story, so the day either is persisted both are, in one
    /// move (§4.2). Every field is a primitive, a <see cref="SheetId"/> or a flat collection of
    /// them, which is what keeps that a move and not a rewrite; a nine-sheet assembly saves as
    /// one pose instead of nine (G4.4). <b>No persistence code lives here yet</b> — §9 writes
    /// one archive file holding the ledger and every board together (C9.5), a decision that
    /// belongs above both stores.</para>
    ///
    /// <para><b>What is deliberately not here.</b> G3.1's derivation needs
    /// <c>Sheet.CentreGround</c>, which means the island, and this store has never regenerated
    /// anything: it holds the frame and hands it out, and <c>BoardFrame</c> composes it. So is
    /// G9.1's <c>complete(group)</c>, a count against the survey's sheet total.</para>
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

            public readonly Dictionary<int, Group> Groups = new Dictionary<int, Group>();
            public readonly List<int> GroupOrder = new List<int>();         // creation order

            /// <summary>The next group id this board will hand out (G4.2).
            ///
            /// <para><b>Per board, monotonic, and never rewound.</b> Not a static counter,
            /// because two tables would then hand out ids that depend on the order the player
            /// happened to visit them and a saved board would only be readable beside the one
            /// it was saved with. Not a GUID, because a group id is written into every member's
            /// <see cref="Placement"/> and §4.2's save format is a table of primitives, not a
            /// string per sheet. And never reset — not by <see cref="Clear"/>, not by a group
            /// being dissolved — because a reused id would let a stale reference, a half-applied
            /// undo or an older save name a group that is not the one it meant, and be
            /// confidently wrong rather than obviously wrong.</para></summary>
            public int NextGroupId = 1;

            public Board(string tableId) { TableId = tableId; }
        }

        /// <summary>One assembly, live. <see cref="GroupRecord"/> is what leaves the
        /// class.</summary>
        sealed class Group
        {
            public readonly int Id;
            public readonly Office Office;              // survey key (G3.4)
            public readonly bool WholeIsland;

            public double RotationDeg, OffsetX, OffsetY;    // the frame (G3.1)
            public bool OnTable = true;                     // false when parked (G6.4)

            public readonly List<SheetId> Members = new List<SheetId>();

            public Group(int id, Office office, bool wholeIsland,
                         double rotationDeg, double offsetX, double offsetY)
            {
                Id = id;
                Office = office;
                WholeIsland = wholeIsland;
                RotationDeg = rotationDeg;
                OffsetX = offsetX;
                OffsetY = offsetY;
            }
        }

        readonly Dictionary<string, Board> boards = new Dictionary<string, Board>();
        readonly List<string> knownOrder = new List<string>();

        // ---- binding ---------------------------------------------------------------------

        /// <summary>
        /// Fixes which island this table carries (C4.2). Idempotent for the island it is
        /// already bound to.
        ///
        /// <para><b>Refuses to rebind</b>, silently rather than by throwing: this is called
        /// from drag handling at pointer speed, and the caller can test the condition for free
        /// with <see cref="IsBound"/> and <see cref="IslandOf"/>. Changing a board's island is
        /// <see cref="Clear"/> followed by a fresh bind.</para>
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
        /// <para><b>The groups go too, parked ones included</b> — the one place G5.5's
        /// "membership never shrinks" is overruled. A group is an arrangement of <i>this
        /// island's</i> paper, so an assembly that outlived the binding would sit in the Groups
        /// section of a table now bound elsewhere, listing sheets that table will not accept:
        /// not a shrunk group but a group with no board under it. G5.5 is a promise about
        /// <i>gestures</i>, and clearing a table is not one the player has.</para>
        ///
        /// <para>The row stays in <see cref="KnownTables"/> — clearing a table does not remove
        /// the furniture. <c>NextGroupId</c> stays too: its next group must not be able to answer
        /// to a name the last one used.</para>
        /// </summary>
        public void Clear(string tableId)
        {
            Board board;
            if (!IsUsableId(tableId) || !boards.TryGetValue(tableId, out board)) return;

            board.IslandSeed = 0UL;
            board.Placed.Clear();
            board.LayOrder.Clear();
            board.Groups.Clear();
            board.GroupOrder.Clear();
        }

        // ---- placing ---------------------------------------------------------------------

        /// <summary>
        /// Puts a sheet down at a pose the player chose, unseated. Binds an unbound table to
        /// this sheet's island on the way through (C4.2) — the first sheet laid is what fixes
        /// the board, and making the caller call <see cref="Bind"/> first would mean every
        /// call site repeating the rule and one of them eventually forgetting it.
        ///
        /// <para>A sheet of another island is <b>dropped</b>, not placed (C4.3, R6.8). The UI
        /// cannot reach that case — the cabinet only lists the bound island's sheets (§6.3) — but
        /// a promise kept only by the screen in front of it is not kept.</para>
        ///
        /// <para>Laying a sheet that is already down replaces its pose and leaves its place in
        /// the draw order (C4.7): a sheet nudged half a metre is not being put down again, and
        /// jumping to the top of the pile on every release would make the board shuffle itself
        /// under the player's hand. What draws on top while a sheet is <i>dragged</i> is the
        /// view's business (§3.3) and is not stored here.</para>
        ///
        /// <para><b>Laying a grouped sheet takes it out of nothing.</b> The placement becomes
        /// loose and the group loses a member exactly as <see cref="Remove"/> makes it lose
        /// one — see there for why that path exists at all when G5.5 says membership never
        /// shrinks, and for what happens to an assembly that falls below two.</para>
        /// </summary>
        public void Lay(string tableId, SheetId id, double groundX, double groundY,
                        double rotationDeg)
        {
            Put(tableId, id, Placement.Laid(groundX, groundY, rotationDeg));
        }

        /// <summary>
        /// Marks a sheet as being at its true pose (C6.5).
        ///
        /// <para><b>Seating an already-laid sheet throws its coordinates away</b>, which is the
        /// point: a seated sheet's pose is a pure function of the island seed, and keeping the
        /// released coordinates beside the flag would cache that function (C4.6, D-C7, R1.11).
        /// It is also how A6 is provable — delete the pose fields from a save by hand and every
        /// seated sheet must still come back to the right place.</para>
        ///
        /// <para>Seats a sheet that is not on the table at all, rather than refusing. A sheet
        /// arriving directly at its true pose — restored from a save, or laid by the editor
        /// tooling of S1 — is a real case, and it needs no pose to arrive with.</para>
        ///
        /// <para><b>Nothing produces this any more</b> (§13). Absolute correctness is out of
        /// scope for groups (G1.9), so no gesture seats a sheet; it stays because the model of
        /// C4.6 is right and the day the island is revealed for a completed survey it is what
        /// that feature needs. Seating a grouped sheet takes it out of its group, per
        /// <see cref="Lay"/>.</para>
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
        ///
        /// <para><b>A member can be removed, and G5.5 says none ever leave a group.</b> Both
        /// are true: G5.5 is about gestures — there is no detach, and G6.2 makes a grouped
        /// sheet's office row inert so the player cannot reach one — but this method and
        /// <see cref="Clear"/> predate groups and can still be called by tooling or by a save
        /// being repaired, so the store needs a defined answer. It is: the member leaves, and
        /// <b>a group that falls below two members dissolves</b>. A group of one is
        /// geometrically well defined (G3.6) but is a thing the fuse rule could never make, and
        /// it would draw a Groups row reading "1 of 9".</para>
        ///
        /// <para><b>The survivor is left <see cref="Placement.Laid"/> at the origin, which is
        /// wrong on purpose.</b> Its correct pose is the frame composed with its own truth
        /// (G3.1), and composing that needs the island, which this store has never regenerated
        /// and must not start. So the choice is between inventing a plausible pose and admitting
        /// there is none; zero is chosen <i>because</i> it is obviously wrong — a sheet at island
        /// origin is visible from across the table, where a subtly-off pose would look like a
        /// settle that missed. <b>A caller that removes a member owns re-laying the
        /// survivor</b>: ask <see cref="GroupIdOf"/> and <see cref="TryGetGroup"/> <i>before</i>
        /// the call. The alternative — sending the survivor back to the cabinet, lossless and
        /// needing no pose — makes a sheet the caller did not name silently disappear, and a
        /// vanishing sheet is the one failure a player cannot describe.</para>
        /// </summary>
        public void Remove(string tableId, SheetId id)
        {
            Board board;
            if (!IsUsableId(tableId) || !boards.TryGetValue(tableId, out board)) return;
            if (!board.Placed.Remove(id)) return;

            board.LayOrder.Remove(id);
            LeaveGroup(board, id);
        }

        // ---- groups ----------------------------------------------------------------------

        /// <summary>
        /// Opens an empty group on this board and returns its id, or 0 if the table id is
        /// unusable. 0 is the same sentinel <see cref="Placement.GroupId"/> uses, so a caller
        /// that forgets to check writes "loose" rather than a live-looking wrong id.
        ///
        /// <para><b>It is born empty, and briefly invalid.</b> G5.1 creates a group from two
        /// sheets at once, so this is meant to be filled by the two <see cref="AddToGroup"/>
        /// calls immediately following, and <see cref="Remove"/>'s below-two rule does not apply
        /// here — enforced at creation there would be no moment at which both members have been
        /// passed. Taking the founding pair as arguments is the tidier signature but makes G5.1's
        /// other three cases call a different method, giving the store four entry points for one
        /// act.</para>
        ///
        /// <para><b>The frame is passed in, whole.</b> The store cannot derive it: G3.1 builds a
        /// loose sheet's frame from <c>φ = r_B − θ_B</c>, and <c>θ_B</c> is the island's to say.
        /// G5.2 decides <i>whose</i> frame it is — the stationary thing's.</para>
        ///
        /// <para><b>A group does not bind the board</b>, carrying no seed to bind it with: the
        /// survey key is an office and a flag (G3.4), naming a survey of every island equally.
        /// The first member binds, through the same path a laid sheet does (C4.2).
        /// <paramref name="wholeIsland"/> is not refused, though §6 says the whole-island sheet
        /// can never fuse — that follows from R2.2a making it a survey of exactly one sheet, and
        /// the below-two rule already makes a survey of one ungroupable, once.</para>
        /// </summary>
        public int CreateGroup(string tableId, Office office, bool wholeIsland,
                               double rotationDeg, double offsetX, double offsetY)
        {
            if (!IsUsableId(tableId)) return 0;

            Board board = Ensure(tableId);
            var group = new Group(board.NextGroupId++, office, wholeIsland,
                                  rotationDeg, offsetX, offsetY);
            board.Groups[group.Id] = group;
            board.GroupOrder.Add(group.Id);
            return group.Id;
        }

        /// <summary>
        /// Puts a sheet into a group and drops whatever pose it had. True when the sheet is a
        /// member of that group afterwards <i>and</i> this call is why; true also when it was
        /// already a member, the way <see cref="Bind"/> is idempotent for the island it already
        /// has. False is a refusal, and there are four of them:
        ///
        /// <para><b>A different survey</b> (G3.4). The difference between two offices' sheets of
        /// one hillside is what the archive is about (G1.2), and fusing them erases it; second,
        /// offices survey at different scales and rotations, so co-located sheets would satisfy a
        /// relative test whenever roughly on top of one another and every group would swallow the
        /// board. Two comparisons against the identity, no island touched.</para>
        ///
        /// <para><b>Another island</b> (C4.3, R6.8), by the same promise <see cref="Lay"/>
        /// keeps.</para>
        ///
        /// <para><b>Already in another group</b> (G5.5). A sheet does not move house: nothing
        /// leaves a group, so a member joining a second one would be a detach with the gesture
        /// filed off. Two groups become one through <see cref="MergeGroups"/>, which is the act
        /// G5.1 actually describes.</para>
        ///
        /// <para><b>No such group, or no such table.</b></para>
        ///
        /// <para>A sheet that is not on the table is <b>accepted</b>, as <see cref="Seat"/>
        /// accepts one: a member arriving from a save or from a parked group's retrieval (G6.5)
        /// has no pose to arrive with. Which side of C4.5 it lands on is the group's to decide —
        /// a member is on the board exactly when its group is — so joining a parked group takes a
        /// sheet <i>off</i> the table. A group is one object and half of it cannot be in a
        /// drawer.</para>
        /// </summary>
        public bool AddToGroup(string tableId, int groupId, SheetId id)
        {
            Board board;
            Group group;
            if (!TryFindGroup(tableId, groupId, out board, out group)) return false;

            if (group.Office != id.Office || group.WholeIsland != id.WholeIsland) return false;
            if (board.IslandSeed != 0UL && board.IslandSeed != id.IslandSeed) return false;

            int already = GroupIdOf(board, id);
            if (already == group.Id) return true;
            if (already != 0) return false;

            if (board.IslandSeed == 0UL) board.IslandSeed = id.IslandSeed;   // C4.2
            group.Members.Add(id);                                           // G5.6 join order
            SyncMember(board, group, id);
            return true;
        }

        /// <summary>
        /// Pours one group into another and destroys the empty one (G5.1's fourth case). True
        /// when it happened.
        ///
        /// <para><b><paramref name="keepId"/>'s frame survives</b> (G5.2), which is why the
        /// parameters are named for what happens to them rather than left/right. The correction
        /// the absorbed members visibly take is bounded by the fit tolerance that allowed the
        /// merge — at most 154 m on island 0's Land Survey — so even a nine-sheet assembly
        /// joining a lone sheet does not jump.</para>
        ///
        /// <para><b>One flat group, never a nesting</b> (§13). The absorbed members are appended
        /// in their own join order, so each half keeps the run G5.6 draws it in.</para>
        ///
        /// <para>Refuses a merge across surveys (G3.4), a group with itself, and <b>a parked
        /// group with an unparked one</b>. That pair is not a merge the player can perform — both
        /// must be on the table to be dragged into each other — so a caller asking for it means a
        /// retrieval and a merge and has not said which way round.</para>
        /// </summary>
        public bool MergeGroups(string tableId, int keepId, int absorbId)
        {
            if (keepId == absorbId) return false;

            Board board;
            Group keep;
            if (!TryFindGroup(tableId, keepId, out board, out keep)) return false;

            Group absorb;
            if (!board.Groups.TryGetValue(absorbId, out absorb)) return false;
            if (keep.Office != absorb.Office || keep.WholeIsland != absorb.WholeIsland) return false;
            if (keep.OnTable != absorb.OnTable) return false;

            for (int i = 0; i < absorb.Members.Count; i++)
            {
                SheetId member = absorb.Members[i];
                keep.Members.Add(member);
                SyncMember(board, keep, member);
            }
            Destroy(board, absorb);
            return true;
        }

        /// <summary>
        /// Moves the whole assembly: exactly one frame is edited and every member's pose
        /// follows (G5.4). True when the group exists.
        ///
        /// <para>This is the write a drag and a <c>Q</c>/<c>E</c> turn both come down to, and
        /// its being a single write is the point of storing a frame rather than N poses (G4.3):
        /// no path here can move one member and not the others. The turn's <i>pivot</i> is the
        /// caller's arithmetic, because a union is made of quads and quads need the island.
        /// </para>
        ///
        /// <para>Metres and degrees, never board units, matching <see cref="Placement.GroundX"/>
        /// so that a change to <c>TableOptions.BoardUnitsPerMetre</c> cannot move a saved
        /// assembly.</para>
        /// </summary>
        public bool SetGroupFrame(string tableId, int groupId,
                                  double rotationDeg, double offsetX, double offsetY)
        {
            Board board;
            Group group;
            if (!TryFindGroup(tableId, groupId, out board, out group)) return false;

            group.RotationDeg = rotationDeg;
            group.OffsetX = offsetX;
            group.OffsetY = offsetY;
            return true;
        }

        /// <summary>
        /// Parks the group in the cabinet or lays it back out (G6.4, G6.5). True when the group
        /// exists; idempotent for the state it is already in.
        ///
        /// <para><b>Membership and frame both survive a round trip</b> — the whole content of
        /// G6.4: parking is where an assembly is, never what it is. It is also what makes G5.5
        /// tolerable, because R6.5's "nothing is ever stuck" is honoured by the drawer rather
        /// than by a detach gesture.</para>
        ///
        /// <para>Parking removes every member from the board and retrieval puts them all back,
        /// because C4.5 has two states and a group does not get a third — <see cref="IsOnTable"/>
        /// says a parked group's members are in the cabinet. They come back in join order at the
        /// end of the lay order, the contiguous run G5.6 wants.</para>
        ///
        /// <para>The frame's <c>φ</c> is untouched on retrieval (G6.5), unlike
        /// <c>BeginPlace</c> laying a single sheet at rotation 0. Resolving orientation is part
        /// of placing a <i>sheet</i> (P2.6, C6.3); a group has already had its resolved — that is
        /// what made it a group.</para>
        /// </summary>
        public bool SetGroupOnTable(string tableId, int groupId, bool onTable)
        {
            Board board;
            Group group;
            if (!TryFindGroup(tableId, groupId, out board, out group)) return false;
            if (group.OnTable == onTable) return true;

            group.OnTable = onTable;
            for (int i = 0; i < group.Members.Count; i++) SyncMember(board, group, group.Members[i]);
            return true;
        }

        // ---- reading ---------------------------------------------------------------------

        /// <summary>Where a sheet is lying, if it is. False means the cabinet — there is no
        /// third answer (C4.5). Remember that <see cref="Placement.Seated"/> and
        /// <see cref="Placement.GroupId"/> both make the pose fields meaningless; a caller that
        /// reads them anyway will be right until the first sheet settles or the first two
        /// join.</summary>
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

        /// <summary>Whether this sheet is out of the drawer, seated, grouped or not — the
        /// question the cabinet's two row states ask (C7.4). False for a member of a parked
        /// group, which is in the drawer with the rest of its assembly (G6.4).</summary>
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
        /// <para>It lists <b>every</b> sheet on the table, seated and grouped ones included.
        /// The view sorts seated sheets below unseated ones itself (§3.3, points 1 and 2),
        /// because "seated sinks" is a statement about how the board is drawn while it is being
        /// assembled, not about the order the player built it in — and the player's order has
        /// to survive a sheet being seated and then picked up again (C6.7).</para>
        ///
        /// <para>G5.6 makes the stack <b>per group</b>: a group's members take a contiguous run
        /// of tiers, in their own join order, so an assembly can never be interleaved with
        /// another group's paper. That run is composed from <see cref="GroupRecord.Members"/>
        /// by the view, and this list is deliberately <i>not</i> resorted when sheets fuse —
        /// it is the order the player built the board in, and a store that reshuffled it on
        /// every join would move paper the player never touched.</para>
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

        /// <summary>One assembly, as a value (G4.2). False when there is no such group — which
        /// includes a group that has been dissolved or merged away, because ids are never
        /// reused and a stale one therefore fails loudly instead of naming somebody
        /// else.</summary>
        public bool TryGetGroup(string tableId, int groupId, out GroupRecord group)
        {
            Board board;
            Group live;
            if (!TryFindGroup(tableId, groupId, out board, out live))
            {
                group = default(GroupRecord);
                return false;
            }
            group = RecordOf(live);
            return true;
        }

        /// <summary>
        /// Every group on this table in the order they were made, on-table and parked alike —
        /// which is what G6.1 asks the Groups section to list, and why the section starts empty
        /// because no groups exist yet rather than because it only holds parked ones.
        ///
        /// <para>A fresh list of copies every call, per this class's standing rule: the world
        /// outside gets values, never something it can write back into. Unlike
        /// <see cref="LayOrder"/> this one may be kept and sorted — it is a few structs, and a
        /// board holds at most three groups on island 0 (§6).</para>
        /// </summary>
        public IReadOnlyList<GroupRecord> GroupsOn(string tableId)
        {
            Board board;
            if (!IsUsableId(tableId) || !boards.TryGetValue(tableId, out board))
                return new List<GroupRecord>();

            var all = new List<GroupRecord>(board.GroupOrder.Count);
            for (int i = 0; i < board.GroupOrder.Count; i++)
                all.Add(RecordOf(board.Groups[board.GroupOrder[i]]));
            return all;
        }

        /// <summary>
        /// Which group this sheet belongs to, or 0 when it is loose or not on this board at all
        /// — the test G6.2's inert office row is drawn from.
        ///
        /// <para>Answers from the group table, not from the placement, because a parked group
        /// has no placements and its members are still members (G6.4). The placement's copy of
        /// the id is checked first only because it is a dictionary lookup; when it answers it
        /// answers the same thing, since one path writes both.</para>
        /// </summary>
        public int GroupIdOf(string tableId, SheetId id)
        {
            Board board;
            return IsUsableId(tableId) && boards.TryGetValue(tableId, out board)
                ? GroupIdOf(board, id)
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
        ///
        /// <para>A pose written over a member takes it out of its group on the way through:
        /// the two are alternatives (a placement carries one derivation or none), so leaving
        /// the membership behind would make the group's frame claim a sheet that has a pose of
        /// its own — the internally-wrong assembly G4.3 exists to make unrepresentable.</para>
        /// </summary>
        void Put(string tableId, SheetId id, Placement placement)
        {
            if (!IsUsableId(tableId)) return;

            Board board = Ensure(tableId);
            if (board.IslandSeed == 0UL) board.IslandSeed = id.IslandSeed;   // C4.2
            else if (board.IslandSeed != id.IslandSeed) return;              // C4.3, R6.8

            if (!placement.Grouped) LeaveGroup(board, id);

            if (!board.Placed.ContainsKey(id)) board.LayOrder.Add(id);       // C4.7
            board.Placed[id] = placement;
        }

        /// <summary>
        /// Keeps the one group invariant: a member is on the board exactly when its group is,
        /// and its placement names the group it is in. Every path that changes either fact
        /// comes through here, which is what stops <see cref="Placement.GroupId"/> and
        /// <see cref="GroupRecord.Members"/> ever telling two stories.
        /// </summary>
        static void SyncMember(Board board, Group group, SheetId id)
        {
            if (group.OnTable)
            {
                if (!board.Placed.ContainsKey(id)) board.LayOrder.Add(id);
                board.Placed[id] = Placement.InGroup(group.Id);
            }
            else if (board.Placed.Remove(id))
            {
                board.LayOrder.Remove(id);
            }
        }

        /// <summary>
        /// Drops a sheet out of whatever group holds it, and dissolves an assembly that falls
        /// below two members — see <see cref="Remove"/> for the argument, including why the
        /// survivor is left at the origin and why that is the caller's to fix.
        /// </summary>
        static void LeaveGroup(Board board, SheetId id)
        {
            Group group;
            if (!TryFindGroupOf(board, id, out group)) return;

            group.Members.Remove(id);
            if (group.Members.Count >= 2) return;

            if (group.Members.Count == 1)
            {
                SheetId survivor = group.Members[0];
                if (board.Placed.ContainsKey(survivor))
                    board.Placed[survivor] = Placement.Laid(0.0, 0.0, 0.0);
            }
            Destroy(board, group);
        }

        /// <summary>Forgets a group. <c>NextGroupId</c> is deliberately not rewound: an id
        /// names one assembly for the life of the board, so a stale reference fails rather than
        /// hitting whatever was made next.</summary>
        static void Destroy(Board board, Group group)
        {
            board.Groups.Remove(group.Id);
            board.GroupOrder.Remove(group.Id);
        }

        static GroupRecord RecordOf(Group group)
        {
            return new GroupRecord(group.Id, group.RotationDeg, group.OffsetX, group.OffsetY,
                                   group.Office, group.WholeIsland, group.OnTable,
                                   group.Members.ToArray());
        }

        static int GroupIdOf(Board board, SheetId id)
        {
            Placement placement;
            if (board.Placed.TryGetValue(id, out placement) && placement.Grouped)
                return placement.GroupId;

            Group group;
            return TryFindGroupOf(board, id, out group) ? group.Id : 0;
        }

        /// <summary>
        /// Finds the group holding a sheet by walking the membership lists.
        ///
        /// <para>A scan, not an index. A board holds at most three groups on island 0 and its
        /// largest survey has eleven sheets (§6), so the worst case is a few dozen struct
        /// comparisons, and the case that is actually hot — an on-table member — never reaches
        /// here because <see cref="GroupIdOf(Board,SheetId)"/> answers it from the placement
        /// first. A <c>Dictionary&lt;SheetId,int&gt;</c> beside the group table would be one
        /// more thing to keep in step across park, merge and dissolve, for a lookup nobody is
        /// waiting on.</para>
        /// </summary>
        static bool TryFindGroupOf(Board board, SheetId id, out Group found)
        {
            for (int i = 0; i < board.GroupOrder.Count; i++)
            {
                Group group = board.Groups[board.GroupOrder[i]];
                for (int m = 0; m < group.Members.Count; m++)
                {
                    if (!group.Members[m].Equals(id)) continue;
                    found = group;
                    return true;
                }
            }
            found = null;
            return false;
        }

        bool TryFindGroup(string tableId, int groupId, out Board board, out Group group)
        {
            group = null;
            board = null;
            return IsUsableId(tableId)
                && groupId != 0
                && boards.TryGetValue(tableId, out board)
                && board.Groups.TryGetValue(groupId, out group);
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
