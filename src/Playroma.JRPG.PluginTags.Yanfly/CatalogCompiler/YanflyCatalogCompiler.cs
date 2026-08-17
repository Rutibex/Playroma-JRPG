using System.Security.Cryptography;
using System.Text;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyCatalogCompiler
{
    public const int SchemaVersion = 1;

    private readonly YanflyBundleSourceReader sourceReader = new();
    private readonly YanflyHelpDocumentParser helpParser = new();
    private readonly YanflySyntaxDiscoveryEngine discoveryEngine = new();
    private readonly YanflyParserPatternVerifier verifier = new();
    private readonly YanflyFeatureDefinitionFactory featureFactory = new();

    public YanflyCatalogCompilation Compile(
        string inputPath,
        YanflyBundleInventory? inventory = null,
        YanflySemanticOverlayDocument? overlay = null,
        DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        inventory ??= YanflyInventorySerializer.LoadEmbeddedPhase0Inventory();
        overlay ??= YanflySemanticOverlaySerializer.LoadEmbedded();

        IReadOnlyList<YanflyCanonicalPluginSource> sources = sourceReader.ReadCanonicalSources(inputPath, inventory);
        List<YanflyCatalogDiagnostic> diagnostics = [];
        List<YanflySyntaxDiscovery> allDiscoveries = [];
        List<YanflyParserVerification> allVerifications = [];
        List<YanflyHelpSectionIndexEntry> allSections = [];
        List<YanflyPluginParameterIndexEntry> allParameters = [];
        List<PluginFeatureDefinition> allFeatures = [];
        List<YanflyCatalogPluginCompilation> pluginResults = [];

        foreach (YanflyCanonicalPluginSource source in sources)
        {
            YanflyHelpDocument help = helpParser.Parse(source);
            IReadOnlyList<YanflySyntaxDiscovery> discoveries = discoveryEngine.Discover(source, help);
            Dictionary<string, YanflySyntaxDiscovery> byId = discoveries.ToDictionary(d => d.DiscoveryId, StringComparer.OrdinalIgnoreCase);
            List<YanflyParserVerification> verifications = discoveries.Select(d => verifier.Verify(source, d)).ToList();
            List<PluginFeatureDefinition> features = [];
            foreach (YanflySyntaxDiscovery discovery in discoveries)
            {
                try
                {
                    PluginFeatureDefinition? feature = featureFactory.Create(source, discovery, byId, overlay);
                    if (feature is not null) features.Add(feature);
                }
                catch (Exception exception)
                {
                    diagnostics.Add(new("YEP2-0001", YanflyCatalogDiagnosticSeverity.Error,
                        "Feature generation failed: " + exception.Message, source.PluginId, discovery.DiscoveryId));
                }
            }

            allDiscoveries.AddRange(discoveries);
            allVerifications.AddRange(verifications);
            allFeatures.AddRange(features);
            allSections.AddRange(help.Sections.Select(s => new YanflyHelpSectionIndexEntry(source.PluginId, s.Heading, s.StartLine, s.EndLine, s.Surface, s.DocumentKinds)));
            allParameters.AddRange(help.Parameters.Select(p => new YanflyPluginParameterIndexEntry(source.PluginId, p)));

            int reviewCount = discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.RequiresReview);
            if (reviewCount > 0)
                diagnostics.Add(new("YEP2-0002", YanflyCatalogDiagnosticSeverity.Error,
                    $"{reviewCount} source discoveries remain unclassified.", source.PluginId));
            if (!source.InventoryEntry.HasEnglishHelp)
                diagnostics.Add(new("YEP2-0003", YanflyCatalogDiagnosticSeverity.Info,
                    "Plugin has no English @help block; Phase 0 runtime/configuration coverage remains authoritative.", source.PluginId));

            pluginResults.Add(new(
                source.PluginId, source.FileName, source.Version, source.Sha256,
                help.Sections.Count, help.Parameters.Count, discoveries.Count, features.Count,
                verifications.Count(v => v.Status is YanflyParserVerificationStatus.VerifiedParserExpression or YanflyParserVerificationStatus.VerifiedKeywordSequence),
                reviewCount,
                CountBy(discoveries, d => d.Kind),
                CountBy(discoveries, d => d.Disposition)));
        }

        List<PluginFeatureDefinition> uniqueFeatures = [];
        HashSet<PluginFeatureKey> featureKeys = [];
        foreach (PluginFeatureDefinition feature in allFeatures
                     .OrderBy(f => f.PluginId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => f.FeatureId, StringComparer.OrdinalIgnoreCase))
        {
            if (featureKeys.Add(feature.Key)) uniqueFeatures.Add(feature);
            else diagnostics.Add(new("YEP2-0004", YanflyCatalogDiagnosticSeverity.Error,
                "Duplicate generated feature key: " + feature.Key, feature.PluginId));
        }

        PluginTagCatalogBuilder builder = new();
        PluginProviderManifest manifest = YanflyProviderIdentity.CreateManifest(overlay.CatalogVersion);
        builder.AddProvider(manifest);
        foreach (PluginFeatureDefinition feature in uniqueFeatures)
        {
            try { builder.AddFeature(feature); }
            catch (Exception exception)
            {
                diagnostics.Add(new("YEP2-0005", YanflyCatalogDiagnosticSeverity.Error,
                    "Generated feature did not satisfy the platform-neutral catalog contract: " + exception.Message,
                    feature.PluginId, feature.FeatureId));
            }
        }
        PluginTagCatalog catalog = builder.Build();
        PluginTagCatalogDocument catalogDocument = new(
            PluginTagCatalogSerializer.CurrentSchemaVersion,
            [manifest], catalog.Features);

        YanflyCatalogCoverageSummary coverage = BuildCoverage(
            sources, allSections, allParameters, allDiscoveries, catalog.Features, allVerifications);
        if (coverage.UniqueDiscoveryCount + coverage.DuplicateDiscoveryCount != coverage.SourceDiscoveryCount)
            diagnostics.Add(new("YEP2-0006", YanflyCatalogDiagnosticSeverity.Error,
                "Discovery accounting did not reconcile."));
        if (coverage.RequiresReviewCount != 0)
            diagnostics.Add(new("YEP2-0007", YanflyCatalogDiagnosticSeverity.Error,
                "Coverage contract requires every discovery to have a final disposition."));

        return new(
            SchemaVersion,
            overlay.CatalogVersion,
            generatedAtUtc ?? DateTimeOffset.UtcNow,
            Path.GetFileName(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ComputeInventoryFingerprint(inventory),
            catalogDocument,
            coverage,
            pluginResults.OrderBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase).ToArray(),
            allSections.OrderBy(s => s.PluginId, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.StartLine).ToArray(),
            allParameters.OrderBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Parameter.HeaderLine).ToArray(),
            allDiscoveries.OrderBy(d => d.PluginId, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.HelpLine).ThenBy(d => d.Syntax, StringComparer.OrdinalIgnoreCase).ToArray(),
            allVerifications.OrderBy(v => v.PluginId, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.DiscoveryId, StringComparer.OrdinalIgnoreCase).ToArray(),
            diagnostics.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.PluginId, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static YanflyCatalogCoverageSummary BuildCoverage(
        IReadOnlyList<YanflyCanonicalPluginSource> sources,
        IReadOnlyList<YanflyHelpSectionIndexEntry> sections,
        IReadOnlyList<YanflyPluginParameterIndexEntry> parameters,
        IReadOnlyList<YanflySyntaxDiscovery> discoveries,
        IReadOnlyList<PluginFeatureDefinition> features,
        IReadOnlyList<YanflyParserVerification> verifications)
    {
        return new(
            sources.Count,
            sources.Count(s => s.InventoryEntry.HasEnglishHelp),
            sections.Count,
            parameters.Count,
            discoveries.Count,
            discoveries.Count(d => d.Disposition != YanflyDiscoveryDisposition.Duplicate),
            discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.Duplicate),
            features.Count,
            discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.TypedFeature),
            discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.AdvancedRawFeature),
            discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.ContextAssistant),
            discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.PluginConfiguration),
            discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.DocumentationOnly),
            discoveries.Count(d => d.Disposition == YanflyDiscoveryDisposition.RequiresReview),
            verifications.Count(v => v.Status == YanflyParserVerificationStatus.VerifiedParserExpression),
            verifications.Count(v => v.Status == YanflyParserVerificationStatus.VerifiedKeywordSequence),
            verifications.Count(v => v.Status == YanflyParserVerificationStatus.HelpOnly),
            CountBy(discoveries, d => d.Kind),
            CountBy(features, f => f.Surface));
    }

    private static IReadOnlyDictionary<TKey, int> CountBy<T, TKey>(IEnumerable<T> values, Func<T, TKey> selector)
        where TKey : notnull => values.GroupBy(selector).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());

    private static string ComputeInventoryFingerprint(YanflyBundleInventory inventory)
    {
        string material = string.Join("\n", inventory.Plugins.OrderBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase).Select(p => p.PluginId + ":" + p.CanonicalSha256));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }
}
