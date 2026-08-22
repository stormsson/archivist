using System;
using System.Collections.Generic;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Generation.Features
{
    /// <summary>
    /// R1.5 as read by §7.4 and rewritten by D1: the service rule is <b>office-relative</b>, and
    /// the coastline never serves.
    ///
    /// <code>
    /// Serving(office)   = drawn(office) \ { Coast }        // FeatureMatrix, §8.3
    /// served(p, office) = exists class c in Serving(office) present within u of p
    /// </code>
    ///
    /// <para><b>Why the coast is excluded.</b> The coastline is island-scale by R1.4, so every
    /// sheet in a coastal survey carries it and it cannot be the thing that makes a sheet worth
    /// cutting. Excluding it is exactly what makes the rule mean <i>this office draws something
    /// here</i>. Three consequences then fall out instead of being carved out: Hydrographic is
    /// served on the shore by its soundings, so a bare stretch of coast keeps its sheet; Garrison
    /// is served everywhere by its own grid, so §10.3's explicit exemption is deleted rather than
    /// kept; and Land Survey is left as the one office the test actually culls, with teeth but
    /// sparing a hillside that has 50 m of relief and no village.</para>
    ///
    /// <para><b>Presence within u, per class (D1).</b>
    /// Peak / River / Settlement: a discrete feature of that class within <c>u</c>.
    /// Sounding: some sample within <c>u</c> has <c>Elevation &lt; -4 m</c>.
    /// Contour: relief within <c>u</c> spans one contour step, <c>max - min &gt;= 50 m</c>.
    /// Grid: always. Coast: never.</para>
    ///
    /// <para><b>Implementation (D1).</b> One mask per class on the
    /// <see cref="Tuning.BaseCell"/> lattice, built once in the constructor; <see cref="Served"/>
    /// is then a lattice lookup. Discrete classes stamp a disc of radius <c>u</c> per feature;
    /// relief is a separable max-filter and min-filter thresholded at
    /// <see cref="Tuning.ContourStep"/>; soundings are thresholded then max-filtered. Everything
    /// after the field sampling is comparison and integer work, so determinism is free and §13.2
    /// is unaffected.</para>
    /// </summary>
    public sealed class ServiceRule
    {
        readonly double _cell;
        readonly double _originX;
        readonly double _originY;
        readonly int _nx;
        readonly int _ny;
        readonly double _u;

        readonly bool[] _peak;
        readonly bool[] _river;
        readonly bool[] _settlement;
        readonly bool[] _sounding;
        readonly bool[] _relief;

        /// <summary>u, the island-scale unit. D1 pins it to <c>NominalRadius / 4</c> — see
        /// <see cref="IslandParams.ServiceRadius"/> — never to the land bbox, so the service
        /// radius stays independent of the coastline it is used to judge.</summary>
        public double ServiceRadius { get { return _u; } }

        /// <summary>
        /// Builds every mask. The sampled block is <paramref name="landBounds"/> grown by
        /// <paramref name="serviceRadiusU"/> and snapped outward to the global 64 m lattice
        /// (§6.2), so that the neighbourhood of every point inside the land bbox is fully
        /// sampled. Without the growth a coastal point at the bbox edge would see no water
        /// beyond it and Hydrographic's sounding service would fail exactly where D1 relies on
        /// it holding.
        /// </summary>
        public ServiceRule(IHeightField field, Rect2 landBounds, IslandFeatures features, double serviceRadiusU)
        {
            if (field == null) throw new ArgumentNullException("field");
            if (features == null) throw new ArgumentNullException("features");

            _cell = Tuning.BaseCell;
            _u = serviceRadiusU > 0.0 ? serviceRadiusU : 0.0;

            Rect2 area = (landBounds.IsEmpty ? new Rect2(0, 0, 0, 0) : landBounds).Expanded(_u).SnapOut(_cell);
            _originX = area.MinX;
            _originY = area.MinY;
            _nx = (int)Math.Floor(area.Width / _cell + 0.5) + 1;
            _ny = (int)Math.Floor(area.Height / _cell + 0.5) + 1;
            if (_nx < 1) _nx = 1;
            if (_ny < 1) _ny = 1;

            int n = _nx * _ny;
            _peak = new bool[n];
            _river = new bool[n];
            _settlement = new bool[n];
            _sounding = new bool[n];
            _relief = new bool[n];

            // --- discrete classes: stamp a disc of radius u at each feature ----------
            IReadOnlyList<Peak> peaks = features.Peaks;
            if (peaks != null)
            {
                for (int i = 0; i < peaks.Count; i++) StampDisc(_peak, peaks[i].Position);
            }

            IReadOnlyList<Settlement> towns = features.Settlements;
            if (towns != null)
            {
                for (int i = 0; i < towns.Count; i++) StampDisc(_settlement, towns[i].Position);
            }

            IReadOnlyList<River> rivers = features.Rivers;
            if (rivers != null)
            {
                // Vertices are RiverStep (40 m) apart on a 64 m lattice and are about to be
                // dilated by u (~24 cells), so stamping vertices rather than rasterising
                // segments cannot change any mask bit. Distinct cells are stamped once.
                bool[] seen = new bool[n];
                for (int ri = 0; ri < rivers.Count; ri++)
                {
                    Polyline course = rivers[ri].Course;
                    if (course == null) continue;
                    for (int i = 0; i < course.Count; i++)
                    {
                        V2 p = course[i];
                        int ix = CellX(p.X);
                        int iy = CellY(p.Y);
                        int idx = ix * _ny + iy;
                        if (seen[idx]) continue;
                        seen[idx] = true;
                        StampDisc(_river, new V2(_originX + ix * _cell, _originY + iy * _cell));
                    }
                }
            }

            // --- field-derived classes ----------------------------------------------
            // The one expensive step in the whole rule: n Elevation queries. Everything below is
            // O(n) in comparisons.
            double[] elev = new double[n];
            for (int ix = 0; ix < _nx; ix++)
            {
                double x = _originX + ix * _cell;
                for (int iy = 0; iy < _ny; iy++)
                {
                    elev[ix * _ny + iy] = field.Elevation(x, _originY + iy * _cell);
                }
            }

            // The separable filters give the axis-aligned square of half-width r, not a
            // Euclidean disc — that is what D1's "separable max-filter and min-filter" buys, and
            // r = floor(u / cell) keeps the window inside u rather than outside it, so relief and
            // sounding service are marginally conservative rather than generous.
            int r = (int)Math.Floor(_u / _cell);
            if (r < 0) r = 0;

            double[] tmp = new double[n];
            double[] hi = new double[n];
            double[] lo = new double[n];
            int[] deque = new int[Math.Max(_nx, _ny) + 1];

            FilterSeparable(elev, hi, tmp, r, true, deque);
            FilterSeparable(elev, lo, tmp, r, false, deque);
            for (int i = 0; i < n; i++) _relief[i] = (hi[i] - lo[i]) >= Tuning.ContourStep;

            // Soundings: threshold first (Elevation is derived from the quantised Height01, so
            // the comparison is safe, §4.4), then dilate. Reusing the max-filter on 0/1 is
            // exactly a boolean dilation.
            double[] deep = new double[n];
            for (int i = 0; i < n; i++) deep[i] = elev[i] < Tuning.SoundingDepth ? 1.0 : 0.0;
            FilterSeparable(deep, hi, tmp, r, true, deque);
            for (int i = 0; i < n; i++) _sounding[i] = hi[i] > 0.5;
        }

        /// <summary>
        /// D1's <c>served(p, office)</c>: true when any class in
        /// <see cref="FeatureMatrix.Serving"/> — the office's drawn set minus Coast — is present
        /// within <c>u</c> of <paramref name="p"/>.
        /// </summary>
        public bool Served(V2 p, Office office)
        {
            IReadOnlyList<FeatureClass> serving = FeatureMatrix.Serving(office);
            if (serving == null) return false;
            for (int i = 0; i < serving.Count; i++)
            {
                if (ServedClass(p, serving[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Presence of one class within <c>u</c> of <paramref name="p"/>, per D1's table.
        /// <see cref="FeatureClass.Grid"/> is always present; <see cref="FeatureClass.Coast"/>
        /// never is, which is the whole of D1 in one line.
        /// </summary>
        public bool ServedClass(V2 p, FeatureClass cls)
        {
            switch (cls)
            {
                case FeatureClass.Grid:       return true;
                case FeatureClass.Coast:      return false;   // island-scale, so it never serves
                case FeatureClass.Peak:       return Lookup(_peak, p);
                case FeatureClass.River:      return Lookup(_river, p);
                case FeatureClass.Settlement: return Lookup(_settlement, p);
                case FeatureClass.Sounding:   return Lookup(_sounding, p);
                case FeatureClass.Contour:    return Lookup(_relief, p);
                default:                      return false;
            }
        }

        /// <summary>
        /// Fraction of <paramref name="landSamples"/> that are served for
        /// <paramref name="office"/>. §10.3 compares this against
        /// <see cref="Tuning.ServedThreshold"/> for <b>every</b> office, Garrison included (D1),
        /// reading the same 16x16 rect samples it already takes for <c>landFraction</c> so no
        /// extra field evaluation is needed. An empty sample set is unserved.
        /// </summary>
        public double ServedFraction(V2[] landSamples, Office office)
        {
            if (landSamples == null || landSamples.Length == 0) return 0.0;

            IReadOnlyList<FeatureClass> serving = FeatureMatrix.Serving(office);
            if (serving == null || serving.Count == 0) return 0.0;

            int served = 0;
            for (int s = 0; s < landSamples.Length; s++)
            {
                for (int c = 0; c < serving.Count; c++)
                {
                    if (ServedClass(landSamples[s], serving[c])) { served++; break; }
                }
            }
            return (double)served / landSamples.Length;
        }

        // -------------------------------------------------------------------------------

        int CellX(double x)
        {
            int i = (int)Math.Floor((x - _originX) / _cell + 0.5);
            if (i < 0) return 0;
            return i > _nx - 1 ? _nx - 1 : i;
        }

        int CellY(double y)
        {
            int i = (int)Math.Floor((y - _originY) / _cell + 0.5);
            if (i < 0) return 0;
            return i > _ny - 1 ? _ny - 1 : i;
        }

        /// <summary>Nearest-lattice-point read, clamped. Points beyond the block are answered by
        /// its edge, which is at least <c>u</c> outside the land bbox and therefore only ever
        /// reached by sea samples in a sheet rect.</summary>
        bool Lookup(bool[] mask, V2 p)
        {
            return mask[CellX(p.X) * _ny + CellY(p.Y)];
        }

        /// <summary>Marks every lattice point within a true Euclidean <c>u</c> of the feature.</summary>
        void StampDisc(bool[] mask, V2 p)
        {
            int rc = (int)Math.Ceiling(_u / _cell);
            int cx = CellX(p.X);
            int cy = CellY(p.Y);
            double u2 = _u * _u;

            int ix0 = cx - rc; if (ix0 < 0) ix0 = 0;
            int ix1 = cx + rc; if (ix1 > _nx - 1) ix1 = _nx - 1;
            int iy0 = cy - rc; if (iy0 < 0) iy0 = 0;
            int iy1 = cy + rc; if (iy1 > _ny - 1) iy1 = _ny - 1;

            for (int ix = ix0; ix <= ix1; ix++)
            {
                double dx = (_originX + ix * _cell) - p.X;
                double dx2 = dx * dx;
                if (dx2 > u2) continue;
                int col = ix * _ny;
                for (int iy = iy0; iy <= iy1; iy++)
                {
                    double dy = (_originY + iy * _cell) - p.Y;
                    if (dx2 + dy * dy <= u2) mask[col + iy] = true;
                }
            }
        }

        /// <summary>
        /// Two O(n) passes — rows then columns — giving the extreme over the square window of
        /// half-width <paramref name="r"/>. Separable, so the cost is O(n) rather than
        /// O(n * r^2), which is what keeps the whole island inside the §13.8 250 ms budget.
        /// </summary>
        void FilterSeparable(double[] src, double[] dst, double[] tmp, int r, bool maximum, int[] deque)
        {
            for (int iy = 0; iy < _ny; iy++)
            {
                SlidingExtreme(src, iy, _ny, tmp, iy, _ny, _nx, r, maximum, deque);
            }
            for (int ix = 0; ix < _nx; ix++)
            {
                SlidingExtreme(tmp, ix * _ny, 1, dst, ix * _ny, 1, _ny, r, maximum, deque);
            }
        }

        /// <summary>
        /// Sliding-window extreme over one strided line, O(n) via a monotonic deque of indices.
        /// Windows are clipped at the ends of the line.
        /// </summary>
        static void SlidingExtreme(double[] src, int srcStart, int srcStride,
                                   double[] dst, int dstStart, int dstStride,
                                   int n, int r, bool maximum, int[] deque)
        {
            int head = 0;
            int tail = 0;
            for (int i = 0; i < n + r; i++)
            {
                if (i < n)
                {
                    double v = src[srcStart + i * srcStride];
                    while (tail > head)
                    {
                        double back = src[srcStart + deque[tail - 1] * srcStride];
                        bool drop = maximum ? back <= v : back >= v;
                        if (!drop) break;
                        tail--;
                    }
                    deque[tail++] = i;
                }

                int o = i - r;                       // the output whose window is [o-r, o+r]
                if (o < 0) continue;
                while (deque[head] < o - r) head++;
                dst[dstStart + o * dstStride] = src[srcStart + deque[head] * srcStride];
            }
        }
    }
}
