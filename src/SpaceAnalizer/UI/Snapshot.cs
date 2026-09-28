using SpaceAnalizer.Core;
using SpaceAnalizer.Render;

namespace SpaceAnalizer.UI;

/// <summary>
/// Renders the UI straight to a PNG, without any window or OS API: the same pixels the app shows.
/// <c>SpaceAnalizer --snapshot out.png [--os windows|mac|linux] [--size 1400x900] [--scale 2] [--screen welcome|scanning|browse] [folder]</c>
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
        view.LoadVolumesNow();

        switch (o.Screen)
        {
            case "welcome":
                break;
            case "scanning":
                view.DebugShowScanning(o.Path ?? "/Users/demo", 1_284_331, 97_420, 186_400_000_000);
                break;
            default:
                if (o.Path is null) break;
                view.ScanNow(o.Path);
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

    static FileNode? Find(FileNode root, string path)
    {
        if (System.IO.Path.IsPathRooted(path)) return TreeOps.FindByPath(root, path);
        return TreeOps.FindByPath(root, System.IO.Path.Join(root.Name, path));
    }
}
