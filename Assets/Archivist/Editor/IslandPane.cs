using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// §11.0 Pane 1 — the island. Whole island, fit to view, pan and zoom. Draws every feature
    /// class, with the sheet outlines of every survey overlaid as rotated rects (§10.2 step 2),
    /// colour-coded by office. Hover names the sheet; click picks a ground point for Pane 3 and,
    /// if the click landed on a sheet, opens Pane 2 on it.
    ///
    /// The colour coding is the one exception §8.2 allows: it is debug chrome, not the map.
    /// </summary>
    public sealed class IslandPane : IDebugPane
    {
        /// <summary>Ground tile the contour area is snapped to, so a pan reuses the same cache key.</summary>
        const double TileMetres = 2048.0;

        /// <summary>Target contour cell on screen, in view points. Detail follows zoom, not scale.</summary>
        const double TargetCellPx = 2.0;

        const double MinPpm = 1.0e-4;
        const double MaxPpm = 20.0;

        readonly DebugModel _model;
        readonly VisualElement _root;
        readonly VisualElement _canvas;
        readonly VisualElement _textHost;
        readonly TextLayer _text;
        readonly Label _tooltip;

        ViewTransform _view;
        bool _viewInitialised;

        // ---- geometry cache, keyed by (snapped area, lod). A pan changes neither. ----
        List<Polyline> _coast = new List<Polyline>();
        List<Polyline> _contours = new List<Polyline>();
        List<Sounding> _soundings = new List<Sounding>();
        List<Polyline> _grid = new List<Polyline>();
        Rect2 _cachedArea;
        int _cachedLod = -1;
        bool _cacheValid;

        bool _panning;
        bool _dragged;
        Vector2 _lastPointer;
        Sheet? _hovered;

        /// <summary>Raised when a click lands on a sheet outline (§11.0: click opens Pane 2).</summary>
        public event Action<Sheet> SheetClicked;

        /// <summary>Raised on every click, with the ground point — Pane 3's input (§11.0).</summary>
        public event Action<V2> PointPicked;

        public VisualElement Root { get { return _root; } }

        public IslandPane(DebugModel model)
        {
            _model = model;

            _root = new VisualElement();
            _root.style.flexGrow = 1.0f;
            _root.style.flexDirection = FlexDirection.Column;

            _canvas = new VisualElement();
            _canvas.style.flexGrow = 1.0f;
            _canvas.style.backgroundColor = VectorDraw.Paper;
            _canvas.style.overflow = Overflow.Hidden;
            _canvas.generateVisualContent += OnPaint;
            _root.Add(_canvas);

            _textHost = new VisualElement();
            _textHost.style.position = Position.Absolute;
            _textHost.style.left = 0.0f;
            _textHost.style.top = 0.0f;
            _textHost.style.right = 0.0f;
            _textHost.style.bottom = 0.0f;
            _textHost.pickingMode = PickingMode.Ignore;
            _canvas.Add(_textHost);
            _text = new TextLayer(_textHost);

            _tooltip = new Label();
            _tooltip.style.position = Position.Absolute;
            _tooltip.style.display = DisplayStyle.None;
            _tooltip.pickingMode = PickingMode.Ignore;
            _tooltip.style.backgroundColor = new Color(1.0f, 1.0f, 1.0f, 0.92f);
            _tooltip.style.color = Color.black;
            _tooltip.style.fontSize = 11.0f;
            _tooltip.style.paddingLeft = 4.0f;
            _tooltip.style.paddingRight = 4.0f;
            _tooltip.style.paddingTop = 1.0f;
            _tooltip.style.paddingBottom = 1.0f;
            _tooltip.style.borderLeftWidth = 1.0f;
            _tooltip.style.borderRightWidth = 1.0f;
            _tooltip.style.borderTopWidth = 1.0f;
            _tooltip.style.borderBottomWidth = 1.0f;
            _canvas.Add(_tooltip);

            _canvas.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            _canvas.RegisterCallback<WheelEvent>(OnWheel);
            _canvas.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _canvas.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _canvas.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _canvas.RegisterCallback<PointerLeaveEvent>(evt => HideTooltip());

            _view = ViewTransform.Neutral;
        }

        /// <summary>Drop the view so the next Rebuild refits — called after regeneration.</summary>
        public void ResetView()
        {
            _viewInitialised = false;
            _cacheValid = false;
            _cachedLod = -1;
            _hovered = null;
            HideTooltip();
        }

        Rect CanvasRect()
        {
            Rect r = _canvas.contentRect;
            if (!VectorDraw.Settled(r, 2.0f))
            {
                return new Rect(0.0f, 0.0f, 0.0f, 0.0f);
            }

            return new Rect(0.0f, 0.0f, r.width, r.height);
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            Rebuild();
        }

        public void Rebuild()
        {
            Rect rect = CanvasRect();
            if (rect.width <= 0.0f)
            {
                _text.Clear();
                _canvas.MarkDirtyRepaint();
                return;
            }

            if (!_viewInitialised && _model.HasIsland)
            {
                _view = ViewTransform.Fit(_model.ViewExtent(), rect, 0.0, 12.0f);
                _viewInitialised = true;
            }
            else
            {
                _view = _view.WithViewCentre(rect);
            }

            EnsureGeometry(rect);
            UpdateText(rect);
            _canvas.MarkDirtyRepaint();
        }

        // ------------------------------------------------------------------ geometry

        Rect2 VisibleGround(Rect rect)
        {
            V2 a = _view.ToWorld(new Vector2(rect.xMin, rect.yMin));
            V2 b = _view.ToWorld(new Vector2(rect.xMax, rect.yMin));
            V2 c = _view.ToWorld(new Vector2(rect.xMax, rect.yMax));
            V2 d = _view.ToWorld(new Vector2(rect.xMin, rect.yMax));
            Rect2 r = Rect2.Empty;
            r = r.Encapsulate(a);
            r = r.Encapsulate(b);
            r = r.Encapsulate(c);
            r = r.Encapsulate(d);
            return r;
        }

        static Rect2 SnapToTiles(Rect2 r)
        {
            return new Rect2(Math.Floor(r.MinX / TileMetres) * TileMetres,
                             Math.Floor(r.MinY / TileMetres) * TileMetres,
                             Math.Ceiling(r.MaxX / TileMetres) * TileMetres,
                             Math.Ceiling(r.MaxY / TileMetres) * TileMetres);
        }

        /// <summary>
        /// LOD wanted by the current zoom. §6.2 picks LOD from paper detail for a sheet; the island
        /// pane has no paper, so it picks from screen detail instead and lets the sample budget
        /// have the final word.
        /// </summary>
        int DesiredLod()
        {
            double cellMetres = TargetCellPx / Math.Max(1.0e-9, _view.PixelsPerMetre);
            double lod = Math.Ceiling(Math.Log(Tuning.BaseCell / Math.Max(1.0e-6, cellMetres), 2.0));
            if (lod < 0.0) lod = 0.0;
            if (lod > Tuning.MaxLod) lod = Tuning.MaxLod;
            return (int)lod;
        }

        void EnsureGeometry(Rect rect)
        {
            if (!_model.HasIsland)
            {
                _coast.Clear();
                _contours.Clear();
                _soundings.Clear();
                _grid.Clear();
                _cacheValid = false;
                return;
            }

            Rect2 extent = _model.ViewExtent();
            Rect2 area = VisibleGround(rect).Intersection(extent);
            if (area.IsEmpty || area.Width <= 0.0 || area.Height <= 0.0)
            {
                area = extent;
            }

            Rect2 tile = SnapToTiles(area);
            int lod = DesiredLod();

            if (_cacheValid && lod == _cachedLod && SameRect(tile, _cachedArea))
            {
                return;
            }

            _cachedArea = tile;
            _cachedLod = lod;
            _cacheValid = true;

            SheetGeometry g = SheetContent.Gather(_model, tile, lod, GarrisonScale(), GatherGate, true);
            _coast = g.Coast;
            _contours = g.Contours;
            _grid = g.Grid;
            _soundings = g.Soundings;
        }

        /// <summary>
        /// This pane has no office, so the §8.3 matrix does not apply: the user's layer toggles
        /// decide instead. Coast and contours are the exception — they are gathered whatever the
        /// toggles say, because the cache above is keyed on (area, lod) alone, so emptying them
        /// here would leave an empty list behind a still-valid cache when they are switched back
        /// on. The toggle is applied to them at paint time instead.
        /// </summary>
        bool GatherGate(FeatureClass cls)
        {
            return cls == FeatureClass.Coast || cls == FeatureClass.Contour || _model.Layer(cls);
        }

        /// <summary>
        /// The §6.4 grid is defined by a survey's scale. Pane 1 has no sheet, so it borrows the
        /// Garrison survey's — the only office that draws a grid at all.
        /// </summary>
        MapScale GarrisonScale()
        {
            Survey garrison = _model.Island.SurveyFor(Office.Garrison);
            return garrison != null ? garrison.Spec.Scale : MapScale.WholeIsland;
        }

        /// <summary>The point features this pane draws and letters.</summary>
        FeatureMarks Marks()
        {
            IslandFeatures f = _model.Island.Features;
            return new FeatureMarks(_soundings, f.Peaks, f.Settlements, f.Pois);
        }

        static bool SameRect(Rect2 a, Rect2 b)
        {
            return Math.Abs(a.MinX - b.MinX) < 0.5 && Math.Abs(a.MinY - b.MinY) < 0.5
                && Math.Abs(a.MaxX - b.MaxX) < 0.5 && Math.Abs(a.MaxY - b.MaxY) < 0.5;
        }

        // ------------------------------------------------------------------ text

        void UpdateText(Rect rect)
        {
            _text.Begin();
            if (!_model.HasIsland)
            {
                _text.Add(_model.Error != null ? "generation failed — see console" : "no island",
                          new Vector2(12.0f, 12.0f), 12.0f, Color.black);
                _text.End();
                return;
            }

            FeatureLabels.Add(_text, Marks(), _view, LabelGate, v => Inside(rect, v));

            // Sheet numbers, so the outlines are readable without hovering every one.
            if (_model.ShowSheetOutlines)
            {
                IReadOnlyList<Survey> surveys = _model.Island.Surveys;
                for (int i = 0; i < surveys.Count; i++)
                {
                    if (!IsSurveyVisible(i))
                    {
                        continue;
                    }

                    Survey survey = surveys[i];
                    Color colour = DebugModel.OfficeColour(survey.Spec);
                    for (int k = 0; k < survey.Sheets.Count; k++)
                    {
                        Sheet sheet = survey.Sheets[k];
                        Vector2 v = _view.ToView(sheet.CentreGround);
                        if (!Inside(rect, v))
                        {
                            continue;
                        }

                        _text.Add(sheet.Number.ToString(CultureInfo.InvariantCulture), v, 10.0f,
                                  colour, true, true);
                    }
                }
            }

            _text.End();
        }

        /// <summary>
        /// Which classes get lettering. Same layer toggles as the ink, with one extra rule:
        /// sounding depths appear only once a sounding lattice step is wide enough to read.
        /// </summary>
        bool LabelGate(FeatureClass cls)
        {
            if (cls == FeatureClass.Sounding)
            {
                return _model.Layer(cls) && _view.PixelsPerMetre * Tuning.SoundingLattice > 34.0;
            }

            return _model.Layer(cls);
        }

        static bool Inside(Rect rect, Vector2 v)
        {
            return v.x >= rect.xMin - 40.0f && v.x <= rect.xMax + 40.0f
                && v.y >= rect.yMin - 20.0f && v.y <= rect.yMax + 20.0f;
        }

        bool IsSurveyVisible(int index)
        {
            if (index < 0 || index >= _model.SurveyVisible.Length)
            {
                return false;
            }

            Survey s = _model.Island.Surveys[index];
            return _model.SurveyVisible[index] && s != null && s.SheetCount > 0;
        }

        // ------------------------------------------------------------------ paint

        void OnPaint(MeshGenerationContext ctx)
        {
            if (!_model.HasIsland)
            {
                return;
            }

            Rect rect = CanvasRect();
            if (rect.width <= 0.0f)
            {
                return;
            }

            Painter2D p = ctx.painter2D;

            // --- map ink: one style, uniform weight, black on white (§8.2). This pane has no
            // office, so the layer toggles stand in for the §8.3 matrix, and the marks are drawn a
            // notch smaller than on a sheet because the whole island is in view at once. ---
            IslandFeatures f = _model.Island.Features;
            VectorDraw.PaintFeatures(p,
                FeatureLines.FromPolylines(_grid, _contours, _coast, VectorDraw.Courses(f.Rivers)),
                Marks(), _view, rect, MarkSizes.Overview, _model.Layer, v => Inside(rect, v));

            // --- debug chrome: the sheet outlines, colour-coded by office (§11.0) ---
            if (_model.ShowSheetOutlines)
            {
                IReadOnlyList<Survey> surveys = _model.Island.Surveys;
                for (int i = 0; i < surveys.Count; i++)
                {
                    if (!IsSurveyVisible(i))
                    {
                        continue;
                    }

                    Survey survey = surveys[i];
                    VectorDraw.BeginChrome(p, DebugModel.OfficeColour(survey.Spec), 1.0f);
                    for (int k = 0; k < survey.Sheets.Count; k++)
                    {
                        VectorDraw.Quad(p, survey.Sheets[k].GroundCorners(), _view);
                    }
                }

                if (_model.SelectedSheet.HasValue)
                {
                    Sheet sel = _model.SelectedSheet.Value;
                    VectorDraw.BeginChrome(p, DebugModel.OfficeColour(sel.Survey), 3.0f);
                    VectorDraw.Quad(p, sel.GroundCorners(), _view);
                }

                if (_hovered.HasValue)
                {
                    Sheet h = _hovered.Value;
                    VectorDraw.BeginChrome(p, DebugModel.OfficeColour(h.Survey), 2.0f);
                    VectorDraw.Quad(p, h.GroundCorners(), _view);
                }
            }

            // --- the Compare point (§11.0 Pane 3 input) ---
            if (_model.ComparePoint.HasValue)
            {
                VectorDraw.BeginChrome(p, new Color(0.85f, 0.10f, 0.35f), 1.5f);
                Vector2 v = _view.ToView(_model.ComparePoint.Value);
                p.BeginPath();
                p.MoveTo(new Vector2(v.x - 9.0f, v.y));
                p.LineTo(new Vector2(v.x + 9.0f, v.y));
                p.MoveTo(new Vector2(v.x, v.y - 9.0f));
                p.LineTo(new Vector2(v.x, v.y + 9.0f));
                p.Stroke();
                p.BeginPath();
                p.Arc(v, 5.0f, Angle.Degrees(0.0f), Angle.Degrees(360.0f));
                p.Stroke();
            }
        }

        // ------------------------------------------------------------------ input

        void OnWheel(WheelEvent evt)
        {
            if (!_model.HasIsland)
            {
                return;
            }

            double factor = Math.Pow(1.15, -evt.delta.y);
            _view = _view.ZoomedAbout(evt.localMousePosition, factor, MinPpm, MaxPpm);
            evt.StopPropagation();
            Rebuild();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            _panning = true;
            _dragged = false;
            _lastPointer = evt.localPosition;
            _canvas.CapturePointer(evt.pointerId);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            Vector2 pos = evt.localPosition;

            if (_panning)
            {
                Vector2 delta = pos - _lastPointer;
                if (delta.sqrMagnitude > 0.0f)
                {
                    if (delta.sqrMagnitude > 9.0f || _dragged)
                    {
                        _dragged = true;
                    }

                    _lastPointer = pos;
                    _view = _view.Panned(delta);
                    Rebuild();
                }

                return;
            }

            Sheet? hit = HitTest(pos);
            if (!SameSheet(hit, _hovered))
            {
                _hovered = hit;
                _canvas.MarkDirtyRepaint();
            }

            if (hit.HasValue)
            {
                _tooltip.text = DebugModel.SheetLabel(hit.Value);
                _tooltip.style.display = DisplayStyle.Flex;
                _tooltip.style.left = pos.x + 14.0f;
                _tooltip.style.top = pos.y + 14.0f;
            }
            else
            {
                HideTooltip();
            }
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (_canvas.HasPointerCapture(evt.pointerId))
            {
                _canvas.ReleasePointer(evt.pointerId);
            }

            bool wasPanning = _panning;
            _panning = false;
            if (!wasPanning || _dragged || !_model.HasIsland)
            {
                return;
            }

            Vector2 pos = evt.localPosition;
            V2 ground = _view.ToWorld(pos);

            // Every click picks a point for Pane 3; a click that also lands on a sheet opens Pane 2.
            if (PointPicked != null)
            {
                PointPicked(ground);
            }

            Sheet? hit = HitTest(pos);
            if (hit.HasValue && SheetClicked != null)
            {
                SheetClicked(hit.Value);
            }
        }

        void HideTooltip()
        {
            _tooltip.style.display = DisplayStyle.None;
        }

        static bool SameSheet(Sheet? a, Sheet? b)
        {
            if (a.HasValue != b.HasValue)
            {
                return false;
            }

            if (!a.HasValue)
            {
                return true;
            }

            return a.Value.Number == b.Value.Number
                && a.Value.Survey.Office == b.Value.Survey.Office
                && a.Value.Survey.IsWholeIsland == b.Value.Survey.IsWholeIsland;
        }

        /// <summary>Topmost visible sheet under a view point, or null.</summary>
        Sheet? HitTest(Vector2 posPx)
        {
            if (!_model.HasIsland || !_model.ShowSheetOutlines)
            {
                return null;
            }

            V2 ground = _view.ToWorld(posPx);
            Sheet? found = null;
            IReadOnlyList<Survey> surveys = _model.Island.Surveys;
            for (int i = 0; i < surveys.Count; i++)
            {
                if (!IsSurveyVisible(i))
                {
                    continue;
                }

                Survey survey = surveys[i];
                for (int k = 0; k < survey.Sheets.Count; k++)
                {
                    if (DebugModel.SheetContains(survey.Sheets[k], ground))
                    {
                        found = survey.Sheets[k];
                    }
                }
            }

            return found;
        }
    }
}
