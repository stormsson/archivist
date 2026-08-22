using Archivist.Generation;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// Turns a <see cref="SheetId"/> back into the <see cref="Sheet"/> it names.
    ///
    /// <para>This is the other half of the design's central bargain. A sheet in the world
    /// stores an identity and nothing else — no ground rect, no rotation, no year — because
    /// storing them would be caching data that is a pure function of the seed (R1.1, R1.11).
    /// The bargain only works if the walk back exists, and this is it: regenerate the island,
    /// find the survey by office and the whole-island flag, find the sheet by number.</para>
    ///
    /// <para>The match is unique. Every survey of an island has a distinct office except the
    /// whole-island survey (R2.2a), which borrows one — hence the flag — and sheet numbers are
    /// contiguous 1..N within a survey.</para>
    /// </summary>
    public static class SheetLookup
    {
        public static bool TryFind(Island island, SheetId id, out Sheet sheet)
        {
            sheet = default(Sheet);
            if (island == null || island.Seed != id.IslandSeed) return false;

            for (int s = 0; s < island.Surveys.Count; s++)
            {
                Survey survey = island.Surveys[s];
                if (survey.Spec.Office != id.Office) continue;
                if (survey.Spec.IsWholeIsland != id.WholeIsland) continue;

                for (int i = 0; i < survey.Sheets.Count; i++)
                {
                    if (survey.Sheets[i].Number != id.Number) continue;
                    sheet = survey.Sheets[i];
                    return true;
                }
                return false;   // right survey, no such number
            }
            return false;
        }
    }
}
