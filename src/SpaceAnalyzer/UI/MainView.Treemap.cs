using System.Diagnostics;
using SpaceAnalyzer.Core;

namespace SpaceAnalyzer.UI;

public sealed partial class MainView
{
    const double ResizeSettleMs = 140, AnimationMs = 260;

    ILayer? _layer, _prevLayer;
    Cell? _layout;
    bool _layoutDirty = true;
    RectF _tmRect;
    long _lastResize;

    Cell? _hoverCell;
    bool _cardVisible;

    // Zoom animation: the new view grows out of the cell that was clicked (zoom in), or the old
    // view shrinks back into its place inside the parent (zoom out).
    bool _anim, _animIn, _animTargetPending;
    long _animStart;
    RectF _animRect;
    FileNode? _animOldView;

    void PaintTreemapArea(ICanvas c, RectF area)
    {
        _tmRect = area;
        if (_view is null || area.W < 8 || area.H < 8) return;

        bool sizeChanged = _layer is null || !SameLayerSize(_layer, area);
        if (sizeChanged)
        {
            // While the window is being resized, stretch the old image instead of re-rendering every frame.
            if (_layer is not null && Stopwatch.GetElapsedTime(_lastResize).TotalMilliseconds < ResizeSettleMs)
            {
                c.DrawLayer(_layer, area, fast: true);
                P.StartTimer(TimerResize, 50);
                AddZone(ZTreemap, area, hand: false);
                return;
            }
            // A zoom animation hands its old picture to _prevLayer and leaves _layer empty: that is not a resize.
            bool resized = _layer is not null || (_prevLayer is not null && !SameLayerSize(_prevLayer, area));
            _layer?.Dispose();
            _layer = c.CreateLayer(area.W, area.H);
            _layoutDirty = true;
            if (_anim && resized) EndAnimation();
        }

        if (_layoutDirty) RenderLayer();

        if (_anim) PaintAnimation(c, area);
        else
        {
            c.DrawLayer(_layer!, area, fast: false);
            PaintTreemapOverlays(c, area);
        }
        AddZone(ZTreemap, area, hand: false);
    }

    static bool SameLayerSize(ILayer layer, RectF area) =>
        Math.Abs(layer.Width - area.W) < 0.5f && Math.Abs(layer.Height - area.H) < 0.5f;

    void RenderLayer()
    {
        if (_layer is null || _view is null) return;
        _builder.S = S;
        _builder.Snap = 1;
        _builder.Detail = _detail;
        _builder.FreeSpace = ShowFreeBlock ? _volume!.FreeSpace : 0;
        _layout = _builder.Build(_view, new RectF(0, 0, _layer.Width, _layer.Height));

        _painter.T = T;
        _painter.S = S;
        _painter.Mode = _mode;
        _painter.HeaderHeight = _builder.HeaderHeight;
        _painter.Search = _search.Length > 0 ? _search : null;
        _painter.Highlight = _highlight;

        var lc = _layer.BeginDraw();
        try { _painter.Paint(lc, _layout, _layer.Width, _layer.Height, T.WindowBg, 10 * S); }
        finally { _layer.EndDraw(); }
        _layoutDirty = false;

        if (_animTargetPending && _animOldView is not null)
        {
            // Zooming out: now that the parent is laid out, find where the old view lives in it.
            var cell = TreemapBuilder.FindCell(_layout, _animOldView);
            if (cell is not null) _animRect = cell.R;
            else EndAnimation();
            _animTargetPending = false;
        }

        _hoverCell = null;
        if (_mouseInside && _tmRect.Contains(_mouse.X, _mouse.Y))
            _hoverCell = TreemapBuilder.HitTest(_layout, _mouse.X - _tmRect.X, _mouse.Y - _tmRect.Y);
    }

    bool ShowFreeBlock => _showFree && _rootIsVolume && _volume is not null && _volume.FreeSpace > 0 &&
                          ReferenceEquals(_view, _root);

    // ------------------------------------------------------------------ animation

    void PrepareZoomAnimation(FileNode oldView, FileNode newView)
    {
        if (_layer is null || _layout is null || _layoutDirty) return;
        if (oldView.IsAncestorOf(newView))
        {
            var cell = TreemapBuilder.FindCell(_layout, newView);
            if (cell is null || cell.R.W < 2 || cell.R.H < 2) return;
            _animIn = true;
            _animRect = cell.R;
            _animTargetPending = false;
        }
        else if (newView.IsAncestorOf(oldView))
        {
            _animIn = false;
            _animTargetPending = true;
            _animOldView = oldView;
        }
        else return;

        // Keep the current picture; the next render goes into a fresh layer.
        _prevLayer?.Dispose();
        _prevLayer = _layer;
        _layer = null;
        _anim = true;
        _animStart = Stopwatch.GetTimestamp();
        P.StartTimer(TimerAnim, 15);
    }

    void PaintAnimation(ICanvas c, RectF area)
    {
        double ms = Stopwatch.GetElapsedTime(_animStart).TotalMilliseconds;
        if (ms >= AnimationMs || _prevLayer is null || _layer is null)
        {
            EndAnimation();
            c.DrawLayer(_layer!, area, fast: false);
            PaintTreemapOverlays(c, area);
            return;
        }
        float t = (float)(ms / AnimationMs);
        t = 1 - (1 - t) * (1 - t) * (1 - t); // ease-out cubic
        var cellRect = _animRect.Offset(area.X, area.Y);
        if (_animIn)
        {
            c.DrawLayer(_prevLayer, area, fast: true);
            c.DrawLayer(_layer, RectF.Lerp(cellRect, area, t), fast: true);
        }
        else
        {
            c.DrawLayer(_layer, area, fast: true);
            c.DrawLayer(_prevLayer, RectF.Lerp(area, cellRect, t), fast: true);
        }
    }

    void EndAnimation()
    {
        _anim = false;
        _animTargetPending = false;
        _animOldView = null;
        _prevLayer?.Dispose();
        _prevLayer = null;
        P.StopTimer(TimerAnim);
    }

    // ------------------------------------------------------------------ hover & selection

    void UpdateTreemapHover(float x, float y)
    {
        if (_layout is null || _anim)
        {
            ClearTreemapHover();
            return;
        }
        var cell = TreemapBuilder.HitTest(_layout, x - _tmRect.X, y - _tmRect.Y);
        if (!ReferenceEquals(cell, _hoverCell))
        {
            _hoverCell = cell;
            _cardVisible = false;
            P.StopTimer(TimerCard);
            if (cell is not null) P.StartTimer(TimerCard, 420);
            P.Invalidate();
        }
        else if (_cardVisible)
        {
            P.Invalidate(); // the card follows the pointer
        }
    }

    void ClearTreemapHover()
    {
        if (_hoverCell is null && !_cardVisible) return;
        _hoverCell = null;
        _cardVisible = false;
        P.StopTimer(TimerCard);
        P.Invalidate();
    }

    void TreemapMouseDown(float x, float y, int clicks)
    {
        if (_layout is null || _anim) return;
        var cell = TreemapBuilder.HitTest(_layout, x - _tmRect.X, y - _tmRect.Y);
        _cardVisible = false;
        if (clicks >= 2)
        {
            if (cell is null) return;
            var node = cell.Node;
            switch (cell.Kind)
            {
                case CellKind.Folder:
                case CellKind.FolderLeaf:
                    NavigateTo(node!);
                    break;
                case CellKind.File:
                    if (node!.Parent is { } parent && !ReferenceEquals(parent, _view)) NavigateTo(parent, select: node);
                    break;
                case CellKind.Others:
                    if (!ReferenceEquals(node, _view)) NavigateTo(node!);
                    break;
            }
            return;
        }
        Select(cell switch
        {
            null => null,
            { Kind: CellKind.FreeSpace } => null,
            { Kind: CellKind.Others } => ReferenceEquals(cell.Node, _view) ? null : cell.Node,
            _ => cell.Node,
        });
    }

    void TreemapContextMenu(float x, float y)
    {
        if (_layout is null || _anim) return;
        var cell = TreemapBuilder.HitTest(_layout, x - _tmRect.X, y - _tmRect.Y);
        var node = cell?.Kind switch
        {
            null or CellKind.FreeSpace => null,
            CellKind.Others => ReferenceEquals(cell.Node, _view) ? null : cell.Node,
            _ => cell.Node,
        };
        Select(node);
        _cardVisible = false;
        P.Invalidate();

        var items = new List<MenuEntry>();
        if (ZoomInTarget() is not null) items.Add(new MenuEntry((int)Cmd.ZoomIn, Strings.ZoomIn));
        if (_view?.Parent is not null) items.Add(new MenuEntry((int)Cmd.Up, Strings.ZoomOut));
        if (items.Count > 0) items.Add(MenuEntry.Separator);
        items.Add(new MenuEntry((int)Cmd.OpenItem, Strings.OpenItem));
        items.Add(new MenuEntry((int)Cmd.RevealItem, Strings.Reveal));
        items.Add(new MenuEntry((int)Cmd.CopyPath, Strings.CopyPath));
        if (P.CanShowProperties) items.Add(new MenuEntry((int)Cmd.Properties, Strings.Properties));
        items.Add(MenuEntry.Separator);
        if (Target is { IsDirectory: true }) items.Add(new MenuEntry((int)Cmd.RescanItem, Strings.RescanFolder));
        items.Add(new MenuEntry((int)Cmd.AskAiItem, Strings.AskAiItem));
        items.Add(MenuEntry.Separator);
        items.Add(new MenuEntry((int)Cmd.TrashItem, Strings.Trash, IsEnabled(Cmd.TrashItem)));
        OpenMenu(items, x, y, id => Execute((Cmd)id));
    }

    void ZoomTowards(float x, float y)
    {
        if (_layout is null) return;
        var top = TreemapBuilder.TopLevelAt(_layout, x - _tmRect.X, y - _tmRect.Y);
        if (top is { Kind: CellKind.Folder or CellKind.FolderLeaf, Node: { } n }) NavigateTo(n);
        else if (top is { Kind: CellKind.Others, Node: { } o } && !ReferenceEquals(o, _view)) NavigateTo(o);
    }

    /// <summary>Arrow keys: move the selection to the neighbouring block in that direction.</summary>
    void MoveSelection(Key key)
    {
        if (_layout?.Kids is null) return;
        var current = _selected is null ? null : TreemapBuilder.FindCell(_layout, _selected);
        if (current is null || !current.IsSelectable)
        {
            var first = _layout.Kids.FirstOrDefault(k => k.IsSelectable);
            if (first is not null) Select(first.Node);
            return;
        }
        var parent = TreemapBuilder.ParentOf(_layout, current) ?? _layout;
        var siblings = parent.Kids!;
        float cx = current.R.CenterX, cy = current.R.CenterY;
        Cell? best = null;
        float bestScore = float.MaxValue;
        foreach (var s in siblings)
        {
            if (ReferenceEquals(s, current) || !s.IsSelectable) continue;
            float dx = s.R.CenterX - cx, dy = s.R.CenterY - cy;
            float along = key switch { Key.Left => -dx, Key.Right => dx, Key.Up => -dy, _ => dy };
            float across = key is Key.Left or Key.Right ? Math.Abs(dy) : Math.Abs(dx);
            if (along <= 0.5f) continue;
            float score = along + across * 2;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        if (best is not null) Select(best.Node);
        else if (key == Key.Up && !ReferenceEquals(parent, _layout) && parent.Node is not null) Select(parent.Node);
        else if (key == Key.Down && current.Kind == CellKind.Folder && current.Kids?.FirstOrDefault(k => k.IsSelectable) is { } child) Select(child.Node);
    }

    // ------------------------------------------------------------------ overlays

    void PaintTreemapOverlays(ICanvas c, RectF area)
    {
        if (_layout is null) return;
        if (_selected is not null && TreemapBuilder.FindCell(_layout, _selected) is { } sel)
        {
            var r = sel.R.Offset(area.X, area.Y);
            c.StrokeRoundRect(r.Inflate(1.5f * S), sel.Radius + 1.5f * S, T.Accent.WithAlpha((byte)110), 3 * S);
            c.StrokeRoundRect(r.Deflate(S), Math.Max(0, sel.Radius - S), T.Accent, 2 * S);
        }
        if (_hoverCell is { } h && (h.Kind is CellKind.Others or CellKind.FreeSpace || !ReferenceEquals(h.Node, _selected)))
        {
            var r = h.R.Offset(area.X, area.Y);
            c.FillRoundRect(r, h.Radius, (T.Dark ? Color.White : Color.Black).WithAlpha((byte)(T.Dark ? 22 : 16)));
            c.StrokeRoundRect(r.Deflate(S), Math.Max(0, h.Radius - S), T.Dark ? Color.White.WithAlpha((byte)215) : Color.Black.WithAlpha((byte)150), 1.5f * S);
        }
    }

    void PaintHoverCard(ICanvas c)
    {
        if (!_cardVisible || _hoverCell is null || _anim || _view is null) return;
        var cell = _hoverCell;
        var node = cell.Node;

        string title;
        string line1, line2;
        string? line3 = null, path = null;
        Color dot;
        double viewSize = Math.Max(1, _view.Size);
        switch (cell.Kind)
        {
            case CellKind.FreeSpace:
                title = Strings.FreeSpace;
                line1 = Fmt.Size(_volume?.FreeSpace ?? 0);
                line2 = _volume is null ? "" : Strings.FreeOf(Fmt.Size(_volume.FreeSpace), Fmt.Size(_volume.TotalSize));
                line3 = _volume?.Label;
                dot = T.FreeText;
                break;
            case CellKind.Others:
                title = Strings.SmallItems(cell.OthersCount);
                line1 = $"{Fmt.Size(cell.BlockSize)}  ·  {Fmt.Percent(cell.BlockSize / viewSize)} {Strings.OfView}";
                line2 = node!.Name;
                path = node.FullPath;
                dot = _painter.OthersColor(cell).Lighten(0.25f);
                break;
            default:
                title = node!.Name;
                line1 = $"{Fmt.Size(node.Size)}  ·  {Fmt.Percent(node.Size / viewSize)} {Strings.OfView}";
                line2 = node.IsDirectory
                    ? $"{Strings.FilesCount(node.FileCount)}  ·  {Strings.FoldersCount(node.FolderCount)}"
                    : Strings.CategoryName(node.Category);
                line3 = $"{Strings.Modified} {Fmt.Age(node.LastWriteUtcTicks)}  ·  {Fmt.Date(node.LastWriteUtcTicks)}";
                path = node.FullPath;
                dot = cell.Kind == CellKind.File ? _painter.FileColor(cell) : _painter.NodeColor(node, cell.Level);
                break;
        }

        var fTitle = F(13.5f, Weight.Semibold);
        var fLine = F(12.5f);
        var fSmall = F(11.5f);
        float pad = 14 * S;
        float maxW = Math.Min(460 * S, W - 24 * S);
        float w = c.MeasureText(title, fTitle) + 22 * S;
        w = Math.Max(w, c.MeasureText(line1, fLine));
        w = Math.Max(w, c.MeasureText(line2, fLine));
        if (line3 is not null) w = Math.Max(w, c.MeasureText(line3, fSmall));
        if (path is not null) w = Math.Max(w, Math.Min(c.MeasureText(path, fSmall), 360 * S));
        w = Math.Clamp(w + pad * 2, Math.Min(220 * S, maxW), maxW);
        float lineH = 19 * S;
        float h = pad * 2 + 22 * S + lineH * 2 + (line3 is not null ? lineH : 0) + (path is not null ? lineH + 4 * S : 0);

        float x = _mouse.X + 18 * S, y = _mouse.Y + 20 * S;
        if (x + w > W - 8 * S) x = _mouse.X - w - 14 * S;
        if (y + h > H - 8 * S) y = _mouse.Y - h - 14 * S;
        x = Math.Max(8 * S, x);
        y = Math.Max(8 * S, y);
        var r = new RectF(x, y, w, h);

        c.DrawShadow(r, 12 * S, 22 * S, T.Shadow);
        c.FillRoundRect(r, 12 * S, T.TooltipBg);
        c.StrokeRoundRect(r.Deflate(Hair / 2), 12 * S, T.TooltipBorder, Hair);

        float tx = r.X + pad, tw = r.W - pad * 2, ty = r.Y + pad;
        c.FillRoundRect(new RectF(tx, ty + 5 * S, 12 * S, 12 * S), 3.5f * S, dot);
        c.DrawText(title, new RectF(tx + 20 * S, ty, tw - 20 * S, 22 * S), fTitle, T.TextPrimary);
        ty += 24 * S;
        c.DrawText(line1, new RectF(tx, ty, tw, lineH), fLine, T.TextPrimary);
        ty += lineH;
        c.DrawText(line2, new RectF(tx, ty, tw, lineH), fLine, T.TextSecondary);
        ty += lineH;
        if (line3 is not null)
        {
            c.DrawText(line3, new RectF(tx, ty, tw, lineH), fSmall, T.TextSecondary);
            ty += lineH;
        }
        if (path is not null)
        {
            ty += 4 * S;
            c.FillRect(new RectF(tx, ty - 2 * S, tw, Hair), T.TooltipBorder);
            c.DrawText(path, new RectF(tx, ty + 1 * S, tw, lineH), fSmall, T.TextMuted, TextAlign.Left, Trim.Middle);
        }
    }
}
