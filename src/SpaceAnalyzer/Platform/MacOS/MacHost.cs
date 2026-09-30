using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SpaceAnalyzer.Core;
using SpaceAnalyzer.Render;
using SpaceAnalyzer.UI;
using static SpaceAnalyzer.Platform.MacOS.ObjC;

namespace SpaceAnalyzer.Platform.MacOS;

/// <summary>
/// macOS shim: an NSWindow whose content view (a tiny NSView subclass registered at runtime) copies the
/// app's pixel buffer to the screen and forwards mouse and keyboard input. All drawing happens in C#.
/// </summary>
sealed unsafe class MacHost : IPlatform
{
    static MacHost s_host = null!;

    const long ShiftFlag = 1 << 17, ControlFlag = 1 << 18, OptionFlag = 1 << 19, CommandFlag = 1 << 20;

    MainView _view = null!;
    Surface? _surface;
    readonly ConcurrentQueue<Action> _posted = new();
    readonly Dictionary<int, IntPtr> _timers = new();
    IntPtr _app, _window, _nsView;
    float _backing = 2;
    int _pixelW, _pixelH;
    Theme _theme = Theme.DarkTheme;
    CursorKind _cursor = CursorKind.Arrow;

    public static int Run(Options options)
    {
        NativeLibrary.Load(Native.AppKit);
        var pool = objc_autoreleasePoolPush();
        try
        {
            Strings.Spanish = options.Lang is { } lang ? lang.StartsWith("es", StringComparison.OrdinalIgnoreCase) : PrefersSpanish();
            Volumes.DisplayNameProvider = VolumeDisplayName;
            s_host = new MacHost();
            s_host.Launch(options);
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
        Send(s_host._app, "run");
        return 0;
    }

    void Launch(Options options)
    {
        _app = Send(Class("NSApplication"), "sharedApplication");
        SendLong(_app, "setActivationPolicy:", 0); // a regular app: Dock icon and menu bar
        RegisterViewClass();
        _view = new MainView(this, UpdatePrefs.ForThisUser(), UpdateTarget.ForThisProcess());
        CreateWindow();
        BuildMainMenu();
        SetAppIcon();
        _view.SetSystemDark(SystemIsDark());

        var center = Send(Class("NSDistributedNotificationCenter"), "defaultCenter");
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)MsgSend)(
            center, Sel("addObserver:selector:name:object:"), _nsView, Sel("themeChanged:"),
            Str("AppleInterfaceThemeChangedNotification"), IntPtr.Zero);

        _view.Start(options.Path);
        Send(_window, "makeKeyAndOrderFront:", IntPtr.Zero);
        SendBool(_app, "activateIgnoringOtherApps:", true);
    }

    void CreateWindow()
    {
        var screen = Send(Class("NSScreen"), "mainScreen");
        var visible = screen != IntPtr.Zero ? GetRect(screen, "visibleFrame") : new CGRect(0, 0, 1440, 900);
        double w = Math.Clamp(visible.W * 0.8, 960, 1640), h = Math.Clamp(visible.H * 0.84, 620, 1060);
        var frame = new CGRect(0, 0, w, h);

        const ulong style = 1 | 2 | 4 | 8; // titled | closable | miniaturizable | resizable
        _window = ((delegate* unmanaged<IntPtr, IntPtr, CGRect, ulong, ulong, byte, IntPtr>)MsgSend)(
            Alloc("NSWindow"), Sel("initWithContentRect:styleMask:backing:defer:"), frame, style, 2, 0);
        SendBool(_window, "setReleasedWhenClosed:", false);
        SendBool(_window, "setTitlebarAppearsTransparent:", true);
        if (GetBool(_window, "respondsToSelector:", Sel("setTitlebarSeparatorStyle:")))
            SendLong(_window, "setTitlebarSeparatorStyle:", 1);
        Send(_window, "setTitle:", Str(Strings.AppName));
        SendSize(_window, "setContentMinSize:", new CGSize(880, 580));
        SendBool(_window, "setAcceptsMouseMovedEvents:", true);

        _nsView = InitWithRect(Alloc("SACanvasView"), "initWithFrame:", frame);
        Send(_window, "setContentView:", _nsView);
        Send(_window, "setDelegate:", _nsView);

        const ulong tracking = 0x01 | 0x02 | 0x04 | 0x80 | 0x200; // entered/exited, moved, cursor, always, visible rect
        var area = ((delegate* unmanaged<IntPtr, IntPtr, CGRect, ulong, IntPtr, IntPtr, IntPtr>)MsgSend)(
            Alloc("NSTrackingArea"), Sel("initWithRect:options:owner:userInfo:"), new CGRect(0, 0, 0, 0), tracking, _nsView, IntPtr.Zero);
        Send(_nsView, "addTrackingArea:", area);
        Send(area, "release");

        var dragTypes = Send(Class("NSArray"), "arrayWithObject:", Native.Symbol(Native.AppKit, "NSPasteboardTypeFileURL"));
        Send(_nsView, "registerForDraggedTypes:", dragTypes);

        Send(_window, "makeFirstResponder:", _nsView);
        Send(_window, "center");
        _backing = (float)GetDouble(_window, "backingScaleFactor");
        ApplyTheme(_theme);
        SyncSize();
    }

    void SetAppIcon()
    {
        var surface = new Surface(512, 512);
        surface.Clear(0);
        MainView.DrawLogo(new SoftCanvas(surface), new RectF(44, 44, 424, 424));
        using var image = CreateImage(surface);
        var nsImage = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, CGSize, IntPtr>)MsgSend)(
            Alloc("NSImage"), Sel("initWithCGImage:size:"), image.Image, new CGSize(256, 256));
        Send(_app, "setApplicationIconImage:", nsImage);
        Send(nsImage, "release");
    }

    /// <summary>A CGImage that reads the surface's pixels directly (no copy).</summary>
    readonly struct SurfaceImage(IntPtr provider, IntPtr image) : IDisposable
    {
        public IntPtr Image => image;
        public void Dispose()
        {
            CG.CGImageRelease(image);
            CG.CGDataProviderRelease(provider);
        }
    }

    static SurfaceImage CreateImage(Surface s)
    {
        var provider = CG.CGDataProviderCreateWithData(IntPtr.Zero, s.Pointer, (nint)s.Width * s.Height * 4, IntPtr.Zero);
        var image = CG.CGImageCreate(s.Width, s.Height, 8, 32, s.Width * 4, CG.SRGB, CG.BitmapInfo, provider, IntPtr.Zero, 0, 0);
        return new SurfaceImage(provider, image);
    }

    // =====================================================================================
    // Menu bar (the one piece of native UI a Mac app is expected to have)
    // =====================================================================================

    void BuildMainMenu()
    {
        bool es = Strings.Spanish;
        string T(string s, string e) => es ? s : e;
        const long cmd = CommandFlag, shift = ShiftFlag, opt = OptionFlag;
        var main = NewMenu("");

        var app = NewMenu(Strings.AppName);
        AddCmd(app, Strings.About, Cmd.About);
        if (_view.IsEnabled(Cmd.ToggleAutoUpdate)) AddCmd(app, _view.AutoUpdateLabel, Cmd.ToggleAutoUpdate, "");
        AddSeparator(app);
        AddStd(app, T("Ocultar SpaceAnalyzer", "Hide SpaceAnalyzer"), "hide:", "h", cmd);
        AddStd(app, T("Ocultar otros", "Hide Others"), "hideOtherApplications:", "h", cmd | opt);
        AddStd(app, T("Mostrar todo", "Show All"), "unhideAllApplications:", "", cmd);
        AddSeparator(app);
        AddStd(app, T("Salir de SpaceAnalyzer", "Quit SpaceAnalyzer"), "terminate:", "q", cmd);
        AddSubmenu(main, app);

        var file = NewMenu(T("Archivo", "File"));
        AddCmd(file, Strings.ChooseFolder, Cmd.OpenFolder, "o");
        AddCmd(file, Strings.HomeFolder, Cmd.HomeFolder, "h", cmd | shift);
        AddCmd(file, Strings.Rescan, Cmd.Rescan, "r");
        AddSeparator(file);
        AddCmd(file, Strings.OpenItem, Cmd.OpenItem);
        AddCmd(file, Strings.Reveal, Cmd.RevealItem, "r", cmd | shift);
        AddCmd(file, Strings.CopyPath, Cmd.CopyPath, "c", cmd | opt);
        AddCmd(file, Strings.RescanFolder, Cmd.RescanItem);
        AddCmd(file, Strings.AskAi, Cmd.AskAi);
        AddSeparator(file);
        AddCmd(file, Strings.Trash, Cmd.TrashItem, "\b");
        AddSeparator(file);
        AddStd(file, T("Cerrar ventana", "Close Window"), "performClose:", "w", cmd);
        AddSubmenu(main, file);

        var edit = NewMenu(T("Edición", "Edit"));
        AddStd(edit, T("Cortar", "Cut"), "cut:", "x", cmd);
        AddStd(edit, T("Copiar", "Copy"), "copy:", "c", cmd);
        AddStd(edit, T("Pegar", "Paste"), "paste:", "v", cmd);
        AddStd(edit, T("Seleccionar todo", "Select All"), "selectAll:", "a", cmd);
        AddSeparator(edit);
        AddCmd(edit, Strings.Search, Cmd.Find, "f");
        AddSubmenu(main, edit);

        var view = NewMenu(T("Visualización", "View"));
        AddCmd(view, Strings.ColorTypeLong, Cmd.ColorType, "1");
        AddCmd(view, Strings.ColorDepthLong, Cmd.ColorDepth, "2");
        AddCmd(view, Strings.ColorAgeLong, Cmd.ColorAge, "3");
        AddSeparator(view);
        AddCmd(view, Strings.ShowFreeSpace, Cmd.ToggleFreeSpace);
        AddCmd(view, Strings.Sidebar, Cmd.ToggleSidebar, "s", cmd | opt);
        AddSeparator(view);
        AddCmd(view, Strings.DetailLow, Cmd.DetailLow);
        AddCmd(view, Strings.DetailNormal, Cmd.DetailNormal);
        AddCmd(view, Strings.DetailHigh, Cmd.DetailHigh);
        AddSeparator(view);
        AddCmd(view, Strings.ThemeSystem, Cmd.ThemeSystem);
        AddCmd(view, Strings.ThemeDark, Cmd.ThemeDark);
        AddCmd(view, Strings.ThemeLight, Cmd.ThemeLight);
        AddSeparator(view);
        AddCmd(view, "Español", Cmd.LangEs);
        AddCmd(view, "English", Cmd.LangEn);
        AddSubmenu(main, view);

        var go = NewMenu(T("Ir", "Go"));
        AddCmd(go, Strings.Back, Cmd.Back, "[");
        AddCmd(go, Strings.Forward, Cmd.Forward, "]");
        AddCmd(go, Strings.Up, Cmd.Up, "");
        AddCmd(go, Strings.ZoomIn, Cmd.ZoomIn, "");
        AddSeparator(go);
        AddCmd(go, Strings.Home, Cmd.Home, "d", cmd | shift);
        AddSubmenu(main, go);

        var window = NewMenu(T("Ventana", "Window"));
        AddStd(window, T("Minimizar", "Minimize"), "performMiniaturize:", "m", cmd);
        AddStd(window, "Zoom", "performZoom:", "", cmd);
        AddSubmenu(main, window);
        Send(_app, "setWindowsMenu:", window);

        Send(_app, "setMainMenu:", main);
        Send(main, "release");
    }

    static IntPtr NewMenu(string title) => Send(Alloc("NSMenu"), "initWithTitle:", Str(title));

    static void AddSeparator(IntPtr menu) => Send(menu, "addItem:", Send(Class("NSMenuItem"), "separatorItem"));

    static IntPtr AddStd(IntPtr menu, string title, string action, string key, long modifiers)
    {
        var item = Send(Alloc("NSMenuItem"), "initWithTitle:action:keyEquivalent:", Str(title), Sel(action), Str(key));
        SendLong(item, "setKeyEquivalentModifierMask:", modifiers);
        Send(menu, "addItem:", item);
        Send(item, "release");
        return item;
    }

    IntPtr AddCmd(IntPtr menu, string title, Cmd command, string key = "", long modifiers = CommandFlag)
    {
        var item = AddStd(menu, title, "menuItemClicked:", key, modifiers);
        Send(item, "setTarget:", _nsView);
        SendLong(item, "setTag:", (long)command);
        return item;
    }

    static void AddSubmenu(IntPtr main, IntPtr submenu)
    {
        var item = Send(Alloc("NSMenuItem"), "init");
        Send(item, "setSubmenu:", submenu);
        Send(main, "addItem:", item);
        Send(item, "release");
        Send(submenu, "release");
    }

    // =====================================================================================
    // The NSView subclass
    // =====================================================================================

    static readonly string[] s_eventNames =
    [
        "mouseDown:", "mouseUp:", "mouseDragged:", "mouseMoved:", "mouseExited:", "mouseEntered:",
        "rightMouseDown:", "rightMouseUp:", "otherMouseDown:", "otherMouseUp:", "rightMouseDragged:",
        "scrollWheel:", "magnifyWithEvent:", "keyDown:", "cursorUpdate:",
        "menuItemClicked:", "timerFired:", "copy:", "cut:", "paste:", "selectAll:", "themeChanged:",
        "windowWillClose:", "windowDidResize:", "windowDidChangeBackingProperties:",
    ];
    static readonly Dictionary<IntPtr, string> s_events = new();

    static void RegisterViewClass()
    {
        var cls = objc_allocateClassPair(Class("NSView"), "SACanvasView", 0);
        if (cls == IntPtr.Zero) return;
        AddMethod(cls, "drawRect:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, CGRect, void>)&DrawRect, "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");
        AddMethod(cls, "isFlipped", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Yes, "c@:");
        AddMethod(cls, "isOpaque", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Yes, "c@:");
        AddMethod(cls, "acceptsFirstResponder", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&Yes, "c@:");
        AddMethod(cls, "mouseDownCanMoveWindow", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, byte>)&No, "c@:");
        AddMethod(cls, "acceptsFirstMouse:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&YesWithArg, "c@:@");
        var ev = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&OnEvent;
        foreach (var name in s_eventNames)
        {
            AddMethod(cls, name, ev, "v@:@");
            s_events[Sel(name)] = name;
        }
        AddMethod(cls, "validateMenuItem:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&ValidateMenuItem, "c@:@");
        AddMethod(cls, "draggingEntered:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nuint>)&DraggingEntered, "Q@:@");
        AddMethod(cls, "performDragOperation:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)&PerformDrag, "c@:@");
        objc_registerClassPair(cls);
    }

    [UnmanagedCallersOnly] static byte Yes(IntPtr self, IntPtr sel) => 1;
    [UnmanagedCallersOnly] static byte No(IntPtr self, IntPtr sel) => 0;
    [UnmanagedCallersOnly] static byte YesWithArg(IntPtr self, IntPtr sel, IntPtr arg) => 1;

    [UnmanagedCallersOnly]
    static void DrawRect(IntPtr self, IntPtr sel, CGRect dirty)
    {
        try { s_host.OnDraw(); }
        catch (Exception ex) { Report(ex); }
    }

    [UnmanagedCallersOnly]
    static void OnEvent(IntPtr self, IntPtr sel, IntPtr arg)
    {
        try
        {
            if (s_events.TryGetValue(sel, out var name)) s_host.HandleEvent(name, arg);
        }
        catch (Exception ex) { Report(ex); }
    }

    [UnmanagedCallersOnly]
    static byte ValidateMenuItem(IntPtr self, IntPtr sel, IntPtr item)
    {
        try
        {
            if (Send(item, "action") == Sel("menuItemClicked:"))
            {
                var command = (Cmd)GetLong(item, "tag");
                SendLong(item, "setState:", s_host._view.IsChecked(command) ? 1 : 0);
                return s_host._view.IsEnabled(command) ? (byte)1 : (byte)0;
            }
        }
        catch (Exception ex) { Report(ex); }
        return 1;
    }

    [UnmanagedCallersOnly]
    static nuint DraggingEntered(IntPtr self, IntPtr sel, IntPtr info) => 1; // NSDragOperationCopy

    [UnmanagedCallersOnly]
    static byte PerformDrag(IntPtr self, IntPtr sel, IntPtr info)
    {
        try
        {
            var pasteboard = Send(info, "draggingPasteboard");
            var classes = Send(Class("NSArray"), "arrayWithObject:", Class("NSURL"));
            var urls = Send(pasteboard, "readObjectsForClasses:options:", classes, IntPtr.Zero);
            if (urls == IntPtr.Zero) return 0;
            var paths = new List<string>();
            long count = GetLong(urls, "count");
            for (long i = 0; i < count; i++)
                if (ToManaged(Send(Send(urls, "objectAtIndex:", (IntPtr)i), "path")) is { } p) paths.Add(p);
            s_host._view.OnFilesDropped(paths);
            return 1;
        }
        catch (Exception ex) { Report(ex); return 0; }
    }

    void HandleEvent(string name, IntPtr arg)
    {
        switch (name)
        {
            case "mouseDown:":
            {
                var (x, y) = Location(arg);
                var mods = Modifiers(arg);
                if ((mods & Mods.Ctrl) != 0) _view.OnMouseDown(x, y, MouseButton.Right, 1, mods);
                else _view.OnMouseDown(x, y, MouseButton.Left, (int)GetLong(arg, "clickCount"), mods);
                break;
            }
            case "mouseUp:":
            {
                var (x, y) = Location(arg);
                _view.OnMouseUp(x, y, MouseButton.Left);
                break;
            }
            case "mouseMoved:" or "mouseDragged:" or "rightMouseDragged:" or "mouseEntered:":
            {
                var (x, y) = Location(arg);
                _view.OnMouseMove(x, y);
                break;
            }
            case "mouseExited:":
                _view.OnMouseLeave();
                break;
            case "rightMouseDown:":
            {
                var (x, y) = Location(arg);
                _view.OnMouseDown(x, y, MouseButton.Right, 1, Modifiers(arg));
                break;
            }
            case "otherMouseDown:":
            {
                var (x, y) = Location(arg);
                var b = GetLong(arg, "buttonNumber") switch { 3 => MouseButton.Back, 4 => MouseButton.Forward, _ => MouseButton.Middle };
                _view.OnMouseDown(x, y, b, 1, Modifiers(arg));
                break;
            }
            case "scrollWheel:":
                OnScroll(arg);
                break;
            case "magnifyWithEvent:":
            {
                var (x, y) = Location(arg);
                _view.OnMagnify(x, y, (float)GetDouble(arg, "magnification"));
                break;
            }
            case "keyDown:":
                OnKeyDown(arg);
                break;
            case "cursorUpdate:":
                ApplyCursor();
                break;
            case "menuItemClicked:":
                _view.Execute((Cmd)(int)GetLong(arg, "tag"));
                break;
            case "timerFired:":
                _view.OnTimer((int)GetLong(Send(arg, "userInfo"), "integerValue"));
                break;
            // Edit menu: behave like the matching ⌘ shortcut (search box first, then the selection).
            case "copy:": _view.OnKeyDown(Key.C, Mods.Meta); break;
            case "cut:": _view.OnKeyDown(Key.X, Mods.Meta); break;
            case "paste:": _view.OnKeyDown(Key.V, Mods.Meta); break;
            case "selectAll:": _view.OnKeyDown(Key.A, Mods.Meta); break;
            case "themeChanged:":
                Post(() => _view.SetSystemDark(SystemIsDark()));
                break;
            case "windowWillClose:":
                Send(_app, "terminate:", IntPtr.Zero);
                break;
            case "windowDidResize:":
                SyncSize();
                break;
            case "windowDidChangeBackingProperties:":
                _backing = (float)GetDouble(_window, "backingScaleFactor");
                SyncSize();
                _view.InvalidateLayers();
                break;
        }
    }

    (float X, float Y) Location(IntPtr ev)
    {
        var inWindow = GetPoint(ev, "locationInWindow");
        var p = ((delegate* unmanaged<IntPtr, IntPtr, CGPoint, IntPtr, CGPoint>)MsgSend)(
            _nsView, Sel("convertPoint:fromView:"), inWindow, IntPtr.Zero);
        return ((float)(p.X * _backing), (float)(p.Y * _backing)); // points → pixels
    }

    static Mods Modifiers(IntPtr ev)
    {
        long f = GetLong(ev, "modifierFlags");
        var m = Mods.None;
        if ((f & ShiftFlag) != 0) m |= Mods.Shift;
        if ((f & ControlFlag) != 0) m |= Mods.Ctrl;
        if ((f & OptionFlag) != 0) m |= Mods.Alt;
        if ((f & CommandFlag) != 0) m |= Mods.Meta;
        return m;
    }

    void OnScroll(IntPtr ev)
    {
        if (GetLong(ev, "momentumPhase") != 0) return; // ignore inertia
        double dy = GetDouble(ev, "scrollingDeltaY");
        if (GetBool(ev, "isDirectionInvertedFromDevice")) dy = -dy;
        double delta = GetBool(ev, "hasPreciseScrollingDeltas") ? dy / 25.0 : Math.Sign(dy);
        if (delta == 0) return;
        var (x, y) = Location(ev);
        _view.OnMouseWheel(x, y, (float)delta);
    }

    void OnKeyDown(IntPtr ev)
    {
        int code = (int)(GetLong(ev, "keyCode") & 0xFFFF);
        var mods = Modifiers(ev);
        var key = code switch
        {
            36 or 76 => Key.Enter,
            53 => Key.Escape,
            51 => Key.Backspace,
            117 => Key.Delete,
            49 => Key.Space,
            48 => Key.Tab,
            123 => Key.Left,
            124 => Key.Right,
            125 => Key.Down,
            126 => Key.Up,
            115 => Key.Home,
            119 => Key.End,
            116 => Key.PageUp,
            121 => Key.PageDown,
            122 => Key.F1,
            120 => Key.F2,
            99 => Key.F3,
            118 => Key.F4,
            96 => Key.F5,
            _ => Key.None,
        };
        string? chars = ToManaged(Send(ev, "characters"));
        if (key == Key.None && ToManaged(Send(ev, "charactersIgnoringModifiers")) is { Length: > 0 } plain)
        {
            char ch = char.ToLowerInvariant(plain[0]);
            if (ch is >= 'a' and <= 'z') key = Key.A + (ch - 'a');
            else if (ch is >= '0' and <= '9') key = Key.D0 + (ch - '0');
            else if (ch == '[') key = Key.OpenBracket;
            else if (ch == ']') key = Key.CloseBracket;
        }
        bool handled = key != Key.None && _view.OnKeyDown(key, mods);
        // Typed characters (layout and dead keys already applied by AppKit); skip function keys (U+F700…).
        if (!handled && (mods & (Mods.Meta | Mods.Ctrl)) == 0 && chars is { Length: > 0 } && !char.IsControl(chars[0]) && chars[0] < '')
            _view.OnTextInput(chars);
    }

    void OnDraw()
    {
        var context = Send(Class("NSGraphicsContext"), "currentContext");
        if (context == IntPtr.Zero) return;
        var cg = Send(context, "CGContext");
        if (cg == IntPtr.Zero) return;
        SyncSize();
        if (_surface is null || _surface.Width != _pixelW || _surface.Height != _pixelH)
            _surface = new Surface(_pixelW, _pixelH);
        _view.Paint(new SoftCanvas(_surface));

        var b = GetRect(_nsView, "bounds");
        using var image = CreateImage(_surface);
        CG.CGContextSaveGState(cg);
        CG.CGContextSetInterpolationQuality(cg, 1); // none: pixels map 1:1 to the backing store
        CG.CGContextTranslateCTM(cg, 0, b.H);
        CG.CGContextScaleCTM(cg, 1, -1);
        CG.CGContextDrawImage(cg, new CGRect(0, 0, b.W, b.H), image.Image);
        CG.CGContextRestoreGState(cg);
    }

    void SyncSize()
    {
        if (_nsView == IntPtr.Zero) return;
        var b = GetRect(_nsView, "bounds");
        int w = Math.Max(1, (int)Math.Round(b.W * _backing)), h = Math.Max(1, (int)Math.Round(b.H * _backing));
        if (w == _pixelW && h == _pixelH) return;
        _pixelW = w;
        _pixelH = h;
        _view.OnResize(w, h, _backing);
    }

    static void Report(Exception ex) => ErrorLog.Write(ex);

    // =====================================================================================
    // IPlatform
    // =====================================================================================

    public bool IsMac => true;
    public bool CanShowProperties => false;

    public void Invalidate()
    {
        if (_nsView != IntPtr.Zero) SendBool(_nsView, "setNeedsDisplay:", true);
    }

    public void Post(Action action)
    {
        _posted.Enqueue(action);
        Dispatch.dispatch_async_f(Dispatch.MainQueue, IntPtr.Zero, &DrainPosted);
    }

    [UnmanagedCallersOnly]
    static void DrainPosted(IntPtr context)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            while (s_host._posted.TryDequeue(out var action)) action();
        }
        catch (Exception ex) { Report(ex); }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public void StartTimer(int id, int milliseconds)
    {
        if (_timers.ContainsKey(id) || _nsView == IntPtr.Zero) return;
        var info = Send(Class("NSNumber"), "numberWithInteger:", (IntPtr)id);
        var timer = ((delegate* unmanaged<IntPtr, IntPtr, double, IntPtr, IntPtr, IntPtr, byte, IntPtr>)MsgSend)(
            Class("NSTimer"), Sel("timerWithTimeInterval:target:selector:userInfo:repeats:"),
            milliseconds / 1000.0, _nsView, Sel("timerFired:"), info, 1);
        // Common modes: keep firing during live resize and menu tracking.
        Send(Send(Class("NSRunLoop"), "currentRunLoop"), "addTimer:forMode:", timer, CF.RunLoopCommonModes);
        Send(timer, "retain");
        _timers[id] = timer;
    }

    public void StopTimer(int id)
    {
        if (!_timers.Remove(id, out var timer)) return;
        Send(timer, "invalidate");
        Send(timer, "release");
    }

    public void SetCursor(CursorKind cursor)
    {
        if (_cursor == cursor) return;
        _cursor = cursor;
        ApplyCursor();
    }

    void ApplyCursor() => Send(Send(Class("NSCursor"), _cursor switch
    {
        CursorKind.Hand => "pointingHandCursor",
        CursorKind.IBeam => "IBeamCursor",
        _ => "arrowCursor",
    }), "set");

    public void SetTitle(string title)
    {
        if (_window != IntPtr.Zero) Send(_window, "setTitle:", Str(title));
    }

    public string? PickFolder(string title)
    {
        var panel = Send(Class("NSOpenPanel"), "openPanel");
        SendBool(panel, "setCanChooseDirectories:", true);
        SendBool(panel, "setCanChooseFiles:", false);
        SendBool(panel, "setAllowsMultipleSelection:", false);
        Send(panel, "setMessage:", Str(title));
        Send(panel, "setPrompt:", Str(Strings.Choose));
        if (GetLong(panel, "runModal") != 1) return null; // NSModalResponseOK
        var url = Send(Send(panel, "URLs"), "firstObject");
        return url == IntPtr.Zero ? null : ToManaged(Send(url, "path"));
    }

    static IntPtr FileUrl(string path) => Send(Class("NSURL"), "fileURLWithPath:", Str(path));
    static IntPtr Workspace => Send(Class("NSWorkspace"), "sharedWorkspace");

    public bool OpenPath(string path) => GetBool(Workspace, "openURL:", FileUrl(path));

    public bool OpenUrl(string url)
    {
        var nsUrl = Send(Class("NSURL"), "URLWithString:", Str(url)); // nil if the address is malformed
        return nsUrl != IntPtr.Zero && GetBool(Workspace, "openURL:", nsUrl);
    }

    /// <summary>With Foundation, which uses the system's proxy settings and certificate store. Called on a worker thread.</summary>
    public string? DownloadText(string url)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var nsUrl = Send(Class("NSURL"), "URLWithString:", Str(url));
            if (nsUrl == IntPtr.Zero) return null;
            const long Utf8 = 4; // NSUTF8StringEncoding
            return ToManaged(Send(Class("NSString"), "stringWithContentsOfURL:encoding:error:", nsUrl, (IntPtr)Utf8, IntPtr.Zero));
        }
        catch (Exception ex) { Report(ex); return null; }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public bool DownloadFile(string url, string destination)
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var nsUrl = Send(Class("NSURL"), "URLWithString:", Str(url));
            if (nsUrl == IntPtr.Zero) return false;
            var data = Send(Class("NSData"), "dataWithContentsOfURL:", nsUrl);
            if (data == IntPtr.Zero) return false;
            return ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte, byte>)MsgSend)(
                data, Sel("writeToFile:atomically:"), Str(destination), 1) != 0;
        }
        catch (Exception ex) { Report(ex); return false; }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public bool RevealPath(string path) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, byte>)MsgSend)(
            Workspace, Sel("selectFile:inFileViewerRootedAtPath:"), Str(path), Str("")) != 0;

    public bool MoveToTrash(string path, out string? error)
    {
        error = null;
        IntPtr err = IntPtr.Zero;
        var manager = Send(Class("NSFileManager"), "defaultManager");
        bool ok = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr*, byte>)MsgSend)(
            manager, Sel("trashItemAtURL:resultingItemURL:error:"), FileUrl(path), IntPtr.Zero, &err) != 0;
        if (!ok && err != IntPtr.Zero) error = ToManaged(Send(err, "localizedDescription"));
        return ok;
    }

    static IntPtr StringType => Native.Symbol(Native.AppKit, "NSPasteboardTypeString");

    public void CopyText(string text)
    {
        var pasteboard = Send(Class("NSPasteboard"), "generalPasteboard");
        Send(pasteboard, "clearContents");
        Send(pasteboard, "setString:forType:", Str(text), StringType);
    }

    public string? PasteText() =>
        ToManaged(Send(Send(Class("NSPasteboard"), "generalPasteboard"), "stringForType:", StringType));

    public void ShowProperties(string path) { }

    public void ApplyTheme(Theme theme)
    {
        _theme = theme;
        if (_window == IntPtr.Zero) return;
        var appearance = Send(Class("NSAppearance"), "appearanceNamed:", Str(theme.Dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua"));
        Send(_window, "setAppearance:", appearance);
        var bg = theme.ToolbarBg;
        var color = ((delegate* unmanaged<IntPtr, IntPtr, double, double, double, double, IntPtr>)MsgSend)(
            Class("NSColor"), Sel("colorWithSRGBRed:green:blue:alpha:"), bg.R / 255.0, bg.G / 255.0, bg.B / 255.0, 1.0);
        Send(_window, "setBackgroundColor:", color); // the transparent title bar shows this color
        Invalidate();
    }

    public void OnLanguageChanged()
    {
        if (_app != IntPtr.Zero) BuildMainMenu();
    }

    public void Quit() => Send(_app, "terminate:", IntPtr.Zero);

    // =====================================================================================
    // Helpers
    // =====================================================================================

    bool SystemIsDark()
    {
        var appearance = Send(_app, "effectiveAppearance");
        return appearance == IntPtr.Zero || (ToManaged(Send(appearance, "name")) ?? "").Contains("Dark", StringComparison.Ordinal);
    }

    static bool PrefersSpanish()
    {
        var langs = CF.CFLocaleCopyPreferredLanguages();
        if (langs == IntPtr.Zero) return false;
        try
        {
            return CF.CFArrayGetCount(langs) > 0 &&
                   (ToManaged(CF.CFArrayGetValueAtIndex(langs, 0)) ?? "").StartsWith("es", StringComparison.OrdinalIgnoreCase);
        }
        finally { CF.CFRelease(langs); }
    }

    static string? VolumeDisplayName(string path)
    {
        var pool = objc_autoreleasePoolPush();
        try { return ToManaged(Send(Send(Class("NSFileManager"), "defaultManager"), "displayNameAtPath:", Str(path))); }
        finally { objc_autoreleasePoolPop(pool); }
    }
}
