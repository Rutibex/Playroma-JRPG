using System.Security.Cryptography;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Documents;

public sealed record ReadFileSnapshot(
    byte[] Bytes,
    FileMetadata Before,
    FileMetadata After,
    string? Sha256)
{
    public bool ChangedDuringRead =>
        Before.Length != After.Length || Before.LastWriteTimeUtc != After.LastWriteTimeUtc;

    public ContentFingerprint Fingerprint =>
        new(After.Length, After.LastWriteTimeUtc, Sha256);
}

public sealed class FileFingerprintService
{
    private readonly IReadOnlyProjectFileSystem _fileSystem;

    public FileFingerprintService(IReadOnlyProjectFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async ValueTask<ReadFileSnapshot> ReadAsync(
        string path,
        bool calculateSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileMetadata before = _fileSystem.GetMetadata(path);
        byte[] bytes = await _fileSystem.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        FileMetadata after = _fileSystem.GetMetadata(path);
        string? hash = calculateSha256 ? Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() : null;
        return new ReadFileSnapshot(bytes, before, after, hash);
    }

    public ContentFingerprint CreateQuickFingerprint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileMetadata metadata = _fileSystem.GetMetadata(path);
        return new ContentFingerprint(metadata.Length, metadata.LastWriteTimeUtc, null);
    }

    public static ProjectDiagnostic? CreateChangedDuringLoadDiagnostic(
        ReadFileSnapshot snapshot,
        string relativePath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(relativePath);
        if (!snapshot.ChangedDuringRead)
        {
            return null;
        }

        return ProjectDiagnostic.Warning(
            DiagnosticCodes.FileChangedDuringLoad,
            "The file changed while Playroma was reading it; the snapshot may be inconsistent.",
            new ProjectLocation(relativePath));
    }
}
