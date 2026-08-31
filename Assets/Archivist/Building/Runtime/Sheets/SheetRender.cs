using Archivist.Building.Collection;
using Archivist.Generation.Sheets;
using Archivist.Render;

namespace Archivist.Building.Sheets
{
    /// <summary>
    /// One rendered sheet, crossing from the worker thread to the main thread. Holds no
    /// engine types, so everything in it can be produced off-thread; the
    /// <see cref="ImageBuffer"/> becomes a <c>Texture2D</c> only once it lands.
    /// </summary>
    public sealed class SheetRender
    {
        public SheetRender(SheetId id, Sheet sheet, string islandName, ImageBuffer image,
                           double pixelsPerPaperMm)
        {
            Id = id;
            Sheet = sheet;
            IslandName = islandName;
            Image = image;
            PixelsPerPaperMm = pixelsPerPaperMm;
        }

        public SheetId Id { get; private set; }
        public Sheet Sheet { get; private set; }
        public string IslandName { get; private set; }
        public ImageBuffer Image { get; private set; }

        /// <summary>
        /// The resolution the map was drawn at, in pixels per millimetre of paper.
        ///
        /// <para>Carried rather than recovered. A quarter plate covers only part of its sheet
        /// (Q1.1), so <c>Image.Width / Format.MapWidthMm</c> is not the resolution — it is the
        /// resolution times the fraction of the sheet the map occupies, and using it would print
        /// every plate edge to edge whatever it was of.</para>
        /// </summary>
        public double PixelsPerPaperMm { get; private set; }
    }
}
