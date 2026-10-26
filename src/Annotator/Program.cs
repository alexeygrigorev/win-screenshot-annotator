using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Annotator;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--self-test")) return SmokeTests.Run();
        // Single instance. A second launch never captures: it asks the running instance to show a tray balloon and exits.
        const string activateName = @"Local\WinScreenshotAnnotator.Activate";
        using var mutex = new Mutex(true, @"Local\WinScreenshotAnnotator", out var first);
        if (!first)
        {
            if (EventWaitHandle.TryOpenExisting(activateName, out var running)) using (running) running.Set();
            return 0;
        }
        using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, activateName);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Screenshot Annotator");
        // Starts silently in the tray (the former --tray behavior, still accepted). --capture opts into capturing at launch.
        using var context = new CaptureContext(args.Contains("--capture"), activate);
        Application.Run(context);
        return 0;
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public event Action<bool>? CaptureRequested;
    public bool RegionRegistered { get; }
    public bool DesktopRegistered { get; }
    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
        RegionRegistered = RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.A); // Ctrl+Alt, no repeat
        DesktopRegistered = RegisterHotKey(Handle, 2, 0x4003, (uint)Keys.F);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312) CaptureRequested?.Invoke(m.WParam.ToInt32() == 1);
        base.WndProc(ref m);
    }
    public void Dispose() { UnregisterHotKey(Handle, 1); UnregisterHotKey(Handle, 2); DestroyHandle(); }
}

/// App icon (assets/icon.ico, generated from assets/icon.svg), embedded so tray and windows match the EXE icon.
internal static class AppIcon
{
    static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Annotator.icon.ico")
            ?? throw new InvalidOperationException("Embedded icon missing");
        return new Icon(stream, size);
    }
    public static Icon Small { get; } = Load(SystemInformation.SmallIconSize);
    public static Icon Large { get; } = Load(SystemInformation.IconSize);
}

internal sealed class CaptureContext : ApplicationContext
{
    readonly HotkeyWindow hotkeys = new();
    readonly NotifyIcon tray;
    readonly List<EditorForm> editors = [];
    readonly Control ui = new();
    readonly RegisteredWaitHandle activateWait;
    readonly string? statusFile = Environment.GetEnvironmentVariable("WSA_STATUS_FILE");
    int balloons, captures;
    long? lastEditorLatency;
    bool capturing;
    EditorForm? warmEditor;
    public CaptureContext(bool captureAtStart, EventWaitHandle activate)
    {
        _ = ui.Handle; // UI-thread marshaling target for the second-instance signal.
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture region   Ctrl+Alt+A", null, (_,_) => Capture(true));
        menu.Items.Add("Capture desktop   Ctrl+Alt+F", null, (_,_) => Capture(false));
        menu.Items.Add("Open image…", null, (_,_) => OpenImage());
        menu.Items.Add("Paste image", null, (_,_) => {
            try { using var image = Clipboard.GetImage(); if (image != null) Open(new Bitmap(image)); }
            catch (Exception ex) { Error(ex); }
        });
        menu.Items.Add("Settings…", null, (_,_) => ShowSettings());
        menu.Items.Add("Exit", null, (_,_) => ExitThread());
        tray = new NotifyIcon { Icon = AppIcon.Small, Text = "Screenshot Annotator — Ctrl+Alt+A", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_,_) => Capture(true);
        hotkeys.CaptureRequested += Capture;
        if (!hotkeys.RegionRegistered || !hotkeys.DesktopRegistered)
            MessageBox.Show("A capture hotkey is already in use. Close the conflicting app and restart Screenshot Annotator. The tray capture commands still work.", "Hotkey conflict");
        activateWait = ThreadPool.RegisterWaitForSingleObject(activate, (_,_) => ui.BeginInvoke((Action)AlreadyRunning), null, -1, false);
        if (captureAtStart)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (_,_) => { timer.Stop(); timer.Dispose(); Capture(true); };
            timer.Start();
        }
        WriteStatus();
    }
    SettingsForm? settings;
    void ShowSettings()
    {
        if (settings is { IsDisposed: false }) { settings.Activate(); return; }
        settings = new SettingsForm();
        settings.FormClosed += (_,_) => settings = null;
        settings.Show();
    }
    async Task ReportWarmAsync(EditorForm editor)
    {
        try { await editor.Ready; WriteStatus(); }
        catch (Exception ex) { Error(ex); }
    }
    void AlreadyRunning()
    {
        balloons++;
        tray.ShowBalloonTip(4000, "Screenshot Annotator is already running", "Press Ctrl+Alt+A to capture a region, or use the tray icon.", ToolTipIcon.Info);
        WriteStatus();
    }
    // Test hook for CI: state that cannot be observed reliably from outside the process.
    void WriteStatus()
    {
        if (string.IsNullOrEmpty(statusFile)) return;
        try
        {
            File.WriteAllLines(statusFile, new[] {
                $"tray_visible={tray.Visible}",
                $"editor_ready={warmEditor?.Ready.IsCompletedSuccessfully == true}",
                $"editor_open_ms={lastEditorLatency}",
                $"hotkey_region_registered={hotkeys.RegionRegistered}",
                $"hotkey_desktop_registered={hotkeys.DesktopRegistered}",
                $"captures_started={captures}",
                $"balloons_shown={balloons}",
                $"tray_icon_custom={ReferenceEquals(tray.Icon, AppIcon.Small)}",
            });
        }
        catch (IOException) { }
    }
    void Error(Exception ex) => MessageBox.Show(ex.Message, "Screenshot Annotator", MessageBoxButtons.OK, MessageBoxIcon.Error);
    void OpenImage()
    {
        using var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try { using var image = Image.FromFile(dialog.FileName); Open(new Bitmap(image)); }
        catch (Exception ex) { Error(ex); }
    }
    async void Open(Bitmap image, Point? selectionScreenOrigin = null, Stopwatch? timer = null)
    {
        var editor = warmEditor ?? new EditorForm();
        warmEditor = null;
        editors.Add(editor);
        editor.FormClosed += (_,_) => editors.Remove(editor);
        try { using (image) await editor.ShowImageAsync(image, selectionScreenOrigin, timer); lastEditorLatency = editor.OpenLatencyMs; WriteStatus(); }
        catch (Exception ex) { editor.CloseForTest(); Error(ex); }
        if (warmEditor == null) warmEditor = new EditorForm();
    }
    async void Capture(bool region)
    {
        if (capturing) return;
        var timer = Stopwatch.StartNew();
        capturing = true; captures++; WriteStatus();
        var visible = editors.Where(e => e.Visible).ToArray();
        try
        {
            foreach (var editor in visible) editor.Hide();
            await Task.Delay(180); // Let DWM remove our windows before copying desktop pixels.
            using var desktop = ScreenCapture.Desktop();
            Bitmap? selected = null;
            Point? selectionOrigin = null;
            if (region)
            {
                using var selector = new RegionSelector(desktop, SystemInformation.VirtualScreen);
                if (selector.ShowDialog() == DialogResult.OK)
                {
                    timer.Restart(); // Human region-selection time is excluded.
                    selected = desktop.Clone(selector.Selection, desktop.PixelFormat);
                    // Bitmap coords start at (0,0); the screen origin restores
                    // the virtual-desktop offset (negative on left/top monitors).
                    selectionOrigin = new Point(
                        SystemInformation.VirtualScreen.X + selector.Selection.X,
                        SystemInformation.VirtualScreen.Y + selector.Selection.Y);
                }
            }
            else selected = new Bitmap(desktop);
            foreach (var editor in visible) editor.Show();
            if (selected != null) Open(selected, selectionOrigin, timer);
        }
        catch (Exception ex) { foreach (var editor in visible) editor.Show(); Error(ex); }
        finally { capturing = false; }
    }
    protected override void ExitThreadCore()
    {
        // Close() asks about unsaved annotations. Keep the tray alive if any close is cancelled.
        foreach (var editor in editors.ToArray()) { editor.Close(); if (!editor.IsDisposed) return; }
        warmEditor?.Dispose();
        activateWait.Unregister(null); tray.Visible = false; tray.Dispose(); hotkeys.Dispose(); ui.Dispose(); base.ExitThreadCore();
    }
}

internal static class ScreenCapture
{
    public static Bitmap Desktop()
    {
        var bounds = SystemInformation.VirtualScreen;
        var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try { using var g = Graphics.FromImage(bitmap); g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy); return bitmap; }
        catch { bitmap.Dispose(); throw; }
    }
}
