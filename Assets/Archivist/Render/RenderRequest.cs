using System;
using Archivist.Generation;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Render
{
    /// <summary>
    /// What a render draws. One bit per <c>FeatureClass</c> the raster path knows how to put on
    /// paper, plus <see cref="Fill"/>, which is not a feature but a way of colouring the ground.
    ///
    /// <para><b>Append only.</b> These are bit positions and a render request is a value a
    /// caller may hold; renumbering one silently redraws every sheet in the game as something
    /// else.</para>
    ///
    /// <para><see cref="All"/> is a convenience for a debug view and <b>not</b> what a plate
    /// asks for: an office draws its own subset (Q2.1), and <see cref="Fill"/> is off on every
    /// plate (Q2.2). See <c>OfficeLayers</c>.</para>
    /// </summary>
    [Flags]
    public enum LayerMask
    {
        None = 0,
        Fill = 1, Coast = 2, Rivers = 4, Settlements = 8, Peaks = 16, Soundings = 32,
        Contours = 64, Grid = 128,
        All = Fill | Coast | Rivers | Settlements | Peaks | Soundings | Contours | Grid
    }

    /// <summary>§3. One entry point covers both the island overview and a sheet.</summary>
    public readonly struct RenderRequest
    {
        public readonly Rect2 Area;                // ground-space rect
        public readonly double RotationDeg;        // already 0.1-quantised by the generator
        public readonly double PixelsPerMetre;
        public readonly double PixelsPerPaperMm;   // stroke widths only (§7)
        public readonly LayerMask Layers;

        /// <summary>
        /// The map scale this was cut at, or 0 when nobody said. Needed by exactly one layer:
        /// the Garrison grid's pitch is a paper measurement (<c>Tuning.GridPitchPaperMm</c>)
        /// times the scale, so a renderer that does not know the scale cannot space the lines.
        ///
        /// <para>Carried rather than derived. It <i>is</i> recoverable —
        /// <c>PixelsPerPaperMm * 1000 / PixelsPerMetre</c> — but that is a division whose result
        /// has to be rounded back to an integer denominator, and a grid drawn at 1:9999 would be
        /// a bug nobody could see.</para>
        /// </summary>
        public readonly int ScaleDenominator;

        public RenderRequest(Rect2 area, double rotationDeg, double pixelsPerMetre,
                             double pixelsPerPaperMm, LayerMask layers, int scaleDenominator = 0)
        {
            Area = area; RotationDeg = rotationDeg; PixelsPerMetre = pixelsPerMetre;
            PixelsPerPaperMm = pixelsPerPaperMm; Layers = layers;
            ScaleDenominator = scaleDenominator;
        }

        /// <summary>
        /// The same request drawing a different set of layers. <b>Use this rather than rebuilding
        /// field by field.</b>
        ///
        /// <para><c>IslandRenderer</c> needs one bit cleared before it hands the request to
        /// <c>Strokes</c>. Naming every field into a new struct instead silently drops whichever
        /// field was added last — that is F-R13.2, where <see cref="ScaleDenominator"/> went
        /// missing and the Garrison grid drew nothing on every plate in the game. A copy that
        /// changes one thing cannot forget the others, and stays correct as fields are
        /// added.</para>
        /// </summary>
        public RenderRequest WithLayers(LayerMask layers)
        {
            return new RenderRequest(Area, RotationDeg, PixelsPerMetre, PixelsPerPaperMm,
                                     layers, ScaleDenominator);
        }

        /// <summary>round, not ceil, and computed once — dimensions never depend on accumulated error (§4).</summary>
        public int Width  { get { return Math.Max(1, (int)Math.Floor(Area.Width  * PixelsPerMetre + 0.5)); } }
        public int Height { get { return Math.Max(1, (int)Math.Floor(Area.Height * PixelsPerMetre + 0.5)); } }

        /// <summary>Whole island, north-up, at a caller-chosen ground resolution.</summary>
        public static RenderRequest ForIsland(Island island, double pixelsPerMetre,
                                              LayerMask layers = LayerMask.All)
        {
            return new RenderRequest(island.LandBounds, 0.0, pixelsPerMetre,
                                     RenderTuning.SheetPxPerPaperMm, layers);
        }

        /// <summary>
        /// One sheet at a target GROUND resolution, with the paper resolution derived to match.
        ///
        /// <para><b>For the board, where paper detail is the wrong question.</b>
        /// <see cref="ForSheet"/> fixes pixels per paper millimetre, so a sheet at a coarser
        /// scale gets proportionally fewer pixels per metre — correct in the hand, where two
        /// sheets are the same size of paper whatever they are of. On a board every plate is
        /// laid at its GROUND size, so a chart at 1:25000 and a quarter at 1:10000 are shown at
        /// the same metres-per-screen-pixel and the chart came out two and a half times
        /// softer.</para>
        ///
        /// <para>Measured before this existed: at 0.6 px per paper mm a chart gave 0.024 px/m,
        /// so an island 6912 m across was <b>166 pixels of texture stretched over most of the
        /// screen</b>. The board needs about 0.24 px/m to fill a 1920-wide view.</para>
        ///
        /// <para>The paper resolution is derived rather than dropped, because stroke widths are
        /// in paper millimetres (§7) and would otherwise be drawn at the wrong weight.</para>
        /// </summary>
        public static RenderRequest ForSheetAtGroundResolution(Sheet sheet, double pixelsPerMetre,
                                                               LayerMask layers = LayerMask.All)
        {
            SurveySpec spec = sheet.Survey;
            double pxPerPaperMm = pixelsPerMetre * spec.Scale.Denominator / Tuning.MmPerMetre;

            return new RenderRequest(sheet.FrameRect, sheet.RotationDeg, pixelsPerMetre,
                                     pxPerPaperMm, layers, spec.Scale.Denominator);
        }

        /// <summary>
        /// One sheet. PixelsPerMetre derives from paper detail, so every office gets the
        /// same sharpness IN HAND regardless of its scale (§3).
        /// </summary>
        public static RenderRequest ForSheet(Sheet sheet, double pixelsPerPaperMm,
                                             LayerMask layers = LayerMask.All)
        {
            SurveySpec spec = sheet.Survey;
            double pxPerMetre = pixelsPerPaperMm * Tuning.MmPerMetre / spec.Scale.Denominator;
            // The frame rect is axis-aligned in FRAME space and GroundImage rotates it about
            // the ground origin, so its POSITION must survive. Normalising it to (0,0,W,H)
            // renders a correctly-sized, correctly-rotated rectangle of the WRONG GROUND.
            Rect2 frame = sheet.FrameRect;
            // sheet.RotationDeg, NOT spec.RotationDeg. The survey's rotation is nominal: only
            // detail sheets are rotated at all (Q1.2) and the per-sheet value governs. FrameRect
            // is already computed from it, so taking the survey's here would rotate the sampling
            // frame away from the rect it is meant to fill.
            return new RenderRequest(frame, sheet.RotationDeg, pxPerMetre, pixelsPerPaperMm,
                                     layers, spec.Scale.Denominator);
        }
    }
}
