using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Generators;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Contracts;

public interface IUntypedSoundGenerator
{
    string GeneratorId { get; }
    SoundGeneratorDescriptor Descriptor { get; }
    Type DescriptionType { get; }
    SoundGraphDefinition Generate(object description, SoundGenerationContext context);
}

public sealed class SoundGeneratorAdapter<TDescription> : IUntypedSoundGenerator
{
    private readonly ISoundGenerator<TDescription> _generator;

    public SoundGeneratorAdapter(ISoundGenerator<TDescription> generator) =>
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));

    public string GeneratorId => _generator.GeneratorId;
    public SoundGeneratorDescriptor Descriptor => _generator.Descriptor;
    public Type DescriptionType => typeof(TDescription);

    public SoundGraphDefinition Generate(object description, SoundGenerationContext context)
    {
        if (description is not TDescription typed)
        {
            throw new ArgumentException($"Generator '{GeneratorId}' expects {typeof(TDescription).Name}, not {description.GetType().Name}.", nameof(description));
        }

        return _generator.Generate(typed, context);
    }
}

public sealed class SoundGeneratorCatalog
{
    private readonly Dictionary<string, IUntypedSoundGenerator> _generators = new(StringComparer.Ordinal);

    public IReadOnlyCollection<IUntypedSoundGenerator> Generators => _generators.Values;

    public SoundGeneratorCatalog Register<TDescription>(ISoundGenerator<TDescription> generator)
    {
        var adapter = new SoundGeneratorAdapter<TDescription>(generator);
        if (!_generators.TryAdd(adapter.GeneratorId, adapter))
        {
            throw new InvalidOperationException($"Generator ID '{adapter.GeneratorId}' is already registered.");
        }

        return this;
    }

    public IUntypedSoundGenerator GetRequired(string generatorId) =>
        _generators.TryGetValue(generatorId, out IUntypedSoundGenerator? generator)
            ? generator
            : throw new KeyNotFoundException($"Sound generator '{generatorId}' is not registered.");

    public SoundGraphDefinition Generate(string generatorId, object description, SoundGenerationContext context) =>
        GetRequired(generatorId).Generate(description, context);

    public static SoundGeneratorCatalog CreateDefault() => new SoundGeneratorCatalog()
        .Register(new InterfaceSoundGenerator())
        .Register(new ImpactGenerator())
        .Register(new ExplosionGenerator())
        .Register(new WeaponGenerator())
        .Register(new FootstepGenerator())
        .Register(new MagicGenerator())
        .Register(new RetroGameGenerator())
        .Register(new CreatureVoiceGenerator())
        .Register(new CreatureCryGenerator())
        .Register(new ClassicHandheldCryGenerator())
        .Register(new MelodicEffectGenerator())
        .Register(new ComplexMagicGenerator())
        .Register(new CreatureActionGenerator())
        .Register(new CombatSequenceGenerator())
        .Register(new DestructionGenerator())
        .Register(new MaterialFoleyGenerator())
        .Register(new MovementGenerator())
        .Register(new LiquidGenerator())
        .Register(new ElementalEnergyGenerator())
        .Register(new EnvironmentSoundGenerator())
        .Register(new MachineGenerator())
        .Register(new VehicleGenerator())
        .Register(new ScienceFictionGenerator())
        .Register(new HorrorGenerator())
        .Register(new CartoonGenerator())
        .Register(new HouseholdGenerator())
        .Register(new SignalGenerator())
        .Register(new CrowdSportsGenerator())
        .Register(new TransformationGenerator())
        .Register(new AbstractSynthGenerator())
        .Register(new ProceduralSequenceGenerator());
}
