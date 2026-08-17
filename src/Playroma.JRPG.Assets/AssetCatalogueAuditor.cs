using Playroma.JRPG.Core;

namespace Playroma.JRPG.Assets;

public sealed record AssetAuditReport(
    IReadOnlyList<AssetRecord> Missing,
    IReadOnlyList<AssetRecord> NoKnownReferences,
    IReadOnlyList<IReadOnlyList<AssetVariant>> ExactDuplicateGroups,
    IReadOnlyList<AssetRecord> CaseConflicts,
    IReadOnlyList<AssetRecord> VariantConflicts,
    IReadOnlyList<ProjectDiagnostic> Diagnostics,
    DateTimeOffset CompletedUtc);

public sealed class AssetCatalogueAuditor
{
    public async ValueTask<AssetAuditReport> AuditAsync(
        IAssetCatalogueService catalogue,
        bool includeDuplicateHashing = true,
        IProgress<AssetCatalogueProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        IReadOnlyList<AssetRecord> assets = catalogue.Assets;
        List<ProjectDiagnostic> diagnostics = [];
        for (int index = 0; index < assets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AssetRecord asset = assets[index];
            progress?.Report(new AssetCatalogueProgress(index, assets.Count, asset.Key.ToString(), "Auditing assets"));
            diagnostics.AddRange(asset.Diagnostics);
            foreach (AssetVariant variant in asset.Variants)
            {
                if (variant.Size == 0)
                {
                    diagnostics.Add(ProjectDiagnostic.Error(
                        DiagnosticCodes.Phase5ZeroByteAsset,
                        "The asset file is empty.",
                        new ProjectLocation(variant.RelativePath)));
                }
                if (asset.MediaKind == AssetMediaKind.Unknown && !variant.IsEncrypted)
                {
                    diagnostics.Add(ProjectDiagnostic.Warning(
                        DiagnosticCodes.Phase5UnsupportedPreview,
                        "The file extension is not recognized by the asset preview system.",
                        new ProjectLocation(variant.RelativePath),
                        variant.Extension));
                }
            }
        }

        IReadOnlyList<IReadOnlyList<AssetVariant>> duplicates = includeDuplicateHashing
            ? await catalogue.FindExactDuplicatesAsync(cancellationToken).ConfigureAwait(false)
            : [];
        foreach (IReadOnlyList<AssetVariant> duplicate in duplicates)
        {
            string paths = string.Join(", ", duplicate.Select(variant => variant.RelativePath));
            diagnostics.Add(ProjectDiagnostic.Information(
                DiagnosticCodes.Phase5VariantConflict,
                "Multiple asset files have identical content.",
                new ProjectLocation(duplicate[0].RelativePath),
                paths));
        }

        progress?.Report(new AssetCatalogueProgress(assets.Count, assets.Count, null, "Ready"));
        return new AssetAuditReport(
            assets.Where(asset => asset.IsMissing).ToArray(),
            assets.Where(asset => !asset.Usage.HasKnownUse && !asset.IsMissing).ToArray(),
            duplicates,
            assets.Where(asset => asset.Status == AssetStatus.CaseConflict).ToArray(),
            assets.Where(asset => asset.Status == AssetStatus.VariantConflict).ToArray(),
            diagnostics
                .DistinctBy(diagnostic => (diagnostic.Code, diagnostic.Location.RelativePath, diagnostic.Location.JsonPath, diagnostic.Message))
                .OrderByDescending(diagnostic => diagnostic.Severity)
                .ThenBy(diagnostic => diagnostic.Location.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToArray(),
            DateTimeOffset.UtcNow);
    }
}
