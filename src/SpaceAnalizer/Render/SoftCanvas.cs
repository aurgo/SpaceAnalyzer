using System.Runtime.CompilerServices;
using System.Text;
using SpaceAnalizer.UI;

namespace SpaceAnalizer.Render;

/// <summary>
/// The one and only <see cref="ICanvas"/>: pure C# drawing into a <see cref="Surface"/>, identical on every OS.
/// Shapes are anti-aliased with per-pixel coverage (signed distances for rounded rectangles, arcs and strokes),
/// text uses the embedded font through <see cref="TextEngine"/>.
/// </summary>
public sealed class SoftCanvas : ICanvas
{
    readonly Surface _s;
    readonly uint[] _px;
    readonly int _w;
    readonly TextEngine _text = TextEngine.Shared;
    readonly List<(int, int, int, int)> _clips = [];
    int _cx0, _cy0, _cx1, _cy1;

    public SoftCanvas(Surface surface)
    {
        _s = surface;
        _px = surface.Pixels;
        _w = surface.Width;
        _cx1 = surface.Width;
        _cy1 = surface.Height;
    }

    public Surface Surface => _s;

    // ------------------------------------------------------------------ pixel helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Plot(int x, int y, uint src, float coverage)
    {
        if (coverage <= 0.002f) return;
        uint k = coverage >= 0.998f ? 255u : (uint)(coverage * 255f + 0.5f);
        ref uint d = ref _px[y * _w + x];
        d = Px.Over(d, Px.Scale(src, k));
    }

    void Span(int y, int x0, int x1, uint src, float coverage)
    {
        if (x0 < _cx0) x0 = _cx0;
        if (x1 > _cx1) x1 = _cx1;
        if (x0 >= x1 || y < _cy0 || y >= _cy1 || coverage <= 0.002f) return;
        uint c = coverage >= 0.998f ? src : Px.Scale(src, (uint)(coverage * 255f + 0.5f));
        uint a = c >> 24;
        if (a == 0) return;
        var row = _px.AsSpan(y * _w + x0, x1 - x0);
        if (a == 255)
        {
            row.Fill(c);
            return;
        }
        uint inv = 255 - a;
        for (int i = 0; i < row.Length; i++) row[i] = c + Px.Scale(row[i], inv);
    }

    (int x0, int y0, int x1, int y1) ClipBounds(float x0, float y0, float x1, float y1) => (
        Math.Max((int)MathF.Floor(x0), _cx0),
        Math.Max((int)MathF.Floor(y0), _cy0),
        Math.Min((int)MathF.Ceiling(x1), _cx1),
        Math.Min((int)MathF.Ceiling(y1), _cy1));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

    /// <summary>Signed distance to a rounded box centered at the origin (negative inside).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float SdRoundBox(float px, float py, float bx, float by, float r)
    {
        float qx = MathF.Abs(px) - bx + r, qy = MathF.Abs(py) - by + r;
        float ox = qx > 0 ? qx : 0, oy = qy > 0 ? qy : 0;
        return MathF.Sqrt(ox * ox + oy * oy) + MathF.Min(MathF.Max(qx, qy), 0) - r;
    }

    // ------------------------------------------------------------------ rectangles

    public void FillRect(RectF r, Color c)
    {
        if (c.A == 0 || r.W <= 0 || r.H <= 0) return;
        FillRectCore(r.X, r.Y, r.Right, r.Bottom, Px.Premul(c));
    }

    void FillRectCore(float x0, float y0, float x1, float y1, uint src)
    {
        var (ix0, iy0, ix1, iy1) = ClipBounds(x0, y0, x1, y1);
        if (ix0 >= ix1 || iy0 >= iy1) return;
        for (int y = iy0; y < iy1; y++)
        {
            float cy = MathF.Min(y + 1, y1) - MathF.Max(y, y0);
            if (cy > 0) RowWithEdges(y, x0, x1, ix0, ix1, src, cy);
        }
    }

    /// <summary>One row of an axis-aligned rectangle: partial pixels at fractional edges, a solid span inside.</summary>
    void RowWithEdges(int y, float x0, float x1, int ix0, int ix1, uint src, float cy)
    {
        int fx0 = (int)MathF.Ceiling(x0), fx1 = (int)MathF.Floor(x1);
        if (fx1 > fx0)
        {
            if (fx0 - 1 >= ix0 && x0 < fx0) Plot(fx0 - 1, y, src, (fx0 - x0) * cy);
            Span(y, fx0, fx1, src, cy);
            if (fx1 < ix1 && x1 > fx1) Plot(fx1, y, src, (x1 - fx1) * cy);
        }
        else
        {
            for (int x = ix0; x < ix1; x++)
            {
                float cx = MathF.Min(x + 1, x1) - MathF.Max(x, x0);
                if (cx > 0) Plot(x, y, src, cx * cy);
            }
        }
    }

    public void FillRoundRect(RectF r, float radius, Color c)
    {
        if (c.A == 0 || r.W <= 0 || r.H <= 0) return;
        uint src = Px.Premul(c);
        if (radius < 0.5f) FillRectCore(r.X, r.Y, r.Right, r.Bottom, src);
        else RoundRectCore(r, radius, src, src, false);
    }

    public void FillRoundRectGradient(RectF r, float radius, Color top, Color bottom)
    {
        if (r.W <= 0 || r.H <= 0) return;
        RoundRectCore(r, Math.Max(0, radius), Px.Premul(top), Px.Premul(bottom), true);
    }

    void RoundRectCore(RectF r, float radius, uint top, uint bottom, bool gradient)
    {
        float x0 = r.X, y0 = r.Y, x1 = r.Right, y1 = r.Bottom, h = r.H;
        float rad = MathF.Min(radius, MathF.Min(r.W, r.H) / 2);
        var (ix0, iy0, ix1, iy1) = ClipBounds(x0, y0, x1, y1);
        if (ix0 >= ix1 || iy0 >= iy1) return;
        float cxL = x0 + rad, cxR = x1 - rad, cyT = y0 + rad, cyB = y1 - rad;

        for (int y = iy0; y < iy1; y++)
        {
            float py = y + 0.5f;
            uint col = gradient ? Px.Lerp(top, bottom, (py - y0) / h) : top;
            if (rad < 0.5f || (py >= cyT && py <= cyB))
            {
                float cy = MathF.Min(y + 1, y1) - MathF.Max(y, y0);
                if (cy > 0) RowWithEdges(y, x0, x1, ix0, ix1, col, cy);
                continue;
            }
            float ccy = py < cyT ? cyT : cyB;
            float dy = py - ccy;
            float covY = Clamp01(MathF.Min(py - y0, y1 - py) + 0.5f);
            int midStart = Math.Clamp((int)MathF.Ceiling(cxL - 0.5f), ix0, ix1);
            int midEnd = Math.Clamp((int)MathF.Floor(cxR - 0.5f) + 1, midStart, ix1);
            for (int x = ix0; x < midStart; x++)
            {
                float dx = x + 0.5f - cxL;
                Plot(x, y, col, Clamp01(rad + 0.5f - MathF.Sqrt(dx * dx + dy * dy)));
            }
            Span(y, midStart, midEnd, col, covY);
            for (int x = midEnd; x < ix1; x++)
            {
                float dx = x + 0.5f - cxR;
                Plot(x, y, col, Clamp01(rad + 0.5f - MathF.Sqrt(dx * dx + dy * dy)));
            }
        }
    }

    public void StrokeRoundRect(RectF r, float radius, Color c, float width)
    {
        if (c.A == 0 || width <= 0 || r.W <= 0 || r.H <= 0) return;
        uint src = Px.Premul(c);
        float hw = width / 2;
        float x0 = r.X, y0 = r.Y, x1 = r.Right, y1 = r.Bottom;
        float rad = MathF.Min(Math.Max(0, radius), MathF.Min(r.W, r.H) / 2);
        float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, bx = r.W / 2, by = r.H / 2;
        var (ix0, iy0, ix1, iy1) = ClipBounds(x0 - hw - 1, y0 - hw - 1, x1 + hw + 1, y1 + hw + 1);
        float edge = hw + 1.5f;
        for (int y = iy0; y < iy1; y++)
        {
            float py = y + 0.5f;
            bool middle = py > y0 + rad + edge && py < y1 - rad - edge;
            if (middle)
            {
                int leftEnd = Math.Min(ix1, (int)MathF.Ceiling(x0 + edge));
                int rightStart = Math.Max(ix0, (int)MathF.Floor(x1 - edge));
                for (int x = ix0; x < leftEnd; x++) Ring(x, y);
                for (int x = Math.Max(rightStart, leftEnd); x < ix1; x++) Ring(x, y);
            }
            else
            {
                for (int x = ix0; x < ix1; x++) Ring(x, y);
            }
        }

        void Ring(int x, int y)
        {
            float d = SdRoundBox(x + 0.5f - cx, y + 0.5f - cy, bx, by, rad);
            Plot(x, y, src, Clamp01(hw + 0.5f - MathF.Abs(d)));
        }
    }

    // ------------------------------------------------------------------ lines, circles, arcs, shadows

    public void DrawLine(float x1, float y1, float x2, float y2, Color c, float width)
    {
        if (c.A == 0 || width <= 0) return;
        uint src = Px.Premul(c);
        float hw = width / 2;
        if (x1 == x2)
        {
            FillRectCore(x1 - hw, MathF.Min(y1, y2), x1 + hw, MathF.Max(y1, y2), src);
            return;
        }
        if (y1 == y2)
        {
            FillRectCore(MathF.Min(x1, x2), y1 - hw, MathF.Max(x1, x2), y1 + hw, src);
            return;
        }
        float dx = x2 - x1, dy = y2 - y1, len2 = dx * dx + dy * dy;
        var (_, iy0, _, iy1) = ClipBounds(MathF.Min(x1, x2) - hw - 1, MathF.Min(y1, y2) - hw - 1, MathF.Max(x1, x2) + hw + 1, MathF.Max(y1, y2) + hw + 1);
        float reach = hw + 1;
        for (int y = iy0; y < iy1; y++)
        {
            float py = y + 0.5f;
            // x extent of the thick line on this row
            float ta = Clamp01((py - reach - y1) / dy), tb = Clamp01((py + reach - y1) / dy);
            float xa = x1 + dx * ta, xb = x1 + dx * tb;
            if (xa > xb) (xa, xb) = (xb, xa);
            float ext = reach * MathF.Sqrt(len2) / MathF.Abs(dy);
            int sx = Math.Max(_cx0, (int)MathF.Floor(xa - ext)), ex = Math.Min(_cx1, (int)MathF.Ceiling(xb + ext));
            for (int x = sx; x < ex; x++)
            {
                float px = x + 0.5f;
                float t = Clamp01(((px - x1) * dx + (py - y1) * dy) / len2);
                float ex2 = x1 + t * dx - px, ey = y1 + t * dy - py;
                Plot(x, y, src, Clamp01(hw + 0.5f - MathF.Sqrt(ex2 * ex2 + ey * ey)));
            }
        }
    }

    public void FillEllipse(RectF r, Color c)
    {
        if (c.A == 0 || r.W <= 0 || r.H <= 0) return;
        uint src = Px.Premul(c);
        float cx = r.CenterX, cy = r.CenterY, rx = r.W / 2, ry = r.H / 2, m = MathF.Min(rx, ry);
        var (ix0, iy0, ix1, iy1) = ClipBounds(r.X - 1, r.Y - 1, r.Right + 1, r.Bottom + 1);
        for (int y = iy0; y < iy1; y++)
        {
            float ny = (y + 0.5f - cy) / ry;
            for (int x = ix0; x < ix1; x++)
            {
                float nx = (x + 0.5f - cx) / rx;
                float d = (MathF.Sqrt(nx * nx + ny * ny) - 1) * m;
                Plot(x, y, src, Clamp01(0.5f - d));
            }
        }
    }

    public void StrokeArc(RectF r, float startDegrees, float sweepDegrees, Color c, float width)
    {
        if (c.A == 0 || width <= 0) return;
        uint src = Px.Premul(c);
        float hw = width / 2;
        float cx = r.CenterX, cy = r.CenterY, rad = MathF.Min(r.W, r.H) / 2;
        bool full = sweepDegrees >= 360;
        float a0 = startDegrees * MathF.PI / 180, a1 = (startDegrees + sweepDegrees) * MathF.PI / 180;
        float e0x = cx + rad * MathF.Cos(a0), e0y = cy + rad * MathF.Sin(a0);
        float e1x = cx + rad * MathF.Cos(a1), e1y = cy + rad * MathF.Sin(a1);
        float start = ((startDegrees % 360) + 360) % 360;
        var (ix0, iy0, ix1, iy1) = ClipBounds(cx - rad - hw - 1, cy - rad - hw - 1, cx + rad + hw + 1, cy + rad + hw + 1);
        for (int y = iy0; y < iy1; y++)
        {
            float py = y + 0.5f - cy;
            for (int x = ix0; x < ix1; x++)
            {
                float px = x + 0.5f - cx;
                float dist = MathF.Sqrt(px * px + py * py);
                float d;
                if (full) d = MathF.Abs(dist - rad);
                else
                {
                    float ang = MathF.Atan2(py, px) * 180 / MathF.PI;
                    float rel = ((ang - start) % 360 + 360) % 360;
                    if (rel <= sweepDegrees) d = MathF.Abs(dist - rad);
                    else
                    {
                        float ax = x + 0.5f - e0x, ay = y + 0.5f - e0y, bx = x + 0.5f - e1x, by = y + 0.5f - e1y;
                        d = MathF.Sqrt(MathF.Min(ax * ax + ay * ay, bx * bx + by * by));
                    }
                }
                Plot(x, y, src, Clamp01(hw + 0.5f - d));
            }
        }
    }

    public void DrawShadow(RectF r, float radius, float blur, Color c)
    {
        if (c.A == 0 || blur <= 0) return;
        float oy = blur * 0.3f;
        float x0 = r.X, y0 = r.Y + oy, x1 = r.Right, y1 = r.Bottom + oy;
        float rad = MathF.Min(radius, MathF.Min(r.W, r.H) / 2);
        float cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, bx = r.W / 2, by = r.H / 2;
        var (ix0, iy0, ix1, iy1) = ClipBounds(x0 - blur, y0 - blur, x1 + blur, y1 + blur);
        var baseColor = c.WithAlpha((byte)255);
        uint src = Px.Premul(baseColor);
        float strength = c.A / 255f * 0.75f;
        for (int y = iy0; y < iy1; y++)
        {
            float py = y + 0.5f;
            bool inner = py > y0 + rad + 1 && py < y1 - rad - 1;
            int skipA = inner ? (int)MathF.Ceiling(x0 + 1) : int.MaxValue, skipB = inner ? (int)MathF.Floor(x1 - 1) : int.MinValue;
            for (int x = ix0; x < ix1; x++)
            {
                if (x >= skipA && x < skipB) { x = skipB - 1; continue; }
                float d = SdRoundBox(x + 0.5f - cx, py - cy, bx, by, rad);
                if (d < -1) continue; // hidden under the card
                float t = (d + 1) / (blur + 1);
                if (t >= 1) continue;
                float a = 1 - t;
                Plot(x, y, src, a * a * a * strength);
            }
        }
    }

    // ------------------------------------------------------------------ icons

    static readonly Dictionary<(Icon, int, int), byte[]> s_icons = new();

    public void DrawIcon(Icon icon, RectF r, Color c, float strokeWidth)
    {
        int size = (int)MathF.Round(r.W);
        if (size <= 0 || c.A == 0) return;
        int sw = (int)MathF.Round(strokeWidth * 8);
        byte[]? mask;
        lock (s_icons)
        {
            if (!s_icons.TryGetValue((icon, size, sw), out mask))
                s_icons[(icon, size, sw)] = mask = IconMask(icon, size, sw / 8f);
        }
        uint src = Px.Premul(c);
        int ox = (int)MathF.Round(r.X), oy = (int)MathF.Round(r.Y);
        int x0 = Math.Max(ox, _cx0), y0 = Math.Max(oy, _cy0), x1 = Math.Min(ox + size, _cx1), y1 = Math.Min(oy + size, _cy1);
        for (int y = y0; y < y1; y++)
        {
            int m = (y - oy) * size - ox;
            int row = y * _w;
            for (int x = x0; x < x1; x++)
            {
                byte k = mask[m + x];
                if (k == 0) continue;
                ref uint d = ref _px[row + x];
                d = Px.Over(d, Px.Scale(src, k));
            }
        }
    }

    /// <summary>Stroked icon as a coverage mask: distance from each pixel to the flattened path.</summary>
    static byte[] IconMask(Icon icon, int size, float stroke)
    {
        var segs = new List<float>(256);
        float s = size / 24f;
        float cx = 0, cy = 0, sx = 0, sy = 0;
        var d = icon.Data;
        for (int i = 0; i < d.Length;)
        {
            float op = d[i];
            if (op == Icon.MoveTo)
            {
                cx = sx = d[i + 1] * s;
                cy = sy = d[i + 2] * s;
                i += 3;
            }
            else if (op == Icon.LineTo)
            {
                float x = d[i + 1] * s, y = d[i + 2] * s;
                segs.Add(cx); segs.Add(cy); segs.Add(x); segs.Add(y);
                cx = x; cy = y;
                i += 3;
            }
            else if (op == Icon.CubicTo)
            {
                float x1 = d[i + 1] * s, y1 = d[i + 2] * s, x2 = d[i + 3] * s, y2 = d[i + 4] * s, x3 = d[i + 5] * s, y3 = d[i + 6] * s;
                const int steps = 12;
                float px = cx, py = cy;
                for (int k = 1; k <= steps; k++)
                {
                    float t = k / (float)steps, mt = 1 - t;
                    float qx = mt * mt * mt * cx + 3 * mt * mt * t * x1 + 3 * mt * t * t * x2 + t * t * t * x3;
                    float qy = mt * mt * mt * cy + 3 * mt * mt * t * y1 + 3 * mt * t * t * y2 + t * t * t * y3;
                    segs.Add(px); segs.Add(py); segs.Add(qx); segs.Add(qy);
                    px = qx; py = qy;
                }
                cx = x3; cy = y3;
                i += 7;
            }
            else
            {
                segs.Add(cx); segs.Add(cy); segs.Add(sx); segs.Add(sy);
                cx = sx; cy = sy;
                i += 1;
            }
        }

        var mask = new byte[size * size];
        float hw = stroke / 2;
        var arr = segs.ToArray();
        for (int y = 0; y < size; y++)
        {
            float py = y + 0.5f;
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f;
                float best = float.MaxValue;
                for (int k = 0; k < arr.Length; k += 4)
                {
                    float ax = arr[k], ay = arr[k + 1], bx = arr[k + 2], by = arr[k + 3];
                    float dx = bx - ax, dy = by - ay;
                    float len2 = dx * dx + dy * dy;
                    float t = len2 > 1e-8f ? Clamp01(((px - ax) * dx + (py - ay) * dy) / len2) : 0;
                    float ex = ax + t * dx - px, ey = ay + t * dy - py;
                    float dist2 = ex * ex + ey * ey;
                    if (dist2 < best) best = dist2;
                }
                float cov = Clamp01(hw + 0.5f - MathF.Sqrt(best));
                mask[y * size + x] = (byte)(cov * 255f + 0.5f);
            }
        }
        return mask;
    }

    // ------------------------------------------------------------------ text

    public void DrawText(string text, RectF r, FontSpec font, Color c, TextAlign align = TextAlign.Left, Trim trim = Trim.End)
    {
        if (string.IsNullOrEmpty(text) || c.A == 0 || r.W <= 1) return;
        string s = text;
        float w = _text.Measure(text, font);
        if (trim != Trim.None && w > r.W + 0.5f)
        {
            s = Shorten(text, r.W, font, trim);
            if (s.Length == 0) return;
            w = _text.Measure(s, font);
        }
        float x = align switch
        {
            TextAlign.Center => r.X + (r.W - w) / 2,
            TextAlign.Right => r.Right - w,
            _ => r.X,
        };
        float ascent = _text.Ascent(font), descent = _text.Descent(font);
        float baseline = r.Y + (r.H - (ascent + descent)) / 2 + ascent;
        int cx0 = _cx0, cx1 = _cx1;
        if (trim != Trim.None)
        {
            cx0 = Math.Max(cx0, (int)MathF.Floor(r.X - 1));
            cx1 = Math.Min(cx1, (int)MathF.Ceiling(r.Right + 1));
        }
        _text.Draw(_s, cx0, _cy0, cx1, _cy1, s, x, baseline, font, Px.Premul(c));
    }

    string Shorten(string text, float maxWidth, FontSpec font, Trim trim)
    {
        var runes = text.EnumerateRunes().ToArray();
        var adv = _text.Advances(runes, font);
        float ellipsis = _text.Measure("…", font);
        if (maxWidth < ellipsis) return "";
        int n = runes.Length;
        if (trim == Trim.End)
        {
            float sum = 0;
            int k = 0;
            while (k < n && sum + adv[k] + ellipsis <= maxWidth)
            {
                sum += adv[k];
                k++;
            }
            return Join(runes, 0, k) + "…";
        }
        // Middle: keep the start and (a bit more of) the end, e.g. "C:\Users\…\Documents\file.txt".
        var pre = new float[n + 1];
        for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + adv[i];
        for (int keep = n - 1; keep > 0; keep--)
        {
            int head = (int)MathF.Ceiling(keep * 0.4f), tail = keep - head;
            float width = pre[head] + ellipsis + (pre[n] - pre[n - tail]);
            if (width <= maxWidth) return Join(runes, 0, head) + "…" + Join(runes, n - tail, tail);
        }
        return "…";
    }

    static string Join(Rune[] runes, int start, int count)
    {
        var sb = new StringBuilder(count + 4);
        for (int i = start; i < start + count; i++) sb.Append(runes[i].ToString());
        return sb.ToString();
    }

    public float MeasureText(string text, FontSpec font) => string.IsNullOrEmpty(text) ? 0 : _text.Measure(text, font);

    public float LineHeight(FontSpec font) => MathF.Ceiling(_text.Ascent(font) + _text.Descent(font));

    // ------------------------------------------------------------------ clipping and layers

    public void PushClip(RectF r)
    {
        _clips.Add((_cx0, _cy0, _cx1, _cy1));
        _cx0 = Math.Max(_cx0, (int)MathF.Floor(r.X));
        _cy0 = Math.Max(_cy0, (int)MathF.Floor(r.Y));
        _cx1 = Math.Min(_cx1, (int)MathF.Ceiling(r.Right));
        _cy1 = Math.Min(_cy1, (int)MathF.Ceiling(r.Bottom));
        if (_cx1 < _cx0) _cx1 = _cx0;
        if (_cy1 < _cy0) _cy1 = _cy0;
    }

    public void PopClip()
    {
        if (_clips.Count == 0) return;
        (_cx0, _cy0, _cx1, _cy1) = _clips[^1];
        _clips.RemoveAt(_clips.Count - 1);
    }

    public ILayer CreateLayer(float width, float height) => new SoftLayer((int)MathF.Ceiling(width), (int)MathF.Ceiling(height));

    public void DrawLayer(ILayer layer, RectF dest, bool fast)
    {
        var src = ((SoftLayer)layer).Surface;
        int dx = (int)MathF.Round(dest.X), dy = (int)MathF.Round(dest.Y);
        int dw = (int)MathF.Round(dest.W), dh = (int)MathF.Round(dest.H);
        if (dw <= 0 || dh <= 0) return;
        int x0 = Math.Max(dx, _cx0), y0 = Math.Max(dy, _cy0), x1 = Math.Min(dx + dw, _cx1), y1 = Math.Min(dy + dh, _cy1);
        if (x0 >= x1 || y0 >= y1) return;
        if (dw == src.Width && dh == src.Height)
        {
            for (int y = y0; y < y1; y++)
                src.Pixels.AsSpan((y - dy) * src.Width + (x0 - dx), x1 - x0).CopyTo(_px.AsSpan(y * _w + x0, x1 - x0));
            return;
        }
        // Scaled (animations, live resize): nearest neighbour is plenty for a few frames.
        var map = new int[x1 - x0];
        for (int x = x0; x < x1; x++) map[x - x0] = Math.Min(src.Width - 1, (int)((x - dx + 0.5f) * src.Width / dw));
        for (int y = y0; y < y1; y++)
        {
            int sy = Math.Min(src.Height - 1, (int)((y - dy + 0.5f) * src.Height / dh));
            int srow = sy * src.Width, drow = y * _w;
            for (int x = x0; x < x1; x++) _px[drow + x] = src.Pixels[srow + map[x - x0]];
        }
    }
}

/// <summary>An offscreen <see cref="Surface"/> (the cached treemap image).</summary>
public sealed class SoftLayer(int width, int height) : ILayer
{
    public readonly Surface Surface = new(width, height);
    public float Width => Surface.Width;
    public float Height => Surface.Height;
    public ICanvas BeginDraw() => new SoftCanvas(Surface);
    public void EndDraw() { }
    public void Dispose() { }
}
