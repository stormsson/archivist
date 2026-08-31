using System;
using System.Collections.Generic;

namespace Archivist.Harness
{
    /// <summary>
    /// The list of checks this harness knows how to run, and how a command line picks a subset.
    ///
    /// <para>This used to be a chain of <c>if (mode == "all" || mode == "fast")</c> blocks in
    /// <see cref="Program"/>. Three things were wrong with that. A new check had to be wired into
    /// every mode it belonged to, and forgetting one was silent. The mode names lied — "fast" ran
    /// A2's hundred island generations and took two minutes of the full run's three. And there was
    /// no way to ask for ONE check: fixing A8 meant sitting through A2, A4, A5, A6, C2, C3 and C4
    /// first, which is roughly two minutes per iteration for a check that takes six seconds.</para>
    ///
    /// <para>So the checks are data now. Every one of them names its own group and its own cost,
    /// a selector may name a group or a single check, and a selector prefixed with <c>-</c>
    /// removes what it names from whatever was selected before it. Nothing here decides what a
    /// check MEANS — the suites are untouched; this only decides which ones get called.</para>
    /// </summary>
    public static class Suite
    {
        /// <summary>
        /// What running one check costs, coarsely. Real elapsed time is measured and printed by
        /// the runner; this is the a-priori hint <c>--list</c> shows, so that someone choosing
        /// what to run does not have to run it first to find out.
        ///
        /// <para>The unit that matters is island generations: one is ~460 ms on this machine
        /// (A8 measures it, and see the ⚠ note on <c>Acceptance.A8SheetRecontourBudgetMs</c> —
        /// the reference machine records ~118 ms). Everything else the suites do is noise beside
        /// it, so the tag below is essentially "how many islands does this build".</para>
        /// </summary>
        public enum Cost
        {
            /// <summary>A couple of islands at most. Under a second or two.</summary>
            Quick,
            /// <summary>Tens of islands. Ten to twenty seconds.</summary>
            Slow,
            /// <summary>A hundred islands or more. Half a minute upwards.</summary>
            VerySlow
        }

        /// <summary>Everything a check might need from the command line. Passed to every check so
        /// the registry can stay a flat array of <c>Action</c>s; most checks ignore all of it.</summary>
        public sealed class Options
        {
            /// <summary>B5's PNG destination. Null means "let B5 choose".</summary>
            public string OutDir;

            /// <summary>How many seeds the 50-seed metrics (A7, C6) sweep. Lowering it makes the
            /// numbers less trustworthy — they are distributions over characters, and at 10 seeds
            /// an Atoll bucket can hold one island — so it is a knob for a quick look, not for a
            /// recorded measurement.</summary>
            public int Seeds = 50;
        }

        /// <summary>One runnable check.</summary>
        public sealed class Check
        {
            public readonly string Id;        // "A2" — how the command line names it
            public readonly string Group;     // "gen" — the section it belongs to
            public readonly string What;      // one line, for --list
            public readonly Cost Cost;
            /// <summary>True if this check judges a criterion and can fail the run on it. False
            /// for the measurements, which only print. A run made entirely of ungated checks
            /// cannot pass, and the summary says so rather than claiming a pass it never tested.
            /// (B5 is ungated and still calls <see cref="Report.Fail"/> once — when it cannot
            /// create its output directory. That is an I/O failure, not a criterion.)</summary>
            public readonly bool Gates;
            public readonly Action<Options> Run;

            public Check(string id, string group, string what, Cost cost, bool gates, Action<Options> run)
            { Id = id; Group = group; What = what; Cost = cost; Gates = gates; Run = run; }
        }

        /// <summary>A group of checks, and the banner printed above them.</summary>
        public sealed class Group
        {
            public readonly string Name;
            public readonly string Banner;    // printed once, before the group's first check
            public readonly string What;      // one line, for --list

            public Group(string name, string banner, string what)
            { Name = name; Banner = banner; What = what; }
        }

        // ------------------------------------------------------------------ the registry
        // Order here is run order, always, whatever order the selectors arrived in. Two runs that
        // ask for the same checks must produce the same transcript, or diffing them is useless.

        public static readonly Group[] Groups =
        {
            new Group("gen",      "-- POC-01 generation (§13) ----------------------------",
                                  "the generator's own gates: determinism, seams, numbering, blank sheets, budget"),
            new Group("poi",      "-- POC-03 points of interest --------------------------",
                                  "POI determinism, the placeability floor, detail-sheet numbering"),
            new Group("render",   "-- POC-02 rendering (§11) -----------------------------",
                                  "render determinism and cross-rect coherence"),
            new Group("save",     "-- the archive file (cartography table §9) ------------",
                                  "the save: round trip, order, the ledger check, damaged files, the room"),
            new Group("metrics",  "-- measurements (reported, never gated) ---------------",
                                  "the 50-seed sweeps: sheet economy, POI density, render timing"),
            new Group("sweep",    "-- POC-02 resolution sweep ----------------------------",
                                  "B5 only. Writes megabytes of PNG for eyeballing; see --out"),
            new Group("describe", "-- island descriptions --------------------------------",
                                  "prints six islands. Not a check; never part of `all`"),
        };

        public static readonly Check[] All =
        {
            new Check("A2", "gen", "same seed, identical island, 100 times (§13.2)",
                      Cost.VerySlow, true,  o => Acceptance.A2_Determinism()),
            new Check("A3", "gen", "adjacent contour rects do not tear (§13.3)",
                      Cost.Quick,    true,  o => Acceptance.A3_NoSeams()),
            new Check("A4", "gen", "sheet numbers are exactly 1..N (§13.4)",
                      Cost.Slow,     true,  o => Acceptance.A4_Numbering()),
            new Check("A5", "gen", "no blank sheets, plus the A5b thin-sheet metric (§13.5)",
                      Cost.Slow,     true,  o => Acceptance.A5_NoBlankSheets()),
            new Check("A6", "gen", "overlapping cross-office sheets share a class (§13.6)",
                      Cost.Slow,     false, o => Acceptance.A6_SharedClassCoverage()),
            new Check("A8", "gen", "island generation and sheet re-contour budgets (§13.8)",
                      Cost.Slow,     true,  o => Acceptance.A8_Performance()),

            new Check("C2", "poi", "same seed, identical POIs and detail sheets",
                      Cost.VerySlow, true,  o => Poc03Acceptance.C2_Determinism()),
            new Check("C3", "poi", "every detail sheet carries a feature besides its own POI",
                      Cost.Slow,     true,  o => Poc03Acceptance.C3_PlaceabilityFloor()),
            new Check("C4", "poi", "survey and detail numbering, positional form",
                      Cost.Slow,     true,  o => Poc03Acceptance.C4_Numbering()),

            new Check("B2", "render", "100 renders of one request byte-identical (§11)",
                      Cost.Quick,    true,  o => Poc02Acceptance.B2_Determinism()),
            new Check("B3", "render", "two rects, different rotation and resolution, agree (§11)",
                      Cost.Quick,    true,  o => Poc02Acceptance.B3_Coherence()),

            new Check("S1", "save", "written, read and written again is the same file",
                      Cost.Quick,    true,  o => SaveAcceptance.S1_RoundTrip()),
            new Check("S2", "save", "the room: binders, contents, where each lies, loose paper",
                      Cost.Quick,    true,  o => SaveAcceptance.S2_Room()),
            new Check("S3", "save", "every issued sheet is somewhere, and somewhere once",
                      Cost.Quick,    true,  o => SaveAcceptance.S3_EveryIssuedSheetIsSomewhere()),

            new Check("A7", "metrics", "sheet economy over N seeds (§13.7, D2, D5)",
                      Cost.VerySlow, false, o => Acceptance.A7_SheetEconomy(o.Seeds)),
            new Check("C6", "metrics", "POI density and distribution over N seeds",
                      Cost.VerySlow, false, o => Poc03Acceptance.C6_Density(o.Seeds)),
            new Check("B4", "metrics", "render timing (T4.3 — measured, never a budget)",
                      Cost.Slow,     false, o => Poc02Acceptance.B4_Performance()),

            new Check("B5", "sweep", "the resolution ladder, exported as PNGs",
                      Cost.Quick,    false, o => Poc02Acceptance.B5_ResolutionSweep(o.OutDir)),

            new Check("describe", "describe", "six islands, printed",
                      Cost.Quick,    false, o => { for (int i = 0; i < 6; i++) Describe.Print(Report.Collection, i); }),
        };

        /// <summary>
        /// Selector shorthands. <c>gate</c> is the default because it is the answer to "did I
        /// break anything" — every check that can fail, and nothing that only prints.
        ///
        /// <para><c>fast</c> and <c>poc02</c> are kept because they were the old mode names and
        /// are in <c>docs/generation_for_agents.md</c>. <c>fast</c> was always a misnomer: it is
        /// the slowest group of the three.</para>
        /// </summary>
        static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "all",   new[] { "gen", "poi", "render", "save", "metrics", "sweep" } },
            { "gate",  new[] { "gen", "poi", "render", "save" } },
            { "fast",  new[] { "gen", "poi" } },              // the old mode of that name
            { "poc02", new[] { "render", "metrics:B4", "sweep" } },
        };

        /// <summary>
        /// Turns selectors into a run plan, in registry order.
        ///
        /// <para>A selector is a group name, a check id, or an alias; prefix it with <c>-</c> to
        /// subtract. Selectors apply left to right, so <c>all -metrics -A2</c> reads the way it
        /// looks. An unknown selector is an error rather than an empty run — a typo that silently
        /// runs nothing and exits 0 is the failure mode this whole file exists to remove.</para>
        /// </summary>
        public static bool Resolve(List<string> selectors, out List<Check> plan, out string error)
        {
            plan = null;
            error = null;
            var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < selectors.Count; i++)
            {
                string s = selectors[i];
                bool remove = s.Length > 1 && s[0] == '-';
                if (remove) s = s.Substring(1);

                List<string> ids = Expand(s);
                if (ids == null) { error = "unknown selector \"" + selectors[i] + "\""; return false; }
                for (int k = 0; k < ids.Count; k++)
                {
                    if (remove) chosen.Remove(ids[k]);
                    else chosen.Add(ids[k]);
                }
            }

            plan = new List<Check>();
            for (int i = 0; i < All.Length; i++)
                if (chosen.Contains(All[i].Id)) plan.Add(All[i]);
            return true;
        }

        /// <summary>One selector to the check ids it names, or null if it names nothing. Aliases
        /// expand recursively so an alias may list groups, ids, or other aliases.</summary>
        static List<string> Expand(string s)
        {
            var ids = new List<string>();

            string[] alias;
            if (Aliases.TryGetValue(s, out alias))
            {
                for (int i = 0; i < alias.Length; i++)
                {
                    // "metrics:B4" — one check, named with its group for readability.
                    string a = alias[i];
                    int colon = a.IndexOf(':');
                    if (colon >= 0) a = a.Substring(colon + 1);
                    List<string> inner = Expand(a);
                    if (inner == null) return null;
                    ids.AddRange(inner);
                }
                return ids;
            }

            for (int i = 0; i < Groups.Length; i++)
                if (string.Equals(Groups[i].Name, s, StringComparison.OrdinalIgnoreCase))
                {
                    for (int k = 0; k < All.Length; k++)
                        if (All[k].Group == Groups[i].Name) ids.Add(All[k].Id);
                    return ids;
                }

            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Id, s, StringComparison.OrdinalIgnoreCase))
                { ids.Add(All[i].Id); return ids; }

            return null;
        }

        /// <summary>The banner for a group, or null if it has none.</summary>
        public static string BannerFor(string group)
        {
            for (int i = 0; i < Groups.Length; i++)
                if (Groups[i].Name == group) return Groups[i].Banner;
            return null;
        }
    }
}
