using System;
using Archivist.Generation;

namespace Archivist.Render
{
    /// <summary>
    /// §7's LOD rule. Contours.LodForScale derives detail from a SCALE; a raster needs it
    /// from a PIXEL SIZE. Same ladder, same global lattice — implemented here rather than in
    /// Generation so that assembly stays untouched.
    ///
    /// Why it matters: the fill's water edge is computed per pixel from the analytic field.
    /// If the coastline stroke came from a polyline extracted at some fixed LOD, the line
    /// would visibly float off the water. Tying cell size to ~1 pixel makes them agree.
    /// </summary>
    public static class RenderLod
    {
        public static int ForGroundCell(double targetCellMetres)
        {
            if (targetCellMetres <= 0 || double.IsNaN(targetCellMetres)) return Tuning.MaxLod;
            // Integer halving rather than log2: the result feeds a branch, and §4.4 forbids
            // trusting a transcendental's last ulp at a threshold.
            //
            // NEAREST power-of-two cell, not the first one strictly finer. Halving until
            // cell <= target overshoots by up to 2x in each axis — 4x the cells — for a
            // difference no eye can find: a 0.93 m pixel would take a 0.5 m cell when a
            // 1.0 m cell is within 8%. Measured, that overshoot was most of a sheet's
            // render time. sqrt(2) is the log-space midpoint between two rungs.
            const double Sqrt2 = 1.4142135623730951;
            double accept = targetCellMetres * Sqrt2;

            int lod = 0;
            double cell = Tuning.BaseCell;
            while (cell > accept && lod < Tuning.MaxLod) { cell *= 0.5; lod++; }
            return lod;
        }

        public static int ForPixelsPerMetre(double pixelsPerMetre)
        {
            return ForGroundCell(pixelsPerMetre > 0 ? 1.0 / pixelsPerMetre : Tuning.BaseCell);
        }
    }
}
