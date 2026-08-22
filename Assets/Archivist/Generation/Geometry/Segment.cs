namespace Archivist.Generation.Geometry
{
    /// <summary>
    /// Point-to-segment maths. Lives here rather than in a feature pass because it is pure
    /// geometry: <see cref="Features.Settlements"/> and <see cref="Features.PoiSiting"/> both
    /// sweep the coastline and both carried a byte-identical private copy of it.
    /// </summary>
    public static class Segment
    {
        /// <summary>
        /// Squared distance from <paramref name="p"/> to the segment <paramref name="a"/>-<paramref name="b"/>.
        /// A degenerate (zero length) segment falls back to the distance to <paramref name="a"/>.
        /// <para>The expression order is load-bearing. Generation is bit-reproducible (§4.4, asserted
        /// by A2), and reassociating any of these products would change the last bit of a distance
        /// that a band test then branches on, so this must stay exactly as written.</para>
        /// </summary>
        public static double DistSq(V2 p, V2 a, V2 b)
        {
            V2 ab = b - a;
            double len2 = ab.LengthSq;
            if (len2 <= 0.0) return V2.DistSq(p, a);
            double t = V2.Dot(p - a, ab) / len2;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            return V2.DistSq(p, a + ab * t);
        }
    }
}
