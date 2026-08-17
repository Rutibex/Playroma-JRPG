using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyCatalogReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public void WriteAll(YanflyCatalogCompilation compilation, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-compilation.json"), compilation);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-catalog.json"), compilation.Catalog);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-discoveries.json"), compilation.Discoveries);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-parser-verification.json"), compilation.ParserVerifications);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-help-sections.json"), compilation.HelpSections);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-plugin-parameters.json"), compilation.Parameters);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-coverage.json"), compilation.Coverage);
        WriteJson(Path.Combine(outputDirectory, "yanfly-phase2-diagnostics.json"), compilation.Diagnostics);
        File.WriteAllText(Path.Combine(outputDirectory, "yanfly-phase2-coverage.csv"), CreateCoverageCsv(compilation));
        File.WriteAllText(Path.Combine(outputDirectory, "YANFLY_PHASE2_CATALOG_REPORT.md"), CreateMarkdown(compilation));
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static string CreateCoverageCsv(YanflyCatalogCompilation c)
    {
        StringBuilder b = new();
        b.AppendLine("PluginId,Version,HelpSections,Parameters,Discoveries,Features,Verified,RequiresReview");
        foreach (YanflyCatalogPluginCompilation p in c.Plugins)
            b.Append(Csv(p.PluginId)).Append(',').Append(Csv(p.Version)).Append(',')
                .Append(p.HelpSectionCount).Append(',').Append(p.ParameterCount).Append(',')
                .Append(p.DiscoveryCount).Append(',').Append(p.FeatureCount).Append(',')
                .Append(p.VerifiedCount).Append(',').Append(p.ReviewCount).AppendLine();
        return b.ToString();
    }

    private static string CreateMarkdown(YanflyCatalogCompilation c)
    {
        YanflyCatalogCoverageSummary x = c.Coverage;
        StringBuilder b = new();
        b.AppendLine("# Yanfly Phase 2 Catalog Compiler Report").AppendLine();
        b.AppendLine($"Generated from `{c.GeneratedFrom}` at {c.GeneratedAtUtc:O}.").AppendLine();
        b.AppendLine("## Coverage").AppendLine();
        b.AppendLine("| Measurement | Count |").AppendLine("|---|---:|");
        b.AppendLine($"| Canonical plugins | {x.PluginCount} |");
        b.AppendLine($"| Plugins with English help | {x.PluginsWithHelp} |");
        b.AppendLine($"| Help sections indexed | {x.HelpSectionCount} |");
        b.AppendLine($"| Plugin parameters indexed | {x.PluginParameterCount} |");
        b.AppendLine($"| Source discoveries | {x.SourceDiscoveryCount} |");
        b.AppendLine($"| Unique discoveries | {x.UniqueDiscoveryCount} |");
        b.AppendLine($"| Duplicate examples | {x.DuplicateDiscoveryCount} |");
        b.AppendLine($"| Generated catalog features | {x.GeneratedFeatureCount} |");
        b.AppendLine($"| Typed-feature discoveries | {x.TypedFeatureCount} |");
        b.AppendLine($"| Advanced/raw discoveries | {x.AdvancedRawFeatureCount} |");
        b.AppendLine($"| Context-assistant discoveries | {x.ContextAssistantCount} |");
        b.AppendLine($"| Plugin parameters | {x.PluginConfigurationCount} |");
        b.AppendLine($"| Documentation-only discoveries | {x.DocumentationOnlyCount} |");
        b.AppendLine($"| Requires review | {x.RequiresReviewCount} |").AppendLine();
        b.AppendLine("## Parser evidence").AppendLine();
        b.AppendLine($"- Parser-expression verified: **{x.VerifiedParserExpressionCount}**");
        b.AppendLine($"- Keyword-sequence verified: **{x.VerifiedKeywordSequenceCount}**");
        b.AppendLine($"- Help-only conservative matches: **{x.HelpOnlyCount}**").AppendLine();
        b.AppendLine("## Contract").AppendLine();
        b.AppendLine("Every extracted candidate has a final disposition. Documentation markup and changelog references remain in the audit trail but are not emitted as editable features. Source line evidence is stored as line numbers and hashes; YEP JavaScript and help prose are not redistributed.");
        if (c.Diagnostics.Count > 0)
        {
            b.AppendLine().AppendLine("## Diagnostics").AppendLine();
            foreach (YanflyCatalogDiagnostic d in c.Diagnostics)
                b.AppendLine($"- **{d.Severity} {d.Code}** `{d.PluginId ?? "bundle"}`: {d.Message}");
        }
        return b.ToString();
    }

    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions o = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }
}
