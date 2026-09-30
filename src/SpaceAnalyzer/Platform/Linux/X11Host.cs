using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SpaceAnalyzer.Core;
using SpaceAnalyzer.Render;
using SpaceAnalyzer.UI;
using static SpaceAnalyzer.Platform.Linux.Xlib;

namespace SpaceAnalyzer.Platform.Linux;

/// <summary>
/// Linux shim on plain Xlib (works on X11 and on Wayland desktops through XWayland): one window, an event
/// loop built on poll(), XPutImage to show the app's pixels, the CLIPBOARD selection, and the desktop's
/// tools (xdg-open, zenity/kdialog, gio or the freedesktop trash) for shell services.
/// </summary>
[SupportedOSPlatform("linux")]
sealed unsafe class X11Host : IPlatform
{
    static X11Host s_host = null!;

    MainView _view = null!;
    Surface? _surface;
    IntPtr _display, _window, _gc, _visual, _image, _ic;
    int _screen, _depth, _xfd, _width, _height;
    readonly int[] _wakePipe = new int[2];
    float _scale = 1;
    bool _running = true, _dirty = true;
    IntPtr _wmDelete, _wmProtocols, _netWmName, _netWmIcon, _utf8, _clipboard, _targets, _textAtom, _pasteProp, _gtkVariant;
    IntPtr _arrow, _hand, _ibeam;
    CursorKind _cursor = CursorKind.Arrow;
    string? _clipboardText;
    readonly ConcurrentQueue<Action> _posted = new();
    readonly Dictionary<int, (int Interval, long Due)> _timers = new();
    ulong _lastClickTime;
    int _lastClickX, _lastClickY;
    bool _lastWasDouble;

    public static int Run(Options options)
    {
        s_host = new X11Host();
        return s_host.RunWindow(options);
    }

    int RunWindow(Options options)
    {
        LibC.setlocale(LibC.LC_CTYPE, ""); // so the input method delivers text in the user's encoding
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            Console.Error.WriteLine("SpaceAnalyzer: cannot open the X11 display (is DISPLAY set?).");
            return 1;
        }
        XSetErrorHandler((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, int>)&IgnoreXError);

        Strings.Spanish = options.Lang is { } lang ? lang.StartsWith("es", StringComparison.OrdinalIgnoreCase) : PrefersSpanish();
        _screen = XDefaultScreen(_display);
        _visual = XDefaultVisual(_display, _screen);
        _depth = XDefaultDepth(_display, _screen);
        _gc = XDefaultGC(_display, _screen);
        _scale = DetectScale();

        int sw = XDisplayWidth(_display, _screen), sh = XDisplayHeight(_display, _screen);
        _width = Math.Min((int)(1400 * _scale), (int)(sw * 0.9));
        _height = Math.Min((int)(900 * _scale), (int)(sh * 0.9));
        _window = XCreateSimpleWindow(_display, XRootWindow(_display, _screen), 0, 0, (uint)_width, (uint)_height, 0, 0,
            Theme.DarkTheme.WindowBg.Argb & 0xFFFFFF);

        const nint mask = 1 << 0 | 1 << 2 | 1 << 3 | 1 << 4 | 1 << 5 | 1 << 6 | 1 << 15 | 1 << 17 | 1 << 21;
        // KeyPress, ButtonPress, ButtonRelease, EnterWindow, LeaveWindow, PointerMotion, Exposure, StructureNotify, FocusChange
        XSelectInput(_display, _window, mask);

        _wmProtocols = XInternAtom(_display, "WM_PROTOCOLS", 0);
        _wmDelete = XInternAtom(_display, "WM_DELETE_WINDOW", 0);
        _netWmName = XInternAtom(_display, "_NET_WM_NAME", 0);
        _netWmIcon = XInternAtom(_display, "_NET_WM_ICON", 0);
        _utf8 = XInternAtom(_display, "UTF8_STRING", 0);
        _clipboard = XInternAtom(_display, "CLIPBOARD", 0);
        _targets = XInternAtom(_display, "TARGETS", 0);
        _textAtom = XInternAtom(_display, "TEXT", 0);
        _pasteProp = XInternAtom(_display, "SPACEANALYZER_PASTE", 0);
        _gtkVariant = XInternAtom(_display, "_GTK_THEME_VARIANT", 0);
        var deleteAtom = _wmDelete;
        XSetWMProtocols(_display, _window, &deleteAtom, 1);
        SetClassAndSizeHints();
        SetIcon();

        _arrow = XCreateFontCursor(_display, 68);  // XC_left_ptr
        _hand = XCreateFontCursor(_display, 60);   // XC_hand2
        _ibeam = XCreateFontCursor(_display, 152); // XC_xterm
        XDefineCursor(_display, _window, _arrow);
        OpenInputMethod();

        fixed (int* p = _wakePipe) LibC.pipe(p);
        _xfd = XConnectionNumber(_display);

        _view = new MainView(this, UpdatePrefs.ForThisUser(), UpdateTarget.ForThisProcess());
        _view.SetSystemDark(SystemIsDark());
        _view.OnResize(_width, _height, _scale);
        XMapWindow(_display, _window);
        XFlush(_display);
        _view.Start(options.Path);

        Loop();

        _view.CancelScan();
        XDestroyWindow(_display, _window);
        XCloseDisplay(_display);
        return 0;
    }

    [UnmanagedCallersOnly]
    static int IgnoreXError(IntPtr display, IntPtr error) => 0; // e.g. a clipboard requestor that went away

    // =====================================================================================
    // Event loop: X events, cross-thread posts (a pipe) and timers, all multiplexed with poll()
    // =====================================================================================

    void Loop()
    {
        byte* ev = stackalloc byte[EventSize];
        byte* drain = stackalloc byte[64];
        PollFd* fds = stackalloc PollFd[2];
        while (_running)
        {
            while (XPending(_display) > 0)
            {
                XNextEvent(_display, ev);
                if (XFilterEvent(ev, IntPtr.Zero) != 0) continue; // consumed by the input method
                Guard(() => Handle(ev));
                if (!_running) return;
            }
            while (_posted.TryDequeue(out var action)) Guard(action);
            Guard(RunDueTimers);
            if (_dirty)
            {
                _dirty = false;
                Guard(Paint);
            }
            XFlush(_display);
            if (XEventsQueued(_display, 0) > 0 || !_posted.IsEmpty) continue;

            fds[0] = new PollFd { Fd = _xfd, Events = LibC.POLLIN };
            fds[1] = new PollFd { Fd = _wakePipe[0], Events = LibC.POLLIN };
            LibC.poll(fds, 2, _dirty ? 0 : NextTimerTimeout());
            if ((fds[1].Revents & LibC.POLLIN) != 0) LibC.read(_wakePipe[0], drain, 64);
        }
    }

    /// <summary>An unexpected error is logged and the app keeps running, as on Windows and macOS.</summary>
    static void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    int NextTimerTimeout()
    {
        if (_timers.Count == 0) return -1;
        long now = Environment.TickCount64, next = long.MaxValue;
        foreach (var t in _timers.Values) next = Math.Min(next, t.Due);
        return (int)Math.Clamp(next - now, 0, int.MaxValue);
    }

    void RunDueTimers()
    {
        if (_timers.Count == 0) return;
        long now = Environment.TickCount64;
        foreach (var (id, t) in _timers.ToArray())
        {
            if (t.Due > now || !_timers.ContainsKey(id)) continue;
            _timers[id] = (t.Interval, now + t.Interval);
            _view.OnTimer(id);
        }
    }

    void Handle(byte* ev)
    {
        switch (*(int*)ev)
        {
            case KeyPress:
                OnKey(ev);
                break;
            case ButtonPress:
                OnButton(ev, pressed: true);
                break;
            case ButtonRelease:
                OnButton(ev, pressed: false);
                break;
            case MotionNotify:
            case EnterNotify:
                _view.OnMouseMove(*(int*)(ev + 64), *(int*)(ev + 68));
                break;
            case LeaveNotify:
                if (*(int*)(ev + 80) == 0) _view.OnMouseLeave(); // NotifyNormal (not a grab)
                break;
            case FocusIn:
                if (_ic != IntPtr.Zero) XSetICFocus(_ic);
                break;
            case Expose:
                _dirty = true;
                break;
            case ConfigureNotify:
            {
                int w = *(int*)(ev + 56), h = *(int*)(ev + 60);
                if (w != _width || h != _height)
                {
                    _width = w;
                    _height = h;
                    _view.OnResize(w, h, _scale);
                    _dirty = true;
                }
                break;
            }
            case ClientMessage:
                if (*(IntPtr*)(ev + 40) == _wmProtocols && *(IntPtr*)(ev + 56) == _wmDelete) _running = false;
                break;
            case SelectionRequest:
                OnSelectionRequest(ev);
                break;
            case SelectionClear:
                _clipboardText = null;
                break;
        }
    }

    static Mods Modifiers(uint state)
    {
        var m = Mods.None;
        if ((state & 1) != 0) m |= Mods.Shift;
        if ((state & 4) != 0) m |= Mods.Ctrl;
        if ((state & 8) != 0) m |= Mods.Alt; // Mod1; AltGr is usually Mod5 and stays a text key
        return m;
    }

    void OnButton(byte* ev, bool pressed)
    {
        int x = *(int*)(ev + 64), y = *(int*)(ev + 68);
        var mods = Modifiers(*(uint*)(ev + 80));
        uint button = *(uint*)(ev + 84);
        ulong time = *(ulong*)(ev + 56);
        if (!pressed)
        {
            if (button == 1) _view.OnMouseUp(x, y, MouseButton.Left);
            return;
        }
        switch (button)
        {
            case 1:
            {
                // X11 has no double-click event: two presses close in time and space make one.
                bool isDouble = !_lastWasDouble && time - _lastClickTime < 450 &&
                                Math.Abs(x - _lastClickX) <= 4 * _scale && Math.Abs(y - _lastClickY) <= 4 * _scale;
                _lastWasDouble = isDouble;
                _lastClickTime = time;
                _lastClickX = x;
                _lastClickY = y;
                _view.OnMouseDown(x, y, MouseButton.Left, isDouble ? 2 : 1, mods);
                break;
            }
            case 2: _view.OnMouseDown(x, y, MouseButton.Middle, 1, mods); break;
            case 3: _view.OnMouseDown(x, y, MouseButton.Right, 1, mods); break;
            case 4: _view.OnMouseWheel(x, y, 1); break;
            case 5: _view.OnMouseWheel(x, y, -1); break;
            case 8: _view.OnMouseDown(x, y, MouseButton.Back, 1, mods); break;
            case 9: _view.OnMouseDown(x, y, MouseButton.Forward, 1, mods); break;
        }
    }

    void OnKey(byte* ev)
    {
        byte* buffer = stackalloc byte[64];
        nuint keysym = 0;
        string text;
        if (_ic != IntPtr.Zero)
        {
            int status;
            int n = Xutf8LookupString(_ic, ev, buffer, 63, &keysym, &status);
            text = n > 0 && status is 2 or 4 ? Encoding.UTF8.GetString(buffer, n) : ""; // XLookupChars / XLookupBoth
        }
        else
        {
            int n = XLookupString(ev, buffer, 63, &keysym, IntPtr.Zero);
            text = n > 0 ? Encoding.Latin1.GetString(buffer, n) : "";
        }
        var mods = Modifiers(*(uint*)(ev + 80));
        var key = MapKeysym(keysym);
        bool handled = key != Key.None && _view.OnKeyDown(key, mods);
        if (!handled && text.Length > 0 && (mods & (Mods.Ctrl | Mods.Alt)) == 0 && !char.IsControl(text[0]))
            _view.OnTextInput(text);
    }

    static Key MapKeysym(nuint k) => k switch
    {
        0xff0d or 0xff8d => Key.Enter,
        0xff1b => Key.Escape,
        0xff08 => Key.Backspace,
        0xffff or 0xff9f => Key.Delete,
        0x20 => Key.Space,
        0xff09 or 0xfe20 => Key.Tab,
        0xff51 or 0xff96 => Key.Left,
        0xff52 or 0xff97 => Key.Up,
        0xff53 or 0xff98 => Key.Right,
        0xff54 or 0xff99 => Key.Down,
        0xff50 or 0xff95 => Key.Home,
        0xff57 or 0xff9c => Key.End,
        0xff55 or 0xff9a => Key.PageUp,
        0xff56 or 0xff9b => Key.PageDown,
        >= 0xffbe and <= 0xffc2 => Key.F1 + (int)(k - 0xffbe),
        >= 0x61 and <= 0x7a => Key.A + (int)(k - 0x61),
        >= 0x41 and <= 0x5a => Key.A + (int)(k - 0x41),
        >= 0x30 and <= 0x39 => Key.D0 + (int)(k - 0x30),
        0x5b => Key.OpenBracket,
        0x5d => Key.CloseBracket,
        0x1008ff26 => Key.BrowserBack,
        0x1008ff27 => Key.BrowserForward,
        _ => Key.None,
    };

    void Paint()
    {
        if (_width <= 0 || _height <= 0) return;
        if (_surface is null || _surface.Width != _width || _surface.Height != _height)
        {
            FreeImage();
            _surface = new Surface(_width, _height);
            // A ZPixmap over our own buffer: 24-bit TrueColor with 32 bits per pixel is BGRX in memory,
            // byte for byte the same as our BGRA surface.
            _image = XCreateImage(_display, _visual, (uint)_depth, 2, 0, _surface.Pointer, (uint)_width, (uint)_height, 32, _width * 4);
        }
        _view.Paint(new SoftCanvas(_surface));
        if (_image != IntPtr.Zero)
            XPutImage(_display, _window, _gc, _image, 0, 0, 0, 0, (uint)_width, (uint)_height);
    }

    void FreeImage()
    {
        if (_image == IntPtr.Zero) return;
        *(IntPtr*)((byte*)_image + 16) = IntPtr.Zero; // XImage.data belongs to us: don't let Xlib free it
        XFree(_image);
        _image = IntPtr.Zero;
    }

    // =====================================================================================
    // Window setup
    // =====================================================================================

    void SetClassAndSizeHints()
    {
        // WM_CLASS: groups the window in taskbars and docks.
        var name = Marshal.StringToCoTaskMemUTF8("spaceanalyzer");
        var cls = Marshal.StringToCoTaskMemUTF8("SpaceAnalyzer");
        IntPtr* hint = stackalloc IntPtr[2] { name, cls };
        XSetClassHint(_display, _window, hint);
        Marshal.FreeCoTaskMem(name);
        Marshal.FreeCoTaskMem(cls);

        // XSizeHints with PMinSize.
        byte* hints = stackalloc byte[80];
        new Span<byte>(hints, 80).Clear();
        *(nint*)hints = 1 << 4;
        *(int*)(hints + 24) = (int)(900 * _scale);
        *(int*)(hints + 28) = (int)(600 * _scale);
        XSetWMNormalHints(_display, _window, hints);
    }

    /// <summary>_NET_WM_ICON: the logo drawn by the app, as non-premultiplied ARGB in C longs.</summary>
    void SetIcon()
    {
        var data = new List<nint>();
        foreach (int size in new[] { 32, 64, 128 })
        {
            var s = new Surface(size, size);
            s.Clear(0);
            MainView.DrawLogo(new SoftCanvas(s), new RectF(0, 0, size, size));
            data.Add(size);
            data.Add(size);
            foreach (var p in s.Pixels)
            {
                var (r, g, b, a) = Px.Unpremul(p);
                data.Add((nint)(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b));
            }
        }
        var array = data.ToArray();
        fixed (nint* p = array) XChangeProperty(_display, _window, _netWmIcon, XA_CARDINAL, 32, 0, p, array.Length);
    }

    void OpenInputMethod()
    {
        try
        {
            XSetLocaleModifiers("");
            var im = XOpenIM(_display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (im == IntPtr.Zero) return;
            const nint style = 0x0008 | 0x0400; // XIMPreeditNothing | XIMStatusNothing
            _ic = XCreateIC(im, "inputStyle", style, "clientWindow", _window, "focusWindow", _window, IntPtr.Zero);
        }
        catch
        {
            _ic = IntPtr.Zero; // plain XLookupString (Latin-1) still works
        }
    }

    float DetectScale()
    {
        // Xft.dpi is what GNOME, KDE and most toolkits set for HiDPI screens.
        var rm = XResourceManagerString(_display);
        if (rm != IntPtr.Zero && Marshal.PtrToStringUTF8(rm) is { } resources)
        {
            foreach (var line in resources.Split('\n'))
            {
                if (!line.StartsWith("Xft.dpi:", StringComparison.Ordinal)) continue;
                if (float.TryParse(line.AsSpan(8).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float dpi) && dpi >= 72)
                    return Math.Clamp(dpi / 96f, 1f, 4f);
            }
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("GDK_SCALE"), out int gdk) && gdk is >= 1 and <= 4) return gdk;
        return 1;
    }

    static bool PrefersSpanish()
    {
        foreach (var name in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(v)) return v.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    static bool SystemIsDark()
    {
        var scheme = Run("gsettings", "get", "org.gnome.desktop.interface", "color-scheme");
        if (scheme is not null)
        {
            if (scheme.Contains("dark", StringComparison.OrdinalIgnoreCase)) return true;
            var gtk = Run("gsettings", "get", "org.gnome.desktop.interface", "gtk-theme");
            return gtk?.Contains("dark", StringComparison.OrdinalIgnoreCase) ?? false;
        }
        return (Environment.GetEnvironmentVariable("GTK_THEME") ?? "dark").Contains("dark", StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================================
    // Clipboard (the CLIPBOARD selection, UTF-8 text only)
    // =====================================================================================

    void OnSelectionRequest(byte* ev)
    {
        IntPtr requestor = *(IntPtr*)(ev + 40), selection = *(IntPtr*)(ev + 48), target = *(IntPtr*)(ev + 56), property = *(IntPtr*)(ev + 64);
        nuint time = *(nuint*)(ev + 72);
        if (property == IntPtr.Zero) property = target; // obsolete clients
        IntPtr result = IntPtr.Zero;
        if (_clipboardText is { } text)
        {
            if (target == _targets)
            {
                nint* list = stackalloc nint[4] { _targets, _utf8, XA_STRING, _textAtom };
                XChangeProperty(_display, requestor, property, XA_ATOM, 32, 0, list, 4);
                result = property;
            }
            else if (target == _utf8 || target == _textAtom || target == XA_STRING)
            {
                var bytes = target == XA_STRING ? Encoding.Latin1.GetBytes(text) : Encoding.UTF8.GetBytes(text);
                fixed (byte* b = bytes)
                    XChangeProperty(_display, requestor, property, target == XA_STRING ? XA_STRING : _utf8, 8, 0, b, bytes.Length);
                result = property;
            }
        }
        byte* reply = stackalloc byte[EventSize];
        new Span<byte>(reply, EventSize).Clear();
        *(int*)reply = SelectionNotify;
        *(int*)(reply + 16) = 1; // send_event
        *(IntPtr*)(reply + 24) = _display;
        *(IntPtr*)(reply + 32) = requestor;
        *(IntPtr*)(reply + 40) = selection;
        *(IntPtr*)(reply + 48) = target;
        *(IntPtr*)(reply + 56) = result;
        *(nuint*)(reply + 64) = time;
        XSendEvent(_display, requestor, 0, 0, reply);
        XFlush(_display);
    }

    public void CopyText(string text)
    {
        _clipboardText = text;
        XSetSelectionOwner(_display, _clipboard, _window, 0);
        XFlush(_display);
    }

    public string? PasteText()
    {
        if (XGetSelectionOwner(_display, _clipboard) == _window) return _clipboardText;
        XConvertSelection(_display, _clipboard, _utf8, _pasteProp, _window, 0);
        XFlush(_display);
        byte* ev = stackalloc byte[EventSize];
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 800)
        {
            if (XCheckTypedWindowEvent(_display, _window, SelectionNotify, ev) != 0)
            {
                var property = *(IntPtr*)(ev + 56);
                return property == IntPtr.Zero ? null : ReadTextProperty(property);
            }
            var fd = new PollFd { Fd = _xfd, Events = LibC.POLLIN };
            LibC.poll(&fd, 1, 50);
        }
        return null;
    }

    string? ReadTextProperty(IntPtr property)
    {
        IntPtr type, data;
        int format;
        nuint items, after;
        if (XGetWindowProperty(_display, _window, property, 0, 1 << 20, 1, IntPtr.Zero, &type, &format, &items, &after, &data) != 0 || data == IntPtr.Zero)
            return null;
        try
        {
            if (format != 8) return null;
            var span = new ReadOnlySpan<byte>((void*)data, (int)items);
            return type == XA_STRING ? Encoding.Latin1.GetString(span) : Encoding.UTF8.GetString(span);
        }
        finally
        {
            XFree(data);
        }
    }

    // =====================================================================================
    // IPlatform
    // =====================================================================================

    public bool IsMac => false;
    public bool CanShowProperties => false;

    public void Invalidate() => _dirty = true;

    public void Post(Action action)
    {
        _posted.Enqueue(action);
        byte b = 1;
        LibC.write(_wakePipe[1], &b, 1); // wake poll() up
    }

    public void StartTimer(int id, int milliseconds)
    {
        if (_timers.ContainsKey(id)) return;
        _timers[id] = (milliseconds, Environment.TickCount64 + milliseconds);
    }

    public void StopTimer(int id) => _timers.Remove(id);

    public void SetCursor(CursorKind cursor)
    {
        if (_cursor == cursor) return;
        _cursor = cursor;
        XDefineCursor(_display, _window, cursor switch { CursorKind.Hand => _hand, CursorKind.IBeam => _ibeam, _ => _arrow });
    }

    public void SetTitle(string title)
    {
        var bytes = Encoding.UTF8.GetBytes(title);
        fixed (byte* b = bytes)
        {
            XChangeProperty(_display, _window, _netWmName, _utf8, 8, 0, b, bytes.Length);
            XChangeProperty(_display, _window, (IntPtr)39, _utf8, 8, 0, b, bytes.Length); // WM_NAME
        }
    }

    /// <summary>The desktop's own folder dialog (zenity on GNOME-like desktops, kdialog on KDE).</summary>
    public string? PickFolder(string title)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        (string Exe, string[] Args)[] tools =
        [
            ("zenity", ["--file-selection", "--directory", "--title=" + title]),
            ("kdialog", ["--getexistingdirectory", home, "--title", title]),
            ("yad", ["--file", "--directory", "--title=" + title]),
        ];
        foreach (var (exe, args) in tools)
        {
            if (FindInPath(exe) is not { } path) continue;
            try
            {
                var psi = new ProcessStartInfo(path) { RedirectStandardOutput = true, UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var process = Process.Start(psi)!;
                var output = process.StandardOutput.ReadToEndAsync();
                PumpWhile(() => !process.HasExited); // keep repainting while the dialog is open
                process.WaitForExit();
                string folder = output.Result.Trim();
                return process.ExitCode == 0 && Directory.Exists(folder) ? folder : null;
            }
            catch
            {
                // try the next tool
            }
        }
        return null;
    }

    /// <summary>Handles window events (not input) while a helper process is running.</summary>
    void PumpWhile(Func<bool> waiting)
    {
        byte* ev = stackalloc byte[EventSize];
        while (waiting() && _running)
        {
            while (XPending(_display) > 0)
            {
                XNextEvent(_display, ev);
                int type = *(int*)ev;
                if (type is Expose or ConfigureNotify or SelectionRequest or SelectionClear or ClientMessage) Handle(ev);
            }
            if (_dirty)
            {
                _dirty = false;
                Paint();
            }
            XFlush(_display);
            var fd = new PollFd { Fd = _xfd, Events = LibC.POLLIN };
            LibC.poll(&fd, 1, 40);
        }
    }

    public bool OpenPath(string path) => Launch("xdg-open", path);

    public bool OpenUrl(string url) => Launch("xdg-open", url);

    /// <summary>With curl, or wget if there is no curl: nearly every distribution ships one of them.</summary>
    public string? DownloadText(string url) => FindInPath("curl") is not null
        ? RunFor(15000, "curl", "-fsSL", "--max-time", "12", url)
        : RunFor(15000, "wget", "-q", "-O", "-", "--timeout=12", url);

    public bool DownloadFile(string url, string destination)
    {
        bool ok = (FindInPath("curl") is not null
            ? RunFor(150000, "curl", "-fsSL", "--max-time", "140", "-o", destination, url)
            : RunFor(150000, "wget", "-q", "-O", destination, "--timeout=30", url)) is not null;
        if (ok && File.Exists(destination)) return true;
        try { File.Delete(destination); } catch { }
        return false;
    }

    public bool RevealPath(string path)
    {
        // The FileManager1 D-Bus interface selects the item (Nautilus, Dolphin, Nemo, Thunar...).
        string uri = new Uri(path).AbsoluteUri;
        if (Run("dbus-send", "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call",
                "/org/freedesktop/FileManager1", "org.freedesktop.FileManager1.ShowItems", $"array:string:{uri}", "string:") is not null)
            return true;
        return Launch("xdg-open", Path.GetDirectoryName(path) ?? path);
    }

    public bool MoveToTrash(string path, out string? error)
    {
        error = null;
        if (FindInPath("gio") is not null && Run("gio", "trash", "--", path) is not null && !File.Exists(path) && !Directory.Exists(path))
            return true;
        return FreedesktopTrash.Move(path, out error);
    }

    public void ShowProperties(string path) { }

    public void ApplyTheme(Theme theme)
    {
        if (_window == IntPtr.Zero) return;
        // GNOME / GTK window managers draw the title bar dark or light to match.
        var variant = Encoding.UTF8.GetBytes(theme.Dark ? "dark" : "light");
        fixed (byte* v = variant) XChangeProperty(_display, _window, _gtkVariant, _utf8, 8, 0, v, variant.Length);
        _dirty = true;
    }

    public void OnLanguageChanged() { }

    public void Quit() => _running = false;

    // =====================================================================================
    // Helpers
    // =====================================================================================

    static string? FindInPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':'))
        {
            if (dir.Length == 0) continue;
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    static bool Launch(string exe, string argument)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add(argument);
            using var _ = Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Runs a helper and returns its output, or null if it is missing, fails or takes too long.</summary>
    static string? Run(string exe, params string[] args) => RunFor(4000, exe, args);

    static string? RunFor(int milliseconds, string exe, params string[] args)
    {
        if (FindInPath(exe) is not { } path) return null;
        try
        {
            var psi = new ProcessStartInfo(path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(milliseconds))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            return p.ExitCode == 0 ? output.Result : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// The freedesktop.org trash (used when the <c>gio</c> tool is not available): items on the home volume go to
/// ~/.local/share/Trash, items on other volumes to &lt;mount&gt;/.Trash-&lt;uid&gt;, each with a .trashinfo record.
/// </summary>
[SupportedOSPlatform("linux")]
static class FreedesktopTrash
{
    public static bool Move(string path, out string? error)
    {
        error = null;
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share");
            string trash = Path.Combine(dataHome, "Trash");
            string? topDir = null;
            string? itemVolume = Volumes.FindForPath(path)?.RootPath, homeVolume = Volumes.FindForPath(dataHome)?.RootPath;
            if (itemVolume is not null && homeVolume is not null && itemVolume != homeVolume)
            {
                topDir = itemVolume;
                trash = Path.Combine(itemVolume, $".Trash-{LibC.getuid()}");
            }

            string files = Path.Combine(trash, "files"), info = Path.Combine(trash, "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);

            // Original location: absolute, or relative to the volume for a per-volume trash; URL-escaped.
            string original = topDir is null ? path : Path.GetRelativePath(topDir, path);
            string escaped = string.Join('/', original.Split('/').Select(Uri.EscapeDataString));
            string record = $"[Trash Info]\nPath={escaped}\nDeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n";

            string name = Path.GetFileName(path.TrimEnd('/'));
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            for (int n = 1; n < 10_000; n++)
            {
                string candidate = n == 1 ? name : $"{stem}.{n}{ext}";
                string infoPath = Path.Combine(info, candidate + ".trashinfo");
                FileStream stream;
                try { stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write); }
                catch (IOException) { continue; } // name taken: try the next one
                using (stream) stream.Write(Encoding.UTF8.GetBytes(record));

                string target = Path.Combine(files, candidate);
                try
                {
                    if (Directory.Exists(path)) Directory.Move(path, target);
                    else File.Move(path, target);
                    return true;
                }
                catch
                {
                    File.Delete(infoPath);
                    throw;
                }
            }
            error = "trash is full";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
