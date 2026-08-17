using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Runtime.MV;

namespace Playroma.JRPG.RpgMakerMV;

public enum MvProjectLayoutKind
{
    Unknown,
    SourceRoot,
    WwwSubdirectory,
    BrowserProject,
    ReadableDeployment,
    IncompleteProject,
    UnsupportedMzProject
}

public enum ProjectEditability
{
    FullyEditableSource,
    ProbablyEditable,
    ReadOnlyInspection,
    UnsupportedEncryptedDeployment,
    Invalid
}

public enum ProjectConfidence
{
    Certain,
    Strong,
    Probable,
    Weak
}

public enum ProjectEvidenceKind
{
    RpgProjectMarker,
    DataDirectory,
    SystemJson,
    MapInfosJson,
    StandardDatabaseSet,
    RuntimeCoreScript,
    IndexHtml,
    PackageJson,
    PluginsManifest,
    NwjsExecutable,
    MvAssetDirectories,
    EncryptedAssets,
    MzProjectMarker
}

public sealed record ProjectEvidence(
    ProjectEvidenceKind Kind,
    string RelativePath,
    string Description);

public sealed record ProjectSearchOptions(
    int MaximumDepth = 3,
    int MaximumDirectories = 10_000,
    bool SearchChildren = true);

public sealed record MvProjectCandidate(
    string SelectedRoot,
    string ProjectRoot,
    string DataRoot,
    string? RuntimeRoot,
    string? ProjectMarkerPath,
    MvProjectLayoutKind LayoutKind,
    ProjectEditability Editability,
    ProjectConfidence Confidence,
    IReadOnlyList<ProjectEvidence> Evidence,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public int Score => Confidence switch
    {
        ProjectConfidence.Certain => 400,
        ProjectConfidence.Strong => 300,
        ProjectConfidence.Probable => 200,
        _ => 100
    } + Evidence.Count;
}

public enum ProjectLoadDepth
{
    ProbeOnly,
    Inventory,
    StandardData,
    Complete
}

public sealed record ProjectLoadOptions(
    ProjectLoadDepth Depth = ProjectLoadDepth.StandardData,
    bool CalculateStrongAssetHashes = false,
    int MaximumParallelReads = 4,
    bool ContinueAfterErrors = true,
    int MaximumFiles = 250_000)
{
    public int EffectiveParallelReads => Math.Clamp(MaximumParallelReads, 1, 16);
}

public sealed record MvProjectIdentity(
    string ProjectRoot,
    string DataRoot,
    string? RuntimeRoot,
    string? ProjectTitle,
    string? ProjectMarkerVersion);

public sealed record MvProjectLayout(
    MvProjectLayoutKind Kind,
    string ProjectRoot,
    string DataRoot,
    string? RuntimeRoot);

public enum MvDocumentKind
{
    Actors,
    Animations,
    Armors,
    Classes,
    CommonEvents,
    Enemies,
    Items,
    MapInfos,
    Map,
    Skills,
    States,
    System,
    Tilesets,
    Troops,
    Weapons,
    Unknown
}

public sealed record MvDatabaseFileSummary(
    MvDocumentKind Kind,
    string RelativePath,
    int ArrayLength,
    int PopulatedRecordCount,
    int NullSlotCount,
    int HighestRecordId,
    bool HasIndexZeroNull,
    bool IsValidJson,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public sealed record MvDatabaseInventory(
    IReadOnlyList<MvDatabaseFileSummary> Files,
    string? ProjectTitle)
{
    public static MvDatabaseInventory Empty { get; } = new([], null);

    public MvDatabaseFileSummary? Find(MvDocumentKind kind) =>
        Files.FirstOrDefault(file => file.Kind == kind);
}

public sealed record MvMapDescriptor(
    int Id,
    string Name,
    int ParentId,
    int Order,
    string RelativePath,
    bool FileExists,
    int? Width,
    int? Height,
    int? EventCount,
    int? EventPageCount,
    int? EventCommandCount,
    IReadOnlyList<int> UnknownCommandCodes,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

public sealed record MvPluginEntry(
    int LoadOrder,
    string Name,
    bool Enabled,
    string Description,
    IReadOnlyDictionary<string, string> Parameters,
    string? ScriptRelativePath,
    bool ScriptExists,
    string? ScriptSha256,
    bool IsDuplicate,
    bool AppearsNodeDependent,
    string? DeclaredVersion = null,
    IReadOnlyList<string>? ImportedAliases = null,
    string? PluginProviderId = null)
{
    public IReadOnlyList<string> EffectiveImportedAliases => ImportedAliases ?? [];
}

public sealed record MvPluginInventory(
    string? ManifestRelativePath,
    string? ManifestSha256,
    IReadOnlyList<MvPluginEntry> Entries,
    int EnabledCount,
    IReadOnlyList<ProjectDiagnostic> Diagnostics)
{
    public static MvPluginInventory Empty { get; } = new(null, null, [], 0, []);
}

public sealed record MvProjectSnapshot(
    MvProjectIdentity Identity,
    MvProjectLayout Layout,
    ProjectEditability Editability,
    ProjectFileInventory Files,
    MvDatabaseInventory Database,
    IReadOnlyList<MvMapDescriptor> Maps,
    MvPluginInventory Plugins,
    MvRuntimeSnapshot Runtime,
    AssetInventory Assets,
    IReadOnlyList<ProjectDiagnostic> Diagnostics);

internal sealed record DatabaseInspectionResult(
    MvDatabaseInventory Database,
    IReadOnlyList<MvMapDescriptor> Maps,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    string? ProjectMarkerVersion);
