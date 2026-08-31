using Archivist.Generation.Sheets;

namespace Archivist.Render
{
    /// <summary>
    /// How one office draws: its paper, its inks, and how heavily it puts them down.
    /// R2.6, and Q2.6.
    ///
    /// <para><b>This carries more than it looks like it does.</b> Q1.2 gives every office the
    /// same four rects at the same scale on the same paper size — that is what puts the board's
    /// layers in register — so geometry cannot tell two offices apart and neither can rotation,
    /// paper size or scale. <b>Style is the only signal left.</b> §5.4 asks whether a player can
    /// read an office at a glance and whether that gets faster; if the answer is no, the far
    /// signal range of §4.1 is dead and the game's rhythm collapses into reading.</para>
    ///
    /// <para><b>A5b says how uneven the load is.</b> 56% of Garrison's plates carry nothing but a
    /// coastline and its grid, against 0% for Hydrographic and 1.2% for Land Survey. On more than
    /// half its sheets, style is doing <i>all</i> the work.</para>
    ///
    /// <para><b>One weight scale, not seven widths.</b> <see cref="RenderTuning"/> holds a width
    /// per feature in paper millimetres and those relationships are cartographic — a coast is
    /// heavier than a river is heavier than a contour, on anyone's map. What differs between
    /// offices is how hard the pen was pressed overall, so an office scales the table rather than
    /// replacing it. Seven knobs per office would be seven ways to lose the relationship that
    /// makes a sheet readable.</para>
    ///
    /// <para><b>These values are a first pass and are meant to be looked at.</b> §5.4's proof is
    /// three plates told apart at pile distance by someone who has not been told which is which —
    /// a judgement made by looking, not an assertion. Art direction for POC-02 is undefined and
    /// <see cref="Palette"/> says so; these are the same kind of placeholder, put where a future
    /// one can replace them in one file.</para>
    ///
    /// <para><b>Paper grain, wear and fold are NOT here</b> (R3.3): they are authored textures
    /// blended by a condition value at display time, not pixels generated per sheet. What this
    /// holds is the flat tone underneath them, which <c>SheetTexture</c> composites the map onto.
    /// Keeping it that way is also why style costs nothing to render — the expensive half of a
    /// paper stock never reaches the raster.</para>
    /// </summary>
    public readonly struct OfficeStyle
    {
        /// <summary>The unprinted sheet.</summary>
        public readonly Rgba Paper;

        /// <summary>The main pen: coastline and contours — the lines that describe ground.</summary>
        public readonly Rgba Ink;

        /// <summary>Rivers and soundings. Water is drawn in its own colour on a real chart, and
        /// it is the one distinction that survives being seen from across a room.</summary>
        public readonly Rgba Water;

        /// <summary>Settlements and peaks: the discrete marks a reader looks <i>for</i>.</summary>
        public readonly Rgba Marks;

        /// <summary>The Garrison grid. Every office has a value because the struct has no
        /// conditional fields; only one office draws it.</summary>
        public readonly Rgba Grid;

        /// <summary>Multiplies every width in <see cref="RenderTuning"/>. 1.0 is that table as
        /// written; above it the office presses harder.</summary>
        public readonly double Weight;

        /// <summary>
        /// A flat tone laid over the water, zero alpha for an office that leaves the sea blank
        /// (<see cref="HasWash"/>). The sea is the widest area on a plate, so whether it is
        /// toned at all is the strongest far signal an office has (§4.1).
        /// </summary>
        public readonly Rgba Wash;

        /// <summary>Take every n-th of <c>RenderTuning.LandBandEdges</c> as a contour. 1 is every
        /// edge — land as a hatching. 0 is an office that draws no contours at all.</summary>
        public readonly int ContourStride;

        public bool HasWash { get { return Wash.A > 0; } }

        public OfficeStyle(Rgba paper, Rgba ink, Rgba water, Rgba marks, Rgba grid, double weight,
                           Rgba wash, int contourStride)
        {
            Paper = paper; Ink = ink; Water = water; Marks = marks; Grid = grid; Weight = weight;
            Wash = wash; ContourStride = contourStride;
        }
    }

    /// <summary>
    /// The style table, and the one place an office's look is decided.
    ///
    /// <para><b>The offices differ by COMPOSITION, not by colour.</b> Three plates of one
    /// quarter with a thin coastline each, in three off-whites, read as one document however
    /// carefully the hues are chosen: measured, Hydrographic put ink on <b>0.54%</b> of a plate
    /// and Land Survey on 1.24%, so the difference between them was 0.7% of the pixels. Colour
    /// cannot carry a signal that occupies under one part in a hundred.</para>
    ///
    /// <para><b>So each office fills the half of the sheet it cares about</b>, and §2's lore
    /// table already said which half — by naming what each one draws <i>badly</i>:</para>
    ///
    /// <list type="bullet">
    /// <item><b>Hydrographic</b> — coasts and depths, omits anything inland. <b>Sea washed,
    /// land blank.</b> The land is an empty hole in the middle of its sheet.</item>
    /// <item><b>Land Survey</b> — terrain, omits the sea. <b>Land hatched, sea blank.</b>
    /// Contours at every band edge rather than every other, so relief reads as a texture.</item>
    /// <item><b>Garrison</b> — grid and heights, omits civilian detail. <b>Grid over
    /// everything</b>, land and sea alike, because a military sheet is ground to cross.</item>
    /// </list>
    ///
    /// <para>Three inverse compositions. Flipping <c>Q</c>/<c>E</c> swaps which half of the
    /// sheet is full and which is empty, which is Q2.4's "offices differ by omission" made the
    /// dominant visual fact instead of one fewer thin line — and it is legible at the pile
    /// distance §4.1 needs, from the shape of the ink, before any colour resolves.</para>
    ///
    /// <para><b>The papers stay close on purpose.</b> Once the compositions differ, three loudly
    /// different paper tones would read as coloured card rather than as three offices' stock.
    /// </para>
    /// </summary>
    public static class OfficeStyles
    {
        /// <summary>An office that leaves the water alone.</summary>
        static readonly Rgba NoWash = new Rgba(0, 0, 0, 0);
        /// <summary>
        /// Warm cream, brown-black drafting ink, ordinary weight. What a render with no office
        /// behind it gets — the island preview, a bench, a test — and deliberately the plainest
        /// of the four, so that a plate rendered without a style looks wrong rather than looking
        /// like somebody's office.
        /// </summary>
        public static readonly OfficeStyle Neutral = new OfficeStyle(
            paper:  Rgba.FromHex("f2ece0"),
            ink:    Rgba.FromHex("2e2318"),
            water:  Rgba.FromHex("2e2318"),
            marks:  Rgba.FromHex("2e2318"),
            grid:   Rgba.FromHex("8899a6"),
            weight: 1.0,
            wash:   NoWash,
            contourStride: 2);

        /// <summary>
        /// <b>Hydrographic</b> — a sea chart. Cool blue-grey paper and blue-black ink, because
        /// the sea is the subject and everything else is the edge of it. Water is drawn in a
        /// distinctly lighter blue: this office's plates are soundings and shoreline, and the
        /// soundings must read as a wash of small marks rather than as text.
        ///
        /// <para>Light overall weight — an admiralty chart is a fine, crowded drawing.</para>
        /// </summary>
        public static readonly OfficeStyle Hydrographic = new OfficeStyle(
            paper:  Rgba.FromHex("e4e9ec"),
            ink:    Rgba.FromHex("1d3348"),
            water:  Rgba.FromHex("4d7c99"),
            marks:  Rgba.FromHex("1d3348"),
            grid:   Rgba.FromHex("8899a6"),
            weight: 0.85,
            wash:   Rgba.FromHex("c2d4de"),   // the sea, and the whole of its signal
            contourStride: 0);                // no contours: it does not survey the land

        /// <summary>
        /// <b>Land Survey</b> — terrain on warm cream, in brown-black. The only office that draws
        /// contours, and it draws the most of anything: four levels, rivers, settlements and
        /// peaks. Rivers take a muted blue so the one wet line on a dry sheet is findable.
        ///
        /// <para>Ordinary weight. This is the office the eye should read as "a map".</para>
        /// </summary>
        public static readonly OfficeStyle LandSurvey = new OfficeStyle(
            paper:  Rgba.FromHex("f4eddc"),
            ink:    Rgba.FromHex("3a2c1c"),
            water:  Rgba.FromHex("5b7f96"),
            marks:  Rgba.FromHex("2b1f12"),
            grid:   Rgba.FromHex("8899a6"),
            weight: 1.0,
            wash:   NoWash,      // the sea is blank: it is the half this office omits
            contourStride: 1);   // every band edge — relief as a hatching, not three lines

        /// <summary>
        /// <b>Garrison</b> — buff paper, hard black ink, heavy. Its plates are a coastline, a few
        /// spot heights and its grid, and A5b measured that more than half of them are nothing
        /// but the coastline and the grid — so this is the office whose look has to survive
        /// having almost nothing on the sheet.
        ///
        /// <para>The grid is the loudest thing here and is meant to be: it is printed <i>over</i>
        /// the map as a reference, in a red-brown that no other office uses at all, so a Garrison
        /// plate is identifiable at pile distance by its colour before any line is resolved.</para>
        /// </summary>
        public static readonly OfficeStyle Garrison = new OfficeStyle(
            paper:  Rgba.FromHex("ece5d2"),
            ink:    Rgba.FromHex("1c1a17"),
            water:  Rgba.FromHex("1c1a17"),
            marks:  Rgba.FromHex("1c1a17"),
            grid:   Rgba.FromHex("9c5a44"),
            weight: 1.25,
            wash:   NoWash,
            contourStride: 0);   // the grid is its texture; contours would fight it

        /// <summary>
        /// <b>Antiquarian</b> — ivory and sepia, light. Its sheets are small square studies of one
        /// curiosity rather than survey work, and they should look like a different kind of
        /// object, not a fourth survey (P2.1).
        /// </summary>
        public static readonly OfficeStyle Antiquarian = new OfficeStyle(
            paper:  Rgba.FromHex("f5efe2"),
            ink:    Rgba.FromHex("5a4632"),
            water:  Rgba.FromHex("7d8f92"),
            marks:  Rgba.FromHex("5a4632"),
            grid:   Rgba.FromHex("8899a6"),
            weight: 0.9,
            wash:   NoWash,
            contourStride: 2);

        /// <summary>
        /// One office's style. A <c>switch</c> over the member and never an array indexed by
        /// <c>(int)office</c>: <c>Office</c> is append-only precisely because ordinals are
        /// load-bearing elsewhere, and a table indexed by one is a silent wrong answer the day a
        /// member is added. The default is <see cref="Neutral"/> for the same reason.
        /// </summary>
        public static OfficeStyle For(Office office)
        {
            switch (office)
            {
                case Office.Hydrographic: return Hydrographic;
                case Office.LandSurvey:   return LandSurvey;
                case Office.Garrison:     return Garrison;
                case Office.Antiquarian:  return Antiquarian;
                default:                  return Neutral;
            }
        }

        /// <summary>
        /// The style a sheet is drawn in — its office's, chart or quarter alike.
        ///
        /// <para>The chart takes its maker's style rather than a neutral one, because Q4.4 makes
        /// it that office's work: an island whose chart came from the Garrison has a buff base
        /// with a hard black outline, and one charted by the Hydrographic has a blue-grey one.
        /// The base under a board is therefore already saying who drew it.</para>
        /// </summary>
        public static OfficeStyle For(Sheet sheet) { return For(sheet.Survey.Office); }

        /// <summary>
        /// A twelve-entry palette that is a flat wash, not a relief: every sea band the office's
        /// water tone, every land band its paper.
        ///
        /// <para><b>This is why Q2.2 is amended rather than broken.</b> That rule turned
        /// <c>Fill</c> off because F-S1.7 measured the renderer producing a colour relief map —
        /// greens and browns and banded water — where the mockups show ink on paper. A two-tone
        /// wash is not that: land is exactly the paper it would have been, and the sea is one
        /// flat tone. What Q2.2 forbids is <b>relief banding</b>, and this has none.</para>
        ///
        /// <para>Two sea tones rather than one, the deeper slightly stronger, because a chart
        /// that says nothing at all about depth is the one thing a Hydrographic sheet may not
        /// do. It is a hint, not a bathymetric scale.</para>
        ///
        /// <para><b>It also makes the office cheaper.</b> With <c>Fill</c> on, <c>FieldCoast</c>
        /// draws the coastline free from the fill's own samples and the vector extraction is
        /// skipped entirely (F-R13.1).</para>
        /// </summary>
        public static Rgba[] WashPalette(OfficeStyle style)
        {
            var palette = new Rgba[Bands.Count];

            Rgba deep = style.Wash.Scaled(0.88);
            for (int i = 0; i < Bands.SeaBandCount; i++)
                palette[i] = i < 2 ? deep : style.Wash;

            for (int i = Bands.SeaBandCount; i < Bands.Count; i++) palette[i] = style.Paper;
            return palette;
        }
    }
}
