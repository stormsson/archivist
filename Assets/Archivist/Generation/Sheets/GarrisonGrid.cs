using System;
using System.Collections.Generic;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// D4 / §6.4 — <see cref="Features.FeatureClass.Grid"/>, Garrison only (§8.3).
    ///
    /// <para>
    /// A square grid in the TRUE-NORTH frame — Garrison's rotation is always 0 deg (§10.1),
    /// so its frame space and ground space coincide and the grid is axis-aligned in both.
    /// The origin is the DOMAIN ORIGIN (0,0), never the sheet, and the pitch comes from
    /// <see cref="MapScale.GridPitch"/>: 1000 m at 1:25000, 200 m at 1:5000.
    /// </para>
    ///
    /// <para>
    /// The global origin is the whole point, and it is the same argument as §6.2's contour
    /// lattice: two adjacent Garrison sheets must show the same grid lines in the same
    /// places, or the block stops reading as one survey. A per-sheet origin would put a
    /// half-pitch step across every shared border.
    /// </para>
    ///
    /// <para>
    /// Field-independent, so field-derived in §3.1's sense: re-queryable per rect, no
    /// identity, nothing persisted.
    /// </para>
    /// </summary>
    public static class GarrisonGrid
    {
        /// <summary>
        /// The grid lines crossing <paramref name="groundRect"/>, as two-point open polylines
        /// in ground space. Verticals first in ascending easting, then horizontals in
        /// ascending northing — a stable order, so a hash over a sheet's content is stable.
        ///
        /// <para>
        /// Line positions are <c>k * pitch</c> for integer <c>k</c>, computed from the domain
        /// origin and never from the rect, which is what makes two neighbouring sheets agree
        /// exactly. Both pitches (1000, 200) and every plausible <c>k</c> are exact doubles,
        /// so the product is exact and the agreement is bit-for-bit.
        /// </para>
        ///
        /// <para>
        /// <paramref name="groundRect"/> is expected to be a sheet's ground-space AABB
        /// (<see cref="Sheet.GroundBounds"/>); for Garrison that is the sheet rect itself,
        /// since the rotation is 0 deg. Clipping the lines to the rotated rect, if a future
        /// office ever rotates its grid, belongs to the renderer, not here.
        /// </para>
        /// </summary>
        public static List<Polyline> ForRect(Rect2 groundRect, MapScale scale)
        {
            var lines = new List<Polyline>();

            double pitch = scale.GridPitch;
            if (pitch <= 0.0 || groundRect.IsEmpty) return lines;

            // Eastings: vertical lines, ascending x.
            int firstEasting = (int)Math.Ceiling(groundRect.MinX / pitch);
            int lastEasting = (int)Math.Floor(groundRect.MaxX / pitch);
            for (int k = firstEasting; k <= lastEasting; k++)
            {
                double x = k * pitch;
                lines.Add(new Polyline(
                    new[] { new V2(x, groundRect.MinY), new V2(x, groundRect.MaxY) }, false));
            }

            // Northings: horizontal lines, ascending y.
            int firstNorthing = (int)Math.Ceiling(groundRect.MinY / pitch);
            int lastNorthing = (int)Math.Floor(groundRect.MaxY / pitch);
            for (int k = firstNorthing; k <= lastNorthing; k++)
            {
                double y = k * pitch;
                lines.Add(new Polyline(
                    new[] { new V2(groundRect.MinX, y), new V2(groundRect.MaxX, y) }, false));
            }

            return lines;
        }
    }
}
