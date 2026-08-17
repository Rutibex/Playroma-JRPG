using System.Reflection;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyGeneratedCatalogProvider : IPluginTagProvider
{
    private readonly PluginTagCatalogDocument document;

    public YanflyGeneratedCatalogProvider(PluginTagCatalogDocument document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        Manifest = document.Providers.Single(provider => provider.ProviderId.Equals(YanflyProviderIdentity.ProviderId, StringComparison.OrdinalIgnoreCase));
        // Generated catalogs are data, not executable code. A single malformed discovery should
        // never prevent the Database window from opening. Filter structurally unusable rows here
        // as a final safety net; the source catalog and semantic overrides are still corrected so
        // valid features are not silently lost during normal generation.
        IReadOnlyList<PluginFeatureDefinition> generatedFeatures = document.Features
            .Where(IsUsableGeneratedFeature)
            .ToArray();

        Features = YanflyMapEventFeatureDefinitions.ApplyCorrections(
                YanflyEnemyStateFormulaFeatureDefinitions.ApplyCorrections(
                    YanflyItemCommerceFeatureDefinitions.ApplyCorrections(
                        YanflySkillProgressionFeatureDefinitions.ApplyCorrections(
                            YanflyBattleFeatureDefinitions.ApplyCorrections(generatedFeatures)))))
            .Concat(YanflyBattleFeatureDefinitions.CreatePhaseDefinitions())
            .DistinctBy(feature => feature.Key)
            .ToArray();
    }

    public PluginProviderManifest Manifest { get; }
    public IReadOnlyList<PluginFeatureDefinition> Features { get; }


    private static bool IsUsableGeneratedFeature(PluginFeatureDefinition feature)
    {
        return feature is not null
            && !string.IsNullOrWhiteSpace(feature.ProviderId)
            && !string.IsNullOrWhiteSpace(feature.PluginId)
            && !string.IsNullOrWhiteSpace(feature.FeatureId)
            && !string.IsNullOrWhiteSpace(feature.DisplayName)
            && feature.DocumentKinds is not null
            && feature.Syntax is not null
            && !string.IsNullOrWhiteSpace(feature.Syntax.CanonicalTemplate)
            && feature.Syntax.Patterns is { Count: > 0 }
            && feature.Arguments is not null;
    }

    public static YanflyGeneratedCatalogProvider LoadEmbedded()
    {
        Assembly assembly = typeof(YanflyGeneratedCatalogProvider).Assembly;
        string resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith("yanfly-phase2-catalog.json", StringComparison.OrdinalIgnoreCase));
        using Stream stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Embedded YEP Phase 2 catalog was not found.");
        using StreamReader reader = new(stream);
        PluginTagCatalogDocument document = new PluginTagCatalogSerializer().DeserializeDocument(reader.ReadToEnd());
        return new(document);
    }
}
