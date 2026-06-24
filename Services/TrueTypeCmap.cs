using System;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal TrueType/OpenType 'cmap' reader: does the font map a codepoint to a real
    /// (non-zero) glyph? Supports subtable formats 4 (BMP), 6 (trimmed), and 12 (full).
    /// Handles .ttc via faceIndex. Pure and defensive: returns false on malformed input.
    /// </summary>
    public static class TrueTypeCmap
    {
        public static bool CoversCodepoint(byte[] data, int codepoint, int faceIndex = 0)
        {
            try
            {
                int baseOffset = 0;
                if (data.Length >= 16 && data[0] == (byte)'t' && data[1] == (byte)'t' &&
                    data[2] == (byte)'c' && data[3] == (byte)'f')
                {
                    uint numFonts = ReadU32(data, 8);
                    if (faceIndex < 0 || faceIndex >= numFonts) faceIndex = 0;
                    baseOffset = (int)ReadU32(data, 12 + faceIndex * 4);
                }

                int cmap = FindTable(data, baseOffset);
                if (cmap < 0) return false;

                ushort numTables = ReadU16(data, cmap + 2);
                int best = -1, bestScore = -1;
                for (int i = 0; i < numTables; i++)
                {
                    int rec = cmap + 4 + i * 8;
                    ushort plat = ReadU16(data, rec);
                    ushort enc = ReadU16(data, rec + 2);
                    uint sub = ReadU32(data, rec + 4);
                    int score = Score(plat, enc);
                    if (score > bestScore) { bestScore = score; best = cmap + (int)sub; }
                }
                if (best < 0) return false;

                ushort format = ReadU16(data, best);
                return format switch
                {
                    4 => CoversFormat4(data, best, codepoint),
                    6 => CoversFormat6(data, best, codepoint),
                    12 => CoversFormat12(data, best, codepoint),
                    _ => false
                };
            }
            catch { return false; }
        }

        private static int Score(ushort plat, ushort enc)
        {
            if (plat == 3 && enc == 10) return 5; // Windows UCS-4
            if (plat == 3 && enc == 1) return 4;  // Windows BMP
            if (plat == 0) return 3;              // Unicode
            if (plat == 3 && enc == 0) return 1;  // Symbol
            return 0;
        }

        private static int FindTable(byte[] d, int baseOffset)
        {
            ushort numTables = ReadU16(d, baseOffset + 4);
            int dir = baseOffset + 12;
            for (int i = 0; i < numTables; i++)
            {
                int rec = dir + i * 16;
                if (d[rec] == (byte)'c' && d[rec + 1] == (byte)'m' &&
                    d[rec + 2] == (byte)'a' && d[rec + 3] == (byte)'p')
                    return (int)ReadU32(d, rec + 8);
            }
            return -1;
        }

        private static bool CoversFormat4(byte[] d, int off, int cp)
        {
            if (cp > 0xFFFF) return false;
            ushort segX2 = ReadU16(d, off + 6);
            int endCodes = off + 14;
            int startCodes = endCodes + segX2 + 2; // +2 reservedPad
            int idDeltas = startCodes + segX2;
            int idRangeOffsets = idDeltas + segX2;
            int segCount = segX2 / 2;
            for (int i = 0; i < segCount; i++)
            {
                ushort end = ReadU16(d, endCodes + i * 2);
                if (cp <= end)
                {
                    ushort start = ReadU16(d, startCodes + i * 2);
                    if (cp < start) return false;
                    short idDelta = (short)ReadU16(d, idDeltas + i * 2);
                    ushort idRangeOffset = ReadU16(d, idRangeOffsets + i * 2);
                    int glyph;
                    if (idRangeOffset == 0) glyph = (cp + idDelta) & 0xFFFF;
                    else
                    {
                        int gi = idRangeOffsets + i * 2 + idRangeOffset + (cp - start) * 2;
                        if (gi < 0 || gi + 1 >= d.Length) return false;
                        ushort g = ReadU16(d, gi);
                        if (g == 0) return false;
                        glyph = (g + idDelta) & 0xFFFF;
                    }
                    return glyph != 0;
                }
            }
            return false;
        }

        private static bool CoversFormat6(byte[] d, int off, int cp)
        {
            ushort first = ReadU16(d, off + 6);
            ushort count = ReadU16(d, off + 8);
            if (cp < first || cp >= first + count) return false;
            return ReadU16(d, off + 10 + (cp - first) * 2) != 0;
        }

        private static bool CoversFormat12(byte[] d, int off, int cp)
        {
            uint nGroups = ReadU32(d, off + 12);
            int g = off + 16;
            for (uint i = 0; i < nGroups; i++)
            {
                uint start = ReadU32(d, g);
                uint end = ReadU32(d, g + 4);
                uint startGid = ReadU32(d, g + 8);
                if ((uint)cp >= start && (uint)cp <= end)
                    return startGid + ((uint)cp - start) != 0;
                g += 12;
            }
            return false;
        }

        private static ushort ReadU16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);
        private static uint ReadU32(byte[] d, int o) =>
            (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
    }
}
