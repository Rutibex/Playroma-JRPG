namespace Playroma.Sprite.Pixel;

public enum PixelToolKind
{
    Pencil,
    Eraser,
    Line,
    Curve,
    Rectangle,
    RoundedRectangle,
    Ellipse,
    Polygon,
    Contour,
    Fill,
    Gradient,
    Dither,
    Eyedropper,
    SelectionRectangle,
    SelectionEllipse,
    SelectionPolygon,
    SelectionLasso,
    MagicWand,
    MoveSelection
}

public readonly record struct StrokeSample(int X, int Y, double Pressure = 1d, double VelocityPixelsPerSecond = 0d);

public static class PixelDrawing
{
    public static void Stamp(PixelBuffer buffer, int centerX, int centerY, Rgba32 color, int size = 1)
    {
        PixelPaintSettings settings = new()
        {
            Brush = BrushDefinition.CreateBuiltIn(BrushShape.Square, size),
            PrimaryColor = color,
            InkMode = color.A == 0 ? InkMode.CopyColorAndAlpha : InkMode.AlphaComposite
        };
        Stamp(buffer, centerX, centerY, settings);
    }

    public static void Stamp(PixelBuffer buffer, int centerX, int centerY, PixelPaintSettings settings, double pressure = 1d, double velocityPixelsPerSecond = 0d)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(settings);
        StrokeDynamicsResult dynamics = settings.Dynamics.Evaluate(pressure, velocityPixelsPerSecond);
        int targetSize = Math.Max(1, (int)Math.Round(Math.Max(settings.Brush.Width, settings.Brush.Height) * dynamics.SizeFactor));
        BrushDefinition brush = targetSize == Math.Max(settings.Brush.Width, settings.Brush.Height) ? settings.Brush : settings.Brush.Resize(targetSize);
        HashSet<long> written = new();
        foreach ((int offsetX, int offsetY) in brush.EnumerateOffsets())
        {
            int targetX = centerX + offsetX;
            int targetY = centerY + offsetY;
            Rgba32 source = ResolveBrushColor(settings, targetX, targetY, offsetX, offsetY);
            foreach ((int x, int y) in ExpandCoordinates(buffer, targetX, targetY, settings))
            {
                long key = ((long)y << 32) | (uint)x;
                if (!written.Add(key) || !IsAllowed(settings, x, y)) continue;
                Rgba32 destination = buffer.GetPixel(x, y);
                buffer.SetPixel(x, y, PixelCompositor.Apply(destination, source, settings, dynamics.OpacityFactor * SelectionFactor(settings, x, y)));
            }
        }
    }

    public static void DrawLine(PixelBuffer buffer, int x0, int y0, int x1, int y1, Rgba32 color, int size = 1)
    {
        PixelPaintSettings settings = new()
        {
            Brush = BrushDefinition.CreateBuiltIn(BrushShape.Square, size),
            PrimaryColor = color,
            InkMode = color.A == 0 ? InkMode.CopyColorAndAlpha : InkMode.AlphaComposite
        };
        DrawLine(buffer, new StrokeSample(x0, y0), new StrokeSample(x1, y1), settings);
    }

    public static void DrawLine(PixelBuffer buffer, StrokeSample from, StrokeSample to, PixelPaintSettings settings)
    {
        foreach (StrokeSample sample in RasterizeLine(from, to))
            Stamp(buffer, sample.X, sample.Y, settings, sample.Pressure, sample.VelocityPixelsPerSecond);
    }

    public static void DrawStroke(PixelBuffer buffer, IReadOnlyList<StrokeSample> samples, PixelPaintSettings settings)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0) return;
        List<StrokeSample> raster = new();
        if (samples.Count == 1) raster.Add(samples[0]);
        for (int index = 1; index < samples.Count; index++)
        {
            foreach (StrokeSample point in RasterizeLine(samples[index - 1], samples[index]))
                if (raster.Count == 0 || raster[^1].X != point.X || raster[^1].Y != point.Y) raster.Add(point);
        }
        if (settings.PixelPerfect) raster = CleanPixelPerfect(raster);
        foreach (StrokeSample sample in raster)
            Stamp(buffer, sample.X, sample.Y, settings, sample.Pressure, sample.VelocityPixelsPerSecond);
    }

    public static void DrawQuadraticCurve(PixelBuffer buffer, StrokeSample start, StrokeSample control, StrokeSample end, PixelPaintSettings settings)
    {
        double length = Distance(start, control) + Distance(control, end);
        int steps = Math.Clamp((int)Math.Ceiling(length * 2d), 8, 8192);
        List<StrokeSample> samples = new(steps + 1);
        for (int index = 0; index <= steps; index++)
        {
            double t = index / (double)steps;
            double inverse = 1d - t;
            int x = (int)Math.Round(inverse * inverse * start.X + 2d * inverse * t * control.X + t * t * end.X);
            int y = (int)Math.Round(inverse * inverse * start.Y + 2d * inverse * t * control.Y + t * t * end.Y);
            double pressure = start.Pressure + (end.Pressure - start.Pressure) * t;
            double velocity = start.VelocityPixelsPerSecond + (end.VelocityPixelsPerSecond - start.VelocityPixelsPerSecond) * t;
            if (samples.Count == 0 || samples[^1].X != x || samples[^1].Y != y) samples.Add(new StrokeSample(x, y, pressure, velocity));
        }
        DrawStroke(buffer, samples, settings);
    }

    public static void DrawRectangle(PixelBuffer buffer, int x0, int y0, int x1, int y1, Rgba32 color, int size = 1) =>
        DrawRectangle(buffer, x0, y0, x1, y1, CreateSimpleSettings(color, size), false);

    public static void DrawRectangle(PixelBuffer buffer, int x0, int y0, int x1, int y1, PixelPaintSettings settings, bool filled)
    {
        Normalize(ref x0, ref y0, ref x1, ref y1);
        if (filled)
        {
            for (int y = y0; y <= y1; y++) DrawLine(buffer, new StrokeSample(x0, y), new StrokeSample(x1, y), settings);
            return;
        }
        DrawLine(buffer, new StrokeSample(x0, y0), new StrokeSample(x1, y0), settings);
        DrawLine(buffer, new StrokeSample(x1, y0), new StrokeSample(x1, y1), settings);
        DrawLine(buffer, new StrokeSample(x1, y1), new StrokeSample(x0, y1), settings);
        DrawLine(buffer, new StrokeSample(x0, y1), new StrokeSample(x0, y0), settings);
    }

    public static void DrawRoundedRectangle(PixelBuffer buffer, int x0, int y0, int x1, int y1, int radius, PixelPaintSettings settings, bool filled)
    {
        Normalize(ref x0, ref y0, ref x1, ref y1);
        radius = Math.Clamp(radius, 0, Math.Min(x1 - x0, y1 - y0) / 2);
        if (radius == 0) { DrawRectangle(buffer, x0, y0, x1, y1, settings, filled); return; }
        if (filled)
        {
            for (int y = y0; y <= y1; y++)
            {
                int inset = 0;
                if (y < y0 + radius)
                {
                    double dy = y0 + radius - y;
                    inset = radius - (int)Math.Floor(Math.Sqrt(Math.Max(0d, radius * radius - dy * dy)));
                }
                else if (y > y1 - radius)
                {
                    double dy = y - (y1 - radius);
                    inset = radius - (int)Math.Floor(Math.Sqrt(Math.Max(0d, radius * radius - dy * dy)));
                }
                DrawLine(buffer, new StrokeSample(x0 + inset, y), new StrokeSample(x1 - inset, y), settings);
            }
            return;
        }
        DrawLine(buffer, new StrokeSample(x0 + radius, y0), new StrokeSample(x1 - radius, y0), settings);
        DrawLine(buffer, new StrokeSample(x0 + radius, y1), new StrokeSample(x1 - radius, y1), settings);
        DrawLine(buffer, new StrokeSample(x0, y0 + radius), new StrokeSample(x0, y1 - radius), settings);
        DrawLine(buffer, new StrokeSample(x1, y0 + radius), new StrokeSample(x1, y1 - radius), settings);
        DrawArc(buffer, x0 + radius, y0 + radius, radius, Math.PI, Math.PI * 1.5, settings);
        DrawArc(buffer, x1 - radius, y0 + radius, radius, Math.PI * 1.5, Math.PI * 2, settings);
        DrawArc(buffer, x1 - radius, y1 - radius, radius, 0, Math.PI * .5, settings);
        DrawArc(buffer, x0 + radius, y1 - radius, radius, Math.PI * .5, Math.PI, settings);
    }

    public static void DrawEllipse(PixelBuffer buffer, int x0, int y0, int x1, int y1, Rgba32 color, int size = 1) =>
        DrawEllipse(buffer, x0, y0, x1, y1, CreateSimpleSettings(color, size), false);

    public static void DrawEllipse(PixelBuffer buffer, int x0, int y0, int x1, int y1, PixelPaintSettings settings, bool filled)
    {
        Normalize(ref x0, ref y0, ref x1, ref y1);
        double rx = (x1 - x0) / 2d;
        double ry = (y1 - y0) / 2d;
        double cx = x0 + rx;
        double cy = y0 + ry;
        if (rx < .5 || ry < .5) { DrawRectangle(buffer, x0, y0, x1, y1, settings, filled); return; }
        if (filled)
        {
            for (int y = y0; y <= y1; y++)
            {
                double normalizedY = (y - cy) / ry;
                int halfWidth = (int)Math.Floor(rx * Math.Sqrt(Math.Max(0d, 1d - normalizedY * normalizedY)));
                DrawLine(buffer, new StrokeSample((int)Math.Ceiling(cx - halfWidth), y), new StrokeSample((int)Math.Floor(cx + halfWidth), y), settings);
            }
            return;
        }
        int steps = Math.Clamp((int)Math.Ceiling(Math.PI * 2d * Math.Max(rx, ry) * 2d), 16, 8192);
        List<StrokeSample> points = new(steps + 1);
        for (int index = 0; index <= steps; index++)
        {
            double angle = index * Math.PI * 2d / steps;
            int x = (int)Math.Round(cx + Math.Cos(angle) * rx);
            int y = (int)Math.Round(cy + Math.Sin(angle) * ry);
            if (points.Count == 0 || points[^1].X != x || points[^1].Y != y) points.Add(new StrokeSample(x, y));
        }
        DrawStroke(buffer, points, settings);
    }

    public static void DrawPolygon(PixelBuffer buffer, IReadOnlyList<StrokeSample> vertices, PixelPaintSettings settings, bool filled, bool closed = true)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count == 0) return;
        if (filled && vertices.Count >= 3) FillPolygon(buffer, vertices, settings);
        for (int index = 1; index < vertices.Count; index++) DrawLine(buffer, vertices[index - 1], vertices[index], settings);
        if (closed && vertices.Count > 2) DrawLine(buffer, vertices[^1], vertices[0], settings);
    }

    public static int FloodFill(PixelBuffer buffer, int startX, int startY, Rgba32 replacement) =>
        FloodFill(buffer, startX, startY, CreateSimpleSettings(replacement, 1), true, 0);

    public static int FloodFill(PixelBuffer buffer, int startX, int startY, PixelPaintSettings settings, bool contiguous = true, int tolerance = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (!buffer.TryGetPixel(startX, startY, out Rgba32 target) || !IsAllowed(settings, startX, startY)) return 0;
        int threshold = checked(tolerance * tolerance * 4);
        if (!contiguous)
        {
            int total = 0;
            for (int y = 0; y < buffer.Height; y++)
                for (int x = 0; x < buffer.Width; x++)
                    if (IsAllowed(settings, x, y) && buffer.GetPixel(x, y).ColorDistanceSquared(target) <= threshold) { PaintSingle(buffer, x, y, settings, 1d); total++; }
            return total;
        }
        bool[] visited = new bool[checked(buffer.Width * buffer.Height)];
        Queue<(int X, int Y)> queue = new();
        queue.Enqueue((startX, startY));
        int changed = 0;
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            if (x < 0 || x >= buffer.Width || y < 0 || y >= buffer.Height) continue;
            int index = y * buffer.Width + x;
            if (visited[index]) continue;
            visited[index] = true;
            if (!IsAllowed(settings, x, y)) continue;
            Rgba32 current = buffer.GetPixel(x, y);
            if (current.ColorDistanceSquared(target) > threshold) continue;
            PaintSingle(buffer, x, y, settings, 1d);
            changed++;
            queue.Enqueue((x - 1, y)); queue.Enqueue((x + 1, y)); queue.Enqueue((x, y - 1)); queue.Enqueue((x, y + 1));
        }
        return changed;
    }

    public static void DrawLinearGradient(PixelBuffer buffer, int x0, int y0, int x1, int y1, Rgba32 from, Rgba32 to, bool dither = false, DitherMatrixKind matrix = DitherMatrixKind.Bayer4x4, SelectionMask? selection = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        double dx = x1 - x0;
        double dy = y1 - y0;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0d) lengthSquared = 1d;
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                if (selection is not null && !selection.IsEmpty && !selection.Contains(x, y)) continue;
                double t = ((x - x0) * dx + (y - y0) * dy) / lengthSquared;
                t = Math.Clamp(t, 0d, 1d);
                Rgba32 selected = !dither ? Rgba32.Lerp(from, to, t) : DitherThreshold(x, y, matrix) < t ? to : from;
                if (selection is null || selection.IsEmpty || selection.Get(x, y) == 255) buffer.SetPixel(x, y, selected);
                else buffer.SetPixel(x, y, PixelCompositor.AlphaComposite(buffer.GetPixel(x, y), selected.WithAlpha((byte)(selected.A * selection.Get(x, y) / 255))));
            }
        }
    }

    public static void DitherRegion(PixelBuffer buffer, int x0, int y0, int x1, int y1, Rgba32 primary, Rgba32 secondary, double amount, DitherMatrixKind matrix, PixelPaintSettings settings)
    {
        Normalize(ref x0, ref y0, ref x1, ref y1);
        amount = Math.Clamp(amount, 0d, 1d);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                PixelPaintSettings chosen = settings with { PrimaryColor = DitherThreshold(x, y, matrix) < amount ? primary : secondary };
                PaintSingle(buffer, x, y, chosen, 1d);
            }
        }
    }

    public static double DitherThreshold(int x, int y, DitherMatrixKind kind)
    {
        return kind switch
        {
            DitherMatrixKind.Bayer2x2 => new[,] { { 0, 2 }, { 3, 1 } }[Mod(y, 2), Mod(x, 2)] / 4d,
            DitherMatrixKind.Checker => ((x + y) & 1) == 0 ? .25 : .75,
            DitherMatrixKind.Diagonal => Mod(x + y, 4) / 4d,
            _ => new[,] { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } }[Mod(y, 4), Mod(x, 4)] / 16d
        };
    }

    private static void DrawArc(PixelBuffer buffer, int cx, int cy, int radius, double startAngle, double endAngle, PixelPaintSettings settings)
    {
        int steps = Math.Max(4, (int)Math.Ceiling(radius * Math.Abs(endAngle - startAngle) * 2d));
        List<StrokeSample> points = new(steps + 1);
        for (int index = 0; index <= steps; index++)
        {
            double angle = startAngle + (endAngle - startAngle) * index / steps;
            points.Add(new StrokeSample((int)Math.Round(cx + Math.Cos(angle) * radius), (int)Math.Round(cy + Math.Sin(angle) * radius)));
        }
        DrawStroke(buffer, points, settings);
    }

    private static void FillPolygon(PixelBuffer buffer, IReadOnlyList<StrokeSample> vertices, PixelPaintSettings settings)
    {
        int minimumY = vertices.Min(point => point.Y);
        int maximumY = vertices.Max(point => point.Y);
        for (int y = minimumY; y <= maximumY; y++)
        {
            List<double> intersections = new();
            for (int index = 0; index < vertices.Count; index++)
            {
                StrokeSample first = vertices[index];
                StrokeSample second = vertices[(index + 1) % vertices.Count];
                if (first.Y == second.Y) continue;
                int low = Math.Min(first.Y, second.Y);
                int high = Math.Max(first.Y, second.Y);
                if (y < low || y >= high) continue;
                double x = first.X + (y - first.Y) * (second.X - first.X) / (double)(second.Y - first.Y);
                intersections.Add(x);
            }
            intersections.Sort();
            for (int index = 0; index + 1 < intersections.Count; index += 2)
                DrawLine(buffer, new StrokeSample((int)Math.Ceiling(intersections[index]), y), new StrokeSample((int)Math.Floor(intersections[index + 1]), y), settings);
        }
    }

    private static IEnumerable<StrokeSample> RasterizeLine(StrokeSample from, StrokeSample to)
    {
        int x0 = from.X, y0 = from.Y, x1 = to.X, y1 = to.Y;
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        int count = Math.Max(dx, -dy);
        int index = 0;
        while (true)
        {
            double t = count == 0 ? 0d : index / (double)count;
            yield return new StrokeSample(x0, y0,
                from.Pressure + (to.Pressure - from.Pressure) * t,
                from.VelocityPixelsPerSecond + (to.VelocityPixelsPerSecond - from.VelocityPixelsPerSecond) * t);
            if (x0 == x1 && y0 == y1) break;
            int twice = error * 2;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
            index++;
        }
    }

    private static List<StrokeSample> CleanPixelPerfect(List<StrokeSample> points)
    {
        if (points.Count < 3) return points;
        List<StrokeSample> result = new(points.Count) { points[0] };
        for (int index = 1; index < points.Count - 1; index++)
        {
            StrokeSample previous = result[^1];
            StrokeSample current = points[index];
            StrokeSample next = points[index + 1];
            bool diagonalNeighbours = Math.Abs(previous.X - next.X) == 1 && Math.Abs(previous.Y - next.Y) == 1;
            bool currentIsCorner = (previous.X == current.X && current.Y == next.Y) || (previous.Y == current.Y && current.X == next.X);
            if (diagonalNeighbours && currentIsCorner) continue;
            result.Add(current);
        }
        result.Add(points[^1]);
        return result;
    }

    private static Rgba32 ResolveBrushColor(PixelPaintSettings settings, int absoluteX, int absoluteY, int offsetX, int offsetY)
    {
        if (settings.Pattern is null) return settings.PrimaryColor;
        return settings.PatternAlignment switch
        {
            PatternAlignment.StrokeOrigin => settings.Pattern.Sample(absoluteX - settings.StrokeOriginX, absoluteY - settings.StrokeOriginY),
            PatternAlignment.BrushCenter => settings.Pattern.Sample(offsetX, offsetY),
            _ => settings.Pattern.Sample(absoluteX, absoluteY)
        };
    }

    private static IEnumerable<(int X, int Y)> ExpandCoordinates(PixelBuffer buffer, int x, int y, PixelPaintSettings settings)
    {
        HashSet<(int X, int Y)> candidates = new() { (x, y) };
        if ((settings.Symmetry & SymmetryMode.Horizontal) != 0)
        {
            int mirrorX = (int)Math.Round(settings.SymmetryAxisX * 2d - x);
            foreach ((int currentX, int currentY) in candidates.ToArray()) candidates.Add((mirrorX, currentY));
        }
        if ((settings.Symmetry & SymmetryMode.Vertical) != 0)
        {
            int mirrorY = (int)Math.Round(settings.SymmetryAxisY * 2d - y);
            foreach ((int currentX, int currentY) in candidates.ToArray()) candidates.Add((currentX, mirrorY));
        }
        foreach ((int candidateX, int candidateY) in candidates)
        {
            int finalX = candidateX;
            int finalY = candidateY;
            if ((settings.TiledMode & TiledMode.X) != 0) finalX = Mod(finalX, buffer.Width);
            if ((settings.TiledMode & TiledMode.Y) != 0) finalY = Mod(finalY, buffer.Height);
            if (finalX >= 0 && finalX < buffer.Width && finalY >= 0 && finalY < buffer.Height) yield return (finalX, finalY);
        }
    }

    private static void PaintSingle(PixelBuffer buffer, int x, int y, PixelPaintSettings settings, double opacityFactor)
    {
        foreach ((int targetX, int targetY) in ExpandCoordinates(buffer, x, y, settings))
        {
            if (!IsAllowed(settings, targetX, targetY)) continue;
            Rgba32 source = ResolveBrushColor(settings, targetX, targetY, 0, 0);
            buffer.SetPixel(targetX, targetY, PixelCompositor.Apply(buffer.GetPixel(targetX, targetY), source, settings, opacityFactor * SelectionFactor(settings, targetX, targetY)));
        }
    }

    private static bool IsAllowed(PixelPaintSettings settings, int x, int y) => settings.Selection is null || settings.Selection.IsEmpty || settings.Selection.Contains(x, y);
    private static double SelectionFactor(PixelPaintSettings settings, int x, int y) => settings.Selection is null || settings.Selection.IsEmpty ? 1d : settings.Selection.Get(x, y) / 255d;

    private static PixelPaintSettings CreateSimpleSettings(Rgba32 color, int size) => new()
    {
        Brush = BrushDefinition.CreateBuiltIn(BrushShape.Square, size),
        PrimaryColor = color,
        InkMode = color.A == 0 ? InkMode.CopyColorAndAlpha : InkMode.AlphaComposite
    };

    private static double Distance(StrokeSample first, StrokeSample second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static int Mod(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static void Normalize(ref int x0, ref int y0, ref int x1, ref int y1)
    {
        if (x0 > x1) (x0, x1) = (x1, x0);
        if (y0 > y1) (y0, y1) = (y1, y0);
    }
}
