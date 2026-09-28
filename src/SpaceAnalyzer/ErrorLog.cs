namespace SpaceAnalyzer;

/// <summary>
/// Unexpected errors caught by the platform layers. They go to stderr and, when the
/// SPACEANALYZER_ERROR_LOG environment variable names a file (the automated UI tests set it), to that file:
/// the app keeps running after an error, so this is how a test notices one.
/// </summary>
static class ErrorLog
{
    static int s_count;

    public static void Write(Exception ex)
    {
        if (Interlocked.Increment(ref s_count) > 50) return; // a failing paint would repeat every frame
        string text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n";
        try { Console.Error.Write(text); } catch { }
        if (Environment.GetEnvironmentVariable("SPACEANALYZER_ERROR_LOG") is { Length: > 0 } path)
        {
            try { File.AppendAllText(path, text); } catch { }
        }
    }
}
