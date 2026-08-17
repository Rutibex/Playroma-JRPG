using System.Text.Json;
using Playroma.JRPG.Assets;

namespace Playroma.JRPG.Eventing;

public enum EventCommandStructureKind
{
    Single,
    Continuation,
    ContinuationGroup,
    Conditional,
    Loop,
    Choices,
    Battle
}

public enum EventParameterKind
{
    Json,
    Integer,
    Boolean,
    String,
    StringArray,
    Array,
    Object,
    Enum,
    DatabaseId,
    Asset,
    Audio,
    Color,
    Tone,
    MovementRoute
}

public sealed record EventCommandParameterDescriptor(
    int Index,
    string Name,
    string DisplayName,
    EventParameterKind Kind,
    string? Target = null,
    AssetCategory? AssetCategory = null,
    IReadOnlyDictionary<string, string>? Options = null,
    bool Optional = false,
    decimal? Minimum = null,
    decimal? Maximum = null,
    string? Unit = null,
    string? Description = null);

public sealed record EventCommandDefinition(
    int Code,
    string Name,
    string Category,
    EventCommandStructureKind Structure,
    string EditorKey,
    bool IsContinuation,
    IReadOnlyList<EventCommandParameterDescriptor> Parameters,
    JsonElement SampleParameters,
    string Source)
{
    public bool IsKnown => Code >= 0;
    public bool IsInsertable => Code != 0 && !IsContinuation && Structure != EventCommandStructureKind.Continuation;
}

public sealed record MovementRouteCommandDefinition(
    int Code,
    string Name,
    string EditorKey,
    IReadOnlyList<EventCommandParameterDescriptor> Parameters,
    JsonElement SampleParameters,
    string Source);

public sealed record EventCommandSummary(
    string PrimaryText,
    string? SecondaryText = null,
    string? Category = null,
    bool IsWarning = false)
{
    public override string ToString() => string.IsNullOrWhiteSpace(SecondaryText)
        ? PrimaryText
        : $"{PrimaryText}: {SecondaryText}";
}

public enum EventDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record EventStructureDiagnostic(
    string Code,
    EventDiagnosticSeverity Severity,
    string Message,
    int? RawIndex = null,
    EventCommandSpan? Span = null);
