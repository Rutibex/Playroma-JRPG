using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Editor.WinForms.Database;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            using TroopFormationCanvas canvas = new() { Size = new Size(816, 624) };
            MvTroopMemberSnapshot[] members =
            [
                new(0, 1, 250, 390, false, "{}"),
                new(1, 2, 410, 350, true, "{}"),
                new(2, 3, 580, 400, false, "{}")
            ];
            canvas.SetMembers(members, new Dictionary<int, string> { [1] = "Slime", [2] = "Witch", [3] = "Dragon" }, new Dictionary<int, Bitmap>());
            using Bitmap image = new(816, 624);
            canvas.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            if (image.Width != 816 || image.Height != 624) throw new InvalidOperationException("Unexpected visual-test output size.");
            if (!canvas.Members.Select(member => member.Index).SequenceEqual([0, 1, 2])) throw new InvalidOperationException("Visual canvas changed member indexes.");
            Console.WriteLine("PASS Phase 9C troop formation visual smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 9C visual test: " + exception);
            return 1;
        }
    }
}
