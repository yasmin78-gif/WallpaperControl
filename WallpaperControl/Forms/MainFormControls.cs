using System.Drawing;
using System.Windows.Forms;

namespace WallpaperControl
{
    internal sealed class MainFormComboBox : ComboBox
    {
        internal Color MutedColor = SystemColors.GrayText;

        internal MainFormComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? SystemColors.Highlight : BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            string text = e.Index >= 0 ? GetItemText(Items[e.Index]) ?? string.Empty : Text;
            TextRenderer.DrawText(e.Graphics, text, Font,
                Rectangle.Inflate(e.Bounds, -5, 0),
                !Enabled ? MutedColor : selected ? SystemColors.HighlightText : ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }
    }

    internal sealed class MainFormButton : Button
    {
        internal Color MutedColor = SystemColors.GrayText;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Enabled) return;
            using var background = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(background, ClientRectangle);
            using var border = new Pen(FlatAppearance.BorderColor);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, MutedColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
