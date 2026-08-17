using Playroma.Text.Core.Generation;

namespace Playroma.Text.Core.Batch;

public enum BatchContentMode
{
    FillBlankExisting,
    UpdateExisting,
    AppendNew
}

public sealed record BatchCellState(
    string SemanticField,
    string DisplayName,
    string Value,
    bool IsLocked = false,
    string? GeneratedField = null,
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> EffectiveWarnings => Warnings ?? [];
}

public sealed record BatchContentRow(
    Guid Id,
    int DisplayIndex,
    string TargetKey,
    string TargetDisplayName,
    int? RecordId,
    bool CreateRecord,
    ulong Seed,
    GeneratedTextBundle? SourceBundle,
    IReadOnlyList<BatchCellState> Cells,
    bool IsRowLocked = false,
    IReadOnlyList<string>? Warnings = null,
    string? Status = null)
{
    public IReadOnlyList<string> EffectiveWarnings => Warnings ?? [];
    public bool HasWarnings => EffectiveWarnings.Count > 0 || Cells.Any(c => c.EffectiveWarnings.Count > 0);
    public BatchCellState? FindCell(string semanticField) => Cells.FirstOrDefault(c => c.SemanticField.Equals(semanticField, StringComparison.OrdinalIgnoreCase));
}

public sealed record BatchGenerationOptions(
    string RecipeId,
    string AdapterKind,
    BatchContentMode Mode,
    int RequestedRows,
    ulong Seed,
    bool SeedLocked,
    bool AvoidProjectDuplicates = true,
    bool AvoidBatchDuplicates = true,
    string PrimarySemanticField = "name");

public sealed record BatchValidationSummary(
    int RowCount,
    int WarningRows,
    int ErrorRows,
    int DuplicateCount,
    IReadOnlyList<string> Messages)
{
    public bool CanImport => RowCount > 0 && ErrorRows == 0;
}
