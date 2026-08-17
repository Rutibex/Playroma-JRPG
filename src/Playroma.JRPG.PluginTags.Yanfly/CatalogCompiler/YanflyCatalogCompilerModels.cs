using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public enum YanflyDiscoveryKind
{
    Notetag, BlockOpening, BlockClosing, CommentTag, PluginCommand, ActionSequence,
    TextCode, ScriptCall, MoveRouteScript, SwitchNameMetadata, VariableNameMetadata,
    PluginParameter, DocumentationMarkup, ChangelogReference, CodeComparison, Unknown
}

public enum YanflyDiscoveryDisposition
{
    TypedFeature, AdvancedRawFeature, ContextAssistant, PluginConfiguration,
    DocumentationOnly, Duplicate, RequiresReview
}

public enum YanflyParserVerificationStatus
{
    VerifiedParserExpression, VerifiedKeywordSequence, HelpOnly, NotApplicable
}

public enum YanflyCatalogDiagnosticSeverity { Info, Warning, Error }

public sealed record YanflyCanonicalPluginSource(
    string PluginId, string FileName, string RelativePath, string Version,
    string Sha256, string SourceText, YanflyPluginInventoryEntry InventoryEntry);

public sealed record YanflyHelpSection(
    string Heading, int StartLine, int EndLine, YanflyAuthoringSurface? Surface,
    IReadOnlyList<PluginDocumentKind> DocumentKinds);

public sealed record YanflyPluginParameterOption(string Name, string Value);

public sealed record YanflyPluginParameterDefinition(
    string Name, string DisplayName, string? Parent, string? Type, string? DefaultValue,
    string? Minimum, string? Maximum, string? DecimalPlaces,
    IReadOnlyList<YanflyPluginParameterOption> Options, string Description, int HeaderLine,
    string DescriptionHash = "");

public sealed record YanflyHelpDocument(
    string PluginId, string Description, string HelpText, IReadOnlyList<string> Lines,
    IReadOnlyList<YanflyHelpSection> Sections,
    IReadOnlyList<YanflyPluginParameterDefinition> Parameters);

public sealed record YanflySyntaxDiscovery(
    string DiscoveryId, string PluginId, string Syntax, string NormalizedSyntax,
    YanflyDiscoveryKind Kind, YanflyDiscoveryDisposition Disposition,
    PluginAuthoringSurface? Surface, IReadOnlyList<PluginDocumentKind> DocumentKinds,
    string SectionHeading, int HelpLine, string? PairedDiscoveryId,
    bool IsCanonicalCandidate, string ClassificationReason);

public sealed record YanflyParserVerification(
    string DiscoveryId, string PluginId, YanflyParserVerificationStatus Status,
    IReadOnlyList<string> SignificantTokens, IReadOnlyList<int> EvidenceLineNumbers,
    string EvidenceHash, string Reason);

public sealed record YanflySemanticArgumentOverride(
    string Name, string DisplayName, PluginArgumentKind Kind, bool IsRequired = false,
    decimal? Minimum = null, decimal? Maximum = null, IReadOnlyList<string>? Choices = null,
    PluginReferenceKind ReferenceKind = PluginReferenceKind.None, bool AllowMultiple = false,
    string? Description = null, string? DefaultValue = null);

public sealed record YanflySemanticFeatureOverride(
    string PluginId, string? DiscoveryId = null, string? Syntax = null,
    string? FeatureId = null, string? DisplayName = null, string? Summary = null,
    PluginAuthoringSurface? Surface = null,
    IReadOnlyList<PluginDocumentKind>? DocumentKinds = null,
    PluginSyntaxKind? SyntaxKind = null, string? CanonicalTemplate = null,
    string? ClosingTemplate = null, string? OpeningRegularExpression = null,
    string? ClosingRegularExpression = null,
    PluginFeatureMultiplicity? Multiplicity = null,
    IReadOnlyList<YanflySemanticArgumentOverride>? Arguments = null,
    IReadOnlyList<string>? RequiredPlugins = null,
    IReadOnlyList<string>? RecommendedPlugins = null,
    IReadOnlyList<string>? ConflictGroups = null,
    string? SpecializedEditorId = null,
    PluginFeatureComplexity? Complexity = null,
    string? DocumentationUrl = null, IReadOnlyList<string>? SearchTerms = null,
    int? MatchPriority = null, YanflyDiscoveryDisposition? Disposition = null,
    bool Ignore = false, string? Reason = null);

public sealed record YanflySemanticPluginOverride(
    string PluginId, string? DisplayName = null, string? DocumentationUrl = null,
    IReadOnlyList<string>? RequiredPlugins = null, string? SpecializedEditorId = null,
    IReadOnlyList<PluginDocumentKind>? DefaultDocumentKinds = null);

public sealed record YanflySemanticOverlayDocument(
    int SchemaVersion, string CatalogVersion,
    IReadOnlyList<YanflySemanticPluginOverride> Plugins,
    IReadOnlyList<YanflySemanticFeatureOverride> Features);

public sealed record YanflyCatalogPluginCompilation(
    string PluginId, string FileName, string Version, string SourceFingerprint,
    int HelpSectionCount, int ParameterCount, int DiscoveryCount, int FeatureCount,
    int VerifiedCount, int ReviewCount,
    IReadOnlyDictionary<YanflyDiscoveryKind, int> DiscoveryKindCounts,
    IReadOnlyDictionary<YanflyDiscoveryDisposition, int> DispositionCounts);

public sealed record YanflyCatalogCoverageSummary(
    int PluginCount, int PluginsWithHelp, int HelpSectionCount, int PluginParameterCount,
    int SourceDiscoveryCount, int UniqueDiscoveryCount, int DuplicateDiscoveryCount,
    int GeneratedFeatureCount, int TypedFeatureCount, int AdvancedRawFeatureCount,
    int ContextAssistantCount, int PluginConfigurationCount, int DocumentationOnlyCount,
    int RequiresReviewCount, int VerifiedParserExpressionCount,
    int VerifiedKeywordSequenceCount, int HelpOnlyCount,
    IReadOnlyDictionary<YanflyDiscoveryKind, int> DiscoveryKindCounts,
    IReadOnlyDictionary<PluginAuthoringSurface, int> FeatureSurfaceCounts);

public sealed record YanflyCatalogDiagnostic(
    string Code, YanflyCatalogDiagnosticSeverity Severity, string Message,
    string? PluginId = null, string? DiscoveryId = null);

public sealed record YanflyHelpSectionIndexEntry(
    string PluginId, string Heading, int StartLine, int EndLine,
    YanflyAuthoringSurface? Surface, IReadOnlyList<PluginDocumentKind> DocumentKinds);

public sealed record YanflyPluginParameterIndexEntry(
    string PluginId, YanflyPluginParameterDefinition Parameter);

public sealed record YanflyCatalogCompilation(
    int SchemaVersion, string CatalogVersion, DateTimeOffset GeneratedAtUtc,
    string GeneratedFrom, string InventoryFingerprint, PluginTagCatalogDocument Catalog,
    YanflyCatalogCoverageSummary Coverage,
    IReadOnlyList<YanflyCatalogPluginCompilation> Plugins,
    IReadOnlyList<YanflyHelpSectionIndexEntry> HelpSections,
    IReadOnlyList<YanflyPluginParameterIndexEntry> Parameters,
    IReadOnlyList<YanflySyntaxDiscovery> Discoveries,
    IReadOnlyList<YanflyParserVerification> ParserVerifications,
    IReadOnlyList<YanflyCatalogDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(d => d.Severity != YanflyCatalogDiagnosticSeverity.Error)
        && Coverage.RequiresReviewCount == 0;
}

public sealed record YanflyCatalogVerificationDifference(
    string Code, string Message, string? PluginId = null, string? FeatureId = null);

public sealed record YanflyCatalogVerificationResult(
    IReadOnlyList<YanflyCatalogVerificationDifference> Differences)
{
    public bool Success => Differences.Count == 0;
}
