using System;
using Archivist.Generation;
using Archivist.Generation.Geometry;
using Archivist.Generation.Sheets;

namespace Archivist.Render
{
    [Flags]
    public enum LayerMask
    {
        None = 0,
        Fill = 1, Coast = 2, Rivers = 4, Settlements = 8, Peaks = 16, Soundings = 32,
        All = Fill | Coast | Rivers | Settlements | Peaks | Soundings
    }

    /// <summary>§3. One entry point covers both the island overview and a sheet.</summary>
    public readonly struct RenderRequest
    {
        public readonly Rect2 Area;                // ground-space rect
        public readonly double RotationDeg;        // already 0.1-quantised by the generator
        public readonly double PixelsPerMetre;
        public readonly double PixelsPerPaperMm;   // stroke widths only (§7)
        public readonly LayerMask Layers;

        public RenderRequest(Rect2 area, double rotationDeg, double pixelsPerMetre,
                             double pixelsPerPaperMm, LayerMask layers)
        {
            Area = area; RotationDeg = rotationDeg; PixelsPerMetre = pixelsPerMetre;
            PixelsPerPaperMm = pixelsPerPaperMm; Layers = layers;
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
        /// One sheet. PixelsPerMetre derives from paper detail, so every office gets the
        /// same sharpness IN HAND regardless of its scale (§3).
        /// </summary>
        public static RenderRequest ForSheet(Sheet sheet, double pixelsPerPaperMm,
                                             LayerMask layers = LayerMask.All)
        {
            SurveySpec spec = sheet.Survey;
            double pxPerMetre = pixelsPerPaperMm * 1000.0 / spec.Scale.Denominator;
            // The frame rect is axis-aligned in FRAME space and GroundImage rotates it about
            // the ground origin, so its POSITION must survive. Normalising it to (0,0,W,H)
            // renders a correctly-sized, correctly-rotated rectangle of the WRONG GROUND.
            Rect2 frame = sheet.FrameRect;
            return new RenderRequest(frame, spec.RotationDeg, pxPerMetre, pixelsPerPaperMm, layers);
        }
    }
}
