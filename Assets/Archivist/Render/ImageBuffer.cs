using System;
using Archivist.Generation.Determinism;

namespace Archivist.Render
{
    /// <summary>
    /// RGBA32, row-major, TOP-LEFT origin — exactly what Texture2D.LoadRawTextureData
    /// consumes, so the in-game path is a copy with no decode (T3.1, T3.4).
    /// </summary>
    public sealed class ImageBuffer
    {
        public ImageBuffer(int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("width and height must be positive");
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public byte[] Pixels { get; private set; }

        public void SetPixel(int x, int y, Rgba c)
        {
            int i = (y * Width + x) * 4;
            Pixels[i] = c.R; Pixels[i + 1] = c.G; Pixels[i + 2] = c.B; Pixels[i + 3] = c.A;
        }

        public Rgba GetPixel(int x, int y)
        {
            int i = (y * Width + x) * 4;
            return new Rgba(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
        }

        public bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }

        public void Fill(Rgba c)
        {
            for (int y = 0; y < Height; y++) { for (int x = 0; x < Width; x++) { SetPixel(x, y, c); } }
        }

        /// <summary>FNV-1a over the raw bytes. B2 compares this (§11).</summary>
        public ulong ContentHash()
        {
            ulong h = Hash.FnvOffset;
            for (int i = 0; i < Pixels.Length; i++) { h ^= Pixels[i]; h *= Hash.FnvPrime; }
            return Hash.Mix(h, (ulong)((Width << 16) ^ Height));
        }
    }
}
