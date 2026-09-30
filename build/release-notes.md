**SpaceAnalyzer** is a modern disk space visualizer inspired by the classic SpaceMonger, in **one single file**: no installer, no UI framework, drawn entirely in C#.

🌐 **Website:** https://aurgo.github.io/SpaceAnalyzer/en/ · [Español](https://aurgo.github.io/SpaceAnalyzer/)

![SpaceAnalyzer](https://raw.githubusercontent.com/aurgo/SpaceAnalyzer/main/docs/screenshot.png)

## What's new

- **Tells you about new versions.** At start, at most once a day, it asks GitHub for the latest version; when there is a newer one, a **New version** button appears in the toolbar (and on the welcome screen) that opens its download page. Nothing is shown when you are up to date or offline.
- It can be turned off with **Check for updates at start** in the ⋯ menu (in the app menu on macOS). *Check for updates* in About still works as before.
- Asking GitHub for the latest version is the only thing the app goes online for; nothing about your files is ever sent.

## Downloads

| System | File | Size | Requires |
|---|---|---|---|
| **Windows 10/11** (x64) | `SpaceAnalyzer-windows-x64.exe` | ~2 MB | Nothing |
| Windows 10/11 (ARM64) | `SpaceAnalyzer-windows-arm64.exe` | ~2 MB | Nothing |
| Windows 10/11 (x64), *mini* | `SpaceAnalyzer-windows-x64-mini.exe` | ~530 KB | [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **macOS 12+** (Apple Silicon and Intel) | `SpaceAnalyzer-macos.zip` | ~2.5 MB | Nothing |
| **Linux** (x64 / ARM64, X11 or XWayland) | `SpaceAnalyzer-linux-x64.tar.gz` / `-linux-arm64.tar.gz` | ~1.5 MB | Nothing |
| All, *mini* | `SpaceAnalyzer-mini-dotnet10.zip` | ~1.5 MB (~500 KB each) | .NET 10 Runtime |

`SHA256SUMS.txt` has the checksums of every file.

### First start

The executables are not signed with a paid certificate, so the system warns the first time:

- **Windows**: if *"Windows protected your PC"* appears, click **More info → Run anyway**.
- **macOS**: unzip and drag `SpaceAnalyzer.app` to Applications. If macOS won't open it, go to **System Settings → Privacy & Security → Open Anyway**, or run `xattr -dr com.apple.quarantine /Applications/SpaceAnalyzer.app` in Terminal.
- **Linux**: `tar -xzf SpaceAnalyzer-linux-x64.tar.gz && ./SpaceAnalyzer`

---

<details>
<summary>🇪🇸 Notas en español</summary>

### Novedades

- **Avisa de las versiones nuevas.** Al abrirse, como mucho una vez al día, le pregunta a GitHub cuál es la última versión; si hay una nueva, aparece un botón **Nueva versión** en la barra (y en la pantalla de inicio) que abre su página de descarga. Si ya tienes la última o no hay conexión, no muestra nada.
- Se desactiva con **Buscar actualizaciones al abrir** en el menú ⋯ (en el menú de la aplicación en macOS). *Buscar actualizaciones* en «Acerca de» sigue funcionando igual.
- Preguntar a GitHub por la última versión es lo único para lo que la app se conecta a Internet; nunca envía nada de tus archivos.

### Descargas

| Sistema | Archivo | Tamaño | Requisitos |
|---|---|---|---|
| **Windows 10/11** (x64) | `SpaceAnalyzer-windows-x64.exe` | ~2 MB | Ninguno |
| Windows 10/11 (ARM64) | `SpaceAnalyzer-windows-arm64.exe` | ~2 MB | Ninguno |
| Windows 10/11 (x64), *mini* | `SpaceAnalyzer-windows-x64-mini.exe` | ~530 KB | [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **macOS 12+** (Apple Silicon e Intel) | `SpaceAnalyzer-macos.zip` | ~2,5 MB | Ninguno |
| **Linux** (x64 / ARM64, X11 o XWayland) | `SpaceAnalyzer-linux-x64.tar.gz` / `-linux-arm64.tar.gz` | ~1,5 MB | Ninguno |
| Todas, *mini* | `SpaceAnalyzer-mini-dotnet10.zip` | ~1,5 MB (~500 KB cada una) | .NET 10 Runtime |

`SHA256SUMS.txt` contiene las sumas de comprobación de todos los archivos.

#### Primer arranque

Los ejecutables no están firmados con un certificado de pago, así que el sistema avisa la primera vez:

- **Windows**: si aparece *"Windows protegió su PC"*, pulsa **Más información → Ejecutar de todas formas**.
- **macOS**: descomprime el `.zip` y arrastra `SpaceAnalyzer.app` a Aplicaciones. Si macOS no lo deja abrir, ve a **Ajustes del Sistema → Privacidad y seguridad → Abrir igualmente**. También puedes ejecutar en Terminal `xattr -dr com.apple.quarantine /Applications/SpaceAnalyzer.app`.
- **Linux**: `tar -xzf SpaceAnalyzer-linux-x64.tar.gz && ./SpaceAnalyzer`

### Qué incluye

- **Treemap anidado**, como SpaceMonger, con el algoritmo *squarified*: cada carpeta es un bloque con su nombre y su tamaño.
- **Zoom animado** con doble clic, rueda o pellizco. Tiene migas de pan y los botones atrás, adelante y subir.
- **Tres formas de colorear**: por tipo de archivo, por nivel de carpeta (el aspecto clásico) o por antigüedad.
- **Panel lateral** con los detalles, el desglose por tipo (haz clic para resaltar) y los archivos más grandes.
- **Búsqueda**: empieza a escribir y verás las coincidencias en el mapa, cuántas hay y cuánto ocupan.
- **Acciones**: abrir, mostrar en Finder o en el Explorador, copiar la ruta, enviar a la papelera y volver a escanear una carpeta.
- **Espacio libre** de la unidad, tema claro u oscuro, español e inglés.
- **Escaneo multihilo**. No cuenta dos veces los enlaces simbólicos ni los otros volúmenes, y usa el tamaño real en disco de los archivos dispersos.
- **Preguntar a la IA qué borrar**: copia un prompt con lo que más ocupa para pegarlo en el chat de tu IA.
- **Aviso de versiones nuevas** al abrirse, como mucho una vez al día (se desactiva en el menú ⋯), y **Buscar actualizaciones** en «Acerca de». Solo se conecta a Internet para eso.

</details>
