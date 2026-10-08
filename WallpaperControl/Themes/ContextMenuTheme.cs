namespace WallpaperControl;

internal static class ContextMenuTheme
{
    internal static void Apply(ToolStrip menu, bool dark)
    {
        var renderer = new MenuRenderer(dark);
        Apply(menu, dark, renderer);
    }

    private static void Apply(ToolStrip menu, bool dark, ToolStripRenderer renderer)
    {
        menu.Renderer = renderer;
        menu.BackColor = AppTheme.PanelBackground(dark);
        menu.ForeColor = AppTheme.TextPrimary(dark);
        foreach (ToolStripItem item in menu.Items)
        {
            item.BackColor = menu.BackColor;
            item.ForeColor = menu.ForeColor;
            if (item is ToolStripMenuItem submenu)
                Apply(submenu.DropDown, dark, renderer);
        }
        menu.Invalidate();
    }

    private sealed class MenuRenderer(bool dark) : ToolStripProfessionalRenderer(new MenuColors(dark))
    {
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = AppTheme.TextPrimary(dark);
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? AppTheme.TextPrimary(dark) : AppTheme.TextSecondary(dark);
            base.OnRenderItemText(e);
        }
    }

    private sealed class MenuColors(bool dark) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => AppTheme.PanelBackground(dark);
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Color MenuBorder => dark ? AppTheme.DarkBorder : SystemColors.ControlDark;
        public override Color MenuItemBorder => MenuBorder;
        public override Color MenuItemSelected => AppTheme.SelectionBackground(dark);
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => AppTheme.ControlPressed(dark);
        public override Color MenuItemPressedGradientMiddle => MenuItemPressedGradientBegin;
        public override Color MenuItemPressedGradientEnd => MenuItemPressedGradientBegin;
        public override Color CheckBackground => MenuItemSelected;
        public override Color CheckSelectedBackground => MenuItemSelected;
        public override Color CheckPressedBackground => MenuItemPressedGradientBegin;
        public override Color SeparatorDark => MenuBorder;
        public override Color SeparatorLight => ToolStripDropDownBackground;
    }
}
