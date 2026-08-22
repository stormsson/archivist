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
                                 ServiceRule service, Rect2 landBounds, SurveySpec spec)
        {
            var sheets = new List<Sheet>();
            if (coast == null || coast.Count == 0) return new Survey(spec, sheets);

            double alongTrack = spec.SheetGroundWidth;               // long edge, landscape
            double step = alongTrack * (1.0 - spec.OverlapFraction);
            if (step <= 0.0) step = alongTrack;

            // Only SEA-FACING loops. Contours.Extract returns every closed loop at sea level,
            // which includes the boundaries of inland water — and a hydrographic survey of a
            // lake is exactly what D-H1 excludes. Segments are wound land-on-the-left, so a
            // land mass encloses positive signed area and a lake encloses negative.
            var order = new List<int>();
            for (int i = 0; i < coast.Count; i++)
            {
                Polyline loop = coast[i];
                if (loop == null || !loop.Closed || loop.Count < 3) continue;
                if (SignedArea(loop) <= 0.0) continue;          // inland water, skip

                // Land, but is it SEA-facing? An islet in the middle of a lake encloses
                // positive area exactly like an offshore skerry does, and this office has no
                // business charting it. A land loop lying inside a water loop is not coast.
                if (InsideAnyWaterLoop(coast, loop)) continue;

                // Rocks smaller than this cost a whole sheet each and cluster wherever the
                // generator scattered them. The office charts hazards, but not one sheet per
                // 40 m speck.
                if (loop.Length < Tuning.CoastMinLoopLength) continue;

                order.Add(i);
            }
            order.Sort((a, b) => CompareLoops(coast[a], coast[b]));

            // The expedition's ground: a disc anchored somewhere on the main shore. Sheets
            // outside it are not cut, whichever loop they belong to — so one survey reads as
            // one voyage rather than as a scatter of unrelated visits.
            V2 regionCentre = V2.Zero;
            double regionRadius = double.MaxValue;
            if (order.Count > 0)
            {
                Polyline main = coast[order[0]];            // longest, by the sort below
                Pcg32 rr = Streams.For(spec.IslandSeed, StreamNames.CoastRegion);
                double anchorS = rr.NextDouble() * main.Length;
                V2 anchorTan;
                if (SampleAt(main, anchorS, out regionCentre, out anchorTan))
                {
                    double diag = landBounds.Diagonal;
                    regionRadius = diag * rr.Range(Tuning.CoastRegionRadiusMin,
                                                   Tuning.CoastRegionRadiusMax);
                }
            }

            int number = 0;
            for (int oi = 0; oi < order.Count; oi++)
            {
                Polyline loop = coast[order[oi]];
                if (loop == null || loop.Count < 2) continue;

                double len = loop.Length;
                if (len <= 0.0) continue;

                // Every loop is walked in full; the region disc decides what is actually cut,
                // so there is no per-loop partial arc to start or stop at.
                //
                // Resample the shore at CHORD intervals of one step, then lay each sheet
                // BETWEEN consecutive points, oriented along that chord.
                //
                // Two earlier attempts failed here and both failures are instructive.
                // Stepping by arc length piled sheets on top of each other around every bay,
                // because a coastline wiggling on a ~2600 m wavelength turns 1600 m of arc
                // into ~700 m of ground. Orienting each sheet by a PCA of the shore within
                // its own span then came out jittery, because over 2002 m of a wiggling
                // coast the point cloud is near-circular and the axis is noise.
                //
                // A chord between two points a step apart has neither problem: it is stable
                // by construction, and consecutive sheets abut end to end like a ribbon,
                // which is what a survey of a shore actually looks like.
                double walk = step * 0.04;

                var chain = new List<V2>();
                V2 lastPt = V2.Zero;
                for (double travelled = 0.0; travelled <= len; travelled += walk)
                {
                    double sPos = travelled;
                    if (loop.Closed) { sPos = sPos % len; if (sPos < 0) sPos += len; }
                    else if (sPos > len) break;

                    V2 p, tan;
                    if (!SampleAt(loop, sPos, out p, out tan)) continue;
                    if (chain.Count > 0 && V2.Dist(p, lastPt) < step) continue;
                    chain.Add(p);
                    lastPt = p;
                }

                if (chain.Count >= 2)
                {
                    // Where the coast doubles back into a narrow inlet, two chord midpoints
                    // can land almost on top of each other even though their chain points
                    // are a full step apart. Placed centres are therefore kept apart
                    // directly — the fjord clusters were entirely this.
                    var placedCentres = new List<V2>();
                    double minSep = step * Tuning.CoastMinSheetSeparation;

                    for (int c = 0; c + 1 < chain.Count; c++)
                    {
                        V2 a = chain[c];
                        V2 b = chain[c + 1];
                        V2 axis = b - a;
                        if (axis.LengthSq <= 0.0) continue;

                        V2 dir = axis / axis.Length;

                        // Lean the sheet SEAWARD. Centred on the coastline, half of every
                        // sheet covers ground the office does not chart; a hydrographic
                        // sheet is mostly water with the shore along one side. Segments are
                        // wound land-on-the-left, so the right normal points to open water.
                        V2 seaward = new V2(dir.Y, -dir.X);
                        V2 centre = (a + b) * 0.5 + seaward * (spec.SheetGroundHeight * Tuning.CoastSeawardBias);

                        double rotationDeg = TangentDeg(dir);
                        if (V2.Dist(centre, regionCentre) > regionRadius) continue;
                        if (!Keeps(field, service, spec, centre, rotationDeg)) continue;

                        bool tooClose = false;
                        for (int q = 0; q < placedCentres.Count && !tooClose; q++)
                            if (V2.Dist(placedCentres[q], centre) < minSep) tooClose = true;
                        if (tooClose) continue;

                        number++;
                        sheets.Add(new Sheet(spec, number, centre, rotationDeg));
                        placedCentres.Add(centre);
                    }
                }
                else
                {
                    // A skerry smaller than one step: one sheet, oriented to its long axis.
                    V2 c0, a0;
                    if (SpanFit(loop, 0.0, len, out c0, out a0))
                    {
                        double rot0 = TangentDeg(a0);
                        if (V2.Dist(c0, regionCentre) <= regionRadius
                            && Keeps(field, service, spec, c0, rot0))
                        {
                            number++;
                            sheets.Add(new Sheet(spec, number, c0, rot0));
                        }
                    }
                }
            }

            return new Survey(spec, sheets);
        }

        /// <summary>
        /// Shoelace. Positive means the loop encloses land (segments are wound with land on
        /// the left, §6.1), negative means it encloses water — a lake, which this office
        /// does not survey.
        /// </summary>
        static double SignedArea(Polyline p)
        {
            double a = 0.0;
            for (int i = 0; i < p.Count; i++)
            {
                V2 u = p[i];
                V2 v = p[(i + 1) % p.Count];
                a += u.X * v.Y - v.X * u.Y;
            }
            return a * 0.5;
        }

        /// <summary>
        /// Centre and principal axis of the coast over one sheet's worth of shore. PCA of the
        /// points in the span rather than the chord between its ends, because a span that
        /// wraps a headland has its ends close together and would give a meaningless chord.
        /// Falls back to the local tangent when the span is too short or too round to have
        /// an axis.
        /// </summary>
        static bool SpanFit(Polyline loop, double centreS, double span, out V2 centre, out V2 axis)
        {
            centre = V2.Zero;
            axis = new V2(1, 0);

            double len = loop.Length;
            if (len <= 0.0) return false;

            const int Samples = 12;
            var pts = new List<V2>(Samples);
            V2 sum = V2.Zero;
            for (int i = 0; i < Samples; i++)
            {
                double t = centreS + (i - (Samples - 1) * 0.5) * (span / (Samples - 1));
                if (loop.Closed) { t = t % len; if (t < 0) t += len; }
                else if (t < 0 || t > len) continue;

                V2 p, tan;
                if (!SampleAt(loop, t, out p, out tan)) continue;
                pts.Add(p);
                sum = sum + p;
            }
            if (pts.Count < 2) return SampleAt(loop, centreS, out centre, out axis);

            centre = sum / pts.Count;

            PcaResult pca = Pca.PrincipalAxis(pts, Tuning.PcaIsotropyThreshold, 2);
            if (pca.Degenerate)
            {
                V2 p2, tan2;
                if (!SampleAt(loop, centreS, out p2, out tan2)) return false;
                axis = tan2;
                return true;
            }

            double rad = pca.AngleDeg * Math.PI / 180.0;
            axis = new V2(Math.Cos(rad), Math.Sin(rad));
            return true;
        }

        /// <summary>
        /// Is this land loop enclosed by a water loop? Ray cast the loop's first vertex
        /// against every negative-area loop; land inside one is an island in a lake.
        /// </summary>
        static bool InsideAnyWaterLoop(IReadOnlyList<Polyline> coast, Polyline land)
        {
            V2 probe = land[0];
            for (int i = 0; i < coast.Count; i++)
            {
                Polyline w = coast[i];
                if (w == null || !w.Closed || w.Count < 3) continue;
                if (ReferenceEquals(w, land)) continue;
                if (SignedArea(w) >= 0.0) continue;             // only water loops
                if (ContainsPoint(w, probe)) return true;
            }
            return false;
        }

        /// <summary>Even-odd ray cast. Loops are closed, so the wrap-around edge counts.</summary>
        static bool ContainsPoint(Polyline p, V2 q)
        {
            bool inside = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                V2 a = p[i], b = p[j];
                if ((a.Y > q.Y) != (b.Y > q.Y))
                {
                    double x = (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X;
                    if (q.X < x) inside = !inside;
                }
            }
            return inside;
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
        ///
        /// <para>
        /// The 16x16 sampling itself lives in <see cref="RectCull"/>, shared with the lattice
        /// cutter's §10.3 cull. This used to open-code the land test as
        /// <c>Height01 &lt; SeaLevel</c>; that is the exact negation of the
        /// <c>IsLand</c> extension's <c>Height01 &gt;= SeaLevel</c>, over a quantised
        /// <c>h01</c> (D3), so the tie at exactly <c>SeaLevel</c> lands on the same side
        /// either way and the shared sampler culls bit-identically.
        /// </para>
        /// </summary>
        static bool Keeps(IHeightField field, ServiceRule service, SurveySpec spec,
                          V2 centre, double rotationDeg)
        {
            if (service == null) return true;

            double halfW = spec.SheetGroundWidth * 0.5;
            double halfH = spec.SheetGroundHeight * 0.5;
            int grid = Tuning.CullSampleGrid;

            int land, served;
            RectCull.Count(field, service, spec.Office, (a, b) =>
            {
                // SHEET-LOCAL space, cell centres, built around ZERO and translated by the
                // centre afterwards. SurveyCutter.SampleRect reaches the same nominal grid the
                // other way round: it anchors on frameRect.Min and adds a fraction of the full
                // extent. §4.4 forbids collapsing one into the other.
                //
                // Note WHY, because the obvious reason is the wrong one. It is NOT the
                // multiply/divide order: grid is CullSampleGrid = 16, and dividing by a power
                // of two only shifts the exponent, so (w * t) / grid and t * (w / grid) are
                // bit-identical here. The difference is the ANCHOR. Adding a small increment
                // to a large coordinate (frameRect.Min can be kilometres from the origin)
                // discards more low mantissa bits than adding it to a value near zero, so the
                // two routes disagree in the last ulp on roughly a quarter of samples, by up
                // to ~2e-12 m. As a distance that is nothing; as an input to IsLand it is a
                // threshold test, and a sample sitting on the coastline can flip land/sea,
                // move landFraction across the 0.60 cull, and change which sheets exist.
                // The two cutters also differ downstream (centre + RotateDeg here, ToGround
                // there), so these were never two spellings of one expression.
                double lx = -halfW + (a + 0.5) * (2.0 * halfW / grid);
                double ly = -halfH + (b + 0.5) * (2.0 * halfH / grid);
                return centre + new V2(lx, ly).RotateDeg(rotationDeg);
            }, out land, out served);

            // land == 0 is pure sea: nothing to survey. ServedFraction answers 0 there, which
            // is below the threshold, so it needs no special case.
            return RectCull.MeetsServedThreshold(RectCull.ServedFraction(land, served));
        }
    }
}
