using Annotator.Core;

namespace Annotator;

internal sealed class RegionSelector : Form
{
    readonly Bitmap desktop;
    Point start;
    Point end;
    bool dragging;
    public Rectangle Selection { get; private set; }
    public RegionSelector(Bitmap desktop, Rectangle bounds)
    {
        this.desktop = desktop;
        FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Bounds = bounds; TopMost = true; ShowInTaskbar = false; DoubleBuffered = true;
        Cursor = Cursors.Cross; KeyPreview = true; AutoScaleMode = AutoScaleMode.None;
        KeyDown += (_,e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(desktop, 0, 0);
        using var shade = new SolidBrush(Color.FromArgb(110,Color.Black));
        e.Graphics.FillRectangle(shade, ClientRectangle);
        var rect = Rectangle.Round(Geometry.Bounds(start,end));
        if (rect.Width > 0 && rect.Height > 0)
        {
            e.Graphics.DrawImage(desktop, rect, rect, GraphicsUnit.Pixel);
            using var pen = new Pen(Color.DeepSkyBlue,2);
            e.Graphics.DrawRectangle(pen,rect);
        }
        using var background = new SolidBrush(Color.FromArgb(230,25,28,35));
        e.Graphics.FillRectangle(background, 20,20,500,60);
        using var font = new Font("Segoe UI",12);
        e.Graphics.DrawString("Drag to capture a region · Esc to cancel\n" + (dragging ? $"{rect.Width} × {rect.Height} pixels" : "Release to open the annotation editor"),font,Brushes.White,30,26);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        start = end = e.Location; dragging = true; Capture = true; Invalidate();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) return;
        end = new Point(Math.Clamp(e.X,0,desktop.Width),Math.Clamp(e.Y,0,desktop.Height)); Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!dragging) return;
        end = new Point(Math.Clamp(e.X,0,desktop.Width),Math.Clamp(e.Y,0,desktop.Height));
        dragging = false; Capture = false;
        Selection = Rectangle.Intersect(new Rectangle(Point.Empty,desktop.Size),Rectangle.Round(Geometry.Bounds(start,end)));
        if (Selection.Width < 2 || Selection.Height < 2) { Invalidate(); return; }
        DialogResult = DialogResult.OK; Close();
    }
}
