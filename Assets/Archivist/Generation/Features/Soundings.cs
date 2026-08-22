using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// §6.3. Offshore depth spot values, Hydrographic only (§8.3). Field-derived (§3.1):
    /// re-queried per rect, no identity, nothing persisted.
    /// </summary>
    public static class Soundings
    {
        /// <summary>
        /// Samples the <see cref="Tuning.SoundingLattice"/> lattice inside
        /// <paramref name="groundRect"/> and keeps the points where
        /// <c>Elevation &lt; Tuning.SoundingDepth</c>, depth rounded to the metre and stored
        /// positive.
        /// <para>
        /// The lattice is <b>global</b>, not rect-local: sample positions are exact multiples of
        /// the lattice step measured from the domain origin (0,0), which is §6.2's rule applied
        /// to soundings for the same reason it is applied to contours. Two overlapping sheets
        /// must show the same soundings at the same places or the pair stops reading as one
        /// survey.
        /// </para>
        /// <para>Emitted in (x asc, y asc) lattice order, so the list is itself deterministic.</para>
        /// </summary>
        public static List<Sounding> ForRect(IHeightField field, Rect2 groundRect)
        {
            if (field == null) throw new ArgumentNullException("field");

            List<Sounding> result = new List<Sounding>();
            if (groundRect.IsEmpty) return result;

            double cell = Tuning.SoundingLattice;

            // Global lattice indices; the inclusive range is the set of multiples of `cell`
            // that fall inside the rect.
            int gx0 = (int)Math.Ceiling(groundRect.MinX / cell);
            int gx1 = (int)Math.Floor(groundRect.MaxX / cell);
            int gy0 = (int)Math.Ceiling(groundRect.MinY / cell);
            int gy1 = (int)Math.Floor(groundRect.MaxY / cell);

            for (int gx = gx0; gx <= gx1; gx++)
            {
                double x = gx * cell;
                for (int gy = gy0; gy <= gy1; gy++)
                {
                    double y = gy * cell;

                    // Elevation derives from the quantised Height01 (§4.4), so this threshold is
                    // safe to compare directly and gives the same answer on every platform.
                    double e = field.Elevation(x, y);
                    if (e >= Tuning.SoundingDepth) continue;

                    result.Add(new Sounding(new V2(x, y), (int)Q.Metre(-e)));
                }
            }
            return result;
        }
    }
}
