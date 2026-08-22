using System;
using Archivist.Generation.Geometry;

namespace Archivist.Render
{
    /// <summary>
    /// §2 and §5 — the ground &lt;-&gt; image transform, and the ONE place a transcendental is
    /// allowed. cos/sin are evaluated once per render from the already-0.1-quantised angle;
    /// the per-pixel walk is pure add/multiply, so the raster is reproducible without
    /// relying on libm agreeing (same argument as §4.4 of POC-01).
    ///
    /// Image space is y-DOWN with its origin at the buffer's top-left. Getting that wrong
    /// mirrors every render, which is easy to miss on a roughly symmetric island.
    /// </summary>
    public sealed class GroundImage
    {
        readonly V2 _origin;      // ground position of pixel centre (0,0)
        readonly V2 _stepX;       // ground delta per +1 image x
        readonly V2 _stepY;       // ground delta per +1 image y
        readonly double _cos, _sin;

        public GroundImage(RenderRequest req)
        {
            Width = req.Width;
            Height = req.Height;
            PixelsPerMetre = req.PixelsPerMetre;

            double rad = req.RotationDeg * Math.PI / 180.0;
            _cos = Math.Cos(rad);
            _sin = Math.Sin(rad);

            double m = 1.0 / req.PixelsPerMetre;                 // metres per pixel
            _stepX = new V2( _cos * m,  _sin * m);
            _stepY = new V2( _sin * m, -_cos * m);               // y-down flips the ground y axis

            // Pixel centre (0,0) sits half a pixel in from the rect's top-left corner,
            // where "top-left" is in the ROTATED frame.
            V2 localTopLeft = new V2(req.Area.MinX, req.Area.MaxY);
            V2 half = (_stepX + _stepY) * 0.5;
            _origin = Rotate(localTopLeft, _cos, _sin) + half;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public double PixelsPerMetre { get; private set; }

        static V2 Rotate(V2 p, double c, double s) { return new V2(p.X * c - p.Y * s, p.X * s + p.Y * c); }

        public V2 GroundAt(int x, int y) { return _origin + _stepX * x + _stepY * y; }

        /// <summary>Inverse: ground -> continuous image coordinates. Used by stroke rasterisation.</summary>
        public void ImageAt(V2 ground, out double ix, out double iy)
        {
            V2 d = ground - _origin;
            // stepX and stepY are orthogonal with equal length (1/pxPerMetre), so the inverse
            // is a scaled transpose — no matrix solve needed.
            double inv = PixelsPerMetre * PixelsPerMetre;
            ix = (d.X * _stepX.X + d.Y * _stepX.Y) * inv;
            iy = (d.X * _stepY.X + d.Y * _stepY.Y) * inv;
        }

        /// <summary>Pixels per paper millimetre -&gt; pixels, for stroke widths given in mm (§7).</summary>
        public static double MmToPx(double mm, double pixelsPerPaperMm) { return mm * pixelsPerPaperMm; }
    }
}
