using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// The Hydrographic office's cutter. Where the lattice cutter (§10.2) tiles the land
    /// bbox in one survey frame, this one WALKS THE SHORE: sheets are laid along each
    /// coastline loop and each is oriented to its own stretch of coast.
    ///
    /// Consequences, all deliberate (see docs/analysis/hydrographic-contour-following.md):
    /// - There is no survey frame, so §10.2 steps 2-5 do not apply here and
    ///   <see cref="SurveySpec.RotationDeg"/> is nominal — per-sheet rotation governs (D-H2).
    /// - Numbering is by WALK ORDER rather than row-major. R2.10b still holds: a gap in a
    ///   run is unambiguous, because the run follows the shore.
    /// - Every loop is surveyed, islets included. That is the office in character — a
    ///   hydrographic office charts hazards to navigation, and skerries are exactly what it
    ///   exists to record. Measured, islets are ~45% of the survey.
    /// - Coverage hugs the shore instead of blanketing the land bbox, which is what should
    ///   finally give R1.8 some unsurveyed ground (POC-01 finding F8).
    /// </summary>
    public static class CoastWalkCutter
    {
        public static Survey Cut(IHeightField field, IReadOnlyList<Polyline> coast,
                                 ServiceRule service, SurveySpec spec)
        {
            var sheets = new List<Sheet>();
            if (coast == null || coast.Count == 0) return new Survey(spec, sheets);

            double alongTrack = spec.SheetGroundWidth;               // long edge, landscape
            double step = alongTrack * (1.0 - spec.OverlapFraction);
            if (step <= 0.0) step = alongTrack;

            // §10.1's total order, reused: longest first, ties by first vertex (x asc, y asc).
            // Deterministic and independent of the order Contours happened to return.
            var order = new List<int>();
            for (int i = 0; i < coast.Count; i++) order.Add(i);
            order.Sort((a, b) => CompareLoops(coast[a], coast[b]));

            int number = 0;
            for (int oi = 0; oi < order.Count; oi++)
            {
                Polyline loop = coast[order[oi]];
                if (loop == null || loop.Count < 2) continue;

                double len = loop.Length;
                if (len <= 0.0) continue;

                // A loop shorter than one sheet still gets exactly one — a skerry is a real
                // thing to chart, and one sheet is the smallest a survey can spend on it.
                int n = (int)Math.Max(1, Math.Ceiling(len / step));

                for (int k = 0; k < n; k++)
                {
                    // Centred spacing: the first and last sheets sit half an interval in
                    // from the ends, so a closed loop wraps evenly instead of doubling up.
                    double s = (k + 0.5) * len / n;

                    V2 centre, tangent;
                    if (!SampleAt(loop, s, out centre, out tangent)) continue;

                    double rotationDeg = TangentDeg(tangent);

                    if (!Keeps(field, service, spec, centre, rotationDeg)) continue;

                    number++;
                    sheets.Add(new Sheet(spec, number, centre, rotationDeg));
                }
            }

            return new Survey(spec, sheets);
        }

        /// <summary>Longest first; ties by first vertex (x asc, y asc). A total order (§4.1).</summary>
        static int CompareLoops(Polyline a, Polyline b)
        {
            int c = b.Length.CompareTo(a.Length);
            if (c != 0) return c;
            if (a.Count == 0 || b.Count == 0) return a.Count.CompareTo(b.Count);
            c = a[0].X.CompareTo(b[0].X);
            if (c != 0) return c;
            return a[0].Y.CompareTo(b[0].Y);
        }

        /// <summary>
        /// Point and unit tangent at arc length s along the polyline. Walks segments rather
        /// than interpolating an index, so spacing is true arc length and does not bunch up
        /// where marching squares emitted dense vertices — the same bias §10.1 avoids.
        /// </summary>
        static bool SampleAt(Polyline p, double s, out V2 point, out V2 tangent)
        {
            point = V2.Zero;
            tangent = new V2(1, 0);

            int segs = p.Closed ? p.Count : p.Count - 1;
            double acc = 0.0;
            for (int i = 0; i < segs; i++)
            {
                V2 a = p[i];
                V2 b = p[(i + 1) % p.Count];
                V2 d = b - a;
                double segLen = d.Length;
                if (segLen <= 0.0) continue;

                if (acc + segLen >= s || i == segs - 1)
                {
                    double t = (s - acc) / segLen;
                    if (t < 0.0) t = 0.0;
                    if (t > 1.0) t = 1.0;
                    point = V2.Lerp(a, b, t);
                    tangent = d / segLen;
                    return true;
                }
                acc += segLen;
            }
            return false;
        }

        /// <summary>
        /// Tangent direction as a sheet rotation: an AXIS in [0,180), quantised to 0.1 deg.
        /// atan2 is a transcendental, but the result is quantised before it reaches any
        /// branch or the sheet lattice, which is what §4.4 requires.
        /// </summary>
        static double TangentDeg(V2 tangent)
        {
            double deg = Math.Atan2(tangent.Y, tangent.X) * 180.0 / Math.PI;
            return Rotations.NormaliseAxisDeg(deg);
        }

        /// <summary>
        /// The coast crosses every sheet by construction, so only D1's service test remains:
        /// the office must draw something here beyond the coastline itself. In practice its
        /// soundings carry it, which is exactly what D1 predicted.
        /// </summary>
        static bool Keeps(IHeightField field, ServiceRule service, SurveySpec spec,
                          V2 centre, double rotationDeg)
        {
            if (service == null) return true;

            double halfW = spec.SheetGroundWidth * 0.5;
            double halfH = spec.SheetGroundHeight * 0.5;
            int grid = Tuning.CullSampleGrid;

            int land = 0, served = 0;
            for (int a = 0; a < grid; a++)
            {
                for (int b = 0; b < grid; b++)
                {
                    double lx = -halfW + (a + 0.5) * (2.0 * halfW / grid);
                    double ly = -halfH + (b + 0.5) * (2.0 * halfH / grid);
                    V2 g = centre + new V2(lx, ly).RotateDeg(rotationDeg);

                    if (field.Height01(g.X, g.Y) < field.Params.SeaLevel) continue;
                    land++;
                    if (service.Served(g, spec.Office)) served++;
                }
            }

            if (land == 0) return false;               // pure sea: nothing to survey
            return (double)served / land >= Tuning.ServedThreshold;
        }
    }
}
