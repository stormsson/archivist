using System;
using System.Collections.Generic;
using System.Globalization;
using Archivist.Generation;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Archivist.Editor
{
    /// <summary>
    /// §11.0 Pane 2 — one sheet, drawn at paper aspect with its margin.
    ///
    /// The point of this pane is the restriction, not the drawing: it renders ONLY the classes the
    /// sheet's own office draws, per the §8.3 matrix. That restriction is what the whole POC is
    /// testing (§1.2), so it is applied here at the single point where content is selected and
    /// nowhere else.
    ///
    /// Style is neutral throughout (§8.2): one line style, uniform weight, black on white. The
    /// header strip is UI chrome sitting above the paper, not lettering on the sheet.
    /// </summary>
    public sealed class SheetPane : IDebugPane
    {
        readonly DebugModel _model;
        readonly VisualElement _root;
        readonly Label _header;
        readonly Label _subheader;
        readonly ScrollView _scroll;
        readonly VisualElement _centring;
        readonly VisualElement _paper;
        readonly VisualElement _mapArea;
        readonly VisualElement _textHost;
        readonly TextLayer _text;
        readonly Toggle _trueSize;
        readonly Label _empty;

        ViewTransform _view;

        // ---- per-sheet geometry cache. Re-fitting or resizing must not re-contour. ----
        List<Polyline> _coast = new List<Polyline>();
        List<Polyline> _contours = new List<Polyline>();
        List<Polyline> _grid = new List<Polyline>();
        List<Sounding> _soundings = new List<Sounding>();
        string _cacheKey;

        float _paperW;
        float _paperH;
        float _mapW;
        float _mapH;

        public VisualElement Root { get { return _root; } }

        public SheetPane(DebugModel model)
        {
            _model = model;

            _root = new VisualElement();
            _root.style.flexGrow = 1.0f;
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.display = DisplayStyle.None;

            _header = new Label();
            _header.style.unityFontStyleAndWeight = FontStyle.Bold;
            _header.style.paddingLeft = 6.0f;
            _header.style.paddingTop = 4.0f;
            _root.Add(_header);

            _subheader = new Label();
            _subheader.style.paddingLeft = 6.0f;
            _subheader.style.fontSize = 11.0f;
            _subheader.style.opacity = 0.8f;
            _root.Add(_subheader);

            VisualElement controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.alignItems = Align.Center;
            controls.style.paddingLeft = 6.0f;
            controls.style.paddingBottom = 4.0f;

            _trueSize = new Toggle("true size (real mm)");
            _trueSize.value = _model.TrueSize;
            _trueSize.tooltip = "§11: renders the paper at actual millimetres from Screen.dpi — "
                              + "the only way to judge whether 16 sheets per survey reads as an "
                              + "archive or as a chore.";
            _trueSize.RegisterValueChangedCallback(evt =>
            {
                _model.TrueSize = evt.newValue;
                Rebuild();
            });
            controls.Add(_trueSize);

            Label dpi = new Label();
            dpi.style.marginLeft = 12.0f;
            dpi.style.fontSize = 11.0f;
            dpi.style.opacity = 0.7f;
            dpi.text = string.Format(CultureInfo.InvariantCulture,
                                     "screen {0:F0} dpi · {1:F2} pt/mm",
                                     ScreenDpi(), PointsPerMm());
            controls.Add(dpi);
            _root.Add(controls);

            _empty = new Label("No sheet selected. Click a sheet in the Island pane, or a sheet number in the surveys list.");
            _empty.style.paddingLeft = 6.0f;
            _empty.style.opacity = 0.7f;
            _root.Add(_empty);

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            _scroll.style.flexGrow = 1.0f;
            _root.Add(_scroll);

            _centring = new VisualElement();
            _centring.style.alignItems = Align.Center;
            _centring.style.justifyContent = Justify.Center;
            _centring.style.paddingTop = 8.0f;
            _centring.style.paddingBottom = 8.0f;
            _scroll.Add(_centring);

            _paper = new VisualElement();
            _paper.style.backgroundColor = VectorDraw.Paper;
            _paper.style.borderLeftWidth = 1.0f;
            _paper.style.borderRightWidth = 1.0f;
            _paper.style.borderTopWidth = 1.0f;
            _paper.style.borderBottomWidth = 1.0f;
            _paper.style.borderLeftColor = VectorDraw.Ink;
            _paper.style.borderRightColor = VectorDraw.Ink;
            _paper.style.borderTopColor = VectorDraw.Ink;
            _paper.style.borderBottomColor = VectorDraw.Ink;
            _paper.style.flexShrink = 0.0f;
            _centring.Add(_paper);

            _mapArea = new VisualElement();
            _mapArea.style.position = Position.Absolute;
            _mapArea.style.overflow = Overflow.Hidden;
            _mapArea.style.borderLeftWidth = 1.0f;
            _mapArea.style.borderRightWidth = 1.0f;
            _mapArea.style.borderTopWidth = 1.0f;
            _mapArea.style.borderBottomWidth = 1.0f;
            _mapArea.style.borderLeftColor = VectorDraw.Ink;
            _mapArea.style.borderRightColor = VectorDraw.Ink;
            _mapArea.style.borderTopColor = VectorDraw.Ink;
            _mapArea.style.borderBottomColor = VectorDraw.Ink;
            _mapArea.generateVisualContent += OnPaintMap;
            _paper.Add(_mapArea);

            _textHost = new VisualElement();
            _textHost.style.position = Position.Absolute;
            _textHost.style.left = 0.0f;
            _textHost.style.top = 0.0f;
            _textHost.style.right = 0.0f;
            _textHost.style.bottom = 0.0f;
            _textHost.pickingMode = PickingMode.Ignore;
            _mapArea.Add(_textHost);
            _text = new TextLayer(_textHost);

            _scroll.RegisterCallback<GeometryChangedEvent>(evt => Rebuild());

            // The map area's own size settles one layout pass after the paper is resized, and the
            // view transform is derived from it. Re-derive without touching layout, so no recursion.
            _mapArea.RegisterCallback<GeometryChangedEvent>(evt => RefreshView());

            _view = ViewTransform.Neutral;
        }

        static float ScreenDpi()
        {
            float dpi = Screen.dpi;
            if (dpi <= 1.0f || float.IsNaN(dpi))
            {
                dpi = 96.0f;
            }

            return dpi;
        }

        /// <summary>
        /// UI Toolkit lays out in points, the screen is measured in device pixels; true size has to
        /// cross both. §11 wants real millimetres, so: mm -&gt; device px via Screen.dpi, device px
        /// -&gt; points via the editor's pixelsPerPoint.
        /// </summary>
        static float PointsPerMm()
        {
            float devicePxPerMm = ScreenDpi() / 25.4f;
            float ppp = EditorGUIUtility.pixelsPerPoint;
            if (ppp <= 0.0f || float.IsNaN(ppp))
            {
                ppp = 1.0f;
            }

            return devicePxPerMm / ppp;
        }

        public void Rebuild()
        {
            if (!_model.HasIsland || !_model.SelectedSheet.HasValue)
            {
                _header.text = "";
                _subheader.text = "";
                _empty.style.display = DisplayStyle.Flex;
                _paper.style.display = DisplayStyle.None;
                _text.Clear();
                return;
            }

            _empty.style.display = DisplayStyle.None;
            _paper.style.display = DisplayStyle.Flex;
            _trueSize.SetValueWithoutNotify(_model.TrueSize);

            Sheet sheet = _model.SelectedSheet.Value;
            SurveySpec spec = sheet.Survey;

            UpdateHeader(sheet, spec);
            EnsureGeometry(sheet, spec);

            if (!LayoutPaper(spec))
            {
                return;
            }

            RefreshView();
        }

        /// <summary>Derive the view transform from the settled map-area size, then repaint.</summary>
        void RefreshView()
        {
            if (!_model.HasIsland || !_model.SelectedSheet.HasValue)
            {
                return;
            }

            Sheet sheet = _model.SelectedSheet.Value;
            SurveySpec spec = sheet.Survey;

            Rect map = MapRect();
            double ppm = map.width > 0.0f
                ? map.width / Math.Max(1.0e-6, spec.SheetGroundWidth)
                : 1.0;

            _view = ViewTransform.Neutral;
            _view.WorldCentre = sheet.CentreGround;
            _view.RotationDeg = sheet.RotationDeg;
            _view.PixelsPerMetre = ppm;
            _view.ViewCentre = new Vector2(map.width * 0.5f, map.height * 0.5f);

            UpdateText(sheet, spec, map);
            _mapArea.MarkDirtyRepaint();
        }

        void UpdateHeader(Sheet sheet, SurveySpec spec)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string office = spec.IsWholeIsland
                ? "whole-island (" + DebugModel.OfficeName(spec.Office) + ")"
                : DebugModel.OfficeName(spec.Office);

            _header.text = string.Format(ci, "{0} — {1} — sheet {2} of {3}",
                                         _model.Island.Name, office, sheet.Number,
                                         SheetCountOf(spec));

            List<string> drawn = new List<string>();
            for (int i = 0; i < FeatureClasses.Count; i++)
            {
                FeatureClass cls = (FeatureClass)i;
                if (FeatureMatrix.Draws(spec.Office, cls))
                {
                    drawn.Add(cls.ToString().ToLowerInvariant());
                }
            }

            _subheader.text = string.Format(ci,
                "year {0} · scale {1} · rotation {2:F1}° · paper {3:F0}×{4:F0} mm, margin {5:F0} mm · "
                + "ground {6:F0}×{7:F0} m · draws: {8}",
                spec.Year, spec.Scale, sheet.RotationDeg,
                spec.Format.WidthMm, spec.Format.HeightMm, spec.Format.MarginMm,
                spec.SheetGroundWidth, spec.SheetGroundHeight,
                string.Join(", ", drawn.ToArray()));
        }

        int SheetCountOf(SurveySpec spec)
        {
            if (!_model.HasIsland)
            {
                return 0;
            }

            for (int i = 0; i < _model.Island.Surveys.Count; i++)
            {
                Survey s = _model.Island.Surveys[i];
                if (s != null && s.Spec.IsWholeIsland == spec.IsWholeIsland && s.Spec.Office == spec.Office)
                {
                    return s.SheetCount;
                }
            }

            return 0;
        }

        /// <summary>Sizes the paper, either to fit the viewport or to real millimetres (§11.0).</summary>
        bool LayoutPaper(SurveySpec spec)
        {
            double wMm = Math.Max(1.0, spec.Format.WidthMm);
            double hMm = Math.Max(1.0, spec.Format.HeightMm);

            float w, h;
            if (_model.TrueSize)
            {
                float ptPerMm = PointsPerMm();
                w = (float)(wMm * ptPerMm);
                h = (float)(hMm * ptPerMm);
            }
            else
            {
                Rect avail = _scroll.contentRect;
                if (!VectorDraw.Settled(avail, 40.0f))
                {
                    return false;
                }

                float availW = avail.width - 24.0f;
                float availH = avail.height - 24.0f;
                float scale = Mathf.Min(availW / (float)wMm, availH / (float)hMm);
                if (scale <= 0.0f)
                {
                    return false;
                }

                w = (float)wMm * scale;
                h = (float)hMm * scale;
            }

            if (Mathf.Abs(w - _paperW) > 0.5f || Mathf.Abs(h - _paperH) > 0.5f)
            {
                _paperW = w;
                _paperH = h;
                _paper.style.width = w;
                _paper.style.height = h;

                float mm = w / (float)wMm;
                float margin = (float)spec.Format.MarginMm * mm;
                _mapW = Mathf.Max(1.0f, w - 2.0f * margin);
                _mapH = Mathf.Max(1.0f, h - 2.0f * margin);
                _mapArea.style.left = margin;
                _mapArea.style.top = margin;
                _mapArea.style.width = _mapW;
                _mapArea.style.height = _mapH;
            }

            return true;
        }

        Rect MapRect()
        {
            Rect r = _mapArea.contentRect;
            if (!VectorDraw.Settled(r))
            {
                // Layout has not settled yet; use the size we just asked for.
                return new Rect(0.0f, 0.0f, Mathf.Max(1.0f, _mapW), Mathf.Max(1.0f, _mapH));
            }

            return new Rect(0.0f, 0.0f, r.width, r.height);
        }

        // ------------------------------------------------------------------ geometry

        void EnsureGeometry(Sheet sheet, SurveySpec spec)
        {
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3:F1}|{4:F1}",
                                       spec.IsWholeIsland ? "W" : spec.Office.ToString(),
                                       sheet.Number, spec.Scale.Denominator,
                                       sheet.CentreGround.X, sheet.CentreGround.Y);
            if (key == _cacheKey)
            {
                return;
            }

            _cacheKey = key;

            // §8.3 — this is the whole point of the POC. Draw or omit; nothing in between.
            SheetGeometry g = SheetContent.Gather(_model, sheet.GroundBounds,
                                                  Contours.LodForScale(spec.Scale.Denominator),
                                                  spec.Scale, Gate(spec.Office));
            _coast = g.Coast;
            _contours = g.Contours;
            _grid = g.Grid;
            _soundings = g.Soundings;
        }

        /// <summary>The §8.3 matrix as a predicate — the one decision point for what this sheet shows.</summary>
        static Func<FeatureClass, bool> Gate(Office office)
        {
            return cls => FeatureMatrix.Draws(office, cls);
        }

        /// <summary>The point features this sheet draws and letters.</summary>
        FeatureMarks Marks()
        {
            IslandFeatures f = _model.Island.Features;
            return new FeatureMarks(_soundings, f.Peaks, f.Settlements, f.Pois);
        }

        // ------------------------------------------------------------------ text

        void UpdateText(Sheet sheet, SurveySpec spec, Rect map)
        {
            _text.Begin();
            Office office = spec.Office;

            FeatureLabels.Add(_text, Marks(), _view, Gate(office), v => map.Contains(v));

            if (FeatureMatrix.Draws(office, FeatureClass.Grid))
            {
                AddGridLabels(map);
            }

            _text.End();
        }

        /// <summary>§6.4: grid lines carry easting and northing labels in metres from the origin.</summary>
        void AddGridLabels(Rect map)
        {
            for (int i = 0; i < _grid.Count; i++)
            {
                Polyline line = _grid[i];
                if (line == null || line.Count < 2)
                {
                    continue;
                }

                V2 a = line[0];
                V2 b = line[line.Count - 1];
                bool vertical = Math.Abs(a.X - b.X) <= Math.Abs(a.Y - b.Y);
                double value = vertical ? a.X : a.Y;

                // Label where the line first enters the map area.
                for (int k = 0; k < line.Count; k++)
                {
                    Vector2 v = _view.ToView(line[k]);
                    if (!map.Contains(v))
                    {
                        continue;
                    }

                    Vector2 at = vertical
                        ? new Vector2(v.x + 2.0f, map.yMin + 2.0f)
                        : new Vector2(map.xMin + 2.0f, v.y + 2.0f);
                    _text.Add(value.ToString("F0", CultureInfo.InvariantCulture), at, 9.0f, VectorDraw.Ink);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ paint

        void OnPaintMap(MeshGenerationContext ctx)
        {
            if (!_model.HasIsland || !_model.SelectedSheet.HasValue)
            {
                return;
            }

            Sheet sheet = _model.SelectedSheet.Value;
            Office office = sheet.Survey.Office;
            Rect map = MapRect();
            Painter2D p = ctx.painter2D;
            IslandFeatures f = _model.Island.Features;

            // §8.2 — one line style for everything on the sheet; §8.3 decides what is on it.
            VectorDraw.PaintFeatures(p,
                FeatureLines.FromPolylines(_grid, _contours, _coast, VectorDraw.Courses(f.Rivers)),
                Marks(), _view, map, MarkSizes.Sheet, Gate(office), v => map.Contains(v));
        }
    }
}
