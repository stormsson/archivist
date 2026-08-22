using System;
using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Features;
using Archivist.Generation.Field;
using Archivist.Generation.Geometry;
using Archivist.Generation.Naming;
using Archivist.Generation.Sheets;

namespace Archivist.Generation
{
    /// <summary>
    /// Facade: seed -> island (§14). One island seed generates one island deterministically
    /// and completely; nothing else is needed to reproduce it (R1.1). Nothing geometric is
    /// persisted — only the seed (R1.11, R3.1).
    /// </summary>
    public sealed class Island
    {
        public ulong Seed { get; private set; }
        public IslandParams Params { get; private set; }
        public IslandField Field { get; private set; }
        public Rect2 LandBounds { get; private set; }

        /// <summary>Island-scale anchor (R1.4). Atolls yield two loops — outer shore and lagoon.</summary>
        public IReadOnlyList<Polyline> Coastline { get; private set; }

        public IslandFeatures Features { get; private set; }
        public IslandNames Names { get; private set; }
        public ServiceRule Service { get; private set; }

        /// <summary>Whole-island survey first (R2.2a — the entry point), then one per office.</summary>
        public IReadOnlyList<Survey> Surveys { get; private set; }

        public string Name { get { return Names != null ? Names.Island : null; } }

        public int TotalSheets
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Surveys.Count; i++) n += Surveys[i].SheetCount;
                return n;
            }
        }

        public static Island Generate(ulong collectionSeed, int islandIndex, IslandCharacter? forcedCharacter = null)
        {
            return FromSeed(Streams.IslandSeed(collectionSeed, islandIndex), forcedCharacter);
        }

        public static Island FromSeed(ulong islandSeed, IslandCharacter? forcedCharacter = null)
        {
            var island = new Island();
            island.Seed = islandSeed;
            island.Params = IslandParams.FromSeed(islandSeed, forcedCharacter);
            island.Field = new IslandField(island.Params);
            island.LandBounds = island.Field.ComputeLandBounds();

            // Island-scale contours: the whole-island view sits at lod 1 (32 m cell), §6.2.
            double half = island.Params.DomainMetres * 0.5;
            Rect2 domain = new Rect2(-half, -half, half, half);
            island.Coastline = Contours.Extract(island.Field, domain,
                                                Contours.CellSizeForLod(1), island.Params.SeaLevel);

            // Discrete features, once per island, in this order, with stable ids (§3.1, §7).
            List<Peak> peaks = Peaks.Generate(island.Field, island.LandBounds);
            List<Settlement> towns = Settlements.Generate(island.Field, island.LandBounds, island.Coastline);
            List<River> rivers = Rivers.Generate(island.Field, peaks);

            // Naming is a separate pass so it cannot perturb feature selection (§9).
            int namedPeaks = Math.Min(Tuning.PeakNamedCount, peaks.Count);
            island.Names = NameGenerator.Generate(islandSeed, towns.Count, namedPeaks);

            for (int i = 0; i < namedPeaks; i++) peaks[i] = peaks[i].WithName(island.Names.Peaks[i]);
            for (int i = 0; i < towns.Count; i++) towns[i] = towns[i].WithName(island.Names.Settlements[i]);

            island.Features = new IslandFeatures(peaks, towns, rivers);
            island.Service = new ServiceRule(island.Field, island.LandBounds, island.Features,
                                             island.Params.ServiceRadius);

            island.Surveys = CutSurveys(island);
            return island;
        }

        /// <summary>
        /// Rotation order matters: Hydrographic derives from the coast, and Land Survey's
        /// degenerate case falls back to hydroDeg + 90 (D2), so Hydrographic must be first.
        /// </summary>
        static List<Survey> CutSurveys(Island island)
        {
            var surveys = new List<Survey>();
            surveys.Add(SurveyCutter.CutWholeIsland(island.Field, island.LandBounds, island.Seed));

            PcaResult ignored;
            double hydroDeg = Rotations.Hydrographic(island.Coastline, island.Params.ServiceRadius, out ignored);

            Office[] offices = { Office.Hydrographic, Office.LandSurvey, Office.Garrison };
            for (int i = 0; i < offices.Length; i++)
            {
                SurveySpec spec = SurveyCutter.PlanSurvey(island.Field, island.Coastline,
                                                          island.LandBounds, offices[i], hydroDeg);

                // Two cutters, one per coverage shape. Hydrographic walks the shore with
                // per-sheet rotation (D-H2); Land Survey and Garrison keep the single-
                // rotation lattice R2.4 requires of them.
                surveys.Add(offices[i] == Office.Hydrographic
                    ? CoastWalkCutter.Cut(island.Field, island.Coastline, island.Service, spec)
                    : SurveyCutter.Cut(island.Field, island.Coastline, island.Service,
                                       island.LandBounds, spec));
            }
            return surveys;
        }

        public Survey SurveyFor(Office office)
        {
            for (int i = 0; i < Surveys.Count; i++)
            {
                if (!Surveys[i].Spec.IsWholeIsland && Surveys[i].Spec.Office == office) return Surveys[i];
            }
            return null;
        }

        public Survey WholeIslandSurvey
        {
            get
            {
                for (int i = 0; i < Surveys.Count; i++) if (Surveys[i].Spec.IsWholeIsland) return Surveys[i];
                return null;
            }
        }
    }
}
