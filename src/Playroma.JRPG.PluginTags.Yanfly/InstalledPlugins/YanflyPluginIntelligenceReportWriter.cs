using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyPluginIntelligenceReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public void Write(InstalledPluginAnalysis analysis, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "yanfly-phase3-installed-analysis.json"),
            JsonSerializer.Serialize(analysis, JsonOptions));
        File.WriteAllText(Path.Combine(outputDirectory, "YANFLY_PHASE3_INSTALLED_REPORT.md"), BuildMarkdown(analysis));
    }

    public string BuildMarkdown(InstalledPluginAnalysis analysis)
    {
        StringBuilder text = new();
        text.AppendLine("# Yanfly Installed-Plugin Intelligence Report");
        text.AppendLine();
        text.AppendLine($"- Manifest entries: {analysis.Entries.Count}");
        text.AppendLine($"- Enabled: {analysis.Entries.Count(entry => entry.Installed.IsEnabled)}");
        text.AppendLine($"- Errors: {analysis.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Error)}");
        text.AppendLine($"- Warnings: {analysis.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Warning)}");
        text.AppendLine($"- Unregistered scripts: {analysis.UnregisteredScriptPluginIds.Count}");
        text.AppendLine();
        text.AppendLine("## Plugins");
        text.AppendLine();
        text.AppendLine("| Order | Plugin | Enabled | Version | State |");
        text.AppendLine("|---:|---|:---:|---|---|");
        foreach (InstalledPluginAnalysisEntry entry in analysis.Entries)
            text.AppendLine($"| {entry.Installed.LoadOrder} | `{entry.Installed.PluginId}` | {(entry.Installed.IsEnabled ? "Yes" : "No")} | {entry.Installed.Version ?? "Unknown"} | {entry.PrimaryState} |");
        text.AppendLine();
        text.AppendLine("## Diagnostics");
        text.AppendLine();
        if (analysis.Issues.Count == 0) text.AppendLine("No diagnostics.");
        foreach (InstalledPluginIssue issue in analysis.Issues)
            text.AppendLine($"- **{issue.Severity} {issue.Code}:** {issue.Message}");
        return text.ToString();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
