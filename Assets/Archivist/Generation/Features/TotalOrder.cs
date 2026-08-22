using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// The tie-break every selection stage sorts by. §7.1, §7.2 and POC-03 §1.3 each mandate an
    /// explicit TOTAL order of the form (primary key, x asc, y asc); the primary key differs per
    /// pass — elevation desc for peaks, score desc for settlements, kind index ASC for POIs — but
    /// the (x asc, y asc) tail is the same in all three and is what makes the order total.
    ///
    /// <para>Only the tail is shared. Each pass keeps its own comparator so the primary key, and
    /// its direction, stay visible at the site that depends on them.</para>
    /// </summary>
    public static class TotalOrder
    {
        /// <summary>
        /// (x asc, y asc). Returns 0 only for coincident points — which, since candidates are
        /// distinct lattice points within a pass, never happens once the primary key has already
        /// compared equal. That is why the resulting order does not depend on the sort's
        /// (unspecified) stability.
        /// </summary>
        public static int ByPosition(V2 a, V2 b)
        {
            if (a.X != b.X) return a.X < b.X ? -1 : 1;
            if (a.Y != b.Y) return a.Y < b.Y ? -1 : 1;
            return 0;
        }
    }
}
