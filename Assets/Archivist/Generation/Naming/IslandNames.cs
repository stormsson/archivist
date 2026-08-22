using System.Collections.Generic;

namespace Archivist.Generation.Naming
{
    /// <summary>§9. Names are drawn in feature order, unique within the island.</summary>
    public sealed class IslandNames
    {
        public IslandNames(string island, IReadOnlyList<string> settlements, IReadOnlyList<string> peaks)
        {
            Island = island; Settlements = settlements; Peaks = peaks;
        }

        public string Island { get; private set; }
        public IReadOnlyList<string> Settlements { get; private set; }
        /// <summary>Only the top Tuning.PeakNamedCount are named; the rest carry a spot height only.</summary>
        public IReadOnlyList<string> Peaks { get; private set; }
    }
}
