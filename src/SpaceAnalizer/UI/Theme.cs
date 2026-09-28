using SpaceAnalizer.Core;

namespace SpaceAnalizer.UI;

/// <summary>All colors of the app. Two variants: dark (default) and light.</summary>
public sealed class Theme
{
    public bool Dark { get; private init; }

    // Chrome
    public Color WindowBg, ToolbarBg, SidebarBg, StatusBg, Divider;
    public Color Surface, SurfaceHover, SurfacePressed, SurfaceBorder, Raised;
    public Color TextPrimary, TextSecondary, TextMuted, TextDisabled;
    public Color Accent, AccentHover, AccentPressed, OnAccent, AccentSoft;
    public Color Danger, DangerHover, DangerSoft;
    public Color InputBg, InputBorder;
    public Color TooltipBg, TooltipBorder, Overlay, Shadow;
    public Color BarTrack;

    // Treemap
    public Color TreemapBg, FolderBase, FolderBorder, HeaderText, HeaderSubText;
    public Color OthersFill, FreeFill, FreeHatch, FreeText, LabelLight, LabelDark;
    public float FolderStep;

    public Color[] Categories = new Color[FileCategories.Count];
    public Color[] Levels = new Color[8];
    public Color[] AgeStops = new Color[7];

    /// <summary>Age (in days) of each color stop of the age scale.</summary>
    public static readonly double[] AgeStopDays = [0, 7, 30, 180, 365, 730, 1825];

    public static readonly Theme DarkTheme = CreateDark();
    public static readonly Theme LightTheme = CreateLight();

    static Theme CreateDark()
    {
        var t = new Theme
        {
            Dark = true,
            WindowBg = Color.Hex(0x0F1115),
            ToolbarBg = Color.Hex(0x15171C),
            SidebarBg = Color.Hex(0x15171C),
            StatusBg = Color.Hex(0x15171C),
            Divider = Color.Hex(0x24272F),
            Surface = Color.Hex(0x1C1F26),
            SurfaceHover = Color.Hex(0x252932),
            SurfacePressed = Color.Hex(0x2D323D),
            SurfaceBorder = Color.Hex(0x2B2F39),
            Raised = Color.Hex(0x2F3440),
            TextPrimary = Color.Hex(0xECEEF3),
            TextSecondary = Color.Hex(0xA4AAB8),
            TextMuted = Color.Hex(0x6F7687),
            TextDisabled = Color.Hex(0x474C59),
            Accent = Color.Hex(0x7B6EF6),
            AccentHover = Color.Hex(0x8D82F8),
            AccentPressed = Color.Hex(0x6A5CE8),
            OnAccent = Color.Hex(0xFFFFFF),
            AccentSoft = Color.Hex(0x7B6EF6, 40),
            Danger = Color.Hex(0xEF5A6F),
            DangerHover = Color.Hex(0xF3748A),
            DangerSoft = Color.Hex(0xEF5A6F, 38),
            InputBg = Color.Hex(0x1C1F26),
            InputBorder = Color.Hex(0x2E323D),
            TooltipBg = Color.Hex(0x1E2129),
            TooltipBorder = Color.Hex(0x363B48),
            Overlay = Color.Hex(0x000000, 140),
            Shadow = Color.Hex(0x000000, 150),
            BarTrack = Color.Hex(0x2A2E38),

            TreemapBg = Color.Hex(0x0B0C10),
            FolderBase = Color.Hex(0x181B22),
            FolderStep = 0.035f,
            FolderBorder = Color.Hex(0xFFFFFF, 16),
            HeaderText = Color.Hex(0xDADEE7),
            HeaderSubText = Color.Hex(0x868D9D),
            OthersFill = Color.Hex(0x2A2E38),
            FreeFill = Color.Hex(0x121419),
            FreeHatch = Color.Hex(0xFFFFFF, 14),
            FreeText = Color.Hex(0x8C93A3),
            LabelLight = Color.Hex(0xFFFFFF),
            LabelDark = Color.Hex(0x121418),
        };
        SetCommonPalettes(t);
        return t;
    }

    static Theme CreateLight()
    {
        var t = new Theme
        {
            Dark = false,
            WindowBg = Color.Hex(0xF3F4F7),
            ToolbarBg = Color.Hex(0xFBFBFC),
            SidebarBg = Color.Hex(0xFBFBFC),
            StatusBg = Color.Hex(0xFBFBFC),
            Divider = Color.Hex(0xE3E5EA),
            Surface = Color.Hex(0xF1F2F5),
            SurfaceHover = Color.Hex(0xE8EAEF),
            SurfacePressed = Color.Hex(0xDEE1E7),
            SurfaceBorder = Color.Hex(0xDADDE4),
            Raised = Color.Hex(0xFFFFFF),
            TextPrimary = Color.Hex(0x1A1C21),
            TextSecondary = Color.Hex(0x565C6B),
            TextMuted = Color.Hex(0x8A909E),
            TextDisabled = Color.Hex(0xB9BDC7),
            Accent = Color.Hex(0x6556E8),
            AccentHover = Color.Hex(0x5747DE),
            AccentPressed = Color.Hex(0x4A3BCF),
            OnAccent = Color.Hex(0xFFFFFF),
            AccentSoft = Color.Hex(0x6556E8, 34),
            Danger = Color.Hex(0xDC3A52),
            DangerHover = Color.Hex(0xC92E45),
            DangerSoft = Color.Hex(0xDC3A52, 30),
            InputBg = Color.Hex(0xFFFFFF),
            InputBorder = Color.Hex(0xD5D8DF),
            TooltipBg = Color.Hex(0xFFFFFF),
            TooltipBorder = Color.Hex(0xD9DCE3),
            Overlay = Color.Hex(0x0B0D12, 90),
            Shadow = Color.Hex(0x1A1F2E, 70),
            BarTrack = Color.Hex(0xE3E5EA),

            TreemapBg = Color.Hex(0xE4E7EC),
            FolderBase = Color.Hex(0xFFFFFF),
            FolderStep = -0.028f,
            FolderBorder = Color.Hex(0x000000, 22),
            HeaderText = Color.Hex(0x272A31),
            HeaderSubText = Color.Hex(0x6E7483),
            OthersFill = Color.Hex(0xD3D7DE),
            FreeFill = Color.Hex(0xF4F5F7),
            FreeHatch = Color.Hex(0x000000, 18),
            FreeText = Color.Hex(0x6E7483),
            LabelLight = Color.Hex(0xFFFFFF),
            LabelDark = Color.Hex(0x15171B),
        };
        SetCommonPalettes(t);
        return t;
    }

    static void SetCommonPalettes(Theme t)
    {
        t.Categories[(int)FileCategory.Other] = Color.Hex(0x7D8595);
        t.Categories[(int)FileCategory.Video] = Color.Hex(0xEF5350);
        t.Categories[(int)FileCategory.Audio] = Color.Hex(0xF2B233);
        t.Categories[(int)FileCategory.Image] = Color.Hex(0x3CC47C);
        t.Categories[(int)FileCategory.Document] = Color.Hex(0x4C8DF6);
        t.Categories[(int)FileCategory.Archive] = Color.Hex(0x9D6CF2);
        t.Categories[(int)FileCategory.Code] = Color.Hex(0x22B8CF);
        t.Categories[(int)FileCategory.Program] = Color.Hex(0xF28C38);
        t.Categories[(int)FileCategory.Data] = Color.Hex(0xE0609E);

        // Classic SpaceAnalizer look: one hue per folder level.
        uint[] levels = [0x4C8DF6, 0x22B8CF, 0x3CC47C, 0xA3C940, 0xF2B233, 0xF28C38, 0xEF5350, 0x9D6CF2];
        for (int i = 0; i < levels.Length; i++) t.Levels[i] = Color.Hex(levels[i]);

        // Hot = recently modified, cold = untouched for years.
        uint[] ages = [0xFF5C5C, 0xFF8A3D, 0xF7C744, 0x9BD65A, 0x3CC4A0, 0x3FA2F6, 0x7B6EF6];
        for (int i = 0; i < ages.Length; i++) t.AgeStops[i] = Color.Hex(ages[i]);
    }

    public Color Category(FileCategory c) => Categories[(int)c];
    public Color Level(int level) => Levels[((level % Levels.Length) + Levels.Length) % Levels.Length];

    public Color AgeColor(long utcTicks)
    {
        if (utcTicks <= 0) return AgeStops[^1];
        double days = Math.Max(0, (DateTime.UtcNow.Ticks - utcTicks) / (double)TimeSpan.TicksPerDay);
        var stops = AgeStopDays;
        if (days >= stops[^1]) return AgeStops[^1];
        for (int i = 1; i < stops.Length; i++)
        {
            if (days <= stops[i])
            {
                // Interpolate on a log scale so recent changes get more color resolution.
                double a = Math.Log(1 + stops[i - 1]), b = Math.Log(1 + stops[i]), v = Math.Log(1 + days);
                return Color.Lerp(AgeStops[i - 1], AgeStops[i], (float)((v - a) / (b - a)));
            }
        }
        return AgeStops[^1];
    }

    /// <summary>Background of a folder frame at a given nesting level.</summary>
    public Color FolderFill(int level)
    {
        float t = Math.Min(level, 7) * Math.Abs(FolderStep);
        return FolderStep >= 0 ? FolderBase.Lighten(t) : FolderBase.Darken(t);
    }

    /// <summary>Readable label color on top of <paramref name="background"/>.</summary>
    public Color LabelOn(Color background) => background.Luma > 0.62f ? LabelDark : LabelLight;
}
