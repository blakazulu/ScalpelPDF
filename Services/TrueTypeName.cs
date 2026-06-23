using System;
using System.Text;

namespace Scalpel.Services
{
    /// <summary>
    /// Minimal TrueType/OpenType 'name'-table reader. Extracts family (name ID 1, or
    /// typographic family ID 16) and subfamily (ID 2 / ID 17) from font bytes.
    /// Handles .ttc collections via faceIndex. Pure and defensive: returns ("","")
    /// on any malformed input.
    /// </summary>
    public static class TrueTypeName
    {
        public readonly record struct Names(string Family, string Subfamily);

        public static Names Read(byte[] data, int faceIndex = 0)
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

                ushort numTables = ReadU16(data, baseOffset + 4);
                int dir = baseOffset + 12;
                int nameOffset = -1;
                for (int i = 0; i < numTables; i++)
                {
                    int rec = dir + i * 16;
                    if (data[rec] == (byte)'n' && data[rec + 1] == (byte)'a' &&
                        data[rec + 2] == (byte)'m' && data[rec + 3] == (byte)'e')
                    {
                        nameOffset = (int)ReadU32(data, rec + 8);
                        break;
                    }
                }
                if (nameOffset < 0) return new Names("", "");

                ushort count = ReadU16(data, nameOffset + 2);
                ushort stringOffset = ReadU16(data, nameOffset + 4);
                int storage = nameOffset + stringOffset;

                string family = "", subfamily = "", typoFamily = "", typoSub = "";
                for (int i = 0; i < count; i++)
                {
                    int rec = nameOffset + 6 + i * 12;
                    ushort platformId = ReadU16(data, rec);
                    ushort nameId = ReadU16(data, rec + 6);
                    ushort len = ReadU16(data, rec + 8);
                    ushort off = ReadU16(data, rec + 10);
                    string? value = DecodeName(data, storage + off, len, platformId);
                    if (value is null) continue;
                    switch (nameId)
                    {
                        case 1:  if (family == "") family = value; break;
                        case 2:  if (subfamily == "") subfamily = value; break;
                        case 16: if (typoFamily == "") typoFamily = value; break;
                        case 17: if (typoSub == "") typoSub = value; break;
                    }
                }
                return new Names(
                    typoFamily != "" ? typoFamily : family,
                    typoSub != "" ? typoSub : subfamily);
            }
            catch { return new Names("", ""); }
        }

        private static string? DecodeName(byte[] d, int offset, int len, ushort platformId)
        {
            if (offset < 0 || len <= 0 || offset + len > d.Length) return null;
            if (platformId == 3 || platformId == 0) // Windows / Unicode → UTF-16 BE
                return Encoding.BigEndianUnicode.GetString(d, offset, len);
            if (platformId == 1) // Mac Roman (approx)
                return Encoding.ASCII.GetString(d, offset, len);
            return null;
        }

        private static ushort ReadU16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);
        private static uint ReadU32(byte[] d, int o) =>
            (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);
    }
}
