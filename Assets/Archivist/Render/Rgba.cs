using System;

namespace Archivist.Render
{
    /// <summary>RGBA32. No engine types (T3.2).</summary>
    public readonly struct Rgba
    {
        public readonly byte R, G, B, A;

        public Rgba(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

        /// <summary>Hex without '#', e.g. "16324f".</summary>
        public static Rgba FromHex(string rrggbb)
        {
            if (rrggbb == null || rrggbb.Length < 6) throw new ArgumentException("need 6 hex digits", "rrggbb");
            return new Rgba(Convert.ToByte(rrggbb.Substring(0, 2), 16),
                            Convert.ToByte(rrggbb.Substring(2, 2), 16),
                            Convert.ToByte(rrggbb.Substring(4, 2), 16));
        }

        public static Rgba Lerp(Rgba a, Rgba b, double t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            return new Rgba((byte)(a.R + (b.R - a.R) * t + 0.5),
                            (byte)(a.G + (b.G - a.G) * t + 0.5),
                            (byte)(a.B + (b.B - a.B) * t + 0.5),
                            (byte)(a.A + (b.A - a.A) * t + 0.5));
        }

        /// <summary>Source-over composite at coverage [0,1]. Used by strokes (§7).</summary>
        public static Rgba Over(Rgba dst, Rgba src, double coverage)
        {
            double cov = coverage < 0 ? 0 : (coverage > 1 ? 1 : coverage);
            double a = (src.A / 255.0) * cov;
            if (a <= 0) return dst;
            byte outA = dst.A > (byte)(a * 255 + 0.5) ? dst.A : (byte)(a * 255 + 0.5);
            return new Rgba((byte)(dst.R + (src.R - dst.R) * a + 0.5),
                            (byte)(dst.G + (src.G - dst.G) * a + 0.5),
                            (byte)(dst.B + (src.B - dst.B) * a + 0.5),
                            outA);
        }
    }
}
