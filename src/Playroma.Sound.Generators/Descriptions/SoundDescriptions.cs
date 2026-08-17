using Playroma.Sound.Generators.Profiles;

namespace Playroma.Sound.Generators.Descriptions;

public enum InterfaceSoundKind { Confirm, Cancel, Hover, Select, Open, Close, Warning, Error, Success, Achievement, Message, CooldownReady, ResourceGained, ResourceLost }
public enum ExplosionEnergyType { Chemical, Plasma, Electrical, Magical, Steam, Implosion }
public enum WeaponMechanism { ProjectileFirearm, EnergyWeapon, MechanicalLauncher, Bow, Crossbow, MeleeSwing, MeleeStrike, Ricochet, DryFire, Reload }
public enum FootwearType { Bare, SoftPaw, Boot, Shoe, Armoured, Hoof, Claw }
public enum FootstepGait { Sneak, Walk, Run, Sprint, Land }
public enum MagicArchetype { Arcane, Fire, Frost, Lightning, Holy, Shadow, Nature, Psychic, Teleportation, Summoning, Healing, Curse }
public enum RetroStyle { PulseConsole, WavetableConsole, FmArcade, HomeComputer, Handheld, Generic8Bit, Generic16Bit }
public enum RetroEventKind { Pickup, Jump, Hit, Shoot, Explosion, PowerUp, Alarm, Menu, Death }
public enum CreatureCallKind { Growl, Roar, Chirp, Hiss, Pain, Fear, Social, Attack, Idle }
public enum PhraseShape { Rising, Falling, Arch, Valley, Zigzag, Trill, CallAndResponse, RepeatedWarning, Question, Answer, Chaotic, ChargeRelease, Cascade }
public enum PhraseMood { Heroic, Mysterious, Cute, Sinister, Ancient, Alien, Holy, Chaotic, Dreamlike, Mechanical }
public enum PhraseScale { Chromatic, Major, NaturalMinor, MajorPentatonic, MinorPentatonic, WholeTone, Diminished }
public enum PhraseArticulation { Pluck, Smooth, Staccato, Legato, Accent, Tremolo }
public enum CreatureCryStyle { Chirp, Bark, Warble, Growl, Roar, Insectoid, Avian, Draconic, Ghostly, Electronic, Random }
public enum ClassicHandheldCryArchetype { Squeak, Chirp, Buzz, Croak, Roar, Wail, Rattle, Electronic, Random }
public enum ClassicHandheldCryContour { Rising, Falling, Arch, Valley, Zigzag, Trill, Broken, Random }
public enum MelodicEffectKind { ItemDiscovery, LevelUp, TreasureReveal, Healing, Teleportation, Transformation, PuzzleSolved, FailureSting, MysteriousChime, MagicalDoor, SummoningPhrase, Enchantment, Curse, Vision }

public sealed record InterfaceSoundDescription
{
    public InterfaceSoundKind Kind { get; init; } = InterfaceSoundKind.Confirm;
    public float Importance { get; init; } = 0.5f;
    public float Friendliness { get; init; } = 0.7f;
    public float Urgency { get; init; } = 0.2f;
    public float Technology { get; init; } = 0.5f;
    public float TonalBalance { get; init; } = 0.8f;
    public float Duration { get; init; } = 0.3f;
    public float Brightness { get; init; } = 0.65f;
    public int NoteCount { get; init; } = 2;
}

public sealed record ImpactDescription
{
    public SoundMaterial MaterialA { get; init; } = SoundMaterial.Steel;
    public SoundMaterial MaterialB { get; init; } = SoundMaterial.Stone;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.SmallRoom;
    public float Mass { get; init; } = 0.6f;
    public float Velocity { get; init; } = 0.6f;
    public float ContactArea { get; init; } = 0.45f;
    public float Glancing { get; init; } = 0.1f;
    public float Breakage { get; init; } = 0.1f;
    public float Distance { get; init; } = 0.2f;
}

public sealed record ExplosionDescription
{
    public ExplosionEnergyType EnergyType { get; init; } = ExplosionEnergyType.Chemical;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.OpenField;
    public float Size { get; init; } = 0.65f;
    public float Containment { get; init; } = 0.2f;
    public float Distance { get; init; } = 0.25f;
    public float Debris { get; init; } = 0.45f;
    public float Sharpness { get; init; } = 0.55f;
    public int SecondaryEvents { get; init; } = 2;
}

public sealed record WeaponDescription
{
    public WeaponMechanism Mechanism { get; init; } = WeaponMechanism.ProjectileFirearm;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.OpenField;
    public float Power { get; init; } = 0.6f;
    public float Size { get; init; } = 0.5f;
    public float MechanicalComplexity { get; init; } = 0.4f;
    public float Technology { get; init; } = 0.5f;
    public float Distance { get; init; } = 0.15f;
    public float Tail { get; init; } = 0.35f;
}

public sealed record FootstepDescription
{
    public FootwearType Footwear { get; init; } = FootwearType.Boot;
    public SoundMaterial Surface { get; init; } = SoundMaterial.Concrete;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.DomesticRoom;
    public FootstepGait Gait { get; init; } = FootstepGait.Walk;
    public float Weight { get; init; } = 0.5f;
    public float Speed { get; init; } = 0.45f;
    public float Wetness { get; init; }
    public float LooseDebris { get; init; } = 0.1f;
    public bool LeftFoot { get; init; }
}

public sealed record MagicDescription
{
    public MagicArchetype Archetype { get; init; } = MagicArchetype.Arcane;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.StoneChamber;
    public float Power { get; init; } = 0.6f;
    public float Stability { get; init; } = 0.55f;
    public float Benevolence { get; init; } = 0.5f;
    public float OrganicTechnology { get; init; } = 0.35f;
    public float CastDuration { get; init; } = 0.45f;
    public float ImpactSize { get; init; } = 0.45f;
    public float SpatialExtent { get; init; } = 0.5f;
}

public sealed record RetroSoundDescription
{
    public RetroStyle Style { get; init; } = RetroStyle.Generic8Bit;
    public RetroEventKind Event { get; init; } = RetroEventKind.Pickup;
    public float Pitch { get; init; } = 0.55f;
    public float Duration { get; init; } = 0.35f;
    public float Brightness { get; init; } = 0.7f;
    public float Dirt { get; init; } = 0.25f;
    public int Notes { get; init; } = 3;
}

public sealed record CreatureVoiceDescription
{
    public CreatureCallKind Call { get; init; } = CreatureCallKind.Growl;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.OpenField;
    public float Size { get; init; } = 0.55f;
    public float ThroatLength { get; init; } = 0.55f;
    public float Aggression { get; init; } = 0.45f;
    public float Fear { get; init; }
    public float Pain { get; init; }
    public float OrganicSynthetic { get; init; } = 0.15f;
    public float Duration { get; init; } = 0.5f;
}


public sealed record CreatureCryDescription
{
    public CreatureCryStyle Style { get; init; } = CreatureCryStyle.Random;
    public PhraseShape Shape { get; init; } = PhraseShape.Chaotic;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.OpenField;
    public float Size { get; init; } = 0.45f;
    public float Cuteness { get; init; } = 0.55f;
    public float Menace { get; init; } = 0.25f;
    public float Energy { get; init; } = 0.7f;
    public float Intelligence { get; init; } = 0.5f;
    public float OrganicSynthetic { get; init; } = 0.25f;
    public float MusicalAnimal { get; init; } = 0.55f;
    public float SmoothRaspy { get; init; } = 0.35f;
    public float PitchMovement { get; init; } = 0.75f;
    public int Syllables { get; init; } = 3;
    public float Repetition { get; init; } = 0.2f;
    public float Duration { get; init; } = 0.35f;
    public float RetroModern { get; init; } = 0.65f;
}

public sealed record ClassicHandheldCryDescription
{
    public ClassicHandheldCryArchetype Archetype { get; init; } = ClassicHandheldCryArchetype.Random;
    public ClassicHandheldCryContour Contour { get; init; } = ClassicHandheldCryContour.Random;
    public float Pitch { get; init; } = 0.58f;
    public float Length { get; init; } = 0.35f;
    public float BodySize { get; init; } = 0.4f;
    public int Syllables { get; init; } = 6;
    public float Complexity { get; init; } = 0.55f;
    public float Sweep { get; init; } = 0.65f;
    public float PulseBlend { get; init; } = 0.6f;
    public float Noise { get; init; } = 0.32f;
    public float Roughness { get; init; } = 0.35f;
    public float Repetition { get; init; } = 0.15f;
    public float Punch { get; init; } = 0.72f;
}

public sealed record MelodicEffectDescription
{
    public MelodicEffectKind Kind { get; init; } = MelodicEffectKind.ItemDiscovery;
    public PhraseMood Mood { get; init; } = PhraseMood.Mysterious;
    public PhraseShape Shape { get; init; } = PhraseShape.Rising;
    public PhraseScale Scale { get; init; } = PhraseScale.MajorPentatonic;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.SmallRoom;
    public float Duration { get; init; } = 0.4f;
    public float Complexity { get; init; } = 0.45f;
    public float Speed { get; init; } = 0.6f;
    public float Pitch { get; init; } = 0.55f;
    public float PitchRange { get; init; } = 0.5f;
    public float Harmony { get; init; } = 0.25f;
    public float TonalDissonant { get; init; } = 0.15f;
    public float AcousticSynthetic { get; init; } = 0.55f;
    public float Brightness { get; init; } = 0.65f;
    public float Repetition { get; init; } = 0.1f;
    public float Tail { get; init; } = 0.35f;
    public int Notes { get; init; } = 6;
}

public sealed record ComplexMagicDescription
{
    public MagicArchetype Archetype { get; init; } = MagicArchetype.Arcane;
    public PhraseMood Mood { get; init; } = PhraseMood.Mysterious;
    public PhraseShape Shape { get; init; } = PhraseShape.ChargeRelease;
    public PhraseScale Scale { get; init; } = PhraseScale.MinorPentatonic;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.StoneChamber;
    public float Power { get; init; } = 0.65f;
    public float Stability { get; init; } = 0.55f;
    public float Benevolence { get; init; } = 0.5f;
    public float OrganicTechnology { get; init; } = 0.35f;
    public float Duration { get; init; } = 0.45f;
    public float Musicality { get; init; } = 0.55f;
    public float Anticipation { get; init; } = 0.35f;
    public float Charge { get; init; } = 0.75f;
    public float Motif { get; init; } = 0.65f;
    public float Release { get; init; } = 0.75f;
    public float Impact { get; init; } = 0.65f;
    public float Afterglow { get; init; } = 0.55f;
    public int MotifNotes { get; init; } = 7;
    public float SpatialExtent { get; init; } = 0.65f;
}
