using SpaceAnalyzer.Core;

namespace SpaceAnalyzer.UI;

public enum CellKind : byte
{
    File,
    /// <summary>Folder drawn as a frame with its contents inside.</summary>
    Folder,
    /// <summary>Folder too small to show its contents: drawn as a solid block.</summary>
    FolderLeaf,
    /// <summary>Many items too small to draw one by one.</summary>
    Others,
    FreeSpace,
}

/// <summary>One laid-out rectangle of the treemap (coordinates are relative to the treemap layer).</summary>
public sealed class Cell
{
    /// <summary>The file or folder; for <see cref="CellKind.Others"/> the folder that holds them; null for free space.</summary>
    public FileNode? Node;
    public RectF R;
    public Cell[]? Kids;
    public CellKind Kind;
    /// <summary>Number of folder frames around this cell inside the current view.</summary>
    public int Level;
    public bool Header;
    public float Radius;
    public long OthersCount;
    /// <summary>Bytes of a block that isn't one file or folder: its small items together, or the free space.</summary>
    public long BlockSize;
    /// <summary>For <see cref="CellKind.Others"/>: the type taking most of the space, and the size-weighted age.</summary>
    public FileCategory OthersCategory;
    public long OthersTicks;

    public bool IsSelectable => Kind is CellKind.File or CellKind.Folder or CellKind.FolderLeaf;
}

/// <summary>Turns a folder into nested, squarified cells for a given area.</summary>
public sealed class TreemapBuilder
{
    public float S = 1;
    /// <summary>Device pixel size in canvas units (1 on Windows, 0.5 on a Retina Mac).</summary>
    public float Snap = 1;
    public Detail Detail = Detail.Normal;
    /// <summary>Bytes of free space to show as a block next to the view's contents (0 = none).</summary>
    public long FreeSpace;
    public int CellCount { get; private set; }

    public float HeaderHeight => 19 * S;
    float MinArea => (Detail switch { Detail.Low => 110f, Detail.High => 9f, _ => 30f }) * S * S;
    float MinFolder => (Detail switch { Detail.Low => 30f, Detail.High => 11f, _ => 18f }) * S;
    int MaxCells => Detail switch { Detail.Low => 25_000, Detail.High => 150_000, _ => 70_000 };
    float Gap => Math.Max(Snap, MathF.Floor(S));

    readonly record struct Item(FileNode? Node, long Size, CellKind Kind, long Count, FileCategory Category = FileCategory.Other, long Ticks = 0);

    public Cell Build(FileNode view, RectF bounds)
    {
        CellCount = 0;
        var root = new Cell { Node = view, R = bounds, Kind = CellKind.Folder, Level = -1 };
        var content = bounds.Deflate(Gap);
        LayoutChildren(root, view, content, 0, FreeSpace);
        return root;
    }

    void LayoutChildren(Cell cell, FileNode dir, RectF content, int level, long free)
    {
        var kids = dir.Children;
        if (content.W < 1 || content.H < 1) return;

        double area = (double)content.W * content.H;
        double minArea = MinArea;
        long total = dir.Size + Math.Max(free, 0);
        if (total <= 0) return;
        double scale = area / total;
        if (free > 0 && free * scale < minArea)
        {
            free = 0;
            total = dir.Size;
            if (total <= 0) return;
            scale = area / total;
        }

        var items = new List<Item>(Math.Min(kids.Length + 2, 512));
        long shownKids = 0;
        int i = 0;
        bool freePending = free > 0;
        while (true)
        {
            long kidSize = i < kids.Length ? kids[i].Size : -1;
            if (freePending && free >= kidSize)
            {
                items.Add(new Item(null, free, CellKind.FreeSpace, 0));
                freePending = false;
                continue;
            }
            if (i >= kids.Length || kidSize <= 0) break;
            if (kidSize * scale < minArea || CellCount >= MaxCells) break;
            var kid = kids[i];
            items.Add(new Item(kid, kidSize, kid.IsDirectory ? CellKind.Folder : CellKind.File, 0));
            shownKids += kidSize;
            CellCount++;
            i++;
        }
        if (freePending) items.Add(new Item(null, free, CellKind.FreeSpace, 0));

        long rest = dir.Size - shownKids;
        if (rest > 0)
        {
            // Summarize what the block stands for: how many items, which type dominates, how old they are.
            long count = 0;
            Span<long> bytesByType = stackalloc long[FileCategories.Count];
            double weightedTicks = 0;
            for (int k = i; k < kids.Length; k++)
            {
                var kid = kids[k];
                if (kid.Size <= 0) continue;
                count++;
                if (kid.CategorySizes is { } sizes)
                    for (int c = 0; c < sizes.Length; c++) bytesByType[c] += sizes[c];
                else
                    bytesByType[(int)kid.Category] += kid.Size;
                weightedTicks += (double)kid.LastWriteUtcTicks * kid.Size;
            }
            int dominant = 0;
            for (int c = 1; c < bytesByType.Length; c++)
                if (bytesByType[c] > bytesByType[dominant]) dominant = c;
            long ticks = (long)(weightedTicks / Math.Max(1, rest));
            // Keep the list sorted (largest first) so squarify stays well-behaved.
            int at = items.Count;
            while (at > 0 && items[at - 1].Size < rest) at--;
            items.Insert(at, new Item(dir, rest, CellKind.Others, count, (FileCategory)dominant, ticks));
        }

        int n = items.Count;
        if (n == 0) return;
        var values = new double[n];
        var rects = new RectD[n];
        for (int k = 0; k < n; k++) values[k] = items[k].Size;
        Squarify.Layout(values, new RectD(content.X, content.Y, content.W, content.H), rects);

        var cells = new Cell[n];
        int made = 0;
        for (int k = 0; k < n; k++)
        {
            var r = SnapRect(rects[k]);
            if (r.W < Snap * 0.99f || r.H < Snap * 0.99f) continue;
            r = Separate(r);
            var it = items[k];
            Cell c = it.Kind switch
            {
                CellKind.Folder => BuildFolder(it.Node!, r, level),
                CellKind.Others => new Cell
                {
                    Node = dir, R = r, Kind = CellKind.Others, Level = level,
                    OthersCount = it.Count, BlockSize = it.Size, OthersCategory = it.Category, OthersTicks = it.Ticks,
                },
                CellKind.FreeSpace => new Cell { R = r, Kind = CellKind.FreeSpace, Level = level, BlockSize = it.Size },
                _ => new Cell { Node = it.Node, R = r, Kind = CellKind.File, Level = level },
            };
            c.Radius = RadiusFor(c.R, level);
            cells[made++] = c;
        }
        if (made < n) Array.Resize(ref cells, made);
        cell.Kids = cells;
    }

    Cell BuildFolder(FileNode dir, RectF r, int level)
    {
        var cell = new Cell { Node = dir, R = r, Kind = CellKind.Folder, Level = level };
        float minF = MinFolder;
        if (r.W < minF || r.H < minF || dir.Children.Length == 0)
        {
            cell.Kind = CellKind.FolderLeaf;
            return cell;
        }

        float hh = HeaderHeight;
        bool header = r.H >= hh * 2.2f && r.W >= 48 * S;
        float pad = SnapV(Math.Min(r.W, r.H) >= 60 * S ? 3 * S : 2 * S);
        var content = RectF.FromLTRB(r.X + pad, r.Y + (header ? SnapV(hh) : pad), r.Right - pad, r.Bottom - pad);
        if (content.W < 3 * S || content.H < 3 * S)
        {
            cell.Kind = CellKind.FolderLeaf;
            return cell;
        }
        cell.Header = header;
        LayoutChildren(cell, dir, content, level + 1, 0);
        if (cell.Kids is null || cell.Kids.Length == 0)
            cell.Kind = CellKind.FolderLeaf;
        return cell;
    }

    float SnapV(double v) => (float)(Math.Round(v / Snap) * Snap);

    RectF SnapRect(RectD d) => RectF.FromLTRB(SnapV(d.X), SnapV(d.Y), SnapV(d.X + d.W), SnapV(d.Y + d.H));

    /// <summary>Leaves a thin gap between neighbours so every block reads as its own shape.</summary>
    RectF Separate(RectF r)
    {
        float g = Gap;
        float m = Math.Min(r.W, r.H);
        if (m >= 8 * g) return RectF.FromLTRB(r.X + g, r.Y + g, r.Right - g, r.Bottom - g);
        if (m >= 3 * Snap) return RectF.FromLTRB(r.X, r.Y, r.Right - Snap, r.Bottom - Snap);
        return r;
    }

    float RadiusFor(RectF r, int level)
    {
        float m = Math.Min(r.W, r.H);
        if (m < 10 * S) return 0;
        float rad = Math.Max(2, 6 - level) * S;
        return Math.Min(rad, m / 4);
    }

    // ---------------------------------------------------------------- queries

    /// <summary>Deepest cell under the point, or null (the view background).</summary>
    public static Cell? HitTest(Cell root, float x, float y)
    {
        Cell? found = null;
        var cur = root;
        while (cur.Kids is { } kids)
        {
            Cell? next = null;
            foreach (var k in kids)
            {
                if (k.R.Contains(x, y)) { next = k; break; }
            }
            if (next is null) break;
            found = next;
            cur = next;
        }
        return found;
    }

    /// <summary>The cell showing <paramref name="target"/>, or the closest visible cell that contains it.</summary>
    public static Cell? FindCell(Cell root, FileNode target)
    {
        var view = root.Node;
        if (view is null || !view.IsAncestorOf(target)) return null;
        var chain = new List<FileNode>();
        for (var n = target; n is not null && !ReferenceEquals(n, view); n = n.Parent) chain.Add(n);

        var cur = root;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            Cell? next = null, others = null;
            if (cur.Kids is { } kids)
            {
                foreach (var k in kids)
                {
                    if (k.Kind == CellKind.Others) { others = k; continue; }
                    if (ReferenceEquals(k.Node, chain[i])) { next = k; break; }
                }
            }
            if (next is null) return others ?? (ReferenceEquals(cur, root) ? null : cur);
            cur = next;
        }
        return cur;
    }

    /// <summary>The direct child cell of the view that contains <paramref name="cell"/>'s position.</summary>
    public static Cell? TopLevelAt(Cell root, float x, float y)
    {
        if (root.Kids is null) return null;
        foreach (var k in root.Kids)
            if (k.R.Contains(x, y)) return k;
        return null;
    }

    /// <summary>The cell that directly contains <paramref name="child"/>.</summary>
    public static Cell? ParentOf(Cell root, Cell child)
    {
        var stack = new Stack<Cell>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (c.Kids is null) continue;
            foreach (var k in c.Kids)
            {
                if (ReferenceEquals(k, child)) return c;
                if (k.Kids is not null && k.R.Contains(child.R.CenterX, child.R.CenterY)) stack.Push(k);
            }
        }
        return null;
    }
}

/// <summary>Paints a laid-out treemap into a layer.</summary>
public sealed class TreemapPainter
{
    public Theme T = Theme.DarkTheme;
    public ColorMode Mode;
    public float S = 1;
    public float HeaderHeight = 19;
    public string? Search;
    public FileCategory? Highlight;

    readonly record struct Label(string Text, RectF R, FontSpec Font, Color Color, TextAlign Align);
    readonly List<Label> _labels = new(1024);
    FontSpec _header, _label, _labelSmall;
    int _dotBudget;
    bool Filtering => !string.IsNullOrEmpty(Search) || Highlight is not null;

    /// <summary>
    /// Paints the whole treemap. The area outside the rounded treemap "well" is filled with
    /// <paramref name="outside"/> so the layer can be blitted as-is.
    /// </summary>
    public void Paint(ICanvas c, Cell root, float width, float height, Color outside, float cornerRadius)
    {
        _labels.Clear();
        _dotBudget = 12_000;
        _header = new FontSpec(11.5f * S, Weight.Semibold);
        _label = new FontSpec(11f * S, Weight.Medium);
        _labelSmall = new FontSpec(10f * S, Weight.Regular);

        var all = new RectF(0, 0, width, height);
        c.FillRect(all, outside);
        c.FillRoundRect(all, cornerRadius, T.TreemapBg);
        if (root.Kids is { } kids)
            foreach (var k in kids) PaintCell(c, k, false);

        // Text last: fewer switches between shape and text rendering on Windows.
        foreach (var l in _labels)
            c.DrawText(l.Text, l.R, l.Font, l.Color, l.Align, Trim.End);
    }

    void PaintCell(ICanvas c, Cell cell, bool parentMatches)
    {
        var node = cell.Node;
        switch (cell.Kind)
        {
            case CellKind.Folder:
            {
                bool match = parentMatches || NameMatches(node!);
                var fill = FrameColor(cell.Level);
                c.FillRoundRect(cell.R, cell.Radius, fill);
                if (cell.R.W >= 24 * S && cell.R.H >= 24 * S)
                    c.StrokeRoundRect(cell.R.Deflate(0.5f), cell.Radius, T.FolderBorder, 1);
                if (cell.Header)
                    QueueHeader(c, cell, fill, match && !string.IsNullOrEmpty(Search));
                if (cell.Kids is { } kids)
                    foreach (var k in kids) PaintCell(c, k, match);
                break;
            }
            case CellKind.FolderLeaf:
            {
                var col = Color.Lerp(LeafColor(cell), FrameColor(cell.Level), 0.3f);
                bool lit = IsLit(node!, parentMatches, node!.Category);
                if (!lit) col = Dim(col);
                Block(c, cell, col);
                if (lit) QueueBlockLabel(cell, col, node.Name, Fmt.Size(node.Size));
                break;
            }
            case CellKind.File:
            {
                var col = FileColor(cell);
                bool lit = IsLit(node!, parentMatches, node!.Category);
                if (!lit) col = Dim(col);
                Block(c, cell, col);
                if (lit) QueueBlockLabel(cell, col, node.Name, Fmt.Size(node.Size));
                break;
            }
            case CellKind.Others:
            {
                // A muted version of what the small items are (type, level or age), with a dotted
                // texture that says "many tiny things" — like SpaceMonger's unlabeled crowd of boxes.
                var col = OthersColor(cell);
                bool lit = !(Filtering && !parentMatches) && (Highlight is null || Highlight == cell.OthersCategory);
                if (!lit) col = Dim(col);
                c.FillRoundRect(cell.R, cell.Radius, col);
                if (cell.R.W >= 12 * S && cell.R.H >= 12 * S)
                    Dots(c, cell.R, T.Dark ? Color.White.WithAlpha((byte)22) : Color.Black.WithAlpha((byte)22));
                string label = Strings.SmallItems(cell.OthersCount);
                if (c.MeasureText(label, _label) > cell.R.W - 8 * S)
                    label = Strings.MoreItems(cell.OthersCount);
                QueueBlockLabel(cell, col, label, Fmt.Size(cell.BlockSize), Color.Lerp(col, T.LabelOn(col), 0.75f));
                break;
            }
            case CellKind.FreeSpace:
            {
                c.FillRoundRect(cell.R, cell.Radius, T.FreeFill);
                Hatch(c, cell.R, T.FreeHatch, 7 * S);
                c.StrokeRoundRect(cell.R.Deflate(0.5f), cell.Radius, T.FolderBorder, 1);
                QueueBlockLabel(cell, T.FreeFill, Strings.FreeSpace, Fmt.Size(cell.BlockSize), T.FreeText);
                break;
            }
        }
    }

    void Block(ICanvas c, Cell cell, Color col)
    {
        var r = cell.R;
        if (r.W >= 9 * S && r.H >= 9 * S)
            c.FillRoundRectGradient(r, cell.Radius, col.Lighten(0.13f), col.Darken(0.10f));
        else
            c.FillRect(r, col);
    }

    bool NameMatches(FileNode node) =>
        !string.IsNullOrEmpty(Search) && node.Name.Contains(Search, StringComparison.OrdinalIgnoreCase);

    bool IsLit(FileNode node, bool parentMatches, FileCategory category)
    {
        if (Highlight is { } h && category != h) return false;
        if (!string.IsNullOrEmpty(Search) && !parentMatches && !NameMatches(node)) return false;
        return true;
    }

    Color Dim(Color c) => Color.Lerp(c, T.TreemapBg, T.Dark ? 0.8f : 0.72f);

    public Color FrameColor(int level)
    {
        if (Mode != ColorMode.Depth) return T.FolderFill(level);
        var hue = T.Level(level);
        return T.Dark ? Color.Lerp(hue, Color.Hex(0x101217), 0.64f) : Color.Lerp(hue, Color.White, 0.7f);
    }

    public Color FileColor(Cell cell) => Mode switch
    {
        ColorMode.Depth => T.Level(cell.Level - 1),
        ColorMode.Age => T.AgeColor(cell.Node!.LastWriteUtcTicks),
        _ => T.Category(cell.Node!.Category),
    };

    Color LeafColor(Cell cell) => Mode switch
    {
        ColorMode.Depth => T.Level(cell.Level),
        ColorMode.Age => T.AgeColor(cell.Node!.LastWriteUtcTicks),
        _ => T.Category(cell.Node!.Category),
    };

    /// <summary>Muted color of a "small items" block: what they mostly are (type, level or age).</summary>
    public Color OthersColor(Cell cell)
    {
        var hue = Mode switch
        {
            ColorMode.Depth => T.Level(cell.Level),
            ColorMode.Age => T.AgeColor(cell.OthersTicks),
            _ => T.Category(cell.OthersCategory),
        };
        return Color.Lerp(hue, T.OthersFill, T.Dark ? 0.62f : 0.55f);
    }

    /// <summary>Color that represents a node outside the treemap (sidebar icons, lists...).</summary>
    public Color NodeColor(FileNode node, int level = 0) => Mode switch
    {
        ColorMode.Depth => T.Level(level),
        ColorMode.Age => T.AgeColor(node.LastWriteUtcTicks),
        _ => T.Category(node.Category),
    };

    void QueueHeader(ICanvas c, Cell cell, Color fill, bool highlighted)
    {
        var node = cell.Node!;
        var r = new RectF(cell.R.X + 6 * S, cell.R.Y, cell.R.W - 12 * S, HeaderHeight);
        if (r.W < 16 * S) return;
        Color text = highlighted ? T.Accent.Over(fill) : HeaderText(cell.Level, fill);
        Color sub = Color.Lerp(fill, text, 0.62f);
        string size = Fmt.Size(node.Size);
        float sizeW = c.MeasureText(size, _labelSmall);
        if (r.W > sizeW + 48 * S)
        {
            _labels.Add(new Label(size, r, _labelSmall, sub, TextAlign.Right));
            _labels.Add(new Label(node.Name, r.WithW(r.W - sizeW - 8 * S), _header, text, TextAlign.Left));
        }
        else
        {
            _labels.Add(new Label(node.Name, r, _header, text, TextAlign.Left));
        }
    }

    Color HeaderText(int level, Color fill)
    {
        if (Mode != ColorMode.Depth) return T.HeaderText;
        var hue = T.Level(level);
        return T.Dark ? Color.Lerp(hue, Color.White, 0.8f) : Color.Lerp(hue, Color.Black, 0.66f);
    }

    void QueueBlockLabel(Cell cell, Color bg, string name, string? sub, Color? fixedColor = null)
    {
        var r = cell.R;
        if (r.W < 30 * S || r.H < 15 * S) return;
        var fg = fixedColor ?? T.LabelOn(bg);
        float line = 13 * S;
        float inset = 4 * S;
        if (sub is not null && r.H >= 31 * S && r.W >= 40 * S)
        {
            float y = r.CenterY - line;
            _labels.Add(new Label(name, new RectF(r.X + inset, y, r.W - 2 * inset, line), _label, fg, TextAlign.Center));
            _labels.Add(new Label(sub, new RectF(r.X + inset, y + line, r.W - 2 * inset, line), _labelSmall, Color.Lerp(bg, fg, 0.78f), TextAlign.Center));
        }
        else
        {
            _labels.Add(new Label(name, new RectF(r.X + inset, r.Y, r.W - 2 * inset, r.H), _label, fg, TextAlign.Center));
        }
    }

    void Hatch(ICanvas c, RectF r, Color line, float spacing)
    {
        c.PushClip(r);
        for (float x = r.X - r.H; x < r.Right; x += spacing)
            c.DrawLine(x, r.Bottom, x + r.H, r.Y, line, Math.Max(1, S));
        c.PopClip();
    }

    void Dots(ICanvas c, RectF r, Color dot)
    {
        float step = 6 * S, d = 1.6f * S;
        int dots = (int)(r.W * r.H / (step * step));
        if (dots > 1500 || dots > _dotBudget) return; // don't spend time on huge areas
        _dotBudget -= dots;
        c.PushClip(r);
        for (float y = r.Y + step / 2; y < r.Bottom; y += step)
            for (float x = r.X + step / 2; x < r.Right; x += step)
                c.FillRect(new RectF(x, y, d, d), dot);
        c.PopClip();
    }
}
