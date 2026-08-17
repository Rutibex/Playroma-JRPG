using System.Text;

namespace Playroma.Tile.RpgMakerMV.Import;

public sealed record CatalogEntry(int LineNumber, string PrimaryName, string AlternateName);

public static class CatalogTextParser
{
    public static IReadOnlyList<CatalogEntry> Parse(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var result = new List<CatalogEntry>();
        var lines = File.ReadAllLines(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false));

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim().TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separator = line.IndexOf('|');
            string primary;
            string alternate;
            if (separator < 0)
            {
                primary = line;
                alternate = string.Empty;
            }
            else
            {
                primary = line[..separator].Trim();
                alternate = line[(separator + 1)..].Trim().TrimStart('|').Trim();
            }

            result.Add(new CatalogEntry(index + 1, primary, alternate));
        }

        return result;
    }

    public static void Write(string path, IEnumerable<CatalogEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var lines = entries.Select(entry => string.IsNullOrWhiteSpace(entry.AlternateName)
            ? entry.PrimaryName
            : $"{entry.PrimaryName}|{entry.AlternateName}");
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
