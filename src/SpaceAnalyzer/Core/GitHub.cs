using System.Globalization;
using System.Text;

namespace SpaceAnalyzer.Core;

/// <summary>
/// The project on GitHub, and "check for updates": one request for the latest release, made only when the
/// user presses the button. The app never goes online otherwise.
/// </summary>
public static class GitHub
{
    public const string Repo = "https://github.com/aurgo/SpaceAnalyzer";
    public const string LatestReleaseApi = "https://api.github.com/repos/aurgo/SpaceAnalyzer/releases/latest";

    /// <summary>A published release: its version ("1.2.0"), its page, which has the downloads, and its tag ("v1.2.0").</summary>
    public readonly record struct Release(string Version, string Url, string Tag = "");

    /// <summary>The release in the API's answer, or null if the answer isn't one (offline, rate limited, no releases...).</summary>
    public static Release? ParseLatestRelease(string? json)
    {
        if (json is null || TopLevelString(json, "tag_name") is not { } tag || Parse(tag) is not { } v) return null;
        return new Release($"{v.Major}.{v.Minor}.{v.Build}", $"{Repo}/releases/tag/{Uri.EscapeDataString(tag)}", tag);
    }

    /// <summary>True if <paramref name="version"/> is newer than <paramref name="current"/>.</summary>
    public static bool IsNewer(string version, string current) =>
        Parse(version) is { } a && Parse(current) is { } b && a > b;

    /// <summary>"v1.2" → 1.2.0, "1.2.3-beta" → 1.2.3: a leading v, missing parts and suffixes don't matter.</summary>
    static Version? Parse(string text)
    {
        var s = text.AsSpan().Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];
        int suffix = s.IndexOfAny('-', '+');
        if (suffix >= 0) s = s[..suffix];
        Span<int> parts = stackalloc int[3];
        int count = 0;
        foreach (var range in s.Split('.'))
        {
            if (!int.TryParse(s[range], NumberStyles.None, CultureInfo.InvariantCulture, out int n)) return null;
            if (count < 3) parts[count] = n;
            count++;
        }
        return new Version(parts[0], parts[1], parts[2]);
    }

    /// <summary>The string value of a property of the outermost JSON object (strings inside nested ones are skipped).</summary>
    internal static string? TopLevelString(string json, string name)
    {
        int depth = 0;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (c is '{' or '[') depth++;
            else if (c is '}' or ']') depth--;
            else if (c == '"' && ReadString(json, ref i) == name && depth == 1)
            {
                int j = SkipSpaces(json, i + 1);
                if (j >= json.Length || json[j] != ':') continue; // a value that happens to equal the name
                j = SkipSpaces(json, j + 1);
                return j < json.Length && json[j] == '"' ? ReadString(json, ref j) : null;
            }
        }
        return null;
    }

    static int SkipSpaces(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        return i;
    }

    /// <summary>Reads the string whose opening quote is at <paramref name="i"/> and leaves <paramref name="i"/> on the closing one.</summary>
    static string ReadString(string json, ref int i)
    {
        var sb = new StringBuilder();
        for (i++; i < json.Length && json[i] != '"'; i++)
        {
            char c = json[i];
            if (c != '\\' || i + 1 >= json.Length) { sb.Append(c); continue; }
            c = json[++i];
            if (c == 'u' && i + 4 < json.Length && ushort.TryParse(json.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort u))
            {
                sb.Append((char)u);
                i += 4;
            }
            else
            {
                sb.Append(c switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', _ => c }); // also \" \\ \/
            }
        }
        return sb.ToString();
    }
}
