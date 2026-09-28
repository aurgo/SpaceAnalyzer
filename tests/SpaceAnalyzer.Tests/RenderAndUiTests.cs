using SpaceAnalyzer.Core;
using SpaceAnalyzer.Render;
using SpaceAnalyzer.UI;

namespace SpaceAnalyzer.Tests;

public class RenderTests
{
    static uint At(Surface s, int x, int y) => s.Pixels[y * s.Width + x];

    [Fact]
    public void Rectangles_cover_exactly_their_pixels()
    {
        var s = new Surface(20, 20);
        s.Clear(0xFF000000);
        var c = new SoftCanvas(s);
        c.FillRect(new RectF(5, 5, 10, 10), Color.Hex(0xFF0000));
        Assert.Equal(0xFFFF0000u, At(s, 5, 5));
        Assert.Equal(0xFFFF0000u, At(s, 14, 14));
        Assert.Equal(0xFF000000u, At(s, 4, 5));
        Assert.Equal(0xFF000000u, At(s, 15, 14));

        // Half a pixel of coverage blends 50 %.
        c.FillRect(new RectF(0, 0, 0.5f, 1), Color.White);
        Assert.InRange((At(s, 0, 0) >> 16) & 255, 120u, 136u);
    }

    [Fact]
    public void Rounded_corners_are_antialiased()
    {
        var s = new Surface(40, 40);
        s.Clear(0xFF000000);
        new SoftCanvas(s).FillRoundRect(new RectF(0, 0, 40, 40), 12, Color.White);
        Assert.Equal(0xFF000000u, At(s, 0, 0));               // outside the corner arc
        Assert.Equal(0xFFFFFFFFu, At(s, 20, 20));             // inside
        Assert.Equal(0xFFFFFFFFu, At(s, 20, 0));              // straight edge
        uint edge = (At(s, 3, 3) >> 16) & 255;                // on the arc: partially covered
        Assert.InRange(edge, 1u, 254u);
    }

    [Fact]
    public void Clipping_limits_drawing()
    {
        var s = new Surface(10, 10);
        s.Clear(0xFF000000);
        var c = new SoftCanvas(s);
        c.PushClip(new RectF(0, 0, 5, 10));
        c.FillRect(new RectF(0, 0, 10, 10), Color.White);
        c.PopClip();
        Assert.Equal(0xFFFFFFFFu, At(s, 4, 4));
        Assert.Equal(0xFF000000u, At(s, 5, 4));
    }

    [Fact]
    public void Embedded_fonts_load_and_draw_text()
    {
        foreach (var weight in new[] { Weight.Regular, Weight.Medium, Weight.Semibold, Weight.Bold })
        {
            var font = TextEngine.Shared.Font(weight);
            Assert.True(font.GlyphCount > 300);
            Assert.NotEqual(0, font.GlyphIndex('ñ'));
            Assert.NotEqual(0, font.GlyphIndex('€'));
            Assert.True(font.Kerning(font.GlyphIndex('A'), font.GlyphIndex('V')) < 0, "AV should be kerned");
        }

        var f = new FontSpec(14);
        Assert.True(TextEngine.Shared.Measure("Hola, España", f) > 60);
        var s = new Surface(200, 30);
        s.Clear(0xFF000000);
        new SoftCanvas(s).DrawText("Hola, España", new RectF(0, 0, 200, 30), f, Color.White);
        Assert.Contains(s.Pixels, p => p != 0xFF000000u);
    }

    [Fact]
    public void Every_icon_parses()
    {
        foreach (var field in typeof(Icons).GetFields())
        {
            var icon = (Icon)field.GetValue(null)!;
            Assert.NotEmpty(icon.Data);
            Assert.Equal(Icon.MoveTo, icon.Data[0]);
        }
    }

    [Fact]
    public void Png_output_is_well_formed()
    {
        var s = new Surface(8, 8);
        s.Clear(0xFF3366CCu);
        var png = Png.Encode(s);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }
}

public class TextBoxTests
{
    [Fact]
    public void Editing_selection_and_words()
    {
        var t = new TextBox();
        t.Insert("hola mundo");
        t.Backspace(word: true);
        Assert.Equal("hola ", t.Text);
        t.Home(extend: false);
        t.Right(extend: true, word: true);
        Assert.Equal("hola", t.SelectedText);
        t.Insert("adiós");
        Assert.Equal("adiós ", t.Text);
        t.SelectAll();
        t.Delete();
        Assert.Equal("", t.Text);
    }

    [Fact]
    public void Emoji_are_never_split()
    {
        var t = new TextBox();
        t.Insert("a😀b");
        t.Left(extend: false);
        t.Backspace();
        Assert.Equal("ab", t.Text);
    }

    [Fact]
    public void Control_characters_are_ignored()
    {
        var t = new TextBox();
        t.Insert("a\nb\tc\u0007");
        Assert.Equal("abc", t.Text);
    }
}

public class TreemapTests
{
    [Fact]
    public void Cells_nest_inside_their_parents_without_overlapping()
    {
        var builder = new TreemapBuilder { S = 1 };
        var root = builder.Build(DemoTree.Build(), new RectF(0, 0, 1200, 800));
        Assert.True(builder.CellCount > 200);

        var stack = new Stack<Cell>([root]);
        while (stack.Count > 0)
        {
            var cell = stack.Pop();
            if (cell.Kids is not { } kids) continue;
            for (int i = 0; i < kids.Length; i++)
            {
                var k = kids[i].R;
                Assert.True(k.X >= cell.R.X - 0.01f && k.Y >= cell.R.Y - 0.01f && k.Right <= cell.R.Right + 0.01f && k.Bottom <= cell.R.Bottom + 0.01f,
                    $"{kids[i].Node?.Name} escapes {cell.Node?.Name}");
                for (int j = i + 1; j < kids.Length; j++)
                    Assert.True(kids[i].R.Intersect(kids[j].R).IsEmpty, $"{kids[i].Node?.Name} overlaps {kids[j].Node?.Name}");
                stack.Push(kids[i]);
            }
        }
    }

    [Fact]
    public void Hit_testing_finds_the_deepest_cell_and_back()
    {
        var tree = DemoTree.Build();
        var root = new TreemapBuilder { S = 1 }.Build(tree, new RectF(0, 0, 1200, 800));
        var biggest = MainView.LargestFiles(tree, 1)[0];
        var cell = TreemapBuilder.FindCell(root, biggest);
        Assert.NotNull(cell);
        Assert.Same(biggest, cell.Node);
        Assert.Same(cell, TreemapBuilder.HitTest(root, cell.R.CenterX, cell.R.CenterY));
    }
}

/// <summary>The whole UI, driven headlessly: paint, click, type and navigate like a user would.</summary>
public class MainViewTests
{
    static (MainView View, HeadlessPlatform Platform, Surface Surface) Create(int w = 1400, int h = 880)
    {
        var p = new HeadlessPlatform();
        var v = new MainView(p);
        v.OnResize(w, h, 1);
        v.LoadTree(DemoTree.Build(), DemoTree.Volume, TimeSpan.FromSeconds(1));
        var s = new Surface(w, h);
        v.Paint(new SoftCanvas(s));
        return (v, p, s);
    }

    static void Paint(MainView v, Surface s) => v.Paint(new SoftCanvas(s));

    [Fact]
    public void Double_click_zooms_in_and_backspace_zooms_out()
    {
        var (v, _, s) = Create();
        var root = v.ViewNode!;
        var folder = v.Layout!.Kids!.First(k => k.Kind == CellKind.Folder && k.Header);
        var r = folder.R.Offset(v.TreemapRect.X, v.TreemapRect.Y);

        // On the folder's title bar (the middle of a folder is covered by its contents).
        v.OnMouseDown(r.X + 12, r.Y + 8, MouseButton.Left, 2, Mods.None);
        Assert.Same(folder.Node, v.ViewNode);
        Paint(v, s);
        Assert.True(v.IsAnimating, "the zoom should animate");

        Assert.True(v.OnKeyDown(Key.Backspace, Mods.None));
        Assert.Same(root, v.ViewNode);
        Assert.Same(folder.Node, v.SelectedNode); // coming back selects where we were
    }

    [Fact]
    public void Clicking_selects_and_arrow_keys_move_the_selection()
    {
        var (v, _, s) = Create();
        var cell = v.Layout!.Kids![0];
        var r = cell.R.Offset(v.TreemapRect.X, v.TreemapRect.Y);
        v.OnMouseDown(r.CenterX, r.CenterY, MouseButton.Left, 1, Mods.None);
        v.OnMouseUp(r.CenterX, r.CenterY, MouseButton.Left);
        Assert.NotNull(v.SelectedNode);
        var first = v.SelectedNode;

        Paint(v, s);
        foreach (var key in new[] { Key.Right, Key.Down, Key.Left, Key.Up })
            v.OnKeyDown(key, Mods.None);
        Assert.NotNull(v.SelectedNode);
        Assert.True(v.ViewNode!.IsAncestorOf(v.SelectedNode!));
        Assert.NotSame(v.ViewNode, first);
    }

    [Fact]
    public void Typing_starts_a_search()
    {
        var (v, _, s) = Create();
        v.OnTextInput("V");
        v.OnTextInput("a");
        Assert.True(v.SearchFocused);
        Assert.Equal("Va", v.SearchText);
        Assert.True(v.OnKeyDown(Key.Backspace, Mods.None));
        Assert.Equal("V", v.SearchText);
        Assert.True(v.OnKeyDown(Key.Escape, Mods.None)); // clears
        Assert.Equal("", v.SearchText);
        v.OnTimer(7);
        Paint(v, s);
    }

    [Fact]
    public void Search_counts_matches_in_the_current_view()
    {
        var (v, _, s) = Create();
        v.OnSearchTextChanged("clip");
        v.OnTimer(7); // the debounce timer
        Assert.Equal(14, v.SearchCount); // "Clip 001.mov" ... "Clip 014.mov" in the demo tree
        Paint(v, s);

        v.OnSearchTextChanged("no-such-file");
        v.OnTimer(7);
        Assert.Equal(0, v.SearchCount);
    }

    [Fact]
    public void AltGr_characters_are_text_not_shortcuts()
    {
        var p = new HeadlessPlatform { IsMac = false };
        var v = new MainView(p);
        v.OnResize(1200, 800, 1);
        v.LoadTree(DemoTree.Build(), null, TimeSpan.Zero);
        v.OnTextInput("a");
        // AltGr+2 ("@" on a Spanish keyboard) arrives as Ctrl+Alt+2: it must not switch the color mode.
        Assert.False(v.IsChecked(Cmd.ColorDepth));
        v.OnKeyDown(Key.D2, Mods.Ctrl | Mods.Alt);
        Assert.False(v.IsChecked(Cmd.ColorDepth));
        v.OnKeyDown(Key.D2, Mods.Ctrl);
        Assert.True(v.IsChecked(Cmd.ColorDepth));
    }

    [Fact]
    public void Menus_open_and_close()
    {
        var (v, _, s) = Create();
        v.Execute(Cmd.OpenMenu);
        Assert.True(v.MenuOpen);
        Paint(v, s);
        v.OnKeyDown(Key.Escape, Mods.None);
        Assert.False(v.MenuOpen);
    }

    [Fact]
    public void Copy_path_uses_the_clipboard()
    {
        var (v, p, _) = Create();
        var file = MainView.LargestFiles(v.RootNode!, 1)[0];
        v.DebugSelect(file);
        v.OnKeyDown(Key.C, OperatingSystem.IsMacOS() ? Mods.Meta : Mods.Ctrl);
        Assert.Equal(file.FullPath, p.Clipboard);
    }

    [Fact]
    public void A_failed_trash_keeps_the_tree_and_says_so()
    {
        var (v, _, s) = Create();
        var file = MainView.LargestFiles(v.RootNode!, 1)[0];
        long before = v.RootNode!.Size;
        v.DebugSelect(file);
        v.Execute(Cmd.TrashItem);
        Assert.True(v.DialogOpen);
        Paint(v, s);
        v.OnKeyDown(Key.Enter, Mods.None); // confirm; the headless platform refuses to delete
        Assert.False(v.DialogOpen);
        Assert.Equal(before, v.RootNode!.Size);
        Assert.NotNull(v.ToastText);
    }

    /// <summary>
    /// Zooms into every folder of the demo tree in a small window (like a 1024×768 screen) and paints it:
    /// every sidebar/legend combination (1, 2, 3... file types, few or many files) must lay out.
    /// </summary>
    [Fact]
    public void Every_folder_paints_in_a_small_window()
    {
        var (v, _, s) = Create(1024, 700);
        var folders = new List<FileNode>();
        var stack = new Stack<FileNode>([v.RootNode!]);
        while (stack.Count > 0)
            foreach (var c in stack.Pop().Children)
                if (c.IsDirectory) { folders.Add(c); stack.Push(c); }

        foreach (var folder in folders)
        {
            v.DebugNavigate(folder);
            Paint(v, s);
            v.DebugSelect(folder.Children.FirstOrDefault());
            Paint(v, s);
        }
        Assert.True(folders.Count > 80);
    }

    [Theory]
    [InlineData(320, 240)]
    [InlineData(640, 400)]
    [InlineData(900, 600)]
    public void Tiny_windows_do_not_break_the_layout(int w, int h)
    {
        var (v, _, s) = Create(w, h);
        v.OnMouseMove(w / 3f, h / 2f);
        v.DebugHoverCard();
        Paint(v, s);
        v.DebugDialog("trash");
        Paint(v, s);
        v.OnKeyDown(Key.Escape, Mods.None);
        v.Execute(Cmd.OpenMenu);
        Paint(v, s);
        v.OnKeyDown(Key.Escape, Mods.None);
        v.Execute(Cmd.Home);
        Paint(v, s);
        v.DebugShowScanning("/Users/demo", 12, 3, 4096);
        Paint(v, s);
    }

    [Fact]
    public void Every_screen_and_mode_paints_in_both_themes_and_languages()
    {
        foreach (bool spanish in new[] { true, false })
        foreach (var theme in new[] { Cmd.ThemeDark, Cmd.ThemeLight })
        foreach (var mode in new[] { Cmd.ColorType, Cmd.ColorDepth, Cmd.ColorAge })
        {
            var (v, _, s) = Create(1000, 700);
            v.Execute(spanish ? Cmd.LangEs : Cmd.LangEn);
            v.Execute(theme);
            v.Execute(mode);
            v.Execute(Cmd.DetailHigh);
            Paint(v, s);
            v.OnMouseMove(300, 300);
            v.DebugHoverCard();
            v.DebugDialog("about");
            Paint(v, s);
            v.OnKeyDown(Key.Escape, Mods.None);
            v.Execute(Cmd.Home);
            Paint(v, s);
        }
        Strings.Spanish = true;
    }
}
