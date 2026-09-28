using System.IO.Compression;

namespace SpaceAnalizer.Render;

/// <summary>
/// A font in SpaceAnalizer's compact format (see tools/FontBaker.cs): TrueType quadratic outlines, advances,
/// a character map and pair kerning for the characters the app needs, Deflate-compressed.
/// </summary>
public sealed class SaFont
{
    public int UnitsPerEm, Ascender, Descender, LineGap, XHeight, CapHeight;

    public sealed class Glyph
    {
        public int Advance, KernBase;
        public int[] ContourEnds = [];
        public bool[] OnCurve = [];
        public short[] X = [], Y = [];
        public int XMin, YMin, XMax, YMax;
    }

    Glyph[] _glyphs = [];
    readonly Dictionary<int, int> _cmap = new();
    readonly Dictionary<uint, short> _kern = new();

    public int GlyphCount => _glyphs.Length;
    public Glyph this[int index] => _glyphs[index];

    public static SaFont LoadEmbedded(string name)
    {
        using var stream = typeof(SaFont).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded font " + name);
        return Load(stream);
    }

    public static SaFont Load(Stream compressed)
    {
        using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        deflate.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    static SaFont Parse(byte[] d)
    {
        if (d.Length < 4 || d[0] != 'S' || d[1] != 'A' || d[2] != 'F' || d[3] != '1')
            throw new InvalidDataException("Not an SAF1 font");
        int p = 4;
        long U()
        {
            long v = 0;
            int shift = 0;
            byte b;
            do
            {
                b = d[p++];
                v |= (long)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return v;
        }
        long S()
        {
            long u = U();
            return (u >> 1) ^ -(u & 1);
        }

        var f = new SaFont
        {
            UnitsPerEm = (int)U(),
            Ascender = (int)S(),
            Descender = (int)S(),
            LineGap = (int)S(),
            XHeight = (int)S(),
            CapHeight = (int)S(),
        };
        int glyphCount = (int)U();

        int cmapCount = (int)U();
        int cp = 0;
        for (int i = 0; i < cmapCount; i++)
        {
            cp += (int)U();
            f._cmap[cp] = (int)U();
        }

        f._glyphs = new Glyph[glyphCount];
        for (int g = 0; g < glyphCount; g++)
        {
            var glyph = new Glyph { Advance = (int)U(), KernBase = (int)U() };
            int contours = (int)U();
            glyph.ContourEnds = new int[contours];
            int end = -1;
            for (int c = 0; c < contours; c++)
            {
                end += (int)U();
                glyph.ContourEnds[c] = end;
            }
            int n = contours == 0 ? 0 : end + 1;
            glyph.OnCurve = new bool[n];
            for (int i = 0; i < n; i += 8)
            {
                byte bits = d[p++];
                for (int b = 0; b < 8 && i + b < n; b++) glyph.OnCurve[i + b] = (bits & (1 << b)) != 0;
            }
            glyph.X = new short[n];
            glyph.Y = new short[n];
            int x = 0, y = 0;
            for (int i = 0; i < n; i++) glyph.X[i] = (short)(x += (int)S());
            for (int i = 0; i < n; i++) glyph.Y[i] = (short)(y += (int)S());
            if (n > 0)
            {
                glyph.XMin = glyph.X.Min();
                glyph.XMax = glyph.X.Max();
                glyph.YMin = glyph.Y.Min();
                glyph.YMax = glyph.Y.Max();
            }
            f._glyphs[g] = glyph;
        }

        int kernCount = (int)U();
        int left = 0, right = 0;
        for (int i = 0; i < kernCount; i++)
        {
            int dl = (int)U();
            if (dl != 0) right = 0;
            left += dl;
            right += (int)U();
            f._kern[((uint)left << 16) | (uint)right] = (short)S();
        }
        return f;
    }

    /// <summary>Glyph for a Unicode code point (0 = missing glyph box).</summary>
    public int GlyphIndex(int codepoint) => _cmap.TryGetValue(codepoint, out int g) ? g : 0;

    public int Kerning(int left, int right)
    {
        if (_kern.Count == 0) return 0;
        uint key = ((uint)_glyphs[left].KernBase << 16) | (uint)_glyphs[right].KernBase;
        return _kern.TryGetValue(key, out short v) ? v : 0;
    }
}
