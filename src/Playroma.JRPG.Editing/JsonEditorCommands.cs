using System.Text;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.Editing;

public class SetJsonValueCommand : IMergeableEditorCommand
{
    private readonly byte[] _newRawJson;
    private string? _oldRawJson;
    private bool _oldValueExisted;
    private bool _captured;

    public SetJsonValueCommand(
        string relativePath,
        JsonPath path,
        string rawJson,
        string? description = null)
    {
        RelativePath = Normalize(relativePath);
        Path = path;
        _newRawJson = JsonCommandUtilities.Validate(rawJson);
        Description = description ?? $"Set {Path} in {RelativePath}";
        Targets = [new EditorMutationTarget(RelativePath, Path)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public JsonPath Path { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }
    public string NewRawJson => Encoding.UTF8.GetString(_newRawJson);

    public async ValueTask ExecuteAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        if (!_captured)
        {
            _oldValueExisted = document.ContainsPath(Path);
            _oldRawJson = document.GetRawJson(Path);
            _captured = true;
        }

        JsonCommandUtilities.SetOrAdd(document, Path, _newRawJson);
    }

    public async ValueTask UndoAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        EnsureCaptured();
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        if (_oldValueExisted)
        {
            JsonCommandUtilities.SetOrAdd(document, Path, JsonCommandUtilities.Validate(_oldRawJson ?? "null"));
        }
        else
        {
            JsonCommandUtilities.RemovePath(document, Path);
        }

    }

    public bool TryMergeWith(IEditorCommand newerCommand, out IEditorCommand mergedCommand)
    {
        if (newerCommand is SetJsonValueCommand newer
            && string.Equals(RelativePath, newer.RelativePath, PathComparison)
            && Path == newer.Path)
        {
            SetJsonValueCommand merged = new(RelativePath, Path, newer.NewRawJson, newer.Description)
            {
                _oldRawJson = _oldRawJson,
                _oldValueExisted = _oldValueExisted,
                _captured = _captured
            };
            mergedCommand = merged;
            return true;
        }

        mergedCommand = this;
        return false;
    }

    private void EnsureCaptured()
    {
        if (!_captured)
        {
            throw new InvalidOperationException("The command has not been executed and cannot be undone.");
        }
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

public sealed class SetJsonStringCommand : SetJsonValueCommand
{
    public SetJsonStringCommand(
        string relativePath,
        JsonPath path,
        string value,
        string? description = null)
        : base(relativePath, path, Encoding.UTF8.GetString(JsonValueEncoder.EncodeString(value)), description)
    {
    }
}

public sealed class SetJsonInt32Command : SetJsonValueCommand
{
    public SetJsonInt32Command(
        string relativePath,
        JsonPath path,
        int value,
        string? description = null)
        : base(relativePath, path, value.ToString(System.Globalization.CultureInfo.InvariantCulture), description)
    {
    }
}

public sealed class AddJsonPropertyCommand : IEditorCommand
{
    private readonly byte[] _rawJson;
    private bool _executed;

    public AddJsonPropertyCommand(
        string relativePath,
        JsonPath objectPath,
        string propertyName,
        string rawJson,
        string? description = null)
    {
        RelativePath = Normalize(relativePath);
        ObjectPath = objectPath;
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        _rawJson = JsonCommandUtilities.Validate(rawJson);
        JsonPath target = objectPath.Append(propertyName);
        Description = description ?? $"Add {target} in {RelativePath}";
        Targets = [new EditorMutationTarget(RelativePath, target)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public JsonPath ObjectPath { get; }
    public string PropertyName { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        JsonPath target = ObjectPath.Append(PropertyName);
        if (document.ContainsPath(target))
        {
            throw new InvalidOperationException($"Property '{target}' already exists.");
        }

        document.AddProperty(ObjectPath, PropertyName, _rawJson);
        _executed = true;
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!_executed) throw new InvalidOperationException("The command has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.RemoveProperty(ObjectPath, PropertyName);
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}

public sealed class RemoveJsonPropertyCommand : IEditorCommand
{
    private string? _oldRawJson;
    private bool _captured;

    public RemoveJsonPropertyCommand(
        string relativePath,
        JsonPath objectPath,
        string propertyName,
        string? description = null)
    {
        RelativePath = Normalize(relativePath);
        ObjectPath = objectPath;
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
        JsonPath target = objectPath.Append(propertyName);
        Description = description ?? $"Remove {target} from {RelativePath}";
        Targets = [new EditorMutationTarget(RelativePath, target)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public JsonPath ObjectPath { get; }
    public string PropertyName { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        JsonPath target = ObjectPath.Append(PropertyName);
        if (!_captured)
        {
            _oldRawJson = document.GetRawJson(target)
                ?? throw new InvalidOperationException($"Property '{target}' does not exist.");
            _captured = true;
        }

        document.RemoveProperty(ObjectPath, PropertyName);
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!_captured) throw new InvalidOperationException("The command has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.AddProperty(ObjectPath, PropertyName, JsonCommandUtilities.Validate(_oldRawJson ?? "null"));
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}

public class InsertJsonArrayElementCommand : IEditorCommand
{
    private readonly byte[] _rawJson;
    private bool _executed;

    public InsertJsonArrayElementCommand(
        string relativePath,
        JsonPath arrayPath,
        int index,
        string rawJson,
        string? description = null)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        RelativePath = Normalize(relativePath);
        ArrayPath = arrayPath;
        Index = index;
        _rawJson = JsonCommandUtilities.Validate(rawJson);
        Description = description ?? $"Insert array element at {ArrayPath.Append(Index)} in {RelativePath}";
        Targets = [new EditorMutationTarget(RelativePath, ArrayPath.Append(Index))];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public JsonPath ArrayPath { get; }
    public int Index { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.InsertArrayElement(ArrayPath, Index, _rawJson);
        _executed = true;
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!_executed) throw new InvalidOperationException("The command has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.RemoveArrayElement(ArrayPath, Index);
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}

public class RemoveJsonArrayElementCommand : IEditorCommand
{
    private string? _oldRawJson;
    private bool _captured;

    public RemoveJsonArrayElementCommand(
        string relativePath,
        JsonPath arrayPath,
        int index,
        string? description = null)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        RelativePath = Normalize(relativePath);
        ArrayPath = arrayPath;
        Index = index;
        Description = description ?? $"Remove array element at {ArrayPath.Append(Index)} in {RelativePath}";
        Targets = [new EditorMutationTarget(RelativePath, ArrayPath.Append(Index))];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string RelativePath { get; }
    public JsonPath ArrayPath { get; }
    public int Index { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }

    public async ValueTask ExecuteAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        JsonPath target = ArrayPath.Append(Index);
        if (!_captured)
        {
            _oldRawJson = document.GetRawJson(target)
                ?? throw new InvalidOperationException($"Array element '{target}' does not exist.");
            _captured = true;
        }

        document.RemoveArrayElement(ArrayPath, Index);
    }

    public async ValueTask UndoAsync(EditorCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!_captured) throw new InvalidOperationException("The command has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(RelativePath, cancellationToken).ConfigureAwait(false);
        document.InsertArrayElement(ArrayPath, Index, JsonCommandUtilities.Validate(_oldRawJson ?? "null"));
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
}
