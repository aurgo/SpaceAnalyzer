using System.Diagnostics;

namespace SpaceAnalizer.UI;

/// <summary>Popup menus and the search box: drawn by the app, so they look the same on every OS.</summary>
public sealed partial class MainView
{
    const int TimerCaret = 8;

    // =====================================================================================
    // Popup menu
    // =====================================================================================

    sealed class Popup
    {
        public required List<MenuEntry> Items;
        public required Action<int> Pick;
        public float AnchorX, AnchorY;
        public bool AlignRight;
        public RectF Rect;
        public RectF[] ItemRects = [];
        public int Hover = -1;
        public bool Laid;
    }

    Popup? _popup;

    void OpenMenu(List<MenuEntry> items, float x, float y, Action<int> pick, bool alignRight = false)
    {
        _popup = new Popup { Items = items, Pick = pick, AnchorX = x, AnchorY = y, AlignRight = alignRight };
        _tipShown = false;
        _pressed = -1;
        ClearTreemapHover();
        P.SetCursor(CursorKind.Arrow);
        P.Invalidate();
    }

    void CloseMenu()
    {
        if (_popup is null) return;
        _popup = null;
        P.Invalidate();
    }

    void LayoutMenu(ICanvas c, Popup m)
    {
        var f = F(13);
        float itemH = 28 * S, sepH = 9 * S, pad = 6 * S;
        float w = 190 * S;
        foreach (var it in m.Items)
            if (!it.IsSeparator) w = Math.Max(w, c.MeasureText(it.Text!, f) + 58 * S);
        w = Math.Min(w, W - 16 * S);
        float h = pad * 2;
        foreach (var it in m.Items) h += it.IsSeparator ? sepH : itemH;
        float x = m.AlignRight ? m.AnchorX - w : m.AnchorX, y = m.AnchorY;
        if (x + w > W - 6 * S) x = W - 6 * S - w;
        if (y + h > H - 6 * S) y = Math.Max(6 * S, m.AnchorY - h);
        x = Math.Max(6 * S, x);
        m.Rect = new RectF(MathF.Round(x), MathF.Round(y), MathF.Round(w), MathF.Round(h));
        m.ItemRects = new RectF[m.Items.Count];
        float iy = m.Rect.Y + pad;
        for (int i = 0; i < m.Items.Count; i++)
        {
            float ih = m.Items[i].IsSeparator ? sepH : itemH;
            m.ItemRects[i] = new RectF(m.Rect.X + 5 * S, iy, m.Rect.W - 10 * S, ih);
            iy += ih;
        }
        m.Laid = true;
    }

    void PaintMenu(ICanvas c)
    {
        var m = _popup;
        if (m is null) return;
        LayoutMenu(c, m);
        var r = m.Rect;
        var bg = T.Dark ? Color.Hex(0x22252E) : Color.Hex(0xFFFFFF);
        c.DrawShadow(r, 10 * S, 20 * S, T.Shadow);
        c.FillRoundRect(r, 10 * S, bg);
        c.StrokeRoundRect(r.Deflate(Hair / 2), 10 * S, T.TooltipBorder, Hair);
        var f = F(13);
        for (int i = 0; i < m.Items.Count; i++)
        {
            var it = m.Items[i];
            var ir = m.ItemRects[i];
            if (it.IsSeparator)
            {
                c.FillRect(new RectF(ir.X + 6 * S, MathF.Round(ir.CenterY), ir.W - 12 * S, Hair), T.Divider);
                continue;
            }
            bool hot = i == m.Hover && it.Enabled;
            if (hot) c.FillRoundRect(ir, 6 * S, T.Accent);
            var fg = !it.Enabled ? T.TextDisabled : hot ? T.OnAccent : T.TextPrimary;
            if (it.Checked)
                c.DrawIcon(Icons.Check, new RectF(ir.X + 8 * S, ir.CenterY - 7 * S, 14 * S, 14 * S), fg, 2 * S);
            c.DrawText(it.Text!, RectF.FromLTRB(ir.X + 30 * S, ir.Y, ir.Right - 10 * S, ir.Bottom), f, fg);
        }
    }

    int MenuItemAt(float x, float y)
    {
        var m = _popup;
        if (m is null || !m.Laid) return -1;
        for (int i = 0; i < m.ItemRects.Length; i++)
            if (!m.Items[i].IsSeparator && m.ItemRects[i].Contains(x, y)) return i;
        return -1;
    }

    bool MenuMouseMove(float x, float y)
    {
        if (_popup is null) return false;
        int h = MenuItemAt(x, y);
        if (h != _popup.Hover)
        {
            _popup.Hover = h;
            P.Invalidate();
        }
        P.SetCursor(CursorKind.Arrow);
        return true;
    }

    bool MenuMouseDown(float x, float y)
    {
        if (_popup is null) return false;
        if (!_popup.Rect.Contains(x, y)) CloseMenu(); // a click outside just closes the menu
        return true;
    }

    bool MenuMouseUp(float x, float y)
    {
        var m = _popup;
        if (m is null) return false;
        int i = MenuItemAt(x, y);
        if (i >= 0 && m.Items[i].Enabled) PickMenu(m, i);
        return true;
    }

    void PickMenu(Popup m, int index)
    {
        CloseMenu();
        m.Pick(m.Items[index].Id);
        P.Invalidate();
    }

    bool MenuKey(Key key)
    {
        var m = _popup;
        if (m is null) return false;
        switch (key)
        {
            case Key.Escape:
                CloseMenu();
                break;
            case Key.Up:
            case Key.Down:
            {
                int n = m.Items.Count, i = m.Hover;
                for (int step = 0; step < n; step++)
                {
                    i = key == Key.Down ? (i + 1 + n) % n : (i - 1 + n) % n;
                    if (!m.Items[i].IsSeparator && m.Items[i].Enabled) break;
                }
                m.Hover = i;
                P.Invalidate();
                break;
            }
            case Key.Enter:
            case Key.Space:
                if (m.Hover >= 0 && m.Items[m.Hover].Enabled) PickMenu(m, m.Hover);
                break;
        }
        return true; // menus swallow the keyboard while open
    }

    // =====================================================================================
    // Search box
    // =====================================================================================

    readonly TextBox _searchBox = new();
    RectF _searchBoxRect, _searchTextRect;
    bool _caretOn = true;
    bool _selectingSearch;

    void FocusSearch(bool selectAll)
    {
        if (_screen != Screen.Browse) return;
        _searchBox.Focused = true;
        if (selectAll) _searchBox.SelectAll();
        ResetCaret();
    }

    void BlurSearch()
    {
        if (!_searchBox.Focused) return;
        _searchBox.Focused = false;
        _selectingSearch = false;
        P.StopTimer(TimerCaret);
        P.Invalidate();
    }

    void ResetCaret()
    {
        _caretOn = true;
        P.StopTimer(TimerCaret);
        if (_searchBox.Focused) P.StartTimer(TimerCaret, 530);
        P.Invalidate();
    }

    void SearchEdited()
    {
        ResetCaret();
        if (_searchBox.Text != _search) OnSearchTextChanged(_searchBox.Text);
    }

    /// <summary>Printable text typed by the user (after the platform's keyboard layout / IME).</summary>
    public void OnTextInput(string text)
    {
        if (string.IsNullOrEmpty(text) || _dialog != DialogKind.None || _popup is not null) return;
        if (!_searchBox.Focused)
        {
            // Type to search: a letter or digit anywhere in the browser starts a search.
            if (_screen != Screen.Browse || !char.IsLetterOrDigit(text[0])) return;
            _searchBox.SetText("");
            FocusSearch(false);
        }
        _searchBox.Insert(text);
        SearchEdited();
    }

    bool SearchKey(Key key, Mods mods)
    {
        if (!_searchBox.Focused) return false;
        bool cmd = P.IsMac ? (mods & Mods.Meta) != 0 : (mods & Mods.Ctrl) != 0;
        bool word = P.IsMac ? (mods & Mods.Alt) != 0 : (mods & Mods.Ctrl) != 0;
        bool shift = (mods & Mods.Shift) != 0;
        var tb = _searchBox;
        switch (key)
        {
            case Key.Escape:
                if (tb.Text.Length > 0) { tb.SetText(""); SearchEdited(); }
                else BlurSearch();
                return true;
            case Key.Enter:
            case Key.Tab:
                BlurSearch();
                return true;
            case Key.Backspace:
                if (cmd && P.IsMac) tb.Home(extend: true);
                tb.Backspace(word);
                SearchEdited();
                return true;
            case Key.Delete:
                tb.Delete(word);
                SearchEdited();
                return true;
            case Key.Left:
                if (cmd && P.IsMac) tb.Home(shift); else tb.Left(shift, word);
                ResetCaret();
                return true;
            case Key.Right:
                if (cmd && P.IsMac) tb.End(shift); else tb.Right(shift, word);
                ResetCaret();
                return true;
            case Key.Home:
            case Key.Up:
                tb.Home(shift);
                ResetCaret();
                return true;
            case Key.End:
            case Key.Down:
                tb.End(shift);
                ResetCaret();
                return true;
            case Key.A when cmd:
                tb.SelectAll();
                ResetCaret();
                return true;
            case Key.C when cmd:
                if (tb.HasSelection) P.CopyText(tb.SelectedText);
                return true;
            case Key.X when cmd:
                if (tb.HasSelection)
                {
                    P.CopyText(tb.SelectedText);
                    tb.DeleteSelection();
                    SearchEdited();
                }
                return true;
            case Key.V when cmd:
                if (P.PasteText() is { } paste)
                {
                    tb.Insert(paste.Replace("\r", " ").Replace("\n", " "));
                    SearchEdited();
                }
                return true;
        }
        // Plain keys are delivered again as text input; shortcuts with ⌘/Ctrl keep working.
        return !cmd && key is >= Key.A and <= Key.D9;
    }

    int SearchCaretAt(float x)
    {
        var tb = _searchBox;
        var f = SearchFont;
        float local = x - _searchTextRect.X + tb.Scroll;
        int best = 0;
        float bestDist = float.MaxValue;
        // Few characters: measuring every prefix is cheap and exact.
        for (int i = 0; i <= tb.Text.Length; i++)
        {
            if (i > 0 && i < tb.Text.Length && char.IsLowSurrogate(tb.Text[i])) continue;
            float px = _measure?.Invoke(tb.Text[..i]) ?? 0;
            float d = Math.Abs(px - local);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    Func<string, float>? _measure;

    void SearchMouseDown(float x, int clicks, Mods mods)
    {
        bool wasFocused = _searchBox.Focused;
        FocusSearch(false);
        if (clicks >= 2) _searchBox.SelectAll();
        else _searchBox.SetCaret(SearchCaretAt(x), extend: wasFocused && (mods & Mods.Shift) != 0);
        _selectingSearch = clicks < 2;
        ResetCaret();
    }

    void PaintSearch(ICanvas c, RectF box)
    {
        _searchBoxRect = box;
        var tb = _searchBox;
        var f = SearchFont;
        _measure = s => c.MeasureText(s, f);
        bool focused = tb.Focused;
        bool hot = Hot(ZSearch);
        c.FillRoundRect(box, 9 * S, T.InputBg);
        if (focused)
            c.StrokeRoundRect(box.Deflate(S), 9 * S - S, T.Accent, 2 * S);
        else
            c.StrokeRoundRect(box.Deflate(Hair / 2), 9 * S, hot ? T.TextMuted : T.InputBorder, Hair);
        c.DrawIcon(Icons.Search, new RectF(box.X + 10 * S, box.CenterY - 8 * S, 16 * S, 16 * S), focused ? T.TextSecondary : T.TextMuted, 1.7f * S);

        float right = box.Right - (tb.Text.Length > 0 ? 30 * S : 10 * S);
        _searchTextRect = RectF.FromLTRB(box.X + 34 * S, box.Y, right, box.Bottom);
        var tr = _searchTextRect;
        if (tb.Text.Length == 0 && !focused)
        {
            c.DrawText(Strings.Search, tr, f, T.TextMuted, TextAlign.Left, Trim.End);
        }
        else
        {
            // Keep the caret inside the visible part of the field.
            float caretX = c.MeasureText(tb.Text[..tb.Caret], f);
            if (caretX - tb.Scroll > tr.W - 2 * S) tb.Scroll = caretX - tr.W + 2 * S;
            if (caretX - tb.Scroll < 0) tb.Scroll = caretX;
            if (c.MeasureText(tb.Text, f) <= tr.W) tb.Scroll = 0;
            float x0 = tr.X - tb.Scroll;
            c.PushClip(tr);
            if (tb.HasSelection)
            {
                float sx = c.MeasureText(tb.Text[..tb.SelectionStart], f), ex = c.MeasureText(tb.Text[..tb.SelectionEnd], f);
                float lh = c.LineHeight(f);
                c.FillRect(new RectF(x0 + sx, tr.CenterY - lh / 2, ex - sx, lh), focused ? T.AccentSoft.Over(T.InputBg) : T.SurfaceHover);
            }
            c.DrawText(tb.Text, new RectF(x0, tr.Y, 10000, tr.H), f, T.TextPrimary, TextAlign.Left, Trim.None);
            if (focused && _caretOn && !tb.HasSelection)
            {
                float lh = c.LineHeight(f);
                c.FillRect(new RectF(MathF.Round(x0 + caretX), MathF.Round(tr.CenterY - lh / 2), Math.Max(1, MathF.Round(S)), MathF.Round(lh)), T.TextPrimary);
            }
            c.PopClip();
        }
        AddZone(ZSearch, box, null, focused ? null : Strings.WithKeys(Strings.SearchTip, Strings.Cmd + "F"), hand: false);
        if (tb.Text.Length > 0)
        {
            var clear = new RectF(box.Right - 28 * S, box.CenterY - 11 * S, 22 * S, 22 * S);
            if (Hot(ZSearchClear)) c.FillRoundRect(clear, 6 * S, T.SurfaceHover);
            c.DrawIcon(Icons.X, clear.CenterBox(14 * S, 14 * S), Hot(ZSearchClear) ? T.TextPrimary : T.TextMuted, 1.8f * S);
            AddZone(ZSearchClear, clear, () => { ClearSearch(); P.Invalidate(); });
        }
    }

    void OnCaretTimer()
    {
        if (!_searchBox.Focused)
        {
            P.StopTimer(TimerCaret);
            return;
        }
        _caretOn = !_caretOn;
        P.Invalidate();
    }

    static long Now => Stopwatch.GetTimestamp();
}
