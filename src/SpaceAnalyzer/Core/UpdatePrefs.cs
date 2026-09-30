using System.Globalization;

namespace SpaceAnalyzer.Core;

/// <summary>
/// Whether the app looks for a new version by itself when it starts, and when it last did. Kept in a small text
/// file in the user's settings folder (<c>%APPDATA%\SpaceAnalyzer</c>, <c>~/Library/Application Support/SpaceAnalyzer</c>
/// or <c>~/.config/SpaceAnalyzer</c>), one <c>key=value</c> per line. A missing or unreadable file means the defaults:
/// the check is on, and it has never run.
/// </summary>
public sealed class UpdatePrefs
{
    /// <summary>How often the automatic check asks GitHub, at most.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>Where the preferences are read from and saved to; null keeps them in memory only.</summary>
    public string? FilePath { get; }

    public bool AutoCheck { get; set; } = true;

    public DateTime? LastCheckUtc { get; set; }

    public UpdatePrefs(string? filePath = null) => FilePath = filePath;

    /// <summary>
    /// settings.ini in the user's settings folder, or null when the system doesn't name one. DoNotVerify: by default
    /// a folder that doesn't exist yet (a fresh ~/.config) comes back empty, which would put the file in the current
    /// directory.
    /// </summary>
    public static string? DefaultPath =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify) is { Length: > 0 } folder
            && Path.IsPathFullyQualified(folder)
            ? Path.Combine(folder, "SpaceAnalyzer", "settings.ini")
            : null;

    /// <summary>True when the automatic check is on and has not run for a day (or the clock went back since).</summary>
    public bool IsDue(DateTime nowUtc) =>
        AutoCheck && (LastCheckUtc is not { } last || nowUtc - last >= Interval || last > nowUtc);

    /// <summary>
    /// The preferences of whoever runs the app, or null when SPACEANALYZER_NO_UPDATE_CHECK is set (the automated
    /// UI tests set it, so they never go online).
    /// </summary>
    public static UpdatePrefs? ForThisUser() =>
        Environment.GetEnvironmentVariable("SPACEANALYZER_NO_UPDATE_CHECK") is { Length: > 0 } ? null : Load();

    public static UpdatePrefs Load(string? path = null)
    {
        var prefs = new UpdatePrefs(path ?? DefaultPath);
        try
        {
            if (prefs.FilePath is null || !File.Exists(prefs.FilePath)) return prefs;
            foreach (var line in File.ReadAllLines(prefs.FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "auto-check":
                        if (bool.TryParse(value, out bool on)) prefs.AutoCheck = on;
                        break;
                    case "last-check":
                        if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when))
                            prefs.LastCheckUtc = when;
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
        return prefs;
    }

    /// <summary>Writes the preferences; a folder that cannot be written only costs a check on the next start.</summary>
    public void Save()
    {
        if (FilePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var lines = new List<string> { "auto-check=" + (AutoCheck ? "true" : "false") };
            if (LastCheckUtc is { } last) lines.Add("last-check=" + last.ToString("o", CultureInfo.InvariantCulture));
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write(ex);
        }
    }
}
