namespace Archivist.Building.Collection
{
    /// <summary>
    /// What the archive holds of one island, as a value: which island it is, how much of it
    /// has come out of a crate, and how much there is in total. One row of whatever screen
    /// eventually lists the collection.
    ///
    /// <para><b>A snapshot, not a handle.</b> It is taken from the ledger and never watches
    /// it — a caller that keeps one across an opening is reading yesterday's count. UI asks
    /// again; it costs a dictionary lookup.</para>
    ///
    /// <para><b>Why <see cref="Total"/> can be unknown.</b> The number of sheets an island has
    /// is a pure function of its seed (R1.1), but finding it means generating the island —
    /// about a third of a second. The ledger is told it when the island is drawn and not
    /// before, so an island whose entry exists but whose sheets have never been counted
    /// reports <see cref="TotalKnown"/> false. A caller that needs the number anyway
    /// regenerates through <c>IslandGenerator</c> and calls <c>SheetLedger.Describe</c>;
    /// nothing here will do it silently, because a list of thirty islands would then be thirty
    /// generations deep and the room would stop.</para>
    ///
    /// <para><b>Not here yet: how many sheets are filed correctly.</b> That is the number the
    /// game is actually about (R2.10, and the shelving rules the archivist works to), and it
    /// belongs on this struct beside <see cref="Issued"/> the day the racks exist. It is
    /// deliberately absent rather than stubbed at zero: a field that always reads zero is
    /// indistinguishable from a real answer, and something would end up drawing "0% filed"
    /// next to a shelf that has never been implemented.</para>
    /// </summary>
    public readonly struct IslandHolding
    {
        /// <summary>The one thing that is actually persisted about an island (R1.11).</summary>
        public readonly ulong Seed;

        /// <summary>Its place in the collection (R1.1), or -1 if the ledger learned about
        /// this island from a sheet rather than from a drawing.</summary>
        public readonly int Index;

        /// <summary>Null until the island has been described. A memo of a pure function.</summary>
        public readonly string Name;

        /// <summary>Sheets of this island that have entered the world (R2.10).</summary>
        public readonly int Issued;

        /// <summary>Sheets this island has in all, or 0 if it has never been counted.</summary>
        public readonly int Total;

        public IslandHolding(ulong seed, int index, string name, int issued, int total)
        {
            Seed = seed;
            Index = index;
            Name = name;
            Issued = issued;
            Total = total;
        }

        public bool TotalKnown { get { return Total > 0; } }

        /// <summary>0..1, or -1 when the island has never been counted. Not 0: an island with
        /// nothing issued and an island nobody has counted are different states, and a UI
        /// showing an empty bar for both would be lying about one of them.</summary>
        public double IssuedFraction
        {
            get { return TotalKnown ? (double)Issued / Total : -1.0; }
        }

        /// <summary>0..100, or -1 when unknown. See <see cref="IssuedFraction"/>.</summary>
        public double IssuedPercent
        {
            get { return TotalKnown ? IssuedFraction * 100.0 : -1.0; }
        }

        /// <summary>Sheets still in the crates, or -1 when unknown.</summary>
        public int Remaining { get { return TotalKnown ? Total - Issued : -1; } }

        /// <summary>Every sheet of this island is out. R1.8/R2.9: a legitimate resting state,
        /// not an error — islands are finite even though the supply of them is not.</summary>
        public bool IsComplete { get { return TotalKnown && Issued >= Total; } }

        /// <summary>What to put on a label: the island's name once it has one, and its seed
        /// until then. An island always has something to be called.</summary>
        public string Title
        {
            get { return string.IsNullOrEmpty(Name) ? Seed.ToString("X16") : Name; }
        }

        public override string ToString()
        {
            return TotalKnown
                ? $"{Title} ({Seed:X16}) — {Issued}/{Total} issued ({IssuedPercent:0.#}%)"
                : $"{Title} ({Seed:X16}) — {Issued} issued, total unknown";
        }
    }
}
