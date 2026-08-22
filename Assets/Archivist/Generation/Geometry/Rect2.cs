using System;
using Archivist.Generation.Determinism;

namespace Archivist.Generation.Geometry
{
    /// <summary>Axis-aligned rectangle in whatever space the caller is working in.</summary>
    public readonly struct Rect2
    {
        public readonly double MinX, MinY, MaxX, MaxY;

        public Rect2(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
        }

        public static Rect2 FromCentreSize(V2 centre, double w, double h)
        {
            return new Rect2(centre.X - w * 0.5, centre.Y - h * 0.5, centre.X + w * 0.5, centre.Y + h * 0.5);
        }

        public static Rect2 Empty { get { return new Rect2(double.MaxValue, double.MaxValue, double.MinValue, double.MinValue); } }

        public bool IsEmpty { get { return MaxX < MinX || MaxY < MinY; } }
        public double Width  { get { return MaxX - MinX; } }
        public double Height { get { return MaxY - MinY; } }
        public V2 Centre     { get { return new V2((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5); } }
        public double Diagonal { get { return Math.Sqrt(Width * Width + Height * Height); } }

        public bool Contains(V2 p) { return p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY; }

        public bool Intersects(Rect2 o) { return !(o.MinX > MaxX || o.MaxX < MinX || o.MinY > MaxY || o.MaxY < MinY); }

        public Rect2 Intersection(Rect2 o)
        {
            return new Rect2(Math.Max(MinX, o.MinX), Math.Max(MinY, o.MinY),
                             Math.Min(MaxX, o.MaxX), Math.Min(MaxY, o.MaxY));
        }

        public Rect2 Expanded(double d) { return new Rect2(MinX - d, MinY - d, MaxX + d, MaxY + d); }

        public Rect2 Encapsulate(V2 p)
        {
            return new Rect2(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));
        }

        /// <summary>
        /// §6.2 lattice rule: snap outward so corners land on multiples of cell measured
        /// from the domain origin (0,0). Two rects at the same LOD then sample identical points.
        /// </summary>
        public Rect2 SnapOut(double cell)
        {
            return new Rect2(Q.FloorTo(MinX, cell), Q.FloorTo(MinY, cell),
                             Q.CeilTo(MaxX, cell),  Q.CeilTo(MaxY, cell));
        }

        public override string ToString()
        {
            return "[" + MinX.ToString("F1") + "," + MinY.ToString("F1") + " .. " + MaxX.ToString("F1") + "," + MaxY.ToString("F1") + "]";
        }
    }
}
