using System.Text;
using SpaceAnalizer.UI;

namespace SpaceAnalizer.Render;

/// <summary>
/// Draws text with the embedded Inter font: glyph outlines are rasterized once per size (and quarter-pixel
/// horizontal position) and cached as coverage masks. Kerning is applied; the x-height is snapped to the
/// pixel grid ("light" vertical hinting) so small text stays crisp.
/// </summary>
public sealed class TextEngine
{
    public static readonly TextEngine Shared = new();

    public sealed class GlyphImage
    {
        public int Left, Top, Width, Height;
        public byte[] Coverage = [];
    }

    static readonly GlyphImage s_empty = new();
    readonly SaFont?[] _fonts = new SaFont?[4];
    readonly Dictionary<long, GlyphImage> _cache = new(4096);
    readonly Rasterizer _raster = new();
    readonly byte[] _gamma = new byte[256];
    readonly object _gate = new();

    TextEngine()
    {
        // Slightly boost partial coverage: unhinted outlines otherwise look thin and grey at UI sizes.
        for (int i = 0; i < 256; i++) _gamma[i] = (byte)Math.Round(255 * Math.Pow(i / 255.0, 0.8));
    }

    public SaFont Font(Weight weight) => _fonts[(int)weight] ??= SaFont.LoadEmbedded(weight switch
    {
        Weight.Medium => "Inter-Medium.saf",
        Weight.Semibold => "Inter-SemiBold.saf",
        Weight.Bold => "Inter-Bold.saf",
        _ => "Inter-Regular.saf",
    });

    public float Ascent(FontSpec f)
    {
        var font = Font(f.Weight);
        return font.Ascender * f.Size / font.UnitsPerEm;
    }

    public float Descent(FontSpec f)
    {
        var font = Font(f.Weight);
        return -font.Descender * f.Size / font.UnitsPerEm;
    }

    /// <summary>Advance of every rune (including kerning with the previous one), in pixels.</summary>
    public float[] Advances(ReadOnlySpan<Rune> runes, FontSpec f)
    {
        var font = Font(f.Weight);
        float scale = f.Size / font.UnitsPerEm;
        var result = new float[runes.Length];
        int prev = -1;
        for (int i = 0; i < runes.Length; i++)
        {
            int g = font.GlyphIndex(runes[i].Value);
            float adv = font[g].Advance;
            if (prev >= 0) adv += font.Kerning(prev, g);
            result[i] = adv * scale;
            prev = g;
        }
        return result;
    }

    public float Measure(string text, FontSpec f)
    {
        var font = Font(f.Weight);
        float scale = f.Size / font.UnitsPerEm;
        float w = 0;
        int prev = -1;
        foreach (var r in text.EnumerateRunes())
        {
            int g = font.GlyphIndex(r.Value);
            w += font[g].Advance;
            if (prev >= 0) w += font.Kerning(prev, g);
            prev = g;
        }
        return w * scale;
    }

    public void Draw(Surface s, int clipX0, int clipY0, int clipX1, int clipY1, string text, float x, float baseline, FontSpec f, uint color)
    {
        var font = Font(f.Weight);
        float scale = f.Size / font.UnitsPerEm;
        int sizeQ = (int)MathF.Round(f.Size * 4);
        int by = (int)MathF.Round(baseline);
        float pen = x;
        int prev = -1;
        foreach (var r in text.EnumerateRunes())
        {
            int g = font.GlyphIndex(r.Value);
            if (prev >= 0) pen += font.Kerning(prev, g) * scale;
            float fx = MathF.Floor(pen);
            int sub = (int)((pen - fx) * 4) & 3;
            var img = Glyph(f.Weight, font, g, sizeQ, sub);
            if (img.Width > 0)
                Blit(s, clipX0, clipY0, clipX1, clipY1, img, (int)fx + img.Left, by + img.Top, color);
            pen += font[g].Advance * scale;
            prev = g;
        }
    }

    GlyphImage Glyph(Weight weight, SaFont font, int g, int sizeQ, int sub)
    {
        long key = ((long)weight << 40) | ((long)sizeQ << 20) | ((long)g << 2) | (long)sub;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var img)) return img;
            img = Rasterize(font, g, sizeQ / 4f, sub);
            if (_cache.Count > 20000) _cache.Clear();
            _cache[key] = img;
            return img;
        }
    }

    GlyphImage Rasterize(SaFont font, int index, float size, int sub)
    {
        var glyph = font[index];
        if (glyph.X.Length == 0) return s_empty;
        float scale = size / font.UnitsPerEm;
        float yScale = scale;
        float xh = font.XHeight * scale;
        if (xh >= 4 && xh <= 48) yScale = scale * MathF.Round(xh) / xh;
        float ox = sub / 4f;

        float minX = glyph.XMin * scale + ox, maxX = glyph.XMax * scale + ox;
        float minY = -glyph.YMax * yScale, maxY = -glyph.YMin * yScale;
        int left = (int)MathF.Floor(minX), top = (int)MathF.Floor(minY);
        int w = (int)MathF.Ceiling(maxX) - left + 2;
        int h = (int)MathF.Ceiling(maxY) - top + 1;
        if (w <= 0 || h <= 0 || w > 4096 || h > 4096) return s_empty;

        _raster.Reset(w, h);
        float dx = ox - left, dy = -top;
        int start = 0;
        foreach (int end in glyph.ContourEnds)
        {
            Contour(glyph, start, end, scale, yScale, dx, dy);
            start = end + 1;
        }
        var coverage = new byte[w * h];
        _raster.Resolve(coverage, _gamma);
        return new GlyphImage { Left = left, Top = top, Width = w, Height = h, Coverage = coverage };
    }

    /// <summary>One TrueType contour: on-curve points joined by lines, off-curve points are quadratic controls.</summary>
    void Contour(SaFont.Glyph g, int s, int e, float scale, float yScale, float dx, float dy)
    {
        int n = e - s + 1;
        if (n < 2) return;
        float X(int i) => g.X[i] * scale + dx;
        float Y(int i) => -g.Y[i] * yScale + dy;

        // Start on an on-curve point (or on the implied midpoint when there is none at the ends).
        float sx, sy;
        int from, count;
        if (g.OnCurve[s]) { sx = X(s); sy = Y(s); from = s + 1; count = n - 1; }
        else if (g.OnCurve[e]) { sx = X(e); sy = Y(e); from = s; count = n - 1; }
        else { sx = (X(s) + X(e)) / 2; sy = (Y(s) + Y(e)) / 2; from = s; count = n; }

        float cx = sx, cy = sy, qx = 0, qy = 0;
        bool control = false;
        for (int k = 0; k < count; k++)
        {
            int i = from + k;
            if (i > e) i -= n;
            float px = X(i), py = Y(i);
            if (g.OnCurve[i])
            {
                if (control) _raster.Quad(cx, cy, qx, qy, px, py);
                else _raster.Line(cx, cy, px, py);
                control = false;
                cx = px;
                cy = py;
            }
            else
            {
                if (control)
                {
                    float mx = (qx + px) / 2, my = (qy + py) / 2;
                    _raster.Quad(cx, cy, qx, qy, mx, my);
                    cx = mx;
                    cy = my;
                }
                qx = px;
                qy = py;
                control = true;
            }
        }
        if (control) _raster.Quad(cx, cy, qx, qy, sx, sy);
        else _raster.Line(cx, cy, sx, sy);
    }

    static void Blit(Surface s, int cx0, int cy0, int cx1, int cy1, GlyphImage g, int x, int y, uint color)
    {
        int x0 = Math.Max(x, cx0), y0 = Math.Max(y, cy0);
        int x1 = Math.Min(x + g.Width, cx1), y1 = Math.Min(y + g.Height, cy1);
        var cov = g.Coverage;
        var px = s.Pixels;
        for (int yy = y0; yy < y1; yy++)
        {
            int src = (yy - y) * g.Width - x;
            int dst = yy * s.Width;
            for (int xx = x0; xx < x1; xx++)
            {
                byte c = cov[src + xx];
                if (c == 0) continue;
                ref uint d = ref px[dst + xx];
                d = Px.Over(d, Px.Scale(color, c));
            }
        }
    }
}
