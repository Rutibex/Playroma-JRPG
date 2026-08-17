using System.Text.Json;

namespace Playroma.JRPG.Specifications;

public sealed record MovementRouteCommandSpecification(
    int Code,
    string Name,
    JsonElement SampleParameters,
    string Source,
    string Editor = "schema",
    IReadOnlyList<EventCommandParameterSpecification>? Parameters = null);
