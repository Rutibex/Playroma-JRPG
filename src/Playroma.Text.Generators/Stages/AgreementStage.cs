using System.Text.RegularExpressions;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Semantics;

namespace Playroma.Text.Generators.Stages;

/// <summary>
/// Lightweight English surface-realization helper driven by semantic features.
/// Replaces tokens such as {be}, {be:past}, {verb:guard}, {pronoun:subject},
/// {pronoun:object}, {pronoun:possessive}, {pronoun:reflexive}, and {article:apple}.
/// </summary>
public sealed class AgreementStage : ITextGenerationStage
{
    public const string StageKind = "phase6.agreement";
    private static readonly Regex TokenRegex = new(@"\{(?<kind>be|have|do|verb|pronoun|article)(?::(?<arg>[^{}]+))?\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    public string Kind => StageKind;

    public ValueTask<GenerationStageResult> ExecuteAsync(GenerationStageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = StageSettingsReader.GetString(context.Stage, "target", "primary");
        var source = target.Equals("primary", StringComparison.OrdinalIgnoreCase)
            ? context.WorkingBundle.PrimaryText
            : context.WorkingBundle.Fields.LastOrDefault(x => x.Name.Equals(target, StringComparison.OrdinalIgnoreCase))?.Text ?? string.Empty;
        if (string.IsNullOrEmpty(source)) return ValueTask.FromResult(GenerationStageResult.Ok(context.WorkingBundle));

        var number = ReadFeature(context.WorkingBundle, StageSettingsReader.GetString(context.Stage, "numberFeature", "grammar.number"), "singular");
        var personText = ReadFeature(context.WorkingBundle, StageSettingsReader.GetString(context.Stage, "personFeature", "grammar.person"), "3");
        var gender = ReadFeature(context.WorkingBundle, StageSettingsReader.GetString(context.Stage, "genderFeature", "grammar.gender"), "neutral");
        var person = int.TryParse(personText, out var parsed) ? Math.Clamp(parsed, 1, 3) : 3;
        var plural = number.Equals("plural", StringComparison.OrdinalIgnoreCase) || number.Equals("many", StringComparison.OrdinalIgnoreCase);

        string output;
        try
        {
            output = TokenRegex.Replace(source, match => Realize(match.Groups["kind"].Value, match.Groups["arg"].Success ? match.Groups["arg"].Value : null, plural, person, gender));
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(GenerationStageResult.Fail(context.WorkingBundle, new GenerationDiagnostic(GenerationDiagnosticCodes.AgreementFailed,
                GenerationDiagnosticSeverity.Error, "Agreement realization failed.", context.Stage.Id, ex.Message)));
        }

        GeneratedTextBundle bundle;
        if (target.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            var primaryField = context.WorkingBundle.Fields.LastOrDefault(x => x.IsPrimary);
            bundle = primaryField is null
                ? context.WorkingBundle with { PrimaryText = output }
                : context.WorkingBundle.WithField(primaryField.Name, output, primaryField.SemanticRole, true);
        }
        else
        {
            var field = context.WorkingBundle.Fields.LastOrDefault(x => x.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
            bundle = context.WorkingBundle.WithField(target, output, field?.SemanticRole ?? "agreement", field?.IsPrimary ?? false);
        }
        context.Trace.Add(context.Stage.Id, "agreement.realize", input: source, output: output, metadata: new Dictionary<string, string>
        {
            ["number"] = number, ["person"] = person.ToString(), ["gender"] = gender
        });
        return ValueTask.FromResult(GenerationStageResult.Ok(bundle));
    }

    private static string ReadFeature(GeneratedTextBundle bundle, string name, string fallback)
        => bundle.Features.TryGetValue(name, out var feature) && !string.IsNullOrWhiteSpace(feature.DisplayText) ? feature.DisplayText : fallback;

    private static string Realize(string kind, string? argument, bool plural, int person, string gender)
    {
        return kind.ToLowerInvariant() switch
        {
            "be" => argument?.Equals("past", StringComparison.OrdinalIgnoreCase) == true ? PastBe(plural, person) : PresentBe(plural, person),
            "have" => !plural && person == 3 ? "has" : "have",
            "do" => !plural && person == 3 ? "does" : "do",
            "verb" => ConjugatePresent(argument ?? string.Empty, plural, person),
            "pronoun" => Pronoun(argument ?? "subject", plural, gender),
            "article" => Article(argument ?? string.Empty),
            _ => string.Empty
        };
    }

    private static string PresentBe(bool plural, int person)
    {
        if (plural || person == 2) return "are";
        return person == 1 ? "am" : "is";
    }

    private static string PastBe(bool plural, int person) => plural || person == 2 ? "were" : "was";

    private static string ConjugatePresent(string lemma, bool plural, int person)
    {
        lemma = lemma.Trim();
        if (lemma.Length == 0 || plural || person != 3) return lemma;
        if (lemma.EndsWith("y", StringComparison.OrdinalIgnoreCase) && lemma.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(lemma[^2]))) return lemma[..^1] + "ies";
        if (lemma.EndsWith("s", StringComparison.OrdinalIgnoreCase) || lemma.EndsWith("x", StringComparison.OrdinalIgnoreCase) || lemma.EndsWith("z", StringComparison.OrdinalIgnoreCase) || lemma.EndsWith("ch", StringComparison.OrdinalIgnoreCase) || lemma.EndsWith("sh", StringComparison.OrdinalIgnoreCase)) return lemma + "es";
        return lemma + "s";
    }

    private static string Pronoun(string form, bool plural, string gender)
    {
        if (plural || gender.Equals("plural", StringComparison.OrdinalIgnoreCase) || gender.Equals("nonbinary", StringComparison.OrdinalIgnoreCase) || gender.Equals("they", StringComparison.OrdinalIgnoreCase))
            return form.ToLowerInvariant() switch { "object" => "them", "possessive" => "their", "reflexive" => plural ? "themselves" : "themself", _ => "they" };
        if (gender.Equals("male", StringComparison.OrdinalIgnoreCase) || gender.Equals("masculine", StringComparison.OrdinalIgnoreCase) || gender.Equals("he", StringComparison.OrdinalIgnoreCase))
            return form.ToLowerInvariant() switch { "object" => "him", "possessive" => "his", "reflexive" => "himself", _ => "he" };
        if (gender.Equals("female", StringComparison.OrdinalIgnoreCase) || gender.Equals("feminine", StringComparison.OrdinalIgnoreCase) || gender.Equals("she", StringComparison.OrdinalIgnoreCase))
            return form.ToLowerInvariant() switch { "object" => "her", "possessive" => "her", "reflexive" => "herself", _ => "she" };
        return form.ToLowerInvariant() switch { "object" => "it", "possessive" => "its", "reflexive" => "itself", _ => "it" };
    }

    private static string Article(string noun)
    {
        noun = noun.Trim();
        if (noun.Length == 0) return string.Empty;
        var first = char.ToLowerInvariant(noun[0]);
        return ("aeiou".Contains(first) ? "an " : "a ") + noun;
    }
}
