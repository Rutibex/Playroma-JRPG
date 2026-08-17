using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.RpgMakerMV;

public static class MvSystemAssetTemplateFactory
{
    public static PixelBuffer Create(MvSystemAssetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        PixelBuffer result = new(definition.StockWidth, definition.StockHeight);
        result.Clear(Rgba32.Transparent);
        switch (definition.Kind)
        {
            case MvSystemAssetKind.Window:
                PaintWindowTemplate(result);
                break;
            case MvSystemAssetKind.IconSet:
            case MvSystemAssetKind.Balloon:
            case MvSystemAssetKind.Damage:
            case MvSystemAssetKind.States:
            case MvSystemAssetKind.Weapons1:
            case MvSystemAssetKind.Weapons2:
            case MvSystemAssetKind.Weapons3:
                // Cell boundaries are rendered by Sprite Studio as non-destructive guides.
                // The runtime template stays transparent so guide pixels can never be exported accidentally.
                break;
            case MvSystemAssetKind.ButtonSet:
                PaintButtonSetTemplate(result);
                break;
            case MvSystemAssetKind.Loading:
                PaintLoadingTemplate(result);
                break;
            case MvSystemAssetKind.GameOver:
                PaintGameOverTemplate(result);
                break;
            case MvSystemAssetKind.Shadow1:
            case MvSystemAssetKind.Shadow2:
                PaintShadowTemplate(result);
                break;
        }
        return result;
    }

    private static void PaintGrid(PixelBuffer buffer, int cellWidth, int cellHeight, Rgba32 background, Rgba32 guide)
    {
        buffer.Clear(background);
        for (int y = 0; y < buffer.Height; y++)
        for (int x = 0; x < buffer.Width; x++)
            if (x % cellWidth == 0 || y % cellHeight == 0 || x == buffer.Width - 1 || y == buffer.Height - 1)
                buffer.SetPixel(x, y, guide);
    }

    private static void PaintWindowTemplate(PixelBuffer buffer)
    {
        buffer.Clear(Rgba32.Transparent);
        for (int y = 0; y < 96; y++)
        for (int x = 0; x < 96; x++)
        {
            byte shade = (byte)(24 + ((x / 8 + y / 8) % 2) * 8);
            buffer.SetPixel(x, y, new Rgba32(20, 38, shade, 220));
        }
        for (int y = 96; y < 192; y++)
        for (int x = 0; x < 96; x++)
        {
            int localY = y - 96;
            byte accent = (byte)(((x / 12 + localY / 12) % 2) == 0 ? 38 : 28);
            buffer.SetPixel(x, y, new Rgba32(18, 30, accent, 118));
        }
        for (int y = 0; y < 96; y++)
        for (int x = 96; x < 192; x++)
        {
            int lx = x - 96;
            int edge = Math.Min(Math.Min(lx, 95 - lx), Math.Min(y, 95 - y));
            buffer.SetPixel(x, y, edge < 12 ? new Rgba32(220, 230, 255, 255) : Rgba32.Transparent);
        }
        FillRect(buffer, 100, 100, 40, 40, new Rgba32(255, 255, 255, 110));
        DrawRect(buffer, 100, 100, 40, 40, new Rgba32(255, 255, 255, 240));
        // Scroll arrows live inside the frame source region.
        DrawTriangle(buffer, 144, 27, upward: true, new Rgba32(245, 248, 255, 255));
        DrawTriangle(buffer, 144, 63, upward: false, new Rgba32(245, 248, 255, 255));
        // Pause animation: four 24×24 cells at 144,96.
        for (int frame = 0; frame < 4; frame++)
            FillRect(buffer, 148 + (frame % 2) * 24, 100 + (frame / 2) * 24, 16, 16, new Rgba32(255, 255, 255, (byte)(100 + frame * 45)));
        // Thirty-two text/gauge colors in 8×4 12-pixel swatches.
        for (int index = 0; index < 32; index++)
        {
            int column = index % 8, row = index / 8;
            Rgba32 swatch = new((byte)(55 + column * 25), (byte)(50 + row * 48), (byte)(220 - column * 18), 255);
            FillRect(buffer, 96 + column * 12, 144 + row * 12, 12, 12, swatch);
        }
    }

    private static void PaintButtonSetTemplate(PixelBuffer buffer)
    {
        buffer.Clear(Rgba32.Transparent);
        int[] xs = [0, 48, 96, 144, 192];
        int[] widths = [48, 48, 48, 48, 96];
        for (int item = 0; item < 5; item++)
        for (int state = 0; state < 2; state++)
        {
            int x = xs[item], y = state * 48, width = widths[item];
            Rgba32 fill = state == 0 ? new Rgba32(42, 58, 88, 235) : new Rgba32(72, 91, 130, 255);
            Rgba32 edge = state == 0 ? new Rgba32(205, 220, 250, 255) : new Rgba32(255, 230, 150, 255);
            FillRect(buffer, x + 2, y + 2, width - 4, 44, fill);
            DrawRect(buffer, x + 2, y + 2, width - 4, 44, edge);
            int centerX = x + width / 2, centerY = y + 24;
            if (item < 4)
            {
                bool plus = item >= 2;
                bool doubled = item is 0 or 3;
                DrawLine(buffer, centerX - (doubled ? 8 : 0), centerY, centerX + 8, centerY, edge);
                if (plus) DrawLine(buffer, centerX + (doubled ? 8 : 0), centerY - 8, centerX + (doubled ? 8 : 0), centerY + 8, edge);
                if (doubled)
                {
                    DrawLine(buffer, centerX - 14, centerY, centerX - 4, centerY, edge);
                    if (plus) DrawLine(buffer, centerX - 9, centerY - 5, centerX - 9, centerY + 5, edge);
                }
            }
            else
            {
                DrawLine(buffer, centerX - 14, centerY, centerX - 4, centerY + 10, edge);
                DrawLine(buffer, centerX - 4, centerY + 10, centerX + 18, centerY - 12, edge);
            }
        }
    }

    private static void PaintLoadingTemplate(PixelBuffer buffer)
    {
        buffer.Clear(Rgba32.Transparent);
        int cx = buffer.Width / 2, cy = buffer.Height / 2;
        for (int y = 0; y < buffer.Height; y++)
        for (int x = 0; x < buffer.Width; x++)
        {
            double distance = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (distance is > 42 and < 58)
            {
                double angle = Math.Atan2(y - cy, x - cx);
                byte alpha = (byte)Math.Clamp((int)((angle + Math.PI) / (2 * Math.PI) * 255), 40, 255);
                buffer.SetPixel(x, y, new Rgba32(230, 240, 255, alpha));
            }
        }
    }

    private static void PaintGameOverTemplate(PixelBuffer buffer)
    {
        for (int y = 0; y < buffer.Height; y++)
        for (int x = 0; x < buffer.Width; x++)
        {
            byte value = (byte)Math.Clamp(28 - y * 20 / Math.Max(1, buffer.Height), 0, 255);
            buffer.SetPixel(x, y, new Rgba32(value, 0, 8, 255));
        }
        int left = buffer.Width / 2 - 180, top = buffer.Height / 2 - 30;
        FillRect(buffer, left, top, 360, 60, new Rgba32(80, 0, 12, 220));
        DrawRect(buffer, left, top, 360, 60, new Rgba32(220, 180, 180, 255));
    }

    private static void PaintShadowTemplate(PixelBuffer buffer)
    {
        buffer.Clear(Rgba32.Transparent);
        double cx = (buffer.Width - 1) / 2d, cy = (buffer.Height - 1) / 2d;
        double rx = buffer.Width * .45, ry = buffer.Height * .28;
        for (int y = 0; y < buffer.Height; y++)
        for (int x = 0; x < buffer.Width; x++)
        {
            double d = Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2);
            if (d <= 1d) buffer.SetPixel(x, y, new Rgba32(0, 0, 0, (byte)Math.Clamp((int)((1d - d) * 150), 0, 150)));
        }
    }

    private static void DrawLine(PixelBuffer buffer, int x0, int y0, int x1, int y1, Rgba32 color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            buffer.TrySetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }

    private static void DrawTriangle(PixelBuffer buffer, int centerX, int centerY, bool upward, Rgba32 color)
    {
        for (int step = 0; step < 8; step++)
        {
            int y = upward ? centerY + step : centerY - step;
            int half = upward ? step : 7 - step;
            for (int x = centerX - half; x <= centerX + half; x++) buffer.TrySetPixel(x, y, color);
        }
    }

    private static void FillRect(PixelBuffer buffer, int x, int y, int width, int height, Rgba32 color)
    {
        for (int py = Math.Max(0, y); py < Math.Min(buffer.Height, y + height); py++)
        for (int px = Math.Max(0, x); px < Math.Min(buffer.Width, x + width); px++) buffer.SetPixel(px, py, color);
    }

    private static void DrawRect(PixelBuffer buffer, int x, int y, int width, int height, Rgba32 color)
    {
        for (int px = x; px < x + width; px++) { buffer.TrySetPixel(px, y, color); buffer.TrySetPixel(px, y + height - 1, color); }
        for (int py = y; py < y + height; py++) { buffer.TrySetPixel(x, py, color); buffer.TrySetPixel(x + width - 1, py, color); }
    }
}
