using System.Drawing.Drawing2D;
using Playroma.JRPG.DatabaseEditing;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class TroopFormationCanvas : Control
{
    private IReadOnlyList<MvTroopMemberSnapshot> _members = [];
    private IReadOnlyDictionary<int, string> _enemyNames = new Dictionary<int, string>();
    // The canvas owns private copies of all images it paints. Database editors can
    // replace/dispose their source bitmaps during asynchronous rebinding without
    // invalidating a queued canvas paint.
    private readonly Dictionary<int, Bitmap> _images = [];
    private Bitmap? _battleback1;
    private Bitmap? _battleback2;
    private string _battlebackCaption = "No battleback selected";
    private readonly HashSet<int> _selection = [];
    private BattleCanvasProfile _profile = BattleCanvasProfile.MvDefault;
    private int? _dragIndex;
    private Point _lastMouse;
    private float _zoom = 1f;
    private PointF _pan;

    public TroopFormationCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(40, 44, 52);
        ForeColor = Color.White;
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        MouseDown += OnCanvasMouseDown;
        MouseMove += OnCanvasMouseMove;
        MouseUp += (_, _) => { _dragIndex = null; Capture = false; };
        MouseWheel += OnCanvasMouseWheel;
        KeyDown += OnCanvasKeyDown;
    }

    public event EventHandler? SelectionChanged;
    public event EventHandler<TroopMembersChangedEventArgs>? MembersChanged;
    public IReadOnlySet<int> SelectedIndexes => _selection;
    public IReadOnlyList<MvTroopMemberSnapshot> Members => _members;
    public int LoadedImageCount => _images.Count;

    public void SetProfile(BattleCanvasProfile profile)
    {
        _profile = profile ?? BattleCanvasProfile.MvDefault;
        Invalidate();
    }

    public void SetBattlebacks(Bitmap? battleback1, Bitmap? battleback2, string? caption = null)
    {
        ReplaceOwnedBitmap(ref _battleback1, battleback1);
        ReplaceOwnedBitmap(ref _battleback2, battleback2);
        _battlebackCaption = string.IsNullOrWhiteSpace(caption) ? "No battleback selected" : caption;
        Invalidate();
    }

    public void SetMembers(
        IReadOnlyList<MvTroopMemberSnapshot> members,
        IReadOnlyDictionary<int, string> enemyNames,
        IReadOnlyDictionary<int, Bitmap>? images = null)
    {
        _members = members ?? [];
        _enemyNames = enemyNames ?? new Dictionary<int, string>();
        ReplaceOwnedImages(images);
        _selection.RemoveWhere(index => _members.All(member => member.Index != index));
        Invalidate();
    }

    /// <summary>Releases the canvas-owned battler copies.</summary>
    public void ClearImages()
    {
        DisposeOwnedImages();
        Invalidate();
    }

    public void ClearVisuals()
    {
        _members = [];
        _enemyNames = new Dictionary<int, string>();
        DisposeOwnedImages();
        _selection.Clear();
        DisposeOwnedBitmap(ref _battleback1);
        DisposeOwnedBitmap(ref _battleback2);
        _battlebackCaption = "No battleback selected";
        Invalidate();
    }

    public void SelectOnly(int? index)
    {
        _selection.Clear();
        if (index.HasValue && _members.Any(member => member.Index == index.Value)) _selection.Add(index.Value);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    public void SelectAll()
    {
        _selection.Clear();
        foreach (MvTroopMemberSnapshot member in _members) _selection.Add(member.Index);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeOwnedImages();
            DisposeOwnedBitmap(ref _battleback1);
            DisposeOwnedBitmap(ref _battleback2);
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        RectangleF logical = LogicalBounds();
        using Brush background = new SolidBrush(Color.FromArgb(54, 59, 70));
        e.Graphics.FillRectangle(background, logical);
        DrawBattleback(e.Graphics, logical);
        DrawGrid(e.Graphics, logical);
        foreach (MvTroopMemberSnapshot member in _members)
        {
            DrawMember(e.Graphics, member);
        }
        using Pen border = new(Color.FromArgb(170, 180, 195));
        e.Graphics.DrawRectangle(border, Rectangle.Round(logical));
        string caption = $"{_profile.LogicalWidth}×{_profile.LogicalHeight} • {_battlebackCaption} • {Math.Round(_zoom * 100)}%";
        TextRenderer.DrawText(e.Graphics, caption, Font, new Point(8, 8), Color.Gainsboro);
    }

    private void DrawBattleback(Graphics graphics, RectangleF bounds)
    {
        if (_battleback1 is null && _battleback2 is null)
        {
            using LinearGradientBrush fallback = new(
                bounds,
                Color.FromArgb(48, 69, 79),
                Color.FromArgb(30, 34, 42),
                LinearGradientMode.Vertical);
            graphics.FillRectangle(fallback, bounds);
            using Brush floor = new SolidBrush(Color.FromArgb(90, 88, 82));
            graphics.FillRectangle(floor, bounds.Left, bounds.Top + bounds.Height * 0.68f, bounds.Width, bounds.Height * 0.32f);
            return;
        }

        if (_battleback1 is not null) DrawCover(graphics, _battleback1, bounds);
        if (_battleback2 is not null) DrawCover(graphics, _battleback2, bounds);
    }

    private static void DrawCover(Graphics graphics, Bitmap image, RectangleF bounds)
    {
        if (!TryGetBitmapSize(image, out int imageWidth, out int imageHeight)
            || bounds.Width <= 0
            || bounds.Height <= 0)
        {
            return;
        }

        float scale = Math.Max(bounds.Width / imageWidth, bounds.Height / imageHeight);
        float sourceWidth = bounds.Width / scale;
        float sourceHeight = bounds.Height / scale;
        float sourceX = (imageWidth - sourceWidth) / 2f;
        float sourceY = (imageHeight - sourceHeight) / 2f;
        RectangleF source = new(sourceX, sourceY, sourceWidth, sourceHeight);
        try
        {
            graphics.DrawImage(image, bounds, source, GraphicsUnit.Pixel);
        }
        catch (ArgumentException)
        {
            // The owner replaced/disposed the battleback during a queued repaint.
        }
    }

    private void DrawGrid(Graphics graphics, RectangleF bounds)
    {
        using Pen grid = new(Color.FromArgb(54, 255, 255, 255));
        for (int x = 0; x <= _profile.LogicalWidth; x += 48)
        {
            float sx = bounds.Left + x * _zoom;
            graphics.DrawLine(grid, sx, bounds.Top, sx, bounds.Bottom);
        }
        for (int y = 0; y <= _profile.LogicalHeight; y += 48)
        {
            float sy = bounds.Top + y * _zoom;
            graphics.DrawLine(grid, bounds.Left, sy, bounds.Right, sy);
        }
    }

    private void DrawMember(Graphics graphics, MvTroopMemberSnapshot member)
    {
        RectangleF box = MemberBounds(member);
        bool selected = _selection.Contains(member.Index);
        if (TryGetMemberImage(member.Index, out Bitmap image, out int imageWidth, out int imageHeight))
        {
            ColorMatrixOpacity(graphics, image, imageWidth, imageHeight, box, member.Hidden ? 0.45f : 1f);
        }
        else
        {
            using Brush fill = new SolidBrush(member.Hidden ? Color.FromArgb(95, 95, 105) : Color.FromArgb(105, 70, 135));
            graphics.FillRoundedRectangle(fill, box, 8f);
        }
        using Pen outline = new(selected ? Color.Gold : member.Hidden ? Color.SlateGray : Color.WhiteSmoke, selected ? 3f : 1.5f);
        graphics.DrawRoundedRectangle(outline, box, 8f);
        string name = _enemyNames.TryGetValue(member.EnemyId, out string? resolved) ? resolved : $"Enemy #{member.EnemyId}";
        string label = $"#{member.Index + 1} {name}{(member.Hidden ? " [Hidden]" : string.Empty)}";
        Rectangle textBounds = Rectangle.Round(new RectangleF(box.Left - 30, box.Bottom + 2, box.Width + 60, 34));
        TextRenderer.DrawText(graphics, label, Font, textBounds, selected ? Color.Gold : Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    private static void ColorMatrixOpacity(
        Graphics graphics,
        Bitmap image,
        int imageWidth,
        int imageHeight,
        RectangleF destination,
        float opacity)
    {
        using System.Drawing.Imaging.ImageAttributes attributes = new();
        System.Drawing.Imaging.ColorMatrix matrix = new() { Matrix33 = opacity };
        attributes.SetColorMatrix(matrix);
        try
        {
            graphics.DrawImage(
                image,
                Rectangle.Round(destination),
                0,
                0,
                imageWidth,
                imageHeight,
                GraphicsUnit.Pixel,
                attributes);
        }
        catch (ArgumentException)
        {
            // A stale queued paint may observe an image just after its owner released it.
            // The placeholder/outline still makes the member editable.
        }
    }

    private RectangleF MemberBounds(MvTroopMemberSnapshot member)
    {
        RectangleF logical = LogicalBounds();
        float width = 96 * _zoom;
        float height = 96 * _zoom;
        if (TryGetMemberImage(member.Index, out _, out int imageWidth, out int imageHeight))
        {
            float scale = Math.Min(160f / imageWidth, 160f / imageHeight) * _zoom;
            width = Math.Max(38f * _zoom, imageWidth * scale);
            height = Math.Max(38f * _zoom, imageHeight * scale);
        }
        float x = logical.Left + member.X * _zoom - width / 2f;
        float y = logical.Top + member.Y * _zoom - height;
        return new RectangleF(x, y, width, height);
    }

    private void ReplaceOwnedImages(IReadOnlyDictionary<int, Bitmap>? images)
    {
        DisposeOwnedImages();
        if (images is null) return;
        foreach ((int index, Bitmap source) in images)
        {
            if (!TryCloneBitmap(source, out Bitmap? clone)) continue;
            _images[index] = clone;
        }
    }

    private void DisposeOwnedImages()
    {
        foreach (Bitmap image in _images.Values) image.Dispose();
        _images.Clear();
    }

    private static void ReplaceOwnedBitmap(ref Bitmap? destination, Bitmap? source)
    {
        DisposeOwnedBitmap(ref destination);
        if (source is not null && TryCloneBitmap(source, out Bitmap? clone)) destination = clone;
    }

    private static void DisposeOwnedBitmap(ref Bitmap? bitmap)
    {
        bitmap?.Dispose();
        bitmap = null;
    }

    private static bool TryCloneBitmap(Bitmap source, out Bitmap? clone)
    {
        clone = null;
        if (!TryGetBitmapSize(source, out int width, out int height)) return false;
        try
        {
            clone = source.Clone(
                new Rectangle(0, 0, width, height),
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            return true;
        }
        catch (ArgumentException)
        {
            clone?.Dispose();
            clone = null;
            return false;
        }
        catch (OutOfMemoryException)
        {
            clone?.Dispose();
            clone = null;
            return false;
        }
    }

    private bool TryGetMemberImage(int memberIndex, out Bitmap image, out int width, out int height)
    {
        image = null!;
        width = 0;
        height = 0;
        if (!_images.TryGetValue(memberIndex, out Bitmap? candidate) || candidate is null) return false;
        if (!TryGetBitmapSize(candidate, out width, out height)) return false;
        image = candidate;
        return true;
    }

    private static bool TryGetBitmapSize(Bitmap image, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            width = image.Width;
            height = image.Height;
            return width > 0 && height > 0;
        }
        catch (ArgumentException)
        {
            // System.Drawing reports a disposed GDI+ image as "Parameter is not valid".
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private RectangleF LogicalBounds() => new(
        10 + _pan.X,
        34 + _pan.Y,
        _profile.LogicalWidth * _zoom,
        _profile.LogicalHeight * _zoom);

    private int? HitTest(Point location)
    {
        for (int index = _members.Count - 1; index >= 0; index--)
        {
            MvTroopMemberSnapshot member = _members[index];
            if (MemberBounds(member).Contains(location)) return member.Index;
        }
        return null;
    }

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        Focus();
        int? hit = HitTest(e.Location);
        if (e.Button == MouseButtons.Middle)
        {
            _lastMouse = e.Location;
            Capture = true;
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        if (!hit.HasValue)
        {
            if ((ModifierKeys & Keys.Control) == 0) _selection.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            return;
        }
        if ((ModifierKeys & Keys.Control) != 0)
        {
            if (!_selection.Add(hit.Value)) _selection.Remove(hit.Value);
        }
        else if (!_selection.Contains(hit.Value))
        {
            _selection.Clear();
            _selection.Add(hit.Value);
        }
        _dragIndex = hit;
        _lastMouse = e.Location;
        Capture = true;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        if (!Capture) return;
        if (e.Button == MouseButtons.Middle)
        {
            _pan = new PointF(_pan.X + e.X - _lastMouse.X, _pan.Y + e.Y - _lastMouse.Y);
            _lastMouse = e.Location;
            Invalidate();
            return;
        }
        if (e.Button != MouseButtons.Left || !_dragIndex.HasValue) return;
        int dx = (int)Math.Round((e.X - _lastMouse.X) / _zoom);
        int dy = (int)Math.Round((e.Y - _lastMouse.Y) / _zoom);
        if (dx == 0 && dy == 0) return;
        _members = TroopFormationPlanner.Move(_members, _selection, dx, dy, _profile);
        _lastMouse = e.Location;
        MembersChanged?.Invoke(this, new TroopMembersChangedEventArgs(_members, false));
        Invalidate();
    }

    private void OnCanvasMouseWheel(object? sender, MouseEventArgs e)
    {
        float old = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1f : 0.9f), 0.35f, 2.5f);
        if (Math.Abs(old - _zoom) < 0.001f) return;
        Invalidate();
    }

    private void OnCanvasKeyDown(object? sender, KeyEventArgs e)
    {
        if (_selection.Count == 0) return;
        int amount = e.Shift ? 10 : 1;
        int dx = e.KeyCode switch { Keys.Left => -amount, Keys.Right => amount, _ => 0 };
        int dy = e.KeyCode switch { Keys.Up => -amount, Keys.Down => amount, _ => 0 };
        if (dx == 0 && dy == 0) return;
        _members = TroopFormationPlanner.Move(_members, _selection, dx, dy, _profile);
        MembersChanged?.Invoke(this, new TroopMembersChangedEventArgs(_members, true));
        e.Handled = true;
        Invalidate();
    }
}

public sealed class TroopMembersChangedEventArgs : EventArgs
{
    public TroopMembersChangedEventArgs(IReadOnlyList<MvTroopMemberSnapshot> members, bool commitRequested)
    {
        Members = members;
        CommitRequested = commitRequested;
    }
    public IReadOnlyList<MvTroopMemberSnapshot> Members { get; }
    public bool CommitRequested { get; }
}

internal static class GraphicsRoundedExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF rectangle, float radius)
    {
        using GraphicsPath path = RoundedPath(rectangle, radius);
        graphics.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF rectangle, float radius)
    {
        using GraphicsPath path = RoundedPath(rectangle, radius);
        graphics.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedPath(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2f;
        GraphicsPath path = new();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
