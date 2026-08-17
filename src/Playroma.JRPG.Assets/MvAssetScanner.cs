using System.Globalization;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.Assets;

public sealed class MvAssetScanner
{
    public OperationResult<AssetInventory> Scan(
        ProjectFileInventory files,
        string runtimeRelativePrefix = "")
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(runtimeRelativePrefix);
        string prefix = NormalizePrefix(runtimeRelativePrefix);
        List<ProjectDiagnostic> diagnostics = [];
        List<AssetEntry> assets = [];

        foreach (ProjectFileEntry file in files.Files)
        {
            string projectRelative = MvAssetPathResolver.Normalize(file.RelativePath);
            if (!string.IsNullOrEmpty(prefix) && !projectRelative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string runtimeRelative = string.IsNullOrEmpty(prefix) ? projectRelative : projectRelative[prefix.Length..];
            if (!MvAssetPathResolver.TryResolveCategory(runtimeRelative, out AssetCategory category)) continue;

            string extension = Path.GetExtension(runtimeRelative);
            AssetMediaKind mediaKind = MvAssetPathResolver.ResolveMediaKind(extension);
            bool encrypted = MvAssetPathResolver.IsEncryptedExtension(extension);
            if (mediaKind == AssetMediaKind.Unknown)
            {
                diagnostics.Add(ProjectDiagnostic.Information(
                    DiagnosticCodes.UnusualAssetExtension,
                    "An unrecognized file extension was found in an MV asset directory.",
                    new ProjectLocation(file.RelativePath)));
            }

            assets.Add(new AssetEntry(
                file.RelativePath,
                category,
                mediaKind,
                extension,
                file.Size,
                file.LastWriteTimeUtc,
                encrypted,
                file.Fingerprint.Sha256));
        }

        foreach (IGrouping<string, AssetEntry> collision in assets.GroupBy(
                     asset => asset.RelativePath.Replace('\\', '/').ToUpperInvariant(),
                     StringComparer.Ordinal))
        {
            string[] distinct = collision.Select(asset => asset.RelativePath).Distinct(StringComparer.Ordinal).ToArray();
            if (distinct.Length > 1)
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.AssetCaseCollision,
                    "Asset paths differ only by letter casing and may fail on case-sensitive deployment targets: "
                    + string.Join(", ", distinct),
                    new ProjectLocation(distinct[0])));
            }
        }

        int encryptedCount = assets.Count(asset => asset.IsEncrypted);
        if (encryptedCount > 0)
        {
            diagnostics.Add(ProjectDiagnostic.Warning(
                DiagnosticCodes.EncryptedAssetsDetected,
                string.Create(CultureInfo.InvariantCulture, $"The project contains {encryptedCount:N0} encrypted RPG Maker asset file(s).")));
        }

        AssetCategorySummary[] categories = assets
            .GroupBy(asset => asset.Category)
            .OrderBy(group => group.Key)
            .Select(group => new AssetCategorySummary(group.Key, group.Count(), group.Sum(asset => asset.Size)))
            .ToArray();

        AssetInventory inventory = new(
            assets.OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            categories,
            encryptedCount,
            assets.Sum(asset => asset.Size));
        return OperationResult<AssetInventory>.Success(inventory, diagnostics);
    }

    private static string NormalizePrefix(string prefix)
    {
        string normalized = prefix.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(normalized) || normalized == "." ? string.Empty : normalized + "/";
    }
}
