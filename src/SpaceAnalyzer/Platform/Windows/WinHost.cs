using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SpaceAnalyzer.Core;
using SpaceAnalyzer.Render;
using SpaceAnalyzer.UI;
using static SpaceAnalyzer.Platform.Windows.User32;

namespace SpaceAnalyzer.Platform.Windows;

/// <summary>
/// Windows shim: a plain Win32 window. The app draws every pixel itself (SoftCanvas); this class only
/// runs the message loop, forwards input, copies the pixel buffer to the screen with SetDIBitsToDevice
/// and calls a few shell services (folder picker, recycle bin, clipboard, Explorer).
/// </summary>
[SupportedOSPlatform("windows")]
sealed unsafe class WinHost : IPlatform
{
    static WinHost s_host = null!;

    const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_PAINT = 0x000F, WM_ERASEBKGND = 0x0014,
               WM_SETTINGCHANGE = 0x001A, WM_SETCURSOR = 0x0020, WM_GETMINMAXINFO = 0x0024, WM_SETICON = 0x0080,
               WM_KEYDOWN = 0x0100, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104, WM_TIMER = 0x0113,
               WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203,
               WM_RBUTTONUP = 0x0205, WM_MBUTTONDOWN = 0x0207, WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B,
               WM_XBUTTONDBLCLK = 0x020D, WM_DROPFILES = 0x0233, WM_MOUSELEAVE = 0x02A3, WM_DPICHANGED = 0x02E0,
               WM_APP_POST = 0x8001;
    const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    const int CW_USEDEFAULT = unchecked((int)0x80000000);
    const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    MainView _view = null!;
    Surface? _surface;
    readonly ConcurrentQueue<Action> _posted = new();
    readonly HashSet<int> _timers = [];
    IntPtr _hwnd, _instance, _arrow, _hand, _ibeam, _bigIcon, _smallIcon;
    float _scale = 1;
    bool _tracking, _reportedError;
    char _pendingHighSurrogate;
    CursorKind _cursor = CursorKind.Arrow;

    public static int Run(Options options)
    {
        // Crisp on every monitor: per-monitor v2 (Windows 10 1703+), otherwise system DPI.
        try
        {
            if (SetProcessDpiAwarenessContext(-4) == 0) SetProcessDPIAware();
        }
        catch (EntryPointNotFoundException)
        {
            SetProcessDPIAware();
        }

        Strings.Spanish = options.Lang is { } lang
            ? lang.StartsWith("es", StringComparison.OrdinalIgnoreCase)
            : (Kernel32.GetUserDefaultUILanguage() & 0x3FF) == 0x0A; // LANG_SPANISH
        s_host = new WinHost();
        return s_host.RunWindow(options);
    }

    int RunWindow(Options options)
    {
        _instance = Kernel32.GetModuleHandleW(null);
        _arrow = LoadCursorW(IntPtr.Zero, 32512); // IDC_ARROW
        _hand = LoadCursorW(IntPtr.Zero, 32649);  // IDC_HAND
        _ibeam = LoadCursorW(IntPtr.Zero, 32513); // IDC_IBEAM
        _view = new MainView(this);

        fixed (char* className = "SpaceAnalyzerWindow")
        fixed (char* title = Strings.AppName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = 0x0001 | 0x0002 | 0x0008, // CS_VREDRAW | CS_HREDRAW | CS_DBLCLKS
                lpfnWndProc = (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&WndProc,
                hInstance = _instance,
                lpszClassName = (IntPtr)className,
            };
            if (RegisterClassExW(&wc) == 0) throw new InvalidOperationException("RegisterClassEx failed");
            _hwnd = CreateWindowExW(0, className, title, WS_OVERLAPPEDWINDOW,
                CW_USEDEFAULT, CW_USEDEFAULT, 1280, 800, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        }
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed");

        _scale = Math.Max(96, GetDpiForWindow(_hwnd)) / 96f;
        PlaceWindow();
        Shell32.DragAcceptFiles(_hwnd, 1);
        SetWindowIcons();
        _view.SetSystemDark(SystemIsDark());
        OnSize();
        ShowWindow(_hwnd, 1); // SW_SHOWNORMAL
        UpdateWindow(_hwnd);
        SetFocus(_hwnd);
        _view.Start(options.Path);

        MSG msg;
        while (GetMessageW(&msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        return 0;
    }

    /// <summary>A comfortable size, centered on the monitor's work area.</summary>
    void PlaceWindow()
    {
        var monitor = MonitorFromWindow(_hwnd, 2); // MONITOR_DEFAULTTONEAREST
        var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
        if (GetMonitorInfoW(monitor, &info) == 0) return;
        var work = info.rcWork;
        int ww = work.Right - work.Left, wh = work.Bottom - work.Top;
        int w = Math.Min((int)(1400 * _scale), (int)(ww * 0.9));
        int h = Math.Min((int)(900 * _scale), (int)(wh * 0.9));
        SetWindowPos(_hwnd, IntPtr.Zero, work.Left + (ww - w) / 2, work.Top + (wh - h) / 2, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // =====================================================================================
    // Messages
    // =====================================================================================

    [UnmanagedCallersOnly]
    static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return s_host.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            s_host.Report(ex);
            return msg == WM_PAINT ? IntPtr.Zero : DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    static int X(IntPtr lParam) => (short)(lParam.ToInt64() & 0xFFFF);
    static int Y(IntPtr lParam) => (short)((lParam.ToInt64() >> 16) & 0xFFFF);
    static int HiWord(IntPtr w) => (int)((w.ToInt64() >> 16) & 0xFFFF);
    static int LoWord(IntPtr w) => (int)(w.ToInt64() & 0xFFFF);

    IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_CREATE:
                _hwnd = hwnd;
                return IntPtr.Zero;

            case WM_DESTROY:
                _view.CancelScan();
                PostQuitMessage(0);
                return IntPtr.Zero;

            case WM_SIZE:
                OnSize();
                return IntPtr.Zero;

            case WM_PAINT:
                Paint();
                return IntPtr.Zero;

            case WM_ERASEBKGND:
                return 1; // every pixel comes from our buffer: no flicker

            case WM_SETCURSOR:
                if (LoWord(lParam) == 1) // HTCLIENT
                {
                    User32.SetCursor(CursorHandle());
                    return 1;
                }
                break;

            case WM_GETMINMAXINFO:
            {
                var mmi = (MINMAXINFO*)lParam;
                mmi->ptMinTrackSize = new POINT((int)(900 * _scale), (int)(600 * _scale));
                return IntPtr.Zero;
            }

            case WM_MOUSEMOVE:
                if (!_tracking)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = 0x2, hwndTrack = hwnd }; // TME_LEAVE
                    TrackMouseEvent(&tme);
                    _tracking = true;
                }
                _view.OnMouseMove(X(lParam), Y(lParam));
                return IntPtr.Zero;

            case WM_MOUSELEAVE:
                _tracking = false;
                _view.OnMouseLeave();
                return IntPtr.Zero;

            case WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                SetCapture(hwnd);
                SetFocus(hwnd);
                _view.OnMouseDown(X(lParam), Y(lParam), MouseButton.Left, msg == WM_LBUTTONDBLCLK ? 2 : 1, Modifiers());
                return IntPtr.Zero;

            case WM_LBUTTONUP:
                ReleaseCapture();
                _view.OnMouseUp(X(lParam), Y(lParam), MouseButton.Left);
                return IntPtr.Zero;

            case WM_RBUTTONUP: // Windows opens context menus when the right button is released
                _view.OnMouseDown(X(lParam), Y(lParam), MouseButton.Right, 1, Modifiers());
                return IntPtr.Zero;

            case WM_MBUTTONDOWN:
                _view.OnMouseDown(X(lParam), Y(lParam), MouseButton.Middle, 1, Modifiers());
                return IntPtr.Zero;

            case WM_XBUTTONDOWN:
            case WM_XBUTTONDBLCLK:
                _view.OnMouseDown(X(lParam), Y(lParam), HiWord(wParam) == 1 ? MouseButton.Back : MouseButton.Forward, 1, Modifiers());
                return 1;

            case WM_MOUSEWHEEL:
            {
                var pt = new POINT(X(lParam), Y(lParam)); // screen coordinates
                ScreenToClient(hwnd, &pt);
                _view.OnMouseWheel(pt.X, pt.Y, (short)HiWord(wParam) / 120f);
                return IntPtr.Zero;
            }

            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
            {
                var key = MapKey((int)wParam.ToInt64());
                if (key != Key.None && _view.OnKeyDown(key, Modifiers())) return IntPtr.Zero;
                break;
            }

            case WM_CHAR:
                OnChar((char)wParam.ToInt64());
                return IntPtr.Zero;

            case WM_TIMER:
                _view.OnTimer((int)wParam.ToInt64());
                return IntPtr.Zero;

            case WM_DROPFILES:
                OnDrop(wParam);
                return IntPtr.Zero;

            case WM_DPICHANGED:
            {
                _scale = Math.Max(96, LoWord(wParam)) / 96f;
                var r = (RECT*)lParam; // the size Windows suggests for the new monitor
                SetWindowPos(hwnd, IntPtr.Zero, r->Left, r->Top, r->Right - r->Left, r->Bottom - r->Top, SWP_NOZORDER | SWP_NOACTIVATE);
                SetWindowIcons();
                OnSize();
                _view.InvalidateLayers();
                return IntPtr.Zero;
            }

            case WM_SETTINGCHANGE:
                if (lParam != IntPtr.Zero && new string((char*)lParam) == "ImmersiveColorSet")
                    _view.SetSystemDark(SystemIsDark());
                break;

            case WM_APP_POST:
                while (_posted.TryDequeue(out var action)) action();
                return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    void OnSize()
    {
        RECT rc;
        GetClientRect(_hwnd, &rc);
        if (rc.Right <= 0 || rc.Bottom <= 0) return; // minimized
        _view.OnResize(rc.Right, rc.Bottom, _scale);
        Invalidate();
    }

    void Paint()
    {
        PAINTSTRUCT ps;
        var hdc = BeginPaint(_hwnd, &ps);
        try
        {
            RECT rc;
            GetClientRect(_hwnd, &rc);
            int w = Math.Max(1, rc.Right), h = Math.Max(1, rc.Bottom);
            if (_surface is null || _surface.Width != w || _surface.Height != h)
                _surface = new Surface(w, h);
            _view.Paint(new SoftCanvas(_surface));

            // Our pixels are BGRA in memory, exactly a top-down 32-bit DIB: copy them 1:1.
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
            };
            Gdi32.StretchDIBits(hdc, 0, 0, w, h, 0, 0, w, h, _surface.Pointer, &bmi, 0, 0x00CC0020); // DIB_RGB_COLORS, SRCCOPY
        }
        finally
        {
            EndPaint(_hwnd, &ps); // always validate, or Windows would keep asking to paint
        }
    }

    static Mods Modifiers()
    {
        var m = Mods.None;
        if (GetKeyState(0x10) < 0) m |= Mods.Shift;
        if (GetKeyState(0x11) < 0) m |= Mods.Ctrl;
        if (GetKeyState(0x12) < 0) m |= Mods.Alt;
        return m;
    }

    static Key MapKey(int vk) => vk switch
    {
        0x0D => Key.Enter,
        0x1B => Key.Escape,
        0x08 => Key.Backspace,
        0x2E => Key.Delete,
        0x20 => Key.Space,
        0x09 => Key.Tab,
        0x25 => Key.Left,
        0x26 => Key.Up,
        0x27 => Key.Right,
        0x28 => Key.Down,
        0x24 => Key.Home,
        0x23 => Key.End,
        0x21 => Key.PageUp,
        0x22 => Key.PageDown,
        >= 0x70 and <= 0x74 => Key.F1 + (vk - 0x70),
        >= 0x41 and <= 0x5A => Key.A + (vk - 0x41),
        >= 0x30 and <= 0x39 => Key.D0 + (vk - 0x30),
        0xDB => Key.OpenBracket,
        0xDD => Key.CloseBracket,
        0xA6 => Key.BrowserBack,
        0xA7 => Key.BrowserForward,
        _ => Key.None,
    };

    /// <summary>Typed text (after the keyboard layout, dead keys and IME). Emoji arrive as two UTF-16 halves.</summary>
    void OnChar(char c)
    {
        if (char.IsHighSurrogate(c))
        {
            _pendingHighSurrogate = c;
            return;
        }
        string text;
        if (char.IsLowSurrogate(c) && _pendingHighSurrogate != '\0') text = new string([_pendingHighSurrogate, c]);
        else text = c.ToString();
        _pendingHighSurrogate = '\0';
        if (char.IsControl(text[0])) return; // Enter, Backspace, Ctrl+letter... are handled as keys
        _view.OnTextInput(text);
    }

    void OnDrop(IntPtr drop)
    {
        var paths = new List<string>();
        try
        {
            uint count = Shell32.DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
            var buffer = new char[32768];
            fixed (char* p = buffer)
            {
                for (uint i = 0; i < count; i++)
                {
                    uint n = Shell32.DragQueryFileW(drop, i, p, (uint)buffer.Length);
                    if (n > 0) paths.Add(new string(p, 0, (int)n));
                }
            }
        }
        finally
        {
            Shell32.DragFinish(drop);
        }
        SetForegroundWindow(_hwnd);
        _view.OnFilesDropped(paths);
    }

    void Report(Exception ex)
    {
        ErrorLog.Write(ex);
        if (_reportedError) return;
        _reportedError = true;
        string text = $"{Strings.AppName}\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";
        fixed (char* t = text)
        fixed (char* c = Strings.AppName)
            MessageBoxW(_hwnd, t, c, 0x30); // MB_ICONWARNING
    }

    IntPtr CursorHandle() => _cursor switch
    {
        CursorKind.Hand => _hand,
        CursorKind.IBeam => _ibeam,
        _ => _arrow,
    };

    // =====================================================================================
    // IPlatform
    // =====================================================================================

    public bool IsMac => false;
    public bool CanShowProperties => true;

    public void Invalidate()
    {
        if (_hwnd != IntPtr.Zero) InvalidateRect(_hwnd, null, 0);
    }

    public void Post(Action action)
    {
        _posted.Enqueue(action);
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_APP_POST, IntPtr.Zero, IntPtr.Zero);
    }

    public void StartTimer(int id, int milliseconds)
    {
        if (_hwnd == IntPtr.Zero || !_timers.Add(id)) return;
        SetTimer(_hwnd, (nuint)id, (uint)milliseconds, IntPtr.Zero);
    }

    public void StopTimer(int id)
    {
        if (_timers.Remove(id) && _hwnd != IntPtr.Zero) KillTimer(_hwnd, (nuint)id);
    }

    public void SetCursor(CursorKind cursor)
    {
        if (_cursor == cursor) return;
        _cursor = cursor;
        User32.SetCursor(CursorHandle());
    }

    public void SetTitle(string title)
    {
        if (_hwnd == IntPtr.Zero) return;
        fixed (char* t = title) SetWindowTextW(_hwnd, t);
    }

    /// <summary>The standard "choose folder" dialog (IFileOpenDialog with FOS_PICKFOLDERS), through its COM vtable.</summary>
    public string? PickFolder(string title)
    {
        var clsid = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"); // CLSID_FileOpenDialog
        var iid = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");   // IID_IFileOpenDialog
        IntPtr dialog;
        if (Ole32.CoCreateInstance(&clsid, IntPtr.Zero, 1, &iid, &dialog) < 0 || dialog == IntPtr.Zero) return null;
        try
        {
            var vt = *(IntPtr**)dialog;
            uint options;
            ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)vt[10])(dialog, &options);                       // GetOptions
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, int>)vt[9])(dialog, options | 0x20 | 0x40 | 0x800);    // SetOptions: PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST
            fixed (char* t = title) ((delegate* unmanaged[Stdcall]<IntPtr, char*, int>)vt[17])(dialog, t);    // SetTitle
            fixed (char* ok = Strings.Choose) ((delegate* unmanaged[Stdcall]<IntPtr, char*, int>)vt[18])(dialog, ok); // SetOkButtonLabel
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vt[3])(dialog, _hwnd) < 0) return null;    // Show (cancelled = error code)
            IntPtr item;
            if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)vt[20])(dialog, &item) < 0 || item == IntPtr.Zero) return null; // GetResult
            try
            {
                var ivt = *(IntPtr**)item;
                char* path;
                if (((delegate* unmanaged[Stdcall]<IntPtr, uint, char**, int>)ivt[5])(item, 0x80058000, &path) < 0) return null; // GetDisplayName(SIGDN_FILESYSPATH)
                string result = new(path);
                Ole32.CoTaskMemFree((IntPtr)path);
                return result;
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)item)[2])(item); // Release
            }
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)(*(IntPtr**)dialog)[2])(dialog); // Release
        }
    }

    public bool OpenPath(string path)
    {
        fixed (char* p = path)
        fixed (char* dir = Path.GetDirectoryName(path) ?? "")
            return Shell32.ShellExecuteW(_hwnd, null, p, null, dir, 1) > 32;
    }

    /// <summary>Opens Explorer with the item selected.</summary>
    public bool RevealPath(string path)
    {
        IntPtr pidl;
        fixed (char* p = path) pidl = Shell32.ILCreateFromPathW(p);
        if (pidl == IntPtr.Zero) return false;
        try { return Shell32.SHOpenFolderAndSelectItems(pidl, 0, null, 0) >= 0; }
        finally { Shell32.ILFree(pidl); }
    }

    public bool MoveToTrash(string path, out string? error)
    {
        error = null;
        // SHFileOperation wants a double-null-terminated list of paths.
        var from = Marshal.AllocHGlobal((path.Length + 2) * sizeof(char));
        try
        {
            var dst = (char*)from;
            path.AsSpan().CopyTo(new Span<char>(dst, path.Length));
            dst[path.Length] = '\0';
            dst[path.Length + 1] = '\0';
            var op = new SHFILEOPSTRUCTW
            {
                hwnd = _hwnd,
                wFunc = 3, // FO_DELETE
                pFrom = from,
                // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING: to the Recycle Bin (we already asked),
                // but Windows still warns if the item is too big for it and would be deleted for good.
                fFlags = 0x0040 | 0x0010 | 0x4000,
            };
            int rc = Shell32.SHFileOperationW(&op);
            if (rc != 0) error = $"0x{rc:X}";
            return rc == 0 && op.fAnyOperationsAborted == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    public void CopyText(string text)
    {
        if (OpenClipboard(_hwnd) == 0) return;
        try
        {
            EmptyClipboard();
            var mem = Kernel32.GlobalAlloc(0x0002, (nuint)((text.Length + 1) * sizeof(char))); // GMEM_MOVEABLE
            if (mem == IntPtr.Zero) return;
            var dst = (char*)Kernel32.GlobalLock(mem);
            text.AsSpan().CopyTo(new Span<char>(dst, text.Length));
            dst[text.Length] = '\0';
            Kernel32.GlobalUnlock(mem);
            if (SetClipboardData(13, mem) == IntPtr.Zero) Kernel32.GlobalFree(mem); // CF_UNICODETEXT
        }
        finally
        {
            CloseClipboard();
        }
    }

    public string? PasteText()
    {
        if (OpenClipboard(_hwnd) == 0) return null;
        try
        {
            var mem = GetClipboardData(13); // CF_UNICODETEXT
            if (mem == IntPtr.Zero) return null;
            var p = (char*)Kernel32.GlobalLock(mem);
            if (p == null) return null;
            try { return new string(p); }
            finally { Kernel32.GlobalUnlock(mem); }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void ShowProperties(string path)
    {
        fixed (char* p = path) Shell32.SHObjectProperties(_hwnd, 0x2, p, null); // SHOP_FILEPATH
    }

    /// <summary>Title bar that matches the app: dark mode (Windows 10/11) and, on Windows 11, the toolbar's colors.</summary>
    public void ApplyTheme(Theme theme)
    {
        if (_hwnd == IntPtr.Zero) return;
        int dark = theme.Dark ? 1 : 0;
        if (Dwm.DwmSetWindowAttribute(_hwnd, 20, &dark, 4) != 0) Dwm.DwmSetWindowAttribute(_hwnd, 19, &dark, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
        uint caption = theme.ToolbarBg.ColorRef, text = theme.TextPrimary.ColorRef, border = theme.Divider.ColorRef;
        Dwm.DwmSetWindowAttribute(_hwnd, 35, &caption, 4); // DWMWA_CAPTION_COLOR
        Dwm.DwmSetWindowAttribute(_hwnd, 36, &text, 4);    // DWMWA_TEXT_COLOR
        Dwm.DwmSetWindowAttribute(_hwnd, 34, &border, 4);  // DWMWA_BORDER_COLOR
        Invalidate();
    }

    public void OnLanguageChanged() { }

    public void Quit()
    {
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    static bool SystemIsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Window icons drawn by the app itself (the same logo as everywhere else) at the current DPI.</summary>
    void SetWindowIcons()
    {
        uint dpi = (uint)(96 * _scale);
        int big = 32, small = 16;
        try
        {
            big = Math.Max(16, GetSystemMetricsForDpi(11, dpi));   // SM_CXICON
            small = Math.Max(16, GetSystemMetricsForDpi(49, dpi)); // SM_CXSMICON
        }
        catch (EntryPointNotFoundException)
        {
            big = (int)(32 * _scale);
            small = (int)(16 * _scale);
        }
        var oldBig = _bigIcon;
        var oldSmall = _smallIcon;
        _bigIcon = CreateLogoIcon(big);
        _smallIcon = CreateLogoIcon(small);
        if (_bigIcon != IntPtr.Zero) SendMessageW(_hwnd, WM_SETICON, 1, _bigIcon);
        if (_smallIcon != IntPtr.Zero) SendMessageW(_hwnd, WM_SETICON, 0, _smallIcon);
        if (oldBig != IntPtr.Zero) DestroyIcon(oldBig);
        if (oldSmall != IntPtr.Zero) DestroyIcon(oldSmall);
    }

    static IntPtr CreateLogoIcon(int size)
    {
        var surface = new Surface(size, size);
        surface.Clear(0);
        MainView.DrawLogo(new SoftCanvas(surface), new RectF(0, 0, size, size));

        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
        };
        void* bits;
        var color = Gdi32.CreateDIBSection(IntPtr.Zero, &bmi, 0, &bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero) return IntPtr.Zero;
        // Icons use straight (not premultiplied) alpha.
        var dst = (uint*)bits;
        for (int i = 0; i < size * size; i++)
        {
            var (r, g, b, a) = Px.Unpremul(surface.Pixels[i]);
            dst[i] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        // The AND mask is still required; all zeros = "use the alpha channel". Rows are WORD-aligned.
        var maskBits = new byte[((size + 15) / 16) * 2 * size];
        IntPtr mask;
        fixed (byte* mb = maskBits) mask = Gdi32.CreateBitmap(size, size, 1, 1, mb);
        var info = new ICONINFO { fIcon = 1, hbmMask = mask, hbmColor = color };
        var icon = CreateIconIndirect(&info);
        Gdi32.DeleteObject(color);
        Gdi32.DeleteObject(mask);
        return icon;
    }
}
