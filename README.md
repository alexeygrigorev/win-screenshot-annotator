# Screenshot Annotator for Windows

## How to run

1. Download `ScreenshotAnnotator-ARM64.exe` (ARM / Surface / Snapdragon) or `ScreenshotAnnotator-AMD64.exe` (Intel / AMD) from [Releases](https://github.com/alexeygrigorev/win-screenshot-annotator/releases/latest).
2. Double-click it. It is unsigned, so if SmartScreen appears click **More info → Run anyway**. Nothing pops up: it starts quietly in the notification area (tray).
3. Press Ctrl+Alt+A any time to select a region and annotate it. Launching it again just shows an "already running" tray message.

Capture a region, draw on it immediately, then copy or save a PNG. The app uses C# / Windows Forms with GDI capture and a Win32 global hotkey, with no Electron or browser runtime. Windows is the supported platform.

## Install on Tabo / Surface

Use the portable release for your Windows architecture:

1. Open [Releases](https://github.com/alexeygrigorev/win-screenshot-annotator/releases/latest).
2. Download ScreenshotAnnotator-ARM64.exe for ARM Windows (Tabo / Snapdragon Surface), or ScreenshotAnnotator-AMD64.exe for Intel/AMD Windows. Check Settings → System → About → System type if unsure.
3. Optionally move the EXE somewhere permanent, such as `%LOCALAPPDATA%\ScreenshotAnnotator`. It is a single self-contained file; nothing else is needed.
4. Run it. It starts silently in the notification area and waits for Ctrl+Alt+A. Drag over the screen and release to open the editor. Escape cancels capture. The app stays in the notification area after closing an editor. Only one copy runs; launching it again shows a tray balloon instead of capturing. Pass `--capture` to start a region capture immediately at launch.

Requires Windows 11 (or Windows 10 22H2 with applicable .NET support). The .NET 10 runtime is included, so you don't need an SDK or installer. You don't need admin rights to run it.

Release binaries are unsigned, so Windows may show a SmartScreen reputation prompt. Verify the downloaded EXE against the release's `SHA256SUMS.txt` with `Get-FileHash` before choosing to run it.

To start the app when you sign in, press Win+R, enter `shell:startup`, and add a shortcut to the EXE. It starts quietly in the tray without capturing (`--tray` is still accepted and does the same). To uninstall, exit from the tray menu, then delete the EXE and startup shortcut.

## Controls

| Hotkey | Action |
| --- | --- |
| Ctrl+Alt+A | Global: select a screenshot region |
| Ctrl+Alt+F | Global: capture the full virtual desktop, across monitors |
| P / L / A / R / E / T | Editor: pen / line / arrow / rectangle / ellipse / text |
| Shift while dragging | Snap line or arrow to the nearest 45° direction |
| Ctrl+Z / Ctrl+Y | Undo / redo annotations, text edits, deletion, and Clear |
| Ctrl+C | Copy flattened image; while typing, copy selected text |
| Ctrl+S | Save PNG, including any caption being edited |
| Enter | Newline in a caption |
| Ctrl+Enter / Escape while typing | Commit caption and keep editor open |
| Escape in editor | Close; unsaved work offers Keep editing, Save PNG, or Discard |
| Escape during close confirmation | Keep editing |

Use the toolbar to choose a tool, ink colour, or stroke weight. It also provides undo and redo alongside Clear, Copy, and Save PNG. Click an existing caption with the text tool to edit it, or empty it to delete it.

Text size follows stroke weight, with a minimum size to keep text readable, and a pen click adds a dot. Clicking without dragging adds nothing for lines and outlines, while rectangles and ellipses can have any proportions. PNG dimensions match the original capture, even when the preview scales down.

The notification-area menu also offers region and desktop capture. You can also open an image, paste an image, or exit. Double-click its icon to capture a region.

Hotkeys are fixed defaults, and only one app instance runs per Windows session. If a hotkey can't be registered, the app shows a message and tray commands remain available.

## Behavior and implementation

Ports the annotation behavior documented in PocketShell Desktop `docs/COMPOSER.md` §27, `docs/SHORTCUTS.md` §1.5, and its shared `DoodleCanvas.vue` / `doodleGeometry.ts`. The app runs independently of PocketShell. Attachment staging and composer integration are outside this app's scope.

Annotations are vector items with version history, rather than per-stroke screenshots. Preview and export use the same painter, and export commits the caption being edited before flattening. Saving clears the unsaved flag, while copying keeps it.

To discard unsaved edits when closing, click Discard. The app doesn't upload images or send telemetry, and it has no background network activity. Captures and annotations live in memory until saved or copied, so drafts don't persist after exit.

The app captures the Windows virtual desktop in physical pixels and supports negative monitor coordinates. Our editor windows are hidden briefly before capture. Windows can return blank pixels for protected video or secure desktops. Content in elevated or protected windows may also appear blank. Multi-monitor layouts with mixed display scaling should be checked on the target hardware.

## Build and test

Install the .NET 10 SDK, then:

```powershell
dotnet run --project tests/Annotator.Tests -c Release
dotnet build src/Annotator -c Release
dotnet publish src/Annotator -c Release -r win-arm64 --self-contained true -o artifacts/arm64
dotnet publish src/Annotator -c Release -r win-x64 --self-contained true -o artifacts/amd64
```

On Windows, run `WinScreenshotAnnotator.exe --self-test` from a writable working directory. It exits with code 0 on success and writes `artifacts/self-test/results.txt`, an annotated PNG, and an editor screenshot. It tests desktop capture and global keyboard delivery alongside UI drawing. It also checks caption export, clipboard copy, and close protection.

The self-test temporarily replaces the clipboard image. Core model and geometry tests also run on Linux.

The app icon is drawn in `assets/icon.svg`; run `assets/make-icon.sh` to regenerate the multi-size `assets/icon.ico` (16–256 px) used for the EXE, tray, and editor windows.

[Windows CI](https://github.com/alexeygrigorev/win-screenshot-annotator/actions/workflows/windows.yml) builds and runs tests on native AMD64 and ARM64 runners, builds a single-file self-contained EXE per architecture, runs the integration self-test against that EXE, and publishes the EXEs (plus ZIPs holding only the EXE and README) on `v*` tags with a `SHA256SUMS.txt`. See [self-contained deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/) and [Win32 hotkey registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).

MIT licensed.
