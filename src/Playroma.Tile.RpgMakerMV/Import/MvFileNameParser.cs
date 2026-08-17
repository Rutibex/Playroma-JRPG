using System.Text.RegularExpressions;
using Playroma.Tile.Core.Models;

namespace Playroma.Tile.RpgMakerMV.Import;

public sealed record ParsedMvFileName(string SetName, MvSheetSlot Slot, string FileNameWithoutExtension);

public static partial class MvFileNameParser
{
    [GeneratedRegex(@"^(?:(?<set>.+?)[_\- ]+)?(?<slot>A1|A2|A3|A4|A5|B|C|D|E)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();

    public static bool TryParse(string filePath, out ParsedMvFileName? result)
    {
        result = null;
        if (!string.Equals(Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(filePath);
        var match = FileNameRegex().Match(stem);
        if (!match.Success || !Enum.TryParse<MvSheetSlot>(match.Groups["slot"].Value, true, out var slot))
        {
            return false;
        }

        var setName = match.Groups["set"].Value.Trim().TrimEnd('_', '-', ' ');
        if (string.IsNullOrWhiteSpace(setName))
        {
            setName = "Tileset";
        }

        result = new ParsedMvFileName(setName, slot, stem);
        return true;
    }
}
