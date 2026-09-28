using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace SpaceAnalizer.Core;

/// <summary>Live counters of a running scan; written by the scan threads, read by the UI.</summary>
public sealed class ScanProgress
{
    public long Files;
    public long Folders;
    public long Bytes;
    public long Errors;
    public volatile string CurrentPath = "";
}

/// <summary>
/// Walks a folder with several threads and builds a <see cref="FileNode"/> tree with totals.
/// Symbolic links, junctions and other mounted volumes are not followed, so nothing is counted twice.
/// </summary>
public static class Scanner
{
    public static FileNode Scan(string path, ScanProgress progress, CancellationToken cancel, int threads = 0)
    {
        string rootPath = NormalizePath(path);
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);

        var root = new FileNode(rootPath, NodeKind.Directory, null);
        try { root.LastWriteUtcTicks = Directory.GetLastWriteTimeUtc(rootPath).Ticks; } catch { }

        using (var walker = new Walker(progress, cancel, Volumes.NestedMountPoints(rootPath)))
            walker.Run(root, threads);

        cancel.ThrowIfCancellationRequested();
        Aggregate(root);
        return root;
    }

    public static string NormalizePath(string path)
    {
        path = path.Trim();
        if (OperatingSystem.IsWindows() && path.Length == 2 && path[1] == ':')
            path += "\\";
        string full = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && full.Length > root.Length)
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    /// <summary>Computes folder totals bottom-up and sorts every folder's children by size.</summary>
    internal static void Aggregate(FileNode root)
    {
        // Breadth-first order puts every folder before its sub-folders; walking it backwards
        // therefore visits children before parents without recursion.
        var dirs = new List<FileNode> { root };
        for (int i = 0; i < dirs.Count; i++)
            foreach (var c in dirs[i].Children)
                if (c.IsDirectory) dirs.Add(c);

        for (int i = dirs.Count - 1; i >= 0; i--)
            AggregateFolder(dirs[i]);
    }

    internal static void AggregateFolder(FileNode d)
    {
        long size = 0, files = 0, folders = 0, last = 0;
        var cats = d.CategorySizes ?? new long[FileCategories.Count];
        Array.Clear(cats);
        foreach (var c in d.Children)
        {
            size += c.Size;
            if (c.IsDirectory)
            {
                files += c.FileCount;
                folders += 1 + c.FolderCount;
                if (c.CategorySizes is { } cc)
                    for (int k = 0; k < cats.Length; k++) cats[k] += cc[k];
            }
            else
            {
                files++;
                cats[(int)c.Category] += c.Size;
            }
            if (c.LastWriteUtcTicks > last) last = c.LastWriteUtcTicks;
        }
        d.Size = size;
        d.FileCount = files;
        d.FolderCount = folders;
        if (last > 0) d.LastWriteUtcTicks = last;
        d.CategorySizes = cats;
        d.Category = Dominant(cats);
        if (d.Children.Length > 1)
            Array.Sort(d.Children, FileNode.BySizeDescending);
    }

    internal static FileCategory Dominant(long[] cats)
    {
        int best = 0;
        for (int k = 1; k < cats.Length; k++)
            if (cats[k] > cats[best]) best = k;
        return (FileCategory)best;
    }

    readonly record struct Entry(string Name, bool IsDirectory, long Length, long LastWrite, FileAttributes Attributes);

    sealed class Walker(ScanProgress progress, CancellationToken cancel, HashSet<string>? skip) : IDisposable
    {
        const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
        const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
        const FileAttributes NotOnDisk = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;

        static readonly EnumerationOptions s_options = new()
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,   // we want to know (and count) what could not be read
            AttributesToSkip = 0,         // include hidden and system files (pagefile.sys, dot-files...)
            ReturnSpecialDirectories = false,
            BufferSize = 16 * 1024,
        };

        static readonly bool s_windows = OperatingSystem.IsWindows();

        readonly BlockingCollection<FileNode> _queue = new(new ConcurrentStack<FileNode>());
        int _pending;

        public void Run(FileNode root, int threads)
        {
            if (threads <= 0) threads = Math.Clamp(Environment.ProcessorCount, 2, 8);
            _pending = 1;
            _queue.Add(root);
            var workers = new Thread[threads];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = new Thread(Work) { IsBackground = true, Name = "SpaceAnalizer scan", Priority = ThreadPriority.BelowNormal };
                workers[i].Start();
            }
            foreach (var w in workers) w.Join();
        }

        void Work()
        {
            try
            {
                foreach (var dir in _queue.GetConsumingEnumerable(cancel))
                {
                    ScanDirectory(dir);
                    if (Interlocked.Decrement(ref _pending) == 0)
                        _queue.CompleteAdding();
                }
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { } // completed while waiting
        }

        void ScanDirectory(FileNode dir)
        {
            string path = dir.FullPath;
            progress.CurrentPath = path;
            var list = new List<FileNode>();
            long files = 0, folders = 0, bytes = 0;
            try
            {
                var entries = new FileSystemEnumerable<Entry>(path, static (ref FileSystemEntry e) =>
                {
                    bool isDir = e.IsDirectory;
                    return new Entry(
                        e.FileName.ToString(),
                        isDir,
                        isDir ? 0 : e.Length,
                        isDir ? 0 : e.LastWriteTimeUtc.UtcTicks,
                        e.Attributes);
                }, s_options);

                foreach (var e in entries)
                {
                    if (cancel.IsCancellationRequested) break;

                    if (e.IsDirectory)
                    {
                        string childPath = Path.Join(path, e.Name);
                        if ((e.Attributes & FileAttributes.ReparsePoint) != 0 && IsLinkOrMount(childPath))
                            continue;
                        if (skip is not null && skip.Contains(childPath))
                            continue;
                        var child = new FileNode(e.Name, NodeKind.Directory, dir);
                        list.Add(child);
                        folders++;
                        Interlocked.Increment(ref _pending);
                        _queue.Add(child);
                    }
                    else
                    {
                        long size = e.Length;
                        if (s_windows && (e.Attributes & NotOnDisk) != 0)
                            size = 0;                               // cloud placeholder (OneDrive...)
                        else if (!s_windows && (e.Attributes & FileAttributes.ReparsePoint) != 0)
                            size = 0;                               // symbolic link: don't count its target
                        else
                            size = AllocatedSize.Adjust(path, e.Name, size, e.Attributes);

                        var category = FileCategories.Classify(e.Name);
                        if (category == FileCategory.Other && !OperatingSystem.IsWindows() && IsExecutable(path, e.Name))
                            category = FileCategory.Program; // Mach-O / ELF binaries and scripts have no extension

                        list.Add(new FileNode(e.Name, NodeKind.File, dir)
                        {
                            Size = size,
                            LastWriteUtcTicks = e.LastWrite,
                            Category = category,
                        });
                        files++;
                        bytes += size;
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                dir.AccessDenied = true;
                Interlocked.Increment(ref progress.Errors);
            }

            dir.Children = list.Count == 0 ? FileNode.NoChildren : list.ToArray();
            Interlocked.Add(ref progress.Files, files);
            Interlocked.Add(ref progress.Folders, folders);
            Interlocked.Add(ref progress.Bytes, bytes);
        }

        /// <summary>Extension-less file with an execute bit (Unix only; one extra stat, only for such files).</summary>
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        static bool IsExecutable(string directory, string name)
        {
            if (name.IndexOf('.', 1) >= 0) return false;
            try
            {
                const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                return (File.GetUnixFileMode(Path.Join(directory, name)) & exec) != 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// True for symbolic links, junctions and mounted volumes (which have a link target), false for
        /// other reparse points such as OneDrive / cloud folders, which must be scanned normally.
        /// </summary>
        static bool IsLinkOrMount(string path)
        {
            try { return new DirectoryInfo(path).LinkTarget is not null; }
            catch { return true; }
        }

        public void Dispose() => _queue.Dispose();
    }
}
