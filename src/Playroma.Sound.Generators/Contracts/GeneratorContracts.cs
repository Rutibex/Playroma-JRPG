using Playroma.Sound.Core.Random;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Contracts;

public enum SoundQualityMode
{
    Draft,
    RealtimeLow,
    RealtimeHigh,
    Offline
}

public enum SoundGeneratorCategory
{
    Interface,
    Impact,
    Explosion,
    Weapon,
    Footstep,
    Magic,
    Retro,
    Creature,
    CreatureCry,
    ClassicHandheldCry,
    Melodic,
    ComplexMagic,
    CreatureAction,
    CombatSequence,
    Destruction,
    MaterialFoley,
    Movement,
    Liquid,
    ElementalEnergy,
    Environment,
    Machine,
    Vehicle,
    ScienceFiction,
    Horror,
    Cartoon,
    Household,
    Signal,
    CrowdSports,
    Transformation,
    AbstractSynth,
    SoundSequence
}

public enum GeneratorParameterKind
{
    Number,
    Enumeration,
    Boolean
}

public sealed record GeneratorParameterDescriptor(
    string Id,
    string DisplayName,
    GeneratorParameterKind Kind,
    double DefaultValue = 0,
    double Minimum = 0,
    double Maximum = 1,
    string Unit = "",
    IReadOnlyList<string>? AllowedValues = null,
    string Description = "");

public sealed record SoundGeneratorDescriptor(
    string Id,
    string DisplayName,
    SoundGeneratorCategory Category,
    string Description,
    IReadOnlyList<GeneratorParameterDescriptor> Parameters,
    IReadOnlyList<string> Tags,
    double MinimumDurationSeconds,
    double MaximumDurationSeconds,
    int Version = 1);

public sealed record SoundComplexityBudget(
    int MaximumNodes,
    int MaximumProcessors,
    int MaximumSecondaryEvents,
    int MaximumResonances,
    int MaximumPhraseNotes,
    int MaximumPhraseVoices,
    int MaximumGesturePoints,
    double MaximumTailSeconds)
{
    public static SoundComplexityBudget ForQuality(SoundQualityMode quality) => quality switch
    {
        SoundQualityMode.Draft => new(24, 8, 1, 2, 4, 1, 5, 1.5),
        SoundQualityMode.RealtimeLow => new(48, 16, 2, 3, 8, 2, 8, 2.5),
        SoundQualityMode.RealtimeHigh => new(84, 26, 4, 6, 16, 4, 16, 6),
        SoundQualityMode.Offline => new(144, 48, 8, 10, 32, 8, 32, 14),
        _ => new(48, 16, 2, 3, 8, 2, 8, 2.5)
    };
}

public sealed record SoundGenerationContext
{
    public ulong RootSeed { get; init; } = 1;
    public ulong FamilySeed { get; init; } = 1;
    public int VariationIndex { get; init; }
    public SoundQualityMode QualityMode { get; init; } = SoundQualityMode.RealtimeHigh;
    public SoundComplexityBudget? BudgetOverride { get; init; }
    public string? GraphId { get; init; }
    public string? DisplayName { get; init; }

    public SoundComplexityBudget Budget => BudgetOverride ?? SoundComplexityBudget.ForQuality(QualityMode);

    public Pcg32 CreateRandom(string streamName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ulong seed = StableHash.Combine(RootSeed, FamilySeed, unchecked((ulong)VariationIndex), StableHash.String(streamName));
        ulong stream = StableHash.Combine(StableHash.String(streamName), 0x9E3779B97F4A7C15UL);
        return new Pcg32(seed, stream);
    }

    public SoundGenerationContext WithVariation(int variationIndex) => this with { VariationIndex = variationIndex };
}

public interface ISoundGenerator<in TDescription>
{
    string GeneratorId { get; }
    SoundGeneratorDescriptor Descriptor { get; }
    SoundGraphDefinition Generate(TDescription description, SoundGenerationContext context);
}

public sealed record SoundVariationSet(
    string FamilyId,
    ulong FamilySeed,
    IReadOnlyList<SoundGraphDefinition> Variations);

internal static class StableHash
{
    public static ulong String(string value)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char character in value)
        {
            hash ^= character;
            hash = unchecked(hash * prime);
        }

        return hash;
    }

    public static ulong Combine(params ulong[] values)
    {
        unchecked
        {
            ulong hash = 0xCBF29CE484222325UL;
            foreach (ulong value in values)
            {
                hash ^= value + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2);
            }

            return hash;
        }
    }
}

internal static class Semantic
{
    public static float Clamp01(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    public static double Lerp(double minimum, double maximum, float amount) => minimum + ((maximum - minimum) * Clamp01(amount));
    public static double Exp(double minimum, double maximum, float amount)
    {
        minimum = Math.Max(0.000001, minimum);
        maximum = Math.Max(minimum, maximum);
        return minimum * Math.Pow(maximum / minimum, Clamp01(amount));
    }
}
