namespace SpaceAnalyzer.Core;

/// <summary>
/// A made-up but realistic home folder, used for screenshots (<c>--demo</c>) and tests,
/// so that no real user data ever appears in them. Always the same for the same seed.
/// </summary>
public static class DemoTree
{
    public static readonly VolumeInfo Volume = new("/", "Macintosh HD", 994_662_584_320, 287_400_000_000, DriveType.Fixed);

    public static readonly VolumeInfo[] Volumes =
    [
        Volume,
        new("/Volumes/Backup", "Backup", 2_000_398_934_016, 1_412_000_000_000, DriveType.Fixed),
        new("/Volumes/SD Card", "SD Card", 127_865_454_592, 88_500_000_000, DriveType.Removable),
    ];

    const long KB = 1024, MB = KB * 1024, GB = MB * 1024;

    public static FileNode Build(int seed = 7)
    {
        var b = new Builder(seed);
        var root = b.Root("/Users/demo");

        var movies = b.Dir(root, "Movies");
        b.File(movies, "Vacaciones Islandia 2025.mov", 8_400 * MB, 290);
        b.File(movies, "Boda Ana y Luis.mp4", 5_100 * MB, 700);
        var fcp = b.Dir(movies, "Final Cut Projects");
        b.Files(fcp, "Clip", ".mov", 14, 180 * MB, 900 * MB, 120, 400);
        var render = b.Dir(fcp, "Render Files");
        b.Files(render, "render", ".mov", 22, 40 * MB, 260 * MB, 120, 400);
        var screen = b.Dir(movies, "Screen Recordings");
        b.Files(screen, "Grabación de pantalla", ".mov", 26, 30 * MB, 320 * MB, 5, 200);

        var pictures = b.Dir(root, "Pictures");
        var library = b.Dir(pictures, "Photos Library.photoslibrary");
        var originals = b.Dir(library, "originals");
        foreach (var hex in "0123456789ABCDEF")
            b.Files(b.Dir(originals, hex.ToString()), "IMG_", ".heic", 70, 2 * MB, 7 * MB, 30, 2200);
        b.Files(b.Dir(library, "resources"), "derivative", ".jpg", 300, 200 * KB, 1 * MB, 30, 900);
        b.File(library, "Photos.sqlite", 1_300 * MB, 3);
        var raw = b.Dir(pictures, "RAW");
        b.Files(raw, "DSC_", ".cr3", 90, 24 * MB, 58 * MB, 400, 1500);
        var design = b.Dir(pictures, "Diseño");
        b.Files(design, "Cartel", ".psd", 7, 120 * MB, 820 * MB, 60, 800);
        b.Files(design, "Logo", ".svg", 20, 40 * KB, 900 * KB, 60, 800);
        b.Files(design, "Maqueta", ".fig", 5, 30 * MB, 90 * MB, 20, 300);

        var music = b.Dir(root, "Music");
        var media = b.Dir(b.Dir(music, "Music"), "Media");
        foreach (var artist in new[] { "Rosalía", "Daft Punk", "Bad Bunny", "Radiohead", "Vetusta Morla", "Queen", "Nathy Peluso", "Björk" })
            b.Files(b.Dir(media, artist), "Pista", ".m4a", 24, 6 * MB, 14 * MB, 400, 3000);
        var logic = b.Dir(music, "Logic");
        b.File(logic, "Maqueta EP.logicx", 2_300 * MB, 45);
        b.Files(b.Dir(logic, "Samples"), "sample", ".wav", 40, 12 * MB, 80 * MB, 45, 600);

        var documents = b.Dir(root, "Documents");
        b.Files(b.Dir(documents, "Facturas"), "Factura", ".pdf", 120, 120 * KB, 2 * MB, 10, 1400);
        var work = b.Dir(documents, "Trabajo");
        b.Files(work, "Informe", ".docx", 30, 200 * KB, 12 * MB, 5, 600);
        b.Files(work, "Presupuesto", ".xlsx", 25, 100 * KB, 30 * MB, 5, 600);
        b.Files(work, "Presentación", ".pptx", 12, 20 * MB, 240 * MB, 5, 600);
        b.Files(work, "Keynote", ".key", 6, 60 * MB, 400 * MB, 5, 600);
        b.Files(b.Dir(documents, "Libros"), "Libro", ".epub", 60, 1 * MB, 30 * MB, 200, 2500);
        b.Files(b.Dir(documents, "Escaneos"), "Escaneo", ".pdf", 80, 1 * MB, 9 * MB, 300, 2000);

        var downloads = b.Dir(root, "Downloads");
        b.File(downloads, "Xcode_16.xip", 7_300 * MB, 180);
        b.File(downloads, "ubuntu-24.04-desktop-arm64.iso", 3_100 * MB, 240);
        b.File(downloads, "Docker.dmg", 910 * MB, 90);
        b.File(downloads, "setup-juego.exe", 1_400 * MB, 400);
        b.Files(downloads, "archivo", ".zip", 30, 5 * MB, 400 * MB, 1, 900);
        b.Files(downloads, "documento", ".pdf", 40, 200 * KB, 20 * MB, 1, 900);
        b.Files(downloads, "imagen", ".png", 50, 100 * KB, 8 * MB, 1, 900);

        var dev = b.Dir(root, "Developer");
        var web = b.Dir(dev, "tienda-web");
        var nodeModules = b.Dir(web, "node_modules");
        foreach (var pkg in new[] { "react", "next", "typescript", "@babel", "webpack", "eslint", "lodash", "@swc", "esbuild", "prettier", "sharp", "rxjs" })
        {
            var p = b.Dir(nodeModules, pkg);
            b.Files(p, "index", ".js", 60, 2 * KB, 900 * KB, 20, 200);
            b.Files(p, "index", ".map", 20, 20 * KB, 2 * MB, 20, 200);
            b.Files(p, "package", ".json", 10, 1 * KB, 60 * KB, 20, 200);
            if (pkg is "@swc" or "esbuild" or "sharp") b.File(p, pkg.TrimStart('@') + ".node", 40 * MB + b.Next(80 * MB), 20);
        }
        b.File(b.Dir(b.Dir(b.Dir(web, ".git"), "objects"), "pack"), "pack-3f9a.pack", 820 * MB, 2);
        b.Files(b.Dir(web, "src"), "componente", ".tsx", 180, 2 * KB, 40 * KB, 0, 30);
        var ml = b.Dir(dev, "ml-lab");
        b.File(b.Dir(ml, "models"), "llama-3-8b-instruct.Q4_K_M.gguf", 4_900 * MB, 60);
        b.File(b.Dir(ml, "models"), "whisper-large-v3.safetensors", 3_100 * MB, 90);
        b.Files(b.Dir(ml, "datasets"), "train", ".parquet", 8, 300 * MB, 1_800 * MB, 60, 200);
        b.Files(ml, "experimento", ".ipynb", 25, 100 * KB, 12 * MB, 2, 120);
        var app = b.Dir(dev, "spaceanalyzer");
        b.Files(b.Dir(app, "src"), "Source", ".cs", 45, 2 * KB, 40 * KB, 0, 3);
        b.Files(b.Dir(b.Dir(app, "bin"), "Release"), "lib", ".dll", 30, 50 * KB, 30 * MB, 0, 3);
        b.Files(b.Dir(dev, "juego-unity"), "asset", ".asset", 90, 1 * MB, 60 * MB, 100, 500);

        var lib = b.Dir(root, "Library");
        var caches = b.Dir(lib, "Caches");
        foreach (var name in new[] { "com.apple.Safari", "Google", "com.spotify.client", "Homebrew", "pip", "Yarn", "JetBrains" })
            b.Files(b.Dir(caches, name), "cache", ".db", 50, 100 * KB, 90 * MB, 0, 60);
        var support = b.Dir(lib, "Application Support");
        var steam = b.Dir(b.Dir(support, "Steam"), "steamapps");
        b.Files(b.Dir(steam, "Baldur's Gate 3"), "Game", ".pak", 9, 900 * MB, 6_000 * MB, 300, 500);
        b.Files(b.Dir(support, "Slack"), "IndexedDB", ".ldb", 80, 1 * MB, 20 * MB, 0, 30);
        b.Files(b.Dir(support, "Code"), "workspaceStorage", ".json", 200, 1 * KB, 3 * MB, 0, 90);
        b.File(b.Dir(b.Dir(lib, "Containers"), "com.docker.docker"), "Docker.raw", 21_000 * MB, 1);
        b.Files(b.Dir(lib, "Mail"), "mensaje", ".emlx", 900, 5 * KB, 2 * MB, 10, 3000);
        b.File(b.Dir(lib, "Logs"), "system.log", 460 * MB, 0);

        var trash = b.Dir(root, ".Trash");
        b.Files(trash, "borrado", ".mp4", 3, 200 * MB, 900 * MB, 30, 60);

        return b.Finish(root);
    }

    sealed class Builder(int seed)
    {
        readonly Random _rng = new(seed);
        readonly Dictionary<FileNode, List<FileNode>> _kids = new();
        readonly long _now = DateTime.UtcNow.Ticks;

        public long Next(long max) => (long)(_rng.NextDouble() * max);

        public FileNode Root(string path)
        {
            var r = new FileNode(path, NodeKind.Directory, null);
            _kids[r] = [];
            return r;
        }

        public FileNode Dir(FileNode parent, string name)
        {
            foreach (var existing in _kids[parent])
                if (existing.IsDirectory && existing.Name == name) return existing;
            var d = new FileNode(name, NodeKind.Directory, parent);
            _kids[parent].Add(d);
            _kids[d] = [];
            return d;
        }

        public void File(FileNode parent, string name, long size, double ageDays)
        {
            _kids[parent].Add(new FileNode(name, NodeKind.File, parent)
            {
                Size = size,
                Category = FileCategories.Classify(name),
                LastWriteUtcTicks = _now - (long)(ageDays * TimeSpan.TicksPerDay),
            });
        }

        /// <summary>Several files with sizes skewed towards the small end, like real folders.</summary>
        public void Files(FileNode parent, string stem, string ext, int count, long min, long max, double minAge, double maxAge)
        {
            for (int i = 1; i <= count; i++)
            {
                double t = _rng.NextDouble();
                long size = min + (long)((max - min) * t * t * t);
                double age = minAge + (maxAge - minAge) * _rng.NextDouble();
                File(parent, stem.EndsWith('_') ? $"{stem}{i:0000}{ext}" : $"{stem} {i:000}{ext}", size, age);
            }
        }

        public FileNode Finish(FileNode root)
        {
            foreach (var (dir, list) in _kids)
                dir.Children = list.Count == 0 ? FileNode.NoChildren : [.. list];
            Scanner.Aggregate(root);
            return root;
        }
    }
}
