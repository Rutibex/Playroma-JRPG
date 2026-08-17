using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyFeatureDefinitionFactory
{
    private static readonly Regex Placeholder = new(@"(?<![A-Za-z0-9_])(?<token>x\.y|x|y|z|n|id|ids|value|values|amount|rate|filename|file|text|string|formula|javascript|code|stat|type)(?![A-Za-z0-9_])", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));

    public PluginFeatureDefinition? Create(YanflyCanonicalPluginSource source, YanflySyntaxDiscovery discovery, IReadOnlyDictionary<string, YanflySyntaxDiscovery> byId, YanflySemanticOverlayDocument overlay)
    {
        YanflySemanticFeatureOverride? fo = FindOverride(discovery, overlay);
        YanflyDiscoveryDisposition disposition = fo?.Disposition ?? discovery.Disposition;
        if (fo?.Ignore == true || disposition is YanflyDiscoveryDisposition.DocumentationOnly or YanflyDiscoveryDisposition.Duplicate or YanflyDiscoveryDisposition.PluginConfiguration or YanflyDiscoveryDisposition.RequiresReview || discovery.Kind == YanflyDiscoveryKind.BlockClosing || discovery.Surface is null) return null;
        YanflySemanticPluginOverride? po = overlay.Plugins.FirstOrDefault(x => x.PluginId.Equals(source.PluginId, StringComparison.OrdinalIgnoreCase));
        YanflySyntaxDiscovery? closing = discovery.PairedDiscoveryId is not null && byId.TryGetValue(discovery.PairedDiscoveryId, out YanflySyntaxDiscovery? c) ? c : null;
        TemplateResult template = fo?.CanonicalTemplate is not null ? FromOverride(fo) : CreateTemplate(discovery.Syntax);
        IReadOnlyList<PluginArgumentDefinition> args = fo?.Arguments is { Count: > 0 } ? fo.Arguments.Select(ToArgument).ToArray() : template.Arguments;
        string display = fo?.DisplayName ?? Display(discovery.Syntax, discovery.Kind);
        string featureId = fo?.FeatureId ?? Slug(source.PluginId.Replace("YEP_", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("KELYEP_", string.Empty, StringComparison.OrdinalIgnoreCase)) + "." + Slug(display) + "." + discovery.DiscoveryId[^8..];
        PluginSyntaxKind kind = fo?.SyntaxKind ?? SyntaxKind(discovery, closing, disposition);
        string? closingTemplate = fo?.ClosingTemplate ?? closing?.Syntax;
        string? closingRegex = fo?.ClosingRegularExpression ?? (closingTemplate is null ? null : Exact(closingTemplate));
        PluginSyntaxDefinition syntax = new(kind, template.Template, [new PluginSyntaxPattern(fo?.OpeningRegularExpression ?? template.Regex, true, true, "Generated from supplied YEP source metadata.")], closingTemplate, closingRegex is null ? null : [new PluginSyntaxPattern(closingRegex)], true);
        IReadOnlySet<PluginDocumentKind> docs = (fo?.DocumentKinds ?? (discovery.DocumentKinds.Count > 0 ? discovery.DocumentKinds : po?.DefaultDocumentKinds) ?? [PluginDocumentKind.Unknown]).ToHashSet();
        HashSet<string> required = new(StringComparer.OrdinalIgnoreCase) { source.PluginId };
        foreach (YanflyDependencyEvidence d in source.InventoryEntry.Dependencies.Where(x => x.Kind == YanflyDependencyKind.Required && x.ResolvedPluginId is not null)) required.Add(d.ResolvedPluginId!);
        foreach (string p in po?.RequiredPlugins ?? []) required.Add(p);
        foreach (string p in fo?.RequiredPlugins ?? []) required.Add(p);
        return new(YanflyProviderIdentity.ProviderId, source.PluginId, featureId, display,
            fo?.Summary ?? (disposition == YanflyDiscoveryDisposition.AdvancedRawFeature ? $"Authors the advanced {display} block. JavaScript is preserved but never executed by the compiler." : $"Authors {display} syntax for {source.InventoryEntry.DisplayName}."),
            fo?.Surface ?? discovery.Surface.Value, docs, syntax, args,
            fo?.Multiplicity ?? Multiplicity(discovery, closing), required.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(), fo?.RecommendedPlugins, fo?.ConflictGroups,
            fo?.SpecializedEditorId ?? po?.SpecializedEditorId,
            fo?.Complexity ?? Complexity(discovery, closing, disposition),
            new PluginVersionRange(source.Version, source.Version, [source.Version], [source.Sha256]),
            fo?.DocumentationUrl ?? po?.DocumentationUrl ?? "https://www.yanfly.moe/wiki/Category:Yanfly_Engine_Plugins",
            fo?.SearchTerms ?? [source.PluginId, source.InventoryEntry.DisplayName, display, discovery.SectionHeading, discovery.Syntax],
            fo?.MatchPriority ?? template.Template.Count(ch => ch is not '{' and not '}') + (closing is null ? 0 : 100) + (args.Count == 0 ? 50 : 0));
    }

    private static YanflySemanticFeatureOverride? FindOverride(YanflySyntaxDiscovery d, YanflySemanticOverlayDocument o) => o.Features.FirstOrDefault(x => x.PluginId.Equals(d.PluginId, StringComparison.OrdinalIgnoreCase) && x.DiscoveryId is not null && x.DiscoveryId.Equals(d.DiscoveryId, StringComparison.OrdinalIgnoreCase)) ?? o.Features.FirstOrDefault(x => x.PluginId.Equals(d.PluginId, StringComparison.OrdinalIgnoreCase) && x.Syntax is not null && YanflySyntaxDiscoveryEngine.NormalizeSyntax(x.Syntax).Equals(d.NormalizedSyntax, StringComparison.OrdinalIgnoreCase));
    private static PluginSyntaxKind SyntaxKind(YanflySyntaxDiscovery d, YanflySyntaxDiscovery? closing, YanflyDiscoveryDisposition disposition) { if (closing is not null) return d.Surface == PluginAuthoringSurface.ActionSequence ? PluginSyntaxKind.OrderedBlock : disposition == YanflyDiscoveryDisposition.AdvancedRawFeature ? PluginSyntaxKind.ScriptBlock : PluginSyntaxKind.PairedBlock; return d.Kind switch { YanflyDiscoveryKind.PluginCommand or YanflyDiscoveryKind.ActionSequence => PluginSyntaxKind.CommandLine, YanflyDiscoveryKind.TextCode => PluginSyntaxKind.TextCode, YanflyDiscoveryKind.ScriptCall or YanflyDiscoveryKind.MoveRouteScript => PluginSyntaxKind.MetadataExpression, _ => d.Syntax.Contains(':') ? PluginSyntaxKind.KeyValue : PluginSyntaxKind.Flag }; }
    private static PluginFeatureComplexity Complexity(YanflySyntaxDiscovery d, YanflySyntaxDiscovery? c, YanflyDiscoveryDisposition disposition) => disposition == YanflyDiscoveryDisposition.AdvancedRawFeature ? PluginFeatureComplexity.AdvancedCode : c is not null ? PluginFeatureComplexity.Structured : d.Kind is YanflyDiscoveryKind.ActionSequence or YanflyDiscoveryKind.ScriptCall or YanflyDiscoveryKind.MoveRouteScript ? PluginFeatureComplexity.Specialized : PluginFeatureComplexity.Simple;
    private static PluginFeatureMultiplicity Multiplicity(YanflySyntaxDiscovery d, YanflySyntaxDiscovery? c) => c is not null ? PluginFeatureMultiplicity.Singleton : d.Kind is YanflyDiscoveryKind.PluginCommand or YanflyDiscoveryKind.ActionSequence or YanflyDiscoveryKind.TextCode or YanflyDiscoveryKind.ScriptCall or YanflyDiscoveryKind.MoveRouteScript ? PluginFeatureMultiplicity.Repeatable : d.Syntax.Contains(" x ", StringComparison.OrdinalIgnoreCase) || d.Syntax.Contains(": x", StringComparison.OrdinalIgnoreCase) ? PluginFeatureMultiplicity.RepeatableByKey : PluginFeatureMultiplicity.Singleton;
    private static PluginArgumentDefinition ToArgument(YanflySemanticArgumentOverride a) => new(a.Name, a.DisplayName, a.Kind, a.IsRequired, a.Minimum, a.Maximum, a.Choices, a.ReferenceKind, a.AllowMultiple, a.Description, a.DefaultValue);
    private static TemplateResult FromOverride(YanflySemanticFeatureOverride o) { string t = o.CanonicalTemplate!; IReadOnlyList<PluginArgumentDefinition> a = o.Arguments?.Select(ToArgument).ToArray() ?? InferFromTemplate(t); return new(t, o.OpeningRegularExpression ?? Pattern(t), a); }
    private static TemplateResult CreateTemplate(string syntax) { List<PluginArgumentDefinition> args = []; int n = 0; string template = Placeholder.Replace(syntax, m => { string token = m.Groups["token"].Value; string name = token.ToLowerInvariant() switch { "filename" or "file" => "fileName", "formula" => "formula", "javascript" or "code" => "code", "stat" => "parameter", "type" => "type", "rate" => "rate", _ => "value" + (++n).ToString(CultureInfo.InvariantCulture) }; while (args.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = "value" + (++n).ToString(CultureInfo.InvariantCulture); args.Add(InferArg(name, token)); return "{" + name + "}"; }); return new(template, Pattern(template), args); }
    private static IReadOnlyList<PluginArgumentDefinition> InferFromTemplate(string t) => Regex.Matches(t, @"\{(?<n>[A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant).Cast<Match>().Select(m => m.Groups["n"].Value).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => InferArg(n, n)).ToArray();
    private static PluginArgumentDefinition InferArg(string name, string token) { PluginArgumentKind kind = token.ToLowerInvariant() switch { "x" or "y" or "z" or "n" or "id" => PluginArgumentKind.Integer, "x.y" or "rate" => PluginArgumentKind.Decimal, "filename" or "file" => PluginArgumentKind.AssetFile, "formula" => PluginArgumentKind.Formula, "javascript" or "code" => PluginArgumentKind.JavaScriptExpression, "stat" => PluginArgumentKind.ParameterSelector, "type" => PluginArgumentKind.Enumeration, _ => PluginArgumentKind.Text }; return new(name, Human(name), kind, true, Description: "Argument inferred from the supplied help syntax."); }
    private static string Pattern(string template) { StringBuilder b = new("^"); int p = 0; foreach (Match m in Regex.Matches(template, @"\{(?<n>[A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)) { b.Append(Escape(template[p..m.Index])); b.Append("(?<").Append(m.Groups["n"].Value).Append(">.+?)"); p = m.Index + m.Length; } b.Append(Escape(template[p..])).Append('$'); return b.ToString(); }
    private static string Exact(string value) => "^" + Escape(value) + "$";
    private static string Escape(string value) => Regex.Replace(Regex.Escape(value), @"(?:\\ )+", @"\s+", RegexOptions.CultureInvariant);
    private static string Display(string syntax, YanflyDiscoveryKind kind) { if (kind is YanflyDiscoveryKind.PluginCommand or YanflyDiscoveryKind.ActionSequence or YanflyDiscoveryKind.ScriptCall or YanflyDiscoveryKind.MoveRouteScript) return Human(syntax.Split(new[] { ' ', ':', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? syntax); string v = syntax.Trim().TrimStart('<').TrimEnd('>').TrimStart('/').Trim(); v = Placeholder.Replace(v, string.Empty); v = Regex.Replace(v, @"[^A-Za-z0-9]+", " ", RegexOptions.CultureInvariant).Trim(); return v.Length == 0 ? "YEP Syntax" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(v.ToLowerInvariant()); }
    private static string Human(string value) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Regex.Replace(value, @"(?<=[a-z0-9])(?=[A-Z])|[_\-.]+", " ", RegexOptions.CultureInvariant).Trim().ToLowerInvariant());
    private static string Slug(string value) { string s = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-", RegexOptions.CultureInvariant).Trim('-'); return s.Length == 0 ? "feature" : s; }
    private sealed record TemplateResult(string Template, string Regex, IReadOnlyList<PluginArgumentDefinition> Arguments);
}
