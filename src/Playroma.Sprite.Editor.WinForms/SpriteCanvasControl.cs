using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.Editor.WinForms;

public sealed class SpriteCanvasControl : ScrollableControl
{
    private PixelBuffer? buffer;
    private PixelBuffer? strokeBefore;
    private readonly List<StrokeSample> strokeSamples = [];
    private readonly List<StrokeSample> polygonPoints = [];
    private PixelBuffer? polygonBefore;
    private Point polygonHover;
    private Point strokeStart;
    private Point lastPoint;
    private long lastSampleTimestamp;
    private double currentPressure = 1d;
    private bool drawing;
    private IReadOnlyList<IntRect> guideCells = [];
    private BrushDefinition brush = BrushDefinition.CreateBuiltIn(BrushShape.Square, 1);
    private SelectionMask selection = new(1, 1);
    private SelectionMask? selectionBase;
    private Point selectionStart;
    private bool selecting;
    private readonly List<IntPoint> selectionPath = [];
    private SelectionMask? selectionPolygonBase;
    private Point selectionPolygonHover;

    public SpriteCanvasControl()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Color.FromArgb(38, 40, 46);
        TabStop = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public PixelToolKind Tool { get; set; } = PixelToolKind.Pencil;
    public Rgba32 ForegroundColor { get; set; } = new(30, 30, 35, 255);
    public Rgba32 BackgroundColor { get; set; } = new(245, 245, 248, 255);
    public Rgba32 ReplaceColor { get; set; } = Rgba32.Transparent;
    public IReadOnlyList<Rgba32> ShadeRamp { get; set; } = [new(20, 20, 24), new(70, 70, 82), new(145, 145, 155), new(225, 225, 232)];
    public int ShadeDirection { get; set; } = 1;
    public BrushDefinition Brush { get => brush; set => brush = value ?? throw new ArgumentNullException(nameof(value)); }
    public BrushPattern? Pattern { get; set; }
    public PatternAlignment PatternAlignment { get; set; } = PatternAlignment.Canvas;
    public InkMode InkMode { get; set; } = InkMode.AlphaComposite;
    public byte BrushOpacity { get; set; } = 255;
    public bool PixelPerfect { get; set; }
    public StrokeDynamicsSettings Dynamics { get; set; } = new();
    public SymmetryMode Symmetry { get; set; }
    public double SymmetryAxisX { get; set; }
    public double SymmetryAxisY { get; set; }
    public TiledMode TiledMode { get; set; }
    public bool ShowTiledPreview { get; set; }
    public bool FilledShapes { get; set; }
    public int RoundedRadius { get; set; } = 4;
    public int CurveBendPercent { get; set; } = 35;
    public bool ContiguousFill { get; set; } = true;
    public int FillTolerance { get; set; }
    public double DitherAmount { get; set; } = .5;
    public DitherMatrixKind DitherMatrix { get; set; } = DitherMatrixKind.Bayer4x4;
    public bool SnapToGrid { get; set; }
    public int GuideWidth { get; set; } = 16;
    public int GuideHeight { get; set; } = 16;
    public int GuideOriginX { get; set; }
    public int GuideOriginY { get; set; }
    public int Zoom { get; private set; } = 4;
    public bool ShowPixelGrid { get; set; } = true;
    public bool ShowTemplateGrid { get; set; } = true;
    public bool ShowGuides { get; set; } = true;
    public bool ShowSymmetryAxes { get; set; } = true;
    public bool ShowSelection { get; set; } = true;
    public bool CanEditPixels { get; set; } = true;
    public SelectionCombineMode SelectionCombineMode { get; set; } = SelectionCombineMode.Replace;
    public bool MagicWandContiguous { get; set; } = true;
    public int MagicWandTolerance { get; set; }
    public Func<PixelBuffer>? CompositeProvider { get; set; }
    public SelectionMask Selection => selection;
    public PixelBuffer? Buffer => buffer;
    public bool HasActivePolygon => polygonBefore is not null || selectionPolygonBase is not null;

    public event EventHandler<PixelEditCommittedEventArgs>? EditCommitted;
    public event EventHandler<ColorPickedEventArgs>? ColorPicked;
    public event EventHandler<CanvasCursorEventArgs>? CursorChanged;
    public event EventHandler? ViewChanged;
    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public void SetDocument(PixelBuffer value, IReadOnlyList<IntRect>? cells = null, bool fit = true)
    {
        buffer = value ?? throw new ArgumentNullException(nameof(value));
        guideCells = cells ?? [];
        if (selection.Width != value.Width || selection.Height != value.Height) selection = new SelectionMask(value.Width, value.Height);
        SymmetryAxisX = (value.Width - 1) / 2d;
        SymmetryAxisY = (value.Height - 1) / 2d;
        drawing = false;
        CancelPolygon();
        if (fit) SetZoom(FitZoom(value.Width, value.Height));
        else
        {
            int multiplierX = ShowTiledPreview && (TiledMode & TiledMode.X) != 0 ? 3 : 1;
            int multiplierY = ShowTiledPreview && (TiledMode & TiledMode.Y) != 0 ? 3 : 1;
            AutoScrollMinSize = new Size(checked(value.Width * Zoom * multiplierX + 80), checked(value.Height * Zoom * multiplierY + 80));
            Invalidate();
        }
    }

    public void SetSelection(SelectionMask value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (buffer is not null && (value.Width != buffer.Width || value.Height != buffer.Height)) throw new ArgumentException("Selection dimensions do not match the canvas.", nameof(value));
        selection = value.Clone(); selecting = false; selectionBase = null; Invalidate();
    }

    public void ClearSelection()
    {
        selection.Clear(); SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(selection.Clone())); Invalidate();
    }

    public void SetZoom(int zoom)
    {
        Zoom = Math.Clamp(zoom, 1, 64);
        if (buffer is not null)
        {
            int multiplierX = ShowTiledPreview && (TiledMode & TiledMode.X) != 0 ? 3 : 1;
            int multiplierY = ShowTiledPreview && (TiledMode & TiledMode.Y) != 0 ? 3 : 1;
            AutoScrollMinSize = new Size(checked(buffer.Width * Zoom * multiplierX + 80), checked(buffer.Height * Zoom * multiplierY + 80));
        }
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => SetZoom(Zoom < 8 ? Zoom + 1 : Math.Min(64, Zoom + 2));
    public void ZoomOut() => SetZoom(Zoom <= 8 ? Zoom - 1 : Zoom - 2);

    public void CompletePolygon()
    {
        if (selectionPolygonBase is not null)
        {
            CompleteSelectionPolygon(); return;
        }
        if (buffer is null || polygonBefore is null || polygonPoints.Count < 2) { CancelPolygon(); return; }
        buffer.CopyFrom(polygonBefore);
        PixelDrawing.DrawPolygon(buffer, polygonPoints, CreateSettings(polygonPoints[0].X, polygonPoints[0].Y), FilledShapes, true);
        PixelBuffer before = polygonBefore;
        polygonBefore = null;
        polygonPoints.Clear();
        EditCommitted?.Invoke(this, new PixelEditCommittedEventArgs("Polygon", before));
        Invalidate();
    }

    public void CancelPolygon()
    {
        if (selectionPolygonBase is not null)
        {
            selection = selectionPolygonBase; selectionPolygonBase = null; selectionPath.Clear(); SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(selection.Clone()));
        }
        if (buffer is not null && polygonBefore is not null) buffer.CopyFrom(polygonBefore);
        polygonBefore = null;
        polygonPoints.Clear();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
        {
            if (e.Delta > 0) ZoomIn(); else ZoomOut();
            return;
        }
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (buffer is null) return;
        Point pixel = Snap(ClientToPixel(e.Location));
        if (Tool == PixelToolKind.SelectionPolygon)
        {
            HandleSelectionPolygonMouseDown(e, pixel); return;
        }
        if (Tool is PixelToolKind.SelectionRectangle or PixelToolKind.SelectionEllipse or PixelToolKind.SelectionLasso or PixelToolKind.MagicWand or PixelToolKind.MoveSelection)
        {
            HandleSelectionMouseDown(e, pixel); return;
        }
        if (Tool == PixelToolKind.Polygon)
        {
            HandlePolygonMouseDown(e, pixel);
            return;
        }
        if (e.Button != MouseButtons.Left || !InsideEditable(pixel)) return;
        pixel = WrapForCursor(pixel);
        if (!CanEditPixels && Tool != PixelToolKind.Eyedropper) return;
        if (Tool == PixelToolKind.Eyedropper)
        {
            Rgba32 picked = buffer.GetPixel(pixel.X, pixel.Y);
            ForegroundColor = picked;
            ColorPicked?.Invoke(this, new ColorPickedEventArgs(picked));
            Invalidate();
            return;
        }
        strokeBefore = buffer.Clone();
        strokeStart = pixel;
        lastPoint = pixel;
        strokeSamples.Clear();
        strokeSamples.Add(new StrokeSample(pixel.X, pixel.Y, currentPressure, 0d));
        lastSampleTimestamp = Stopwatch.GetTimestamp();
        drawing = true;
        if (Tool == PixelToolKind.Fill)
        {
            PixelDrawing.FloodFill(buffer, pixel.X, pixel.Y, CreateSettings(pixel.X, pixel.Y), ContiguousFill, FillTolerance);
            CommitStroke("Fill");
            return;
        }
        RedrawStroke(pixel, preview: false);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point raw = ClientToPixel(e.Location);
        Point pixel = Snap(raw);
        bool inside = InsideEditable(pixel);
        Point display = inside ? WrapForCursor(pixel) : pixel;
        CursorChanged?.Invoke(this, new CanvasCursorEventArgs(display.X, display.Y, inside));
        if (Tool == PixelToolKind.SelectionPolygon && selectionPolygonBase is not null)
        {
            selectionPolygonHover = inside ? WrapForCursor(pixel) : selectionPolygonHover; PreviewSelectionPolygon(); return;
        }
        if (selecting && buffer is not null && selectionBase is not null && (e.Button & MouseButtons.Left) != 0)
        {
            Point current = inside ? WrapForCursor(pixel) : lastPoint;
            PreviewSelection(current); lastPoint = current; return;
        }
        if (Tool == PixelToolKind.Polygon && polygonBefore is not null)
        {
            polygonHover = WrapForCursor(pixel);
            PreviewPolygon();
            return;
        }
        if (!drawing || buffer is null || strokeBefore is null || (e.Button & MouseButtons.Left) == 0 || !inside) return;
        pixel = WrapForCursor(pixel);
        AddStrokeSample(pixel);
        RedrawStroke(pixel, preview: true);
        lastPoint = pixel;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (selecting) { CompleteSelectionDrag(); return; }
        if (!drawing || buffer is null || strokeBefore is null) return;
        Point pixel = Snap(ClientToPixel(e.Location));
        if (InsideEditable(pixel)) pixel = WrapForCursor(pixel); else pixel = lastPoint;
        AddStrokeSample(pixel);
        RedrawStroke(pixel, preview: false);
        if (Tool == PixelToolKind.Contour)
        {
            buffer.CopyFrom(strokeBefore);
            PixelDrawing.DrawPolygon(buffer, strokeSamples, CreateSettings(strokeStart.X, strokeStart.Y), FilledShapes, true);
        }
        CommitStroke(Tool.ToString());
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (buffer is null) return;
        Point origin = CanvasOrigin();
        PixelBuffer displayBuffer = CompositeProvider?.Invoke() ?? buffer;
        using Bitmap image = ToBitmap(displayBuffer);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        DrawCanvasCopy(e.Graphics, image, origin, 0, 0, true);
        if (ShowTiledPreview)
        {
            int minX = (TiledMode & TiledMode.X) != 0 ? -1 : 0;
            int maxX = (TiledMode & TiledMode.X) != 0 ? 1 : 0;
            int minY = (TiledMode & TiledMode.Y) != 0 ? -1 : 0;
            int maxY = (TiledMode & TiledMode.Y) != 0 ? 1 : 0;
            for (int tileY = minY; tileY <= maxY; tileY++)
                for (int tileX = minX; tileX <= maxX; tileX++)
                    if (tileX != 0 || tileY != 0) DrawCanvasCopy(e.Graphics, image, origin, tileX, tileY, false);
        }
        if (ShowSymmetryAxes) DrawSymmetryAxes(e.Graphics, origin);
        if (ShowSelection && !selection.IsEmpty) DrawSelectionOutline(e.Graphics, origin);
    }

    private void HandleSelectionPolygonMouseDown(MouseEventArgs e, Point pixel)
    {
        if (buffer is null) return;
        if (e.Button == MouseButtons.Right) { CancelPolygon(); return; }
        if (e.Button != MouseButtons.Left || !InsideEditable(pixel)) return;
        pixel = WrapForCursor(pixel);
        if (selectionPolygonBase is null)
        {
            selectionPolygonBase = selection.Clone(); selectionPath.Clear();
        }
        IntPoint point = new(pixel.X, pixel.Y);
        if (selectionPath.Count == 0 || selectionPath[^1] != point) selectionPath.Add(point);
        selectionPolygonHover = pixel;
        if (e.Clicks >= 2 && selectionPath.Count >= 3) CompleteSelectionPolygon(); else PreviewSelectionPolygon();
    }

    private void PreviewSelectionPolygon()
    {
        if (buffer is null || selectionPolygonBase is null) return;
        List<IntPoint> points = [.. selectionPath];
        IntPoint hover = new(selectionPolygonHover.X, selectionPolygonHover.Y);
        if (points.Count > 0 && points[^1] != hover) points.Add(hover);
        SelectionMask incoming = SelectionMask.Polygon(buffer.Width, buffer.Height, points);
        selection = selectionPolygonBase.Clone(); selection.Combine(incoming, SelectionCombineMode); Invalidate();
    }

    private void CompleteSelectionPolygon()
    {
        if (buffer is null || selectionPolygonBase is null) return;
        SelectionMask incoming = SelectionMask.Polygon(buffer.Width, buffer.Height, selectionPath);
        selection = selectionPolygonBase.Clone(); selection.Combine(incoming, SelectionCombineMode);
        selectionPolygonBase = null; selectionPath.Clear();
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(selection.Clone())); Invalidate();
    }

    private void HandleSelectionMouseDown(MouseEventArgs e, Point pixel)
    {
        if (buffer is null || e.Button != MouseButtons.Left || !InsideEditable(pixel)) return;
        pixel = WrapForCursor(pixel);
        if (Tool == PixelToolKind.MagicWand)
        {
            SelectionMask incoming = SelectionMask.MagicWand(buffer, pixel.X, pixel.Y, MagicWandTolerance, MagicWandContiguous);
            selection.Combine(incoming, SelectionCombineMode);
            SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(selection.Clone())); Invalidate(); return;
        }
        if (Tool == PixelToolKind.MoveSelection && (selection.IsEmpty || !CanEditPixels)) return;
        selectionStart = pixel; lastPoint = pixel; selectionBase = selection.Clone(); selecting = true;
        selectionPath.Clear(); selectionPath.Add(new IntPoint(pixel.X, pixel.Y));
        if (Tool == PixelToolKind.MoveSelection) strokeBefore = buffer.Clone();
        PreviewSelection(pixel);
    }

    private void PreviewSelection(Point current)
    {
        if (buffer is null || selectionBase is null) return;
        if (Tool == PixelToolKind.SelectionLasso)
        {
            if (selectionPath.Count == 0 || selectionPath[^1].X != current.X || selectionPath[^1].Y != current.Y) selectionPath.Add(new IntPoint(current.X, current.Y));
            SelectionMask incoming = SelectionMask.Polygon(buffer.Width, buffer.Height, selectionPath);
            selection = selectionBase.Clone(); selection.Combine(incoming, SelectionCombineMode);
        }
        else if (Tool == PixelToolKind.MoveSelection)
        {
            if (strokeBefore is null) return;
            buffer.CopyFrom(strokeBefore);
            selection = PixelSelectionTransforms.Move(buffer, selectionBase, current.X - selectionStart.X, current.Y - selectionStart.Y);
        }
        else
        {
            int left = Math.Min(selectionStart.X, current.X), top = Math.Min(selectionStart.Y, current.Y);
            int width = Math.Abs(current.X - selectionStart.X) + 1, height = Math.Abs(current.Y - selectionStart.Y) + 1;
            SelectionMask incoming = Tool == PixelToolKind.SelectionEllipse
                ? SelectionMask.Ellipse(buffer.Width, buffer.Height, new IntRect(left, top, width, height))
                : SelectionMask.Rectangle(buffer.Width, buffer.Height, new IntRect(left, top, width, height));
            selection = selectionBase.Clone(); selection.Combine(incoming, SelectionCombineMode);
        }
        Invalidate();
    }

    private void CompleteSelectionDrag()
    {
        selecting = false;
        SelectionMask completed = selection.Clone();
        if (Tool == PixelToolKind.MoveSelection && strokeBefore is not null)
        {
            PixelBuffer before = strokeBefore; strokeBefore = null;
            EditCommitted?.Invoke(this, new PixelEditCommittedEventArgs("Move Selection", before));
        }
        selectionBase = null; selectionPath.Clear();
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(completed)); Invalidate();
    }

    private void DrawSelectionOutline(Graphics graphics, Point origin)
    {
        IntRect? bounds = selection.GetBounds(); if (bounds is null) return;
        using Pen light = new(Color.White) { DashStyle = DashStyle.Dot };
        using Pen dark = new(Color.Black) { DashStyle = DashStyle.Dash, DashOffset = 2 };
        if (Zoom < 4)
        {
            Rectangle rectangle = new(origin.X + bounds.Value.X * Zoom, origin.Y + bounds.Value.Y * Zoom, bounds.Value.Width * Zoom, bounds.Value.Height * Zoom);
            graphics.DrawRectangle(dark, rectangle); graphics.DrawRectangle(light, rectangle); return;
        }
        for (int y = 0; y < selection.Height; y++) for (int x = 0; x < selection.Width; x++)
        {
            if (!selection.Contains(x, y)) continue;
            int px = origin.X + x * Zoom, py = origin.Y + y * Zoom;
            if (x == 0 || !selection.Contains(x - 1, y)) { graphics.DrawLine(dark, px, py, px, py + Zoom); graphics.DrawLine(light, px, py, px, py + Zoom); }
            if (x == selection.Width - 1 || !selection.Contains(x + 1, y)) { graphics.DrawLine(dark, px + Zoom, py, px + Zoom, py + Zoom); graphics.DrawLine(light, px + Zoom, py, px + Zoom, py + Zoom); }
            if (y == 0 || !selection.Contains(x, y - 1)) { graphics.DrawLine(dark, px, py, px + Zoom, py); graphics.DrawLine(light, px, py, px + Zoom, py); }
            if (y == selection.Height - 1 || !selection.Contains(x, y + 1)) { graphics.DrawLine(dark, px, py + Zoom, px + Zoom, py + Zoom); graphics.DrawLine(light, px, py + Zoom, px + Zoom, py + Zoom); }
        }
    }

    protected override void WndProc(ref Message message)
    {
        const int WmPointerUpdate = 0x0245;
        const int WmPointerDown = 0x0246;
        const int WmPointerUp = 0x0247;
        if (message.Msg is WmPointerUpdate or WmPointerDown or WmPointerUp)
        {
            uint pointerId = unchecked((uint)message.WParam.ToInt64()) & 0xFFFFu;
            if (GetPointerPenInfo(pointerId, out PointerPenInfo penInfo) && (penInfo.PenMask & PenMask.Pressure) != 0)
                currentPressure = Math.Clamp(penInfo.Pressure / 1024d, 0d, 1d);
            if (message.Msg == WmPointerUp) currentPressure = 1d;
        }
        base.WndProc(ref message);
    }

    private void HandlePolygonMouseDown(MouseEventArgs e, Point pixel)
    {
        if (buffer is null) return;
        if (e.Button == MouseButtons.Right) { CancelPolygon(); return; }
        if (e.Button != MouseButtons.Left || !InsideEditable(pixel)) return;
        pixel = WrapForCursor(pixel);
        if (polygonBefore is null)
        {
            polygonBefore = buffer.Clone();
            polygonPoints.Clear();
        }
        if (polygonPoints.Count == 0 || polygonPoints[^1].X != pixel.X || polygonPoints[^1].Y != pixel.Y)
            polygonPoints.Add(new StrokeSample(pixel.X, pixel.Y, currentPressure));
        polygonHover = pixel;
        if (e.Clicks >= 2 && polygonPoints.Count >= 3) CompletePolygon(); else PreviewPolygon();
    }

    private void PreviewPolygon()
    {
        if (buffer is null || polygonBefore is null) return;
        buffer.CopyFrom(polygonBefore);
        List<StrokeSample> preview = [.. polygonPoints];
        if (preview.Count > 0 && (preview[^1].X != polygonHover.X || preview[^1].Y != polygonHover.Y)) preview.Add(new StrokeSample(polygonHover.X, polygonHover.Y));
        PixelDrawing.DrawPolygon(buffer, preview, CreateSettings(preview[0].X, preview[0].Y), false, false);
        Invalidate();
    }

    private void AddStrokeSample(Point pixel)
    {
        if (strokeSamples.Count > 0 && strokeSamples[^1].X == pixel.X && strokeSamples[^1].Y == pixel.Y) return;
        long now = Stopwatch.GetTimestamp();
        double elapsed = Math.Max(.0001, (now - lastSampleTimestamp) / (double)Stopwatch.Frequency);
        double dx = pixel.X - lastPoint.X;
        double dy = pixel.Y - lastPoint.Y;
        double velocity = Math.Sqrt(dx * dx + dy * dy) / elapsed;
        strokeSamples.Add(new StrokeSample(pixel.X, pixel.Y, currentPressure, velocity));
        lastSampleTimestamp = now;
    }

    private void RedrawStroke(Point pixel, bool preview)
    {
        if (buffer is null || strokeBefore is null) return;
        PixelPaintSettings settings = CreateSettings(strokeStart.X, strokeStart.Y);
        switch (Tool)
        {
            case PixelToolKind.Pencil:
            case PixelToolKind.Eraser:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DrawStroke(buffer, strokeSamples, settings);
                break;
            case PixelToolKind.Contour:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DrawStroke(buffer, strokeSamples, settings);
                break;
            case PixelToolKind.Line:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DrawLine(buffer, strokeSamples[0], strokeSamples[^1], settings);
                break;
            case PixelToolKind.Curve:
                buffer.CopyFrom(strokeBefore);
                StrokeSample control = CurveControl(strokeSamples[0], strokeSamples[^1], CurveBendPercent);
                PixelDrawing.DrawQuadraticCurve(buffer, strokeSamples[0], control, strokeSamples[^1], settings);
                break;
            case PixelToolKind.Rectangle:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DrawRectangle(buffer, strokeStart.X, strokeStart.Y, pixel.X, pixel.Y, settings, FilledShapes);
                break;
            case PixelToolKind.RoundedRectangle:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DrawRoundedRectangle(buffer, strokeStart.X, strokeStart.Y, pixel.X, pixel.Y, RoundedRadius, settings, FilledShapes);
                break;
            case PixelToolKind.Ellipse:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DrawEllipse(buffer, strokeStart.X, strokeStart.Y, pixel.X, pixel.Y, settings, FilledShapes);
                break;
            case PixelToolKind.Gradient:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DrawLinearGradient(buffer, strokeStart.X, strokeStart.Y, pixel.X, pixel.Y, ForegroundColor, BackgroundColor, false, DitherMatrixKind.Bayer4x4, selection);
                break;
            case PixelToolKind.Dither:
                buffer.CopyFrom(strokeBefore);
                PixelDrawing.DitherRegion(buffer, strokeStart.X, strokeStart.Y, pixel.X, pixel.Y, ForegroundColor, BackgroundColor, DitherAmount, DitherMatrix, settings);
                break;
            case PixelToolKind.Fill when !preview:
                PixelDrawing.FloodFill(buffer, pixel.X, pixel.Y, settings, ContiguousFill, FillTolerance);
                break;
        }
    }

    private PixelPaintSettings CreateSettings(int originX, int originY)
    {
        InkMode mode = Tool == PixelToolKind.Eraser ? InkMode.Erase : InkMode;
        return new PixelPaintSettings
        {
            Brush = Brush,
            PrimaryColor = ForegroundColor,
            SecondaryColor = BackgroundColor,
            Opacity = BrushOpacity,
            InkMode = mode,
            ReplaceColor = ReplaceColor,
            ReplaceTolerance = FillTolerance,
            ShadeRamp = ShadeRamp,
            ShadeDirection = ShadeDirection,
            Pattern = Pattern,
            PatternAlignment = PatternAlignment,
            Symmetry = Symmetry,
            SymmetryAxisX = SymmetryAxisX,
            SymmetryAxisY = SymmetryAxisY,
            TiledMode = TiledMode,
            PixelPerfect = PixelPerfect,
            Dynamics = Dynamics,
            StrokeOriginX = originX,
            StrokeOriginY = originY,
            Selection = selection
        };
    }

    private void CommitStroke(string description)
    {
        if (strokeBefore is null) return;
        PixelBuffer before = strokeBefore;
        strokeBefore = null;
        strokeSamples.Clear();
        drawing = false;
        EditCommitted?.Invoke(this, new PixelEditCommittedEventArgs(description, before));
        Invalidate();
    }

    private void DrawCanvasCopy(Graphics graphics, Bitmap image, Point origin, int tileX, int tileY, bool primary)
    {
        if (buffer is null) return;
        int x = origin.X + tileX * buffer.Width * Zoom;
        int y = origin.Y + tileY * buffer.Height * Zoom;
        DrawCheckerboard(graphics, new Point(x, y));
        if (!primary)
        {
            ColorMatrix matrix = new() { Matrix33 = .72f };
            using ImageAttributes attributes = new();
            attributes.SetColorMatrix(matrix);
            graphics.DrawImage(image, new Rectangle(x, y, buffer.Width * Zoom, buffer.Height * Zoom), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
        else graphics.DrawImage(image, new Rectangle(x, y, buffer.Width * Zoom, buffer.Height * Zoom));
        if (primary && ShowPixelGrid && Zoom >= 8) DrawPixelGrid(graphics, new Point(x, y));
        if (primary && ShowTemplateGrid) DrawTemplateGrid(graphics, new Point(x, y));
        if (primary && ShowGuides) DrawCustomGuides(graphics, new Point(x, y));
        using Pen border = new(primary ? Color.FromArgb(235, 230, 232, 238) : Color.FromArgb(110, 210, 212, 220));
        graphics.DrawRectangle(border, x - 1, y - 1, buffer.Width * Zoom + 1, buffer.Height * Zoom + 1);
    }

    private void DrawCheckerboard(Graphics graphics, Point origin)
    {
        if (buffer is null) return;
        int cell = Math.Max(Zoom * 4, 8);
        using SolidBrush light = new(Color.FromArgb(210, 210, 215));
        using SolidBrush dark = new(Color.FromArgb(170, 172, 178));
        Rectangle area = new(origin.X, origin.Y, buffer.Width * Zoom, buffer.Height * Zoom);
        graphics.FillRectangle(light, area);
        for (int y = 0; y < area.Height; y += cell)
            for (int x = 0; x < area.Width; x += cell)
                if (((x / cell) + (y / cell)) % 2 != 0)
                    graphics.FillRectangle(dark, origin.X + x, origin.Y + y, Math.Min(cell, area.Width - x), Math.Min(cell, area.Height - y));
    }

    private void DrawPixelGrid(Graphics graphics, Point origin)
    {
        if (buffer is null) return;
        using Pen pen = new(Color.FromArgb(45, 20, 20, 20));
        for (int x = 1; x < buffer.Width; x++) graphics.DrawLine(pen, origin.X + x * Zoom, origin.Y, origin.X + x * Zoom, origin.Y + buffer.Height * Zoom);
        for (int y = 1; y < buffer.Height; y++) graphics.DrawLine(pen, origin.X, origin.Y + y * Zoom, origin.X + buffer.Width * Zoom, origin.Y + y * Zoom);
    }

    private void DrawTemplateGrid(Graphics graphics, Point origin)
    {
        using Pen pen = new(Color.FromArgb(210, 45, 160, 255), Math.Max(1, Zoom / 5f));
        foreach (IntRect cell in guideCells)
            graphics.DrawRectangle(pen, origin.X + cell.X * Zoom, origin.Y + cell.Y * Zoom, cell.Width * Zoom, cell.Height * Zoom);
    }

    private void DrawCustomGuides(Graphics graphics, Point origin)
    {
        if (buffer is null || GuideWidth <= 0 || GuideHeight <= 0) return;
        using Pen pen = new(Color.FromArgb(115, 255, 195, 55)) { DashStyle = DashStyle.Dash };
        for (int x = GuideOriginX; x <= buffer.Width; x += GuideWidth)
            if (x >= 0) graphics.DrawLine(pen, origin.X + x * Zoom, origin.Y, origin.X + x * Zoom, origin.Y + buffer.Height * Zoom);
        for (int x = GuideOriginX - GuideWidth; x >= 0; x -= GuideWidth)
            graphics.DrawLine(pen, origin.X + x * Zoom, origin.Y, origin.X + x * Zoom, origin.Y + buffer.Height * Zoom);
        for (int y = GuideOriginY; y <= buffer.Height; y += GuideHeight)
            if (y >= 0) graphics.DrawLine(pen, origin.X, origin.Y + y * Zoom, origin.X + buffer.Width * Zoom, origin.Y + y * Zoom);
        for (int y = GuideOriginY - GuideHeight; y >= 0; y -= GuideHeight)
            graphics.DrawLine(pen, origin.X, origin.Y + y * Zoom, origin.X + buffer.Width * Zoom, origin.Y + y * Zoom);
    }

    private void DrawSymmetryAxes(Graphics graphics, Point origin)
    {
        if (buffer is null || Symmetry == SymmetryMode.None) return;
        using Pen pen = new(Color.FromArgb(210, 235, 75, 145)) { DashStyle = DashStyle.Dash };
        if ((Symmetry & SymmetryMode.Horizontal) != 0)
        {
            float x = origin.X + (float)(SymmetryAxisX + .5) * Zoom;
            graphics.DrawLine(pen, x, origin.Y, x, origin.Y + buffer.Height * Zoom);
        }
        if ((Symmetry & SymmetryMode.Vertical) != 0)
        {
            float y = origin.Y + (float)(SymmetryAxisY + .5) * Zoom;
            graphics.DrawLine(pen, origin.X, y, origin.X + buffer.Width * Zoom, y);
        }
    }

    private Point CanvasOrigin()
    {
        if (buffer is null) return new Point(AutoScrollPosition.X + 36, AutoScrollPosition.Y + 36);
        int offsetX = ShowTiledPreview && (TiledMode & TiledMode.X) != 0 ? buffer.Width * Zoom : 0;
        int offsetY = ShowTiledPreview && (TiledMode & TiledMode.Y) != 0 ? buffer.Height * Zoom : 0;
        return new Point(AutoScrollPosition.X + 36 + offsetX, AutoScrollPosition.Y + 36 + offsetY);
    }

    private Point ClientToPixel(Point point)
    {
        Point origin = CanvasOrigin();
        return new Point(
            SaturatingFloorToInt((point.X - (double)origin.X) / Math.Max(1, Zoom)),
            SaturatingFloorToInt((point.Y - (double)origin.Y) / Math.Max(1, Zoom)));
    }

    private Point Snap(Point pixel)
    {
        if (!SnapToGrid) return pixel;
        int x = SnapCoordinate(pixel.X, GuideOriginX, Math.Max(1, GuideWidth));
        int y = SnapCoordinate(pixel.Y, GuideOriginY, Math.Max(1, GuideHeight));
        return new Point(x, y);
    }

    private static int SnapCoordinate(int value, int origin, int interval)
    {
        double snapped = origin + Math.Round((value - (double)origin) / Math.Max(1, interval)) * interval;
        return SaturatingRoundToInt(snapped);
    }

    private static int SaturatingFloorToInt(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value <= int.MinValue) return int.MinValue;
        if (value >= int.MaxValue) return int.MaxValue;
        return (int)Math.Floor(value);
    }

    private static int SaturatingRoundToInt(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value <= int.MinValue) return int.MinValue;
        if (value >= int.MaxValue) return int.MaxValue;
        return (int)Math.Round(value);
    }

    private bool InsideEditable(Point pixel)
    {
        if (buffer is null) return false;
        // Do not use the common unsigned bounds-check idiom here. This solution enables
        // checked arithmetic globally, so casting an off-canvas negative mouse coordinate
        // to uint throws OverflowException before we can report that it is outside.
        bool xValid = (pixel.X >= 0 && pixel.X < buffer.Width) || (TiledMode & TiledMode.X) != 0;
        bool yValid = (pixel.Y >= 0 && pixel.Y < buffer.Height) || (TiledMode & TiledMode.Y) != 0;
        return xValid && yValid;
    }

    private Point WrapForCursor(Point pixel)
    {
        if (buffer is null) return pixel;
        int x = (TiledMode & TiledMode.X) != 0 ? Mod(pixel.X, buffer.Width) : Math.Clamp(pixel.X, 0, buffer.Width - 1);
        int y = (TiledMode & TiledMode.Y) != 0 ? Mod(pixel.Y, buffer.Height) : Math.Clamp(pixel.Y, 0, buffer.Height - 1);
        return new Point(x, y);
    }

    private int FitZoom(int width, int height)
    {
        int availableWidth = Math.Max(1, ClientSize.Width - 100);
        int availableHeight = Math.Max(1, ClientSize.Height - 100);
        return Math.Clamp(Math.Min(availableWidth / Math.Max(1, width), availableHeight / Math.Max(1, height)), 1, 16);
    }

    private static StrokeSample CurveControl(StrokeSample start, StrokeSample end, int bendPercent)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0d) return start;
        double amount = length * bendPercent / 100d;
        double middleX = (start.X + end.X) / 2d;
        double middleY = (start.Y + end.Y) / 2d;
        return new StrokeSample((int)Math.Round(middleX - dy / length * amount), (int)Math.Round(middleY + dx / length * amount), (start.Pressure + end.Pressure) / 2d);
    }

    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static Bitmap ToBitmap(PixelBuffer source)
    {
        Bitmap bitmap = new(source.Width, source.Height, PixelFormat.Format32bppArgb);
        Rectangle rectangle = new(0, 0, source.Width, source.Height);
        BitmapData data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[checked(source.Width * source.Height * 4)];
            ReadOnlySpan<byte> rgba = source.Bytes.Span;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                bgra[i] = rgba[i + 2]; bgra[i + 1] = rgba[i + 1]; bgra[i + 2] = rgba[i]; bgra[i + 3] = rgba[i + 3];
            }
            Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerPenInfo(uint pointerId, out PointerPenInfo penInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    private enum PointerInputType : uint { Pointer = 1, Touch = 2, Pen = 3, Mouse = 4, TouchPad = 5 }
    [Flags] private enum PenFlags : uint { None = 0, Barrel = 1, Inverted = 2, Eraser = 4 }
    [Flags] private enum PenMask : uint { None = 0, Pressure = 1, Rotation = 2, TiltX = 4, TiltY = 8 }
    private enum PointerButtonChangeType : uint { None, FirstDown, FirstUp, SecondDown, SecondUp, ThirdDown, ThirdUp, FourthDown, FourthUp, FifthDown, FifthUp }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public PointerInputType PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public IntPtr SourceDevice;
        public IntPtr HwndTarget;
        public NativePoint PixelLocation;
        public NativePoint HimetricLocation;
        public NativePoint PixelLocationRaw;
        public NativePoint HimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public PointerButtonChangeType ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerPenInfo
    {
        public PointerInfo PointerInfo;
        public PenFlags PenFlags;
        public PenMask PenMask;
        public uint Pressure;
        public uint Rotation;
        public int TiltX;
        public int TiltY;
    }
}

public sealed class PixelEditCommittedEventArgs(string description, PixelBuffer before) : EventArgs
{
    public string Description { get; } = description;
    public PixelBuffer Before { get; } = before;
}

public sealed class ColorPickedEventArgs(Rgba32 color) : EventArgs
{
    public Rgba32 Color { get; } = color;
}

public sealed class SelectionChangedEventArgs(SelectionMask selection) : EventArgs
{
    public SelectionMask Selection { get; } = selection;
}

public sealed class CanvasCursorEventArgs(int x, int y, bool inside) : EventArgs
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public bool Inside { get; } = inside;
}
