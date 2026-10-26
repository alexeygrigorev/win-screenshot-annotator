// Paints the doodle sheet. Ports pocketshell-core
// packages/ui/src/app/doodlePaint.ts (strokePath/paintText) over
// src/shared/doodleGeometry.ts, on GDI+ instead of Canvas2D.
//
// Differences that are inherent to the engine, not the port:
// - Text resolves --font-ui/--fw-semibold/--lh-300 from computed style in the
//   reference; GDI+ has no DOM, so the family ladder is probed from installed
//   fonts ("Segoe UI Variable Text" first — the family --font-ui resolves to
//   on Windows 11) and weight 600 is set as Bold (GDI has no 600 step).
// - Leading is the resolved --lh-300 ratio (1.3846); StringFormat.
//   GenericTypographic draws top-anchored like textBaseline='top'.
using System.Drawing.Drawing2D;
using Annotator.Core;

namespace Annotator;

internal static class Painter
{
    public static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    static FontFamily? textFamily;
    static FontFamily TextFamily()
    {
        if (textFamily != null) return textFamily;
        foreach (var name in new[] { "Segoe UI Variable Text", "Inter", "Segoe UI" })
        {
            try { textFamily = new FontFamily(name); return textFamily; }
            catch (ArgumentException) { }
        }
        textFamily = FontFamily.GenericSansSerif;
        return textFamily;
    }

    public static Font TextFont(Mark mark) =>
        // Reference: `${fw-semibold} ${textFontSize(w)}px ${font-ui}`.
        // GDI+ exposes only 400/700, so 600 lands on Bold — the nearest step.
        new(TextFamily(), Geometry.TextSize(mark.Weight), FontStyle.Bold, GraphicsUnit.Pixel);

    static readonly StringFormat TextFormat = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near,
        LineAlignment = StringAlignment.Near,
        FormatFlags = StringFormatFlags.MeasureTrailingSpaces,
    };

    static float Measure(Graphics g, Font font, string s)
    {
        if (s == "") return 0;
        return g.MeasureString(s, font, new PointF(0, 0), TextFormat).Width;
    }

    public static Geometry.TextLayout Layout(Graphics g, Mark mark, int imageWidth)
    {
        using var font = TextFont(mark);
        var size = Geometry.TextSize(mark.Weight);
        return Geometry.LayoutText(mark.Text, mark.Points[0], size, imageWidth, s => Measure(g, font, s));
    }

    public static RectangleF TextBounds(Graphics g, Mark mark, int imageWidth)
    {
        var layout = Layout(g, mark, imageWidth);
        return new RectangleF(layout.X, layout.Y, layout.Width, layout.Height);
    }

    public static void Paint(Graphics g, Bitmap backdrop, IEnumerable<Mark> marks)
    {
        g.DrawImageUnscaled(backdrop, 0, 0);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        foreach (var mark in marks) PaintMark(g, mark, backdrop.Width);
    }

    public static void PaintMark(Graphics g, Mark mark, int imageWidth)
    {
        using var brush = new SolidBrush(Color.FromArgb(mark.Color));
        using var pen = new Pen(brush, mark.Weight) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var a = mark.Points[0]; var b = mark.Points[^1];
        switch (mark.Tool)
        {
            case Tool.Pen:
                if (mark.Points.Length == 1)
                {
                    // A tap is a dot, not nothing.
                    g.FillEllipse(brush, a.X - mark.Weight / 2, a.Y - mark.Weight / 2, mark.Weight, mark.Weight);
                }
                else g.DrawLines(pen, FlattenPen(mark.Points));
                break;
            case Tool.Line:
                g.DrawLine(pen, a, b);
                break;
            case Tool.Arrow:
                // Head computed before the shaft: it decides where the shaft ENDS.
                if (!Geometry.Arrow(a, b, mark.Weight, out var barbA, out var barbB, out var shaftEnd)) break;
                g.DrawLine(pen, a, shaftEnd);
                g.FillPolygon(brush, [b, barbA, barbB]);
                break;
            case Tool.Rectangle:
                var rect = Geometry.Bounds(a, b);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;
            case Tool.Ellipse:
                var ell = Geometry.Bounds(a, b);
                g.DrawEllipse(pen, ell.X, ell.Y, ell.Width, ell.Height);
                break;
            case Tool.Text:
            {
                var layout = Layout(g, mark, imageWidth);
                using var font = TextFont(mark);
                for (var i = 0; i < layout.Lines.Length; i++)
                    g.DrawString(layout.Lines[i], font, brush, new PointF(layout.X, layout.Y + i * layout.LineHeight), TextFormat);
                break;
            }
        }
    }

    // Quadratic smoothing through the midpoints, exactly like the reference
    // pen branch (moveTo first, quadraticCurveTo(p[i], mid) per interior point,
    // lineTo last), flattened to a polyline GDI can draw with round joins.
    internal static PointF[] FlattenPen(PointF[] points)
    {
        if (points.Length <= 2) return points.ToArray();
        var flat = new List<PointF>(points.Length * 8) { points[0] };
        for (var i = 1; i < points.Length - 1; i++)
        {
            var ctrl = points[i];
            var next = points[i + 1];
            var end = new PointF((ctrl.X + next.X) / 2, (ctrl.Y + next.Y) / 2);
            var from = flat[^1];
            for (var s = 1; s <= 8; s++)
            {
                var t = s / 8f;
                var u = 1 - t;
                flat.Add(new PointF(
                    u * u * from.X + 2 * u * t * ctrl.X + t * t * end.X,
                    u * u * from.Y + 2 * u * t * ctrl.Y + t * t * end.Y));
            }
        }
        flat.Add(points[^1]);
        return [.. flat];
    }

    public static Bitmap Flatten(Bitmap backdrop, IEnumerable<Mark> marks)
    {
        var output = new Bitmap(backdrop.Width, backdrop.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        Paint(graphics, backdrop, marks);
        return output;
    }
}
