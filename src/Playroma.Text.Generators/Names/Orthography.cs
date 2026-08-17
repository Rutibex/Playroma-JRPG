using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Playroma.Text.Core.Names;
using Playroma.Text.Core.Random;

namespace Playroma.Text.Generators.Names;

public static class NameOrthography
{
    public static string Apply(string value, LanguageProfile profile, IDeterministicRandom random)
    {
        var result = value;
        foreach (var rule in profile.EffectiveOrthographicRules)
        {
            if (string.IsNullOrEmpty(rule.Pattern) || !random.NextBool(Math.Clamp(rule.Probability, 0, 1))) continue;
            if (rule.UseRegex)
            {
                var options = RegexOptions.CultureInvariant;
                if (rule.IgnoreCase) options |= RegexOptions.IgnoreCase;
                result = Regex.Replace(result, rule.Pattern, rule.Replacement ?? string.Empty, options, TimeSpan.FromMilliseconds(100));
            }
            else
            {
                result = result.Replace(rule.Pattern, rule.Replacement ?? string.Empty, rule.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
        }
        return Cleanup(result);
    }

    public static string TitleCaseName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var lower = value.ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        var capitalize = true;
        foreach (var ch in lower)
        {
            if (capitalize && char.IsLetter(ch)) { builder.Append(char.ToUpperInvariant(ch)); capitalize = false; }
            else builder.Append(ch);
            if (ch is ' ' or '-' or '\'') capitalize = true;
        }
        return builder.ToString();
    }

    private static string Cleanup(string value)
    {
        var result = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim(' ', '-', '\'');
        result = Regex.Replace(result, @"-{2,}", "-");
        result = Regex.Replace(result, @"'{2,}", "'");
        return result;
    }
}
