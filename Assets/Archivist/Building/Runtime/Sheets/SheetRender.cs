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
        public SheetRender(SheetId id, Sheet sheet, string islandName, ImageBuffer image)
        {
            Id = id;
            Sheet = sheet;
            IslandName = islandName;
            Image = image;
        }

        public SheetId Id { get; private set; }
        public Sheet Sheet { get; private set; }
        public string IslandName { get; private set; }
        public ImageBuffer Image { get; private set; }
    }
}
