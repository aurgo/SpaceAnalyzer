using System.Globalization;

namespace SpaceAnalyzer;

/// <summary>
/// Command line: <c>SpaceAnalyzer [folder]</c>.
/// Developer switches render the UI to a PNG without opening a window, e.g.
/// <c>SpaceAnalyzer --snapshot out.png --size 1400x900 --scale 2 --screen browse ~/Downloads</c>.
/// </summary>
public sealed class Options
{
    public string? Path;
    public string? Snapshot;
    public int Width = 1400, Height = 900;
    public float Scale = 2;
    public string Screen = "browse";   // welcome | scanning | browse
    public string? Mode;               // type | depth | age
    public string? Theme;              // dark | light
    public string? Lang;               // es | en
    public string? Select;             // path (absolute or relative to the scanned folder)
    public string? View;               // folder to zoom into
    public string? Search;
    public string? Dialog;             // trash | about
    public string? Toast;
    public string? Os;                 // windows | mac | linux (snapshot wording)
    public float HoverX = -1, HoverY = -1;
    public bool HideSidebar;
    public bool Demo;                  // made-up home folder instead of a real scan

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : "";
            switch (a)
            {
                case "--snapshot": o.Snapshot = Next(); break;
                case "--size":
                {
                    var parts = Next().Split('x', 'X');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                    {
                        o.Width = w;
                        o.Height = h;
                    }
                    break;
                }
                case "--scale": o.Scale = float.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--screen": o.Screen = Next(); break;
                case "--mode": o.Mode = Next(); break;
                case "--theme": o.Theme = Next(); break;
                case "--lang": o.Lang = Next(); break;
                case "--select": o.Select = Next(); break;
                case "--view": o.View = Next(); break;
                case "--search": o.Search = Next(); break;
                case "--dialog": o.Dialog = Next(); break;
                case "--toast": o.Toast = Next(); break;
                case "--os": o.Os = Next(); break;
                case "--no-sidebar": o.HideSidebar = true; break;
                case "--demo": o.Demo = true; break;
                case "--hover":
                {
                    var parts = Next().Split(',');
                    if (parts.Length == 2)
                    {
                        o.HoverX = float.Parse(parts[0], CultureInfo.InvariantCulture);
                        o.HoverY = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    }
                    break;
                }
                default:
                    if (!a.StartsWith("--", StringComparison.Ordinal)) o.Path = a;
                    break;
            }
        }
        return o;
    }
}
