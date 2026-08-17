using System.Text.Json;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public sealed record ClipboardDatabaseRecord(int SourceId, string RawJson);
public sealed record ClipboardReference(string SourcePath, ReferenceTarget Target, ReferenceConfidence Confidence, string? Context);

public sealed record DatabaseClipboardPayload
{
    public const int CurrentFormatVersion = 1;
    public const string MimeType = "application/x-playroma-jrpg-database-record+json";

    public required int FormatVersion { get; init; } = CurrentFormatVersion;
    public required Guid SourceProjectId { get; init; }
    public required MvDocumentKind SourceKind { get; init; }
    public required IReadOnlyList<ClipboardDatabaseRecord> Records { get; init; }
    public required IReadOnlyList<ClipboardReference> References { get; init; }
    public required IReadOnlyList<string> Assets { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static DatabaseClipboardPayload Deserialize(string json)
    {
        DatabaseClipboardPayload payload = JsonSerializer.Deserialize<DatabaseClipboardPayload>(json, Options)
            ?? throw new InvalidDataException("The database clipboard payload is empty.");
        if (payload.FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException($"Database clipboard format {payload.FormatVersion} is not supported.");
        }
        return payload;
    }

    private static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };
}

public sealed record DatabasePasteAssignment(int SourceId, int DestinationId, string RawJson);

public sealed record DatabasePastePlan(
    MvDocumentKind SourceKind,
    MvDocumentKind DestinationKind,
    IReadOnlyList<DatabasePasteAssignment> Assignments,
    IReadOnlyList<ClipboardReference> ExactReferences,
    IReadOnlyList<ClipboardReference> HeuristicReferences,
    IReadOnlyList<string> MissingAssets,
    bool IsKindCompatible)
{
    public bool RequiresReview => !IsKindCompatible || HeuristicReferences.Count > 0 || MissingAssets.Count > 0;
}

public static class DatabasePastePlanner
{
    public static DatabasePastePlan Create(
        DatabaseClipboardPayload payload,
        MvDocumentKind destinationKind,
        IReadOnlyCollection<int> occupiedIds,
        int firstCandidateId = 1,
        IReadOnlyCollection<string>? availableAssets = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        HashSet<int> occupied = occupiedIds.ToHashSet();
        HashSet<string> available = availableAssets?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        List<DatabasePasteAssignment> assignments = [];
        int candidate = Math.Max(1, firstCandidateId);
        foreach (ClipboardDatabaseRecord source in payload.Records.OrderBy(record => record.SourceId))
        {
            while (occupied.Contains(candidate)) candidate++;
            assignments.Add(new DatabasePasteAssignment(source.SourceId, candidate, DatabaseRecordTemplateFactory.DuplicateWithId(source.RawJson, candidate)));
            occupied.Add(candidate++);
        }
        return new DatabasePastePlan(
            payload.SourceKind,
            destinationKind,
            assignments,
            payload.References.Where(reference => reference.Confidence == ReferenceConfidence.Exact).ToArray(),
            payload.References.Where(reference => reference.Confidence == ReferenceConfidence.Heuristic).ToArray(),
            payload.Assets.Where(asset => !available.Contains(asset)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            payload.SourceKind == destinationKind);
    }
}

public enum NumericBatchOperation
{
    Set,
    Add,
    Subtract,
    Multiply,
    Divide,
    ClampMinimum,
    ClampMaximum,
    Round
}

public static class NumericBatchCalculator
{
    public static double Apply(double current, NumericBatchOperation operation, double operand) => operation switch
    {
        NumericBatchOperation.Set => operand,
        NumericBatchOperation.Add => current + operand,
        NumericBatchOperation.Subtract => current - operand,
        NumericBatchOperation.Multiply => current * operand,
        NumericBatchOperation.Divide when operand != 0 => current / operand,
        NumericBatchOperation.Divide => throw new DivideByZeroException(),
        NumericBatchOperation.ClampMinimum => Math.Max(current, operand),
        NumericBatchOperation.ClampMaximum => Math.Min(current, operand),
        NumericBatchOperation.Round => Math.Round(current, checked((int)operand), MidpointRounding.AwayFromZero),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };
}
