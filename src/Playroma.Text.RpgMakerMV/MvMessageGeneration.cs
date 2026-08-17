using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.RpgMakerMV;

public sealed record MvMessageControlCode(string Raw, string Name, string? Argument, int StartIndex, bool IsKnown);
public sealed record MvMessageAnalysis(string Source, IReadOnlyList<MvMessageControlCode> ControlCodes, IReadOnlyList<string> Warnings)
{
    public bool HasUnknownControlCodes => ControlCodes.Any(x => !x.IsKnown);
}
public sealed record MvMessagePage(IReadOnlyList<string> Lines)
{
    public string Text => string.Join("\n", Lines);
}

/// <summary>Parses and validates RPG Maker MV message escape/control codes without evaluating game state.</summary>
public static class MvMessageSyntax
{
    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "V", "N", "P", "G", "C", "I", "{", "}", "$", ".", "|", "!", ">", "<", "^", "\\"
    };

    public static MvMessageAnalysis Analyze(string? text)
    {
        text ??= string.Empty;
        var codes = new List<MvMessageControlCode>();
        var warnings = new List<string>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\') continue;
            if (i + 1 >= text.Length)
            {
                warnings.Add("The message ends with a lone backslash.");
                continue;
            }
            int start = i;
            char next = text[++i];
            string name;
            string? argument = null;
            if (char.IsLetter(next))
            {
                int nameStart = i;
                while (i + 1 < text.Length && char.IsLetter(text[i + 1])) i++;
                name = text[nameStart..(i + 1)].ToUpperInvariant();
                if (i + 1 < text.Length && text[i + 1] == '[')
                {
                    int close = text.IndexOf(']', i + 2);
                    if (close < 0)
                    {
                        warnings.Add($"Control code \\{name} has an unterminated argument.");
                    }
                    else
                    {
                        argument = text[(i + 2)..close];
                        i = close;
                    }
                }
            }
            else name = next.ToString();
            string raw = text[start..(i + 1)];
            bool known = Known.Contains(name);
            if (!known) warnings.Add($"Unknown RPG Maker message control code '{raw}'. It will be preserved literally.");
            codes.Add(new(raw, name, argument, start, known));
        }
        return new(text, codes, warnings);
    }

    /// <summary>Returns a conservative visible-width estimate for wrapping and previews.</summary>
    public static int EstimateVisibleColumns(string text)
    {
        int width = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\') { width++; continue; }
            if (!TryReadControl(text, ref i, out string name, out _)) { width++; continue; }
            width += name.ToUpperInvariant() switch
            {
                "I" => 2,                 // icon
                "V" or "N" or "P" => 8, // runtime value/name is unknown; reserve useful space
                "G" => 1,
                _ => 0
            };
        }
        return width;
    }

    internal static bool TryReadControl(string text, ref int index, out string name, out string? argument)
    {
        name = string.Empty; argument = null;
        if (index < 0 || index >= text.Length || text[index] != '\\' || index + 1 >= text.Length) return false;
        int i = index + 1;
        char next = text[i];
        if (char.IsLetter(next))
        {
            int start = i;
            while (i + 1 < text.Length && char.IsLetter(text[i + 1])) i++;
            name = text[start..(i + 1)];
            if (i + 1 < text.Length && text[i + 1] == '[')
            {
                int close = text.IndexOf(']', i + 2);
                if (close >= 0)
                {
                    argument = text[(i + 2)..close];
                    i = close;
                }
            }
        }
        else name = next.ToString();
        index = i;
        return true;
    }
}

/// <summary>Word-wraps message text and splits it into RPG Maker MV's normal four-line message pages.</summary>
public static class MvMessagePaginator
{
    public const int DefaultLinesPerPage = 4;
    public const int DefaultVisibleColumns = 46;

    public static IReadOnlyList<MvMessagePage> Paginate(string? text, int linesPerPage = DefaultLinesPerPage, int visibleColumns = DefaultVisibleColumns)
    {
        if (linesPerPage <= 0) throw new ArgumentOutOfRangeException(nameof(linesPerPage));
        if (visibleColumns <= 8) throw new ArgumentOutOfRangeException(nameof(visibleColumns));
        text = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = new List<string>();
        foreach (string sourceLine in text.Split('\n')) lines.AddRange(WrapLine(sourceLine, visibleColumns));
        if (lines.Count == 0) lines.Add(string.Empty);
        var pages = new List<MvMessagePage>();
        for (int i = 0; i < lines.Count; i += linesPerPage)
            pages.Add(new(lines.Skip(i).Take(linesPerPage).ToArray()));
        return pages;
    }

    public static IReadOnlyList<string> WrapLine(string line, int visibleColumns)
    {
        if (MvMessageSyntax.EstimateVisibleColumns(line) <= visibleColumns) return [line];
        var result = new List<string>();
        string remaining = line;
        while (MvMessageSyntax.EstimateVisibleColumns(remaining) > visibleColumns)
        {
            int cut = FindWrapCut(remaining, visibleColumns);
            if (cut <= 0 || cut >= remaining.Length) break;
            string head = remaining[..cut].TrimEnd();
            if (head.Length == 0) break;
            result.Add(head);
            remaining = remaining[cut..].TrimStart();
        }
        result.Add(remaining);
        return result;
    }

    private static int FindWrapCut(string text, int maxColumns)
    {
        int width = 0, lastWhitespace = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                int before = i;
                if (MvMessageSyntax.TryReadControl(text, ref i, out string name, out _))
                {
                    width += name.ToUpperInvariant() switch { "I" => 2, "V" or "N" or "P" => 8, "G" => 1, _ => 0 };
                    if (width > maxColumns) return lastWhitespace > 0 ? lastWhitespace : before;
                    continue;
                }
            }
            if (char.IsWhiteSpace(text[i])) lastWhitespace = i;
            width++;
            if (width > maxColumns) return lastWhitespace > 0 ? lastWhitespace : Math.Max(1, i);
        }
        return text.Length;
    }
}

public enum MvGeneratedEventTextKind { ShowText, ShowChoices, ScrollingText, Comment, BranchingDialogue }
public sealed record MvShowTextSettings(string FaceName = "", int FaceIndex = 0, int Background = 0, int Position = 2);
public sealed record MvScrollingTextSettings(int Speed = 2, bool DisableFastForward = false);
public sealed record MvChoiceSettings(int CancelType = -2, int DefaultType = 0, int Position = 2, int Background = 0);
public sealed record MvGeneratedChoice(string Text, string? Response = null);
public sealed record MvEventTextPlan(
    MvGeneratedEventTextKind Kind,
    IReadOnlyList<string> RawCommands,
    string PreviewText,
    IReadOnlyList<TextImportValidationIssue> Issues)
{
    public bool CanApply => RawCommands.Count > 0 && Issues.All(x => !x.IsError);
}

/// <summary>Compiles generated prose and dialogue into valid RPG Maker MV event-command rows.</summary>
public static class MvEventTextCompiler
{
    public const int MaxChoices = 6;

    public static MvEventTextPlan CompileShowText(string text, int indent = 0, MvShowTextSettings? settings = null)
    {
        settings ??= new();
        var issues = AnalyzeMessage(text);
        var commands = new List<string>();
        foreach (MvMessagePage page in MvMessagePaginator.Paginate(text))
        {
            commands.Add(Command(101, indent, new object?[] { settings.FaceName, settings.FaceIndex, settings.Background, settings.Position }));
            foreach (string line in page.Lines) commands.Add(Command(401, indent, new object?[] { line }));
        }
        return new(MvGeneratedEventTextKind.ShowText, commands, PreviewPages(text), issues);
    }

    public static MvEventTextPlan CompileScrollingText(string text, int indent = 0, MvScrollingTextSettings? settings = null)
    {
        settings ??= new();
        var issues = AnalyzeMessage(text);
        var commands = new List<string> { Command(105, indent, new object?[] { Math.Clamp(settings.Speed, 1, 8), settings.DisableFastForward }) };
        foreach (string line in NormalizeLines(text)) commands.Add(Command(405, indent, new object?[] { line }));
        return new(MvGeneratedEventTextKind.ScrollingText, commands, text, issues);
    }

    public static MvEventTextPlan CompileComment(string text, int indent = 0)
    {
        string[] lines = NormalizeLines(text);
        if (lines.Length == 0) lines = [string.Empty];
        var commands = new List<string> { Command(108, indent, new object?[] { lines[0] }) };
        foreach (string line in lines.Skip(1)) commands.Add(Command(408, indent, new object?[] { line }));
        return new(MvGeneratedEventTextKind.Comment, commands, text, []);
    }

    public static MvEventTextPlan CompileChoices(
        IReadOnlyList<MvGeneratedChoice> choices,
        int indent = 0,
        MvChoiceSettings? settings = null,
        MvShowTextSettings? responseSettings = null)
    {
        settings ??= new(); responseSettings ??= new();
        var issues = new List<TextImportValidationIssue>();
        var normalized = choices.Where(x => !string.IsNullOrWhiteSpace(x.Text)).Take(MaxChoices + 1).ToArray();
        if (normalized.Length == 0) issues.Add(new("PTXTMV0901", "At least one choice is required.", true));
        if (normalized.Length > MaxChoices) issues.Add(new("PTXTMV0902", $"RPG Maker MV Show Choices supports at most {MaxChoices} choices.", true));
        var used = normalized.Take(MaxChoices).ToArray();
        int cancel = settings.CancelType >= used.Length ? -2 : settings.CancelType;
        int def = settings.DefaultType >= used.Length ? 0 : settings.DefaultType;
        var commands = new List<string>();
        if (used.Length > 0)
        {
            commands.Add(Command(102, indent, new object?[] { used.Select(x => x.Text).ToArray(), cancel, def, settings.Position, settings.Background }));
            for (int i = 0; i < used.Length; i++)
            {
                commands.Add(Command(402, indent, new object?[] { i, used[i].Text }));
                if (!string.IsNullOrWhiteSpace(used[i].Response))
                    commands.AddRange(CompileShowText(used[i].Response!, indent + 1, responseSettings).RawCommands);
            }
            if (cancel == -1) commands.Add(Command(403, indent, Array.Empty<object?>()));
            commands.Add(Command(404, indent, Array.Empty<object?>()));
        }
        string preview = string.Join("\n", used.Select((x, i) => $"> {x.Text}" + (string.IsNullOrWhiteSpace(x.Response) ? string.Empty : $"\n  {x.Response}")));
        return new(MvGeneratedEventTextKind.ShowChoices, commands, preview, issues);
    }

    public static MvEventTextPlan CompileBranchingDialogue(
        string prompt,
        IReadOnlyList<MvGeneratedChoice> choices,
        int indent = 0,
        MvShowTextSettings? promptSettings = null,
        MvChoiceSettings? choiceSettings = null)
    {
        var promptPlan = CompileShowText(prompt, indent, promptSettings);
        var choicePlan = CompileChoices(choices, indent, choiceSettings, promptSettings);
        var commands = promptPlan.RawCommands.Concat(choicePlan.RawCommands).ToArray();
        var issues = promptPlan.Issues.Concat(choicePlan.Issues).ToArray();
        return new(MvGeneratedEventTextKind.BranchingDialogue, commands,
            promptPlan.PreviewText + "\n\n" + choicePlan.PreviewText, issues);
    }

    public static MvEventTextPlan CompileBundle(GeneratedTextBundle bundle, MvGeneratedEventTextKind kind, int indent = 0)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        string primary = bundle.PrimaryText;
        return kind switch
        {
            MvGeneratedEventTextKind.ShowText => CompileShowText(primary, indent),
            MvGeneratedEventTextKind.ScrollingText => CompileScrollingText(primary, indent),
            MvGeneratedEventTextKind.Comment => CompileComment(primary, indent),
            MvGeneratedEventTextKind.ShowChoices => CompileChoices(ExtractChoices(bundle), indent),
            MvGeneratedEventTextKind.BranchingDialogue => CompileBranchingDialogue(primary, ExtractChoices(bundle), indent),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public static IReadOnlyList<MvGeneratedChoice> ExtractChoices(GeneratedTextBundle bundle)
    {
        var result = new List<MvGeneratedChoice>();
        for (int i = 1; i <= MaxChoices + 1; i++)
        {
            string? text = Field(bundle, $"choice{i}");
            if (string.IsNullOrWhiteSpace(text)) continue;
            result.Add(new(text, Field(bundle, $"response{i}")));
        }
        if (result.Count == 0)
        {
            string? serialized = Field(bundle, "choices");
            if (!string.IsNullOrWhiteSpace(serialized))
                result.AddRange(serialized.Split(['|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => new MvGeneratedChoice(x)));
        }
        return result;
    }

    private static string? Field(GeneratedTextBundle bundle, string name) => bundle.Fields.FirstOrDefault(x =>
        x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.SemanticRole, name, StringComparison.OrdinalIgnoreCase))?.Text;

    private static List<TextImportValidationIssue> AnalyzeMessage(string text)
    {
        MvMessageAnalysis analysis = MvMessageSyntax.Analyze(text);
        var issues = analysis.Warnings.Select(x => new TextImportValidationIssue("PTXTMV0903", x, false)).ToList();
        foreach (string line in NormalizeLines(text))
            if (MvMessageSyntax.EstimateVisibleColumns(line) > 70)
                issues.Add(new("PTXTMV0904", "A message line is unusually long; automatic wrapping will split it for Show Text.", false));
        return issues;
    }

    private static string PreviewPages(string text)
    {
        IReadOnlyList<MvMessagePage> pages = MvMessagePaginator.Paginate(text);
        return string.Join("\n\n──────── page ────────\n\n", pages.Select(x => x.Text));
    }

    private static string[] NormalizeLines(string? text) => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static string Command(int code, int indent, object?[] parameters) =>
        JsonSerializer.Serialize(new { code, indent = Math.Max(0, indent), parameters });
}
