using System;
using System.Collections.Generic;
using Archivist.Generation.Features;
using Archivist.Generation.Geometry;
using UnityEngine;
using UnityEngine.UIElements;

namespace Archivist.Editor
{
    /// <summary>
    /// Ground space (metres, Y up) -> view space (UI points, Y down), with an optional frame
    /// rotation so a rotated sheet (§10.2 step 2 — sheets tile axis-aligned in frame space and
    /// are rotated rects in ground space) can be drawn axis-aligned on its paper.
    /// </summary>
    public struct ViewTransform
    {
        /// <summary>Ground point that sits at <see cref="ViewCentre"/>.</summary>
        public V2 WorldCentre;

        /// <summary>Frame rotation in degrees. Ground is rotated by -RotationDeg before scaling.</summary>
        public double RotationDeg;

        /// <summary>View points per ground metre.</summary>
        public double PixelsPerMetre;

        /// <summary>The view-space point that <see cref="WorldCentre"/> maps to.</summary>
        public Vector2 ViewCentre;

        /// <summary>An identity-ish transform, safe to draw with when there is nothing to show.</summary>
        public static ViewTransform Neutral
        {
            get
            {
                ViewTransform t = new ViewTransform();
                t.WorldCentre = V2.Zero;
                t.RotationDeg = 0.0;
                t.PixelsPerMetre = 1.0;
                t.ViewCentre = Vector2.zero;
                return t;
            }
        }

        /// <summary>Fit an axis-aligned ground rect into a viewport, in the given frame rotation.</summary>
        public static ViewTransform Fit(Rect2 world, Rect viewport, double rotationDeg, float paddingPx)
        {
            if (world.IsEmpty)
            {
                return Fit(new List<V2>(), viewport, rotationDeg, paddingPx);
            }

            List<V2> corners = new List<V2>(4);
            corners.Add(new V2(world.MinX, world.MinY));
            corners.Add(new V2(world.MaxX, world.MinY));
            corners.Add(new V2(world.MaxX, world.MaxY));
            corners.Add(new V2(world.MinX, world.MaxY));
            return Fit(corners, viewport, rotationDeg, paddingPx);
        }

        /// <summary>Fit an arbitrary point set (e.g. the Compare pane's intersection polygon).</summary>
        public static ViewTransform Fit(IReadOnlyList<V2> pts, Rect viewport, double rotationDeg, float paddingPx)
        {
            ViewTransform t = new ViewTransform();
            t.RotationDeg = rotationDeg;
            t.PixelsPerMetre = 1.0;
            t.WorldCentre = V2.Zero;
            t.ViewCentre = new Vector2(viewport.x + viewport.width * 0.5f, viewport.y + viewport.height * 0.5f);

            if (pts == null || pts.Count == 0)
            {
                return t;
            }

            Rect2 frame = Rect2.Empty;
            for (int i = 0; i < pts.Count; i++)
            {
                frame = frame.Encapsulate(pts[i].RotateDeg(-rotationDeg));
            }

            t.WorldCentre = frame.Centre.RotateDeg(rotationDeg);

            double availW = Math.Max(8.0, viewport.width - 2.0 * paddingPx);
            double availH = Math.Max(8.0, viewport.height - 2.0 * paddingPx);
            double w = Math.Max(1e-6, frame.Width);
            double h = Math.Max(1e-6, frame.Height);
            double ppm = Math.Min(availW / w, availH / h);
            if (!(ppm > 0.0) || double.IsInfinity(ppm) || double.IsNaN(ppm))
            {
                ppm = 1.0;
            }

            t.PixelsPerMetre = ppm;
            return t;
        }

        public Vector2 ToView(V2 world)
        {
            V2 d = (world - WorldCentre).RotateDeg(-RotationDeg);
            return new Vector2((float)(ViewCentre.x + d.X * PixelsPerMetre),
                               (float)(ViewCentre.y - d.Y * PixelsPerMetre));
        }

        public V2 ToWorld(Vector2 view)
        {
            double ppm = PixelsPerMetre <= 0.0 ? 1.0 : PixelsPerMetre;
            V2 d = new V2((view.x - ViewCentre.x) / ppm, -(view.y - ViewCentre.y) / ppm);
            return WorldCentre + d.RotateDeg(RotationDeg);
        }

        /// <summary>Zoom keeping the ground point currently under <paramref name="pivotPx"/> fixed.</summary>
        public ViewTransform ZoomedAbout(Vector2 pivotPx, double factor, double minPpm, double maxPpm)
        {
            V2 anchor = ToWorld(pivotPx);
            ViewTransform t = this;
            double ppm = PixelsPerMetre * factor;
            if (ppm < minPpm) ppm = minPpm;
            if (ppm > maxPpm) ppm = maxPpm;
            t.PixelsPerMetre = ppm;

            V2 d = new V2((pivotPx.x - ViewCentre.x) / ppm, -(pivotPx.y - ViewCentre.y) / ppm);
            t.WorldCentre = anchor - d.RotateDeg(RotationDeg);
            return t;
        }

        /// <summary>Pan by a view-space delta (the direction the content moves).</summary>
        public ViewTransform Panned(Vector2 deltaPx)
        {
            ViewTransform t = this;
            double ppm = PixelsPerMetre <= 0.0 ? 1.0 : PixelsPerMetre;
            V2 d = new V2(deltaPx.x / ppm, -deltaPx.y / ppm);
            t.WorldCentre = WorldCentre - d.RotateDeg(RotationDeg);
            return t;
        }

        /// <summary>Re-centre on a viewport without changing scale or rotation.</summary>
        public ViewTransform WithViewCentre(Rect viewport)
        {
            ViewTransform t = this;
            t.ViewCentre = new Vector2(viewport.x + viewport.width * 0.5f, viewport.y + viewport.height * 0.5f);
            return t;
        }
    }

    /// <summary>
    /// How big the point marks are drawn, in view points. §8.2 fixes the line WEIGHT and says
    /// nothing about mark SIZE, so the panes had quietly drifted into three different schemes plus
    /// a fourth in the SVG exporter. This is the one record of them.
    ///
    /// Nothing in §13 measures these, so the numbers are a judgement, not a requirement:
    /// <see cref="Sheet"/> is the majority scheme (Panes 2 and 3, which agreed with each other),
    /// and Pane 1 gets that same table scaled by <see cref="OverviewScale"/> because it draws the
    /// whole island at once and full-size marks silt it up.
    /// </summary>
    public struct MarkSizes
    {
        /// <summary>Half-length of a sounding's cross tick (§6.3).</summary>
        public readonly float SoundingTickHalf;

        /// <summary>Half-size of a peak's triangle (§7.1).</summary>
        public readonly float PeakHalf;

        /// <summary>Radius of a settlement's ring (§7.2).</summary>
        public readonly float SettlementRadius;

        /// <summary>Half-diagonal of a POI's diamond (POC-03).</summary>
        public readonly float PoiHalf;

        public MarkSizes(float soundingTickHalf, float peakHalf, float settlementRadius, float poiHalf)
        {
            SoundingTickHalf = soundingTickHalf;
            PeakHalf = peakHalf;
            SettlementRadius = settlementRadius;
            PoiHalf = poiHalf;
        }

        public MarkSizes Scaled(float k)
        {
            return new MarkSizes(SoundingTickHalf * k, PeakHalf * k, SettlementRadius * k, PoiHalf * k);
        }

        /// <summary>Marks on a sheet — one sheet's worth of ground, drawn on its paper.</summary>
        public static readonly MarkSizes Sheet = new MarkSizes(2.0f, 4.0f, 3.5f, 4.0f);

        /// <summary>
        /// Pane 1 draws the whole island with every sounding in view at once, and wants the same
        /// marks a fraction smaller. One factor, so the four stay in proportion and the reason is
        /// written down rather than living in four unexplained literals.
        /// </summary>
        public const float OverviewScale = 0.875f;

        /// <summary>Marks at island overview (Pane 1). Declared after <see cref="Sheet"/>, which it reads.</summary>
        public static readonly MarkSizes Overview = Sheet.Scaled(OverviewScale);

        /// <summary>
        /// SVG marks are sized in multiples of the stroke width, not in view points: the file is in
        /// millimetres on paper, so a fixed point size would mean nothing there.
        /// </summary>
        public const double SvgStrokeMultiple = 4.0;
    }

    /// <summary>
    /// The line layers a pane has ready to draw, in whichever of the two shapes it holds them:
    /// whole polylines (Panes 1 and 2 draw them and let the painter cull), or point runs already
    /// cut to a crop polygon (Pane 3). Exactly one of the two sets is populated.
    /// </summary>
    public struct FeatureLines
    {
        public IEnumerable<Polyline> Grid, Contours, Coast, Rivers;
        public IEnumerable<List<V2>> GridRuns, ContourRuns, CoastRuns, RiverRuns;

        public static FeatureLines FromPolylines(IEnumerable<Polyline> grid, IEnumerable<Polyline> contours,
                                                 IEnumerable<Polyline> coast, IEnumerable<Polyline> rivers)
        {
            FeatureLines l = new FeatureLines();
            l.Grid = grid; l.Contours = contours; l.Coast = coast; l.Rivers = rivers;
            return l;
        }

        public static FeatureLines FromRuns(IEnumerable<List<V2>> grid, IEnumerable<List<V2>> contours,
                                            IEnumerable<List<V2>> coast, IEnumerable<List<V2>> rivers)
        {
            FeatureLines l = new FeatureLines();
            l.GridRuns = grid; l.ContourRuns = contours; l.CoastRuns = coast; l.RiverRuns = rivers;
            return l;
        }
    }

    /// <summary>The point features a pane has ready to draw and to letter.</summary>
    public struct FeatureMarks
    {
        public readonly IReadOnlyList<Sounding> Soundings;
        public readonly IReadOnlyList<Peak> Peaks;
        public readonly IReadOnlyList<Settlement> Settlements;
        public readonly IReadOnlyList<Poi> Pois;

        public FeatureMarks(IReadOnlyList<Sounding> soundings, IReadOnlyList<Peak> peaks,
                            IReadOnlyList<Settlement> settlements, IReadOnlyList<Poi> pois)
        {
            Soundings = soundings; Peaks = peaks; Settlements = settlements; Pois = pois;
        }
    }

    /// <summary>
    /// The one place anything reaches <see cref="Painter2D"/>. Everything the window draws goes
    /// through here so that §8.2's rule — one line style, uniform weight, black on white — holds
    /// by construction. Colour appears only where a caller passes debug chrome in.
    /// </summary>
    public static class VectorDraw
    {
        /// <summary>Map ink. §8.2: black on white, no ink colour, ever.</summary>
        public static readonly Color Ink = Color.black;

        /// <summary>Paper. §8.2: no paper tone.</summary>
        public static readonly Color Paper = Color.white;

        /// <summary>Uniform map line weight, in view points (§8.2).</summary>
        public const float InkWidth = 1.0f;

        /// <summary>Points closer than this to the previous emitted point are dropped.</summary>
        const float MinSegmentPx = 0.7f;

        /// <summary>Painter2D dislikes absurd coordinates; clamp when zoomed far in.</summary>
        const float CoordClamp = 1.0e5f;

        static readonly Vector2[] ScratchQuad = new Vector2[4];

        /// <summary>Begin a run of map ink: uniform weight, black, no fill (§8.2).</summary>
        public static void BeginInk(Painter2D p)
        {
            p.lineWidth = InkWidth;
            p.strokeColor = Ink;
            p.fillColor = Ink;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Butt;
        }

        /// <summary>Begin a run of debug chrome — the only place a colour is permitted (§11.0).</summary>
        public static void BeginChrome(Painter2D p, Color colour, float width)
        {
            p.lineWidth = width;
            p.strokeColor = colour;
            p.fillColor = colour;
            p.lineJoin = LineJoin.Miter;
            p.lineCap = LineCap.Butt;
        }

        static Vector2 Clamp(Vector2 v)
        {
            if (float.IsNaN(v.x) || float.IsNaN(v.y)) return Vector2.zero;
            return new Vector2(Mathf.Clamp(v.x, -CoordClamp, CoordClamp),
                               Mathf.Clamp(v.y, -CoordClamp, CoordClamp));
        }

        /// <summary>
        /// Append one polyline as a subpath. Returns false when nothing was appended (culled or
        /// degenerate). The caller owns BeginPath/Stroke so many lines batch into one path.
        /// </summary>
        public static bool AppendPolyline(Painter2D p, IReadOnlyList<V2> pts, bool closed,
                                          ViewTransform t, Rect clip)
        {
            if (pts == null || pts.Count < 2)
            {
                return false;
            }

            Vector2 first = Clamp(t.ToView(pts[0]));
            Vector2 last = first;
            Vector2 minV = first;
            Vector2 maxV = first;

            // One pass: transform, decimate, and accumulate the view-space bbox for culling.
            List<Vector2> buffer = _buffer;
            buffer.Clear();
            buffer.Add(first);

            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 v = Clamp(t.ToView(pts[i]));
                if (v.x < minV.x) minV.x = v.x;
                if (v.y < minV.y) minV.y = v.y;
                if (v.x > maxV.x) maxV.x = v.x;
                if (v.y > maxV.y) maxV.y = v.y;

                float dx = v.x - last.x;
                float dy = v.y - last.y;
                if (dx * dx + dy * dy >= MinSegmentPx * MinSegmentPx || i == pts.Count - 1)
                {
                    buffer.Add(v);
                    last = v;
                }
            }

            if (buffer.Count < 2)
            {
                return false;
            }

            // Cull whole polylines that cannot touch the viewport.
            if (clip.width > 0.0f && clip.height > 0.0f)
            {
                const float pad = 4.0f;
                if (maxV.x < clip.xMin - pad || minV.x > clip.xMax + pad ||
                    maxV.y < clip.yMin - pad || minV.y > clip.yMax + pad)
                {
                    return false;
                }
            }

            p.MoveTo(buffer[0]);
            for (int i = 1; i < buffer.Count; i++)
            {
                p.LineTo(buffer[i]);
            }

            if (closed)
            {
                p.ClosePath();
            }

            return true;
        }

        static readonly List<Vector2> _buffer = new List<Vector2>(1024);

        /// <summary>Stroke many polylines as one batched path — one Stroke call, one weight (§8.2).</summary>
        public static void Lines(Painter2D p, IEnumerable<Polyline> lines, ViewTransform t, Rect clip)
        {
            if (lines == null)
            {
                return;
            }

            p.BeginPath();
            bool any = false;
            foreach (Polyline pl in lines)
            {
                if (pl == null)
                {
                    continue;
                }

                if (AppendPolyline(p, pl.Points, pl.Closed, t, clip))
                {
                    any = true;
                }
            }

            if (any)
            {
                p.Stroke();
            }
        }

        /// <summary>Stroke many already-clipped point runs as one batched path.</summary>
        public static void Runs(Painter2D p, IEnumerable<List<V2>> runs, ViewTransform t, Rect clip)
        {
            if (runs == null)
            {
                return;
            }

            p.BeginPath();
            bool any = false;
            foreach (List<V2> run in runs)
            {
                if (AppendPolyline(p, run, false, t, clip))
                {
                    any = true;
                }
            }

            if (any)
            {
                p.Stroke();
            }
        }

        /// <summary>Stroke a ground-space quad (four corners, in order).</summary>
        public static void Quad(Painter2D p, V2[] corners, ViewTransform t)
        {
            if (corners == null || corners.Length < 4)
            {
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                ScratchQuad[i] = Clamp(t.ToView(corners[i]));
            }

            StrokeQuadPx(p, ScratchQuad);
        }

        /// <summary>Stroke a closed ground-space polygon of any vertex count.</summary>
        public static void Polygon(Painter2D p, IReadOnlyList<V2> poly, ViewTransform t)
        {
            if (poly == null || poly.Count < 2)
            {
                return;
            }

            p.BeginPath();
            p.MoveTo(Clamp(t.ToView(poly[0])));
            for (int i = 1; i < poly.Count; i++)
            {
                p.LineTo(Clamp(t.ToView(poly[i])));
            }

            p.ClosePath();
            p.Stroke();
        }

        static void StrokeQuadPx(Painter2D p, Vector2[] q)
        {
            p.BeginPath();
            p.MoveTo(q[0]);
            p.LineTo(q[1]);
            p.LineTo(q[2]);
            p.LineTo(q[3]);
            p.ClosePath();
            p.Stroke();
        }

        /// <summary>A filled dot — a peak's spot-height mark, a settlement, a sounding position.</summary>
        public static void Dot(Painter2D p, V2 world, float radiusPx, ViewTransform t)
        {
            Vector2 v = Clamp(t.ToView(world));
            p.BeginPath();
            p.Arc(v, Mathf.Max(0.5f, radiusPx), Angle.Degrees(0.0f), Angle.Degrees(360.0f));
            p.Fill();
        }

        /// <summary>A hollow ring — settlements, so they read differently from peaks at one weight.</summary>
        public static void Ring(Painter2D p, V2 world, float radiusPx, ViewTransform t)
        {
            Vector2 v = Clamp(t.ToView(world));
            p.BeginPath();
            p.Arc(v, Mathf.Max(0.5f, radiusPx), Angle.Degrees(0.0f), Angle.Degrees(360.0f));
            p.Stroke();
        }

        /// <summary>Append a small cross tick as a subpath. Cheap enough for a field of soundings.</summary>
        public static void AppendTick(Painter2D p, V2 world, float halfPx, ViewTransform t)
        {
            Vector2 v = Clamp(t.ToView(world));
            p.MoveTo(new Vector2(v.x - halfPx, v.y));
            p.LineTo(new Vector2(v.x + halfPx, v.y));
            p.MoveTo(new Vector2(v.x, v.y - halfPx));
            p.LineTo(new Vector2(v.x, v.y + halfPx));
        }

        /// <summary>
        /// Append a POI's diamond mark as a subpath (POC-03). Distinct at a glance from a
        /// peak's triangle, a settlement's ring and a sounding's cross, at one line weight.
        /// </summary>
        public static void AppendDiamond(Painter2D p, V2 world, float halfPx, ViewTransform t)
        {
            Vector2 v = Clamp(t.ToView(world));
            p.MoveTo(new Vector2(v.x, v.y - halfPx));
            p.LineTo(new Vector2(v.x + halfPx, v.y));
            p.LineTo(new Vector2(v.x, v.y + halfPx));
            p.LineTo(new Vector2(v.x - halfPx, v.y));
            p.ClosePath();
        }

        /// <summary>Append a peak's triangle mark as a subpath.</summary>
        public static void AppendTriangle(Painter2D p, V2 world, float halfPx, ViewTransform t)
        {
            Vector2 v = Clamp(t.ToView(world));
            p.MoveTo(new Vector2(v.x, v.y - halfPx));
            p.LineTo(new Vector2(v.x + halfPx, v.y + halfPx));
            p.LineTo(new Vector2(v.x - halfPx, v.y + halfPx));
            p.ClosePath();
        }

        /// <summary>
        /// True when a laid-out <c>contentRect</c> is big enough to draw into. NaN fails every
        /// comparison, so the single <c>&gt;=</c> catches "layout has not settled yet" as well as
        /// "too small" — which is what the three panes' hand-rolled guards each got partly right.
        /// </summary>
        public static bool Settled(Rect r, float minPx)
        {
            return r.width >= minPx && r.height >= minPx;
        }

        /// <summary>Any positive, non-NaN size at all.</summary>
        public static bool Settled(Rect r)
        {
            return Settled(r, float.Epsilon);
        }

        /// <summary>The rivers' courses as plain polylines, ready for <see cref="Lines"/>.</summary>
        public static IEnumerable<Polyline> Courses(IReadOnlyList<River> rivers)
        {
            if (rivers == null)
            {
                yield break;
            }

            for (int i = 0; i < rivers.Count; i++)
            {
                Polyline course = rivers[i].Course;
                if (course != null)
                {
                    yield return course;
                }
            }
        }

        /// <summary>
        /// Paint one office's map ink, in the order the three interactive panes share: grid,
        /// contours, coast, rivers, then the point marks — soundings, peaks, settlements, POIs.
        ///
        /// The SVG exporter deliberately does NOT come through here: it emits soundings last, and
        /// that per-backend ordering is a real difference, not drift.
        /// </summary>
        /// <param name="gate">Per-class "does this get drawn?". Null means everything; pass the
        /// §8.3 matrix for a sheet or a cell, the layer toggles for Pane 1.</param>
        /// <param name="visible">View-space cull for the point marks. Null means draw them all —
        /// which is what a pane whose geometry is already clipped to its viewport wants.</param>
        public static void PaintFeatures(Painter2D p, FeatureLines lines, FeatureMarks marks,
                                         ViewTransform view, Rect clip, MarkSizes sizes,
                                         Func<FeatureClass, bool> gate, Func<Vector2, bool> visible)
        {
            // §8.2 — one line style, uniform weight, black on white, for every class below.
            BeginInk(p);

            if (Draws(gate, FeatureClass.Grid))
            {
                StrokeLayer(p, lines.Grid, lines.GridRuns, view, clip);
            }

            if (Draws(gate, FeatureClass.Contour))
            {
                StrokeLayer(p, lines.Contours, lines.ContourRuns, view, clip);
            }

            if (Draws(gate, FeatureClass.Coast))
            {
                StrokeLayer(p, lines.Coast, lines.CoastRuns, view, clip);
            }

            if (Draws(gate, FeatureClass.River))
            {
                StrokeLayer(p, lines.Rivers, lines.RiverRuns, view, clip);
            }

            if (Draws(gate, FeatureClass.Sounding) && Count(marks.Soundings) > 0)
            {
                p.BeginPath();
                for (int i = 0; i < marks.Soundings.Count; i++)
                {
                    V2 at = marks.Soundings[i].Position;
                    if (Shows(visible, view, at))
                    {
                        AppendTick(p, at, sizes.SoundingTickHalf, view);
                    }
                }

                p.Stroke();
            }

            if (Draws(gate, FeatureClass.Peak) && Count(marks.Peaks) > 0)
            {
                p.BeginPath();
                for (int i = 0; i < marks.Peaks.Count; i++)
                {
                    V2 at = marks.Peaks[i].Position;
                    if (Shows(visible, view, at))
                    {
                        AppendTriangle(p, at, sizes.PeakHalf, view);
                    }
                }

                p.Stroke();
            }

            if (Draws(gate, FeatureClass.Settlement) && Count(marks.Settlements) > 0)
            {
                for (int i = 0; i < marks.Settlements.Count; i++)
                {
                    V2 at = marks.Settlements[i].Position;
                    if (Shows(visible, view, at))
                    {
                        Ring(p, at, sizes.SettlementRadius, view);
                    }
                }
            }

            // POC-03 — points of interest. A diamond, so it reads apart from the peak triangles
            // and the settlement rings at the one line weight §8.2 allows. Only the Antiquarian
            // office draws them, and its detail sheet is centred on one (spec §2).
            if (Draws(gate, FeatureClass.Poi) && Count(marks.Pois) > 0)
            {
                p.BeginPath();
                for (int i = 0; i < marks.Pois.Count; i++)
                {
                    V2 at = marks.Pois[i].Position;
                    if (Shows(visible, view, at))
                    {
                        AppendDiamond(p, at, sizes.PoiHalf, view);
                    }
                }

                p.Stroke();
            }
        }

        /// <summary>One line layer, in whichever of the two shapes the caller holds it.</summary>
        static void StrokeLayer(Painter2D p, IEnumerable<Polyline> polylines, IEnumerable<List<V2>> runs,
                                ViewTransform view, Rect clip)
        {
            if (polylines != null)
            {
                Lines(p, polylines, view, clip);
            }
            else if (runs != null)
            {
                Runs(p, runs, view, clip);
            }
        }

        static bool Draws(Func<FeatureClass, bool> gate, FeatureClass cls)
        {
            return gate == null || gate(cls);
        }

        static bool Shows(Func<Vector2, bool> visible, ViewTransform view, V2 world)
        {
            return visible == null || visible(view.ToView(world));
        }

        static int Count<T>(IReadOnlyList<T> list)
        {
            return list == null ? 0 : list.Count;
        }
    }

    /// <summary>
    /// Text placement. <see cref="Painter2D"/> draws no glyphs, and a repaint callback may not
    /// add child elements, so labels live in a pooled overlay that is refreshed from the same
    /// place that recomputes the view — never from generateVisualContent.
    ///
    /// Spot heights (§7.1) and sounding depths (§6.3) are numbers on the map, so text is map
    /// content here, not typography: one size, one weight, ink black (§8.2).
    /// </summary>
    public sealed class TextLayer
    {
        readonly VisualElement _host;
        readonly List<Label> _pool = new List<Label>();
        int _used;

        /// <summary>Hard ceiling so a zoomed-out island cannot spawn thousands of labels.</summary>
        public int Budget = 400;

        public TextLayer(VisualElement host)
        {
            _host = host;
            _host.pickingMode = PickingMode.Ignore;
        }

        public void Begin()
        {
            _used = 0;
        }

        /// <summary>Place one label at a view-space point.</summary>
        public void Add(string text, Vector2 posPx, float fontSize, Color colour,
                        bool centreX = false, bool centreY = false)
        {
            if (string.IsNullOrEmpty(text) || _used >= Budget)
            {
                return;
            }

            Label label;
            if (_used < _pool.Count)
            {
                label = _pool[_used];
            }
            else
            {
                label = new Label();
                label.pickingMode = PickingMode.Ignore;
                label.style.position = Position.Absolute;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.unityTextAlign = TextAnchor.UpperLeft;
                _pool.Add(label);
                _host.Add(label);
            }

            _used++;
            label.text = text;
            label.style.display = DisplayStyle.Flex;
            label.style.left = posPx.x;
            label.style.top = posPx.y;
            label.style.fontSize = fontSize;
            label.style.color = colour;
            label.style.translate = new Translate(
                centreX ? Length.Percent(-50.0f) : Length.Percent(0.0f),
                centreY ? Length.Percent(-50.0f) : Length.Percent(0.0f));
        }

        /// <summary>Hide whatever this pass did not use.</summary>
        public void End()
        {
            for (int i = _used; i < _pool.Count; i++)
            {
                _pool[i].style.display = DisplayStyle.None;
            }
        }

        public void Clear()
        {
            Begin();
            End();
        }
    }
}
