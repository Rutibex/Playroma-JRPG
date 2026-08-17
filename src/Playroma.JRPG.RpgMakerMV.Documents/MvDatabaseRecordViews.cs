using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public sealed class MvActorView : MvRecordView
{
    public MvActorView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public string Nickname { get => GetString("nickname"); set => SetString("nickname", value); }
    public string Profile { get => GetString("profile"); set => SetString("profile", value); }
    public int ClassId { get => GetInt32("classId"); set => SetInt32("classId", value); }
    public int InitialLevel { get => GetInt32("initialLevel", 1); set => SetInt32("initialLevel", value); }
    public int MaximumLevel { get => GetInt32("maxLevel", 99); set => SetInt32("maxLevel", value); }
    public string CharacterName { get => GetString("characterName"); set => SetString("characterName", value); }
    public string FaceName { get => GetString("faceName"); set => SetString("faceName", value); }
    public string BattlerName { get => GetString("battlerName"); set => SetString("battlerName", value); }
    public MvRawArrayView EquipmentIds => GetRawArray("equips");
    public MvArrayView<MvTraitView> Traits => GetObjectArray("traits", static (document, path) => new MvTraitView(document, path));
}

public sealed class MvClassView : MvRecordView
{
    public MvClassView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public MvRawArrayView ExperienceParameters => GetRawArray("expParams");
    public MvRawArrayView Parameters => GetRawArray("params");
    public MvArrayView<MvClassLearningView> Learnings => GetObjectArray("learnings", static (document, path) => new MvClassLearningView(document, path));
    public MvArrayView<MvTraitView> Traits => GetObjectArray("traits", static (document, path) => new MvTraitView(document, path));
}

public abstract class MvUsableItemView : MvRecordView
{
    protected MvUsableItemView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public string Description { get => GetString("description"); set => SetString("description", value); }
    public int IconIndex { get => GetInt32("iconIndex"); set => SetInt32("iconIndex", value); }
    public int Scope { get => GetInt32("scope"); set => SetInt32("scope", value); }
    public int Occasion { get => GetInt32("occasion"); set => SetInt32("occasion", value); }
    public int Speed { get => GetInt32("speed"); set => SetInt32("speed", value); }
    public int SuccessRate { get => GetInt32("successRate", 100); set => SetInt32("successRate", value); }
    public int AnimationId { get => GetInt32("animationId"); set => SetInt32("animationId", value); }
    public MvDamageView Damage => new(Document, Path.Append("damage"));
    public MvArrayView<MvEffectView> Effects => GetObjectArray("effects", static (document, path) => new MvEffectView(document, path));
}

public sealed class MvSkillView : MvUsableItemView
{
    public MvSkillView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int MpCost { get => GetInt32("mpCost"); set => SetInt32("mpCost", value); }
    public int TpCost { get => GetInt32("tpCost"); set => SetInt32("tpCost", value); }
    public int SkillTypeId { get => GetInt32("stypeId"); set => SetInt32("stypeId", value); }
}

public sealed class MvItemView : MvUsableItemView
{
    public MvItemView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Price { get => GetInt32("price"); set => SetInt32("price", value); }
    public bool Consumable { get => GetBoolean("consumable", true); set => SetBoolean("consumable", value); }
}

public abstract class MvEquipmentView : MvRecordView
{
    protected MvEquipmentView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public string Description { get => GetString("description"); set => SetString("description", value); }
    public int Price { get => GetInt32("price"); set => SetInt32("price", value); }
    public int IconIndex { get => GetInt32("iconIndex"); set => SetInt32("iconIndex", value); }
    public int EquipmentTypeId { get => GetInt32("etypeId"); set => SetInt32("etypeId", value); }
    public MvRawArrayView Parameters => GetRawArray("params");
    public MvArrayView<MvTraitView> Traits => GetObjectArray("traits", static (document, path) => new MvTraitView(document, path));
}

public sealed class MvWeaponView : MvEquipmentView
{
    public MvWeaponView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int WeaponTypeId { get => GetInt32("wtypeId"); set => SetInt32("wtypeId", value); }
    public int AnimationId { get => GetInt32("animationId"); set => SetInt32("animationId", value); }
}

public sealed class MvArmorView : MvEquipmentView
{
    public MvArmorView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int ArmorTypeId { get => GetInt32("atypeId"); set => SetInt32("atypeId", value); }
}

public sealed class MvEnemyView : MvRecordView
{
    public MvEnemyView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public string BattlerName { get => GetString("battlerName"); set => SetString("battlerName", value); }
    public int BattlerHue { get => GetInt32("battlerHue"); set => SetInt32("battlerHue", value); }
    public string Note { get => GetString("note"); set => SetString("note", value); }
    public int Experience { get => GetInt32("exp"); set => SetInt32("exp", value); }
    public int Gold { get => GetInt32("gold"); set => SetInt32("gold", value); }
    public MvRawArrayView Parameters => GetRawArray("params");
    public MvArrayView<MvEnemyActionView> Actions => GetObjectArray("actions", static (document, path) => new MvEnemyActionView(document, path));
    public MvArrayView<MvDropItemView> DropItems => GetObjectArray("dropItems", static (document, path) => new MvDropItemView(document, path));
    public MvArrayView<MvTraitView> Traits => GetObjectArray("traits", static (document, path) => new MvTraitView(document, path));
}

public sealed class MvTroopView : MvRecordView
{
    public MvTroopView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public MvArrayView<MvTroopMemberView> Members => GetObjectArray("members", static (document, path) => new MvTroopMemberView(document, path));
    public MvArrayView<MvTroopEventPageView> Pages => GetObjectArray("pages", static (document, path) => new MvTroopEventPageView(document, path));
}

public sealed class MvStateView : MvRecordView
{
    public MvStateView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Priority { get => GetInt32("priority"); set => SetInt32("priority", value); }
    public int Restriction { get => GetInt32("restriction"); set => SetInt32("restriction", value); }
    public MvArrayView<MvTraitView> Traits => GetObjectArray("traits", static (document, path) => new MvTraitView(document, path));
}

public sealed class MvAnimationView : MvRecordView
{
    public MvAnimationView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public string Animation1Name { get => GetString("animation1Name"); set => SetString("animation1Name", value); }
    public int Animation1Hue { get => GetInt32("animation1Hue"); set => SetInt32("animation1Hue", value); }
    public string Animation2Name { get => GetString("animation2Name"); set => SetString("animation2Name", value); }
    public int Animation2Hue { get => GetInt32("animation2Hue"); set => SetInt32("animation2Hue", value); }
    public int Position { get => GetInt32("position"); set => SetInt32("position", value); }
    public MvRawArrayView Frames => GetRawArray("frames");
    public MvArrayView<MvAnimationTimingView> Timings => GetObjectArray("timings", static (document, path) => new MvAnimationTimingView(document, path));
}

public sealed class MvTilesetView : MvRecordView
{
    public MvTilesetView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Mode { get => GetInt32("mode"); set => SetInt32("mode", value); }
    public MvRawArrayView Flags => GetRawArray("flags");
    public MvRawArrayView TilesetNames => GetRawArray("tilesetNames");
}

public sealed class MvCommonEventView : MvRecordView
{
    public MvCommonEventView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Trigger { get => GetInt32("trigger"); set => SetInt32("trigger", value); }
    public int SwitchId { get => GetInt32("switchId"); set => SetInt32("switchId", value); }
    public MvArrayView<MvEventCommandView> Commands => GetObjectArray("list", static (document, path) => new MvEventCommandView(document, path));
}
