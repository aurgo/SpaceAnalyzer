**SpaceAnalyzer** es un visualizador de espacio en disco moderno, inspirado en el clásico SpaceMonger. Todo cabe en **un único archivo**: sin instalador, sin frameworks de interfaz y dibujado entero en C#.

🌐 **Página:** https://aurgo.github.io/SpaceAnalyzer/ · [English](https://aurgo.github.io/SpaceAnalyzer/en/)

![SpaceAnalyzer](https://raw.githubusercontent.com/aurgo/SpaceAnalyzer/main/docs/screenshot.png)

## Descargas

| Sistema | Archivo | Tamaño | Requisitos |
|---|---|---|---|
| **Windows 10/11** (x64) | `SpaceAnalyzer-windows-x64.exe` | ~2 MB | Ninguno |
| Windows 10/11 (ARM64) | `SpaceAnalyzer-windows-arm64.exe` | ~2 MB | Ninguno |
| Windows 10/11 (x64), *mini* | `SpaceAnalyzer-windows-x64-mini.exe` | ~530 KB | [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **macOS 12+** (Apple Silicon e Intel) | `SpaceAnalyzer-macos.zip` | ~2,5 MB | Ninguno |
| **Linux** (x64 / ARM64, X11 o XWayland) | `SpaceAnalyzer-linux-x64.tar.gz` / `-linux-arm64.tar.gz` | ~1,5 MB | Ninguno |
| Todas, *mini* | `SpaceAnalyzer-mini-dotnet10.zip` | ~1,5 MB (~500 KB cada una) | .NET 10 Runtime |

`SHA256SUMS.txt` contiene las sumas de comprobación de todos los archivos.

### Primer arranque

Los ejecutables no están firmados con un certificado de pago, así que el sistema avisa la primera vez:

- **Windows**: si aparece *"Windows protegió su PC"*, pulsa **Más información → Ejecutar de todas formas**.
- **macOS**: descomprime el `.zip` y arrastra `SpaceAnalyzer.app` a Aplicaciones. Si macOS no lo deja abrir, ve a **Ajustes del Sistema → Privacidad y seguridad → Abrir igualmente**. También puedes ejecutar en Terminal `xattr -dr com.apple.quarantine /Applications/SpaceAnalyzer.app`.
- **Linux**: `tar -xzf SpaceAnalyzer-linux-x64.tar.gz && ./SpaceAnalyzer`

## Qué incluye

- **Treemap anidado**, como SpaceMonger, con el algoritmo *squarified*: cada carpeta es un bloque con su nombre y su tamaño.
- **Zoom animado** con doble clic, rueda o pellizco. Tiene migas de pan y los botones atrás, adelante y subir.
- **Tres formas de colorear**: por tipo de archivo, por nivel de carpeta (el aspecto clásico) o por antigüedad.
- **Panel lateral** con los detalles, el desglose por tipo (haz clic para resaltar) y los archivos más grandes.
- **Búsqueda**: empieza a escribir y verás las coincidencias en el mapa, cuántas hay y cuánto ocupan.
- **Acciones**: abrir, mostrar en Finder o en el Explorador, copiar la ruta, enviar a la papelera y volver a escanear una carpeta.
- **Espacio libre** de la unidad, tema claro u oscuro, español e inglés.
- **Escaneo multihilo**. No cuenta dos veces los enlaces simbólicos ni los otros volúmenes, y usa el tamaño real en disco de los archivos dispersos.
- **Buscar actualizaciones** desde «Acerca de». Es la única vez que se conecta a Internet, y solo cuando pulsas el botón.

---

**English:** a modern, single-file disk space visualizer inspired by the classic SpaceMonger. Download the file for your system above; there is nothing to install. The *mini* builds are ~500 KB but need the .NET 10 runtime. Windows and macOS will warn the first time, because the binaries aren't signed with a paid certificate (see "Primer arranque").
