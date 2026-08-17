namespace Playroma.JRPG.Editor.WinForms.Visual;

/// <summary>Theme-aware renderer with strong checked and keyboard-focus cues.</summary>
public sealed class EditorToolStripRenderer : ToolStripProfessionalRenderer
{
    private readonly ThemePalette _palette;
    private readonly int _focusThickness;

    public EditorToolStripRenderer(ThemePalette palette, int focusThickness)
        : base(new EditorColorTable(palette))
    {
        _palette = palette;
        _focusThickness = Math.Max(1, focusThickness);
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using Brush brush = new SolidBrush(_palette.ToolbarBackground);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripButton button)
        {
            base.OnRenderButtonBackground(e);
            return;
        }

        Rectangle bounds = new(Point.Empty, e.Item.Size);
        Color background = button.Pressed
            ? _palette.ToolbarPressed
            : button.Checked
                ? _palette.Selection
                : button.Selected
                    ? _palette.ToolbarHot
                    : _palette.ToolbarBackground;
        using Brush brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, bounds);

        if (button.Checked || button.Selected)
        {
            using Pen pen = new(button.Checked ? _palette.FocusIndicator : _palette.Border, _focusThickness);
            Rectangle border = Rectangle.Inflate(bounds, -1, -1);
            e.Graphics.DrawRectangle(pen, border);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using Pen pen = new(_palette.Divider);
        if (e.Vertical)
            e.Graphics.DrawLine(pen, e.Item.Width / 2, 4, e.Item.Width / 2, e.Item.Height - 5);
        else
            e.Graphics.DrawLine(pen, 4, e.Item.Height / 2, e.Item.Width - 5, e.Item.Height / 2);
    }

    private sealed class EditorColorTable : ProfessionalColorTable
    {
        private readonly ThemePalette _palette;
        public EditorColorTable(ThemePalette palette) => _palette = palette;
        public override Color ToolStripGradientBegin => _palette.ToolbarBackground;
        public override Color ToolStripGradientMiddle => _palette.ToolbarBackground;
        public override Color ToolStripGradientEnd => _palette.ToolbarBackground;
        public override Color MenuStripGradientBegin => _palette.ToolbarBackground;
        public override Color MenuStripGradientEnd => _palette.ToolbarBackground;
        public override Color StatusStripGradientBegin => _palette.ToolbarBackground;
        public override Color StatusStripGradientEnd => _palette.ToolbarBackground;
        public override Color ImageMarginGradientBegin => _palette.RaisedSurface;
        public override Color ImageMarginGradientMiddle => _palette.RaisedSurface;
        public override Color ImageMarginGradientEnd => _palette.RaisedSurface;
        public override Color MenuItemSelected => _palette.ToolbarHot;
        public override Color MenuItemSelectedGradientBegin => _palette.ToolbarHot;
        public override Color MenuItemSelectedGradientEnd => _palette.ToolbarHot;
        public override Color MenuItemPressedGradientBegin => _palette.ToolbarPressed;
        public override Color MenuItemPressedGradientMiddle => _palette.ToolbarPressed;
        public override Color MenuItemPressedGradientEnd => _palette.ToolbarPressed;
        public override Color ButtonSelectedHighlight => _palette.ToolbarHot;
        public override Color ButtonPressedHighlight => _palette.ToolbarPressed;
        public override Color ToolStripBorder => _palette.Border;
        public override Color SeparatorDark => _palette.Divider;
        public override Color SeparatorLight => _palette.Divider;
    }
}
