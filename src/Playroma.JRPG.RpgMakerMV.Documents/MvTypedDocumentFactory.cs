using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public static class MvTypedDocumentFactory
{
    public static object Wrap(MutableJsonProjectDocument document) => document.Kind switch
    {
        MvDocumentKind.Actors => new MvActorsDocument(document),
        MvDocumentKind.Animations => new MvAnimationsDocument(document),
        MvDocumentKind.Armors => new MvArmorsDocument(document),
        MvDocumentKind.Classes => new MvClassesDocument(document),
        MvDocumentKind.CommonEvents => new MvCommonEventsDocument(document),
        MvDocumentKind.Enemies => new MvEnemiesDocument(document),
        MvDocumentKind.Items => new MvItemsDocument(document),
        MvDocumentKind.Skills => new MvSkillsDocument(document),
        MvDocumentKind.States => new MvStatesDocument(document),
        MvDocumentKind.Tilesets => new MvTilesetsDocument(document),
        MvDocumentKind.Troops => new MvTroopsDocument(document),
        MvDocumentKind.Weapons => new MvWeaponsDocument(document),
        MvDocumentKind.System => new MvSystemDocument(document),
        MvDocumentKind.MapInfos => new MvMapInfosDocument(document),
        MvDocumentKind.Map => new MvMapDocument(document),
        _ => document
    };
}
