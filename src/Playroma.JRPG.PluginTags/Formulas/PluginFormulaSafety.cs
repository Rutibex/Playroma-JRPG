namespace Playroma.JRPG.PluginTags;

public enum PluginFormulaIssueSeverity
{
    Information,
    Warning,
    Error
}

public sealed record PluginFormulaIssue(
    PluginFormulaIssueSeverity Severity,
    string Code,
    string Message,
    int? Offset = null);

public sealed record PluginFormulaVariable(
    string Name,
    string Description,
    bool IsCommon = true);

public static class PluginFormulaSafetyAnalyzer
{
    private static readonly IReadOnlyList<(string Token, string Message)> DangerousTokens =
    [
        ("require(", "Node module loading is unsafe in editor-authored formulas."),
        ("process.", "Process access is unsafe in editor-authored formulas."),
        ("child_process", "Child-process execution is unsafe in editor-authored formulas."),
        ("nw.", "NW.js host access should not be used in a data formula."),
        ("window.", "Window access makes the formula dependent on the game host."),
        ("document.", "DOM access makes the formula dependent on the game host."),
        ("XMLHttpRequest", "Network access should not occur in a formula."),
        ("fetch(", "Network access should not occur in a formula."),
        ("eval(", "Nested eval makes formula behavior difficult to validate."),
        ("Function(", "Dynamic function construction makes formula behavior difficult to validate.")
    ];

    public static IReadOnlyList<PluginFormulaVariable> CommonYanflyVariables { get; } =
    [
        new("a / user", "The acting battler."),
        new("b / target", "The target battler."),
        new("item", "The current skill or item."),
        new("value", "The value being calculated or returned."),
        new("s[n]", "Game switch n."),
        new("v[n]", "Game variable n."),
        new("result", "The current action result when provided by the plugin."),
        new("origin", "The original source battler or object in some state/aura callbacks.", false)
    ];

    public static IReadOnlyList<PluginFormulaIssue> Analyze(string? code)
    {
        string text = code ?? string.Empty;
        List<PluginFormulaIssue> issues = [];
        if (string.IsNullOrWhiteSpace(text))
        {
            issues.Add(new(PluginFormulaIssueSeverity.Warning, "FORMULA_EMPTY", "The formula or script body is empty."));
            return issues;
        }

        Stack<(char Bracket, int Offset)> brackets = new();
        bool single = false;
        bool dbl = false;
        bool template = false;
        bool escape = false;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (escape) { escape = false; continue; }
            if ((single || dbl || template) && character == '\\') { escape = true; continue; }
            if (!dbl && !template && character == '\'') { single = !single; continue; }
            if (!single && !template && character == '"') { dbl = !dbl; continue; }
            if (!single && !dbl && character == '`') { template = !template; continue; }
            if (single || dbl || template) continue;

            if (character is '(' or '[' or '{') brackets.Push((character, index));
            else if (character is ')' or ']' or '}')
            {
                if (brackets.Count == 0 || !Matches(brackets.Peek().Bracket, character))
                    issues.Add(new(PluginFormulaIssueSeverity.Error, "FORMULA_BRACKET", $"Unexpected closing '{character}'.", index));
                else brackets.Pop();
            }
        }

        foreach ((char bracket, int offset) in brackets)
            issues.Add(new(PluginFormulaIssueSeverity.Error, "FORMULA_BRACKET", $"Opening '{bracket}' has no closing bracket.", offset));
        if (single || dbl || template)
            issues.Add(new(PluginFormulaIssueSeverity.Error, "FORMULA_STRING", "A string literal is not closed."));

        foreach ((string token, string message) in DangerousTokens)
        {
            int offset = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (offset >= 0) issues.Add(new(PluginFormulaIssueSeverity.Warning, "FORMULA_HOST_ACCESS", message, offset));
        }

        return issues;
    }

    private static bool Matches(char opening, char closing) =>
        (opening == '(' && closing == ')') ||
        (opening == '[' && closing == ']') ||
        (opening == '{' && closing == '}');
}
