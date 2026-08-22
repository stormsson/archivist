using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Archivist.Generation;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;
using UnityEditor;
using UnityEngine;

namespace Archivist.Editor
{
    /// <summary>
    /// §11 "Export (optional, cheap)": writes `island.svg`, one SVG per sheet, and `manifest.json`
    /// to a folder — diffable, shareable, pasteable.
    ///
    /// The sheet SVGs are the same documents Pane 2 draws, under the same two rules: only the
    /// classes the office draws (§8.3), and one uniform black line style on white (§8.2). Sheets
    /// are written at real paper size (mm units in the SVG header), so a browser or Illustrator
    /// prints them at 1:1 without further arithmetic.
    /// </summary>
    public static class SvgExport
    {
        /// <summary>Uniform map line weight on paper, in millimetres (§8.2).</summary>
        const double SheetStrokeMm = 0.25;

        /// <summary>Nominal pixel width of island.svg; the viewBox stays in ground metres.</summary>
        const double IslandPixelWidth = 1600.0;

        /// <summary>
        /// Contour detail for island.svg. Fixed rather than derived: the whole-island view is a
        /// single fixed-size document, so it wants one fixed level of detail (§6.2 lod).
        /// </summary>
        const int IslandContourLod = 2;

        /// <summary>SVG spelling of <see cref="VectorDraw.Ink"/>: everything drawn is this (§8.2).</summary>
        const string InkHex = "#000000";

        /// <summary>SVG spelling of <see cref="VectorDraw.Paper"/>: the sheet under the ink (§8.2).</summary>
        const string PaperHex = "#ffffff";

        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <summary>Writes every file. Returns a one-line summary for the console and the dialog.</summary>
        public static string Export(DebugModel model, string folder)
        {
            if (model == null || !model.HasIsland)
            {
                return "nothing to export";
            }

            if (string.IsNullOrEmpty(folder))
            {
                return "no folder chosen";
            }

            int sheetFiles = 0;
            try
            {
                Directory.CreateDirectory(folder);

                File.WriteAllText(Path.Combine(folder, "island.svg"), BuildIslandSvg(model), Encoding.UTF8);

                IReadOnlyList<Survey> surveys = model.Island.Surveys;
                int totalSheets = model.Island.TotalSheets;
                int done = 0;

                for (int i = 0; i < surveys.Count; i++)
                {
                    Survey survey = surveys[i];
                    if (survey == null)
                    {
                        continue;
                    }

                    for (int k = 0; k < survey.Sheets.Count; k++)
                    {
                        Sheet sheet = survey.Sheets[k];
                        string name = SheetFileName(sheet);
                        EditorUtility.DisplayProgressBar("Exporting SVG", name,
                                                         totalSheets > 0 ? (float)done / totalSheets : 1.0f);
                        File.WriteAllText(Path.Combine(folder, name), BuildSheetSvg(model, sheet), Encoding.UTF8);
                        sheetFiles++;
                        done++;
                    }
                }

                File.WriteAllText(Path.Combine(folder, "manifest.json"), BuildManifest(model), Encoding.UTF8);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                return "export failed: " + e.Message;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return string.Format(Ci, "exported island.svg + {0} sheet SVG{1} + manifest.json to {2}",
                                 sheetFiles, sheetFiles == 1 ? "" : "s", folder);
        }

        public static string SheetFileName(Sheet sheet)
        {
            SurveySpec spec = sheet.Survey;
            string who = spec.IsWholeIsland ? "whole-island" : Sanitise(DebugModel.OfficeName(spec.Office));

            // POC-03 §2.4: the detail run is numbered independently, so its files are named
            // "detail-...-D001" and cannot collide with a survey sheet of the same number.
            return sheet.IsDetail
                ? string.Format(Ci, "detail-{0}-D{1:D3}.svg", who, sheet.Number)
                : string.Format(Ci, "sheet-{0}-{1:D3}.svg", who, sheet.Number);
        }

        static string Sanitise(string s)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (c == ' ' || c == '-' || c == '_')
                {
                    sb.Append('-');
                }
            }

            return sb.Length == 0 ? "x" : sb.ToString();
        }

        static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "0";
            }

            return v.ToString("0.###", Ci);
        }

        static string Xml(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                    .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        static string Json(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\')
                {
                    sb.Append('\\').Append(c);
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else if (c < ' ')
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4", Ci));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ island.svg

        static string BuildIslandSvg(DebugModel model)
        {
            // The fifth and last copy of the empty-land-bounds fallback, now pointed at the one
            // implementation. This copy tested IsEmpty and Width but not Height, so a bbox
            // degenerate on the y axis alone slipped through and produced a zero-height SVG;
            // DebugModel.SafeExtent tests all three.
            Rect2 extent = DebugModel.SafeExtent(model.Island.LandBounds,
                                                 model.Island.Params.DomainMetres);

            extent = extent.Expanded(Math.Max(DebugModel.ViewPadMinMetres,
                                             extent.Diagonal * DebugModel.ViewPadFraction));

            double w = extent.Width;
            double h = extent.Height;
            double stroke = w / IslandPixelWidth;
            Func<V2, V2> proj = p => new V2(p.X - extent.MinX, extent.MaxY - p.Y);

            StringBuilder sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"");
            sb.Append(Num(IslandPixelWidth));
            sb.Append("\" height=\"");
            sb.Append(Num(IslandPixelWidth * h / Math.Max(1.0, w)));
            sb.Append("\" viewBox=\"0 0 ");
            sb.Append(Num(w));
            sb.Append(' ');
            sb.Append(Num(h));
            sb.Append("\">\n");
            sb.Append("<title>").Append(Xml(model.Island.Name)).Append("</title>\n");
            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(Num(w)).Append("\" height=\"")
              .Append(Num(h)).Append("\" fill=\"" + PaperHex + "\"/>\n");

            // §8.2 neutral rendering: one weight, black on white, for every class.
            string open = "<g fill=\"none\" stroke=\"" + InkHex + "\" stroke-width=\"" + Num(stroke)
                        + "\" stroke-linejoin=\"round\">\n";

            int lod = IslandContourLod;
            sb.Append("<g id=\"contours\">\n").Append(open);
            List<Polyline> contours = model.ContoursFor(extent, lod, model.ContourLevels);
            for (int i = 0; i < contours.Count; i++)
            {
                AppendPath(sb, contours[i].Points, contours[i].Closed, proj);
            }

            sb.Append("</g></g>\n");

            sb.Append("<g id=\"coast\">\n").Append(open);
            IReadOnlyList<Polyline> coast = model.Island.Coastline;
            for (int i = 0; i < coast.Count; i++)
            {
                AppendPath(sb, coast[i].Points, coast[i].Closed, proj);
            }

            sb.Append("</g></g>\n");

            IslandFeatures f = model.Island.Features;

            sb.Append("<g id=\"rivers\">\n").Append(open);
            for (int i = 0; i < f.Rivers.Count; i++)
            {
                Polyline course = f.Rivers[i].Course;
                if (course != null)
                {
                    AppendPath(sb, course.Points, course.Closed, proj);
                }
            }

            sb.Append("</g></g>\n");

            double markR = stroke * MarkSizes.SvgStrokeMultiple;
            double fontSize = stroke * FeatureLabels.SvgStrokeMultiple;

            sb.Append("<g id=\"peaks\">\n");
            for (int i = 0; i < f.Peaks.Count; i++)
            {
                Peak pk = f.Peaks[i];
                V2 q = proj(pk.Position);
                sb.Append("<circle cx=\"").Append(Num(q.X)).Append("\" cy=\"").Append(Num(q.Y))
                  .Append("\" r=\"").Append(Num(markR)).Append("\" fill=\"" + InkHex + "\"/>\n");
                AppendText(sb, q.X + markR * 1.6, q.Y - markR, fontSize, FeatureLabels.PeakText(pk));
            }

            sb.Append("</g>\n");

            sb.Append("<g id=\"settlements\">\n");
            for (int i = 0; i < f.Settlements.Count; i++)
            {
                Settlement st = f.Settlements[i];
                V2 q = proj(st.Position);
                sb.Append("<circle cx=\"").Append(Num(q.X)).Append("\" cy=\"").Append(Num(q.Y))
                  .Append("\" r=\"").Append(Num(markR))
                  .Append("\" fill=\"none\" stroke=\"" + InkHex + "\" stroke-width=\"")
                  .Append(Num(stroke)).Append("\"/>\n");
                AppendText(sb, q.X + markR * 1.6, q.Y - markR, fontSize, st.Name);
            }

            sb.Append("</g>\n");

            // Debug chrome, in its own group so it can be deleted from the file (§11.0).
            sb.Append("<g id=\"sheet-outlines\">\n");
            IReadOnlyList<Survey> surveys = model.Island.Surveys;
            for (int i = 0; i < surveys.Count; i++)
            {
                Survey survey = surveys[i];
                if (survey == null || survey.SheetCount == 0)
                {
                    continue;
                }

                Color c = DebugModel.OfficeColour(survey.Spec);
                sb.Append("<g fill=\"none\" stroke=\"").Append(Hex(c)).Append("\" stroke-width=\"")
                  .Append(Num(stroke)).Append("\">\n");
                for (int k = 0; k < survey.Sheets.Count; k++)
                {
                    V2[] corners = survey.Sheets[k].GroundCorners();
                    AppendPath(sb, corners, true, proj);
                }

                sb.Append("</g>\n");
            }

            sb.Append("</g>\n");
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        static string Hex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255.0f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255.0f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255.0f), 0, 255);
            return "#" + r.ToString("x2", Ci) + g.ToString("x2", Ci) + b.ToString("x2", Ci);
        }

        // ------------------------------------------------------------------ sheet SVG

        static string BuildSheetSvg(DebugModel model, Sheet sheet)
        {
            SurveySpec spec = sheet.Survey;
            Office office = spec.Office;
            SheetFormat fmt = spec.Format;

            double mmPerMetre = Tuning.MmPerMetre / Math.Max(1, spec.Scale.Denominator);
            double cx = fmt.MarginMm + fmt.MapWidthMm * 0.5;
            double cy = fmt.MarginMm + fmt.MapHeightMm * 0.5;
            double rot = sheet.RotationDeg;
            V2 centre = sheet.CentreGround;

            Func<V2, V2> proj = p =>
            {
                V2 local = (p - centre).RotateDeg(-rot);
                return new V2(cx + local.X * mmPerMetre, cy - local.Y * mmPerMetre);
            };

            StringBuilder sb = new StringBuilder();
            sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"");
            sb.Append(Num(fmt.WidthMm)).Append("mm\" height=\"").Append(Num(fmt.HeightMm));
            sb.Append("mm\" viewBox=\"0 0 ").Append(Num(fmt.WidthMm)).Append(' ')
              .Append(Num(fmt.HeightMm)).Append("\">\n");

            sb.Append("<title>").Append(Xml(model.Island.Name)).Append(" — ")
              .Append(Xml(spec.IsWholeIsland ? "whole-island" : DebugModel.OfficeName(office)))
              .Append(" sheet ").Append(sheet.Number.ToString(Ci)).Append("</title>\n");

            sb.Append("<defs><clipPath id=\"map\"><rect x=\"").Append(Num(fmt.MarginMm))
              .Append("\" y=\"").Append(Num(fmt.MarginMm))
              .Append("\" width=\"").Append(Num(fmt.MapWidthMm))
              .Append("\" height=\"").Append(Num(fmt.MapHeightMm))
              .Append("\"/></clipPath></defs>\n");

            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(Num(fmt.WidthMm))
              .Append("\" height=\"").Append(Num(fmt.HeightMm)).Append("\" fill=\"" + PaperHex + "\"/>\n");

            string stroke = Num(SheetStrokeMm);
            sb.Append("<g fill=\"none\" stroke=\"" + InkHex + "\" stroke-width=\"").Append(stroke).Append("\">\n");
            sb.Append("<rect x=\"0\" y=\"0\" width=\"").Append(Num(fmt.WidthMm))
              .Append("\" height=\"").Append(Num(fmt.HeightMm)).Append("\"/>\n");
            sb.Append("<rect x=\"").Append(Num(fmt.MarginMm)).Append("\" y=\"").Append(Num(fmt.MarginMm))
              .Append("\" width=\"").Append(Num(fmt.MapWidthMm)).Append("\" height=\"")
              .Append(Num(fmt.MapHeightMm)).Append("\"/>\n");
            sb.Append("</g>\n");

            sb.Append("<g clip-path=\"url(#map)\">\n");
            sb.Append("<g fill=\"none\" stroke=\"" + InkHex + "\" stroke-width=\"").Append(stroke)
              .Append("\" stroke-linejoin=\"round\">\n");

            Rect2 ground = sheet.GroundBounds;

            // §8.3 — draw or omit. Same single decision point as Pane 2, and the same gather.
            // What differs here is the ORDER: this backend emits soundings last, after the marks,
            // where the panes put them first. That is a real per-backend difference, so the gather
            // is shared and the emission below is not.
            SheetGeometry geo = SheetContent.Gather(model, ground,
                                                    Contours.LodForScale(spec.Scale.Denominator),
                                                    spec.Scale,
                                                    cls => FeatureMatrix.Draws(office, cls));

            for (int i = 0; i < geo.Grid.Count; i++)
            {
                AppendPath(sb, geo.Grid[i].Points, geo.Grid[i].Closed, proj);
            }

            for (int i = 0; i < geo.Contours.Count; i++)
            {
                AppendPath(sb, geo.Contours[i].Points, geo.Contours[i].Closed, proj);
            }

            for (int i = 0; i < geo.Coast.Count; i++)
            {
                AppendPath(sb, geo.Coast[i].Points, geo.Coast[i].Closed, proj);
            }

            IslandFeatures f = model.Island.Features;

            if (FeatureMatrix.Draws(office, FeatureClass.River))
            {
                for (int i = 0; i < f.Rivers.Count; i++)
                {
                    Polyline course = f.Rivers[i].Course;
                    if (course != null)
                    {
                        AppendPath(sb, course.Points, course.Closed, proj);
                    }
                }
            }

            sb.Append("</g>\n");

            double markR = SheetStrokeMm * MarkSizes.SvgStrokeMultiple;
            double fontSize = SheetStrokeMm * FeatureLabels.SvgStrokeMultiple;

            if (FeatureMatrix.Draws(office, FeatureClass.Peak))
            {
                for (int i = 0; i < f.Peaks.Count; i++)
                {
                    Peak pk = f.Peaks[i];
                    if (!DebugModel.SheetContains(sheet, pk.Position))
                    {
                        continue;
                    }

                    V2 q = proj(pk.Position);
                    sb.Append("<circle cx=\"").Append(Num(q.X)).Append("\" cy=\"").Append(Num(q.Y))
                      .Append("\" r=\"").Append(Num(markR)).Append("\" fill=\"" + InkHex + "\"/>\n");
                    AppendText(sb, q.X + markR * 1.6, q.Y - markR, fontSize, FeatureLabels.PeakText(pk));
                }
            }

            if (FeatureMatrix.Draws(office, FeatureClass.Settlement))
            {
                for (int i = 0; i < f.Settlements.Count; i++)
                {
                    Settlement st = f.Settlements[i];
                    if (!DebugModel.SheetContains(sheet, st.Position))
                    {
                        continue;
                    }

                    V2 q = proj(st.Position);
                    sb.Append("<circle cx=\"").Append(Num(q.X)).Append("\" cy=\"").Append(Num(q.Y))
                      .Append("\" r=\"").Append(Num(markR))
                      .Append("\" fill=\"none\" stroke=\"" + InkHex + "\" stroke-width=\"")
                      .Append(Num(SheetStrokeMm)).Append("\"/>\n");
                    AppendText(sb, q.X + markR * 1.6, q.Y - markR, fontSize, st.Name);
                }
            }

            // POC-03 — POIs, drawn only by the Antiquarian office. A diamond, matching the
            // debug panes, so an exported detail sheet reads the same as the screen.
            if (FeatureMatrix.Draws(office, FeatureClass.Poi))
            {
                for (int i = 0; i < f.Pois.Count; i++)
                {
                    Poi poi = f.Pois[i];
                    if (!DebugModel.SheetContains(sheet, poi.Position))
                    {
                        continue;
                    }

                    V2 q = proj(poi.Position);
                    sb.Append("<polygon points=\"")
                      .Append(Num(q.X)).Append(',').Append(Num(q.Y - markR)).Append(' ')
                      .Append(Num(q.X + markR)).Append(',').Append(Num(q.Y)).Append(' ')
                      .Append(Num(q.X)).Append(',').Append(Num(q.Y + markR)).Append(' ')
                      .Append(Num(q.X - markR)).Append(',').Append(Num(q.Y))
                      .Append("\" fill=\"none\" stroke=\"" + InkHex + "\" stroke-width=\"")
                      .Append(Num(SheetStrokeMm)).Append("\"/>\n");
                    AppendText(sb, q.X + markR * 1.6, q.Y - markR, fontSize, poi.Kind.Label());
                }
            }

            // Soundings last — see the note on the gather above.
            for (int i = 0; i < geo.Soundings.Count; i++)
            {
                V2 q = proj(geo.Soundings[i].Position);
                AppendText(sb, q.X, q.Y, fontSize, FeatureLabels.DepthText(geo.Soundings[i]));
            }

            sb.Append("</g>\n");
            sb.Append("</svg>\n");
            return sb.ToString();
        }

        static void AppendText(StringBuilder sb, double x, double y, double size, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            sb.Append("<text x=\"").Append(Num(x)).Append("\" y=\"").Append(Num(y))
              .Append("\" font-size=\"").Append(Num(size)).Append("\" fill=\"" + InkHex + "\">")
              .Append(Xml(text)).Append("</text>\n");
        }

        static void AppendPath(StringBuilder sb, IReadOnlyList<V2> pts, bool closed, Func<V2, V2> proj)
        {
            if (pts == null || pts.Count < 2)
            {
                return;
            }

            sb.Append("<path d=\"");
            for (int i = 0; i < pts.Count; i++)
            {
                V2 q = proj(pts[i]);
                sb.Append(i == 0 ? 'M' : 'L');
                sb.Append(Num(q.X));
                sb.Append(' ');
                sb.Append(Num(q.Y));
                if (i < pts.Count - 1)
                {
                    sb.Append(' ');
                }
            }

            if (closed)
            {
                sb.Append(" Z");
            }

            sb.Append("\"/>\n");
        }

        // ------------------------------------------------------------------ manifest.json

        static string BuildManifest(DebugModel model)
        {
            StringBuilder sb = new StringBuilder();
            IslandStats stats = model.Stats;

            sb.Append("{\n");
            sb.Append("  \"collectionSeed\": ").Append(model.CollectionSeed.ToString(Ci)).Append(",\n");
            sb.Append("  \"islandIndex\": ").Append(model.IslandIndex.ToString(Ci)).Append(",\n");
            sb.Append("  \"islandSeed\": ").Append(model.Island.Seed.ToString(Ci)).Append(",\n");
            sb.Append("  \"name\": \"").Append(Json(model.Island.Name)).Append("\",\n");
            sb.Append("  \"character\": \"").Append(Json(model.Island.Params.Character.ToString())).Append("\",\n");
            sb.Append("  \"forcedCharacter\": ")
              .Append(model.ForcedCharacter.HasValue
                          ? "\"" + Json(model.ForcedCharacter.Value.ToString()) + "\""
                          : "null").Append(",\n");
            sb.Append("  \"generationMs\": ").Append(Num(model.GenMillis)).Append(",\n");
            sb.Append("  \"maxElevationM\": ").Append(Num(model.Island.Params.MaxElevation)).Append(",\n");
            sb.Append("  \"nominalRadiusM\": ").Append(Num(model.Island.Params.NominalRadius)).Append(",\n");

            Rect2 lb = model.Island.LandBounds;
            sb.Append("  \"landBounds\": { \"minX\": ").Append(Num(lb.MinX))
              .Append(", \"minY\": ").Append(Num(lb.MinY))
              .Append(", \"maxX\": ").Append(Num(lb.MaxX))
              .Append(", \"maxY\": ").Append(Num(lb.MaxY))
              .Append(", \"width\": ").Append(Num(lb.Width))
              .Append(", \"height\": ").Append(Num(lb.Height)).Append(" },\n");

            sb.Append("  \"stats\": ");
            if (stats == null)
            {
                sb.Append("null,\n");
            }
            else
            {
                sb.Append("{\n");
                sb.Append("    \"totalSheets\": ").Append(stats.TotalSheets.ToString(Ci)).Append(",\n");
                sb.Append("    \"sheetsPerOffice\": { \"hydrographic\": ")
                  .Append(stats.SheetsPerOffice[(int)Office.Hydrographic].ToString(Ci))
                  .Append(", \"landSurvey\": ")
                  .Append(stats.SheetsPerOffice[(int)Office.LandSurvey].ToString(Ci))
                  .Append(", \"garrison\": ")
                  .Append(stats.SheetsPerOffice[(int)Office.Garrison].ToString(Ci))
                  .Append(", \"antiquarian\": ")
                  .Append(stats.SheetsPerOffice[(int)Office.Antiquarian].ToString(Ci))
                  .Append(", \"wholeIsland\": ")
                  .Append(stats.WholeIslandSheets.ToString(Ci)).Append(" },\n");
                sb.Append("    \"wholeIslandScale\": \"").Append(Json(stats.WholeIslandScale)).Append("\",\n");
                sb.Append("    \"coastCoveredByAllThreePct\": ").Append(Num(stats.CoastAllThreePct)).Append(",\n");
                sb.Append("    \"interiorCoveredPct\": ").Append(Num(stats.InteriorCoveredPct)).Append(",\n");
                sb.Append("    \"gapPctOfLand\": ").Append(Num(stats.GapPct)).Append(",\n");
                sb.Append("    \"landSamples\": ").Append(stats.LandSamples.ToString(Ci)).Append(",\n");
                sb.Append("    \"overlapHistogram\": [")
                  .Append(stats.OverlapHistogram[0].ToString(Ci)).Append(", ")
                  .Append(stats.OverlapHistogram[1].ToString(Ci)).Append(", ")
                  .Append(stats.OverlapHistogram[2].ToString(Ci)).Append(", ")
                  .Append(stats.OverlapHistogram[3].ToString(Ci)).Append("],\n");
                sb.Append("    \"thinSheetPct\": { \"hydrographic\": ")
                  .Append(Num(stats.ThinSheetPct[(int)Office.Hydrographic]))
                  .Append(", \"landSurvey\": ").Append(Num(stats.ThinSheetPct[(int)Office.LandSurvey]))
                  .Append(", \"garrison\": ").Append(Num(stats.ThinSheetPct[(int)Office.Garrison]))
                  .Append(", \"antiquarian\": ").Append(Num(stats.ThinSheetPct[(int)Office.Antiquarian]))
                  .Append(" },\n");
                sb.Append("    \"poiCount\": ")
                  .Append(model.Island.Features.Pois.Count.ToString(Ci)).Append("\n");
                sb.Append("  },\n");
            }

            sb.Append("  \"surveys\": [\n");
            List<Survey> surveys = new List<Survey>();
            for (int i = 0; i < model.Island.Surveys.Count; i++)
            {
                if (model.Island.Surveys[i] != null)
                {
                    surveys.Add(model.Island.Surveys[i]);
                }
            }

            for (int i = 0; i < surveys.Count; i++)
            {
                Survey survey = surveys[i];
                SurveySpec spec = survey.Spec;
                sb.Append("    {\n");
                sb.Append("      \"office\": \"").Append(Json(DebugModel.OfficeName(spec.Office))).Append("\",\n");
                sb.Append("      \"isWholeIsland\": ").Append(spec.IsWholeIsland ? "true" : "false").Append(",\n");
                sb.Append("      \"year\": ").Append(spec.Year.ToString(Ci)).Append(",\n");
                sb.Append("      \"scaleDenominator\": ").Append(spec.Scale.Denominator.ToString(Ci)).Append(",\n");
                sb.Append("      \"rotationDeg\": ").Append(Num(spec.RotationDeg)).Append(",\n");
                sb.Append("      \"overlapFraction\": ").Append(Num(spec.OverlapFraction)).Append(",\n");
                sb.Append("      \"format\": { \"widthMm\": ").Append(Num(spec.Format.WidthMm))
                  .Append(", \"heightMm\": ").Append(Num(spec.Format.HeightMm))
                  .Append(", \"marginMm\": ").Append(Num(spec.Format.MarginMm)).Append(" },\n");
                sb.Append("      \"groundPerSheet\": { \"widthM\": ").Append(Num(spec.SheetGroundWidth))
                  .Append(", \"heightM\": ").Append(Num(spec.SheetGroundHeight)).Append(" },\n");
                sb.Append("      \"drawnClasses\": [");
                bool first = true;
                for (int c = 0; c < FeatureClasses.Count; c++)
                {
                    FeatureClass cls = (FeatureClass)c;
                    if (!FeatureMatrix.Draws(spec.Office, cls))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(", ");
                    }

                    sb.Append('"').Append(cls.ToString()).Append('"');
                    first = false;
                }

                sb.Append("],\n");
                sb.Append("      \"sheetCount\": ").Append(survey.SheetCount.ToString(Ci)).Append(",\n");
                sb.Append("      \"sheets\": [");
                for (int k = 0; k < survey.Sheets.Count; k++)
                {
                    Sheet sheet = survey.Sheets[k];
                    if (k > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append("\n        { \"number\": ").Append(sheet.Number.ToString(Ci))
                      .Append(", \"centre\": [").Append(Num(sheet.CentreGround.X)).Append(", ")
                      .Append(Num(sheet.CentreGround.Y)).Append("], \"rotationDeg\": ")
                      .Append(Num(sheet.RotationDeg)).Append(", \"file\": \"")
                      .Append(Json(SheetFileName(sheet))).Append("\" }");
                }

                sb.Append(survey.Sheets.Count > 0 ? "\n      ]\n" : "]\n");
                sb.Append(i == surveys.Count - 1 ? "    }\n" : "    },\n");
            }

            sb.Append("  ],\n");

            IslandFeatures f = model.Island.Features;
            sb.Append("  \"features\": {\n");
            sb.Append("    \"peaks\": [");
            for (int i = 0; i < f.Peaks.Count; i++)
            {
                Peak pk = f.Peaks[i];
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("\n      { \"name\": ")
                  .Append(string.IsNullOrEmpty(pk.Name) ? "null" : "\"" + Json(pk.Name) + "\"")
                  .Append(", \"spotHeightM\": ").Append(pk.SpotHeightM.ToString(Ci))
                  .Append(", \"position\": [").Append(Num(pk.Position.X)).Append(", ")
                  .Append(Num(pk.Position.Y)).Append("] }");
            }

            sb.Append(f.Peaks.Count > 0 ? "\n    ],\n" : "],\n");

            sb.Append("    \"settlements\": [");
            for (int i = 0; i < f.Settlements.Count; i++)
            {
                Settlement st = f.Settlements[i];
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("\n      { \"name\": \"").Append(Json(st.Name))
                  .Append("\", \"position\": [").Append(Num(st.Position.X)).Append(", ")
                  .Append(Num(st.Position.Y)).Append("] }");
            }

            sb.Append(f.Settlements.Count > 0 ? "\n    ],\n" : "],\n");
            sb.Append("    \"riverCount\": ").Append(f.Rivers.Count.ToString(Ci)).Append("\n");
            sb.Append("  }\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
