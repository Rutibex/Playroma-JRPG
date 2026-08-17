namespace Playroma.JRPG.Editor.WinForms.Eventing;

public sealed record EventCommandPaletteGroup(
    int Page,
    string Name,
    IReadOnlyList<int> CommandCodes);

public sealed record EventCommandPaletteLayout(
    IReadOnlyList<EventCommandPaletteGroup> Groups)
{
    public IReadOnlyList<EventCommandPaletteGroup> ForPage(int page) =>
        Groups.Where(group => group.Page == page).ToArray();

    public IReadOnlyList<int> AllCommandCodes => Groups
        .SelectMany(group => group.CommandCodes)
        .Distinct()
        .ToArray();

    public static EventCommandPaletteLayout CreateRpgMakerMv()
    {
        return new EventCommandPaletteLayout(
        [
            new(1, "Message", [101, 102, 103, 104, 105, 108]),
            new(1, "Game Progression", [121, 122, 123, 124]),
            new(1, "Flow Control", [111, 112, 113, 115, 117, 118, 119]),
            new(1, "Party", [125, 126, 127, 128, 129]),
            new(1, "Actor", [311, 312, 313, 314, 315, 316, 317, 318, 319, 320, 321, 322, 323, 324, 325, 326]),

            new(2, "Movement", [201, 202, 203, 204, 205, 206, 211, 212, 213, 214, 216, 217]),
            new(2, "Picture and Screen", [221, 222, 223, 224, 225, 231, 232, 233, 234, 235, 236]),
            new(2, "Timing", [230]),
            new(2, "Audio and Video", [241, 242, 243, 244, 245, 246, 249, 250, 251, 261]),
            new(2, "Map", [281, 282, 283, 284, 285]),

            new(3, "System Settings", [132, 133, 134, 135, 136, 137, 138, 139, 140]),
            new(3, "Scene Control", [301, 302, 303, 351, 352, 353, 354]),
            new(3, "Battle", [331, 332, 333, 334, 335, 336, 337, 339, 340, 342]),
            new(3, "Advanced", [355, 356])
        ]);
    }
}
