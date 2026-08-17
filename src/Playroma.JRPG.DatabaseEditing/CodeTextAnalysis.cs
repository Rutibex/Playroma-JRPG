using Playroma.JRPG.Editing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.DatabaseEditing;

public enum CodeTextTokenKind
{
    Plain,
    Identifier,
    Number,
    String,
    Comment,
    Operator,
    NoteTag,
    Bracket,
    Error
}

public sealed record CodeTextToken(int Start, int Length, CodeTextTokenKind Kind, string Text);
public sealed record CodeTextDiagnostic(int Start, int Length, string Code, string Message);
public sealed record CodeTextAnalysis(IReadOnlyList<CodeTextToken> Tokens, IReadOnlyList<CodeTextDiagnostic> Diagnostics);

public static class CodeTextAnalyzer
{
    public static CodeTextAnalysis AnalyzeJavaScript(string? text, bool noteTags = false)
    {
        string source = text ?? string.Empty;
        List<CodeTextToken> tokens = [];
        List<CodeTextDiagnostic> diagnostics = [];
        Stack<(char Character, int Position)> brackets = [];
        int index = 0;
        while (index < source.Length)
        {
            char current = source[index];
            if (char.IsWhiteSpace(current))
            {
                int start = index++;
                while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
                tokens.Add(new(start, index - start, CodeTextTokenKind.Plain, source[start..index]));
                continue;
            }
            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                int start = index;
                index += 2;
                while (index < source.Length && source[index] is not '\r' and not '\n') index++;
                tokens.Add(new(start, index - start, CodeTextTokenKind.Comment, source[start..index]));
                continue;
            }
            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                int start = index;
                index += 2;
                int close = source.IndexOf("*/", index, StringComparison.Ordinal);
                if (close < 0)
                {
                    tokens.Add(new(start, source.Length - start, CodeTextTokenKind.Error, source[start..]));
                    diagnostics.Add(new(start, source.Length - start, "PJRPG8006", "Unterminated block comment."));
                    break;
                }
                index = close + 2;
                tokens.Add(new(start, index - start, CodeTextTokenKind.Comment, source[start..index]));
                continue;
            }
            if (current is '\'' or '"' or '`')
            {
                int start = index;
                char quote = current;
                index++;
                bool escaped = false;
                while (index < source.Length)
                {
                    char candidate = source[index++];
                    if (escaped) { escaped = false; continue; }
                    if (candidate == '\\') { escaped = true; continue; }
                    if (candidate == quote) break;
                }
                bool closed = index > start + 1 && source[index - 1] == quote;
                tokens.Add(new(start, index - start, closed ? CodeTextTokenKind.String : CodeTextTokenKind.Error, source[start..index]));
                if (!closed) diagnostics.Add(new(start, index - start, "PJRPG8007", "Unterminated string literal."));
                continue;
            }
            if (noteTags && current == '<')
            {
                int close = source.IndexOf('>', index + 1);
                if (close >= 0)
                {
                    int length = close - index + 1;
                    tokens.Add(new(index, length, CodeTextTokenKind.NoteTag, source.Substring(index, length)));
                    index = close + 1;
                    continue;
                }
            }
            if (char.IsDigit(current) || current == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1]))
            {
                int start = index++;
                while (index < source.Length && (char.IsDigit(source[index]) || source[index] is '.' or 'x' or 'X' || source[index] is >= 'a' and <= 'f' || source[index] is >= 'A' and <= 'F')) index++;
                tokens.Add(new(start, index - start, CodeTextTokenKind.Number, source[start..index]));
                continue;
            }
            if (char.IsLetter(current) || current is '_' or '$')
            {
                int start = index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '$')) index++;
                tokens.Add(new(start, index - start, CodeTextTokenKind.Identifier, source[start..index]));
                continue;
            }
            if (current is '(' or '[' or '{')
            {
                brackets.Push((current, index));
                tokens.Add(new(index, 1, CodeTextTokenKind.Bracket, current.ToString()));
                index++;
                continue;
            }
            if (current is ')' or ']' or '}')
            {
                bool matched = brackets.Count > 0 && Matches(brackets.Peek().Character, current);
                if (matched) brackets.Pop();
                else diagnostics.Add(new(index, 1, "PJRPG8008", $"Unmatched closing bracket '{current}'."));
                tokens.Add(new(index, 1, matched ? CodeTextTokenKind.Bracket : CodeTextTokenKind.Error, current.ToString()));
                index++;
                continue;
            }
            tokens.Add(new(index, 1, CodeTextTokenKind.Operator, current.ToString()));
            index++;
        }
        foreach ((char character, int position) in brackets)
        {
            diagnostics.Add(new(position, 1, "PJRPG8009", $"Unmatched opening bracket '{character}'."));
        }
        return new CodeTextAnalysis(tokens, diagnostics.OrderBy(diagnostic => diagnostic.Start).ToArray());
    }

    public static IReadOnlyList<(string Name, bool IsClosing, int Start, int Length)> ExtractNoteTags(string? text)
    {
        string source = text ?? string.Empty;
        List<(string, bool, int, int)> tags = [];
        int index = 0;
        while ((index = source.IndexOf('<', index)) >= 0)
        {
            int close = source.IndexOf('>', index + 1);
            if (close < 0) break;
            string content = source[(index + 1)..close].Trim();
            bool closing = content.StartsWith("/", StringComparison.Ordinal);
            if (closing) content = content[1..].TrimStart();
            int separator = content.IndexOfAny([':', ' ']);
            string name = separator < 0 ? content : content[..separator];
            if (name.Length > 0) tags.Add((name, closing, index, close - index + 1));
            index = close + 1;
        }
        return tags;
    }

    private static bool Matches(char open, char close) => (open, close) is ('(', ')') or ('[', ']') or ('{', '}');
}

public sealed record DatabaseHistoryEntry(
    Guid CommandId,
    string Description,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<DatabaseRecordHandle> Records);

public sealed class DatabaseHistoryIndex
{
    private readonly List<DatabaseHistoryEntry> _entries = [];

    public IReadOnlyList<DatabaseHistoryEntry> Entries => _entries;

    public void Observe(EditorCommandEvent notification, MvDocumentKind kind)
    {
        DatabaseRecordHandle[] records = notification.Targets
            .Where(target => target.Kind == EditorMutationTargetKind.Json)
            .SelectMany(target => ExtractRecord(target, kind))
            .Distinct()
            .ToArray();
        if (records.Length == 0) return;
        _entries.Add(new DatabaseHistoryEntry(notification.CommandId, notification.Description, notification.TimestampUtc, records));
    }

    public IReadOnlyList<DatabaseHistoryEntry> ForRecord(string relativePath, int id) => _entries
        .Where(entry => entry.Records.Any(record => record.Id == id && string.Equals(record.RelativePath, relativePath, PathComparison)))
        .OrderByDescending(entry => entry.TimestampUtc)
        .ToArray();

    private static IEnumerable<DatabaseRecordHandle> ExtractRecord(EditorMutationTarget target, MvDocumentKind kind)
    {
        IReadOnlyList<string> segments = target.Path.Segments();
        if (segments.Count > 0 && int.TryParse(segments[0], out int id) && id > 0)
        {
            yield return new DatabaseRecordHandle(kind, target.NormalizedRelativePath, id);
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
