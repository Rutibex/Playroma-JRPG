using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.Indexing;

internal static partial class HeuristicReferenceScanner
{
    private static readonly IReadOnlyDictionary<string, ReferenceTargetKind> DataArrayKinds =
        new Dictionary<string, ReferenceTargetKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["Actors"] = ReferenceTargetKind.Actor,
            ["Classes"] = ReferenceTargetKind.Class,
            ["Skills"] = ReferenceTargetKind.Skill,
            ["Items"] = ReferenceTargetKind.Item,
            ["Weapons"] = ReferenceTargetKind.Weapon,
            ["Armors"] = ReferenceTargetKind.Armor,
            ["Enemies"] = ReferenceTargetKind.Enemy,
            ["Troops"] = ReferenceTargetKind.Troop,
            ["States"] = ReferenceTargetKind.State,
            ["Animations"] = ReferenceTargetKind.Animation,
            ["Tilesets"] = ReferenceTargetKind.Tileset,
            ["CommonEvents"] = ReferenceTargetKind.CommonEvent,
            ["MapInfos"] = ReferenceTargetKind.Map
        };

    private static readonly IReadOnlyDictionary<string, ReferenceTargetKind> NamedKinds =
        new Dictionary<string, ReferenceTargetKind>(StringComparer.OrdinalIgnoreCase)
        {
            ["actor"] = ReferenceTargetKind.Actor,
            ["class"] = ReferenceTargetKind.Class,
            ["skill"] = ReferenceTargetKind.Skill,
            ["item"] = ReferenceTargetKind.Item,
            ["weapon"] = ReferenceTargetKind.Weapon,
            ["armor"] = ReferenceTargetKind.Armor,
            ["armour"] = ReferenceTargetKind.Armor,
            ["enemy"] = ReferenceTargetKind.Enemy,
            ["troop"] = ReferenceTargetKind.Troop,
            ["state"] = ReferenceTargetKind.State,
            ["animation"] = ReferenceTargetKind.Animation,
            ["tileset"] = ReferenceTargetKind.Tileset,
            ["common event"] = ReferenceTargetKind.CommonEvent,
            ["commonevent"] = ReferenceTargetKind.CommonEvent,
            ["map"] = ReferenceTargetKind.Map,
            ["switch"] = ReferenceTargetKind.Switch,
            ["variable"] = ReferenceTargetKind.Variable
        };

    public static void ScanJsonStrings(JsonElement root, MvReferenceExtractor.ExtractionContext context) =>
        ScanElement(root, string.Empty, null, context);

    public static void ScanText(
        string? text,
        ReferenceSource source,
        ReferenceUsageKind usage,
        MvReferenceExtractor.ExtractionContext context)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        HashSet<ReferenceTarget> emitted = [];

        foreach (Match match in DataArrayRegex().Matches(text))
        {
            string family = match.Groups[1].Value;
            if (!DataArrayKinds.TryGetValue(family, out ReferenceTargetKind kind)) continue;
            if (!TryParsePositive(match.Groups[2].Value, out int id)) continue;
            Add(context, source, new ReferenceTarget(kind, id), usage, match.Value, emitted);
        }

        foreach (Match match in GameSwitchRegex().Matches(text))
        {
            if (TryParsePositive(match.Groups[1].Value, out int id))
            {
                Add(context, source, new ReferenceTarget(ReferenceTargetKind.Switch, id), usage, match.Value, emitted);
            }
        }

        foreach (Match match in GameVariableRegex().Matches(text))
        {
            if (TryParsePositive(match.Groups[1].Value, out int id))
            {
                Add(context, source, new ReferenceTarget(ReferenceTargetKind.Variable, id), usage, match.Value, emitted);
            }
        }

        foreach (Match match in GameActorRegex().Matches(text))
        {
            if (TryParsePositive(match.Groups[1].Value, out int id))
            {
                Add(context, source, new ReferenceTarget(ReferenceTargetKind.Actor, id), usage, match.Value, emitted);
            }
        }

        foreach (Match match in NamedIdRegex().Matches(text))
        {
            string name = Regex.Replace(match.Groups[1].Value, "\\s+", " ").Trim();
            if (!NamedKinds.TryGetValue(name, out ReferenceTargetKind kind)) continue;
            if (!TryParsePositive(match.Groups[2].Value, out int id)) continue;
            Add(context, source, new ReferenceTarget(kind, id), usage, match.Value, emitted);
        }
    }

    private static void ScanElement(
        JsonElement element,
        string path,
        string? propertyName,
        MvReferenceExtractor.ExtractionContext context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string propertyPath = path + "/" + Escape(property.Name);
                    ScanElement(property.Value, propertyPath, property.Name, context);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement value in element.EnumerateArray())
                {
                    ScanElement(value, path + "/" + index.ToString(CultureInfo.InvariantCulture), propertyName, context);
                    index++;
                }
                break;
            case JsonValueKind.String:
                string? text = element.GetString();
                ReferenceUsageKind usage = GetUsage(propertyName, path);
                ScanText(
                    text,
                    new ReferenceSource(
                        context.RelativePath,
                        string.IsNullOrEmpty(path) ? "/" : path,
                        context.MapId,
                        RecordId: TryGetRecordId(path)),
                    usage,
                    context);
                break;
        }
    }

    private static int? TryGetRecordId(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string first = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : null;
    }

    private static ReferenceUsageKind GetUsage(string? propertyName, string path)
    {
        if (string.Equals(propertyName, "note", StringComparison.OrdinalIgnoreCase)) return ReferenceUsageKind.Note;
        if (string.Equals(propertyName, "formula", StringComparison.OrdinalIgnoreCase)) return ReferenceUsageKind.Formula;
        if (string.Equals(propertyName, "script", StringComparison.OrdinalIgnoreCase)) return ReferenceUsageKind.Script;
        if (path.Contains("/parameters/", StringComparison.Ordinal)) return ReferenceUsageKind.Script;
        return ReferenceUsageKind.Unknown;
    }

    private static void Add(
        MvReferenceExtractor.ExtractionContext context,
        ReferenceSource source,
        ReferenceTarget target,
        ReferenceUsageKind usage,
        string match,
        ISet<ReferenceTarget> emitted)
    {
        if (!emitted.Add(target)) return;
        context.Add(source, target, usage, ReferenceConfidence.Heuristic, match);
    }

    private static bool TryParsePositive(string value, out int id) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    private static string Escape(string value) => value
        .Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal);

    [GeneratedRegex(@"\$data(Actors|Classes|Skills|Items|Weapons|Armors|Enemies|Troops|States|Animations|Tilesets|CommonEvents|MapInfos)\s*\[\s*(\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DataArrayRegex();

    [GeneratedRegex(@"\$gameSwitches\s*\.\s*value\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GameSwitchRegex();

    [GeneratedRegex(@"\$gameVariables\s*\.\s*value\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GameVariableRegex();

    [GeneratedRegex(@"\$gameActors\s*\.\s*actor\s*\(\s*(\d+)\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GameActorRegex();

    [GeneratedRegex(@"\b(actor|class|skill|item|weapon|armor|armour|enemy|troop|state|animation|tileset|common\s*event|commonevent|map|switch|variable)\s*(?:id\s*)?(?:[:=#]|\[|\()\s*(\d+)\s*(?:\]|\))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedIdRegex();
}
