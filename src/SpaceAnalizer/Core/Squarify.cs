namespace SpaceAnalizer.Core;

public readonly record struct RectD(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double Area => W * H;
}

/// <summary>
/// Squarified treemap layout (Bruls, Huizing &amp; van Wijk, 2000): rectangles as close to squares
/// as possible, which makes them easy to compare, label and click.
/// </summary>
public static class Squarify
{
    /// <summary>
    /// Splits <paramref name="bounds"/> into one rectangle per value. <paramref name="values"/> should be
    /// sorted from largest to smallest and be positive; each rectangle's area is proportional to its value.
    /// </summary>
    public static void Layout(ReadOnlySpan<double> values, RectD bounds, Span<RectD> result)
    {
        int n = values.Length;
        if (n == 0) return;

        double total = 0;
        foreach (var v in values) total += v;

        double x = bounds.X, y = bounds.Y, w = bounds.W, h = bounds.H;
        if (total <= 0 || w <= 0 || h <= 0)
        {
            for (int i = 0; i < n; i++) result[i] = new RectD(x, y, 0, 0);
            return;
        }

        double scale = w * h / total;
        int start = 0;
        while (start < n)
        {
            if (start == n - 1 || w <= 0 || h <= 0)
            {
                // The last item (or anything left after rounding trouble) takes what remains.
                result[start] = new RectD(x, y, Math.Max(w, 0), Math.Max(h, 0));
                for (int i = start + 1; i < n; i++) result[i] = new RectD(x + Math.Max(w, 0), y + Math.Max(h, 0), 0, 0);
                return;
            }

            double side = Math.Min(w, h);
            double first = values[start] * scale;
            double rowArea = first;
            double worst = Worst(rowArea, first, first, side);
            int end = start + 1;
            while (end < n)
            {
                double a = values[end] * scale;
                double candidate = Worst(rowArea + a, first, a, side);
                if (candidate > worst) break;
                rowArea += a;
                worst = candidate;
                end++;
            }

            bool last = end == n;
            if (w >= h)
            {
                // Column on the left side, items stacked top to bottom.
                double colW = last ? w : Math.Min(w, rowArea / h);
                double cy = y;
                for (int i = start; i < end; i++)
                {
                    double ih = i == end - 1 ? y + h - cy : values[i] * scale / colW;
                    result[i] = new RectD(x, cy, colW, Math.Max(ih, 0));
                    cy += ih;
                }
                x += colW;
                w -= colW;
            }
            else
            {
                // Row along the top side, items left to right.
                double rowH = last ? h : Math.Min(h, rowArea / w);
                double cx = x;
                for (int i = start; i < end; i++)
                {
                    double iw = i == end - 1 ? x + w - cx : values[i] * scale / rowH;
                    result[i] = new RectD(cx, y, Math.Max(iw, 0), rowH);
                    cx += iw;
                }
                y += rowH;
                h -= rowH;
            }
            start = end;
        }
    }

    /// <summary>Worst aspect ratio of a row with total area <paramref name="rowArea"/> laid along <paramref name="side"/>.</summary>
    static double Worst(double rowArea, double maxArea, double minArea, double side)
    {
        double s2 = side * side;
        double r2 = rowArea * rowArea;
        if (minArea <= 0 || r2 <= 0) return double.MaxValue;
        return Math.Max(s2 * maxArea / r2, r2 / (s2 * minArea));
    }
}
