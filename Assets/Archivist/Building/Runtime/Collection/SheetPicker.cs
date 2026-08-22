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
        /// already issued, drawn without replacement.
        ///
        /// <para>Drawing from the flattened set of every survey — rather than picking a survey
        /// and then sheets within it — is what makes a crate's contents mixed, which is the
        /// arrangement §4.3 describes: a crate carries sheets for one or two islands, and
        /// sorting them by office is the player's first act, not the generator's.</para>
        ///
        /// <para>Returns fewer than asked, or none, when the island is exhausted. That is a
        /// legitimate outcome, not an error: islands are finite even though the supply of
        /// islands is not (R1.2, R1.8).</para>
        /// </summary>
        public static List<Sheet> PickUnissued(Island island, int count,
                                               HashSet<SheetId> alreadyIssued, int drawSeed)
        {
            if (island == null) throw new ArgumentNullException(nameof(island));

            var pool = new List<Sheet>();
            for (int s = 0; s < island.Surveys.Count; s++)
            {
                Survey survey = island.Surveys[s];
                for (int i = 0; i < survey.Sheets.Count; i++)
                {
                    Sheet sheet = survey.Sheets[i];
                    if (alreadyIssued == null || !alreadyIssued.Contains(SheetId.Of(sheet)))
                        pool.Add(sheet);
                }
            }

            var picked = new List<Sheet>(Math.Min(count, pool.Count));
            var rng = new Random(drawSeed);

            // Partial Fisher-Yates: swap a random survivor to the front, take it, shrink.
            for (int taken = 0; taken < count && pool.Count - taken > 0; taken++)
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
