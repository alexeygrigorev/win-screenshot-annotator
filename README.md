# Screenshot Annotator for Windows

Capture a region, annotate it with the PocketShell Desktop drawing tools, then copy or save a PNG. The app starts in the Windows notification area (tray) and waits for a global hotkey.

## Run

Choose the release for your processor:

1. Download `ScreenshotAnnotator-ARM64.exe` for Surface/Snapdragon Windows, or `ScreenshotAnnotator-AMD64.exe` for Intel/AMD Windows, from [Releases](https://github.com/alexeygrigorev/win-screenshot-annotator/releases/latest).
2. Move the EXE somewhere permanent, such as `%LOCALAPPDATA%\ScreenshotAnnotator`, and run it. If SmartScreen appears for an unsigned release, choose More info → Run anyway after checking the release's `SHA256SUMS.txt` with `Get-FileHash`.
3. Press Ctrl+Alt+A, drag over a region, then release to annotate it. Launching the EXE again shows an "already running" tray message.

You need Windows 11, or Windows 10 22H2 with applicable .NET support, and Microsoft Edge WebView2 Runtime. Windows 11 includes WebView2. If it's missing, install the [Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download). The single EXE includes .NET 10 and the drawing UI. You don't need the .NET SDK to run it.

Use Tray → Settings → Start with Windows to start quietly when you sign in. This writes a per-user Windows startup entry without admin rights. To uninstall, clear that checkbox, exit from the tray menu, then delete the EXE.

The tray menu also offers desktop capture, opening an image, pasting an image, and exiting. Double-click the tray icon to capture a region. Pass `--capture` to capture immediately at launch.

## Controls

| Key | Action |
| --- | --- |
| Ctrl+Alt+A | Global: select a screenshot region |
| Ctrl+Alt+F | Global: capture the full virtual desktop |
| Shift while dragging | Snap a line or arrow to the nearest 45° direction |
| Ctrl+Z | Undo a mark, caption edit, deletion, or Clear |
| Ctrl+C | Copy the annotated image; while typing, copy selected text |
| Ctrl+S | Save PNG, including a caption being edited |
| Enter while typing | Add a newline |
| Ctrl+Enter or Escape while typing | Commit the caption |
| Escape in the editor | Ask whether to discard the drawing |
| Escape during confirmation | Keep editing |

Use the icon toolbar for freehand drawing and text, or to add lines, arrows, rectangles and ellipses. The six colours and 6/12/24 stroke weights match PocketShell. The default is red at weight 12, and captions stay at 36px with font weight 600.

 Click an existing caption with the text tool to edit it, or empty it to delete it. A pen click paints a dot.

Undo and Clear sit at the right of the toolbar. Copy and Save PNG sit in the footer. To discard a drawing, click Discard in the confirmation bar.

The captured image opens over the selected region at its original physical pixel size. Large captures may fit the work area. PNG exports retain their original resolution. For a selection near the screen's top edge, find the toolbar beneath the image.

## Implementation and verification

The native Windows tray process handles capture and global hotkeys. It also handles clipboard export, file dialogs, and placement. A hidden WebView2 editor loads ahead of capture.

 We render PocketShell's `DoodleCanvas.vue` with its text editor and painter in WebView2. We embed its geometry, icons, and design tokens in the EXE. There's no Electron dependency.

The app serves its drawing UI from embedded resources and doesn't upload captures. Preview and export use the same canvas painter. Before closing an editor, the app asks whether to discard the drawing.

See [parity evidence](docs/parity/README.md) for screenshots and Windows test results. The vendored source revision and file hashes are in [provenance.json](ui/vendor/provenance.json). In the Windows host, we preserve 36px captions and original image resolution. We also provide physical pixel placement and Copy/Save actions.

Install .NET 10 to build the app with the committed UI bundle:

```powershell
dotnet run --project tests/Annotator.Tests -c Release
dotnet build src/Annotator -c Release
dotnet publish src/Annotator -c Release -r win-arm64 --self-contained true -o artifacts/arm64
dotnet publish src/Annotator -c Release -r win-x64 --self-contained true -o artifacts/amd64
```

To resync from PocketShell and rebuild the UI, install Python 3 and Node 24:

```bash
python3 scripts/sync-doodle-ui.py
npm --prefix ui ci
npm --prefix ui run build
npx --prefix ui playwright install chromium
npm --prefix ui test
```

Set `POCKETSHELL_CORE` to the PocketShell Core directory if it isn't `~/git/pocketshell-core`. Commit the regenerated source and bundle together. Windows CI rebuilds the UI and fails if the bundle differs from the committed version.

On Windows, run `WinScreenshotAnnotator.exe --self-test` from a writable directory. It writes `artifacts/self-test/results.txt` and screenshots. It tests keyboard delivery, region selection, drawing and caption editing. It also checks clipboard export, placement, startup timing and the discard guard. It temporarily replaces the clipboard image.

[Windows CI](https://github.com/alexeygrigorev/win-screenshot-annotator/actions/workflows/windows.yml) tests native AMD64 and ARM64 builds. On `v*` tags, it publishes one EXE per architecture, ZIPs containing the EXE and README, and `SHA256SUMS.txt`.

Run `assets/make-icon.sh` to regenerate the EXE and tray icon from `assets/icon.svg`. The app is MIT licensed.
