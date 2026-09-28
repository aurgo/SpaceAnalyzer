using System.Runtime.InteropServices;

namespace SpaceAnalyzer.Platform.Linux;

/// <summary>The handful of Xlib calls the Linux shim needs (libX11.so.6, present on every X11 / XWayland desktop).</summary>
static unsafe partial class Xlib
{
    const string Lib = "libX11.so.6";

    [LibraryImport(Lib)] public static partial IntPtr XOpenDisplay(IntPtr name);
    [LibraryImport(Lib)] public static partial int XCloseDisplay(IntPtr display);
    [LibraryImport(Lib)] public static partial int XDefaultScreen(IntPtr display);
    [LibraryImport(Lib)] public static partial IntPtr XRootWindow(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial IntPtr XDefaultVisual(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial int XDefaultDepth(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial IntPtr XDefaultGC(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial int XDisplayWidth(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial int XDisplayHeight(IntPtr display, int screen);
    [LibraryImport(Lib)] public static partial IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);
    [LibraryImport(Lib)] public static partial int XDestroyWindow(IntPtr display, IntPtr window);
    [LibraryImport(Lib)] public static partial int XSelectInput(IntPtr display, IntPtr window, nint mask);
    [LibraryImport(Lib)] public static partial int XMapWindow(IntPtr display, IntPtr window);
    [LibraryImport(Lib)] public static partial int XFlush(IntPtr display);
    [LibraryImport(Lib)] public static partial int XPending(IntPtr display);
    [LibraryImport(Lib)] public static partial int XEventsQueued(IntPtr display, int mode);
    [LibraryImport(Lib)] public static partial int XNextEvent(IntPtr display, void* ev);
    [LibraryImport(Lib)] public static partial int XCheckTypedWindowEvent(IntPtr display, IntPtr window, int type, void* ev);
    [LibraryImport(Lib)] public static partial int XSendEvent(IntPtr display, IntPtr window, int propagate, nint mask, void* ev);
    [LibraryImport(Lib)] public static partial int XFilterEvent(void* ev, IntPtr window);
    [LibraryImport(Lib)] public static partial int XConnectionNumber(IntPtr display);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr XInternAtom(IntPtr display, string name, int onlyIfExists);
    [LibraryImport(Lib)] public static partial int XSetWMProtocols(IntPtr display, IntPtr window, IntPtr* protocols, int count);
    [LibraryImport(Lib)] public static partial int XSetWMNormalHints(IntPtr display, IntPtr window, void* hints);
    [LibraryImport(Lib)] public static partial int XSetClassHint(IntPtr display, IntPtr window, void* hint);
    [LibraryImport(Lib)] public static partial int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, void* data, int count);
    [LibraryImport(Lib)] public static partial int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, nint offset, nint length, int delete, IntPtr requestType, IntPtr* actualType, int* actualFormat, nuint* items, nuint* bytesAfter, IntPtr* data);
    [LibraryImport(Lib)] public static partial int XFree(IntPtr data);
    [LibraryImport(Lib)] public static partial IntPtr XCreateImage(IntPtr display, IntPtr visual, uint depth, int format, int offset, void* data, uint width, uint height, int bitmapPad, int bytesPerLine);
    [LibraryImport(Lib)] public static partial int XPutImage(IntPtr display, IntPtr drawable, IntPtr gc, IntPtr image, int srcX, int srcY, int destX, int destY, uint width, uint height);
    [LibraryImport(Lib)] public static partial IntPtr XCreateFontCursor(IntPtr display, uint shape);
    [LibraryImport(Lib)] public static partial int XDefineCursor(IntPtr display, IntPtr window, IntPtr cursor);
    [LibraryImport(Lib)] public static partial int XLookupString(void* keyEvent, byte* buffer, int length, nuint* keysym, IntPtr status);
    [LibraryImport(Lib)] public static partial int Xutf8LookupString(IntPtr ic, void* keyEvent, byte* buffer, int length, nuint* keysym, int* status);
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr XSetLocaleModifiers(string modifiers);
    [LibraryImport(Lib)] public static partial IntPtr XOpenIM(IntPtr display, IntPtr database, IntPtr resName, IntPtr resClass);
    /// <summary>XCreateIC is variadic; on Linux (x86-64 and ARM64) the extra arguments are passed like regular ones.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr XCreateIC(IntPtr im, string name1, nint value1, string name2, IntPtr value2, string name3, IntPtr value3, IntPtr end);
    [LibraryImport(Lib)] public static partial void XSetICFocus(IntPtr ic);
    [LibraryImport(Lib)] public static partial int XSetSelectionOwner(IntPtr display, IntPtr selection, IntPtr owner, nuint time);
    [LibraryImport(Lib)] public static partial IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [LibraryImport(Lib)] public static partial int XConvertSelection(IntPtr display, IntPtr selection, IntPtr target, IntPtr property, IntPtr requestor, nuint time);
    [LibraryImport(Lib)] public static partial IntPtr XResourceManagerString(IntPtr display);
    [LibraryImport(Lib)] public static partial IntPtr XSetErrorHandler(IntPtr handler);

    // Predefined atoms
    public static readonly IntPtr XA_ATOM = 4, XA_CARDINAL = 6, XA_STRING = 31;

    // Event types
    public const int KeyPress = 2, ButtonPress = 4, ButtonRelease = 5, MotionNotify = 6, EnterNotify = 7, LeaveNotify = 8,
                     FocusIn = 9, Expose = 12, ConfigureNotify = 22, SelectionClear = 29, SelectionRequest = 30,
                     SelectionNotify = 31, ClientMessage = 33;

    /// <summary>Size of the XEvent union on 64-bit systems (24 longs).</summary>
    public const int EventSize = 192;
}

static unsafe partial class LibC
{
    [LibraryImport("libc")] public static partial int pipe(int* fds);
    [LibraryImport("libc")] public static partial nint read(int fd, void* buffer, nuint count);
    [LibraryImport("libc")] public static partial nint write(int fd, void* buffer, nuint count);
    [LibraryImport("libc")] public static partial int poll(PollFd* fds, nuint count, int timeout);
    [LibraryImport("libc")] public static partial uint getuid();
    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)] public static partial IntPtr setlocale(int category, string locale);

    public const short POLLIN = 1;
    public const int LC_CTYPE = 0;
}

[StructLayout(LayoutKind.Sequential)]
struct PollFd
{
    public int Fd;
    public short Events, Revents;
}
