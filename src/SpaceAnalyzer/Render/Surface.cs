using System.Runtime.CompilerServices;
using SpaceAnalyzer.UI;

namespace SpaceAnalyzer.Render;

/// <summary>
/// A 32-bit image in memory. Pixels are premultiplied BGRA (0xAARRGGBB as a little-endian uint), which is
/// exactly what Windows DIBs, CoreGraphics and X11 expect, so the platform layers can show it without converting.
/// The buffer lives on the pinned heap so native code can read it directly.
/// </summary>
public sealed class Surface
{
    public readonly int Width, Height;
    public readonly uint[] Pixels;

    public Surface(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = GC.AllocateUninitializedArray<uint>(Width * Height, pinned: true);
    }

    public unsafe void* Pointer => Unsafe.AsPointer(ref Pixels[0]);

    public void Clear(uint color) => Pixels.AsSpan().Fill(color);
}

/// <summary>Premultiplied-color helpers used by every drawing routine.</summary>
public static class Px
{
    /// <summary>Converts a straight-alpha color into a premultiplied pixel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Premul(Color c)
    {
        uint a = c.A;
        if (a == 255) return 0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        return (a << 24) | (Mul(c.R, a) << 16) | (Mul(c.G, a) << 8) | Mul(c.B, a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Mul(uint x, uint a)
    {
        uint t = x * a + 128;
        return (t + (t >> 8)) >> 8;
    }

    /// <summary>Scales all four channels of a premultiplied pixel by <paramref name="k"/>/255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Scale(uint c, uint k)
    {
        if (k >= 255) return c;
        if (k == 0) return 0;
        uint rb = (c & 0x00FF00FFu) * k + 0x00800080u;
        rb = ((rb + ((rb >> 8) & 0x00FF00FFu)) >> 8) & 0x00FF00FFu;
        uint ag = ((c >> 8) & 0x00FF00FFu) * k + 0x00800080u;
        ag = (ag + ((ag >> 8) & 0x00FF00FFu)) & 0xFF00FF00u;
        return rb | ag;
    }

    /// <summary>Porter-Duff "source over" for premultiplied pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Over(uint dst, uint src)
    {
        uint sa = src >> 24;
        if (sa == 255) return src;
        if (sa == 0) return dst;
        return src + Scale(dst, 255 - sa);
    }

    /// <summary>Linear interpolation between two premultiplied pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Lerp(uint a, uint b, float t)
    {
        uint k = (uint)Math.Clamp(t * 255f + 0.5f, 0f, 255f);
        return Scale(a, 255 - k) + Scale(b, k);
    }

    /// <summary>Straight-alpha RGBA bytes of a premultiplied pixel (for PNG export).</summary>
    public static (byte R, byte G, byte B, byte A) Unpremul(uint p)
    {
        uint a = p >> 24;
        if (a == 0) return (0, 0, 0, 0);
        uint r = (p >> 16) & 255, g = (p >> 8) & 255, b = p & 255;
        if (a == 255) return ((byte)r, (byte)g, (byte)b, 255);
        return ((byte)Math.Min(255, r * 255 / a), (byte)Math.Min(255, g * 255 / a), (byte)Math.Min(255, b * 255 / a), (byte)a);
    }
}
