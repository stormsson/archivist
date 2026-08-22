using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;

namespace Archivist.Generation.Naming
{
    /// <summary>
    /// §9. One phonology per island; every name on the island is built from it, so the island's
    /// names cohere with each other and differ from the next island's.
    /// <para>
    /// <b>Draw order is the contract.</b> Island name first, then settlements in feature order,
    /// then peaks in feature order - the caller attaches names to features positionally
    /// (§7.1 step 6, §7.2 step 6), so this order may never change.
    /// </para>
    /// <para>
    /// <b>Streams (§4.3).</b> Four purposes, never one linear stream:
    /// <c>"names"</c> picks the phonology, <c>"names.island"</c> the island name,
    /// <c>"names.settlements"</c> indexed per settlement, <c>"names.peaks"</c> indexed per peak.
    /// Indexing per feature means adding a settlement does not rename the others, and a
    /// uniqueness retry in one kind cannot disturb another kind.
    /// </para>
    /// <para>
    /// <b>Determinism (§4.1).</b> Randomness comes only from <see cref="Streams"/> /
    /// <see cref="Pcg32"/>. The <see cref="HashSet{T}"/> below is a uniqueness <i>lookup</i>
    /// only - it is never enumerated, so its internal (process-randomised) string hashing
    /// cannot reach the output.
    /// </para>
    /// </summary>
    public static class NameGenerator
    {
        /// <summary>
        /// Retry bound for in-island uniqueness (§9). Twenty-four fresh root+suffix draws;
        /// after that the two deterministic fallbacks below take over, so the generator can
        /// never hang, not even on a phonology with a handful of morphemes.
        /// </summary>
        const int ComposeAttempts = 24;

        /// <summary>Upper bound of the roman-numeral fallback. Unreachable in practice: at most
        /// 1 + settlements + peaks (&lt; 20) names exist per island, so a distinct numeral is
        /// always found within a couple of steps.</summary>
        const int RomanMax = 3999;

        // Qualifiers are a sprinkle, not a rule (§9): a minority of names carry one.
        // Naming has no entry in Tuning (§12); these live here with the tables they shape.
        const double IslandQualifierChance     = 0.08;
        const double SettlementQualifierChance = 0.20;
        const double PeakQualifierChance       = 0.10;

        /// <summary>
        /// The island's phonology (§9), from <c>Streams.For(seed, "names")</c>. Exposed so the
        /// debug UI (§11) can report which one an island drew; generation calls it internally.
        /// </summary>
        public static Phonology PhonologyFor(ulong islandSeed)
        {
            Pcg32 rng = Streams.For(islandSeed, StreamNames.Names);
            return Phonology.All[rng.Range(0, Phonology.All.Count)];
        }

        /// <summary>
        /// §9. Names for one island: the island itself, <paramref name="settlementCount"/>
        /// settlements (§7.2 - every settlement is named), and <paramref name="namedPeakCount"/>
        /// peaks (§7.1 - only the top <c>Tuning.PeakNamedCount</c> are named; the rest carry a
        /// spot height only). All unique within the island.
        /// <para>
        /// Peak names carry the peak suffix register, so "Braefell" reads as a peak. The height
        /// is <b>not</b> baked into the string - it is stored separately in <c>Peak.SpotHeightM</c>.
        /// </para>
        /// </summary>
        /// <param name="islandSeed">R1.1 island seed.</param>
        /// <param name="settlementCount">Number of settlements, in feature order.</param>
        /// <param name="namedPeakCount">Number of peaks to name, in feature order.</param>
        public static IslandNames Generate(ulong islandSeed, int settlementCount, int namedPeakCount)
        {
            if (settlementCount < 0) settlementCount = 0;
            if (namedPeakCount < 0) namedPeakCount = 0;

            Phonology phon = PhonologyFor(islandSeed);

            // Lookup only. Never enumerated (§4.1).
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);

            // 1. island
            Pcg32 rngIsland = Streams.For(islandSeed, StreamNames.NamesIsland);
            string island = Draw(ref rngIsland, phon, phon.Suffixes, IslandQualifierChance, used);

            // 2. settlements, in feature order
            string[] settlements = new string[settlementCount];
            for (int i = 0; i < settlementCount; i++)
            {
                Pcg32 rngTown = Streams.For(islandSeed, StreamNames.NamesSettlements, i);
                settlements[i] = Draw(ref rngTown, phon, phon.Suffixes, SettlementQualifierChance, used);
            }

            // 3. peaks, in feature order
            string[] peaks = new string[namedPeakCount];
            for (int i = 0; i < namedPeakCount; i++)
            {
                Pcg32 rngPeak = Streams.For(islandSeed, StreamNames.NamesPeaks, i);
                peaks[i] = Draw(ref rngPeak, phon, phon.PeakSuffixes, PeakQualifierChance, used);
            }

            return new IslandNames(island, settlements, peaks);
        }

        /// <summary>
        /// One name. Composes root + suffix, optionally prefixed by a qualifier, and retries on
        /// collision up to <see cref="ComposeAttempts"/> times (§9).
        /// <para>
        /// Then, deterministically and in table order: prefix each qualifier in turn; then append
        /// a roman numeral. Both are exhaustive over a finite set of distinct strings, so the
        /// method terminates on any phonology, however small.
        /// </para>
        /// </summary>
        static string Draw(ref Pcg32 rng, Phonology phon, IReadOnlyList<string> suffixes,
                           double qualifierChance, HashSet<string> used)
        {
            string stem = string.Empty;

            for (int attempt = 0; attempt < ComposeAttempts; attempt++)
            {
                string root = phon.Roots[rng.Range(0, phon.Roots.Length)];
                string suffix = suffixes[rng.Range(0, suffixes.Count)];
                stem = Join(root, suffix);

                // Draw order inside one name is fixed: root, suffix, qualifier chance,
                // qualifier index. The index is drawn only when the chance passes.
                string candidate = stem;
                if (phon.Qualifiers != null && phon.Qualifiers.Length > 0 && rng.NextDouble() < qualifierChance)
                {
                    candidate = phon.Qualifiers[rng.Range(0, phon.Qualifiers.Length)] + " " + stem;
                }

                if (used.Add(candidate)) return candidate;
            }

            // Fallback 1 - every qualifier in table order, no randomness.
            if (phon.Qualifiers != null)
            {
                for (int q = 0; q < phon.Qualifiers.Length; q++)
                {
                    string candidate = phon.Qualifiers[q] + " " + stem;
                    if (used.Add(candidate)) return candidate;
                }
            }

            // Fallback 2 - roman numeral. Distinct per n, so this always terminates.
            for (int n = 2; n <= RomanMax; n++)
            {
                string candidate = stem + " " + Roman(n);
                if (used.Add(candidate)) return candidate;
            }

            // Unreachable: fewer than 20 names exist per island against 3998 numerals.
            throw new InvalidOperationException("NameGenerator: uniqueness fallback exhausted.");
        }

        /// <summary>
        /// Joins a capitalised root to a lower-case suffix, ASCII only.
        /// <para>
        /// Two elisions keep the tables free to combine without producing "Westton" or
        /// "Braeaig": a trailing vowel is dropped before a leading vowel, and a repeated
        /// letter across the seam collapses to one. Both are pure string rules - no randomness,
        /// so the same pair always joins the same way.
        /// </para>
        /// Worked example (§9): Sten+holm = Stenholm, Kirk+wick = Kirkwick, Orm+voe = Ormvoe,
        /// Brae+ness = Braeness, Brae+fell = Braefell - none of them elide.
        /// </summary>
        static string Join(string root, string suffix)
        {
            if (string.IsNullOrEmpty(root)) return suffix;
            if (string.IsNullOrEmpty(suffix)) return root;

            string head = root;
            string tail = suffix;

            if (IsVowel(head[head.Length - 1]) && IsVowel(tail[0]))
            {
                head = head.Substring(0, head.Length - 1);
                if (head.Length == 0) return tail;
            }

            if (Lower(head[head.Length - 1]) == Lower(tail[0]))
            {
                tail = tail.Substring(1);
                if (tail.Length == 0) return head;
            }

            return head + tail;
        }

        static bool IsVowel(char c)
        {
            char l = Lower(c);
            return l == 'a' || l == 'e' || l == 'i' || l == 'o' || l == 'u';
        }

        /// <summary>ASCII lower-case. Explicit rather than culture-dependent (§4.1).</summary>
        static char Lower(char c)
        {
            return (c >= 'A' && c <= 'Z') ? (char)(c + 32) : c;
        }

        static readonly int[] RomanValues = new int[]
        {
            1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1
        };

        static readonly string[] RomanNumerals = new string[]
        {
            "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"
        };

        /// <summary>1..3999 as a roman numeral. Parallel arrays, fixed order, no dictionary (§4.1).</summary>
        static string Roman(int value)
        {
            if (value < 1) return "I";

            string result = string.Empty;
            int remaining = value;
            for (int i = 0; i < RomanValues.Length; i++)
            {
                while (remaining >= RomanValues[i])
                {
                    result += RomanNumerals[i];
                    remaining -= RomanValues[i];
                }
            }
            return result;
        }
    }
}
