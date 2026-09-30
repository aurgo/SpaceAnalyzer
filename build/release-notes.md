**SpaceAnalyzer** is a modern disk space visualizer inspired by the classic SpaceMonger, in **one single file**: no installer, no UI framework, drawn entirely in C#.

🌐 **Website:** https://aurgo.github.io/SpaceAnalyzer/en/ · [Español](https://aurgo.github.io/SpaceAnalyzer/)

![SpaceAnalyzer](https://raw.githubusercontent.com/aurgo/SpaceAnalyzer/main/docs/screenshot.png)

## What's new

- **Updates itself.** At start, at most once a day, it asks GitHub for the latest version. When there is a newer one it downloads it, checks it against the release's `SHA256SUMS.txt` and puts it in place of the copy you run. A **Restart to use…** button opens it right away; otherwise the new version is used the next time you open the app.
- Turn it off with **Update automatically** in the ⋯ menu (in the app menu on macOS); *Update to…* in About then does it when you ask.
- Where it can't replace itself (a folder you can't write to, or the macOS and Linux *mini* builds) it shows a **New version** button that opens the download page instead.
- Updating is the only thing the app goes online for; nothing about your files is ever sent.

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

- **Se actualiza sola.** Al abrirse, como mucho una vez al día, le pregunta a GitHub cuál es la última versión. Si hay una nueva, la descarga, la comprueba con el `SHA256SUMS.txt` de la versión y la pone en lugar de la que usas. El botón **Reiniciar para usar…** la abre al momento; si no, se usa la próxima vez que abras la app.
- Se desactiva con **Actualizar automáticamente** en el menú ⋯ (en el menú de la aplicación en macOS); entonces *Actualizar a…* en «Acerca de» lo hace cuando se lo pidas.
- Si no puede sustituirse (una carpeta sin permiso de escritura, o las versiones *mini* de macOS y Linux), muestra un botón **Nueva versión** que abre la página de descarga.
- Actualizarse es lo único para lo que la app se conecta a Internet; nunca envía nada de tus archivos.

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
- **Se actualiza sola** al abrirse, como mucho una vez al día (se desactiva en el menú ⋯). Solo se conecta a Internet para eso.

</details>
