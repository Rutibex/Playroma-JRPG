using Playroma.JRPG.Documents;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public abstract class MvSingletonDocument<TView> : IMvTypedDocument<TView>
    where TView : MvNodeView
{
    protected MvSingletonDocument(MutableJsonProjectDocument inner, TView root)
    {
        Inner = inner;
        Root = root;
    }

    protected MutableJsonProjectDocument Inner { get; }

    public TView Root { get; }
    public string RelativePath => Inner.RelativePath;
    public MvDocumentKind Kind => Inner.Kind;
    public MutableDocumentState State => Inner.State;
    public ContentFingerprint OriginalFingerprint => Inner.OriginalFingerprint;
    public ContentFingerprint? CurrentDiskFingerprint => Inner.CurrentDiskFingerprint;
    public IReadOnlyList<DocumentChange> Changes => Inner.Changes;
    public IReadOnlyList<Playroma.JRPG.Core.ProjectDiagnostic> Diagnostics => Inner.Diagnostics;
    public ReadOnlyMemory<byte> GetOriginalBytes() => Inner.GetOriginalBytes();
    public ValueTask<ReadOnlyMemory<byte>> GenerateBytesAsync(CancellationToken cancellationToken) =>
        Inner.GenerateBytesAsync(cancellationToken);
    public void DiscardChanges() => Inner.DiscardChanges();
    public void Dispose() => Inner.Dispose();
}

public sealed class MvSystemDocument : MvSingletonDocument<MvSystemView>
{
    public MvSystemDocument(MutableJsonProjectDocument inner)
        : base(inner, new MvSystemView(inner))
    {
    }
}

public sealed class MvMapDocument : MvSingletonDocument<MvMapView>
{
    public MvMapDocument(MutableJsonProjectDocument inner)
        : base(inner, new MvMapView(inner))
    {
    }
}

public sealed class MvMapInfosDocument : IMvTypedDocument<MvIdArray<MvMapInfoView>>
{
    private readonly MutableJsonProjectDocument _inner;

    public MvMapInfosDocument(MutableJsonProjectDocument inner)
    {
        _inner = inner;
        Root = new MvIdArray<MvMapInfoView>(inner, JsonPath.Root, static (document, path) => new MvMapInfoView(document, path));
    }

    public MvIdArray<MvMapInfoView> Root { get; }
    public string RelativePath => _inner.RelativePath;
    public MvDocumentKind Kind => _inner.Kind;
    public MutableDocumentState State => _inner.State;
    public ContentFingerprint OriginalFingerprint => _inner.OriginalFingerprint;
    public ContentFingerprint? CurrentDiskFingerprint => _inner.CurrentDiskFingerprint;
    public IReadOnlyList<DocumentChange> Changes => _inner.Changes;
    public IReadOnlyList<Playroma.JRPG.Core.ProjectDiagnostic> Diagnostics => _inner.Diagnostics;
    public ReadOnlyMemory<byte> GetOriginalBytes() => _inner.GetOriginalBytes();
    public ValueTask<ReadOnlyMemory<byte>> GenerateBytesAsync(CancellationToken cancellationToken) =>
        _inner.GenerateBytesAsync(cancellationToken);
    public void DiscardChanges() => _inner.DiscardChanges();
    public void Dispose() => _inner.Dispose();
}
