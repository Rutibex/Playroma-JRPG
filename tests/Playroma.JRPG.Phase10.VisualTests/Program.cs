using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Eventing;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            EventCommandRegistry registry = EventCommandRegistry.CreateBuiltIn(FindRepositoryRoot());
            EventCommandFormatter formatter = new(registry);
            string[] raw =
            [
                RawEventCommand.CreateJson(101, 0, "[\"Actor1\",0,0,2]"),
                RawEventCommand.CreateJson(401, 0, "[\"The crystal is hidden below.\"]"),
                RawEventCommand.CreateJson(111, 0, "[0,1,0]"),
                RawEventCommand.CreateJson(102, 1, "[[\"Search\",\"Leave\"],-2,0,2,0]"),
                RawEventCommand.CreateJson(402, 1, "[0,\"Search\"]"),
                RawEventCommand.CreateJson(230, 2, "[30]"),
                RawEventCommand.CreateJson(402, 1, "[1,\"Leave\"]"),
                RawEventCommand.CreateJson(404, 1, "[]"),
                RawEventCommand.CreateJson(411, 0, "[]"),
                RawEventCommand.CreateJson(250, 1, "[{\"name\":\"Buzzer1\",\"volume\":90,\"pitch\":100,\"pan\":0}]"),
                RawEventCommand.CreateJson(412, 0, "[]"),
                RawEventCommand.CreateJson(0, 0, "[]")
            ];
            RawEventCommand[] commands = raw.Select((value, index) => RawEventCommand.Parse(index, value)).ToArray();
            EventListSnapshot snapshot = new(
                EventListAddress.ForCommonEvent("data/CommonEvents.json", 1),
                commands,
                "[" + string.Join(',', raw) + "]",
                "visual",
                DateTimeOffset.UtcNow);
            EventStructureDocument structure = new EventStructureProjector().Project(snapshot);
            IReadOnlyList<ProjectedEventRow> rows = EventRowProjector.Build(structure);

            using Form host = new() { ClientSize = new Size(1100, 720), ShowInTaskbar = false };
            using VirtualEventCommandListControl control = new() { Dock = DockStyle.Fill };
            host.Controls.Add(control);
            host.CreateControl();
            control.CreateControl();
            control.SetRows(rows, registry, formatter, 2);
            using Bitmap image = new(host.ClientSize.Width, host.ClientSize.Height);
            control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));

            int changedPixels = 0;
            int background = SystemColors.Window.ToArgb();
            for (int y = 0; y < image.Height; y += 12)
            {
                for (int x = 0; x < image.Width; x += 12)
                {
                    if (image.GetPixel(x, y).ToArgb() != background) changedPixels++;
                }
            }
            if (rows.Count < raw.Length) throw new InvalidOperationException("The structural row projection lost event commands.");
            if (changedPixels < 25) throw new InvalidOperationException("The virtual event-command list did not visibly render its rows.");
            Console.WriteLine("PASS Phase 10 virtual event-list visual smoke test");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL Phase 10 visual test: " + exception);
            return 1;
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Playroma.JRPG.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find the Playroma jRPG repository root.");
    }
}
