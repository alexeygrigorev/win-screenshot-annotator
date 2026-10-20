using System.Drawing;
using Annotator.Core;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
static bool Near(float a, float b) => Math.Abs(a-b) < .01;
var doc = new Document();
var line = new Mark(Tool.Arrow,[new(10,10),new(10,10)],Color.Red.ToArgb(),4);
doc.Add(line); Check(doc.Items.Count == 0 && !doc.CanUndo,"Zero-length shape consumes no undo");
doc.Add(line with { Tool = Tool.Pen }); Check(doc.Items.Count == 1,"Pen dot is retained");
var text = new Mark(Tool.Text,[new(20,20)],Color.Red.ToArgb(),4,"first\nsecond");
doc.SetText(-1,text); doc.SetText(1,text with { Text = "edited" });
doc.Undo(); Check(doc.Items[1].Text == "first\nsecond","Undo restores an edited caption");
doc.Redo(); Check(doc.Items[1].Text == "edited","Redo restores an edited caption");
doc.SetText(1,text with { Text = "" }); Check(doc.Items.Count == 1,"Empty caption deletes item");
doc.Undo(); Check(doc.Items.Count == 2,"Undo restores deleted caption");
doc.Clear(); doc.Undo(); Check(doc.Items.Count == 2,"Undo restores Clear");
doc.Undo(); doc.Add(line with {Tool = Tool.Pen}); Check(!doc.CanRedo,"New mutation clears redo");
doc.MarkSaved(); Check(!doc.IsDirty,"Successful save clears dirty state");
doc.Undo(); Check(doc.IsDirty,"Undo after save requires close confirmation");
var snap = Geometry.Snap(new(0,0),new(10,2)); Check(Near(snap.X,10)&&Near(snap.Y,0),"Snap projects onto closest axis");
snap = Geometry.Snap(new(0,0),new(10,8)); Check(Near(snap.X,9)&&Near(snap.Y,9),"Snap projects onto diagonal");
var arrow = Geometry.Arrow(new(0,0),new(10,0),12);
Check(Near(arrow.Shaft.X,4)&&Near(arrow.A.X,4),"Short thick arrow caps head at 60 percent and stops shaft at baseline");
Check(Geometry.TextSize(2)==36 && Geometry.TextSize(8)==64,"Caption remains readable at every stroke weight");
Check(Geometry.Bounds(new(50,40),new(10,20))==new RectangleF(10,20,40,20),"Reverse drag bounds");
Console.WriteLine("All core tests passed.");
