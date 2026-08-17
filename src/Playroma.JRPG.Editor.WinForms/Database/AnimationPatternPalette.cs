using Playroma.JRPG.AnimationEditing;

namespace Playroma.JRPG.Editor.WinForms.Database;

internal sealed class AnimationPatternSelectedEventArgs(int pattern) : EventArgs
{
    public int Pattern { get; } = pattern;
}

internal sealed class AnimationPatternPalette : ScrollableControl
{
    private const int PreferredDisplayCell = 92;
    private const int MinimumDisplayCell = 56;
    private int _displayCell = PreferredDisplayCell;
    private Bitmap? _sheet1;
    private Bitmap? _sheet2;
    private int _selectedPattern = -1;
    private int _activeSheet;

    public AnimationPatternPalette()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(40, 43, 50);
        ForeColor = Color.WhiteSmoke;
        TabStop = true;
        UpdateScrollSize();
    }

    public event EventHandler<AnimationPatternSelectedEventArgs>? PatternSelected;
    public event EventHandler<AnimationPatternSelectedEventArgs>? PatternActivated;

    public int ActiveSheet
    {
        get => _activeSheet;
        set { _activeSheet = value == 1 ? 1 : 0; UpdateScrollSize(); Invalidate(); }
    }

    public int SelectedPattern
    {
        get => _selectedPattern;
        set { _selectedPattern = value; Invalidate(); }
    }

    public void SetSheets(Bitmap? first, Bitmap? second)
    {
        Bitmap? old1 = _sheet1;
        Bitmap? old2 = _sheet2;
        _sheet1 = first;
        _sheet2 = second;
        if (!ReferenceEquals(old1, first)) old1?.Dispose();
        if (!ReferenceEquals(old2, second)) old2?.Dispose();
        UpdateScrollSize();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sheet1?.Dispose();
            _sheet2?.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        Bitmap? sheet = ActiveSheet == 0 ? _sheet1 : _sheet2;
        int basePattern = ActiveSheet == 0 ? 0 : 100;
        const int runtimeRows = 100 / MvAnimationConstants.RuntimeColumns;
        using Pen grid = new(Color.FromArgb(100, 170, 180, 195));
        using Pen selected = new(Color.Gold, 3f);
        using Pen missing = new(Color.IndianRed, 2f);
        using SolidBrush labelBack = new(Color.FromArgb(180, 20, 22, 28));
        using SolidBrush labelText = new(Color.WhiteSmoke);
        for (int row = 0; row < runtimeRows; row++)
        {
            for (int column = 0; column < MvAnimationConstants.RuntimeColumns; column++)
            {
                int local = row * MvAnimationConstants.RuntimeColumns + column;
                int pattern = basePattern + local;
                Rectangle destination = new(column * _displayCell, row * _displayCell, _displayCell, _displayCell);
                Rectangle content = Rectangle.Inflate(destination, -3, -3);
                MvAnimationPatternCell mapped = MvAnimationSheetMapper.Map(pattern);
                bool sourceExists = sheet is not null
                    && mapped.SourceX + mapped.Width <= sheet.Width
                    && mapped.SourceY + mapped.Height <= sheet.Height;
                if (sourceExists)
                {
                    e.Graphics.DrawImage(sheet!, content, new Rectangle(mapped.SourceX, mapped.SourceY, mapped.Width, mapped.Height), GraphicsUnit.Pixel);
                }
                else
                {
                    e.Graphics.FillRectangle(Brushes.DimGray, content);
                    e.Graphics.DrawLine(missing, content.Left, content.Top, content.Right, content.Bottom);
                    e.Graphics.DrawLine(missing, content.Right, content.Top, content.Left, content.Bottom);
                }
                e.Graphics.DrawRectangle(pattern == SelectedPattern ? selected : grid, destination);
                Rectangle label = new(destination.Left + 2, destination.Bottom - 20, 42, 18);
                e.Graphics.FillRectangle(labelBack, label);
                e.Graphics.DrawString(pattern.ToString(), Font, labelText, label.Location);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        int x = e.X - AutoScrollPosition.X;
        int y = e.Y - AutoScrollPosition.Y;
        int column = x / _displayCell;
        int row = y / _displayCell;
        if (column is < 0 or >= MvAnimationConstants.RuntimeColumns || row < 0) return;
        int pattern = (ActiveSheet == 0 ? 0 : 100) + row * MvAnimationConstants.RuntimeColumns + column;
        SelectedPattern = pattern;
        PatternSelected?.Invoke(this, new AnimationPatternSelectedEventArgs(pattern));
        if (e.Clicks > 1) PatternActivated?.Invoke(this, new AnimationPatternSelectedEventArgs(pattern));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollSize();
        Invalidate();
    }

    private void UpdateScrollSize()
    {
        const int runtimeRows = 100 / MvAnimationConstants.RuntimeColumns;

        // Fit all five RPG Maker pattern columns into the pane whenever possible.
        // The old fixed 92 px cells forced a horizontal scrollbar in the new side
        // browser and hid much of each sheet. Larger panes still receive the original
        // 92 px previews, while narrower panes scale down only as far as 56 px.
        int scrollbarAllowance = SystemInformation.VerticalScrollBarWidth + 4;
        int availableWidth = Math.Max(0, ClientSize.Width - scrollbarAllowance);
        int fittedCell = availableWidth / MvAnimationConstants.RuntimeColumns;
        _displayCell = Math.Clamp(fittedCell, MinimumDisplayCell, PreferredDisplayCell);
        AutoScrollMinSize = new Size(
            MvAnimationConstants.RuntimeColumns * _displayCell + 2,
            runtimeRows * _displayCell + 2);
    }
}
