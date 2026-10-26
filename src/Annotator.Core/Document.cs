using System.Drawing;

namespace Annotator.Core;

public enum Tool { Pen, Line, Arrow, Rectangle, Ellipse, Text }
public sealed record Mark(Tool Tool, PointF[] Points, int Color, float Weight, string Text = "");

// The item list is the document. Every mutation goes through Change(), which is
// what lets one Ctrl+Z cover strokes, shapes, text edits and Clear — the same
// rule as DoodleCanvas.vue's mutate(). Like the reference, history is
// undo-only: there is no redo stack and no redo shortcut.
public sealed class Document
{
    private List<Mark> items = [];
    private readonly Stack<Mark[]> undo = new();
    public IReadOnlyList<Mark> Items => items;
    public bool CanUndo => undo.Count > 0;
    public bool IsDirty { get; private set; }
    public void MarkSaved() => IsDirty = false;
    public void Change(Action<List<Mark>> action)
    {
        undo.Push(items.ToArray());
        action(items);
        IsDirty = true;
    }
    public void Add(Mark mark)
    {
        // A click with no drag commits nothing — except the pen, where a tap is
        // a dot. An invisible zero-length arrow would still eat an Undo.
        if (mark.Tool != Tool.Pen && mark.Tool != Tool.Text &&
            (mark.Points.Length < 2 || Geometry.Distance(mark.Points[0], mark.Points[^1]) < Geometry.MinLength)) return;
        Change(list => list.Add(mark));
    }
    public void SetText(int index, Mark mark)
    {
        if (index >= 0 && items[index] == mark) return;
        // A caret placed and abandoned leaves nothing behind and costs no Undo.
        if (index < 0 && Geometry.IsBlankText(mark.Text)) return;
        Change(list => {
            // Emptying an annotation deletes it. No separate delete control.
            if (index >= 0) { if (Geometry.IsBlankText(mark.Text)) list.RemoveAt(index); else list[index] = mark; }
            else list.Add(mark);
        });
    }
    public void Clear() { if (items.Count > 0) Change(list => list.Clear()); }
    public void Undo()
    {
        if (!undo.TryPop(out var previous)) return;
        items = previous.ToList(); IsDirty = true;
    }
}

// Ports of pocketshell-core src/shared/doodleGeometry.ts (pure arithmetic).
// Names and comments follow the reference; see scripts/sync-doodle-reference.sh.
public static class Geometry
{
    // ARROW (doodleGeometry.ARROW)
    public const float HeadRatio = 4;
    public const float MaxHeadFraction = 0.6f;
    public const double Spread = Math.PI / 7;
    public const float MinLength = 2;
    public const double SnapRadians = Math.PI / 4;

    // TEXT (doodleGeometry.TEXT)
    public const float TextSizeRatio = 8;
    public const float TextMinSize = 36;
    public const float LineHeightRatio = 1.3846f;
    public const float TextEdgePadding = 0.5f;
    public const float TextHitPadding = 0.25f;

    public static float Distance(PointF a, PointF b) => MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    public static bool IsDegenerateDrag(PointF from, PointF to) => Distance(from, to) < MinLength;

    // Snap onto the nearest ray at multiples of step, projected onto the ray.
    public static PointF Snap(PointF start, PointF end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var step = (float)SnapRadians;
        var snapped = MathF.Round(MathF.Atan2(dy, dx) / step) * step;
        var projected = Math.Max(0, dx * MathF.Cos(snapped) + dy * MathF.Sin(snapped));
        return new(start.X + (float)(projected * MathF.Cos(snapped)), start.Y + (float)(projected * MathF.Sin(snapped)));
    }

    public static RectangleF Bounds(PointF a, PointF b) => RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    // Geometry for an arrowhead drawn at tip, pointing away from start.
    // Head = min(4 * width, 60% of length); shaft stops at the barb baseline so
    // a fat round cap cannot poke out of the point. Null-equivalent: returns
    // false for a drag too short to have a direction.
    public static bool Arrow(PointF start, PointF tip, float weight, out PointF barbA, out PointF barbB, out PointF shaftEnd)
    {
        var length = Distance(start, tip);
        if (length < MinLength) { barbA = barbB = shaftEnd = tip; return false; }
        var angle = Math.Atan2(tip.Y - start.Y, tip.X - start.X);
        var head = Math.Min(weight * HeadRatio, length * MaxHeadFraction);
        PointF Barb(double offset) => new(
            tip.X - (float)(head * Math.Cos(angle + offset)),
            tip.Y - (float)(head * Math.Sin(angle + offset)));
        barbA = Barb(-Spread);
        barbB = Barb(Spread);
        var baseline = head * Math.Cos(Spread);
        shaftEnd = new(tip.X - (float)(baseline * Math.Cos(angle)), tip.Y - (float)(baseline * Math.Sin(angle)));
        return true;
    }

    public static float TextSize(float weight) => MathF.Round(Math.Max(weight * TextSizeRatio, TextMinSize));

    public static bool IsBlankText(string text) => string.IsNullOrWhiteSpace(text);

    // Greedy wrap on spaces with hard newline breaks; a single word wider than
    // the line breaks between characters. maxWidth <= 0 disables wrapping.
    public static string[] WrapLines(string text, float maxWidth, Func<string, float> measure)
    {
        var paragraphs = text.Split('\n');
        if (!(maxWidth > 0)) return paragraphs;
        var gross = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph == "")
            {
                gross.Add("");
                continue;
            }
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                if (line != "" && measure($"{line} {word}") > maxWidth)
                {
                    gross.Add(line);
                    line = "";
                }
                if (line == "" && measure(word) > maxWidth)
                {
                    var pieces = BreakWord(word, maxWidth, measure);
                    gross.AddRange(pieces.Take(pieces.Count - 1));
                    line = pieces.Count > 0 ? pieces[^1] : "";
                }
                else
                {
                    line = line == "" ? word : $"{line} {word}";
                }
            }
            gross.Add(line);
        }
        return [.. gross];
    }

    static List<string> BreakWord(string word, float maxWidth, Func<string, float> measure)
    {
        var pieces = new List<string>();
        var chunk = "";
        foreach (var ch in word)
        {
            var candidate = chunk + ch;
            if (chunk != "" && measure(candidate) > maxWidth)
            {
                pieces.Add(chunk);
                chunk = ch.ToString();
            }
            else
            {
                chunk = candidate;
            }
        }
        pieces.Add(chunk);
        return pieces;
    }

    public sealed record TextLayout(string[] Lines, float LineHeight, float FontSize, float X, float Y, float Width, float Height);

    // Lay text out at its origin (top of the first line). Wraps to sheetWidth
    // minus half a font of breathing room, never narrower than one em.
    public static TextLayout LayoutText(string text, PointF origin, float fontSize, float sheetWidth, Func<string, float> measure)
    {
        var maxWidth = Math.Max(fontSize, sheetWidth - origin.X - fontSize * TextEdgePadding);
        var lines = WrapLines(text, maxWidth, measure);
        var lineHeight = fontSize * LineHeightRatio;
        var width = 0f;
        foreach (var line in lines) width = Math.Max(width, measure(line));
        return new TextLayout(lines, lineHeight, fontSize, origin.X, origin.Y, width, lineHeight * lines.Length);
    }

    public static bool HitTestText(TextLayout layout, PointF point)
    {
        var pad = layout.FontSize * TextHitPadding;
        return point.X >= layout.X - pad && point.X <= layout.X + layout.Width + pad &&
               point.Y >= layout.Y - pad && point.Y <= layout.Y + layout.Height + pad;
    }
}
