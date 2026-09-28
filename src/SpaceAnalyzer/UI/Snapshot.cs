using SpaceAnalyzer.Core;
using SpaceAnalyzer.Render;

namespace SpaceAnalyzer.UI;

/// <summary>
/// Renders the UI straight to a PNG, without any window or OS API: the same pixels the app shows.
/// <c>SpaceAnalyzer --snapshot out.png [--os windows|mac|linux] [--size 1400x900] [--scale 2] [--screen welcome|scanning|browse] [folder]</c>
/// </summary>
public static class Snapshot
{
    public static int Run(Options o)
    {
        if (o.Os is not null)
        {
            Strings.Os = o.Os switch { "mac" or "macos" => OsKind.Mac, "linux" => OsKind.Linux, _ => OsKind.Windows };
            Fmt.DecimalUnits = Strings.Os == OsKind.Mac;
        }
        if (o.Lang is not null) Strings.Spanish = o.Lang.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        if (o.Screen == "icon") return SaveIcon(o.Snapshot!, o.Width);

        var platform = new HeadlessPlatform { IsMac = Strings.Os == OsKind.Mac };
        int w = (int)MathF.Round(o.Width * o.Scale), h = (int)MathF.Round(o.Height * o.Scale);
        var view = new MainView(platform);
        view.OnResize(w, h, o.Scale);
        Prepare(view, o);
        platform.RunPosted();

        var surface = new Surface(w, h);
        view.Paint(new SoftCanvas(surface));
        AfterFirstPaint(view, o);
        platform.RunPosted();
        view.Paint(new SoftCanvas(surface));

        string file = Path.GetFullPath(o.Snapshot!);
        Png.Save(surface, file);
        Console.WriteLine($"Snapshot written to {file}");
        return 0;
    }

    static void Prepare(MainView view, Options o)
    {
        if (o.Theme is not null) view.Execute(o.Theme == "light" ? Cmd.ThemeLight : Cmd.ThemeDark);
        if (o.Mode is not null)
            view.DebugSetMode(o.Mode switch { "depth" => ColorMode.Depth, "age" => ColorMode.Age, _ => ColorMode.Type });
        if (o.HideSidebar) view.Execute(Cmd.ToggleSidebar);
        view.LoadVolumesNow(o.Demo ? DemoTree.Volumes : null);

        switch (o.Screen)
        {
            case "welcome":
                break;
            case "scanning":
                view.DebugShowScanning(o.Path ?? "/Users/demo", 1_284_331, 97_420, 186_400_000_000);
                break;
            default:
                if (o.Demo) view.LoadTree(DemoTree.Build(), DemoTree.Volume, TimeSpan.FromSeconds(3.4));
                else if (o.Path is not null) view.ScanNow(o.Path);
                else break;
                var root = view.RootNode;
                if (root is null) break;
                if (o.View is not null && Find(root, o.View) is { IsDirectory: true } v) view.DebugNavigate(v);
                if (o.Select is not null && Find(root, o.Select) is { } s) view.DebugSelect(s);
                if (o.Search is not null) view.OnSearchTextChanged(o.Search);
                break;
        }
    }

    static void AfterFirstPaint(MainView view, Options o)
    {
        if (o.Search is not null) view.OnTimer(7); // apply the debounced search now
        if (o.HoverX >= 0)
        {
            view.OnMouseMove(o.HoverX * o.Scale, o.HoverY * o.Scale);
            view.DebugHoverCard();
        }
        if (o.Dialog is not null) view.DebugDialog(o.Dialog);
        if (o.Toast is not null) view.DebugToast(o.Toast);
    }

    /// <summary>The app logo as a PNG (<c>--size 256x256</c>) or as a multi-size Windows .ico.</summary>
    static int SaveIcon(string path, int size)
    {
        static Surface Render(int px)
        {
            var s = new Surface(px, px);
            s.Clear(0);
            float margin = px >= 64 ? px * 0.06f : 0;
            MainView.DrawLogo(new SoftCanvas(s), new RectF(margin, margin, px - 2 * margin, px - 2 * margin));
            return s;
        }

        string file = Path.GetFullPath(path);
        if (file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            Png.SaveIco([.. new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }.Select(Render)], file);
        else
            Png.Save(Render(size), file);
        Console.WriteLine($"Icon written to {file}");
        return 0;
    }

    static FileNode? Find(FileNode root, string path)
    {
        if (System.IO.Path.IsPathRooted(path)) return TreeOps.FindByPath(root, path);
        return TreeOps.FindByPath(root, System.IO.Path.Join(root.Name, path));
    }
}
