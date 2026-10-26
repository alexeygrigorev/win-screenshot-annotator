using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Annotator;

// The actual PocketShell DoodleCanvas, embedded in the portable EXE. The tray
// constructs one hidden editor ahead of time; captures reuse its warm renderer.
internal sealed class EditorForm : Form
{
    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(22, 27, 34) };
    readonly TaskCompletionSource ready = new();
    TaskCompletionSource<JsonElement>? loaded;
    TaskCompletionSource? painted;
    static Task<CoreWebView2Environment>? environment;
    bool allowClose;
    byte[]? backdropBytes;
    public Task Ready => ready.Task;
    public long OpenLatencyMs { get; private set; }
    public Point ImageOrigin { get; private set; }
    public Size DisplaySize { get; private set; }
    public Size SourceSize { get; private set; }
    public bool IsPrepared { get; private set; }
    public event Action<string>? Exported;

    public EditorForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Text = "Annotate";
        Icon = AppIcon.Large;
        BackColor = Color.FromArgb(22, 27, 34);
        ClientSize = new Size(960, 720);
        Controls.Add(web);
        _ = Handle;
        _ = InitializeAsync();
    }

    async Task InitializeAsync()
    {
        try
        {
            environment ??= CoreWebView2Environment.CreateAsync(null,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinScreenshotAnnotator", "WebView2"));
            await web.EnsureCoreWebView2Async(await environment);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
            web.CoreWebView2.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith("https://annotator.local/")) e.Cancel = true; };
            web.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.CoreWebView2.WebResourceRequested += (_, e) =>
            {
                var name = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
                if (name == "") name = "index.html";
                Stream? stream = name == "backdrop.bmp" && backdropBytes != null ? new MemoryStream(backdropBytes, false) : Assembly.GetExecutingAssembly().GetManifestResourceStream("Annotator.Web." + name);
                var type = name.EndsWith(".woff2") ? "font/woff2" : name.EndsWith(".bmp") ? "image/bmp" : name.EndsWith(".js") ? "text/javascript" : name.EndsWith(".css") ? "text/css" : "text/html";
                e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(stream ?? new MemoryStream(), stream == null ? 404 : 200, stream == null ? "Not Found" : "OK", $"Content-Type: {type}; charset=utf-8\r\nCache-Control: no-store");
            };
            web.CoreWebView2.WebMessageReceived += Receive;
            web.CoreWebView2.Navigate("https://annotator.local/");
        }
        catch (Exception ex) { ready.TrySetException(ex); }
    }

    void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var data = JsonDocument.Parse(e.WebMessageAsJson);
        var root = data.RootElement;
        switch (root.GetProperty("kind").GetString())
        {
            case "painted": painted?.TrySetResult(); break;
            case "ready": ready.TrySetResult(); break;
            case "loaded": loaded?.TrySetResult(root.Clone()); break;
            case "close": allowClose = true; Close(); break;
            case "copy":
            case "save":
                try
                {
                    var kind = root.GetProperty("kind").GetString()!;
                    var bytes = Convert.FromBase64String(root.GetProperty("png").GetString()!.Split(',')[1]);
                    using var stream = new MemoryStream(bytes);
                    using var image = Image.FromStream(stream);
                    if (kind == "copy") Clipboard.SetImage(image);
                    else
                    {
                        using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", DefaultExt = "png", AddExtension = true, FileName = $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        File.WriteAllBytes(dialog.FileName, bytes);
                    }
                    Exported?.Invoke(kind);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not export drawing"); }
                break;
        }
    }

    public async Task ShowImageAsync(Bitmap image, Point? origin = null, Stopwatch? timer = null)
    {
        timer ??= Stopwatch.StartNew();
        await Ready.WaitAsync(TimeSpan.FromSeconds(30));
        SourceSize = image.Size;
        var work = Screen.FromPoint(origin ?? Cursor.Position).WorkingArea;
        // Move to the target monitor before measuring its renderer scale.
        // Keep chrome at the OS scale; only the sheet converts physical image
        // pixels into CSS pixels. This also keeps the editing caret aligned.
        Location = origin ?? work.Location;
        web.ZoomFactor = 1;
        var pixelRatio = JsonSerializer.Deserialize<double>(await Script("devicePixelRatio"));
        var oversized = image.Width > work.Width || image.Height > work.Height;
        var scale = oversized ? Math.Min(1d, Math.Min((work.Width - 42 * pixelRatio) / image.Width, (work.Height - 179 * pixelRatio) / image.Height)) : 1d;
        scale = Math.Max(.01, scale);
        DisplaySize = new Size((int)Math.Round(image.Width * scale), (int)Math.Round(image.Height * scale));
        ClientSize = new Size(Math.Max((int)Math.Ceiling(560 * pixelRatio), DisplaySize.Width + (int)Math.Ceiling(42 * pixelRatio)), DisplaySize.Height + (int)Math.Ceiling(179 * pixelRatio));
        if (!origin.HasValue) Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);
        // Lossless BMP crosses the in-process resource bridge without PNG
        // compression or a multi-megabyte base64 script on the hotkey path.
        using var bitmap = new MemoryStream();
        image.Save(bitmap, ImageFormat.Bmp);
        backdropBytes = bitmap.ToArray();
        var below = origin.HasValue && origin.Value.Y - work.Y < 104 * pixelRatio;
        var side = (int)Math.Round(21 * pixelRatio);
        var windowX = origin.HasValue
            ? Math.Clamp(origin.Value.X - side, work.Left - side, Math.Max(work.Left - side, work.Right - Width + side))
            : Location.X;
        var offsetX = origin.HasValue ? Math.Max(0, origin.Value.X - windowX - side) / pixelRatio : 0;
        await Script($"document.body.classList.toggle('chrome-below', {below.ToString().ToLowerInvariant()}); document.documentElement.style.setProperty('--sheet-offset-x','{offsetX.ToString(System.Globalization.CultureInfo.InvariantCulture)}px')");
        loaded = new();
        var payload = JsonSerializer.Serialize(new { url = "https://annotator.local/backdrop.bmp", width = image.Width, height = image.Height });
        await Script($"window.annotator.load({payload},{(scale / pixelRatio).ToString(System.Globalization.CultureInfo.InvariantCulture)})");
        var metrics = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var sheet = metrics.GetProperty("sheet");
        var dpr = metrics.GetProperty("dpr").GetDouble();
        var offset = new Point((int)Math.Round(sheet.GetProperty("x").GetDouble() * dpr), (int)Math.Round(sheet.GetProperty("y").GetDouble() * dpr));
        if (origin.HasValue) Location = new Point(origin.Value.X - offset.X, origin.Value.Y - offset.Y);
        ImageOrigin = web.PointToScreen(offset);
        DisplaySize = new Size((int)Math.Round(sheet.GetProperty("width").GetDouble() * dpr), (int)Math.Round(sheet.GetProperty("height").GetDouble() * dpr));
        IsPrepared = true;
        Show(); Activate(); web.Focus();
        painted = new();
        await Script("requestAnimationFrame(()=>requestAnimationFrame(()=>window.chrome.webview.postMessage({kind:'painted'})))");
        await painted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        OpenLatencyMs = timer.ElapsedMilliseconds;
    }

    public Task<string> Script(string js) => web.CoreWebView2.ExecuteScriptAsync(js);
    public Task<string> DevTools(string method, object parameters) => web.CoreWebView2.CallDevToolsProtocolMethodAsync(method, JsonSerializer.Serialize(parameters));
    public Task ScreenshotAsync(Stream stream) => web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
    public void CloseForTest() { allowClose = true; Close(); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose && IsPrepared) { e.Cancel = true; _ = Script("window.annotator.close()"); }
        base.OnFormClosing(e);
    }
}
