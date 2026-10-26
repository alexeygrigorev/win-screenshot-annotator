using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Annotator;

internal static class SmokeTests
{
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    static readonly List<string> results = [];
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        results.Add("PASS " + description);
    }
    static void Mouse(Control canvas, string name, Point location) =>
        canvas.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canvas, [new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0)]);
    public static int Run()
    {
        Directory.CreateDirectory("artifacts/self-test");
        int exit = 1;
        using var loop = new Form { ShowInTaskbar = false, Opacity = 0 };
        loop.Shown += async (_, _) =>
        {
            try { await RunAsync(); exit = 0; }
            catch (Exception ex) { results.Add("FAIL " + ex); }
            finally { File.WriteAllLines("artifacts/self-test/results.txt", results); loop.Close(); }
        };
        Application.Run(loop);
        return exit;
    }
    static async Task<JsonElement> State(EditorForm editor)
    {
        using var json = JsonDocument.Parse(await editor.Script("window.annotator.state()"));
        return json.RootElement.Clone();
    }
    static async Task<bool> Eval(EditorForm editor, string expression) => await editor.Script(expression) == "true";
    static Task Click(EditorForm editor, string selector) => editor.Script($"document.querySelector({JsonSerializer.Serialize(selector)}).click()");
    static async Task Key(EditorForm editor, string key, string code, int vk, int modifiers = 0)
    {
        await editor.DevTools("Input.dispatchKeyEvent", new { type = "keyDown", key, code, windowsVirtualKeyCode = vk, modifiers });
        await editor.DevTools("Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = vk, modifiers });
    }
    static async Task Pointer(EditorForm editor, float x, float y, string type)
    {
        var sheet = (await State(editor)).GetProperty("sheet");
        await editor.DevTools("Input.dispatchMouseEvent", new { type, x = sheet.GetProperty("x").GetDouble() + x, y = sheet.GetProperty("y").GetDouble() + y, button = type == "mouseMoved" ? "none" : "left", buttons = type == "mouseReleased" ? 0 : 1, clickCount = 1 });
    }
    static async Task Drag(EditorForm editor, string tool, Point a, Point b)
    {
        await Click(editor, $"button[aria-label='{tool}']");
        await Pointer(editor, a.X, a.Y, "mousePressed");
        if (a != b) await Pointer(editor, b.X, b.Y, "mouseMoved");
        await Pointer(editor, b.X, b.Y, "mouseReleased");
    }
    static async Task Screenshot(EditorForm editor, string name)
    {
        using var stream = File.Create($"artifacts/self-test/{name}.png");
        await editor.ScreenshotAsync(stream);
    }
    static async Task RunAsync()
    {
        using var desktop = ScreenCapture.Desktop();
        Check(desktop.Width == SystemInformation.VirtualScreen.Width, "Desktop capture uses physical pixels");
        using (var hotkeys = new HotkeyWindow())
        {
            Check(hotkeys.RegionRegistered && hotkeys.DesktopRegistered, "Both global hotkeys register");
            bool received = false;
            hotkeys.CaptureRequested += region => received |= region;
            keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x41, 0, 0, UIntPtr.Zero);
            keybd_event(0x41, 0, 2, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
            for (var i=0; i<300 && !received; i++) await Task.Delay(10);
            Check(received, "Real keyboard input delivers Ctrl+Alt+A through WM_HOTKEY");
        }
        using (var selector = new RegionSelector(desktop, SystemInformation.VirtualScreen))
        {
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) => { timer.Stop(); Mouse(selector,"OnMouseDown",new(220,180)); Mouse(selector,"OnMouseMove",new(20,30)); Mouse(selector,"OnMouseUp",new(20,30)); };
            timer.Start();
            Check(selector.ShowDialog() == DialogResult.OK, "Region selector accepts a reverse drag");
            Check(selector.Selection == new Rectangle(20,30,200,150), "Region coordinates remain physical pixels");
        }
        using var sampleStream = typeof(SmokeTests).Assembly.GetManifestResourceStream("Annotator.ParitySample.png")!;
        using var sample = new Bitmap(sampleStream);
        using var editor = new EditorForm();
        await editor.Ready.WaitAsync(TimeSpan.FromSeconds(30));
        Check(!editor.Visible, "Prewarmed editor has no visible window");
        var origin = new Point(60, 160);
        await editor.ShowImageAsync(sample, origin);
        results.Add($"editor_open_warm_ms={editor.OpenLatencyMs} dpi={editor.DeviceDpi}");
        Check(editor.OpenLatencyMs < 1000, "Prewarmed capture-to-visible-editor is under 1 second");
        Check(editor.ImageOrigin == origin, "Image position equals selection origin in physical pixels");
        Check(editor.DisplaySize == sample.Size, "Image display equals selected 900x550 physical pixels");
        Check(await Eval(editor,"document.querySelectorAll('.toolbar .group')[0].querySelectorAll('button').length === 6"), "Toolbar has all six original icon tools");
        Check(await Eval(editor,"window.annotator.state().width === 12 && window.annotator.state().pen === '--error'"), "Default ink and weight match PocketShell");
        Check(await Eval(editor,"Array.from(document.querySelectorAll('.toolbar .group')[1].querySelectorAll('.swatch')).map(e=>getComputedStyle(e).backgroundColor).join('|') === 'rgb(239, 68, 68)|rgb(245, 158, 11)|rgb(34, 197, 94)|rgb(34, 211, 238)|rgb(167, 139, 250)|rgb(230, 237, 243)'"), "All six colour tokens match the reference in order");
        await Click(editor,"button[aria-label='Mark weight 24']");
        Check(await Eval(editor,"window.annotator.state().width === 24"),"Weight controls change the active mark width");
        await Click(editor,"button[aria-label='Mark weight 12']");
        await Drag(editor,"Arrow",new(80,280),new(260,210));
        await Drag(editor,"Rectangle",new(300,160),new(650,330));
        await Drag(editor,"Draw",new(100,400),new(100,400));
        await Drag(editor,"Text",new(357,200),new(357,200));
        await editor.DevTools("Input.insertText",new { text="Look here\nMultiline caption" });
        Check(await Eval(editor,"getComputedStyle(document.querySelector('textarea')).fontSize === '36px'"), "Caption uses Alexey's 36px font exception");
        Check(await Eval(editor,"getComputedStyle(document.querySelector('textarea')).fontWeight === '600'"), "Caption font weight is 600");
        await Screenshot(editor,"editing");
        await Key(editor,"Enter","Enter",13,2);
        await Click(editor,"button[aria-label='Arrow']");
        Check((await State(editor)).GetProperty("items").GetArrayLength()==4, "Pointer and keyboard input commits the four reference annotations");
        await Screenshot(editor,"editor");
        var png = JsonSerializer.Deserialize<string>(await editor.Script("document.querySelector('canvas').toDataURL('image/png')"))!;
        File.WriteAllBytes("artifacts/self-test/annotated.png",Convert.FromBase64String(png.Split(',')[1]));
        using (var pngStream = new MemoryStream(Convert.FromBase64String(png.Split(',')[1])))
        using (var painted = new Bitmap(pngStream))
            Check(painted.GetPixel(100,400).R > 200 && painted.GetPixel(100,400).G < 100, "Pen click paints a red dot at the same reference coordinate");
        await editor.Script("window.annotator.export('copy')");
        for(int i=0;i<100 && !Clipboard.ContainsImage();i++) await Task.Delay(10);
        using var pasted=Clipboard.GetImage();
        Check(pasted?.Size==sample.Size,"Copy exports the original PNG dimensions to the Windows clipboard");
        await Drag(editor,"Text",new(360,210),new(360,210));
        Check(await Eval(editor,"document.querySelector('textarea').value.startsWith('Look here')"),"Click existing caption reopens it");
        await editor.DevTools("Input.insertText",new {text="Edited"});
        await Key(editor,"Escape","Escape",27);
        await Key(editor,"z","KeyZ",90,2);
        Check(await Eval(editor,"window.annotator.state().items[3].text.startsWith('Look here')"),"Ctrl+Z restores text edit");
        await Click(editor,"button[title='Clear']");
        Check((await State(editor)).GetProperty("items").GetArrayLength()==0,"Clear empties the drawing");
        await Key(editor,"z","KeyZ",90,2);
        Check((await State(editor)).GetProperty("items").GetArrayLength()==4,"Undo restores Clear");
        await Key(editor,"Escape","Escape",27);
        Check(await Eval(editor,"!!document.querySelector('.actions.confirm')"),"Escape arms the discard guard");
        await Screenshot(editor,"guard");
        await Key(editor,"Escape","Escape",27);
        Check(await Eval(editor,"!document.querySelector('.actions.confirm')"),"Second Escape keeps editing");
        editor.Close(); await Task.Delay(100);
        Check(!editor.IsDisposed && await Eval(editor,"!!document.querySelector('.actions.confirm')"),"Window close routes through the discard guard");
        editor.Close(); await Task.Delay(100);
        Check(!editor.IsDisposed,"Repeated window close never discards work");
        editor.CloseForTest();
        // A small near-edge selection keeps its 1:1 pixels and moves attached
        // chrome below the image instead of pushing the image away from it.
        using (var edge = new EditorForm())
        using (var small = new Bitmap(200,150))
        {
            await edge.Ready.WaitAsync(TimeSpan.FromSeconds(30));
            var work = Screen.PrimaryScreen!.WorkingArea;
            var selected = new Point(work.Right - 200, work.Top);
            await edge.ShowImageAsync(small,selected);
            Check(edge.DisplaySize == small.Size && edge.ImageOrigin == selected,"Top-right edge selection remains 1:1 at its original physical position");
            Check(await Eval(edge,"document.body.classList.contains('chrome-below')"),"Toolbar attaches below a selection near the top edge");
            edge.CloseForTest();
        }
        var previous = Autostart.Current;
        try
        {
            Autostart.Set(false);
            using var settings=new SettingsForm(); settings.Show();
            settings.StartWithWindows.Checked=true;
            Check(Autostart.Current==Autostart.Command,"Settings writes the quoted EXE path to HKCU Run");
            settings.StartWithWindows.Checked=false;
            Check(Autostart.Current==null,"Settings removes the HKCU Run entry");
            settings.Close();
        }
        finally
        {
            if(previous==null) Autostart.Set(false);
            else using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) key.SetValue(Autostart.ValueName,previous);
        }
        results.Add("Windows self-test passed: "+RuntimeInformation.ProcessArchitecture);
    }
}
