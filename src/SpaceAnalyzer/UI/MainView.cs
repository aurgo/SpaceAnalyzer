using System.Diagnostics;
using SpaceAnalyzer.Core;

namespace SpaceAnalyzer.UI;

/// <summary>
/// The whole application UI, independent of the operating system: state, navigation, commands,
/// input handling and (in the other partial files) painting. Platform hosts forward window events here.
/// </summary>
public sealed partial class MainView
{
    /// <summary>From &lt;Version&gt; in Directory.Build.props; the release workflow checks that the tag matches it.</summary>
    public static readonly string Version = typeof(MainView).Assembly.GetName().Version is { } v
        ? $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}"
        : "0.0.0";

    // Timers (ids shared with the platform host).
    const int TimerScan = 1, TimerAnim = 2, TimerTip = 3, TimerCard = 4, TimerToast = 5, TimerResize = 6, TimerSearch = 7;

    // Hit-zone ids.
    const int ZHome = 1, ZOpen = 2, ZRescan = 3, ZBack = 4, ZForward = 5, ZUp = 6, ZSearch = 7, ZSearchClear = 8,
              ZSidebarToggle = 9, ZMore = 10, ZMode = 11, // 11..13
              ZTreemap = 20, ZCancel = 21, ZChoose = 22, ZHomeFolder = 23, ZResume = 24, ZInfoPath = 25,
              ZDlgBackdrop = 30, ZDlgCard = 31, ZDlgCancel = 32, ZDlgOk = 33, ZDlgLink = 34, ZDlgUpdate = 35,
              ZAction = 40,    // 40..43
              ZCrumb = 100, ZCrumbMore = 199, ZType = 200, ZLargest = 300, ZDrive = 400;

    readonly IPlatform P;
    Theme T = Theme.DarkTheme;
    readonly TreemapBuilder _builder = new();
    readonly TreemapPainter _painter = new();
    float S = 1, W = 1200, H = 800;
    /// <summary>One physical pixel (canvas units are physical pixels on every platform).</summary>
    const float Hair = 1;

    enum Screen { Welcome, Scanning, Browse }
    Screen _screen = Screen.Welcome;

    // ---- data
    FileNode? _root, _view, _selected;
    VolumeInfo? _volume;
    bool _rootIsVolume;
    TimeSpan _scanTime;
    long _scanErrors;
    readonly List<FileNode> _back = [], _fwd = [];
    List<FileNode> _largest = [];

    // ---- options
    ColorMode _mode = ColorMode.Type;
    Detail _detail = Detail.Normal;
    bool _showFree = true, _sidebar = true, _systemDark = true;
    ThemeChoice _themeChoice = ThemeChoice.System;
    string _search = "";
    FileCategory? _highlight;

    // ---- scanning
    CancellationTokenSource? _cts;
    ScanProgress? _progress;
    string _scanPath = "";
    long _scanStart, _scanExpected;
    FileNode? _scanReplace;
    string? _restoreView, _restoreSelection;

    // ---- welcome
    List<VolumeInfo> _volumes = [];
    bool _volumesLoaded;

    // ---- interaction
    sealed class Zone
    {
        public int Id;
        public RectF R;
        public Action? Click, DoubleClick, RightClick;
        public string? Tip;
        public bool Hand;
    }

    readonly List<Zone> _zones = new(128);
    int _hot = -1, _pressed = -1;
    PointF _mouse;
    bool _mouseInside, _tipShown;
    long _lastWheel;
    float _wheelAccum;
    RectF _openButton, _moreButton;

    // ---- dialogs and toasts
    enum DialogKind { None, Trash, About }
    DialogKind _dialog;
    FileNode? _dialogNode;
    string? _toast;
    bool _toastError;
    long _toastUntil;

    // ---- check for updates (only when the user presses the button: the app doesn't go online otherwise)
    internal enum UpdateState { None, Checking, UpToDate, Available, Failed }
    UpdateState _update;
    GitHub.Release? _latest;
    Task? _updateTask;

    public MainView(IPlatform platform)
    {
        P = platform;
        UpdateTheme();
    }

    // =====================================================================================
    // Public entry points used by the platform hosts
    // =====================================================================================

    /// <summary>Called once the window exists. Scans <paramref name="path"/> right away when given.</summary>
    public void Start(string? path)
    {
        RefreshVolumesAsync();
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            StartScan(path);
        P.SetTitle(Strings.AppName);
    }

    public void OnResize(float width, float height, float scale)
    {
        if (Math.Abs(scale - S) > 0.001f)
        {
            S = scale;
            _layoutDirty = true;
        }
        W = Math.Max(1, width);
        H = Math.Max(1, height);
        _lastResize = Stopwatch.GetTimestamp();
        P.Invalidate();
    }

    /// <summary>Called at startup and whenever the OS theme changes; always re-applies (the window frame too).</summary>
    public void SetSystemDark(bool dark)
    {
        _systemDark = dark;
        UpdateTheme();
    }

    public void OnSearchTextChanged(string text)
    {
        if (_searchBox.Text != text) _searchBox.SetText(text);
        if (text == _search) return;
        _search = text;
        _searchPending = text.Length > 0;
        P.StartTimer(TimerSearch, 160);
        P.Invalidate();
    }

    /// <summary>The search text changed and the results have not been recomputed yet (debounced).</summary>
    bool _searchPending;

    public void OnFilesDropped(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) { StartScan(p); return; }
            if (File.Exists(p) && Path.GetDirectoryName(p) is { } dir) { StartScan(dir); return; }
        }
    }

    public void OnTimer(int id)
    {
        switch (id)
        {
            case TimerScan:
                P.Invalidate();
                break;
            case TimerAnim:
                if (!_anim) P.StopTimer(TimerAnim);
                P.Invalidate();
                break;
            case TimerTip:
                P.StopTimer(TimerTip);
                if (_hot >= 0 && FindZone(_hot)?.Tip is not null && _pressed < 0)
                {
                    _tipShown = true;
                    P.Invalidate();
                }
                break;
            case TimerCard:
                P.StopTimer(TimerCard);
                if (_hoverCell is not null && _mouseInside && !_anim)
                {
                    _cardVisible = true;
                    P.Invalidate();
                }
                break;
            case TimerToast:
                if (_toast is null || Stopwatch.GetTimestamp() >= _toastUntil)
                {
                    _toast = null;
                    P.StopTimer(TimerToast);
                    P.Invalidate();
                }
                break;
            case TimerResize:
                if (Stopwatch.GetElapsedTime(_lastResize).TotalMilliseconds >= ResizeSettleMs)
                {
                    P.StopTimer(TimerResize);
                    P.Invalidate();
                }
                break;
            case TimerSearch:
                P.StopTimer(TimerSearch);
                _layoutDirty = true;
                _searchPending = false;
                UpdateSearchStats();
                P.Invalidate();
                break;
            case TimerCaret:
                OnCaretTimer();
                break;
        }
    }

    // =====================================================================================
    // Mouse and keyboard
    // =====================================================================================

    public void OnMouseMove(float x, float y)
    {
        _mouse = new PointF(x, y);
        _mouseInside = true;
        if (MenuMouseMove(x, y)) return;
        if (_selectingSearch && _pressed == ZSearch)
        {
            _searchBox.SetCaret(SearchCaretAt(x), extend: true);
            ResetCaret();
        }
        int hot = HitZone(x, y);
        if (hot != _hot)
        {
            _hot = hot;
            _tipShown = false;
            P.StopTimer(TimerTip);
            if (hot >= 0 && FindZone(hot)?.Tip is not null) P.StartTimer(TimerTip, 550);
            P.Invalidate();
        }
        if (hot == ZTreemap) UpdateTreemapHover(x, y);
        else ClearTreemapHover();
        P.SetCursor(hot == ZSearch && _searchTextRect.Contains(x, y) ? CursorKind.IBeam
            : hot >= 0 && FindZone(hot) is { Hand: true, Click: not null } ? CursorKind.Hand : CursorKind.Arrow);
    }

    public void OnMouseLeave()
    {
        _mouseInside = false;
        _hot = -1;
        _tipShown = false;
        P.StopTimer(TimerTip);
        ClearTreemapHover();
        P.Invalidate();
    }

    public void OnMouseDown(float x, float y, MouseButton button, int clicks, Mods mods)
    {
        _tipShown = false;
        if (MenuMouseDown(x, y)) return;
        if (button == MouseButton.Back) { Execute(Cmd.Back); return; }
        if (button == MouseButton.Forward) { Execute(Cmd.Forward); return; }

        int z = HitZone(x, y);
        var zone = z >= 0 ? FindZone(z) : null;
        if (z != ZSearch && z != ZSearchClear) BlurSearch();
        if (button == MouseButton.Left)
        {
            _pressed = z;
            if (z == ZTreemap) TreemapMouseDown(x, y, clicks);
            else if (z == ZSearch) SearchMouseDown(x, clicks, mods);
            else if (clicks >= 2 && zone?.DoubleClick is { } dbl) { _pressed = -1; dbl(); }
            P.Invalidate();
        }
        else if (button == MouseButton.Right)
        {
            if (z == ZTreemap) TreemapContextMenu(x, y);
            else zone?.RightClick?.Invoke();
        }
        else if (button == MouseButton.Middle && z == ZTreemap)
        {
            Execute(Cmd.Up);
        }
    }

    public void OnMouseUp(float x, float y, MouseButton button)
    {
        if (button != MouseButton.Left) return;
        _selectingSearch = false;
        if (MenuMouseUp(x, y)) return;
        int pressed = _pressed;
        _pressed = -1;
        int z = HitZone(x, y);
        if (z >= 0 && z == pressed && z != ZTreemap && FindZone(z)?.Click is { } click)
            click();
        P.Invalidate();
    }

    /// <summary><paramref name="delta"/>: +1 per wheel notch away from the user (zoom in), -1 towards (zoom out).</summary>
    public void OnMouseWheel(float x, float y, float delta)
    {
        if (_screen != Screen.Browse || _dialog != DialogKind.None || _popup is not null) return;
        if (!_tmRect.Contains(x, y)) return;
        // Trackpads send many tiny deltas and keep scrolling by inertia: accumulate, then rate-limit.
        _wheelAccum += delta;
        if (Math.Abs(_wheelAccum) < 1) return;
        if (Stopwatch.GetElapsedTime(_lastWheel).TotalMilliseconds < 260) { _wheelAccum = 0; return; }
        _lastWheel = Stopwatch.GetTimestamp();
        bool zoomIn = _wheelAccum > 0;
        _wheelAccum = 0;
        if (zoomIn) ZoomTowards(x, y);
        else Execute(Cmd.Up);
    }

    /// <summary>Pinch gesture (macOS). Positive = zoom in.</summary>
    public void OnMagnify(float x, float y, float amount) => OnMouseWheel(x, y, amount * 4);

    public bool OnKeyDown(Key key, Mods mods)
    {
        bool cmd = IsCommand(mods);
        bool alt = (mods & Mods.Alt) != 0;

        if (MenuKey(key)) return true;
        if (_dialog == DialogKind.None && SearchKey(key, mods)) return true;

        if (_dialog != DialogKind.None)
        {
            if (key == Key.Escape) CloseDialog();
            else if (key is Key.Enter or Key.Space) ConfirmDialog();
            return true;
        }

        switch (key)
        {
            case Key.Escape:
                if (_screen == Screen.Scanning) CancelScan();
                else if (_search.Length > 0) ClearSearch();
                else if (_highlight is not null) { _highlight = null; _layoutDirty = true; }
                else if (_selected is not null) Select(null);
                else return false;
                P.Invalidate();
                return true;
            case Key.O when cmd: Execute(Cmd.OpenFolder); return true;
            case Key.F when cmd: Execute(Cmd.Find); return true;
            case Key.R when cmd:
            case Key.F5: Execute(Cmd.Rescan); return true;
            case Key.C when cmd: Execute(Cmd.CopyPath); return true;
            case Key.D1 when cmd: Execute(Cmd.ColorType); return true;
            case Key.D2 when cmd: Execute(Cmd.ColorDepth); return true;
            case Key.D3 when cmd: Execute(Cmd.ColorAge); return true;
            case Key.Q when cmd && P.IsMac: Execute(Cmd.Quit); return true;
        }

        if (_screen != Screen.Browse) return false;
        switch (key)
        {
            case Key.Backspace when cmd && P.IsMac:
            case Key.Delete:
                Execute(Cmd.TrashItem);
                return true;
            case Key.Backspace:
                Execute(Cmd.Up);
                return true;
            case Key.BrowserBack:
            case Key.Left when alt && !P.IsMac:
            case Key.OpenBracket when cmd:
                Execute(Cmd.Back);
                return true;
            case Key.BrowserForward:
            case Key.Right when alt && !P.IsMac:
            case Key.CloseBracket when cmd:
                Execute(Cmd.Forward);
                return true;
            case Key.Up when alt || cmd:
                Execute(Cmd.Up);
                return true;
            case Key.Down when cmd:
                Execute(Cmd.ZoomIn);
                return true;
            case Key.Enter when alt && P.CanShowProperties:
                Execute(Cmd.Properties);
                return true;
            case Key.Enter:
                Execute(_selected is { IsDirectory: true } ? Cmd.ZoomIn : Cmd.OpenItem);
                return true;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                MoveSelection(key);
                return true;
        }
        return false;
    }

    // =====================================================================================
    // Commands (toolbar, menus, keyboard, macOS menu bar)
    // =====================================================================================

    FileNode? Target => _selected ?? _view;

    public bool IsEnabled(Cmd cmd) => cmd switch
    {
        Cmd.Rescan => _root is not null && _screen != Screen.Scanning,
        Cmd.Back => _back.Count > 0 && _screen == Screen.Browse,
        Cmd.Forward => _fwd.Count > 0 && _screen == Screen.Browse,
        Cmd.Up => _view?.Parent is not null && _screen == Screen.Browse,
        Cmd.ZoomIn => _screen == Screen.Browse && ZoomInTarget() is not null,
        Cmd.OpenItem or Cmd.RevealItem or Cmd.CopyPath => _screen == Screen.Browse && Target is not null,
        Cmd.Properties => _screen == Screen.Browse && Target is not null && P.CanShowProperties,
        Cmd.RescanItem => _screen == Screen.Browse && Target is { IsDirectory: true },
        Cmd.TrashItem => _screen == Screen.Browse && _selected is { Parent: not null },
        Cmd.Find => _screen == Screen.Browse,
        Cmd.ToggleFreeSpace => _rootIsVolume,
        _ => true,
    };

    public bool IsChecked(Cmd cmd) => cmd switch
    {
        Cmd.ToggleSidebar => _sidebar,
        Cmd.ToggleFreeSpace => _showFree && _rootIsVolume,
        Cmd.ColorType => _mode == ColorMode.Type,
        Cmd.ColorDepth => _mode == ColorMode.Depth,
        Cmd.ColorAge => _mode == ColorMode.Age,
        Cmd.DetailLow => _detail == Detail.Low,
        Cmd.DetailNormal => _detail == Detail.Normal,
        Cmd.DetailHigh => _detail == Detail.High,
        Cmd.LangEs => Strings.Spanish,
        Cmd.LangEn => !Strings.Spanish,
        Cmd.ThemeSystem => _themeChoice == ThemeChoice.System,
        Cmd.ThemeDark => _themeChoice == ThemeChoice.Dark,
        Cmd.ThemeLight => _themeChoice == ThemeChoice.Light,
        _ => false,
    };

    public void Execute(Cmd cmd)
    {
        if (!IsEnabled(cmd)) return;
        switch (cmd)
        {
            case Cmd.OpenFolder:
                if (P.PickFolder(Strings.ChooseFolderTitle) is { } folder) StartScan(folder);
                break;
            case Cmd.OpenMenu: ShowOpenMenu(); break;
            case Cmd.HomeFolder: StartScan(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); break;
            case Cmd.Rescan: if (_root is not null) StartScan(_root.Name); break;
            case Cmd.Home: GoHome(); break;
            case Cmd.Back: GoHistory(_back, _fwd); break;
            case Cmd.Forward: GoHistory(_fwd, _back); break;
            case Cmd.Up: if (_view?.Parent is { } parent) NavigateTo(parent, select: _view); break;
            case Cmd.ZoomIn: if (ZoomInTarget() is { } zt) NavigateTo(zt); break;
            case Cmd.OpenItem:
                if (Target is { } o && !P.OpenPath(o.FullPath)) ShowToast(Strings.OpenFailed, error: true);
                break;
            case Cmd.RevealItem:
                if (Target is { } rv) P.RevealPath(rv.FullPath);
                break;
            case Cmd.CopyPath:
                if (Target is { } cp) { P.CopyText(cp.FullPath); ShowToast(Strings.Copied); }
                break;
            case Cmd.Properties:
                if (Target is { } pr) P.ShowProperties(pr.FullPath);
                break;
            case Cmd.RescanItem:
                if (Target is { IsDirectory: true } rs)
                {
                    if (rs.Parent is null) StartScan(rs.Name);
                    else StartScan(rs.FullPath, rs);
                }
                break;
            case Cmd.TrashItem:
                if (_selected is { Parent: not null } victim) { _dialog = DialogKind.Trash; _dialogNode = victim; }
                break;
            case Cmd.Find: FocusSearch(selectAll: true); break;
            case Cmd.ToggleSidebar: _sidebar = !_sidebar; break;
            case Cmd.ToggleFreeSpace: _showFree = !_showFree; _layoutDirty = true; break;
            case Cmd.ColorType: SetMode(ColorMode.Type); break;
            case Cmd.ColorDepth: SetMode(ColorMode.Depth); break;
            case Cmd.ColorAge: SetMode(ColorMode.Age); break;
            case Cmd.DetailLow: _detail = Detail.Low; _layoutDirty = true; break;
            case Cmd.DetailNormal: _detail = Detail.Normal; _layoutDirty = true; break;
            case Cmd.DetailHigh: _detail = Detail.High; _layoutDirty = true; break;
            case Cmd.LangEs: SetLanguage(true); break;
            case Cmd.LangEn: SetLanguage(false); break;
            case Cmd.ThemeSystem: _themeChoice = ThemeChoice.System; UpdateTheme(); break;
            case Cmd.ThemeDark: _themeChoice = ThemeChoice.Dark; UpdateTheme(); break;
            case Cmd.ThemeLight: _themeChoice = ThemeChoice.Light; UpdateTheme(); break;
            case Cmd.About: _dialog = DialogKind.About; break;
            case Cmd.Quit: P.Quit(); break;
            default:
                int vi = (int)cmd - (int)Cmd.VolumeBase;
                if (vi >= 0 && vi < _volumes.Count) StartScan(_volumes[vi].RootPath);
                break;
        }
        P.Invalidate();
    }

    void SetMode(ColorMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _layoutDirty = true;
    }

    void SetLanguage(bool spanish)
    {
        Strings.Spanish = spanish;
        _layoutDirty = true;
        RefreshVolumesAsync();
        UpdateTitle();
        P.OnLanguageChanged();
    }

    /// <summary>Drops cached bitmaps (e.g. when the window moves to a screen with another pixel density).</summary>
    public void InvalidateLayers()
    {
        EndAnimation();
        _layer?.Dispose();
        _layer = null;
        _layoutDirty = true;
        P.Invalidate();
    }

    internal void LoadVolumesNow(IEnumerable<VolumeInfo>? volumes = null)
    {
        _volumes = volumes?.ToList() ?? Volumes.List();
        _volumesLoaded = true;
    }

    /// <summary>Shows an already built tree (demo data, tests) as if it had just been scanned.</summary>
    internal void LoadTree(FileNode root, VolumeInfo? volume, TimeSpan scanTime)
    {
        _root = root;
        _view = root;
        _selected = null;
        _volume = volume;
        _rootIsVolume = Volumes.IsVolumeRoot(root.Name, volume);
        _scanErrors = 0;
        _scanTime = scanTime;
        _back.Clear();
        _fwd.Clear();
        _screen = Screen.Browse;
        _layoutDirty = true;
        UpdateLargest();
        UpdateTitle();
    }

    void UpdateTheme()
    {
        bool dark = _themeChoice switch { ThemeChoice.Dark => true, ThemeChoice.Light => false, _ => _systemDark };
        T = dark ? Theme.DarkTheme : Theme.LightTheme;
        _layoutDirty = true;
        P.ApplyTheme(T);
        P.Invalidate();
    }

    void ShowOpenMenu()
    {
        var items = new List<MenuEntry>();
        for (int i = 0; i < _volumes.Count; i++)
        {
            var v = _volumes[i];
            items.Add(new MenuEntry((int)Cmd.VolumeBase + i, $"{v.Label}  —  {Strings.FreeOf(Fmt.Size(v.FreeSpace), Fmt.Size(v.TotalSize))}"));
        }
        if (items.Count > 0) items.Add(MenuEntry.Separator);
        items.Add(new MenuEntry((int)Cmd.OpenFolder, Strings.ChooseFolder));
        items.Add(new MenuEntry((int)Cmd.HomeFolder, Strings.HomeFolder));
        OpenMenu(items, _openButton.X, _openButton.Bottom + 4 * S, id => Execute((Cmd)id));
    }

    void ShowMoreMenu()
    {
        var items = new List<MenuEntry>
        {
            new((int)Cmd.ToggleFreeSpace, Strings.ShowFreeSpace, IsEnabled(Cmd.ToggleFreeSpace), IsChecked(Cmd.ToggleFreeSpace)),
            new((int)Cmd.ToggleSidebar, Strings.Sidebar, true, _sidebar),
            MenuEntry.Separator,
            new((int)Cmd.ColorType, Strings.ColorTypeLong, true, _mode == ColorMode.Type),
            new((int)Cmd.ColorDepth, Strings.ColorDepthLong, true, _mode == ColorMode.Depth),
            new((int)Cmd.ColorAge, Strings.ColorAgeLong, true, _mode == ColorMode.Age),
            MenuEntry.Separator,
            new((int)Cmd.DetailLow, Strings.DetailLow, true, _detail == Detail.Low),
            new((int)Cmd.DetailNormal, Strings.DetailNormal, true, _detail == Detail.Normal),
            new((int)Cmd.DetailHigh, Strings.DetailHigh, true, _detail == Detail.High),
            MenuEntry.Separator,
            new((int)Cmd.ThemeSystem, Strings.ThemeSystem, true, _themeChoice == ThemeChoice.System),
            new((int)Cmd.ThemeDark, Strings.ThemeDark, true, _themeChoice == ThemeChoice.Dark),
            new((int)Cmd.ThemeLight, Strings.ThemeLight, true, _themeChoice == ThemeChoice.Light),
            MenuEntry.Separator,
            new((int)Cmd.LangEs, "Español", true, Strings.Spanish),
            new((int)Cmd.LangEn, "English", true, !Strings.Spanish),
            MenuEntry.Separator,
            new((int)Cmd.About, Strings.About),
        };
        OpenMenu(items, _moreButton.Right, _moreButton.Bottom + 4 * S, id => Execute((Cmd)id), alignRight: true);
    }

    // =====================================================================================
    // Scanning
    // =====================================================================================

    public void StartScan(string path, FileNode? replace = null)
    {
        _cts?.Cancel();
        string normalized;
        try { normalized = Scanner.NormalizePath(path); }
        catch (Exception ex) { ShowToast(Strings.ScanFailed(ex.Message), error: true); return; }

        // Re-scanning the same root: come back to the same folder and selection afterwards.
        if (replace is null && _root is not null && SamePath(normalized, _root.Name))
        {
            _restoreView = _view?.FullPath;
            _restoreSelection = _selected?.FullPath;
        }
        else if (replace is null)
        {
            _restoreView = _restoreSelection = null;
        }

        var cts = new CancellationTokenSource();
        var progress = new ScanProgress();
        _cts = cts;
        _progress = progress;
        _scanPath = normalized;
        _scanReplace = replace;
        _scanStart = Stopwatch.GetTimestamp();
        var vol = Volumes.FindForPath(normalized);
        _scanExpected = replace is null && Volumes.IsVolumeRoot(normalized, vol) ? vol!.UsedSize : 0;
        _screen = Screen.Scanning;
        _dialog = DialogKind.None;
        ClearTreemapHover();

        var thread = new Thread(() =>
        {
            FileNode? result = null;
            Exception? error = null;
            try { result = Scanner.Scan(normalized, progress, cts.Token); }
            catch (Exception ex) { error = ex; }
            P.Post(() => OnScanFinished(cts, result, error, vol));
        })
        { IsBackground = true, Name = "SpaceAnalyzer scanner" };
        thread.Start();
        P.StartTimer(TimerScan, 40);
        P.Invalidate();
    }

    /// <summary>Scans on the calling thread (snapshot mode and tests).</summary>
    internal void ScanNow(string path)
    {
        var progress = new ScanProgress();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _progress = progress;
        _scanPath = Scanner.NormalizePath(path);
        _scanStart = Stopwatch.GetTimestamp();
        var vol = Volumes.FindForPath(_scanPath);
        FileNode? result = null;
        Exception? error = null;
        try { result = Scanner.Scan(_scanPath, progress, cts.Token); }
        catch (Exception ex) { error = ex; }
        OnScanFinished(cts, result, error, vol);
    }

    public void CancelScan()
    {
        _cts?.Cancel();
    }

    void OnScanFinished(CancellationTokenSource cts, FileNode? result, Exception? error, VolumeInfo? volume)
    {
        if (!ReferenceEquals(cts, _cts)) return; // a newer scan replaced this one
        _cts = null;
        P.StopTimer(TimerScan);

        if (result is null || cts.IsCancellationRequested)
        {
            if (error is not null and not OperationCanceledException)
                ShowToast(Strings.ScanFailed(error.Message), error: true);
            _screen = _root is not null && _view is not null ? Screen.Browse : Screen.Welcome;
            if (_screen == Screen.Welcome) RefreshVolumesAsync();
            P.Invalidate();
            return;
        }

        _scanTime = Stopwatch.GetElapsedTime(_scanStart);
        long errors = _progress?.Errors ?? 0;

        if (_scanReplace is { } old && _root is not null && ReferenceEquals(old.Root, _root))
        {
            // A sub-folder was re-scanned: graft the fresh sub-tree and keep everything else.
            var remember = RememberPaths(old);
            var fresh = TreeOps.Replace(old, result);
            RestorePaths(remember, fresh);
            _scanErrors += errors;
        }
        else
        {
            _root = result;
            _volume = volume;
            _rootIsVolume = Volumes.IsVolumeRoot(result.Name, volume);
            _scanErrors = errors;
            _back.Clear();
            _fwd.Clear();
            _view = (_restoreView is not null ? TreeOps.FindByPath(result, _restoreView) : null) ?? result;
            if (!_view.IsDirectory) _view = _view.Parent ?? result;
            _selected = _restoreSelection is not null ? TreeOps.FindByPath(result, _restoreSelection) : null;
            if (_selected is not null && !_view.IsAncestorOf(_selected)) _selected = null;
            _restoreView = _restoreSelection = null;
        }
        _scanReplace = null;
        _screen = Screen.Browse;
        _layoutDirty = true;
        _anim = false;
        UpdateLargest();
        UpdateTitle();
        P.Invalidate();
    }

    /// <summary>Paths of every navigation reference that lives inside <paramref name="subtree"/>.</summary>
    List<(int Slot, int Index, string Path)> RememberPaths(FileNode subtree)
    {
        var list = new List<(int, int, string)>();
        void Add(int slot, int index, FileNode? n)
        {
            if (n is not null && subtree.IsSelfOrAncestorOf(n)) list.Add((slot, index, n.FullPath));
        }
        Add(0, 0, _view);
        Add(1, 0, _selected);
        for (int i = 0; i < _back.Count; i++) Add(2, i, _back[i]);
        for (int i = 0; i < _fwd.Count; i++) Add(3, i, _fwd[i]);
        return list;
    }

    void RestorePaths(List<(int Slot, int Index, string Path)> saved, FileNode fallback)
    {
        foreach (var (slot, index, path) in saved)
        {
            var found = _root is null ? null : TreeOps.FindByPath(_root, path);
            switch (slot)
            {
                case 0: _view = found is { IsDirectory: true } ? found : fallback; break;
                case 1: _selected = found; break;
                case 2: _back[index] = found ?? fallback; break;
                case 3: _fwd[index] = found ?? fallback; break;
            }
        }
        if (_selected is not null && _view is not null && !_view.IsAncestorOf(_selected)) _selected = null;
    }

    void RefreshVolumesAsync()
    {
        Task.Run(() =>
        {
            var list = Volumes.List();
            P.Post(() =>
            {
                _volumes = list;
                _volumesLoaded = true;
                P.Invalidate();
            });
        });
    }

    // =====================================================================================
    // Navigation and selection
    // =====================================================================================

    void NavigateTo(FileNode target, bool addHistory = true, FileNode? select = null)
    {
        if (_view is null || ReferenceEquals(target, _view) || !target.IsDirectory) return;
        if (addHistory)
        {
            _back.Add(_view);
            _fwd.Clear();
            if (_back.Count > 200) _back.RemoveAt(0);
        }
        SetView(target, select);
    }

    void GoHistory(List<FileNode> from, List<FileNode> to)
    {
        if (from.Count == 0 || _view is null) return;
        var target = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(_view);
        SetView(target, ReferenceEquals(target, _view.Parent) ? _view : null);
    }

    void SetView(FileNode target, FileNode? select)
    {
        var old = _view!;
        PrepareZoomAnimation(old, target);
        _view = target;
        if (select is not null && target.IsAncestorOf(select)) _selected = select;
        else if (_selected is not null && !target.IsAncestorOf(_selected)) _selected = null;
        _layoutDirty = true;
        ClearTreemapHover();
        UpdateLargest();
        UpdateTitle();
        P.Invalidate();
    }

    void GoHome()
    {
        _cts?.Cancel();
        _screen = Screen.Welcome;
        RefreshVolumesAsync();
        UpdateTitle();
    }

    FileNode? ZoomInTarget()
    {
        var s = _selected;
        if (s is null || _view is null) return null;
        if (s.IsDirectory && !ReferenceEquals(s, _view)) return s;
        if (s.Parent is { } p && !ReferenceEquals(p, _view) && _view.IsAncestorOf(p)) return p;
        return null;
    }

    void Select(FileNode? node)
    {
        if (ReferenceEquals(node, _selected)) return;
        _selected = node;
        P.Invalidate();
    }

    void ToggleHighlight(FileCategory category)
    {
        _highlight = _highlight == category ? null : category;
        _layoutDirty = true;
        P.Invalidate();
    }

    void ClearSearch()
    {
        _search = "";
        _searchBox.SetText("");
        _searchCount = _searchBytes = 0;
        _searchPending = false;
        _layoutDirty = true;
    }

    void UpdateTitle()
    {
        string title = _screen == Screen.Browse && _view is not null
            ? $"{_view.FullPath} — {Strings.AppName}"
            : Strings.AppName;
        P.SetTitle(title);
    }

    /// <summary>Refreshes what depends on the current view: the largest files and the search results.</summary>
    void UpdateLargest()
    {
        _largest = _view is null ? [] : LargestFiles(_view, 40);
        UpdateSearchStats();
    }

    long _searchCount, _searchBytes;

    /// <summary>
    /// How many items in the current view match the search, and how much they take. A matching folder counts
    /// once with everything inside it (the treemap lights up its whole contents too).
    /// </summary>
    void UpdateSearchStats()
    {
        _searchCount = _searchBytes = 0;
        if (_view is null || _search.Length == 0) return;
        var stack = new Stack<FileNode>();
        stack.Push(_view);
        while (stack.Count > 0)
        {
            foreach (var c in stack.Pop().Children)
            {
                if (c.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
                {
                    _searchCount++;
                    _searchBytes += c.Size;
                }
                else if (c.IsDirectory)
                {
                    stack.Push(c);
                }
            }
        }
    }

    /// <summary>
    /// The <paramref name="count"/> largest files below <paramref name="root"/>. Children are sorted by size and a
    /// folder can never hold a file larger than itself, so whole branches are skipped: this touches very few nodes.
    /// </summary>
    internal static List<FileNode> LargestFiles(FileNode root, int count)
    {
        var heap = new PriorityQueue<FileNode, long>(count + 1);
        var stack = new Stack<FileNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var c in dir.Children)
            {
                if (heap.Count == count && c.Size <= heap.Peek().Size) break; // everything after is smaller
                if (c.IsDirectory) stack.Push(c);
                else if (heap.Count < count) heap.Enqueue(c, c.Size);
                else heap.EnqueueDequeue(c, c.Size);
            }
        }
        var list = new List<FileNode>(heap.Count);
        while (heap.Count > 0) list.Add(heap.Dequeue());
        list.Reverse();
        return list;
    }

    // =====================================================================================
    // Dialogs and toasts
    // =====================================================================================

    void CloseDialog()
    {
        _dialog = DialogKind.None;
        _dialogNode = null;
        P.Invalidate();
    }

    void ConfirmDialog()
    {
        if (_dialog == DialogKind.Trash && _dialogNode is { } node)
        {
            CloseDialog();
            TrashNow(node);
        }
        else
        {
            CloseDialog();
        }
    }

    /// <summary>Opens a web page; without a browser the address goes to the clipboard instead.</summary>
    void OpenWeb(string url)
    {
        if (P.OpenUrl(url)) return;
        P.CopyText(url);
        ShowToast(Strings.BrowserFailed, error: true);
    }

    /// <summary>Asks GitHub for the latest release on a worker thread (the request blocks).</summary>
    void CheckForUpdates()
    {
        if (_update == UpdateState.Checking) return;
        _update = UpdateState.Checking;
        _updateTask = Task.Run(() =>
        {
            GitHub.Release? latest = null;
            try { latest = GitHub.ParseLatestRelease(P.DownloadText(GitHub.LatestReleaseApi)); }
            catch (Exception ex) { ErrorLog.Write(ex); }
            P.Post(() =>
            {
                _latest = latest;
                _update = latest is not { } r ? UpdateState.Failed
                    : GitHub.IsNewer(r.Version, Version) ? UpdateState.Available
                    : UpdateState.UpToDate;
                P.Invalidate();
            });
        });
        P.Invalidate();
    }

    void OpenLatestRelease()
    {
        if (_latest is { } r) OpenWeb(r.Url);
    }

    void TrashNow(FileNode node)
    {
        string path = node.FullPath;
        bool ok = P.MoveToTrash(path, out var error);
        if (ok && (File.Exists(path) || Directory.Exists(path))) ok = false; // cancelled by the user
        if (!ok)
        {
            ShowToast(error is null ? Strings.TrashFailed : $"{Strings.TrashFailed}: {error}", error: true);
            return;
        }

        // Anything that pointed inside the deleted item now points to its parent.
        var parent = node.Parent!;
        if (_view is not null && node.IsSelfOrAncestorOf(_view)) _view = parent;
        if (_selected is not null && node.IsSelfOrAncestorOf(_selected)) _selected = null;
        _back.RemoveAll(n => node.IsSelfOrAncestorOf(n));
        _fwd.RemoveAll(n => node.IsSelfOrAncestorOf(n));
        TreeOps.Remove(node);
        _layoutDirty = true;
        UpdateLargest();
        UpdateTitle();
        ShowToast(Strings.Trashed(node.Name));
    }

    void ShowToast(string text, bool error = false)
    {
        _toast = text;
        _toastError = error;
        _toastUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * (error ? 5 : 3);
        P.StartTimer(TimerToast, 200);
        P.Invalidate();
    }

    // =====================================================================================
    // Hit zones
    // =====================================================================================

    Zone AddZone(int id, RectF r, Action? click = null, string? tip = null, bool hand = true, Action? doubleClick = null, Action? rightClick = null)
    {
        var z = new Zone { Id = id, R = r, Click = click, Tip = tip, Hand = hand, DoubleClick = doubleClick, RightClick = rightClick };
        _zones.Add(z);
        return z;
    }

    int HitZone(float x, float y)
    {
        for (int i = _zones.Count - 1; i >= 0; i--)
            if (_zones[i].R.Contains(x, y)) return _zones[i].Id;
        return -1;
    }

    Zone? FindZone(int id)
    {
        for (int i = _zones.Count - 1; i >= 0; i--)
            if (_zones[i].Id == id) return _zones[i];
        return null;
    }

    /// <summary>
    /// The shortcut modifier: ⌘ on macOS, Ctrl elsewhere. Ctrl+Alt is AltGr on many keyboard layouts
    /// (it types "@", "[", "€"...), so it is never treated as a shortcut.
    /// </summary>
    bool IsCommand(Mods mods) => P.IsMac
        ? (mods & Mods.Meta) != 0
        : (mods & Mods.Ctrl) != 0 && (mods & Mods.Alt) == 0;

    bool Hot(int id) => _hot == id;
    bool Down(int id) => _pressed == id && _hot == id;

    static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // ---- test / snapshot hooks
    internal FileNode? RootNode => _root;
    internal FileNode? ViewNode => _view;
    internal FileNode? SelectedNode => _selected;
    internal Cell? Layout => _layout;
    internal void DebugSelect(FileNode? n) => _selected = n;
    internal void DebugNavigate(FileNode n) => NavigateTo(n);
    internal void DebugSetMode(ColorMode m) => SetMode(m);
    internal void DebugShowScanning(string path, long files, long folders, long bytes)
    {
        _screen = Screen.Scanning;
        _scanPath = path;
        _progress = new ScanProgress { Files = files, Folders = folders, Bytes = bytes, CurrentPath = Path.Combine(path, "Library", "Caches", "com.example.app", "data.bin") };
        _scanStart = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 7;
    }
    internal void DebugDialog(string kind)
    {
        switch (kind)
        {
            case "trash":
                _dialog = DialogKind.Trash;
                _dialogNode = _selected ?? _largest.FirstOrDefault();
                break;
            case "about": _dialog = DialogKind.About; break;
            case "menu": ShowMoreMenu(); break;
            case "open": ShowOpenMenu(); break;
            case "context": TreemapContextMenu(_mouse.X, _mouse.Y); break;
            case "search": FocusSearch(false); break;
        }
    }
    internal void DebugHoverCard() => _cardVisible = _hoverCell is not null;
    internal void DebugToast(string text) => ShowToast(text);
    internal bool IsAnimating => _anim;
    internal string SearchText => _search;
    internal long SearchCount => _searchCount;
    internal bool SearchFocused => _searchBox.Focused;
    internal bool MenuOpen => _popup is not null;
    internal string? ToastText => _toast;
    internal bool DialogOpen => _dialog != DialogKind.None;
    internal RectF? RepoLinkRect => FindZone(ZDlgLink)?.R;
    internal RectF? UpdateButtonRect => FindZone(ZDlgUpdate)?.R;
    internal UpdateState Update => _update;
    internal Task? UpdateTask => _updateTask;
    internal RectF TreemapRect => _tmRect;
}
