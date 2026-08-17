namespace Playroma.JRPG.PluginTags.Yanfly;

public enum YanflySourceFamily
{
    YanflyEnginePlugins,
    KelDragonBones
}

public enum YanflyAuthoringSurface
{
    DatabaseNote,
    MapNote,
    TilesetNote,
    EventNote,
    EventPageComment,
    PluginCommand,
    MoveRouteScript,
    ActionSequence,
    MessageTextCode,
    SwitchNameMetadata,
    VariableNameMetadata,
    ScriptCall,
    LunaticMode,
    PluginParameter
}

public enum YanflyCoverageMode
{
    TypedCatalog,
    SpecializedEditor,
    ContextAssistant,
    PluginConfiguration,
    RuntimeValidationOnly
}

public enum YanflyDuplicateKind
{
    Unique,
    ExactDuplicate,
    ConflictingDuplicate
}

public enum YanflyResolutionReason
{
    SoleOccurrence,
    ExactDuplicate,
    HighestVersion,
    PreferredCategorizedCopy,
    ManualOverride
}

public enum YanflyDependencyKind
{
    Required,
    OptionalFeature
}

public enum YanflyEvidenceLocation
{
    PluginDescription,
    Introduction,
    HelpBody,
    ManualOverride
}

public enum YanflyAuditSeverity
{
    Info,
    Warning,
    Error
}

public sealed record YanflyBundleOccurrence(
    string RelativePath,
    string FileName,
    string Category,
    long SizeBytes,
    string Sha256,
    string? Version,
    bool IsFreeCollectionCopy);

public sealed record YanflyDependencyEvidence(
    string RequestedPluginId,
    string? ResolvedPluginId,
    YanflyDependencyKind Kind,
    YanflyEvidenceLocation Location,
    string Evidence,
    bool IsResolved);

public sealed record YanflyPluginInventoryEntry(
    string PluginId,
    string FileName,
    string DisplayName,
    YanflySourceFamily SourceFamily,
    string? Version,
    string Category,
    string CanonicalPath,
    string CanonicalSha256,
    YanflyDuplicateKind DuplicateKind,
    YanflyResolutionReason ResolutionReason,
    IReadOnlyList<YanflyBundleOccurrence> Occurrences,
    IReadOnlyList<string> ImportedAliases,
    IReadOnlyList<YanflyAuthoringSurface> AuthoringSurfaces,
    IReadOnlyList<string> DeclaredContexts,
    IReadOnlyList<YanflyCoverageMode> CoverageModes,
    string PlannedImplementationPhase,
    int HelpAngleBracketExampleCount,
    bool HasEnglishHelp,
    bool HasPluginParameters,
    IReadOnlyList<YanflyDependencyEvidence> Dependencies);

public sealed record YanflyBundleSummary(
    int TotalJavaScriptFiles,
    int IgnoredSeparatorFiles,
    int PluginOccurrences,
    int UniquePlugins,
    int YanflyPlugins,
    int IntegrationPlugins,
    int DuplicateFileNameGroups,
    int ExactDuplicateGroups,
    int ConflictingDuplicateGroups,
    IReadOnlyDictionary<string, int> CategoryCounts,
    IReadOnlyDictionary<YanflyAuthoringSurface, int> SurfaceCounts,
    IReadOnlyDictionary<YanflyCoverageMode, int> CoverageModeCounts);

public sealed record YanflyBundleInventory(
    int SchemaVersion,
    string ProviderId,
    string RuntimeFamily,
    string GeneratedFrom,
    DateTimeOffset GeneratedAtUtc,
    YanflyBundleSummary Summary,
    IReadOnlyList<YanflyPluginInventoryEntry> Plugins);

public sealed record YanflyBundleDiagnostic(
    string Code,
    YanflyAuditSeverity Severity,
    string Message,
    string? PluginId = null,
    string? RelativePath = null);

public sealed record YanflyBundleAuditResult(
    YanflyBundleInventory Inventory,
    IReadOnlyList<YanflyBundleDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(diagnostic => diagnostic.Severity != YanflyAuditSeverity.Error);
}

public sealed record YanflyVerificationDifference(
    string Code,
    string Message,
    string? PluginId = null);

public sealed record YanflyVerificationResult(
    IReadOnlyList<YanflyVerificationDifference> Differences)
{
    public bool Success => Differences.Count == 0;
}
