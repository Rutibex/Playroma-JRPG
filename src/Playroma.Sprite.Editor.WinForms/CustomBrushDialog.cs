using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class CustomBrushDialog : Form
{
    private readonly BrushMaskEditor editor = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown widthBox = new() { Minimum = 1, Maximum = 32, Value = 8, Width = 60 };
    private readonly NumericUpDown heightBox = new() { Minimum = 1, Maximum = 32, Value = 8, Width = 60 };

    public CustomBrushDialog(BrushDefinition? existing = null)
    {
        Text = "Custom Pixel Brush";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(500, 520);
        Size = new Size(620, 650);
        AutoScaleMode = AutoScaleMode.Dpi;

        FlowLayoutPanel dimensions = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };
        dimensions.Controls.Add(new Label { Text = "Width", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        dimensions.Controls.Add(widthBox);
        dimensions.Controls.Add(new Label { Text = "Height", AutoSize = true, Padding = new Padding(12, 5, 0, 0) });
        dimensions.Controls.Add(heightBox);
        Button resize = new() { Text = "Resize", AutoSize = true };
        Button clear = new() { Text = "Clear", AutoSize = true };
        Button invert = new() { Text = "Invert", AutoSize = true };
        Button fill = new() { Text = "Fill", AutoSize = true };
        dimensions.Controls.AddRange([resize, clear, invert, fill]);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        Button ok = new() { Text = "Use Brush", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([ok, cancel]);

        Controls.Add(editor);
        Controls.Add(dimensions);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        resize.Click += (_, _) => editor.ResizeMask(Decimal.ToInt32(widthBox.Value), Decimal.ToInt32(heightBox.Value));
        clear.Click += (_, _) => editor.SetAll(false);
        fill.Click += (_, _) => editor.SetAll(true);
        invert.Click += (_, _) => editor.Invert();

        if (existing is not null)
        {
            widthBox.Value = existing.Width;
            heightBox.Value = existing.Height;
            editor.LoadBrush(existing);
        }
        else editor.ResizeMask(8, 8);
    }

    public BrushDefinition Result => editor.CreateBrush();

    private sealed class BrushMaskEditor : Control
    {
        private bool[] mask = new bool[64];
        private int width = 8;
        private int height = 8;
        private bool drawValue = true;

        public BrushMaskEditor()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(48, 50, 58);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void LoadBrush(BrushDefinition brush)
        {
            width = brush.Width; height = brush.Height; mask = brush.Mask.ToArray(); Invalidate();
        }

        public void ResizeMask(int newWidth, int newHeight)
        {
            bool[] resized = new bool[checked(newWidth * newHeight)];
            int copyWidth = Math.Min(width, newWidth);
            int copyHeight = Math.Min(height, newHeight);
            for (int y = 0; y < copyHeight; y++)
                for (int x = 0; x < copyWidth; x++) resized[y * newWidth + x] = mask[y * width + x];
            width = newWidth; height = newHeight; mask = resized; Invalidate();
        }

        public void SetAll(bool value) { Array.Fill(mask, value); Invalidate(); }
        public void Invert() { for (int i = 0; i < mask.Length; i++) mask[i] = !mask[i]; Invalidate(); }
        public BrushDefinition CreateBrush() => new("Custom", width, height, mask, width / 2, height / 2);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (TryCell(e.Location, out int x, out int y))
            {
                drawValue = !mask[y * width + x];
                mask[y * width + x] = drawValue;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if ((e.Button & MouseButtons.Left) == 0 || !TryCell(e.Location, out int x, out int y)) return;
            mask[y * width + x] = drawValue;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Rectangle area = GridArea();
            int cell = Math.Max(1, Math.Min(area.Width / width, area.Height / height));
            int drawWidth = cell * width;
            int drawHeight = cell * height;
            int startX = (ClientSize.Width - drawWidth) / 2;
            int startY = (ClientSize.Height - drawHeight) / 2;
            using SolidBrush on = new(Color.FromArgb(238, 238, 242));
            using SolidBrush off = new(Color.FromArgb(76, 78, 88));
            using Pen grid = new(Color.FromArgb(120, 20, 20, 24));
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Rectangle cellRect = new(startX + x * cell, startY + y * cell, cell, cell);
                    e.Graphics.FillRectangle(mask[y * width + x] ? on : off, cellRect);
                    e.Graphics.DrawRectangle(grid, cellRect);
                }
            using Pen anchor = new(Color.FromArgb(230, 235, 70, 145), 2f);
            e.Graphics.DrawRectangle(anchor, startX + width / 2 * cell + 1, startY + height / 2 * cell + 1, Math.Max(1, cell - 2), Math.Max(1, cell - 2));
        }

        private bool TryCell(Point point, out int x, out int y)
        {
            Rectangle area = GridArea();
            int cell = Math.Max(1, Math.Min(area.Width / width, area.Height / height));
            int startX = (ClientSize.Width - cell * width) / 2;
            int startY = (ClientSize.Height - cell * height) / 2;
            x = (point.X - startX) / cell;
            y = (point.Y - startY) / cell;
            return point.X >= startX && point.Y >= startY && x >= 0 && y >= 0 && x < width && y < height;
        }

        private Rectangle GridArea() => new(12, 12, Math.Max(1, ClientSize.Width - 24), Math.Max(1, ClientSize.Height - 24));
    }
}
