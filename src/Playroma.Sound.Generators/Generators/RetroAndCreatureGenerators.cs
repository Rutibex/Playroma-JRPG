using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Generators;

public sealed class RetroGameGenerator : ISoundGenerator<RetroSoundDescription>
{
    public const string Id = "playroma.retro";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Retro Game Effect", SoundGeneratorCategory.Retro,
        "Constraint-based pulse, wavetable-like, FM-like, handheld, 8-bit, and 16-bit game effects.",
        .025, 4, ["retro", "chip", "arcade", "game"]);

    public SoundGraphDefinition Generate(RetroSoundDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        var random = context.CreateRandom("retro.notes");
        float durationControl = Semantic.Clamp01(description.Duration);
        double duration = Semantic.Lerp(.045, 1.25, durationControl);
        double root = Semantic.Exp(95, 1_650, description.Pitch) * EventPitch(description.Event);
        int maximumNotes = context.QualityMode == SoundQualityMode.Draft ? 2 : 4;
        int notes = Math.Clamp(description.Notes, 1, Math.Min(maximumNotes, context.Budget.MaximumSecondaryEvents + 2));
        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"retro.{description.Event.ToString().ToLowerInvariant()}", $"Retro {description.Event}");
        var layers = new List<string>();
        string? primary = null;

        for (int i = 0; i < notes; i++)
        {
            double step = SequenceStep(description.Event, i, notes);
            double quantized = Quantize(root * step, description.Style);
            string note = LayerBuilders.AddTonalBody(builder, $"voice-{i + 1}", StyleWaveform(description.Style), quantized,
                .18 + (description.Brightness * .26), duration / Math.Max(1, notes - .25), .0005, duration / notes * .72);
            if (i > 0)
            {
                note = LayerBuilders.AddDelayed(builder, note, $"voice-{i + 1}-offset", duration * i / notes * .72);
            }
            primary ??= note;
            layers.Add(note);
        }

        if (description.Event is RetroEventKind.Explosion or RetroEventKind.Hit or RetroEventKind.Death)
        {
            layers.Add(LayerBuilders.AddNoiseBurst(builder, "noise-channel", "White", .16 + description.Dirt * .34,
                duration * .65, Semantic.Lerp(600, 5_000, description.Brightness), .7));
        }

        string mix = builder.AddMixer("retro-mix", layers, .72);
        string crusher = builder.AddNode("style-constraints", "dsp.bitcrusher",
            ("bitDepth", StyleBits(description.Style, description.Dirt)),
            ("sampleRateReduction", StyleReduction(description.Style, description.Dirt)),
            ("mix", Semantic.Lerp(.35, 1, description.Dirt)));
        builder.Connect(mix, crusher);
        string current = builder.AddProtection(crusher, -1);

        if (primary is not null)
        {
            builder.AddMacro("pitch", "Pitch", description.Pitch,
                GeneratedGraphBuilder.Binding(primary, "frequencyHz", (float)(root * .5), (float)(root * 2), MacroMappingCurve.Logarithmic));
        }

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["style"] = description.Style.ToString(),
            ["event"] = description.Event.ToString(),
            ["notes"] = notes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private static string StyleWaveform(RetroStyle style) => style switch
    {
        RetroStyle.PulseConsole or RetroStyle.Handheld or RetroStyle.Generic8Bit => "Pulse",
        RetroStyle.FmArcade => "Sine",
        RetroStyle.WavetableConsole or RetroStyle.Generic16Bit => "Triangle",
        _ => "Square"
    };

    private static double Quantize(double frequency, RetroStyle style)
    {
        double steps = style is RetroStyle.Generic16Bit or RetroStyle.WavetableConsole ? 48 : 24;
        double midi = 69 + (12 * Math.Log2(frequency / 440));
        midi = Math.Round(midi * (steps / 12)) / (steps / 12);
        return 440 * Math.Pow(2, (midi - 69) / 12);
    }

    private static double EventPitch(RetroEventKind kind) => kind switch
    {
        RetroEventKind.Explosion or RetroEventKind.Death => .45,
        RetroEventKind.Hit => .75,
        RetroEventKind.PowerUp => 1.25,
        RetroEventKind.Alarm => .9,
        _ => 1
    };

    private static double SequenceStep(RetroEventKind kind, int index, int count)
    {
        double t = count <= 1 ? 0 : index / (double)(count - 1);
        return kind switch
        {
            RetroEventKind.Death => Math.Pow(.55, t),
            RetroEventKind.Alarm => index % 2 == 0 ? 1 : 1.335,
            RetroEventKind.Hit => 1 - (.22 * t),
            RetroEventKind.PowerUp or RetroEventKind.Pickup or RetroEventKind.Jump => 1 + (.8 * t),
            _ => 1 + (.35 * t)
        };
    }

    private static double StyleBits(RetroStyle style, float dirt) => style switch
    {
        RetroStyle.Handheld => Semantic.Lerp(5, 9, 1 - dirt),
        RetroStyle.Generic8Bit or RetroStyle.PulseConsole => Semantic.Lerp(6, 11, 1 - dirt),
        RetroStyle.Generic16Bit or RetroStyle.WavetableConsole => Semantic.Lerp(10, 16, 1 - dirt),
        _ => Semantic.Lerp(8, 14, 1 - dirt)
    };

    private static double StyleReduction(RetroStyle style, float dirt) => style switch
    {
        RetroStyle.Handheld => Semantic.Lerp(3, 12, dirt),
        RetroStyle.Generic8Bit or RetroStyle.PulseConsole => Semantic.Lerp(2, 8, dirt),
        RetroStyle.Generic16Bit or RetroStyle.WavetableConsole => Semantic.Lerp(1, 3, dirt),
        _ => Semantic.Lerp(1, 5, dirt)
    };
}

public sealed class CreatureVoiceGenerator : ISoundGenerator<CreatureVoiceDescription>
{
    public const string Id = "playroma.creature";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Creature Voice", SoundGeneratorCategory.Creature,
        "A creature-vocal foundation using pitched excitation, breath, subharmonics, formant-like filters, and emotional contour.",
        .08, 8, ["creature", "voice", "growl", "formant"]);

    public SoundGraphDefinition Generate(CreatureVoiceDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        var random = context.CreateRandom("creature.voice");
        float size = Semantic.Clamp01(description.Size);
        float aggression = Semantic.Clamp01(description.Aggression);
        double duration = Semantic.Lerp(.12, 2.4, description.Duration);
        double root = Semantic.Lerp(720, 42, size) * Semantic.Lerp(1.3, .72, description.ThroatLength);
        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"creature.{description.Call.ToString().ToLowerInvariant()}", $"Creature {description.Call}");
        var layers = new List<string>();

        string voiced = LayerBuilders.AddTonalBody(builder, "voice-source", VoiceWaveform(description), root,
            .2 + aggression * .42, duration, Semantic.Lerp(.004, .12, description.Fear), duration * .65);
        layers.Add(voiced);

        string breath = LayerBuilders.AddNoiseBurst(builder, "breath", aggression > .6f ? "Brown" : "Pink",
            .08 + aggression * .28 + description.Pain * .18, duration * .82,
            Semantic.Lerp(450, 4_200, 1 - size), .6,
            out string breathSource);
        layers.Add(breath);

        if (size > .28f || aggression > .45f)
        {
            string sub = LayerBuilders.AddTonalBody(builder, "subharmonic", "Sine", Math.Max(20, root * .5),
                .06 + size * .3, duration * .9, .005, duration * .74);
            layers.Add(sub);
        }

        string mix = builder.AddMixer("excitation-mix", layers, .72);
        var formants = new List<string>();
        int formantCount = context.QualityMode switch
        {
            SoundQualityMode.Draft => 1,
            SoundQualityMode.RealtimeLow => 2,
            _ => 3
        };
        double[] ratios = [2.6, 5.2, 8.7];
        for (int i = 0; i < formantCount; i++)
        {
            string filter = builder.AddNode($"formant-{i + 1}", "dsp.biquad",
                ("filterType", "BandPass"),
                ("frequencyHz", Math.Clamp(root * ratios[i] * GeneratorMath.Range(ref random, .92, 1.08), 80, 12_000)),
                ("q", Semantic.Lerp(1.2, 5.5, description.ThroatLength)),
                ("gainDecibels", 0));
            builder.Connect(mix, filter);
            formants.Add(filter);
        }

        string formantMix = builder.AddMixer("formant-mix", formants, 1d / Math.Max(1, formantCount));
        string current = formantMix;
        if (aggression > .2f || description.OrganicSynthetic > .25f)
        {
            string growl = builder.AddNode("vocal-drive", "dsp.waveshaper",
                ("mode", description.OrganicSynthetic > .6f ? "WaveFold" : "HyperbolicTangent"),
                ("driveDecibels", Semantic.Lerp(1, 15, Math.Max(aggression, description.OrganicSynthetic))),
                ("bias", 0), ("mix", Semantic.Lerp(.12, .62, aggression)), ("outputDecibels", -2));
            builder.Connect(current, growl);
            current = growl;
        }

        current = builder.AddEnvironment(current, description.Environment, .1f, .55f);
        current = builder.AddProtection(current, -.8);
        builder.AddMacro("size", "Size", size,
            GeneratedGraphBuilder.Binding(voiced, "frequencyHz", (float)(root * 1.8), (float)(root * .45), MacroMappingCurve.Inverted));
        builder.AddMacro("aggression", "Aggression", aggression,
            GeneratedGraphBuilder.Binding(voiced, "amplitude", .1f, .82f, MacroMappingCurve.SmoothStep),
            GeneratedGraphBuilder.Binding(breathSource, "amplitude", .04f, .5f, MacroMappingCurve.Exponential));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["call"] = description.Call.ToString(),
            ["environment"] = description.Environment.ToString()
        });
    }

    private static string VoiceWaveform(CreatureVoiceDescription description) => description.Call switch
    {
        CreatureCallKind.Chirp or CreatureCallKind.Social => "Sine",
        CreatureCallKind.Hiss => "Triangle",
        _ when description.OrganicSynthetic > .55f => "Pulse",
        _ => "Saw"
    };
}
