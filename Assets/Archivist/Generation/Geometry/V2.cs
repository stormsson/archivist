using System;

namespace Archivist.Generation.Geometry
{
    /// <summary>Ground space is double (§4.4). No UnityEngine in this assembly (§14).</summary>
    public readonly struct V2 : IEquatable<V2>
    {
        public readonly double X;
        public readonly double Y;

        public V2(double x, double y) { X = x; Y = y; }

        public static readonly V2 Zero = new V2(0, 0);

        public double LengthSq { get { return X * X + Y * Y; } }
        public double Length   { get { return Math.Sqrt(X * X + Y * Y); } }

        public static V2 operator +(V2 a, V2 b) { return new V2(a.X + b.X, a.Y + b.Y); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.X - b.X, a.Y - b.Y); }
        public static V2 operator -(V2 a)       { return new V2(-a.X, -a.Y); }
        public static V2 operator *(V2 a, double s) { return new V2(a.X * s, a.Y * s); }
        public static V2 operator *(double s, V2 a) { return new V2(a.X * s, a.Y * s); }
        public static V2 operator /(V2 a, double s) { return new V2(a.X / s, a.Y / s); }

        public static double Dot(V2 a, V2 b)   { return a.X * b.X + a.Y * b.Y; }
        public static double Cross(V2 a, V2 b) { return a.X * b.Y - a.Y * b.X; }
        public static double Dist(V2 a, V2 b)  { return (a - b).Length; }
        public static double DistSq(V2 a, V2 b) { return (a - b).LengthSq; }

        public static V2 Lerp(V2 a, V2 b, double t) { return new V2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

        /// <summary>Rotate by radians. Caller is responsible for §4.4 if the result feeds a branch.</summary>
        public V2 RotateRad(double rad)
        {
            double c = Math.Cos(rad), s = Math.Sin(rad);
            return new V2(X * c - Y * s, X * s + Y * c);
        }

        public V2 RotateDeg(double deg) { return RotateRad(deg * Math.PI / 180.0); }

        public bool Equals(V2 o) { return X == o.X && Y == o.Y; }
        public override bool Equals(object o) { return o is V2 v && Equals(v); }
        public override int GetHashCode() { unchecked { return (X.GetHashCode() * 397) ^ Y.GetHashCode(); } }
        public override string ToString() { return "(" + X.ToString("F2") + ", " + Y.ToString("F2") + ")"; }
    }
}
