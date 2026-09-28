namespace SpaceAnalizer.UI;

public readonly struct Color
{
    public readonly byte R, G, B, A;

    public Color(byte r, byte g, byte b, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }

    public static Color Hex(uint rgb, byte alpha = 255) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, alpha);
    public static readonly Color White = new(255, 255, 255);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color Transparent = new(0, 0, 0, 0);

    public Color WithAlpha(byte a) => new(R, G, B, a);
    public Color WithAlpha(float a) => new(R, G, B, (byte)Math.Clamp(a * 255f + 0.5f, 0, 255));

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t + 0.5f),
            (byte)(a.G + (b.G - a.G) * t + 0.5f),
            (byte)(a.B + (b.B - a.B) * t + 0.5f),
            (byte)(a.A + (b.A - a.A) * t + 0.5f));
    }

    public Color Lighten(float t) => Lerp(this, White.WithAlpha(A), t);
    public Color Darken(float t) => Lerp(this, Black.WithAlpha(A), t);

    /// <summary>Composites this (possibly translucent) color over an opaque background.</summary>
    public Color Over(Color background) => Lerp(background, WithAlpha((byte)255), A / 255f);

    /// <summary>Perceived brightness 0..1 (sRGB luma).</summary>
    public float Luma => (0.299f * R + 0.587f * G + 0.114f * B) / 255f;

    public uint Argb => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;
    /// <summary>Win32 COLORREF (0x00BBGGRR).</summary>
    public uint ColorRef => ((uint)B << 16) | ((uint)G << 8) | R;

    public static Color Hsl(float h, float s, float l, byte alpha = 255)
    {
        h = ((h % 360) + 360) % 360 / 360f;
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
        float p = 2 * l - q;
        static float Hue(float p, float q, float t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1f / 6) return p + (q - p) * 6 * t;
            if (t < 0.5f) return q;
            if (t < 2f / 3) return p + (q - p) * (2f / 3 - t) * 6;
            return p;
        }
        return new Color(
            (byte)Math.Clamp(Hue(p, q, h + 1f / 3) * 255 + 0.5f, 0, 255),
            (byte)Math.Clamp(Hue(p, q, h) * 255 + 0.5f, 0, 255),
            (byte)Math.Clamp(Hue(p, q, h - 1f / 3) * 255 + 0.5f, 0, 255),
            alpha);
    }
}

public readonly struct PointF(float x, float y)
{
    public readonly float X = x, Y = y;
}

public readonly struct RectF
{
    public readonly float X, Y, W, H;

    public RectF(float x, float y, float w, float h)
    {
        X = x; Y = y; W = w; H = h;
    }

    public static RectF FromLTRB(float l, float t, float r, float b) => new(l, t, r - l, b - t);
    public static readonly RectF Empty = default;

    public float Right => X + W;
    public float Bottom => Y + H;
    public float CenterX => X + W / 2;
    public float CenterY => Y + H / 2;
    public bool IsEmpty => W <= 0 || H <= 0;
    public float Area => W * H;

    public bool Contains(float px, float py) => px >= X && py >= Y && px < X + W && py < Y + H;
    public bool Contains(PointF p) => Contains(p.X, p.Y);

    public RectF Deflate(float d) => new(X + d, Y + d, W - 2 * d, H - 2 * d);
    public RectF Deflate(float dx, float dy) => new(X + dx, Y + dy, W - 2 * dx, H - 2 * dy);
    public RectF Inflate(float d) => new(X - d, Y - d, W + 2 * d, H + 2 * d);
    public RectF Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);
    public RectF WithX(float x) => new(x, Y, W, H);
    public RectF WithY(float y) => new(X, y, W, H);
    public RectF WithW(float w) => new(X, Y, w, H);
    public RectF WithH(float h) => new(X, Y, W, h);

    public RectF Intersect(RectF o)
    {
        float l = Math.Max(X, o.X), t = Math.Max(Y, o.Y);
        float r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > l && b > t ? FromLTRB(l, t, r, b) : Empty;
    }

    public static RectF Lerp(RectF a, RectF b, float t) => new(
        a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
        a.W + (b.W - a.W) * t, a.H + (b.H - a.H) * t);

    /// <summary>A rectangle of the given size centered in this one.</summary>
    public RectF CenterBox(float w, float h) => new(X + (W - w) / 2, Y + (H - h) / 2, w, h);

    public bool SameSize(RectF o) => Math.Abs(W - o.W) < 0.5f && Math.Abs(H - o.H) < 0.5f;
    public override string ToString() => $"[{X:0.#},{Y:0.#} {W:0.#}x{H:0.#}]";
}

public enum Weight : byte { Regular, Medium, Semibold, Bold }

public readonly struct FontSpec(float size, Weight weight = Weight.Regular)
{
    public readonly float Size = size;
    public readonly Weight Weight = weight;
}

public enum TextAlign : byte { Left, Center, Right }

public enum Trim : byte
{
    None,
    /// <summary>"Very long name…"</summary>
    End,
    /// <summary>"C:\Users\…\file.txt"</summary>
    Middle,
}

public enum CursorKind : byte { Arrow, Hand, IBeam }

public enum MouseButton : byte { Left, Right, Middle, Back, Forward }

[Flags]
public enum Mods : byte
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    /// <summary>⌘ on macOS.</summary>
    Meta = 8,
}

public enum Key
{
    None,
    Enter, Escape, Backspace, Delete, Space, Tab,
    Left, Right, Up, Down, Home, End, PageUp, PageDown,
    F1, F2, F3, F4, F5,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    OpenBracket, CloseBracket,
    BrowserBack, BrowserForward,
}
