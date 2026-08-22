using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Archivist.Generation;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using UnityEngine;
using UnityEngine.UIElements;

namespace Archivist.Editor
{
    /// <summary>
    /// §11.0 Pane 3 — Compare. THE ACCEPTANCE ARTIFACT (A1, §13.1).
    ///
    /// Pick a point on the island (or a sheet); this lists every sheet covering that point and
    /// renders up to four side by side, each cropped to the ground they share. Each cell draws only
    /// the classes its own office draws (§8.3), in the one neutral line style (§8.2), at one common
    /// scale — so the only thing that can differ between two cells is what the office chose to
    /// record, and how it chose to lie the paper on the ground.
    ///
    /// The north-up toggle exists to separate the two: with it on, rotation stops being a variable
    /// and any remaining difference is a difference of content. §13.1 passes if the cells are
    /// plainly different documents, both truthful, still recognisably the same ground.
    /// </summary>
    public sealed class ComparePane : IDebugPane
    {
        /// <summary>§11.0: "renders up to four side by side".</summary>
        public const int MaxCells = 4;

        sealed class Cell
        {
            public VisualElement Root;
            public Label Header;
            public Label Classes;
            public VisualElement Canvas;
            public TextLayer Text;

            public bool Active;
            public Sheet Sheet;
            public ViewTransform View;

            public readonly List<List<V2>> Coast = new List<List<V2>>();
            public readonly List<List<V2>> Contours = new List<List<V2>>();
            public readonly List<List<V2>> Grid = new List<List<V2>>();
            public readonly List<List<V2>> Rivers = new List<List<V2>>();
            public readonly List<Peak> Peaks = new List<Peak>();
            public readonly List<Settlement> Towns = new List<Settlement>();
            public readonly List<Sounding> Soundings = new List<Sounding>();

            public void ClearGeometry()
            {
                Coast.Clear();
                Contours.Clear();
                Grid.Clear();
                Rivers.Clear();
                Peaks.Clear();
                Towns.Clear();
                Soundings.Clear();
            }
        }

        readonly DebugModel _model;
        readonly VisualElement _root;
        readonly Label _title;
        readonly Label _hint;
        readonly VisualElement _sheetList;
        readonly VisualElement _cellRow;
        readonly Cell[] _cells = new Cell[MaxCells];
        readonly Toggle _northUp;
        readonly Toggle _cropOutline;

        readonly List<Sheet> _covering = new List<Sheet>();
        readonly List<bool> _selected = new List<bool>();

        /// <summary>The shared ground, as a convex polygon. Every cell is cropped to exactly this.</summary>
        List<V2> _crop = new List<V2>();

        public VisualElement Root { get { return _root; } }

        public ComparePane(DebugModel model)
        {
            _model = model;

            _root = new VisualElement();
            _root.style.flexGrow = 1.0f;
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.display = DisplayStyle.None;

            _title = new Label();
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.paddingLeft = 6.0f;
            _title.style.paddingTop = 4.0f;
            _root.Add(_title);

            VisualElement controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.alignItems = Align.Center;
            controls.style.paddingLeft = 6.0f;

            _northUp = new Toggle("north-up normalisation");
            _northUp.tooltip = "§11.0: separates \"different because rotated\" from "
                             + "\"different because differently drawn\".";
            _northUp.value = _model.NorthUp;
            _northUp.RegisterValueChangedCallback(evt =>
            {
                _model.NorthUp = evt.newValue;
                Rebuild();
            });
            controls.Add(_northUp);

            _cropOutline = new Toggle("crop outline");
            _cropOutline.tooltip = "Outlines the shared intersection. Debug chrome, not map ink.";
            _cropOutline.value = _model.ShowCropOutline;
            _cropOutline.style.marginLeft = 14.0f;
            _cropOutline.RegisterValueChangedCallback(evt =>
            {
                _model.ShowCropOutline = evt.newValue;
                RefreshViews();
                RepaintCells();
            });
            controls.Add(_cropOutline);

            _hint = new Label();
            _hint.style.marginLeft = 14.0f;
            _hint.style.fontSize = 11.0f;
            _hint.style.opacity = 0.7f;
            controls.Add(_hint);
            _root.Add(controls);

            _sheetList = new VisualElement();
            _sheetList.style.flexDirection = FlexDirection.Row;
            _sheetList.style.flexWrap = Wrap.Wrap;
            _sheetList.style.paddingLeft = 6.0f;
            _sheetList.style.paddingBottom = 4.0f;
            _root.Add(_sheetList);

            _cellRow = new VisualElement();
            _cellRow.style.flexDirection = FlexDirection.Row;
            _cellRow.style.flexGrow = 1.0f;
            _root.Add(_cellRow);

            for (int i = 0; i < MaxCells; i++)
            {
                _cells[i] = BuildCell();
                _cellRow.Add(_cells[i].Root);
            }
        }

        Cell BuildCell()
        {
            Cell cell = new Cell();

            cell.Root = new VisualElement();
            cell.Root.style.flexGrow = 1.0f;
            cell.Root.style.flexBasis = 0.0f;
            cell.Root.style.flexDirection = FlexDirection.Column;
            cell.Root.style.marginLeft = 3.0f;
            cell.Root.style.marginRight = 3.0f;
            cell.Root.style.marginBottom = 4.0f;
            cell.Root.style.display = DisplayStyle.None;

            cell.Header = new Label();
            cell.Header.style.unityFontStyleAndWeight = FontStyle.Bold;
            cell.Header.style.fontSize = 11.0f;
            cell.Root.Add(cell.Header);

            cell.Classes = new Label();
            cell.Classes.style.fontSize = 10.0f;
            cell.Classes.style.opacity = 0.75f;
            cell.Root.Add(cell.Classes);

            cell.Canvas = new VisualElement();
            cell.Canvas.style.flexGrow = 1.0f;
            cell.Canvas.style.backgroundColor = VectorDraw.Paper;
            cell.Canvas.style.overflow = Overflow.Hidden;
            cell.Canvas.style.borderLeftWidth = 1.0f;
            cell.Canvas.style.borderRightWidth = 1.0f;
            cell.Canvas.style.borderTopWidth = 1.0f;
            cell.Canvas.style.borderBottomWidth = 1.0f;
            cell.Canvas.style.borderLeftColor = VectorDraw.Ink;
            cell.Canvas.style.borderRightColor = VectorDraw.Ink;
            cell.Canvas.style.borderTopColor = VectorDraw.Ink;
            cell.Canvas.style.borderBottomColor = VectorDraw.Ink;
            cell.Canvas.generateVisualContent += ctx => PaintCell(ctx, cell);
            cell.Canvas.RegisterCallback<GeometryChangedEvent>(evt => RefreshViews());
            cell.Root.Add(cell.Canvas);

            VisualElement textHost = new VisualElement();
            textHost.style.position = Position.Absolute;
            textHost.style.left = 0.0f;
            textHost.style.top = 0.0f;
            textHost.style.right = 0.0f;
            textHost.style.bottom = 0.0f;
            textHost.pickingMode = PickingMode.Ignore;
            cell.Canvas.Add(textHost);
            cell.Text = new TextLayer(textHost);

            return cell;
        }

        /// <summary>Called when the window changes the compare point or the selected sheet.</summary>
        public void OnSelectionChanged()
        {
            RecomputeCovering();
        }

        void RecomputeCovering()
        {
            _covering.Clear();
            _selected.Clear();

            if (!_model.HasIsland)
            {
                return;
            }

            V2? point = _model.ComparePoint;
            if (!point.HasValue && _model.SelectedSheet.HasValue)
            {
                point = _model.SelectedSheet.Value.CentreGround;
                _model.ComparePoint = point;
            }

            if (!point.HasValue)
            {
                return;
            }

            List<Sheet> hits = _model.SheetsCovering(point.Value);

            // Detail surveys first; the whole-island sheet covers everything by construction
            // (§10.5) and would otherwise take a cell that a real office should have.
            for (int i = 0; i < hits.Count; i++)
            {
                if (!hits[i].Survey.IsWholeIsland)
                {
                    _covering.Add(hits[i]);
                }
            }

            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].Survey.IsWholeIsland)
                {
                    _covering.Add(hits[i]);
                }
            }

            int chosen = 0;
            for (int i = 0; i < _covering.Count; i++)
            {
                bool take = chosen < MaxCells;
                _selected.Add(take);
                if (take)
                {
                    chosen++;
                }
            }
        }

        public void Rebuild()
        {
            _northUp.SetValueWithoutNotify(_model.NorthUp);
            _cropOutline.SetValueWithoutNotify(_model.ShowCropOutline);

            if (!_model.HasIsland)
            {
                _title.text = _model.Error != null ? "generation failed" : "no island";
                _hint.text = "";
                _sheetList.Clear();
                HideAllCells();
                return;
            }

            if (_covering.Count == 0 && _model.ComparePoint.HasValue)
            {
                RecomputeCovering();
            }

            if (!_model.ComparePoint.HasValue)
            {
                _title.text = "Compare — no point picked";
                _hint.text = "Click a point in the Island pane, or pick a sheet number in the surveys list.";
                _sheetList.Clear();
                HideAllCells();
                return;
            }

            V2 point = _model.ComparePoint.Value;
            _title.text = string.Format(CultureInfo.InvariantCulture,
                                        "Compare — ground ({0:F0}, {1:F0}) m · {2} sheet{3} cover this point",
                                        point.X, point.Y, _covering.Count,
                                        _covering.Count == 1 ? "" : "s");

            BuildSheetList();

            List<Sheet> picked = SelectedSheets();
            if (picked.Count == 0)
            {
                // An atoll can leave a point covered by nothing at all (§5.3): show it, do not crash.
                _hint.text = _covering.Count == 0
                    ? "No sheet covers this point — a legitimate answer, not a failure (R1.8 wants gaps)."
                    : "No sheet selected.";
                HideAllCells();
                return;
            }

            _crop = SharedIntersection(picked);
            bool degenerate = _crop.Count < 3;
            if (degenerate)
            {
                _crop = new List<V2>(picked[0].GroundCorners());
            }

            _hint.text = degenerate
                ? "no common intersection — showing the first sheet's footprint"
                : string.Format(CultureInfo.InvariantCulture, "shared ground {0:F0} × {1:F0} m",
                                CropBounds().Width, CropBounds().Height);

            BuildCellGeometry(picked);
            RefreshViews();
            RepaintCells();
        }

        List<Sheet> SelectedSheets()
        {
            List<Sheet> picked = new List<Sheet>();
            for (int i = 0; i < _covering.Count && picked.Count < MaxCells; i++)
            {
                if (i < _selected.Count && _selected[i])
                {
                    picked.Add(_covering[i]);
                }
            }

            return picked;
        }

        void BuildSheetList()
        {
            _sheetList.Clear();
            for (int i = 0; i < _covering.Count; i++)
            {
                int index = i;
                Sheet sheet = _covering[i];
                Toggle t = new Toggle(DebugModel.SheetLabel(sheet));
                t.value = index < _selected.Count && _selected[index];
                t.style.marginRight = 12.0f;
                t.RegisterValueChangedCallback(evt =>
                {
                    if (index >= _selected.Count)
                    {
                        return;
                    }

                    if (evt.newValue && CountSelected() >= MaxCells)
                    {
                        // §11.0 renders up to four; refuse the fifth rather than silently drop one.
                        t.SetValueWithoutNotify(false);
                        _hint.text = "at most " + MaxCells + " sheets are rendered side by side";
                        return;
                    }

                    _selected[index] = evt.newValue;

                    // Rebuild() rebuilds this very toggle list, so never do it inside the toggle's
                    // own callback — defer one frame and let the event finish dispatching.
                    _root.schedule.Execute(Rebuild);
                });
                _sheetList.Add(t);
            }
        }

        int CountSelected()
        {
            int n = 0;
            for (int i = 0; i < _selected.Count; i++)
            {
                if (_selected[i])
                {
                    n++;
                }
            }

            return n;
        }

        void HideAllCells()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i].Active = false;
                _cells[i].Root.style.display = DisplayStyle.None;
                _cells[i].Text.Clear();
            }
        }

        Rect2 CropBounds()
        {
            Rect2 r = Rect2.Empty;
            for (int i = 0; i < _crop.Count; i++)
            {
                r = r.Encapsulate(_crop[i]);
            }

            return r;
        }

        // ------------------------------------------------------------------ geometry

        void BuildCellGeometry(List<Sheet> picked)
        {
            Rect2 bounds = CropBounds();
            IslandFeatures features = _model.Island.Features;

            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                cell.ClearGeometry();

                if (i >= picked.Count)
                {
                    cell.Active = false;
                    cell.Root.style.display = DisplayStyle.None;
                    cell.Text.Clear();
                    continue;
                }

                Sheet sheet = picked[i];
                cell.Active = true;
                cell.Sheet = sheet;
                cell.Root.style.display = DisplayStyle.Flex;

                SurveySpec spec = sheet.Survey;
                Office office = spec.Office;

                // Four headers share one row here, so offices are abbreviated and the
                // "sheet"/"rot" words dropped — the values are unambiguous without them.
                cell.Header.text = string.Format(CultureInfo.InvariantCulture,
                                                 "{0} · {1} · #{2} · {3} · {4:F1}°",
                                                 spec.IsWholeIsland ? "WHOLE" : DebugModel.OfficeAbbr(office),
                                                 spec.Year, sheet.Number, spec.Scale, sheet.RotationDeg);
                cell.Classes.text = "draws: " + DrawnClasses(office);

                int lod = Contours.LodForScale(spec.Scale.Denominator);

                // §8.3 — draw or omit. Every cell asks the matrix, and nothing else decides.
                if (FeatureMatrix.Draws(office, FeatureClass.Coast))
                {
                    ClipInto(_model.CoastFor(bounds, lod), cell.Coast);
                }

                if (FeatureMatrix.Draws(office, FeatureClass.Contour))
                {
                    ClipInto(_model.ContoursFor(bounds, lod, _model.ContourLevels), cell.Contours);
                }

                if (FeatureMatrix.Draws(office, FeatureClass.Grid))
                {
                    try
                    {
                        List<Polyline> g = GarrisonGrid.ForRect(bounds, spec.Scale);
                        if (g != null)
                        {
                            ClipInto(g, cell.Grid);
                        }
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogWarning("[Archivist] garrison grid failed: " + e.Message);
                    }
                }

                if (FeatureMatrix.Draws(office, FeatureClass.Sounding))
                {
                    try
                    {
                        List<Sounding> s = Soundings.ForRect(_model.Island.Field, bounds);
                        if (s != null)
                        {
                            for (int k = 0; k < s.Count; k++)
                            {
                                if (Inside(s[k].Position))
                                {
                                    cell.Soundings.Add(s[k]);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogWarning("[Archivist] soundings failed: " + e.Message);
                    }
                }

                if (FeatureMatrix.Draws(office, FeatureClass.River))
                {
                    for (int k = 0; k < features.Rivers.Count; k++)
                    {
                        Polyline course = features.Rivers[k].Course;
                        if (course != null)
                        {
                            ClipPolyline(course.Points, course.Closed, cell.Rivers);
                        }
                    }
                }

                if (FeatureMatrix.Draws(office, FeatureClass.Peak))
                {
                    for (int k = 0; k < features.Peaks.Count; k++)
                    {
                        if (Inside(features.Peaks[k].Position))
                        {
                            cell.Peaks.Add(features.Peaks[k]);
                        }
                    }
                }

                if (FeatureMatrix.Draws(office, FeatureClass.Settlement))
                {
                    for (int k = 0; k < features.Settlements.Count; k++)
                    {
                        if (Inside(features.Settlements[k].Position))
                        {
                            cell.Towns.Add(features.Settlements[k]);
                        }
                    }
                }
            }
        }

        static string DrawnClasses(Office office)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 7; i++)
            {
                FeatureClass cls = (FeatureClass)i;
                if (!FeatureMatrix.Draws(office, cls))
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(cls.ToString().ToLowerInvariant());
            }

            return sb.ToString();
        }

        void ClipInto(List<Polyline> lines, List<List<V2>> outRuns)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Polyline pl = lines[i];
                if (pl != null)
                {
                    ClipPolyline(pl.Points, pl.Closed, outRuns);
                }
            }
        }

        bool Inside(V2 p)
        {
            return InsideConvex(p, _crop);
        }

        void ClipPolyline(IReadOnlyList<V2> pts, bool closed, List<List<V2>> outRuns)
        {
            if (pts == null || pts.Count < 2 || _crop.Count < 3)
            {
                return;
            }

            int n = closed ? pts.Count : pts.Count - 1;
            List<V2> run = null;
            V2 lastEnd = V2.Zero;

            for (int i = 0; i < n; i++)
            {
                V2 p0 = pts[i];
                V2 p1 = pts[(i + 1) % pts.Count];
                V2 a, b;
                if (!ClipSegment(p0, p1, _crop, out a, out b))
                {
                    run = null;
                    continue;
                }

                if (run != null && V2.DistSq(lastEnd, a) < 1.0e-6)
                {
                    run.Add(b);
                }
                else
                {
                    run = new List<V2>(4);
                    run.Add(a);
                    run.Add(b);
                    outRuns.Add(run);
                }

                lastEnd = b;
            }
        }

        // ------------------------------------------------------------------ convex geometry

        /// <summary>Intersection of the selected sheets' rotated footprints — the shared ground.</summary>
        static List<V2> SharedIntersection(List<Sheet> sheets)
        {
            if (sheets.Count == 0)
            {
                return new List<V2>();
            }

            List<V2> poly = new List<V2>(sheets[0].GroundCorners());
            for (int i = 1; i < sheets.Count && poly.Count > 0; i++)
            {
                V2[] clipper = sheets[i].GroundCorners();
                for (int e = 0; e < clipper.Length && poly.Count > 0; e++)
                {
                    poly = ClipHalfPlane(poly, clipper[e], clipper[(e + 1) % clipper.Length]);
                }
            }

            return poly;
        }

        /// <summary>Sutherland-Hodgman against one edge of a CCW clipper; keeps the left side.</summary>
        static List<V2> ClipHalfPlane(List<V2> poly, V2 a, V2 b)
        {
            List<V2> result = new List<V2>();
            if (poly.Count == 0)
            {
                return result;
            }

            V2 d = b - a;
            for (int i = 0; i < poly.Count; i++)
            {
                V2 p = poly[i];
                V2 q = poly[(i + 1) % poly.Count];
                double sp = V2.Cross(d, p - a);
                double sq = V2.Cross(d, q - a);
                bool inP = sp >= 0.0;
                bool inQ = sq >= 0.0;

                if (inP)
                {
                    result.Add(p);
                }

                if (inP != inQ)
                {
                    double denom = sp - sq;
                    if (Math.Abs(denom) > 1.0e-12)
                    {
                        result.Add(V2.Lerp(p, q, sp / denom));
                    }
                }
            }

            return result;
        }

        static bool InsideConvex(V2 p, IReadOnlyList<V2> convex)
        {
            if (convex == null || convex.Count < 3)
            {
                return false;
            }

            for (int i = 0; i < convex.Count; i++)
            {
                V2 a = convex[i];
                V2 b = convex[(i + 1) % convex.Count];
                if (V2.Cross(b - a, p - a) < 0.0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Liang-Barsky against a convex polygon: one interval per segment, or nothing.</summary>
        static bool ClipSegment(V2 p0, V2 p1, IReadOnlyList<V2> convex, out V2 a, out V2 b)
        {
            a = p0;
            b = p1;
            double tmin = 0.0;
            double tmax = 1.0;

            for (int i = 0; i < convex.Count; i++)
            {
                V2 e0 = convex[i];
                V2 e1 = convex[(i + 1) % convex.Count];
                V2 d = e1 - e0;
                double c0 = V2.Cross(d, p0 - e0);
                double c1 = V2.Cross(d, p1 - e0);

                if (c0 < 0.0 && c1 < 0.0)
                {
                    return false;
                }

                if (c0 >= 0.0 && c1 >= 0.0)
                {
                    continue;
                }

                double denom = c0 - c1;
                if (Math.Abs(denom) < 1.0e-12)
                {
                    continue;
                }

                double t = c0 / denom;
                if (c0 < 0.0)
                {
                    if (t > tmin) tmin = t;
                }
                else
                {
                    if (t < tmax) tmax = t;
                }

                if (tmin > tmax)
                {
                    return false;
                }
            }

            a = V2.Lerp(p0, p1, tmin);
            b = V2.Lerp(p0, p1, tmax);
            return V2.DistSq(a, b) > 1.0e-12;
        }

        // ------------------------------------------------------------------ views

        /// <summary>
        /// Fit every cell to the same shared ground, then force one common scale across all of them.
        /// A1 compares content and rotation; it must not also be comparing zoom levels.
        /// </summary>
        void RefreshViews()
        {
            if (_crop.Count < 3)
            {
                return;
            }

            double commonPpm = double.MaxValue;
            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                if (!cell.Active)
                {
                    continue;
                }

                Rect rect = CanvasRect(cell);
                if (rect.width <= 0.0f)
                {
                    continue;
                }

                double rot = _model.NorthUp ? 0.0 : cell.Sheet.RotationDeg;
                cell.View = ViewTransform.Fit(_crop, rect, rot, 10.0f);
                if (cell.View.PixelsPerMetre < commonPpm)
                {
                    commonPpm = cell.View.PixelsPerMetre;
                }
            }

            if (commonPpm == double.MaxValue || commonPpm <= 0.0)
            {
                return;
            }

            for (int i = 0; i < _cells.Length; i++)
            {
                Cell cell = _cells[i];
                if (!cell.Active)
                {
                    continue;
                }

                cell.View.PixelsPerMetre = commonPpm;
                UpdateCellText(cell);
            }

            RepaintCells();
        }

        static Rect CanvasRect(Cell cell)
        {
            Rect r = cell.Canvas.contentRect;
            if (float.IsNaN(r.width) || r.width < 2.0f || r.height < 2.0f)
            {
                return new Rect(0.0f, 0.0f, 0.0f, 0.0f);
            }

            return new Rect(0.0f, 0.0f, r.width, r.height);
        }

        void RepaintCells()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].Active)
                {
                    _cells[i].Canvas.MarkDirtyRepaint();
                }
            }
        }

        void UpdateCellText(Cell cell)
        {
            Rect rect = CanvasRect(cell);
            cell.Text.Begin();

            for (int i = 0; i < cell.Peaks.Count; i++)
            {
                Peak p = cell.Peaks[i];
                Vector2 v = cell.View.ToView(p.Position);
                if (!rect.Contains(v))
                {
                    continue;
                }

                string label = p.SpotHeightM.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(p.Name))
                {
                    label = p.Name + " " + label;
                }

                cell.Text.Add(label, new Vector2(v.x + 6.0f, v.y - 7.0f), 10.0f, VectorDraw.Ink);
            }

            for (int i = 0; i < cell.Towns.Count; i++)
            {
                Vector2 v = cell.View.ToView(cell.Towns[i].Position);
                if (rect.Contains(v))
                {
                    cell.Text.Add(cell.Towns[i].Name, new Vector2(v.x + 6.0f, v.y - 7.0f), 10.0f, VectorDraw.Ink);
                }
            }

            for (int i = 0; i < cell.Soundings.Count; i++)
            {
                Vector2 v = cell.View.ToView(cell.Soundings[i].Position);
                if (rect.Contains(v))
                {
                    cell.Text.Add(cell.Soundings[i].DepthM.ToString(CultureInfo.InvariantCulture),
                                  new Vector2(v.x + 3.0f, v.y - 6.0f), 9.0f, VectorDraw.Ink);
                }
            }

            cell.Text.End();
        }

        void PaintCell(MeshGenerationContext ctx, Cell cell)
        {
            if (!cell.Active || _crop.Count < 3)
            {
                return;
            }

            Rect rect = CanvasRect(cell);
            if (rect.width <= 0.0f)
            {
                return;
            }

            Painter2D p = ctx.painter2D;

            // §8.2 — one line style. This is the whole reason A1 can attribute a difference.
            VectorDraw.BeginInk(p);

            VectorDraw.Runs(p, cell.Grid, false, cell.View, rect);
            VectorDraw.Runs(p, cell.Contours, false, cell.View, rect);
            VectorDraw.Runs(p, cell.Coast, false, cell.View, rect);
            VectorDraw.Runs(p, cell.Rivers, false, cell.View, rect);

            if (cell.Soundings.Count > 0)
            {
                p.BeginPath();
                for (int i = 0; i < cell.Soundings.Count; i++)
                {
                    VectorDraw.AppendTick(p, cell.Soundings[i].Position, 2.0f, cell.View);
                }

                p.Stroke();
            }

            if (cell.Peaks.Count > 0)
            {
                p.BeginPath();
                for (int i = 0; i < cell.Peaks.Count; i++)
                {
                    VectorDraw.AppendTriangle(p, cell.Peaks[i].Position, 4.0f, cell.View);
                }

                p.Stroke();
            }

            for (int i = 0; i < cell.Towns.Count; i++)
            {
                VectorDraw.Ring(p, cell.Towns[i].Position, 3.5f, cell.View);
            }

            // Chrome: the crop boundary, so a rotated cell still shows where the shared ground ends.
            if (_model.ShowCropOutline)
            {
                VectorDraw.BeginChrome(p, new Color(0.0f, 0.0f, 0.0f, 0.22f), 1.0f);
                VectorDraw.Polygon(p, _crop, cell.View);
            }
        }
    }
}
