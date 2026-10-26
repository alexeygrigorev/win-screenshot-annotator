// The doodle sheet. Ports pocketshell-core
// packages/ui/src/app/components/DoodleCanvas.vue (+ useDoodleText.ts) to GDI+.
//
// Reference rules kept: document-as-item-list with undo-only history, pen
// smoothing, Shift-constrains-line/arrow-only, click-with-no-drag commits
// nothing (pen tap = dot), text stays editable (click reopens, emptying
// deletes), leaving the text tool commits, blur never commits.
//
// The sheet paints the capture at 1:1 physical pixels (no up/down scaling —
// PocketShell's 56vh fit does NOT apply here; only a region larger than the
// work area is shown scaled, via ViewScale). The form positions the sheet
// over the selected region, Snipaste-style.
using Annotator.Core;

namespace Annotator;

internal sealed class AnnotationCanvas : ScrollableControl
{
    public Bitmap Backdrop { get; }
    public Document Document { get; } = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Tool Tool { get; set; } = Tool.Pen;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Ink { get; set; } = Painter.Hex(DoodleReference.PenHex[0]);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Weight { get; set; } = DoodleReference.Weights[DoodleReference.DefaultWeightIndex];
    // <1 only when the capture is larger than the work area (see EditorForm).
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float ViewScale { get; set; } = 1;

    readonly List<PointF> gesture = [];
    Mark? preview;
    TransparentTextBox? caption;
    Mark? editingMark;
    int editingIndex = -1;
    public bool HasCaption => caption != null;
    public event Action? Changed;

    public AnnotationCanvas(Bitmap image)
    {
        Backdrop = image;
        DoubleBuffered = true;
        TabStop = true;
        BackColor = Painter.Hex(DoodleReference.Bg);
        Cursor = Cursors.Cross;
        AutoScroll = true;
        AutoScrollMinSize = image.Size;
        Scroll += (_, _) => CommitCaption();
        Resize += (_, _) => Invalidate();
    }

    public Size ImageSize => Backdrop.Size;

    PointF ToImage(Point location)
    {
        var scale = ViewScale;
        return new(
            (location.X - AutoScrollPosition.X) / scale,
            (location.Y - AutoScrollPosition.Y) / scale);
    }

    Point ToDisplay(PointF image)
    {
        var scale = ViewScale;
        return new(
            (int)(image.X * scale + AutoScrollPosition.X),
            (int)(image.Y * scale + AutoScrollPosition.Y));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        e.Graphics.ScaleTransform(ViewScale, ViewScale);
        e.Graphics.SetClip(new Rectangle(0, 0, Backdrop.Width, Backdrop.Height));
        // The item under an open caret is skipped: the editor shows the live
        // text over that rectangle, and painting both would ghost the old text.
        Painter.Paint(e.Graphics, Backdrop, Document.Items.Where((_, i) => caption == null || i != editingIndex));
        if (preview != null) Painter.PaintMark(e.Graphics, preview, Backdrop.Width);
        e.Graphics.Restore(state);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var point = ToImage(e.Location);
        if (point.X < 0 || point.Y < 0 || point.X >= Backdrop.Width || point.Y >= Backdrop.Height) return;
        CommitCaption();
        Focus();
        if (Tool == Tool.Text) { BeginCaption(point); return; }
        gesture.Clear();
        gesture.Add(point);
        preview = new Mark(Tool, gesture.ToArray(), Ink.ToArgb(), Weight);
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!Capture || gesture.Count == 0) return;
        var point = ToImage(e.Location);
        // Shift constrains line/arrow to 45-degree steps only — deliberately
        // not rectangle/ellipse (where it would mean square/circle).
        if (Tool is Tool.Line or Tool.Arrow && ModifierKeys.HasFlag(Keys.Shift)) point = Geometry.Snap(gesture[0], point);
        if (Tool == Tool.Pen) { if (Geometry.Distance(gesture[^1], point) > .1f) gesture.Add(point); }
        else { if (gesture.Count > 1) gesture.RemoveAt(1); gesture.Add(point); }
        preview = new Mark(Tool, gesture.ToArray(), Ink.ToArgb(), Weight);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !Capture) return;
        OnMouseMove(e);
        var completed = preview;
        Capture = false;
        if (completed != null) Document.Add(completed);
        gesture.Clear();
        preview = null;
        Invalidate();
        Changed?.Invoke();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) { gesture.Clear(); preview = null; Invalidate(); }
    }

    void BeginCaption(PointF point)
    {
        editingIndex = -1;
        using (var graphics = CreateGraphics())
            // Back to front: later items paint on top, so they hit first.
            for (var i = Document.Items.Count - 1; i >= 0; i--)
            {
                var mark = Document.Items[i];
                if (mark.Tool == Tool.Text && Geometry.HitTestText(Painter.Layout(graphics, mark, Backdrop.Width), point))
                { editingIndex = i; break; }
            }
        editingMark = editingIndex >= 0 ? Document.Items[editingIndex] : new Mark(Tool.Text, [point], Ink.ToArgb(), Weight);
        var anchor = editingMark.Points[0];
        // Same available width the painter wraps to, so the box breaks lines
        // where the painted result will; never narrower than one em.
        var fontSize = Geometry.TextSize(editingMark.Weight);
        var available = MathF.Max(fontSize, Backdrop.Width - anchor.X - fontSize * Geometry.TextEdgePadding);
        caption = new TransparentTextBox
        {
            Text = editingMark.Text,
            ForeColor = Color.FromArgb(editingMark.Color),
            Location = ToDisplay(anchor),
            Size = new Size((int)(available * ViewScale), 40),
        };
        SetCaptionFont();
        caption.TextChanged += (_, _) => AutoSizeCaption();
        caption.KeyDown += (_, e) =>
        {
            // Escape commits AND stops here; Ctrl+Enter commits. Enter is a newline.
            if (e.KeyCode == Keys.Escape || (e.Control && e.KeyCode == Keys.Enter))
            { e.SuppressKeyPress = true; e.Handled = true; CommitCaption(); Focus(); }
            // Ctrl+Z stays the textbox's own undo and must not reach the sheet.
        };
        Controls.Add(caption);
        AutoSizeCaption();
        caption.Focus();
        caption.SelectionStart = caption.Text.Length;
        Invalidate();
    }

    void SetCaptionFont()
    {
        if (caption == null || editingMark == null) return;
        using var face = Painter.TextFont(editingMark);
        var previous = caption.Font;
        caption.Font = new Font(face.FontFamily, face.Size * ViewScale, FontStyle.Bold, GraphicsUnit.Pixel);
        // The inherited control font belongs to WinForms.
    }

    void AutoSizeCaption()
    {
        if (caption == null) return;
        var size = caption.GetPreferredSize(new Size(caption.Width, 0));
        caption.Height = Math.Max(size.Height, 10);
    }

    // Picking a colour or weight with a caret open retargets the annotation.
    public void RetargetEditing(Color ink, float weight)
    {
        if (caption == null || editingMark == null) return;
        editingMark = editingMark with { Color = ink.ToArgb(), Weight = weight };
        caption.ForeColor = ink;
        SetCaptionFont();
        AutoSizeCaption();
    }

    public void CommitCaption()
    {
        if (caption == null || editingMark == null) return;
        var text = caption.Text;
        var box = caption;
        caption = null;
        Controls.Remove(box);
        var font = box.Font;
        box.Dispose();
        font.Dispose();
        var saved = editingMark with { Text = text };
        Document.SetText(editingIndex, saved);
        editingIndex = -1;
        editingMark = null;
        Invalidate();
        Changed?.Invoke();
    }

    public Bitmap Export() { CommitCaption(); return Painter.Flatten(Backdrop, Document.Items); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (caption != null) { var font = caption.Font; caption.Dispose(); font.Dispose(); }
            Backdrop.Dispose();
        }
        base.Dispose(disposing);
    }

    // Borderless, background-transparent editor: no chrome the export does not
    // have. WS_EX_TRANSPARENT asks the canvas (which paints the capture) to
    // paint first underneath, so the caret floats over the picture like the
    // reference <textarea>.
    sealed class TransparentTextBox : TextBox
    {
        public TransparentTextBox()
        {
            BorderStyle = BorderStyle.None;
            Multiline = true;
            AcceptsReturn = true;
            WordWrap = true;
            ScrollBars = ScrollBars.None;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
    }
}
