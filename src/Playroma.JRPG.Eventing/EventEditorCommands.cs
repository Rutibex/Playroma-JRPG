using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;

namespace Playroma.JRPG.Eventing;

public sealed class ReplaceEventCommandSpanCommand : IEditorCommand
{
    private readonly IReadOnlyList<string> _newCommands;
    private List<string>? _oldCommands;
    private bool _captured;

    public ReplaceEventCommandSpanCommand(
        EventListAddress address,
        EventCommandSpan span,
        IEnumerable<string> newCommands,
        string? description = null)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        if (span.StartIndex < 0) throw new ArgumentOutOfRangeException(nameof(span));
        if (span.Length < 0) throw new ArgumentOutOfRangeException(nameof(span));
        Span = span;
        ArgumentNullException.ThrowIfNull(newCommands);
        _newCommands = newCommands.Select(ValidateObject).ToArray();
        Description = description ?? $"Replace event command span {span.StartIndex}–{Math.Max(span.StartIndex, span.EndExclusive - 1)}";
        Targets = [new EditorMutationTarget(Address.NormalizedRelativePath, Address.ListPath)];
    }

    public Guid Id { get; } = Guid.NewGuid();
    public EventListAddress Address { get; }
    public EventCommandSpan Span { get; }
    public string Description { get; }
    public IReadOnlyCollection<EditorMutationTarget> Targets { get; }
    public int NewCommandCount => _newCommands.Count;

    public async ValueTask ExecuteAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(Address.NormalizedRelativePath, cancellationToken).ConfigureAwait(false);
        JsonSyntaxNode? node = document.Syntax.Resolve(Address.ListPath);
        if (node is not JsonArraySyntax array)
        {
            throw new InvalidOperationException($"Event list '{Address.ListPath}' is not an array.");
        }
        if (Span.EndExclusive > array.Elements.Count)
        {
            throw new InvalidOperationException($"Event command span {Span.StartIndex}+{Span.Length} exceeds list length {array.Elements.Count}.");
        }

        if (!_captured)
        {
            _oldCommands = [];
            for (int index = 0; index < Span.Length; index++)
            {
                _oldCommands.Add(document.GetRawJson(Address.ListPath.Append(Span.StartIndex + index)) ?? "null");
            }
            _captured = true;
        }

        Replace(document, Span.StartIndex, Span.Length, _newCommands);
    }

    public async ValueTask UndoAsync(
        EditorCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_captured || _oldCommands is null) throw new InvalidOperationException("The command has not been executed.");
        MutableJsonProjectDocument document = await context.OpenJsonDocumentAsync(Address.NormalizedRelativePath, cancellationToken).ConfigureAwait(false);
        Replace(document, Span.StartIndex, _newCommands.Count, _oldCommands);
    }

    private void Replace(
        MutableJsonProjectDocument document,
        int startIndex,
        int removeCount,
        IReadOnlyList<string> insert)
    {
        for (int index = 0; index < removeCount; index++)
        {
            document.RemoveArrayElement(Address.ListPath, startIndex);
        }
        for (int index = 0; index < insert.Count; index++)
        {
            document.InsertArrayElement(
                Address.ListPath,
                startIndex + index,
                System.Text.Encoding.UTF8.GetBytes(insert[index]));
        }
    }

    private static string ValidateObject(string rawJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawJson);
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(rawJson);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            throw new ArgumentException("An event command must be a JSON object.", nameof(rawJson));
        }
        return rawJson;
    }
}
