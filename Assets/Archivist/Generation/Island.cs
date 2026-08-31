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
            //
            // Over the LAND BOUNDS and a margin, not the whole domain. The sea-level isoline
            // can only run where land meets sea, and every sampled land point is inside the
            // bounds -- which ComputeLandBounds has just paid for. Scanning all 256 km² of the
            // domain was 68.6% of the cost of an island, and five sixths of it was open sea.
            //
            // The margin is Tuning.CoastlineMarginCells, and that comment is where the reason
            // lives: land is sampled on the 64 m lattice, so an islet finer than that can fall
            // between samples and be missed. 4 cells was measured, not chosen.
            double half = island.Params.DomainMetres * 0.5;
            island.Coastline = Contours.Extract(island.Field, CoastlineArea(island.LandBounds, half),
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
        /// Where the island-scale coastline is extracted: the land bounds, grown by
        /// <see cref="Tuning.CoastlineMarginCells"/> cells and clamped to the domain.
        ///
        /// <para>Clamped rather than trusted: an island whose land reaches the domain edge would
        /// otherwise ask <c>Contours.Extract</c> for ground the field does not define. Empty
        /// bounds — a seed that produced no land — fall back to the whole domain, because there
        /// is nothing to centre a margin on and the answer is an empty list either way.</para>
        /// </summary>
        static Rect2 CoastlineArea(Rect2 landBounds, double half)
        {
            if (landBounds.IsEmpty) return new Rect2(-half, -half, half, half);

            double m = Tuning.CoastlineMarginCells * Tuning.BaseCell;
            return new Rect2(Math.Max(landBounds.MinX - m, -half), Math.Max(landBounds.MinY - m, -half),
                             Math.Min(landBounds.MaxX + m,  half), Math.Min(landBounds.MaxY + m,  half));
        }

        /// <summary>
        /// The chart first, then one four-plate survey per office that cuts one (Q2.3).
        ///
        /// <para>Order is the file's and the board's: the chart is the base everything else is
        /// laid over (Q4.4), and the offices follow in <see cref="Offices.All"/> order, which is
        /// the order <c>Q</c>/<c>E</c> cycles them in.</para>
        /// </summary>
        static List<Survey> CutSurveys(Island island)
        {
            var surveys = new List<Survey>();
            surveys.Add(QuarterCutter.CutChart(island.LandBounds, island.Seed));

            for (int i = 0; i < Offices.All.Length; i++)
            {
                Office office = Offices.All[i];
                if (!CutsOffice(office)) continue;

                surveys.Add(CutSurvey(island, office));
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
            return CutSurvey(this, office);
        }

        /// <summary>Shared by both entry points, so the loop and the on-demand call cannot
        /// diverge.</summary>
        static Survey CutSurvey(Island island, Office office)
        {
            if (office == Office.Antiquarian)
            {
                return DetailSheetCutter.Cut(island.Features.Pois, island.Service,
                                             QuarterCutter.PlanDetail(island.Seed));
            }

            return QuarterCutter.Cut(island.LandBounds, island.Seed, office);
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
