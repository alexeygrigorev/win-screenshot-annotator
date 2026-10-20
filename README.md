# Screenshot Annotator for Windows

Capture a region, draw on it immediately, then copy or save a PNG. C# / Windows Forms with GDI capture and a Win32 global hotkey. No Electron or browser runtime. Windows is the supported platform.

## Install on Tabo / Surface

1. Open [Releases](https://github.com/alexeygrigorev/win-screenshot-annotator/releases/latest).
2. Download **ScreenshotAnnotator-ARM64.zip** for ARM Windows (Tabo / Snapdragon Surface), or **ScreenshotAnnotator-AMD64.zip** for Intel/AMD Windows. Check **Settings → System → About → System type** if unsure.
3. Extract the **whole ZIP** into a folder such as `%LOCALAPPDATA%\ScreenshotAnnotator`. Keep its files together.
4. Run `WinScreenshotAnnotator.exe`. A region selector opens immediately. Drag over the screen; releasing opens the editor. Escape cancels capture. The app stays in the notification area after closing an editor.

Requires Windows 11 (or Windows 10 22H2 with applicable .NET support). The .NET 10 runtime is included; no SDK, admin rights, or installer required. Release binaries are unsigned; Windows may show a SmartScreen reputation prompt. Verify the downloaded ZIP against the release's `SHA256SUMS.txt` with `Get-FileHash` before choosing to run it.

For optional startup, press Win+R, enter `shell:startup`, and add a shortcut to the EXE with `--tray` in its target. `--tray` starts quietly without capturing. To uninstall, exit from the tray menu and remove the extracted folder and any startup shortcut.

## Controls

| Hotkey | Action |
| --- | --- |
| **Ctrl+Alt+A** | Global: select a screenshot region |
| **Ctrl+Alt+F** | Global: capture the full virtual desktop, across monitors |
| P / L / A / R / E / T | Editor: pen / line / arrow / rectangle / ellipse / text |
| Shift while dragging | Snap line or arrow to the nearest 45° direction |
| Ctrl+Z / Ctrl+Y | Undo / redo annotations, text edits, deletion, and Clear |
| Ctrl+C | Copy flattened image; while typing, copy selected text |
| Ctrl+S | Save PNG, including any caption being edited |
| Enter | Newline in a caption |
| Ctrl+Enter / Escape while typing | Commit caption and keep editor open |
| Escape in editor | Close; unsaved work offers Keep editing, Save PNG, or Discard |
| Escape during close confirmation | Keep editing |

The toolbar provides six tools, six ink colours, four weights, undo, redo, Clear, Copy, and Save PNG. Click an existing caption with the text tool to edit it; empty it to delete it. Text size follows weight, with a readable minimum. Shape clicks without dragging add nothing; a pen click adds a dot. Rectangles and ellipses are unconstrained. PNG dimensions match the original capture, even when the preview scales down.

The notification-area menu also offers region capture, desktop capture, opening an image, pasting an image, and Exit. Double-click its icon to capture a region. Hotkeys are fixed defaults; a registration conflict shows a message and tray commands remain available. Only one app instance runs per Windows session.

## Behavior and implementation

Ports the annotation behavior documented in PocketShell Desktop `docs/COMPOSER.md` §27, `docs/SHORTCUTS.md` §1.5, and its shared `DoodleCanvas.vue` / `doodleGeometry.ts`. This is a standalone implementation, with no PocketShell dependency. Attachment staging and composer integration are outside this app's scope.

Annotations are vector items with version history, rather than per-stroke screenshots. Preview and export use the same painter. Export flushes the open caption before flattening; saving clears the unsaved flag, while copying keeps it. Close confirmation requires an explicit Discard click. There is no cloud upload, telemetry, or background network activity in the app. Captures and annotations live in memory until saved or copied; drafts are not persisted after exit.

Capture includes the Windows virtual desktop with physical pixels and supports negative monitor coordinates. Our editor windows are hidden briefly before capture. Protected video, secure desktops, and elevated/protected content can appear blank. Multi-monitor layouts with mixed display scaling should be checked on the target hardware.

## Build and test

Install the .NET 10 SDK, then:

```powershell
dotnet run --project tests/Annotator.Tests -c Release
dotnet build src/Annotator -c Release
dotnet publish src/Annotator -c Release -r win-arm64 --self-contained true -o artifacts/arm64
dotnet publish src/Annotator -c Release -r win-x64 --self-contained true -o artifacts/amd64
```

On Windows, run `WinScreenshotAnnotator.exe --self-test` from a writable working directory. It exits with code 0 on success and writes `artifacts/self-test/results.txt`, an annotated PNG, and an editor screenshot. It exercises real desktop capture, global keyboard delivery, UI drawing, caption editing and export, clipboard copy, and close protection. It temporarily replaces the clipboard image. Core model and geometry tests also run on Linux.

[Windows CI](https://github.com/alexeygrigorev/win-screenshot-annotator/actions/workflows/windows.yml) builds and runs tests on native AMD64 and ARM64 runners, uploads portable ZIPs and test evidence, and publishes both ZIPs on `v*` tags. Each package includes SHA-256 hashes; the release also hashes the ZIPs. Runtime sources: [self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/) and [Win32 hotkey registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

MIT licensed.
