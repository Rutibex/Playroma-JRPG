namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyCatalogGoldenVerifier
{
    public YanflyCatalogVerificationResult Verify(
        YanflyCatalogCompilation expected,
        YanflyCatalogCompilation actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        List<YanflyCatalogVerificationDifference> differences = [];
        Compare(differences, "YEP2V001", "Inventory fingerprint", expected.InventoryFingerprint, actual.InventoryFingerprint);
        Compare(differences, "YEP2V002", "Plugin count", expected.Coverage.PluginCount, actual.Coverage.PluginCount);
        Compare(differences, "YEP2V003", "Discovery count", expected.Coverage.SourceDiscoveryCount, actual.Coverage.SourceDiscoveryCount);
        Compare(differences, "YEP2V004", "Generated feature count", expected.Coverage.GeneratedFeatureCount, actual.Coverage.GeneratedFeatureCount);
        Compare(differences, "YEP2V005", "Requires-review count", expected.Coverage.RequiresReviewCount, actual.Coverage.RequiresReviewCount);

        Dictionary<string, Playroma.JRPG.PluginTags.PluginFeatureDefinition> expectedFeatures = expected.Catalog.Features.ToDictionary(f => f.Key.ToString(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Playroma.JRPG.PluginTags.PluginFeatureDefinition> actualFeatures = actual.Catalog.Features.ToDictionary(f => f.Key.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach ((string key, Playroma.JRPG.PluginTags.PluginFeatureDefinition feature) in expectedFeatures)
        {
            if (!actualFeatures.TryGetValue(key, out Playroma.JRPG.PluginTags.PluginFeatureDefinition? current))
            {
                differences.Add(new("YEP2V006", "Expected catalog feature is missing.", feature.PluginId, feature.FeatureId));
                continue;
            }
            if (!string.Equals(feature.Syntax.CanonicalTemplate, current.Syntax.CanonicalTemplate, StringComparison.Ordinal))
                differences.Add(new("YEP2V007", "Canonical template changed.", feature.PluginId, feature.FeatureId));
            if (feature.Surface != current.Surface)
                differences.Add(new("YEP2V008", "Authoring surface changed.", feature.PluginId, feature.FeatureId));
        }
        foreach ((string key, Playroma.JRPG.PluginTags.PluginFeatureDefinition feature) in actualFeatures)
            if (!expectedFeatures.ContainsKey(key)) differences.Add(new("YEP2V009", "Unexpected catalog feature was generated.", feature.PluginId, feature.FeatureId));

        HashSet<string> expectedDiscoveries = expected.Discoveries.Select(d => d.DiscoveryId + ":" + d.Disposition).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> actualDiscoveries = actual.Discoveries.Select(d => d.DiscoveryId + ":" + d.Disposition).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string missing in expectedDiscoveries.Except(actualDiscoveries, StringComparer.OrdinalIgnoreCase))
            differences.Add(new("YEP2V010", "Discovery classification changed or disappeared: " + missing));
        foreach (string added in actualDiscoveries.Except(expectedDiscoveries, StringComparer.OrdinalIgnoreCase))
            differences.Add(new("YEP2V011", "New discovery classification appeared: " + added));
        return new(differences);
    }

    private static void Compare<T>(ICollection<YanflyCatalogVerificationDifference> target, string code, string name, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            target.Add(new(code, $"{name} differs. Expected '{expected}', actual '{actual}'."));
    }
}
