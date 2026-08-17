using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Composition;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Generators;

/// <summary>Compact original creature signatures with multiple syllables and continuous pitch gestures.</summary>
public sealed class CreatureCryGenerator : ISoundGenerator<CreatureCryDescription>
{
    public const string Id = "playroma.creature-cry";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Creature Cry", SoundGeneratorCategory.CreatureCry,
        "Original multi-syllable creature cries with pitch bends, vocal formants, breath, rasp, repetition, and retro or modern character.",
        .12, 8, ["creature", "cry", "monster", "gesture", "vocal"]);

    public SoundGraphDefinition Generate(CreatureCryDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        var identityRandom = context.CreateRandom("cry.identity");
        CreatureCryStyle style = description.Style == CreatureCryStyle.Random
            ? (CreatureCryStyle)identityRandom.NextInt(Enum.GetValues<CreatureCryStyle>().Length - 1)
            : description.Style;

        float size = Semantic.Clamp01(description.Size);
        float cute = Semantic.Clamp01(description.Cuteness);
        float menace = Semantic.Clamp01(description.Menace);
        float energy = Semantic.Clamp01(description.Energy);
        float synthetic = Semantic.Clamp01(description.OrganicSynthetic);
        float animal = 1 - Semantic.Clamp01(description.MusicalAnimal);
        float rasp = Semantic.Clamp01(description.SmoothRaspy);
        double totalDuration = Semantic.Lerp(.16, 7.2, description.Duration);
        int requestedSyllables = Math.Clamp(description.Syllables + (int)Math.Round(description.Repetition * 3), 1, 32);
        int maximumByNodes = Math.Max(1, (context.Budget.MaximumNodes - 14) / 2);
        int syllables = Math.Min(requestedSyllables, Math.Min(context.Budget.MaximumPhraseNotes, maximumByNodes));
        double root = Semantic.Lerp(1_350, 58, size) * Semantic.Lerp(.8, 1.28, cute) * StylePitch(style);

        PhraseMood mood = menace > .62f ? PhraseMood.Sinister
            : cute > .66f ? PhraseMood.Cute
            : synthetic > .65f ? PhraseMood.Mechanical
            : PhraseMood.Alien;
        PhraseScale scale = animal > .58f ? PhraseScale.Chromatic
            : cute > .58f ? PhraseScale.MajorPentatonic
            : menace > .5f ? PhraseScale.MinorPentatonic
            : PhraseScale.WholeTone;
        var phrase = PhraseComposer.Compose(new PhrasePlanOptions
        {
            Shape = description.Shape,
            Mood = mood,
            Scale = scale,
            DesiredNotes = syllables,
            DurationSeconds = totalDuration * .88,
            Speed = Math.Clamp(energy * .82f + .1f, 0, 1),
            Complexity = Math.Clamp((description.Intelligence + animal) * .5f, 0, 1),
            PitchRange = description.PitchMovement,
            Repetition = description.Repetition,
            PitchMovement = Math.Clamp(description.PitchMovement * .75f + animal * .25f, 0, 1),
            Musicality = 1 - animal,
            OrganicAmount = 1 - synthetic
        }, context);

        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"creature-cry.{style.ToString().ToLowerInvariant()}", $"{style} Creature Cry");
        var voice = new PhraseVoiceSettings
        {
            Waveform = CryWaveform(style, synthetic),
            RootFrequencyHz = root,
            Amplitude = .18 + (energy * .34) + (menace * .16),
            VibratoDepthSemitones = Semantic.Lerp(.08, 2.8, description.PitchMovement) * Semantic.Lerp(.45, 1.2, animal),
            VibratoRateHz = Semantic.Lerp(3.2, 13, energy),
            TremoloDepth = style is CreatureCryStyle.Insectoid or CreatureCryStyle.Ghostly ? .28 : .04 + rasp * .12,
            TremoloRateHz = style == CreatureCryStyle.Insectoid ? Semantic.Lerp(14, 28, energy) : Semantic.Lerp(3, 9, energy),
            DriftDepthSemitones = Semantic.Lerp(.06, 1.8, 1 - synthetic) * Semantic.Lerp(.6, 1.2, animal),
            DriftRateHz = Semantic.Lerp(.35, 3.2, energy),
            AttackSeconds = Semantic.Lerp(.001, .045, 1 - energy),
            ReleaseSeconds = Semantic.Lerp(.025, .22, size),
            Quantization = synthetic > .72f ? "Chromatic" : "None",
            PulseWidth = Semantic.Lerp(.22, .65, cute)
        };
        PhraseRenderResult rendered = PhraseGraphRenderer.AddPhrase(builder, "syllable", phrase, voice,
            usePerNotePan: context.QualityMode == SoundQualityMode.Offline && syllables <= 10);

        var excitation = new List<string>(rendered.Outputs);
        string breath = LayerBuilders.AddNoiseBurst(builder, "breath", rasp > .65f ? "Brown" : "Pink",
            .035 + (rasp * .24) + (animal * .08), Math.Max(.08, rendered.EndSeconds * .94),
            Semantic.Lerp(500, 6_500, 1 - size), Semantic.Lerp(.45, 1.8, 1 - rasp), out string breathSource,
            .002, .72);
        excitation.Add(breath);

        if ((size > .5f || menace > .55f) && builder.NodeCount + 1 < builder.Budget.MaximumNodes)
        {
            string sub = LayerBuilders.AddGestureTone(builder, "chest-sub", "Sine", Math.Max(22, root * .5),
                .035 + (size * .18) + (menace * .08), Math.Max(.08, rendered.EndSeconds * .88),
                "0:-1:EaseOut;0.35:0:SmoothStep;1:-3:EaseIn",
                "0:0:EaseOut;0.08:0.8:SmoothStep;0.7:0.55:SmoothStep;1:0:EaseIn",
                driftDepthSemitones: .12 + ((1 - synthetic) * .35), driftRateHz: .7,
                attackSeconds: .008, releaseSeconds: .12);
            excitation.Add(sub);
        }

        string mix = builder.AddMixer("cry-excitation", excitation, .62);
        int formantCount = Math.Min(context.Budget.MaximumResonances, context.QualityMode switch
        {
            SoundQualityMode.Draft => 1,
            SoundQualityMode.RealtimeLow => 2,
            _ => 3
        });
        double[] ratios = FormantRatios(style, description.Intelligence, cute);
        var formants = new List<string>(formantCount);
        for (int i = 0; i < formantCount && builder.NodeCount + 1 < builder.Budget.MaximumNodes; i++)
        {
            string filter = builder.AddNode($"moving-formant-{i + 1}", "dsp.biquad",
                ("filterType", "BandPass"),
                ("frequencyHz", Math.Clamp(root * ratios[i], 90, 12_000)),
                ("q", Semantic.Lerp(.75, 5.8, 1 - rasp)),
                ("gainDecibels", 0));
            builder.Connect(mix, filter);
            formants.Add(filter);
        }

        string current = formants.Count > 0 ? builder.AddMixer("cry-formants", formants, 1d / formants.Count) : mix;
        string? driveNode = null;
        if ((rasp > .08f || menace > .18f || synthetic > .3f) && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            driveNode = builder.AddNode("cry-rasp", "dsp.waveshaper",
                ("mode", synthetic > .62f ? "WaveFold" : "HyperbolicTangent"),
                ("driveDecibels", Semantic.Lerp(1, 18, Math.Max(rasp, menace))),
                ("bias", 0), ("mix", Semantic.Lerp(.06, .58, Math.Max(rasp, synthetic))), ("outputDecibels", -2.5));
            builder.Connect(current, driveNode);
            current = driveNode;
        }

        if (description.RetroModern < .55f && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string crusher = builder.AddNode("cry-era", "dsp.bitcrusher",
                ("bitDepth", Semantic.Lerp(6, 15, description.RetroModern)),
                ("sampleRateReduction", Semantic.Lerp(8, 1, description.RetroModern)),
                ("mix", Semantic.Lerp(.72, .08, description.RetroModern)));
            builder.Connect(current, crusher);
            current = crusher;
        }

        if ((description.Repetition > .38f || style == CreatureCryStyle.Ghostly) && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            double delayMs = Math.Clamp(totalDuration * Semantic.Lerp(90, 240, description.Repetition), 55, 650);
            string echo = builder.AddNode("cry-echo", "dsp.delay",
                ("maximumDelaySeconds", 1.2), ("delayMilliseconds", delayMs),
                ("feedback", Semantic.Lerp(.08, .42, description.Repetition)),
                ("wet", Semantic.Lerp(.08, .32, description.Repetition)), ("dry", 1),
                ("tailSeconds", Math.Min(builder.Budget.MaximumTailSeconds, 2.5)));
            builder.Connect(current, echo);
            current = echo;
        }

        current = builder.AddEnvironment(current, description.Environment, .08f, .42f + (size * .25f));
        current = builder.AddProtection(current, -.8);

        if (rendered.SourceNodes.Count > 0)
        {
            string primary = rendered.SourceNodes[0];
            builder.AddMacro("size", "Size", size,
                GeneratedGraphBuilder.Binding(primary, "rootFrequencyHz", (float)(root * 1.8), (float)(root * .42), MacroMappingCurve.Inverted));
            builder.AddMacro("energy", "Energy", energy,
                GeneratedGraphBuilder.Binding(primary, "amplitude", .08f, .9f, MacroMappingCurve.SmoothStep),
                GeneratedGraphBuilder.Binding(primary, "vibratoRateHz", 2.5f, 15f, MacroMappingCurve.Exponential));
        }
        builder.AddMacro("rasp", "Rasp", rasp,
            GeneratedGraphBuilder.Binding(breathSource, "amplitude", .02f, .48f, MacroMappingCurve.Exponential));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["style"] = style.ToString(),
            ["shape"] = description.Shape.ToString(),
            ["syllables"] = syllables.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["durationSeconds"] = rendered.EndSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["environment"] = description.Environment.ToString()
        });
    }

    private static string CryWaveform(CreatureCryStyle style, float synthetic) => style switch
    {
        CreatureCryStyle.Chirp or CreatureCryStyle.Avian => "Sine",
        CreatureCryStyle.Warble or CreatureCryStyle.Ghostly => "Triangle",
        CreatureCryStyle.Insectoid or CreatureCryStyle.Electronic => "Pulse",
        CreatureCryStyle.Growl or CreatureCryStyle.Roar or CreatureCryStyle.Draconic => "Saw",
        _ when synthetic > .58f => "Pulse",
        _ => "Triangle"
    };

    private static double StylePitch(CreatureCryStyle style) => style switch
    {
        CreatureCryStyle.Chirp or CreatureCryStyle.Avian => 1.42,
        CreatureCryStyle.Insectoid => 1.7,
        CreatureCryStyle.Growl => .72,
        CreatureCryStyle.Roar or CreatureCryStyle.Draconic => .54,
        CreatureCryStyle.Ghostly => .82,
        _ => 1
    };

    private static double[] FormantRatios(CreatureCryStyle style, float intelligence, float cute)
    {
        double speech = Semantic.Lerp(.8, 1.18, intelligence);
        double brightness = Semantic.Lerp(.82, 1.25, cute);
        return style switch
        {
            CreatureCryStyle.Insectoid => [4.8 * brightness, 8.4 * brightness, 13.2 * brightness],
            CreatureCryStyle.Growl or CreatureCryStyle.Roar or CreatureCryStyle.Draconic => [2.1 * speech, 3.8 * speech, 6.5 * speech],
            CreatureCryStyle.Avian or CreatureCryStyle.Chirp => [3.5 * brightness, 6.6 * brightness, 10.5 * brightness],
            _ => [2.6 * speech, 5.1 * speech, 8.6 * speech]
        };
    }
}

/// <summary>Short melodic signatures for discoveries, spells, transformations, doors, victories, and failures.</summary>
public sealed class MelodicEffectGenerator : ISoundGenerator<MelodicEffectDescription>
{
    public const string Id = "playroma.melodic-effect";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Melodic Effect", SoundGeneratorCategory.Melodic,
        "Compact motifs and stingers with selectable mood, contour, scale, harmony, articulation, and synthetic or acoustic character.",
        .12, 12, ["melodic", "motif", "stinger", "chime", "game"]);

    public SoundGraphDefinition Generate(MelodicEffectDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        float harmony = Semantic.Clamp01(description.Harmony);
        float synthetic = Semantic.Clamp01(description.AcousticSynthetic);
        float brightness = Semantic.Clamp01(description.Brightness);
        float dissonance = Semantic.Clamp01(description.TonalDissonant);
        double duration = Semantic.Lerp(.16, 9.5, description.Duration);
        int voices = harmony > .12f && context.Budget.MaximumPhraseVoices >= 2 ? 2 : 1;
        int nodeAllowance = Math.Max(1, (context.Budget.MaximumNodes - 14) / Math.Max(2, voices * 2));
        int notes = Math.Clamp(description.Notes + (int)Math.Round(description.Complexity * 5), 1,
            Math.Min(context.Budget.MaximumPhraseNotes, nodeAllowance));
        PhraseShape shape = EventShape(description.Kind, description.Shape);
        PhraseMood mood = EventMood(description.Kind, description.Mood);
        double root = Semantic.Exp(105, 1_250, description.Pitch) * MoodPitch(mood);

        IReadOnlyList<PhraseEvent> phrase = PhraseComposer.Compose(new PhrasePlanOptions
        {
            Shape = shape,
            Mood = mood,
            Scale = description.Scale,
            DesiredNotes = notes,
            DurationSeconds = duration * .86,
            Speed = description.Speed,
            Complexity = description.Complexity,
            PitchRange = description.PitchRange,
            Repetition = description.Repetition,
            PitchMovement = .08f + (description.Complexity * .28f),
            Musicality = 1 - dissonance,
            OrganicAmount = 1 - synthetic
        }, context);

        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"melodic.{description.Kind.ToString().ToLowerInvariant()}", $"{description.Kind} Melodic Effect");
        var mainVoice = new PhraseVoiceSettings
        {
            Waveform = MelodicWaveform(mood, synthetic),
            RootFrequencyHz = root,
            Amplitude = .2 + (brightness * .2),
            VibratoDepthSemitones = Semantic.Lerp(.02, .45, 1 - synthetic),
            VibratoRateHz = Semantic.Lerp(4, 7.5, brightness),
            DriftDepthSemitones = Semantic.Lerp(.01, .18, 1 - synthetic),
            DriftRateHz = 1.1,
            TremoloDepth = mood == PhraseMood.Dreamlike ? .08 : 0,
            TremoloRateHz = 4.2,
            AttackSeconds = Semantic.Lerp(.001, .025, 1 - description.Speed),
            ReleaseSeconds = Semantic.Lerp(.04, .24, description.Tail),
            Quantization = synthetic > .82f ? "Chromatic" : "None",
            PulseWidth = Semantic.Lerp(.25, .62, brightness)
        };
        PhraseRenderResult main = PhraseGraphRenderer.AddPhrase(builder, "melody", phrase, mainVoice,
            usePerNotePan: context.QualityMode == SoundQualityMode.Offline && notes <= 10);
        var layers = new List<string>(main.Outputs);

        string? harmonyMixer = null;
        if (voices >= 2)
        {
            double interval = HarmonyInterval(mood, dissonance);
            var harmonyVoice = mainVoice with
            {
                Waveform = synthetic > .62f ? "Sine" : "Triangle",
                Amplitude = mainVoice.Amplitude * Semantic.Lerp(.25, .62, harmony),
                VibratoDepthSemitones = mainVoice.VibratoDepthSemitones * .7,
                DriftDepthSemitones = mainVoice.DriftDepthSemitones * .6
            };
            PhraseRenderResult harmonyResult = PhraseGraphRenderer.AddPhrase(builder, "harmony", phrase, harmonyVoice,
                interval, Semantic.Lerp(.35, .8, harmony));
            harmonyMixer = builder.AddMixer("harmony-mix", harmonyResult.Outputs, .7);
            layers.Add(harmonyMixer);
        }

        if (brightness > .55f && builder.NodeCount + 2 < builder.Budget.MaximumNodes)
        {
            string shimmer = LayerBuilders.AddNoiseBurst(builder, "shimmer", "Blue", .025 + brightness * .08,
                Math.Min(.45, duration * .28), Semantic.Lerp(3_000, 10_000, brightness), 1.6);
            layers.Add(shimmer);
        }

        string mix = builder.AddMixer("melodic-mix", layers, .68);
        string current = mix;
        if (synthetic > .38f && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string chorus = builder.AddNode("voice-width", "dsp.chorus",
                ("baseDelayMilliseconds", 6), ("depthMilliseconds", Semantic.Lerp(.7, 5, synthetic)),
                ("rateHz", Semantic.Lerp(.18, 1.6, description.Complexity)), ("feedback", .08),
                ("wet", Semantic.Lerp(.06, .34, synthetic)), ("dry", 1));
            builder.Connect(current, chorus);
            current = chorus;
        }

        if (description.Tail > .16f && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string delay = builder.AddNode("motif-echo", "dsp.delay",
                ("maximumDelaySeconds", 2.5),
                ("delayMilliseconds", Semantic.Lerp(75, 420, description.Tail)),
                ("feedback", Semantic.Lerp(.05, .38, description.Tail)),
                ("wet", Semantic.Lerp(.04, .26, description.Tail)), ("dry", 1),
                ("tailSeconds", Math.Min(builder.Budget.MaximumTailSeconds, Semantic.Lerp(.4, 4, description.Tail))));
            builder.Connect(current, delay);
            current = delay;
        }

        current = builder.AddEnvironment(current, description.Environment, .04f, .25f + (description.Tail * .65f));
        current = builder.AddProtection(current, -.9);

        if (main.SourceNodes.Count > 0)
        {
            builder.AddMacro("pitch", "Pitch", description.Pitch,
                GeneratedGraphBuilder.Binding(main.SourceNodes[0], "rootFrequencyHz", (float)(root * .5), (float)(root * 2), MacroMappingCurve.Logarithmic));
            builder.AddMacro("brightness", "Brightness", brightness,
                GeneratedGraphBuilder.Binding(main.SourceNodes[0], "amplitude", .08f, .72f, MacroMappingCurve.SmoothStep));
        }
        if (harmonyMixer is not null)
        {
            builder.AddMacro("harmony", "Harmony", harmony,
                GeneratedGraphBuilder.Binding(harmonyMixer, "gain", 0f, 1.15f, MacroMappingCurve.SmoothStep));
        }

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["kind"] = description.Kind.ToString(),
            ["mood"] = mood.ToString(),
            ["shape"] = shape.ToString(),
            ["scale"] = description.Scale.ToString(),
            ["notes"] = notes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["durationSeconds"] = main.EndSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    private static PhraseShape EventShape(MelodicEffectKind kind, PhraseShape requested) => kind switch
    {
        MelodicEffectKind.LevelUp or MelodicEffectKind.ItemDiscovery or MelodicEffectKind.TreasureReveal => PhraseShape.Rising,
        MelodicEffectKind.FailureSting or MelodicEffectKind.Curse => PhraseShape.Falling,
        MelodicEffectKind.Teleportation or MelodicEffectKind.Transformation => PhraseShape.ChargeRelease,
        MelodicEffectKind.Vision or MelodicEffectKind.MysteriousChime => PhraseShape.Arch,
        _ => requested
    };

    private static PhraseMood EventMood(MelodicEffectKind kind, PhraseMood requested) => kind switch
    {
        MelodicEffectKind.Healing or MelodicEffectKind.Enchantment => PhraseMood.Holy,
        MelodicEffectKind.Curse or MelodicEffectKind.FailureSting => PhraseMood.Sinister,
        MelodicEffectKind.Teleportation or MelodicEffectKind.Transformation => PhraseMood.Alien,
        MelodicEffectKind.PuzzleSolved or MelodicEffectKind.LevelUp => PhraseMood.Heroic,
        _ => requested
    };

    private static string MelodicWaveform(PhraseMood mood, float synthetic) => mood switch
    {
        PhraseMood.Holy or PhraseMood.Dreamlike => "Sine",
        PhraseMood.Ancient => "Triangle",
        PhraseMood.Mechanical => synthetic > .4f ? "Pulse" : "Triangle",
        PhraseMood.Sinister => synthetic > .6f ? "Saw" : "Triangle",
        _ => synthetic > .7f ? "Pulse" : "Sine"
    };

    private static double MoodPitch(PhraseMood mood) => mood switch
    {
        PhraseMood.Cute => 1.25,
        PhraseMood.Holy => 1.12,
        PhraseMood.Sinister or PhraseMood.Ancient => .72,
        PhraseMood.Mechanical => .92,
        _ => 1
    };

    private static double HarmonyInterval(PhraseMood mood, float dissonance)
    {
        if (dissonance > .75f) return 6;
        if (dissonance > .42f) return 1 + (dissonance * 3);
        return mood switch
        {
            PhraseMood.Sinister => 3,
            PhraseMood.Holy or PhraseMood.Heroic => 7,
            PhraseMood.Cute => 4,
            PhraseMood.Alien => 8,
            _ => 5
        };
    }
}

/// <summary>Longer spell effects constructed from anticipation, charge, motif, release, impact, and afterglow stages.</summary>
public sealed class ComplexMagicGenerator : ISoundGenerator<ComplexMagicDescription>
{
    public const string Id = "playroma.complex-magic";
    public string GeneratorId => Id;
    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id, "Complex Magic", SoundGeneratorCategory.ComplexMagic,
        "Multi-stage spell effects with anticipation, a composed casting motif, release, impact, and a controllable residual afterglow.",
        .4, 16, ["magic", "spell", "multi-stage", "motif", "long"]);

    public SoundGraphDefinition Generate(ComplexMagicDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);
        float power = Semantic.Clamp01(description.Power);
        float stability = Semantic.Clamp01(description.Stability);
        float synthetic = Semantic.Clamp01(description.OrganicTechnology);
        double total = Semantic.Lerp(.55, 12.5, description.Duration);
        (double root, string waveform, string noise) = ArchetypeVoice(description.Archetype);

        double[] weights =
        [
            .35 + description.Anticipation,
            .55 + description.Charge,
            .55 + description.Motif,
            .18 + description.Release * .55,
            .25 + description.Impact * .7,
            .45 + description.Afterglow
        ];
        double sum = weights.Sum();
        double anticipationDuration = total * weights[0] / sum;
        double chargeDuration = total * weights[1] / sum;
        double motifDuration = total * weights[2] / sum;
        double releaseDuration = total * weights[3] / sum;
        double impactDuration = total * weights[4] / sum;
        double afterglowDuration = total * weights[5] / sum;
        double chargeStart = anticipationDuration * .62;
        double releaseStart = anticipationDuration + chargeDuration + motifDuration;
        double impactStart = releaseStart + (releaseDuration * .35);
        double afterglowStart = impactStart + (impactDuration * .42);

        int nodeAllowance = Math.Max(1, (context.Budget.MaximumNodes - 24) / 2);
        int notes = Math.Clamp(description.MotifNotes, 1, Math.Min(context.Budget.MaximumPhraseNotes, nodeAllowance));
        IReadOnlyList<PhraseEvent> phrase = PhraseComposer.Compose(new PhrasePlanOptions
        {
            Shape = description.Shape,
            Mood = description.Mood,
            Scale = description.Scale,
            DesiredNotes = notes,
            DurationSeconds = chargeDuration + motifDuration,
            StartOffsetSeconds = chargeStart,
            Speed = Math.Clamp(.3f + power * .45f, 0, 1),
            Complexity = Math.Clamp(description.Musicality * .65f + (1 - stability) * .35f, 0, 1),
            PitchRange = Math.Clamp(.35f + power * .45f, 0, 1),
            Repetition = description.Archetype == MagicArchetype.Summoning ? .4f : .08f,
            PitchMovement = Math.Clamp(.2f + (1 - stability) * .65f, 0, 1),
            Musicality = description.Musicality,
            OrganicAmount = 1 - synthetic
        }, context);

        var builder = new GeneratedGraphBuilder(Id, Descriptor.Version, context,
            $"complex-magic.{description.Archetype.ToString().ToLowerInvariant()}", $"Complex {description.Archetype} Spell");
        var layers = new List<string>();

        string anticipation = LayerBuilders.AddGestureTone(builder, "anticipation", waveform, root * .42,
            .06 + power * .15, Math.Max(.08, anticipationDuration),
            "0:-7:EaseOut;0.45:-2:SmoothStep;1:0:EaseIn",
            "0:0:EaseOut;0.3:0.55:SmoothStep;0.82:0.72:SmoothStep;1:0:EaseIn",
            vibratoDepthSemitones: .08 + (1 - stability) * .45,
            vibratoRateHz: 3.4, driftDepthSemitones: .15 + (1 - stability) * .5,
            driftRateHz: .65, attackSeconds: .02, releaseSeconds: .08);
        layers.Add(anticipation);

        var voice = new PhraseVoiceSettings
        {
            Waveform = waveform,
            RootFrequencyHz = root,
            Amplitude = .12 + power * .34,
            VibratoDepthSemitones = Semantic.Lerp(.04, 1.35, 1 - stability),
            VibratoRateHz = Semantic.Lerp(3.5, 9.5, power),
            DriftDepthSemitones = Semantic.Lerp(.02, .7, 1 - stability),
            DriftRateHz = Semantic.Lerp(.55, 2.8, 1 - stability),
            TremoloDepth = description.Archetype is MagicArchetype.Shadow or MagicArchetype.Psychic ? .12 : 0,
            TremoloRateHz = Semantic.Lerp(3, 11, power),
            AttackSeconds = .003,
            ReleaseSeconds = .12,
            Quantization = synthetic > .76f ? "Chromatic" : "None",
            PulseWidth = .38
        };
        PhraseRenderResult motif = PhraseGraphRenderer.AddPhrase(builder, "casting-motif", phrase, voice);
        layers.AddRange(motif.Outputs);

        string release = LayerBuilders.AddNoiseBurst(builder, "release", noise, .14 + power * .65,
            Math.Clamp(releaseDuration, .035, .55), root * Semantic.Lerp(2.5, 8, power), .65 + stability,
            out string releaseSource, .0004, .82);
        release = LayerBuilders.AddDelayed(builder, release, "release-offset", releaseStart);
        layers.Add(release);

        string impact = LayerBuilders.AddGestureTone(builder, "impact-body", ImpactWaveform(description.Archetype),
            Math.Max(24, root * Semantic.Lerp(.32, .82, description.Benevolence)),
            .12 + power * .48, Math.Clamp(impactDuration * 1.5, .08, 2.2),
            "0:5:EaseOut;0.08:0:EaseOut;0.62:-4:SmoothStep;1:-9:EaseIn",
            "0:0:EaseOut;0.012:1.05:EaseOut;0.2:0.72:SmoothStep;1:0:EaseIn",
            driftDepthSemitones: (1 - stability) * .35, driftRateHz: 1.2,
            attackSeconds: .0005, releaseSeconds: Math.Min(.5, impactDuration));
        impact = LayerBuilders.AddDelayed(builder, impact, "impact-offset", impactStart);
        layers.Add(impact);

        string afterglow = LayerBuilders.AddGestureTone(builder, "afterglow", description.Benevolence > .55f ? "Sine" : "Triangle",
            root * Semantic.Lerp(1.5, 3.2, description.Musicality),
            .04 + description.Afterglow * .16, Math.Clamp(afterglowDuration * 1.8, .12, 4.5),
            description.Archetype == MagicArchetype.Teleportation
                ? "0:-12:EaseOut;0.35:0:SmoothStep;0.72:7:SmoothStep;1:12:EaseIn"
                : "0:4:EaseOut;0.35:0:SmoothStep;0.72:-5:SmoothStep;1:-12:EaseIn",
            "0:0:EaseOut;0.16:0.72:SmoothStep;0.68:0.5:SmoothStep;1:0:EaseIn",
            vibratoDepthSemitones: .12 + description.Afterglow * .42,
            vibratoRateHz: 5.2, tremoloDepth: .05 + description.Afterglow * .12, tremoloRateHz: 4.2,
            driftDepthSemitones: .08 + (1 - stability) * .3, driftRateHz: .55,
            attackSeconds: .03, releaseSeconds: Math.Min(1.2, afterglowDuration));
        afterglow = LayerBuilders.AddDelayed(builder, afterglow, "afterglow-offset", afterglowStart);
        layers.Add(afterglow);

        int sparks = Math.Min(context.Budget.MaximumSecondaryEvents, context.QualityMode switch
        {
            SoundQualityMode.Draft => 0,
            SoundQualityMode.RealtimeLow => 1,
            SoundQualityMode.RealtimeHigh => 2,
            _ => 4
        });
        var sparkRandom = context.CreateRandom("complex-magic.sparks");
        for (int i = 0; i < sparks && builder.NodeCount + 2 < builder.Budget.MaximumNodes; i++)
        {
            double sparkTime = releaseStart + GeneratorMath.Range(ref sparkRandom, 0, Math.Max(.05, afterglowDuration));
            string spark = LayerBuilders.AddGestureTone(builder, $"spark-{i + 1}", "Sine",
                root * GeneratorMath.Range(ref sparkRandom, 3, 9), GeneratorMath.Range(ref sparkRandom, .025, .09),
                GeneratorMath.Range(ref sparkRandom, .06, .28),
                "0:3:EaseOut;0.3:0:SmoothStep;1:-5:EaseIn",
                "0:0:EaseOut;0.04:1:EaseOut;1:0:EaseIn",
                vibratoDepthSemitones: .2, vibratoRateHz: 9,
                attackSeconds: .0005, releaseSeconds: .08);
            spark = LayerBuilders.AddDelayed(builder, spark, $"spark-{i + 1}-offset", sparkTime);
            layers.Add(spark);
        }

        string mix = builder.AddMixer("spell-stages", layers, .58);
        string current = mix;
        if ((synthetic > .3f || stability < .7f) && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string character;
            if (synthetic > .55f)
            {
                character = builder.AddNode("spell-character", "dsp.waveshaper",
                    ("mode", synthetic > .8f ? "WaveFold" : "HyperbolicTangent"),
                    ("driveDecibels", Semantic.Lerp(2, 14, synthetic)),
                    ("bias", 0),
                    ("mix", Semantic.Lerp(.1, .48, synthetic)),
                    ("outputDecibels", -2));
            }
            else
            {
                character = builder.AddNode("spell-character", "dsp.chorus",
                    ("baseDelayMilliseconds", 5),
                    ("depthMilliseconds", Semantic.Lerp(1, 7, 1 - stability)),
                    ("rateHz", Semantic.Lerp(.2, 4, 1 - stability)),
                    ("feedback", Semantic.Lerp(.02, .35, 1 - stability)),
                    ("wet", Semantic.Lerp(.08, .48, 1 - stability)),
                    ("dry", 1));
            }
            builder.Connect(current, character);
            current = character;
        }

        if (description.Afterglow > .18f && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string echo = builder.AddNode("afterglow-echo", "dsp.delay",
                ("maximumDelaySeconds", 3.5),
                ("delayMilliseconds", Semantic.Lerp(90, 520, description.Afterglow)),
                ("feedback", Semantic.Lerp(.08, .48, description.Afterglow)),
                ("wet", Semantic.Lerp(.06, .34, description.Afterglow)), ("dry", 1),
                ("tailSeconds", Math.Min(builder.Budget.MaximumTailSeconds, Semantic.Lerp(.6, 7, description.Afterglow))));
            builder.Connect(current, echo);
            current = echo;
        }

        current = builder.AddEnvironment(current, description.Environment, .06f, description.SpatialExtent);
        current = builder.AddProtection(current, -.8);

        if (motif.SourceNodes.Count > 0)
        {
            builder.AddMacro("power", "Power", power,
                GeneratedGraphBuilder.Binding(motif.SourceNodes[0], "amplitude", .06f, .9f, MacroMappingCurve.SmoothStep));
            builder.AddMacro("instability", "Instability", 1 - stability,
                GeneratedGraphBuilder.Binding(motif.SourceNodes[0], "vibratoDepthSemitones", .02f, 3.2f, MacroMappingCurve.Exponential),
                GeneratedGraphBuilder.Binding(releaseSource, "amplitude", .08f, .95f, MacroMappingCurve.SmoothStep));
        }
        builder.AddMacro("distance", "Distance", .08f,
            GeneratedGraphBuilder.Binding(mix, "gain", .85f, .22f, MacroMappingCurve.Inverted));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["archetype"] = description.Archetype.ToString(),
            ["mood"] = description.Mood.ToString(),
            ["shape"] = description.Shape.ToString(),
            ["scale"] = description.Scale.ToString(),
            ["motifNotes"] = notes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["stages"] = "anticipation,charge,motif,release,impact,afterglow",
            ["durationSeconds"] = total.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        });
    }

    internal static (double Root, string Waveform, string Noise) ArchetypeVoice(MagicArchetype archetype) => archetype switch
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

    private static string ImpactWaveform(MagicArchetype archetype) => archetype switch
    {
        MagicArchetype.Lightning or MagicArchetype.Teleportation => "Pulse",
        MagicArchetype.Fire or MagicArchetype.Shadow or MagicArchetype.Curse => "Saw",
        MagicArchetype.Holy or MagicArchetype.Healing => "Sine",
        _ => "Triangle"
    };
}
