using Annotator.Core;
using System.Drawing.Imaging;

namespace Annotator;

internal sealed class EditorForm : Form
{
    internal AnnotationCanvas Canvas { get; }
    readonly ToolStrip toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8), Dock = DockStyle.Top };
    readonly StatusStrip status = new();
    readonly ToolStripStatusLabel statusText = new();
    readonly Dictionary<Tool,ToolStripButton> tools = [];
    bool closeArmed;
    readonly FlowLayoutPanel closePanel = new() { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(255,245,210), Visible = false, Padding = new Padding(8) };
    public EditorForm(Bitmap image)
    {
        Text = "Screenshot Annotator"; StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1200,850); MinimumSize = new Size(850,550);
        Canvas = new AnnotationCanvas(image) { Dock = DockStyle.Fill };
        foreach (var tool in Enum.GetValues<Tool>())
        {
            var button = new ToolStripButton(tool.ToString()) { CheckOnClick = false, Checked = tool == Canvas.Tool };
            button.Click += (_,_) => SelectTool(tool);
            toolbar.Items.Add(button); tools.Add(tool,button);
        }
        toolbar.Items.Add(new ToolStripSeparator());
        foreach (var color in new[] { Color.FromArgb(235,65,70), Color.FromArgb(35,145,255), Color.FromArgb(40,170,90), Color.Gold, Color.White, Color.Black })
        {
            var button = new ToolStripButton("●") { ForeColor = color, ToolTipText = color.Name, AccessibleName = $"Ink {color.Name}" };
            button.Click += (_,_) => { Canvas.CommitCaption(); Canvas.Ink = color; UpdateStatus(); };
            toolbar.Items.Add(button);
        }
        var weight = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 60, AccessibleName = "Stroke weight" };
        weight.Items.AddRange(["2 px","4 px","8 px","12 px"]); weight.SelectedIndex = 1;
        weight.SelectedIndexChanged += (_,_) => { Canvas.CommitCaption(); Canvas.Weight = new float[] {2,4,8,12}[weight.SelectedIndex]; UpdateStatus(); };
        toolbar.Items.Add(weight);
        toolbar.Items.Add(new ToolStripSeparator());
        AddButton("Undo", () => { Canvas.CommitCaption(); Canvas.Document.Undo(); RefreshDocument(); });
        AddButton("Redo", () => { Canvas.CommitCaption(); Canvas.Document.Redo(); RefreshDocument(); });
        AddButton("Clear", () => { Canvas.CommitCaption(); Canvas.Document.Clear(); RefreshDocument(); });
        AddButton("Copy", Copy);
        AddButton("Save PNG…", Save);
        status.Items.Add(statusText);
        closePanel.Controls.Add(new Label { Text = "Keep your annotations?", AutoSize = true, Padding = new Padding(0,7,8,0) });
        var keep = new Button { Text = "Keep editing", AutoSize = true };
        keep.Click += (_,_) => DisarmClose();
        var save = new Button { Text = "Save PNG…", AutoSize = true };
        save.Click += (_,_) => { if (SaveImage()) { DisarmClose(); Close(); } };
        var discard = new Button { Text = "Discard", AutoSize = true };
        discard.Click += (_,_) => { Canvas.Document.MarkSaved(); DisarmClose(); Close(); };
        closePanel.Controls.AddRange([keep,save,discard]);
        Controls.Add(Canvas); Controls.Add(closePanel); Controls.Add(status); Controls.Add(toolbar);
        Canvas.Changed += RefreshDocument;
        FormClosing += (_,e) => {
            Canvas.CommitCaption();
            if (Canvas.Document.IsDirty) { e.Cancel = true; closeArmed = true; closePanel.Visible = true; }
        };
        UpdateStatus();
    }
    void AddButton(string label, Action action) => toolbar.Items.Add(label,null,(_,_) => action());
    void SelectTool(Tool tool)
    {
        Canvas.CommitCaption(); Canvas.Tool = tool;
        foreach (var pair in tools) pair.Value.Checked = pair.Key == tool;
        Canvas.Focus(); UpdateStatus();
    }
    void RefreshDocument() { Canvas.Invalidate(); UpdateStatus(); }
    void UpdateStatus()
    {
        statusText.Text = $"{Canvas.Backdrop.Width} × {Canvas.Backdrop.Height} px   ·   {Canvas.Tool} · {Canvas.Weight}px · {Canvas.Ink.Name}   ·   Shift: snap line/arrow   ·   Ctrl+Z: undo   ·   Ctrl+C: copy   ·   Ctrl+S: save";
        Text = "Screenshot Annotator" + (Canvas.Document.IsDirty ? " *" : "");
    }
    void DisarmClose() { closeArmed = false; closePanel.Visible = false; Canvas.Focus(); }
    void Copy()
    {
        try { using var image = Canvas.Export(); Clipboard.SetImage(image); statusText.Text = "Copied annotated image to the clipboard."; }
        catch (Exception ex) { MessageBox.Show(ex.Message,"Could not copy"); }
    }
    void Save() => SaveImage();
    bool SaveImage()
    {
        Canvas.CommitCaption();
        using var dialog = new SaveFileDialog { Filter = "PNG image|*.png", DefaultExt = "png", AddExtension = true, FileName = $"Screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try
        {
            using var image = Canvas.Export(); image.Save(dialog.FileName,ImageFormat.Png);
            Canvas.Document.MarkSaved(); UpdateStatus(); statusText.Text = $"Saved {dialog.FileName}"; return true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message,"Could not save"); return false; }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Canvas.HasCaption)
        {
            if (keyData is Keys.Escape or (Keys.Control | Keys.Enter)) { Canvas.CommitCaption(); Canvas.Focus(); return true; }
            if (keyData is (Keys.Control | Keys.S)) { Save(); return true; }
            return base.ProcessCmdKey(ref msg,keyData); // TextBox owns copy and undo while typing.
        }
        switch (keyData)
        {
            case Keys.Control | Keys.S: Save(); return true;
            case Keys.Control | Keys.C: Copy(); return true;
            case Keys.Control | Keys.Z: Canvas.Document.Undo(); RefreshDocument(); return true;
            case Keys.Control | Keys.Y: Canvas.Document.Redo(); RefreshDocument(); return true;
            case Keys.Escape: if (closeArmed) DisarmClose(); else Close(); return true;
            case Keys.P: SelectTool(Tool.Pen); return true;
            case Keys.L: SelectTool(Tool.Line); return true;
            case Keys.A: SelectTool(Tool.Arrow); return true;
            case Keys.R: SelectTool(Tool.Rectangle); return true;
            case Keys.E: SelectTool(Tool.Ellipse); return true;
            case Keys.T: SelectTool(Tool.Text); return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
}
