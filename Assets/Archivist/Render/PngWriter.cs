using System;
using System.Collections.Generic;
using System.IO;

namespace Archivist.Render
{
    /// <summary>
    /// §8 — writes a real, viewer-openable .png from an <see cref="ImageBuffer"/>:
    /// 8-bit RGBA (colour type 6, bit depth 8), non-interlaced, every scanline filtered
    /// with type 0 (None).
    ///
    /// The IDAT payload is a zlib stream whose deflate data is nothing but **stored**
    /// (uncompressed) blocks, so no compressor — and no <c>System.IO.Compression</c> —
    /// is needed. That makes the files large; they are debug artifacts, and §8 asks for
    /// correct rather than fast.
    ///
    /// Endianness is the classic trap here: PNG chunk lengths, CRC-32s and the zlib
    /// Adler-32 are BIG-endian, while deflate's LEN/NLEN pair is LITTLE-endian.
    ///
    /// No engine types and no wall-clock — this compiles inside the noEngineReferences
    /// assembly described in §1.
    /// </summary>
    public static class PngWriter
    {
        // Local consts rather than RenderTuning entries (§10 owns tuning, not file format
        // invariants — these are fixed by the PNG/zlib/deflate specs and are not tunable).

        /// <summary>Largest payload a single stored deflate block can carry (RFC 1951 §3.2.4).</summary>
        const int MaxStoredBlock = 65535;

        /// <summary>Bytes per pixel for colour type 6 at bit depth 8.</summary>
        const int BytesPerPixel = 4;

        /// <summary>CRC-32 polynomial, reflected form (PNG spec, Annex "Sample CRC code").</summary>
        const uint CrcPolynomial = 0xEDB88320u;

        /// <summary>Adler-32 modulus (RFC 1950 §9).</summary>
        const uint AdlerBase = 65521u;

        static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>
        /// §8 — encodes <paramref name="buf"/> and writes it to <paramref name="path"/>,
        /// creating the parent directory if it is missing and overwriting any existing file.
        /// </summary>
        public static void Write(ImageBuffer buf, string path)
        {
            if (buf == null)
            {
                throw new ArgumentNullException("buf");
            }
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("path must be non-empty", "path");
            }

            byte[] png = Encode(buf);

            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(path, png);
        }

        /// <summary>
        /// §8 — the whole .png as bytes, with no file IO, so tests can assert on the
        /// stream directly.
        /// </summary>
        public static byte[] Encode(ImageBuffer buf)
        {
            if (buf == null)
            {
                throw new ArgumentNullException("buf");
            }

            int width = buf.Width;
            int height = buf.Height;
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException("buffer must have positive extents", "buf");
            }

            // Size the stream in long arithmetic first: the raw scanline block is
            // height * (1 + 4 * width), which can overflow an int before the array
            // allocation would fail, and a silently wrapped length writes a corrupt PNG.
            long rawLength = (long)height * (1L + (long)width * BytesPerPixel);
            if (rawLength > int.MaxValue / 2)
            {
                throw new ArgumentException("image is too large to encode as a single PNG", "buf");
            }

            int stride = width * BytesPerPixel;
            if (buf.Pixels == null || buf.Pixels.Length < (long)stride * height)
            {
                throw new ArgumentException("Pixels is smaller than Width * Height * 4", "buf");
            }

            byte[] raw = BuildRawScanlines(buf, height, stride);
            byte[] idat = ZlibStored(raw);

            List<byte> png = new List<byte>(Signature.Length + 12 + 13 + 12 + idat.Length + 12);
            png.AddRange(Signature);
            WriteChunk(png, "IHDR", BuildIhdr(width, height));
            WriteChunk(png, "IDAT", idat);
            WriteChunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        // ---------------------------------------------------------------- scanlines

        /// <summary>
        /// §8 — filter byte 0 (None) in front of each row's RGBA bytes. The buffer is
        /// already top-left origin and row-major (§2), so rows go out in storage order
        /// with no flip.
        /// </summary>
        static byte[] BuildRawScanlines(ImageBuffer buf, int height, int stride)
        {
            byte[] raw = new byte[height * (1 + stride)];
            byte[] pixels = buf.Pixels;
            int dst = 0;
            for (int y = 0; y < height; y++)
            {
                raw[dst] = 0;                                     // filter type 0 = None
                dst++;
                Array.Copy(pixels, y * stride, raw, dst, stride);
                dst += stride;
            }
            return raw;
        }

        // -------------------------------------------------------------------- chunks

        /// <summary>§8 — IHDR data: 8-bit RGBA, no compression/filter/interlace variants.</summary>
        static byte[] BuildIhdr(int width, int height)
        {
            byte[] data = new byte[13];
            PutBigEndian(data, 0, (uint)width);
            PutBigEndian(data, 4, (uint)height);
            data[8] = 8;      // bit depth
            data[9] = 6;      // colour type 6 = truecolour with alpha
            data[10] = 0;     // compression method 0 = deflate
            data[11] = 0;     // filter method 0 = adaptive, five basic filter types
            data[12] = 0;     // interlace method 0 = none
            return data;
        }

        /// <summary>
        /// §8 — one chunk: big-endian length, four-byte type, data, then the CRC-32 of
        /// type + data (the length is NOT covered by the CRC).
        /// </summary>
        static void WriteChunk(List<byte> outp, string type, byte[] data)
        {
            byte[] typeBytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                typeBytes[i] = (byte)type[i];
            }

            AddBigEndian(outp, (uint)data.Length);
            outp.AddRange(typeBytes);
            outp.AddRange(data);

            uint crc = Crc32Start();
            crc = Crc32Update(crc, typeBytes, 0, typeBytes.Length);
            crc = Crc32Update(crc, data, 0, data.Length);
            AddBigEndian(outp, Crc32Finish(crc));
        }

        // ---------------------------------------------------------------------- zlib

        /// <summary>
        /// §8 — a zlib stream (RFC 1950) around stored deflate blocks (RFC 1951 §3.2.4):
        /// two header bytes, the blocks, then the big-endian Adler-32 of the
        /// *uncompressed* data.
        /// </summary>
        static byte[] ZlibStored(byte[] raw)
        {
            int blocks = raw.Length / MaxStoredBlock;
            if (blocks * MaxStoredBlock < raw.Length || blocks == 0)
            {
                blocks++;                                         // partial tail, or one empty block
            }

            // 2 zlib header bytes + 5 bytes of block header each + the literals + 4 Adler bytes.
            byte[] outp = new byte[2 + blocks * 5 + raw.Length + 4];
            int w = 0;

            // CMF: CM = 8 (deflate), CINFO = 7 (32K window) -> 0x78.
            // FLG: FCHECK chosen so (CMF << 8 | FLG) % 31 == 0, FDICT = 0, FLEVEL = 0.
            outp[w] = 0x78;
            w++;
            outp[w] = 0x01;
            w++;

            int offset = 0;
            for (int b = 0; b < blocks; b++)
            {
                int remaining = raw.Length - offset;
                int count = remaining < MaxStoredBlock ? remaining : MaxStoredBlock;
                bool final = b == blocks - 1;
                w = WriteStoredBlock(outp, w, raw, offset, count, final);
                offset += count;
            }

            PutBigEndian(outp, w, Adler32(raw));
            return outp;
        }

        /// <summary>
        /// §8 — one stored block: a byte-aligned header (BFINAL in bit 0, BTYPE 00 in
        /// bits 1-2), then LEN and its one's complement NLEN, both LITTLE-endian, then
        /// the literal bytes. Returns the new write cursor.
        /// </summary>
        static int WriteStoredBlock(byte[] outp, int w, byte[] raw, int offset, int count, bool final)
        {
            outp[w] = final ? (byte)0x01 : (byte)0x00;
            w++;

            ushort len = (ushort)count;
            ushort nlen = (ushort)(~len & 0xFFFF);
            outp[w] = (byte)(len & 0xFF);
            outp[w + 1] = (byte)((len >> 8) & 0xFF);
            outp[w + 2] = (byte)(nlen & 0xFF);
            outp[w + 3] = (byte)((nlen >> 8) & 0xFF);
            w += 4;

            Array.Copy(raw, offset, outp, w, count);
            return w + count;
        }

        // ------------------------------------------------------------------ checksums

        /// <summary>§8 — Adler-32 (RFC 1950 §9) over the uncompressed data.</summary>
        static uint Adler32(byte[] data)
        {
            uint a = 1;
            uint b = 0;
            for (int i = 0; i < data.Length; i++)
            {
                a += data[i];
                if (a >= AdlerBase)
                {
                    a -= AdlerBase;
                }
                b += a;
                if (b >= AdlerBase)
                {
                    b -= AdlerBase;
                }
            }
            return (b << 16) | a;
        }

        /// <summary>§8 — the 256-entry table for the reflected CRC-32 used by PNG.</summary>
        static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? (CrcPolynomial ^ (c >> 1)) : (c >> 1);
                }
                table[n] = c;
            }
            return table;
        }

        static uint Crc32Start()
        {
            return 0xFFFFFFFFu;
        }

        static uint Crc32Update(uint crc, byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                crc = CrcTable[(crc ^ data[offset + i]) & 0xFF] ^ (crc >> 8);
            }
            return crc;
        }

        static uint Crc32Finish(uint crc)
        {
            return crc ^ 0xFFFFFFFFu;
        }

        // ------------------------------------------------------------------- endian

        /// <summary>§8 — PNG's network byte order: most significant byte first.</summary>
        static void AddBigEndian(List<byte> outp, uint value)
        {
            outp.Add((byte)((value >> 24) & 0xFF));
            outp.Add((byte)((value >> 16) & 0xFF));
            outp.Add((byte)((value >> 8) & 0xFF));
            outp.Add((byte)(value & 0xFF));
        }

        static void PutBigEndian(byte[] dst, int offset, uint value)
        {
            dst[offset] = (byte)((value >> 24) & 0xFF);
            dst[offset + 1] = (byte)((value >> 16) & 0xFF);
            dst[offset + 2] = (byte)((value >> 8) & 0xFF);
            dst[offset + 3] = (byte)(value & 0xFF);
        }
    }
}
