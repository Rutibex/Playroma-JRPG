namespace Playroma.JRPG.PluginTags;

public enum InstalledPluginIssueSeverity
{
    Information,
    Warning,
    Error
}

public enum InstalledPluginCompatibilityState
{
    Available,
    InstalledDisabled,
    MissingScript,
    UnknownPlugin,
    UnknownVersion,
    UnverifiedSource,
    MissingDependency,
    DisabledDependency,
    LoadOrderViolation,
    DuplicateManifestEntry,
    Conflict
}

public enum PluginDependencyStrength
{
    Required,
    Recommended
}

public enum PluginFeatureAvailabilityState
{
    Available,
    OwnerPluginDisabled,
    OwnerPluginMissing,
    RequiredPluginDisabled,
    RequiredPluginMissing,
    LoadOrderInvalid,
    PluginConflict,
    VersionUnverified,
    SourceUnverified
}

public sealed record InstalledPluginDescriptor(
    int LoadOrder,
    string PluginId,
    bool IsEnabled,
    string? ScriptPath = null,
    bool ScriptExists = true,
    string? Version = null,
    string? SourceFingerprint = null,
    IReadOnlyList<string>? ImportedAliases = null,
    IReadOnlyDictionary<string, string>? Parameters = null)
{
    public IReadOnlyList<string> EffectiveImportedAliases => ImportedAliases ?? [];
    public IReadOnlyDictionary<string, string> EffectiveParameters =>
        Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record KnownPluginVersion(
    string? Version,
    IReadOnlyList<string> SourceFingerprints);

public sealed record KnownPluginProfile(
    string PluginId,
    string DisplayName,
    string ProviderId,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<KnownPluginVersion>? KnownVersions = null)
{
    public IReadOnlyList<string> EffectiveAliases => Aliases ?? [];
    public IReadOnlyList<KnownPluginVersion> EffectiveKnownVersions => KnownVersions ?? [];
}

public sealed record PluginDependencyRule(
    string PluginId,
    string RequiredPluginId,
    PluginDependencyStrength Strength = PluginDependencyStrength.Required,
    string? Evidence = null);

public sealed record PluginLoadOrderRule(
    string BeforePluginId,
    string AfterPluginId,
    string Reason,
    bool IsRequired = true);

public sealed record PluginConflictRule(
    string ConflictId,
    IReadOnlyList<string> PluginIds,
    string Message,
    bool EnabledOnly = true);

public sealed record PluginIntelligenceProfile(
    string ProviderId,
    IReadOnlyList<KnownPluginProfile> Plugins,
    IReadOnlyList<PluginDependencyRule> Dependencies,
    IReadOnlyList<PluginLoadOrderRule> LoadOrderRules,
    IReadOnlyList<PluginConflictRule> Conflicts)
{
    public static PluginIntelligenceProfile Empty(string providerId) => new(providerId, [], [], [], []);
}

public sealed record InstalledPluginIssue(
    string Code,
    InstalledPluginIssueSeverity Severity,
    string Message,
    string? PluginId = null,
    string? RelatedPluginId = null,
    int? LoadOrder = null);

public sealed record InstalledPluginAnalysisEntry(
    InstalledPluginDescriptor Installed,
    KnownPluginProfile? Profile,
    IReadOnlyList<InstalledPluginCompatibilityState> States,
    IReadOnlyList<InstalledPluginIssue> Issues)
{
    public InstalledPluginCompatibilityState PrimaryState => States.Count == 0
        ? InstalledPluginCompatibilityState.Available
        : States.OrderByDescending(InstalledPluginStateOrdering.Rank).First();

    public bool IsKnown => Profile is not null;
}

public sealed record PluginLoadOrderMove(
    string PluginId,
    int CurrentLoadOrder,
    int SuggestedLoadOrder,
    string Reason);

public sealed record InstalledPluginAnalysis(
    string ProviderId,
    IReadOnlyList<InstalledPluginAnalysisEntry> Entries,
    IReadOnlyList<InstalledPluginIssue> Issues,
    IReadOnlyList<PluginLoadOrderMove> SuggestedMoves,
    IReadOnlyList<string> UnregisteredScriptPluginIds,
    IReadOnlyDictionary<string, InstalledPluginStatus> Statuses)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == InstalledPluginIssueSeverity.Error);
    public bool HasWarnings => Issues.Any(issue => issue.Severity == InstalledPluginIssueSeverity.Warning);

    public InstalledPluginAnalysisEntry? Find(string pluginId) => Entries.FirstOrDefault(entry =>
        string.Equals(entry.Installed.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
}

public sealed record PluginFeatureAvailability(
    PluginFeatureKey Feature,
    PluginFeatureAvailabilityState State,
    string Message,
    IReadOnlyList<string> BlockingPluginIds)
{
    public bool CanAuthor => State is PluginFeatureAvailabilityState.Available
        or PluginFeatureAvailabilityState.VersionUnverified
        or PluginFeatureAvailabilityState.SourceUnverified;
}

internal static class InstalledPluginStateOrdering
{
    public static int Rank(InstalledPluginCompatibilityState state) => state switch
    {
        InstalledPluginCompatibilityState.Conflict => 100,
        InstalledPluginCompatibilityState.MissingDependency => 90,
        InstalledPluginCompatibilityState.DisabledDependency => 80,
        InstalledPluginCompatibilityState.LoadOrderViolation => 70,
        InstalledPluginCompatibilityState.MissingScript => 65,
        InstalledPluginCompatibilityState.DuplicateManifestEntry => 60,
        InstalledPluginCompatibilityState.InstalledDisabled => 50,
        InstalledPluginCompatibilityState.UnverifiedSource => 30,
        InstalledPluginCompatibilityState.UnknownVersion => 20,
        InstalledPluginCompatibilityState.UnknownPlugin => 10,
        _ => 0
    };
}
