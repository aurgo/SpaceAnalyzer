using System.Diagnostics;
using SpaceAnalyzer.Core;

namespace SpaceAnalyzer.UI;

public sealed partial class MainView
{
    enum Btn { Primary, Secondary, Ghost, Danger, Link }

    FontSpec F(float size, Weight weight = Weight.Regular) => new(size * S, weight);
    FontSpec SearchFont => F(13);

    public void Paint(ICanvas c)
    {
        _zones.Clear();
        c.FillRect(new RectF(0, 0, W, H), T.WindowBg);

        switch (_screen)
        {
            case Screen.Welcome: PaintWelcome(c); break;
            case Screen.Scanning: PaintScanning(c); break;
            default: PaintBrowse(c); break;
        }

        if (_dialog != DialogKind.None)
        {
            _zones.Clear();
            PaintDialog(c);
        }
        PaintToast(c); // above an open dialog too, like the one its link can show
        if (_dialog == DialogKind.None && _popup is null) PaintTooltip(c);
        PaintMenu(c);
    }

    // =====================================================================================
    // Browse screen
    // =====================================================================================

    void PaintBrowse(ICanvas c)
    {
        float toolbarH = MathF.Round(50 * S), statusH = MathF.Round(28 * S);
        float sideW = _sidebar ? MathF.Round(Math.Clamp(W * 0.27f, 250 * S, 320 * S)) : 0;
        if (W - sideW < 420 * S) sideW = 0;

        PaintToolbar(c, new RectF(0, 0, W, toolbarH));
        float m = MathF.Round(8 * S);
        var area = RectF.FromLTRB(m, toolbarH + m, MathF.Floor(W - sideW - m), MathF.Floor(H - statusH - m));
        PaintTreemapArea(c, area);
        if (sideW > 0) PaintSidebar(c, RectF.FromLTRB(W - sideW, toolbarH, W, H - statusH));
        PaintStatusBar(c, RectF.FromLTRB(0, H - statusH, W, H));
        if (_popup is null) PaintHoverCard(c);
    }

    void PaintToolbar(ICanvas c, RectF bar)
    {
        c.FillRect(bar, T.ToolbarBg);
        c.FillRect(new RectF(0, bar.Bottom - Hair, W, Hair), T.Divider);
        float cy = bar.CenterY, bh = 32 * S, top = cy - bh / 2;
        float x = 10 * S;

        // Logo = home (drive list)
        var home = new RectF(x, top, bh, bh);
        if (Hot(ZHome)) c.FillRoundRect(home, 8 * S, Down(ZHome) ? T.SurfacePressed : T.SurfaceHover);
        PaintLogo(c, home.CenterBox(22 * S, 22 * S));
        AddZone(ZHome, home, () => Execute(Cmd.Home), Strings.Home);
        x += bh + 6 * S;

        float ow = MeasureButton(c, Icons.FolderOpen, Strings.Open, true);
        _openButton = new RectF(x, top, ow, bh);
        Button(c, ZOpen, _openButton, Icons.FolderOpen, Strings.Open, Btn.Secondary, () => Execute(Cmd.OpenMenu), Strings.OpenTip, chevron: true);
        x += ow + 6 * S;
        IconButton(c, ZRescan, new RectF(x, top, bh, bh), Icons.Refresh, () => Execute(Cmd.Rescan),
            Strings.WithKeys(Strings.Rescan, P.IsMac ? "⌘R" : "F5"), IsEnabled(Cmd.Rescan));
        x += bh + 10 * S;
        c.FillRect(new RectF(x, cy - 10 * S, Hair, 20 * S), T.Divider);
        x += 10 * S;
        IconButton(c, ZBack, new RectF(x, top, bh, bh), Icons.ArrowLeft, () => Execute(Cmd.Back), Strings.WithKeys(Strings.Back, Strings.BackKeys), IsEnabled(Cmd.Back));
        x += bh + 2 * S;
        IconButton(c, ZForward, new RectF(x, top, bh, bh), Icons.ArrowRight, () => Execute(Cmd.Forward), Strings.WithKeys(Strings.Forward, Strings.ForwardKeys), IsEnabled(Cmd.Forward));
        x += bh + 2 * S;
        IconButton(c, ZUp, new RectF(x, top, bh, bh), Icons.ArrowUp, () => Execute(Cmd.Up), Strings.WithKeys(Strings.Up, Strings.UpKeys), IsEnabled(Cmd.Up));
        x += bh + 10 * S;

        // Right side, from the right edge inwards.
        float rx = W - 10 * S;
        _moreButton = new RectF(rx - bh, top, bh, bh);
        IconButton(c, ZMore, _moreButton, Icons.More, ShowMoreMenu, Strings.MoreOptions);
        rx -= bh + 2 * S;
        IconButton(c, ZSidebarToggle, new RectF(rx - bh, top, bh, bh), Icons.PanelRight, () => Execute(Cmd.ToggleSidebar), Strings.Sidebar, active: _sidebar);
        rx -= bh + 2 * S;
        IconButton(c, ZAskAi, new RectF(rx - bh, top, bh, bh), Icons.Sparkles, () => Execute(Cmd.AskAi), Strings.AskAiTip, IsEnabled(Cmd.AskAi));
        rx -= bh + 10 * S;

        var segF = F(12.5f, Weight.Medium);
        float segW = 0;
        foreach (var l in new[] { Strings.ColorType, Strings.ColorDepth, Strings.ColorAge })
            segW = Math.Max(segW, c.MeasureText(l, segF) + 22 * S);
        var seg = new RectF(rx - segW * 3 - 4 * S, top, segW * 3 + 4 * S, bh);
        Segmented(c, seg, segF);
        rx = seg.X - 10 * S;

        float searchW = Math.Clamp((rx - x) * 0.42f, 130 * S, 250 * S);
        var box = new RectF(rx - searchW, top, searchW, bh);
        PaintSearch(c, box);
        rx = box.X - 10 * S;

        PaintBreadcrumb(c, RectF.FromLTRB(x, top, rx, top + bh));
    }

    void Segmented(ICanvas c, RectF r, FontSpec f)
    {
        c.FillRoundRect(r, 9 * S, T.Surface);
        c.StrokeRoundRect(r.Deflate(Hair / 2), 9 * S, T.SurfaceBorder, Hair);
        string[] labels = [Strings.ColorType, Strings.ColorDepth, Strings.ColorAge];
        string[] tips = [Strings.ColorTypeLong, Strings.ColorDepthLong, Strings.ColorAgeLong];
        float w = (r.W - 4 * S) / 3;
        for (int i = 0; i < 3; i++)
        {
            var s = new RectF(r.X + 2 * S + i * w, r.Y + 2 * S, w, r.H - 4 * S);
            bool sel = (int)_mode == i, hot = Hot(ZMode + i);
            if (sel)
            {
                if (!T.Dark) c.DrawShadow(s, 7 * S, 4 * S, T.Shadow.WithAlpha((byte)40));
                c.FillRoundRect(s, 7 * S, T.Raised);
            }
            else if (hot) c.FillRoundRect(s, 7 * S, T.SurfaceHover);
            c.DrawText(labels[i], s, f, sel || hot ? T.TextPrimary : T.TextSecondary, TextAlign.Center);
            var mode = (ColorMode)i;
            AddZone(ZMode + i, s, () => { SetMode(mode); P.Invalidate(); }, Strings.WithKeys(tips[i], Strings.Cmd + (i + 1)));
        }
    }

    void PaintBreadcrumb(ICanvas c, RectF area)
    {
        if (_view is null || area.W < 60 * S) return;
        var chain = new List<FileNode>();
        for (var n = _view; n is not null; n = n.Parent) chain.Add(n);
        chain.Reverse();

        var f = F(13, Weight.Medium);
        var fLast = F(13, Weight.Semibold);
        float pad = 8 * S, sep = 18 * S, icon = 22 * S, maxCrumb = 200 * S;
        int n2 = chain.Count;
        var labels = new string[n2];
        var widths = new float[n2];
        for (int i = 0; i < n2; i++)
        {
            labels[i] = CrumbLabel(chain[i]);
            widths[i] = Math.Min(maxCrumb, c.MeasureText(labels[i], i == n2 - 1 ? fLast : f)) + pad * 2 + (i == 0 ? icon : 0);
        }

        // The current folder always shows. When space is short, middle folders fold into "…" (a menu),
        // and on very narrow windows the first one goes too: "… › current".
        float ellW = c.MeasureText("…", f) + pad * 2;
        float Width(bool withFirst, int tail)
        {
            float t = 0;
            bool any = false;
            if (withFirst) { t += widths[0]; any = true; }
            if (tail > (withFirst ? 1 : 0)) { t += (any ? sep : 0) + ellW; any = true; }
            for (int i = tail; i < n2; i++) { t += (any ? sep : 0) + widths[i]; any = true; }
            return t;
        }
        bool showFirst = true;
        int tail = 1;
        while (tail < n2 - 1 && Width(true, tail) > area.W) tail++;
        if (n2 > 1 && Width(true, tail) > area.W)
        {
            showFirst = false;
            tail = n2 - 1;
        }

        float x = area.X;
        bool drawn = false;
        void Separator()
        {
            c.DrawIcon(Icons.ChevronRight, new RectF(x + 2 * S, area.CenterY - 7 * S, 14 * S, 14 * S), T.TextMuted, 1.7f * S);
            x += sep;
        }

        void Crumb(int i)
        {
            bool last = i == n2 - 1;
            float w = Math.Min(widths[i], area.Right - x);
            if (w < 24 * S) return;
            var r = new RectF(x, area.Y, w, area.H);
            int id = ZCrumb + Math.Min(i, 98);
            if (!last && Hot(id)) c.FillRoundRect(r, 7 * S, Down(id) ? T.SurfacePressed : T.SurfaceHover);
            float tx = r.X + pad;
            if (i == 0)
            {
                c.DrawIcon(_rootIsVolume ? Icons.HardDrive : Icons.Folder, new RectF(tx, r.CenterY - 8 * S, 16 * S, 16 * S),
                    last || Hot(id) ? T.TextPrimary : T.TextSecondary, 1.7f * S);
                tx += icon;
            }
            c.DrawText(labels[i], RectF.FromLTRB(tx, r.Y, r.Right - pad, r.Bottom), last ? fLast : f,
                last || Hot(id) ? T.TextPrimary : T.TextSecondary);
            var node = chain[i];
            if (!last) AddZone(id, r, () => NavigateTo(node), i == 0 ? node.FullPath : null);
            else AddZone(id, r, null, node.FullPath, hand: false);
            x += w;
            drawn = true;
        }

        if (showFirst) Crumb(0);
        int hiddenFrom = showFirst ? 1 : 0, hiddenTo = tail - 1;
        if (hiddenTo >= hiddenFrom)
        {
            if (drawn) Separator();
            var r = new RectF(x, area.Y, ellW, area.H);
            if (Hot(ZCrumbMore)) c.FillRoundRect(r, 7 * S, Down(ZCrumbMore) ? T.SurfacePressed : T.SurfaceHover);
            c.DrawText("…", r, f, Hot(ZCrumbMore) ? T.TextPrimary : T.TextSecondary, TextAlign.Center, Trim.None);
            AddZone(ZCrumbMore, r, () =>
            {
                var items = new List<MenuEntry>();
                for (int i = hiddenFrom; i <= hiddenTo; i++) items.Add(new MenuEntry(i, labels[i]));
                OpenMenu(items, r.X, r.Bottom + 4 * S, i => NavigateTo(chain[i]));
            });
            x += ellW;
            drawn = true;
        }
        for (int i = tail; i < n2; i++)
        {
            if (drawn) Separator();
            Crumb(i);
        }
    }

    string CrumbLabel(FileNode n)
    {
        if (n.Parent is not null) return n.Name;
        if (_rootIsVolume && _volume is not null) return _volume.Label;
        var name = Path.GetFileName(n.Name.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(name) ? n.Name : name;
    }

    // ------------------------------------------------------------------ sidebar

    void PaintSidebar(ICanvas c, RectF r)
    {
        c.FillRect(r, T.SidebarBg);
        c.FillRect(new RectF(r.X, r.Y, Hair, r.H), T.Divider);
        var target = _selected ?? _view;
        if (target is null) return;

        float x = r.X + 18 * S, w = r.W - 36 * S, y = r.Y + 18 * S;
        SectionLabel(c, _selected is not null ? Strings.Selection : Strings.CurrentFolder, x, y, w);
        y += 24 * S;
        y = PaintInfo(c, target, x, y, w) + 14 * S;
        y = PaintActions(c, x, y, w) + 20 * S;
        c.FillRect(new RectF(x, y, w, Hair), T.Divider);
        y += 18 * S;
        y = PaintLegend(c, x, y, w);

        // Share what is left between the type breakdown and the largest files.
        float bottom = r.Bottom - 12 * S;
        int types = _view?.CategorySizes?.Count(v => v > 0) ?? 0;
        float rowType = 28 * S, rowFile = 26 * S, head = 22 * S, sep = 26 * S;
        float largestMin = sep + head + Math.Min(_largest.Count, 5) * rowFile;
        int typeRows = types;
        if (y + head + types * rowType + largestMin > bottom)
            typeRows = Math.Clamp((int)((bottom - y - head - largestMin) / rowType), Math.Min(3, types), types);
        y = PaintTypes(c, x, y, w, typeRows);
        if (_largest.Count > 0 && y + sep + head + rowFile <= bottom)
        {
            y += 8 * S;
            c.FillRect(new RectF(x, y, w, Hair), T.Divider);
            y += 18 * S;
            PaintLargest(c, x, y, w, bottom);
        }
    }

    float PaintInfo(ICanvas c, FileNode n, float x, float y, float w)
    {
        var col = n.IsDirectory && _mode == ColorMode.Depth
            ? T.Level(n.Depth - (_view?.Depth ?? 0) - 1)
            : _painter.NodeColor(n, n.Depth - (_view?.Depth ?? 0) - 2);
        var tile = new RectF(x, y, 42 * S, 42 * S);
        c.FillRoundRectGradient(tile, 11 * S, col.Lighten(0.14f), col.Darken(0.1f));
        c.DrawIcon(n.IsDirectory ? Icons.Folder : Icons.File, tile.CenterBox(22 * S, 22 * S), T.LabelOn(col), 1.8f * S);
        float tx = tile.Right + 12 * S, tw = x + w - tx;
        string title = n.Parent is null ? CrumbLabel(n) : n.Name;
        c.DrawText(title, new RectF(tx, y + 1 * S, tw, 21 * S), F(14.5f, Weight.Semibold), T.TextPrimary);
        string kind = n.IsDirectory ? Strings.Folder : Strings.CategoryName(n.Category);
        string ext = n.IsDirectory ? "" : Path.GetExtension(n.Name);
        if (ext.Length > 1) kind += $" · {ext[1..].ToUpperInvariant()}";
        c.DrawText(kind, new RectF(tx, y + 22 * S, tw, 18 * S), F(12), T.TextSecondary);
        y += 42 * S + 16 * S;

        c.DrawText(Fmt.Size(n.Size), new RectF(x, y, w, 32 * S), F(26, Weight.Semibold), T.TextPrimary);
        y += 34 * S;
        double total = Math.Max(1, _root?.Size ?? 1);
        double frac = n.Size / total;
        string pct = $"{Fmt.Percent(frac)} {Strings.OfTotal}";
        if (_view is not null && !ReferenceEquals(n, _view) && !ReferenceEquals(_view, _root))
            pct += $"  ·  {Fmt.Percent(n.Size / Math.Max(1.0, _view.Size))} {Strings.OfView}";
        c.DrawText(pct, new RectF(x, y, w, 18 * S), F(12), T.TextSecondary);
        y += 22 * S;
        var bar = new RectF(x, y, w, 5 * S);
        c.FillRoundRect(bar, 2.5f * S, T.BarTrack);
        if (frac > 0) c.FillRoundRect(bar.WithW(Math.Max(5 * S, (float)(w * Math.Min(1, frac)))), 2.5f * S, T.Accent);
        y += 5 * S + 14 * S;

        var fl = F(12.5f);
        var fv = F(12.5f, Weight.Medium);
        void Row(string label, string value)
        {
            c.DrawText(label, new RectF(x, y, w * 0.45f, 20 * S), fl, T.TextSecondary);
            c.DrawText(value, new RectF(x + w * 0.35f, y, w * 0.65f, 20 * S), fv, T.TextPrimary, TextAlign.Right);
            y += 22 * S;
        }
        if (n.IsDirectory)
        {
            Row(Strings.FilesTitle, Fmt.Count(n.FileCount));
            Row(Strings.FoldersTitle, Fmt.Count(n.FolderCount));
        }
        Row(Strings.Modified, Fmt.Date(n.LastWriteUtcTicks));

        var pathRect = new RectF(x, y + 2 * S, w, 18 * S);
        c.DrawText(n.FullPath, pathRect, F(11.5f), Hot(ZInfoPath) ? T.TextSecondary : T.TextMuted, TextAlign.Left, Trim.Middle);
        AddZone(ZInfoPath, pathRect, () => Execute(Cmd.CopyPath), n.FullPath);
        return pathRect.Bottom;
    }

    float PaintActions(ICanvas c, float x, float y, float w)
    {
        (Icon icon, string label, string tip, Cmd cmd)[] actions =
        [
            (Icons.External, Strings.OpenItem, Strings.OpenItem, Cmd.OpenItem),
            (Icons.FolderSearch, Strings.RevealShort, Strings.Reveal, Cmd.RevealItem),
            (Icons.Copy, Strings.CopyShort, Strings.CopyPath, Cmd.CopyPath),
            (Icons.Trash, Strings.TrashShort, Strings.Trash, Cmd.TrashItem),
        ];
        float gap = 8 * S, bw = (w - gap * 3) / 4, bh = 58 * S;
        var f = F(11.5f, Weight.Medium);
        for (int i = 0; i < actions.Length; i++)
        {
            var (icon, label, tip, cmd) = actions[i];
            var r = new RectF(x + i * (bw + gap), y, bw, bh);
            bool enabled = IsEnabled(cmd);
            int id = ZAction + i;
            bool hot = enabled && Hot(id), down = enabled && Down(id);
            bool danger = cmd == Cmd.TrashItem;
            c.FillRoundRect(r, 10 * S, down ? T.SurfacePressed : hot ? (danger ? T.DangerSoft.Over(T.SidebarBg) : T.SurfaceHover) : T.Surface);
            c.StrokeRoundRect(r.Deflate(Hair / 2), 10 * S, hot && danger ? T.Danger.WithAlpha((byte)120) : T.SurfaceBorder, Hair);
            var fg = !enabled ? T.TextDisabled : danger && hot ? T.Danger : hot ? T.TextPrimary : T.TextSecondary;
            c.DrawIcon(icon, new RectF(r.CenterX - 9 * S, r.Y + 11 * S, 18 * S, 18 * S), fg, 1.7f * S);
            c.DrawText(label, new RectF(r.X + 2 * S, r.Y + 33 * S, r.W - 4 * S, 16 * S), f, enabled ? (danger && hot ? T.Danger : T.TextPrimary) : T.TextDisabled, TextAlign.Center);
            AddZone(id, r, enabled ? () => Execute(cmd) : null, tip, enabled);
        }
        return y + bh;
    }

    float PaintLegend(ICanvas c, float x, float y, float w)
    {
        if (_mode == ColorMode.Depth)
        {
            SectionLabel(c, Strings.Levels, x, y, w);
            y += 22 * S;
            float gap = 5 * S, sw = (w - gap * 7) / 8;
            for (int i = 0; i < 8; i++)
            {
                var r = new RectF(x + i * (sw + gap), y, sw, 20 * S);
                var col = T.Level(i);
                c.FillRoundRectGradient(r, 5 * S, col.Lighten(0.12f), col.Darken(0.08f));
                c.DrawText((i + 1).ToString(), r, F(11, Weight.Semibold), T.LabelOn(col), TextAlign.Center, Trim.None);
            }
            return y + 20 * S + 20 * S;
        }
        if (_mode == ColorMode.Age)
        {
            SectionLabel(c, Strings.AgeTitle, x, y, w);
            y += 22 * S;
            var bar = new RectF(x, y, w, 10 * S);
            const int steps = 48;
            var stops = Theme.AgeStopDays;
            for (int i = 0; i < steps; i++)
            {
                // Evenly spaced along the (log) scale of the stops.
                float t = i / (float)(steps - 1);
                float pos = t * (stops.Length - 1);
                int k = Math.Min((int)pos, stops.Length - 2);
                var col = Color.Lerp(T.AgeStops[k], T.AgeStops[k + 1], pos - k);
                c.FillRect(new RectF(bar.X + bar.W * i / steps, bar.Y, bar.W / steps + Hair, bar.H), col);
            }
            y += 14 * S;
            var labels = Strings.AgeLabels;
            var lf = F(10.5f);
            for (int i = 0; i < labels.Length; i++)
            {
                float lx = x + w * i / (labels.Length - 1);
                var align = i == 0 ? TextAlign.Left : i == labels.Length - 1 ? TextAlign.Right : TextAlign.Center;
                float lw = 44 * S;
                var lr = align switch
                {
                    TextAlign.Left => new RectF(lx, y, lw, 16 * S),
                    TextAlign.Right => new RectF(lx - lw, y, lw, 16 * S),
                    _ => new RectF(lx - lw / 2, y, lw, 16 * S),
                };
                c.DrawText(labels[i], lr, lf, T.TextMuted, align, Trim.None);
            }
            return y + 16 * S + 20 * S;
        }
        return y;
    }

    float PaintTypes(ICanvas c, float x, float y, float w, int maxRows)
    {
        var view = _view;
        if (view?.CategorySizes is not { } sizes || maxRows <= 0) return y;
        SectionLabel(c, Strings.FileTypes, x, y, w);
        y += 22 * S;
        var order = Enumerable.Range(0, sizes.Length).Where(i => sizes[i] > 0).OrderByDescending(i => sizes[i]).Take(maxRows).ToArray();
        double total = Math.Max(1, view.Size);
        var fn = F(12.5f);
        var fs = F(12);
        foreach (int i in order)
        {
            var cat = (FileCategory)i;
            var row = new RectF(x - 7 * S, y - 2 * S, w + 14 * S, 28 * S);
            int id = ZType + i;
            bool active = _highlight == cat, hot = Hot(id);
            if (active) c.FillRoundRect(row, 8 * S, T.AccentSoft);
            else if (hot) c.FillRoundRect(row, 8 * S, T.SurfaceHover);
            var col = _mode == ColorMode.Type ? T.Category(cat) : T.TextMuted;
            c.FillRoundRect(new RectF(x, y + 5 * S, 10 * S, 10 * S), 3 * S, col);
            double frac = sizes[i] / total;
            string size = Fmt.Size(sizes[i]);
            float sw = c.MeasureText(size, fs);
            c.DrawText(Strings.CategoryName(cat), new RectF(x + 18 * S, y, w - 26 * S - sw, 20 * S), fn, T.TextPrimary);
            c.DrawText(size, new RectF(x, y, w, 20 * S), fs, T.TextSecondary, TextAlign.Right);
            var bar = new RectF(x + 18 * S, y + 20 * S, w - 18 * S, 3 * S);
            c.FillRoundRect(bar, 1.5f * S, T.BarTrack);
            c.FillRoundRect(bar.WithW(Math.Max(3 * S, (float)(bar.W * frac))), 1.5f * S, _mode == ColorMode.Type ? col : T.Accent);
            AddZone(id, row, () => ToggleHighlight(cat), $"{Strings.CategoryName(cat)}: {Fmt.Percent(frac)}");
            y += 28 * S;
        }
        return y;
    }

    void PaintLargest(ICanvas c, float x, float y, float w, float bottom)
    {
        SectionLabel(c, Strings.Largest, x, y, w);
        y += 22 * S;
        var fn = F(12.5f);
        var fs = F(12);
        int baseDepth = _view?.Depth ?? 0;
        for (int i = 0; i < _largest.Count; i++)
        {
            if (y + 26 * S > bottom) break;
            var file = _largest[i];
            var row = new RectF(x - 7 * S, y, w + 14 * S, 26 * S);
            int id = ZLargest + i;
            bool sel = ReferenceEquals(file, _selected), hot = Hot(id);
            if (sel) c.FillRoundRect(row, 7 * S, T.AccentSoft);
            else if (hot) c.FillRoundRect(row, 7 * S, T.SurfaceHover);
            c.FillRoundRect(new RectF(x, y + 8 * S, 10 * S, 10 * S), 3 * S, _painter.NodeColor(file, file.Depth - baseDepth - 2));
            string size = Fmt.Size(file.Size);
            float sw = c.MeasureText(size, fs);
            c.DrawText(file.Name, new RectF(x + 18 * S, y, w - 26 * S - sw, 26 * S), fn, T.TextPrimary);
            c.DrawText(size, new RectF(x, y, w, 26 * S), fs, T.TextSecondary, TextAlign.Right);
            AddZone(id, row, () => Select(file), file.FullPath,
                doubleClick: () => { if (file.Parent is { } p && !ReferenceEquals(p, _view)) NavigateTo(p, select: file); else Select(file); });
            y += 27 * S;
        }
    }

    void SectionLabel(ICanvas c, string text, float x, float y, float w) =>
        c.DrawText(text, new RectF(x, y, w, 16 * S), F(11, Weight.Semibold), T.TextMuted, TextAlign.Left, Trim.End);

    // ------------------------------------------------------------------ status bar

    void PaintStatusBar(ICanvas c, RectF r)
    {
        c.FillRect(r, T.StatusBg);
        c.FillRect(new RectF(0, r.Y, W, Hair), T.Divider);
        var view = _view;
        if (view is null) return;
        var f = F(12);
        float x = r.X + 14 * S;

        string left = $"{Strings.FilesCount(view.FileCount)}  ·  {Strings.FoldersCount(view.FolderCount)}  ·  {Fmt.Size(view.Size)}";
        float lw = c.MeasureText(left, f);
        c.DrawText(left, new RectF(x, r.Y, lw + 2, r.H), f, T.TextSecondary, TextAlign.Left, Trim.None);
        x += lw + 14 * S;
        if (ReferenceEquals(view, _root) && _scanTime > TimeSpan.Zero)
        {
            string t = Strings.ScannedIn(Fmt.Duration(_scanTime));
            float tw = c.MeasureText(t, f);
            c.DrawText(t, new RectF(x, r.Y, tw + 2, r.H), f, T.TextMuted, TextAlign.Left, Trim.None);
            x += tw + 14 * S;
        }
        if (_scanErrors > 0)
        {
            var warn = Color.Hex(0xF2B233);
            c.DrawIcon(Icons.Alert, new RectF(x, r.CenterY - 7 * S, 14 * S, 14 * S), warn, 1.8f * S);
            string t = Strings.Unreadable(_scanErrors);
            float tw = c.MeasureText(t, f);
            c.DrawText(t, new RectF(x + 19 * S, r.Y, tw + 2, r.H), f, T.Dark ? warn : warn.Darken(0.35f), TextAlign.Left, Trim.None);
            x += 19 * S + tw + 14 * S;
        }

        float rx = r.Right - 14 * S;
        if (_volume is not null)
        {
            var bar = new RectF(rx - 70 * S, r.CenterY - 3 * S, 70 * S, 6 * S);
            c.FillRoundRect(bar, 3 * S, T.BarTrack);
            float used = (float)_volume.UsedFraction;
            c.FillRoundRect(bar.WithW(Math.Max(6 * S, bar.W * used)), 3 * S, used > 0.9f ? T.Danger : T.Accent);
            string free = $"{Strings.FreeLabel}: {Strings.FreeOf(Fmt.Size(_volume.FreeSpace), Fmt.Size(_volume.TotalSize))}";
            float fw = c.MeasureText(free, f);
            rx = bar.X - 10 * S;
            c.DrawText(free, new RectF(rx - fw, r.Y, fw + 2, r.H), f, T.TextSecondary, TextAlign.Right, Trim.None);
            rx -= fw + 18 * S;
        }

        var shown = _hoverCell?.Node ?? _selected;
        if (shown is not null && rx - x > 80 * S)
        {
            string text = _hoverCell?.Kind == CellKind.Others
                ? $"{shown.FullPath}  —  {Strings.SmallItems(_hoverCell.OthersCount)}"
                : $"{shown.FullPath}  —  {Fmt.Size(shown.Size)}";
            c.DrawText(text, RectF.FromLTRB(x, r.Y, rx, r.Bottom), f, T.TextMuted, TextAlign.Right, Trim.Middle);
        }
    }

    // =====================================================================================
    // Welcome screen
    // =====================================================================================

    void PaintWelcome(ICanvas c)
    {
        float colW = Math.Min(760 * S, W - 64 * S);
        float cardW = 232 * S, cardH = 100 * S, gap = 14 * S;
        int count = Math.Max(1, _volumes.Count);
        int cols = Math.Clamp((int)((colW + gap) / (cardW + gap)), 1, count);
        int rows = (count + cols - 1) / cols;
        float gridW = cols * cardW + (cols - 1) * gap;
        float contentH = (72 + 18 + 40 + 26 + 44 + 30) * S + rows * cardH + (rows - 1) * gap + (40 + 44 + 30) * S;
        float y = Math.Max(36 * S, (H - contentH) / 2);
        float cx = W / 2;

        PaintLogo(c, new RectF(cx - 36 * S, y, 72 * S, 72 * S));
        y += 72 * S + 18 * S;
        c.DrawText(Strings.AppName, new RectF(0, y, W, 40 * S), F(32, Weight.Bold), T.TextPrimary, TextAlign.Center);
        y += 40 * S;
        c.DrawText(Strings.Tagline, new RectF(0, y, W, 24 * S), F(15), T.TextSecondary, TextAlign.Center);
        y += 26 * S + 44 * S;

        float gx = cx - gridW / 2;
        SectionLabel(c, Strings.DrivesTitle, gx, y, gridW);
        y += 30 * S;
        if (_volumes.Count == 0)
        {
            c.DrawText(_volumesLoaded ? Strings.NoDrives : "…", new RectF(gx, y, gridW, cardH), F(13), T.TextMuted, TextAlign.Center);
        }
        for (int i = 0; i < _volumes.Count; i++)
        {
            int col = i % cols, row = i / cols;
            var r = new RectF(gx + col * (cardW + gap), y + row * (cardH + gap), cardW, cardH);
            var vol = _volumes[i];
            PaintDriveCard(c, ZDrive + i, r, vol);
        }
        y += rows * cardH + (rows - 1) * gap + 40 * S;

        float bh = 40 * S;
        var fb = F(13.5f, Weight.Semibold);
        float w1 = MeasureButton(c, Icons.FolderOpen, Strings.ChooseFolder, false, fb) + 8 * S;
        float w2 = MeasureButton(c, Icons.House, Strings.HomeFolder, false, fb) + 8 * S;
        string? resume = _root is not null ? CrumbLabel(_root) : null;
        float w3 = resume is null ? 0 : MeasureButton(c, Icons.ArrowRight, resume, false, fb) + 8 * S;
        float total = w1 + 12 * S + w2 + (resume is null ? 0 : 12 * S + w3);
        float bx = cx - total / 2;
        Button(c, ZChoose, new RectF(bx, y, w1, bh), Icons.FolderOpen, Strings.ChooseFolder, Btn.Primary, () => Execute(Cmd.OpenFolder), font: fb);
        bx += w1 + 12 * S;
        Button(c, ZHomeFolder, new RectF(bx, y, w2, bh), Icons.House, Strings.HomeFolder, Btn.Secondary, () => Execute(Cmd.HomeFolder), font: fb);
        bx += w2 + 12 * S;
        if (resume is not null)
            Button(c, ZResume, new RectF(bx, y, w3, bh), Icons.ArrowRight, resume, Btn.Ghost, () => { _screen = Screen.Browse; UpdateTitle(); P.Invalidate(); }, _root!.FullPath, font: fb);
        y += bh + 22 * S;
        c.DrawText(Strings.DropHint, new RectF(0, y, W, 20 * S), F(12.5f), T.TextMuted, TextAlign.Center);

        c.DrawText($"v{Version}", new RectF(0, H - 30 * S, W - 16 * S, 20 * S), F(11.5f), T.TextMuted, TextAlign.Right);
    }

    void PaintDriveCard(ICanvas c, int id, RectF r, VolumeInfo v)
    {
        bool hot = Hot(id), down = Down(id);
        if (hot && !down) c.DrawShadow(r, 14 * S, 16 * S, T.Shadow.WithAlpha((byte)(T.Dark ? 120 : 45)));
        c.FillRoundRect(r, 14 * S, down ? T.SurfacePressed : hot ? T.SurfaceHover : T.Surface);
        c.StrokeRoundRect(r.Deflate(Hair / 2), 14 * S, hot ? T.Accent.WithAlpha((byte)150) : T.SurfaceBorder, Hair);

        var tile = new RectF(r.X + 16 * S, r.Y + 16 * S, 36 * S, 36 * S);
        c.FillRoundRect(tile, 10 * S, T.AccentSoft.Over(hot ? T.SurfaceHover : T.Surface));
        c.DrawIcon(Icons.HardDrive, tile.CenterBox(20 * S, 20 * S), T.Accent, 1.8f * S);
        float tx = tile.Right + 12 * S, tw = r.Right - 16 * S - tx;
        c.DrawText(v.Label, new RectF(tx, r.Y + 16 * S, tw, 19 * S), F(14, Weight.Semibold), T.TextPrimary);
        c.DrawText(v.RootPath, new RectF(tx, r.Y + 35 * S, tw, 17 * S), F(11.5f), T.TextMuted, TextAlign.Left, Trim.Middle);

        var bar = new RectF(r.X + 16 * S, r.Bottom - 36 * S, r.W - 32 * S, 6 * S);
        c.FillRoundRect(bar, 3 * S, T.BarTrack);
        float used = (float)v.UsedFraction;
        var fill = used > 0.9f ? T.Danger : used > 0.75f ? Color.Hex(0xF2B233) : T.Accent;
        if (used > 0) c.FillRoundRect(bar.WithW(Math.Max(6 * S, bar.W * used)), 3 * S, fill);
        c.DrawText(Strings.FreeOf(Fmt.Size(v.FreeSpace), Fmt.Size(v.TotalSize)), new RectF(r.X + 16 * S, r.Bottom - 26 * S, r.W - 32 * S, 17 * S), F(12), T.TextSecondary);
        string path = v.RootPath;
        AddZone(id, r, () => StartScan(path), path);
    }

    // =====================================================================================
    // Scanning screen
    // =====================================================================================

    void PaintScanning(ICanvas c)
    {
        var p = _progress;
        if (p is null) return;
        var elapsed = Stopwatch.GetElapsedTime(_scanStart);
        float cw = Math.Min(500 * S, W - 48 * S), ch = 268 * S;
        var card = new RectF(MathF.Round((W - cw) / 2), MathF.Round((H - ch) / 2 - 16 * S), cw, ch);
        c.DrawShadow(card, 18 * S, 36 * S, T.Shadow);
        c.FillRoundRect(card, 18 * S, T.Surface);
        c.StrokeRoundRect(card.Deflate(Hair / 2), 18 * S, T.SurfaceBorder, Hair);

        float pad = 28 * S;
        var spin = new RectF(card.X + pad, card.Y + pad, 38 * S, 38 * S);
        c.StrokeArc(spin.Deflate(2 * S), 0, 360, T.BarTrack, 3.5f * S);
        float angle = (float)(elapsed.TotalMilliseconds * 0.4 % 360);
        c.StrokeArc(spin.Deflate(2 * S), angle, 110, T.Accent, 3.5f * S);

        float tx = spin.Right + 16 * S, tw = card.Right - pad - tx;
        c.DrawText(Strings.Scanning + "…", new RectF(tx, card.Y + pad - 2 * S, tw, 24 * S), F(18, Weight.Semibold), T.TextPrimary);
        c.DrawText(_scanPath, new RectF(tx, card.Y + pad + 21 * S, tw, 19 * S), F(12.5f), T.TextSecondary, TextAlign.Left, Trim.Middle);

        float sy = card.Y + 100 * S, colW = (card.W - pad * 2) / 3;
        Stat(c, card.X + pad, sy, colW, Fmt.Count(Interlocked.Read(ref p.Files)), Strings.FilesTitle);
        Stat(c, card.X + pad + colW, sy, colW, Fmt.Count(Interlocked.Read(ref p.Folders)), Strings.FoldersTitle);
        Stat(c, card.X + pad + colW * 2, sy, colW, Fmt.Size(Interlocked.Read(ref p.Bytes)), Strings.SizeTitle);

        // Progress: exact when scanning a whole volume (we know how much is used), indeterminate otherwise.
        var bar = new RectF(card.X + pad, card.Y + 166 * S, card.W - pad * 2, 5 * S);
        c.FillRoundRect(bar, 2.5f * S, T.BarTrack);
        if (_scanExpected > 0)
        {
            float frac = Math.Clamp(Interlocked.Read(ref p.Bytes) / (float)_scanExpected, 0, 1);
            c.FillRoundRect(bar.WithW(Math.Max(5 * S, bar.W * frac)), 2.5f * S, T.Accent);
        }
        else
        {
            float seg = bar.W * 0.28f;
            float pos = (float)(elapsed.TotalMilliseconds % 1400 / 1400) * (bar.W + seg) - seg;
            var s = RectF.FromLTRB(Math.Max(bar.X, bar.X + pos), bar.Y, Math.Min(bar.Right, bar.X + pos + seg), bar.Bottom);
            if (s.W > 1) c.FillRoundRect(s, 2.5f * S, T.Accent);
        }
        c.DrawText(p.CurrentPath, new RectF(card.X + pad, card.Y + 178 * S, card.W - pad * 2, 18 * S), F(11.5f), T.TextMuted, TextAlign.Left, Trim.Middle);

        float by = card.Bottom - pad - 34 * S;
        c.DrawText($"{Strings.Elapsed}: {Fmt.Duration(elapsed)}", new RectF(card.X + pad, by, 220 * S, 34 * S), F(12.5f), T.TextSecondary);
        float bw = MeasureButton(c, null, Strings.Cancel, false) + 8 * S;
        Button(c, ZCancel, new RectF(card.Right - pad - bw, by, bw, 34 * S), null, Strings.Cancel, Btn.Secondary, CancelScan, "Esc");
    }

    void Stat(ICanvas c, float x, float y, float w, string value, string label)
    {
        c.DrawText(value, new RectF(x, y, w - 8 * S, 32 * S), F(23, Weight.Semibold), T.TextPrimary);
        c.DrawText(label, new RectF(x, y + 32 * S, w - 8 * S, 18 * S), F(12), T.TextMuted);
    }

    // =====================================================================================
    // Dialogs, toasts, tooltips
    // =====================================================================================

    void PaintDialog(ICanvas c)
    {
        c.FillRect(new RectF(0, 0, W, H), T.Overlay);
        AddZone(ZDlgBackdrop, new RectF(0, 0, W, H), CloseDialog, hand: false);
        if (_dialog == DialogKind.Trash && _dialogNode is { } n) PaintTrashDialog(c, n);
        else PaintAboutDialog(c);
    }

    RectF DialogCard(ICanvas c, float w, float h)
    {
        var r = new RectF(MathF.Round((W - w) / 2), MathF.Round((H - h) / 2 - 20 * S), w, h);
        c.DrawShadow(r, 18 * S, 40 * S, T.Shadow);
        c.FillRoundRect(r, 18 * S, T.Surface);
        c.StrokeRoundRect(r.Deflate(Hair / 2), 18 * S, T.SurfaceBorder, Hair);
        AddZone(ZDlgCard, r, hand: false);
        return r;
    }

    void PaintTrashDialog(ICanvas c, FileNode n)
    {
        float w = Math.Min(480 * S, W - 48 * S);
        var r = DialogCard(c, w, 204 * S);
        float pad = 26 * S;
        var icon = new RectF(r.X + pad, r.Y + pad, 44 * S, 44 * S);
        c.FillEllipse(icon, T.DangerSoft.Over(T.Surface));
        c.DrawIcon(Icons.Trash, icon.CenterBox(22 * S, 22 * S), T.Danger, 1.8f * S);
        float tx = icon.Right + 16 * S, tw = r.Right - pad - tx;
        c.DrawText(Strings.ConfirmTrashTitle, new RectF(tx, r.Y + pad, tw, 24 * S), F(17, Weight.Semibold), T.TextPrimary);
        c.DrawText(n.Name, new RectF(tx, r.Y + pad + 28 * S, tw, 20 * S), F(13.5f, Weight.Semibold), T.TextPrimary);
        string info = n.IsDirectory
            ? $"{Fmt.Size(n.Size)}  ·  {Strings.FilesCount(n.FileCount)}  ·  {Strings.FoldersCount(n.FolderCount)}"
            : $"{Fmt.Size(n.Size)}  ·  {Strings.CategoryName(n.Category)}";
        c.DrawText(info, new RectF(tx, r.Y + pad + 50 * S, tw, 19 * S), F(12.5f), T.TextSecondary);
        c.DrawText(n.FullPath, new RectF(tx, r.Y + pad + 72 * S, tw, 18 * S), F(11.5f), T.TextMuted, TextAlign.Left, Trim.Middle);

        var fb = F(13, Weight.Semibold);
        float bh = 36 * S, by = r.Bottom - pad - bh;
        float okW = MeasureButton(c, Icons.Trash, Strings.Trash, false, fb) + 8 * S;
        float cancelW = MeasureButton(c, null, Strings.Cancel, false, fb) + 16 * S;
        Button(c, ZDlgOk, new RectF(r.Right - pad - okW, by, okW, bh), Icons.Trash, Strings.Trash, Btn.Danger, ConfirmDialog, font: fb);
        Button(c, ZDlgCancel, new RectF(r.Right - pad - okW - 10 * S - cancelW, by, cancelW, bh), null, Strings.Cancel, Btn.Secondary, CloseDialog, font: fb);
    }

    void PaintAboutDialog(ICanvas c)
    {
        float w = Math.Min(540 * S, W - 48 * S);
        var r = DialogCard(c, w, 340 * S);
        float y = r.Y + 30 * S;
        PaintLogo(c, new RectF(r.CenterX - 30 * S, y, 60 * S, 60 * S));
        y += 72 * S;
        c.DrawText(Strings.AppName, new RectF(r.X, y, r.W, 30 * S), F(22, Weight.Bold), T.TextPrimary, TextAlign.Center);
        y += 30 * S;
        var inner = new RectF(r.X + 24 * S, 0, r.W - 48 * S, 18 * S);
        PaintVersionLine(c, inner.WithY(y));
        y += 30 * S;
        c.DrawText(Strings.AboutLine1, inner.WithY(y), F(12.5f), T.TextSecondary, TextAlign.Center);
        y += 20 * S;
        c.DrawText(Strings.AboutLine2, inner.WithY(y), F(12.5f), T.TextSecondary, TextAlign.Center);
        y += 20 * S;
        c.DrawText(Strings.AboutLine3, inner.WithY(y), F(11.5f), T.TextMuted, TextAlign.Center);
        y += 30 * S;
        var fl = F(12.5f, Weight.Medium);
        string repo = GitHub.Repo["https://".Length..];
        float lw = MeasureButton(c, Icons.GitHub, repo, false, fl);
        Button(c, ZDlgLink, new RectF(r.CenterX - lw / 2, y, lw, 28 * S), Icons.GitHub, repo, Btn.Link, () => OpenWeb(GitHub.Repo), font: fl);

        // "Check for updates", which becomes "Download 1.2.0" once it has found a newer version; then "Close".
        var fb = F(13, Weight.Semibold);
        bool newer = _update == UpdateState.Available && _latest is not null;
        var icon = newer ? Icons.Download : Icons.Refresh;
        string label = newer ? Strings.Download(_latest!.Value.Version) : Strings.CheckForUpdates;
        float uw = MeasureButton(c, icon, label, false, fb) + 8 * S;
        float cw = MeasureButton(c, null, Strings.Close, false, fb) + 24 * S;
        float bh = 36 * S, bx = MathF.Round(r.CenterX - (uw + 10 * S + cw) / 2), by = r.Bottom - 26 * S - bh;
        Button(c, ZDlgUpdate, new RectF(bx, by, uw, bh), icon, label, newer ? Btn.Primary : Btn.Secondary,
            newer ? OpenLatestRelease : CheckForUpdates, enabled: _update != UpdateState.Checking, font: fb);
        Button(c, ZDlgOk, new RectF(bx + uw + 10 * S, by, cw, bh), null, Strings.Close, Btn.Secondary, CloseDialog, font: fb);
    }

    /// <summary>"Version 1.0.0 · .NET 10.0", with the result of the update check, if any, after the version.</summary>
    void PaintVersionLine(ICanvas c, RectF r)
    {
        var f = F(12);
        string head = Strings.Version(Version), tail = " · .NET " + Environment.Version.ToString(2);
        var (status, color) = _update switch
        {
            UpdateState.Checking => (Strings.CheckingForUpdates, T.TextMuted),
            UpdateState.UpToDate => (Strings.UpToDate, T.TextSecondary),
            UpdateState.Available when _latest is { } latest => (Strings.NewVersion(latest.Version), T.Dark ? T.AccentHover : T.Accent),
            UpdateState.Failed => (Strings.UpdateCheckFailed, T.Danger),
            _ => ((string?)null, T.TextMuted),
        };
        if (status is null)
        {
            c.DrawText(head + tail, r, f, T.TextMuted, TextAlign.Center);
            return;
        }
        head += " · ";
        float hw = c.MeasureText(head, f), sw = c.MeasureText(status, f), tw = c.MeasureText(tail, f);
        if (hw + sw + tw > r.W) { tail = ""; tw = 0; } // the .NET version is the first thing to go
        float x = MathF.Round(r.CenterX - (hw + sw + tw) / 2);
        c.DrawText(head, new RectF(x, r.Y, hw + 2 * S, r.H), f, T.TextMuted, TextAlign.Left, Trim.None);
        c.DrawText(status, new RectF(x + hw, r.Y, sw + 2 * S, r.H), f, color, TextAlign.Left, Trim.None);
        if (tw > 0) c.DrawText(tail, new RectF(x + hw + sw, r.Y, tw + 2 * S, r.H), f, T.TextMuted, TextAlign.Left, Trim.None);
    }

    void PaintToast(ICanvas c)
    {
        if (_toast is null) return;
        var f = F(13, Weight.Medium);
        float tw = Math.Min(c.MeasureText(_toast, f) + 58 * S, W - 40 * S);
        float bottom = _screen == Screen.Browse ? H - 28 * S - 22 * S : H - 30 * S;
        var r = new RectF(MathF.Round((W - tw) / 2), bottom - 40 * S, tw, 40 * S);
        var bg = T.Dark ? Color.Hex(0x2B2F3A) : Color.Hex(0x22252D);
        c.DrawShadow(r, 20 * S, 22 * S, T.Shadow);
        c.FillRoundRect(r, 20 * S, bg);
        var iconColor = _toastError ? Color.Hex(0xF3748A) : Color.Hex(0x5EE0A0);
        c.DrawIcon(_toastError ? Icons.Alert : Icons.Check, new RectF(r.X + 16 * S, r.CenterY - 8 * S, 16 * S, 16 * S), iconColor, 2 * S);
        c.DrawText(_toast, RectF.FromLTRB(r.X + 40 * S, r.Y, r.Right - 18 * S, r.Bottom), f, Color.Hex(0xF2F3F6));
    }

    void PaintTooltip(ICanvas c)
    {
        if (!_tipShown || _hot < 0 || _hot == ZTreemap) return;
        var z = FindZone(_hot);
        if (z?.Tip is not { } tip) return;
        var f = F(12);
        float tw = Math.Min(c.MeasureText(tip, f) + 20 * S, W - 16 * S), th = 26 * S;
        float x = Math.Clamp(z.R.CenterX - tw / 2, 8 * S, W - tw - 8 * S);
        float y = z.R.Bottom + 6 * S;
        if (y + th > H - 6 * S) y = z.R.Y - th - 6 * S;
        var r = new RectF(MathF.Round(x), MathF.Round(y), tw, th);
        c.DrawShadow(r, 7 * S, 10 * S, T.Shadow.WithAlpha((byte)(T.Dark ? 140 : 50)));
        c.FillRoundRect(r, 7 * S, T.TooltipBg);
        c.StrokeRoundRect(r.Deflate(Hair / 2), 7 * S, T.TooltipBorder, Hair);
        c.DrawText(tip, r.Deflate(10 * S, 0), f, T.TextPrimary, TextAlign.Center, Trim.Middle);
    }

    // =====================================================================================
    // Widgets
    // =====================================================================================

    void IconButton(ICanvas c, int id, RectF r, Icon icon, Action? click, string? tip, bool enabled = true, bool active = false)
    {
        bool hot = enabled && Hot(id), down = enabled && Down(id);
        if (down) c.FillRoundRect(r, 8 * S, T.SurfacePressed);
        else if (hot) c.FillRoundRect(r, 8 * S, T.SurfaceHover);
        else if (active) c.FillRoundRect(r, 8 * S, T.Surface);
        var col = !enabled ? T.TextDisabled : hot || active ? T.TextPrimary : T.TextSecondary;
        c.DrawIcon(icon, r.CenterBox(18 * S, 18 * S), col, 1.75f * S);
        AddZone(id, r, enabled ? click : null, tip, enabled);
    }

    float MeasureButton(ICanvas c, Icon? icon, string text, bool chevron, FontSpec? font = null)
    {
        var f = font ?? F(13, Weight.Medium);
        return 14 * S * 2 + (icon is not null ? 24 * S : 0) + c.MeasureText(text, f) + (chevron ? 20 * S : 0);
    }

    void Button(ICanvas c, int id, RectF r, Icon? icon, string text, Btn style, Action? click, string? tip = null,
                bool enabled = true, bool chevron = false, FontSpec? font = null)
    {
        var f = font ?? F(13, Weight.Medium);
        bool hot = enabled && Hot(id), down = enabled && Down(id);
        Color bg, fg, border = Color.Transparent;
        switch (style)
        {
            case Btn.Primary:
                bg = down ? T.AccentPressed : hot ? T.AccentHover : T.Accent;
                fg = T.OnAccent;
                break;
            case Btn.Danger:
                bg = down ? T.Danger.Darken(0.12f) : hot ? T.DangerHover : T.Danger;
                fg = Color.White;
                break;
            case Btn.Ghost:
                bg = down ? T.SurfacePressed : hot ? T.SurfaceHover : Color.Transparent;
                fg = hot ? T.TextPrimary : T.TextSecondary;
                break;
            case Btn.Link:
                bg = down ? T.AccentSoft.Over(T.SurfacePressed) : hot ? T.AccentSoft.Over(T.Surface) : Color.Transparent;
                fg = T.Dark ? T.AccentHover : T.Accent; // the lighter accent reads better on dark surfaces
                break;
            default:
                bg = down ? T.SurfacePressed : hot ? T.SurfaceHover : T.Surface;
                fg = T.TextPrimary;
                border = T.SurfaceBorder;
                break;
        }
        if (!enabled) fg = T.TextDisabled;
        float rad = Math.Min(9 * S, r.H / 2);
        if (style == Btn.Primary && !down) c.DrawShadow(r, rad, 10 * S, T.Accent.WithAlpha((byte)(T.Dark ? 70 : 60)));
        if (bg.A > 0) c.FillRoundRect(r, rad, bg);
        if (border.A > 0) c.StrokeRoundRect(r.Deflate(Hair / 2), rad, border, Hair);

        float tw = c.MeasureText(text, f);
        float contentW = tw + (icon is not null ? 24 * S : 0) + (chevron ? 20 * S : 0);
        float x = r.X + (r.W - contentW) / 2;
        if (icon is not null)
        {
            c.DrawIcon(icon, new RectF(x, r.CenterY - 8 * S, 16 * S, 16 * S), fg, 1.8f * S);
            x += 24 * S;
        }
        c.DrawText(text, new RectF(x, r.Y, tw + 2 * S, r.H), f, fg, TextAlign.Left, Trim.None);
        if (chevron)
            c.DrawIcon(Icons.ChevronDown, new RectF(x + tw + 6 * S, r.CenterY - 7 * S, 14 * S, 14 * S), fg, 1.8f * S);
        AddZone(id, r, enabled ? click : null, tip, enabled);
    }

    /// <summary>The app logo: a little colorful treemap on an accent tile.</summary>
    internal static void DrawLogo(ICanvas c, RectF r)
    {
        float rad = r.W * 0.24f;
        c.FillRoundRectGradient(r, rad, Color.Hex(0x9A8FFF), Color.Hex(0x5A48DB));
        var inner = r.Deflate(r.W * 0.17f);
        float g = Math.Max(1, r.W * 0.05f), br = r.W * 0.07f;
        var a = new RectF(inner.X, inner.Y, inner.W * 0.56f - g / 2, inner.H);
        float bx = a.Right + g, bw = inner.Right - bx;
        var b = new RectF(bx, inner.Y, bw, inner.H * 0.52f - g / 2);
        float cy = b.Bottom + g, chh = inner.Bottom - cy;
        var cc = new RectF(bx, cy, bw * 0.5f - g / 2, chh);
        var d = RectF.FromLTRB(cc.Right + g, cy, inner.Right, inner.Bottom);
        c.FillRoundRect(a, br, Color.Hex(0xFFFFFF));
        c.FillRoundRect(b, br, Color.Hex(0xFFD166));
        c.FillRoundRect(cc, br, Color.Hex(0x4FD1C5));
        c.FillRoundRect(d, br, Color.Hex(0xFF6B8B));
    }

    void PaintLogo(ICanvas c, RectF r) => DrawLogo(c, r);
}
