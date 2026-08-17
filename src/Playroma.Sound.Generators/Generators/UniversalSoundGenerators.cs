using Playroma.Sound.Core.Generation;
using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Generators;

internal sealed record UniversalSoundTraits(
    HybridSynthesisMode Mode,
    Waveform Waveform,
    ProceduralTextureKind? Texture,
    UniversalPitchPattern Pattern,
    double RootMultiplier = 1,
    double DurationMultiplier = 1,
    float Tonal = .55f,
    float Transient = .35f,
    float Sub = .15f,
    float Tail = .25f,
    float Sweep = 0,
    int Events = 1,
    string NoiseColor = "White");

internal static class UniversalGeneratorSupport
{
    public static UniversalSoundPlan CreatePlan(
        string generatorId,
        int version,
        string key,
        string displayName,
        UniversalSoundDescriptionBase description,
        UniversalSoundTraits traits,
        double minimumDuration,
        double maximumDuration,
        IReadOnlyDictionary<string, string> metadata)
    {
        float size = Semantic.Clamp01(description.Size);
        (double pitchSemitones, float emotionBrightness, float emotionRoughness, float emotionMovement, float emotionIntensity) = EmotionShape(description.Emotion);
        (double energyDuration, float energyIntensity, float energyMovement, float energyBrightness, float energyRoughness) = EnergyShape(description.EnergyState);
        (float conditionBrightness, float conditionRoughness, float conditionDamage) = ConditionShape(description.Condition);
        float brightness = Math.Clamp(description.Brightness + emotionBrightness + energyBrightness + conditionBrightness, 0, 1);
        float roughness = Math.Clamp(description.Roughness + emotionRoughness + energyRoughness + conditionRoughness, 0, 1);
        float movement = Math.Clamp(description.Movement * energyMovement + emotionMovement, 0, 1);
        float intensity = Math.Clamp(description.Intensity * energyIntensity + emotionIntensity, 0, 1);
        float damage = Math.Clamp(description.Damage + conditionDamage, 0, 1);
        double baseRoot = Semantic.Lerp(1_150, 48, size) * Semantic.Lerp(.72, 1.35, brightness) * Math.Pow(2, pitchSemitones / 12);
        double duration = Semantic.Lerp(minimumDuration, maximumDuration, description.Duration) * traits.DurationMultiplier * energyDuration;
        int structureEvents = description.Structure switch
        {
            UniversalPhraseStructure.Single => 1,
            UniversalPhraseStructure.Double => 2,
            UniversalPhraseStructure.Triple => 3,
            UniversalPhraseStructure.CallAndResponse => 4,
            UniversalPhraseStructure.RepeatedMotif => 5,
            UniversalPhraseStructure.BuildAndRelease => 6,
            UniversalPhraseStructure.Escalation => 7,
            UniversalPhraseStructure.FalseEnding => 6,
            UniversalPhraseStructure.Combo => 8,
            UniversalPhraseStructure.Evolving => 10,
            _ => 1
        };

        return new UniversalSoundPlan
        {
            GeneratorId = generatorId,
            GeneratorVersion = version,
            // Stable legacy fallback prefix retained for serialized-request compatibility.
            FallbackId = $"phase8.{key}",
            DisplayName = displayName,
            SynthesisMode = traits.Mode,
            Waveform = traits.Waveform,
            TextureKind = traits.Texture,
            PitchPattern = MapPitchContour(description.PitchContour, traits.Pattern),
            Environment = description.Environment,
            VariationMode = description.VariationMode,
            Articulation = description.Articulation,
            Structure = description.Structure,
            RhythmStyle = description.RhythmStyle,
            EnvelopeStyle = description.EnvelopeStyle,
            Emotion = description.Emotion,
            EnergyState = description.EnergyState,
            Condition = description.Condition,
            StereoStyle = description.StereoStyle,
            RootFrequencyHz = Math.Clamp(baseRoot * traits.RootMultiplier, 12, 18_000),
            DurationSeconds = Math.Clamp(duration, .02, 90),
            Intensity = intensity,
            Size = size,
            Brightness = brightness,
            Roughness = roughness,
            TonalNoise = Math.Clamp((Semantic.Clamp01(description.TonalNoise) + traits.Tonal) * .5f, 0, 1),
            OrganicSynthetic = Semantic.Clamp01(description.OrganicSynthetic),
            Complexity = Semantic.Clamp01(description.Complexity),
            Movement = movement,
            Distance = Semantic.Clamp01(description.Distance),
            Damage = damage,
            Density = Math.Clamp((description.Complexity * .58f) + (intensity * .42f), 0, 1),
            PitchSweepSemitones = traits.Sweep,
            PulseWidth = Math.Clamp(.18f + (description.OrganicSynthetic * .62f), .05f, .95f),
            Modulation = Math.Clamp((description.Complexity * .55f) + (roughness * .35f), 0, 1),
            Feedback = Math.Clamp((roughness * .55f) + (damage * .35f), 0, 1),
            Damping = Math.Clamp(.85f - (brightness * .42f) + (description.Size * .12f), 0, 1),
            Spread = Math.Clamp((movement * .55f) + (description.Complexity * .35f), 0, 1),
            Harmonics = 3 + (int)Math.Round(description.Complexity * 21),
            EventCount = Math.Max(traits.Events, structureEvents),
            TransientAmount = Math.Clamp((traits.Transient + intensity * .45f) * .7f, 0, 1),
            SubAmount = Math.Clamp(traits.Sub + (description.Size * .25f), 0, 1),
            TailAmount = Math.Clamp(traits.Tail + (movement * .2f), 0, 1),
            StereoMovement = StereoAmount(description.StereoStyle, movement, description.Complexity),
            NoiseColor = traits.NoiseColor,
            Metadata = metadata
        };
    }

    private static UniversalPitchPattern MapPitchContour(UniversalPitchContour contour, UniversalPitchPattern fallback) => contour switch
    {
        UniversalPitchContour.Automatic => fallback,
        UniversalPitchContour.Flat => UniversalPitchPattern.Flat,
        UniversalPitchContour.Rising => UniversalPitchPattern.Rising,
        UniversalPitchContour.Falling => UniversalPitchPattern.Falling,
        UniversalPitchContour.Arch => UniversalPitchPattern.Arch,
        UniversalPitchContour.Valley => UniversalPitchPattern.Valley,
        UniversalPitchContour.Zigzag => UniversalPitchPattern.Zigzag,
        UniversalPitchContour.Alarm => UniversalPitchPattern.Alarm,
        UniversalPitchContour.Trill => UniversalPitchPattern.Trill,
        UniversalPitchContour.Bounce => UniversalPitchPattern.Bounce,
        UniversalPitchContour.Cascade => UniversalPitchPattern.Cascade,
        UniversalPitchContour.Question => UniversalPitchPattern.Question,
        UniversalPitchContour.CallAndResponse => UniversalPitchPattern.CallAndResponse,
        UniversalPitchContour.Evolving => UniversalPitchPattern.Evolving,
        UniversalPitchContour.Random => UniversalPitchPattern.Random,
        UniversalPitchContour.Prime => UniversalPitchPattern.Prime,
        _ => fallback
    };

    private static (double Pitch, float Brightness, float Roughness, float Movement, float Intensity) EmotionShape(UniversalEmotion emotion) => emotion switch
    {
        UniversalEmotion.Happy => (3, .08f, -.05f, .08f, .04f),
        UniversalEmotion.Playful => (5, .1f, -.02f, .18f, .05f),
        UniversalEmotion.Curious => (2, .04f, 0, .1f, 0),
        UniversalEmotion.Nervous => (3, .04f, .08f, .18f, -.04f),
        UniversalEmotion.Frightened => (6, .1f, .12f, .25f, .08f),
        UniversalEmotion.Angry => (-2, -.04f, .14f, .12f, .12f),
        UniversalEmotion.Enraged => (-5, -.08f, .25f, .2f, .2f),
        UniversalEmotion.Sad => (-3, -.12f, -.02f, -.12f, -.12f),
        UniversalEmotion.Exhausted => (-4, -.1f, .04f, -.2f, -.22f),
        UniversalEmotion.Confident => (0, .03f, -.03f, 0, .06f),
        UniversalEmotion.Majestic => (-5, .02f, -.04f, -.04f, .1f),
        UniversalEmotion.Sinister => (-4, -.15f, .12f, -.04f, .05f),
        UniversalEmotion.Confused => (1, 0, .08f, .16f, -.02f),
        UniversalEmotion.Desperate => (2, .02f, .16f, .22f, .12f),
        UniversalEmotion.Triumphant => (5, .12f, -.04f, .12f, .15f),
        _ => (0, 0, 0, 0, 0)
    };

    private static (double Duration, float Intensity, float Movement, float Brightness, float Roughness) EnergyShape(UniversalEnergyState energy) => energy switch
    {
        UniversalEnergyState.Weak => (.9, .55f, .55f, -.08f, .02f),
        UniversalEnergyState.Sleepy => (1.35, .62f, .35f, -.12f, -.03f),
        UniversalEnergyState.Calm => (1.15, .82f, .7f, -.03f, -.04f),
        UniversalEnergyState.Energetic => (.9, 1.12f, 1.25f, .07f, .02f),
        UniversalEnergyState.Frantic => (.78, 1.25f, 1.55f, .1f, .12f),
        UniversalEnergyState.Overcharged => (.88, 1.3f, 1.25f, .16f, .1f),
        UniversalEnergyState.Unstable => (1, 1.12f, 1.35f, .05f, .2f),
        UniversalEnergyState.Dying => (1.4, .45f, .45f, -.18f, .15f),
        UniversalEnergyState.Recovering => (1.15, .76f, .75f, .03f, -.02f),
        _ => (1, 1, 1, 0, 0)
    };

    private static (float Brightness, float Roughness, float Damage) ConditionShape(UniversalCondition condition) => condition switch
    {
        UniversalCondition.New => (.08f, -.08f, -.05f),
        UniversalCondition.Polished => (.14f, -.1f, -.05f),
        UniversalCondition.Worn => (-.04f, .12f, .12f),
        UniversalCondition.Rusty => (-.1f, .25f, .25f),
        UniversalCondition.Damaged => (-.08f, .22f, .45f),
        UniversalCondition.Ancient => (-.12f, .18f, .28f),
        UniversalCondition.Corrupted => (-.06f, .3f, .62f),
        UniversalCondition.Wet => (-.14f, .05f, .08f),
        UniversalCondition.Frozen => (.12f, .08f, .12f),
        UniversalCondition.Burning => (.1f, .2f, .22f),
        _ => (0, 0, 0)
    };

    private static float StereoAmount(UniversalStereoStyle style, float movement, float complexity) => style switch
    {
        UniversalStereoStyle.Mono => 0,
        UniversalStereoStyle.Narrow => .1f,
        UniversalStereoStyle.Wide => .5f,
        UniversalStereoStyle.Moving => .82f,
        UniversalStereoStyle.Alternating => .68f,
        _ => Math.Clamp(movement * .65f + complexity * .15f, 0, 1)
    };

    public static IReadOnlyDictionary<string, string> Meta(string kind, string value) =>
        new Dictionary<string, string> { [kind] = value };
}

public sealed class CreatureActionGenerator : ISoundGenerator<CreatureActionDescription>
{
    public const string Id = "playroma.creature-action";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Creature Action", SoundGeneratorCategory.CreatureAction,
        "Creature movement and gameplay actions: spawning, attacks, damage, death, evolution, flight, burrowing, slime motion, and more.",
        .05, 12, ["creature", "action", "monster", "animation"]);

    public SoundGraphDefinition Generate(CreatureActionDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        UniversalSoundTraits action = description.Action switch
        {
            CreatureActionKind.Spawn or CreatureActionKind.Awaken => new(HybridSynthesisMode.Granular, Waveform.Sine, ProceduralTextureKind.Organic, UniversalPitchPattern.Rising, 1, 1.2, .62f, .28f, .18f, .55f, 12, 5),
            CreatureActionKind.NoticePlayer => new(HybridSynthesisMode.Formant, Waveform.Saw, null, UniversalPitchPattern.Question, 1.15, .8, .72f, .25f, .12f, .18f, 7, 3),
            CreatureActionKind.Idle or CreatureActionKind.Sniff or CreatureActionKind.Eat or CreatureActionKind.Sleep => new(HybridSynthesisMode.Formant, Waveform.Sine, ProceduralTextureKind.Organic, UniversalPitchPattern.Flat, .82, 1.2, .48f, .12f, .24f, .12f, -2, 2, "Pink"),
            CreatureActionKind.Attack or CreatureActionKind.HeavyAttack => new(HybridSynthesisMode.Formant, Waveform.Saw, ProceduralTextureKind.Organic, UniversalPitchPattern.Falling, .65, .8, .6f, .78f, .55f, .22f, -11, 3, "Brown"),
            CreatureActionKind.Cast => new(HybridSynthesisMode.FrequencyModulation, Waveform.Sine, ProceduralTextureKind.Space, UniversalPitchPattern.Arch, 1.2, 1.25, .78f, .22f, .15f, .62f, 16, 6),
            CreatureActionKind.TakeDamage or CreatureActionKind.Stagger => new(HybridSynthesisMode.Formant, Waveform.Saw, null, UniversalPitchPattern.Falling, .75, .48, .5f, .82f, .28f, .08f, -8, 2, "White"),
            CreatureActionKind.Flee => new(HybridSynthesisMode.Formant, Waveform.Pulse, null, UniversalPitchPattern.Rising, 1.4, .65, .72f, .3f, .06f, .12f, 10, 4),
            CreatureActionKind.Die => new(HybridSynthesisMode.Formant, Waveform.Saw, ProceduralTextureKind.Organic, UniversalPitchPattern.Cascade, .52, 1.8, .46f, .58f, .62f, .62f, -20, 6, "Brown"),
            CreatureActionKind.Revive or CreatureActionKind.Evolve or CreatureActionKind.Transform or CreatureActionKind.LevelUp => new(HybridSynthesisMode.Additive, Waveform.Sine, ProceduralTextureKind.Space, UniversalPitchPattern.Rising, 1.1, 1.6, .82f, .22f, .22f, .72f, 24, 8),
            CreatureActionKind.Enrage or CreatureActionKind.CallAllies => new(HybridSynthesisMode.Formant, Waveform.Saw, ProceduralTextureKind.Organic, UniversalPitchPattern.CallAndResponse, .55, 1.45, .62f, .65f, .72f, .38f, -10, 6, "Brown"),
            CreatureActionKind.WingFlap or CreatureActionKind.Land => new(HybridSynthesisMode.NoiseResonator, Waveform.Triangle, ProceduralTextureKind.Wind, UniversalPitchPattern.Falling, .45, .7, .25f, .82f, .42f, .15f, -5, 2, "Pink"),
            CreatureActionKind.Burrow => new(HybridSynthesisMode.NoiseResonator, Waveform.Saw, ProceduralTextureKind.Dust, UniversalPitchPattern.Falling, .35, 1.25, .18f, .75f, .38f, .12f, -6, 4, "Brown"),
            CreatureActionKind.Swim => new(HybridSynthesisMode.Formant, Waveform.Sine, ProceduralTextureKind.Underwater, UniversalPitchPattern.Zigzag, .8, 1.2, .45f, .3f, .2f, .3f, 5, 4, "Pink"),
            CreatureActionKind.SlimeSplit or CreatureActionKind.SlimeReform => new(HybridSynthesisMode.Granular, Waveform.Sine, ProceduralTextureKind.Organic, UniversalPitchPattern.Valley, .7, 1.1, .42f, .55f, .22f, .18f, description.Action == CreatureActionKind.SlimeSplit ? -9 : 9, 5, "Pink"),
            _ => new(HybridSynthesisMode.Formant, Waveform.Saw, ProceduralTextureKind.Organic, UniversalPitchPattern.Zigzag)
        };
        double bodyMultiplier = description.BodyType switch
        {
            CreatureBodyType.TinyMammal or CreatureBodyType.Avian or CreatureBodyType.Insectoid => 1.8,
            CreatureBodyType.Dragon or CreatureBodyType.Giant => .42,
            CreatureBodyType.Aquatic or CreatureBodyType.Slime => .72,
            CreatureBodyType.Mechanical => 1.1,
            _ => 1
        };
        ProceduralTextureKind? texture = description.BodyType switch
        {
            CreatureBodyType.Insectoid => ProceduralTextureKind.Insects,
            CreatureBodyType.Aquatic => ProceduralTextureKind.Underwater,
            CreatureBodyType.Mechanical => ProceduralTextureKind.Machine,
            CreatureBodyType.Elemental => ProceduralTextureKind.Electrical,
            CreatureBodyType.Undead => ProceduralTextureKind.Radio,
            _ => action.Texture
        };
        action = action with { RootMultiplier = action.RootMultiplier * bodyMultiplier, Texture = texture };
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Action.ToString().ToLowerInvariant(), $"{description.BodyType} {description.Action}", description, action, .06, 5,
            new Dictionary<string, string> { ["action"] = description.Action.ToString(), ["bodyType"] = description.BodyType.ToString() });
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class CombatSequenceGenerator : ISoundGenerator<CombatSequenceDescription>
{
    public const string Id = "playroma.combat-sequence";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Combat Sequence", SoundGeneratorCategory.CombatSequence,
        "Complete combat gestures combining motion, mechanism, projectile or energy body, target impact, and environmental tail.",
        .04, 12, ["combat", "weapon", "melee", "gun", "impact"]);

    public SoundGraphDefinition Generate(CombatSequenceDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool firearm = description.Action is CombatSequenceKind.PistolShot or CombatSequenceKind.RevolverShot or CombatSequenceKind.RifleShot
            or CombatSequenceKind.ShotgunBlast or CombatSequenceKind.MachineGunBurst or CombatSequenceKind.SniperShot or CombatSequenceKind.SuppressedShot;
        bool energy = description.Action is CombatSequenceKind.LaserPulse or CombatSequenceKind.PlasmaBolt or CombatSequenceKind.Railgun
            or CombatSequenceKind.ParticleBeam or CombatSequenceKind.IonCannon or CombatSequenceKind.SonicWeapon or CombatSequenceKind.GravityWeapon;
        bool defense = description.Action is CombatSequenceKind.Block or CombatSequenceKind.Parry or CombatSequenceKind.Dodge or CombatSequenceKind.PerfectGuard;
        bool explosive = description.Action is CombatSequenceKind.Grenade or CombatSequenceKind.Rocket or CombatSequenceKind.Missile;
        bool combo = description.Action is CombatSequenceKind.Combo or CombatSequenceKind.Execution or CombatSequenceKind.Finisher;
        UniversalSoundTraits traits = firearm
            ? new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Falling, .45, .55, .38f, .95f, .65f, .32f, -8, description.Action == CombatSequenceKind.MachineGunBurst ? 7 : 2, "White")
            : energy
                ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, ProceduralTextureKind.Electrical, UniversalPitchPattern.Rising, 1.25, .9, .78f, .48f, .2f, .62f, 18, 3, "Violet")
                : explosive
                    ? new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Fire, UniversalPitchPattern.Falling, .28, 1.7, .25f, 1f, .95f, .72f, -18, 5, "Brown")
                    : defense
                        ? new(HybridSynthesisMode.Modal, Waveform.Triangle, null, UniversalPitchPattern.Bounce, 1.05, .55, .58f, .88f, .35f, .42f, 5, 2, "White")
                        : combo
                            ? new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Wind, UniversalPitchPattern.Zigzag, .78, 1.25, .52f, .82f, .5f, .35f, -5, 8, "White")
                            : new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Wind, UniversalPitchPattern.Falling, .72, .65, .45f, .78f, .42f, .2f, -7, 3, "Pink");
        double targetPitch = description.Target switch
        {
            TargetMaterial.Glass or TargetMaterial.Crystal => 2.2,
            TargetMaterial.Steel or TargetMaterial.Armour or TargetMaterial.Shield => 1.35,
            TargetMaterial.Flesh or TargetMaterial.Bone => .62,
            TargetMaterial.Stone or TargetMaterial.Concrete => .48,
            TargetMaterial.EnergyBarrier => 1.8,
            _ => 1
        };
        traits = traits with { RootMultiplier = traits.RootMultiplier * targetPitch };
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Action.ToString().ToLowerInvariant(), description.Action.ToString(), description, traits, .05, 4.5,
            new Dictionary<string, string> { ["action"] = description.Action.ToString(), ["target"] = description.Target.ToString() });
        plan = plan with
        {
            Complexity = Math.Clamp((plan.Complexity + description.MechanicalDetail) * .5f, 0, 1),
            Density = Math.Clamp(description.MechanicalDetail, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class DestructionGenerator : ISoundGenerator<DestructionDescription>
{
    public const string Id = "playroma.destruction";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Destruction", SoundGeneratorCategory.Destruction,
        "Breakage, collapse, debris, structural failure, crashes, hull breaches, and impacts from object scale to planetary scale.",
        .08, 30, ["destruction", "break", "collapse", "debris"]);

    public SoundGraphDefinition Generate(DestructionDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        UniversalSoundTraits traits = description.Kind switch
        {
            DestructionKind.GlassBreak or DestructionKind.CrystalBurst => new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Dust, UniversalPitchPattern.Cascade, 3.1, .8, .36f, 1f, .05f, .42f, -15, 8, "Violet"),
            DestructionKind.CeramicSmash or DestructionKind.IceShatter => new(HybridSynthesisMode.Modal, Waveform.Triangle, ProceduralTextureKind.Dust, UniversalPitchPattern.Cascade, 2.1, .9, .3f, .95f, .12f, .3f, -12, 7, "White"),
            DestructionKind.WoodSplinter or DestructionKind.DoorBreach => new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Dust, UniversalPitchPattern.Falling, .8, 1.05, .34f, .9f, .42f, .25f, -8, 5, "Brown"),
            DestructionKind.MetalBend or DestructionKind.MetalTear or DestructionKind.HullBreach => new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Falling, .55, 1.5, .48f, .72f, .62f, .55f, -16, 6, "Pink"),
            DestructionKind.StoneCrack or DestructionKind.ConcreteCollapse or DestructionKind.Rockfall or DestructionKind.RubbleSettle => new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Dust, UniversalPitchPattern.Cascade, .32, 1.8, .18f, .88f, .95f, .48f, -12, 7, "Brown"),
            DestructionKind.WallCollapse or DestructionKind.CeilingCaveIn or DestructionKind.BridgeFailure or DestructionKind.TowerCollapse => new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Dust, UniversalPitchPattern.Cascade, .18, 2.6, .16f, 1f, 1f, .68f, -20, 10, "Brown"),
            DestructionKind.VehicleCrash => new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Zigzag, .38, 1.5, .3f, 1f, .82f, .5f, -14, 7, "White"),
            DestructionKind.PlanetaryImpact => new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Space, UniversalPitchPattern.Falling, .07, 4, .18f, 1f, 1f, 1f, -30, 12, "Brown"),
            _ => new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Dust, UniversalPitchPattern.Falling, .65, 1, .3f, .8f, .5f, .3f, -9, 5, "White")
        };
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .08, 8,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            Density = Math.Clamp(description.Debris, 0, 1),
            EventCount = Math.Max(plan.EventCount, Math.Clamp(description.SecondaryEvents + 1, 1, context.Budget.MaximumPhraseNotes))
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class MaterialFoleyGenerator : ISoundGenerator<MaterialFoleyDescription>
{
    public const string Id = "playroma.material-foley";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Material Foley", SoundGeneratorCategory.MaterialFoley,
        "General material interaction: taps, drops, scrapes, bends, tears, rattles, rustles, locks, handling, and object movement.",
        .02, 12, ["foley", "material", "object", "handling"]);

    public SoundGraphDefinition Generate(MaterialFoleyDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        SoundMaterialProfile a = MaterialProfiles.Get(description.MaterialA);
        SoundMaterialProfile b = MaterialProfiles.Get(description.MaterialB);
        bool sustained = description.Action is FoleyActionKind.Scrape or FoleyActionKind.Grind or FoleyActionKind.Bend or FoleyActionKind.Stretch
            or FoleyActionKind.Tear or FoleyActionKind.Rustle or FoleyActionKind.Flap or FoleyActionKind.Rattle or FoleyActionKind.Shake;
        bool ringing = description.Action is FoleyActionKind.Clink or FoleyActionKind.Clang or FoleyActionKind.Ring;
        UniversalSoundTraits traits = ringing
            ? new(HybridSynthesisMode.Modal, Waveform.Sine, null, UniversalPitchPattern.Bounce, (a.FundamentalHz + b.FundamentalHz) / 440, .85, .72f, .75f, .18f, .48f, -3, 3, "White")
            : sustained
                ? new(HybridSynthesisMode.NoiseResonator, Waveform.Saw, ProceduralTextureKind.Dust, UniversalPitchPattern.Flat, Math.Sqrt(a.FundamentalHz * b.FundamentalHz) / 440, 1.5, .28f, .55f, .16f, .2f, -4, 4, "Pink")
                : new(HybridSynthesisMode.Modal, Waveform.Triangle, null, UniversalPitchPattern.Falling, Math.Sqrt(a.FundamentalHz * b.FundamentalHz) / 440, .65, .48f, .82f, .25f, .22f, -5, 2, "White");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            $"{description.MaterialA}-{description.Action}".ToLowerInvariant(), $"{description.MaterialA} {description.Action}", description, traits, .025, 3,
            new Dictionary<string, string> { ["action"] = description.Action.ToString(), ["materialA"] = description.MaterialA.ToString(), ["materialB"] = description.MaterialB.ToString() });
        plan = plan with
        {
            Damping = Math.Clamp((float)((a.Damping + b.Damping) * .5), 0, 1),
            Roughness = Math.Clamp((float)((a.Roughness + b.Roughness) * .5) + description.Roughness * .35f, 0, 1),
            TransientAmount = Math.Clamp((float)((a.TransientSharpness + b.TransientSharpness) * .5) + description.Intensity * .25f, 0, 1),
            Density = Math.Clamp(description.ContactArea, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class MovementGenerator : ISoundGenerator<MovementDescription>
{
    public const string Id = "playroma.movement";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Movement & Traversal", SoundGeneratorCategory.Movement,
        "Footstep runs, jumps, landings, climbing, sliding, dashes, rolls, grapples, jetpacks, hovering, swimming, flying, and equipment movement.",
        .04, 30, ["movement", "footsteps", "traversal", "foley"]);

    public SoundGraphDefinition Generate(MovementDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool airborne = description.MovementKind is MovementKind.Fly or MovementKind.Glide or MovementKind.Jetpack or MovementKind.Hover or MovementKind.GrappleSwing;
        bool watery = description.MovementKind == MovementKind.Swim;
        bool special = description.MovementKind is MovementKind.TeleportStep or MovementKind.Dash or MovementKind.DoubleJump or MovementKind.WallJump;
        UniversalSoundTraits traits = watery
            ? new(HybridSynthesisMode.NoiseResonator, Waveform.Sine, ProceduralTextureKind.Underwater, UniversalPitchPattern.Zigzag, .75, 1.4, .28f, .55f, .18f, .25f, 3, 5, "Pink")
            : airborne
                ? new(HybridSynthesisMode.NoiseResonator, Waveform.Triangle, description.MovementKind == MovementKind.Jetpack ? ProceduralTextureKind.Fire : ProceduralTextureKind.Wind, UniversalPitchPattern.Rising, .65, 1.5, .25f, .58f, .22f, .28f, 9, 4, "Pink")
                : special
                    ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, ProceduralTextureKind.Wind, UniversalPitchPattern.Rising, 1.3, .7, .62f, .55f, .1f, .35f, 16, 3, "Violet")
                    : new(HybridSynthesisMode.Modal, Waveform.Triangle, ProceduralTextureKind.Dust, UniversalPitchPattern.Zigzag, .62, 1, .28f, .82f, .38f, .12f, -3, Math.Clamp(description.Steps, 1, 24), "Brown");
        double mover = description.Mover switch
        {
            MoverType.Tiny or MoverType.Talons => 1.8,
            MoverType.Giant => .35,
            MoverType.RobotFeet or MoverType.Armoured => .62,
            MoverType.Hooves => .82,
            _ => 1
        };
        traits = traits with { RootMultiplier = traits.RootMultiplier * mover };
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.MovementKind.ToString().ToLowerInvariant(), $"{description.Mover} {description.MovementKind}", description, traits, .05, 5,
            new Dictionary<string, string> { ["movement"] = description.MovementKind.ToString(), ["mover"] = description.Mover.ToString(), ["surface"] = description.Surface.ToString() });
        plan = plan with
        {
            EventCount = Math.Max(plan.EventCount, Math.Clamp(description.Steps, 1, context.Budget.MaximumPhraseNotes)),
            Density = Math.Clamp(description.Equipment + description.Wetness * .35f, 0, 1),
            TonalNoise = Math.Clamp(plan.TonalNoise - description.Wetness * .25f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class LiquidGenerator : ISoundGenerator<LiquidDescription>
{
    public const string Id = "playroma.liquid";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Water & Liquids", SoundGeneratorCategory.Liquid,
        "Drops, splashes, pours, streams, waves, bubbles, boiling, freezing, spraying, draining, suction, sonar, and underwater events.",
        .03, 30, ["water", "liquid", "splash", "underwater"]);

    public SoundGraphDefinition Generate(LiquidDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool underwater = description.Kind is LiquidSoundKind.UnderwaterMovement or LiquidSoundKind.UnderwaterImpact or LiquidSoundKind.UnderwaterExplosion or LiquidSoundKind.Sonar;
        bool continuous = description.Kind is LiquidSoundKind.Stream or LiquidSoundKind.River or LiquidSoundKind.Waterfall or LiquidSoundKind.OceanWave
            or LiquidSoundKind.Pour or LiquidSoundKind.Boil or LiquidSoundKind.Spray or LiquidSoundKind.Drain;
        bool bubbles = description.Kind is LiquidSoundKind.Bubble or LiquidSoundKind.Gurgle or LiquidSoundKind.Boil;
        UniversalSoundTraits traits = description.Kind == LiquidSoundKind.Sonar
            ? new(HybridSynthesisMode.Basic, Waveform.Sine, ProceduralTextureKind.Underwater, UniversalPitchPattern.Falling, 2.2, 1.3, .9f, .08f, .05f, .82f, -6, 2, "Pink")
            : bubbles
                ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Sine, ProceduralTextureKind.Water, UniversalPitchPattern.Rising, 1.25, 1.1, .55f, .38f, .08f, .18f, 8, 6, "Pink")
                : continuous
                    ? new(HybridSynthesisMode.NoiseResonator, Waveform.Sine, ProceduralTextureKind.Water, UniversalPitchPattern.Flat, .7, 2.2, .18f, .35f, .12f, .25f, 1, 3, "Pink")
                    : new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Water, UniversalPitchPattern.Valley, 1, .65, .45f, .78f, .12f, .2f, -4, 3, "White");
        if (underwater) traits = traits with { Texture = ProceduralTextureKind.Underwater, RootMultiplier = traits.RootMultiplier * .65, Tail = Math.Max(traits.Tail, .55f) };
        double liquidPitch = description.Liquid switch
        {
            LiquidType.Lava or LiquidType.Mud or LiquidType.Syrup => .48,
            LiquidType.Oil or LiquidType.Slime => .65,
            LiquidType.Acid or LiquidType.Potion => 1.3,
            LiquidType.AlienFluid => 1.6,
            _ => 1
        };
        traits = traits with { RootMultiplier = traits.RootMultiplier * liquidPitch };
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), $"{description.Liquid} {description.Kind}", description, traits, .04, 8,
            new Dictionary<string, string> { ["kind"] = description.Kind.ToString(), ["liquid"] = description.Liquid.ToString() });
        plan = plan with
        {
            Damping = Math.Clamp(.35f + description.Viscosity * .58f, 0, 1),
            Density = Math.Clamp(.2f + description.Viscosity * .45f + description.Complexity * .25f, 0, 1),
            Modulation = Math.Clamp(plan.Modulation + description.ContainerResonance * .35f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class ElementalEnergyGenerator : ISoundGenerator<ElementalEnergyDescription>
{
    public const string Id = "playroma.elemental-energy";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Elemental & Energy", SoundGeneratorCategory.ElementalEnergy,
        "Fire, electricity, lightning, plasma, beams, shields, reactors, antimatter, void energy, frost, ice, steam, and thermal change.",
        .02, 30, ["fire", "electricity", "energy", "elemental"]);

    public SoundGraphDefinition Generate(ElementalEnergyDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool fire = description.Kind is ElementalEnergyKind.MatchStrike or ElementalEnergyKind.Candle or ElementalEnergyKind.Torch
            or ElementalEnergyKind.Campfire or ElementalEnergyKind.Fireplace or ElementalEnergyKind.Inferno or ElementalEnergyKind.Wildfire
            or ElementalEnergyKind.Flamethrower or ElementalEnergyKind.MagicalFlame;
        bool electric = description.Kind is ElementalEnergyKind.StaticSnap or ElementalEnergyKind.Spark or ElementalEnergyKind.ElectricalArc
            or ElementalEnergyKind.TransformerBuzz or ElementalEnergyKind.Lightning or ElementalEnergyKind.ShortCircuit or ElementalEnergyKind.TeslaDischarge;
        bool cold = description.Kind is ElementalEnergyKind.FrostSpread or ElementalEnergyKind.IceForm or ElementalEnergyKind.IceMelt;
        bool massive = description.Kind is ElementalEnergyKind.ReactorCore or ElementalEnergyKind.Antimatter or ElementalEnergyKind.VoidEnergy;
        UniversalSoundTraits traits = fire
            ? new(HybridSynthesisMode.NoiseResonator, Waveform.Saw, ProceduralTextureKind.Fire, UniversalPitchPattern.Rising, .75, 1.6, .28f, .58f, .42f, .35f, 6, 4, "Brown")
            : electric
                ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, ProceduralTextureKind.Electrical, UniversalPitchPattern.Zigzag, 1.8, .85, .62f, .72f, .18f, .42f, 18, 5, "Violet")
                : cold
                    ? new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Dust, UniversalPitchPattern.Rising, 2.1, 1.3, .72f, .45f, .12f, .58f, 10, 6, "White")
                    : massive
                        ? new(HybridSynthesisMode.Additive, Waveform.Sine, ProceduralTextureKind.Space, UniversalPitchPattern.Arch, .22, 2.6, .65f, .45f, 1f, .92f, 8, 8, "Pink")
                        : new(HybridSynthesisMode.FrequencyModulation, Waveform.Sine, ProceduralTextureKind.Electrical, UniversalPitchPattern.Rising, 1.25, 1.2, .76f, .42f, .2f, .62f, 16, 5, "Violet");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .03, 8,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            Modulation = Math.Clamp(plan.Modulation + description.Instability * .55f, 0, 1),
            Roughness = Math.Clamp(plan.Roughness + description.Instability * .35f, 0, 1),
            TailAmount = Math.Clamp(plan.TailAmount + description.Tail * .45f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class EnvironmentSoundGenerator : ISoundGenerator<EnvironmentDescription>
{
    public const string Id = "playroma.environment";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Environment & Ambience", SoundGeneratorCategory.Environment,
        "Weather, natural biomes, settlements, interiors, fantasy spaces, science-fiction spaces, and evolving environmental soundscapes.",
        .5, 90, ["ambience", "environment", "weather", "soundscape"]);

    public SoundGraphDefinition Generate(EnvironmentDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ProceduralTextureKind texture = description.Kind switch
        {
            EnvironmentKind.Drizzle or EnvironmentKind.SteadyRain or EnvironmentKind.Downpour or EnvironmentKind.RoofRain or EnvironmentKind.WindowRain or EnvironmentKind.Hail => ProceduralTextureKind.Rain,
            EnvironmentKind.Forest or EnvironmentKind.Jungle or EnvironmentKind.Meadow or EnvironmentKind.MagicForest => ProceduralTextureKind.Forest,
            EnvironmentKind.Coast or EnvironmentKind.Riverbank or EnvironmentKind.Swamp or EnvironmentKind.Geyser => ProceduralTextureKind.Water,
            EnvironmentKind.Spaceship or EnvironmentKind.SpaceStation or EnvironmentKind.AlienPlanet or EnvironmentKind.DreamRealm => ProceduralTextureKind.Space,
            EnvironmentKind.ModernCity or EnvironmentKind.CyberpunkStreet or EnvironmentKind.IndustrialDistrict or EnvironmentKind.Warehouse or EnvironmentKind.ReactorChamber => ProceduralTextureKind.Machine,
            EnvironmentKind.Village or EnvironmentKind.MedievalTown or EnvironmentKind.Suburb or EnvironmentKind.Cathedral => ProceduralTextureKind.Crowd,
            EnvironmentKind.DistantThunder or EnvironmentKind.CloseThunder or EnvironmentKind.RollingThunder or EnvironmentKind.Earthquake or EnvironmentKind.VolcanicTremor or EnvironmentKind.Eruption => ProceduralTextureKind.Fire,
            _ => ProceduralTextureKind.Wind
        };
        bool thunderous = description.Kind is EnvironmentKind.DistantThunder or EnvironmentKind.CloseThunder or EnvironmentKind.RollingThunder
            or EnvironmentKind.Earthquake or EnvironmentKind.Landslide or EnvironmentKind.Avalanche or EnvironmentKind.Eruption;
        UniversalSoundTraits traits = thunderous
            ? new(HybridSynthesisMode.Modal, Waveform.Sine, texture, UniversalPitchPattern.Falling, .14, 2.2, .12f, .82f, 1f, .88f, -18, 4, "Brown")
            : new(HybridSynthesisMode.Additive, Waveform.Sine, texture, UniversalPitchPattern.Evolving, .42, 2.8, .18f, .12f, .22f, .72f, 2, 5, "Pink");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description with { Structure = UniversalPhraseStructure.Evolving }, traits, 2, 30,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            Density = Math.Clamp(description.Density, 0, 1),
            Movement = Math.Clamp((plan.Movement + description.Evolution) * .5f, 0, 1),
            EventCount = Math.Max(plan.EventCount, 6 + (int)Math.Round(description.Evolution * 10)),
            TransientAmount = thunderous ? plan.TransientAmount : plan.TransientAmount * .35f
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class MachineGenerator : ISoundGenerator<MachineDescription>
{
    public const string Id = "playroma.machine";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Machines & Industry", SoundGeneratorCategory.Machine,
        "Motors, fans, pumps, turbines, gears, factories, construction equipment, power systems, malfunctions, and breakdowns.",
        .05, 60, ["machine", "industry", "motor", "factory"]);

    public SoundGraphDefinition Generate(MachineDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool impact = description.Kind is MachineKind.BreakerTrip or MachineKind.Relay or MachineKind.Press or MachineKind.Jackhammer
            or MachineKind.NailGun or MachineKind.PileDriver;
        bool failure = description.Kind is MachineKind.MachineJam or MachineKind.EmergencyShutdown or MachineKind.Sputter or MachineKind.LooseBelt
            or MachineKind.GearGrinding or MachineKind.Overheat or MachineKind.BearingFailure or MachineKind.CatastrophicBreakdown or MachineKind.ReactorOverload;
        bool electrical = description.Kind is MachineKind.TransformerHum or MachineKind.Relay or MachineKind.BreakerTrip or MachineKind.BatteryCharge or MachineKind.Welding;
        UniversalSoundTraits traits = impact
            ? new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Alarm, .42, .75, .34f, .88f, .68f, .2f, -6, 5, "White")
            : failure
                ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Glitch, UniversalPitchPattern.Falling, .5, 1.6, .38f, .72f, .72f, .35f, -16, 7, "Brown")
                : electrical
                    ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Sine, ProceduralTextureKind.Electrical, UniversalPitchPattern.Flat, .85, 1.5, .62f, .25f, .42f, .28f, 2, 3, "Violet")
                    : new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Flat, .55, 1.8, .48f, .35f, .62f, .22f, 4, 4, "Pink");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .08, 12,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            RootFrequencyHz = plan.RootFrequencyHz * Semantic.Lerp(.45, 2.6, description.Speed),
            Modulation = Math.Clamp(plan.Modulation + description.Load * .35f, 0, 1),
            Roughness = Math.Clamp(plan.Roughness + Math.Max(description.Malfunction, failure ? .45f : 0), 0, 1),
            Damage = Math.Clamp(Math.Max(plan.Damage, description.Malfunction), 0, 1),
            Density = Math.Clamp(description.Speed * .6f + description.Load * .4f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class VehicleGenerator : ISoundGenerator<VehicleDescription>
{
    public const string Id = "playroma.vehicle";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Vehicles", SoundGeneratorCategory.Vehicle,
        "Ground, air, water, military, futuristic, and alien vehicle actions including engines, acceleration, braking, collisions, docking, and hyperspace.",
        .05, 60, ["vehicle", "engine", "transport", "flyby"]);

    public SoundGraphDefinition Generate(VehicleDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool air = description.Vehicle is VehicleKind.PropellerPlane or VehicleKind.Jet or VehicleKind.Helicopter or VehicleKind.Drone or VehicleKind.Glider or VehicleKind.Airship;
        bool water = description.Vehicle is VehicleKind.Rowboat or VehicleKind.Sailboat or VehicleKind.Speedboat or VehicleKind.Submarine or VehicleKind.CargoShip or VehicleKind.NavalVessel;
        bool futuristic = description.Vehicle is VehicleKind.Hovercar or VehicleKind.AlienVessel || description.Action is VehicleActionKind.HyperspaceCharge or VehicleActionKind.Jump or VehicleActionKind.WarpExit;
        bool impact = description.Action is VehicleActionKind.Collision or VehicleActionKind.Skid or VehicleActionKind.Brake;
        UniversalSoundTraits traits = futuristic
            ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, ProceduralTextureKind.Space, UniversalPitchPattern.Rising, .72, 1.6, .68f, .45f, .62f, .72f, 24, 6, "Violet")
            : impact
                ? new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Falling, .38, .8, .3f, .92f, .78f, .25f, -14, 5, "White")
                : water
                    ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Water, UniversalPitchPattern.Rising, .36, 1.7, .42f, .32f, .7f, .25f, 8, 4, "Pink")
                    : air
                        ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Wind, UniversalPitchPattern.Rising, .48, 1.7, .5f, .28f, .72f, .3f, 10, 4, "Pink")
                        : new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Rising, .42, 1.6, .5f, .35f, .75f, .22f, 8, 4, "Pink");
        double sizeScale = description.Vehicle switch
        {
            VehicleKind.Bicycle or VehicleKind.Drone or VehicleKind.Rowboat => 1.5,
            VehicleKind.Truck or VehicleKind.Bus or VehicleKind.Tank or VehicleKind.Train or VehicleKind.CargoShip or VehicleKind.NavalVessel => .5,
            VehicleKind.AlienVessel or VehicleKind.Airship => .38,
            _ => 1
        };
        traits = traits with { RootMultiplier = traits.RootMultiplier * sizeScale };
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            $"{description.Vehicle}-{description.Action}".ToLowerInvariant(), $"{description.Vehicle} {description.Action}", description, traits, .08, 12,
            new Dictionary<string, string> { ["vehicle"] = description.Vehicle.ToString(), ["action"] = description.Action.ToString() });
        float sweepSign = description.Action is VehicleActionKind.Brake or VehicleActionKind.EngineFailure ? -1 : 1;
        plan = plan with
        {
            RootFrequencyHz = plan.RootFrequencyHz * Semantic.Lerp(.6, 2.2, description.Speed),
            PitchSweepSemitones = plan.PitchSweepSemitones + sweepSign * (4 + description.Speed * 18),
            Modulation = Math.Clamp(plan.Modulation + description.EngineLoad * .42f, 0, 1),
            Density = Math.Clamp(description.EngineLoad, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class ScienceFictionGenerator : ISoundGenerator<ScienceFictionDescription>
{
    public const string Id = "playroma.science-fiction";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Science-Fiction Technology", SoundGeneratorCategory.ScienceFiction,
        "Computers, scanners, holograms, doors, robots, spacecraft, shields, alien technology, laboratories, cloaking, EMP, and psychic machinery.",
        .03, 30, ["science-fiction", "technology", "robot", "spaceship"]);

    public SoundGraphDefinition Generate(ScienceFictionDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool ui = description.Kind is ScienceFictionKind.ComputerBeep or ScienceFictionKind.ScannerPulse or ScienceFictionKind.TerminalAccess
            or ScienceFictionKind.AccessGranted or ScienceFictionKind.AccessDenied or ScienceFictionKind.SensorSweep or ScienceFictionKind.RadiationDetector;
        bool door = description.Kind is ScienceFictionKind.SlidingDoor or ScienceFictionKind.Airlock or ScienceFictionKind.BlastDoor or ScienceFictionKind.IrisDoor;
        bool robot = description.Kind is ScienceFictionKind.ServoMove or ScienceFictionKind.HydraulicMove or ScienceFictionKind.SyntheticBreathing
            or ScienceFictionKind.RobotBoot or ScienceFictionKind.RobotShutdown or ScienceFictionKind.DamageSparks;
        bool alien = description.Kind is ScienceFictionKind.OrganicMachine or ScienceFictionKind.CrystalComputer or ScienceFictionKind.GravityMechanism or ScienceFictionKind.PsychicInterface;
        UniversalSoundTraits traits = ui
            ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, null, UniversalPitchPattern.Rising, 2, .65, .9f, .18f, .04f, .28f, 8, 4, "Violet")
            : door
                ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Falling, .45, 1.2, .45f, .48f, .55f, .3f, -8, 3, "Pink")
                : robot
                    ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Zigzag, .65, 1.1, .58f, .45f, .38f, .25f, 5, 4, "White")
                    : alien
                        ? new(HybridSynthesisMode.Granular, Waveform.Sine, ProceduralTextureKind.Organic, UniversalPitchPattern.Arch, 1.15, 1.5, .72f, .25f, .2f, .72f, 18, 7, "Violet")
                        : new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, ProceduralTextureKind.Space, UniversalPitchPattern.Rising, .85, 1.5, .72f, .38f, .45f, .65f, 15, 6, "Violet");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .04, 8,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            OrganicSynthetic = Math.Clamp((plan.OrganicSynthetic + description.Technology) * .5f, 0, 1),
            Modulation = Math.Clamp(plan.Modulation + description.Alienness * .45f, 0, 1),
            Spread = Math.Clamp(plan.Spread + description.Alienness * .4f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class HorrorGenerator : ISoundGenerator<HorrorDescription>
{
    public const string Id = "playroma.horror";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Horror", SoundGeneratorCategory.Horror,
        "Psychological, supernatural, body-horror, and haunted-environment sounds with whispers, drones, impossible spaces, mutation, and spectral events.",
        .08, 60, ["horror", "ghost", "dread", "body-horror"]);

    public SoundGraphDefinition Generate(HorrorDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool voice = description.Kind is HorrorKind.Whisper or HorrorKind.ReversedWhisper or HorrorKind.DistantScream or HorrorKind.Breathing or HorrorKind.PossessionVoice or HorrorKind.OccultChant;
        bool body = description.Kind is HorrorKind.OrganicStretch or HorrorKind.Mutation or HorrorKind.ChitinGrowth or HorrorKind.ParasiticMovement or HorrorKind.InternalCrawling;
        bool environment = description.Kind is HorrorKind.AbandonedHospital or HorrorKind.EmptySchool or HorrorKind.UndergroundTunnel or HorrorKind.DerelictShip or HorrorKind.HauntedForest or HorrorKind.RitualChamber;
        UniversalSoundTraits traits = voice
            ? new(HybridSynthesisMode.Formant, Waveform.Saw, ProceduralTextureKind.Radio, UniversalPitchPattern.Valley, .58, 1.8, .48f, .28f, .45f, .82f, -9, 6, "Pink")
            : body
                ? new(HybridSynthesisMode.Granular, Waveform.Saw, ProceduralTextureKind.Organic, UniversalPitchPattern.Zigzag, .42, 1.6, .28f, .62f, .55f, .42f, -7, 7, "Brown")
                : environment
                    ? new(HybridSynthesisMode.Additive, Waveform.Sine, description.Kind == HorrorKind.DerelictShip ? ProceduralTextureKind.Space : ProceduralTextureKind.Wind, UniversalPitchPattern.Evolving, .25, 2.8, .3f, .12f, .65f, .95f, -3, 10, "Pink")
                    : new(HybridSynthesisMode.Granular, Waveform.Sine, ProceduralTextureKind.Radio, UniversalPitchPattern.Random, .7, 1.6, .55f, .35f, .35f, .85f, -12, 6, "Violet");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .1, 12,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            Roughness = Math.Clamp(plan.Roughness + description.Dread * .38f, 0, 1),
            Spread = Math.Clamp(plan.Spread + description.Supernatural * .52f, 0, 1),
            TailAmount = Math.Clamp(plan.TailAmount + description.Dread * .4f, 0, 1),
            Damage = Math.Clamp(plan.Damage + description.Supernatural * .18f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class CartoonGenerator : ISoundGenerator<CartoonDescription>
{
    public const string Id = "playroma.cartoon";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Cartoon & Comedy", SoundGeneratorCategory.Cartoon,
        "Boings, bonks, zips, pops, whistles, springs, splats, reactions, exaggerated motion, comic impacts, and elastic rebounds.",
        .02, 8, ["cartoon", "comedy", "boing", "reaction"]);

    public SoundGraphDefinition Generate(CartoonDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool whistle = description.Kind is CartoonKind.SlideWhistle or CartoonKind.LongFall or CartoonKind.Surprise or CartoonKind.Gulp;
        bool impact = description.Kind is CartoonKind.Bonk or CartoonKind.PanHit or CartoonKind.AnvilDrop or CartoonKind.CoconutKnock or CartoonKind.CymbalCrash;
        bool elastic = description.Kind is CartoonKind.Boing or CartoonKind.Spring or CartoonKind.RubberSqueak or CartoonKind.ElasticRebound or CartoonKind.Wobble;
        UniversalSoundTraits traits = whistle
            ? new(HybridSynthesisMode.Basic, Waveform.Sine, null, description.Kind == CartoonKind.LongFall ? UniversalPitchPattern.Falling : UniversalPitchPattern.Rising, 1.8, 1.2, .92f, .12f, .02f, .2f, description.Kind == CartoonKind.LongFall ? -28 : 22, 3, "White")
            : impact
                ? new(HybridSynthesisMode.Modal, Waveform.Triangle, null, UniversalPitchPattern.Bounce, .75, .55, .58f, .92f, .55f, .18f, -4, 3, "White")
                : elastic
                    ? new(HybridSynthesisMode.KarplusStrong, Waveform.Pulse, null, UniversalPitchPattern.Bounce, 1.25, .9, .82f, .35f, .12f, .22f, 14, 5, "White")
                    : new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, null, UniversalPitchPattern.Zigzag, 1.65, .65, .84f, .45f, .05f, .18f, 10, 4, "White");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .025, 2.5,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            PitchSweepSemitones = plan.PitchSweepSemitones * (.65f + description.Exaggeration),
            Modulation = Math.Clamp(plan.Modulation + description.Elasticity * .45f, 0, 1),
            Damping = Math.Clamp(plan.Damping - description.Elasticity * .32f, 0, 1),
            EventCount = Math.Max(plan.EventCount, 2 + (int)Math.Round(description.Exaggeration * 5))
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class HouseholdGenerator : ISoundGenerator<HouseholdDescription>
{
    public const string Id = "playroma.household";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Household & Everyday", SoundGeneratorCategory.Household,
        "Doors, locks, keys, appliances, kitchen, bathroom, office, electronics, paper, books, containers, and common object interactions.",
        .02, 30, ["household", "everyday", "appliance", "office"]);

    public SoundGraphDefinition Generate(HouseholdDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool appliance = description.Kind is HouseholdKind.Blender or HouseholdKind.Microwave or HouseholdKind.Refrigerator or HouseholdKind.Printer
            or HouseholdKind.Television or HouseholdKind.Radio or HouseholdKind.ComputerBoot or HouseholdKind.HardDrive;
        bool liquid = description.Kind is HouseholdKind.Faucet or HouseholdKind.Shower or HouseholdKind.Toilet or HouseholdKind.SinkDrain or HouseholdKind.Boiling;
        bool click = description.Kind is HouseholdKind.Switch or HouseholdKind.Mouse or HouseholdKind.Stapler or HouseholdKind.PenClick or HouseholdKind.CameraShutter
            or HouseholdKind.CanOpen or HouseholdKind.BottleOpen;
        UniversalSoundTraits traits = appliance
            ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Flat, .7, 1.8, .48f, .28f, .45f, .2f, 2, 4, "Pink")
            : liquid
                ? new(HybridSynthesisMode.NoiseResonator, Waveform.Sine, ProceduralTextureKind.Water, UniversalPitchPattern.Flat, .8, 1.7, .2f, .32f, .1f, .2f, 1, 4, "Pink")
                : click
                    ? new(HybridSynthesisMode.Modal, Waveform.Triangle, null, UniversalPitchPattern.Bounce, 1.4, .45, .58f, .85f, .08f, .1f, -2, 2, "White")
                    : new(HybridSynthesisMode.Modal, Waveform.Triangle, ProceduralTextureKind.Dust, UniversalPitchPattern.Falling, .82, .8, .42f, .65f, .25f, .15f, -4, 3, "Pink");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .025, 6,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            Roughness = Math.Clamp(plan.Roughness + description.Age * .35f, 0, 1),
            Damage = Math.Clamp(plan.Damage + description.Age * .12f, 0, 1),
            TransientAmount = Math.Clamp(plan.TransientAmount + description.Force * .32f, 0, 1),
            Intensity = Math.Clamp((plan.Intensity + description.Force) * .5f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class SignalGenerator : ISoundGenerator<SignalDescription>
{
    public const string Id = "playroma.signal";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Signals & Alarms", SoundGeneratorCategory.Signal,
        "Sirens, bells, buzzers, klaxons, warnings, radio and telephone signals, radar, sonar, navigation cues, and countdown urgency.",
        .03, 30, ["signal", "alarm", "warning", "communication"]);

    public SoundGraphDefinition Generate(SignalDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool communication = description.Kind is SignalKind.RadioChirp or SignalKind.WalkieStatic or SignalKind.TelephoneRing
            or SignalKind.IntercomTone or SignalKind.MorseSignal or SignalKind.EncodedTransmission;
        bool ping = description.Kind is SignalKind.RadarPing or SignalKind.SonarPing or SignalKind.TargetAcquired or SignalKind.CompassLock or SignalKind.WaypointReached;
        bool alarm = description.Kind is SignalKind.Siren or SignalKind.Klaxon or SignalKind.EvacuationAlarm or SignalKind.FireAlarm
            or SignalKind.IntrusionAlarm or SignalKind.ReactorWarning or SignalKind.LowOxygen or SignalKind.LowFuel or SignalKind.LowHealth or SignalKind.ImminentExplosion;
        UniversalSoundTraits traits = ping
            ? new(HybridSynthesisMode.Basic, Waveform.Sine, description.Kind == SignalKind.SonarPing ? ProceduralTextureKind.Underwater : null, UniversalPitchPattern.Falling, 2.1, 1.1, .94f, .1f, .04f, .72f, -5, 2, "Pink")
            : communication
                ? new(HybridSynthesisMode.OneBit, Waveform.Pulse, ProceduralTextureKind.Radio, UniversalPitchPattern.Prime, 1.6, 1.3, .78f, .2f, .04f, .24f, 3, 8, "White")
                : alarm
                    ? new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, null, UniversalPitchPattern.Alarm, 1.15, 1.8, .88f, .28f, .18f, .38f, 2, 8, "White")
                    : new(HybridSynthesisMode.Modal, Waveform.Sine, null, UniversalPitchPattern.Bounce, 1.4, 1, .82f, .35f, .08f, .45f, 4, 4, "White");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .04, 6,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            Intensity = Math.Clamp((plan.Intensity + description.Urgency) * .5f, 0, 1),
            EventCount = Math.Max(plan.EventCount, 2 + (int)Math.Round(description.Repetition * 10)),
            TailAmount = Math.Clamp(plan.TailAmount + description.Repetition * .25f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class CrowdSportsGenerator : ISoundGenerator<CrowdSportsDescription>
{
    public const string Id = "playroma.crowd-sports";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Crowds, Sports & Social Spaces", SoundGeneratorCategory.CrowdSports,
        "Crowd reactions, sporting actions and signals, taverns, markets, festivals, stations, schools, offices, arenas, and temples.",
        .05, 60, ["crowd", "sports", "social", "ambience"]);

    public SoundGraphDefinition Generate(CrowdSportsDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool action = description.Kind is CrowdSportsKind.BallHit or CrowdSportsKind.BatStrike or CrowdSportsKind.RacketHit or CrowdSportsKind.RefereeWhistle or CrowdSportsKind.ScoreBuzzer or CrowdSportsKind.RacingCountdown;
        bool reaction = description.Kind is CrowdSportsKind.Applause or CrowdSportsKind.Cheer or CrowdSportsKind.Boo or CrowdSportsKind.Gasp
            or CrowdSportsKind.Chant or CrowdSportsKind.Celebration or CrowdSportsKind.Panic or CrowdSportsKind.Riot;
        UniversalSoundTraits traits = action
            ? new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Crowd, UniversalPitchPattern.Bounce, 1.25, .7, .58f, .85f, .12f, .25f, -3, 3, "White")
            : reaction
                ? new(HybridSynthesisMode.Formant, Waveform.Saw, ProceduralTextureKind.Crowd, UniversalPitchPattern.Rising, .85, 1.7, .38f, .25f, .35f, .52f, 8, 8, "Pink")
                : new(HybridSynthesisMode.Formant, Waveform.Sine, ProceduralTextureKind.Crowd, UniversalPitchPattern.Evolving, .7, 2.4, .32f, .12f, .32f, .48f, 2, 10, "Pink");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description with { Structure = action ? description.Structure : UniversalPhraseStructure.Evolving }, traits, .08, 12,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            Density = Math.Clamp(description.Population, 0, 1),
            Intensity = Math.Clamp((plan.Intensity + description.Excitement) * .5f, 0, 1),
            EventCount = Math.Max(plan.EventCount, 3 + (int)Math.Round(description.Population * 12))
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class TransformationGenerator : ISoundGenerator<TransformationDescription>
{
    public const string Id = "playroma.transformation";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Transformation & State Change", SoundGeneratorCategory.Transformation,
        "Power changes, transformations, mutations, phasing, object states, status effects, world transitions, corruption, capture, and timeline alteration.",
        .04, 30, ["transformation", "state", "power-up", "status"]);

    public SoundGraphDefinition Generate(TransformationDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool upward = description.Kind is TransformationKind.PowerUp or TransformationKind.Transform or TransformationKind.Evolve or TransformationKind.Grow
            or TransformationKind.PhaseIn or TransformationKind.Activate or TransformationKind.Charge or TransformationKind.Repair or TransformationKind.Invulnerable
            or TransformationKind.DayBegins or TransformationKind.TerritoryCaptured;
        bool downward = description.Kind is TransformationKind.PowerDown or TransformationKind.Shrink or TransformationKind.PhaseOut or TransformationKind.Deactivate
            or TransformationKind.Discharge or TransformationKind.Break or TransformationKind.NightBegins;
        bool negative = description.Kind is TransformationKind.Poisoned or TransformationKind.Frozen or TransformationKind.Burning or TransformationKind.Shocked
            or TransformationKind.Stunned or TransformationKind.Confused or TransformationKind.CorruptionSpreads;
        UniversalSoundTraits traits = negative
            ? new(HybridSynthesisMode.Granular, Waveform.Saw, ProceduralTextureKind.Organic, UniversalPitchPattern.Zigzag, .72, 1.4, .48f, .42f, .32f, .62f, -8, 7, "Violet")
            : new(HybridSynthesisMode.Additive, Waveform.Sine, ProceduralTextureKind.Space, upward ? UniversalPitchPattern.Rising : downward ? UniversalPitchPattern.Falling : UniversalPitchPattern.Arch,
                upward ? 1.25 : .85, 1.6, .82f, .25f, .24f, .75f, upward ? 24 : downward ? -20 : 12, 8, "Violet");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .05, 8,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            PitchSweepSemitones = plan.PitchSweepSemitones * (.55f + description.Magnitude),
            Modulation = Math.Clamp(plan.Modulation + description.MysticalTechnological * .35f, 0, 1),
            Intensity = Math.Clamp((plan.Intensity + description.Magnitude) * .5f, 0, 1),
            TailAmount = Math.Clamp(plan.TailAmount + description.Magnitude * .28f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class AbstractSynthGenerator : ISoundGenerator<AbstractSynthDescription>
{
    public const string Id = "playroma.abstract-synth";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Abstract Synthesis Lab", SoundGeneratorCategory.AbstractSynth,
        "Blips, sweeps, drones, glitches, spectral textures, granular clouds, mathematical rhythms, data-like sequences, and direct access to all hybrid synthesis modes.",
        .02, 60, ["synth", "abstract", "granular", "fm", "procedural"]);

    public SoundGraphDefinition Generate(AbstractSynthDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        bool texture = description.Kind is AbstractSynthKind.Drone or AbstractSynthKind.Texture or AbstractSynthKind.Glitch or AbstractSynthKind.SpectralSmear
            or AbstractSynthKind.HarmonicCloud or AbstractSynthKind.InharmonicCluster or AbstractSynthKind.GranularCloud or AbstractSynthKind.Freeze;
        ProceduralTextureKind? textureKind = description.Kind switch
        {
            AbstractSynthKind.Glitch or AbstractSynthKind.Stutter or AbstractSynthKind.RhythmicGate => ProceduralTextureKind.Glitch,
            AbstractSynthKind.Drone or AbstractSynthKind.HarmonicCloud or AbstractSynthKind.SpectralSmear => ProceduralTextureKind.Space,
            AbstractSynthKind.MetallicResonance => ProceduralTextureKind.Machine,
            _ when texture => ProceduralTextureKind.Radio,
            _ => null
        };
        UniversalPitchPattern pattern = description.Kind switch
        {
            AbstractSynthKind.Riser or AbstractSynthKind.Sweep or AbstractSynthKind.Shimmer => UniversalPitchPattern.Rising,
            AbstractSynthKind.Fall or AbstractSynthKind.ReverseTail => UniversalPitchPattern.Falling,
            AbstractSynthKind.FractalMelody or AbstractSynthKind.CellularRhythm => UniversalPitchPattern.Prime,
            AbstractSynthKind.ChaoticModulation or AbstractSynthKind.ProbabilitySequence => UniversalPitchPattern.Random,
            AbstractSynthKind.Stutter or AbstractSynthKind.RhythmicGate => UniversalPitchPattern.Trill,
            _ => UniversalPitchPattern.Zigzag
        };
        double root = Semantic.Exp(35, 4_500, description.Pitch) / Semantic.Lerp(1.4, .72, description.Size);
        UniversalSoundTraits traits = new(description.SynthesisMode, description.Waveform, textureKind, pattern,
            root / 440, texture ? 2.1 : 1, texture ? .55f : .82f, texture ? .18f : .42f,
            texture ? .32f : .08f, texture ? .72f : .35f,
            description.Kind is AbstractSynthKind.Riser ? 30 : description.Kind is AbstractSynthKind.Fall ? -30 : 8,
            description.Kind is AbstractSynthKind.PrimeRhythm or AbstractSynthKind.ProbabilitySequence ? 11 : texture ? 8 : 4,
            description.Brightness > .6f ? "Violet" : "Pink");
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), description, traits, .03, 12,
            new Dictionary<string, string> { ["kind"] = description.Kind.ToString(), ["synthesisMode"] = description.SynthesisMode.ToString() });
        plan = plan with
        {
            RootFrequencyHz = root,
            Modulation = Math.Clamp(description.Modulation, 0, 1),
            Spread = Math.Clamp(description.Spread, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}

public sealed class ProceduralSequenceGenerator : ISoundGenerator<ProceduralSequenceDescription>
{
    public const string Id = "playroma.procedural-sequence";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Procedural Action Sequence", SoundGeneratorCategory.SoundSequence,
        "Complete mini-scenes: footsteps, weapon mechanics, spell chains, machine cycles, storms, collapses, UI families, portals, crafting, combos, and transformations.",
        .08, 60, ["sequence", "mini-scene", "chain", "procedural"]);

    public SoundGraphDefinition Generate(ProceduralSequenceDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        UniversalSoundTraits traits = description.Kind switch
        {
            ProceduralSequenceKind.FootstepRun => new(HybridSynthesisMode.Modal, Waveform.Triangle, ProceduralTextureKind.Dust, UniversalPitchPattern.Zigzag, .55, 1.6, .28f, .82f, .35f, .16f, -2, 8, "Brown"),
            ProceduralSequenceKind.WeaponAction or ProceduralSequenceKind.GunshotMechanics or ProceduralSequenceKind.CombatCombo => new(HybridSynthesisMode.Modal, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Zigzag, .6, 1.2, .42f, .88f, .58f, .3f, -8, 8, "White"),
            ProceduralSequenceKind.SpellCastImpact or ProceduralSequenceKind.PortalOpenClose => new(HybridSynthesisMode.Granular, Waveform.Sine, ProceduralTextureKind.Space, UniversalPitchPattern.Arch, 1.1, 1.7, .78f, .35f, .22f, .78f, 20, 8, "Violet"),
            ProceduralSequenceKind.CreaturePhrase => new(HybridSynthesisMode.Formant, Waveform.Saw, ProceduralTextureKind.Organic, UniversalPitchPattern.CallAndResponse, .72, 1.5, .58f, .42f, .42f, .35f, -6, 8, "Brown"),
            ProceduralSequenceKind.MachineCycle or ProceduralSequenceKind.EngineAcceleration or ProceduralSequenceKind.CraftingSequence => new(HybridSynthesisMode.FrequencyModulation, Waveform.Saw, ProceduralTextureKind.Machine, UniversalPitchPattern.Rising, .55, 1.8, .48f, .45f, .62f, .22f, 10, 8, "Pink"),
            ProceduralSequenceKind.StormEvent or ProceduralSequenceKind.EnvironmentalEvent => new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Rain, UniversalPitchPattern.Falling, .22, 2.4, .18f, .68f, .85f, .72f, -16, 10, "Brown"),
            ProceduralSequenceKind.BuildingCollapse => new(HybridSynthesisMode.Modal, Waveform.Sine, ProceduralTextureKind.Dust, UniversalPitchPattern.Cascade, .14, 2.6, .16f, 1f, 1f, .7f, -22, 12, "Brown"),
            ProceduralSequenceKind.UiNotificationFamily or ProceduralSequenceKind.RewardPhrase => new(HybridSynthesisMode.Additive, Waveform.Sine, null, UniversalPitchPattern.Rising, 1.8, 1.1, .92f, .12f, .04f, .48f, 12, 7, "White"),
            ProceduralSequenceKind.AlarmEscalation => new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, null, UniversalPitchPattern.Alarm, 1.2, 1.8, .88f, .22f, .18f, .38f, 4, 10, "White"),
            ProceduralSequenceKind.TransformationSequence => new(HybridSynthesisMode.Additive, Waveform.Sine, ProceduralTextureKind.Space, UniversalPitchPattern.Rising, 1.1, 1.8, .82f, .28f, .22f, .8f, 24, 9, "Violet"),
            _ => new(HybridSynthesisMode.FrequencyModulation, Waveform.Pulse, ProceduralTextureKind.Glitch, UniversalPitchPattern.Random, 1, 1.5, .65f, .4f, .2f, .4f, 8, 6, "White")
        };
        UniversalSoundDescriptionBase shaped = description with
        {
            Structure = description.Kind switch
            {
                ProceduralSequenceKind.AlarmEscalation => UniversalPhraseStructure.Escalation,
                ProceduralSequenceKind.SpellCastImpact or ProceduralSequenceKind.PortalOpenClose or ProceduralSequenceKind.TransformationSequence => UniversalPhraseStructure.BuildAndRelease,
                ProceduralSequenceKind.CreaturePhrase => UniversalPhraseStructure.CallAndResponse,
                ProceduralSequenceKind.CombatCombo => UniversalPhraseStructure.Combo,
                _ => description.Structure == UniversalPhraseStructure.Single ? UniversalPhraseStructure.Evolving : description.Structure
            }
        };
        UniversalSoundPlan plan = UniversalGeneratorSupport.CreatePlan(Id, Descriptor.Version,
            description.Kind.ToString().ToLowerInvariant(), description.Kind.ToString(), shaped, traits, .1, 12,
            UniversalGeneratorSupport.Meta("kind", description.Kind.ToString()));
        plan = plan with
        {
            EventCount = Math.Clamp(description.Events, 2, context.Budget.MaximumPhraseNotes),
            Density = Math.Clamp(description.Rhythm, 0, 1),
            TailAmount = Math.Clamp(plan.TailAmount + description.Repetition * .35f, 0, 1)
        };
        return UniversalSoundRenderer.Render(plan, context);
    }
}
