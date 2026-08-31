using System;
using System.Collections.Generic;

namespace Archivist.Generation.Geometry
{
    /// <summary>A contour line or a river course. Vector data only (R1.3).</summary>
    public sealed class Polyline
    {
        readonly V2[] _points;

        public Polyline(V2[] points, bool closed)
        {
            _points = points ?? new V2[0];
            Closed = closed;
        }

        public Polyline(List<V2> points, bool closed) : this(points.ToArray(), closed) { }

        public IReadOnlyList<V2> Points { get { return _points; } }
        public int Count { get { return _points.Length; } }
        public V2 this[int i] { get { return _points[i]; } }
        public bool Closed { get; private set; }

        public double Length
        {
            get
            {
                double t = 0;
                for (int i = 1; i < _points.Length; i++) t += V2.Dist(_points[i - 1], _points[i]);
                if (Closed && _points.Length > 1) t += V2.Dist(_points[_points.Length - 1], _points[0]);
                return t;
            }
        }

        public Rect2 Bounds
        {
            get
            {
                Rect2 r = Rect2.Empty;
                for (int i = 0; i < _points.Length; i++) r = r.Encapsulate(_points[i]);
                return r;
            }
        }
    }
}
