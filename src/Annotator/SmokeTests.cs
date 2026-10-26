using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using Annotator.Core;

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
    static void Mouse(Control canvas, string name, Point location)
    {
        var method = canvas.GetType().GetMethod(name,BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(canvas,[new MouseEventArgs(MouseButtons.Left,1,location.X,location.Y,0)]);
    }
    internal static int Run()
    {
        Directory.CreateDirectory("artifacts/self-test");
        try
        {
            using var desktop = ScreenCapture.Desktop();
            Check(desktop.Width == SystemInformation.VirtualScreen.Width && desktop.Height > 0,"GDI desktop capture at physical dimensions");
            using (var hotkeys = new HotkeyWindow())
            {
                Check(hotkeys.RegionRegistered && hotkeys.DesktopRegistered,"Both global hotkeys register on Windows");
                bool received = false;
                hotkeys.CaptureRequested += region => received |= region;
                keybd_event(0x11,0,0,UIntPtr.Zero); keybd_event(0x12,0,0,UIntPtr.Zero); keybd_event(0x41,0,0,UIntPtr.Zero);
                keybd_event(0x41,0,2,UIntPtr.Zero); keybd_event(0x12,0,2,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero);
                var timeout = DateTime.UtcNow.AddSeconds(3);
                while (!received && DateTime.UtcNow < timeout) { Application.DoEvents(); Thread.Sleep(10); }
                Check(received,"Ctrl+Alt+A reaches WM_HOTKEY through Windows keyboard input");
            }
            using (var selector = new RegionSelector(desktop,SystemInformation.VirtualScreen))
            {
                using var selectTimer = new System.Windows.Forms.Timer { Interval = 100 };
                selectTimer.Tick += (_,_) => {
                    selectTimer.Stop();
                    Mouse(selector,"OnMouseDown",new Point(220,180));
                    Mouse(selector,"OnMouseMove",new Point(20,30));
                    Mouse(selector,"OnMouseUp",new Point(20,30));
                };
                selectTimer.Start();
                Check(selector.ShowDialog() == DialogResult.OK,"Region drag accepts selection and closes overlay");
                Check(selector.Selection == new Rectangle(20,30,200,150),"Reverse region drag maps to source pixels");
                using var cropped = desktop.Clone(selector.Selection,desktop.PixelFormat);
                Check(cropped.Size == new Size(200,150),"Region capture keeps selected resolution");
            }
            var backdrop = new Bitmap(900,550);
            using (var g = Graphics.FromImage(backdrop))
            {
                g.Clear(Color.FromArgb(242,245,250));
                using var font = new Font("Segoe UI",24);
                g.DrawString("Screenshot → annotate → copy / save",font,Brushes.Black,30,30);
                g.FillRectangle(Brushes.White,50,150,780,300);
            }
            using var editor = new EditorForm(backdrop);
            editor.Show(); Application.DoEvents();
            var canvas = editor.Canvas;
            var rect = canvas.ImageRect; var scale = rect.Width/backdrop.Width;
            Point At(float x,float y) => new((int)(rect.X+x*scale),(int)(rect.Y+y*scale));
            canvas.Tool = Tool.Arrow;
            Mouse(canvas,"OnMouseDown",At(80,280)); Mouse(canvas,"OnMouseMove",At(260,210)); Mouse(canvas,"OnMouseUp",At(260,210));
            Check(canvas.Document.Items.Count == 1,"Mouse gesture commits arrow despite capture release");
            canvas.Tool = Tool.Rectangle;
            Mouse(canvas,"OnMouseDown",At(300,160)); Mouse(canvas,"OnMouseUp",At(650,330));
            canvas.Tool = Tool.Pen;
            Mouse(canvas,"OnMouseDown",At(100,400)); Mouse(canvas,"OnMouseUp",At(100,400));
            canvas.Tool = Tool.Text;
            Mouse(canvas,"OnMouseDown",At(350,200));
            var caption = canvas.Controls.OfType<TextBox>().Single();
            caption.Text = "Look here\nMultiline caption";
            using var output = canvas.Export();
            Check(!canvas.HasCaption && canvas.Document.Items.Count == 4,"Export flushes pending multiline caption");
            Check(canvas.Document.Items[^1].Text.Contains('\n'),"Caption preserves newlines");
            Check(output.GetPixel(100,400).ToArgb() != backdrop.GetPixel(100,400).ToArgb(),"Single pen click renders a visible dot");
            Check(output.Width == 900 && output.Height == 550,"Export preserves source resolution");
            Check(output.GetPixel(300,200).ToArgb() != backdrop.GetPixel(300,200).ToArgb(),"Annotation pixels present in exported image");
            output.Save("artifacts/self-test/annotated.png",ImageFormat.Png);
            Clipboard.SetImage(output); using var pasted = Clipboard.GetImage();
            Check(pasted?.Width == output.Width,"Clipboard accepts flattened bitmap");
            Mouse(canvas,"OnMouseDown",At(360,210));
            Check(canvas.Controls.OfType<TextBox>().Single().Text.StartsWith("Look here"),"Text tool reopens existing caption");
            canvas.Controls.OfType<TextBox>().Single().Text = "Edited"; canvas.CommitCaption();
            canvas.Document.Undo(); Check(canvas.Document.Items[^1].Text.StartsWith("Look here"),"Undo restores text edit through UI");
            using var screenshot = new Bitmap(editor.Width,editor.Height);
            editor.DrawToBitmap(screenshot,new Rectangle(Point.Empty,screenshot.Size));
            screenshot.Save("artifacts/self-test/editor.png",ImageFormat.Png);
            editor.Close(); Check(!editor.IsDisposed,"Window close retains unsaved annotations");
            canvas.Document.MarkSaved(); editor.Close();
            Check(editor.IsDisposed,"Saved editor closes");
            var previousAutostart = Autostart.Current;
            try
            {
                Autostart.Set(false);
                using var settings = new SettingsForm();
                settings.Show(); Application.DoEvents();
                Check(!settings.StartWithWindows.Checked,"Settings shows Start with Windows off when Run entry is absent");
                settings.StartWithWindows.Checked = true;
                Check(Autostart.Current == $"\"{Environment.ProcessPath}\"","Checking Start with Windows writes HKCU Run entry with the EXE path");
                results.Add("INFO autostart Run value: " + Autostart.Current);
                settings.StartWithWindows.Checked = false;
                Check(Autostart.Current == null,"Unchecking Start with Windows removes the HKCU Run entry");
                settings.Close();
            }
            finally
            {
                if (previousAutostart == null) Autostart.Set(false);
                else using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) key.SetValue(Autostart.ValueName, previousAutostart);
            }
            results.Add("Windows self-test passed: " + RuntimeInformation.ProcessArchitecture);
            File.WriteAllLines("artifacts/self-test/results.txt",results);
            return 0;
        }
        catch (Exception ex)
        {
            results.Add("FAIL " + ex);
            File.WriteAllLines("artifacts/self-test/results.txt",results);
            return 1;
        }
    }
}
