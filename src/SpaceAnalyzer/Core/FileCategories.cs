namespace SpaceAnalyzer.Core;

public enum FileCategory : byte
{
    Other,
    Video,
    Audio,
    Image,
    Document,
    Archive,
    Code,
    Program,
    Data,
}

public static class FileCategories
{
    public const int Count = 9;

    static readonly Dictionary<string, FileCategory> s_map = Build();
    static readonly Dictionary<string, FileCategory>.AlternateLookup<ReadOnlySpan<char>> s_lookup =
        s_map.GetAlternateLookup<ReadOnlySpan<char>>();

    static Dictionary<string, FileCategory> Build()
    {
        var d = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, string list)
        {
            foreach (var e in list.Split(' ', StringSplitOptions.RemoveEmptyEntries)) d[e] = c;
        }

        Add(FileCategory.Video, "mp4 m4v mov mkv avi wmv flv webm mpg mpeg m2ts mts ts 3gp vob ogv rm rmvb asf divx f4v prproj braw r3d");
        Add(FileCategory.Audio, "mp3 wav flac aac m4a ogg oga opus wma aiff aif alac ape mid midi amr caf m4b m4p logicx band");
        Add(FileCategory.Image, "jpg jpeg jpe png gif bmp tif tiff webp heic heif avif cr2 cr3 nef arw dng orf rw2 raf psd psb ai eps svg ico icns tga xcf jxl exr hdr kra afphoto sketch fig car");
        Add(FileCategory.Document, "pdf doc docx dot dotx xls xlsx xlsm xlsb ppt pptx pps ppsx txt rtf odt ods odp md markdown pages numbers key epub mobi azw azw3 csv tsv tex djvu xps oxps one msg eml vsd vsdx pub");
        Add(FileCategory.Archive, "zip rar 7z tar gz tgz bz2 tbz2 xz txz zst lz lz4 lzma cab iso img dmg vhd vhdx vmdk vdi qcow2 wim esd swm sparseimage xip asar xar cpio lzfse aar raw");
        Add(FileCategory.Code, "c h cpp cc cxx hpp hh hxx inl cs fs fsx vb java kt kts scala groovy go rs swift m mm py pyi rb php pl pm lua r dart js mjs cjs ts tsx jsx vue svelte astro html htm xhtml css scss sass less json jsonc xml xsd xsl yaml yml toml ini cfg conf properties sql sh bash zsh fish ps1 psm1 psd1 bat cmd gradle cmake mk make proto graphql gql ipynb sln slnx csproj fsproj vbproj vcxproj props targets xaml axaml razor cshtml asm s zig nim ex exs erl hs clj elm " +
            "strings stringsdict xcstrings plist swiftmodule swiftinterface swiftdoc swiftsourceinfo modulemap entitlements xcconfig pbxproj storyboard xib sdef applescript scpt");
        Add(FileCategory.Program, "exe dll sys msi msix msixbundle appx appxbundle so dylib a lib o obj bin jar war apk aab ipa deb rpm pkg mpkg com scr ocx drv efi elf wasm node pyd pdb nupkg snap flatpak appimage vsix crx xpi tbd nib metallib");
        Add(FileCategory.Data, "db sqlite sqlite3 db3 mdb accdb ldf mdf ndf frm ibd myd log dat bak old tmp temp cache idx pack etl evtx dmp mdmp hprof blob pst ost vmem vmsn vmss nvram swp swap npy npz parquet feather arrow h5 hdf5 pkl pickle pt pth onnx safetensors ckpt gguf ggml tflite mlmodel lance realm ldb sst edb chk jfm jrs regtrans-ms blf pak wal shm journal bdic mom");
        return d;
    }

    /// <summary>Classifies a file by its extension (plus a few well-known system files).</summary>
    public static FileCategory Classify(string fileName)
    {
        var span = fileName.AsSpan();
        int dot = span.LastIndexOf('.');
        if (dot <= 0 || dot == span.Length - 1)
            return FileCategory.Other;
        var ext = span[(dot + 1)..];
        if (ext.Length > 16)
            return FileCategory.Other;

        // Page file, hibernation file and friends are system data, not "programs".
        if (ext.Equals("sys", StringComparison.OrdinalIgnoreCase) &&
            (span.StartsWith("pagefile", StringComparison.OrdinalIgnoreCase) ||
             span.StartsWith("hiberfil", StringComparison.OrdinalIgnoreCase) ||
             span.StartsWith("swapfile", StringComparison.OrdinalIgnoreCase)))
            return FileCategory.Data;

        return s_lookup.TryGetValue(ext, out var c) ? c : FileCategory.Other;
    }
}
