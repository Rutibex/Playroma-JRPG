using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Rendering;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            using Bitmap sheet = new(768, 768);
            using (Graphics graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.White);
                graphics.FillRectangle(Brushes.ForestGreen, 0, 0, 384, 384);
                graphics.FillRectangle(Brushes.SaddleBrown, 384, 0, 384, 384);
            }
            IReadOnlyList<TilesetPaletteCell> cells = TilesetPaletteCellBuilder.Build(TileSheetKind.B, sheet.Width, sheet.Height);
            int[] flags = new int[8192];
            flags[cells[0].TileId] = (int)MvPassageDirection.All;
            flags[cells[1].TileId] = MvTilesetFlag.StarMask | (7 << 12);
            using TilesetFlagCanvas canvas = new() { Size = new Size(900, 820), Zoom = 0.75f };
            canvas.SetImage((Bitmap)sheet.Clone(), cells);
            canvas.SetFlags(flags);
            using Bitmap image = new(canvas.Width, canvas.Height);
            canvas.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            bool changed = false;
            Color background = Color.FromArgb(38, 38, 42);
            for (int y = 0; y < image.Height && !changed; y += 20)
                for (int x = 0; x < image.Width; x += 20)
                    if (image.GetPixel(x, y).ToArgb() != background.ToArgb()) { changed = true; break; }
            if (!changed) throw new InvalidOperationException("Tileset flag canvas did not render the tileset image and overlays.");
            Console.WriteLine("PASS Phase 9E tileset visual smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 9E visual test: " + exception);
            return 1;
        }
    }
}
