namespace Playroma.Text.RpgMakerMV;

/// <summary>Combines already validated row plans and detects cross-row collisions.</summary>
public static class TextBatchImportPlanner
{
    public static TextBatchImportPlan Combine(string description, IReadOnlyList<TextBatchImportRowPlan> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var issues = rows.SelectMany(r => r.Plan.Issues.Select(i => i with
        {
            Message = string.IsNullOrWhiteSpace(r.RowLabel) ? i.Message : $"{r.RowLabel}: {i.Message}"
        })).ToList();

        var creationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TextBatchImportRowPlan row in rows)
        {
            if (row.Plan.Creation is not { } creation) continue;
            string key = $"{creation.DataFile}|{creation.RecordId}";
            if (!creationKeys.Add(key))
                issues.Add(new("PTXTBATCH0001", $"{row.RowLabel}: multiple batch rows attempt to create record #{creation.RecordId} in {creation.DataFile}.", true));
        }

        var changeKeys = new Dictionary<string, TextImportChange>(StringComparer.OrdinalIgnoreCase);
        foreach (TextBatchImportRowPlan row in rows)
        {
            foreach (TextImportChange change in row.Plan.Changes)
            {
                string key = $"{change.DataFile}|{change.RecordId}|{change.JsonPath}";
                if (changeKeys.TryGetValue(key, out TextImportChange? previous) && !string.Equals(previous.NewValue, change.NewValue, StringComparison.Ordinal))
                    issues.Add(new("PTXTBATCH0002", $"{row.RowLabel}: conflicting writes target {change.DataFile} record {change.RecordId} {change.JsonPath}.", true, change.SemanticField));
                else
                    changeKeys[key] = change;
            }
        }

        return new TextBatchImportPlan(description, rows, issues);
    }
}
