using System.Drawing;
using System.Windows.Forms;

namespace RtlFix.App;

/// <summary>
/// Draws text the way a renderer with no bidirectional support does: one code point at a time, each
/// placed strictly to the right of the previous one. Windows reorders anything right-to-left before
/// it reaches a normal control, so a preview of visually reordered text has to bypass that to show
/// what Notepad, a terminal or a game chat will really put on screen.
/// </summary>
sealed class DumbRenderBox : Control
{
    string[] lines = [""];

    public DumbRenderBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>The text to draw, one code point at a time.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Display
    {
        set
        {
            lines = value.Replace("\r", string.Empty).Split('\n');
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        var top = Padding.Top;
        foreach (var line in lines)
        {
            var x = Padding.Left;
            var height = 0;
            foreach (var character in line)
            {
                var glyph = character.ToString();
                var size = TextRenderer.MeasureText(e.Graphics, glyph, Font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, glyph, Font, new Point(x, top), ForeColor,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                x += size.Width;
                height = Math.Max(height, size.Height);
            }
            top += Math.Max(height, Font.Height);
        }

        e.Graphics.DrawRectangle(SystemPens.ControlDark, 0, 0, Width - 1, Height - 1);
    }
}
