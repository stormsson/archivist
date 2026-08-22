using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Archivist.Generation;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using Archivist.Render;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GenIsland = Archivist.Generation.Island;
using RenderLayers = Archivist.Render.LayerMask;

namespace Archivist.Editor
{
    /// <summary>
    /// §9 Pane 4 — Texture. THE ACCEPTANCE ARTIFACT for POC-02 (§11 B1, requirements §3).
    ///
    /// Island overview on the LEFT, the selected sheet on the RIGHT, each rendered at its own
    /// resolution. That pairing *is* the primary criterion — "a viewer locates the sheet's ground
    /// on the overview unaided" (T5.2) — so it is the default layout, not an option behind a
    /// toggle. Everything else on this pane exists to make that judgement, or to measure what it
    /// costs (§11 B4, B5).
    ///
    /// <para><b>Nothing here renders during a repaint.</b> The fill is ~300 ns/pixel, so a
    /// 2000 × 2000 overview is over a second; a pane that rendered on paint would make the window
    /// unusable. A render happens only when an *input* changes — island, selected sheet, layer
    /// mask, or a resolution slider released — and the resulting <see cref="ImageBuffer"/> and
    /// <see cref="Texture2D"/> are cached behind a key string. Panning, resizing, tab switching
    /// and repainting all read the cache. The last measured time is always on screen, so the cost
    /// is visible rather than mysterious.</para>
    /// </summary>
    public sealed class TexturePane : IDebugPane
    {
        /// <summary>
        /// §9 guardrail. A slider must never be able to freeze the Editor, so a request is shrunk
        /// until it fits — and the pane says that it shrank it. ~3.6 s at the measured 300 ns/px.
        /// </summary>
        public const long MaxPixels = 12000000L;

        /// <summary>Longest edge. Keeps the upload copy sane and stays inside every Texture2D limit.</summary>
        public const int MaxDimension = 8192;

        /// <summary>§11 B5 — the overview ladder, in pixels per metre. Reported, never gated.</summary>
        static readonly double[] OverviewLadder = { 0.02, 0.03, 0.05, 0.08, 0.12, 0.18, 0.25 };

        /// <summary>§11 B5 — the sheet ladder, in pixels per paper millimetre (2.7 is §10's default).</summary>
        static readonly double[] SheetLadder = { 0.675, 1.35, 2.7, 5.4, 10.8 };

        const string OverviewSubtitleDefault = "the whole island, north-up";
        const string SheetSubtitleDefault = "one sheet, at its own rotation and scale";

        /// <summary>Slider range for the overview, as log10(pixels per metre). Two decades.</summary>
        const float OverviewLogMin = -2.0f;   // 0.01 px/m
        const float OverviewLogMax = 0.0f;    // 1.00 px/m

        /// <summary>Slider range for the sheet, as log10(pixels per paper millimetre). ~12–500 dpi.</summary>
        const float SheetLogMin = -0.35f;     // ~0.45 px/mm
        const float SheetLogMax = 1.30f;      // ~20 px/mm

        /// <summary>
        /// One column: the UI, plus the cache that keeps a repaint free. <see cref="Key"/> names
        /// the render that produced <see cref="Texture"/>; a Rebuild that computes the same key
        /// does no work at all.
        /// </summary>
        sealed class Side
        {
            public VisualElement Root;
            public Label Title;
            public Label Subtitle;
            public Slider Resolution;
            public Label ResolutionText;
            public Label Status;
            public ScrollView Scroll;
            public Image View;
            public Label Empty;
            public Button Export;

            public ImageBuffer Buffer;
            public Texture2D Texture;
            public string Key;

            public double Millis;
            public double UsedResolution;
            public bool Clamped;
            public bool Dragging;
            public bool PendingRender;
        }

        readonly DebugModel _model;
        readonly VisualElement _root;
        readonly Label _title;
        readonly Side _overview = new Side();
        readonly Side _sheet = new Side();
        readonly Label _report;

        /// <summary>Built by <see cref="BuildControls"/>, so it cannot be readonly.</summary>
        Toggle _sweepExport;

        ScrollView _reportScroll;

        RenderLayers _layers = RenderLayers.All;
        bool _oneToOne;
        bool _ready;

        public VisualElement Root { get { return _root; } }

        /// <summary>The layer mask the pane renders with (§9). Local to this pane: the sidebar's
        /// FeatureClass toggles drive the vector panes and mean something different.</summary>
        public RenderLayers Layers { get { return _layers; } }

        public TexturePane(DebugModel model)
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

            _root.Add(BuildControls());

            VisualElement columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.flexGrow = 1.0f;
            _root.Add(columns);

            BuildSide(_overview, "Island overview", OverviewSubtitleDefault,
                      OverviewLogMin, OverviewLogMax,
                      (float)Math.Log10(RenderTuning.IslandPreviewPxPerMetre));
            BuildSide(_sheet, "Selected sheet", SheetSubtitleDefault,
                      SheetLogMin, SheetLogMax,
                      (float)Math.Log10(RenderTuning.SheetPxPerPaperMm));

            columns.Add(_overview.Root);
            columns.Add(_sheet.Root);

            _report = new Label();
            _report.style.whiteSpace = WhiteSpace.Normal;
            _report.style.fontSize = 11.0f;

            ScrollView reportScroll = new ScrollView();
            reportScroll.style.maxHeight = 170.0f;
            reportScroll.style.flexShrink = 0.0f;
            reportScroll.style.paddingLeft = 6.0f;
            reportScroll.style.paddingRight = 6.0f;
            reportScroll.style.display = DisplayStyle.None;
            reportScroll.Add(_report);
            _reportScroll = reportScroll;
            _root.Add(reportScroll);

            // A pointer released anywhere in the pane flushes a slider drag whose PointerUpEvent
            // did not reach the slider itself. Belt and braces; the slider handles the normal case.
            _root.RegisterCallback<PointerUpEvent>(evt => FlushPending());

            // The window is destroyed by every domain reload (§9) and each Texture2D here is
            // HideAndDontSave, so releasing them on detach is the only thing standing between
            // this pane and a leak per reload.
            _root.RegisterCallback<DetachFromPanelEvent>(evt => ReleaseTextures());

            _ready = true;
        }

        VisualElement BuildControls()
        {
            VisualElement bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.flexWrap = Wrap.Wrap;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 6.0f;
            bar.style.paddingBottom = 2.0f;

            Label layersTag = new Label("layers");
            layersTag.style.unityFontStyleAndWeight = FontStyle.Bold;
            layersTag.style.marginRight = 4.0f;
            bar.Add(layersTag);

            bar.Add(LayerToggle("fill", RenderLayers.Fill));
            bar.Add(LayerToggle("coast", RenderLayers.Coast));
            bar.Add(LayerToggle("rivers", RenderLayers.Rivers));
            bar.Add(LayerToggle("towns", RenderLayers.Settlements));
            bar.Add(LayerToggle("peaks", RenderLayers.Peaks));
            bar.Add(LayerToggle("soundings", RenderLayers.Soundings));

            Toggle oneToOne = new Toggle("1:1 pixels");
            oneToOne.tooltip = "Show the raster at native size inside a scroll view, point-filtered, "
                             + "so individual pixels are inspectable. Zooming and panning never re-render.";
            oneToOne.style.marginLeft = 14.0f;
            oneToOne.RegisterValueChangedCallback(evt =>
            {
                _oneToOne = evt.newValue;
                FitImage(_overview);
                FitImage(_sheet);
            });
            bar.Add(oneToOne);

            Button sweep = new Button(RunSweep);
            sweep.text = "Resolution sweep";
            sweep.tooltip = "§11 B5 — renders the ladder and reports dimensions and milliseconds for "
                          + "each. This is how open question 1 gets an answer with evidence.";
            sweep.style.marginLeft = 14.0f;
            bar.Add(sweep);

            _sweepExport = new Toggle("export sweep PNGs");
            _sweepExport.tooltip = "Write every rung of the sweep to a folder, for eyeballing (§11 B5).";
            bar.Add(_sweepExport);

            return bar;
        }

        Toggle LayerToggle(string label, RenderLayers layer)
        {
            Toggle t = new Toggle(label);
            t.value = (_layers & layer) != 0;
            t.style.marginRight = 8.0f;
            t.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    _layers |= layer;
                }
                else
                {
                    _layers &= ~layer;
                }

                // A layer toggle IS an input change, so it renders — but only the two cached views,
                // and only once.
                RenderSide(_overview);
                RenderSide(_sheet);
            });

            return t;
        }

        void BuildSide(Side side, string title, string subtitle,
                       float logMin, float logMax, float logStart)
        {
            side.Root = new VisualElement();
            side.Root.style.flexGrow = 1.0f;
            side.Root.style.flexBasis = 0.0f;
            side.Root.style.flexDirection = FlexDirection.Column;
            side.Root.style.marginLeft = 4.0f;
            side.Root.style.marginRight = 4.0f;
            side.Root.style.marginBottom = 4.0f;

            side.Title = new Label(title);
            side.Title.style.unityFontStyleAndWeight = FontStyle.Bold;
            side.Title.style.fontSize = 11.0f;
            side.Root.Add(side.Title);

            side.Subtitle = new Label(subtitle);
            side.Subtitle.style.fontSize = 10.0f;
            side.Subtitle.style.opacity = 0.7f;
            side.Root.Add(side.Subtitle);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            // Log scale: the useful range spans two decades, and a linear slider would put every
            // interesting overview value in the first 10% of the track.
            side.Resolution = new Slider(logMin, logMax);
            side.Resolution.value = Mathf.Clamp(logStart, logMin, logMax);
            side.Resolution.showInputField = false;
            side.Resolution.style.flexGrow = 1.0f;
            side.Resolution.RegisterCallback<PointerDownEvent>(evt => { side.Dragging = true; },
                                                               TrickleDown.TrickleDown);
            side.Resolution.RegisterCallback<PointerUpEvent>(evt =>
            {
                side.Dragging = false;
                FlushPending();
            }, TrickleDown.TrickleDown);
            side.Resolution.RegisterValueChangedCallback(evt =>
            {
                if (!_ready)
                {
                    return;
                }

                UpdateResolutionText(side);
                if (side.Dragging)
                {
                    // Dragging shows the resulting dimensions live but renders nothing — a render
                    // per slider tick is a second per tick.
                    side.PendingRender = true;
                }
                else
                {
                    RenderSide(side);
                }
            });
            row.Add(side.Resolution);

            side.ResolutionText = new Label();
            side.ResolutionText.style.fontSize = 10.0f;
            side.ResolutionText.style.minWidth = 150.0f;
            side.ResolutionText.style.marginLeft = 6.0f;
            row.Add(side.ResolutionText);

            side.Export = new Button(() => ExportSide(side));
            side.Export.text = "Export PNG…";
            side.Export.style.fontSize = 10.0f;
            row.Add(side.Export);

            side.Root.Add(row);

            side.Status = new Label();
            side.Status.style.fontSize = 10.0f;
            side.Status.style.opacity = 0.85f;
            side.Status.style.whiteSpace = WhiteSpace.Normal;
            side.Root.Add(side.Status);

            side.Scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            side.Scroll.style.flexGrow = 1.0f;
            side.Scroll.style.borderLeftWidth = 1.0f;
            side.Scroll.style.borderRightWidth = 1.0f;
            side.Scroll.style.borderTopWidth = 1.0f;
            side.Scroll.style.borderBottomWidth = 1.0f;
            Color edge = new Color(0.0f, 0.0f, 0.0f, 0.4f);
            side.Scroll.style.borderLeftColor = edge;
            side.Scroll.style.borderRightColor = edge;
            side.Scroll.style.borderTopColor = edge;
            side.Scroll.style.borderBottomColor = edge;
            // A resize re-fits the element. It never re-renders: FitImage only resizes.
            side.Scroll.RegisterCallback<GeometryChangedEvent>(evt => FitImage(side));
            side.Root.Add(side.Scroll);

            side.View = new Image();
            side.View.scaleMode = ScaleMode.StretchToFill;   // FitImage sizes it to the exact aspect
            side.Scroll.Add(side.View);

            side.Empty = new Label();
            side.Empty.style.fontSize = 11.0f;
            side.Empty.style.opacity = 0.7f;
            side.Empty.style.whiteSpace = WhiteSpace.Normal;
            // The scroll view scrolls horizontally, so the label needs a width of its own to wrap in.
            side.Empty.style.maxWidth = 380.0f;
            side.Empty.style.paddingLeft = 8.0f;
            side.Empty.style.paddingTop = 8.0f;
            side.Empty.style.display = DisplayStyle.None;
            side.Scroll.Add(side.Empty);

            UpdateResolutionText(side);
        }

        // ------------------------------------------------------------------ resolution values

        /// <summary>Overview resolution in pixels per metre, quantised so cache keys are stable.</summary>
        double OverviewPxPerMetre
        {
            get { return Quantise(Math.Pow(10.0, _overview.Resolution.value)); }
        }

        /// <summary>Sheet resolution in pixels per PAPER millimetre (§3 derives px/m from it).</summary>
        double SheetPxPerPaperMm
        {
            get { return Quantise(Math.Pow(10.0, _sheet.Resolution.value)); }
        }

        /// <summary>Four decimals. A slider produces a float; the key must not wobble in the noise.</summary>
        static double Quantise(double v)
        {
            return Math.Round(v, 4, MidpointRounding.AwayFromZero);
        }

        void UpdateResolutionText(Side side)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (side == _overview)
            {
                side.ResolutionText.text = OverviewPxPerMetre.ToString("F3", ci) + " px/m";
            }
            else
            {
                double ppmm = SheetPxPerPaperMm;
                string derived = "";
                if (_model.SelectedSheet.HasValue)
                {
                    double pxPerMetre = ppmm * 1000.0 / _model.SelectedSheet.Value.Survey.Scale.Denominator;
                    derived = " (= " + pxPerMetre.ToString("F3", ci) + " px/m)";
                }

                side.ResolutionText.text = ppmm.ToString("F2", ci) + " px/mm" + derived;
            }
        }

        // ------------------------------------------------------------------ IDebugPane

        /// <summary>
        /// Recompute the cache keys and render only what actually changed. Called on tab switch,
        /// regeneration and sheet selection — never per repaint (see <see cref="IDebugPane"/>).
        /// </summary>
        public void Rebuild()
        {
            if (!_model.HasIsland)
            {
                _title.text = "Texture — " + _model.NoIslandMessage(": " + _model.Error);
                ShowEmpty(_overview, "No island to render.");
                ShowEmpty(_sheet, "No island to render.");
                _overview.Key = null;
                _sheet.Key = null;
                _overview.Status.text = "";
                _sheet.Status.text = "";
                _sheet.Subtitle.text = SheetSubtitleDefault;
                return;
            }

            GenIsland island = _model.Island;
            _title.text = string.Format(CultureInfo.InvariantCulture,
                                        "Texture — {0} · {1} · seed {2}   "
                                      + "(B1: can you find the right-hand sheet's ground on the left?)",
                                        island.Name,
                                        island.Params.Character.ToString().ToLowerInvariant(),
                                        island.Seed);

            UpdateResolutionText(_overview);
            UpdateResolutionText(_sheet);

            RenderSideIfStale(_overview);
            RenderSideIfStale(_sheet);
        }

        void RenderSideIfStale(Side side)
        {
            string key = KeyFor(side);
            if (key != null && key == side.Key && side.Texture != null)
            {
                return;
            }

            RenderSide(side);
        }

        void FlushPending()
        {
            if (_overview.PendingRender)
            {
                _overview.PendingRender = false;
                _overview.Dragging = false;
                RenderSide(_overview);
            }

            if (_sheet.PendingRender)
            {
                _sheet.PendingRender = false;
                _sheet.Dragging = false;
                RenderSide(_sheet);
            }
        }

        // ------------------------------------------------------------------ the render itself

        /// <summary>
        /// Identifies the render a cached texture came from. Every input that can change the
        /// pixels is in here; nothing else is. Two Rebuilds with the same key do no work.
        /// </summary>
        string KeyFor(Side side)
        {
            if (!_model.HasIsland)
            {
                return null;
            }

            CultureInfo ci = CultureInfo.InvariantCulture;
            GenIsland island = _model.Island;
            StringBuilder sb = new StringBuilder();
            sb.Append(island.Seed.ToString(ci));
            sb.Append('|');
            sb.Append(_model.CollectionSeed.ToString(ci));
            sb.Append('|');
            sb.Append(_model.IslandIndex.ToString(ci));
            sb.Append('|');
            sb.Append(_model.ForcedCharacter.HasValue
                          ? _model.ForcedCharacter.Value.ToString()
                          : "auto");
            sb.Append('|');
            sb.Append(((int)_layers).ToString(ci));
            sb.Append('|');

            if (side == _overview)
            {
                sb.Append("island|");
                sb.Append(OverviewPxPerMetre.ToString("R", ci));
                return sb.ToString();
            }

            if (!_model.SelectedSheet.HasValue)
            {
                return null;
            }

            Sheet sheet = _model.SelectedSheet.Value;
            sb.Append("sheet|");
            sb.Append(sheet.Survey.IsWholeIsland ? "whole" : sheet.Survey.Office.ToString());
            sb.Append('|');
            sb.Append(sheet.Number.ToString(ci));
            sb.Append('|');
            sb.Append(sheet.Survey.Scale.Denominator.ToString(ci));
            sb.Append('|');
            sb.Append(sheet.RotationDeg.ToString("F1", ci));
            sb.Append('|');
            sb.Append(SheetPxPerPaperMm.ToString("R", ci));
            return sb.ToString();
        }

        void RenderSide(Side side)
        {
            if (!_ready)
            {
                return;
            }

            UpdateResolutionText(side);

            if (!_model.HasIsland)
            {
                ShowEmpty(side, "No island to render.");
                return;
            }

            if (side == _overview)
            {
                RenderOverview();
            }
            else
            {
                RenderSheet();
            }
        }

        void RenderOverview()
        {
            GenIsland island = _model.Island;

            // A degenerate island (no land above sea level at all) leaves LandBounds empty, and
            // Rect2.Empty has a negative width — every dimension would collapse to 1 px. Fall back
            // to the domain square and say so. The fallback itself is DebugModel.SafeExtent, which
            // is what DebugModel.ViewExtent and HeightMapping.Calibrate use too.
            bool fellBack;
            Rect2 bounds = DebugModel.SafeExtent(island.LandBounds, island.Params.DomainMetres,
                                                 out fellBack);

            double asked = OverviewPxPerMetre;
            bool clamped;
            double used;
            RenderRequest req = OverviewRequest(bounds, asked, out used, out clamped);

            RenderInto(_overview, island, req, KeyFor(_overview), asked, used, clamped);

            if (fellBack)
            {
                _overview.Status.text = "LandBounds is empty — showing the whole "
                                      + island.Params.DomainMetres.ToString("F0", CultureInfo.InvariantCulture)
                                      + " m domain instead. " + _overview.Status.text;
            }
        }

        /// <summary>
        /// The overview request for one asked-for resolution, clamped to fit, and the resolution
        /// actually used. Shared by the interactive render and the sweep (§11 B5): the sweep exists
        /// to measure what the interactive path costs, so if the two built their requests
        /// separately the measurement would quietly stop describing the thing being measured.
        ///
        /// <para><c>RenderRequest.ForIsland(island, used, _layers)</c> builds exactly this when
        /// LandBounds is healthy; the explicit constructor is only here so the empty-bounds
        /// fallback in <see cref="RenderOverview"/> has somewhere to go.</para>
        /// </summary>
        RenderRequest OverviewRequest(Rect2 bounds, double askedPxPerMetre,
                                      out double used, out bool clamped)
        {
            used = FitResolution(bounds.Width, bounds.Height, askedPxPerMetre, out clamped);
            return new RenderRequest(bounds, 0.0, used, RenderTuning.SheetPxPerPaperMm, _layers);
        }

        /// <summary>
        /// The request for one sheet at an asked-for paper resolution, clamped to fit, and the
        /// paper resolution actually used. Shared by the interactive render and the sweep — and the
        /// ForSheet workaround below is precisely why: it used to be written out twice, comment
        /// included, so fixing RenderRequest.ForSheet and deleting one copy would have left the
        /// other rendering the patch of ground at the origin for every sheet.
        /// </summary>
        RenderRequest SheetRequest(Sheet sheet, double askedPpmm, out double usedPpmm, out bool clamped)
        {
            Rect2 frame = sheet.FrameRect;
            double denominator = sheet.Survey.Scale.Denominator;
            double askedPxPerMetre = askedPpmm * 1000.0 / denominator;

            double usedPxPerMetre = FitResolution(frame.Width, frame.Height, askedPxPerMetre, out clamped);

            // Shrink the paper resolution by the same factor rather than only the ground one, so a
            // clamped sheet keeps its stroke weights in proportion (§7 widths are paper mm).
            usedPpmm = askedPpmm * (askedPxPerMetre > 0.0 ? usedPxPerMetre / askedPxPerMetre : 1.0);

            RenderRequest derived = RenderRequest.ForSheet(sheet, usedPpmm, _layers);

            // ForSheet currently re-seats the frame rect at the origin (Archivist.Render/
            // RenderRequest.cs: `new Rect2(0, 0, frame.Width, frame.Height)`), which would render
            // the same patch of ground for every sheet and sink B1 outright. Put the real frame
            // rect back. If ForSheet is fixed to keep the position, this becomes a no-op rather
            // than a conflict.
            return new RenderRequest(frame, derived.RotationDeg, derived.PixelsPerMetre,
                                     derived.PixelsPerPaperMm, derived.Layers);
        }

        void RenderSheet()
        {
            GenIsland island = _model.Island;
            if (!_model.SelectedSheet.HasValue)
            {
                ShowEmpty(_sheet, NoSheetMessage(island));
                _sheet.Key = null;
                _sheet.Status.text = "";
                _sheet.Subtitle.text = SheetSubtitleDefault;
                return;
            }

            Sheet sheet = _model.SelectedSheet.Value;
            Rect2 frame = sheet.FrameRect;
            if (frame.IsEmpty || frame.Width <= 0.0 || frame.Height <= 0.0)
            {
                ShowEmpty(_sheet, "This sheet's frame rect is degenerate — nothing to render.");
                _sheet.Key = null;
                _sheet.Status.text = "";
                return;
            }

            double askedPpmm = SheetPxPerPaperMm;
            bool clamped;
            double usedPpmm;
            RenderRequest req = SheetRequest(sheet, askedPpmm, out usedPpmm, out clamped);

            RenderInto(_sheet, island, req, KeyFor(_sheet), askedPpmm, usedPpmm, clamped);

            V2 c = sheet.CentreGround;
            _sheet.Subtitle.text = string.Format(CultureInfo.InvariantCulture,
                                                 "{0} · centre ({1:F0}, {2:F0}) m · {3:F0} × {4:F0} m of ground",
                                                 DebugModel.SheetLabel(sheet), c.X, c.Y,
                                                 frame.Width, frame.Height);
        }

        /// <summary>§5.3's stress case: an atoll can legitimately cut zero Land Survey sheets.</summary>
        static string NoSheetMessage(GenIsland island)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("No sheet selected. Pick a sheet number in the surveys list on the right, ");
            sb.Append("or click a sheet in the Island pane.");

            int total = 0;
            List<string> empties = new List<string>();
            for (int i = 0; island.Surveys != null && i < island.Surveys.Count; i++)
            {
                Survey survey = island.Surveys[i];
                if (survey == null)
                {
                    continue;
                }

                total += survey.SheetCount;
                if (survey.SheetCount == 0)
                {
                    empties.Add(survey.Spec.IsWholeIsland
                                    ? "whole-island"
                                    : DebugModel.OfficeName(survey.Spec.Office));
                }
            }

            if (total == 0)
            {
                sb.Append("\n\nThis island cut NO sheets at all — a legitimate answer, not a failure "
                        + "(R1.8 wants gaps). B1 cannot be judged on it; try another island index.");
                return sb.ToString();
            }

            if (empties.Count > 0)
            {
                sb.Append("\n\nNote: ");
                for (int i = 0; i < empties.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(empties[i]);
                }

                sb.Append(empties.Count == 1 ? " cut no sheets" : " cut no sheets between them");
                sb.Append(" on this island — measured on every atoll, and not an error.");
            }

            return sb.ToString();
        }

        /// <summary>
        /// The single render call, plus the timing and the cache write. Never called from a paint
        /// path. Failures land in the status line rather than as an exception through the Editor.
        /// </summary>
        void RenderInto(Side side, GenIsland island, RenderRequest req, string key,
                        double asked, double used, bool clamped)
        {
            double millis;
            string error;
            ImageBuffer buf = RenderTimed(island, req, out millis, out error);
            if (buf == null)
            {
                ShowEmpty(side, "Render failed — " + error);
                side.Key = null;
                side.Status.text = "";
                return;
            }

            side.Buffer = buf;
            side.Millis = millis;
            side.UsedResolution = used;
            side.Clamped = clamped;
            side.Key = key;

            Upload(side, buf);
            side.Empty.style.display = DisplayStyle.None;
            side.View.style.display = DisplayStyle.Flex;
            FitImage(side);

            side.Status.text = StatusText(side, asked, used, clamped);
        }

        /// <summary>
        /// One render, timed, never throwing. The only place the Editor calls
        /// <see cref="IslandRenderer.Render"/>: the interactive path and the sweep both need the
        /// Stopwatch around exactly that one call and the same "a failure is a message, not an
        /// exception through the Editor" contract, and two copies meant two catch blocks to keep
        /// in step. Returns null on failure, with <paramref name="error"/> set to the text to show;
        /// the caller supplies its own prefix.
        /// </summary>
        static ImageBuffer RenderTimed(GenIsland island, RenderRequest req,
                                       out double millis, out string error)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                ImageBuffer buf = IslandRenderer.Render(island, req);
                sw.Stop();
                millis = sw.Elapsed.TotalMilliseconds;
                error = null;
                return buf;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                millis = 0.0;
                error = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        /// <summary>
        /// The measurement row: pixels, megapixels, milliseconds, nanoseconds per pixel. §11 B4's
        /// number, and the sweep's (§11 B5) — they were the same four figures computed twice, so
        /// the ns/px the status line showed and the ns/px the sweep report showed were only
        /// coincidentally comparable.
        ///
        /// <para>The two differ in spacing alone: the status line is one line of chrome and reads
        /// with "·", the sweep report is a column and reads with double spaces. The separator is
        /// the parameter; the numbers are not. (Merging them dropped the parentheses the status
        /// line used to put round the MP figure — spacing, not information.)</para>
        /// </summary>
        static string MetricsRow(ImageBuffer buf, double millis, string separator, CultureInfo ci)
        {
            long pixels = (long)buf.Width * buf.Height;

            StringBuilder sb = new StringBuilder();
            sb.Append(buf.Width.ToString(ci));
            sb.Append(" × ");
            sb.Append(buf.Height.ToString(ci));
            sb.Append(" px");
            sb.Append(separator);
            sb.Append((pixels / 1000000.0).ToString("F2", ci));
            sb.Append(" MP");
            sb.Append(separator);
            sb.Append(millis.ToString("F0", ci));
            sb.Append(" ms");
            sb.Append(separator);
            sb.Append((pixels > 0 ? millis * 1000000.0 / pixels : 0.0).ToString("F0", ci));
            sb.Append(" ns/px");
            return sb.ToString();
        }

        string StatusText(Side side, double asked, double used, bool clamped)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder(MetricsRow(side.Buffer, side.Millis, " · ", ci));

            if (clamped)
            {
                string unit = side == _overview ? " px/m" : " px/mm";
                sb.Append("  ·  CLAMPED to ");
                sb.Append((MaxPixels / 1000000.0).ToString("F0", ci));
                sb.Append(" MP: asked ");
                sb.Append(asked.ToString("F3", ci));
                sb.Append(unit);
                sb.Append(", rendered ");
                sb.Append(used.ToString("F3", ci));
                sb.Append(unit);
            }

            return sb.ToString();
        }

        void ShowEmpty(Side side, string message)
        {
            side.Empty.text = message;
            side.Empty.style.display = DisplayStyle.Flex;
            side.View.style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ clamping

        /// <summary>
        /// §9's guardrail. Shrinks a resolution until the image fits <see cref="MaxPixels"/> and
        /// <see cref="MaxDimension"/>, so a slider dragged to the end of its travel costs a
        /// smaller render and a note, never a frozen Editor. Works in doubles throughout: the
        /// un-shrunk dimensions of an absurd request would overflow an int before it got here.
        /// </summary>
        static double FitResolution(double areaWidth, double areaHeight, double pxPerMetre,
                                    out bool clamped)
        {
            clamped = false;
            if (pxPerMetre <= 0.0 || areaWidth <= 0.0 || areaHeight <= 0.0)
            {
                return pxPerMetre;
            }

            double w = areaWidth * pxPerMetre;
            double h = areaHeight * pxPerMetre;
            double factor = 1.0;

            double pixels = w * h;
            if (pixels > MaxPixels)
            {
                factor = Math.Sqrt(MaxPixels / pixels);
            }

            double longest = Math.Max(w, h) * factor;
            if (longest > MaxDimension)
            {
                factor *= MaxDimension / longest;
            }

            if (factor >= 1.0)
            {
                return pxPerMetre;
            }

            clamped = true;
            return pxPerMetre * factor;
        }

        // ------------------------------------------------------------------ texture upload

        /// <summary>
        /// <b>The one and only place the vertical flip happens.</b>
        ///
        /// <para><see cref="ImageBuffer"/> is RGBA32, row-major, TOP-LEFT origin (§2, §8) —
        /// image space is y-down because that is what every raster consumer expects. Unity's
        /// <see cref="Texture2D"/> is BOTTOM-LEFT origin, so <c>LoadRawTextureData</c> on the raw
        /// bytes shows the island upside down, which is easy to miss on a roughly symmetric
        /// island (§2 makes exactly this warning). Rows are therefore reversed here, on upload,
        /// with one BlockCopy each; nothing downstream — UVs, export, the sweep — knows or cares.
        /// PngWriter consumes the unflipped buffer, because PNG is top-left origin too.</para>
        ///
        /// <para>The texture is recreated only when the dimensions change, so a re-render at the
        /// same resolution reuses it and no repaint allocates anything.</para>
        /// </summary>
        static void Upload(Side side, ImageBuffer buf)
        {
            if (side.Texture == null || side.Texture.width != buf.Width || side.Texture.height != buf.Height)
            {
                if (side.Texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(side.Texture);
                    side.Texture = null;
                }

                side.Texture = new Texture2D(buf.Width, buf.Height, TextureFormat.RGBA32, false);
                side.Texture.wrapMode = TextureWrapMode.Clamp;
                side.Texture.hideFlags = HideFlags.HideAndDontSave;
            }

            int stride = buf.Width * 4;
            byte[] flipped = new byte[buf.Pixels.Length];
            for (int y = 0; y < buf.Height; y++)
            {
                System.Buffer.BlockCopy(buf.Pixels, y * stride,
                                        flipped, (buf.Height - 1 - y) * stride, stride);
            }

            side.Texture.LoadRawTextureData(flipped);
            side.Texture.Apply(false, false);
            side.View.image = side.Texture;
        }

        /// <summary>
        /// Resize the element to the raster's aspect. Pure layout — it never renders, which is
        /// what makes a window resize and a pan free.
        /// </summary>
        void FitImage(Side side)
        {
            if (side.Texture == null)
            {
                return;
            }

            float texW = side.Texture.width;
            float texH = side.Texture.height;

            float scale = 1.0f;
            if (!_oneToOne)
            {
                // 18 px of gutter keeps the scrollbars from fighting the fit.
                float viewW = side.Scroll.resolvedStyle.width - 18.0f;
                float viewH = side.Scroll.resolvedStyle.height - 18.0f;
                if (viewW > 1.0f && viewH > 1.0f)
                {
                    scale = Mathf.Min(viewW / texW, viewH / texH);
                    scale = Mathf.Min(scale, 1.0f);   // never upscale in fit mode; 1:1 is the zoom
                }
            }

            // Point when at or above 1:1 so pixels are inspectable (§9); bilinear when shrunk,
            // where point sampling would drop thin coastline strokes and make B1 harder to judge
            // than the render actually is.
            side.Texture.filterMode = scale >= 1.0f ? FilterMode.Point : FilterMode.Bilinear;

            side.View.style.width = Mathf.Max(1.0f, texW * scale);
            side.View.style.height = Mathf.Max(1.0f, texH * scale);
        }

        void ReleaseTextures()
        {
            ReleaseTexture(_overview);
            ReleaseTexture(_sheet);
        }

        static void ReleaseTexture(Side side)
        {
            if (side.Texture != null)
            {
                UnityEngine.Object.DestroyImmediate(side.Texture);
                side.Texture = null;
            }

            side.View.image = null;
            side.Buffer = null;
            side.Key = null;
        }

        // ------------------------------------------------------------------ export (§8)

        void ExportSide(Side side)
        {
            if (!_model.HasIsland)
            {
                EditorUtility.DisplayDialog("Island Debug", "Nothing to export — generation failed.", "OK");
                return;
            }

            bool haveSheet = side == _overview || _model.SelectedSheet.HasValue;
            if (side.Buffer == null || !haveSheet)
            {
                EditorUtility.DisplayDialog("Island Debug",
                                            side == _overview
                                                ? "Nothing rendered yet."
                                                : "No sheet selected — pick one in the surveys list.",
                                            "OK");
                return;
            }

            string folder = EditorUtility.SaveFolderPanel("Export render as PNG", "", "");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            string name = side == _overview
                ? IslandFileName(_model.Island, side.UsedResolution)
                : SheetFileName(_model.Island, _model.SelectedSheet.Value, side.UsedResolution);

            string path = folder + "/" + name;
            try
            {
                PngWriter.Write(side.Buffer, path);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                EditorUtility.DisplayDialog("Island Debug", "Export failed — " + e.Message, "OK");
                return;
            }

            string summary = "wrote " + path + "  ("
                           + side.Buffer.Width.ToString(CultureInfo.InvariantCulture) + " × "
                           + side.Buffer.Height.ToString(CultureInfo.InvariantCulture) + " px)";
            UnityEngine.Debug.Log("[Archivist] " + summary);
            EditorUtility.DisplayDialog("Island Debug", summary, "OK");
        }

        /// <summary>§8: filenames encode the request, so exports are self-describing and diffable.</summary>
        static string IslandFileName(GenIsland island, double pxPerMetre)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            return "island_s" + island.Seed.ToString(ci) + "_px" + pxPerMetre.ToString("F3", ci) + ".png";
        }

        /// <summary>§8: <c>sheet_s&lt;seed&gt;_&lt;office&gt;_&lt;number&gt;_pp&lt;px per paper mm&gt;.png</c>.</summary>
        static string SheetFileName(GenIsland island, Sheet sheet, double pxPerPaperMm)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            string office = sheet.Survey.IsWholeIsland
                ? "whole"
                : DebugModel.OfficeAbbr(sheet.Survey.Office).ToLowerInvariant();

            return "sheet_s" + island.Seed.ToString(ci)
                 + "_" + office
                 + "_" + sheet.Number.ToString(ci)
                 + "_pp" + pxPerPaperMm.ToString("F2", ci) + ".png";
        }

        // ------------------------------------------------------------------ the sweep (§11 B5)

        /// <summary>
        /// §11 B5 — renders the ladder and reports dimensions and milliseconds for each rung.
        /// This is the evidence that answers open question 1 in requirements.md ("what resolution
        /// is recognisable?"): reported, never gated, because a budget before the measurement
        /// would be a guess (T4.3).
        ///
        /// <para>The sweep renders into locals and never touches the interactive caches, so the
        /// two views on screen are exactly what they were before it ran.</para>
        /// </summary>
        void RunSweep()
        {
            if (!_model.HasIsland)
            {
                EditorUtility.DisplayDialog("Island Debug", "Nothing to sweep — generation failed.", "OK");
                return;
            }

            GenIsland island = _model.Island;
            string folder = null;
            if (_sweepExport != null && _sweepExport.value)
            {
                folder = EditorUtility.SaveFolderPanel("Export sweep PNGs", "", "");
                if (string.IsNullOrEmpty(folder))
                {
                    folder = null;
                }
            }

            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder();
            sb.Append("resolution sweep — ");
            sb.Append(island.Name);
            sb.Append(" · ");
            sb.Append(island.Params.Character.ToString().ToLowerInvariant());
            sb.Append(" · seed ");
            sb.Append(island.Seed.ToString(ci));
            sb.Append(" · layers ");
            sb.Append(_layers.ToString());

            // Same fallback the interactive overview takes, from the same place (§11.0).
            bool fellBack;
            Rect2 bounds = DebugModel.SafeExtent(island.LandBounds, island.Params.DomainMetres,
                                                 out fellBack);
            if (fellBack)
            {
                sb.Append("\n(LandBounds empty — swept over the whole domain instead)");
            }

            try
            {
                for (int i = 0; i < OverviewLadder.Length; i++)
                {
                    double asked = OverviewLadder[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Resolution sweep",
                            "island overview at " + asked.ToString("F3", ci) + " px/m",
                            (float)i / OverviewLadder.Length))
                    {
                        sb.Append("\n  (cancelled)");
                        break;
                    }

                    bool clamped;
                    double used;
                    RenderRequest req = OverviewRequest(bounds, asked, out used, out clamped);

                    sb.Append("\n  island  ");
                    sb.Append(asked.ToString("F3", ci));
                    sb.Append(" px/m  ");
                    sb.Append(SweepRow(island, req, clamped, used, ci,
                                       folder, IslandFileName(island, used)));
                }

                if (_model.SelectedSheet.HasValue)
                {
                    Sheet sheet = _model.SelectedSheet.Value;
                    Rect2 frame = sheet.FrameRect;
                    sb.Append("\n  sheet — ");
                    sb.Append(DebugModel.SheetLabel(sheet));

                    if (frame.IsEmpty || frame.Width <= 0.0 || frame.Height <= 0.0)
                    {
                        sb.Append("\n    (degenerate frame rect — skipped)");
                    }
                    else
                    {
                        for (int i = 0; i < SheetLadder.Length; i++)
                        {
                            double askedPpmm = SheetLadder[i];
                            if (EditorUtility.DisplayCancelableProgressBar(
                                    "Resolution sweep",
                                    "sheet at " + askedPpmm.ToString("F2", ci) + " px/paper-mm",
                                    (float)i / SheetLadder.Length))
                            {
                                sb.Append("\n    (cancelled)");
                                break;
                            }

                            bool clamped;
                            double usedPpmm;
                            RenderRequest req = SheetRequest(sheet, askedPpmm, out usedPpmm, out clamped);

                            sb.Append("\n    sheet  ");
                            sb.Append(askedPpmm.ToString("F2", ci));
                            sb.Append(" px/mm (");
                            sb.Append(req.PixelsPerMetre.ToString("F3", ci));
                            sb.Append(" px/m)  ");
                            sb.Append(SweepRow(island, req, clamped, usedPpmm, ci,
                                               folder, SheetFileName(island, sheet, usedPpmm)));
                        }
                    }
                }
                else
                {
                    sb.Append("\n  sheet — none selected, so only the overview was swept.");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (folder != null)
            {
                sb.Append("\n  PNGs written to ");
                sb.Append(folder);
            }

            _report.text = sb.ToString();
            _reportScroll.style.display = DisplayStyle.Flex;
            UnityEngine.Debug.Log("[Archivist] " + sb);
        }

        /// <summary>One rung: render, time it, optionally write the PNG, and format the row.</summary>
        static string SweepRow(GenIsland island, RenderRequest req, bool clamped, double used,
                               CultureInfo ci, string folder, string fileName)
        {
            double millis;
            string error;
            ImageBuffer buf = RenderTimed(island, req, out millis, out error);
            if (buf == null)
            {
                return "render failed — " + error;
            }

            StringBuilder row = new StringBuilder(MetricsRow(buf, millis, "  ", ci));

            if (clamped)
            {
                row.Append("  [clamped to ");
                row.Append(used.ToString("F3", ci));
                row.Append(']');
            }

            if (folder != null)
            {
                try
                {
                    PngWriter.Write(buf, folder + "/" + fileName);
                    row.Append("  → ");
                    row.Append(fileName);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                    row.Append("  (png failed: ");
                    row.Append(e.Message);
                    row.Append(')');
                }
            }

            return row.ToString();
        }
    }
}
