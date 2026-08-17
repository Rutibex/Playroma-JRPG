using Playroma.Sound.Core.Generation;
using Playroma.Sound.Generators.Profiles;

namespace Playroma.Sound.Generators.Descriptions;

public enum SoundVariationMode
{
    Natural,
    Stable,
    Chaotic,
    IdentityPreserving,
    Mutated,
    Crossbred,
    RetroConstrained,
    Damaged,
    Cinematic
}

public enum UniversalArticulation
{
    Smooth,
    Staccato,
    Legato,
    Accented,
    Gated,
    Trembling,
    Whispered,
    Rough,
    Swell,
    Choked
}

public enum UniversalPhraseStructure
{
    Single,
    Double,
    Triple,
    CallAndResponse,
    RepeatedMotif,
    BuildAndRelease,
    Escalation,
    FalseEnding,
    Combo,
    Evolving
}


public enum UniversalPitchContour
{
    Automatic,
    Flat,
    Rising,
    Falling,
    Arch,
    Valley,
    Zigzag,
    Alarm,
    Trill,
    Bounce,
    Cascade,
    Question,
    CallAndResponse,
    Evolving,
    Random,
    Prime
}

public enum UniversalRhythmStyle
{
    Natural,
    Even,
    Syncopated,
    Swing,
    Triplet,
    Bursts,
    Accelerating,
    Decelerating,
    Irregular,
    Prime,
    CallAndResponse
}

public enum UniversalEnvelopeStyle
{
    Automatic,
    Percussive,
    Plucked,
    Sustained,
    Gated,
    Swell,
    DoubleAttack,
    LongRelease
}

public enum UniversalEmotion
{
    Neutral,
    Happy,
    Playful,
    Curious,
    Nervous,
    Frightened,
    Angry,
    Enraged,
    Sad,
    Exhausted,
    Confident,
    Majestic,
    Sinister,
    Confused,
    Desperate,
    Triumphant
}

public enum UniversalEnergyState
{
    Weak,
    Sleepy,
    Calm,
    Alert,
    Energetic,
    Frantic,
    Overcharged,
    Unstable,
    Dying,
    Recovering
}

public enum UniversalCondition
{
    Normal,
    New,
    Polished,
    Worn,
    Rusty,
    Damaged,
    Ancient,
    Corrupted,
    Wet,
    Frozen,
    Burning
}

public enum UniversalStereoStyle
{
    Automatic,
    Mono,
    Narrow,
    Wide,
    Moving,
    Alternating
}

public enum CreatureActionKind
{
    Spawn,
    Awaken,
    NoticePlayer,
    Idle,
    Sniff,
    Eat,
    Sleep,
    Attack,
    HeavyAttack,
    Cast,
    TakeDamage,
    Stagger,
    Flee,
    Die,
    Revive,
    Evolve,
    Transform,
    LevelUp,
    Enrage,
    CallAllies,
    Land,
    WingFlap,
    Burrow,
    Swim,
    SlimeSplit,
    SlimeReform
}

public enum CreatureBodyType
{
    TinyMammal,
    Canine,
    Feline,
    Avian,
    Reptilian,
    Amphibian,
    Insectoid,
    Aquatic,
    Slime,
    Undead,
    Elemental,
    Mechanical,
    Dragon,
    Giant,
    Alien
}

public enum CombatSequenceKind
{
    SwordSlash,
    KnifeStab,
    AxeSwing,
    HammerSlam,
    SpearThrust,
    StaffSweep,
    ClawSwipe,
    WhipCrack,
    ChainSwing,
    ShieldBash,
    BowShot,
    CrossbowShot,
    ThrownKnife,
    Javelin,
    PistolShot,
    RevolverShot,
    RifleShot,
    ShotgunBlast,
    MachineGunBurst,
    SniperShot,
    SuppressedShot,
    LaserPulse,
    PlasmaBolt,
    Railgun,
    ParticleBeam,
    IonCannon,
    SonicWeapon,
    GravityWeapon,
    Grenade,
    Rocket,
    Missile,
    Block,
    Parry,
    Dodge,
    PerfectGuard,
    CriticalHit,
    Finisher,
    Combo,
    Execution
}

public enum TargetMaterial
{
    None,
    Flesh,
    Bone,
    Cloth,
    Leather,
    Wood,
    Stone,
    Concrete,
    Glass,
    Ceramic,
    Steel,
    Armour,
    Shield,
    Ice,
    Crystal,
    EnergyBarrier,
    AlienShell
}

public enum DestructionKind
{
    GlassBreak,
    CeramicSmash,
    WoodSplinter,
    MetalBend,
    MetalTear,
    StoneCrack,
    ConcreteCollapse,
    IceShatter,
    CrystalBurst,
    BoneSnap,
    PlasticBreak,
    DoorBreach,
    WallCollapse,
    CeilingCaveIn,
    BridgeFailure,
    TowerCollapse,
    VehicleCrash,
    HullBreach,
    Rockfall,
    RubbleSettle,
    DebrisScatter,
    PlanetaryImpact
}

public enum FoleyActionKind
{
    Tap,
    Knock,
    Drop,
    Scrape,
    Grind,
    Roll,
    Bend,
    Stretch,
    Tear,
    Snap,
    Crush,
    Crumple,
    Rustle,
    Flap,
    Clink,
    Clang,
    Ring,
    Creak,
    Brush,
    Rattle,
    Shake,
    Open,
    Close,
    Lock,
    Unlock,
    PickUp,
    PutDown
}

public enum MovementKind
{
    Walk,
    Run,
    Sprint,
    Sneak,
    Limp,
    Crawl,
    Climb,
    Slide,
    Skid,
    Shuffle,
    March,
    Stumble,
    Tiptoe,
    Jump,
    HeavyLanding,
    DoubleJump,
    WallJump,
    Dash,
    DodgeRoll,
    GrappleSwing,
    Zipline,
    WallRun,
    Jetpack,
    Hover,
    TeleportStep,
    Burrow,
    Swim,
    Fly,
    Glide
}

public enum MoverType
{
    BareFeet,
    Boots,
    Shoes,
    Paws,
    Claws,
    Hooves,
    Talons,
    RobotFeet,
    Slime,
    Armoured,
    Giant,
    Tiny
}

public enum LiquidSoundKind
{
    Drop,
    Drip,
    Splash,
    Pour,
    Stream,
    River,
    Waterfall,
    OceanWave,
    Bubble,
    Gurgle,
    FillContainer,
    EmptyContainer,
    Stir,
    Shake,
    Boil,
    Freeze,
    Spray,
    Squirt,
    Slosh,
    Drain,
    Suction,
    UnderwaterMovement,
    UnderwaterImpact,
    UnderwaterExplosion,
    Sonar
}

public enum LiquidType
{
    Water,
    Mud,
    Slime,
    Oil,
    Acid,
    Lava,
    Potion,
    Syrup,
    OrganicFluid,
    AlienFluid
}

public enum ElementalEnergyKind
{
    MatchStrike,
    Candle,
    Torch,
    Campfire,
    Fireplace,
    Inferno,
    Wildfire,
    Flamethrower,
    MagicalFlame,
    StaticSnap,
    Spark,
    ElectricalArc,
    TransformerBuzz,
    Lightning,
    ShortCircuit,
    TeslaDischarge,
    Charge,
    Pulse,
    Beam,
    Wave,
    Orb,
    Shield,
    ReactorCore,
    Plasma,
    Radiation,
    Antimatter,
    VoidEnergy,
    FrostSpread,
    IceForm,
    IceMelt,
    SteamRelease,
    ThermalShock
}

public enum EnvironmentKind
{
    Breeze,
    Gust,
    Gale,
    Hurricane,
    ForestWind,
    DesertWind,
    MountainWind,
    Drizzle,
    SteadyRain,
    Downpour,
    RoofRain,
    WindowRain,
    DistantThunder,
    CloseThunder,
    RollingThunder,
    Hail,
    Tornado,
    Sandstorm,
    Snowstorm,
    Earthquake,
    Landslide,
    Avalanche,
    VolcanicTremor,
    Eruption,
    Geyser,
    Forest,
    Jungle,
    Swamp,
    Desert,
    Tundra,
    Cave,
    Mountain,
    Coast,
    Riverbank,
    Meadow,
    Village,
    MedievalTown,
    ModernCity,
    CyberpunkStreet,
    IndustrialDistrict,
    Suburb,
    Cathedral,
    Bunker,
    Laboratory,
    Spaceship,
    Dungeon,
    Warehouse,
    Sewer,
    MagicForest,
    CrystalCave,
    Underworld,
    DreamRealm,
    AlienPlanet,
    SpaceStation,
    ReactorChamber,
    AbandonedColony
}

public enum MachineKind
{
    Motor,
    Fan,
    Pump,
    Compressor,
    Turbine,
    Piston,
    Belt,
    Gear,
    Chain,
    Conveyor,
    Press,
    Drill,
    Saw,
    Grinder,
    GeneratorStart,
    TransformerHum,
    Relay,
    BreakerTrip,
    BatteryCharge,
    ReactorIdle,
    ReactorOverload,
    FactoryStartup,
    ProductionCycle,
    MachineJam,
    EmergencyShutdown,
    CoolingCycle,
    Jackhammer,
    NailGun,
    Welding,
    CuttingTorch,
    Crane,
    CementMixer,
    PileDriver,
    Excavator,
    Sputter,
    LooseBelt,
    GearGrinding,
    Overheat,
    BearingFailure,
    CatastrophicBreakdown
}

public enum VehicleKind
{
    Car,
    Truck,
    Bus,
    Motorcycle,
    Bicycle,
    Tank,
    Tractor,
    Train,
    Tram,
    Hovercar,
    PropellerPlane,
    Jet,
    Helicopter,
    Drone,
    Glider,
    Airship,
    Rowboat,
    Sailboat,
    Speedboat,
    Submarine,
    CargoShip,
    NavalVessel,
    AlienVessel
}

public enum VehicleActionKind
{
    Ignition,
    Idle,
    Accelerate,
    Brake,
    Skid,
    GearShift,
    Horn,
    Collision,
    Door,
    Launch,
    Flyby,
    Dock,
    EngineFailure,
    InteriorLoop,
    HyperspaceCharge,
    Jump,
    WarpExit
}

public enum ScienceFictionKind
{
    ComputerBeep,
    ScannerPulse,
    HologramActivate,
    TerminalAccess,
    DataTransfer,
    HackingProgress,
    AccessGranted,
    AccessDenied,
    SlidingDoor,
    Airlock,
    ForceField,
    BlastDoor,
    IrisDoor,
    TeleportGate,
    ContainmentField,
    ServoMove,
    HydraulicMove,
    SyntheticBreathing,
    SensorSweep,
    RobotBoot,
    RobotShutdown,
    DamageSparks,
    EngineIdle,
    EngineAcceleration,
    Hyperspace,
    HullStress,
    ShieldImpact,
    DockingClamp,
    OrganicMachine,
    CrystalComputer,
    GravityMechanism,
    PsychicInterface,
    Cloak,
    Decloak,
    TractorBeam,
    Emp,
    MedicalScanner,
    CloningVat,
    CryogenicPod,
    RadiationDetector
}

public enum HorrorKind
{
    Whisper,
    ReversedWhisper,
    DistantScream,
    Breathing,
    Heartbeat,
    DraggingChain,
    BoneCreak,
    WetFootstep,
    Tinnitus,
    ImpossibleHum,
    PhantomKnock,
    BehindListener,
    GhostWail,
    PossessionVoice,
    DimensionalTear,
    CursedObject,
    OccultChant,
    SpectralBell,
    OrganicStretch,
    Mutation,
    ChitinGrowth,
    ParasiticMovement,
    InternalCrawling,
    AbandonedHospital,
    EmptySchool,
    UndergroundTunnel,
    DerelictShip,
    HauntedForest,
    RitualChamber
}

public enum CartoonKind
{
    Boing,
    Bonk,
    Zip,
    Pop,
    SlideWhistle,
    Spring,
    RubberSqueak,
    Splat,
    Surprise,
    Confusion,
    Dizzy,
    Embarrassment,
    AngrySteam,
    Gulp,
    Tiptoe,
    FastRun,
    SuddenStop,
    Stumble,
    LongFall,
    TinySteps,
    GiantSteps,
    Wobble,
    PanHit,
    AnvilDrop,
    CoconutKnock,
    CymbalCrash,
    SquishyFlatten,
    ElasticRebound
}

public enum HouseholdKind
{
    Door,
    Lock,
    Keys,
    Drawer,
    Cabinet,
    Switch,
    Dishes,
    Cutlery,
    Glass,
    Bottle,
    Paper,
    Book,
    Chopping,
    Frying,
    Boiling,
    Blender,
    Toaster,
    Microwave,
    Refrigerator,
    CanOpen,
    BottleOpen,
    Faucet,
    Shower,
    Toilet,
    SinkDrain,
    Toothbrush,
    Towel,
    Keyboard,
    Mouse,
    Printer,
    Stapler,
    PenClick,
    PhoneVibration,
    Television,
    Radio,
    ComputerBoot,
    HardDrive,
    CameraShutter
}

public enum SignalKind
{
    Siren,
    Bell,
    Buzzer,
    Klaxon,
    EvacuationAlarm,
    FireAlarm,
    IntrusionAlarm,
    ReactorWarning,
    RadioChirp,
    WalkieStatic,
    TelephoneRing,
    IntercomTone,
    MorseSignal,
    EncodedTransmission,
    RadarPing,
    SonarPing,
    ProximityWarning,
    TargetAcquired,
    CompassLock,
    WaypointReached,
    Countdown,
    LowOxygen,
    LowFuel,
    LowHealth,
    ImminentExplosion
}

public enum CrowdSportsKind
{
    CrowdMurmur,
    Applause,
    Cheer,
    Boo,
    Gasp,
    Chant,
    Celebration,
    Panic,
    Riot,
    BallHit,
    BatStrike,
    RacketHit,
    RefereeWhistle,
    ScoreBuzzer,
    RacingCountdown,
    Tavern,
    Market,
    Festival,
    Restaurant,
    TrainStation,
    School,
    Office,
    Arena,
    Temple
}

public enum TransformationKind
{
    PowerUp,
    PowerDown,
    Transform,
    Evolve,
    Mutate,
    Grow,
    Shrink,
    PhaseIn,
    PhaseOut,
    Open,
    Close,
    Lock,
    Unlock,
    Activate,
    Deactivate,
    Charge,
    Discharge,
    Repair,
    Break,
    Poisoned,
    Frozen,
    Burning,
    Shocked,
    Stunned,
    Confused,
    Charmed,
    Invisible,
    Invulnerable,
    DayBegins,
    NightBegins,
    SeasonChanges,
    CorruptionSpreads,
    TerritoryCaptured,
    TimelineAltered
}

public enum AbstractSynthKind
{
    Blip,
    Bloop,
    Zap,
    Sweep,
    Riser,
    Fall,
    Pulse,
    Drone,
    Texture,
    Glitch,
    Shimmer,
    SpectralSmear,
    HarmonicCloud,
    InharmonicCluster,
    MetallicResonance,
    FormantSweep,
    Stutter,
    Freeze,
    GranularCloud,
    ReverseTail,
    RhythmicGate,
    FractalMelody,
    CellularRhythm,
    ChaoticModulation,
    PrimeRhythm,
    ProbabilitySequence
}

public enum ProceduralSequenceKind
{
    FootstepRun,
    WeaponAction,
    GunshotMechanics,
    SpellCastImpact,
    CreaturePhrase,
    MachineCycle,
    EngineAcceleration,
    StormEvent,
    BuildingCollapse,
    UiNotificationFamily,
    RewardPhrase,
    AlarmEscalation,
    PortalOpenClose,
    CraftingSequence,
    CombatCombo,
    TransformationSequence,
    EnvironmentalEvent
}

public abstract record UniversalSoundDescriptionBase
{
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.SmallRoom;
    public SoundVariationMode VariationMode { get; init; } = SoundVariationMode.Natural;
    public UniversalArticulation Articulation { get; init; } = UniversalArticulation.Smooth;
    public UniversalPhraseStructure Structure { get; init; } = UniversalPhraseStructure.Single;
    public UniversalPitchContour PitchContour { get; init; } = UniversalPitchContour.Automatic;
    public UniversalRhythmStyle RhythmStyle { get; init; } = UniversalRhythmStyle.Natural;
    public UniversalEnvelopeStyle EnvelopeStyle { get; init; } = UniversalEnvelopeStyle.Automatic;
    public UniversalEmotion Emotion { get; init; } = UniversalEmotion.Neutral;
    public UniversalEnergyState EnergyState { get; init; } = UniversalEnergyState.Alert;
    public UniversalCondition Condition { get; init; } = UniversalCondition.Normal;
    public UniversalStereoStyle StereoStyle { get; init; } = UniversalStereoStyle.Automatic;
    public float Intensity { get; init; } = 0.6f;
    public float Duration { get; init; } = 0.35f;
    public float Size { get; init; } = 0.5f;
    public float Brightness { get; init; } = 0.55f;
    public float Roughness { get; init; } = 0.25f;
    public float TonalNoise { get; init; } = 0.55f;
    public float OrganicSynthetic { get; init; } = 0.4f;
    public float Complexity { get; init; } = 0.5f;
    public float Movement { get; init; } = 0.35f;
    public float Distance { get; init; } = 0.1f;
    public float Damage { get; init; }
}

public sealed record CreatureActionDescription : UniversalSoundDescriptionBase
{
    public CreatureActionKind Action { get; init; } = CreatureActionKind.Attack;
    public CreatureBodyType BodyType { get; init; } = CreatureBodyType.Alien;
}

public sealed record CombatSequenceDescription : UniversalSoundDescriptionBase
{
    public CombatSequenceKind Action { get; init; } = CombatSequenceKind.SwordSlash;
    public TargetMaterial Target { get; init; } = TargetMaterial.Armour;
    public float MechanicalDetail { get; init; } = 0.45f;
}

public sealed record DestructionDescription : UniversalSoundDescriptionBase
{
    public DestructionKind Kind { get; init; } = DestructionKind.WoodSplinter;
    public float Debris { get; init; } = 0.55f;
    public int SecondaryEvents { get; init; } = 3;
}

public sealed record MaterialFoleyDescription : UniversalSoundDescriptionBase
{
    public FoleyActionKind Action { get; init; } = FoleyActionKind.Drop;
    public SoundMaterial MaterialA { get; init; } = SoundMaterial.Wood;
    public SoundMaterial MaterialB { get; init; } = SoundMaterial.Concrete;
    public float ContactArea { get; init; } = 0.45f;
}

public sealed record MovementDescription : UniversalSoundDescriptionBase
{
    public MovementKind MovementKind { get; init; } = MovementKind.Walk;
    public MoverType Mover { get; init; } = MoverType.Boots;
    public SoundMaterial Surface { get; init; } = SoundMaterial.Concrete;
    public int Steps { get; init; } = 4;
    public float Wetness { get; init; }
    public float Equipment { get; init; } = 0.2f;
}

public sealed record LiquidDescription : UniversalSoundDescriptionBase
{
    public LiquidSoundKind Kind { get; init; } = LiquidSoundKind.Splash;
    public LiquidType Liquid { get; init; } = LiquidType.Water;
    public float Viscosity { get; init; } = 0.2f;
    public float ContainerResonance { get; init; } = 0.25f;
}

public sealed record ElementalEnergyDescription : UniversalSoundDescriptionBase
{
    public ElementalEnergyKind Kind { get; init; } = ElementalEnergyKind.Spark;
    public float Instability { get; init; } = 0.35f;
    public float Tail { get; init; } = 0.3f;
}

public sealed record EnvironmentDescription : UniversalSoundDescriptionBase
{
    public EnvironmentKind Kind { get; init; } = EnvironmentKind.Forest;
    public float Density { get; init; } = 0.5f;
    public float Evolution { get; init; } = 0.4f;
}

public sealed record MachineDescription : UniversalSoundDescriptionBase
{
    public MachineKind Kind { get; init; } = MachineKind.Motor;
    public float Speed { get; init; } = 0.5f;
    public float Load { get; init; } = 0.5f;
    public float Malfunction { get; init; }
}

public sealed record VehicleDescription : UniversalSoundDescriptionBase
{
    public VehicleKind Vehicle { get; init; } = VehicleKind.Car;
    public VehicleActionKind Action { get; init; } = VehicleActionKind.Accelerate;
    public float Speed { get; init; } = 0.5f;
    public float EngineLoad { get; init; } = 0.55f;
}

public sealed record ScienceFictionDescription : UniversalSoundDescriptionBase
{
    public ScienceFictionKind Kind { get; init; } = ScienceFictionKind.ScannerPulse;
    public float Technology { get; init; } = 0.8f;
    public float Alienness { get; init; } = 0.35f;
}

public sealed record HorrorDescription : UniversalSoundDescriptionBase
{
    public HorrorKind Kind { get; init; } = HorrorKind.Whisper;
    public float Dread { get; init; } = 0.65f;
    public float Supernatural { get; init; } = 0.55f;
}

public sealed record CartoonDescription : UniversalSoundDescriptionBase
{
    public CartoonKind Kind { get; init; } = CartoonKind.Boing;
    public float Exaggeration { get; init; } = 0.8f;
    public float Elasticity { get; init; } = 0.6f;
}

public sealed record HouseholdDescription : UniversalSoundDescriptionBase
{
    public HouseholdKind Kind { get; init; } = HouseholdKind.Door;
    public float Age { get; init; } = 0.3f;
    public float Force { get; init; } = 0.5f;
}

public sealed record SignalDescription : UniversalSoundDescriptionBase
{
    public SignalKind Kind { get; init; } = SignalKind.RadarPing;
    public float Urgency { get; init; } = 0.5f;
    public float Repetition { get; init; } = 0.45f;
}

public sealed record CrowdSportsDescription : UniversalSoundDescriptionBase
{
    public CrowdSportsKind Kind { get; init; } = CrowdSportsKind.CrowdMurmur;
    public float Population { get; init; } = 0.6f;
    public float Excitement { get; init; } = 0.45f;
}

public sealed record TransformationDescription : UniversalSoundDescriptionBase
{
    public TransformationKind Kind { get; init; } = TransformationKind.PowerUp;
    public float Magnitude { get; init; } = 0.65f;
    public float MysticalTechnological { get; init; } = 0.5f;
}

public sealed record AbstractSynthDescription : UniversalSoundDescriptionBase
{
    public AbstractSynthKind Kind { get; init; } = AbstractSynthKind.Blip;
    public HybridSynthesisMode SynthesisMode { get; init; } = HybridSynthesisMode.FrequencyModulation;
    public Waveform Waveform { get; init; } = Waveform.Sine;
    public float Pitch { get; init; } = 0.55f;
    public float Modulation { get; init; } = 0.45f;
    public float Spread { get; init; } = 0.25f;
}

public sealed record ProceduralSequenceDescription : UniversalSoundDescriptionBase
{
    public ProceduralSequenceKind Kind { get; init; } = ProceduralSequenceKind.SpellCastImpact;
    public int Events { get; init; } = 5;
    public float Rhythm { get; init; } = 0.5f;
    public float Repetition { get; init; } = 0.2f;
}
