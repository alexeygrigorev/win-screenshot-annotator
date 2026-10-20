using System.Drawing;

namespace Annotator.Core;

public enum Tool { Pen, Line, Arrow, Rectangle, Ellipse, Text }
public sealed record Mark(Tool Tool, PointF[] Points, int Color, float Weight, string Text = "");

// Published versions contain immutable marks. A gesture is built separately, then added once.
public sealed class Document
{
    private List<Mark> items = [];
    private readonly Stack<Mark[]> undo = new();
    private readonly Stack<Mark[]> redo = new();
    public IReadOnlyList<Mark> Items => items;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool IsDirty { get; private set; }
    public void MarkSaved() => IsDirty = false;
    public void Change(Action<List<Mark>> action)
    {
        undo.Push(items.ToArray());
        redo.Clear();
        action(items);
        IsDirty = true;
    }
    public void Add(Mark mark)
    {
        if (mark.Tool != Tool.Pen && mark.Tool != Tool.Text &&
            (mark.Points.Length < 2 || Geometry.Distance(mark.Points[0], mark.Points[^1]) < 1)) return;
        Change(list => list.Add(mark));
    }
    public void SetText(int index, Mark mark)
    {
        if (index >= 0 && items[index] == mark) return;
        if (index < 0 && string.IsNullOrWhiteSpace(mark.Text)) return;
        Change(list => {
            if (index >= 0) { if (string.IsNullOrWhiteSpace(mark.Text)) list.RemoveAt(index); else list[index] = mark; }
            else list.Add(mark);
        });
    }
    public void Clear() { if (items.Count > 0) Change(list => list.Clear()); }
    public void Undo()
    {
        if (!undo.TryPop(out var previous)) return;
        redo.Push(items.ToArray()); items = previous.ToList(); IsDirty = true;
    }
    public void Redo()
    {
        if (!redo.TryPop(out var next)) return;
        undo.Push(items.ToArray()); items = next.ToList(); IsDirty = true;
    }
}

public static class Geometry
{
    public static float Distance(PointF a, PointF b) => MathF.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y));
    public static float TextSize(float weight) => Math.Max(36, weight * 8);
    public static PointF Snap(PointF start, PointF end)
    {
        var x = end.X-start.X; var y = end.Y-start.Y;
        var angle = MathF.Round(MathF.Atan2(y,x)/(MathF.PI/4))*MathF.PI/4;
        var projection = Math.Max(0, x*MathF.Cos(angle)+y*MathF.Sin(angle));
        return new(start.X+projection*MathF.Cos(angle), start.Y+projection*MathF.Sin(angle));
    }
    public static RectangleF Bounds(PointF a, PointF b) => RectangleF.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));
    public static (PointF A, PointF B, PointF Shaft) Arrow(PointF start, PointF tip, float weight)
    {
        var length = Distance(start,tip);
        if (length < 0.001f) return (tip,tip,tip);
        var head = Math.Min(Math.Max(12,weight*4),length*0.6f);
        var ux = (tip.X-start.X)/length; var uy = (tip.Y-start.Y)/length;
        var baseline = new PointF(tip.X-ux*head,tip.Y-uy*head);
        return (new(baseline.X-uy*head*.45f,baseline.Y+ux*head*.45f),new(baseline.X+uy*head*.45f,baseline.Y-ux*head*.45f),baseline);
    }
}
