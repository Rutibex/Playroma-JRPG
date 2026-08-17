using System.Text.Json;

namespace Playroma.JRPG.Documents;

public sealed record ProjectJsonDocumentDescriptor(
    string RelativePath,
    string Kind,
    ContentFingerprint Fingerprint,
    bool IsValidJson,
    IReadOnlyList<Playroma.JRPG.Core.ProjectDiagnostic> Diagnostics);

public interface IReadOnlyJsonDocumentLease : IDisposable
{
    ReadOnlyMemory<byte> OriginalBytes { get; }

    JsonDocument Document { get; }

    ContentFingerprint Fingerprint { get; }
}

public interface IReadOnlyDocumentStore
{
    ValueTask<IReadOnlyJsonDocumentLease> OpenJsonAsync(
        ProjectJsonDocumentDescriptor descriptor,
        CancellationToken cancellationToken);
}

public sealed class ReadOnlyDocumentStore : IReadOnlyDocumentStore
{
    private readonly ProjectPathPolicy _paths;
    private readonly FileFingerprintService _fingerprints;

    public ReadOnlyDocumentStore(
        ProjectPathPolicy paths,
        FileFingerprintService fingerprints)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
    }

    public async ValueTask<IReadOnlyJsonDocumentLease> OpenJsonAsync(
        ProjectJsonDocumentDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        string fullPath = _paths.ResolveRelative(descriptor.RelativePath);
        ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(
            fullPath,
            calculateSha256: true,
            cancellationToken).ConfigureAwait(false);
#pragma warning disable CA2000 // Ownership is transferred to the returned lease.
        JsonDocument document = JsonDocument.Parse(snapshot.Bytes);
#pragma warning restore CA2000
        return new ReadOnlyJsonDocumentLease(snapshot.Bytes, document, snapshot.Fingerprint);
    }
}

internal sealed class ReadOnlyJsonDocumentLease : IReadOnlyJsonDocumentLease
{
    private JsonDocument? _document;

    public ReadOnlyJsonDocumentLease(
        byte[] originalBytes,
        JsonDocument document,
        ContentFingerprint fingerprint)
    {
        OriginalBytes = originalBytes;
        _document = document;
        Fingerprint = fingerprint;
    }

    public ReadOnlyMemory<byte> OriginalBytes { get; }

    public JsonDocument Document =>
        _document ?? throw new ObjectDisposedException(nameof(ReadOnlyJsonDocumentLease));

    public ContentFingerprint Fingerprint { get; }

    public void Dispose()
    {
        _document?.Dispose();
        _document = null;
    }
}
