using System.Globalization;
using Playroma.Sound.Core.Generation;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Generators.Profiles;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Building;

internal enum UniversalPitchPattern
{
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

internal sealed record UniversalSoundPlan
{
    public required string GeneratorId { get; init; }
    public required int GeneratorVersion { get; init; }
    public required string FallbackId { get; init; }
    public required string DisplayName { get; init; }
    public HybridSynthesisMode SynthesisMode { get; init; } = HybridSynthesisMode.Basic;
    public Waveform Waveform { get; init; } = Waveform.Sine;
    public ProceduralTextureKind? TextureKind { get; init; }
    public UniversalPitchPattern PitchPattern { get; init; } = UniversalPitchPattern.Flat;
    public AcousticEnvironment Environment { get; init; } = AcousticEnvironment.SmallRoom;
    public SoundVariationMode VariationMode { get; init; } = SoundVariationMode.Natural;
    public UniversalArticulation Articulation { get; init; } = UniversalArticulation.Smooth;
    public UniversalPhraseStructure Structure { get; init; } = UniversalPhraseStructure.Single;
    public UniversalRhythmStyle RhythmStyle { get; init; } = UniversalRhythmStyle.Natural;
    public UniversalEnvelopeStyle EnvelopeStyle { get; init; } = UniversalEnvelopeStyle.Automatic;
    public UniversalEmotion Emotion { get; init; } = UniversalEmotion.Neutral;
    public UniversalEnergyState EnergyState { get; init; } = UniversalEnergyState.Alert;
    public UniversalCondition Condition { get; init; } = UniversalCondition.Normal;
    public UniversalStereoStyle StereoStyle { get; init; } = UniversalStereoStyle.Automatic;
    public double RootFrequencyHz { get; init; } = 220;
    public double DurationSeconds { get; init; } = 0.5;
    public float Intensity { get; init; } = 0.6f;
    public float Size { get; init; } = 0.5f;
    public float Brightness { get; init; } = 0.55f;
    public float Roughness { get; init; } = 0.25f;
    public float TonalNoise { get; init; } = 0.55f;
    public float OrganicSynthetic { get; init; } = 0.4f;
    public float Complexity { get; init; } = 0.5f;
    public float Movement { get; init; } = 0.35f;
    public float Distance { get; init; } = 0.1f;
    public float Damage { get; init; }
    public float Density { get; init; } = 0.4f;
    public float PitchSweepSemitones { get; init; }
    public float PulseWidth { get; init; } = 0.5f;
    public float Modulation { get; init; } = 0.35f;
    public float Feedback { get; init; } = 0.2f;
    public float Damping { get; init; } = 0.45f;
    public float Spread { get; init; } = 0.2f;
    public int Harmonics { get; init; } = 8;
    public int EventCount { get; init; } = 1;
    public float TransientAmount { get; init; } = 0.35f;
    public float SubAmount { get; init; } = 0.15f;
    public float TailAmount { get; init; } = 0.25f;
    public float StereoMovement { get; init; } = 0.1f;
    public string NoiseColor { get; init; } = "White";
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

internal static class UniversalSoundRenderer
{
    public static SoundGraphDefinition Render(UniversalSoundPlan sourcePlan, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(sourcePlan);
        ArgumentNullException.ThrowIfNull(context);
        UniversalSoundPlan plan = ApplyVariation(sourcePlan, context);
        var builder = new GeneratedGraphBuilder(
            plan.GeneratorId,
            plan.GeneratorVersion,
            context,
            plan.FallbackId,
            plan.DisplayName);

        double duration = Math.Clamp(plan.DurationSeconds, 0.02, 90);
        float tonal = Semantic.Clamp01(plan.TonalNoise);
        float noise = 1 - tonal;
        var layers = new List<string>();
        string? primarySource = null;
        string? textureSource = null;

        if (plan.EventCount > 1 || plan.Structure != UniversalPhraseStructure.Single)
        {
            string program = BuildToneProgram(plan, context, duration);
            primarySource = builder.AddNode("phrase", "core.tone-sequence",
                ("program", program),
                ("synthesisMode", plan.SynthesisMode.ToString()),
                ("waveform", plan.Waveform.ToString()),
                ("rootFrequencyHz", Math.Clamp(plan.RootFrequencyHz, 8, 20_000)),
                ("amplitude", .18 + (plan.Intensity * .48) + (tonal * .12)),
                ("attackSeconds", Attack(plan.Articulation, plan.EnvelopeStyle, duration)),
                ("releaseSeconds", Release(plan.Articulation, plan.EnvelopeStyle, duration)),
                ("pulseWidth", Math.Clamp(plan.PulseWidth, .02f, .98f)),
                ("vibratoDepthSemitones", plan.Articulation == UniversalArticulation.Trembling ? 1.4 + (plan.Movement * 2.8) : plan.OrganicSynthetic < .4f ? plan.Movement * .35 : 0),
                ("vibratoRateHz", 3.5 + (plan.Movement * 10)),
                ("quantizeSemitones", plan.VariationMode == SoundVariationMode.RetroConstrained ? 1 : 0),
                ("brightness", plan.Brightness),
                ("timbre", Math.Clamp((plan.OrganicSynthetic * .55f) + (plan.Roughness * .3f) + .08f, 0, 1)),
                ("modulation", plan.Modulation),
                ("damping", plan.Damping),
                ("harmonics", Math.Clamp(plan.Harmonics, 1, 16)));
            layers.Add(primarySource);
        }
        else if (tonal > .04f)
        {
            primarySource = builder.AddNode("body", "core.hybrid-synth",
                ("mode", plan.SynthesisMode.ToString()),
                ("waveform", plan.Waveform.ToString()),
                ("frequencyHz", Math.Clamp(plan.RootFrequencyHz, 8, 20_000)),
                ("amplitude", .16 + (plan.Intensity * .52) + (tonal * .1)),
                ("durationSeconds", duration),
                ("attackSeconds", Attack(plan.Articulation, plan.EnvelopeStyle, duration)),
                ("releaseSeconds", Release(plan.Articulation, plan.EnvelopeStyle, duration)),
                ("brightness", plan.Brightness),
                ("timbre", Math.Clamp((plan.OrganicSynthetic * .55f) + (plan.Roughness * .3f) + .08f, 0, 1)),
                ("modulation", plan.Modulation),
                ("feedback", plan.Feedback),
                ("harmonics", Math.Clamp(plan.Harmonics, 1, 32)),
                ("damping", plan.Damping),
                ("spread", plan.Spread),
                ("pulseWidth", Math.Clamp(plan.PulseWidth, .02f, .98f)),
                ("pitchSweepSemitones", plan.PitchSweepSemitones));
            layers.Add(primarySource);
        }

        if (plan.TextureKind is not null && builder.NodeCount + 1 < builder.Budget.MaximumNodes)
        {
            textureSource = builder.AddNode("texture", "core.procedural-texture",
                ("kind", plan.TextureKind.Value.ToString()),
                ("durationSeconds", duration),
                ("amplitude", .08 + (noise * .42) + (plan.Density * .16)),
                ("intensity", plan.Intensity),
                ("density", plan.Density),
                ("brightness", plan.Brightness),
                ("movement", plan.Movement),
                ("baseFrequencyHz", Math.Clamp(plan.RootFrequencyHz, 8, 12_000)),
                ("attackSeconds", Math.Min(duration * .25, Attack(plan.Articulation, plan.EnvelopeStyle, duration) * 1.5)),
                ("releaseSeconds", Math.Min(duration * .45, Release(plan.Articulation, plan.EnvelopeStyle, duration) * 1.4)));
            layers.Add(textureSource);
        }

        if ((noise > .08f || plan.TransientAmount > .08f) && builder.NodeCount + 2 < builder.Budget.MaximumNodes)
        {
            double transientDuration = Math.Clamp(duration * (.08 + (plan.Size * .24) + (plan.TransientAmount * .3)), .015, Math.Min(2.5, duration));
            string transient = LayerBuilders.AddNoiseBurst(
                builder,
                "transient",
                plan.NoiseColor,
                .04 + (noise * .36) + (plan.TransientAmount * .45),
                transientDuration,
                Semantic.Lerp(160, 11_000, plan.Brightness) * Semantic.Lerp(.55, 1.4, 1 - plan.Size),
                Semantic.Lerp(.35, 3.4, 1 - plan.Roughness),
                out _,
                .0005,
                Semantic.Lerp(.45, .92, plan.Size));
            layers.Add(transient);
        }

        if (plan.SubAmount > .04f && plan.Size > .25f && builder.NodeCount + 1 < builder.Budget.MaximumNodes)
        {
            string sub = builder.AddNode("sub", "core.hybrid-synth",
                ("mode", HybridSynthesisMode.Basic.ToString()),
                ("waveform", Waveform.Sine.ToString()),
                ("frequencyHz", Math.Clamp(plan.RootFrequencyHz * Semantic.Lerp(.22, .55, 1 - plan.Size), 18, 600)),
                ("amplitude", .025 + (plan.SubAmount * .32) + (plan.Size * .12)),
                ("durationSeconds", duration * Semantic.Lerp(.55, 1, plan.Size)),
                ("attackSeconds", Math.Min(.08, duration * .15)),
                ("releaseSeconds", Math.Min(.4, duration * .45)),
                ("brightness", .1),
                ("timbre", .2),
                ("modulation", .05),
                ("feedback", 0),
                ("harmonics", 2),
                ("damping", .6),
                ("spread", 0),
                ("pulseWidth", .5),
                ("pitchSweepSemitones", plan.PitchSweepSemitones * .35));
            layers.Add(sub);
        }

        if (layers.Count == 0)
        {
            string fallback = LayerBuilders.AddNoiseBurst(builder, "fallback", "Pink", .3, duration, 1_500, .8);
            layers.Add(fallback);
        }

        string current = layers.Count == 1 ? layers[0] : builder.AddMixer("universal-mix", layers, Math.Clamp(.9 / Math.Sqrt(layers.Count), .32, .88));

        if (plan.StereoMovement > .12f && builder.NodeCount + 1 < builder.Budget.MaximumNodes)
        {
            string chorus = builder.AddNode("motion", "dsp.chorus",
                ("baseDelayMilliseconds", 3 + (plan.Size * 12)),
                ("depthMilliseconds", 1 + (plan.StereoMovement * 8)),
                ("rateHz", .08 + (plan.Movement * 3.5)),
                ("feedback", Math.Clamp(plan.Roughness * .22, 0, .35)),
                ("wet", Math.Clamp(plan.StereoMovement * .42, 0, .55)),
                ("dry", 1));
            builder.Connect(current, chorus);
            current = chorus;
        }

        if ((plan.Roughness > .18f || plan.Damage > .08f) && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string drive = builder.AddNode("character", "dsp.waveshaper",
                ("mode", plan.OrganicSynthetic > .65f || plan.Damage > .55f ? "WaveFold" : "HyperbolicTangent"),
                ("driveDecibels", Semantic.Lerp(.5, 22, Math.Max(plan.Roughness, plan.Damage))),
                ("bias", plan.Damage * .16 - .04),
                ("mix", Semantic.Lerp(.04, .68, Math.Max(plan.Roughness, plan.Damage))),
                ("outputDecibels", -2.5));
            builder.Connect(current, drive);
            current = drive;
        }

        if ((plan.VariationMode == SoundVariationMode.RetroConstrained || plan.Damage > .62f) && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string crusher = builder.AddNode("degradation", "dsp.bitcrusher",
                ("bitDepth", Semantic.Lerp(5, 14, 1 - Math.Max(plan.Damage, .45f))),
                ("sampleRateReduction", Semantic.Lerp(2, 18, Math.Max(plan.Damage, .35f))),
                ("mix", Semantic.Lerp(.22, .82, Math.Max(plan.Damage, .45f))));
            builder.Connect(current, crusher);
            current = crusher;
        }

        if (plan.TailAmount > .24f && builder.ProcessorCount < builder.Budget.MaximumProcessors)
        {
            string delay = builder.AddNode("signature-delay", "dsp.delay",
                ("maximumDelaySeconds", 2.5),
                ("delayMilliseconds", Math.Clamp(duration * Semantic.Lerp(80, 280, plan.TailAmount), 45, 720)),
                ("feedback", Semantic.Lerp(.04, .48, plan.TailAmount)),
                ("wet", Semantic.Lerp(.04, .3, plan.TailAmount)),
                ("dry", 1),
                ("tailSeconds", Math.Min(builder.Budget.MaximumTailSeconds, 3.5)));
            builder.Connect(current, delay);
            current = delay;
        }

        float environmentAmount = plan.VariationMode == SoundVariationMode.Cinematic ? .95f : .42f + (plan.TailAmount * .4f);
        current = builder.AddEnvironment(current, plan.Environment, plan.Distance, environmentAmount);
        current = builder.AddProtection(current, -.8);

        if (primarySource is not null)
        {
            builder.AddMacro("intensity", "Intensity", plan.Intensity,
                GeneratedGraphBuilder.Binding(primarySource, "amplitude", .05f, 1.15f, MacroMappingCurve.SmoothStep));
            string pitchParameter = plan.EventCount > 1 || plan.Structure != UniversalPhraseStructure.Single ? "rootFrequencyHz" : "frequencyHz";
            builder.AddMacro("size", "Size", plan.Size,
                GeneratedGraphBuilder.Binding(primarySource, pitchParameter,
                    (float)Math.Clamp(plan.RootFrequencyHz * 1.8, 8, 20_000),
                    (float)Math.Clamp(plan.RootFrequencyHz * .45, 8, 20_000),
                    MacroMappingCurve.Inverted));
        }
        if (textureSource is not null)
        {
            builder.AddMacro("density", "Density", plan.Density,
                GeneratedGraphBuilder.Binding(textureSource, "density", 0, 1, MacroMappingCurve.SmoothStep));
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["synthesisMode"] = plan.SynthesisMode.ToString(),
            ["textureKind"] = plan.TextureKind?.ToString() ?? "None",
            ["structure"] = plan.Structure.ToString(),
            ["variationMode"] = plan.VariationMode.ToString(),
            ["articulation"] = plan.Articulation.ToString(),
            ["rhythmStyle"] = plan.RhythmStyle.ToString(),
            ["envelopeStyle"] = plan.EnvelopeStyle.ToString(),
            ["emotion"] = plan.Emotion.ToString(),
            ["energyState"] = plan.EnergyState.ToString(),
            ["condition"] = plan.Condition.ToString(),
            ["stereoStyle"] = plan.StereoStyle.ToString(),
            ["eventCount"] = plan.EventCount.ToString(CultureInfo.InvariantCulture)
        };
        if (plan.Metadata is not null)
            foreach ((string key, string value) in plan.Metadata) metadata[key] = value;
        return builder.Complete(current, metadata);
    }

    private static UniversalSoundPlan ApplyVariation(UniversalSoundPlan plan, SoundGenerationContext context)
    {
        // Stable stream name retained so existing seeds keep their deterministic identity.
        var random = context.CreateRandom("phase8.universal-variation");
        float amount = plan.VariationMode switch
        {
            SoundVariationMode.Stable => .025f,
            SoundVariationMode.IdentityPreserving => .07f,
            SoundVariationMode.Natural => .12f,
            SoundVariationMode.Cinematic => .1f,
            SoundVariationMode.RetroConstrained => .11f,
            SoundVariationMode.Mutated => .23f,
            SoundVariationMode.Crossbred => .32f,
            SoundVariationMode.Damaged => .3f,
            SoundVariationMode.Chaotic => .42f,
            _ => .12f
        };
        double pitchJitter = (random.NextDouble() * 2 - 1) * amount * 5;
        double durationScale = 1 + ((random.NextDouble() * 2 - 1) * amount * .35);
        float roughness = Math.Clamp(plan.Roughness + ((random.NextFloat() * 2 - 1) * amount * .35f), 0, 1);
        float damage = plan.VariationMode == SoundVariationMode.Damaged
            ? Math.Clamp(Math.Max(plan.Damage, .55f + random.NextFloat() * .4f), 0, 1)
            : plan.Damage;
        return plan with
        {
            RootFrequencyHz = plan.RootFrequencyHz * Math.Pow(2, pitchJitter / 12),
            DurationSeconds = Math.Max(.02, plan.DurationSeconds * durationScale),
            Roughness = roughness,
            Damage = damage,
            PulseWidth = Math.Clamp(plan.PulseWidth + ((random.NextFloat() * 2 - 1) * amount * .22f), .05f, .95f),
            Modulation = Math.Clamp(plan.Modulation + ((random.NextFloat() * 2 - 1) * amount * .3f), 0, 1),
            Spread = Math.Clamp(plan.Spread + ((random.NextFloat() * 2 - 1) * amount * .4f), 0, 1)
        };
    }

    private static string BuildToneProgram(UniversalSoundPlan plan, SoundGenerationContext context, double duration)
    {
        // Stable stream name retained so existing seeds keep their deterministic phrase.
        var random = context.CreateRandom("phase8.tone-program");
        int structureMinimum = plan.Structure switch
        {
            UniversalPhraseStructure.Single => 1,
            UniversalPhraseStructure.Double => 2,
            UniversalPhraseStructure.Triple => 3,
            UniversalPhraseStructure.CallAndResponse => 4,
            UniversalPhraseStructure.RepeatedMotif => 4,
            UniversalPhraseStructure.BuildAndRelease => 5,
            UniversalPhraseStructure.Escalation => 6,
            UniversalPhraseStructure.FalseEnding => 5,
            UniversalPhraseStructure.Combo => 7,
            UniversalPhraseStructure.Evolving => 8,
            _ => 1
        };
        int count = Math.Clamp(Math.Max(plan.EventCount, structureMinimum), 1, Math.Min(32, context.Budget.MaximumPhraseNotes));
        double spacing = duration / Math.Max(1, count + .25);
        double noteDuration = spacing * ArticulationLength(plan.Articulation, plan.EnvelopeStyle);
        var commands = new List<string>(count + 4);
        for (int index = 0; index < count; index++)
        {
            double t = count <= 1 ? 0 : index / (double)(count - 1);
            double start = RhythmStart(plan.RhythmStyle, index, count, duration, spacing, ref random);
            if (plan.Structure == UniversalPhraseStructure.FalseEnding && index == count - 1) start += spacing * 1.25;
            if (plan.Structure == UniversalPhraseStructure.CallAndResponse && index >= count / 2) start += spacing * .8;
            start = Math.Clamp(start, 0, duration * .96);
            double semitones = PatternSemitones(plan.PitchPattern, index, count, t, ref random, plan.Movement);
            double velocity = Math.Clamp(.58 + (plan.Intensity * .46) + ((random.NextDouble() * 2 - 1) * .12), .15, 1.35);
            double localDuration = noteDuration * (1 + ((random.NextDouble() * 2 - 1) * plan.Complexity * .25));
            commands.Add(FormattableString.Invariant($"{start:0.#####}|{Math.Max(.008, localDuration):0.#####}|{semitones:0.###}|{velocity:0.###}"));

            if (plan.Complexity > .68f && index % 3 == 1 && commands.Count < context.Budget.MaximumPhraseNotes)
            {
                double graceStart = start + localDuration * .42;
                double gracePitch = semitones + (index % 2 == 0 ? 2 : -2);
                commands.Add(FormattableString.Invariant($"{graceStart:0.#####}|{Math.Max(.006, localDuration * .35):0.#####}|{gracePitch:0.###}|{velocity * .55:0.###}"));
            }
        }
        return string.Join(';', commands);
    }

    private static double PatternSemitones(UniversalPitchPattern pattern, int index, int count, double t, ref Playroma.Sound.Core.Random.Pcg32 random, float movement)
    {
        double range = 2 + (movement * 22);
        return pattern switch
        {
            UniversalPitchPattern.Rising => t * range,
            UniversalPitchPattern.Falling => -t * range,
            UniversalPitchPattern.Arch => Math.Sin(Math.PI * t) * range,
            UniversalPitchPattern.Valley => -Math.Sin(Math.PI * t) * range,
            UniversalPitchPattern.Zigzag => (index % 2 == 0 ? -.35 : .65) * range + (t * range * .25),
            UniversalPitchPattern.Alarm => index % 2 == 0 ? 0 : range * .42,
            UniversalPitchPattern.Trill => index % 2 == 0 ? 0 : Math.Max(1, range * .18),
            UniversalPitchPattern.Bounce => Math.Abs(Math.Sin(Math.PI * t * 2.5)) * range,
            UniversalPitchPattern.Cascade => -(t * range) + ((index % 3) * range * .18),
            UniversalPitchPattern.Question => (t * range * .55) + (index == count - 1 ? range * .45 : 0),
            UniversalPitchPattern.CallAndResponse => index < Math.Max(1, count / 2)
                ? t * range * .65
                : (range * .45) - ((t - .5) * range * .8),
            UniversalPitchPattern.Evolving => (Math.Sin(t * Math.Tau * 1.5) * range * .35) + ((random.NextDouble() * 2 - 1) * range * .12),
            UniversalPitchPattern.Random => (random.NextDouble() * 2 - 1) * range,
            UniversalPitchPattern.Prime => PrimeStep(index) * range * .1,
            _ => 0
        };
    }

    private static int PrimeStep(int index)
    {
        int[] primes = [2, 3, 5, 7, 11, 13, 17, 19];
        return (primes[index % primes.Length] % 12) - 5;
    }

    private static double RhythmStart(
        UniversalRhythmStyle style,
        int index,
        int count,
        double duration,
        double spacing,
        ref Playroma.Sound.Core.Random.Pcg32 random)
    {
        if (count <= 1) return 0;
        double t = index / (double)(count - 1);
        double linear = spacing * index;
        return style switch
        {
            UniversalRhythmStyle.Even => duration * .88 * t,
            UniversalRhythmStyle.Syncopated => linear + (index % 2 == 1 ? spacing * .3 : 0),
            UniversalRhythmStyle.Swing => linear + (index % 2 == 1 ? spacing * .38 : 0),
            UniversalRhythmStyle.Triplet => (index / 3 * spacing * 3.15) + (index % 3 * spacing * .72),
            UniversalRhythmStyle.Bursts => (index / 3 * spacing * 3.4) + (index % 3 * spacing * .42),
            UniversalRhythmStyle.Accelerating => duration * .88 * Math.Pow(t, .62),
            UniversalRhythmStyle.Decelerating => duration * .88 * Math.Pow(t, 1.55),
            UniversalRhythmStyle.Irregular => linear + ((random.NextDouble() * 2 - 1) * spacing * .38),
            UniversalRhythmStyle.Prime => PrimeRhythmStart(index, count, duration * .88),
            UniversalRhythmStyle.CallAndResponse => linear + (index >= count / 2 ? spacing * 1.15 : 0),
            _ => linear + ((random.NextDouble() * 2 - 1) * spacing * .08)
        };
    }

    private static double PrimeRhythmStart(int index, int count, double duration)
    {
        int[] gaps = [2, 3, 5, 7, 11, 13, 17, 19];
        double total = 0;
        double current = 0;
        for (int i = 0; i < Math.Max(1, count - 1); i++)
        {
            double gap = gaps[i % gaps.Length];
            total += gap;
            if (i < index) current += gap;
        }
        return total <= 0 ? 0 : duration * current / total;
    }

    private static double Attack(UniversalArticulation articulation, UniversalEnvelopeStyle envelope, double duration)
    {
        if (envelope != UniversalEnvelopeStyle.Automatic)
        {
            return envelope switch
            {
                UniversalEnvelopeStyle.Percussive => Math.Min(.0008, duration * .015),
                UniversalEnvelopeStyle.Plucked => Math.Min(.0025, duration * .035),
                UniversalEnvelopeStyle.Sustained => Math.Min(.09, duration * .22),
                UniversalEnvelopeStyle.Gated => Math.Min(.0015, duration * .025),
                UniversalEnvelopeStyle.Swell => Math.Min(.42, duration * .48),
                UniversalEnvelopeStyle.DoubleAttack => Math.Min(.012, duration * .08),
                UniversalEnvelopeStyle.LongRelease => Math.Min(.035, duration * .12),
                _ => Math.Min(.015, duration * .1)
            };
        }
        return articulation switch
        {
            UniversalArticulation.Staccato or UniversalArticulation.Accented or UniversalArticulation.Gated => Math.Min(.003, duration * .04),
            UniversalArticulation.Whispered => Math.Min(.08, duration * .22),
            UniversalArticulation.Swell => Math.Min(.35, duration * .4),
            UniversalArticulation.Smooth or UniversalArticulation.Legato => Math.Min(.045, duration * .16),
            _ => Math.Min(.015, duration * .1)
        };
    }

    private static double Release(UniversalArticulation articulation, UniversalEnvelopeStyle envelope, double duration)
    {
        if (envelope != UniversalEnvelopeStyle.Automatic)
        {
            return envelope switch
            {
                UniversalEnvelopeStyle.Percussive => Math.Min(.04, duration * .18),
                UniversalEnvelopeStyle.Plucked => Math.Min(.08, duration * .28),
                UniversalEnvelopeStyle.Sustained => Math.Min(.3, duration * .42),
                UniversalEnvelopeStyle.Gated => Math.Min(.012, duration * .08),
                UniversalEnvelopeStyle.Swell => Math.Min(.45, duration * .5),
                UniversalEnvelopeStyle.DoubleAttack => Math.Min(.16, duration * .34),
                UniversalEnvelopeStyle.LongRelease => Math.Min(.7, duration * .65),
                _ => Math.Min(.12, duration * .3)
            };
        }
        return articulation switch
        {
            UniversalArticulation.Staccato or UniversalArticulation.Gated or UniversalArticulation.Choked => Math.Min(.025, duration * .12),
            UniversalArticulation.Legato or UniversalArticulation.Swell => Math.Min(.45, duration * .48),
            UniversalArticulation.Whispered => Math.Min(.25, duration * .35),
            _ => Math.Min(.12, duration * .3)
        };
    }

    private static double ArticulationLength(UniversalArticulation articulation, UniversalEnvelopeStyle envelope)
    {
        if (envelope != UniversalEnvelopeStyle.Automatic)
        {
            return envelope switch
            {
                UniversalEnvelopeStyle.Percussive => .48,
                UniversalEnvelopeStyle.Plucked => .65,
                UniversalEnvelopeStyle.Sustained => 1.08,
                UniversalEnvelopeStyle.Gated => .58,
                UniversalEnvelopeStyle.Swell => 1.3,
                UniversalEnvelopeStyle.DoubleAttack => .92,
                UniversalEnvelopeStyle.LongRelease => 1.18,
                _ => .9
            };
        }
        return articulation switch
        {
            UniversalArticulation.Staccato => .48,
            UniversalArticulation.Gated => .62,
            UniversalArticulation.Accented => .72,
            UniversalArticulation.Legato => 1.15,
            UniversalArticulation.Swell => 1.35,
            UniversalArticulation.Choked => .38,
            _ => .9
        };
    }
}
