using System.Text.Json;
using System.Text.RegularExpressions;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Generators.Stages;

public sealed class ConstraintStage : ITextGenerationStage
{
    public const string StageKind = "phase1.constraint";
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = StageSettingsReader.GetString(context.Stage, "target", "primary");
        var text = target.Equals("primary", StringComparison.OrdinalIgnoreCase)
            ? context.WorkingBundle.PrimaryText
            : context.WorkingBundle.Fields.FirstOrDefault(x => string.Equals(x.Name, target, StringComparison.OrdinalIgnoreCase))?.Text ?? string.Empty;
        var failures = Evaluate(context.Stage, text);
        if (failures.Count == 0)
        {
            context.Trace.Add(context.Stage.Id, "constraint.pass", output: text);
            return ValueTask.FromResult(GenerationStageResult.Ok(context.WorkingBundle));
        }
        var severity = StageSettingsReader.GetString(context.Stage, "mode", "error").Equals("warn", StringComparison.OrdinalIgnoreCase) ? GenerationDiagnosticSeverity.Warning : GenerationDiagnosticSeverity.Error;
        var diagnostic = new GenerationDiagnostic(GenerationDiagnosticCodes.ConstraintFailed, severity, string.Join(" ", failures), context.Stage.Id);
        context.Trace.Add(context.Stage.Id, "constraint.fail", output: text, metadata: new Dictionary<string, string> { ["failures"] = failures.Count.ToString() });
        return ValueTask.FromResult(new GenerationStageResult(severity != GenerationDiagnosticSeverity.Error, context.WorkingBundle, new[] { diagnostic }));
    }

    public static IReadOnlyList<string> Evaluate(TextRecipeStage stage, string text)
    {
        var failures = new List<string>();
        var comparison = StageSettingsReader.GetBool(stage, "caseSensitive", false) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var minLength = StageSettingsReader.GetInt(stage, "minLength", -1);
        var maxLength = StageSettingsReader.GetInt(stage, "maxLength", -1);
        if (minLength >= 0 && text.Length < minLength) failures.Add($"Minimum length is {minLength}.");
        if (maxLength >= 0 && text.Length > maxLength) failures.Add($"Maximum length is {maxLength}.");
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var minWords = StageSettingsReader.GetInt(stage, "minWords", -1);
        var maxWords = StageSettingsReader.GetInt(stage, "maxWords", -1);
        if (minWords >= 0 && words < minWords) failures.Add($"Minimum word count is {minWords}.");
        if (maxWords >= 0 && words > maxWords) failures.Add($"Maximum word count is {maxWords}.");
        var starts = StageSettingsReader.GetString(stage, "startsWith", string.Empty);
        if (starts.Length > 0 && !text.StartsWith(starts, comparison)) failures.Add($"Text must start with '{starts}'.");
        var ends = StageSettingsReader.GetString(stage, "endsWith", string.Empty);
        if (ends.Length > 0 && !text.EndsWith(ends, comparison)) failures.Add($"Text must end with '{ends}'.");
        foreach (var required in StageSettingsReader.GetStringArray(stage, "required")) if (!text.Contains(required, comparison)) failures.Add($"Required text '{required}' is missing.");
        foreach (var forbidden in StageSettingsReader.GetStringArray(stage, "forbidden")) if (text.Contains(forbidden, comparison)) failures.Add($"Forbidden text '{forbidden}' was present.");
        var pattern = StageSettingsReader.GetString(stage, "regex", string.Empty);
        if (pattern.Length > 0)
        {
            try { if (!Regex.IsMatch(text, pattern, StageSettingsReader.GetBool(stage, "caseSensitive", false) ? RegexOptions.CultureInvariant : RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) failures.Add("Regular-expression constraint did not match."); }
            catch (ArgumentException) { failures.Add("Regular-expression constraint is invalid."); }
            catch (RegexMatchTimeoutException) { failures.Add("Regular-expression constraint timed out."); }
        }
        return failures;
    }
}
