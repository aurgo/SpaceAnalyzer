namespace SpaceAnalyzer;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Snapshot is not null) return UI.Snapshot.Run(options); // no window needed
        if (OperatingSystem.IsWindows()) return Platform.Windows.WinHost.Run(options);
        if (OperatingSystem.IsMacOS()) return Platform.MacOS.MacHost.Run(options);
        if (OperatingSystem.IsLinux()) return Platform.Linux.X11Host.Run(options);
        Console.Error.WriteLine("SpaceAnalyzer runs on Windows, macOS and Linux (X11).");
        return 1;
    }
}
