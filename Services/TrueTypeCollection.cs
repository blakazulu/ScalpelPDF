using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Pulls one face out of a TrueType collection (.ttc) as a standalone font. A collection
    /// shares tables between its faces; each face has its own table directory whose offsets point
    /// into the shared file. Copying that face's tables into a fresh file with its own directory
    /// gives an ordinary .ttf that PdfSharpCore can embed. Pure and defensive: null on bad input.
    /// </summary>
    public static class TrueTypeCollection
    {
        public static bool IsCollection(byte[]? data)
            => data is not null && data.Length >= 16
               && data[0] == (byte)'t' && data[1] == (byte)'t' && data[2] == (byte)'c' && data[3] == (byte)'f';

        public static byte[]? ExtractFace(byte[] data, int face)
        {
            try
            {
                if (!IsCollection(data)) return null;
                uint numFonts = U32(data, 8);
                if (face < 0 || face >= numFonts) return null;
                int dir = checked((int)U32(data, 12 + face * 4));
                int numTables = U16(data, dir + 4);
                if (numTables == 0 || numTables > 512) return null;

                int headerLen = 12 + 16 * numTables;
                int total = headerLen;
                for (int t = 0; t < numTables; t++)
                {
                    int rec = dir + 12 + t * 16;
                    int off = checked((int)U32(data, rec + 8));
                    int len = checked((int)U32(data, rec + 12));
                    if (off < 0 || len < 0 || off > data.Length - len) return null;
                    total = checked(total + Pad4(len));
                }

                var output = new byte[total];
                // Offset table: sfnt version and the search fields are copied as they are.
                Buffer.BlockCopy(data, dir, output, 0, 12);
                int cursor = headerLen;
                for (int t = 0; t < numTables; t++)
                {
                    int rec = dir + 12 + t * 16;
                    int outRec = 12 + t * 16;
                    int off = (int)U32(data, rec + 8);
                    int len = (int)U32(data, rec + 12);
                    Buffer.BlockCopy(data, rec, output, outRec, 8);          // tag + checksum
                    W32(output, outRec + 8, (uint)cursor);
                    W32(output, outRec + 12, (uint)len);
                    Buffer.BlockCopy(data, off, output, cursor, len);
                    cursor += Pad4(len);
                }
                return output;
            }
            catch { return null; }
        }

        private static int Pad4(int n) => (n + 3) & ~3;

        private static uint U32(byte[] d, int o)
            => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);

        private static int U16(byte[] d, int o) => d[o] << 8 | d[o + 1];

        private static void W32(byte[] d, int o, uint v)
        {
            d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v;
        }
    }
}
