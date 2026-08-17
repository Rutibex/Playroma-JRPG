using Playroma.JRPG.Core;

namespace Playroma.JRPG.Assets;

public enum AssetCategory
{
    Unknown,
    Animations,
    Battlebacks1,
    Battlebacks2,
    Characters,
    Enemies,
    Faces,
    Parallaxes,
    Pictures,
    SideViewActors,
    SideViewEnemies,
    System,
    Tilesets,
    Titles1,
    Titles2,
    BackgroundMusic,
    BackgroundSounds,
    MusicEffects,
    SoundEffects,
    Fonts,
    Movies,
    ProjectIcon
}

public enum AssetMediaKind
{
    Unknown,
    Image,
    Audio,
    Movie,
    Font
}

public enum AssetStatus
{
    Available,
    Missing,
    UnsupportedFormat,
    Encrypted,
    Invalid,
    CaseConflict,
    VariantConflict,
    ExternallyModified,
    PendingImport,
    PendingReplacement,
    PendingRename,
    PendingDeletion
}

public readonly record struct AssetKey
{
    public AssetKey(AssetCategory category, string logicalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalName);
        string normalized = NormalizeLogicalName(logicalName);
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("The logical asset name is empty after normalization.", nameof(logicalName));
        Category = category;
        LogicalName = normalized;
    }

    public AssetCategory Category { get; }
    public string LogicalName { get; }

    public override string ToString() => $"{Category}:{LogicalName}";

    public static string NormalizeLogicalName(string value) => value
        .Replace('\\', '/')
        .Trim('/')
        .Trim();
}

public sealed record AssetEntry(
    string RelativePath,
    AssetCategory Category,
    AssetMediaKind MediaKind,
    string Extension,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    bool IsEncrypted,
    string? Sha256);

public sealed record AssetVariant(
    string RelativePath,
    string Extension,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string? Sha256,
    bool IsEncrypted,
    AssetStatus Status = AssetStatus.Available)
{
    public string NormalizedRelativePath => RelativePath.Replace('\\', '/');
}

public sealed record ImageAssetMetadata(
    int Width,
    int Height,
    int BitDepth,
    bool HasAlpha,
    string Format,
    bool IsValid,
    string? Detail = null);

public sealed record AudioAssetMetadata(
    TimeSpan? Duration,
    int? Channels,
    int? SampleRate,
    int? BitsPerSample,
    long? AverageBitRate,
    long? LoopStart,
    long? LoopLength,
    string Format,
    bool IsValid,
    IReadOnlyDictionary<string, string>? Comments = null,
    string? Detail = null)
{
    public long? LoopEnd => LoopStart.HasValue && LoopLength.HasValue
        ? checked(LoopStart.Value + LoopLength.Value)
        : null;
}

public sealed record MovieAssetMetadata(
    TimeSpan? Duration,
    int? Width,
    int? Height,
    double? FramesPerSecond,
    string Container,
    string? VideoCodec,
    string? AudioCodec,
    bool IsValid,
    string? Detail = null);

public sealed record FontAssetMetadata(
    string? Family,
    string? Subfamily,
    string? FullName,
    string Format,
    bool IsValid,
    string? Detail = null);

public sealed record AssetMetadataSummary(
    ImageAssetMetadata? Image = null,
    AudioAssetMetadata? Audio = null,
    MovieAssetMetadata? Movie = null,
    FontAssetMetadata? Font = null,
    DateTimeOffset? AnalysedUtc = null)
{
    public static AssetMetadataSummary Empty { get; } = new();
}

public sealed record AssetReferenceHint(
    AssetKey Key,
    string SourceRelativePath,
    string SourceLocation,
    bool IsExact,
    string? Context = null);

public sealed record AssetUsageSummary(
    int ExactReferenceCount,
    int HeuristicReferenceCount,
    bool IsRuntimeReserved,
    IReadOnlyList<AssetReferenceHint> References)
{
    public static AssetUsageSummary Empty { get; } = new(0, 0, false, []);
    public bool HasKnownUse => ExactReferenceCount > 0 || HeuristicReferenceCount > 0 || IsRuntimeReserved;
}

public sealed record AssetRecord(
    AssetKey Key,
    AssetMediaKind MediaKind,
    IReadOnlyList<AssetVariant> Variants,
    AssetMetadataSummary Metadata,
    AssetUsageSummary Usage,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    AssetStatus Status)
{
    public AssetVariant? PreferredVariant => Variants
        .OrderBy(variant => variant.Status == AssetStatus.PendingDeletion ? 1 : 0)
        .ThenBy(variant => AssetCategoryRegistry.ExtensionPreference(variant.Extension))
        .ThenBy(variant => variant.RelativePath, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();

    public long TotalBytes => Variants.Sum(variant => variant.Size);
    public bool IsMissing => Status == AssetStatus.Missing || Variants.Count == 0;
}

public sealed record AssetCategorySummary(
    AssetCategory Category,
    int Count,
    long TotalBytes);

public sealed record AssetInventory(
    IReadOnlyList<AssetEntry> Assets,
    IReadOnlyList<AssetCategorySummary> Categories,
    int EncryptedAssetCount,
    long TotalBytes)
{
    public static AssetInventory Empty { get; } = new([], [], 0, 0);
}

public sealed record AssetCatalogueProgress(
    int Completed,
    int Total,
    string? CurrentAsset,
    string Stage);

public enum AssetPendingMutationKind
{
    Create,
    Replace,
    Move,
    Delete
}

public sealed record AssetPendingMutation(
    AssetPendingMutationKind Kind,
    string? SourceRelativePath,
    string? DestinationRelativePath,
    string? StagedContentPath,
    long GeneratedLength);

public sealed record AssetCatalogueStatistics(
    int LogicalAssetCount,
    int PhysicalVariantCount,
    int MissingAssetCount,
    int EncryptedVariantCount,
    int ExactReferenceCount,
    int HeuristicReferenceCount,
    long TotalBytes,
    DateTimeOffset BuiltUtc);

public sealed record AssetQuery(
    string? Text = null,
    AssetCategory? Category = null,
    AssetMediaKind? MediaKind = null,
    bool? Used = null,
    bool? Missing = null,
    bool? Encrypted = null,
    bool? Duplicate = null,
    string? Extension = null,
    AssetStatus? Status = null);

public sealed record WaveformBucket(float Minimum, float Maximum);

public sealed record WaveformEnvelope(
    TimeSpan Duration,
    int SampleRate,
    int Channels,
    IReadOnlyList<WaveformBucket> Buckets);
