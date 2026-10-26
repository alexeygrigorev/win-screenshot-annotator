using System.Drawing;
using Annotator.Core;

// Ports of the pocketshell-core doodleGeometry decision records, pinned the
// same way: pure arithmetic with a fake measure callback, no canvas.
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
static bool Near(float a, float b) => Math.Abs(a - b) < .05f;
static float Chars(string s) => s.Length;

var doc = new Document();
var line = new Mark(Tool.Arrow, [new(10, 10), new(10, 10)], Color.Red.ToArgb(), 12);
doc.Add(line); Check(doc.Items.Count == 0 && !doc.CanUndo, "Zero-length shape consumes no undo");
doc.Add(line with { Tool = Tool.Pen }); Check(doc.Items.Count == 1, "Pen dot is retained");
var text = new Mark(Tool.Text, [new(20, 20)], Color.Red.ToArgb(), 12, "first\nsecond");
doc.SetText(-1, text); doc.SetText(1, text with { Text = "edited" });
doc.Undo(); Check(doc.Items[1].Text == "first\nsecond", "Undo restores an edited caption");
doc.SetText(1, text with { Text = "edited" });
doc.SetText(1, text with { Text = "" }); Check(doc.Items.Count == 1, "Empty caption deletes item");
doc.Undo(); Check(doc.Items.Count == 2, "Undo restores deleted caption");
doc.Clear(); doc.Undo(); Check(doc.Items.Count == 2, "Undo restores Clear");
doc.Undo(); Check(!doc.CanUndo, "History drains to empty");
doc.Add(line with { Tool = Tool.Pen }); Check(doc.Items.Count == 2, "Pen dot commits after drain");
doc.MarkSaved(); Check(!doc.IsDirty, "Successful save clears dirty state");
doc.Undo(); Check(doc.IsDirty, "Undo after save requires close confirmation");

var snap = Geometry.Snap(new(0, 0), new(10, 2)); Check(Near(snap.X, 10) && Near(snap.Y, 0), "Snap projects onto closest axis");
snap = Geometry.Snap(new(0, 0), new(10, 8)); Check(Near(snap.X, 9) && Near(snap.Y, 9), "Snap projects onto diagonal");
Check(Geometry.IsDegenerateDrag(new(0, 0), new(1, 1)), "Click-length drag is degenerate");

var ok = Geometry.Arrow(new(0, 0), new(1, 1), 12, out _, out _, out _);
Check(!ok, "Degenerate arrow has no direction");
Geometry.Arrow(new(0, 0), new(10, 0), 12, out var barbA, out _, out var shaft);
Check(Near(shaft.X, 4.59f) && Near(shaft.Y, 0), "Short thick arrow caps head at 60 percent and stops shaft at baseline");
Check(Near(barbA.X, 4.59f) && Near(barbA.Y, 2.60f), "Barbs spread at pi/7 around the tip");
Geometry.Arrow(new(0, 0), new(100, 0), 12, out _, out _, out shaft);
Check(Near(shaft.X, 100 - 48 * MathF.Cos((float)(Math.PI / 7))), "Normal arrow head is 4x the weight");

Check(Geometry.TextSize(6) == 48 && Geometry.TextSize(12) == 96 && Geometry.TextSize(24) == 192, "Caption size is 8x the weight");
Check(Geometry.TextSize(2) == 36, "Caption floor stays at 36");
Check(Geometry.IsBlankText("   ") && !Geometry.IsBlankText("x"), "Blank text is whitespace-only");

var wrapped = Geometry.WrapLines("a b c", 3, Chars);
Check(wrapped is ["a b", "c"], "Greedy wrap breaks on spaces");
Check(Geometry.WrapLines("a\nb", 100, Chars) is ["a", "b"], "Typed newline is a hard break");
Check(Geometry.WrapLines("abcdef", 2, Chars) is ["ab", "cd", "ef"], "Unbreakable word breaks between characters");
Check(Geometry.WrapLines("a b", 0, Chars) is ["a b"], "Non-positive width disables wrapping");
var layout = Geometry.LayoutText(new string('x', 40), new PointF(990, 0), 36, 1000, Chars);
Check(layout.Lines.Length == 2 && layout.Lines[0].Length == 36, "Edge caret wraps at one em, never off the sheet");
Check(Near(layout.LineHeight, 36 * 1.3846f), "Line height follows --lh-300");
var block = Geometry.LayoutText("ab", new PointF(10, 20), 36, 1000, Chars);
Check(Geometry.HitTestText(block, new PointF(11, 21)), "Click inside text hits");
Check(!Geometry.HitTestText(block, new PointF(500, 500)), "Click far away misses");
Check(Geometry.Bounds(new(50, 40), new(10, 20)) == new RectangleF(10, 20, 40, 20), "Reverse drag bounds");
Console.WriteLine("All core tests passed.");
