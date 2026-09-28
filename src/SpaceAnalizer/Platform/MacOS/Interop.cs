using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SpaceAnalizer.Platform.MacOS;

[StructLayout(LayoutKind.Sequential)]
struct CGPoint
{
    public double X, Y;
    public CGPoint(double x, double y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
struct CGSize
{
    public double W, H;
    public CGSize(double w, double h) { W = w; H = h; }
}

[StructLayout(LayoutKind.Sequential)]
struct CGRect
{
    public double X, Y, W, H;
    public CGRect(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
}

[StructLayout(LayoutKind.Sequential)]
struct CGAffineTransform
{
    public double A, B, C, D, Tx, Ty;
}

/// <summary>Objective-C runtime: classes, selectors and typed objc_msgSend calls.</summary>
static unsafe partial class ObjC
{
    const string Lib = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getClass(string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr sel_registerName(string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);

    [LibraryImport(Lib)]
    public static partial void objc_registerClassPair(IntPtr cls);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

    [LibraryImport(Lib)]
    public static partial IntPtr objc_autoreleasePoolPush();

    [LibraryImport(Lib)]
    public static partial void objc_autoreleasePoolPop(IntPtr pool);

    static readonly IntPtr s_lib = NativeLibrary.Load(Lib);
    public static readonly IntPtr MsgSend = NativeLibrary.GetExport(s_lib, "objc_msgSend");
    /// <summary>On x86-64, methods returning large structs (NSRect) need objc_msgSend_stret.</summary>
    public static readonly IntPtr MsgSendStret = RuntimeInformation.ProcessArchitecture == Architecture.X64
        ? NativeLibrary.GetExport(s_lib, "objc_msgSend_stret")
        : NativeLibrary.GetExport(s_lib, "objc_msgSend");

    static readonly ConcurrentDictionary<string, IntPtr> s_selectors = new();
    static readonly ConcurrentDictionary<string, IntPtr> s_classes = new();

    public static IntPtr Sel(string name) => s_selectors.GetOrAdd(name, static n => sel_registerName(n));
    public static IntPtr Class(string name) => s_classes.GetOrAdd(name, static n => objc_getClass(n));

    public static IntPtr Send(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)MsgSend)(o, Sel(sel));
    public static IntPtr Send(IntPtr o, string sel, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(o, Sel(sel), a);
    public static IntPtr Send(IntPtr o, string sel, IntPtr a, IntPtr b) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(o, Sel(sel), a, b);
    public static IntPtr Send(IntPtr o, string sel, IntPtr a, IntPtr b, IntPtr c) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)MsgSend)(o, Sel(sel), a, b, c);

    public static void SendBool(IntPtr o, string sel, bool v) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte, void>)MsgSend)(o, Sel(sel), v ? (byte)1 : (byte)0);
    public static bool GetBool(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, byte>)MsgSend)(o, Sel(sel)) != 0;
    public static bool GetBool(IntPtr o, string sel, IntPtr a) =>
        ((delegate* unmanaged<IntPtr, IntPtr, IntPtr, byte>)MsgSend)(o, Sel(sel), a) != 0;
    public static long GetLong(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, long>)MsgSend)(o, Sel(sel));
    public static void SendLong(IntPtr o, string sel, long v) =>
        ((delegate* unmanaged<IntPtr, IntPtr, long, void>)MsgSend)(o, Sel(sel), v);
    public static double GetDouble(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, double>)MsgSend)(o, Sel(sel));
    public static CGPoint GetPoint(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGPoint>)MsgSend)(o, Sel(sel));
    public static CGRect GetRect(IntPtr o, string sel) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect>)MsgSendStret)(o, Sel(sel));
    public static void SendRect(IntPtr o, string sel, CGRect r) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, void>)MsgSend)(o, Sel(sel), r);
    public static IntPtr InitWithRect(IntPtr o, string sel, CGRect r) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGRect, IntPtr>)MsgSend)(o, Sel(sel), r);
    public static void SendSize(IntPtr o, string sel, CGSize s) =>
        ((delegate* unmanaged<IntPtr, IntPtr, CGSize, void>)MsgSend)(o, Sel(sel), s);

    public static IntPtr Alloc(string cls) => Send(Class(cls), "alloc");

    /// <summary>An autoreleased NSString.</summary>
    public static IntPtr Str(string s)
    {
        fixed (char* p = s)
        {
            var str = Send(Alloc("NSString"), "initWithCharacters:length:", (IntPtr)p, s.Length);
            return Send(str, "autorelease");
        }
    }

    public static string? ToManaged(IntPtr nsString)
    {
        if (nsString == IntPtr.Zero) return null;
        return Marshal.PtrToStringUTF8(Send(nsString, "UTF8String"));
    }

    public static void AddMethod(IntPtr cls, string sel, IntPtr imp, string types)
    {
        if (!class_addMethod(cls, Sel(sel), imp, types))
            throw new InvalidOperationException("class_addMethod failed for " + sel);
    }
}

static unsafe partial class CG
{
    const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(Lib)] public static partial void CGContextSetRGBFillColor(IntPtr c, double r, double g, double b, double a);
    [LibraryImport(Lib)] public static partial void CGContextSetRGBStrokeColor(IntPtr c, double r, double g, double b, double a);
    [LibraryImport(Lib)] public static partial void CGContextFillRect(IntPtr c, CGRect rect);
    [LibraryImport(Lib)] public static partial void CGContextFillEllipseInRect(IntPtr c, CGRect rect);
    [LibraryImport(Lib)] public static partial void CGContextSetLineWidth(IntPtr c, double width);
    [LibraryImport(Lib)] public static partial void CGContextSetLineCap(IntPtr c, int cap);
    [LibraryImport(Lib)] public static partial void CGContextSetLineJoin(IntPtr c, int join);
    [LibraryImport(Lib)] public static partial void CGContextBeginPath(IntPtr c);
    [LibraryImport(Lib)] public static partial void CGContextMoveToPoint(IntPtr c, double x, double y);
    [LibraryImport(Lib)] public static partial void CGContextAddLineToPoint(IntPtr c, double x, double y);
    [LibraryImport(Lib)] public static partial void CGContextAddCurveToPoint(IntPtr c, double cp1x, double cp1y, double cp2x, double cp2y, double x, double y);
    [LibraryImport(Lib)] public static partial void CGContextClosePath(IntPtr c);
    [LibraryImport(Lib)] public static partial void CGContextAddArc(IntPtr c, double x, double y, double radius, double startAngle, double endAngle, int clockwise);
    [LibraryImport(Lib)] public static partial void CGContextAddPath(IntPtr c, IntPtr path);
    [LibraryImport(Lib)] public static partial void CGContextStrokePath(IntPtr c);
    [LibraryImport(Lib)] public static partial void CGContextFillPath(IntPtr c);
    [LibraryImport(Lib)] public static partial void CGContextClip(IntPtr c);
    [LibraryImport(Lib)] public static partial void CGContextClipToRect(IntPtr c, CGRect rect);
    [LibraryImport(Lib)] public static partial void CGContextSaveGState(IntPtr c);
    [LibraryImport(Lib)] public static partial void CGContextRestoreGState(IntPtr c);
    [LibraryImport(Lib)] public static partial void CGContextTranslateCTM(IntPtr c, double tx, double ty);
    [LibraryImport(Lib)] public static partial void CGContextScaleCTM(IntPtr c, double sx, double sy);
    [LibraryImport(Lib)] public static partial void CGContextSetTextMatrix(IntPtr c, CGAffineTransform t);
    [LibraryImport(Lib)] public static partial void CGContextSetTextPosition(IntPtr c, double x, double y);
    [LibraryImport(Lib)] public static partial void CGContextSetShadowWithColor(IntPtr c, CGSize offset, double blur, IntPtr color);
    [LibraryImport(Lib)] public static partial void CGContextSetInterpolationQuality(IntPtr c, int quality);
    [LibraryImport(Lib)] public static partial void CGContextDrawImage(IntPtr c, CGRect rect, IntPtr image);
    [LibraryImport(Lib)] public static partial void CGContextDrawLinearGradient(IntPtr c, IntPtr gradient, CGPoint start, CGPoint end, uint options);
    [LibraryImport(Lib)] public static partial IntPtr CGPathCreateWithRoundedRect(CGRect rect, double cornerWidth, double cornerHeight, IntPtr transform);
    [LibraryImport(Lib)] public static partial void CGPathRelease(IntPtr path);
    [LibraryImport(Lib)] public static partial IntPtr CGColorSpaceCreateWithName(IntPtr name);
    [LibraryImport(Lib)] public static partial IntPtr CGGradientCreateWithColorComponents(IntPtr space, double* components, double* locations, nint count);
    [LibraryImport(Lib)] public static partial void CGGradientRelease(IntPtr gradient);
    [LibraryImport(Lib)] public static partial IntPtr CGColorCreateSRGB(double r, double g, double b, double a);
    [LibraryImport(Lib)] public static partial void CGColorRelease(IntPtr color);
    [LibraryImport(Lib)] public static partial IntPtr CGBitmapContextCreate(IntPtr data, nint width, nint height, nint bitsPerComponent, nint bytesPerRow, IntPtr space, uint bitmapInfo);
    [LibraryImport(Lib)] public static partial IntPtr CGDataProviderCreateWithData(IntPtr info, void* data, nint size, IntPtr release);
    [LibraryImport(Lib)] public static partial void CGDataProviderRelease(IntPtr provider);
    [LibraryImport(Lib)] public static partial IntPtr CGImageCreate(nint width, nint height, nint bitsPerComponent, nint bitsPerPixel, nint bytesPerRow, IntPtr space, uint bitmapInfo, IntPtr provider, IntPtr decode, byte shouldInterpolate, int intent);
    [LibraryImport(Lib)] public static partial IntPtr CGBitmapContextCreateImage(IntPtr context);
    [LibraryImport(Lib)] public static partial void CGContextRelease(IntPtr context);
    [LibraryImport(Lib)] public static partial void CGImageRelease(IntPtr image);

    public static readonly IntPtr SRGB = CGColorSpaceCreateWithName(Native.Symbol(Lib, "kCGColorSpaceSRGB"));

    // CGImageAlphaInfo.PremultipliedFirst | CGBitmapInfo.ByteOrder32Little (BGRA in memory)
    public const uint BitmapInfo = 2 | (2 << 12);
}

static unsafe partial class CF
{
    const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(Lib)] public static partial IntPtr CFStringCreateWithCharacters(IntPtr alloc, char* chars, nint count);
    [LibraryImport(Lib)] public static partial IntPtr CFAttributedStringCreate(IntPtr alloc, IntPtr str, IntPtr attributes);
    [LibraryImport(Lib)] public static partial IntPtr CFDictionaryCreate(IntPtr alloc, IntPtr* keys, IntPtr* values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);
    [LibraryImport(Lib)] public static partial void CFRelease(IntPtr cf);
    [LibraryImport(Lib)] public static partial IntPtr CFRetain(IntPtr cf);
    [LibraryImport(Lib)] public static partial IntPtr CFLocaleCopyPreferredLanguages();
    [LibraryImport(Lib)] public static partial nint CFArrayGetCount(IntPtr array);
    [LibraryImport(Lib)] public static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [LibraryImport(Lib)] public static partial IntPtr CFURLCreateWithFileSystemPath(IntPtr alloc, IntPtr path, nint style, byte isDirectory);

    public static readonly IntPtr TypeDictionaryKeyCallBacks = Native.Address(Lib, "kCFTypeDictionaryKeyCallBacks");
    public static readonly IntPtr TypeDictionaryValueCallBacks = Native.Address(Lib, "kCFTypeDictionaryValueCallBacks");
    public static readonly IntPtr BooleanTrue = Native.Symbol(Lib, "kCFBooleanTrue");
    public static readonly IntPtr RunLoopCommonModes = Native.Symbol(Lib, "kCFRunLoopCommonModes");

    public static IntPtr String(string s)
    {
        fixed (char* p = s) return CFStringCreateWithCharacters(IntPtr.Zero, p, s.Length);
    }
}

static unsafe partial class Dispatch
{
    const string Lib = "/usr/lib/libSystem.B.dylib";

    [LibraryImport(Lib)]
    public static partial void dispatch_async_f(IntPtr queue, IntPtr context, delegate* unmanaged<IntPtr, void> work);

    /// <summary>dispatch_get_main_queue() is a macro for &amp;_dispatch_main_q.</summary>
    public static readonly IntPtr MainQueue = NativeLibrary.GetExport(NativeLibrary.Load(Lib), "_dispatch_main_q");
}

static unsafe class Native
{
    public const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    /// <summary>Address of an exported variable.</summary>
    public static IntPtr Address(string lib, string name) => NativeLibrary.GetExport(NativeLibrary.Load(lib), name);

    /// <summary>Value of an exported pointer variable (e.g. a CFStringRef constant).</summary>
    public static IntPtr Symbol(string lib, string name) => *(IntPtr*)Address(lib, name);

    public static double DoubleSymbol(string lib, string name) => *(double*)Address(lib, name);
}
