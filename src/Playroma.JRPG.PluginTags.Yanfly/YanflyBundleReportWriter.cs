using System.Text;
using System.Text.Json;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyBundleReportWriter
{
    public void WriteAll(YanflyBundleAuditResult result, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        YanflyInventorySerializer.Save(result.Inventory, Path.Combine(outputDirectory, "yanfly-phase0-inventory.json"));
        WriteJson(result.Diagnostics, Path.Combine(outputDirectory, "yanfly-phase0-diagnostics.json"));
        WriteJson(CreateConstitution(), Path.Combine(outputDirectory, "yanfly-phase0-constitution.json"));
        WriteJson(CreateCoverageContract(result.Inventory), Path.Combine(outputDirectory, "yanfly-phase0-coverage-contract.json"));
        WriteJson(CreateDuplicateRecords(result.Inventory), Path.Combine(outputDirectory, "yanfly-phase0-duplicate-resolutions.json"));
        WriteJson(CreateDependencyGraph(result.Inventory), Path.Combine(outputDirectory, "yanfly-phase0-dependency-graph.json"));
        File.WriteAllText(Path.Combine(outputDirectory, "yanfly-phase0-coverage.csv"), CreateCoverageCsv(result.Inventory), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outputDirectory, "YANFLY_PHASE0_BUNDLE_AUDIT.md"), CreateMarkdown(result), new UTF8Encoding(false));
    }

    public string CreateMarkdown(YanflyBundleAuditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        YanflyBundleSummary summary = result.Inventory.Summary;
        StringBuilder text = new();
        text.AppendLine("# Yanfly Plugin Tags Phase 0 Bundle Audit");
        text.AppendLine();
        text.AppendLine($"Generated from `{result.Inventory.GeneratedFrom}` at {result.Inventory.GeneratedAtUtc:O}.");
        text.AppendLine();
        text.AppendLine("## Normalized inventory");
        text.AppendLine();
        text.AppendLine("| Measure | Count |");
        text.AppendLine("|---|---:|");
        text.AppendLine($"| JavaScript files encountered | {summary.TotalJavaScriptFiles} |");
        text.AppendLine($"| Separator files ignored | {summary.IgnoredSeparatorFiles} |");
        text.AppendLine($"| Plugin occurrences | {summary.PluginOccurrences} |");
        text.AppendLine($"| Unique plugins | {summary.UniquePlugins} |");
        text.AppendLine($"| YEP plugins | {summary.YanflyPlugins} |");
        text.AppendLine($"| Integration plugins | {summary.IntegrationPlugins} |");
        text.AppendLine($"| Duplicate filename groups | {summary.DuplicateFileNameGroups} |");
        text.AppendLine($"| Exact duplicate groups | {summary.ExactDuplicateGroups} |");
        text.AppendLine($"| Conflicting duplicate groups | {summary.ConflictingDuplicateGroups} |");
        text.AppendLine();
        text.AppendLine("## Conflicting duplicate resolutions");
        text.AppendLine();
        foreach (YanflyPluginInventoryEntry plugin in result.Inventory.Plugins.Where(plugin => plugin.DuplicateKind == YanflyDuplicateKind.ConflictingDuplicate))
        {
            text.AppendLine($"- **{plugin.FileName}**: `{plugin.ResolutionReason}` selected `{plugin.CanonicalPath}` at version `{plugin.Version}`.");
        }

        text.AppendLine();
        text.AppendLine("## Authoring surface signals");
        text.AppendLine();
        text.AppendLine("| Surface | Plugins |");
        text.AppendLine("|---|---:|");
        foreach ((YanflyAuthoringSurface surface, int count) in summary.SurfaceCounts.OrderBy(pair => pair.Key))
            text.AppendLine($"| {surface} | {count} |");

        text.AppendLine();
        text.AppendLine("## Coverage contract");
        text.AppendLine();
        foreach (YanflyConstitutionPromise promise in YanflyPhase0Constitution.Promises)
            text.AppendLine($"- **{promise.Title}:** {promise.Guarantee}");

        text.AppendLine();
        text.AppendLine("## Diagnostics");
        text.AppendLine();
        if (result.Diagnostics.Count == 0)
        {
            text.AppendLine("No diagnostics were produced.");
        }
        else
        {
            foreach (YanflyBundleDiagnostic diagnostic in result.Diagnostics)
            {
                string subject = diagnostic.PluginId ?? diagnostic.RelativePath ?? "bundle";
                text.AppendLine($"- `{diagnostic.Severity}` `{diagnostic.Code}` **{subject}:** {diagnostic.Message}");
            }
        }

        text.AppendLine();
        text.AppendLine("## Licensing boundary");
        text.AppendLine();
        text.AppendLine("This report contains independently derived metadata, short identifiers, versions, hashes, and classification signals. It does not contain or redistribute YEP plugin source or full help text.");
        return text.ToString();
    }


    private static object CreateConstitution() => new
    {
        schemaVersion = YanflyPhase0Constitution.SchemaVersion,
        providerId = YanflyPhase0Constitution.ProviderId,
        runtimeFamily = YanflyPhase0Constitution.RuntimeFamily,
        promises = YanflyPhase0Constitution.Promises,
        phase0ExitConditions = YanflyPhase0Constitution.Phase0ExitConditions,
        prohibitedRepositoryPatterns = YanflyPhase0Constitution.ProhibitedRepositoryPatterns
    };

    private static object CreateCoverageContract(YanflyBundleInventory inventory) => new
    {
        schemaVersion = inventory.SchemaVersion,
        providerId = inventory.ProviderId,
        classificationStatus = "Phase 0 evidence-backed baseline; semantic tag definitions are Phase 2.",
        authoringSurfaces = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(YanflyAuthoringSurface.DatabaseNote)] = "Database-record note fields. Specific record kinds are carried in declaredContexts.",
            [nameof(YanflyAuthoringSurface.MapNote)] = "Map note property.",
            [nameof(YanflyAuthoringSurface.TilesetNote)] = "Tileset database note property.",
            [nameof(YanflyAuthoringSurface.EventNote)] = "Event-level note property.",
            [nameof(YanflyAuthoringSurface.EventPageComment)] = "Event commands 108 and 408 on a specific event page.",
            [nameof(YanflyAuthoringSurface.PluginCommand)] = "Event command 356 or equivalent structured command insertion.",
            [nameof(YanflyAuthoringSurface.MoveRouteScript)] = "Movement route script commands and helpers.",
            [nameof(YanflyAuthoringSurface.ActionSequence)] = "Ordered Battle Engine action-sequence blocks.",
            [nameof(YanflyAuthoringSurface.MessageTextCode)] = "Caret-position insertion in message-capable text fields.",
            [nameof(YanflyAuthoringSurface.SwitchNameMetadata)] = "Metadata encoded in switch display names.",
            [nameof(YanflyAuthoringSurface.VariableNameMetadata)] = "Metadata encoded in variable display names.",
            [nameof(YanflyAuthoringSurface.ScriptCall)] = "Reference-assisted event script calls.",
            [nameof(YanflyAuthoringSurface.LunaticMode)] = "Advanced JavaScript or formula blocks; never executed in the editor process.",
            [nameof(YanflyAuthoringSurface.PluginParameter)] = "Plugin Manager configuration and validation."
        },
        coverageModes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(YanflyCoverageMode.TypedCatalog)] = "A catalog definition plus reusable typed controls is expected.",
            [nameof(YanflyCoverageMode.SpecializedEditor)] = "The feature requires an ordered or domain-specific editor.",
            [nameof(YanflyCoverageMode.ContextAssistant)] = "The syntax is inserted from the editor surface where it executes.",
            [nameof(YanflyCoverageMode.PluginConfiguration)] = "The plugin is represented in Plugin Manager intelligence.",
            [nameof(YanflyCoverageMode.RuntimeValidationOnly)] = "No authoring syntax was detected; presence, version, and compatibility are still validated."
        },
        completionRules = new[]
        {
            "Every normalized plugin must have one or more coverage modes.",
            "Every conflicting duplicate must have an explicit resolution.",
            "Every dependency edge must include evidence and a source location.",
            "Unknown or unresolved dependencies remain visible; they are not silently discarded.",
            "Source hashes may be distributed; plugin JavaScript and full help text may not.",
            "Phase 0 declaredContexts are extraction signals and must be semantically reviewed before tag generation."
        },
        goldenCounts = inventory.Summary
    };

    private static object CreateDuplicateRecords(YanflyBundleInventory inventory) => new
    {
        schemaVersion = inventory.SchemaVersion,
        duplicateGroups = inventory.Plugins
            .Where(plugin => plugin.Occurrences.Count > 1)
            .Select(plugin => new
            {
                plugin.PluginId,
                plugin.FileName,
                plugin.DuplicateKind,
                plugin.ResolutionReason,
                plugin.CanonicalPath,
                plugin.CanonicalSha256,
                occurrences = plugin.Occurrences.Select(occurrence => new
                {
                    occurrence.RelativePath,
                    occurrence.Version,
                    occurrence.Sha256,
                    occurrence.SizeBytes
                })
            })
    };

    private static object CreateDependencyGraph(YanflyBundleInventory inventory) => new
    {
        schemaVersion = inventory.SchemaVersion,
        nodes = inventory.Plugins.Select(plugin => plugin.PluginId),
        edges = inventory.Plugins.SelectMany(plugin => plugin.Dependencies.Select(dependency => new
        {
            source = plugin.PluginId,
            requested = dependency.RequestedPluginId,
            target = dependency.ResolvedPluginId,
            dependency.Kind,
            dependency.Location,
            dependency.Evidence,
            dependency.IsResolved
        }))
    };

    private static string CreateCoverageCsv(YanflyBundleInventory inventory)
    {
        StringBuilder csv = new();
        csv.AppendLine("PluginId,Version,Category,DuplicateKind,ResolutionReason,AuthoringSurfaces,DeclaredContexts,CoverageModes,PlannedImplementationPhase,RequiredDependencies,OptionalDependencies,CanonicalSha256");
        foreach (YanflyPluginInventoryEntry plugin in inventory.Plugins)
        {
            string required = string.Join(";", plugin.Dependencies.Where(dependency => dependency.Kind == YanflyDependencyKind.Required).Select(dependency => dependency.ResolvedPluginId ?? dependency.RequestedPluginId));
            string optional = string.Join(";", plugin.Dependencies.Where(dependency => dependency.Kind == YanflyDependencyKind.OptionalFeature).Select(dependency => dependency.ResolvedPluginId ?? dependency.RequestedPluginId));
            string[] cells =
            [
                plugin.PluginId,
                plugin.Version ?? string.Empty,
                plugin.Category,
                plugin.DuplicateKind.ToString(),
                plugin.ResolutionReason.ToString(),
                string.Join(";", plugin.AuthoringSurfaces),
                string.Join(";", plugin.DeclaredContexts),
                string.Join(";", plugin.CoverageModes),
                plugin.PlannedImplementationPhase,
                required,
                optional,
                plugin.CanonicalSha256
            ];
            csv.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
        }

        return csv.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static void WriteJson<T>(T value, string path)
    {
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, value, YanflyInventorySerializer.JsonOptions);
    }
}
