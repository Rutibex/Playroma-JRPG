namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed class YanflyGoldenManifestVerifier
{
    public YanflyVerificationResult Verify(YanflyBundleInventory expected, YanflyBundleInventory actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        List<YanflyVerificationDifference> differences = [];

        Compare("YEPV001", "Unique plugin count", expected.Summary.UniquePlugins, actual.Summary.UniquePlugins, differences);
        Compare("YEPV002", "Plugin occurrence count", expected.Summary.PluginOccurrences, actual.Summary.PluginOccurrences, differences);
        Compare("YEPV003", "Conflicting duplicate count", expected.Summary.ConflictingDuplicateGroups, actual.Summary.ConflictingDuplicateGroups, differences);

        Dictionary<string, YanflyPluginInventoryEntry> expectedById = expected.Plugins.ToDictionary(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, YanflyPluginInventoryEntry> actualById = actual.Plugins.ToDictionary(plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase);

        foreach (string missing in expectedById.Keys.Except(actualById.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            differences.Add(new("YEPV004", "Expected plugin is missing from the audited bundle.", missing));
        foreach (string unexpected in actualById.Keys.Except(expectedById.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            differences.Add(new("YEPV005", "Audited bundle contains a plugin absent from the golden inventory.", unexpected));

        foreach (string pluginId in expectedById.Keys.Intersect(actualById.Keys, StringComparer.OrdinalIgnoreCase))
        {
            YanflyPluginInventoryEntry left = expectedById[pluginId];
            YanflyPluginInventoryEntry right = actualById[pluginId];
            if (!StringComparer.OrdinalIgnoreCase.Equals(left.Version ?? string.Empty, right.Version ?? string.Empty))
                differences.Add(new("YEPV006", $"Version changed from '{left.Version}' to '{right.Version}'.", pluginId));
            if (!StringComparer.OrdinalIgnoreCase.Equals(left.CanonicalSha256, right.CanonicalSha256))
                differences.Add(new("YEPV007", "Canonical source fingerprint changed.", pluginId));
            if (left.DuplicateKind != right.DuplicateKind)
                differences.Add(new("YEPV008", $"Duplicate classification changed from {left.DuplicateKind} to {right.DuplicateKind}.", pluginId));
            if (left.Occurrences.Count != right.Occurrences.Count)
                differences.Add(new("YEPV009", $"Occurrence count changed from {left.Occurrences.Count} to {right.Occurrences.Count}.", pluginId));
        }

        return new YanflyVerificationResult(differences);
    }

    private static void Compare(
        string code,
        string label,
        int expected,
        int actual,
        ICollection<YanflyVerificationDifference> differences)
    {
        if (expected != actual)
            differences.Add(new(code, $"{label} changed from {expected} to {actual}."));
    }
}
