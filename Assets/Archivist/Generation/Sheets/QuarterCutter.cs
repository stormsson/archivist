using System.Collections.Generic;
using Archivist.Generation.Determinism;
using Archivist.Generation.Geometry;

namespace Archivist.Generation.Sheets
{
    /// <summary>
    /// Cuts an island into four plates per office, and one chart per island.
    /// <c>docs/quarters/requirements.md</c> §2 is the authority.
    ///
    /// <para><b>The cut is a pure function of the land bounds</b> (Q1.1–Q1.4). Halve the bounds
    /// on both axes: four axis-aligned rects, NW NE SW SE, meeting exactly. No rotation, no
    /// overlap, no cull, and — the part worth stating — <b>no sub-stream</b>. Nothing here draws
    /// a number, so nothing here can move an island by drawing one more.</para>
    ///
    /// <para><b>Every office gets the same four rects</b> (Q1.2). That is what makes the board's
    /// layers register: flip from one office to another and nothing moves but the ink. An office
    /// that chose its own extent, angle or paper would break the only thing the table does.</para>
    ///
    /// <para><b>Scale is per island, not per office or per survey</b> (Q1.6). The smallest rung
    /// of <see cref="QuarterLadder"/> at which one quarter fits the map area. A small island
    /// therefore sits in blank margin and a large one crowds its sheets — physical size made
    /// legible without a scale bar, which suits a game with no readouts (R4.9).</para>
    /// </summary>
    public static class QuarterCutter
    {
        /// <summary>
        /// IMPLEMENTATION CHOICE. The eras of §2 are not in v1 (R1.6), so a year is a label
        /// on a sheet and nothing reads it.
        /// </summary>
        const int YearMinInclusive = 1860;
        const int YearMaxExclusive = 1936;

        /// <summary>
        /// The four quarters, in <see cref="Sheet.Number"/> order. Numbering runs 1..4 with no
        /// gaps, which is what A4's contiguity check asks for and what R2.10b's "a gap in a run
        /// is unambiguous" rests on.
        ///
        /// <para><b>Order is load-bearing.</b> A plate's identity is its number
        /// (<c>SheetId.Number</c>), so renumbering the quarters renames every plate in every
        /// binder in every save.</para>
        /// </summary>
        public static readonly string[] QuarterNames = { "NW", "NE", "SW", "SE" };

        /// <summary>
        /// R2.3's fixed set, for quarters, ascending: 1:5000, 1:10000, 1:25000. Three rungs and
        /// never a continuous value.
        ///
        /// <para><b>1:2500 was removed, not replaced.</b> No island's quarter could ever reach
        /// it — 1:2500 on an A1 covers 1285 x 1902 m, and a quarter of even a small island is
        /// two or three kilometres across — so the fine rung was dead and the live ladder was
        /// really two rungs a factor of 2.5 apart. Measured over the first three islands, every
        /// one of them landed on 1:10000 filling 14–28% of its sheet, which makes Q1.6's
        /// "a small island sits in margin" signal say nothing. With 1:5000 in its place two of
        /// the three drop a rung and fill 54–65%.</para>
        /// </summary>
        public static MapScale[] QuarterLadder
        {
            get
            {
                return new[]
                {
                    new MapScale(Tuning.QuarterScaleFineDenominator),
                    new MapScale(Tuning.QuarterScaleDenominator),
                    MapScale.WholeIsland
                };
            }
        }

        /// <summary>
        /// The paper every plate in the game is printed on (Q1.5): <b>A1, in whichever
        /// orientation suits the island</b>.
        ///
        /// <para>Orientation is not size. The sheet, the binder and the rack are the same
        /// object either way; only which way the map runs on it changes, and every plate of one
        /// island shares the choice, so Q1.2's register is untouched.</para>
        ///
        /// <para><b>Turning the paper cannot change how full a sheet is</b> — the map area is
        /// 514 x 761 mm either way, so the fraction covered at a given rung is identical. What
        /// it changes is <i>which rung the island lands on</i>, and that is worth having:
        /// Ormwick's quarter needs 1:5043 portrait, misses the 1:5000 rung by 43 parts and
        /// falls to 1:10000 at 16% fill; landscape it needs 1:4732, lands on 1:5000, and fills
        /// <b>64.5%</b>. Choosing per island can only help, never hurt.</para>
        /// </summary>
        public static SheetFormat Paper { get { return SheetFormat.A1; } }

        /// <summary>
        /// One office's four plates of one island.
        ///
        /// <para>An empty land bounds — a seed that produced no land, which the thin-atoll case
        /// can still do — yields four sheets around the origin rather than none. A survey with
        /// no sheets would be a hole in the collection that the ledger cannot describe, and the
        /// plates are blank rather than absent, which is R2.9's texture and not a failure.</para>
        /// </summary>
        public static Survey Cut(Rect2 landBounds, ulong islandSeed, Office office)
        {
            MapScale scale;
            SheetFormat paper;
            ChooseQuarterPaper(landBounds, out scale, out paper);

            var spec = new SurveySpec(islandSeed, office, PickYear(islandSeed, office, false),
                                      scale, 0.0, paper, 0.0, false);

            var sheets = new List<Sheet>(4);
            Rect2[] rects = Quarters(landBounds);

            // The rect, not just its centre. A plate is of its quarter (Q1.1); taking only the
            // centre and letting the paper decide the extent is what made four quarters into
            // four overlapping drawings of the same island.
            for (int i = 0; i < 4; i++)
                sheets.Add(new Sheet(spec, i + 1, rects[i].Centre, rects[i].Width, rects[i].Height));

            return new Survey(spec, sheets);
        }

        /// <summary>
        /// The island's one chart (Q2.3, R2.2a) — the board's base (Q4.4), and the sheet without
        /// which a board cannot open (R6.8a).
        ///
        /// <para><b>One per island, not one per office.</b> The base's job is to be the thing
        /// that does not move while <c>Q</c>/<c>E</c> flips the layers over it; a chart per
        /// office would make the reference flicker along with everything else. Which office made
        /// it is drawn from <see cref="StreamNames.WholeIsland"/>.</para>
        ///
        /// <para><c>Range(0, 3)</c>, not <c>Offices.Count</c>: the chart is a reconnaissance map
        /// of the whole island and Antiquarian has no island-scale remit. Widening the draw would
        /// re-roll the office — and, because the year stream is indexed by office ordinal, the
        /// year too — on roughly a quarter of existing islands.</para>
        /// </summary>
        public static Survey CutChart(Rect2 landBounds, ulong islandSeed)
        {
            Pcg32 pick = Streams.For(islandSeed, StreamNames.WholeIsland);
            var office = (Office)pick.Range(0, 3);

            MapScale scale;
            SheetFormat format;
            ChooseChartPaper(landBounds, out scale, out format);

            var spec = new SurveySpec(islandSeed, office, PickYear(islandSeed, office, true),
                                      scale, 0.0, format, 0.0, true);

            // Of the ISLAND, not of the paper it is printed on — the same rule the quarters
            // follow (Q1.1). F-S1.6 measured a chart's paper at 564% of the land area, so a
            // paper-extent chart spends seven pixels in eight on open sea and gives the island
            // the eighth. What it is a chart OF is the island.
            V2 centre = landBounds.IsEmpty ? V2.Zero : landBounds.Centre;
            double w = landBounds.IsEmpty ? spec.SheetGroundWidth : landBounds.Width;
            double h = landBounds.IsEmpty ? spec.SheetGroundHeight : landBounds.Height;

            return new Survey(spec, new List<Sheet> { new Sheet(spec, 1, centre, w, h) });
        }

        /// <summary>
        /// The Antiquarian survey's spec. That office does not tile ground — one small square
        /// sheet per curiosity (POC-03 §2.1) — so it has no quarters and
        /// <see cref="DetailSheetCutter"/> still cuts it.
        ///
        /// <para>Its rotation is <b>nominal</b>: every detail sheet carries its own, rolled per
        /// POI from <see cref="StreamNames.PoiSheet"/> (POC-03 §2.2). Whether detail sheets
        /// survive the quarter model at all is open — <c>docs/rework1/01-removal.md</c> §5 — and
        /// nothing here decides it. This exists so that question can be answered later without
        /// unpicking the cutter.</para>
        /// </summary>
        public static SurveySpec PlanDetail(ulong islandSeed)
        {
            return new SurveySpec(islandSeed, Office.Antiquarian,
                                  PickYear(islandSeed, Office.Antiquarian, false),
                                  MapScale.PoiDetail, 0.0, SheetFormat.DetailSheet, 0.0, false);
        }

        /// <summary>
        /// The four rects, NW NE SW SE, meeting exactly at the bounds' centre (Q1.4). Ground Y
        /// runs north, so the two north quarters are the ones above the centre line.
        /// </summary>
        public static Rect2[] Quarters(Rect2 landBounds)
        {
            if (landBounds.IsEmpty)
            {
                var empty = new Rect2[4];
                for (int i = 0; i < 4; i++) empty[i] = Rect2.FromCentreSize(V2.Zero, 0.0, 0.0);
                return empty;
            }

            V2 c = landBounds.Centre;
            double hw = landBounds.Width * 0.5, hh = landBounds.Height * 0.5;
            double qw = hw * 0.5, qh = hh * 0.5;

            return new[]
            {
                Rect2.FromCentreSize(new V2(c.X - qw, c.Y + qh), hw, hh),   // 1 NW
                Rect2.FromCentreSize(new V2(c.X + qw, c.Y + qh), hw, hh),   // 2 NE
                Rect2.FromCentreSize(new V2(c.X - qw, c.Y - qh), hw, hh),   // 3 SW
                Rect2.FromCentreSize(new V2(c.X + qw, c.Y - qh), hw, hh),   // 4 SE
            };
        }

        /// <summary>
        /// The finest rung at which one quarter fits the map area, and the orientation that
        /// gets it there (Q1.6).
        /// </summary>
        public static void ChooseQuarterPaper(Rect2 landBounds, out MapScale scale,
                                              out SheetFormat format)
        {
            double quarterWidth = landBounds.IsEmpty ? 0.0 : landBounds.Width * 0.5;
            double quarterHeight = landBounds.IsEmpty ? 0.0 : landBounds.Height * 0.5;
            ChoosePaper(quarterWidth, quarterHeight, QuarterLadder, out scale, out format);
        }

        /// <summary>
        /// The chart's scale and orientation: the island has to fit one sheet, so the ladder is
        /// 1:25000 then 1:50000.
        ///
        /// <para>This is the one place a plate is not portrait A1 (Q1.5 governs the quarters).
        /// The chart is not a quarter, is never laid beside one, and is drawn under everything —
        /// so it costs nothing that it is the only landscape sheet in the archive.</para>
        /// </summary>
        static void ChooseChartPaper(Rect2 landBounds, out MapScale scale, out SheetFormat format)
        {
            double width = landBounds.IsEmpty ? 0.0 : landBounds.Width;
            double height = landBounds.IsEmpty ? 0.0 : landBounds.Height;
            MapScale[] ladder = { MapScale.WholeIsland, MapScale.WholeIslandFallback };
            ChoosePaper(width, height, ladder, out scale, out format);
        }

        /// <summary>
        /// One rule for one question: an extent and a ladder in, a rung and an orientation out.
        /// Quarters and chart differ in nothing else, and a quarter choosing its paper
        /// differently would be a second rule for one question.
        ///
        /// <para>Rungs are tried in order and both orientations at each, so a coarser scale is
        /// never taken to keep the paper the way up it started.</para>
        ///
        /// <para>Falls through to the coarsest rung rather than throwing. An island too large
        /// for 1:25000 would have to be over 25 km across, which the 16 km domain makes
        /// impossible; a slightly clipped plate beats a hard stop on an otherwise valid
        /// seed.</para>
        /// </summary>
        static void ChoosePaper(double width, double height, MapScale[] ladder,
                                out MapScale scale, out SheetFormat format)
        {
            SheetFormat portrait = SheetFormat.A1;
            SheetFormat landscape = portrait.Landscape;

            SheetFormat preferred = width > height ? landscape : portrait;
            SheetFormat alternate = width > height ? portrait : landscape;

            for (int i = 0; i < ladder.Length; i++)
            {
                if (Fits(ladder[i], preferred, width, height))
                { scale = ladder[i]; format = preferred; return; }
                if (Fits(ladder[i], alternate, width, height))
                { scale = ladder[i]; format = alternate; return; }
            }

            scale = ladder[ladder.Length - 1];
            format = preferred;
        }

        static bool Fits(MapScale scale, SheetFormat format, double width, double height)
        {
            return scale.GroundMetres(format.MapWidthMm) >= width
                && scale.GroundMetres(format.MapHeightMm) >= height;
        }

        /// <summary>
        /// The survey's year, indexed by <c>(int)office</c>, so the ordinal warning on
        /// <see cref="Office"/> still binds.
        ///
        /// <para>The chart draws from its own stream so it does not inherit the same year as
        /// that office's quarters — a reconnaissance sheet and a detail survey by one office
        /// would not share a date, and identical years would read as a bug.</para>
        /// </summary>
        static int PickYear(ulong islandSeed, Office office, bool chart)
        {
            Pcg32 rng = Streams.For(islandSeed, chart ? StreamNames.YearWholeIsland : StreamNames.Year,
                                    (int)office);
            return rng.Range(YearMinInclusive, YearMaxExclusive);
        }
    }
}
