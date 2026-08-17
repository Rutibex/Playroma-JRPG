using Playroma.JRPG.AnimationEditing;
using Playroma.JRPG.Editor.WinForms.Database;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            using Bitmap sheet = BuildSheet();
            MvAnimationCellSnapshot first = new(0, -60, 0, 100, 0, false, 255, 0, "[0,-60,0,100,0,0,255,0]");
            MvAnimationCellSnapshot second = new(1, 60, 0, 120, 45, true, 180, 1, "[1,60,0,120,45,1,180,1]");
            MvAnimationSnapshot animation = new(1, "Visual", "Sheet", 0, "", 0, 1,
                [new MvAnimationFrameSnapshot(0, [first, second], "[]")], [], new Dictionary<string,string>(), "{}");
            using AnimationCanvas canvas = new() { Size = new Size(816, 624) };
            canvas.SetAnimation(animation, sheet, null);
            using Bitmap image = new(816, 624);
            canvas.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            Color center = image.GetPixel(408, 312);
            if (center.ToArgb() == Color.FromArgb(35, 38, 45).ToArgb()) throw new InvalidOperationException("Animation canvas did not draw projected cells.");
            Color additive = AnimationSoftwareBlend.Blend(Color.FromArgb(255, 100, 100, 100), Color.FromArgb(255, 100, 20, 10), 1);
            if (additive.R <= 100) throw new InvalidOperationException("Additive software blend did not increase the red channel.");
            using Bitmap destination = new(1, 1);
            destination.SetPixel(0, 0, Color.FromArgb(255, 100, 100, 100));
            using Bitmap source = new(1, 1);
            source.SetPixel(0, 0, Color.FromArgb(255, 100, 20, 10));
            AnimationSoftwareBlend.Composite(destination, source, (int)MvAnimationBlendMode.Additive);
            if (destination.GetPixel(0, 0).R <= 100) throw new InvalidOperationException("Additive bitmap compositor did not update the destination surface.");
            Console.WriteLine("PASS Phase 9D animation visual smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 9D visual test: " + exception);
            return 1;
        }
    }

    private static Bitmap BuildSheet()
    {
        Bitmap bitmap = new(960, 192);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.FillEllipse(Brushes.Red, 12, 12, 168, 168);
        graphics.FillRectangle(Brushes.Blue, 204, 12, 168, 168);
        return bitmap;
    }
}
