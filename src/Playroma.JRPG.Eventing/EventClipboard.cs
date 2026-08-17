using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playroma.JRPG.Eventing;

public enum EventReferenceConfidence { Exact, Heuristic }

public sealed record EventClipboardReference(
    string Kind,
    int? Id,
    string? Name,
    EventReferenceConfidence Confidence,
    int RawCommandIndex,
    int ParameterIndex);

public sealed record EventClipboardAsset(
    string Category,
    string Name,
    EventReferenceConfidence Confidence,
    int RawCommandIndex);

public sealed record EventClipboardPackage(
    string Format,
    int Version,
    string SourceProjectIdentity,
    string RuntimeFamily,
    IReadOnlyList<string> Commands,
    IReadOnlyList<EventClipboardReference> References,
    IReadOnlyList<EventClipboardAsset> Assets,
    string ReadableText)
{
    public const string CurrentFormat = "playroma.mv.event-commands";
}

public static class EventClipboardCodec
{
    private const string ReadableHeader = "PLAYROMA-EVENT-TEXT 1";
    private const string PayloadMarker = "PLAYROMA-RAW-PAYLOAD ";

    public static EventClipboardPackage Create(
        IEnumerable<RawEventCommand> commands,
        string sourceProjectIdentity,
        EventCommandRegistry registry,
        EventCommandFormatter formatter)
    {
        RawEventCommand[] rows = commands.Select((command, index) => command.WithRawIndex(index)).ToArray();
        EventDependencyExtractor extractor = new();
        IReadOnlyList<EventClipboardReference> references = extractor.Extract(rows, registry);
        IReadOnlyList<EventClipboardAsset> assets = extractor.ExtractAssets(rows, registry);
        string readable = ToReadableText(rows, registry, formatter);
        return new EventClipboardPackage(
            EventClipboardPackage.CurrentFormat, 1, sourceProjectIdentity, "MV",
            rows.Select(row => row.RawJson).ToArray(), references, assets, readable);
    }

    public static string Serialize(EventClipboardPackage package) =>
        JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = false });

    public static EventClipboardPackage Deserialize(string json)
    {
        EventClipboardPackage? package = JsonSerializer.Deserialize<EventClipboardPackage>(json);
        if (package is null || package.Format != EventClipboardPackage.CurrentFormat || package.Version != 1)
        {
            throw new InvalidDataException("The clipboard does not contain a supported Playroma event-command package.");
        }
        foreach (string command in package.Commands) _ = RawEventCommand.Parse(0, command);
        return package;
    }

    public static string ToReadableText(
        IReadOnlyList<RawEventCommand> commands,
        EventCommandRegistry registry,
        EventCommandFormatter formatter)
    {
        StringBuilder text = new();
        text.AppendLine(ReadableHeader);
        foreach (RawEventCommand command in commands)
        {
            EventCommandSummary summary = formatter.Format(command);
            text.Append(' ', Math.Max(0, command.Indent) * 4);
            text.AppendLine(summary.PrimaryText);
            if (!string.IsNullOrWhiteSpace(summary.SecondaryText))
            {
                text.Append(' ', Math.Max(0, command.Indent + 1) * 4);
                text.AppendLine(summary.SecondaryText);
            }
        }
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(commands.Select(row => row.RawJson).ToArray())));
        text.Append(PayloadMarker).Append(payload);
        return text.ToString();
    }

    public static IReadOnlyList<string> ParseReadableText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!text.StartsWith(ReadableHeader, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Readable event text must begin with the Playroma event-text header.");
        }
        int marker = text.LastIndexOf(PayloadMarker, StringComparison.Ordinal);
        if (marker < 0) throw new InvalidDataException("The readable event text has no lossless raw payload.");
        string encoded = text[(marker + PayloadMarker.Length)..].Trim();
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        string[]? commands = JsonSerializer.Deserialize<string[]>(json);
        return commands ?? throw new InvalidDataException("The readable event payload is invalid.");
    }
}

public sealed class EventDependencyExtractor
{
    public IReadOnlyList<EventClipboardReference> Extract(
        IEnumerable<RawEventCommand> commands,
        EventCommandRegistry registry)
    {
        List<EventClipboardReference> result = [];
        foreach (RawEventCommand command in commands)
        {
            JsonElement parameters = JsonParameterReader.ParseArray(command.ParametersRawJson);
            EventCommandDefinition definition = registry.Get(command.Code);

            foreach (EventCommandParameterDescriptor descriptor in definition.Parameters)
            {
                if (descriptor.Kind != EventParameterKind.DatabaseId || string.IsNullOrWhiteSpace(descriptor.Target))
                {
                    continue;
                }

                if (!ShouldExtractDirectReference(command.Code, descriptor.Index, parameters))
                {
                    continue;
                }

                AddDatabaseReference(result, command, parameters, descriptor.Index, descriptor.Target);
            }

            AddConditionalVariableReferences(result, command, parameters);

            if (command.Code is 355 or 655 or 356)
            {
                result.Add(new EventClipboardReference(
                    "script-or-plugin",
                    null,
                    null,
                    EventReferenceConfidence.Heuristic,
                    command.RawIndex,
                    0));
            }
        }

        return result;
    }

    public IReadOnlyList<EventClipboardAsset> ExtractAssets(
        IEnumerable<RawEventCommand> commands,
        EventCommandRegistry registry)
    {
        List<EventClipboardAsset> result = [];
        foreach (RawEventCommand command in commands)
        {
            JsonElement parameters = JsonParameterReader.ParseArray(command.ParametersRawJson);
            EventCommandDefinition definition = registry.Get(command.Code);

            foreach (EventCommandParameterDescriptor descriptor in definition.Parameters)
            {
                if (descriptor.Kind == EventParameterKind.Asset && descriptor.AssetCategory is not null)
                {
                    AddAsset(result, command, parameters, descriptor.Index, descriptor.AssetCategory.Value.ToString());
                }
                else if (descriptor.Kind == EventParameterKind.Audio)
                {
                    AddAudioAsset(result, command, parameters, descriptor.Index, AudioCategoryFor(command.Code));
                }
            }

            if (command.Code == 205)
            {
                AddHeuristic(result, command, "character-route");
            }
        }

        return result;
    }

    private static bool ShouldExtractDirectReference(int commandCode, int parameterIndex, JsonElement parameters)
    {
        return commandCode switch
        {
            // Direct/variable designation. A variable-driven target is handled separately below.
            201 when parameterIndex == 1 => ReadInt(parameters, 0) == 0,
            301 when parameterIndex == 1 => ReadInt(parameters, 0) == 0,
            _ => true
        };
    }

    private static void AddConditionalVariableReferences(
        List<EventClipboardReference> result,
        RawEventCommand command,
        JsonElement parameters)
    {
        switch (command.Code)
        {
            case 201 when ReadInt(parameters, 0) == 1:
                AddDatabaseReference(result, command, parameters, 1, "Variable");
                AddDatabaseReference(result, command, parameters, 2, "Variable");
                AddDatabaseReference(result, command, parameters, 3, "Variable");
                break;

            case 202 when ReadInt(parameters, 1) == 0:
                AddDatabaseReference(result, command, parameters, 2, "Map");
                break;

            case 202 when ReadInt(parameters, 1) == 1:
                AddDatabaseReference(result, command, parameters, 2, "Variable");
                AddDatabaseReference(result, command, parameters, 3, "Variable");
                AddDatabaseReference(result, command, parameters, 4, "Variable");
                break;

            case 301 when ReadInt(parameters, 0) == 1:
                AddDatabaseReference(result, command, parameters, 1, "Variable");
                break;
        }
    }

    private static void AddDatabaseReference(
        List<EventClipboardReference> result,
        RawEventCommand command,
        JsonElement parameters,
        int parameterIndex,
        string target)
    {
        if (!JsonParameterReader.TryGet(parameters, parameterIndex, out JsonElement value)
            || !value.TryGetInt32(out int id))
        {
            return;
        }

        result.Add(new EventClipboardReference(
            NormalizeKind(target),
            id,
            null,
            EventReferenceConfidence.Exact,
            command.RawIndex,
            parameterIndex));
    }

    private static string NormalizeKind(string target) => string.IsNullOrEmpty(target)
        ? target
        : char.ToLowerInvariant(target[0]) + target[1..];

    private static int? ReadInt(JsonElement parameters, int index) =>
        JsonParameterReader.TryGet(parameters, index, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : null;

    private static void AddAsset(
        List<EventClipboardAsset> result,
        RawEventCommand command,
        JsonElement parameters,
        int index,
        string category)
    {
        if (JsonParameterReader.TryGet(parameters, index, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            string name = value.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                result.Add(new EventClipboardAsset(
                    category,
                    name,
                    EventReferenceConfidence.Exact,
                    command.RawIndex));
            }
        }
    }

    private static void AddAudioAsset(
        List<EventClipboardAsset> result,
        RawEventCommand command,
        JsonElement parameters,
        int index,
        string category)
    {
        if (!JsonParameterReader.TryGet(parameters, index, out JsonElement audio)
            || audio.ValueKind != JsonValueKind.Object
            || !audio.TryGetProperty("name", out JsonElement name)
            || name.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string assetName = name.GetString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(assetName))
        {
            result.Add(new EventClipboardAsset(
                category,
                assetName,
                EventReferenceConfidence.Exact,
                command.RawIndex));
        }
    }

    private static string AudioCategoryFor(int commandCode) => commandCode switch
    {
        241 => "Bgm",
        245 => "Bgs",
        249 => "Me",
        250 => "Se",
        _ => "Audio"
    };

    private static void AddHeuristic(
        List<EventClipboardAsset> result,
        RawEventCommand command,
        string category) =>
        result.Add(new EventClipboardAsset(
            category,
            string.Empty,
            EventReferenceConfidence.Heuristic,
            command.RawIndex));
}

public sealed record EventDependencyRemap(
    string Kind,
    int SourceId,
    int TargetId);

public sealed record EventPastePlan(
    IReadOnlyList<string> Commands,
    IReadOnlyList<EventClipboardReference> ExactReferences,
    IReadOnlyList<EventClipboardReference> HeuristicReferences,
    IReadOnlyList<EventClipboardAsset> Assets,
    IReadOnlyList<EventDependencyRemap> Remaps)
{
    public bool RequiresReview => HeuristicReferences.Count > 0 || Assets.Any(asset => asset.Confidence == EventReferenceConfidence.Heuristic);
}

public static class EventPastePlanner
{
    public static EventPastePlan Create(EventClipboardPackage package, IEnumerable<EventDependencyRemap>? remaps = null)
    {
        EventDependencyRemap[] map = remaps?.ToArray() ?? [];
        string[] commands = package.Commands.ToArray();
        foreach (EventDependencyRemap remap in map)
        {
            EventClipboardReference[] references = package.References
                .Where(item => item.Confidence == EventReferenceConfidence.Exact && item.Kind == remap.Kind && item.Id == remap.SourceId)
                .ToArray();
            foreach (EventClipboardReference reference in references)
            {
                RawEventCommand parsed = RawEventCommand.Parse(reference.RawCommandIndex, commands[reference.RawCommandIndex]);
                JsonArray parameters = JsonNode.Parse(parsed.ParametersRawJson) as JsonArray ?? [];
                if (reference.ParameterIndex >= 0 && reference.ParameterIndex < parameters.Count) parameters[reference.ParameterIndex] = remap.TargetId;
                commands[reference.RawCommandIndex] = EventCommandFactory.Rewrite(parsed, parametersRawJson: parameters.ToJsonString());
            }
        }
        return new EventPastePlan(
            commands,
            package.References.Where(item => item.Confidence == EventReferenceConfidence.Exact).ToArray(),
            package.References.Where(item => item.Confidence == EventReferenceConfidence.Heuristic).ToArray(),
            package.Assets,
            map);
    }
}
