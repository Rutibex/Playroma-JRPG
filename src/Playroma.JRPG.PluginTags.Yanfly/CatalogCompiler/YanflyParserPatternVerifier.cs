using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyParserPatternVerifier
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    { "the", "and", "or", "to", "from", "with", "named", "name", "value", "values", "number", "text", "string", "code", "eval", "custom", "effect", "user", "target", "stat", "type", "filename", "formula", "javascript", "true", "false", "above", "below", "per", "level", "nothing" };

    public YanflyParserVerification Verify(YanflyCanonicalPluginSource source, YanflySyntaxDiscovery discovery)
    {
        if (discovery.Disposition is YanflyDiscoveryDisposition.DocumentationOnly or YanflyDiscoveryDisposition.PluginConfiguration or YanflyDiscoveryDisposition.Duplicate)
            return Make(discovery, YanflyParserVerificationStatus.NotApplicable, [], [], "No runtime parser verification is required.");
        string[] tokens = Regex.Matches(discovery.Syntax, @"[A-Za-z][A-Za-z0-9_]{2,}", RegexOptions.CultureInvariant)
            .Cast<Match>().Select(m => m.Value).Where(t => !Generic.Contains(t)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (tokens.Length == 0) return Make(discovery, YanflyParserVerificationStatus.HelpOnly, tokens, [], "No stable literal tokens were available.");
        string runtime = Regex.Replace(source.SourceText, @"/\*:[A-Za-z-]*.*?\*/", string.Empty, RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        string[] lines = runtime.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        List<int> parser = []; List<int> keyword = [];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i]; int matched = tokens.Count(t => line.Contains(t, StringComparison.OrdinalIgnoreCase)); if (matched == 0) continue;
            bool parserLine = line.Contains("RegExp", StringComparison.OrdinalIgnoreCase) || line.Contains("regex", StringComparison.OrdinalIgnoreCase) || line.Contains("match(", StringComparison.OrdinalIgnoreCase) || line.Contains("processNotetag", StringComparison.OrdinalIgnoreCase) || line.Contains("pluginCommand", StringComparison.OrdinalIgnoreCase) || line.Contains("actionSequence", StringComparison.OrdinalIgnoreCase) || line.Contains("convertEscape", StringComparison.OrdinalIgnoreCase);
            if (parserLine && matched >= Math.Max(1, (tokens.Length + 1) / 2)) parser.Add(i + 1);
            if (matched == tokens.Length || matched >= Math.Min(2, tokens.Length)) keyword.Add(i + 1);
        }
        if (parser.Count > 0) return Make(discovery, YanflyParserVerificationStatus.VerifiedParserExpression, tokens, parser.Take(8).ToArray(), "Tokens occur in runtime parser expressions.");
        if (keyword.Count > 0) return Make(discovery, YanflyParserVerificationStatus.VerifiedKeywordSequence, tokens, keyword.Take(8).ToArray(), "Tokens occur together in runtime source.");
        return Make(discovery, YanflyParserVerificationStatus.HelpOnly, tokens, [], "Conservative source verification found no stable parser evidence.");
    }

    private static YanflyParserVerification Make(YanflySyntaxDiscovery d, YanflyParserVerificationStatus status, IReadOnlyList<string> tokens, IReadOnlyList<int> lines, string reason)
    {
        string material = d.PluginId + "\n" + d.Syntax + "\n" + string.Join(",", lines);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return new(d.DiscoveryId, d.PluginId, status, tokens, lines, hash, reason);
    }
}
