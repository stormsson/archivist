using Archivist.Generation.Determinism;

namespace Archivist.Generation.Field
{
    /// <summary>§5.1. Everything reproducible from Seed alone (R1.1, R1.11).</summary>
    public readonly struct IslandParams
    {
        public readonly ulong Seed;
        public readonly IslandCharacter Character;
        public readonly double DomainMetres;
        public readonly double NominalRadius;
        public readonly double MaxElevation;
        public readonly double SeaLevel;

        public IslandParams(ulong seed, IslandCharacter character, double domainMetres,
                            double nominalRadius, double maxElevation, double seaLevel)
        {
            Seed = seed; Character = character; DomainMetres = domainMetres;
            NominalRadius = nominalRadius; MaxElevation = maxElevation; SeaLevel = seaLevel;
        }

        /// <summary>u, the island-scale unit (D1 / §7.4). Pinned to NominalRadius, never the land bbox.</summary>
        public double ServiceRadius { get { return NominalRadius * Tuning.ServiceRadiusFrac; } }

        /// <summary>Per-character recipe constants (§5.3).</summary>
        public static double MaxElevationFor(IslandCharacter c)
        {
            switch (c)
            {
                case IslandCharacter.Mountainous: return 620.0;
                case IslandCharacter.Fjorded:     return 540.0;
                default:                          return 90.0;
            }
        }

        public static double GainFor(IslandCharacter c)
        {
            switch (c)
            {
                case IslandCharacter.Mountainous: return 1.15;
                case IslandCharacter.Fjorded:     return 1.05;
                default:                          return 0.95;
            }
        }

        public static double BiasFor(IslandCharacter c)
        {
            return c == IslandCharacter.Mountainous ? 0.02 : 0.00;
        }

        public static int PeakCapFor(IslandCharacter c)
        {
            switch (c)
            {
                case IslandCharacter.Mountainous: return 9;
                case IslandCharacter.Fjorded:     return 7;
                default:                          return 2;
            }
        }

        /// <summary>Settlement count range, inclusive-exclusive upper (§7.2 step 5).</summary>
        public static void SettlementRangeFor(IslandCharacter c, out int minInc, out int maxExc)
        {
            switch (c)
            {
                case IslandCharacter.Mountainous: minInc = 4; maxExc = 8; break;   // 4-7
                case IslandCharacter.Fjorded:     minInc = 5; maxExc = 10; break;  // 5-9
                default:                          minInc = 1; maxExc = 4; break;   // 1-3
            }
        }

        /// <summary>
        /// POC-03 P1.4 — POI count range, inclusive-exclusive upper, drawn from
        /// <c>Streams.For(seed, "poi")</c> (spec §1.3 step 4). Values in
        /// <see cref="Tuning"/>; this switch is the per-character recipe, exactly as
        /// <see cref="SettlementRangeFor"/> and <see cref="PeakCapFor"/> are.
        /// <para>The atoll minimum is 0 on purpose: "an island with no POIs at all is a
        /// legitimate outcome" (P1.4), and an atoll has neither peaks nor high ground for most
        /// of the table.</para>
        /// </summary>
        public static void PoiRangeFor(IslandCharacter c, out int minInc, out int maxExc)
        {
            switch (c)
            {
                case IslandCharacter.Mountainous:
                    minInc = Tuning.PoiCountMountainousMin; maxExc = Tuning.PoiCountMountainousMax; break;
                case IslandCharacter.Fjorded:
                    minInc = Tuning.PoiCountFjordedMin; maxExc = Tuning.PoiCountFjordedMax; break;
                default:
                    minInc = Tuning.PoiCountAtollMin; maxExc = Tuning.PoiCountAtollMax; break;
            }
        }

        /// <summary>Derives the full parameter set from a seed. The only entry point (R1.1).</summary>
        public static IslandParams FromSeed(ulong islandSeed, IslandCharacter? forced = null)
        {
            IslandCharacter character;
            if (forced.HasValue)
            {
                character = forced.Value;
            }
            else
            {
                var rc = Streams.For(islandSeed, "character");
                character = (IslandCharacter)rc.Range(0, 3);
            }

            var rr = Streams.For(islandSeed, "radius");
            double jitter = rr.Range(-Tuning.NominalRadiusJitter, Tuning.NominalRadiusJitter);
            double nominal = Tuning.DomainMetres * Tuning.NominalRadiusFrac * (1.0 + jitter);

            return new IslandParams(islandSeed, character, Tuning.DomainMetres, nominal,
                                    MaxElevationFor(character), Tuning.SeaLevel);
        }
    }
}
