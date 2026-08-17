using Playroma.JRPG.Assets;
using Playroma.JRPG.Specifications;

namespace Playroma.JRPG.Eventing;

public sealed class EventCommandRegistry
{
    private readonly IReadOnlyDictionary<int, EventCommandDefinition> _commands;
    private readonly IReadOnlyDictionary<int, MovementRouteCommandDefinition> _routeCommands;

    private EventCommandRegistry(
        IReadOnlyDictionary<int, EventCommandDefinition> commands,
        IReadOnlyDictionary<int, MovementRouteCommandDefinition> routeCommands)
    {
        _commands = commands;
        _routeCommands = routeCommands;
    }

    public IReadOnlyCollection<EventCommandDefinition> Commands => _commands.Values.ToArray();
    public IReadOnlyCollection<MovementRouteCommandDefinition> MovementRouteCommands => _routeCommands.Values.ToArray();

    public static EventCommandRegistry CreateBuiltIn(string? repositoryRoot = null)
    {
        MvSpecificationCatalog catalog = MvSpecificationCatalog.Load(repositoryRoot);
        Dictionary<int, EventCommandDefinition> commands = catalog.EventCommands.ToDictionary(
            item => item.Code,
            item => new EventCommandDefinition(
                item.Code,
                item.Name,
                item.Category,
                ParseStructure(item.Structure),
                item.Editor,
                item.IsContinuation,
                ConvertParameters(item.Parameters),
                item.SampleParameters,
                item.Source));
        Dictionary<int, MovementRouteCommandDefinition> routes = catalog.MovementRouteCommands.ToDictionary(
            item => item.Code,
            item => new MovementRouteCommandDefinition(
                item.Code,
                item.Name,
                item.Editor,
                ConvertParameters(item.Parameters),
                item.SampleParameters,
                item.Source));
        return new EventCommandRegistry(commands, routes);
    }

    public EventCommandDefinition Get(int code)
    {
        if (_commands.TryGetValue(code, out EventCommandDefinition? definition)) return definition;
        return new EventCommandDefinition(
            code,
            $"Unknown Command {code}",
            "Unknown",
            EventCommandStructureKind.Single,
            "raw",
            false,
            [],
            EmptyArray(),
            "Unknown or plugin-created command");
    }

    public MovementRouteCommandDefinition GetMovementRouteCommand(int code)
    {
        if (_routeCommands.TryGetValue(code, out MovementRouteCommandDefinition? definition)) return definition;
        return new MovementRouteCommandDefinition(
            code,
            $"Unknown Move Command {code}",
            "raw",
            [],
            EmptyArray(),
            "Unknown or plugin-created movement command");
    }

    public IReadOnlyList<EventCommandDefinition> Search(string? query, string? category = null)
    {
        string text = query?.Trim() ?? string.Empty;
        IEnumerable<EventCommandDefinition> candidates = _commands.Values.Where(item => item.IsInsertable);
        if (!string.IsNullOrWhiteSpace(category)) candidates = candidates.Where(item => string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));
        if (text.Length == 0) return candidates.OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        return candidates
            .Select(item => new { Definition = item, Score = Score(item, text) })
            .Where(item => item.Score < int.MaxValue)
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Definition.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Definition)
            .ToArray();
    }

    private static int Score(EventCommandDefinition definition, string text)
    {
        if (definition.Name.Equals(text, StringComparison.OrdinalIgnoreCase)) return 0;
        if (definition.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase)) return 10;
        int name = definition.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (name >= 0) return 30 + name;
        int category = definition.Category.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (category >= 0) return 70 + category;
        string compact = definition.Name.Replace(" ", string.Empty, StringComparison.Ordinal);
        string query = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Contains(query, StringComparison.OrdinalIgnoreCase)) return 100;
        return int.MaxValue;
    }

    private static IReadOnlyList<EventCommandParameterDescriptor> ConvertParameters(
        IReadOnlyList<EventCommandParameterSpecification>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return [];
        return parameters.Select(item => new EventCommandParameterDescriptor(
            item.Index,
            item.Name,
            item.DisplayName,
            ParseParameterKind(item.Type),
            item.Target,
            ParseAssetCategory(item.AssetCategory),
            item.Options,
            item.Optional,
            item.Minimum,
            item.Maximum,
            item.Unit,
            item.Description)).ToArray();
    }

    private static EventCommandStructureKind ParseStructure(string? value) => value?.ToLowerInvariant() switch
    {
        "continuation" => EventCommandStructureKind.Continuation,
        "continuationgroup" => EventCommandStructureKind.ContinuationGroup,
        "conditional" => EventCommandStructureKind.Conditional,
        "loop" => EventCommandStructureKind.Loop,
        "choices" => EventCommandStructureKind.Choices,
        "battle" => EventCommandStructureKind.Battle,
        _ => EventCommandStructureKind.Single
    };

    private static EventParameterKind ParseParameterKind(string? value) => value?.ToLowerInvariant() switch
    {
        "integer" => EventParameterKind.Integer,
        "boolean" => EventParameterKind.Boolean,
        "string" => EventParameterKind.String,
        "stringarray" => EventParameterKind.StringArray,
        "array" => EventParameterKind.Array,
        "object" => EventParameterKind.Object,
        "enum" => EventParameterKind.Enum,
        "databaseid" => EventParameterKind.DatabaseId,
        "asset" => EventParameterKind.Asset,
        "audio" => EventParameterKind.Audio,
        "color" => EventParameterKind.Color,
        "tone" => EventParameterKind.Tone,
        "moveroute" => EventParameterKind.MovementRoute,
        _ => EventParameterKind.Json
    };

    private static AssetCategory? ParseAssetCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse(value, ignoreCase: true, out AssetCategory category) ? category : null;
    }

    private static System.Text.Json.JsonElement EmptyArray()
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }
}
