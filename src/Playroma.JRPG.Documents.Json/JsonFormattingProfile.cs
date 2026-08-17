using System.Text;

namespace Playroma.JRPG.Documents.Json;

public enum JsonIndentationStyle
{
    Minified,
    Spaces,
    Tabs
}

public sealed record JsonFormattingProfile(
    bool HasUtf8Bom,
    string NewLine,
    JsonIndentationStyle Indentation,
    int IndentWidth,
    bool HasTrailingNewLine,
    bool SpaceAfterColon)
{
    public string Indent(int depth) => Indentation switch
    {
        JsonIndentationStyle.Tabs => new string('\t', Math.Max(0, depth)),
        JsonIndentationStyle.Spaces => new string(' ', Math.Max(0, depth) * Math.Max(1, IndentWidth)),
        _ => string.Empty
    };
}

public static class JsonFormattingProfileDetector
{
    public static JsonFormattingProfile Detect(ReadOnlySpan<byte> source)
    {
        bool bom = source.Length >= 3 && source[0] == 0xEF && source[1] == 0xBB && source[2] == 0xBF;
        string text = Encoding.UTF8.GetString(source);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool trailing = text.EndsWith("\r\n", StringComparison.Ordinal) || text.EndsWith('\n');
        string[] lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        List<int> spaceIndents = [];
        bool tabs = false;
        foreach (string line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            int spaces = 0;
            int tabCount = 0;
            while (spaces + tabCount < line.Length)
            {
                char current = line[spaces + tabCount];
                if (current == ' ') spaces++;
                else if (current == '\t') tabCount++;
                else break;
            }

            if (tabCount > 0) tabs = true;
            else if (spaces > 0) spaceIndents.Add(spaces);
        }

        JsonIndentationStyle style;
        int width;
        if (lines.Length <= 1)
        {
            style = JsonIndentationStyle.Minified;
            width = 0;
        }
        else if (tabs)
        {
            style = JsonIndentationStyle.Tabs;
            width = 1;
        }
        else
        {
            style = JsonIndentationStyle.Spaces;
            width = GreatestCommonDivisor(spaceIndents.Where(value => value > 0).DefaultIfEmpty(2));
            if (width <= 0 || width > 8) width = 2;
        }

        bool spaceAfterColon = text.Contains(": ", StringComparison.Ordinal);
        return new JsonFormattingProfile(bom, newline, style, width, trailing, spaceAfterColon);
    }

    private static int GreatestCommonDivisor(IEnumerable<int> values)
    {
        int result = 0;
        foreach (int value in values)
        {
            result = result == 0 ? value : Gcd(result, value);
        }

        return result;
    }

    private static int Gcd(int left, int right)
    {
        while (right != 0)
        {
            int temporary = left % right;
            left = right;
            right = temporary;
        }

        return Math.Abs(left);
    }
}
