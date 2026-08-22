using System;
using System.Globalization;
using Archivist.Generation.Features;
using Archivist.Generation.Geometry;
using UnityEngine;

namespace Archivist.Editor
{
    /// <summary>
    /// The lettering every pane puts next to a mark: spot heights (§7.1), settlement names (§7.2),
    /// POI kinds (POC-03) and sounding depths (§6.3).
    ///
    /// Text is map content here, not typography — §8.2's "one style" applies to it too — so the
    /// offsets and sizes below are the record of that one style rather than four sets of bare
    /// literals sitting in three panes and an exporter.
    /// </summary>
    public static class FeatureLabels
    {
        /// <summary>Offset from a mark to its name or height, in view points.</summary>
        public const float NameOffsetX = 6.0f;
        public const float NameOffsetY = -7.0f;

        /// <summary>Size a name or a spot height is set in, in view points.</summary>
        public const float NameSize = 10.0f;

        /// <summary>
        /// Soundings sit tighter and smaller: there are many of them on a lattice (§6.3) and the
        /// number IS the mark, so it has to crowd its own tick without crowding its neighbours.
        /// </summary>
        public const float DepthOffsetX = 3.0f;
        public const float DepthOffsetY = -6.0f;
        public const float DepthSize = 9.0f;

        /// <summary>
        /// SVG lettering is sized in multiples of the sheet stroke width, not in view points: the
        /// file is in millimetres on paper, so a fixed point size would mean nothing there.
        /// </summary>
        public const double SvgStrokeMultiple = 10.0;

        /// <summary>"Cairn Head 412", or just "412" when the peak is unnamed (§7.1).</summary>
        public static string PeakText(Peak p)
        {
            string label = p.SpotHeightM.ToString(CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(p.Name) ? label : p.Name + " " + label;
        }

        /// <summary>The depth in whole metres below sea level (§6.3).</summary>
        public static string DepthText(Sounding s)
        {
            return s.DepthM.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Emits every mark label for one office, in the one order all three panes use: peaks,
        /// settlements, POIs, soundings. The caller owns <c>Begin</c>/<c>End</c> so it can add its
        /// own lettering — sheet numbers, grid values — around this.
        /// </summary>
        /// <param name="gate">Per-class "does this office letter this?". Null means everything;
        /// pass the §8.3 matrix, or the layer toggles, or a stricter rule of your own.</param>
        /// <param name="visible">View-space cull. Null means place every label.</param>
        public static void Add(TextLayer text, FeatureMarks marks, ViewTransform view,
                               Func<FeatureClass, bool> gate, Func<Vector2, bool> visible)
        {
            if (text == null)
            {
                return;
            }

            if (Draws(gate, FeatureClass.Peak) && marks.Peaks != null)
            {
                for (int i = 0; i < marks.Peaks.Count; i++)
                {
                    AddName(text, view, visible, marks.Peaks[i].Position, PeakText(marks.Peaks[i]));
                }
            }

            if (Draws(gate, FeatureClass.Settlement) && marks.Settlements != null)
            {
                for (int i = 0; i < marks.Settlements.Count; i++)
                {
                    AddName(text, view, visible, marks.Settlements[i].Position, marks.Settlements[i].Name);
                }
            }

            if (Draws(gate, FeatureClass.Poi) && marks.Pois != null)
            {
                for (int i = 0; i < marks.Pois.Count; i++)
                {
                    AddName(text, view, visible, marks.Pois[i].Position, marks.Pois[i].Kind.Label());
                }
            }

            if (Draws(gate, FeatureClass.Sounding) && marks.Soundings != null)
            {
                for (int i = 0; i < marks.Soundings.Count; i++)
                {
                    Vector2 v = view.ToView(marks.Soundings[i].Position);
                    if (visible == null || visible(v))
                    {
                        text.Add(DepthText(marks.Soundings[i]),
                                 new Vector2(v.x + DepthOffsetX, v.y + DepthOffsetY),
                                 DepthSize, VectorDraw.Ink);
                    }
                }
            }
        }

        static void AddName(TextLayer text, ViewTransform view, Func<Vector2, bool> visible,
                            V2 world, string label)
        {
            Vector2 v = view.ToView(world);
            if (visible == null || visible(v))
            {
                text.Add(label, new Vector2(v.x + NameOffsetX, v.y + NameOffsetY), NameSize, VectorDraw.Ink);
            }
        }

        static bool Draws(Func<FeatureClass, bool> gate, FeatureClass cls)
        {
            return gate == null || gate(cls);
        }
    }
}
