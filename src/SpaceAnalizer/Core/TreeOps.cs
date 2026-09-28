namespace SpaceAnalizer.Core;

/// <summary>Keeps a scanned tree consistent after files are deleted or folders re-scanned.</summary>
public static class TreeOps
{
    /// <summary>Removes <paramref name="node"/> from its parent and updates every ancestor.</summary>
    public static void Remove(FileNode node)
    {
        var parent = node.Parent ?? throw new InvalidOperationException("The root cannot be removed.");
        var kids = parent.Children;
        int index = Array.IndexOf(kids, node);
        if (index < 0) return;

        var next = new FileNode[kids.Length - 1];
        Array.Copy(kids, 0, next, 0, index);
        Array.Copy(kids, index + 1, next, index, kids.Length - index - 1);
        parent.Children = next.Length == 0 ? FileNode.NoChildren : next;
        node.Parent = null;

        RefreshAncestors(parent);
    }

    /// <summary>
    /// Replaces <paramref name="old"/> with a freshly scanned <paramref name="fresh"/> (whose root name is a full path).
    /// Returns the node that now lives in the tree.
    /// </summary>
    public static FileNode Replace(FileNode old, FileNode fresh)
    {
        var parent = old.Parent;
        if (parent is null) return fresh; // a whole new tree

        fresh.Name = old.Name;
        fresh.Parent = parent;
        var kids = parent.Children;
        int index = Array.IndexOf(kids, old);
        if (index >= 0) kids[index] = fresh;
        old.Parent = null;

        RefreshAncestors(parent);
        return fresh;
    }

    static void RefreshAncestors(FileNode start)
    {
        // Folder totals are sums of their children, so recomputing each ancestor from its
        // (already correct) children is exact, cheap and keeps the size ordering intact.
        for (var a = start; a is not null; a = a.Parent)
        {
            long ownTime = a.LastWriteUtcTicks;
            Scanner.AggregateFolder(a);
            if (a.Children.Length == 0) a.LastWriteUtcTicks = ownTime;
        }
    }

    /// <summary>Finds the node with the given full path below (or at) <paramref name="root"/>.</summary>
    public static FileNode? FindByPath(FileNode root, string fullPath)
    {
        string rootPath = root.Name;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(fullPath.TrimEnd('/', '\\'), rootPath.TrimEnd('/', '\\'), cmp))
            return root;
        if (!fullPath.StartsWith(rootPath, cmp))
            return null;

        var rest = fullPath.AsSpan(rootPath.Length);
        var node = root;
        foreach (var range in rest.SplitAny('/', '\\'))
        {
            var part = rest[range];
            if (part.IsEmpty) continue;
            node = node.FindChild(part.ToString());
            if (node is null) return null;
        }
        return node;
    }
}
