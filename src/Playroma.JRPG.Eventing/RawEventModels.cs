using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Eventing;

public sealed record RawEventCommand(
    int RawIndex,
    int Code,
    int Indent,
    string ParametersRawJson,
    string RawJson,
    string Fingerprint,
    int ExtraPropertyCount = 0)
{
    public static RawEventCommand Parse(int rawIndex, string rawJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rawIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new RawEventCommand(
                    rawIndex,
                    0,
                    0,
                    "[]",
                    rawJson,
                    Hash(rawJson),
                    0);
            }

            int code = root.TryGetProperty("code", out JsonElement codeElement) && codeElement.TryGetInt32(out int parsedCode)
                ? parsedCode
                : 0;
            int indent = root.TryGetProperty("indent", out JsonElement indentElement) && indentElement.TryGetInt32(out int parsedIndent)
                ? parsedIndent
                : 0;
            string parameters = root.TryGetProperty("parameters", out JsonElement parameterElement)
                ? parameterElement.GetRawText()
                : "[]";
            int propertyCount = 0;
            foreach (JsonProperty _ in root.EnumerateObject()) propertyCount++;
            return new RawEventCommand(
                rawIndex,
                code,
                indent,
                parameters,
                rawJson,
                Hash(rawJson),
                Math.Max(0, propertyCount - 3));
        }
        catch (JsonException)
        {
            return new RawEventCommand(rawIndex, 0, 0, "[]", rawJson, Hash(rawJson), 0);
        }
    }

    public JsonElement ParseParameters()
    {
        using JsonDocument document = JsonDocument.Parse(ParametersRawJson);
        return document.RootElement.Clone();
    }

    public RawEventCommand WithRawIndex(int index) => this with { RawIndex = index };

    public static string CreateJson(int code, int indent, string parametersRawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersRawJson);
        using JsonDocument parameters = JsonDocument.Parse(parametersRawJson);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteNumber("indent", indent);
            writer.WritePropertyName("parameters");
            parameters.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public sealed record EventListSnapshot(
    EventListAddress Address,
    IReadOnlyList<RawEventCommand> Commands,
    string RawListJson,
    string Fingerprint,
    DateTimeOffset CapturedUtc)
{
    public int Count => Commands.Count;

    public static EventListSnapshot Capture(
        MutableJsonProjectDocument document,
        EventListAddress address)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(address);
        JsonSyntaxNode? node = document.Syntax.Resolve(address.ListPath);
        if (node is not JsonArraySyntax array)
        {
            throw new InvalidDataException($"Event command path '{address.ListPath}' is not a JSON array.");
        }

        List<RawEventCommand> commands = new(array.Elements.Count);
        for (int index = 0; index < array.Elements.Count; index++)
        {
            string raw = document.GetRawJson(address.ListPath.Append(index)) ?? "null";
            commands.Add(RawEventCommand.Parse(index, raw));
        }

        string rawList = document.GetRawJson(address.ListPath) ?? "[]";
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawList))).ToLowerInvariant();
        return new EventListSnapshot(address, commands, rawList, fingerprint, DateTimeOffset.UtcNow);
    }

    public EventListSnapshot Reindex()
    {
        RawEventCommand[] commands = Commands
            .Select((command, index) => command.WithRawIndex(index))
            .ToArray();
        return this with { Commands = commands };
    }
}
