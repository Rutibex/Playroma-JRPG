using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyInstalledPluginIntelligence
{
    private readonly InstalledPluginIntelligenceAnalyzer analyzer;

    public YanflyInstalledPluginIntelligence(
        PluginIntelligenceProfile? profile = null,
        InstalledPluginIntelligenceAnalyzer? analyzer = null)
    {
        Profile = profile ?? new YanflyPluginIntelligenceProfileFactory().CreateEmbedded();
        this.analyzer = analyzer ?? new InstalledPluginIntelligenceAnalyzer();
    }

    public PluginIntelligenceProfile Profile { get; }

    public InstalledPluginAnalysis Analyze(
        IEnumerable<InstalledPluginDescriptor> installedPlugins,
        IEnumerable<string>? scriptPluginIds = null) =>
        analyzer.Analyze(installedPlugins, Profile, scriptPluginIds);

    public IReadOnlyList<PluginFeatureAvailability> EvaluateCatalog(
        InstalledPluginAnalysis analysis,
        IEnumerable<PluginFeatureDefinition>? features = null) =>
        analyzer.EvaluateFeatures(features ?? YanflyGeneratedCatalogProvider.LoadEmbedded().Features, analysis);
}
