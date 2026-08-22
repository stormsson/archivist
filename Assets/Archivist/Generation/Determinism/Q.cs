using System;

namespace Archivist.Generation.Determinism
{
    /// <summary>
    /// Quantisation (§4.4). Quantise the derived scalar that feeds the branch, never the
    /// intermediate that feeds it. Floor(x+0.5) rather than Math.Round: exact in IEEE-754,
    /// with no midpoint-mode dependency.
    /// </summary>
    public static class Q
    {
        public const double H01Scale = 65536.0;      // 2^16, quantum ~1.5e-5
        public const double DegScale = 10.0;         // 0.1 degrees
        public const double GradScale = 10000.0;     // 1e-4

        public static double H01(double h)     { return Math.Floor(h * H01Scale + 0.5) / H01Scale; }
        public static double Deg(double deg)   { return Math.Floor(deg * DegScale + 0.5) / DegScale; }
        public static double Grad(double g)    { return Math.Floor(g * GradScale + 0.5) / GradScale; }
        public static double Metre(double m)   { return Math.Floor(m + 0.5); }

        /// <summary>Snap v outward to a multiple of cell, away from zero (§6.2 lattice).</summary>
        public static double FloorTo(double v, double cell) { return Math.Floor(v / cell) * cell; }
        public static double CeilTo(double v, double cell)  { return Math.Ceiling(v / cell) * cell; }
    }
}
