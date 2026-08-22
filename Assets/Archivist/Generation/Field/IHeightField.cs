using Archivist.Generation.Geometry;

namespace Archivist.Generation.Field
{
    /// <summary>
    /// §3: the island is f(x,y), never a grid. Contouring is a query (§6.1), so this is
    /// the single source of geometry and nothing derived from it is ever cached (R3.1).
    /// </summary>
    public interface IHeightField
    {
        IslandParams Params { get; }

        /// <summary>Normalised, sea level = 0.50. QUANTISED at 2^-16 (§4.4) — safe to compare.</summary>
        double Height01(double x, double y);

        /// <summary>Metres; negative below sea. Derived from the quantised Height01.</summary>
        double Elevation(double x, double y);

        /// <summary>d(Elevation)/d(distance), m/m. Central difference at Tuning.GradientStep,
        /// against the UNQUANTISED composition (§4.4 carve-out).</summary>
        V2 Gradient(double x, double y);

        /// <summary>
        /// Both values from ONE field evaluation. Elevation is derived from Height01, so a
        /// caller needing both — every pixel of a raster fill does — otherwise pays for the
        /// composition twice. Identical results to calling Height01 and Elevation separately.
        /// </summary>
        double Sample(double x, double y, out double elevation);

        /// <summary>
        /// Elevation for an already-known Height01, without touching the field. Lets a
        /// caller that interpolates h01 convert it consistently.
        /// </summary>
        double ElevationFrom(double h01);
    }

    public static class HeightFieldExtensions
    {
        public static bool IsLand(this IHeightField f, double x, double y)
        {
            // Tie at exactly SeaLevel counts as land (§4.4), stated once.
            return f.Height01(x, y) >= f.Params.SeaLevel;
        }

        public static bool IsLand(this IHeightField f, V2 p) { return f.IsLand(p.X, p.Y); }
    }
}
