using SpaceAnalyzer.Core;
using System.Runtime.InteropServices;

// Several tests change process-wide settings (language, units): run everything sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SpaceAnalyzer.Tests;

public class SquarifyTests
{
    [Fact]
    public void Areas_are_proportional_inside_bounds_and_do_not_overlap()
    {
        var rng = new Random(1);
        for (int round = 0; round < 50; round++)
        {
            int n = rng.Next(1, 60);
            var values = Enumerable.Range(0, n).Select(_ => 1 + rng.NextDouble() * 1000).OrderByDescending(v => v).ToArray();
            var bounds = new RectD(10, 20, 300 + rng.Next(500), 200 + rng.Next(400));
            var rects = new RectD[n];
            Squarify.Layout(values, bounds, rects);

            double total = values.Sum();
            for (int i = 0; i < n; i++)
            {
                var r = rects[i];
                Assert.True(r.X >= bounds.X - 1e-6 && r.Y >= bounds.Y - 1e-6, $"rect {i} starts outside");
                Assert.True(r.Right <= bounds.Right + 1e-6 && r.Bottom <= bounds.Bottom + 1e-6, $"rect {i} ends outside");
                Assert.Equal(values[i] / total * bounds.Area, r.Area, 1e-6 * bounds.Area);
                for (int j = i + 1; j < n; j++)
                {
                    var o = rects[j];
                    double ix = Math.Min(r.Right, o.Right) - Math.Max(r.X, o.X);
                    double iy = Math.Min(r.Bottom, o.Bottom) - Math.Max(r.Y, o.Y);
                    Assert.False(ix > 1e-6 && iy > 1e-6, $"rects {i} and {j} overlap");
                }
            }
        }
    }

    [Fact]
    public void Squarified_rectangles_are_not_slivers()
    {
        var values = Enumerable.Repeat(1.0, 16).ToArray();
        var rects = new RectD[16];
        Squarify.Layout(values, new RectD(0, 0, 400, 400), rects);
        foreach (var r in rects)
            Assert.InRange(Math.Max(r.W / r.H, r.H / r.W), 1, 1.5);
    }

    [Fact]
    public void A_single_item_takes_the_whole_area()
    {
        var rects = new RectD[1];
        Squarify.Layout([42], new RectD(5, 6, 70, 80), rects);
        Assert.Equal(new RectD(5, 6, 70, 80), rects[0]);
    }
}

public class ScannerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "sa-test-" + Guid.NewGuid().ToString("N"));

    public ScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "videos", "2025"));
        Directory.CreateDirectory(Path.Combine(_dir, "docs"));
        Directory.CreateDirectory(Path.Combine(_dir, "empty"));
        File.WriteAllBytes(Path.Combine(_dir, "videos", "2025", "a.mp4"), new byte[30_000]);
        File.WriteAllBytes(Path.Combine(_dir, "videos", "b.mov"), new byte[20_000]);
        File.WriteAllBytes(Path.Combine(_dir, "docs", "c.pdf"), new byte[5_000]);
        File.WriteAllBytes(Path.Combine(_dir, "notes.txt"), new byte[1_000]);
        if (!OperatingSystem.IsWindows())
            Directory.CreateSymbolicLink(Path.Combine(_dir, "link-to-videos"), Path.Combine(_dir, "videos"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Totals_counts_order_and_types_are_computed()
    {
        var root = Scanner.Scan(_dir, new ScanProgress(), CancellationToken.None);

        Assert.Equal(56_000, root.Size); // the symbolic link is not followed: nothing counted twice
        Assert.Equal(4, root.FileCount);
        Assert.Equal(4, root.FolderCount);
        Assert.Equal(["videos", "docs", "notes.txt"], root.Children.Where(c => c.Size > 0).Select(c => c.Name));
        Assert.Equal(FileCategory.Video, root.Category);
        Assert.Equal(50_000, root.CategorySizes![(int)FileCategory.Video]);
        Assert.Equal(6_000, root.CategorySizes[(int)FileCategory.Document]);

        var videos = root.FindChild("videos")!;
        Assert.Equal(["2025", "b.mov"], videos.Children.Select(c => c.Name));
        Assert.Equal(Path.Combine(_dir, "videos", "2025", "a.mp4"), videos.Children[0].Children[0].FullPath);
    }

    [Fact]
    public void Cancelling_stops_the_scan()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Scanner.Scan(_dir, new ScanProgress(), cts.Token));
    }

    [Fact]
    public void Removing_an_item_updates_every_ancestor()
    {
        var root = Scanner.Scan(_dir, new ScanProgress(), CancellationToken.None);
        var movie = TreeOps.FindByPath(root, Path.Combine(_dir, "videos", "2025", "a.mp4"))!;
        TreeOps.Remove(movie);

        Assert.Equal(26_000, root.Size);
        Assert.Equal(3, root.FileCount);
        Assert.Equal(20_000, root.FindChild("videos")!.Size);
        Assert.Equal(20_000, root.CategorySizes![(int)FileCategory.Video]);
        Assert.Null(TreeOps.FindByPath(root, Path.Combine(_dir, "videos", "2025", "a.mp4")));
    }

    [Fact]
    public void Rescanning_a_folder_replaces_it_in_place()
    {
        var root = Scanner.Scan(_dir, new ScanProgress(), CancellationToken.None);
        var docs = root.FindChild("docs")!;
        File.WriteAllBytes(Path.Combine(_dir, "docs", "d.docx"), new byte[70_000]);
        var fresh = Scanner.Scan(docs.FullPath, new ScanProgress(), CancellationToken.None);
        var placed = TreeOps.Replace(docs, fresh);

        Assert.Equal("docs", placed.Name);
        Assert.Same(root, placed.Parent);
        Assert.Equal(126_000, root.Size);
        Assert.Equal("docs", root.Children[0].Name); // the bigger folder moved to the front
    }
}

public class FormattingTests
{
    [Fact]
    public void Sizes_in_spanish_and_english()
    {
        bool es = Strings.Spanish, dec = Fmt.DecimalUnits;
        try
        {
            Strings.Spanish = true;
            Fmt.DecimalUnits = false;
            Assert.Equal("1,50 KB", Fmt.Size(1536));
            Assert.Equal("1,21 GB", Fmt.Size(1_300_000_000));
            Assert.Equal("1234", Fmt.Count(1234));
            Assert.Equal("12.345", Fmt.Count(12345));
            Assert.Equal("7,0 %", Fmt.Percent(0.07));
            Assert.Equal("45 %", Fmt.Percent(0.45));

            Strings.Spanish = false;
            Fmt.DecimalUnits = true;
            Assert.Equal("1.30 GB", Fmt.Size(1_300_000_000));
            Assert.Equal("12,345", Fmt.Count(12345));
            Assert.Equal("999 B", Fmt.Size(999));
            Assert.Equal("1.00 MB", Fmt.Size(999_999));
        }
        finally
        {
            Strings.Spanish = es;
            Fmt.DecimalUnits = dec;
        }
    }

    [Theory]
    [InlineData("Película.MKV", FileCategory.Video)]
    [InlineData("foto.heic", FileCategory.Image)]
    [InlineData("Docker.raw", FileCategory.Archive)]
    [InlineData("pagefile.sys", FileCategory.Data)]
    [InlineData("driver.sys", FileCategory.Program)]
    [InlineData("Program.cs", FileCategory.Code)]
    [InlineData("README", FileCategory.Other)]
    [InlineData(".bashrc", FileCategory.Other)]
    public void Files_are_classified_by_extension(string name, FileCategory expected) =>
        Assert.Equal(expected, FileCategories.Classify(name));
}

public class DemoTreeTests
{
    [Fact]
    public void Demo_tree_is_deterministic_and_consistent()
    {
        var a = DemoTree.Build();
        var b = DemoTree.Build();
        Assert.Equal(a.Size, b.Size);
        Assert.Equal(a.FileCount, b.FileCount);
        Assert.True(a.Size > 100L * 1024 * 1024 * 1024);

        var stack = new Stack<FileNode>([a]);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            Assert.Equal(d.Children.Sum(c => c.Size), d.Size);
            for (int i = 1; i < d.Children.Length; i++) Assert.True(d.Children[i - 1].Size >= d.Children[i].Size);
            foreach (var c in d.Children) if (c.IsDirectory) stack.Push(c);
        }
    }

    [Fact]
    public void Largest_files_matches_a_full_search()
    {
        var root = DemoTree.Build();
        var fast = UI.MainView.LargestFiles(root, 25);
        var all = new List<FileNode>();
        var stack = new Stack<FileNode>([root]);
        while (stack.Count > 0)
            foreach (var c in stack.Pop().Children)
                if (c.IsDirectory) stack.Push(c); else all.Add(c);
        var expected = all.OrderByDescending(f => f.Size).Take(25).Select(f => f.Size);
        Assert.Equal(expected, fast.Select(f => f.Size));
    }
}

public class UpdateCheckTests
{
    [Theory]
    [InlineData("v1.1.0", "1.0.0", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v1.0", "1.0.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("v2.0.0-beta.1", "1.9.9", true)]
    [InlineData("v0.9.9", "1.0.0", false)]
    [InlineData("latest", "1.0.0", false)]
    public void Versions_compare_by_number(string latest, string current, bool newer) =>
        Assert.Equal(newer, GitHub.IsNewer(latest, current));

    [Fact]
    public void Reads_the_tag_of_the_latest_release()
    {
        // Shaped like the real answer: nested objects have their own fields, and the notes can quote anything.
        const string json = """
            {"url":"https://api.github.com/repos/aurgo/SpaceAnalyzer/releases/1",
             "author":{"login":"aurgo","html_url":"https://github.com/aurgo","tag_name":"v7.0.0"},
             "tag_name" : "v1.2.0", "name":"SpaceAnalyzer 1.2.0",
             "assets":[{"name":"SpaceAnalyzer-windows-x64.exe","tag_name":"v8.0.0"}],
             "body":"\"quotes\", a \\ backslash, {braces} and [brackets], and \"tag_name\": \"v9.0.0\" é"}
            """;
        var release = Assert.NotNull(GitHub.ParseLatestRelease(json));
        Assert.Equal("1.2.0", release.Version);
        Assert.Equal("https://github.com/aurgo/SpaceAnalyzer/releases/tag/v1.2.0", release.Url);
        Assert.Equal("\"quotes\", a \\ backslash, {braces} and [brackets], and \"tag_name\": \"v9.0.0\" é", GitHub.TopLevelString(json, "body"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>Sign in to the Wi-Fi</html>")]
    [InlineData("""{"message":"Not Found","documentation_url":"https://docs.github.com/rest"}""")]
    [InlineData("""{"message":"API rate limit exceeded","tag_name":null}""")]
    [InlineData("""{"tag_name":"nightly"}""")]
    public void Anything_but_a_release_fails_the_check(string? answer) =>
        Assert.Null(GitHub.ParseLatestRelease(answer));
}

public class UpdatePrefsTests
{
    [Fact]
    public void Saves_and_loads_the_setting_and_the_last_check()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sa-prefs-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "settings.ini");
            var fresh = UpdatePrefs.Load(path); // no file yet: the defaults
            Assert.True(fresh.AutoCheck);
            Assert.Null(fresh.LastCheckUtc);

            var when = new DateTime(2026, 9, 30, 8, 15, 0, DateTimeKind.Utc);
            new UpdatePrefs(path) { AutoCheck = false, LastCheckUtc = when }.Save();
            var loaded = UpdatePrefs.Load(path);
            Assert.False(loaded.AutoCheck);
            Assert.Equal(when, loaded.LastCheckUtc);
            Assert.Equal(DateTimeKind.Utc, loaded.LastCheckUtc!.Value.Kind);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Is_due_once_a_day_while_turned_on()
    {
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(new UpdatePrefs().IsDue(now));
        Assert.False(new UpdatePrefs { LastCheckUtc = now.AddHours(-23) }.IsDue(now));
        Assert.True(new UpdatePrefs { LastCheckUtc = now.AddHours(-25) }.IsDue(now));
        Assert.True(new UpdatePrefs { LastCheckUtc = now.AddDays(3) }.IsDue(now)); // the clock went back
        Assert.False(new UpdatePrefs { AutoCheck = false }.IsDue(now));
    }
}

public class SelfUpdateTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "sa-update-" + Guid.NewGuid().ToString("N"));

    public SelfUpdateTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(55)]
    [InlineData(56)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(119)]
    [InlineData(200_000)]
    public void Sha256_matches_the_framework(int length)
    {
        var data = new byte[length];
        new Random(length).NextBytes(data);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data)), Sha256.Of(data));
    }

    [Fact]
    public void Sha256_of_the_standard_vectors()
    {
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Sha256.Of([]));
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", Sha256.Of("abc"u8));
    }

    [Fact]
    public void Reads_the_checksum_of_one_file_from_SHA256SUMS()
    {
        string a = new('a', 64), b = new('B', 64);
        string sums = $"{a}  SpaceAnalyzer-windows-x64.exe\n{b} *SpaceAnalyzer-macos.zip\r\n";
        Assert.Equal(a, SelfUpdate.ParseChecksum(sums, "SpaceAnalyzer-windows-x64.exe"));
        Assert.Equal(b.ToLowerInvariant(), SelfUpdate.ParseChecksum(sums, "SpaceAnalyzer-macos.zip"));
        Assert.Null(SelfUpdate.ParseChecksum(sums, "SpaceAnalyzer-windows-x64-mini.exe"));
        Assert.Null(SelfUpdate.ParseChecksum(null, "SpaceAnalyzer-macos.zip"));
    }

    [Fact]
    public void Picks_the_file_that_replaces_this_copy()
    {
        var win = UpdateTarget.For(@"C:\Tools\SpaceAnalyzer.exe", OSPlatform.Windows, Architecture.Arm64, nativeAot: true)!;
        Assert.Equal(("SpaceAnalyzer-windows-arm64.exe", UpdateKind.Executable), (win.AssetName, win.Kind));
        Assert.Equal("SpaceAnalyzer-windows-x64-mini.exe", UpdateTarget.For(@"C:\SpaceAnalyzer.exe", OSPlatform.Windows, Architecture.X64, nativeAot: false)!.AssetName);
        Assert.Equal("SpaceAnalyzer-linux-x64.tar.gz", UpdateTarget.For("/opt/sa/SpaceAnalyzer", OSPlatform.Linux, Architecture.X64, nativeAot: true)!.AssetName);

        var mac = UpdateTarget.For("/Applications/SpaceAnalyzer.app/Contents/MacOS/SpaceAnalyzer", OSPlatform.OSX, Architecture.Arm64, nativeAot: true)!;
        Assert.Equal(("/Applications/SpaceAnalyzer.app", UpdateKind.AppBundleZip), (mac.Path.Replace('\\', '/').Replace("C:", ""), mac.Kind));

        // Mini builds that run through the dotnet host, a Mac executable outside its bundle, 32-bit, and copies SharpCommander keeps.
        Assert.Null(UpdateTarget.For("/usr/bin/dotnet", OSPlatform.Linux, Architecture.X64, nativeAot: false));
        Assert.Null(UpdateTarget.For("/tmp/SpaceAnalyzer", OSPlatform.OSX, Architecture.Arm64, nativeAot: true));
        Assert.Null(UpdateTarget.For(@"C:\SpaceAnalyzer.exe", OSPlatform.Windows, Architecture.X86, nativeAot: true));
        Assert.Null(UpdateTarget.For("/Users/ana/Library/Application Support/SharpCommander/tools/SpaceAnalyzer/1.2.0/SpaceAnalyzer.app/Contents/MacOS/SpaceAnalyzer",
            OSPlatform.OSX, Architecture.Arm64, nativeAot: true));
    }

    /// <summary>A fake release: the asset's bytes and a SHA256SUMS.txt that lists them (or <paramref name="listedHash"/>).</summary>
    static (Func<string, string?> Text, Func<string, string, bool> File) Release(string tag, string asset, byte[] bytes, string? listedHash = null)
    {
        string sums = $"{listedHash ?? Sha256.Of(bytes)}  {asset}\n";
        return (url => url == SelfUpdate.AssetUrl(tag, "SHA256SUMS.txt") ? sums : null,
                (url, path) => { if (url != SelfUpdate.AssetUrl(tag, asset)) return false; File.WriteAllBytes(path, bytes); return true; });
    }

    [Fact]
    public void Replaces_an_executable_and_leaves_nothing_behind()
    {
        string exe = Path.Combine(_dir, "SpaceAnalyzer.exe");
        File.WriteAllText(exe, "1.2.0");
        var target = new UpdateTarget(exe, "SpaceAnalyzer-windows-x64.exe", UpdateKind.Executable);
        var (text, file) = Release("v1.3.0", target.AssetName, "1.3.0"u8.ToArray());

        Assert.Null(SelfUpdate.Install(target, "v1.3.0", text, file));
        Assert.Equal("1.3.0", File.ReadAllText(exe));
        Assert.Equal([exe], Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void A_download_that_does_not_match_its_checksum_changes_nothing()
    {
        string exe = Path.Combine(_dir, "SpaceAnalyzer.exe");
        File.WriteAllText(exe, "1.2.0");
        var target = new UpdateTarget(exe, "SpaceAnalyzer-windows-x64.exe", UpdateKind.Executable);
        var (text, file) = Release("v1.3.0", target.AssetName, "tampered"u8.ToArray(), listedHash: new string('0', 64));

        Assert.NotNull(SelfUpdate.Install(target, "v1.3.0", text, file));
        Assert.Equal("1.2.0", File.ReadAllText(exe));
        Assert.Equal([exe], Directory.GetFileSystemEntries(_dir));

        // No checksum published, or no network: nothing either.
        Assert.NotNull(SelfUpdate.Install(target, "v1.3.0", _ => null, file));
        Assert.Equal("1.2.0", File.ReadAllText(exe));
    }

    [Fact]
    public void Replaces_the_Linux_program_from_its_tar_gz()
    {
        if (OperatingSystem.IsWindows()) return; // the .tar.gz is only published for Linux
        string exe = Path.Combine(_dir, "SpaceAnalyzer");
        File.WriteAllText(exe, "1.2.0");

        var archive = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(archive, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new System.Formats.Tar.TarWriter(gzip))
        {
            var entry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "SpaceAnalyzer")
            {
                DataStream = new MemoryStream("1.3.0"u8.ToArray()),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            };
            tar.WriteEntry(entry);
        }
        var target = new UpdateTarget(exe, "SpaceAnalyzer-linux-x64.tar.gz", UpdateKind.TarGz);
        var (text, file) = Release("v1.3.0", target.AssetName, archive.ToArray());

        Assert.Null(SelfUpdate.Install(target, "v1.3.0", text, file));
        Assert.Equal("1.3.0", File.ReadAllText(exe));
        Assert.True(File.GetUnixFileMode(exe).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal([exe], Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void Replaces_the_whole_macOS_bundle()
    {
        string app = Path.Combine(_dir, "SpaceAnalyzer.app");
        string program = Path.Combine(app, "Contents", "MacOS", "SpaceAnalyzer");
        Directory.CreateDirectory(Path.GetDirectoryName(program)!);
        File.WriteAllText(program, "1.2.0");
        File.WriteAllText(Path.Combine(app, "Contents", "only-in-1.2.0"), "");
        var target = new UpdateTarget(app, "SpaceAnalyzer-macos.zip", UpdateKind.AppBundleZip);
        var (text, file) = Release("v1.3.0", target.AssetName, "zip"u8.ToArray());

        // ditto only exists on macOS: unpack by writing what the zip holds.
        bool Unpack(string archive, string destination, UpdateKind kind)
        {
            string p = Path.Combine(destination, "SpaceAnalyzer.app", "Contents", "MacOS", "SpaceAnalyzer");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "1.3.0");
            return true;
        }

        Assert.Null(SelfUpdate.Install(target, "v1.3.0", text, file, Unpack));
        Assert.Equal("1.3.0", File.ReadAllText(program));
        Assert.False(File.Exists(Path.Combine(app, "Contents", "only-in-1.2.0")));
        Assert.Equal([app], Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void Cleans_up_old_copies_and_interrupted_downloads()
    {
        string exe = Path.Combine(_dir, "SpaceAnalyzer.exe");
        File.WriteAllText(exe, "");
        File.WriteAllText(exe + ".1a2b3c4d.old", "");
        string stale = Directory.CreateDirectory(Path.Combine(_dir, ".SpaceAnalyzer-update-00000000")).FullName;
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-1));
        string fresh = Directory.CreateDirectory(Path.Combine(_dir, ".SpaceAnalyzer-update-11111111")).FullName; // maybe another copy's

        SelfUpdate.CleanUp(new UpdateTarget(exe, "SpaceAnalyzer-windows-x64.exe", UpdateKind.Executable));
        Assert.Equal(new[] { exe, fresh }.Order(), Directory.GetFileSystemEntries(_dir).Order());
    }
}

public class UpdatePrefsPathTests
{
    [Fact]
    public void The_settings_file_is_never_put_in_the_current_directory()
    {
        if (OperatingSystem.IsWindows()) return; // %APPDATA% always exists there
        string? before = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        string missing = Path.Combine(Path.GetTempPath(), "sa-no-such-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A fresh account whose ~/.config doesn't exist yet: the file goes there once it is created.
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", missing);
            if (OperatingSystem.IsLinux()) Assert.Equal(Path.Combine(missing, "SpaceAnalyzer", "settings.ini"), UpdatePrefs.DefaultPath);
            Assert.True(UpdatePrefs.DefaultPath is null || Path.IsPathFullyQualified(UpdatePrefs.DefaultPath));
        }
        finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", before); }
    }
}
