using Playroma.Text.Core.Generation;

namespace Playroma.Text.RpgMakerMV;

public enum TextImportMode { Replace, Append, Prepend, FillIfBlank, InsertAtCursor, ReplaceSelection, CreateRecord }

public sealed record TextTargetDescriptor(
    string AdapterKind,
    string DataFile,
    string RecordId,
    string JsonPath,
    string SemanticField,
    string DisplayName,
    string CurrentValue,
    bool SupportsMultiline,
    bool MayContainPluginTags,
    int? SuggestedMaximumLength = null,
    IReadOnlySet<TextImportMode>? SupportedModes = null,
    string? FieldKey = null,
    string? RecordDisplayName = null,
    IReadOnlyList<string>? RecommendedRecipeIds = null)
{
    public IReadOnlySet<TextImportMode> EffectiveSupportedModes { get; } = SupportedModes
        ?? new HashSet<TextImportMode>([TextImportMode.Replace, TextImportMode.Append, TextImportMode.Prepend, TextImportMode.FillIfBlank]);

    public IReadOnlyList<string> EffectiveRecommendedRecipeIds { get; } = RecommendedRecipeIds ?? [];
}

public sealed record TextRecordTargetDescriptor(
    string AdapterKind,
    string DataFile,
    string RecordId,
    string DisplayName,
    IReadOnlyList<TextTargetDescriptor> Fields,
    string? SelectedSemanticField = null,
    IReadOnlyDictionary<string, object?>? ContextValues = null,
    bool CanCreateRecord = false,
    int? SuggestedCreateRecordId = null)
{
    public IReadOnlyDictionary<string, object?> EffectiveContextValues { get; } = ContextValues
        ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public TextTargetDescriptor? SelectedField =>
        Fields.FirstOrDefault(candidateField => string.Equals(candidateField.SemanticField, SelectedSemanticField, StringComparison.OrdinalIgnoreCase))
        ?? Fields.FirstOrDefault();
}

public sealed record TextImportFieldSelection(
    string SemanticField,
    bool Include = true,
    TextImportMode Mode = TextImportMode.Replace,
    string? GeneratedField = null);

public interface ITextContextProvider
{
    ValueTask<GenerationContext> BuildContextAsync(TextTargetDescriptor target, CancellationToken cancellationToken = default);
}

public interface ITextTargetAdapter
{
    string Kind { get; }
    bool CanHandle(TextTargetDescriptor target);
    IReadOnlyList<TextImportChange> PlanImport(TextTargetDescriptor target, GeneratedTextBundle bundle, TextImportMode mode);
    IReadOnlyList<TextImportValidationIssue> Validate(TextTargetDescriptor target, GeneratedTextBundle bundle, TextImportMode mode);
}

public sealed record TextImportChange(
    string DataFile,
    string RecordId,
    string JsonPath,
    string OldValue,
    string NewValue,
    string SemanticField,
    string? DisplayName = null);

public sealed record TextImportRecordCreation(
    string DataFile,
    int RecordId,
    string RawJson,
    string DisplayName);

public sealed record TextImportValidationIssue(string Code, string Message, bool IsError, string? SemanticField = null);

public sealed record TextImportPlan(
    string Description,
    IReadOnlyList<TextImportChange> Changes,
    IReadOnlyList<TextImportValidationIssue> Issues,
    TextImportRecordCreation? Creation = null)
{
    public bool CanApply => Changes.Count > 0 && Issues.All(i => !i.IsError);
}

/// <summary>
/// Host bridge implemented by the Playroma editor. The platform-neutral Text Studio never
/// mutates RPG Maker data directly; it sends a declarative plan to the host transaction layer.
/// </summary>
public interface ITextImportTransactionSink
{
    ValueTask ApplyAsync(TextImportPlan plan, CancellationToken cancellationToken = default);
}

/// <summary>Compact host snapshot used by the platform-neutral Batch Content Builder.</summary>
public sealed record TextBatchTargetSnapshot(
    string AdapterKind,
    string DataFile,
    string DisplayName,
    IReadOnlyList<TextRecordTargetDescriptor> Records,
    TextRecordTargetDescriptor Template,
    IReadOnlyList<int> SuggestedCreateRecordIds,
    IReadOnlyList<string>? ExistingPrimaryValues = null)
{
    public IReadOnlyList<string> EffectiveExistingPrimaryValues => ExistingPrimaryValues ?? [];
}

public sealed record TextBatchImportRequest(
    TextRecordTargetDescriptor Target,
    GeneratedTextBundle Bundle,
    IReadOnlyList<TextImportFieldSelection> Selections,
    bool CreateRecord = false,
    int? CreateRecordId = null,
    string? RowLabel = null);

public sealed record TextBatchImportRowPlan(
    string RowLabel,
    TextImportPlan Plan);

public sealed record TextBatchImportPlan(
    string Description,
    IReadOnlyList<TextBatchImportRowPlan> Rows,
    IReadOnlyList<TextImportValidationIssue> Issues)
{
    public IReadOnlyList<TextImportChange> Changes => Rows.SelectMany(r => r.Plan.Changes).ToArray();
    public IReadOnlyList<TextImportRecordCreation> Creations => Rows.Select(r => r.Plan.Creation).Where(x => x is not null).Cast<TextImportRecordCreation>().ToArray();
    public bool CanApply => Rows.Count > 0 && Changes.Count > 0 && Issues.All(i => !i.IsError) && Rows.All(r => r.Plan.Issues.All(i => !i.IsError));
}

/// <summary>Host capability required by the Phase 13 Batch Content Builder.</summary>
public interface ITextBatchHost
{
    ValueTask<TextBatchTargetSnapshot> BuildSnapshotAsync(string adapterKind, int requestedCreateSlots = 0, CancellationToken cancellationToken = default);
    ValueTask<TextBatchImportPlan> BuildBatchPlanAsync(IReadOnlyList<TextBatchImportRequest> requests, CancellationToken cancellationToken = default);
    ValueTask ApplyBatchAsync(TextBatchImportPlan plan, CancellationToken cancellationToken = default);
}
