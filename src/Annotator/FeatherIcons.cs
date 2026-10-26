// Feather 4.29 icon paths (MIT), verbatim from pocketshell-core
// packages/ui/src/components/AppIcon.vue GEOMETRY. One 24x24 viewBox per
// mark, stroke currentColor, stroke-width 2, round caps/joins — the same
// register as DoodleCanvas.vue's toolbar (edit-2, minus, arrow-right,
// square, circle, type, rotate-ccw, trash-2).
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Annotator;

internal static class FeatherIcons
{
    public static void Draw(Graphics g, string name, RectangleF bounds, Color color, float opacity = 1)
    {
        if (!DoodleReference.IconPaths.TryGetValue(name, out var paths)) return;
        using var path = new GraphicsPath();
        foreach (var d in paths) Parse(d, path);
        // 24-grid path data mapped onto the target box.
        var scale = Math.Min(bounds.Width, bounds.Height) / 24f;
        var state = g.Save();
        try
        {
            g.TranslateTransform(bounds.X + (bounds.Width - 24 * scale) / 2, bounds.Y + (bounds.Height - 24 * scale) / 2);
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var alpha = (int)(color.A * Math.Clamp(opacity, 0, 1));
            using var pen = new Pen(Color.FromArgb(alpha, color), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawPath(pen, path);
        }
        finally { g.Restore(state); }
    }

    // Minimal SVG path parser: the Feather subset used by the 8 toolbar icons
    // (M L H V C A Z, relative and absolute). Numbers follow CSS syntax.
    static void Parse(string d, GraphicsPath path)
    {
        var tokens = Tokenize(d);
        int i = 0;
        char cmd = 'M';
        float cx = 0, cy = 0, sx = 0, sy = 0;
        while (i < tokens.Count)
        {
            if (IsCommand(tokens[i])) cmd = tokens[i++][0];
            bool rel = char.IsLower(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                {
                    var x = Num(tokens[i++]); var y = Num(tokens[i++]);
                    if (rel) { x += cx; y += cy; }
                    cx = sx = x; cy = sy = y;
                    path.StartFigure();
                    // Extra coordinate pairs after M are implicit L.
                    cmd = rel ? 'l' : 'L';
                    break;
                }
                case 'L':
                {
                    var x = Num(tokens[i++]); var y = Num(tokens[i++]);
                    if (rel) { x += cx; y += cy; }
                    path.AddLine(cx, cy, x, y);
                    cx = x; cy = y;
                    break;
                }
                case 'H':
                {
                    var x = Num(tokens[i++]);
                    if (rel) x += cx;
                    path.AddLine(cx, cy, x, cy);
                    cx = x;
                    break;
                }
                case 'V':
                {
                    var y = Num(tokens[i++]);
                    if (rel) y += cy;
                    path.AddLine(cx, cy, cx, y);
                    cy = y;
                    break;
                }
                case 'C':
                {
                    var x1 = Num(tokens[i++]); var y1 = Num(tokens[i++]);
                    var x2 = Num(tokens[i++]); var y2 = Num(tokens[i++]);
                    var x = Num(tokens[i++]); var y = Num(tokens[i++]);
                    if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                    path.AddBezier(cx, cy, x1, y1, x2, y2, x, y);
                    cx = x; cy = y;
                    break;
                }
                case 'A':
                {
                    var rx = Num(tokens[i++]); var ry = Num(tokens[i++]);
                    Num(tokens[i++]); // x-axis rotation: 0 in every Feather icon we use
                    var large = Num(tokens[i++]) != 0;
                    var sweep = Num(tokens[i++]) != 0;
                    var x = Num(tokens[i++]); var y = Num(tokens[i++]);
                    if (rel) { x += cx; y += cy; }
                    AppendArc(path, cx, cy, x, y, rx, ry, large, sweep);
                    cx = x; cy = y;
                    break;
                }
                case 'Z':
                    path.CloseFigure();
                    cx = sx; cy = sy;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported path command '{cmd}'");
            }
        }
    }

    // Endpoint-to-center arc conversion (SVG F.16.5). Feather arcs here are
    // circular (rx == ry); the code handles the general case anyway.
    static void AppendArc(GraphicsPath path, float x1, float y1, float x2, float y2, float rx, float ry, bool large, bool sweep)
    {
        rx = Math.Abs(rx); ry = Math.Abs(ry);
        if (rx < 1e-6f || ry < 1e-6f || (x1 == x2 && y1 == y2))
        {
            path.AddLine(x1, y1, x2, y2);
            return;
        }
        var dx = (x1 - x2) / 2; var dy = (y1 - y2) / 2;
        var lambda = dx * dx / (rx * rx) + dy * dy / (ry * ry);
        if (lambda > 1) { var s = MathF.Sqrt((float)lambda); rx *= s; ry *= s; }
        var num = rx * rx * ry * ry - rx * rx * dy * dy - ry * ry * dx * dx;
        var den = rx * rx * dy * dy + ry * ry * dx * dx;
        var f = num <= 0 ? 0 : MathF.Sqrt(num / Math.Max(den, 1e-9f));
        if (large == sweep) f = -f;
        var cxp = f * rx * dy / ry; var cyp = -f * ry * dx / rx;
        var cx = cxp + (x1 + x2) / 2; var cy = cyp + (y1 + y2) / 2;
        double Ang(double ux, double uy, double vx, double vy)
        {
            var dot = ux * vx + uy * vy;
            var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            var c = Math.Clamp(dot / Math.Max(len, 1e-9), -1, 1);
            var a = Math.Acos(c);
            return ux * vy - uy * vx < 0 ? -a : a;
        }
        var start = Ang(1, 0, (dx - cxp) / rx, (dy - cyp) / ry);
        var delta = Ang((dx - cxp) / rx, (dy - cyp) / ry, (-dx - cxp) / rx, (-dy - cyp) / ry);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        if (sweep && delta < 0) delta += 2 * Math.PI;
        var startDeg = (float)(start * 180 / Math.PI);
        var sweepDeg = (float)(delta * 180 / Math.PI);
        path.AddArc(cx - rx, cy - ry, 2 * rx, 2 * ry, startDeg, sweepDeg);
    }

    static List<string> Tokenize(string d)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < d.Length)
        {
            var c = d[i];
            if (char.IsWhiteSpace(c) || c == ',') { i++; continue; }
            if (char.IsLetter(c)) { tokens.Add(c.ToString()); i++; continue; }
            int j = i;
            if (d[j] == '+' || d[j] == '-') j++;
            while (j < d.Length && (char.IsDigit(d[j]) || d[j] == '.')) j++;
            if (j < d.Length && (d[j] == 'e' || d[j] == 'E'))
            {
                j++;
                if (j < d.Length && (d[j] == '+' || d[j] == '-')) j++;
                while (j < d.Length && char.IsDigit(d[j])) j++;
            }
            tokens.Add(d[i..j]);
            i = j;
        }
        return tokens;
    }

    static bool IsCommand(string t) => t.Length == 1 && char.IsLetter(t[0]);
    static float Num(string t) => float.Parse(t, CultureInfo.InvariantCulture);
}
