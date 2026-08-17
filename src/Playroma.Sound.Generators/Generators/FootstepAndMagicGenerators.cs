using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Generators;

public sealed class FootstepGenerator : ISoundGenerator<FootstepDescription>
{
    public const string Id = "playroma.footstep";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Footstep", SoundGeneratorCategory.Footstep,
        "Variation-oriented footsteps combining contact, sole or paw body, surface texture, grit, splash, and release.",
        .04, 2.2, ["footstep", "surface", "variation", "movement"]);

    public SoundGraphDefinition Generate(FootstepDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        SoundMaterialProfile surface = MaterialProfiles.Get(description.Surface);
        var random = context.CreateRandom("footstep.detail");
        float weight = Semantic.Clamp01(description.Weight);
        float speed = Semantic.Clamp01(description.Speed);
        double gaitScale = description.Gait switch
        {
            FootstepGait.Sneak => .5,
            FootstepGait.Walk => .72,
            FootstepGait.Run => 1,
            FootstepGait.Sprint => 1.2,
            FootstepGait.Land => 1.55,
            _ => .72
        };
        double duration = Math.Clamp(Semantic.Lerp(.09, .48, weight) / Math.Max(.65, gaitScale), .055, .72);
        double bodyFrequency = surface.FundamentalHz * Semantic.Lerp(1.35, .62, weight) * FootwearPitch(description.Footwear);
        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"footstep.{description.Footwear.ToString().ToLowerInvariant()}-{description.Surface.ToString().ToLowerInvariant()}",
            $"{description.Footwear} on {description.Surface}");
        var layers = new List<string>();

        string contact = LayerBuilders.AddNoiseBurst(builder, "contact", "White",
            (.12 + (weight * .42)) * gaitScale, Semantic.Lerp(.015, .07, 1 - speed),
            Semantic.Lerp(650, 6_500, (float)surface.TransientSharpness), .75 + description.LooseDebris,
            out string contactSource);
        layers.Add(contact);

        string sole = LayerBuilders.AddTonalBody(builder, "body", FootwearWaveform(description.Footwear), bodyFrequency,
            (.12 + weight * .42) * gaitScale, duration, .001, duration * .72);
        layers.Add(sole);

        string texture = LayerBuilders.AddNoiseBurst(builder, "surface-texture",
            description.Surface is SoundMaterial.Dirt or SoundMaterial.Sand ? "Brown" : "Pink",
            .04 + ((float)surface.Roughness * .22f) + (description.LooseDebris * .24f), duration * .8,
            Semantic.Lerp(280, 3_500, (float)surface.HighFrequencyContent), .5);
        texture = LayerBuilders.AddDelayed(builder, texture, "texture-offset", .008 + GeneratorMath.Range(ref random, 0, .018));
        layers.Add(texture);

        if (description.Wetness > .05f || description.Surface == SoundMaterial.Water)
        {
            string splash = LayerBuilders.AddNoiseBurst(builder, "splash", "Pink", .08 + description.Wetness * .38,
                duration * (1 + description.Wetness), Semantic.Lerp(420, 2_600, 1 - description.Wetness), .42);
            splash = LayerBuilders.AddDelayed(builder, splash, "splash-offset", .012);
            layers.Add(splash);
        }

        if (context.QualityMode != SoundQualityMode.Draft &&
            (description.Gait is FootstepGait.Run or FootstepGait.Sprint or FootstepGait.Land || context.QualityMode == SoundQualityMode.Offline))
        {
            string release = LayerBuilders.AddNoiseBurst(builder, "release", "White", .04 + speed * .2,
                duration * .38, Semantic.Lerp(1_200, 4_800, speed), 1.1);
            release = LayerBuilders.AddDelayed(builder, release, "release-offset", duration * .55);
            layers.Add(release);
        }

        string mix = builder.AddMixer("footstep-mix", layers, .68);
        string pan = builder.AddNode("foot-pan", "core.pan", ("pan", description.LeftFoot ? -.08 : .08));
        builder.Connect(mix, pan);
        string current = builder.AddEnvironment(pan, description.Environment, .08f, .5f);
        current = builder.AddProtection(current, -1.2);
        builder.AddMacro("weight", "Weight", weight,
            GeneratedGraphBuilder.Binding(sole, "frequencyHz", (float)(bodyFrequency * 1.5), (float)(bodyFrequency * .55), MacroMappingCurve.Inverted),
            GeneratedGraphBuilder.Binding(sole, "amplitude", .08f, .72f, MacroMappingCurve.SmoothStep));
        builder.AddMacro("speed", "Speed", speed,
            GeneratedGraphBuilder.Binding(contactSource, "amplitude", .08f, .78f, MacroMappingCurve.SmoothStep));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["footwear"] = description.Footwear.ToString(),
            ["surface"] = description.Surface.ToString(),
            ["gait"] = description.Gait.ToString(),
            ["side"] = description.LeftFoot ? "left" : "right"
        });
    }

    public SoundVariationSet GenerateSet(FootstepDescription description, SoundGenerationContext context, int count)
    {
        count = Math.Clamp(count, 1, 128);
        var variations = new List<SoundGraphDefinition>(count);
        for (int i = 0; i < count; i++)
        {
            SoundGenerationContext variationContext = context.WithVariation(i) with
            {
                GraphId = $"{context.GraphId ?? "footstep.family"}.{i + 1:00}",
                DisplayName = $"{context.DisplayName ?? "Footstep"} {i + 1:00}"
            };
            variations.Add(Generate(description with { LeftFoot = (i & 1) == 0 }, variationContext));
        }

        return new SoundVariationSet(context.GraphId ?? "footstep.family", context.FamilySeed, variations);
    }

    private static double FootwearPitch(FootwearType footwear) => footwear switch
    {
        FootwearType.Bare or FootwearType.SoftPaw => .82,
        FootwearType.Boot => .72,
        FootwearType.Armoured => 1.24,
        FootwearType.Hoof => 1.45,
        FootwearType.Claw => 1.65,
        _ => 1
    };

    private static string FootwearWaveform(FootwearType footwear) => footwear switch
    {
        FootwearType.Armoured or FootwearType.Hoof or FootwearType.Claw => "Triangle",
        _ => "Sine"
    };
}

public sealed class MagicGenerator : ISoundGenerator<MagicDescription>
{
    public const string Id = "playroma.magic";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Magic Effect", SoundGeneratorCategory.Magic,
        "Compositional magic with charge, release, manifestation, impact, instability, and residual spatial tail.",
        .12, 14, ["magic", "spell", "fantasy", "macro"]);

    public SoundGraphDefinition Generate(MagicDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        var random = context.CreateRandom("magic.layers");
        float power = Semantic.Clamp01(description.Power);
        float stability = Semantic.Clamp01(description.Stability);
        double castDuration = Semantic.Lerp(.12, 5.8, description.CastDuration);
        (double root, string waveform, string noise) = ArchetypeVoice(description.Archetype);
        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"magic.{description.Archetype.ToString().ToLowerInvariant()}", $"{description.Archetype} Magic");
        var layers = new List<string>();

        string charge = LayerBuilders.AddGestureTone(builder, "charge", waveform, root * .62, .12 + power * .28,
            castDuration, "0:-12:EaseOut;0.32:-5:SmoothStep;0.72:2:SmoothStep;1:7:EaseIn",
            "0:0:EaseOut;0.28:0.62:SmoothStep;0.78:0.88:SmoothStep;1:0:EaseIn",
            vibratoDepthSemitones: Semantic.Lerp(.04, 1.1, 1 - stability),
            vibratoRateHz: Semantic.Lerp(3.2, 9, power),
            driftDepthSemitones: Semantic.Lerp(.02, .55, 1 - stability),
            driftRateHz: 1.2, attackSeconds: castDuration * .08, releaseSeconds: castDuration * .12);
        layers.Add(charge);

        int motifCount = Math.Min(context.Budget.MaximumPhraseNotes, context.QualityMode switch
        {
            SoundQualityMode.Draft => 0,
            SoundQualityMode.RealtimeLow => 1,
            SoundQualityMode.RealtimeHigh => 2,
            _ => 4
        });
        double[] motifRatios = [1.0, 1.5, 2.0, 2.5];
        for (int i = 0; i < motifCount && builder.NodeCount + 2 < builder.Budget.MaximumNodes; i++)
        {
            string motif = LayerBuilders.AddGestureTone(builder, $"casting-note-{i + 1}", waveform,
                root * motifRatios[i], .045 + power * .12, Math.Clamp(castDuration * .24, .08, .7),
                "0:-3:EaseOut;0.18:0:SmoothStep;0.72:1:SmoothStep;1:-2:EaseIn",
                "0:0:EaseOut;0.06:1:EaseOut;0.68:0.7:SmoothStep;1:0:EaseIn",
                vibratoDepthSemitones: .08 + (1 - stability) * .45,
                vibratoRateHz: 5 + (i * .7), attackSeconds: .002, releaseSeconds: .08);
            motif = LayerBuilders.AddDelayed(builder, motif, $"casting-note-{i + 1}-offset",
                castDuration * (.18 + (i * .13)));
            layers.Add(motif);
        }

        string release = LayerBuilders.AddNoiseBurst(builder, "release", noise, .18 + power * .6,
            Semantic.Lerp(.045, .25, 1 - stability), root * Semantic.Lerp(2.2, 6.5, power), .8 + stability,
            out string releaseSource);
        release = LayerBuilders.AddDelayed(builder, release, "release-offset", castDuration * .62);
        layers.Add(release);

        string manifestationSource = LayerBuilders.AddGestureTone(builder, "manifestation", waveform,
            root * Semantic.Lerp(1.1, 2.8, power), .16 + power * .4,
            castDuration * (.55 + description.ImpactSize),
            "0:7:EaseOut;0.08:0:EaseOut;0.48:-2:SmoothStep;1:-12:EaseIn",
            "0:0:EaseOut;0.018:1.05:EaseOut;0.36:0.72:SmoothStep;1:0:EaseIn",
            vibratoDepthSemitones: Semantic.Lerp(.02, .9, 1 - stability),
            vibratoRateHz: Semantic.Lerp(4, 10, power),
            driftDepthSemitones: Semantic.Lerp(.01, .42, 1 - stability),
            driftRateHz: 1.5, attackSeconds: .002, releaseSeconds: Math.Min(.65, castDuration * .42));
        string manifestation = LayerBuilders.AddDelayed(builder, manifestationSource, "manifestation-offset", castDuration * .54);
        if (stability < .85f)
        {
            string chorus = builder.AddNode("instability", "dsp.chorus",
                ("baseDelayMilliseconds", 5), ("depthMilliseconds", Semantic.Lerp(1, 8, 1 - stability)),
                ("rateHz", Semantic.Lerp(.2, 5.5, 1 - stability)), ("feedback", Semantic.Lerp(.04, .55, 1 - stability)),
                ("wet", Semantic.Lerp(.1, .62, 1 - stability)), ("dry", .9));
            builder.Connect(manifestation, chorus);
            manifestation = chorus;
        }
        layers.Add(manifestation);

        int sparkCount = Math.Min(context.Budget.MaximumSecondaryEvents, context.QualityMode switch
        {
            SoundQualityMode.Draft => 0,
            SoundQualityMode.RealtimeLow => 1,
            SoundQualityMode.RealtimeHigh => 2,
            _ => 4
        });
        for (int i = 0; i < sparkCount; i++)
        {
            string spark = LayerBuilders.AddGestureTone(builder, $"spark-{i + 1}", "Sine",
                root * GeneratorMath.Range(ref random, 3.1, 8.4), GeneratorMath.Range(ref random, .035, .12),
                GeneratorMath.Range(ref random, .08, .28),
                "0:4:EaseOut;0.22:0:SmoothStep;1:-6:EaseIn",
                "0:0:EaseOut;0.035:1:EaseOut;1:0:EaseIn",
                vibratoDepthSemitones: .15, vibratoRateHz: 8,
                attackSeconds: .0005, releaseSeconds: GeneratorMath.Range(ref random, .06, .22));
            spark = LayerBuilders.AddDelayed(builder, spark, $"spark-{i + 1}-offset",
                castDuration * GeneratorMath.Range(ref random, .45, 1.35));
            layers.Add(spark);
        }

        string mix = builder.AddMixer("magic-mix", layers, .68);
        string current = mix;
        if (description.OrganicTechnology > .52f)
        {
            string shape = builder.AddNode("synthetic-character", "dsp.waveshaper",
                ("mode", "WaveFold"), ("driveDecibels", 2 + description.OrganicTechnology * 12),
                ("bias", 0), ("mix", .18 + description.OrganicTechnology * .4), ("outputDecibels", -2));
            builder.Connect(current, shape);
            current = shape;
        }
        current = builder.AddEnvironment(current, description.Environment, 0.1f, description.SpatialExtent);
        current = builder.AddProtection(current, -.8);

        builder.AddMacro("power", "Power", power,
            GeneratedGraphBuilder.Binding(manifestationSource, "amplitude", .08f, .9f, MacroMappingCurve.SmoothStep));
        builder.AddMacro("instability", "Instability", 1 - stability,
            GeneratedGraphBuilder.Binding(releaseSource, "amplitude", .08f, .88f, MacroMappingCurve.Exponential));
        builder.AddMacro("distance", "Distance", .1f,
            GeneratedGraphBuilder.Binding(mix, "gain", .85f, .28f, MacroMappingCurve.Inverted));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["archetype"] = description.Archetype.ToString(),
            ["environment"] = description.Environment.ToString()
        });
    }

    private static (double Root, string Waveform, string Noise) ArchetypeVoice(MagicArchetype archetype) => archetype switch
    {
        MagicArchetype.Fire => (185, "Saw", "Pink"),
        MagicArchetype.Frost => (520, "Triangle", "Blue"),
        MagicArchetype.Lightning => (880, "Pulse", "White"),
        MagicArchetype.Holy => (440, "Sine", "White"),
        MagicArchetype.Shadow => (74, "Saw", "Brown"),
        MagicArchetype.Nature => (260, "Triangle", "Pink"),
        MagicArchetype.Psychic => (720, "Sine", "Violet"),
        MagicArchetype.Teleportation => (610, "Pulse", "Blue"),
        MagicArchetype.Summoning => (120, "Saw", "Brown"),
        MagicArchetype.Healing => (392, "Sine", "Pink"),
        MagicArchetype.Curse => (96, "Pulse", "Brown"),
        _ => (330, "Triangle", "Blue")
    };
}
