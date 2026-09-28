namespace SpaceAnalyzer.Render;

/// <summary>
/// Exact-area anti-aliased polygon rasterizer (signed-area accumulation, as in Raph Levien's font-rs).
/// Used for glyph outlines: every edge adds its signed coverage to a buffer, and a running sum per row
/// turns that into per-pixel coverage.
/// </summary>
public sealed class Rasterizer
{
    float[] _acc = new float[64];
    int _w, _h;

    public void Reset(int width, int height)
    {
        _w = width;
        _h = height;
        int n = width * height + 4;
        if (_acc.Length < n) _acc = new float[Math.Max(n, _acc.Length * 2)];
        else Array.Clear(_acc, 0, n);
    }

    public void Line(float x0, float y0, float x1, float y1)
    {
        if (Math.Abs(y0 - y1) <= float.Epsilon) return;
        float dir;
        if (y0 < y1) dir = 1;
        else
        {
            dir = -1;
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
        }
        float dxdy = (x1 - x0) / (y1 - y0);
        float x = x0;
        if (y0 < 0) x -= y0 * dxdy;
        int yStart = Math.Max(0, (int)y0);
        int yEnd = Math.Min(_h, (int)MathF.Ceiling(y1));
        var a = _acc;
        for (int y = yStart; y < yEnd; y++)
        {
            int line = y * _w;
            float dy = MathF.Min(y + 1, y1) - MathF.Max(y, y0);
            float xnext = x + dxdy * dy;
            float d = dy * dir;
            float xa = x < xnext ? x : xnext, xb = x < xnext ? xnext : x;
            float xaFloor = MathF.Floor(xa);
            int xai = (int)xaFloor;
            float xbCeil = MathF.Ceiling(xb);
            int xbi = (int)xbCeil;
            if (xai < 0) { xai = 0; xaFloor = 0; }
            if (xbi <= xai + 1)
            {
                float xmf = 0.5f * (x + xnext) - xaFloor;
                int i = line + xai;
                a[i] += d - d * xmf;
                a[i + 1] += d * xmf;
            }
            else
            {
                float s = 1f / (xb - xa);
                float xaf = xa - xaFloor;
                float a0 = 0.5f * s * (1 - xaf) * (1 - xaf);
                float xbf = xb - xbCeil + 1;
                float am = 0.5f * s * xbf * xbf;
                int i = line + xai;
                a[i] += d * a0;
                if (xbi == xai + 2)
                {
                    a[i + 1] += d * (1 - a0 - am);
                }
                else
                {
                    float a1 = s * (1.5f - xaf);
                    a[i + 1] += d * (a1 - a0);
                    for (int xi = xai + 2; xi < xbi - 1; xi++) a[line + xi] += d * s;
                    float a2 = a1 + (xbi - xai - 3) * s;
                    a[line + xbi - 1] += d * (1 - a2 - am);
                }
                a[line + xbi] += d * am;
            }
            x = xnext;
        }
    }

    /// <summary>Quadratic Bézier, flattened adaptively into lines.</summary>
    public void Quad(float x0, float y0, float x1, float y1, float x2, float y2)
    {
        float devx = x0 - 2 * x1 + x2, devy = y0 - 2 * y1 + y2;
        float devsq = devx * devx + devy * devy;
        if (devsq < 0.333f)
        {
            Line(x0, y0, x2, y2);
            return;
        }
        int n = 1 + (int)MathF.Sqrt(MathF.Sqrt(3f * devsq));
        float px = x0, py = y0;
        for (int i = 1; i <= n; i++)
        {
            float t = i / (float)n, mt = 1 - t;
            float qx = mt * mt * x0 + 2 * mt * t * x1 + t * t * x2;
            float qy = mt * mt * y0 + 2 * mt * t * y1 + t * t * y2;
            Line(px, py, qx, qy);
            px = qx;
            py = qy;
        }
    }

    /// <summary>Resolves the accumulated edges into 8-bit coverage (optionally through a gamma table).</summary>
    public void Resolve(byte[] coverage, byte[] gamma)
    {
        // One running sum over the whole buffer: the edges of every row cancel out by its end, and anything
        // spilled just past the last column is picked up correctly at the start of the next one.
        var a = _acc;
        int n = _w * _h;
        float acc = 0;
        for (int i = 0; i < n; i++)
        {
            acc += a[i];
            float c = MathF.Min(MathF.Abs(acc), 1f);
            coverage[i] = gamma[(int)(c * 255f + 0.5f)];
        }
    }
}
