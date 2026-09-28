using SpaceAnalyzer.Core;

namespace SpaceAnalyzer;

/// <summary>User-visible text in Spanish and English (chosen from the OS language, switchable at runtime).</summary>
public static class Strings
{
    public static bool Spanish = true;
    public static OsKind Os = OperatingSystem.IsWindows() ? OsKind.Windows : OperatingSystem.IsMacOS() ? OsKind.Mac : OsKind.Linux;
    static bool s_mac => Os == OsKind.Mac;
    static bool s_win => Os == OsKind.Windows;

    static string T(string es, string en) => Spanish ? es : en;

    public static string AppName => "SpaceAnalyzer";
    public static string Tagline => T("Descubre qué ocupa espacio en tu disco", "See what is taking up space on your disk");
    public static string DrivesTitle => T("UNIDADES", "DRIVES");
    public static string ChooseFolder => T("Elegir carpeta…", "Choose folder…");
    public static string ChooseFolderTitle => T("Elige la carpeta que quieres analizar", "Choose the folder to analyze");
    public static string Choose => T("Analizar", "Analyze");
    public static string HomeFolder => T("Carpeta personal", "Home folder");
    public static string DropHint => T("También puedes arrastrar una carpeta a esta ventana", "You can also drop a folder onto this window");
    public static string FreeOf(string free, string total) => T($"{free} libres de {total}", $"{free} free of {total}");
    public static string LocalDisk => T("Disco local", "Local Disk");
    public static string RemovableDisk => T("Disco extraíble", "Removable Disk");
    public static string NetworkDrive => T("Unidad de red", "Network Drive");
    public static string StartupDisk => "Macintosh HD";
    public static string NoDrives => T("No se encontraron unidades", "No drives found");

    public static string Open => T("Abrir", "Open");
    public static string OpenTip => T("Analizar una unidad o carpeta", "Analyze a drive or folder");
    public static string Home => T("Inicio", "Home");
    public static string Rescan => T("Volver a escanear", "Rescan");
    public static string Back => T("Atrás", "Back");
    public static string Forward => T("Adelante", "Forward");
    public static string Up => T("Subir un nivel", "Up one level");
    public static string ZoomIn => T("Acercar", "Zoom in");
    public static string ZoomOut => T("Alejar", "Zoom out");
    public static string Search => T("Buscar", "Search");
    public static string SearchTip => T("Resaltar archivos por nombre", "Highlight files by name");
    public static string MoreOptions => T("Más opciones", "More options");
    public static string Sidebar => T("Panel lateral", "Sidebar");
    public static string ColorType => T("Tipo", "Type");
    public static string ColorDepth => T("Nivel", "Depth");
    public static string ColorAge => T("Edad", "Age");
    public static string ColorTypeLong => T("Colorear por tipo de archivo", "Color by file type");
    public static string ColorDepthLong => T("Colorear por nivel de carpeta (clásico)", "Color by folder depth (classic)");
    public static string ColorAgeLong => T("Colorear por antigüedad", "Color by age");

    public static string Scanning => T("Escaneando", "Scanning");
    public static string Cancel => T("Cancelar", "Cancel");
    public static string FilesLower => T("archivos", "files");
    public static string FoldersLower => T("carpetas", "folders");
    public static string FilesTitle => T("Archivos", "Files");
    public static string FoldersTitle => T("Carpetas", "Folders");
    public static string SizeTitle => T("Tamaño", "Size");
    public static string Elapsed => T("Tiempo", "Elapsed");

    public static string Selection => T("SELECCIÓN", "SELECTION");
    public static string CurrentFolder => T("CARPETA ACTUAL", "CURRENT FOLDER");
    public static string OfTotal => T("del total", "of total");
    public static string OfView => T("de la vista", "of this view");
    public static string Modified => T("Modificado", "Modified");
    public static string Kind => T("Tipo", "Kind");
    public static string Folder => T("Carpeta", "Folder");
    public static string Contents => T("Contenido", "Contents");

    public static string OpenItem => T("Abrir", "Open");
    public static string Reveal => s_mac ? T("Mostrar en Finder", "Show in Finder") : s_win ? T("Mostrar en el Explorador", "Show in Explorer") : T("Mostrar en la carpeta", "Show in folder");
    public static string RevealShort => T("Mostrar", "Reveal");
    public static string CopyPath => T("Copiar ruta", "Copy path");
    public static string CopyShort => T("Copiar", "Copy");
    public static string Trash => !s_win ? T("Mover a la Papelera", "Move to Trash") : T("Enviar a la papelera", "Move to Recycle Bin");
    public static string TrashShort => T("Papelera", s_win ? "Recycle" : "Trash");
    public static string Properties => T("Propiedades", "Properties");
    public static string RescanFolder => T("Volver a escanear esta carpeta", "Rescan this folder");

    public static string FileTypes => T("TIPOS DE ARCHIVO", "FILE TYPES");
    public static string Largest => T("LOS MÁS GRANDES", "LARGEST FILES");
    public static string Levels => T("NIVELES", "LEVELS");
    public static string AgeTitle => T("ANTIGÜEDAD", "AGE");
    public static string FreeSpace => T("Espacio libre", "Free space");
    public static string SmallItems(long n) => T($"{Fmt.Count(n)} elementos pequeños", $"{Fmt.Count(n)} small items");
    public static string MoreItems(long n) => T($"+{Fmt.Count(n)} más", $"+{Fmt.Count(n)} more");
    public static string ShowFreeSpace => T("Mostrar espacio libre", "Show free space");
    public static string DetailLow => T("Detalle bajo", "Low detail");
    public static string DetailNormal => T("Detalle normal", "Normal detail");
    public static string DetailHigh => T("Detalle alto", "High detail");
    public static string ThemeSystem => T("Tema del sistema", "System theme");
    public static string ThemeDark => T("Tema oscuro", "Dark theme");
    public static string ThemeLight => T("Tema claro", "Light theme");
    public static string About => T("Acerca de SpaceAnalyzer", "About SpaceAnalyzer");
    public static string Close => T("Cerrar", "Close");
    public static string Level(int n) => T($"Nivel {n}", $"Level {n}");

    public static string ConfirmTrashTitle => !s_win ? T("¿Mover a la Papelera?", "Move to Trash?") : T("¿Enviar a la papelera?", "Move to the Recycle Bin?");
    public static string TrashFailed => !s_win ? T("No se pudo mover a la Papelera", "Could not move to the Trash") : T("No se pudo enviar a la papelera", "Could not move to the Recycle Bin");
    public static string Trashed(string name) => !s_win ? T($"«{name}» se movió a la Papelera", $"“{name}” moved to the Trash") : T($"«{name}» se envió a la papelera", $"“{name}” moved to the Recycle Bin");
    public static string Copied => T("Ruta copiada", "Path copied");
    public static string EmptyFolder => T("Esta carpeta está vacía", "This folder is empty");
    public static string Unreadable(long n) => T($"{Fmt.Count(n)} carpetas sin acceso", $"{Fmt.Count(n)} unreadable folders");
    public static string ScannedIn(string t) => T($"escaneado en {t}", $"scanned in {t}");
    public static string ScanFailed(string why) => T($"No se pudo escanear: {why}", $"Scan failed: {why}");
    public static string OpenFailed => T("No se pudo abrir", "Could not open");
    public static string FreeLabel => T("Libre", "Free");
    public static string Items(long n) => T($"{Fmt.Count(n)} elementos", $"{Fmt.Count(n)} items");
    public static string FilesCount(long n) => $"{Fmt.Count(n)} {(n == 1 ? T("archivo", "file") : FilesLower)}";
    public static string FoldersCount(long n) => $"{Fmt.Count(n)} {(n == 1 ? T("carpeta", "folder") : FoldersLower)}";
    public static string Version(string v) => T($"Versión {v}", $"Version {v}");
    public static string AboutLine1 => T("Visualizador de espacio en disco inspirado en el clásico SpaceMonger.", "Disk space visualizer inspired by the classic SpaceMonger.");
    public static string AboutLine2 => T("Un único archivo, sin instalación y sin dependencias de interfaz.", "One single file: no installer, no UI framework.");
    public static string AboutLine3 => T("Doble clic para entrar en una carpeta · rueda o Retroceso para salir", "Double-click to zoom into a folder · wheel or Backspace to zoom out");
    public static string Language => T("Idioma", "Language");

    public static string CategoryName(FileCategory c) => c switch
    {
        FileCategory.Video => T("Vídeo", "Video"),
        FileCategory.Audio => "Audio",
        FileCategory.Image => T("Imágenes", "Images"),
        FileCategory.Document => T("Documentos", "Documents"),
        FileCategory.Archive => T("Comprimidos y discos", "Archives & disk images"),
        FileCategory.Code => T("Código", "Code"),
        FileCategory.Program => T("Programas", "Programs"),
        FileCategory.Data => T("Sistema y datos", "System & data"),
        _ => T("Otros", "Other"),
    };

    public static readonly string[] AgeLabelsEs = ["hoy", "1 sem", "1 mes", "6 m", "1 año", "2 a", "5 a+"];
    public static readonly string[] AgeLabelsEn = ["today", "1 wk", "1 mo", "6 mo", "1 yr", "2 yr", "5 yr+"];
    public static string[] AgeLabels => Spanish ? AgeLabelsEs : AgeLabelsEn;

    // Keyboard hints shown in tooltips
    public static string Cmd => s_mac ? "⌘" : "Ctrl+";
    public static string BackKeys => s_mac ? "⌘[" : "Alt+←";
    public static string ForwardKeys => s_mac ? "⌘]" : "Alt+→";
    public static string UpKeys => s_mac ? "⌘↑" : T("Retroceso", "Backspace");
    public static string WithKeys(string text, string keys) => $"{text}  ({keys})";
}

public enum OsKind : byte { Windows, Mac, Linux }
