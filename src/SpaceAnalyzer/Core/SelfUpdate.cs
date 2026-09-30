using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SpaceAnalyzer.Core;

/// <summary>How a release packs the program for one system.</summary>
public enum UpdateKind
{
    /// <summary>The download is the program itself (Windows .exe).</summary>
    Executable,
    /// <summary>A .tar.gz that holds the program (Linux).</summary>
    TarGz,
    /// <summary>A .zip that holds SpaceAnalyzer.app (macOS).</summary>
    AppBundleZip,
}

/// <summary>
/// What an update replaces: the running executable, or on macOS the application bundle it lives in, and the
/// release file that replaces it.
/// </summary>
public sealed record UpdateTarget(string Path, string AssetName, UpdateKind Kind)
{
    /// <summary>
    /// The target for this process, or null when it can't update itself and the user is sent to the download page
    /// instead: a development build, a <i>mini</i> build other than the Windows one (those run through the
    /// <c>dotnet</c> host), a macOS executable outside its bundle, or a copy kept by SharpCommander, which updates
    /// its own copies.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "The empty location of a single-file app is exactly what is being tested for.")]
    public static UpdateTarget? ForThisProcess()
    {
        OSPlatform? os = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsLinux() ? OSPlatform.Linux
            : null;
        // A published single file has no assembly location; a development build has one, and is left alone.
        bool published = string.IsNullOrEmpty(typeof(UpdateTarget).Assembly.Location);
        return os is { } known && published
            ? For(Environment.ProcessPath, known, RuntimeInformation.ProcessArchitecture, !RuntimeFeature.IsDynamicCodeSupported)
            : null;
    }

    /// <summary>The target of an executable at <paramref name="processPath"/>; <paramref name="nativeAot"/> tells the full builds from the mini ones.</summary>
    public static UpdateTarget? For(string? processPath, OSPlatform os, Architecture architecture, bool nativeAot)
    {
        if (string.IsNullOrEmpty(processPath)) return null;
        if (processPath.Replace('\\', '/').Contains("/SharpCommander/tools/", StringComparison.OrdinalIgnoreCase)) return null;
        string? cpu = architecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => null };
        if (cpu is null) return null;

        if (os == OSPlatform.Windows)
        {
            if (nativeAot) return new(processPath, $"SpaceAnalyzer-windows-{cpu}.exe", UpdateKind.Executable);
            return cpu == "x64" ? new(processPath, "SpaceAnalyzer-windows-x64-mini.exe", UpdateKind.Executable) : null;
        }
        if (!nativeAot) return null;
        if (os == OSPlatform.Linux) return new(processPath, $"SpaceAnalyzer-linux-{cpu}.tar.gz", UpdateKind.TarGz);
        if (os == OSPlatform.OSX)
        {
            // .../SpaceAnalyzer.app/Contents/MacOS/SpaceAnalyzer: the whole bundle is replaced.
            var macOS = Directory.GetParent(processPath);
            var bundle = macOS?.Parent?.Parent;
            if (macOS?.Name != "MacOS" || macOS.Parent?.Name != "Contents" || bundle is null
                || !bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return null;
            return new(bundle.FullName, "SpaceAnalyzer-macos.zip", UpdateKind.AppBundleZip);
        }
        return null;
    }
}

/// <summary>
/// Replaces the running copy with a newer release. The download goes to a staging folder next to the copy (so the
/// final step is a rename on the same volume), is checked against the release's SHA256SUMS.txt, unpacked, and
/// only then swapped in:
///
/// - Windows can't overwrite a running .exe but can rename it: the old one is renamed aside and removed on a later start.
/// - Linux replaces the file under the running process, which keeps its own copy until it quits.
/// - macOS moves the old bundle aside and the new one in, then removes the old one.
///
/// The running process carries on as it was; the new version starts from the next launch (or <see cref="Relaunch"/>).
/// </summary>
public static class SelfUpdate
{
    const string StagingPrefix = ".SpaceAnalyzer-update-";

    public static string AssetUrl(string tag, string asset) =>
        $"{GitHub.Repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(asset)}";

    /// <summary>
    /// Installs release <paramref name="tag"/> over <paramref name="target"/>. Returns null when it's done, or why
    /// not; nothing is replaced unless the download is complete and matches its checksum.
    /// </summary>
    /// <param name="downloadText">Fetches a small text document, or null (the platform's <c>DownloadText</c>).</param>
    /// <param name="downloadFile">Fetches a file to a path (the platform's <c>DownloadFile</c>).</param>
    /// <param name="unpack">Unpacks an archive into a folder; by default <c>tar</c> or <c>ditto</c>.</param>
    public static string? Install(UpdateTarget target, string tag, Func<string, string?> downloadText,
        Func<string, string, bool> downloadFile, Func<string, string, UpdateKind, bool>? unpack = null)
    {
        string folder = Path.GetDirectoryName(target.Path)!;
        string staging = Path.Combine(folder, StagingPrefix + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"can't write to {folder}: {ex.Message}";
        }

        try
        {
            if (ParseChecksum(downloadText(AssetUrl(tag, "SHA256SUMS.txt")), target.AssetName) is not { } expected)
                return "the release has no checksum for " + target.AssetName;

            string file = Path.Combine(staging, target.AssetName);
            if (!downloadFile(AssetUrl(tag, target.AssetName), file)) return "the download failed";
            if (Sha256.OfFile(file) != expected) return "the download doesn't match its checksum";

            string fresh = file;
            if (target.Kind != UpdateKind.Executable)
            {
                string unpacked = Path.Combine(staging, "unpacked");
                Directory.CreateDirectory(unpacked);
                if (!(unpack ?? Unpack)(file, unpacked, target.Kind)) return "the download could not be unpacked";
                fresh = Path.Combine(unpacked, target.Kind == UpdateKind.TarGz ? "SpaceAnalyzer" : "SpaceAnalyzer.app");
                string program = target.Kind == UpdateKind.TarGz ? fresh : Path.Combine(fresh, "Contents", "MacOS", "SpaceAnalyzer");
                if (!File.Exists(program)) return "the download doesn't contain SpaceAnalyzer";
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(program, File.GetUnixFileMode(program) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            else if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(fresh, File.GetUnixFileMode(fresh) | UnixFileMode.UserExecute);
            }

            Swap(target, fresh);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ex.Message;
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>The checksum listed for <paramref name="fileName"/> in a <c>sha256sum</c> listing, as lowercase hex.</summary>
    public static string? ParseChecksum(string? sums, string fileName)
    {
        if (sums is null) return null;
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            int space = line.IndexOf(' ');
            if (space != 64) continue;
            string name = line[space..].TrimStart(' ', '*');
            if (name == fileName && line[..64].All(Uri.IsHexDigit)) return line[..64].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Puts <paramref name="fresh"/> where <paramref name="target"/> is, keeping the old copy if that fails.</summary>
    static void Swap(UpdateTarget target, string fresh)
    {
        string aside = $"{target.Path}.{Guid.NewGuid().ToString("N")[..8]}.old";
        switch (target.Kind)
        {
            case UpdateKind.TarGz:
                File.Move(fresh, target.Path, overwrite: true);
                break;
            case UpdateKind.Executable:
                File.Move(target.Path, aside);
                try { File.Move(fresh, target.Path); }
                catch { File.Move(aside, target.Path); throw; }
                TryDelete(aside); // fails while it runs (Windows): CleanUp removes it on a later start
                break;
            case UpdateKind.AppBundleZip:
                Directory.Move(target.Path, aside);
                try { Directory.Move(fresh, target.Path); }
                catch { Directory.Move(aside, target.Path); throw; }
                TryDelete(aside);
                break;
        }
    }

    /// <summary>Removes what earlier updates left next to <paramref name="target"/>: old copies and interrupted downloads.</summary>
    public static void CleanUp(UpdateTarget target)
    {
        try
        {
            string folder = Path.GetDirectoryName(target.Path)!, name = Path.GetFileName(target.Path);
            foreach (var old in Directory.EnumerateFileSystemEntries(folder, name + ".*.old")) TryDelete(old);
            foreach (var staging in Directory.EnumerateDirectories(folder, StagingPrefix + "*"))
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(staging) > TimeSpan.FromHours(1)) TryDelete(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Starts the (updated) copy again, on <paramref name="folder"/> if given. The caller then quits.</summary>
    public static bool Relaunch(UpdateTarget target, string? folder)
    {
        try
        {
            ProcessStartInfo psi;
            if (target.Kind == UpdateKind.AppBundleZip)
            {
                psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                psi.ArgumentList.Add("-n");
                psi.ArgumentList.Add(target.Path);
                if (folder is not null) { psi.ArgumentList.Add("--args"); psi.ArgumentList.Add(folder); }
            }
            else
            {
                psi = new ProcessStartInfo(target.Path) { UseShellExecute = false };
                if (folder is not null) psi.ArgumentList.Add(folder);
            }
            using var _ = Process.Start(psi);
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            ErrorLog.Write(ex);
            return false;
        }
    }

    /// <summary>Unpacks with the system's own tools: <c>tar</c> on Linux, <c>ditto</c> on macOS (which keeps the bundle's signature).</summary>
    static bool Unpack(string archive, string destination, UpdateKind kind)
    {
        var psi = kind == UpdateKind.TarGz
            ? new ProcessStartInfo("tar") { ArgumentList = { "-xzf", archive, "-C", destination } }
            : new ProcessStartInfo("/usr/bin/ditto") { ArgumentList = { "-x", "-k", archive, destination } };
        psi.UseShellExecute = false;
        psi.RedirectStandardError = true;
        try
        {
            using var p = Process.Start(psi)!;
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
