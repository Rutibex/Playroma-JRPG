using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Indexing;

namespace Playroma.JRPG.DatabaseEditing;

public sealed class DatabaseRecordIndex
{
    private readonly MutableJsonProjectDocument _document;
    private readonly Dictionary<int, DatabaseRecordIndexEntry> _entries = [];
    private readonly Dictionary<int, DateTimeOffset> _lastEdited = [];
    private readonly Dictionary<int, DiagnosticSeverity> _severity = [];
    private readonly HashSet<int> _missingReferences = [];

    public DatabaseRecordIndex(MutableJsonProjectDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Rebuild();
    }

    public string RelativePath => _document.RelativePath;
    public int PhysicalLength => (_document.Syntax.Root as JsonArraySyntax)?.Elements.Count
        ?? throw new InvalidOperationException("The database document root is not an array.");
    public int HighestId => Math.Max(0, PhysicalLength - 1);
    public IReadOnlyList<DatabaseRecordIndexEntry> Records => _entries.Values.OrderBy(entry => entry.Id).ToArray();

    public event EventHandler? Changed;

    public void Rebuild()
    {
        _entries.Clear();
        JsonArraySyntax array = _document.Syntax.Root as JsonArraySyntax
            ?? throw new InvalidOperationException("The database document root is not an array.");
        for (int id = 1; id < array.Elements.Count; id++) _entries[id] = BuildEntry(id, array.Elements[id]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateRecord(int id, bool markEdited = true)
    {
        UpdateRecords([id], markEdited);
    }

    public void UpdateRecords(IEnumerable<int> recordIds, bool markEdited = true)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        JsonArraySyntax array = _document.Syntax.Root as JsonArraySyntax
            ?? throw new InvalidOperationException("The database document root is not an array.");
        bool changed = false;
        foreach (int id in recordIds.Distinct())
        {
            if (id <= 0 || id >= array.Elements.Count)
            {
                changed |= _entries.Remove(id);
                continue;
            }
            if (markEdited) _lastEdited[id] = DateTimeOffset.UtcNow;
            _entries[id] = BuildEntry(id, array.Elements[id]);
            changed = true;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetDiagnostics(IEnumerable<ProjectDiagnostic> diagnostics)
    {
        _severity.Clear();
        foreach (ProjectDiagnostic diagnostic in diagnostics)
        {
            if (!string.Equals(diagnostic.Location.RelativePath, RelativePath, PathComparison) || !diagnostic.Location.RecordId.HasValue) continue;
            int id = diagnostic.Location.RecordId.Value;
            if (!_severity.TryGetValue(id, out DiagnosticSeverity current) || diagnostic.Severity > current) _severity[id] = diagnostic.Severity;
        }
        RebuildWithoutNotification();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetMissingReferences(IEnumerable<ProjectReference> references)
    {
        _missingReferences.Clear();
        foreach (ProjectReference reference in references)
        {
            if (string.Equals(reference.Source.RelativePath, RelativePath, PathComparison) && reference.Source.RecordId.HasValue)
            {
                _missingReferences.Add(reference.Source.RecordId.Value);
            }
        }
        RebuildWithoutNotification();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<DatabaseRecordIndexEntry> Query(DatabaseRecordFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        IEnumerable<DatabaseRecordIndexEntry> query = _entries.Values;
        if (filter.Id.HasValue) query = query.Where(entry => entry.Id == filter.Id.Value);
        if (filter.Populated.HasValue) query = query.Where(entry => entry.IsPopulated == filter.Populated.Value);
        if (filter.Modified.HasValue) query = query.Where(entry => entry.IsModified == filter.Modified.Value);
        if (filter.MissingReferences.HasValue) query = query.Where(entry => entry.HasMissingReferences == filter.MissingReferences.Value);
        if (filter.MinimumSeverity.HasValue) query = query.Where(entry => entry.HighestSeverity.HasValue && entry.HighestSeverity.Value >= filter.MinimumSeverity.Value);
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            string text = filter.Text.Trim();
            query = query.Where(entry => entry.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        query = filter.Sort switch
        {
            DatabaseRecordSort.Name => query.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Id),
            DatabaseRecordSort.LastEdited => query.OrderBy(entry => entry.LastEdited ?? DateTimeOffset.MinValue).ThenBy(entry => entry.Id),
            DatabaseRecordSort.Severity => query.OrderBy(entry => entry.HighestSeverity ?? DiagnosticSeverity.Information).ThenBy(entry => entry.Id),
            _ => query.OrderBy(entry => entry.Id)
        };
        if (filter.Descending) query = query.Reverse();
        return query.ToArray();
    }

    public DatabaseRecordIndexEntry? Find(int id) => _entries.TryGetValue(id, out DatabaseRecordIndexEntry? entry) ? entry : null;

    private void RebuildWithoutNotification()
    {
        JsonArraySyntax array = _document.Syntax.Root as JsonArraySyntax
            ?? throw new InvalidOperationException("The database document root is not an array.");
        foreach (int id in _entries.Keys.ToArray())
        {
            if (id < array.Elements.Count) _entries[id] = BuildEntry(id, array.Elements[id]);
        }
    }

    private DatabaseRecordIndexEntry BuildEntry(int id, JsonSyntaxNode node)
    {
        bool populated = node.Kind == JsonSyntaxKind.Object;
        JsonPath root = JsonPath.Root.Append(id);
        string name = populated ? _document.GetString(root.Append("name")) ?? $"Record {id:D4}" : "(empty)";
        string note = populated ? _document.GetString(root.Append("note")) ?? string.Empty : string.Empty;
        string nickname = populated ? _document.GetString(root.Append("nickname")) ?? string.Empty : string.Empty;
        string profile = populated ? _document.GetString(root.Append("profile")) ?? string.Empty : string.Empty;
        string description = populated ? _document.GetString(root.Append("description")) ?? string.Empty : string.Empty;
        string formula = populated ? _document.GetString(root.Append("damage").Append("formula")) ?? string.Empty : string.Empty;
        string message1 = populated ? _document.GetString(root.Append("message1")) ?? string.Empty : string.Empty;
        string message2 = populated ? _document.GetString(root.Append("message2")) ?? string.Empty : string.Empty;
        string message3 = populated ? _document.GetString(root.Append("message3")) ?? string.Empty : string.Empty;
        string message4 = populated ? _document.GetString(root.Append("message4")) ?? string.Empty : string.Empty;
        string effects = populated ? _document.GetRawJson(root.Append("effects")) ?? string.Empty : string.Empty;
        string traits = populated ? _document.GetRawJson(root.Append("traits")) ?? string.Empty : string.Empty;
        string battlerName = populated ? _document.GetString(root.Append("battlerName")) ?? string.Empty : string.Empty;
        string actions = populated ? _document.GetRawJson(root.Append("actions")) ?? string.Empty : string.Empty;
        string drops = populated ? _document.GetRawJson(root.Append("dropItems")) ?? string.Empty : string.Empty;
        string members = populated ? _document.GetRawJson(root.Append("members")) ?? string.Empty : string.Empty;
        string pages = populated ? _document.GetRawJson(root.Append("pages")) ?? string.Empty : string.Empty;
        int icon = populated ? _document.GetInt32(root.Append("iconIndex")) ?? 0 : 0;
        _lastEdited.TryGetValue(id, out DateTimeOffset edited);
        _severity.TryGetValue(id, out DiagnosticSeverity severity);
        string search = string.Join(
            '\n',
            id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            name,
            nickname,
            profile,
            description,
            formula,
            message1,
            message2,
            message3,
            message4,
            effects,
            traits,
            battlerName,
            actions,
            drops,
            members,
            pages,
            note);
        return new DatabaseRecordIndexEntry(
            id,
            populated,
            name,
            icon,
            search,
            _severity.ContainsKey(id) ? severity : null,
            _missingReferences.Contains(id),
            _lastEdited.ContainsKey(id),
            _lastEdited.ContainsKey(id) ? edited : null,
            note.Length <= 120 ? note : note[..120] + "…");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

public static class DatabaseValueAccessor
{
    public static AggregateValue<object?> ReadAggregate(
        MutableJsonProjectDocument document,
        IReadOnlyList<DatabaseRecordHandle> records,
        DatabaseFieldDescriptor field)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(field);
        if (records.Count == 0) return AggregateValue<object?>.Empty;
        object? first = Read(document, records[0], field);
        for (int index = 1; index < records.Count; index++)
        {
            object? candidate = Read(document, records[index], field);
            if (!Equals(first, candidate)) return AggregateValue<object?>.Mixed;
        }
        return AggregateValue<object?>.Uniform(first);
    }

    public static object? Read(
        MutableJsonProjectDocument document,
        DatabaseRecordHandle record,
        DatabaseFieldDescriptor field)
    {
        JsonPath path = Combine(record.Path, field.RelativePath);
        return field.ValueKind switch
        {
            DatabaseFieldValueKind.String or DatabaseFieldValueKind.MultilineString or DatabaseFieldValueKind.Formula or DatabaseFieldValueKind.Note or DatabaseFieldValueKind.AssetReference => document.GetString(path) ?? string.Empty,
            DatabaseFieldValueKind.Integer or DatabaseFieldValueKind.Enum or DatabaseFieldValueKind.DatabaseReference or DatabaseFieldValueKind.Icon or DatabaseFieldValueKind.Animation => document.GetInt32(path) ?? 0,
            DatabaseFieldValueKind.Decimal => document.GetDouble(path) ?? 0d,
            DatabaseFieldValueKind.Boolean => document.GetBoolean(path) ?? false,
            _ => document.GetRawJson(path)
        };
    }

    public static JsonPath Combine(JsonPath recordPath, JsonPath relativePath)
    {
        JsonPath result = recordPath;
        foreach (string segment in relativePath.Segments())
        {
            result = int.TryParse(segment, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)
                ? result.Append(index)
                : result.Append(segment);
        }
        return result;
    }
}
