namespace Playroma.JRPG.PluginTags.Yanfly;

internal sealed record YanflySourceAnalysis(
    string FileName,
    string PluginId,
    string DisplayName,
    YanflySourceFamily SourceFamily,
    string? Version,
    string RelativePath,
    string Category,
    long SizeBytes,
    string Sha256,
    bool IsFreeCollectionCopy,
    bool HasEnglishHelp,
    bool HasPluginParameters,
    int HelpAngleBracketExampleCount,
    IReadOnlyList<string> ImportedAliases,
    IReadOnlyList<YanflyAuthoringSurface> AuthoringSurfaces,
    IReadOnlyList<string> DeclaredContexts,
    IReadOnlyList<YanflyUnresolvedDependency> Dependencies);

internal sealed record YanflyUnresolvedDependency(
    string RequestedPluginId,
    YanflyDependencyKind Kind,
    YanflyEvidenceLocation Location,
    string Evidence);
