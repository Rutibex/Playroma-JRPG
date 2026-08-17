namespace Playroma.JRPG.Assets;

public static class AssetQueryParser
{
    public static AssetQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new AssetQuery();
        List<string> free = [];
        AssetCategory? category = null;
        AssetMediaKind? media = null;
        bool? used = null;
        bool? missing = null;
        bool? encrypted = null;
        bool? duplicate = null;
        string? extension = null;
        AssetStatus? status = null;
        foreach (string token in Tokenize(text))
        {
            int colon = token.IndexOf(':');
            if (colon <= 0)
            {
                free.Add(token);
                continue;
            }

            string key = token[..colon].ToLowerInvariant();
            string value = token[(colon + 1)..].Trim('"');
            switch (key)
            {
                case "category" when Enum.TryParse(value, true, out AssetCategory parsedCategory): category = parsedCategory; break;
                case "media" when Enum.TryParse(value, true, out AssetMediaKind parsedMedia): media = parsedMedia; break;
                case "used" when bool.TryParse(value, out bool parsedUsed): used = parsedUsed; break;
                case "unused" when bool.TryParse(value, out bool parsedUnused): used = !parsedUnused; break;
                case "missing" when bool.TryParse(value, out bool parsedMissing): missing = parsedMissing; break;
                case "encrypted" when bool.TryParse(value, out bool parsedEncrypted): encrypted = parsedEncrypted; break;
                case "duplicate" when bool.TryParse(value, out bool parsedDuplicate): duplicate = parsedDuplicate; break;
                case "extension": extension = value.TrimStart('.'); break;
                case "status" when Enum.TryParse(value, true, out AssetStatus parsedStatus): status = parsedStatus; break;
                case "name": free.Add(value); break;
                default: free.Add(token); break;
            }
        }

        return new AssetQuery(string.Join(' ', free), category, media, used, missing, encrypted, duplicate, extension, status);
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        List<string> tokens = [];
        System.Text.StringBuilder current = new();
        bool quoted = false;
        foreach (char character in text)
        {
            if (character == '"')
            {
                quoted = !quoted;
                current.Append(character);
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
            }
            else current.Append(character);
        }

        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
