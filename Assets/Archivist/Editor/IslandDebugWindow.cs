using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Archivist.Generation;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using GenIsland = Archivist.Generation.Island;

namespace Archivist.Editor
{
    /// <summary>A pane of the debug window (§11.0). Rebuild() recomputes view state, then repaints.</summary>
    public interface IDebugPane
    {
        VisualElement Root { get; }

        /// <summary>Recompute caches and text, then mark the canvases dirty. Never called per repaint.</summary>
        void Rebuild();
    }

    /// <summary>
    /// Contour results keyed by (lod, lattice-snapped area, level). §6.2 makes this sound: a
    /// contour grid is never free, so an area snapped to the lattice at a given LOD names one
    /// and only one set of samples, and the same key can never mean two different results.
    ///
    /// Contouring is a query (§3), not a build step — this cache is a *view* cache owned by the
    /// window, never by the generator, and it is dropped whole on regeneration. It exists because
    /// a pan must not re-run marching squares.
    /// </summary>
    public sealed class ContourCache
    {
        readonly struct Key : IEquatable<Key>
        {
            public readonly int Lod;
            public readonly long MinX, MinY, MaxX, MaxY, Level;

            public Key(int lod, long minX, long minY, long maxX, long maxY, long level)
            {
                Lod = lod; MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; Level = level;
            }

            public bool Equals(Key o)
            {
                return Lod == o.Lod && MinX == o.MinX && MinY == o.MinY
                    && MaxX == o.MaxX && MaxY == o.MaxY && Level == o.Level;
            }

            public override bool Equals(object o) { return o is Key k && Equals(k); }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Lod;
                    h = (h * 397) ^ MinX.GetHashCode();
                    h = (h * 397) ^ MinY.GetHashCode();
                    h = (h * 397) ^ MaxX.GetHashCode();
                    h = (h * 397) ^ MaxY.GetHashCode();
                    h = (h * 397) ^ Level.GetHashCode();
                    return h;
                }
            }
        }

        /// <summary>
        /// Field samples this window will spend on one set of levels over one area. §13.8 budgets
        /// 50 ms for a sheet at 1:5000, which at lod 6 (1 m cells) is ten million samples — a
        /// number no fbm field reaches. So the window contours as fine as the budget allows and
        /// no finer. Contour *density* (50 m of elevation, §6.1) is never touched; only the
        /// smoothness of the line is. A single-level extraction (the coastline, the shared class
        /// the whole comparison rests on) therefore gets the whole budget to itself.
        /// </summary>
        public const int SampleBudget = 500000;

        /// <summary>Entries before the cache is dropped whole. Pans reuse; zooms churn.</summary>
        public const int MaxEntries = 512;

        readonly Dictionary<Key, IReadOnlyList<Polyline>> _map = new Dictionary<Key, IReadOnlyList<Polyline>>();

        public int Extractions { get; private set; }
        public int Hits { get; private set; }
        public double LastExtractMillis { get; private set; }

        /// <summary>Highest LOD whose cell count over <paramref name="area"/> fits the budget.</summary>
        public static int ChooseLod(Rect2 area, int levelCount, int desiredLod)
        {
            int lod = desiredLod;
            if (lod < 0) lod = 0;
            if (lod > Tuning.MaxLod) lod = Tuning.MaxLod;

            double budget = SampleBudget / Math.Max(1, levelCount);
            double w = Math.Max(1.0, area.Width);
            double h = Math.Max(1.0, area.Height);

            while (lod > 0)
            {
                double cell = Contours.CellSizeForLod(lod);
                double cells = (w / cell + 2.0) * (h / cell + 2.0);
                if (cells <= budget)
                {
                    break;
                }

                lod--;
            }

            return lod;
        }

        public IReadOnlyList<Polyline> Get(IHeightField field, Rect2 snapped, int lod, double cell, double level01)
        {
            Key key = new Key(lod,
                              (long)Math.Round(snapped.MinX / cell),
                              (long)Math.Round(snapped.MinY / cell),
                              (long)Math.Round(snapped.MaxX / cell),
                              (long)Math.Round(snapped.MaxY / cell),
                              (long)Math.Round(level01 * 1.0e9));

            IReadOnlyList<Polyline> hit;
            if (_map.TryGetValue(key, out hit))
            {
                Hits++;
                return hit;
            }

            if (_map.Count >= MaxEntries)
            {
                _map.Clear();
            }

            Stopwatch sw = Stopwatch.StartNew();
            IReadOnlyList<Polyline> lines;
            try
            {
                lines = Contours.Extract(field, snapped, cell, level01);
            }
            catch (Exception e)
            {
                // A degenerate area must not take the window down (§11: show it, do not crash).
                UnityEngine.Debug.LogWarning("[Archivist] contour extraction failed: " + e.Message);
                lines = new List<Polyline>();
            }

            sw.Stop();
            LastExtractMillis = sw.Elapsed.TotalMilliseconds;
            Extractions++;
            _map[key] = lines;
            return lines;
        }

        public void Clear()
        {
            _map.Clear();
            Extractions = 0;
            Hits = 0;
        }
    }

    /// <summary>
    /// Height01 &lt;-&gt; metres, calibrated from the field itself rather than assumed.
    /// §6.1 asks for contours at every `SeaLevel + k * contourStep01` for a contour step of 50 m
    /// of elevation, and only <see cref="IHeightField.Elevation"/> knows how h01 maps to metres.
    /// Two probes are enough because the map is linear above sea level; the analytic form is kept
    /// as a fallback so a different mapping degrades to plausible contours instead of throwing.
    /// </summary>
    public sealed class HeightMapping
    {
        double _h0;
        double _e0;
        double _slope;
        bool _linear;
        double _seaLevel;
        double _maxElevationParam;

        readonly List<double> _levels = new List<double>();

        /// <summary>Highest land elevation actually observed while probing, in metres.</summary>
        public double MaxLandMetres { get; private set; }

        /// <summary>§6.1 contour levels, in Height01, one per 50 m of elevation above sea.</summary>
        public IReadOnlyList<double> ContourLevels01 { get { return _levels; } }

        /// <summary>The coastline level (§6.1: coastline = Extract at SeaLevel).</summary>
        public double SeaLevel01 { get { return _seaLevel; } }

        public static HeightMapping Calibrate(IHeightField field, Rect2 landBounds)
        {
            HeightMapping m = new HeightMapping();
            m._seaLevel = field.Params.SeaLevel;
            m._maxElevationParam = Math.Max(1.0, field.Params.MaxElevation);
            m._linear = false;
            m.MaxLandMetres = 0.0;

            Rect2 area = landBounds;
            if (area.IsEmpty || area.Width <= 0.0 || area.Height <= 0.0)
            {
                double half = field.Params.DomainMetres * 0.5;
                area = new Rect2(-half, -half, half, half);
            }

            const int n = 48;
            double hMin = double.MaxValue, hMax = double.MinValue;
            double eAtMin = 0.0, eAtMax = 0.0;
            double dx = area.Width / n;
            double dy = area.Height / n;

            for (int j = 0; j <= n; j++)
            {
                double y = area.MinY + j * dy;
                for (int i = 0; i <= n; i++)
                {
                    double x = area.MinX + i * dx;
                    double h = field.Height01(x, y);
                    if (h < m._seaLevel)
                    {
                        continue;
                    }

                    double e = field.Elevation(x, y);
                    if (h < hMin) { hMin = h; eAtMin = e; }
                    if (h > hMax) { hMax = h; eAtMax = e; }
                    if (e > m.MaxLandMetres) m.MaxLandMetres = e;
                }
            }

            if (hMax - hMin > 1.0e-9 && Math.Abs(eAtMax - eAtMin) > 1.0e-9)
            {
                m._h0 = hMin;
                m._e0 = eAtMin;
                m._slope = (eAtMax - eAtMin) / (hMax - hMin);
                m._linear = true;
            }

            m.BuildLevels();
            return m;
        }

        void BuildLevels()
        {
            _levels.Clear();
            if (MaxLandMetres <= Tuning.ContourStep)
            {
                return;
            }

            for (int k = 1; k <= 64; k++)
            {
                double metres = k * Tuning.ContourStep;
                if (metres >= MaxLandMetres)
                {
                    break;
                }

                double level = Level01ForMetres(metres);
                if (level <= _seaLevel || level >= 1.0)
                {
                    continue;
                }

                _levels.Add(level);
            }
        }

        /// <summary>Height01 for a given land elevation in metres.</summary>
        public double Level01ForMetres(double metres)
        {
            if (_linear)
            {
                return _h0 + (metres - _e0) / _slope;
            }

            return _seaLevel + (metres / _maxElevationParam) * (1.0 - _seaLevel);
        }
    }

    /// <summary>
    /// The §11 stats footer: the §13.5a, §13.6 and §13.7 numbers, computed once per island.
    /// All coverage figures are read off one land lattice, so the percentages are all commensurate.
    /// </summary>
    public sealed class IslandStats
    {
        /// <summary>Land lattice resolution per axis. 96 x 96 over the land bbox.</summary>
        public const int Lattice = 96;

        public int[] SheetsPerOffice = new int[3];
        public double[] RotationPerOffice = new double[3];
        public bool[] OfficePresent = new bool[3];
        public int WholeIslandSheets;
        public int TotalSheets;
        public string WholeIslandScale = "-";

        public int LandSamples;
        public int CoastalSamples;
        public int InteriorSamples;

        /// <summary>Coastal land covered by all three offices — the "coast x3" of §10.3.</summary>
        public double CoastAllThreePct;

        /// <summary>Interior land covered by at least one office.</summary>
        public double InteriorCoveredPct;

        /// <summary>Land covered by no office at all. R1.8 wants this above zero.</summary>
        public double GapPct;

        /// <summary>Land samples by office-coverage count, index 0..3 (3 = three or more).</summary>
        public int[] OverlapHistogram = new int[4];

        /// <summary>A5b (§13.5a): sheets whose only content is Coast and/or Grid.</summary>
        public int[] ThinSheets = new int[3];
        public double[] ThinSheetPct = new double[3];

        public string Note = "";

        public static IslandStats Compute(GenIsland island)
        {
            IslandStats s = new IslandStats();
            if (island == null)
            {
                s.Note = "no island";
                return s;
            }

            IHeightField field = island.Field;

            // --- per-survey counts, rotation, whole-island scale ---
            for (int i = 0; i < island.Surveys.Count; i++)
            {
                Survey survey = island.Surveys[i];
                if (survey == null)
                {
                    continue;
                }

                if (survey.Spec.IsWholeIsland)
                {
                    s.WholeIslandSheets += survey.SheetCount;
                    s.WholeIslandScale = survey.Spec.Scale.ToString();
                }
                else
                {
                    int o = (int)survey.Spec.Office;
                    if (o >= 0 && o < 3)
                    {
                        s.SheetsPerOffice[o] = survey.SheetCount;
                        s.RotationPerOffice[o] = survey.Spec.RotationDeg;
                        s.OfficePresent[o] = true;
                    }
                }

                s.TotalSheets += survey.SheetCount;
            }

            // --- coverage over a land lattice ---
            Rect2 land = island.LandBounds;
            if (!land.IsEmpty && land.Width > 0.0 && land.Height > 0.0)
            {
                s.ComputeCoverage(island, field, land);
            }
            else
            {
                s.Note = "land bbox empty";
            }

            // --- A5b thin sheets, per office ---
            for (int i = 0; i < island.Surveys.Count; i++)
            {
                Survey survey = island.Surveys[i];
                if (survey == null || survey.Spec.IsWholeIsland || survey.SheetCount == 0)
                {
                    continue;
                }

                int o = (int)survey.Spec.Office;
                if (o < 0 || o >= 3)
                {
                    continue;
                }

                int thin = 0;
                for (int k = 0; k < survey.Sheets.Count; k++)
                {
                    if (IsThinSheet(field, island.Features, survey.Sheets[k]))
                    {
                        thin++;
                    }
                }

                s.ThinSheets[o] = thin;
                s.ThinSheetPct[o] = 100.0 * thin / survey.SheetCount;
            }

            return s;
        }

        void ComputeCoverage(GenIsland island, IHeightField field, Rect2 land)
        {
            int n = Lattice;
            bool[] isLand = new bool[n * n];
            byte[] cover = new byte[n * n];
            double dx = land.Width / n;
            double dy = land.Height / n;

            // Only the three office surveys count toward coverage; the whole-island sheet
            // covers everything by construction (§10.5) and would flatten the picture.
            List<Survey> offices = new List<Survey>();
            for (int i = 0; i < island.Surveys.Count; i++)
            {
                Survey sv = island.Surveys[i];
                if (sv != null && !sv.Spec.IsWholeIsland && sv.SheetCount > 0)
                {
                    offices.Add(sv);
                }
            }

            for (int j = 0; j < n; j++)
            {
                double y = land.MinY + (j + 0.5) * dy;
                for (int i = 0; i < n; i++)
                {
                    double x = land.MinX + (i + 0.5) * dx;
                    int idx = j * n + i;
                    isLand[idx] = field.Height01(x, y) >= field.Params.SeaLevel;
                    if (!isLand[idx])
                    {
                        continue;
                    }

                    V2 p = new V2(x, y);
                    int c = 0;
                    for (int k = 0; k < offices.Count; k++)
                    {
                        if (DebugModel.SurveyCovers(offices[k], p))
                        {
                            c++;
                        }
                    }

                    cover[idx] = (byte)c;
                }
            }

            int landCount = 0, coastal = 0, interior = 0, coastAll3 = 0, interiorCovered = 0, gaps = 0;
            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    int idx = j * n + i;
                    if (!isLand[idx])
                    {
                        continue;
                    }

                    landCount++;
                    int c = cover[idx];
                    OverlapHistogram[c > 3 ? 3 : c]++;
                    if (c == 0)
                    {
                        gaps++;
                    }

                    bool edge = i == 0 || j == 0 || i == n - 1 || j == n - 1
                                || !isLand[idx - 1] || !isLand[idx + 1]
                                || !isLand[idx - n] || !isLand[idx + n];

                    if (edge)
                    {
                        coastal++;
                        if (c >= 3)
                        {
                            coastAll3++;
                        }
                    }
                    else
                    {
                        interior++;
                        if (c >= 1)
                        {
                            interiorCovered++;
                        }
                    }
                }
            }

            LandSamples = landCount;
            CoastalSamples = coastal;
            InteriorSamples = interior;
            CoastAllThreePct = coastal > 0 ? 100.0 * coastAll3 / coastal : 0.0;
            InteriorCoveredPct = interior > 0 ? 100.0 * interiorCovered / interior : 0.0;
            GapPct = landCount > 0 ? 100.0 * gaps / landCount : 0.0;
        }

        /// <summary>
        /// A5b (§13.5a): a sheet is thin when its only content is Coast and/or Grid. Both are
        /// unconditional — every coastal sheet carries a coastline, every Garrison sheet carries
        /// a grid — so they are exactly the classes that make A5 cost nothing.
        /// </summary>
        public static bool IsThinSheet(IHeightField field, IslandFeatures features, Sheet sheet)
        {
            Office office = sheet.Survey.Office;

            if (features != null)
            {
                if (FeatureMatrix.Draws(office, FeatureClass.Peak))
                {
                    for (int i = 0; i < features.Peaks.Count; i++)
                    {
                        if (DebugModel.SheetContains(sheet, features.Peaks[i].Position))
                        {
                            return false;
                        }
                    }
                }

                if (FeatureMatrix.Draws(office, FeatureClass.Settlement))
                {
                    for (int i = 0; i < features.Settlements.Count; i++)
                    {
                        if (DebugModel.SheetContains(sheet, features.Settlements[i].Position))
                        {
                            return false;
                        }
                    }
                }

                if (FeatureMatrix.Draws(office, FeatureClass.River))
                {
                    for (int i = 0; i < features.Rivers.Count; i++)
                    {
                        Polyline course = features.Rivers[i].Course;
                        if (course == null)
                        {
                            continue;
                        }

                        for (int k = 0; k < course.Count; k++)
                        {
                            if (DebugModel.SheetContains(sheet, course[k]))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            bool wantsContour = FeatureMatrix.Draws(office, FeatureClass.Contour);
            bool wantsSounding = FeatureMatrix.Draws(office, FeatureClass.Sounding);
            if (!wantsContour && !wantsSounding)
            {
                return true;
            }

            // §10.3 already samples every rect on a 16x16 frame-space lattice; do the same here.
            Rect2 frame = sheet.FrameRect;
            int n = Tuning.CullSampleGrid;
            double dx = frame.Width / n;
            double dy = frame.Height / n;
            double lo = double.MaxValue, hi = double.MinValue;

            for (int j = 0; j < n; j++)
            {
                double v = frame.MinY + (j + 0.5) * dy;
                for (int i = 0; i < n; i++)
                {
                    double u = frame.MinX + (i + 0.5) * dx;
                    V2 g = new V2(u, v).RotateDeg(sheet.RotationDeg);
                    double e = field.Elevation(g.X, g.Y);
                    if (e < lo) lo = e;
                    if (e > hi) hi = e;
                }
            }

            if (wantsSounding && lo < Tuning.SoundingDepth)
            {
                return false;
            }

            if (wantsContour && hi - lo >= Tuning.ContourStep)
            {
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Everything the three panes share: the island, the view caches, the layer toggles, and the
    /// current selection. The window owns it; panes only read it and raise events.
    /// </summary>
    public sealed class DebugModel
    {
        public ulong CollectionSeed = 8412;
        public int IslandIndex;
        public IslandCharacter? ForcedCharacter;

        public GenIsland Island { get; private set; }

        /// <summary>Set instead of throwing when generation fails — §11 must show it, not crash.</summary>
        public string Error { get; private set; }

        public double GenMillis { get; private set; }
        public double StatsMillis { get; private set; }
        public IslandStats Stats { get; private set; }
        public HeightMapping Mapping { get; private set; }

        public readonly ContourCache Cache = new ContourCache();

        readonly bool[] _layers = new bool[7];

        /// <summary>Per-survey outline visibility in the island pane, parallel to Island.Surveys.</summary>
        public bool[] SurveyVisible = new bool[0];

        public bool ShowSheetOutlines = true;

        /// <summary>Pane 2 / Pane 3 selection.</summary>
        public Sheet? SelectedSheet;

        /// <summary>Pane 3's point of interest, in ground metres.</summary>
        public V2? ComparePoint;

        /// <summary>Pane 3: rotate every cell to north so rotation stops being a variable (§11.0).</summary>
        public bool NorthUp;

        /// <summary>Pane 2: render the paper at real millimetres via Screen DPI (§11.0).</summary>
        public bool TrueSize;

        /// <summary>Pane 3: outline the shared intersection. Chrome, not ink.</summary>
        public bool ShowCropOutline = true;

        public DebugModel()
        {
            for (int i = 0; i < _layers.Length; i++)
            {
                _layers[i] = true;
            }

            // The grid is defined by a survey's scale (§6.4); at island scale it is chrome-dense
            // and says nothing, so it starts off in Pane 1. Sheets always draw it when Garrison.
            _layers[(int)FeatureClass.Grid] = false;
        }

        public bool HasIsland { get { return Island != null; } }

        public IHeightField Field { get { return Island == null ? null : (IHeightField)Island.Field; } }

        public bool Layer(FeatureClass cls)
        {
            int i = (int)cls;
            return i >= 0 && i < _layers.Length && _layers[i];
        }

        public void SetLayer(FeatureClass cls, bool on)
        {
            int i = (int)cls;
            if (i >= 0 && i < _layers.Length)
            {
                _layers[i] = on;
            }
        }

        /// <summary>Regenerate from the current seed / index / character. Never throws.</summary>
        public void Regenerate()
        {
            Error = null;
            Island = null;
            Stats = null;
            Mapping = null;
            SelectedSheet = null;
            ComparePoint = null;
            Cache.Clear();
            GenMillis = 0.0;
            StatsMillis = 0.0;

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                Island = GenIsland.Generate(CollectionSeed, IslandIndex, ForcedCharacter);
            }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                UnityEngine.Debug.LogException(e);
            }

            sw.Stop();
            GenMillis = sw.Elapsed.TotalMilliseconds;

            int surveyCount = Island != null && Island.Surveys != null ? Island.Surveys.Count : 0;
            SurveyVisible = new bool[surveyCount];
            for (int i = 0; i < surveyCount; i++)
            {
                // The whole-island footprint covers everything and would hide the detail surveys.
                SurveyVisible[i] = Island.Surveys[i] != null && !Island.Surveys[i].Spec.IsWholeIsland;
            }

            if (Island == null)
            {
                return;
            }

            Stopwatch sw2 = Stopwatch.StartNew();
            try
            {
                Mapping = HeightMapping.Calibrate(Island.Field, Island.LandBounds);
                Stats = IslandStats.Compute(Island);
            }
            catch (Exception e)
            {
                Error = "stats: " + e.Message;
                UnityEngine.Debug.LogException(e);
            }

            sw2.Stop();
            StatsMillis = sw2.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Contours over a ground area, cached. The area is snapped outward to the LOD lattice
        /// first (§6.2), so two callers asking for overlapping ground share the same samples and
        /// the same result — which is also why panning never re-runs marching squares.
        /// </summary>
        public List<Polyline> ContoursFor(Rect2 area, int desiredLod, IReadOnlyList<double> levels)
        {
            List<Polyline> result = new List<Polyline>();
            if (Island == null || levels == null || levels.Count == 0)
            {
                return result;
            }

            if (area.IsEmpty || area.Width <= 0.0 || area.Height <= 0.0)
            {
                return result;
            }

            int lod = ContourCache.ChooseLod(area, levels.Count, desiredLod);
            double cell = Contours.CellSizeForLod(lod);
            Rect2 snapped = area.SnapOut(cell);

            for (int i = 0; i < levels.Count; i++)
            {
                IReadOnlyList<Polyline> lines = Cache.Get(Island.Field, snapped, lod, cell, levels[i]);
                for (int k = 0; k < lines.Count; k++)
                {
                    result.Add(lines[k]);
                }
            }

            return result;
        }

        /// <summary>The coastline over an area — one level, so it gets the whole sample budget.</summary>
        public List<Polyline> CoastFor(Rect2 area, int desiredLod)
        {
            if (Island == null)
            {
                return new List<Polyline>();
            }

            return ContoursFor(area, desiredLod, new double[] { Island.Params.SeaLevel });
        }

        /// <summary>§6.1 contour levels for this island, or an empty list on an atoll with no relief.</summary>
        public IReadOnlyList<double> ContourLevels
        {
            get { return Mapping != null ? Mapping.ContourLevels01 : (IReadOnlyList<double>)new double[0]; }
        }

        /// <summary>Point-in-rotated-rect: transform into frame space, where the sheet is axis-aligned.</summary>
        public static bool SheetContains(Sheet sheet, V2 groundPoint)
        {
            V2 f = groundPoint.RotateDeg(-sheet.RotationDeg);
            return sheet.FrameRect.Contains(f);
        }

        public static bool SurveyCovers(Survey survey, V2 groundPoint)
        {
            if (survey == null)
            {
                return false;
            }

            for (int i = 0; i < survey.Sheets.Count; i++)
            {
                if (SheetContains(survey.Sheets[i], groundPoint))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Every sheet of every survey covering a ground point — Pane 3's input (§11.0).</summary>
        public List<Sheet> SheetsCovering(V2 groundPoint)
        {
            List<Sheet> hits = new List<Sheet>();
            if (Island == null || Island.Surveys == null)
            {
                return hits;
            }

            for (int i = 0; i < Island.Surveys.Count; i++)
            {
                Survey survey = Island.Surveys[i];
                if (survey == null)
                {
                    continue;
                }

                for (int k = 0; k < survey.Sheets.Count; k++)
                {
                    if (SheetContains(survey.Sheets[k], groundPoint))
                    {
                        hits.Add(survey.Sheets[k]);
                    }
                }
            }

            return hits;
        }

        /// <summary>Ground extent worth fitting the island pane to: land plus every sheet footprint.</summary>
        public Rect2 ViewExtent()
        {
            if (Island == null)
            {
                return new Rect2(-1000, -1000, 1000, 1000);
            }

            Rect2 r = Island.LandBounds;
            if (r.IsEmpty)
            {
                double half = Island.Params.DomainMetres * 0.5;
                r = new Rect2(-half, -half, half, half);
            }

            for (int i = 0; i < Island.Surveys.Count; i++)
            {
                Survey survey = Island.Surveys[i];
                if (survey == null || survey.Spec.IsWholeIsland)
                {
                    continue;
                }

                for (int k = 0; k < survey.Sheets.Count; k++)
                {
                    V2[] c = survey.Sheets[k].GroundCorners();
                    for (int m = 0; m < c.Length; m++)
                    {
                        r = r.Encapsulate(c[m]);
                    }
                }
            }

            double pad = Math.Max(200.0, r.Diagonal * 0.03);
            return r.Expanded(pad);
        }

        /// <summary>
        /// Short office tag for the Compare pane, where four headers share one row and the
        /// full names overflow. Full names stay everywhere there is room.
        /// </summary>
        public static string OfficeAbbr(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return "HYD";
                case Office.LandSurvey: return "LS";
                case Office.Garrison: return "GAR";
                default: return office.ToString();
            }
        }

        public static string OfficeName(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return "Hydrographic";
                case Office.LandSurvey: return "Land Survey";
                case Office.Garrison: return "Garrison";
                default: return office.ToString();
            }
        }

        /// <summary>
        /// Debug chrome only (§11.0). This is the ONLY colour anywhere in the window: §8.2 keeps
        /// the maps themselves to one line style, black on white, so that any difference the eye
        /// finds in Pane 3 is a difference of content.
        /// </summary>
        public static Color OfficeColour(SurveySpec spec)
        {
            if (spec.IsWholeIsland)
            {
                return new Color(0.45f, 0.45f, 0.45f);
            }

            switch (spec.Office)
            {
                case Office.Hydrographic: return new Color(0.10f, 0.45f, 0.85f);
                case Office.LandSurvey: return new Color(0.12f, 0.58f, 0.24f);
                case Office.Garrison: return new Color(0.85f, 0.45f, 0.10f);
                default: return Color.magenta;
            }
        }

        public static string SurveyLabel(Survey survey)
        {
            if (survey == null)
            {
                return "(missing survey)";
            }

            SurveySpec spec = survey.Spec;
            string who = spec.IsWholeIsland ? "whole-island" : OfficeName(spec.Office);
            return string.Format(CultureInfo.InvariantCulture,
                                 "{0}  {1}   {2} sheet{3}   {4}   rot {5:F1}°",
                                 who, spec.Year, survey.SheetCount, survey.SheetCount == 1 ? "" : "s",
                                 spec.Scale, spec.RotationDeg);
        }

        public static string SheetLabel(Sheet sheet)
        {
            SurveySpec spec = sheet.Survey;
            string who = spec.IsWholeIsland ? "whole-island" : OfficeName(spec.Office);
            return string.Format(CultureInfo.InvariantCulture,
                                 "{0} #{1}  {2}  rot {3:F1}°",
                                 who, sheet.Number, spec.Scale, sheet.RotationDeg);
        }
    }

    /// <summary>
    /// §11 debug window. `Window -> Archivist -> Island Debug`. UI Toolkit + Painter2D; no scene,
    /// no play mode, no camera, no build.
    ///
    /// Layout follows the §11.0 mock: toolbar on top, island pane left, surveys and layer toggles
    /// right, stats footer along the bottom. Panes 2 and 3 replace the island pane through the
    /// tab strip; the sidebar and footer stay put, because picking a sheet there is how Pane 2 and
    /// Pane 3 are driven.
    /// </summary>
    public sealed class IslandDebugWindow : EditorWindow
    {
        [SerializeField] long _collectionSeed = 8412;
        [SerializeField] int _islandIndex;
        [SerializeField] int _characterIndex;   // 0 = Auto, then Mountainous / Fjorded / Atoll
        [SerializeField] int _activeTab;

        static readonly string[] CharacterChoices = { "Auto", "Mountainous", "Fjorded", "Atoll" };
        static readonly string[] TabNames = { "Island", "Sheet", "Compare", "Texture" };

        DebugModel _model;
        IslandPane _islandPane;
        SheetPane _sheetPane;
        ComparePane _comparePane;
        TexturePane _texturePane;

        VisualElement _paneHost;
        VisualElement _sidebarSurveys;
        Label _footer;
        readonly List<Button> _tabButtons = new List<Button>();
        TextField _seedField;
        TextField _indexField;

        [MenuItem("Window/Archivist/Island Debug")]
        public static void Open()
        {
            IslandDebugWindow w = GetWindow<IslandDebugWindow>();
            w.titleContent = new GUIContent("Island Debug");
            w.minSize = new Vector2(900.0f, 560.0f);
            w.Show();
        }

        public void CreateGUI()
        {
            _model = new DebugModel();
            _model.CollectionSeed = unchecked((ulong)_collectionSeed);
            _model.IslandIndex = _islandIndex;
            _model.ForcedCharacter = CharacterFor(_characterIndex);

            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;

            root.Add(BuildToolbar());
            root.Add(BuildTabs());

            VisualElement body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1.0f;
            root.Add(body);

            _paneHost = new VisualElement();
            _paneHost.style.flexGrow = 1.0f;
            _paneHost.style.flexDirection = FlexDirection.Column;
            body.Add(_paneHost);

            body.Add(BuildSidebar());

            _islandPane = new IslandPane(_model);
            _sheetPane = new SheetPane(_model);
            _comparePane = new ComparePane(_model);
            _texturePane = new TexturePane(_model);

            _islandPane.SheetClicked += OnSheetClicked;
            _islandPane.PointPicked += OnPointPicked;

            _paneHost.Add(_islandPane.Root);
            _paneHost.Add(_sheetPane.Root);
            _paneHost.Add(_comparePane.Root);
            _paneHost.Add(_texturePane.Root);

            _footer = new Label();
            _footer.style.whiteSpace = WhiteSpace.Normal;
            _footer.style.paddingLeft = 6.0f;
            _footer.style.paddingRight = 6.0f;
            _footer.style.paddingTop = 4.0f;
            _footer.style.paddingBottom = 4.0f;
            _footer.style.borderTopWidth = 1.0f;
            _footer.style.borderTopColor = new Color(0.0f, 0.0f, 0.0f, 0.25f);
            _footer.style.fontSize = 11.0f;
            root.Add(_footer);

            Regenerate();
        }

        static IslandCharacter? CharacterFor(int index)
        {
            switch (index)
            {
                case 1: return IslandCharacter.Mountainous;
                case 2: return IslandCharacter.Fjorded;
                case 3: return IslandCharacter.Atoll;
                default: return null;
            }
        }

        VisualElement BuildToolbar()
        {
            VisualElement bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.paddingLeft = 4.0f;
            bar.style.paddingRight = 4.0f;
            bar.style.paddingTop = 3.0f;
            bar.style.paddingBottom = 3.0f;
            bar.style.borderBottomWidth = 1.0f;
            bar.style.borderBottomColor = new Color(0.0f, 0.0f, 0.0f, 0.25f);

            bar.Add(Tag("collection seed"));
            _seedField = new TextField();
            _seedField.value = _collectionSeed.ToString(CultureInfo.InvariantCulture);
            _seedField.style.width = 110.0f;
            _seedField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Regenerate();
                }
            });
            bar.Add(_seedField);

            bar.Add(Tag("island"));
            _indexField = new TextField();
            _indexField.value = _islandIndex.ToString(CultureInfo.InvariantCulture);
            _indexField.style.width = 50.0f;
            _indexField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    Regenerate();
                }
            });
            bar.Add(_indexField);

            Button regen = new Button(Regenerate);
            regen.text = "Regenerate";
            bar.Add(regen);

            bar.Add(Tag("character"));
            DropdownField character = new DropdownField();
            character.choices = new List<string>(CharacterChoices);
            character.index = Mathf.Clamp(_characterIndex, 0, CharacterChoices.Length - 1);
            character.style.width = 130.0f;
            character.RegisterValueChangedCallback(evt =>
            {
                _characterIndex = Mathf.Max(0, Array.IndexOf(CharacterChoices, evt.newValue));
                _model.ForcedCharacter = CharacterFor(_characterIndex);
                Regenerate();
            });
            bar.Add(character);

            Button randomise = new Button(() =>
            {
                _collectionSeed = (long)(DateTime.Now.Ticks & 0x7FFFFFFF);
                _seedField.value = _collectionSeed.ToString(CultureInfo.InvariantCulture);
                Regenerate();
            });
            randomise.text = "Random seed";
            bar.Add(randomise);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1.0f;
            bar.Add(spacer);

            Button export = new Button(ExportSvg);
            export.text = "Export SVG…";
            bar.Add(export);

            return bar;
        }

        static Label Tag(string text)
        {
            Label l = new Label(text);
            l.style.marginLeft = 8.0f;
            l.style.marginRight = 3.0f;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }

        VisualElement BuildTabs()
        {
            VisualElement tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.paddingLeft = 4.0f;
            tabs.style.paddingTop = 2.0f;
            tabs.style.paddingBottom = 2.0f;

            _tabButtons.Clear();
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                Button b = new Button(() => SetTab(index));
                b.text = TabNames[i];
                b.style.width = 110.0f;
                _tabButtons.Add(b);
                tabs.Add(b);
            }

            Label hint = new Label("Pane 1 click: pick a point and a sheet · wheel: zoom · drag: pan");
            hint.style.unityTextAlign = TextAnchor.MiddleLeft;
            hint.style.marginLeft = 12.0f;
            hint.style.opacity = 0.6f;
            hint.style.fontSize = 11.0f;
            tabs.Add(hint);

            return tabs;
        }

        VisualElement BuildSidebar()
        {
            VisualElement sidebar = new VisualElement();
            sidebar.style.width = 320.0f;
            sidebar.style.flexShrink = 0.0f;
            sidebar.style.borderLeftWidth = 1.0f;
            sidebar.style.borderLeftColor = new Color(0.0f, 0.0f, 0.0f, 0.25f);
            sidebar.style.paddingLeft = 6.0f;
            sidebar.style.paddingRight = 6.0f;
            sidebar.style.paddingTop = 4.0f;

            Label surveysTitle = new Label("surveys");
            surveysTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            sidebar.Add(surveysTitle);

            ScrollView scroll = new ScrollView();
            scroll.style.flexGrow = 1.0f;
            _sidebarSurveys = scroll.contentContainer;
            sidebar.Add(scroll);

            Label layersTitle = new Label("layers");
            layersTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            layersTitle.style.marginTop = 6.0f;
            sidebar.Add(layersTitle);

            sidebar.Add(LayerToggle("coast", FeatureClass.Coast));
            sidebar.Add(LayerToggle("contour", FeatureClass.Contour));
            sidebar.Add(LayerToggle("peak", FeatureClass.Peak));
            sidebar.Add(LayerToggle("river", FeatureClass.River));
            sidebar.Add(LayerToggle("town", FeatureClass.Settlement));
            sidebar.Add(LayerToggle("sounding", FeatureClass.Sounding));
            sidebar.Add(LayerToggle("grid", FeatureClass.Grid));

            Toggle outlines = new Toggle("sheet outlines");
            outlines.value = _model.ShowSheetOutlines;
            outlines.RegisterValueChangedCallback(evt =>
            {
                _model.ShowSheetOutlines = evt.newValue;
                RefreshPanes();
            });
            outlines.style.marginTop = 4.0f;
            sidebar.Add(outlines);

            return sidebar;
        }

        Toggle LayerToggle(string label, FeatureClass cls)
        {
            Toggle t = new Toggle(label);
            t.value = _model.Layer(cls);
            t.RegisterValueChangedCallback(evt =>
            {
                _model.SetLayer(cls, evt.newValue);
                RefreshPanes();
            });
            return t;
        }

        void SetTab(int index)
        {
            _activeTab = Mathf.Clamp(index, 0, TabNames.Length - 1);
            if (_islandPane == null)
            {
                return;
            }

            _islandPane.Root.style.display = _activeTab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _sheetPane.Root.style.display = _activeTab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _comparePane.Root.style.display = _activeTab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            _texturePane.Root.style.display = _activeTab == 3 ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                _tabButtons[i].style.unityFontStyleAndWeight = i == _activeTab ? FontStyle.Bold : FontStyle.Normal;
            }

            RefreshPanes();
        }

        void Regenerate()
        {
            if (_seedField != null)
            {
                long seed;
                if (long.TryParse(_seedField.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                {
                    _collectionSeed = seed;
                }
                else
                {
                    // Any text is a legal seed source; hash it rather than refuse.
                    _collectionSeed = StableTextSeed(_seedField.value);
                }
            }

            if (_indexField != null)
            {
                int idx;
                if (int.TryParse(_indexField.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                {
                    _islandIndex = Mathf.Max(0, idx);
                }
            }

            _model.CollectionSeed = unchecked((ulong)_collectionSeed);
            _model.IslandIndex = _islandIndex;
            _model.ForcedCharacter = CharacterFor(_characterIndex);
            _model.Regenerate();

            _islandPane.ResetView();
            BuildSurveyList();
            SetTab(_activeTab);
            RefreshPanes();
        }

        /// <summary>Editor-side only; the Generation assembly never hashes text (§13.2).</summary>
        static long StableTextSeed(string text)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                for (int i = 0; i < text.Length; i++)
                {
                    h ^= text[i];
                    h *= 1099511628211UL;
                }

                return (long)(h & 0x7FFFFFFFFFFFUL);
            }
        }

        void BuildSurveyList()
        {
            _sidebarSurveys.Clear();
            if (!_model.HasIsland)
            {
                _sidebarSurveys.Add(new Label(_model.Error != null ? "generation failed" : "no island"));
                return;
            }

            IReadOnlyList<Survey> surveys = _model.Island.Surveys;
            for (int i = 0; i < surveys.Count; i++)
            {
                int index = i;
                Survey survey = surveys[i];

                VisualElement block = new VisualElement();
                block.style.marginBottom = 4.0f;

                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                Toggle vis = new Toggle();
                vis.value = index < _model.SurveyVisible.Length && _model.SurveyVisible[index];
                vis.RegisterValueChangedCallback(evt =>
                {
                    if (index < _model.SurveyVisible.Length)
                    {
                        _model.SurveyVisible[index] = evt.newValue;
                    }

                    RefreshPanes();
                });
                row.Add(vis);

                VisualElement swatch = new VisualElement();
                swatch.style.width = 10.0f;
                swatch.style.height = 10.0f;
                swatch.style.marginRight = 4.0f;
                swatch.style.backgroundColor = survey != null
                    ? DebugModel.OfficeColour(survey.Spec)
                    : Color.magenta;
                row.Add(swatch);

                Label name = new Label(DebugModel.SurveyLabel(survey));
                name.style.fontSize = 11.0f;
                row.Add(name);
                block.Add(row);

                if (survey == null || survey.SheetCount == 0)
                {
                    // §5.3's stress case: an atoll can legitimately yield zero Land Survey sheets.
                    // An empty survey is a row, never an exception.
                    Label empty = new Label("    — no sheets cut —");
                    empty.style.fontSize = 11.0f;
                    empty.style.opacity = 0.6f;
                    block.Add(empty);
                }
                else
                {
                    VisualElement numbers = new VisualElement();
                    numbers.style.flexDirection = FlexDirection.Row;
                    numbers.style.flexWrap = Wrap.Wrap;
                    numbers.style.marginLeft = 18.0f;

                    for (int k = 0; k < survey.Sheets.Count; k++)
                    {
                        Sheet sheet = survey.Sheets[k];
                        Button b = new Button(() => SelectSheet(sheet));
                        b.text = sheet.Number.ToString(CultureInfo.InvariantCulture);
                        b.style.width = 26.0f;
                        b.style.height = 18.0f;
                        b.style.fontSize = 10.0f;
                        b.style.marginLeft = 0.0f;
                        b.style.marginRight = 1.0f;
                        b.style.marginTop = 1.0f;
                        b.style.marginBottom = 1.0f;
                        b.style.paddingLeft = 0.0f;
                        b.style.paddingRight = 0.0f;
                        numbers.Add(b);
                    }

                    block.Add(numbers);
                }

                _sidebarSurveys.Add(block);
            }
        }

        void SelectSheet(Sheet sheet)
        {
            _model.SelectedSheet = sheet;
            _model.ComparePoint = sheet.CentreGround;
            _comparePane.OnSelectionChanged();
            RefreshPanes();
        }

        void OnSheetClicked(Sheet sheet)
        {
            _model.SelectedSheet = sheet;
            _comparePane.OnSelectionChanged();
            SetTab(1);
        }

        void OnPointPicked(V2 point)
        {
            _model.ComparePoint = point;
            _comparePane.OnSelectionChanged();
            RefreshPanes();
        }

        void RefreshPanes()
        {
            if (_islandPane == null)
            {
                return;
            }

            if (_activeTab == 0) _islandPane.Rebuild();
            else if (_activeTab == 1) _sheetPane.Rebuild();
            else if (_activeTab == 2) _comparePane.Rebuild();
            else _texturePane.Rebuild();

            UpdateFooter();
        }

        void ExportSvg()
        {
            if (!_model.HasIsland)
            {
                EditorUtility.DisplayDialog("Island Debug", "Nothing to export — generation failed.", "OK");
                return;
            }

            string folder = EditorUtility.SaveFolderPanel("Export island SVG + manifest", "", "");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            string summary = SvgExport.Export(_model, folder);
            UnityEngine.Debug.Log("[Archivist] " + summary);
            EditorUtility.DisplayDialog("Island Debug", summary, "OK");
        }

        /// <summary>§11.0 stats footer — the §13.5a, §13.6 and §13.7 numbers.</summary>
        void UpdateFooter()
        {
            if (_footer == null)
            {
                return;
            }

            if (!_model.HasIsland)
            {
                _footer.text = _model.Error != null
                    ? "generation failed — " + _model.Error
                    : "no island";
                return;
            }

            IslandStats s = _model.Stats;
            StringBuilder sb = new StringBuilder();
            CultureInfo ci = CultureInfo.InvariantCulture;

            sb.Append(_model.Island.Name);
            sb.Append(" · ");
            sb.Append(_model.Island.Params.Character.ToString().ToLowerInvariant());
            sb.Append(" · seed ");
            sb.Append(_model.Island.Seed.ToString(ci));

            if (s == null)
            {
                sb.Append(" · stats unavailable");
                _footer.text = sb.ToString();
                return;
            }

            sb.Append(" · sheets ");
            sb.Append(FormatOffice(s, Office.Hydrographic, "hyd"));
            sb.Append(' ');
            sb.Append(FormatOffice(s, Office.LandSurvey, "land"));
            sb.Append(' ');
            sb.Append(FormatOffice(s, Office.Garrison, "garr"));
            sb.Append(" whole ");
            sb.Append(s.WholeIslandSheets.ToString(ci));
            sb.Append(" — total ");
            sb.Append(s.TotalSheets.ToString(ci));

            sb.Append("\ncoast ×3 ");
            sb.Append(s.CoastAllThreePct.ToString("F0", ci));
            sb.Append("% · interior ≥1 ");
            sb.Append(s.InteriorCoveredPct.ToString("F0", ci));
            sb.Append("% · gaps ");
            sb.Append(s.GapPct.ToString("F0", ci));
            sb.Append("% of land");

            int total = s.LandSamples > 0 ? s.LandSamples : 1;
            sb.Append(" · overlap [0] ");
            sb.Append((100.0 * s.OverlapHistogram[0] / total).ToString("F0", ci));
            sb.Append("%  [1] ");
            sb.Append((100.0 * s.OverlapHistogram[1] / total).ToString("F0", ci));
            sb.Append("%  [2] ");
            sb.Append((100.0 * s.OverlapHistogram[2] / total).ToString("F0", ci));
            sb.Append("%  [3+] ");
            sb.Append((100.0 * s.OverlapHistogram[3] / total).ToString("F0", ci));
            sb.Append('%');

            sb.Append("\nthin sheets (A5b) hyd ");
            sb.Append(s.ThinSheetPct[(int)Office.Hydrographic].ToString("F0", ci));
            sb.Append("% · land ");
            sb.Append(s.ThinSheetPct[(int)Office.LandSurvey].ToString("F0", ci));
            sb.Append("% · garr ");
            sb.Append(s.ThinSheetPct[(int)Office.Garrison].ToString("F0", ci));
            sb.Append("% · whole-island scale ");
            sb.Append(s.WholeIslandScale);
            sb.Append(" · gen ");
            sb.Append(_model.GenMillis.ToString("F0", ci));
            sb.Append(" ms · stats ");
            sb.Append(_model.StatsMillis.ToString("F0", ci));
            sb.Append(" ms · contour extracts ");
            sb.Append(_model.Cache.Extractions.ToString(ci));
            sb.Append(" (cache hits ");
            sb.Append(_model.Cache.Hits.ToString(ci));
            sb.Append(')');

            if (!string.IsNullOrEmpty(s.Note))
            {
                sb.Append(" · ");
                sb.Append(s.Note);
            }

            if (_model.Error != null)
            {
                sb.Append(" · ERROR ");
                sb.Append(_model.Error);
            }

            _footer.text = sb.ToString();
        }

        static string FormatOffice(IslandStats s, Office office, string tag)
        {
            int i = (int)office;
            return tag + " " + s.SheetsPerOffice[i].ToString(CultureInfo.InvariantCulture);
        }
    }
}
