using System.Collections.Generic;

namespace Archivist.Generation.Naming
{
    /// <summary>
    /// §9. Morpheme tables for one per-island phonology.
    /// <para>
    /// These are <b>data for a generator</b>, not authored content: the supply of islands is
    /// unbounded (R1.2), so the tables must combine freely rather than encode specific places.
    /// Each island draws exactly one phonology, so its own names cohere with each other and
    /// differ audibly from the next island's.
    /// </para>
    /// <para>
    /// Three phonologies, kept deliberately unlabelled (A / B / C). They differ in their
    /// suffix register, which is what the ear actually hears: A is soft and vowel-carrying,
    /// B is hard and coastal, C is flat and inland-plain.
    /// </para>
    /// <para>
    /// ASCII only, by contract with <c>Hash.Fnv1a64</c> (§4.2). The apostrophe in "Nor'" is
    /// ASCII 0x27 and is fine; accented characters are not.
    /// </para>
    /// <para>
    /// <b>Stability note (§4.3).</b> The tables and the order of <see cref="All"/> are part of
    /// the reproducibility contract. Adding a new stream never reshuffles names, but editing a
    /// table - or inserting a fourth phonology - re-rolls every island's names. Geometry is
    /// untouched either way: naming draws from its own sub-streams and feeds nothing back.
    /// </para>
    /// </summary>
    public sealed class Phonology
    {
        /// <summary>Neutral identifier, "A" / "B" / "C". Debug UI only; never parsed.</summary>
        public string Id;

        /// <summary>~24 name-initial elements (§9). Capitalised; the generator does not re-case them.</summary>
        public string[] Roots;

        /// <summary>~10 place suffixes, lower case. Used for the island and for settlements.</summary>
        public string[] Suffixes;

        /// <summary>
        /// Peak suffixes, lower case. A distinct register so a peak name reads as a peak
        /// ("Braefell") without the spot height, which lives in <c>Peak.SpotHeightM</c> (§7.1).
        /// Kept disjoint from <see cref="Suffixes"/> so a peak can never collide with a village.
        /// </summary>
        public string[] PeakSuffixes;

        /// <summary>
        /// Prefixed words, applied to a minority of names only ("Little Braeness", "Nor' Ormvoe").
        /// Also the first deterministic tie-break when uniqueness retry runs out (§9).
        /// </summary>
        public string[] Qualifiers;

        public Phonology()
        {
        }

        public Phonology(string id, string[] roots, string[] suffixes, string[] peakSuffixes, string[] qualifiers)
        {
            Id = id;
            Roots = roots;
            Suffixes = suffixes;
            PeakSuffixes = peakSuffixes;
            Qualifiers = qualifiers;
        }

        static readonly Phonology[] TheThree = new Phonology[] { MakeA(), MakeB(), MakeC() };

        /// <summary>
        /// The fixed table of phonologies, in a fixed order. Index is the contract: the island
        /// picks by index from <c>Streams.For(seed, "names")</c>, so this list is never sorted,
        /// filtered, or built from a dictionary (§4.1).
        /// </summary>
        public static IReadOnlyList<Phonology> All
        {
            get { return TheThree; }
        }

        // ------------------------------------------------------------------
        // A - soft register. Vowel-carrying suffixes, consonant-final roots.
        // ------------------------------------------------------------------
        static Phonology MakeA()
        {
            return new Phonology(
                "A",
                new string[]
                {
                    "Ard", "Auch", "Bal", "Barr", "Cairn", "Clach",
                    "Corr", "Craig", "Cul", "Dal", "Drum", "Dun",
                    "Garv", "Glen", "Inver", "Kil", "Kin", "Knock",
                    "Lag", "Mor", "Ross", "Strath", "Tarb", "Tor"
                },
                new string[]
                {
                    "more", "beg", "aig", "vaig", "nish",
                    "bost", "dour", "lish", "shader", "ary"
                },
                new string[]
                {
                    "ven", "val", "carn", "torr", "stac", "sgor"
                },
                new string[]
                {
                    "Little", "Meikle", "Upper", "Nether", "Old", "West"
                });
        }

        // ------------------------------------------------------------------
        // B - hard coastal register. The worked example in §9 is this one:
        // Sten + holm, Kirk + wick, Orm + voe, Little + Brae + ness, Brae + fell.
        // ------------------------------------------------------------------
        static Phonology MakeB()
        {
            return new Phonology(
                "B",
                new string[]
                {
                    "Kirk", "Brae", "Sten", "Vald", "Orm", "Berg",
                    "Eyr", "Fjar", "Gard", "Grim", "Haf", "Hald",
                    "Kald", "Rask", "Sand", "Sker", "Skal", "Stor",
                    "Svin", "Thors", "Trond", "Uls", "Vig", "Hval"
                },
                new string[]
                {
                    "wick", "holm", "ness", "voe", "garth", "sund",
                    "sta", "geo", "quoy", "toft", "firth", "by"
                },
                new string[]
                {
                    "fell", "tind", "hamar", "klett", "ward", "nup"
                },
                new string[]
                {
                    "Nor'", "Sud'", "Muckle", "Little", "Auld", "Wester"
                });
        }

        // ------------------------------------------------------------------
        // C - flat inland register. Plainer, drier, no coastal vocabulary.
        // ------------------------------------------------------------------
        static Phonology MakeC()
        {
            return new Phonology(
                "C",
                new string[]
                {
                    "Ash", "Barn", "Black", "Bracken", "Bram", "Cold",
                    "Drift", "Elm", "Grey", "Hale", "Harrow", "Hart",
                    "Holt", "Marsh", "Oak", "Rush", "Salt", "Shaw",
                    "Slade", "Stone", "Thorn", "Water", "Whin", "Wray"
                },
                new string[]
                {
                    "ton", "ham", "ford", "bury", "wold",
                    "thorpe", "worth", "combe", "mouth", "stead"
                },
                new string[]
                {
                    "down", "tor", "hill", "edge", "beacon", "barrow"
                },
                new string[]
                {
                    "Little", "Great", "Upper", "Nether", "Old", "Far"
                });
        }
    }
}
