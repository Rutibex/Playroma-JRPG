using System.Text.Json;

namespace Playroma.JRPG.Specifications;

public sealed record EventCommandParameterSpecification(
    int Index,
    string Name,
    string DisplayName,
    string Type,
    string? Target = null,
    string? AssetCategory = null,
    IReadOnlyDictionary<string, string>? Options = null,
    bool Optional = false,
    decimal? Minimum = null,
    decimal? Maximum = null,
    string? Unit = null,
    string? Description = null);

public sealed record EventCommandSpecification(
    int Code,
    string Name,
    string Category,
    bool IsContinuation,
    JsonElement SampleParameters,
    string Source,
    string Structure = "single",
    string Editor = "schema",
    IReadOnlyList<EventCommandParameterSpecification>? Parameters = null);
