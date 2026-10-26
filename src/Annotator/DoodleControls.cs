// PocketShell-styled toolbar and footer controls. Visual language copied from
// DoodleCanvas.vue scoped CSS: 24px tool buttons, 14px Feather glyphs,
// 14px colour chips with a ring for the selected pen, weight dots capped at
// 16px, state-selected fill with a strong border and a cyan glyph.
using System.Drawing.Drawing2D;

namespace Annotator;

internal static class DoodleColors
{
    public static Color Hex(string hex) => ColorTranslator.FromHtml(hex);
    static Color Over(Color top, Color bottom)
    {
        float a = top.A / 255f;
        return Color.FromArgb(
            (int)(top.R * a + bottom.R * (1 - a)),
            (int)(top.G * a + bottom.G * (1 - a)),
            (int)(top.B * a + bottom.B * (1 - a)));
    }

    public static readonly Color Bg = Hex(DoodleReference.Bg);
    public static readonly Color Surface2 = Hex(DoodleReference.Surface2);
    public static readonly Color Fg = Hex(DoodleReference.Fg);
    public static readonly Color FgSecondary = Hex(DoodleReference.FgSecondary);
    public static readonly Color Border = Hex(DoodleReference.Border);
    public static readonly Color BorderStrong = Hex(DoodleReference.BorderStrong);
    public static readonly Color Accent = Hex(DoodleReference.Accent);
    public static readonly Color OnAccent = Hex(DoodleReference.OnAccent);
    public static readonly Color Error = Hex(DoodleReference.Error);
    // --accent-soft over --surface-2 (state-selected fill, pre-composited).
    public static readonly Color SelectedFill = Over(Color.FromArgb(31, 34, 211, 238), Surface2);
    // --state-hover over --surface-2.
    public static readonly Color HoverFill = Over(Color.FromArgb(13, 230, 237, 243), Surface2);
    // --error-soft over --bg (discard guard fill, pre-composited).
    public static readonly Color GuardFill = Over(Color.FromArgb(31, 239, 68, 68), Bg);
}

internal abstract class DoodleToolButton : Control
{
    bool hover;
    bool active;
    public bool Active
    {
        get => active;
        set { if (active != value) { active = value; Invalidate(); } }
    }

    protected DoodleToolButton()
    {
        Size = new Size(DoodleReference.ControlHSm, DoodleReference.ControlHSm);
        TabStop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    protected void PaintChrome(Graphics g)
    {
        if (Active)
        {
            using var fill = new SolidBrush(DoodleColors.SelectedFill);
            g.FillRounded(BackgroundPaintRect(), DoodleReference.RadiusSm, fill);
            using var border = new Pen(DoodleColors.BorderStrong);
            g.DrawRounded(BackgroundPaintRect(), DoodleReference.RadiusSm, border);
        }
        else if (hover)
        {
            using var fill = new SolidBrush(DoodleColors.HoverFill);
            g.FillRounded(BackgroundPaintRect(), DoodleReference.RadiusSm, fill);
        }
        if (Focused)
        {
            using var focus = new Pen(DoodleColors.Accent, 2);
            g.DrawRounded(BackgroundPaintRect(), DoodleReference.RadiusSm, focus);
        }
    }

    Rectangle BackgroundPaintRect() => new(0, 0, Width - 1, Height - 1);
    protected Color GlyphColor() => !Enabled ? Color.FromArgb(115, DoodleColors.FgSecondary) : Active ? DoodleColors.Accent : hover ? DoodleColors.Fg : DoodleColors.FgSecondary;

    protected override void OnPaintBackground(PaintEventArgs e) { }
}

internal static class GraphicsRound
{
    public static void FillRounded(this Graphics g, Rectangle r, int radius, Brush brush)
    {
        using var path = Rounded(r, radius);
        g.FillPath(brush, path);
    }
    public static void DrawRounded(this Graphics g, Rectangle r, int radius, Pen pen)
    {
        using var path = Rounded(r, radius);
        g.DrawPath(pen, path);
    }
    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// 24px button with a 14px Feather glyph (edit-2, minus, arrow-right, square,
// circle, type, rotate-ccw, trash-2).
internal sealed class IconButton : DoodleToolButton
{
    public string IconName { get; set; } = "";
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        PaintChrome(e.Graphics);
        var opacity = !Enabled ? 0.45f : 1f;
        var box = new RectangleF((Width - 14) / 2f, (Height - 14) / 2f, 14, 14);
        FeatherIcons.Draw(e.Graphics, IconName, box, GlyphColor(), opacity);
    }
}

// 24px button with a 14px round colour chip; the selected pen shows a ring.
internal sealed class SwatchButton : DoodleToolButton
{
    public Color PenColor { get; set; } = Color.Red;
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (!Active && IsHover()) PaintChrome(e.Graphics);
        else if (Focused) PaintChrome(e.Graphics);
        using var fill = new SolidBrush(!Enabled ? Color.FromArgb(115, PenColor) : PenColor);
        var box = new RectangleF((Width - 14) / 2f, (Height - 14) / 2f, 14, 14);
        e.Graphics.FillEllipse(fill, box);
        if (Active)
        {
            using var ring = new Pen(DoodleColors.Fg, 1.5f);
            e.Graphics.DrawEllipse(ring, box);
        }
    }
    bool IsHover() => ClientRectangle.Contains(PointToClient(Cursor.Position));
}

// One weight rung: the dot previews the weight (capped at 16px because the
// button is 24px — ordering survives the cap).
internal sealed class WeightButton : DoodleToolButton
{
    public float Weight { get; set; } = 12;
    // --dot: min(round(w / 1.5) + 2, 16)
    public static float DotFor(float w) => Math.Min(MathF.Round(w / 1.5f) + 2, 16);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        PaintChrome(e.Graphics);
        var dot = DotFor(Weight);
        using var fill = new SolidBrush(GlyphColor());
        e.Graphics.FillEllipse(fill, (Width - dot) / 2f, (Height - dot) / 2f, dot, dot);
    }
}

internal sealed class ToolbarSeparator : Control
{
    public ToolbarSeparator()
    {
        Size = new Size(1, DoodleReference.ControlHSm);
        Margin = new Padding(DoodleReference.Sp1, 0, DoodleReference.Sp1, 0);
        TabStop = false;
        SetStyle(ControlStyles.Selectable, false);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(DoodleColors.Border);
        e.Graphics.DrawLine(pen, 0, 0, 0, Height);
    }
}

internal sealed class DoodleButton : Button
{
    public enum Kind { Default, Primary, Danger }
    public Kind ButtonKind { get; set; } = Kind.Default;
    public DoodleButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 1;
        Height = DoodleReference.ControlH;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12, 0, 12, 0);
        Font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        ApplyKind();
    }
    public void ApplyKind()
    {
        switch (ButtonKind)
        {
            case Kind.Primary:
                BackColor = DoodleColors.Accent;
                ForeColor = DoodleColors.OnAccent;
                FlatAppearance.BorderColor = DoodleColors.BorderStrong;
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold, GraphicsUnit.Pixel);
                break;
            case Kind.Danger:
                BackColor = DoodleColors.GuardFill;
                ForeColor = DoodleColors.Error;
                FlatAppearance.BorderColor = DoodleColors.BorderStrong;
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Regular, GraphicsUnit.Pixel);
                break;
            default:
                BackColor = DoodleColors.Surface2;
                ForeColor = DoodleColors.Fg;
                FlatAppearance.BorderColor = DoodleColors.BorderStrong;
                break;
        }
        FlatAppearance.MouseOverBackColor = ButtonKind == Kind.Primary ? BackColor : DoodleColors.HoverFill;
    }
}
