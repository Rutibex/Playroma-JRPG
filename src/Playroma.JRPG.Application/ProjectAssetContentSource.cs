using Playroma.JRPG.Assets;
using Playroma.JRPG.Documents.Saving;

namespace Playroma.JRPG.Application;

/// <summary>
/// Presents the in-memory/staged file overlay as an asset content source so previews
/// display imported, replaced and renamed assets before the user saves the project.
/// </summary>
public sealed class ProjectAssetContentSource : IAssetOverlayContentSource
{
    private readonly IProjectFileOverlay _overlay;

    public ProjectAssetContentSource(IProjectFileOverlay overlay)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    public string ProjectRoot => _overlay.ProjectRoot;

    public IReadOnlyList<AssetContentOverlayEntry> PendingEntries => _overlay.PendingMutations.Select(ToAssetEntry).ToArray();

    public ValueTask<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default) =>
        _overlay.OpenReadAsync(relativePath, cancellationToken);

    public bool Exists(string relativePath) => _overlay.Exists(relativePath);

    public string GetFullPath(string relativePath)
    {
        string normalized = Normalize(relativePath);
        PendingFileMutation[] mutations = _overlay.PendingMutations.ToArray();
        PendingFileMutation? destination = mutations.LastOrDefault(mutation => PathEquals(mutation.DestinationRelativePath, normalized));
        if (destination is not null)
        {
            if (destination.Kind is FileMutationKind.Create or FileMutationKind.Replace)
            {
                if (!string.IsNullOrWhiteSpace(destination.StagedContentPath)) return destination.StagedContentPath;
            }
            else if (destination.Kind == FileMutationKind.Move && !string.IsNullOrWhiteSpace(destination.SourceRelativePath))
            {
                return Resolve(destination.SourceRelativePath);
            }
        }

        PendingFileMutation? source = mutations.LastOrDefault(mutation => PathEquals(mutation.SourceRelativePath, normalized));
        if (source?.Kind == FileMutationKind.Delete || source?.Kind == FileMutationKind.Move)
        {
            throw new FileNotFoundException("The asset is hidden by a pending project file operation.", normalized);
        }

        return Resolve(normalized);
    }

    private string Resolve(string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(ProjectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = ProjectRoot.EndsWith(Path.DirectorySeparatorChar) ? ProjectRoot : ProjectRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(prefix, comparison) && !string.Equals(full, ProjectRoot, comparison))
        {
            throw new InvalidOperationException("The asset path escapes the project root.");
        }
        return full;
    }

    private static AssetContentOverlayEntry ToAssetEntry(PendingFileMutation mutation) => new(
        mutation.Kind switch
        {
            FileMutationKind.Create => AssetContentOverlayKind.Create,
            FileMutationKind.Replace => AssetContentOverlayKind.Replace,
            FileMutationKind.Move => AssetContentOverlayKind.Move,
            FileMutationKind.Delete => AssetContentOverlayKind.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation.Kind))
        },
        mutation.SourceRelativePath,
        mutation.DestinationRelativePath,
        mutation.StagedContentPath,
        mutation.StagedLength,
        DateTimeOffset.UtcNow);

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
    private static bool PathEquals(string? left, string right) =>
        left is not null && string.Equals(Normalize(left), Normalize(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
