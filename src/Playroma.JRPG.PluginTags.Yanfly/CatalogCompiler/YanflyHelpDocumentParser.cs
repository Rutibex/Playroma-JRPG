using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyHelpDocumentParser
{
    private static readonly Regex EnglishBlock = new(@"/\*:(?![A-Za-z])(?<body>.*?)\*/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
        TimeSpan.FromSeconds(1));
    private static readonly Regex Directive = new(
        @"^\s*@(?<name>param|parent|type|default|min|max|decimals|option|value|desc)\b\s*(?<value>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));

    private static readonly IReadOnlyDictionary<string, PluginDocumentKind> ContextTerms =
        new Dictionary<string, PluginDocumentKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["actor"] = PluginDocumentKind.Actor, ["actors"] = PluginDocumentKind.Actor,
            ["class"] = PluginDocumentKind.Class, ["classes"] = PluginDocumentKind.Class,
            ["skill"] = PluginDocumentKind.Skill, ["skills"] = PluginDocumentKind.Skill,
            ["item"] = PluginDocumentKind.Item, ["items"] = PluginDocumentKind.Item,
            ["weapon"] = PluginDocumentKind.Weapon, ["weapons"] = PluginDocumentKind.Weapon,
            ["armor"] = PluginDocumentKind.Armor, ["armors"] = PluginDocumentKind.Armor,
            ["armour"] = PluginDocumentKind.Armor, ["armours"] = PluginDocumentKind.Armor,
            ["enemy"] = PluginDocumentKind.Enemy, ["enemies"] = PluginDocumentKind.Enemy,
            ["state"] = PluginDocumentKind.State, ["states"] = PluginDocumentKind.State,
            ["map"] = PluginDocumentKind.Map, ["maps"] = PluginDocumentKind.Map,
            ["tileset"] = PluginDocumentKind.Tileset, ["tilesets"] = PluginDocumentKind.Tileset,
            ["event"] = PluginDocumentKind.Event, ["events"] = PluginDocumentKind.Event,
            ["troop"] = PluginDocumentKind.Troop, ["troops"] = PluginDocumentKind.Troop,
            ["common event"] = PluginDocumentKind.CommonEvent,
            ["common events"] = PluginDocumentKind.CommonEvent
        };

    public YanflyHelpDocument Parse(YanflyCanonicalPluginSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Match block = EnglishBlock.Match(source.SourceText);
        if (!block.Success) return new(source.PluginId, string.Empty, string.Empty, [], [], []);
        string pluginBlock = block.Groups["body"].Value;
        string description = ExtractDirectiveValue(pluginBlock, "plugindesc");
        int helpIndex = pluginBlock.IndexOf("@help", StringComparison.OrdinalIgnoreCase);
        string help = helpIndex < 0 ? string.Empty : pluginBlock[(helpIndex + 5)..];
        IReadOnlyList<string> lines = CleanLines(help);
        return new(source.PluginId, description, help, lines,
            ParseSections(lines, source.InventoryEntry), ParseParameters(pluginBlock));
    }

    public static IReadOnlyList<PluginDocumentKind> InferDocumentKinds(
        IReadOnlyList<string> lines, int lineIndex, YanflyHelpSection? section,
        YanflyPluginInventoryEntry inventory)
    {
        HashSet<PluginDocumentKind> kinds = [];
        if (section is not null)
        {
            foreach (PluginDocumentKind kind in section.DocumentKinds) kinds.Add(kind);
            AddContextTerms(kinds, section.Heading);
        }
        for (int i = Math.Max(0, lineIndex - 12); i <= Math.Min(lines.Count - 1, lineIndex + 2); i++)
            AddContextTerms(kinds, lines[i]);
        foreach (string context in inventory.DeclaredContexts)
        {
            if (Enum.TryParse(context, true, out PluginDocumentKind parsed)) kinds.Add(parsed);
            if (context.Equals("CommonEvent", StringComparison.OrdinalIgnoreCase)) kinds.Add(PluginDocumentKind.CommonEvent);
        }
        if (section?.Surface == YanflyAuthoringSurface.MapNote) return [PluginDocumentKind.Map];
        if (section?.Surface == YanflyAuthoringSurface.TilesetNote) return [PluginDocumentKind.Tileset];
        if (section?.Surface == YanflyAuthoringSurface.EventNote) return [PluginDocumentKind.Event];
        if (section?.Surface == YanflyAuthoringSurface.EventPageComment) return [PluginDocumentKind.EventPage];
        if (section?.Surface == YanflyAuthoringSurface.MessageTextCode) return [PluginDocumentKind.Message];
        if (section?.Surface == YanflyAuthoringSurface.VariableNameMetadata) return [PluginDocumentKind.Variable];
        if (section?.Surface == YanflyAuthoringSurface.SwitchNameMetadata) return [PluginDocumentKind.Switch];
        return kinds.OrderBy(k => k).ToArray();
    }

    private static IReadOnlyList<string> CleanLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(line => Regex.Replace(line, @"^\s*\* ?", string.Empty,
                RegexOptions.CultureInvariant).TrimEnd()).ToArray();

    private static IReadOnlyList<YanflyHelpSection> ParseSections(
        IReadOnlyList<string> lines, YanflyPluginInventoryEntry inventory)
    {
        List<(string Heading, int Start)> headings = [];
        for (int i = 1; i + 1 < lines.Count; i++)
        {
            if (!IsEquals(lines[i - 1]) || !IsEquals(lines[i + 1])) continue;
            string heading = lines[i].Trim();
            if (heading.Length > 0 && !IsEquals(heading)) headings.Add((heading, i + 1));
        }
        if (headings.Count == 0 && lines.Count > 0) headings.Add(("Help", 1));
        List<YanflyHelpSection> result = [];
        for (int i = 0; i < headings.Count; i++)
        {
            (string heading, int start) = headings[i];
            int end = i + 1 < headings.Count ? headings[i + 1].Start - 2 : lines.Count;
            YanflyAuthoringSurface? surface = InferSurface(heading);
            HashSet<PluginDocumentKind> kinds = [];
            AddContextTerms(kinds, heading);
            if (surface == YanflyAuthoringSurface.MapNote) kinds.Add(PluginDocumentKind.Map);
            if (surface == YanflyAuthoringSurface.TilesetNote) kinds.Add(PluginDocumentKind.Tileset);
            if (surface == YanflyAuthoringSurface.EventNote) kinds.Add(PluginDocumentKind.Event);
            if (surface == YanflyAuthoringSurface.EventPageComment) kinds.Add(PluginDocumentKind.EventPage);
            if (surface == YanflyAuthoringSurface.MessageTextCode) kinds.Add(PluginDocumentKind.Message);
            if (surface == YanflyAuthoringSurface.VariableNameMetadata) kinds.Add(PluginDocumentKind.Variable);
            if (surface == YanflyAuthoringSurface.SwitchNameMetadata) kinds.Add(PluginDocumentKind.Switch);
            if (surface == YanflyAuthoringSurface.DatabaseNote && kinds.Count == 0)
                foreach (string context in inventory.DeclaredContexts)
                    if (Enum.TryParse(context, true, out PluginDocumentKind parsed)) kinds.Add(parsed);
            result.Add(new(heading, start, Math.Max(start, end), surface, kinds.OrderBy(k => k).ToArray()));
        }
        return result;
    }

    private static YanflyAuthoringSurface? InferSurface(string heading)
    {
        string value = heading.ToLowerInvariant();
        if (value.Contains("changelog")) return null;
        if (value.Contains("plugin command")) return YanflyAuthoringSurface.PluginCommand;
        if (value.Contains("comment tag") || value.Contains("event comment")) return YanflyAuthoringSurface.EventPageComment;
        if (value.Contains("action sequence") || value.Contains("ala melody")) return YanflyAuthoringSurface.ActionSequence;
        if (value.Contains("text code") || value.Contains("escape code") || value.Contains("word wrapping")) return YanflyAuthoringSurface.MessageTextCode;
        if (value.Contains("script call")) return YanflyAuthoringSurface.ScriptCall;
        if (value.Contains("move route") || value.Contains("movement route")) return YanflyAuthoringSurface.MoveRouteScript;
        if (value.Contains("variable name")) return YanflyAuthoringSurface.VariableNameMetadata;
        if (value.Contains("switch name")) return YanflyAuthoringSurface.SwitchNameMetadata;
        if (value.Contains("tileset") && value.Contains("note")) return YanflyAuthoringSurface.TilesetNote;
        if (value.Contains("map") && value.Contains("note")) return YanflyAuthoringSurface.MapNote;
        if (value.Contains("event") && value.Contains("note")) return YanflyAuthoringSurface.EventNote;
        if (value.Contains("notetag") || value.Contains("note tag") || value.Contains("lunatic mode")) return YanflyAuthoringSurface.DatabaseNote;
        return null;
    }

    private static IReadOnlyList<YanflyPluginParameterDefinition> ParseParameters(string pluginBlock)
    {
        int helpIndex = pluginBlock.IndexOf("@help", StringComparison.OrdinalIgnoreCase);
        string parameterHeader = helpIndex < 0 ? pluginBlock : pluginBlock[..helpIndex];
        string[] lines = parameterHeader.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        List<YanflyPluginParameterDefinition> result = [];
        ParameterBuilder? current = null;
        string? optionName = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = Regex.Replace(lines[i], @"^\s*\* ?", string.Empty,
                RegexOptions.CultureInvariant).TrimEnd();
            Match match = Directive.Match(line);
            if (!match.Success)
            {
                if (current is { ReadingDescription: true } && line.Trim().Length > 0)
                    current.Description.Add(line.Trim());
                continue;
            }
            string name = match.Groups["name"].Value.ToLowerInvariant();
            string value = match.Groups["value"].Value.Trim();
            if (name == "param")
            {
                if (current is not null) result.Add(current.Build());
                current = new(value, i + 1);
                optionName = null;
                continue;
            }
            if (current is null) continue;
            current.ReadingDescription = name == "desc";
            switch (name)
            {
                case "parent": current.Parent = Null(value); break;
                case "type": current.Type = Null(value); break;
                case "default": current.DefaultValue = value; break;
                case "min": current.Minimum = Null(value); break;
                case "max": current.Maximum = Null(value); break;
                case "decimals": current.DecimalPlaces = Null(value); break;
                case "option": optionName = value; break;
                case "value": current.Options.Add(new(optionName ?? value, value)); optionName = null; break;
                case "desc": if (value.Length > 0) current.Description.Add(value); break;
            }
        }
        if (current is not null) result.Add(current.Build());
        return result;
    }

    private static string ExtractDirectiveValue(string block, string directive)
    {
        Match match = Regex.Match(block, "@" + Regex.Escape(directive) + @"\s+(?<v>[^\r\n]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["v"].Value.Trim() : string.Empty;
    }

    private static void AddContextTerms(ISet<PluginDocumentKind> target, string text)
    {
        foreach ((string term, PluginDocumentKind kind) in ContextTerms)
            if (Regex.IsMatch(text, @"\b" + Regex.Escape(term) + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) target.Add(kind);
    }

    private static bool IsEquals(string value)
    {
        string v = value.Trim();
        return v.Length >= 12 && v.All(c => c == '=');
    }
    private static string? Null(string value) => value.Length == 0 ? null : value;

    private sealed class ParameterBuilder
    {
        public ParameterBuilder(string name, int line) { Name = name; HeaderLine = line; }
        public string Name { get; }
        public int HeaderLine { get; }
        public string? Parent { get; set; }
        public string? Type { get; set; }
        public string? DefaultValue { get; set; }
        public string? Minimum { get; set; }
        public string? Maximum { get; set; }
        public string? DecimalPlaces { get; set; }
        public bool ReadingDescription { get; set; }
        public List<YanflyPluginParameterOption> Options { get; } = [];
        public List<string> Description { get; } = [];
        public YanflyPluginParameterDefinition Build()
        {
            string description = string.Join(" ", Description).Trim();
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description))).ToLowerInvariant();
            return new(Name, Name, Parent, Type, DefaultValue, Minimum, Maximum,
                DecimalPlaces, Options.ToArray(), string.Empty, HeaderLine, hash);
        }
    }
}
