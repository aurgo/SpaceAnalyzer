using System.Globalization;

namespace SpaceAnalyzer.UI;

/// <summary>
/// A stroked vector icon on a 24×24 grid, stored as flat drawing commands
/// (0 = move x y, 1 = line x y, 2 = cubic x1 y1 x2 y2 x y, 3 = close).
/// </summary>
public sealed class Icon
{
    public const float MoveTo = 0, LineTo = 1, CubicTo = 2, Close = 3;

    public readonly float[] Data;

    Icon(float[] data) => Data = data;

    /// <summary>Builds an icon from one or more SVG path strings.</summary>
    public static Icon Parse(params string[] paths)
    {
        var list = new List<float>(64);
        foreach (var p in paths) SvgPath.Parse(p, list);
        return new Icon(list.ToArray());
    }
}

/// <summary>Icon set in the style of Lucide (ISC license, https://lucide.dev).</summary>
public static class Icons
{
    static string Circle(int cx, int cy, int r) =>
        string.Create(CultureInfo.InvariantCulture, $"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0z");

    public static readonly Icon FolderOpen = Icon.Parse(
        "m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2");
    public static readonly Icon Folder = Icon.Parse(
        "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z");
    public static readonly Icon File = Icon.Parse(
        "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z", "M14 2v4a2 2 0 0 0 2 2h4");
    public static readonly Icon Refresh = Icon.Parse(
        "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8", "M21 3v5h-5",
        "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16", "M8 16H3v5");
    public static readonly Icon ArrowLeft = Icon.Parse("m12 19-7-7 7-7", "M19 12H5");
    public static readonly Icon ArrowRight = Icon.Parse("M5 12h14", "m12 5 7 7-7 7");
    public static readonly Icon ArrowUp = Icon.Parse("m5 12 7-7 7 7", "M12 19V5");
    public static readonly Icon ChevronRight = Icon.Parse("m9 18 6-6-6-6");
    public static readonly Icon ChevronDown = Icon.Parse("m6 9 6 6 6-6");
    public static readonly Icon Search = Icon.Parse(Circle(11, 11, 8), "m21 21-4.3-4.3");
    public static readonly Icon X = Icon.Parse("M18 6 6 18", "m6 6 12 12");
    public static readonly Icon More = Icon.Parse(Circle(12, 12, 1), Circle(19, 12, 1), Circle(5, 12, 1));
    public static readonly Icon HardDrive = Icon.Parse(
        "M22 12H2",
        "M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z",
        "M6 16h.01", "M10 16h.01");
    public static readonly Icon House = Icon.Parse(
        "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8",
        "M3 10a2 2 0 0 1 .709-1.528l7-5.999a2 2 0 0 1 2.582 0l7 5.999A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z");
    public static readonly Icon Trash = Icon.Parse(
        "M3 6h18", "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6", "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2", "M10 11v6", "M14 11v6");
    public static readonly Icon External = Icon.Parse(
        "M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6");
    public static readonly Icon Copy = Icon.Parse(
        "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H10a2 2 0 0 1-2-2V10a2 2 0 0 1 2-2z",
        "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2");
    public static readonly Icon PanelRight = Icon.Parse(
        "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z", "M15 3v18");
    public static readonly Icon Info = Icon.Parse(Circle(12, 12, 10), "M12 16v-4", "M12 8h.01");
    public static readonly Icon Alert = Icon.Parse(
        "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3", "M12 9v4", "M12 17h.01");
    public static readonly Icon FolderSearch = Icon.Parse(
        "M10.7 20H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H20a2 2 0 0 1 2 2v4.1",
        Circle(17, 17, 3), "m21 21-1.9-1.9");
    public static readonly Icon Check = Icon.Parse("M20 6 9 17l-5-5");
    public static readonly Icon Clock = Icon.Parse(Circle(12, 12, 10), "M12 6v6l4 2");
    public static readonly Icon Download = Icon.Parse("M12 15V3", "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m7 10 5 5 5-5");
    public static readonly Icon GitHub = Icon.Parse(
        "M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4",
        "M9 18c-4.51 2-5-2-7-2");
}

/// <summary>Minimal SVG path parser (M L H V C S Q T A Z, absolute and relative) that emits cubic Béziers.</summary>
static class SvgPath
{
    public static void Parse(string d, List<float> o)
    {
        int i = 0;
        char cmd = '\0';
        float cx = 0, cy = 0, sx = 0, sy = 0;     // current point, sub-path start
        float qx = 0, qy = 0;                     // last control point (for S / T)
        char prev = '\0';

        while (true)
        {
            SkipSeparators(d, ref i);
            if (i >= d.Length) break;
            char ch = d[i];
            if (char.IsLetter(ch))
            {
                cmd = ch;
                i++;
            }
            else if (cmd == '\0')
            {
                throw new FormatException("Path data must start with a command: " + d);
            }

            bool rel = char.IsLower(cmd);
            float ox = rel ? cx : 0, oy = rel ? cy : 0;
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                    cx = ox + Num(d, ref i); cy = oy + Num(d, ref i);
                    sx = cx; sy = cy;
                    o.Add(Icon.MoveTo); o.Add(cx); o.Add(cy);
                    cmd = rel ? 'l' : 'L'; // following pairs are implicit line-tos
                    break;
                case 'L':
                    cx = ox + Num(d, ref i); cy = oy + Num(d, ref i);
                    Line(o, cx, cy);
                    break;
                case 'H':
                    cx = ox + Num(d, ref i);
                    Line(o, cx, cy);
                    break;
                case 'V':
                    cy = oy + Num(d, ref i);
                    Line(o, cx, cy);
                    break;
                case 'C':
                {
                    float x1 = ox + Num(d, ref i), y1 = oy + Num(d, ref i);
                    float x2 = ox + Num(d, ref i), y2 = oy + Num(d, ref i);
                    float x = ox + Num(d, ref i), y = oy + Num(d, ref i);
                    Cubic(o, x1, y1, x2, y2, x, y);
                    qx = x2; qy = y2; cx = x; cy = y;
                    break;
                }
                case 'S':
                {
                    bool smooth = "CcSs".Contains(prev);
                    float x1 = smooth ? 2 * cx - qx : cx, y1 = smooth ? 2 * cy - qy : cy;
                    float x2 = ox + Num(d, ref i), y2 = oy + Num(d, ref i);
                    float x = ox + Num(d, ref i), y = oy + Num(d, ref i);
                    Cubic(o, x1, y1, x2, y2, x, y);
                    qx = x2; qy = y2; cx = x; cy = y;
                    break;
                }
                case 'Q':
                {
                    float x1 = ox + Num(d, ref i), y1 = oy + Num(d, ref i);
                    float x = ox + Num(d, ref i), y = oy + Num(d, ref i);
                    Quad(o, cx, cy, x1, y1, x, y);
                    qx = x1; qy = y1; cx = x; cy = y;
                    break;
                }
                case 'T':
                {
                    bool smooth = "QqTt".Contains(prev);
                    float x1 = smooth ? 2 * cx - qx : cx, y1 = smooth ? 2 * cy - qy : cy;
                    float x = ox + Num(d, ref i), y = oy + Num(d, ref i);
                    Quad(o, cx, cy, x1, y1, x, y);
                    qx = x1; qy = y1; cx = x; cy = y;
                    break;
                }
                case 'A':
                {
                    float rx = Num(d, ref i), ry = Num(d, ref i), rot = Num(d, ref i);
                    bool large = Flag(d, ref i), sweep = Flag(d, ref i);
                    float x = ox + Num(d, ref i), y = oy + Num(d, ref i);
                    Arc(o, cx, cy, rx, ry, rot, large, sweep, x, y);
                    cx = x; cy = y;
                    break;
                }
                case 'Z':
                    o.Add(Icon.Close);
                    cx = sx; cy = sy;
                    break;
                default:
                    throw new FormatException($"Unsupported path command '{cmd}'.");
            }
            prev = cmd;
        }
    }

    static void Line(List<float> o, float x, float y)
    {
        o.Add(Icon.LineTo); o.Add(x); o.Add(y);
    }

    static void Cubic(List<float> o, float x1, float y1, float x2, float y2, float x, float y)
    {
        o.Add(Icon.CubicTo);
        o.Add(x1); o.Add(y1); o.Add(x2); o.Add(y2); o.Add(x); o.Add(y);
    }

    static void Quad(List<float> o, float x0, float y0, float x1, float y1, float x, float y)
        => Cubic(o, x0 + 2f / 3 * (x1 - x0), y0 + 2f / 3 * (y1 - y0), x + 2f / 3 * (x1 - x), y + 2f / 3 * (y1 - y), x, y);

    /// <summary>SVG elliptical arc converted to at most four cubic Béziers (SVG spec, appendix F.6).</summary>
    static void Arc(List<float> o, float x1, float y1, float rx, float ry, float angle, bool large, bool sweep, float x2, float y2)
    {
        if (x1 == x2 && y1 == y2) return;
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx == 0 || ry == 0) { Line(o, x2, y2); return; }

        double phi = angle * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx2 = (x1 - x2) / 2.0, dy2 = (y1 - y2) / 2.0;
        double x1p = cos * dx2 + sin * dy2, y1p = -sin * dx2 + cos * dy2;

        double rxd = rx, ryd = ry;
        double lambda = x1p * x1p / (rxd * rxd) + y1p * y1p / (ryd * ryd);
        if (lambda > 1) { double s = Math.Sqrt(lambda); rxd *= s; ryd *= s; }

        double num = rxd * rxd * ryd * ryd - rxd * rxd * y1p * y1p - ryd * ryd * x1p * x1p;
        double den = rxd * rxd * y1p * y1p + ryd * ryd * x1p * x1p;
        double coef = den == 0 ? 0 : Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
        double cxp = coef * (rxd * y1p / ryd), cyp = coef * (-ryd * x1p / rxd);
        double ccx = cos * cxp - sin * cyp + (x1 + x2) / 2.0;
        double ccy = sin * cxp + cos * cyp + (y1 + y2) / 2.0;

        double ux = (x1p - cxp) / rxd, uy = (y1p - cyp) / ryd;
        double vx = (-x1p - cxp) / rxd, vy = (-y1p - cyp) / ryd;
        double theta = Math.Atan2(uy, ux);
        double delta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        int segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        double step = delta / segments;
        double t = 4.0 / 3.0 * Math.Tan(step / 4);

        (double X, double Y) Map(double ex, double ey) =>
            (ccx + rxd * ex * cos - ryd * ey * sin, ccy + rxd * ex * sin + ryd * ey * cos);

        for (int k = 0; k < segments; k++)
        {
            double a1 = theta + k * step, a2 = a1 + step;
            double c1 = Math.Cos(a1), s1 = Math.Sin(a1), c2 = Math.Cos(a2), s2 = Math.Sin(a2);
            var p1 = Map(c1 - t * s1, s1 + t * c1);
            var p2 = Map(c2 + t * s2, s2 - t * c2);
            var p3 = k == segments - 1 ? (x2, y2) : Map(c2, s2);
            Cubic(o, (float)p1.X, (float)p1.Y, (float)p2.X, (float)p2.Y, (float)p3.Item1, (float)p3.Item2);
        }
    }

    static void SkipSeparators(string d, ref int i)
    {
        while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\n' || d[i] == '\t' || d[i] == '\r')) i++;
    }

    static bool Flag(string d, ref int i)
    {
        SkipSeparators(d, ref i);
        if (i >= d.Length) throw new FormatException("Missing arc flag.");
        return d[i++] == '1';
    }

    static float Num(string d, ref int i)
    {
        SkipSeparators(d, ref i);
        int start = i;
        if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
        bool dot = false;
        while (i < d.Length)
        {
            char c = d[i];
            if (char.IsAsciiDigit(c)) { i++; continue; }
            if (c == '.' && !dot) { dot = true; i++; continue; }
            if ((c == 'e' || c == 'E') && i + 1 < d.Length && (char.IsAsciiDigit(d[i + 1]) || d[i + 1] == '-' || d[i + 1] == '+'))
            {
                i += 2;
                while (i < d.Length && char.IsAsciiDigit(d[i])) i++;
            }
            break;
        }
        if (i == start) throw new FormatException($"Number expected at {start} in: {d}");
        return float.Parse(d.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
