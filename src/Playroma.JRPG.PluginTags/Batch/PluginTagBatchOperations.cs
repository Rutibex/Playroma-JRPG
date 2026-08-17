namespace Playroma.JRPG.PluginTags;

public enum PluginFeatureWriteMode
{
    Smart,
    AddOccurrence,
    ReplaceFirst,
    ReplaceAll,
    Merge
}

public enum PluginConflictResolution
{
    Fail,
    KeepExisting,
    RemoveConflicting
}

public sealed record PluginTagBatchRecord(string RecordKey, PluginTextEditingSession Session);

public sealed record PluginTagWriteRequest(
    PluginFeatureKey Feature,
    PluginFeatureValues Values,
    PluginFeatureWriteMode WriteMode = PluginFeatureWriteMode.Smart,
    PluginConflictResolution ConflictResolution = PluginConflictResolution.Fail);

public sealed record PluginTagBatchRecordChange(
    string RecordKey,
    string Before,
    string After,
    bool Changed,
    IReadOnlyList<string> Warnings,
    string? Error = null);

public sealed record PluginTagBatchPlan(
    PluginTagWriteRequest Request,
    IReadOnlyList<PluginTagBatchRecordChange> Records)
{
    public int ChangedCount => Records.Count(record => record.Changed);
    public int UnchangedCount => Records.Count(record => !record.Changed && record.Error is null);
    public int ErrorCount => Records.Count(record => record.Error is not null);
    public bool CanApply => ErrorCount == 0;
}

/// <summary>
/// Plans and applies one feature across records without partial mutation. All records are
/// previewed first; Apply rejects stale sessions whose text changed since planning.
/// </summary>
public sealed class PluginTagBatchPlanner
{
    private readonly PluginTagCatalog catalog;

    public PluginTagBatchPlanner(PluginTagCatalog catalog)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public PluginTagBatchPlan Plan(
        IEnumerable<PluginTagBatchRecord> records,
        PluginTagWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(request);
        PluginFeatureDefinition definition = catalog.GetRequiredFeature(request.Feature);
        List<PluginTagBatchRecordChange> changes = [];
        foreach (PluginTagBatchRecord record in records)
        {
            List<string> warnings = [];
            try
            {
                PluginTextEditingSession clone = new(catalog, record.Session.Context, record.Session.WorkingText);
                bool mayWrite = ResolveConflicts(clone, definition, request.ConflictResolution, warnings);
                if (mayWrite) ApplyWrite(clone, definition, request, warnings);
                changes.Add(new(record.RecordKey, record.Session.WorkingText, clone.WorkingText,
                    !string.Equals(record.Session.WorkingText, clone.WorkingText, StringComparison.Ordinal), warnings));
            }
            catch (Exception exception) when (exception is PluginTextMutationException or PluginTagRenderException or InvalidOperationException)
            {
                changes.Add(new(record.RecordKey, record.Session.WorkingText, record.Session.WorkingText,
                    false, warnings, exception.Message));
            }
        }
        return new(request, changes);
    }

    public void Apply(
        PluginTagBatchPlan plan,
        IReadOnlyDictionary<string, PluginTextEditingSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sessions);
        if (!plan.CanApply) throw new PluginTextMutationException("The batch plan contains errors and cannot be applied.");
        foreach (PluginTagBatchRecordChange change in plan.Records)
        {
            if (!sessions.TryGetValue(change.RecordKey, out PluginTextEditingSession? session))
                throw new PluginTextMutationException("Batch session is missing: " + change.RecordKey);
            if (!string.Equals(session.WorkingText, change.Before, StringComparison.Ordinal))
                throw new PluginTextMutationException("Batch session changed after preview: " + change.RecordKey);
        }
        foreach (PluginTagBatchRecordChange change in plan.Records.Where(change => change.Changed))
            sessions[change.RecordKey].ReplaceText(change.After);
    }

    private bool ResolveConflicts(
        PluginTextEditingSession session,
        PluginFeatureDefinition definition,
        PluginConflictResolution resolution,
        ICollection<string> warnings)
    {
        if (definition.EffectiveConflictGroups.Count == 0) return true;
        PluginFeatureDefinition[] conflicts = session.ParsedDocument.RecognizedNodes
            .Select(node => node switch
            {
                RecognizedTagNode tag => tag.Definition,
                RecognizedBlockNode block => block.Definition,
                _ => null
            })
            .OfType<PluginFeatureDefinition>()
            .Where(other => other.Key != definition.Key &&
                other.EffectiveConflictGroups.Intersect(definition.EffectiveConflictGroups, StringComparer.OrdinalIgnoreCase).Any())
            .DistinctBy(other => other.Key)
            .ToArray();
        if (conflicts.Length == 0) return true;
        if (resolution == PluginConflictResolution.Fail)
            throw new PluginTextMutationException($"{definition.DisplayName} conflicts with {string.Join(", ", conflicts.Select(conflict => conflict.DisplayName))}.");
        if (resolution == PluginConflictResolution.KeepExisting)
        {
            warnings.Add("Existing conflicting features were kept; the requested feature was skipped.");
            return false;
        }
        foreach (PluginFeatureDefinition conflict in conflicts)
        {
            session.RemoveFeature(conflict.Key, removeAll: true);
            warnings.Add("Removed conflicting feature: " + conflict.DisplayName);
        }
        return true;
    }

    private static void ApplyWrite(
        PluginTextEditingSession session,
        PluginFeatureDefinition definition,
        PluginTagWriteRequest request,
        ICollection<string> warnings)
    {
        IReadOnlyList<PluginTextNode> occurrences = session.ParsedDocument.Find(definition.Key);
        PluginFeatureWriteMode mode = request.WriteMode == PluginFeatureWriteMode.Smart
            ? definition.Multiplicity switch
            {
                PluginFeatureMultiplicity.Singleton => PluginFeatureWriteMode.ReplaceFirst,
                PluginFeatureMultiplicity.Repeatable => PluginFeatureWriteMode.AddOccurrence,
                PluginFeatureMultiplicity.RepeatableByKey => PluginFeatureWriteMode.Smart,
                PluginFeatureMultiplicity.Mergeable => PluginFeatureWriteMode.Merge,
                _ => PluginFeatureWriteMode.ReplaceFirst
            }
            : request.WriteMode;

        if (mode == PluginFeatureWriteMode.Smart && definition.Multiplicity == PluginFeatureMultiplicity.RepeatableByKey)
        {
            int matchingIndex = FindKeyedOccurrence(definition, occurrences, request.Values);
            if (matchingIndex >= 0) session.UpsertFeature(definition.Key, request.Values, matchingIndex);
            else session.UpsertFeature(definition.Key, request.Values, occurrences.Count);
            return;
        }

        switch (mode)
        {
            case PluginFeatureWriteMode.AddOccurrence:
                session.UpsertFeature(definition.Key, request.Values, occurrences.Count);
                break;
            case PluginFeatureWriteMode.ReplaceAll:
                session.RemoveFeature(definition.Key, removeAll: true);
                session.UpsertFeature(definition.Key, request.Values);
                break;
            case PluginFeatureWriteMode.Merge:
                if (occurrences.Count == 0)
                {
                    session.UpsertFeature(definition.Key, request.Values);
                    break;
                }
                PluginFeatureValues existing = ValuesOf(occurrences[0]);
                session.UpsertFeature(definition.Key, MergeValues(definition, existing, request.Values), 0);
                warnings.Add("Merged list values with the first existing occurrence.");
                break;
            default:
                session.UpsertFeature(definition.Key, request.Values, 0);
                break;
        }
    }


    private static int FindKeyedOccurrence(
        PluginFeatureDefinition definition,
        IReadOnlyList<PluginTextNode> occurrences,
        PluginFeatureValues incoming)
    {
        PluginArgumentDefinition? keyArgument = definition.Arguments.FirstOrDefault(argument => argument.IsRequired)
            ?? definition.Arguments.FirstOrDefault();
        if (keyArgument is null || !incoming.TryGetValue(keyArgument.Name, out PluginValue incomingValue)) return -1;
        string canonicalIncoming = PluginTypedValueCodec.Canonicalize(keyArgument, incomingValue.Text);
        for (int index = 0; index < occurrences.Count; index++)
        {
            PluginFeatureValues current = ValuesOf(occurrences[index]);
            if (!current.TryGetValue(keyArgument.Name, out PluginValue currentValue)) continue;
            string canonicalCurrent = PluginTypedValueCodec.Canonicalize(keyArgument, currentValue.Text);
            if (string.Equals(canonicalCurrent, canonicalIncoming, StringComparison.OrdinalIgnoreCase)) return index;
        }
        return -1;
    }

    private static PluginFeatureValues ValuesOf(PluginTextNode node) => node switch
    {
        RecognizedTagNode tag => tag.Values,
        RecognizedBlockNode block => block.Values,
        _ => PluginFeatureValues.Empty
    };

    private static PluginFeatureValues MergeValues(
        PluginFeatureDefinition definition,
        PluginFeatureValues existing,
        PluginFeatureValues incoming)
    {
        List<KeyValuePair<string, PluginValue>> values = [];
        foreach (PluginArgumentDefinition argument in definition.Arguments)
        {
            existing.TryGetValue(argument.Name, out PluginValue oldValue);
            incoming.TryGetValue(argument.Name, out PluginValue newValue);
            string oldText = oldValue?.Text ?? string.Empty;
            string newText = newValue?.Text ?? string.Empty;
            PluginValue merged = argument.AllowMultiple || argument.Kind is PluginArgumentKind.DatabaseIdList
                ? PluginValue.FromText(PluginTypedValueCodec.MergeIdentifierLists(oldText, newText))
                : newText.Length > 0 ? newValue : oldValue ?? PluginValue.FromText(string.Empty);
            values.Add(new(argument.Name, merged));
        }
        string? body = incoming.Body ?? existing.Body;
        return new(values, body);
    }
}
