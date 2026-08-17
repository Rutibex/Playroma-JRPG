using Playroma.JRPG.Documents;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Documents.Mutable;

public sealed class MutableDocumentFactory
{
    private readonly IReadOnlyProjectFileSystem _fileSystem;
    private readonly ProjectPathPolicy _paths;
    private readonly FileFingerprintService _fingerprints;

    public MutableDocumentFactory(string projectRoot, IReadOnlyProjectFileSystem? fileSystem = null)
    {
        _fileSystem = fileSystem ?? new PhysicalReadOnlyProjectFileSystem();
        _paths = new ProjectPathPolicy(projectRoot);
        _fingerprints = new FileFingerprintService(_fileSystem);
    }

    public async ValueTask<MutableJsonProjectDocument> OpenJsonAsync(
        string relativePath,
        MvDocumentKind kind,
        CancellationToken cancellationToken = default)
    {
        string fullPath = _paths.ResolveRelative(relativePath);
        ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(fullPath, calculateSha256: true, cancellationToken).ConfigureAwait(false);
        return MutableJsonProjectDocument.Create(relativePath.Replace('\\', '/'), kind, snapshot.Bytes, snapshot.Fingerprint);
    }
}
