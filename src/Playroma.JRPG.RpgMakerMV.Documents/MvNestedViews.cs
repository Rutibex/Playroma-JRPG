using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;

namespace Playroma.JRPG.RpgMakerMV.Documents;

public sealed class MvTraitView : MvNodeView
{
    public MvTraitView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Code { get => GetInt32("code"); set => SetInt32("code", value); }
    public int DataId { get => GetInt32("dataId"); set => SetInt32("dataId", value); }
    public double Value { get => GetDouble("value"); set => SetDouble("value", value); }
}

public sealed class MvEffectView : MvNodeView
{
    public MvEffectView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Code { get => GetInt32("code"); set => SetInt32("code", value); }
    public int DataId { get => GetInt32("dataId"); set => SetInt32("dataId", value); }
    public double Value1 { get => GetDouble("value1"); set => SetDouble("value1", value); }
    public double Value2 { get => GetDouble("value2"); set => SetDouble("value2", value); }
}

public sealed class MvDamageView : MvNodeView
{
    public MvDamageView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public bool Critical { get => GetBoolean("critical"); set => SetBoolean("critical", value); }
    public int ElementId { get => GetInt32("elementId"); set => SetInt32("elementId", value); }
    public string Formula { get => GetString("formula"); set => SetString("formula", value); }
    public int Type { get => GetInt32("type"); set => SetInt32("type", value); }
    public int Variance { get => GetInt32("variance"); set => SetInt32("variance", value); }
}

public sealed class MvClassLearningView : MvNodeView
{
    public MvClassLearningView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Level { get => GetInt32("level"); set => SetInt32("level", value); }
    public string Note { get => GetString("note"); set => SetString("note", value); }
    public int SkillId { get => GetInt32("skillId"); set => SetInt32("skillId", value); }
}

public sealed class MvEnemyActionView : MvNodeView
{
    public MvEnemyActionView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public double ConditionParam1 { get => GetDouble("conditionParam1"); set => SetDouble("conditionParam1", value); }
    public double ConditionParam2 { get => GetDouble("conditionParam2"); set => SetDouble("conditionParam2", value); }
    public int ConditionType { get => GetInt32("conditionType"); set => SetInt32("conditionType", value); }
    public int Rating { get => GetInt32("rating"); set => SetInt32("rating", value); }
    public int SkillId { get => GetInt32("skillId"); set => SetInt32("skillId", value); }
}

public sealed class MvDropItemView : MvNodeView
{
    public MvDropItemView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Kind { get => GetInt32("kind"); set => SetInt32("kind", value); }
    public int DataId { get => GetInt32("dataId"); set => SetInt32("dataId", value); }
    public int Denominator { get => GetInt32("denominator", 1); set => SetInt32("denominator", value); }
}

public sealed class MvTroopMemberView : MvNodeView
{
    public MvTroopMemberView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int EnemyId { get => GetInt32("enemyId"); set => SetInt32("enemyId", value); }
    public int X { get => GetInt32("x"); set => SetInt32("x", value); }
    public int Y { get => GetInt32("y"); set => SetInt32("y", value); }
    public bool Hidden { get => GetBoolean("hidden"); set => SetBoolean("hidden", value); }
}

public sealed class MvTroopPageConditionView : MvNodeView
{
    public MvTroopPageConditionView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public bool TurnEnding { get => GetBoolean("turnEnding"); set => SetBoolean("turnEnding", value); }
    public bool TurnValid { get => GetBoolean("turnValid"); set => SetBoolean("turnValid", value); }
    public int TurnA { get => GetInt32("turnA"); set => SetInt32("turnA", value); }
    public int TurnB { get => GetInt32("turnB"); set => SetInt32("turnB", value); }
    public bool EnemyValid { get => GetBoolean("enemyValid"); set => SetBoolean("enemyValid", value); }
    public int EnemyIndex { get => GetInt32("enemyIndex"); set => SetInt32("enemyIndex", value); }
    public int EnemyHp { get => GetInt32("enemyHp", 50); set => SetInt32("enemyHp", value); }
    public bool ActorValid { get => GetBoolean("actorValid"); set => SetBoolean("actorValid", value); }
    public int ActorId { get => GetInt32("actorId", 1); set => SetInt32("actorId", value); }
    public int ActorHp { get => GetInt32("actorHp", 50); set => SetInt32("actorHp", value); }
    public bool SwitchValid { get => GetBoolean("switchValid"); set => SetBoolean("switchValid", value); }
    public int SwitchId { get => GetInt32("switchId", 1); set => SetInt32("switchId", value); }
}

public sealed class MvTroopEventPageView : MvNodeView
{
    public MvTroopEventPageView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Span { get => GetInt32("span"); set => SetInt32("span", value); }
    public MvTroopPageConditionView Conditions => new(Document, Path.Append("conditions"));
    public MvArrayView<MvEventCommandView> Commands => GetObjectArray("list", static (document, path) => new MvEventCommandView(document, path));
}

public sealed class MvEventPageConditionView : MvNodeView
{
    public MvEventPageConditionView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public bool ActorValid { get => GetBoolean("actorValid"); set => SetBoolean("actorValid", value); }
    public int ActorId { get => GetInt32("actorId", 1); set => SetInt32("actorId", value); }
    public bool ItemValid { get => GetBoolean("itemValid"); set => SetBoolean("itemValid", value); }
    public int ItemId { get => GetInt32("itemId", 1); set => SetInt32("itemId", value); }
    public bool SelfSwitchValid { get => GetBoolean("selfSwitchValid"); set => SetBoolean("selfSwitchValid", value); }
    public string SelfSwitchCharacter { get => GetString("selfSwitchCh", "A"); set => SetString("selfSwitchCh", value); }
    public bool Switch1Valid { get => GetBoolean("switch1Valid"); set => SetBoolean("switch1Valid", value); }
    public int Switch1Id { get => GetInt32("switch1Id", 1); set => SetInt32("switch1Id", value); }
    public bool Switch2Valid { get => GetBoolean("switch2Valid"); set => SetBoolean("switch2Valid", value); }
    public int Switch2Id { get => GetInt32("switch2Id", 1); set => SetInt32("switch2Id", value); }
    public bool VariableValid { get => GetBoolean("variableValid"); set => SetBoolean("variableValid", value); }
    public int VariableId { get => GetInt32("variableId", 1); set => SetInt32("variableId", value); }
    public int VariableValue { get => GetInt32("variableValue"); set => SetInt32("variableValue", value); }
}

public sealed class MvEventImageView : MvNodeView
{
    public MvEventImageView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int TileId { get => GetInt32("tileId"); set => SetInt32("tileId", value); }
    public string CharacterName { get => GetString("characterName"); set => SetString("characterName", value); }
    public int CharacterIndex { get => GetInt32("characterIndex"); set => SetInt32("characterIndex", value); }
    public int Direction { get => GetInt32("direction", 2); set => SetInt32("direction", value); }
    public int Pattern { get => GetInt32("pattern"); set => SetInt32("pattern", value); }
}

public sealed class MvMoveCommandView : MvNodeView
{
    public MvMoveCommandView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Code { get => GetInt32("code"); set => SetInt32("code", value); }
    public MvRawArrayView Parameters => GetRawArray("parameters");
}

public sealed class MvMoveRouteView : MvNodeView
{
    public MvMoveRouteView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public bool Repeat { get => GetBoolean("repeat"); set => SetBoolean("repeat", value); }
    public bool Skippable { get => GetBoolean("skippable"); set => SetBoolean("skippable", value); }
    public bool Wait { get => GetBoolean("wait"); set => SetBoolean("wait", value); }
    public MvArrayView<MvMoveCommandView> Commands => GetObjectArray("list", static (document, path) => new MvMoveCommandView(document, path));
}

public sealed class MvEventPageView : MvNodeView
{
    public MvEventPageView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public MvEventPageConditionView Conditions => new(Document, Path.Append("conditions"));
    public MvEventImageView Image => new(Document, Path.Append("image"));
    public MvMoveRouteView MoveRoute => new(Document, Path.Append("moveRoute"));
    public MvArrayView<MvEventCommandView> Commands => GetObjectArray("list", static (document, path) => new MvEventCommandView(document, path));
    public int MoveType { get => GetInt32("moveType"); set => SetInt32("moveType", value); }
    public int MoveSpeed { get => GetInt32("moveSpeed", 3); set => SetInt32("moveSpeed", value); }
    public int MoveFrequency { get => GetInt32("moveFrequency", 3); set => SetInt32("moveFrequency", value); }
    public int PriorityType { get => GetInt32("priorityType", 1); set => SetInt32("priorityType", value); }
    public int Trigger { get => GetInt32("trigger"); set => SetInt32("trigger", value); }
    public bool WalkAnimation { get => GetBoolean("walkAnime", true); set => SetBoolean("walkAnime", value); }
    public bool StepAnimation { get => GetBoolean("stepAnime"); set => SetBoolean("stepAnime", value); }
    public bool DirectionFix { get => GetBoolean("directionFix"); set => SetBoolean("directionFix", value); }
    public bool Through { get => GetBoolean("through"); set => SetBoolean("through", value); }
}

public sealed class MvMapEncounterView : MvNodeView
{
    public MvMapEncounterView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int TroopId { get => GetInt32("troopId"); set => SetInt32("troopId", value); }
    public int Weight { get => GetInt32("weight", 10); set => SetInt32("weight", value); }
    public MvRawArrayView RegionSet => GetRawArray("regionSet");
}

public sealed class MvAnimationTimingView : MvNodeView
{
    public MvAnimationTimingView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public int Frame { get => GetInt32("frame"); set => SetInt32("frame", value); }
    public int FlashScope { get => GetInt32("flashScope"); set => SetInt32("flashScope", value); }
    public int FlashDuration { get => GetInt32("flashDuration", 5); set => SetInt32("flashDuration", value); }
    public MvRawArrayView FlashColor => GetRawArray("flashColor");
    public bool HasSoundEffect => Document.ContainsPath(Path.Append("se")) && Document.GetRawJson(Path.Append("se")) is not null and not "null";
    public MvAudioFileView SoundEffect => new(Document, Path.Append("se"));
}

public sealed class MvAudioFileView : MvNodeView
{
    public MvAudioFileView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public string Name { get => GetString("name"); set => SetString("name", value); }
    public int Pan { get => GetInt32("pan"); set => SetInt32("pan", value); }
    public int Pitch { get => GetInt32("pitch", 100); set => SetInt32("pitch", value); }
    public int Volume { get => GetInt32("volume", 90); set => SetInt32("volume", value); }
}

public sealed class MvVehicleView : MvNodeView
{
    public MvVehicleView(MutableJsonProjectDocument document, JsonPath path) : base(document, path) { }
    public MvAudioFileView BackgroundMusic => new(Document, Path.Append("bgm"));
    public string CharacterName { get => GetString("characterName"); set => SetString("characterName", value); }
    public int CharacterIndex { get => GetInt32("characterIndex"); set => SetInt32("characterIndex", value); }
    public int StartMapId { get => GetInt32("startMapId"); set => SetInt32("startMapId", value); }
    public int StartX { get => GetInt32("startX"); set => SetInt32("startX", value); }
    public int StartY { get => GetInt32("startY"); set => SetInt32("startY", value); }
}
