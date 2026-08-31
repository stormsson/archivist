using System;
using UnityEngine;
using Archivist.Generation.Sheets;
using Archivist.Render;

namespace Archivist.Building.Sheets
{
    /// <summary>
    /// Composites a rendered map onto its paper and uploads the result as one texture.
    ///
    /// <para><b>One surface, on purpose.</b> Drawing the margin as a separate quad under the
    /// map meant two surfaces a fraction of a millimetre apart, which z-fights at exactly the
    /// grazing angle a sheet on the floor is always seen from. Putting the margin in the
    /// texture removes the second surface, and with it the whole class of bug — no offset to
    /// tune, no depth bias, no render-queue ordering.</para>
    ///
    /// <para><b>The runtime's one and only vertical flip.</b> <see cref="ImageBuffer"/> is
    /// RGBA32, row-major, TOP-LEFT origin, because that is what raster consumers and PNG
    /// expect. Unity's <see cref="Texture2D"/> is BOTTOM-LEFT, so raw bytes show the map
    /// upside down — easy to miss on a roughly symmetric island, which is why it happens in
    /// exactly one place. The editor's <c>TexturePane.Upload</c> is the same flip for the
    /// editor assembly; they cannot share code because <c>Archivist.Render</c> is engine-free
    /// by design and neither may pull it toward UnityEngine. The flip is folded into the
    /// composite here, so the pixels are walked once rather than twice.</para>
    /// </summary>
    public static class SheetTexture
    {
        public static Texture2D Compose(ImageBuffer map, SheetFormat format, Color32 paper,
                                        string name, double pixelsPerPaperMm)
        {
            // PASSED IN, not derived. It used to be map.Width / format.MapWidthMm — sound while
            // every map filled its sheet's map area, and wrong the moment one did not. A quarter
            // plate is of its quarter (Q1.1) and covers only part of the paper, so deriving the
            // resolution from its width would report a coarser render than actually happened,
            // shrink the paper to fit, and print the quarter edge to edge with no margin at all.
            if (!(pixelsPerPaperMm > 0.0)) pixelsPerPaperMm = map.Width / format.MapWidthMm;

            int paperW = Math.Max(map.Width, (int)Math.Round(format.WidthMm * pixelsPerPaperMm));
            int paperH = Math.Max(map.Height, (int)Math.Round(format.HeightMm * pixelsPerPaperMm));

            int offsetX = (paperW - map.Width) / 2;
            int offsetY = (paperH - map.Height) / 2;

            int paperStride = paperW * 4;
            var pixels = new byte[paperStride * paperH];

            // One row of paper, then copied down the sheet — cheaper than writing every byte.
            var row = new byte[paperStride];
            for (int x = 0; x < paperW; x++)
            {
                int i = x * 4;
                row[i] = paper.r; row[i + 1] = paper.g; row[i + 2] = paper.b; row[i + 3] = 255;
            }
            for (int y = 0; y < paperH; y++)
                Buffer.BlockCopy(row, 0, pixels, y * paperStride, paperStride);

            // Blit the map in, flipping as we go: source row y lands at the destination row
            // counted from the bottom.
            int mapStride = map.Width * 4;
            for (int y = 0; y < map.Height; y++)
            {
                int destinationRow = paperH - 1 - (offsetY + y);
                Buffer.BlockCopy(map.Pixels, y * mapStride,
                                 pixels, destinationRow * paperStride + offsetX * 4, mapStride);
            }

            var tex = new Texture2D(paperW, paperH, TextureFormat.RGBA32, mipChain: true, linear: false);
            tex.name = name;
            // See SheetMesh.CreateSlab: a sheet's GameObject is outside the serialized graph,
            // so anything only it references is collected on the next reload unless flagged.
            tex.hideFlags = HideFlags.DontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 8;   // a sheet on the floor is never seen face-on

            // SetPixelData, not LoadRawTextureData: with a mip chain the latter expects bytes
            // for every level, and only level 0 exists here.
            tex.SetPixelData(pixels, 0);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return tex;
        }
    }
}
