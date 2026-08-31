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
        const double Sqrt2 = 1.4142135623730951;

        /// <summary>
        /// How much coarser than the target cell is still accepted, when the coastline is the
        /// only thing drawing the water's edge — i.e. when there is no fill.
        ///
        /// <para><see cref="ForPixelsPerMetre(double)"/>'s tight <c>sqrt(2)</c> exists so the
        /// stroke agrees with the fill's per-pixel water edge. <b>With <c>Fill</c> off (Q2.2)
        /// there is no second opinion to agree with</b>, and the only cost of a coarser lattice
        /// is faceting on the line itself. Measured on a quarter plate in hand: 4 m cell,
        /// 672 ms; 8 m cell, <b>168 ms</b>. 2*sqrt(2) is the next rung up in log space, and it
        /// is what puts an in-hand plate at 8 m.</para>
        ///
        /// <para>Lowering the pixel density instead buys the same 4x and costs more: strokes
        /// clamp at <c>Strokes.MinHalfWidthPx</c>, so below ~2 px/mm a 0.35 mm coast and a
        /// 0.25 mm river come out the same width — and Q2.6 makes stroke weight one of the few
        /// things left that separates two offices.</para>
        /// </summary>
        public const double NoFillSlack = 2.8284271247461903;

        public static int ForGroundCell(double targetCellMetres)
        {
            return ForGroundCell(targetCellMetres, Sqrt2);
        }

        public static int ForGroundCell(double targetCellMetres, double slack)
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
            double accept = targetCellMetres * slack;

            int lod = 0;
            double cell = Tuning.BaseCell;
            while (cell > accept && lod < Tuning.MaxLod) { cell *= 0.5; lod++; }
            return lod;
        }

        public static int ForPixelsPerMetre(double pixelsPerMetre)
        {
            return ForGroundCell(pixelsPerMetre > 0 ? 1.0 / pixelsPerMetre : Tuning.BaseCell);
        }

        /// <summary>The same, at <see cref="NoFillSlack"/>. For the vector coastline, which only
        /// ever runs when there is no fill.</summary>
        public static int ForPixelsPerMetreWithoutFill(double pixelsPerMetre)
        {
            return ForGroundCell(pixelsPerMetre > 0 ? 1.0 / pixelsPerMetre : Tuning.BaseCell,
                                 NoFillSlack);
        }
    }
}
