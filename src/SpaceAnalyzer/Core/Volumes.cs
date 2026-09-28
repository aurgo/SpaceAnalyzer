namespace SpaceAnalyzer.Core;

public sealed record VolumeInfo(string RootPath, string Label, long TotalSize, long FreeSpace, DriveType Type)
{
    public long UsedSize => Math.Max(0, TotalSize - FreeSpace);
    public double UsedFraction => TotalSize > 0 ? (double)UsedSize / TotalSize : 0;
}

public static class Volumes
{
    /// <summary>Optional platform hook that returns a friendly volume name (e.g. "Macintosh HD").</summary>
    public static Func<string, string?>? DisplayNameProvider;

    /// <summary>The volumes a user would want to scan (skips system, virtual and pseudo mounts).</summary>
    public static List<VolumeInfo> List()
    {
        var result = new List<VolumeInfo>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return result; }

        foreach (var d in drives)
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network))
                    continue;
                string name = d.Name;
                if (!OperatingSystem.IsWindows())
                {
                    // "/" and anything the user mounted under /Volumes (macOS) or /media, /mnt (Linux).
                    bool wanted = name == "/" || name.StartsWith("/Volumes/", StringComparison.Ordinal) ||
                                  name.StartsWith("/media/", StringComparison.Ordinal) ||
                                  name.StartsWith("/mnt/", StringComparison.Ordinal) ||
                                  name.StartsWith("/run/media/", StringComparison.Ordinal);
                    if (!wanted || d.TotalSize <= 0) continue;
                }

                long total = d.TotalSize;
                long free = OperatingSystem.IsWindows() ? d.TotalFreeSpace : d.AvailableFreeSpace;
                result.Add(new VolumeInfo(name, Label(d), total, free, d.DriveType));
            }
            catch
            {
                // Not ready, no permission, disconnected network drive...
            }
        }
        return result;
    }

    static string Label(DriveInfo d)
    {
        string? friendly = null;
        try { friendly = DisplayNameProvider?.Invoke(d.Name); } catch { }
        if (!string.IsNullOrWhiteSpace(friendly)) return friendly;

        if (OperatingSystem.IsWindows())
        {
            string letter = d.Name.TrimEnd('\\');
            string label = "";
            try { label = d.VolumeLabel; } catch { }
            if (string.IsNullOrWhiteSpace(label))
                label = d.DriveType switch
                {
                    DriveType.Removable => Strings.RemovableDisk,
                    DriveType.Network => Strings.NetworkDrive,
                    _ => Strings.LocalDisk,
                };
            return $"{label} ({letter})";
        }

        if (d.Name == "/") return Strings.StartupDisk;
        return Path.GetFileName(d.Name.TrimEnd('/'));
    }

    /// <summary>The volume that contains <paramref name="path"/> (longest matching mount point).</summary>
    public static VolumeInfo? FindForPath(string path)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        VolumeInfo? best = null;
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    string root = d.Name;
                    if (!IsUnder(path, root, cmp)) continue;
                    if (best is not null && best.RootPath.Length >= root.Length) continue;
                    long free = OperatingSystem.IsWindows() ? d.TotalFreeSpace : d.AvailableFreeSpace;
                    best = new VolumeInfo(root, Label(d), d.TotalSize, free, d.DriveType);
                }
                catch { }
            }
        }
        catch { }
        return best;
    }

    /// <summary>True when <paramref name="path"/> is the root of a volume (so free space can be shown next to it).</summary>
    public static bool IsVolumeRoot(string path, VolumeInfo? volume)
    {
        if (volume is null) return false;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Trim(path), Trim(volume.RootPath), cmp);
    }

    /// <summary>
    /// Mount points strictly below <paramref name="root"/> (Unix only). The scanner skips them so that
    /// other disks, FUSE / cloud mounts or the macOS data volume firmlinks are not walked or counted twice.
    /// </summary>
    public static HashSet<string>? NestedMountPoints(string root)
    {
        if (OperatingSystem.IsWindows()) return null; // junctions and mounted folders are reparse points
        HashSet<string>? set = null;
        string r = Trim(root);
        string prefix = r == "/" ? "/" : r + "/";
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                string m = Trim(d.Name);
                if (m == r || !m.StartsWith(prefix, StringComparison.Ordinal)) continue;
                (set ??= new HashSet<string>(StringComparer.Ordinal)).Add(m);
            }
        }
        catch { }
        return set;
    }

    static bool IsUnder(string path, string root, StringComparison cmp)
    {
        string p = Trim(path), r = Trim(root);
        if (string.Equals(p, r, cmp)) return true;
        string prefix = r.EndsWith('/') || r.EndsWith('\\') ? r : r + Path.DirectorySeparatorChar;
        return p.StartsWith(prefix, cmp);
    }

    static string Trim(string p)
    {
        if (p.Length <= 1) return p;
        var t = p.TrimEnd('/', '\\');
        if (t.Length == 0) return "/";
        if (t.Length == 2 && t[1] == ':') return t + "\\";
        return t;
    }
}
