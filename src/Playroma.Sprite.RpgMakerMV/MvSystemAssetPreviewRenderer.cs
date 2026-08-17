using Playroma.Sprite.Core;
using Playroma.Sprite.Pixel;

namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvSystemAssetPreviewOptions(
    int ItemIndex = 0,
    int FrameIndex = 0,
    string DamageText = "1234",
    bool DarkBackground = true,
    int PreviewWidth = 420,
    int PreviewHeight = 260);

public static class MvSystemAssetPreviewRenderer
{
    public static PixelBuffer Render(PixelBuffer source, MvSystemAssetDefinition definition, MvSystemAssetPreviewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(definition);
        options ??= new();
        int width = Math.Max(64, options.PreviewWidth);
        int height = Math.Max(64, options.PreviewHeight);
        PixelBuffer preview = Checker(width, height, options.DarkBackground);
        switch (definition.Kind)
        {
            case MvSystemAssetKind.Window: RenderWindow(source, preview, options.FrameIndex); break;
            case MvSystemAssetKind.IconSet: RenderIcon(source, preview, options.ItemIndex); break;
            case MvSystemAssetKind.Damage: RenderDamage(source, preview, options.DamageText, options.ItemIndex); break;
            case MvSystemAssetKind.Balloon: RenderGridCell(source, preview, definition, options.ItemIndex, options.FrameIndex, 4); break;
            case MvSystemAssetKind.States: RenderGridCell(source, preview, definition, options.ItemIndex, options.FrameIndex, 2); break;
            case MvSystemAssetKind.Weapons1:
            case MvSystemAssetKind.Weapons2:
            case MvSystemAssetKind.Weapons3:
                RenderWeapon(source, preview, definition, options.ItemIndex, options.FrameIndex); break;
            case MvSystemAssetKind.ButtonSet: RenderGridCell(source, preview, definition, options.ItemIndex, options.FrameIndex, 3); break;
            case MvSystemAssetKind.Loading: RenderCentered(source, preview, 1); break;
            case MvSystemAssetKind.GameOver: RenderFit(source, preview, 12); break;
            case MvSystemAssetKind.Shadow1: RenderShadow(source, preview, true); break;
            case MvSystemAssetKind.Shadow2: RenderShadow(source, preview, false); break;
            default: RenderFit(source, preview, 12); break;
        }
        return preview;
    }

    public static IntRect ResolveCell(MvSystemAssetDefinition definition, int sourceWidth, int sourceHeight, int itemIndex, int frameIndex)
    {
        (int columns, int rows, int cellWidth, int cellHeight) = MvSystemAssetCatalog.ResolveGrid(definition, sourceWidth, sourceHeight);
        int count = columns * rows;
        int cellIndex;
        if (definition.Kind == MvSystemAssetKind.Damage)
        {
            int row = Math.Clamp(itemIndex, 0, rows - 1);
            cellIndex = row * columns + Math.Clamp(frameIndex, 0, columns - 1);
        }
        else if (definition.Kind == MvSystemAssetKind.ButtonSet)
        {
            int item = Math.Clamp(itemIndex, 0, 4);
            int state = Math.Clamp(frameIndex, 0, 1);
            int[] xs = [0, 48, 96, 144, 192];
            int[] widths = [48, 48, 48, 48, 96];
            return new IntRect(xs[item], state * 48, widths[item], 48);
        }
        else if (definition.Kind is MvSystemAssetKind.Weapons1 or MvSystemAssetKind.Weapons2 or MvSystemAssetKind.Weapons3)
        {
            int maximumItem = definition.Kind == MvSystemAssetKind.Weapons3 ? 5 : 11;
            int item = Math.Clamp(itemIndex, 0, maximumItem);
            int group = definition.Kind == MvSystemAssetKind.Weapons3 ? 0 : item / 6;
            int row = item % 6;
            int weaponColumn = group * 3 + Math.Clamp(frameIndex, 0, 2);
            weaponColumn = Math.Clamp(weaponColumn, 0, columns - 1);
            row = Math.Clamp(row, 0, rows - 1);
            return new IntRect(weaponColumn * cellWidth, row * cellHeight, cellWidth, cellHeight);
        }
        else if (definition.FramesPerItem > 1)
        {
            cellIndex = Math.Max(0, itemIndex) * definition.FramesPerItem + Math.Clamp(frameIndex, 0, definition.FramesPerItem - 1);
        }
        else cellIndex = Math.Max(0, itemIndex);
        cellIndex = Math.Clamp(cellIndex, 0, Math.Max(0, count - 1));
        int column = cellIndex % columns, rowIndex = cellIndex / columns;
        return new IntRect(column * cellWidth, rowIndex * cellHeight, cellWidth, cellHeight);
    }

    private static void RenderIcon(PixelBuffer source, PixelBuffer target, int iconIndex)
    {
        MvSystemAssetDefinition definition = MvSystemAssetCatalog.GetRequired("mv-iconset");
        IntRect cell = ResolveCell(definition, source.Width, source.Height, iconIndex, 0);
        int scale = Math.Max(1, Math.Min((target.Width - 48) / Math.Max(1, cell.Width), (target.Height - 48) / Math.Max(1, cell.Height)));
        BlitScaled(source, cell, target, (target.Width - cell.Width * scale) / 2, (target.Height - cell.Height * scale) / 2, cell.Width * scale, cell.Height * scale);
        DrawRect(target, (target.Width - cell.Width * scale) / 2 - 1, (target.Height - cell.Height * scale) / 2 - 1, cell.Width * scale + 2, cell.Height * scale + 2, new Rgba32(230, 235, 255, 255));
    }

    private static void RenderGridCell(PixelBuffer source, PixelBuffer target, MvSystemAssetDefinition definition, int item, int frame, int preferredScale)
    {
        IntRect cell = ResolveCell(definition, source.Width, source.Height, item, frame);
        int scale = Math.Max(1, Math.Min(preferredScale, Math.Min((target.Width - 48) / Math.Max(1, cell.Width), (target.Height - 48) / Math.Max(1, cell.Height))));
        int x = (target.Width - cell.Width * scale) / 2;
        int y = (target.Height - cell.Height * scale) / 2;
        BlitScaled(source, cell, target, x, y, cell.Width * scale, cell.Height * scale);
        DrawRect(target, x - 1, y - 1, cell.Width * scale + 2, cell.Height * scale + 2, new Rgba32(220, 230, 255, 255));
    }

    private static void RenderDamage(PixelBuffer source, PixelBuffer target, string text, int row)
    {
        string glyphs = string.IsNullOrWhiteSpace(text) ? "0" : new string(text.Where(char.IsDigit).Take(8).ToArray());
        if (glyphs.Length == 0) glyphs = "0";
        MvSystemAssetDefinition definition = MvSystemAssetCatalog.GetRequired("mv-damage");
        IntRect firstCell = ResolveCell(definition, source.Width, source.Height, row, 0);
        int scale = Math.Max(1, Math.Min(3, (target.Width - 32) / Math.Max(1, glyphs.Length * firstCell.Width)));
        int totalWidth = glyphs.Length * firstCell.Width * scale;
        int x = (target.Width - totalWidth) / 2;
        int y = (target.Height - firstCell.Height * scale) / 2;
        foreach (char glyph in glyphs)
        {
            IntRect cell = ResolveCell(definition, source.Width, source.Height, row, glyph - '0');
            BlitScaled(source, cell, target, x, y, cell.Width * scale, cell.Height * scale);
            x += cell.Width * scale;
        }
    }

    private static void RenderWeapon(PixelBuffer source, PixelBuffer target, MvSystemAssetDefinition definition, int item, int frame)
    {
        IntRect cell = ResolveCell(definition, source.Width, source.Height, item, frame);
        int centerX = target.Width / 2, floor = target.Height - 42;
        FillRect(target, centerX - 15, floor - 88, 30, 72, new Rgba32(80, 115, 170, 255));
        FillRect(target, centerX - 22, floor - 18, 44, 18, new Rgba32(55, 75, 110, 255));
        int scale = Math.Max(1, Math.Min(3, Math.Min((target.Width - 40) / cell.Width, (target.Height - 40) / cell.Height)));
        BlitScaled(source, cell, target, centerX - cell.Width * scale / 2, floor - 110, cell.Width * scale, cell.Height * scale);
        DrawLine(target, 20, floor, target.Width - 20, floor, new Rgba32(140, 150, 175, 255));
    }

    private static void RenderWindow(PixelBuffer source, PixelBuffer target, int pauseFrame)
    {
        FillRect(target, 22, 24, target.Width - 44, target.Height - 48, new Rgba32(12, 18, 30, 245));
        if (source.Width < 192 || source.Height < 192) { RenderFit(source, target, 24); return; }
        IntRect backgroundTone = new(0, 0, 96, 96);
        IntRect backgroundPattern = new(0, 96, 96, 96);
        IntRect frame = new(96, 0, 96, 96);
        IntRect window = new(32, 34, target.Width - 64, target.Height - 68);
        BlitScaled(source, backgroundTone, target, window.X, window.Y, window.Width, window.Height);
        Tile(source, backgroundPattern, target, window);
        NineSlice(source, frame, target, window, 24);
        IntRect cursorSource = new(96, 96, 48, 48);
        IntRect cursorTarget = new(window.X + 30, window.Y + 62, Math.Max(80, window.Width - 80), 44);
        NineSlice(source, cursorSource, target, cursorTarget, 12);
        // Simple text bars make the preview read like an MV command window without depending on fonts.
        Rgba32 normal = SampleWindowColor(source, 0, new Rgba32(238, 242, 255, 220));
        Rgba32 system = SampleWindowColor(source, 16, new Rgba32(255, 235, 170, 235));
        FillRect(target, window.X + 42, window.Y + 22, Math.Min(170, window.Width - 70), 5, normal);
        FillRect(target, window.X + 42, window.Y + 42, Math.Min(125, window.Width - 70), 5, new Rgba32(normal.R, normal.G, normal.B, 185));
        FillRect(target, cursorTarget.X + 15, cursorTarget.Y + 18, Math.Min(150, cursorTarget.Width - 30), 5, system);
        BlitScaled(source, new IntRect(132, 24, 24, 12), target, window.X + window.Width / 2 - 12, window.Y + 3, 24, 12);
        BlitScaled(source, new IntRect(132, 60, 24, 12), target, window.X + window.Width / 2 - 12, window.Bottom - 15, 24, 12);
        int normalizedPauseFrame = Math.Clamp(pauseFrame, 0, 3);
        IntRect pause = new(144 + (normalizedPauseFrame % 2) * 24, 96 + (normalizedPauseFrame / 2) * 24, 24, 24);
        BlitScaled(source, pause, target, window.Right - 34, window.Bottom - 30, 24, 24);
    }

    private static void RenderCentered(PixelBuffer source, PixelBuffer target, int maximumScale)
    {
        int scale = Math.Max(1, Math.Min(maximumScale, Math.Min((target.Width - 32) / source.Width, (target.Height - 32) / source.Height)));
        int width = source.Width * scale, height = source.Height * scale;
        BlitScaled(source, new IntRect(0, 0, source.Width, source.Height), target, (target.Width - width) / 2, (target.Height - height) / 2, width, height);
    }

    private static void RenderFit(PixelBuffer source, PixelBuffer target, int margin)
    {
        double scale = Math.Min((target.Width - margin * 2d) / source.Width, (target.Height - margin * 2d) / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        BlitScaled(source, new IntRect(0, 0, source.Width, source.Height), target, (target.Width - width) / 2, (target.Height - height) / 2, width, height);
    }

    private static void RenderShadow(PixelBuffer source, PixelBuffer target, bool vehicle)
    {
        int floor = target.Height - 50;
        IntRect destination = new((target.Width - source.Width * 3) / 2, floor - source.Height * 2 / 3, source.Width * 3, source.Height * 3);
        BlitScaled(source, new IntRect(0, 0, source.Width, source.Height), target, destination.X, destination.Y, destination.Width, destination.Height);
        if (vehicle)
        {
            FillRect(target, target.Width / 2 - 62, floor - 72, 124, 48, new Rgba32(78, 105, 145, 255));
            FillRect(target, target.Width / 2 - 38, floor - 94, 76, 30, new Rgba32(110, 140, 180, 255));
        }
        else
        {
            FillRect(target, target.Width / 2 - 16, floor - 88, 32, 64, new Rgba32(90, 125, 175, 255));
            FillRect(target, target.Width / 2 - 12, floor - 108, 24, 24, new Rgba32(120, 155, 205, 255));
        }
        DrawLine(target, 18, floor, target.Width - 18, floor, new Rgba32(125, 135, 155, 255));
    }

    private static Rgba32 SampleWindowColor(PixelBuffer source, int index, Rgba32 fallback)
    {
        int x = 96 + Math.Clamp(index, 0, 31) % 8 * 12 + 6;
        int y = 144 + Math.Clamp(index, 0, 31) / 8 * 12 + 6;
        if (x < 0 || x >= source.Width || y < 0 || y >= source.Height) return fallback;
        Rgba32 sampled = source.GetPixel(x, y);
        return sampled.A == 0 ? fallback : sampled;
    }

    private static PixelBuffer Checker(int width, int height, bool dark)
    {
        PixelBuffer result = new(width, height);
        Rgba32 a = dark ? new Rgba32(31, 34, 42, 255) : new Rgba32(215, 218, 224, 255);
        Rgba32 b = dark ? new Rgba32(42, 46, 56, 255) : new Rgba32(235, 237, 241, 255);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) result.SetPixel(x, y, ((x / 16 + y / 16) & 1) == 0 ? a : b);
        return result;
    }

    private static void Tile(PixelBuffer source, IntRect sourceRect, PixelBuffer target, IntRect targetRect)
    {
        for (int y = targetRect.Y; y < targetRect.Bottom; y++)
        for (int x = targetRect.X; x < targetRect.Right; x++)
        {
            int sx = sourceRect.X + Mod(x - targetRect.X, sourceRect.Width);
            int sy = sourceRect.Y + Mod(y - targetRect.Y, sourceRect.Height);
            Blend(target, x, y, source.GetPixel(sx, sy));
        }
    }

    private static void NineSlice(PixelBuffer source, IntRect sourceRect, PixelBuffer target, IntRect targetRect, int border)
    {
        border = Math.Clamp(border, 1, Math.Min(sourceRect.Width, sourceRect.Height) / 2);
        int[] sx = [sourceRect.X, sourceRect.X + border, sourceRect.Right - border, sourceRect.Right];
        int[] sy = [sourceRect.Y, sourceRect.Y + border, sourceRect.Bottom - border, sourceRect.Bottom];
        int targetBorderX = Math.Min(border, targetRect.Width / 2);
        int targetBorderY = Math.Min(border, targetRect.Height / 2);
        int[] tx = [targetRect.X, targetRect.X + targetBorderX, targetRect.Right - targetBorderX, targetRect.Right];
        int[] ty = [targetRect.Y, targetRect.Y + targetBorderY, targetRect.Bottom - targetBorderY, targetRect.Bottom];
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        {
            IntRect sr = new(sx[column], sy[row], sx[column + 1] - sx[column], sy[row + 1] - sy[row]);
            int dw = tx[column + 1] - tx[column], dh = ty[row + 1] - ty[row];
            if (dw > 0 && dh > 0) BlitScaled(source, sr, target, tx[column], ty[row], dw, dh);
        }
    }

    private static void BlitScaled(PixelBuffer source, IntRect sourceRect, PixelBuffer target, int destinationX, int destinationY, int destinationWidth, int destinationHeight)
    {
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || destinationWidth <= 0 || destinationHeight <= 0) return;
        for (int y = 0; y < destinationHeight; y++)
        for (int x = 0; x < destinationWidth; x++)
        {
            int tx = destinationX + x, ty = destinationY + y;
            if (tx < 0 || tx >= target.Width || ty < 0 || ty >= target.Height) continue;
            int sx = sourceRect.X + Math.Min(sourceRect.Width - 1, x * sourceRect.Width / destinationWidth);
            int sy = sourceRect.Y + Math.Min(sourceRect.Height - 1, y * sourceRect.Height / destinationHeight);
            if (sx < 0 || sx >= source.Width || sy < 0 || sy >= source.Height) continue;
            Blend(target, tx, ty, source.GetPixel(sx, sy));
        }
    }

    private static void Blend(PixelBuffer target, int x, int y, Rgba32 source)
    {
        if (source.A == 0) return;
        if (source.A == 255) { target.SetPixel(x, y, source); return; }
        Rgba32 destination = target.GetPixel(x, y);
        double alpha = source.A / 255d;
        target.SetPixel(x, y, new Rgba32(
            (byte)Math.Clamp((int)Math.Round(source.R * alpha + destination.R * (1d - alpha)), 0, 255),
            (byte)Math.Clamp((int)Math.Round(source.G * alpha + destination.G * (1d - alpha)), 0, 255),
            (byte)Math.Clamp((int)Math.Round(source.B * alpha + destination.B * (1d - alpha)), 0, 255),
            255));
    }

    private static void FillRect(PixelBuffer target, int x, int y, int width, int height, Rgba32 color)
    {
        for (int py = Math.Max(0, y); py < Math.Min(target.Height, y + height); py++)
        for (int px = Math.Max(0, x); px < Math.Min(target.Width, x + width); px++) Blend(target, px, py, color);
    }

    private static void DrawRect(PixelBuffer target, int x, int y, int width, int height, Rgba32 color)
    {
        for (int px = x; px < x + width; px++) { if (px >= 0 && px < target.Width) { if (y >= 0 && y < target.Height) target.SetPixel(px, y, color); int bottom = y + height - 1; if (bottom >= 0 && bottom < target.Height) target.SetPixel(px, bottom, color); } }
        for (int py = y; py < y + height; py++) { if (py >= 0 && py < target.Height) { if (x >= 0 && x < target.Width) target.SetPixel(x, py, color); int right = x + width - 1; if (right >= 0 && right < target.Width) target.SetPixel(right, py, color); } }
    }

    private static void DrawLine(PixelBuffer target, int x0, int y0, int x1, int y1, Rgba32 color)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            if (x0 >= 0 && x0 < target.Width && y0 >= 0 && y0 < target.Height) target.SetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1) break;
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
