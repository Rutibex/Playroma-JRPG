using Playroma.JRPG.Eventing;

Random random = new(104729);
EventStructureProjector projector = new();
EventStructureCompiler compiler = new();
for (int iteration = 0; iteration < 500; iteration++)
{
    int count = random.Next(1, 180);
    List<string> raw = [];
    for (int index = 0; index < count; index++)
    {
        int[] codes = [0, 101, 102, 111, 112, 205, 301, 355, 401, 402, 411, 412, 413, 505, 601, 604, 655, 9999];
        int code = codes[random.Next(codes.Length)]; int indent = random.Next(0, 8);
        string parameters = code switch { 401 or 655 => "[\"fuzz\"]", _ => "[]" };
        raw.Add(RawEventCommand.CreateJson(code, indent, parameters));
    }
    RawEventCommand[] commands = raw.Select((value, index) => RawEventCommand.Parse(index, value)).ToArray();
    EventListSnapshot snapshot = new(EventListAddress.ForCommonEvent("data/CommonEvents.json", 1), commands, "[" + string.Join(',', raw) + "]", "fuzz", DateTimeOffset.UtcNow);
    EventStructureDocument structure = projector.Project(snapshot);
    if (!compiler.IsLosslessRoundTrip(structure, out string? reason)) throw new InvalidOperationException($"Fuzz iteration {iteration} was not lossless: {reason}");
}
Console.WriteLine("PASS 500 malformed/unknown event-list fuzz projections were lossless.");
return 0;
