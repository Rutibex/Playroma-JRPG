using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.Documents.Mutable;

public enum MutableDocumentState
{
    Unloaded,
    Loading,
    Clean,
    Modified,
    InvalidReadOnly,
    ExternallyModified,
    Saving,
    SaveFailed,
    Disposed
}

public enum DocumentChangeKind
{
    SetValue,
    AddProperty,
    RemoveProperty,
    InsertArrayElement,
    RemoveArrayElement,
    ReplaceSubtree
}

public sealed record JsonValueSnapshot(string? RawJson);

public sealed record DocumentChange(
    Guid Id,
    JsonPath Path,
    DocumentChangeKind Kind,
    JsonValueSnapshot? OldValue,
    JsonValueSnapshot? NewValue,
    string Description);

public interface ITransactionalDocumentSnapshot
{
    byte[] CaptureGeneratedBytes();
    void RestoreGeneratedBytes(ReadOnlySpan<byte> bytes);
}

public interface IMutableProjectDocument : IDisposable
{
    string RelativePath { get; }

    MvDocumentKind Kind { get; }

    MutableDocumentState State { get; }

    ContentFingerprint OriginalFingerprint { get; }

    ContentFingerprint? CurrentDiskFingerprint { get; }

    IReadOnlyList<DocumentChange> Changes { get; }

    IReadOnlyList<ProjectDiagnostic> Diagnostics { get; }

    ReadOnlyMemory<byte> GetOriginalBytes();

    ValueTask<ReadOnlyMemory<byte>> GenerateBytesAsync(CancellationToken cancellationToken = default);

    void DiscardChanges();
}

public interface IMvTypedDocument<out TRoot> : IMutableProjectDocument
{
    TRoot Root { get; }
}

public sealed record ProjectDocumentHandle(
    string RelativePath,
    MvDocumentKind Kind,
    MutableDocumentState State,
    ContentFingerprint Fingerprint,
    long Size);
