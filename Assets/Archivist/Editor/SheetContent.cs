using System;
using System.Collections.Generic;
using Archivist.Generation.Features;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Editor
{
    /// <summary>
    /// The four area-derived layers: everything whose extraction depends on a ground rect and a
    /// LOD rather than on the island's feature lists. Peaks, rivers, settlements and POIs are not
    /// here — they are already in <c>Island.Features</c> and cost nothing to reach.
    /// </summary>
    public sealed class SheetGeometry
    {
        public List<Polyline> Coast = new List<Polyline>();
        public List<Polyline> Contours = new List<Polyline>();
        public List<Polyline> Grid = new List<Polyline>();
        public List<Sounding> Soundings = new List<Sounding>();
    }

    /// <summary>
    /// The §8.3 gather, in one place. Pane 1, Pane 2, Pane 3 and the SVG export all ask the same
    /// question — "what does this office draw over this ground, at this LOD?" — and used to answer
    /// it with four copies of the same eight-class ladder.
    ///
    /// Two things legitimately differ between the callers and are therefore parameters:
    /// <list type="bullet">
    /// <item>the <b>gate</b>: the §8.3 matrix for a sheet or a cell, the user's layer toggles for
    /// the island pane, which has no office;</item>
    /// <item>the <b>area</b>: a sheet's ground bounds, the Compare pane's shared crop, or the
    /// island pane's tile-snapped viewport.</item>
    /// </list>
    /// What must NOT be unified is the order the results are drawn in — that is a real per-backend
    /// difference (the SVG puts soundings last) and the callers keep it.
    /// </summary>
    public static class SheetContent
    {
        /// <summary>
        /// Gathers the four area-derived layers for one office over one ground rect.
        /// </summary>
        /// <param name="gridScale">Scale the §6.4 grid is defined by — a sheet's own, or the
        /// Garrison survey's when the caller has no sheet.</param>
        /// <param name="gate">Per-class "does this get drawn at all?". Null means everything.</param>
        /// <param name="wholeIslandCoastAtLowLod">Take the coastline straight from the island at
        /// lod &lt;= 1 rather than re-extracting it. Only Pane 1 draws that far out.</param>
        public static SheetGeometry Gather(DebugModel model, Rect2 area, int lod, MapScale gridScale,
                                           Func<FeatureClass, bool> gate,
                                           bool wholeIslandCoastAtLowLod = false)
        {
            SheetGeometry g = new SheetGeometry();
            if (model == null || !model.HasIsland)
            {
                return g;
            }

            if (Draws(gate, FeatureClass.Coast))
            {
                // The coastline at overview zoom is already in hand — Island.Coastline is extracted
                // at lod 1 over the whole domain (§6.1). Re-extracting it would cost a second of
                // nothing.
                g.Coast = wholeIslandCoastAtLowLod && lod <= 1
                    ? new List<Polyline>(model.Island.Coastline)
                    : model.CoastFor(area, lod);
            }

            if (Draws(gate, FeatureClass.Contour))
            {
                g.Contours = model.ContoursFor(area, lod, model.ContourLevels);
            }

            if (Draws(gate, FeatureClass.Grid))
            {
                try
                {
                    List<Polyline> grid = GarrisonGrid.ForRect(area, gridScale);
                    if (grid != null)
                    {
                        g.Grid = grid;
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[Archivist] garrison grid failed: " + e.Message);
                }
            }

            if (Draws(gate, FeatureClass.Sounding))
            {
                try
                {
                    List<Sounding> s = Soundings.ForRect(model.Island.Field, area);
                    if (s != null)
                    {
                        g.Soundings = s;
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[Archivist] soundings failed: " + e.Message);
                }
            }

            return g;
        }

        static bool Draws(Func<FeatureClass, bool> gate, FeatureClass cls)
        {
            return gate == null || gate(cls);
        }
    }
}
