using System.Runtime.InteropServices;

namespace SpaceAnalyzer.Platform.Windows;

[StructLayout(LayoutKind.Sequential)]
struct POINT
{
    public int X, Y;
    public POINT(int x, int y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
struct RECT
{
    public int Left, Top, Right, Bottom;
}

[StructLayout(LayoutKind.Sequential)]
struct WNDCLASSEXW
{
    public uint cbSize, style;
    public IntPtr lpfnWndProc;
    public int cbClsExtra, cbWndExtra;
    public IntPtr hInstance, hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam, lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct PAINTSTRUCT
{
    public IntPtr hdc;
    public int fErase;
    public RECT rcPaint;
    public int fRestore, fIncUpdate;
    public fixed byte rgbReserved[32];
}

[StructLayout(LayoutKind.Sequential)]
struct TRACKMOUSEEVENT
{
    public uint cbSize, dwFlags;
    public IntPtr hwndTrack;
    public uint dwHoverTime;
}

[StructLayout(LayoutKind.Sequential)]
struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth, biHeight;
    public ushort biPlanes, biBitCount;
    public uint biCompression, biSizeImage;
    public int biXPelsPerMeter, biYPelsPerMeter;
    public uint biClrUsed, biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
struct MINMAXINFO
{
    public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
}

[StructLayout(LayoutKind.Sequential)]
struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor, rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
struct ICONINFO
{
    public int fIcon;
    public uint xHotspot, yHotspot;
    public IntPtr hbmMask, hbmColor;
}

/// <summary>SHFILEOPSTRUCTW with natural (64-bit) packing.</summary>
[StructLayout(LayoutKind.Sequential)]
struct SHFILEOPSTRUCTW
{
    public IntPtr hwnd;
    public uint wFunc;
    public IntPtr pFrom, pTo;
    public ushort fFlags;
    public int fAnyOperationsAborted;
    public IntPtr hNameMappings, lpszProgressTitle;
}

static unsafe partial class User32
{
    const string Lib = "user32.dll";

    [LibraryImport(Lib)] public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);
    [LibraryImport(Lib)] public static partial IntPtr CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [LibraryImport(Lib)] public static partial IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport(Lib)] public static partial int GetMessageW(MSG* msg, IntPtr hwnd, uint min, uint max);
    [LibraryImport(Lib)] public static partial int TranslateMessage(MSG* msg);
    [LibraryImport(Lib)] public static partial IntPtr DispatchMessageW(MSG* msg);
    [LibraryImport(Lib)] public static partial void PostQuitMessage(int code);
    [LibraryImport(Lib)] public static partial int PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport(Lib)] public static partial IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [LibraryImport(Lib)] public static partial int ShowWindow(IntPtr hwnd, int cmd);
    [LibraryImport(Lib)] public static partial int UpdateWindow(IntPtr hwnd);
    [LibraryImport(Lib)] public static partial int DestroyWindow(IntPtr hwnd);
    [LibraryImport(Lib)] public static partial int InvalidateRect(IntPtr hwnd, RECT* rect, int erase);
    [LibraryImport(Lib)] public static partial IntPtr BeginPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [LibraryImport(Lib)] public static partial int EndPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [LibraryImport(Lib)] public static partial int GetClientRect(IntPtr hwnd, RECT* rect);
    [LibraryImport(Lib)] public static partial IntPtr SetCursor(IntPtr cursor);
    [LibraryImport(Lib)] public static partial IntPtr LoadCursorW(IntPtr instance, IntPtr name);
    [LibraryImport(Lib)] public static partial IntPtr SetCapture(IntPtr hwnd);
    [LibraryImport(Lib)] public static partial int ReleaseCapture();
    [LibraryImport(Lib)] public static partial int TrackMouseEvent(TRACKMOUSEEVENT* tme);
    [LibraryImport(Lib)] public static partial nuint SetTimer(IntPtr hwnd, nuint id, uint elapse, IntPtr proc);
    [LibraryImport(Lib)] public static partial int KillTimer(IntPtr hwnd, nuint id);
    [LibraryImport(Lib)] public static partial int ScreenToClient(IntPtr hwnd, POINT* pt);
    [LibraryImport(Lib)] public static partial uint GetDpiForWindow(IntPtr hwnd);
    [LibraryImport(Lib)] public static partial int SetProcessDpiAwarenessContext(IntPtr context);
    [LibraryImport(Lib)] public static partial int SetProcessDPIAware();
    [LibraryImport(Lib)] public static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport(Lib)] public static partial int SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport(Lib)] public static partial int SetWindowTextW(IntPtr hwnd, char* text);
    [LibraryImport(Lib)] public static partial IntPtr SetFocus(IntPtr hwnd);
    [LibraryImport(Lib)] public static partial int OpenClipboard(IntPtr hwnd);
    [LibraryImport(Lib)] public static partial int EmptyClipboard();
    [LibraryImport(Lib)] public static partial IntPtr SetClipboardData(uint format, IntPtr mem);
    [LibraryImport(Lib)] public static partial IntPtr GetClipboardData(uint format);
    [LibraryImport(Lib)] public static partial int CloseClipboard();
    [LibraryImport(Lib)] public static partial short GetKeyState(int key);
    [LibraryImport(Lib)] public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [LibraryImport(Lib)] public static partial int GetMonitorInfoW(IntPtr monitor, MONITORINFO* info);
    [LibraryImport(Lib)] public static partial int MessageBoxW(IntPtr hwnd, char* text, char* caption, uint type);
    [LibraryImport(Lib)] public static partial IntPtr CreateIconIndirect(ICONINFO* info);
    [LibraryImport(Lib)] public static partial int DestroyIcon(IntPtr icon);
    [LibraryImport(Lib)] public static partial int SetForegroundWindow(IntPtr hwnd);
}

static unsafe partial class Gdi32
{
    const string Lib = "gdi32.dll";

    [LibraryImport(Lib)] public static partial int StretchDIBits(IntPtr hdc, int xDest, int yDest, int destWidth, int destHeight, int xSrc, int ySrc, int srcWidth, int srcHeight, void* bits, BITMAPINFOHEADER* info, uint usage, uint rop);
    [LibraryImport(Lib)] public static partial IntPtr CreateDIBSection(IntPtr hdc, BITMAPINFOHEADER* info, uint usage, void** bits, IntPtr section, uint offset);
    [LibraryImport(Lib)] public static partial IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);
    [LibraryImport(Lib)] public static partial int DeleteObject(IntPtr obj);
}

static unsafe partial class Shell32
{
    const string Lib = "shell32.dll";

    [LibraryImport(Lib)] public static partial IntPtr ShellExecuteW(IntPtr hwnd, char* operation, char* file, char* parameters, char* directory, int show);
    [LibraryImport(Lib)] public static partial int SHFileOperationW(SHFILEOPSTRUCTW* op);
    [LibraryImport(Lib)] public static partial IntPtr ILCreateFromPathW(char* path);
    [LibraryImport(Lib)] public static partial void ILFree(IntPtr pidl);
    [LibraryImport(Lib)] public static partial int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint count, IntPtr* items, uint flags);
    [LibraryImport(Lib)] public static partial int SHObjectProperties(IntPtr hwnd, uint type, char* name, char* page);
    [LibraryImport(Lib)] public static partial void DragAcceptFiles(IntPtr hwnd, int accept);
    [LibraryImport(Lib)] public static partial uint DragQueryFileW(IntPtr drop, uint index, char* file, uint count);
    [LibraryImport(Lib)] public static partial void DragFinish(IntPtr drop);
}

static unsafe partial class Kernel32
{
    const string Lib = "kernel32.dll";

    [LibraryImport(Lib)] public static partial IntPtr GetModuleHandleW(char* name);
    [LibraryImport(Lib)] public static partial IntPtr GlobalAlloc(uint flags, nuint bytes);
    [LibraryImport(Lib)] public static partial IntPtr GlobalLock(IntPtr mem);
    [LibraryImport(Lib)] public static partial int GlobalUnlock(IntPtr mem);
    [LibraryImport(Lib)] public static partial IntPtr GlobalFree(IntPtr mem);
    [LibraryImport(Lib)] public static partial ushort GetUserDefaultUILanguage();
}

static unsafe partial class WinHttp
{
    const string Lib = "winhttp.dll";

    [LibraryImport(Lib)] public static partial IntPtr WinHttpOpen(char* agent, uint accessType, char* proxy, char* proxyBypass, uint flags);
    [LibraryImport(Lib)] public static partial int WinHttpSetTimeouts(IntPtr handle, int resolve, int connect, int send, int receive);
    [LibraryImport(Lib)] public static partial IntPtr WinHttpConnect(IntPtr session, char* server, ushort port, uint reserved);
    [LibraryImport(Lib)] public static partial IntPtr WinHttpOpenRequest(IntPtr connect, char* verb, char* path, char* version, char* referrer, char** acceptTypes, uint flags);
    [LibraryImport(Lib)] public static partial int WinHttpSendRequest(IntPtr request, char* headers, uint headersLength, void* optional, uint optionalLength, uint totalLength, nuint context);
    [LibraryImport(Lib)] public static partial int WinHttpReceiveResponse(IntPtr request, IntPtr reserved);
    [LibraryImport(Lib)] public static partial int WinHttpQueryHeaders(IntPtr request, uint infoLevel, char* name, void* buffer, uint* length, uint* index);
    [LibraryImport(Lib)] public static partial int WinHttpReadData(IntPtr request, void* buffer, uint toRead, uint* read);
    [LibraryImport(Lib)] public static partial int WinHttpCloseHandle(IntPtr handle);
}

static unsafe partial class Dwm
{
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, void* value, uint size);
}

static unsafe partial class Ole32
{
    [LibraryImport("ole32.dll")] public static partial int CoCreateInstance(Guid* clsid, IntPtr outer, uint context, Guid* iid, IntPtr* instance);
    [LibraryImport("ole32.dll")] public static partial void CoTaskMemFree(IntPtr memory);
}
