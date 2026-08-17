using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public abstract class MvDatabaseDocument<TView> : IMvTypedDocument<MvIdArray<TView>>
    where TView : MvNodeView
{
    protected MvDatabaseDocument(
        MutableJsonProjectDocument inner,
        Func<MutableJsonProjectDocument, JsonPath, TView> factory)
    {
        Inner = inner;
        Root = new MvIdArray<TView>(inner, JsonPath.Root, factory);
    }

    protected MutableJsonProjectDocument Inner { get; }

    public MvIdArray<TView> Root { get; }
    public string RelativePath => Inner.RelativePath;
    public MvDocumentKind Kind => Inner.Kind;
    public MutableDocumentState State => Inner.State;
    public ContentFingerprint OriginalFingerprint => Inner.OriginalFingerprint;
    public ContentFingerprint? CurrentDiskFingerprint => Inner.CurrentDiskFingerprint;
    public IReadOnlyList<DocumentChange> Changes => Inner.Changes;
    public IReadOnlyList<Playroma.JRPG.Core.ProjectDiagnostic> Diagnostics => Inner.Diagnostics;
    public ReadOnlyMemory<byte> GetOriginalBytes() => Inner.GetOriginalBytes();
    public ValueTask<ReadOnlyMemory<byte>> GenerateBytesAsync(CancellationToken cancellationToken) => Inner.GenerateBytesAsync(cancellationToken);
    public void DiscardChanges() => Inner.DiscardChanges();
    public void Dispose() => Inner.Dispose();
}

public sealed class MvActorsDocument : MvDatabaseDocument<MvActorView>
{ public MvActorsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvActorView(d, p)) { } }
public sealed class MvClassesDocument : MvDatabaseDocument<MvClassView>
{ public MvClassesDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvClassView(d, p)) { } }
public sealed class MvSkillsDocument : MvDatabaseDocument<MvSkillView>
{ public MvSkillsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvSkillView(d, p)) { } }
public sealed class MvItemsDocument : MvDatabaseDocument<MvItemView>
{ public MvItemsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvItemView(d, p)) { } }
public sealed class MvWeaponsDocument : MvDatabaseDocument<MvWeaponView>
{ public MvWeaponsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvWeaponView(d, p)) { } }
public sealed class MvArmorsDocument : MvDatabaseDocument<MvArmorView>
{ public MvArmorsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvArmorView(d, p)) { } }
public sealed class MvEnemiesDocument : MvDatabaseDocument<MvEnemyView>
{ public MvEnemiesDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvEnemyView(d, p)) { } }
public sealed class MvTroopsDocument : MvDatabaseDocument<MvTroopView>
{ public MvTroopsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvTroopView(d, p)) { } }
public sealed class MvStatesDocument : MvDatabaseDocument<MvStateView>
{ public MvStatesDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvStateView(d, p)) { } }
public sealed class MvAnimationsDocument : MvDatabaseDocument<MvAnimationView>
{ public MvAnimationsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvAnimationView(d, p)) { } }
public sealed class MvTilesetsDocument : MvDatabaseDocument<MvTilesetView>
{ public MvTilesetsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvTilesetView(d, p)) { } }
public sealed class MvCommonEventsDocument : MvDatabaseDocument<MvCommonEventView>
{ public MvCommonEventsDocument(MutableJsonProjectDocument inner) : base(inner, static (d, p) => new MvCommonEventView(d, p)) { } }
