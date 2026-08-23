using System.Collections.Generic;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// Which sheets have entered the world, per island. <b>A different memory structure from
    /// the island itself, and it has to be.</b>
    ///
    /// <para>An <see cref="Generation.Island"/> is a pure function of its seed (R1.1) and
    /// nothing geometric is ever persisted (R1.11, R3.1) — the island is thrown away and
    /// regenerated on demand, identically, forever. So it is exactly the wrong place to
    /// record a fact about the player's collection: the moment issuance lived on the island
    /// object, it would either be lost on the next regeneration or would have to be persisted,
    /// and persisting it would make the island something other than a function of its seed.</para>
    ///
    /// <para>The ledger holds only identities. It is small, flat, trivially serialisable, and
    /// it is what makes R2.10 — every sheet in the collection is unique, no duplicates, no
    /// reprints — an enforceable rule rather than an intention. R2.10b follows directly: a
    /// slot is binary because issuance is.</para>
    ///
    /// <para><b>It also answers questions, not just enforces one.</b> The ledger is the only
    /// thing that knows what the archive has met, so it is where a list of islands and their
    /// progress has to come from; see <see cref="IslandHolding"/>. Two derived scalars —
    /// an island's name and its sheet count — are <i>memos</i>, written by
    /// <see cref="Describe"/> when an island is generated anyway. They are pure functions of
    /// the seed, so they are never authoritative, never worth persisting, and always safe to
    /// recompute; they exist only so that listing thirty islands does not cost thirty
    /// generations. Everything else here is fact.</para>
    ///
    /// <para><b>Order is kept on purpose.</b> Islands come back in the order they were drawn
    /// and sheets in the order they were issued, because a screen that reordered itself
    /// between two openings would be unreadable — and a <c>HashSet</c>'s enumeration order is
    /// not a promise. Issuance still tests through the set; the list is only the order.</para>
    ///
    /// <para><b>Not thread-safe.</b> Reads and writes happen on the main thread; the picker
    /// runs on a worker and gets <see cref="Snapshot"/> instead.</para>
    ///
    /// <para><b>Not yet: how many sheets are filed correctly.</b> That count belongs here,
    /// per island, beside issuance — it is the same shape of fact (a set of
    /// <see cref="SheetId"/>, this time the ones in their right slot) and it will arrive with
    /// the racks. See <see cref="IslandHolding"/> for why it is absent rather than stubbed.</para>
    ///
    /// <para>Deliberately free of UnityEngine — this is the half that can move to a headless
    /// assembly and be covered by <c>Tools/run-acceptance.sh</c> the day it needs tests.
    /// <see cref="SheetLedger"/> is the scene-facing component around it, and holds the
    /// serialised state; everything that is actually logic lives here.</para>
    /// </summary>
    public sealed class SheetLedgerStore
    {
        /// <summary>One island's row. Mutable and private: the world outside gets
        /// <see cref="IslandHolding"/>, which is a copy and cannot be written back.</summary>
        sealed class Entry
        {
            public readonly ulong Seed;
            public int Index = -1;          // collection index (R1.1), -1 until known
            public string Name;             // memo; null until described
            public int TotalSheets;         // memo; 0 until counted

            public readonly HashSet<SheetId> Issued = new HashSet<SheetId>();
            public readonly List<SheetId> IssueOrder = new List<SheetId>();

            public Entry(ulong seed) { Seed = seed; }
        }

        readonly Dictionary<ulong, Entry> islands = new Dictionary<ulong, Entry>();
        readonly List<ulong> drawOrder = new List<ulong>();

        int totalIssued;

        // ---- recording -------------------------------------------------------------------

        /// <summary>
        /// Notes that an island exists, before any sheet of it has been issued. Called when an
        /// index is reserved: an island the player has opened and found nothing in is still an
        /// island the archive has met, and a list that skipped it would be wrong in the one
        /// case the player is most likely to ask about.
        ///
        /// <para><paramref name="islandIndex"/> may be -1 when the caller does not know it —
        /// a seed reached through editor tooling, say. A real index later overwrites -1;
        /// nothing overwrites a real index, because R1.1 makes it a property of the seed and a
        /// second answer would mean one of them is wrong.</para>
        /// </summary>
        public void Record(ulong islandSeed, int islandIndex)
        {
            Entry entry = Ensure(islandSeed);
            if (entry.Index < 0) entry.Index = islandIndex;
        }

        /// <summary>
        /// Fills in the two memos — what the island is called and how many sheets it has —
        /// for an island that has just been generated. Cheap at the call site and expensive
        /// anywhere else, which is the whole reason the ledger keeps them.
        ///
        /// <para>Refuses to un-know: a call with no name or a zero count leaves what is
        /// already there. Overwriting with a real value is fine — both are functions of the
        /// seed, so a second answer is the same answer.</para>
        /// </summary>
        public void Describe(ulong islandSeed, string name, int totalSheets)
        {
            Entry entry = Ensure(islandSeed);
            if (!string.IsNullOrEmpty(name)) entry.Name = name;
            if (totalSheets > 0) entry.TotalSheets = totalSheets;
        }

        /// <summary>True if this call issued it; false if it was already out.</summary>
        public bool MarkIssued(SheetId id)
        {
            Entry entry = Ensure(id.IslandSeed);
            if (!entry.Issued.Add(id)) return false;

            entry.IssueOrder.Add(id);
            totalIssued++;
            return true;
        }

        // ---- reading ---------------------------------------------------------------------

        public bool IsIssued(SheetId id)
        {
            Entry entry;
            return islands.TryGetValue(id.IslandSeed, out entry) && entry.Issued.Contains(id);
        }

        public int IssuedCount(ulong islandSeed)
        {
            Entry entry;
            return islands.TryGetValue(islandSeed, out entry) ? entry.Issued.Count : 0;
        }

        /// <summary>
        /// Every sheet of this island that has been issued, oldest first. The live list, not a
        /// copy — read it, do not keep it: it grows under any caller that holds it across an
        /// opening. Empty (and shared) for an island nothing has been issued from.
        ///
        /// <para>Issued, <b>not</b> lying on the floor. A sheet is issued once and stays
        /// issued wherever the player carries it; what is physically in the room is a
        /// different question, and <c>SheetSpawner.AllInScene</c> answers that one by asking
        /// the scene.</para>
        /// </summary>
        public IReadOnlyList<SheetId> IssuedSheets(ulong islandSeed)
        {
            Entry entry;
            return islands.TryGetValue(islandSeed, out entry) ? entry.IssueOrder : Empty;
        }

        /// <summary>
        /// A copy, because the picker runs on a worker thread while the main thread may still
        /// be writing. Cheap: a few hundred structs at worst.
        /// </summary>
        public HashSet<SheetId> Snapshot(ulong islandSeed)
        {
            Entry entry;
            return islands.TryGetValue(islandSeed, out entry)
                ? new HashSet<SheetId>(entry.Issued)
                : new HashSet<SheetId>();
        }

        /// <summary>Every island the archive has met, in the order it met them. The live
        /// list — read it, do not keep it.</summary>
        public IReadOnlyList<ulong> KnownIslands { get { return drawOrder; } }

        public int KnownIslandCount { get { return islands.Count; } }

        /// <summary>Sheets issued across the whole collection.</summary>
        public int TotalIssuedCount { get { return totalIssued; } }

        /// <summary>What the archive holds of one island. False for an island it has never
        /// met — which is not the same as an island with nothing issued.</summary>
        public bool TryGetHolding(ulong islandSeed, out IslandHolding holding)
        {
            Entry entry;
            if (!islands.TryGetValue(islandSeed, out entry))
            {
                holding = default(IslandHolding);
                return false;
            }
            holding = HoldingOf(entry);
            return true;
        }

        /// <summary>
        /// Every island, in draw order, as one list — the shape a screen listing the
        /// collection wants. A fresh list each call, so the caller may sort or filter it;
        /// it is a few dozen structs, not a thing to be careful with.
        /// </summary>
        public List<IslandHolding> Holdings()
        {
            var all = new List<IslandHolding>(drawOrder.Count);
            for (int i = 0; i < drawOrder.Count; i++)
                all.Add(HoldingOf(islands[drawOrder[i]]));
            return all;
        }

        // ---- internals -------------------------------------------------------------------

        static readonly SheetId[] Empty = new SheetId[0];

        static IslandHolding HoldingOf(Entry entry)
        {
            return new IslandHolding(entry.Seed, entry.Index, entry.Name,
                                     entry.Issued.Count, entry.TotalSheets);
        }

        Entry Ensure(ulong islandSeed)
        {
            Entry entry;
            if (islands.TryGetValue(islandSeed, out entry)) return entry;

            entry = new Entry(islandSeed);
            islands[islandSeed] = entry;
            drawOrder.Add(islandSeed);
            return entry;
        }
    }
}
