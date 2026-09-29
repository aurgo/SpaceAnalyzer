using System.Globalization;
using System.Text;
using SpaceAnalyzer.Core;

namespace SpaceAnalyzer.UI;

/// <summary>
/// "Ask AI": a prompt with what takes up the most space in a folder, for the user to paste into whichever AI
/// chat they use so it can say what is safe to delete. The app sends it nowhere: it only goes to the clipboard.
/// </summary>
public static class AiPrompt
{
    /// <summary>Lines of the folder outline: enough to show the big picture, small enough to paste anywhere.</summary>
    public const int MaxTreeLines = 150;
    /// <summary>The largest files, listed again with their full paths so the answer can quote them.</summary>
    public const int LargestFiles = 20;
    const int MaxDepth = 8;

    /// <summary>Items listed per folder: all the big ones at the top, fewer below (similar files would crowd it).</summary>
    static int PerFolder(Entry folder) => folder.Depth < 0 ? 12 : 6;

    /// <param name="volume">The drive the item is on, to mention its free space; null if unknown.</param>
    public static string Build(FileNode node, VolumeInfo? volume)
    {
        bool es = Strings.Spanish;
        string T(string s, string e) => es ? s : e;
        var sb = new StringBuilder();
        string system = Strings.Os switch { OsKind.Windows => "Windows", OsKind.Mac => "macOS", _ => "Linux" };

        if (!node.IsDirectory)
        {
            sb.Append(T("¿Puedo borrar este archivo sin riesgo? Dime qué es, para qué sirve y, si conviene quitarlo, la mejor forma de hacerlo (desde el propio programa, con un comando de limpieza o desinstalando algo). Los datos son de SpaceAnalyzer.",
                        "Can I safely delete this file? Tell me what it is, what it's for and, if it's worth removing, the best way to do it (from the app itself, with a cleanup command or by uninstalling something). The data comes from SpaceAnalyzer.")).Append("\n\n");
            sb.Append(T("Sistema: ", "System: ")).Append(system).Append('\n');
            sb.Append(T("Archivo: ", "File: ")).Append(node.FullPath).Append('\n');
            sb.Append(T("Tamaño: ", "Size: ")).Append(Fmt.Size(node.Size)).Append('\n');
            sb.Append(T("Último cambio: ", "Last changed: ")).Append(Date(node.LastWriteUtcTicks)).Append('\n');
            sb.Append(T("Tipo: ", "Type: ")).Append(Strings.CategoryName(node.Category)).Append('\n');
            sb.Append(T("Hoy es ", "Today is ")).Append(Date(DateTime.UtcNow.Ticks)).Append(".\n");
            return sb.ToString();
        }

        char sep = Path.DirectorySeparatorChar;
        sb.Append(T("Estoy liberando espacio en disco. Te paso lo que más ocupa en una carpeta, sacado con SpaceAnalyzer, y quiero que me digas qué puedo borrar.",
                    "I'm freeing up disk space. Here is what takes up the most space in a folder, from SpaceAnalyzer, and I'd like you to tell me what I can delete.")).Append("\n\n");
        sb.Append(T("Sistema: ", "System: ")).Append(system).Append('\n');
        sb.Append(T("Carpeta: ", "Folder: ")).Append(node.FullPath).Append(" — ").Append(Fmt.Size(node.Size))
          .Append(" · ").Append(Strings.FilesCount(node.FileCount)).Append('\n');
        if (volume is not null)
            sb.Append(T("Disco: ", "Disk: ")).Append(Strings.FreeOf(Fmt.Size(volume.FreeSpace), Fmt.Size(volume.TotalSize))).Append('\n');
        sb.Append(T("Hoy es ", "Today is ")).Append(Date(DateTime.UtcNow.Ticks)).Append(".\n\n");

        sb.Append(T("""
            Sepáralo en tres grupos:
            1. Se puede borrar sin riesgo (cachés, temporales, instaladores ya usados, descargas olvidadas…).
            2. Conviene revisarlo antes: di qué comprobar.
            3. No hay que tocarlo, porque es del sistema o de un programa.
            Para cada cosa da la ruta completa, cuánto libera y por qué. Si hay una forma mejor que borrarlo a mano (desde el propio programa, con un comando de limpieza o desinstalándolo), dímela. Ordena cada grupo de más a menos espacio.
            Si algo depende de cómo lo uso (un juego, un proyecto, unas fotos), pregúntamelo en vez de suponerlo.
            """, """
            Sort it into three groups:
            1. Safe to delete (caches, temporary files, installers already used, forgotten downloads…).
            2. Worth checking first: say what to look at.
            3. Leave alone, because it belongs to the system or to an app.
            For each item give the full path, how much it frees and why. If there is a better way than deleting it by hand (from the app itself, a cleanup command, or uninstalling it), tell me. Order each group from most to least space.
            If something depends on how I use it (a game, a project, some photos), ask me instead of assuming.
            """)).Append("\n\n");

        sb.Append(T($"Contenido, de mayor a menor (tamaño · archivos · último cambio; las carpetas acaban en «{sep}»):",
                    $"Contents, largest first (size · files · last change; folders end in \"{sep}\"):")).Append('\n');
        var outline = Outline(node, sep);
        Print(sb, outline, 0);

        var files = MainView.LargestFiles(node, LargestFiles);
        if (files.Count > 0)
        {
            sb.Append('\n').Append(T("Los archivos más grandes:", "Largest files:")).Append('\n');
            foreach (var f in files)
                sb.Append(f.FullPath).Append(" — ").Append(Fmt.Size(f.Size)).Append(" · ").Append(Date(f.LastWriteUtcTicks))
                  .Append(" · ").Append(Strings.CategoryName(f.Category)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>One line of the outline: a file or folder, or a chain of folders that each hold just one thing.</summary>
    sealed class Entry(FileNode node, string label, int depth)
    {
        public readonly FileNode Node = node;
        public readonly string Label = label;
        public readonly int Depth = depth;
        public List<Entry>? Kids;
        public long RestSize;
        public int RestCount;
    }

    /// <summary>
    /// The biggest folders are opened first, whatever their depth, until the outline is <see cref="MaxTreeLines"/>
    /// long: the space goes to what matters, and every top-level item is listed.
    /// </summary>
    static Entry Outline(FileNode root, char sep)
    {
        long minSize = Math.Max(1, root.Size / 1000);
        var top = new Entry(root, "", -1);
        int lines = Expand(top, minSize, sep);
        var queue = new PriorityQueue<Entry, long>();
        Enqueue(queue, top);
        while (queue.TryDequeue(out var e, out _))
        {
            if (e.Depth + 1 >= MaxDepth) continue;
            var (shown, added) = Count(e, minSize);
            if (shown == 0 || lines + added > MaxTreeLines) continue; // a smaller folder may still fit
            lines += Expand(e, minSize, sep);
            Enqueue(queue, e);
        }
        return top;
    }

    static void Enqueue(PriorityQueue<Entry, long> queue, Entry e)
    {
        foreach (var k in e.Kids!)
            if (k.Node.IsDirectory && k.Node.Children.Length > 0) queue.Enqueue(k, -k.Node.Size);
    }

    /// <summary>Children of the folder that would be listed, and the lines they take with "+N more".</summary>
    static (int Shown, int Lines) Count(Entry e, long minSize)
    {
        int shown = 0, cap = PerFolder(e);
        foreach (var c in e.Node.Children)
            if (shown < cap && c.Size >= minSize) shown++;
        return (shown, shown + (shown < e.Node.Children.Length ? 1 : 0));
    }

    static int Expand(Entry e, long minSize, char sep)
    {
        e.Kids = [];
        int cap = PerFolder(e);
        foreach (var c in e.Node.Children) // largest first
        {
            if (e.Kids.Count < cap && c.Size >= minSize) e.Kids.Add(Make(c, sep, e.Depth + 1));
            else { e.RestCount++; e.RestSize += c.Size; }
        }
        return e.Kids.Count + (e.RestCount > 0 ? 1 : 0);
    }

    static Entry Make(FileNode node, char sep, int depth)
    {
        var label = new StringBuilder(node.Name);
        while (node.IsDirectory && node.Children.Length == 1)
        {
            node = node.Children[0];
            label.Append(sep).Append(node.Name);
        }
        if (node.IsDirectory) label.Append(sep);
        return new Entry(node, label.ToString(), depth);
    }

    static void Print(StringBuilder sb, Entry e, int indent)
    {
        foreach (var k in e.Kids!)
        {
            sb.Append(' ', indent * 2).Append(k.Label).Append(" — ").Append(Fmt.Size(k.Node.Size));
            if (k.Node.IsDirectory) sb.Append(" · ").Append(Strings.FilesCount(k.Node.FileCount));
            sb.Append(" · ").Append(Date(k.Node.LastWriteUtcTicks)).Append('\n');
            if (k.Kids is not null) Print(sb, k, indent + 1);
        }
        if (e.RestCount > 0)
            sb.Append(' ', indent * 2).Append('(').Append(Strings.MoreItems(e.RestCount)).Append(" · ").Append(Fmt.Size(e.RestSize)).Append(")\n");
    }

    /// <summary>ISO dates: short, and the same in every language.</summary>
    static string Date(long utcTicks) =>
        utcTicks > 0 ? new DateTime(utcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "?";
}
