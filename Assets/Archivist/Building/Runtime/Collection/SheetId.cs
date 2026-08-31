using System;
using Archivist.Generation.Sheets;

namespace Archivist.Building.Collection
{
    /// <summary>
    /// The identity of one sheet, as a value. Four fields, all of them derivable from the
    /// sheet itself, none of them a reference to it — which is the whole point: a sheet's
    /// geometry lives only as long as the island object that produced it, but its identity
    /// has to outlive every regeneration.
    ///
    /// <para><b>Why not the survey's index.</b> Surveys are a list, and a list position is
    /// stable only while nothing above it changes. Office plus the whole-island flag says
    /// what the sheet actually is, and survives an office being added, removed, or
    /// temporarily switched off by the debug flags on <see cref="Generation.Island"/>.
    /// The flag is needed because the whole-island survey (R2.2a) borrows one of the three
    /// offices, so office alone would collide with that office's own survey.</para>
    ///
    /// <para><b><see cref="Number"/> is the quarter</b>, for everything except a detail sheet:
    /// 1 NW, 2 NE, 3 SW, 4 SE (Q1.1), and 1 for the island's chart. A plate is therefore named
    /// by island, office and corner, which is exactly how a binder is read.</para>
    /// </summary>
    public readonly struct SheetId : IEquatable<SheetId>
    {
        public readonly ulong IslandSeed;
        public readonly Office Office;
        public readonly bool WholeIsland;
        public readonly int Number;

        public SheetId(ulong islandSeed, Office office, bool wholeIsland, int number)
        {
            IslandSeed = islandSeed;
            Office = office;
            WholeIsland = wholeIsland;
            Number = number;
        }

        public static SheetId Of(Sheet sheet)
        {
            SurveySpec spec = sheet.Survey;
            return new SheetId(spec.IslandSeed, spec.Office, spec.IsWholeIsland, sheet.Number);
        }

        public bool Equals(SheetId other)
        {
            return IslandSeed == other.IslandSeed
                && Office == other.Office
                && WholeIsland == other.WholeIsland
                && Number == other.Number;
        }

        public override bool Equals(object obj) { return obj is SheetId && Equals((SheetId)obj); }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = IslandSeed.GetHashCode();
                h = (h * 397) ^ (int)Office;
                h = (h * 397) ^ (WholeIsland ? 1 : 0);
                h = (h * 397) ^ Number;
                return h;
            }
        }

        public override string ToString()
        {
            return $"{IslandSeed:X16}/{Office}{(WholeIsland ? "-whole" : "")}/{Number}";
        }
    }
}
