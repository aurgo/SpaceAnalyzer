using SpaceAnalyzer.Core;

namespace SpaceAnalyzer.UI;

/// <summary>
/// The few drawing primitives the whole UI is built from. Implemented with GDI/GDI+ on Windows and
/// CoreGraphics/CoreText on macOS. Coordinates are device units: pixels on Windows, points on macOS.
/// </summary>
public interface ICanvas
{
    void FillRect(RectF r, Color c);
    void FillRoundRect(RectF r, float radius, Color c);
    void FillRoundRectGradient(RectF r, float radius, Color top, Color bottom);
    void StrokeRoundRect(RectF r, float radius, Color c, float width);
    void DrawLine(float x1, float y1, float x2, float y2, Color c, float width);
    void FillEllipse(RectF r, Color c);
    void StrokeArc(RectF r, float startDegrees, float sweepDegrees, Color c, float width);
    /// <summary>Soft drop shadow for a rounded card (drawn before the card itself).</summary>
    void DrawShadow(RectF r, float radius, float blur, Color c);
    /// <summary>Stroked vector icon (24×24 design grid) scaled into <paramref name="r"/>.</summary>
    void DrawIcon(Icon icon, RectF r, Color c, float strokeWidth);
    /// <summary>Single line of text, vertically centered in <paramref name="r"/>. <paramref name="c"/> must be opaque.</summary>
    void DrawText(string text, RectF r, FontSpec font, Color c, TextAlign align = TextAlign.Left, Trim trim = Trim.End);
    float MeasureText(string text, FontSpec font);
    float LineHeight(FontSpec font);
    void PushClip(RectF r);
    void PopClip();
    ILayer CreateLayer(float width, float height);
    void DrawLayer(ILayer layer, RectF destination, bool fast);
}

/// <summary>Offscreen bitmap (used to cache the rendered treemap).</summary>
public interface ILayer : IDisposable
{
    float Width { get; }
    float Height { get; }
    ICanvas BeginDraw();
    void EndDraw();
}

public readonly struct MenuEntry
{
    public readonly int Id;
    public readonly string? Text;
    public readonly bool Enabled, Checked;

    public MenuEntry(int id, string text, bool enabled = true, bool isChecked = false)
    {
        Id = id; Text = text; Enabled = enabled; Checked = isChecked;
    }

    public static readonly MenuEntry Separator = default;
    public bool IsSeparator => Text is null;
}

/// <summary>Services the UI needs from the operating system.</summary>
/// <summary>
/// The small set of services the UI needs from the operating system. Everything visual (including menus
/// and the search box) is drawn by the app itself, so this is only the window plumbing and shell actions.
/// </summary>
public interface IPlatform
{
    bool IsMac { get; }
    void Invalidate();
    /// <summary>Runs <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);
    void StartTimer(int id, int milliseconds);
    void StopTimer(int id);
    void SetCursor(CursorKind cursor);
    void SetTitle(string title);
    string? PickFolder(string title);
    bool OpenPath(string path);
    /// <summary>Opens a web address in the default browser.</summary>
    bool OpenUrl(string url);
    bool RevealPath(string path);
    bool MoveToTrash(string path, out string? error);
    void CopyText(string text);
    string? PasteText();
    bool CanShowProperties { get; }
    void ShowProperties(string path);
    /// <summary>Lets the window frame (title bar) follow the app theme.</summary>
    void ApplyTheme(Theme theme);
    /// <summary>The UI language changed (e.g. the macOS menu bar needs new titles).</summary>
    void OnLanguageChanged();
    void Quit();
}

/// <summary>A platform that does nothing: used to render snapshots and in tests.</summary>
public sealed class HeadlessPlatform : IPlatform
{
    public readonly List<Action> Posted = [];
    public string Title = "";
    public string? Clipboard, OpenedUrl;
    public bool IsMac { get; init; } = OperatingSystem.IsMacOS();
    public void Invalidate() { }
    public void Post(Action action) { lock (Posted) Posted.Add(action); }
    public void RunPosted()
    {
        Action[] actions;
        lock (Posted) { actions = [.. Posted]; Posted.Clear(); }
        foreach (var a in actions) a();
    }
    public void StartTimer(int id, int milliseconds) { }
    public void StopTimer(int id) { }
    public void SetCursor(CursorKind cursor) { }
    public void SetTitle(string title) => Title = title;
    public string? PickFolder(string title) => null;
    public bool OpenPath(string path) => false;
    public bool OpenUrl(string url) { OpenedUrl = url; return false; }
    public bool RevealPath(string path) => false;
    public bool MoveToTrash(string path, out string? error) { error = "headless"; return false; }
    public void CopyText(string text) => Clipboard = text;
    public string? PasteText() => Clipboard;
    public bool CanShowProperties => false;
    public void ShowProperties(string path) { }
    public void ApplyTheme(Theme theme) { }
    public void OnLanguageChanged() { }
    public void Quit() { }
}

public enum Cmd
{
    None = 0,
    OpenFolder, Rescan, Home, OpenMenu,
    Back, Forward, Up, ZoomIn,
    OpenItem, RevealItem, CopyPath, TrashItem, Properties, RescanItem,
    Find, ToggleSidebar, ToggleFreeSpace,
    ColorType, ColorDepth, ColorAge,
    DetailLow, DetailNormal, DetailHigh,
    LangEs, LangEn,
    ThemeSystem, ThemeDark, ThemeLight,
    About, Quit,
    HomeFolder,
    /// <summary>Menu ids at or above this value scan the volume with that index.</summary>
    VolumeBase = 1000,
}

public enum ColorMode : byte { Type, Depth, Age }
public enum Detail : byte { Low, Normal, High }
public enum ThemeChoice : byte { System, Dark, Light }

public readonly record struct ScanRequest(string Path, FileNode? Replace);
