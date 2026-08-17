using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Generators;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Presets;

public interface IGeneratorPreset
{
    string Id { get; }
    string DisplayName { get; }
    string GeneratorId { get; }
    Type DescriptionType { get; }
    SoundGraphDefinition Generate(SoundGenerationContext context);
}

public sealed record GeneratorPreset<TDescription>(
    string Id,
    string DisplayName,
    ISoundGenerator<TDescription> Generator,
    TDescription Description) : IGeneratorPreset
{
    public string GeneratorId => Generator.GeneratorId;
    public Type DescriptionType => typeof(TDescription);
    public SoundGraphDefinition Generate(SoundGenerationContext context) =>
        Generator.Generate(Description, context with { GraphId = Id, DisplayName = DisplayName });
}

public static class BuiltInPresetCatalog
{
    public static IReadOnlyList<IGeneratorPreset> All { get; } =
    [
        new GeneratorPreset<InterfaceSoundDescription>("ui.soft-confirm", "Soft Confirm", new InterfaceSoundGenerator(),
            new InterfaceSoundDescription { Kind = InterfaceSoundKind.Confirm, Friendliness = .9f, Brightness = .62f, Duration = .24f, NoteCount = 2 }),
        new GeneratorPreset<InterfaceSoundDescription>("ui.urgent-warning", "Urgent Warning", new InterfaceSoundGenerator(),
            new InterfaceSoundDescription { Kind = InterfaceSoundKind.Warning, Importance = .85f, Urgency = .9f, Brightness = .78f, Duration = .45f, NoteCount = 3 }),
        new GeneratorPreset<ImpactDescription>("impact.heavy-steel-on-stone", "Heavy Steel on Stone", new ImpactGenerator(),
            new ImpactDescription { MaterialA = SoundMaterial.Steel, MaterialB = SoundMaterial.Stone, Mass = .88f, Velocity = .72f, Breakage = .08f, Environment = AcousticEnvironment.Warehouse }),
        new GeneratorPreset<ExplosionDescription>("explosion.plasma-corridor", "Plasma Explosion in Corridor", new ExplosionGenerator(),
            new ExplosionDescription { EnergyType = ExplosionEnergyType.Plasma, Environment = AcousticEnvironment.Corridor, Size = .72f, Containment = .55f, Debris = .32f, SecondaryEvents = 3 }),
        new GeneratorPreset<WeaponDescription>("weapon.arc-rifle", "Arc Rifle", new WeaponGenerator(),
            new WeaponDescription { Mechanism = WeaponMechanism.EnergyWeapon, Power = .68f, Technology = .9f, Environment = AcousticEnvironment.IndustrialHall }),
        new GeneratorPreset<FootstepDescription>("footstep.boot-wet-concrete", "Boot on Wet Concrete", new FootstepGenerator(),
            new FootstepDescription { Footwear = FootwearType.Boot, Surface = SoundMaterial.Concrete, Wetness = .55f, Weight = .62f, Environment = AcousticEnvironment.UrbanStreet }),
        new GeneratorPreset<MagicDescription>("magic.unstable-teleport", "Unstable Teleport", new MagicGenerator(),
            new MagicDescription { Archetype = MagicArchetype.Teleportation, Power = .72f, Stability = .18f, SpatialExtent = .88f, Environment = AcousticEnvironment.StoneChamber }),
        new GeneratorPreset<RetroSoundDescription>("retro.arcade-pickup", "Arcade Pickup", new RetroGameGenerator(),
            new RetroSoundDescription { Style = RetroStyle.FmArcade, Event = RetroEventKind.Pickup, Pitch = .7f, Notes = 3, Dirt = .12f }),
        new GeneratorPreset<CreatureVoiceDescription>("creature.large-warning-growl", "Large Warning Growl", new CreatureVoiceGenerator(),
            new CreatureVoiceDescription { Call = CreatureCallKind.Growl, Size = .82f, Aggression = .72f, Duration = .68f, Environment = AcousticEnvironment.Cave }),
        new GeneratorPreset<CreatureCryDescription>("creature-cry.spark-mouse", "Spark Mouse Cry", new CreatureCryGenerator(),
            new CreatureCryDescription { Style = CreatureCryStyle.Chirp, Shape = PhraseShape.Zigzag, Size = .18f, Cuteness = .88f, Energy = .92f, MusicalAnimal = .62f, PitchMovement = .9f, Syllables = 4, Duration = .22f, RetroModern = .42f }),
        new GeneratorPreset<CreatureCryDescription>("creature-cry.ancient-drake", "Ancient Drake Cry", new CreatureCryGenerator(),
            new CreatureCryDescription { Style = CreatureCryStyle.Draconic, Shape = PhraseShape.CallAndResponse, Size = .9f, Menace = .85f, Energy = .7f, SmoothRaspy = .82f, PitchMovement = .72f, Syllables = 3, Duration = .58f, Environment = AcousticEnvironment.Cave }),
        new GeneratorPreset<ClassicHandheldCryDescription>("handheld-cry.pocket-spark", "Pocket Spark Squeak", new ClassicHandheldCryGenerator(),
            new ClassicHandheldCryDescription { Archetype = ClassicHandheldCryArchetype.Squeak, Contour = ClassicHandheldCryContour.Zigzag, Pitch = .82f, Length = .2f, BodySize = .12f, Syllables = 7, Complexity = .62f, Sweep = .82f, PulseBlend = .58f, Noise = .16f, Roughness = .22f, Repetition = .36f, Punch = .9f }),
        new GeneratorPreset<ClassicHandheldCryDescription>("handheld-cry.marsh-croak", "Marsh Cartridge Croak", new ClassicHandheldCryGenerator(),
            new ClassicHandheldCryDescription { Archetype = ClassicHandheldCryArchetype.Croak, Contour = ClassicHandheldCryContour.Valley, Pitch = .34f, Length = .52f, BodySize = .72f, Syllables = 6, Complexity = .48f, Sweep = .46f, PulseBlend = .7f, Noise = .58f, Roughness = .63f, Repetition = .18f, Punch = .64f }),
        new GeneratorPreset<ClassicHandheldCryDescription>("handheld-cry.ancient-roar", "Ancient Cartridge Roar", new ClassicHandheldCryGenerator(),
            new ClassicHandheldCryDescription { Archetype = ClassicHandheldCryArchetype.Roar, Contour = ClassicHandheldCryContour.Falling, Pitch = .28f, Length = .72f, BodySize = .92f, Syllables = 8, Complexity = .72f, Sweep = .64f, PulseBlend = .82f, Noise = .78f, Roughness = .74f, Repetition = .08f, Punch = .78f }),
        new GeneratorPreset<ClassicHandheldCryDescription>("handheld-cry.static-wisp", "Static Wisp Cry", new ClassicHandheldCryGenerator(),
            new ClassicHandheldCryDescription { Archetype = ClassicHandheldCryArchetype.Electronic, Contour = ClassicHandheldCryContour.Broken, Pitch = .68f, Length = .34f, BodySize = .26f, Syllables = 9, Complexity = .84f, Sweep = .88f, PulseBlend = .66f, Noise = .52f, Roughness = .8f, Repetition = .48f, Punch = .82f }),
        new GeneratorPreset<MelodicEffectDescription>("melodic.treasure-reveal", "Treasure Reveal", new MelodicEffectGenerator(),
            new MelodicEffectDescription { Kind = MelodicEffectKind.TreasureReveal, Mood = PhraseMood.Heroic, Shape = PhraseShape.Rising, Scale = PhraseScale.MajorPentatonic, Notes = 7, Harmony = .48f, Brightness = .82f, Duration = .38f, Tail = .42f }),
        new GeneratorPreset<MelodicEffectDescription>("melodic.ominous-vision", "Ominous Vision", new MelodicEffectGenerator(),
            new MelodicEffectDescription { Kind = MelodicEffectKind.Vision, Mood = PhraseMood.Sinister, Shape = PhraseShape.Arch, Scale = PhraseScale.Diminished, Notes = 6, Harmony = .32f, TonalDissonant = .55f, Duration = .62f, Tail = .72f, Environment = AcousticEnvironment.StoneChamber }),
        new GeneratorPreset<ComplexMagicDescription>("complex-magic.grand-teleport", "Grand Teleport", new ComplexMagicGenerator(),
            new ComplexMagicDescription { Archetype = MagicArchetype.Teleportation, Mood = PhraseMood.Alien, Shape = PhraseShape.ChargeRelease, Scale = PhraseScale.WholeTone, Power = .82f, Stability = .28f, Duration = .52f, MotifNotes = 8, Afterglow = .78f, SpatialExtent = .9f }),
        new GeneratorPreset<ComplexMagicDescription>("complex-magic.healing-rite", "Healing Rite", new ComplexMagicGenerator(),
            new ComplexMagicDescription { Archetype = MagicArchetype.Healing, Mood = PhraseMood.Holy, Shape = PhraseShape.Arch, Scale = PhraseScale.MajorPentatonic, Power = .58f, Stability = .82f, Benevolence = .95f, Duration = .46f, MotifNotes = 6, Afterglow = .68f }),

        new GeneratorPreset<CreatureActionDescription>("creature-action.dragon-enrage", "Dragon Enrage", new CreatureActionGenerator(),
            new CreatureActionDescription { Action = CreatureActionKind.Enrage, BodyType = CreatureBodyType.Dragon, Size = .9f, Intensity = .92f, Roughness = .78f, Structure = UniversalPhraseStructure.CallAndResponse, Duration = .55f, Environment = AcousticEnvironment.Cave }),
        new GeneratorPreset<CreatureActionDescription>("creature-action.slime-split", "Gelatinous Split", new CreatureActionGenerator(),
            new CreatureActionDescription { Action = CreatureActionKind.SlimeSplit, BodyType = CreatureBodyType.Slime, Size = .45f, TonalNoise = .35f, OrganicSynthetic = .08f, Complexity = .72f, Duration = .38f }),
        new GeneratorPreset<CombatSequenceDescription>("combat.railgun-armour", "Railgun Against Armour", new CombatSequenceGenerator(),
            new CombatSequenceDescription { Action = CombatSequenceKind.Railgun, Target = TargetMaterial.Armour, Intensity = .9f, Size = .72f, Brightness = .82f, MechanicalDetail = .78f, Environment = AcousticEnvironment.IndustrialHall }),
        new GeneratorPreset<CombatSequenceDescription>("combat.sword-combo", "Three-Part Sword Combo", new CombatSequenceGenerator(),
            new CombatSequenceDescription { Action = CombatSequenceKind.Combo, Target = TargetMaterial.Steel, Structure = UniversalPhraseStructure.Combo, Intensity = .76f, Movement = .82f, Complexity = .72f, Duration = .42f }),
        new GeneratorPreset<DestructionDescription>("destruction.cathedral-collapse", "Cathedral Collapse", new DestructionGenerator(),
            new DestructionDescription { Kind = DestructionKind.TowerCollapse, Size = .95f, Intensity = .9f, Debris = .92f, SecondaryEvents = 10, Duration = .72f, Environment = AcousticEnvironment.StoneChamber, VariationMode = SoundVariationMode.Cinematic }),
        new GeneratorPreset<DestructionDescription>("destruction.crystal-burst", "Crystal Burst", new DestructionGenerator(),
            new DestructionDescription { Kind = DestructionKind.CrystalBurst, Size = .32f, Brightness = .95f, Debris = .72f, SecondaryEvents = 8, Duration = .3f }),
        new GeneratorPreset<MaterialFoleyDescription>("foley.old-leather-strap", "Old Leather Strap", new MaterialFoleyGenerator(),
            new MaterialFoleyDescription { Action = FoleyActionKind.Stretch, MaterialA = SoundMaterial.Leather, MaterialB = SoundMaterial.Wood, Roughness = .62f, Damage = .28f, Duration = .5f, ContactArea = .3f }),
        new GeneratorPreset<MaterialFoleyDescription>("foley.brass-key-ring", "Brass Key Ring", new MaterialFoleyGenerator(),
            new MaterialFoleyDescription { Action = FoleyActionKind.Rattle, MaterialA = SoundMaterial.Brass, MaterialB = SoundMaterial.Steel, Brightness = .8f, Complexity = .68f, Structure = UniversalPhraseStructure.RepeatedMotif }),
        new GeneratorPreset<MovementDescription>("movement.armoured-sprint", "Armoured Sprint on Stone", new MovementGenerator(),
            new MovementDescription { MovementKind = MovementKind.Sprint, Mover = MoverType.Armoured, Surface = SoundMaterial.Stone, Steps = 8, Size = .7f, Intensity = .82f, Equipment = .8f, Duration = .46f, Environment = AcousticEnvironment.StoneChamber }),
        new GeneratorPreset<MovementDescription>("movement-jetpack-dash", "Jetpack Dash", new MovementGenerator(),
            new MovementDescription { MovementKind = MovementKind.Jetpack, Mover = MoverType.Armoured, Intensity = .82f, Brightness = .76f, OrganicSynthetic = .92f, Movement = .9f, Duration = .4f }),
        new GeneratorPreset<LiquidDescription>("liquid.alien-gurgle", "Alien Fluid Gurgle", new LiquidGenerator(),
            new LiquidDescription { Kind = LiquidSoundKind.Gurgle, Liquid = LiquidType.AlienFluid, Viscosity = .68f, ContainerResonance = .72f, Complexity = .75f, VariationMode = SoundVariationMode.Mutated }),
        new GeneratorPreset<LiquidDescription>("liquid.deep-sonar", "Deep Sonar Ping", new LiquidGenerator(),
            new LiquidDescription { Kind = LiquidSoundKind.Sonar, Liquid = LiquidType.Water, Size = .85f, Brightness = .42f, Duration = .55f, Environment = AcousticEnvironment.Underwater }),
        new GeneratorPreset<ElementalEnergyDescription>("energy-unstable-void", "Unstable Void Energy", new ElementalEnergyGenerator(),
            new ElementalEnergyDescription { Kind = ElementalEnergyKind.VoidEnergy, Size = .82f, Intensity = .82f, Instability = .88f, Tail = .85f, Complexity = .8f, VariationMode = SoundVariationMode.Chaotic }),
        new GeneratorPreset<ElementalEnergyDescription>("energy-tesla-arc", "Tesla Arc", new ElementalEnergyGenerator(),
            new ElementalEnergyDescription { Kind = ElementalEnergyKind.TeslaDischarge, Brightness = .9f, Intensity = .78f, Instability = .74f, Structure = UniversalPhraseStructure.Escalation, Duration = .35f }),
        new GeneratorPreset<EnvironmentDescription>("environment.cyberpunk-rain", "Cyberpunk Street in Rain", new EnvironmentSoundGenerator(),
            new EnvironmentDescription { Kind = EnvironmentKind.CyberpunkStreet, Density = .72f, Evolution = .68f, Duration = .65f }),
        new GeneratorPreset<EnvironmentDescription>("environment.alien-planet", "Alien Planet Ambience", new EnvironmentSoundGenerator(),
            new EnvironmentDescription { Kind = EnvironmentKind.AlienPlanet, Density = .55f, Evolution = .85f, OrganicSynthetic = .62f, Complexity = .8f, VariationMode = SoundVariationMode.Crossbred, Duration = .72f }),
        new GeneratorPreset<MachineDescription>("machine.damaged-turbine", "Damaged Turbine", new MachineGenerator(),
            new MachineDescription { Kind = MachineKind.Turbine, Speed = .72f, Load = .82f, Malfunction = .7f, Damage = .68f, VariationMode = SoundVariationMode.Damaged, Duration = .55f }),
        new GeneratorPreset<MachineDescription>("machine.factory-cycle", "Factory Production Cycle", new MachineGenerator(),
            new MachineDescription { Kind = MachineKind.ProductionCycle, Structure = UniversalPhraseStructure.Evolving, Speed = .55f, Load = .65f, Complexity = .72f, Duration = .68f }),
        new GeneratorPreset<VehicleDescription>("vehicle-alien-warp", "Alien Vessel Warp Exit", new VehicleGenerator(),
            new VehicleDescription { Vehicle = VehicleKind.AlienVessel, Action = VehicleActionKind.WarpExit, Speed = .92f, EngineLoad = .85f, OrganicSynthetic = .75f, Complexity = .82f, Duration = .58f, VariationMode = SoundVariationMode.Cinematic }),
        new GeneratorPreset<VehicleDescription>("vehicle-heavy-tank", "Heavy Tank Acceleration", new VehicleGenerator(),
            new VehicleDescription { Vehicle = VehicleKind.Tank, Action = VehicleActionKind.Accelerate, Size = .88f, Speed = .6f, EngineLoad = .9f, Roughness = .65f, Duration = .58f }),
        new GeneratorPreset<ScienceFictionDescription>("scifi.psychic-interface", "Psychic Interface", new ScienceFictionGenerator(),
            new ScienceFictionDescription { Kind = ScienceFictionKind.PsychicInterface, Alienness = .92f, Technology = .72f, OrganicSynthetic = .45f, Complexity = .82f, Duration = .55f }),
        new GeneratorPreset<ScienceFictionDescription>("scifi.shield-impact", "Capital Shield Impact", new ScienceFictionGenerator(),
            new ScienceFictionDescription { Kind = ScienceFictionKind.ShieldImpact, Size = .92f, Intensity = .9f, Technology = .95f, Duration = .48f, Environment = AcousticEnvironment.SyntheticVacuum }),
        new GeneratorPreset<HorrorDescription>("horror.behind-you", "Something Behind You", new HorrorGenerator(),
            new HorrorDescription { Kind = HorrorKind.BehindListener, Dread = .92f, Supernatural = .85f, Movement = .72f, Duration = .62f, Environment = AcousticEnvironment.Corridor }),
        new GeneratorPreset<HorrorDescription>("horror.internal-crawling", "Internal Crawling", new HorrorGenerator(),
            new HorrorDescription { Kind = HorrorKind.InternalCrawling, Dread = .78f, Supernatural = .25f, OrganicSynthetic = .02f, Roughness = .68f, Complexity = .85f, Duration = .48f }),
        new GeneratorPreset<CartoonDescription>("cartoon.heroic-boing", "Heroic Boing", new CartoonGenerator(),
            new CartoonDescription { Kind = CartoonKind.Boing, Exaggeration = .95f, Elasticity = .9f, Brightness = .82f, Structure = UniversalPhraseStructure.Triple, Duration = .28f }),
        new GeneratorPreset<CartoonDescription>("cartoon.long-fall", "Very Long Fall", new CartoonGenerator(),
            new CartoonDescription { Kind = CartoonKind.LongFall, Exaggeration = .9f, Movement = .9f, Duration = .62f, Size = .35f }),
        new GeneratorPreset<HouseholdDescription>("household.old-door", "Old Haunted Door", new HouseholdGenerator(),
            new HouseholdDescription { Kind = HouseholdKind.Door, Age = .92f, Force = .45f, Roughness = .72f, Damage = .32f, Duration = .55f, Environment = AcousticEnvironment.Corridor }),
        new GeneratorPreset<HouseholdDescription>("household.computer-boot", "Retro Computer Boot", new HouseholdGenerator(),
            new HouseholdDescription { Kind = HouseholdKind.ComputerBoot, OrganicSynthetic = .95f, Brightness = .72f, Complexity = .68f, VariationMode = SoundVariationMode.RetroConstrained }),
        new GeneratorPreset<SignalDescription>("signal-reactor-warning", "Reactor Warning", new SignalGenerator(),
            new SignalDescription { Kind = SignalKind.ReactorWarning, Urgency = .92f, Repetition = .78f, Structure = UniversalPhraseStructure.Escalation, Intensity = .82f, Duration = .58f }),
        new GeneratorPreset<SignalDescription>("signal.deep-radar", "Deep Radar Ping", new SignalGenerator(),
            new SignalDescription { Kind = SignalKind.RadarPing, Size = .75f, Brightness = .62f, Duration = .45f, Environment = AcousticEnvironment.IndustrialHall }),
        new GeneratorPreset<CrowdSportsDescription>("crowd-arena-victory", "Arena Victory Cheer", new CrowdSportsGenerator(),
            new CrowdSportsDescription { Kind = CrowdSportsKind.Cheer, Population = .92f, Excitement = .95f, Intensity = .82f, Duration = .55f, Environment = AcousticEnvironment.Warehouse }),
        new GeneratorPreset<CrowdSportsDescription>("crowd-tavern", "Busy Fantasy Tavern", new CrowdSportsGenerator(),
            new CrowdSportsDescription { Kind = CrowdSportsKind.Tavern, Population = .72f, Excitement = .55f, OrganicSynthetic = .02f, Duration = .72f, Environment = AcousticEnvironment.DomesticRoom }),
        new GeneratorPreset<TransformationDescription>("transform-corruption", "Corruption Spreads", new TransformationGenerator(),
            new TransformationDescription { Kind = TransformationKind.CorruptionSpreads, Magnitude = .82f, MysticalTechnological = .35f, Roughness = .72f, Complexity = .85f, VariationMode = SoundVariationMode.Mutated }),
        new GeneratorPreset<TransformationDescription>("transform-level-powerup", "Grand Power Up", new TransformationGenerator(),
            new TransformationDescription { Kind = TransformationKind.PowerUp, Magnitude = .95f, MysticalTechnological = .65f, Intensity = .88f, Structure = UniversalPhraseStructure.BuildAndRelease, Duration = .55f }),
        new GeneratorPreset<AbstractSynthDescription>("synth.granular-cloud", "Granular Star Cloud", new AbstractSynthGenerator(),
            new AbstractSynthDescription { Kind = AbstractSynthKind.GranularCloud, SynthesisMode = Playroma.Sound.Core.Generation.HybridSynthesisMode.Granular, Waveform = Playroma.Sound.Core.Generation.Waveform.Sine, Pitch = .48f, Spread = .85f, Modulation = .72f, Duration = .72f, VariationMode = SoundVariationMode.Crossbred }),
        new GeneratorPreset<AbstractSynthDescription>("synth.modal-metal", "Modal Metal Resonance", new AbstractSynthGenerator(),
            new AbstractSynthDescription { Kind = AbstractSynthKind.MetallicResonance, SynthesisMode = Playroma.Sound.Core.Generation.HybridSynthesisMode.Modal, Waveform = Playroma.Sound.Core.Generation.Waveform.Sine, Pitch = .62f, Brightness = .8f, Complexity = .78f, Duration = .48f }),
        new GeneratorPreset<ProceduralSequenceDescription>("sequence.portal", "Portal Open and Close", new ProceduralSequenceGenerator(),
            new ProceduralSequenceDescription { Kind = ProceduralSequenceKind.PortalOpenClose, Events = 9, Structure = UniversalPhraseStructure.BuildAndRelease, Rhythm = .55f, Repetition = .2f, Duration = .58f, VariationMode = SoundVariationMode.Cinematic }),
        new GeneratorPreset<ProceduralSequenceDescription>("sequence-building-collapse", "Building Collapse Sequence", new ProceduralSequenceGenerator(),
            new ProceduralSequenceDescription { Kind = ProceduralSequenceKind.BuildingCollapse, Events = 14, Rhythm = .7f, Size = .95f, Intensity = .92f, Duration = .72f, Environment = AcousticEnvironment.UrbanStreet })
    ];

    public static IGeneratorPreset GetRequired(string id) => All.FirstOrDefault(preset => preset.Id == id)
        ?? throw new KeyNotFoundException($"Generator preset '{id}' was not found.");
}
