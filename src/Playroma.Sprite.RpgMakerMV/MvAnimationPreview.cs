using Playroma.Sprite.Core;

namespace Playroma.Sprite.RpgMakerMV;

public sealed record MvPreviewMotion(string Id, string DisplayName, IReadOnlyList<int> CellIndexes, bool Looping, int DefaultFrameMilliseconds = 150);

public static class MvAnimationPreview
{
    private static readonly string[] SideViewMotionNames =
    [
        "Walk", "Wait", "Chant", "Guard", "Damage", "Evade",
        "Thrust", "Swing", "Missile", "Skill", "Spell", "Item",
        "Escape", "Victory", "Dying", "Abnormal", "Sleep", "Dead"
    ];

    private static readonly HashSet<int> SideViewLoopingMotions = [0, 1, 2, 3, 12, 13, 14, 15, 16, 17];

    public static IReadOnlyList<MvPreviewMotion> GetMotions(string? profileId, int characterIndex = 0)
    {
        if (string.Equals(profileId, "mv-sv-actor", StringComparison.OrdinalIgnoreCase)) return BuildSideViewMotions();
        if (string.Equals(profileId, "mv-character-standard", StringComparison.OrdinalIgnoreCase)) return BuildCharacterMotions(characterIndex, false);
        if (string.Equals(profileId, "mv-character-single", StringComparison.OrdinalIgnoreCase)) return BuildCharacterMotions(0, true);
        return [];
    }

    public static IReadOnlyList<IntRect> ResolveSourceRects(string? profileId, int sheetWidth, int sheetHeight, MvPreviewMotion motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        (int columns, int rows) = string.Equals(profileId, "mv-sv-actor", StringComparison.OrdinalIgnoreCase) ? (9, 6)
            : string.Equals(profileId, "mv-character-standard", StringComparison.OrdinalIgnoreCase) ? (12, 8)
            : string.Equals(profileId, "mv-character-single", StringComparison.OrdinalIgnoreCase) ? (3, 4)
            : throw new InvalidOperationException("The selected profile has no RPG Maker MV motion preview.");
        if (sheetWidth % columns != 0 || sheetHeight % rows != 0) throw new InvalidOperationException("The sprite dimensions do not divide evenly into the selected MV motion grid.");
        int cellWidth = sheetWidth / columns;
        int cellHeight = sheetHeight / rows;
        return motion.CellIndexes.Select(index => new IntRect((index % columns) * cellWidth, (index / columns) * cellHeight, cellWidth, cellHeight)).ToArray();
    }

    private static IReadOnlyList<MvPreviewMotion> BuildCharacterMotions(int characterIndex, bool single)
    {
        if (!single && (characterIndex < 0 || characterIndex >= 8)) throw new ArgumentOutOfRangeException(nameof(characterIndex));
        int characterColumn = single ? 0 : (characterIndex % 4) * 3;
        int characterRow = single ? 0 : (characterIndex / 4) * 4;
        string[] directions = ["Down", "Left", "Right", "Up"];
        List<MvPreviewMotion> result = [];
        for (int direction = 0; direction < 4; direction++)
        {
            int row = characterRow + direction;
            int columns = single ? 3 : 12;
            int first = row * columns + characterColumn;
            result.Add(new MvPreviewMotion(directions[direction].ToLowerInvariant(), directions[direction], [first, first + 1, first + 2, first + 1], true, 150));
        }
        return result;
    }

    private static IReadOnlyList<MvPreviewMotion> BuildSideViewMotions()
    {
        List<MvPreviewMotion> result = [];
        for (int motion = 0; motion < SideViewMotionNames.Length; motion++)
        {
            int row = motion / 3;
            int group = motion % 3;
            int first = row * 9 + group * 3;
            bool looping = SideViewLoopingMotions.Contains(motion);
            IReadOnlyList<int> cells = looping ? [first, first + 1, first + 2, first + 1] : [first, first + 1, first + 2];
            result.Add(new MvPreviewMotion(SideViewMotionNames[motion].ToLowerInvariant(), SideViewMotionNames[motion], cells, looping, 150));
        }
        return result;
    }
}
