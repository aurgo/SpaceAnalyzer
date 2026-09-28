using System.Globalization;
using System.Text;

namespace SpaceAnalyzer.Core;

/// <summary>Culture-independent formatting (the app runs with invariant globalization to stay small).</summary>
public static class Fmt
{
    /// <summary>Use 1000-based units like Finder (macOS) instead of 1024-based ones like Explorer.</summary>
    public static bool DecimalUnits = OperatingSystem.IsMacOS();

    static bool Es => Strings.Spanish;
    static char DecimalSep => Es ? ',' : '.';
    static char GroupSep => Es ? '.' : ',';

    static readonly string[] s_units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];

    public static string Size(long bytes)
    {
        if (bytes < 0) bytes = 0;
        double k = DecimalUnits ? 1000 : 1024;
        if (bytes < k) return Count(bytes) + " B";
        double v = bytes;
        int u = 0;
        while (v >= k && u < s_units.Length - 1)
        {
            v /= k;
            u++;
        }
        // Three significant digits: 1,23 GB · 12,3 GB · 123 GB
        int decimals = v < 10 ? 2 : v < 100 ? 1 : 0;
        string s = Number(v, decimals);
        if (s.Length >= 4 && decimals == 0 && v >= 999.5 && u < s_units.Length - 1)
            return Number(v / k, 2) + " " + s_units[u + 1];
        return s + " " + s_units[u];
    }

    public static string Count(long n) => Number(n, 0);

    public static string Percent(double fraction)
    {
        double p = fraction * 100;
        string s = p >= 10 || p == 0 ? Number(p, 0) : p >= 0.1 ? Number(p, 1) : "<" + Number(0.1, 1);
        return Es ? s + " %" : s + "%";
    }

    public static string Number(double value, int decimals)
    {
        string raw = value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        int dot = raw.IndexOf('.');
        string intPart = dot >= 0 ? raw[..dot] : raw;
        string frac = dot >= 0 ? raw[(dot + 1)..] : "";
        bool neg = intPart.StartsWith('-');
        if (neg) intPart = intPart[1..];

        var sb = new StringBuilder(raw.Length + 6);
        if (neg) sb.Append('-');
        int first = intPart.Length % 3;
        if (first == 0) first = 3;
        sb.Append(intPart, 0, first);
        for (int i = first; i < intPart.Length; i += 3)
        {
            // Spanish style keeps 4-digit numbers without separator ("1234"), as the RAE recommends.
            if (!(Es && intPart.Length == 4)) sb.Append(GroupSep);
            sb.Append(intPart, i, 3);
        }
        if (frac.Length > 0) sb.Append(DecimalSep).Append(frac);
        return sb.ToString();
    }

    static readonly string[] s_monthsEs = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sept", "oct", "nov", "dic"];
    static readonly string[] s_monthsEn = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string Date(long utcTicks)
    {
        if (utcTicks <= 0) return "—";
        DateTime local;
        try { local = new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime(); }
        catch { return "—"; }
        return Es
            ? $"{local.Day} {s_monthsEs[local.Month - 1]} {local.Year}"
            : $"{s_monthsEn[local.Month - 1]} {local.Day}, {local.Year}";
    }

    /// <summary>"hace 3 meses" / "3 months ago".</summary>
    public static string Age(long utcTicks)
    {
        if (utcTicks <= 0) return "—";
        var age = DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc);
        double days = age.TotalDays;
        if (days < 1) return Es ? "hoy" : "today";
        if (days < 2) return Es ? "ayer" : "yesterday";
        if (days < 14) return Ago((int)days, "día", "días", "day", "days");
        if (days < 60) return Ago((int)(days / 7), "semana", "semanas", "week", "weeks");
        if (days < 365) return Ago((int)(days / 30.4), "mes", "meses", "month", "months");
        return Ago((int)(days / 365.25), "año", "años", "year", "years");
    }

    static string Ago(int n, string es1, string esN, string en1, string enN)
        => Es ? $"hace {n} {(n == 1 ? es1 : esN)}" : $"{n} {(n == 1 ? en1 : enN)} ago";

    public static string Duration(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return Number(t.TotalSeconds, t.TotalSeconds < 10 ? 1 : 0) + " s";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes} min {t.Seconds} s";
        return $"{(int)t.TotalHours} h {t.Minutes} min";
    }
}
