using System.Text;

namespace SpaceAnalizer.Core;

public enum NodeKind : byte { File, Directory }

/// <summary>
/// One file or folder of a scanned tree. Folders keep their children sorted by size (largest first)
/// together with aggregated totals, so any sub-tree can be laid out and summarized without re-walking it.
/// </summary>
public sealed class FileNode
{
    public static readonly FileNode[] NoChildren = [];

    public FileNode(string name, NodeKind kind, FileNode? parent)
    {
        Name = name;
        Kind = kind;
        Parent = parent;
        if (kind == NodeKind.File) FileCount = 1;
    }

    /// <summary>File or folder name. The root of a scan holds the full path instead.</summary>
    public string Name { get; internal set; }
    public NodeKind Kind { get; }
    public FileNode? Parent { get; internal set; }
    public FileNode[] Children { get; internal set; } = NoChildren;

    /// <summary>Bytes used by the file, or by everything below the folder.</summary>
    public long Size { get; internal set; }
    /// <summary>Number of files in the sub-tree (1 for a file).</summary>
    public long FileCount { get; internal set; }
    /// <summary>Number of folders in the sub-tree, not counting the folder itself.</summary>
    public long FolderCount { get; internal set; }
    /// <summary>Last write time (UTC ticks). For folders: the most recent file inside.</summary>
    public long LastWriteUtcTicks { get; internal set; }
    /// <summary>File type, or for folders the type that takes the most space inside.</summary>
    public FileCategory Category { get; internal set; }
    /// <summary>Bytes per <see cref="FileCategory"/> in the sub-tree (folders only).</summary>
    public long[]? CategorySizes { get; internal set; }
    /// <summary>The folder (or part of it) could not be read.</summary>
    public bool AccessDenied { get; internal set; }

    public bool IsDirectory => Kind == NodeKind.Directory;
    public bool IsFile => Kind == NodeKind.File;
    public bool IsRoot => Parent is null;

    public FileNode Root
    {
        get
        {
            var n = this;
            while (n.Parent is not null) n = n.Parent;
            return n;
        }
    }

    public int Depth
    {
        get
        {
            int d = 0;
            for (var p = Parent; p is not null; p = p.Parent) d++;
            return d;
        }
    }

    public string FullPath
    {
        get
        {
            if (Parent is null) return Name;
            var parts = new List<string>(16);
            var n = this;
            while (n.Parent is not null)
            {
                parts.Add(n.Name);
                n = n.Parent;
            }
            var sb = new StringBuilder(n.Name, n.Name.Length + parts.Count * 16);
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (sb.Length > 0 && sb[^1] != Path.DirectorySeparatorChar && sb[^1] != Path.AltDirectorySeparatorChar)
                    sb.Append(Path.DirectorySeparatorChar);
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }
    }

    public bool IsAncestorOf(FileNode other)
    {
        for (var p = other.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, this)) return true;
        return false;
    }

    public bool IsSelfOrAncestorOf(FileNode other) => ReferenceEquals(this, other) || IsAncestorOf(other);

    /// <summary>Finds a direct child by name (case-sensitive first, then ignoring case).</summary>
    public FileNode? FindChild(string name)
    {
        foreach (var c in Children)
            if (c.Name == name) return c;
        foreach (var c in Children)
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    public override string ToString() => $"{Name} ({Size} bytes)";

    internal static readonly Comparison<FileNode> BySizeDescending = static (a, b) =>
    {
        int c = b.Size.CompareTo(a.Size);
        return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
    };
}
