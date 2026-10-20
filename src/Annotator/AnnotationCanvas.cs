using Annotator.Core;

namespace Annotator;

internal sealed class AnnotationCanvas : Control
{
    public Bitmap Backdrop { get; }
    public Document Document { get; } = new();
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Tool Tool { get; set; } = Tool.Arrow;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Ink { get; set; } = Color.FromArgb(235,65,70);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Weight { get; set; } = 4;
    readonly List<PointF> gesture = [];
    Mark? preview;
    TextBox? caption;
    Mark? editingMark;
    int editingIndex = -1;
    public bool HasCaption => caption != null;
    public event Action? Changed;
    public AnnotationCanvas(Bitmap image)
    {
        Backdrop = image;
        DoubleBuffered = true; TabStop = true; BackColor = Color.FromArgb(30,33,40);
        Resize += (_,_) => { CommitCaption(); Invalidate(); };
    }
    public RectangleF ImageRect
    {
        get
        {
            var scale = Math.Min(1f,Math.Min((ClientSize.Width-32f)/Backdrop.Width,(ClientSize.Height-32f)/Backdrop.Height));
            scale = Math.Max(.001f,scale);
            return new((ClientSize.Width-Backdrop.Width*scale)/2,(ClientSize.Height-Backdrop.Height*scale)/2,Backdrop.Width*scale,Backdrop.Height*scale);
        }
    }
    PointF ToImage(Point p)
    {
        var rect = ImageRect; var scale = rect.Width/Backdrop.Width;
        return new(Math.Clamp((p.X-rect.X)/scale,0,Backdrop.Width-1),Math.Clamp((p.Y-rect.Y)/scale,0,Backdrop.Height-1));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var rect = ImageRect; var state = e.Graphics.Save();
        e.Graphics.TranslateTransform(rect.X,rect.Y);
        e.Graphics.ScaleTransform(rect.Width/Backdrop.Width,rect.Height/Backdrop.Height);
        e.Graphics.SetClip(new Rectangle(0,0,Backdrop.Width,Backdrop.Height));
        Painter.Paint(e.Graphics,Backdrop,Document.Items.Where((_,i) => caption == null || i != editingIndex));
        if (preview != null) Painter.PaintMark(e.Graphics,preview,Backdrop.Width);
        e.Graphics.Restore(state);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !ImageRect.Contains(e.Location)) return;
        CommitCaption(); Focus();
        var point = ToImage(e.Location);
        if (Tool == Tool.Text) { BeginCaption(point); return; }
        gesture.Clear(); gesture.Add(point);
        preview = new Mark(Tool,gesture.ToArray(),Ink.ToArgb(),Weight);
        Capture = true; Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!Capture || gesture.Count == 0) return;
        var point = ToImage(e.Location);
        if (Tool is Tool.Line or Tool.Arrow && ModifierKeys.HasFlag(Keys.Shift)) point = Geometry.Snap(gesture[0],point);
        if (Tool == Tool.Pen) gesture.Add(point);
        else { if (gesture.Count > 1) gesture.RemoveAt(1); gesture.Add(point); }
        preview = new Mark(Tool,gesture.ToArray(),Ink.ToArgb(),Weight); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !Capture) return;
        OnMouseMove(e);
        var completed = preview;
        Capture = false;
        if (completed != null) Document.Add(completed);
        gesture.Clear(); preview = null; Invalidate(); Changed?.Invoke();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture) { gesture.Clear(); preview = null; Invalidate(); }
    }
    void BeginCaption(PointF point)
    {
        editingIndex = -1;
        using (var graphics = Graphics.FromImage(Backdrop))
            for (var i = Document.Items.Count-1; i >= 0; i--)
            {
                var mark = Document.Items[i];
                if (mark.Tool == Tool.Text && Painter.TextBounds(graphics,mark,Backdrop.Width).Contains(point)) { editingIndex = i; break; }
            }
        editingMark = editingIndex >= 0 ? Document.Items[editingIndex] : new Mark(Tool.Text,[point],Ink.ToArgb(),Weight);
        var rect = ImageRect; var scale = rect.Width/Backdrop.Width; var anchor = editingMark.Points[0];
        caption = new TextBox {
            Multiline = true, AcceptsReturn = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
            Text = editingMark.Text, ForeColor = Color.FromArgb(editingMark.Color), BackColor = Color.White,
            Font = new Font("Segoe UI",Math.Max(10,Geometry.TextSize(editingMark.Weight)*scale),FontStyle.Bold,GraphicsUnit.Pixel),
            Location = new Point((int)(rect.X+anchor.X*scale),(int)(rect.Y+anchor.Y*scale)),
            Size = new Size(Math.Max(40,(int)((Backdrop.Width-anchor.X)*scale)),Math.Max(60,Math.Min(180,(int)((Backdrop.Height-anchor.Y)*scale))))
        };
        caption.KeyDown += (_,e) => {
            if (e.KeyCode == Keys.Escape || e.Control && e.KeyCode == Keys.Enter)
            { e.SuppressKeyPress = true; e.Handled = true; CommitCaption(); Focus(); }
        };
        Controls.Add(caption); caption.Focus(); caption.SelectAll(); Invalidate();
    }
    public void CommitCaption()
    {
        if (caption == null || editingMark == null) return;
        var text = caption.Text;
        var old = caption; caption = null;
        Controls.Remove(old); var font = old.Font; old.Dispose(); font.Dispose();
        Document.SetText(editingIndex,editingMark with { Text = text });
        editingIndex = -1; editingMark = null;
        Invalidate(); Changed?.Invoke();
    }
    public Bitmap Export() { CommitCaption(); return Painter.Flatten(Backdrop,Document.Items); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { if (caption != null) { var font = caption.Font; caption.Dispose(); font.Dispose(); } Backdrop.Dispose(); }
        base.Dispose(disposing);
    }
}
