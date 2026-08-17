using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public readonly record struct DatabaseRecordHandle(
    MvDocumentKind Kind,
    string RelativePath,
    int Id,
    JsonPath Path)
{
    public DatabaseRecordHandle(MvDocumentKind kind, string relativePath, int id)
        : this(kind, Normalize(relativePath), ValidateId(id), JsonPath.Root.Append(id))
    {
    }

    private static int ValidateId(int id) => id > 0
        ? id
        : throw new ArgumentOutOfRangeException(nameof(id));

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Replace('\\', '/');
    }
}

public enum AggregateValueKind
{
    EmptySelection,
    Uniform,
    Mixed
}

public readonly record struct AggregateValue<T>(AggregateValueKind Kind, T? Value)
{
    public static AggregateValue<T> Empty => new(AggregateValueKind.EmptySelection, default);
    public static AggregateValue<T> Mixed => new(AggregateValueKind.Mixed, default);
    public static AggregateValue<T> Uniform(T? value) => new(AggregateValueKind.Uniform, value);
}

public enum DatabaseFieldValueKind
{
    String,
    MultilineString,
    Integer,
    Decimal,
    Boolean,
    Enum,
    DatabaseReference,
    AssetReference,
    Icon,
    Audio,
    Animation,
    Colour,
    NumberRange,
    Array,
    Formula,
    Note,
    RawJson
}

public sealed record DatabaseListColumn(
    string Key,
    string DisplayName,
    int Width,
    bool IsPrimary = false);

public sealed record DatabaseListSchema(
    IReadOnlyList<DatabaseListColumn> Columns,
    bool ShowNullSlotsByDefault = false)
{
    public static DatabaseListSchema Standard { get; } = new(
    [
        new("id", "ID", 70),
        new("name", "Name", 240, true),
        new("status", "Status", 90)
    ]);
}

public abstract class DatabaseFieldDescriptor
{
    protected DatabaseFieldDescriptor(
        string key,
        string displayName,
        JsonPath relativePath,
        DatabaseFieldValueKind valueKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        Key = key;
        DisplayName = displayName;
        RelativePath = relativePath;
        ValueKind = valueKind;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public JsonPath RelativePath { get; }
    public DatabaseFieldValueKind ValueKind { get; }
    public bool SupportsBatchEditing { get; init; } = true;
    public bool IsRequired { get; init; }
    public string? HelpText { get; init; }
    public int? Minimum { get; init; }
    public int? Maximum { get; init; }
    public IReadOnlyDictionary<int, string>? Options { get; init; }
    public MvDocumentKind? ReferenceKind { get; init; }
    public ReferenceTargetKind? ReferenceTargetKind { get; init; }
    public AssetCategory? AssetCategory { get; init; }
    public string? EmptyDisplayText { get; init; }

    public abstract string EncodeObject(object? value);

    /// <summary>Validates values from every mutation source, not only WinForms controls.</summary>
    public abstract ProjectDiagnostic? ValidateObject(object? value);
}

public sealed class DatabaseFieldDescriptor<T> : DatabaseFieldDescriptor
{
    private readonly Func<T?, string> _encoder;

    public DatabaseFieldDescriptor(
        string key,
        string displayName,
        JsonPath relativePath,
        DatabaseFieldValueKind valueKind,
        Func<T?, string> encoder)
        : base(key, displayName, relativePath, valueKind)
    {
        _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
    }

    public Func<T?, ProjectDiagnostic?>? Validate { get; init; }

    public override string EncodeObject(object? value)
    {
        if (value is null) return _encoder(default);
        if (value is T typed) return _encoder(typed);
        throw new ArgumentException($"Field '{Key}' expected a value of type {typeof(T).Name}.", nameof(value));
    }

    public override ProjectDiagnostic? ValidateObject(object? value)
    {
        if (value is null)
        {
            return IsRequired
                ? ProjectDiagnostic.Error(DiagnosticCodes.Phase9AInvalidFieldValue, $"{DisplayName} is required.")
                : Validate is null ? null : Validate(default);
        }
        if (value is not T typed)
        {
            return ProjectDiagnostic.Error(
                DiagnosticCodes.Phase9AInvalidFieldValue,
                $"{DisplayName} expected {typeof(T).Name}, but received {value.GetType().Name}.");
        }
        if (typed is IConvertible convertible && (Minimum.HasValue || Maximum.HasValue))
        {
            try
            {
                decimal numeric = convertible.ToDecimal(System.Globalization.CultureInfo.InvariantCulture);
                if (Minimum.HasValue && numeric < Minimum.Value)
                {
                    return ProjectDiagnostic.Error(DiagnosticCodes.Phase9AInvalidFieldValue, $"{DisplayName} must be at least {Minimum.Value}.");
                }
                if (Maximum.HasValue && numeric > Maximum.Value)
                {
                    return ProjectDiagnostic.Error(DiagnosticCodes.Phase9AInvalidFieldValue, $"{DisplayName} must not exceed {Maximum.Value}.");
                }
            }
            catch (InvalidCastException)
            {
                // The custom validator, when present, remains authoritative for non-numeric values.
            }
            catch (FormatException)
            {
                // The custom validator, when present, remains authoritative for non-numeric values.
            }
        }
        return Validate?.Invoke(typed);
    }
}

public sealed record DatabaseEditorSection(
    string Key,
    string DisplayName,
    IReadOnlyList<DatabaseFieldDescriptor> Fields,
    int Columns = 2);

public sealed record DatabaseRecordSchema(
    IReadOnlyList<DatabaseEditorSection> Sections,
    IReadOnlyList<string> KnownTopLevelProperties)
{
    public MvRecordContract? Contract { get; init; }
    public IReadOnlyList<DatabaseFieldDescriptor> Fields => Sections.SelectMany(section => section.Fields).ToArray();

    public DatabaseFieldDescriptor FindField(string key) => Fields.FirstOrDefault(field => field.Key == key)
        ?? throw new KeyNotFoundException($"Database field '{key}' is not registered.");
}

public interface IDatabaseEditorDefinition
{
    MvDocumentKind Kind { get; }
    string DisplayName { get; }
    string SingularName { get; }
    DatabaseRecordSchema RecordSchema { get; }
    DatabaseListSchema ListSchema { get; }
    string CreateDefaultRecordJson(int id);
    string CreateDuplicateRecordJson(string sourceRawJson, int destinationId);
}

public sealed class DatabaseEditorDefinition : IDatabaseEditorDefinition
{
    private readonly Func<int, string> _defaultFactory;
    private readonly Func<string, int, string> _duplicateFactory;

    public DatabaseEditorDefinition(
        MvDocumentKind kind,
        string displayName,
        string singularName,
        DatabaseRecordSchema recordSchema,
        Func<int, string> defaultFactory,
        DatabaseListSchema? listSchema = null,
        Func<string, int, string>? duplicateFactory = null)
    {
        Kind = kind;
        DisplayName = displayName;
        SingularName = singularName;
        RecordSchema = recordSchema;
        ListSchema = listSchema ?? DatabaseListSchema.Standard;
        _defaultFactory = defaultFactory;
        _duplicateFactory = duplicateFactory ?? DatabaseRecordTemplateFactory.DuplicateWithId;
    }

    public MvDocumentKind Kind { get; }
    public string DisplayName { get; }
    public string SingularName { get; }
    public DatabaseRecordSchema RecordSchema { get; }
    public DatabaseListSchema ListSchema { get; }
    public string CreateDefaultRecordJson(int id) => _defaultFactory(id);
    public string CreateDuplicateRecordJson(string sourceRawJson, int destinationId) => _duplicateFactory(sourceRawJson, destinationId);
}

public sealed class DatabaseEditorRegistry
{
    private readonly Dictionary<MvDocumentKind, IDatabaseEditorDefinition> _definitions = [];

    public IReadOnlyCollection<IDatabaseEditorDefinition> Definitions => _definitions.Values;

    public void Register(IDatabaseEditorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_definitions.TryAdd(definition.Kind, definition))
        {
            throw new InvalidOperationException($"A database editor for '{definition.Kind}' is already registered.");
        }
    }

    public bool TryGet(MvDocumentKind kind, out IDatabaseEditorDefinition? definition) =>
        _definitions.TryGetValue(kind, out definition);

    public IDatabaseEditorDefinition Get(MvDocumentKind kind) => _definitions.TryGetValue(kind, out IDatabaseEditorDefinition? definition)
        ? definition
        : throw new KeyNotFoundException($"No database editor is registered for '{kind}'.");

    public static DatabaseEditorRegistry CreateBuiltIn() => BuiltInDatabaseDefinitions.CreateRegistry();
}

public sealed record DatabaseRecordIndexEntry(
    int Id,
    bool IsPopulated,
    string Name,
    int IconIndex,
    string SearchText,
    DiagnosticSeverity? HighestSeverity,
    bool HasMissingReferences,
    bool IsModified,
    DateTimeOffset? LastEdited,
    string? NotePreview = null)
{
    public string IdText => Id.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
}

public enum DatabaseRecordSort
{
    Id,
    Name,
    LastEdited,
    Severity
}

public sealed record DatabaseRecordFilter(
    string Text = "",
    int? Id = null,
    bool? Populated = null,
    bool? Modified = null,
    bool? MissingReferences = null,
    DiagnosticSeverity? MinimumSeverity = null,
    DatabaseRecordSort Sort = DatabaseRecordSort.Id,
    bool Descending = false)
{
    public static DatabaseRecordFilter Parse(string? query)
    {
        string value = query?.Trim() ?? string.Empty;
        if (value.Length == 0) return new();
        List<string> text = [];
        int? id = null;
        bool? populated = null;
        bool? modified = null;
        bool? missing = null;
        DiagnosticSeverity? severity = null;
        foreach (string token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = token.IndexOf(':');
            if (separator <= 0)
            {
                text.Add(token);
                continue;
            }
            string key = token[..separator].ToLowerInvariant();
            string operand = token[(separator + 1)..];
            switch (key)
            {
                case "id" when int.TryParse(operand, out int parsed): id = parsed; break;
                case "used" when bool.TryParse(operand, out bool parsedUsed): populated = parsedUsed; break;
                case "populated" when bool.TryParse(operand, out bool parsedPopulated): populated = parsedPopulated; break;
                case "modified" when bool.TryParse(operand, out bool parsed): modified = parsed; break;
                case "missing" when bool.TryParse(operand, out bool parsed): missing = parsed; break;
                case "error" when bool.TryParse(operand, out bool parsed) && parsed: severity = DiagnosticSeverity.Error; break;
                case "warning" when bool.TryParse(operand, out bool parsed) && parsed: severity = DiagnosticSeverity.Warning; break;
                default: text.Add(token); break;
            }
        }
        return new(string.Join(' ', text), id, populated, modified, missing, severity);
    }
}
