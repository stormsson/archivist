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

        /// <summary>
        /// Whole-island survey first (R2.2a — the entry point), then one per office in
        /// <see cref="Offices.All"/> order. The Antiquarian survey holds detail sheets
        /// (POC-03 §2), every other survey holds survey sheets; <see cref="Sheet.IsDetail"/>
        /// tells them apart.
        /// </summary>
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

        /// <summary>
        /// DEBUG ONLY — which offices actually cut sheets. Ambient static state, which §4.1
        /// otherwise forbids because it changes generated output: an island generated with an
        /// office switched off is NOT the island that seed describes. Determinism still
        /// holds (A2 passes either way), which is exactly why this is a footgun — nothing
        /// will fail to tell you a survey is missing. The debug window shows a warning while
        /// any of these is false. Must be true everywhere outside the Editor.
        /// </summary>
        public static bool CutHydrographic = true;
        public static bool CutLandSurvey = true;
        public static bool CutGarrison = true;
        public static bool CutAntiquarian = true;

        public static bool AllOfficesEnabled
        {
            get { return CutHydrographic && CutLandSurvey && CutGarrison && CutAntiquarian; }
        }

        static bool CutsOffice(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return CutHydrographic;
                case Office.LandSurvey: return CutLandSurvey;
                case Office.Garrison: return CutGarrison;
                case Office.Antiquarian: return CutAntiquarian;
                default: return true;
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

            // POC-03 spec §1.3: POIs run AFTER peaks, settlements and rivers — RuinedTower needs
            // peaks, RuinedChapel needs settlements — and before naming, because nothing
            // references POIs. Their one PRNG draw is the count, from the new "poi" stream, so
            // §4.3 guarantees every feature above is bit-identical to what it was before POIs
            // existed (P1.5, asserted by A2).
            List<Poi> pois = PoiSiting.Generate(island.Field, island.LandBounds, island.Coastline,
                                                peaks, towns);

            // Naming is a separate pass so it cannot perturb feature selection (§9).
            int namedPeaks = Math.Min(Tuning.PeakNamedCount, peaks.Count);
            island.Names = NameGenerator.Generate(islandSeed, towns.Count, namedPeaks);

            for (int i = 0; i < namedPeaks; i++) peaks[i] = peaks[i].WithName(island.Names.Peaks[i]);
            for (int i = 0; i < towns.Count; i++) towns[i] = towns[i].WithName(island.Names.Settlements[i]);

            island.Features = new IslandFeatures(peaks, towns, rivers, pois);
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
            surveys.Add(SurveyCutter.CutWholeIsland(island.LandBounds, island.Seed));

            // Derived once and handed to every office, not once per office: Land Survey's
            // degenerate case falls back to hydroDeg + 90 (D2).
            double hydroDeg = HydroRotation(island);

            for (int i = 0; i < Offices.All.Length; i++)
            {
                Office office = Offices.All[i];
                if (!CutsOffice(office)) continue;

                surveys.Add(CutSurvey(island, office, hydroDeg));
            }
            return surveys;
        }

        /// <summary>
        /// One office's survey of this island, cut on demand.
        ///
        /// <para>The same path <see cref="Generate"/> takes — plan, then dispatch to the
        /// cutter that office's coverage shape needs — so a survey cut here is identical to
        /// the one the island was born with, for the same island and office. That identity is
        /// the point: it is what makes this safe to use for anything the generated set already
        /// covers, rather than a second, drifting way to make a survey.</para>
        ///
        /// <para><b>Nothing is attached.</b> <see cref="Surveys"/> is fixed at generation and
        /// stays that way — an island is a function of its seed (R1.1), and a survey appearing
        /// on it later would make it a function of its seed plus whoever called this. The
        /// result is the caller's to hold.</para>
        ///
        /// <para>The debug flags on this class are deliberately NOT consulted. They exist to
        /// leave an office out of a whole island; asking for one by name is an explicit
        /// request and answering it with null would be a silent refusal.</para>
        /// </summary>
        public Survey CutSurvey(Office office)
        {
            return CutSurvey(this, office, HydroRotation(this));
        }

        /// <summary>
        /// Shared by both entry points, so the loop and the on-demand call cannot diverge.
        /// <paramref name="hydroDeg"/> is a parameter rather than derived here because
        /// <see cref="CutSurveys"/> needs it once for all four offices.
        /// </summary>
        static Survey CutSurvey(Island island, Office office, double hydroDeg)
        {
            SurveySpec spec = SurveyCutter.PlanSurvey(island.Field, island.Coastline,
                                                      island.LandBounds, office, hydroDeg);

            return SurveyCutter.CutFor(island.Field, island.Coastline, island.Service,
                                       island.Features.Pois, island.LandBounds, spec);
        }

        /// <summary>The island's Hydrographic rotation (D2). Every office's plan needs it.</summary>
        static double HydroRotation(Island island)
        {
            PcaResult ignored;
            return Rotations.Hydrographic(island.Coastline, island.Params.ServiceRadius, out ignored);
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
