using System.Drawing.Drawing2D;
using Annotator.Core;

namespace Annotator;

internal static class Painter
{
    public static Font TextFont(Mark mark) => new("Segoe UI",Geometry.TextSize(mark.Weight),FontStyle.Bold,GraphicsUnit.Pixel);
    public static RectangleF TextBounds(Graphics graphics, Mark mark, int imageWidth)
    {
        using var font = TextFont(mark);
        var width = Math.Max(1,imageWidth-mark.Points[0].X);
        var size = graphics.MeasureString(mark.Text,font,new SizeF(width,100000),StringFormat.GenericDefault);
        return new RectangleF(mark.Points[0],new SizeF(width,size.Height));
    }
    public static void Paint(Graphics g, Bitmap backdrop, IEnumerable<Mark> marks)
    {
        g.DrawImageUnscaled(backdrop,0,0);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        foreach (var mark in marks) PaintMark(g,mark,backdrop.Width);
    }
    public static void PaintMark(Graphics g, Mark mark, int imageWidth)
    {
        using var brush = new SolidBrush(Color.FromArgb(mark.Color));
        using var pen = new Pen(brush,mark.Weight) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var a = mark.Points[0]; var b = mark.Points[^1];
        switch (mark.Tool)
        {
            case Tool.Pen:
                if (mark.Points.Length == 1) g.FillEllipse(brush,a.X-mark.Weight/2,a.Y-mark.Weight/2,mark.Weight,mark.Weight);
                else g.DrawLines(pen,mark.Points);
                break;
            case Tool.Line: g.DrawLine(pen,a,b); break;
            case Tool.Arrow:
                var head = Geometry.Arrow(a,b,mark.Weight);
                g.DrawLine(pen,a,head.Shaft); g.FillPolygon(brush,[b,head.A,head.B]); break;
            case Tool.Rectangle:
                var rect = Geometry.Bounds(a,b); g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height); break;
            case Tool.Ellipse: g.DrawEllipse(pen,Geometry.Bounds(a,b)); break;
            case Tool.Text:
                using (var font = TextFont(mark))
                    g.DrawString(mark.Text,font,brush,TextBounds(g,mark,imageWidth),StringFormat.GenericDefault);
                break;
        }
    }
    public static Bitmap Flatten(Bitmap backdrop, IEnumerable<Mark> marks)
    {
        var output = new Bitmap(backdrop.Width,backdrop.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output); Paint(graphics,backdrop,marks); return output;
    }
}
