namespace Archivist.Generation.Features
{
    /// <summary>
    /// POC-03 spec §1.1 — the type table, transcribed exactly.
    ///
    /// <para>Two families (P1.2) kept in ONE enum so a single
    /// <see cref="FeatureClass.Poi"/> covers both and the §8.3 matrix does not need two rows.
    /// <see cref="PoiKinds.IsRuin"/> distinguishes them where it matters.</para>
    ///
    /// <para><b>The enum order is load-bearing.</b> Spec §1.3 step 2 sorts candidates by
    /// <c>(kind index asc, x asc, y asc)</c>, so reordering these members changes which POIs
    /// an island gets. Append only.</para>
    /// </summary>
    public enum PoiKind
    {
        // natural oddities
        SeaArch = 0,
        // 1 was Stack, removed: the only kind sited offshore, so its detail sheet was
        // mostly open water and carried nothing to place it against. The value is left
        // as a gap deliberately — kind index is the primary key of the POI total order,
        // so renumbering the survivors would move every other POI on every island.
        CaveMouth = 2,
        Blowhole = 3,
        Spring = 4,
        ErraticBoulder = 5,
        LandmarkTree = 6,

        // ruins
        RuinedTower = 7,
        Cairn = 8,
        StandingStones = 9,
        RuinedChapel = 10,
        RuinedJetty = 11,
        Enclosure = 12
    }

    /// <summary>Helpers over <see cref="PoiKind"/>. No state, no iteration order (§4.1).</summary>
    public static class PoiKinds
    {
        /// <summary>
        /// How many kinds exist. Derived from <see cref="All"/> so the two cannot drift —
        /// it was a hand-maintained const, and removing one kind left it stale and crashed
        /// generation.
        /// </summary>
        public static int Count { get { return All.Length; } }

        /// <summary>
        /// One past the highest <see cref="PoiKind"/> VALUE — the size an array indexed by
        /// <c>(int)kind</c> must have. This is NOT <see cref="Count"/>: the enum carries a
        /// gap where Stack was removed, so the value range is wider than the member count.
        /// Raise it only if a kind is appended with a higher value.
        /// </summary>
        public const int IndexRange = 13;

        /// <summary>The first ruin. Everything at or after it is a human trace; everything
        /// before it is a natural oddity (spec §1.1's two comment blocks, made a rule).</summary>
        public const PoiKind FirstRuin = PoiKind.RuinedTower;

        /// <summary>Every kind in enum order — the order spec §1.3 step 2 sorts by. A stable
        /// array, never an enum reflection call, so nothing here depends on runtime
        /// metadata ordering.</summary>
        public static readonly PoiKind[] All =
        {
            PoiKind.SeaArch, PoiKind.CaveMouth, PoiKind.Blowhole,
            PoiKind.Spring, PoiKind.ErraticBoulder, PoiKind.LandmarkTree,
            PoiKind.RuinedTower, PoiKind.Cairn, PoiKind.StandingStones,
            PoiKind.RuinedChapel, PoiKind.RuinedJetty, PoiKind.Enclosure
        };

        /// <summary>Spec §1.1: ruins (human traces) versus natural oddities.</summary>
        public static bool IsRuin(this PoiKind kind) { return kind >= FirstRuin; }

        /// <summary>Display label for the debug window and the metrics dump.</summary>
        public static string Label(this PoiKind kind)
        {
            switch (kind)
            {
                case PoiKind.SeaArch:        return "sea arch";
                case PoiKind.CaveMouth:      return "cave mouth";
                case PoiKind.Blowhole:       return "blowhole";
                case PoiKind.Spring:         return "spring";
                case PoiKind.ErraticBoulder: return "erratic boulder";
                case PoiKind.LandmarkTree:   return "landmark tree";
                case PoiKind.RuinedTower:    return "ruined tower";
                case PoiKind.Cairn:          return "cairn";
                case PoiKind.StandingStones: return "standing stones";
                case PoiKind.RuinedChapel:   return "ruined chapel";
                case PoiKind.RuinedJetty:    return "ruined jetty";
                case PoiKind.Enclosure:      return "enclosure";
                default:                     return "poi";
            }
        }
    }
}
