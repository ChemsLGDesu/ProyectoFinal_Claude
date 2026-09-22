using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// Writes an indexed (colour type 3) PNG: a palette plus one byte per pixel.
    ///
    /// Unity's <see cref="ImageConversion.EncodeToPNG"/> always writes truecolour RGBA - four bytes per
    /// pixel - and there is no way to ask it for anything else. On flat-vector art that is four times more
    /// data than the image actually carries, and quantizing the colours only helps insofar as DEFLATE can
    /// squeeze the redundancy back out. Writing the palette form directly removes the redundancy up front
    /// instead, which is where the real saving is.
    ///
    /// Alpha rides in a tRNS chunk, one byte per palette entry, so the palette is built over (colour, alpha)
    /// pairs rather than colours alone. That is why alpha is quantized here too - but only in the partial
    /// range: fully transparent and fully opaque are preserved exactly, and between them lies less than a
    /// tenth of a percent of the pixels, all of them on antialiased edges.
    /// </summary>
    public static class IndexedPngEncoder
    {
        private const int MaxPaletteEntries = 256;

        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Encodes <paramref name="pixels"/> as an indexed PNG. Returns null when the image cannot fit a 256-entry palette even after reduction, so the caller can fall back to truecolour.</summary>
        public static byte[] Encode(Color32[] pixels, int width, int height, int maxColours, int alphaLevels)
        {
            if (pixels == null || pixels.Length != width * height || width <= 0 || height <= 0)
            {
                return null;
            }

            if (!TryBuildPalette(pixels, maxColours, alphaLevels, out var palette, out var indices))
            {
                return null;
            }

            using var stream = new MemoryStream();
            stream.Write(Signature, 0, Signature.Length);

            var ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, width);
            WriteBigEndian(ihdr, 4, height);
            ihdr[8] = 8;    // bit depth
            ihdr[9] = 3;    // colour type 3 = indexed
            ihdr[10] = 0;   // deflate
            ihdr[11] = 0;   // adaptive filtering
            ihdr[12] = 0;   // no interlace
            WriteChunk(stream, "IHDR", ihdr);

            var plte = new byte[palette.Count * 3];
            var trns = new byte[palette.Count];
            bool needsAlpha = false;
            for (int i = 0; i < palette.Count; i++)
            {
                plte[i * 3] = palette[i].r;
                plte[i * 3 + 1] = palette[i].g;
                plte[i * 3 + 2] = palette[i].b;
                trns[i] = palette[i].a;
                if (palette[i].a != 255) needsAlpha = true;
            }
            WriteChunk(stream, "PLTE", plte);
            if (needsAlpha)
            {
                WriteChunk(stream, "tRNS", trns);
            }

            // Scanlines run top to bottom in a PNG; Unity's pixel array starts at the bottom row.
            // Filter type 0 (None) throughout: filters do byte arithmetic, which is meaningless on palette
            // indices - subtracting one index from another produces noise, not a smaller number.
            var raw = new byte[height * (width + 1)];
            int p = 0;
            for (int y = height - 1; y >= 0; y--)
            {
                raw[p++] = 0;
                Array.Copy(indices, y * width, raw, p, width);
                p += width;
            }

            WriteChunk(stream, "IDAT", ZlibCompress(raw));
            WriteChunk(stream, "IEND", Array.Empty<byte>());
            return stream.ToArray();
        }

        /// <summary>
        /// Builds a palette over (colour, alpha) pairs, shrinking the colour count until the pairs fit 256
        /// entries. Colours are reduced before alpha levels because a lost colour shifts a flat region by a
        /// shade, whereas a lost alpha level chips the silhouette.
        /// </summary>
        private static bool TryBuildPalette(Color32[] pixels, int maxColours, int alphaLevels, out List<Color32> palette, out byte[] indices)
        {
            palette = null;
            indices = null;

            for (int colours = Mathf.Min(maxColours, MaxPaletteEntries); colours >= 4; colours /= 2)
            {
                var quantized = QuantizeRgb(pixels, colours);
                var lookup = new Dictionary<int, byte>();
                var entries = new List<Color32>();
                var result = new byte[pixels.Length];
                bool overflowed = false;

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 source = pixels[i];
                    byte alpha = QuantizeAlpha(source.a, alphaLevels);

                    Color32 rgb = alpha == 0 ? new Color32(0, 0, 0, 0) : quantized[i];
                    int key = alpha == 0 ? 0 : (rgb.r << 24) | (rgb.g << 16) | (rgb.b << 8) | alpha;

                    if (!lookup.TryGetValue(key, out byte index))
                    {
                        if (entries.Count >= MaxPaletteEntries)
                        {
                            overflowed = true;
                            break;
                        }

                        index = (byte)entries.Count;
                        entries.Add(new Color32(rgb.r, rgb.g, rgb.b, alpha));
                        lookup[key] = index;
                    }

                    result[i] = index;
                }

                if (!overflowed)
                {
                    palette = entries;
                    indices = result;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Snaps alpha onto a small ladder, leaving 0 and 255 exact - they are the silhouette itself and every other level is a fraction of a percent of the image.</summary>
        private static byte QuantizeAlpha(byte alpha, int levels)
        {
            if (alpha == 0 || alpha == 255 || levels >= 256)
            {
                return alpha;
            }

            int steps = Mathf.Max(2, levels - 1);
            return (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * steps / 255f) * 255 / steps, 0, 255);
        }

        private static Color32[] QuantizeRgb(Color32[] pixels, int maxColours)
        {
            var result = TextureMinifier.QuantizeToPalette(pixels, maxColours);
            return result ?? pixels;
        }

        private static byte[] ZlibCompress(byte[] data)
        {
            using var output = new MemoryStream();
            output.WriteByte(0x78);   // zlib header: deflate, 32K window
            output.WriteByte(0x9C);   // default compression, no preset dictionary

            // Fully qualified: UnityEngine also defines a CompressionLevel, and the two collide here.
            using (var deflate = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(data, 0, data.Length);
            }

            uint adler = Adler32(data);
            output.WriteByte((byte)(adler >> 24));
            output.WriteByte((byte)(adler >> 16));
            output.WriteByte((byte)(adler >> 8));
            output.WriteByte((byte)adler);
            return output.ToArray();
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            stream.Write(length, 0, 4);

            var payload = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) payload[i] = (byte)type[i];
            Array.Copy(data, 0, payload, 4, data.Length);
            stream.Write(payload, 0, payload.Length);

            var crc = new byte[4];
            WriteBigEndian(crc, 0, unchecked((int)Crc32(payload)));
            stream.Write(crc, 0, 4);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte value in data)
            {
                c = CrcTable[(c ^ value) & 0xFF] ^ (c >> 8);
            }
            return c ^ 0xFFFFFFFFu;
        }
    }
}
