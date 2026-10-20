using System.Runtime.InteropServices;

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
        using var mutex = new Mutex(true, @"Local\WinScreenshotAnnotator", out var first);
        if (!first) { MessageBox.Show("Screenshot Annotator is already running. Use Ctrl+Alt+A or its tray icon."); return 0; }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Screenshot Annotator");
        using var context = new CaptureContext(args.Contains("--tray"));
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

internal sealed class CaptureContext : ApplicationContext
{
    readonly HotkeyWindow hotkeys = new();
    readonly NotifyIcon tray;
    readonly List<EditorForm> editors = [];
    bool capturing;
    public CaptureContext(bool trayOnly)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture region   Ctrl+Alt+A", null, (_,_) => Capture(true));
        menu.Items.Add("Capture desktop   Ctrl+Alt+F", null, (_,_) => Capture(false));
        menu.Items.Add("Open image…", null, (_,_) => OpenImage());
        menu.Items.Add("Paste image", null, (_,_) => {
            try { using var image = Clipboard.GetImage(); if (image != null) Open(new Bitmap(image)); }
            catch (Exception ex) { Error(ex); }
        });
        menu.Items.Add("Exit", null, (_,_) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Screenshot Annotator — Ctrl+Alt+A", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_,_) => Capture(true);
        hotkeys.CaptureRequested += Capture;
        if (!hotkeys.RegionRegistered || !hotkeys.DesktopRegistered)
            MessageBox.Show("A capture hotkey is already in use. Close the conflicting app and restart Screenshot Annotator. The tray capture commands still work.", "Hotkey conflict");
        if (!trayOnly)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += (_,_) => { timer.Stop(); timer.Dispose(); Capture(true); };
            timer.Start();
        }
    }
    void Error(Exception ex) => MessageBox.Show(ex.Message, "Screenshot Annotator", MessageBoxButtons.OK, MessageBoxIcon.Error);
    void OpenImage()
    {
        using var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try { using var image = Image.FromFile(dialog.FileName); Open(new Bitmap(image)); }
        catch (Exception ex) { Error(ex); }
    }
    void Open(Bitmap image)
    {
        var editor = new EditorForm(image);
        editors.Add(editor);
        editor.FormClosed += (_,_) => editors.Remove(editor);
        editor.Show(); editor.Activate();
    }
    async void Capture(bool region)
    {
        if (capturing) return;
        capturing = true;
        var visible = editors.Where(e => e.Visible).ToArray();
        try
        {
            foreach (var editor in visible) editor.Hide();
            await Task.Delay(180); // Let DWM remove our windows before copying desktop pixels.
            using var desktop = ScreenCapture.Desktop();
            Bitmap? selected = null;
            if (region)
            {
                using var selector = new RegionSelector(desktop, SystemInformation.VirtualScreen);
                if (selector.ShowDialog() == DialogResult.OK) selected = desktop.Clone(selector.Selection, desktop.PixelFormat);
            }
            else selected = new Bitmap(desktop);
            foreach (var editor in visible) editor.Show();
            if (selected != null) Open(selected);
        }
        catch (Exception ex) { foreach (var editor in visible) editor.Show(); Error(ex); }
        finally { capturing = false; }
    }
    protected override void ExitThreadCore()
    {
        // Close() asks about unsaved annotations. Keep the tray alive if any close is cancelled.
        foreach (var editor in editors.ToArray()) { editor.Close(); if (!editor.IsDisposed) return; }
        tray.Visible = false; tray.Dispose(); hotkeys.Dispose(); base.ExitThreadCore();
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
