using System;
using System.Collections.Generic;
using Archivist.Generation;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// Chooses which sheets a crate delivers. Pure, engine-free, and off the main thread.
    /// </summary>
    public static class SheetPicker
    {
        /// <summary>
        /// Up to <paramref name="count"/> sheets of this island that the ledger has not
        /// already issued, drawn without replacement — or <b>every</b> unissued sheet when
        /// <paramref name="count"/> is zero or less.
        ///
        /// <para>A sentinel rather than "pass a big number", because the callers that want
        /// everything cannot know how many that is without generating the island first, which
        /// is the third of a second this call exists to keep off the main thread. The draw is
        /// still made — the whole pool comes back shuffled, not in survey order — so a caller
        /// that deals the result into shares gets a mixed folder rather than one office's
        /// worth, which is the arrangement §4.3 describes.</para>
        ///
        /// <para>Drawing from the flattened set of every survey, rather than picking a survey
        /// and then sheets within it, is what makes a crate's contents mixed: sorting them by
        /// office is the player's first act, not the generator's (§4.3).</para>
        ///
        /// <para><b>The whole-island sheet (R2.2a) is not in the pool.</b> It is the one sheet
        /// that is an entry point rather than a document — R6.8a makes it the board's outline
        /// — so it is reserved for whatever hands it over deliberately, and a crate must not
        /// be able to deal it out as one more sheet among thirty.</para>
        ///
        /// <para><b>Why the pool and not the crate.</b> Dropping it at the far end —
        /// <c>MapCrate</c> filing, <c>BinderSheetSource</c> listing — is wrong because the draw
        /// would still have happened, so <c>SheetLedger.MarkIssued</c> would have marked the
        /// sheet issued and buried it in a binder that then refused to show it: losing the sheet,
        /// not reserving it. Excluded from the pool it is never drawn, so it stays permanently
        /// unissued and <c>MarkIssued</c> stays the single gate.</para>
        ///
        /// <para>It stays in <c>Island.TotalSheets</c>, so an island sits at 30/31 until that
        /// claim happens. That is accurate — the sheet exists and is still issuable — rather
        /// than a denominator that quietly disagrees with the survey it counts.</para>
        ///
        /// <para>The flag is read off the survey rather than inferred from scale, sheet count
        /// or office: <see cref="SurveySpec.IsWholeIsland"/> is what those would be guessing
        /// at, and the whole-island survey borrows one of the three offices, so office alone
        /// cannot tell them apart.</para>
        ///
        /// <para>Returns fewer than asked, or none, when the island is exhausted. That is a
        /// legitimate outcome, not an error: islands are finite even though the supply of
        /// islands is not (R1.2, R1.8).</para>
        /// </summary>
        public static List<Sheet> PickUnissued(Island island, int count,
                                               HashSet<SheetId> alreadyIssued, int drawSeed)
        {
            return PickUnissued(island, count, alreadyIssued, drawSeed, false);
        }

        /// <summary>
        /// The same, with <paramref name="includeChart"/> to put the island's whole-island
        /// chart (R2.2a) into the pool.
        ///
        /// <para><b>The chart has to be issuable now.</b> Q4.4 makes it the board's base and
        /// R6.8a will not open a board without it, so a chart nobody can be given is a board
        /// nobody can open. It goes into its own office's binder like any other plate — it is
        /// that office's work (Q4.4) — and it is drawn first rather than shuffled, because an
        /// island whose chart came last would be a stack of quarters with nothing to lay them
        /// on.</para>
        /// </summary>
        public static List<Sheet> PickUnissued(Island island, int count,
                                               HashSet<SheetId> alreadyIssued, int drawSeed,
                                               bool includeChart)
        {
            if (island == null) throw new ArgumentNullException(nameof(island));

            var pool = new List<Sheet>();
            Sheet chart = default(Sheet);
            bool haveChart = false;

            for (int s = 0; s < island.Surveys.Count; s++)
            {
                Survey survey = island.Surveys[s];

                if (survey.Spec.IsWholeIsland)
                {
                    // Held back from the shuffle either way: skipped entirely when it is not
                    // wanted, and prepended when it is, so it is never the plate an island runs
                    // out before reaching.
                    if (includeChart && survey.SheetCount > 0
                        && (alreadyIssued == null || !alreadyIssued.Contains(SheetId.Of(survey.Sheets[0]))))
                    {
                        chart = survey.Sheets[0];
                        haveChart = true;
                    }
                    continue;
                }

                for (int i = 0; i < survey.Sheets.Count; i++)
                {
                    Sheet sheet = survey.Sheets[i];
                    if (alreadyIssued == null || !alreadyIssued.Contains(SheetId.Of(sheet)))
                        pool.Add(sheet);
                }
            }

            int wanted = count <= 0 ? pool.Count : count;
            if (haveChart && wanted > 0) wanted--;

            var picked = new List<Sheet>(Math.Min(wanted, pool.Count) + 1);
            if (haveChart) picked.Add(chart);
            var rng = new Random(drawSeed);

            // Partial Fisher-Yates: swap a random survivor to the front, take it, shrink.
            for (int taken = 0; taken < wanted && pool.Count - taken > 0; taken++)
            {
                int j = taken + rng.Next(pool.Count - taken);
                Sheet chosen = pool[j];
                pool[j] = pool[taken];
                pool[taken] = chosen;
                picked.Add(chosen);
            }
            return picked;
        }
    }
}
