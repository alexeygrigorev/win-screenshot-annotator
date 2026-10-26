// The annotate window. Ports the DoodleCanvas.vue sheet chrome to WinForms:
// one rounded toolbar (6 tool icons / 6 colour chips / 3 weight dots, Undo
// and Clear right-aligned), the capture at 1:1 over the selection, and a
// footer with state hints plus Cancel / Save PNG / Copy (Copy is the primary
// action, the Attach equivalent). The discard guard replaces the action row.
//
// Deliberately NOT ported: Redo (undo-only history), letter tool shortcuts
// (P/L/A/R/E/T), PocketShell's 56vh fit (1:1 over the selection instead).
using System.Diagnostics;
using System.Drawing.Imaging;

namespace Annotator;

internal sealed class EditorForm : Form
{
    internal AnnotationCanvas Canvas { get; }
    const int Pad = 8; // --sp-2
    const int ToolbarH = 40; // 24px buttons + 8px padding each side
    const int FooterH = 40;
    const int GuardH = 52;

    readonly Dictionary<Tool, IconButton> toolButtons = [];
    readonly List<SwatchButton> swatches = [];
    readonly List<WeightButton> weightButtons = [];
    readonly IconButton undoButton;
    readonly IconButton clearButton;
    readonly Label hint = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    readonly Panel footerMain = new() { Dock = DockStyle.Bottom, Height = FooterH };
    readonly Panel guard = new() { Dock = DockStyle.Bottom, Height = GuardH, Visible = false };
    readonly ToolTip tips = new();
    bool guardArmed;

    static Stopwatch? openTimer;
    public static long? LastOpenLatencyMs { get; private set; }
    public static void MarkOpenStart() { openTimer = Stopwatch.StartNew(); LastOpenLatencyMs = null; }

    static readonly (Tool Tool, string Icon, string Label)[] Tools =
    [
        (Tool.Pen, "edit-2", "Draw"),
        (Tool.Line, "minus", "Line"),
        (Tool.Arrow, "arrow-right", "Arrow"),
        (Tool.Rectangle, "square", "Rectangle"),
        (Tool.Ellipse, "circle", "Ellipse"),
        (Tool.Text, "type", "Text"),
    ];

    public EditorForm(Bitmap image, Point? selectionScreenOrigin = null)
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = DoodleColors.Bg;
        ForeColor = DoodleColors.Fg;
        Text = "Annotate";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;

        Canvas = new AnnotationCanvas(image) { Dock = DockStyle.Fill };
        Canvas.Changed += RefreshChrome;

        var toolbar = BuildToolbar(out undoButton, out clearButton);
        var canvasPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Pad), BackColor = DoodleColors.Bg };
        canvasPanel.Controls.Add(Canvas);

        BuildFooter();
        BuildGuard();

        Controls.Add(canvasPanel);
        Controls.Add(guard);
        Controls.Add(footerMain);
        Controls.Add(toolbar);

        var screen = selectionScreenOrigin.HasValue
            ? Screen.FromPoint(selectionScreenOrigin.Value)
            : Screen.FromPoint(Cursor.Position);
        var work = screen.WorkingArea;
        var display = new Size(image.Width, image.Height);
        var availW = work.Width - Pad * 2;
        var availH = work.Height - ToolbarH - Pad * 2 - FooterH;
        if (image.Width > availW || image.Height > availH)
        {
            // Only a region larger than the work area is fit, never upscaled.
            var scale = Math.Min(1f, Math.Min(availW / (float)image.Width, availH / (float)image.Height));
            Canvas.ViewScale = Math.Max(0.05f, scale);
            display = new Size(Math.Max(1, (int)(image.Width * Canvas.ViewScale)), Math.Max(1, (int)(image.Height * Canvas.ViewScale)));
        }
        Canvas.AutoScrollMinSize = display;
        ClientSize = new Size(display.Width + Pad * 2, ToolbarH + Pad + display.Height + Pad + FooterH);

        if (selectionScreenOrigin.HasValue) PlaceAtSelection(selectionScreenOrigin.Value, work);
        else Location = new Point(work.X + (work.Width - Width) / 2, work.Y + (work.Height - Height) / 2);

        RefreshChrome();
    }

    // Put the image exactly over the selected region, toolbar attached above
    // it without covering it; clamp into the work area when there is no room.
    void PlaceAtSelection(Point origin, Rectangle work)
    {
        _ = Handle; // PointToScreen needs the handle.
        var chrome = new Point(
            Canvas.PointToScreen(Point.Empty).X - PointToScreen(Point.Empty).X,
            Canvas.PointToScreen(Point.Empty).Y - PointToScreen(Point.Empty).Y);
        var want = new Point(origin.X - chrome.X, origin.Y - chrome.Y);
        want.X = Math.Min(Math.Max(want.X, work.X), Math.Max(work.X, work.Right - Width));
        want.Y = Math.Min(Math.Max(want.Y, work.Y), Math.Max(work.Y, work.Bottom - Height));
        Location = want;
    }

    public Point ImageScreenOrigin() => Canvas.PointToScreen(Point.Empty);

    Panel BuildToolbar(out IconButton undo, out IconButton clear)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = ToolbarH, BackColor = DoodleColors.Surface2, Padding = new Padding(Pad, Pad, Pad, Pad) };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(DoodleColors.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = false, BackColor = Color.Transparent, Margin = Padding.Empty, Padding = Padding.Empty };
        left.Height = DoodleReference.ControlHSm;
        left.Top = (ToolbarH - left.Height) / 2;
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = Padding.Empty };

        foreach (var (tool, icon, label) in Tools)
        {
            var button = new IconButton { IconName = icon, AccessibleName = label };
            button.Click += (_, _) => SelectTool(tool);
            tips.SetToolTip(button, label);
            toolButtons.Add(tool, button);
            left.Controls.Add(button);
        }
        left.Controls.Add(new ToolbarSeparator());
        var inks = DoodleReference.PenHex.Select(Painter.Hex).ToArray();
        for (var i = 0; i < inks.Length; i++)
        {
            var index = i;
            var swatch = new SwatchButton { PenColor = inks[i], AccessibleName = $"Ink {DoodleReference.PenNames[i]}" };
            swatch.Click += (_, _) => SelectInk(index);
            tips.SetToolTip(swatch, DoodleReference.PenNames[index]);
            swatches.Add(swatch);
            left.Controls.Add(swatch);
        }
        left.Controls.Add(new ToolbarSeparator());
        for (var i = 0; i < DoodleReference.Weights.Length; i++)
        {
            var index = i;
            var dot = new WeightButton { Weight = DoodleReference.Weights[i], AccessibleName = $"Mark weight {DoodleReference.Weights[i]}" };
            dot.Click += (_, _) => SelectWeight(index);
            weightButtons.Add(dot);
            left.Controls.Add(dot);
        }

        undo = new IconButton { IconName = "rotate-ccw", AccessibleName = "Undo" };
        undo.Click += (_, _) => { Canvas.CommitCaption(); Canvas.Document.Undo(); Canvas.Invalidate(); RefreshChrome(); };
        tips.SetToolTip(undo, "Undo");
        clear = new IconButton { IconName = "trash-2", AccessibleName = "Clear" };
        clear.Click += (_, _) => { Canvas.CommitCaption(); Canvas.Document.Clear(); Canvas.Invalidate(); RefreshChrome(); };
        tips.SetToolTip(clear, "Clear");
        undoButton = undo;
        clearButton = clear;
        right.Controls.Add(undo);
        right.Controls.Add(clear);

        bar.Controls.Add(left);
        bar.Controls.Add(right);
        return bar;
    }

    void BuildFooter()
    {
        footerMain.BackColor = DoodleColors.Bg;
        footerMain.Padding = new Padding(12, 0, 8, 0);
        hint.ForeColor = DoodleColors.FgSecondary;
        hint.Font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        hint.AutoEllipsis = true;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
        var cancel = new DoodleButton { Text = "Cancel" };
        cancel.Click += (_, _) => RequestClose();
        var save = new DoodleButton { Text = "Save PNG" };
        save.Click += (_, _) => Save();
        var copy = new DoodleButton { Text = "Copy", ButtonKind = DoodleButton.Kind.Primary };
        copy.Click += (_, _) => Copy();
        copy.ApplyKind();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        buttons.Controls.Add(copy);
        footerMain.Controls.Add(hint);
        footerMain.Controls.Add(buttons);
    }

    void BuildGuard()
    {
        guard.BackColor = DoodleColors.GuardFill;
        guard.Padding = new Padding(12, 0, 8, 0);
        guard.Paint += (_, e) =>
        {
            using var pen = new Pen(DoodleColors.BorderStrong);
            e.Graphics.DrawRounded(new Rectangle(0, 0, guard.Width - 1, guard.Height - 1), DoodleReference.RadiusMd, pen);
        };
        var warn = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DoodleColors.Fg, Text = "Discard this drawing? It cannot be recovered." };
        warn.Font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
        var keep = new DoodleButton { Text = "Keep editing" };
        keep.Click += (_, _) => DisarmGuard();
        var discard = new DoodleButton { Text = "Discard", ButtonKind = DoodleButton.Kind.Danger };
        discard.Click += (_, _) => { Canvas.Document.MarkSaved(); DisarmGuard(); Close(); };
        discard.ApplyKind();
        buttons.Controls.Add(keep);
        buttons.Controls.Add(discard);
        guard.Controls.Add(warn);
        guard.Controls.Add(buttons);
    }

    void SelectTool(Tool tool)
    {
        // Leaving the text tool commits, like the reference tool watcher.
        Canvas.CommitCaption();
        Canvas.Tool = tool;
        Canvas.Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
        Canvas.Focus();
        RefreshChrome();
    }

    void SelectInk(int index)
    {
        var ink = Painter.Hex(DoodleReference.PenHex[index]);
        var token = $"--{DoodleReference.PenNames[index]}";
        if (Canvas.HasCaption) Canvas.RetargetEditing(ink, Canvas.Weight);
        else Canvas.Ink = ink;
        // The open editor keeps the toolbar token in sync (reference watcher).
        _ = token;
        Canvas.Focus();
        RefreshChrome();
    }

    void SelectWeight(int index)
    {
        var weight = DoodleReference.Weights[index];
        if (Canvas.HasCaption) Canvas.RetargetEditing(Canvas.Ink, weight);
        else Canvas.Weight = weight;
        Canvas.Focus();
        RefreshChrome();
    }

    void RefreshChrome()
    {
        foreach (var pair in toolButtons) pair.Value.Active = pair.Key == Canvas.Tool;
        var inks = DoodleReference.PenHex.Select(Painter.Hex).ToArray();
        for (var i = 0; i < swatches.Count; i++)
            swatches[i].Active = Canvas.Ink.ToArgb() == inks[i].ToArgb();
        for (var i = 0; i < weightButtons.Count; i++)
        {
            weightButtons[i].Active = Canvas.Weight == DoodleReference.Weights[i];
            tips.SetToolTip(weightButtons[i], Canvas.Tool == Tool.Text
                ? $"Text {Annotator.Core.Geometry.TextSize(DoodleReference.Weights[i])}px"
                : $"{DoodleReference.Weights[i]}px");
        }
        undoButton.Enabled = Canvas.Document.CanUndo;
        var empty = Canvas.Document.Items.Count == 0 && !Canvas.HasCaption;
        clearButton.Enabled = !empty;
        // Footer hint by state, like the reference action row.
        if (guardArmed) { /* guard carries its own question */ }
        else if (Canvas.HasCaption) hint.Text = "Enter for a new line, Esc or Ctrl+Enter to finish";
        else if (Canvas.Tool == Tool.Text) hint.Text = "Click the sheet to write; click text to edit it";
        else if (Canvas.Tool is Tool.Arrow or Tool.Line) hint.Text = "Drag from tail to head; hold Shift for 45° steps";
        else hint.Text = $"{Canvas.Backdrop.Width} × {Canvas.Backdrop.Height} px capture";
        Text = "Annotate" + (Canvas.Document.IsDirty ? " *" : "");
    }

    void ArmGuard()
    {
        guardArmed = true;
        footerMain.Visible = false;
        guard.Visible = true;
        RefreshChrome();
    }

    void DisarmGuard()
    {
        guardArmed = false;
        guard.Visible = false;
        footerMain.Visible = true;
        Canvas.Focus();
        RefreshChrome();
    }

    void RequestClose()
    {
        Canvas.CommitCaption();
        if (Canvas.Document.IsDirty) { ArmGuard(); return; }
        Close();
    }

    void Copy()
    {
        try
        {
            using var image = Canvas.Export();
            Clipboard.SetImage(image);
            hint.Text = "Copied annotated image to the clipboard.";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not copy"); }
    }

    void Save() => SaveImage();

    bool SaveImage()
    {
        Canvas.CommitCaption();
        using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", DefaultExt = "png", AddExtension = true, FileName = $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try
        {
            using var image = Canvas.Export();
            image.Save(dialog.FileName, ImageFormat.Png);
            Canvas.Document.MarkSaved();
            RefreshChrome();
            hint.Text = $"Saved {dialog.FileName}";
            return true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not save"); return false; }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (openTimer != null)
        {
            LastOpenLatencyMs = openTimer.ElapsedMilliseconds;
            openTimer = null;
        }
        Canvas.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Canvas.CommitCaption();
        if (Canvas.Document.IsDirty && !guardArmed)
        {
            // Escape never destroys work: arm the guard instead of closing.
            e.Cancel = true;
            ArmGuard();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Canvas.HasCaption)
        {
            // While typing, the textbox owns undo/copy; only the commit keys
            // and the app's Save extra are handled here.
            if (keyData is Keys.Escape or (Keys.Control | Keys.Enter)) { Canvas.CommitCaption(); Canvas.Focus(); RefreshChrome(); return true; }
            if (keyData is Keys.Control | Keys.S) { Save(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        switch (keyData)
        {
            // Ctrl+Z undo only — no redo, like the reference.
            case Keys.Control | Keys.Z: Canvas.Document.Undo(); Canvas.Invalidate(); RefreshChrome(); return true;
            case Keys.Control | Keys.C: Copy(); return true;
            case Keys.Control | Keys.S: Save(); return true;
            case Keys.Escape:
                // A second Escape disarms the guard rather than confirming it.
                if (guardArmed) { DisarmGuard(); return true; }
                RequestClose();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tips.Dispose();
        base.Dispose(disposing);
    }
}
