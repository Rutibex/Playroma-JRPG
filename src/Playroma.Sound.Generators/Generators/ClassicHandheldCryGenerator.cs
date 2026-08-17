using System.Globalization;
using Playroma.Sound.Core.Generation;
using Playroma.Sound.Core.Random;
using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;
using Playroma.Sound.Graph.Definitions;

namespace Playroma.Sound.Generators.Generators;

/// <summary>
/// Produces original cartridge-era creature cries as independent command
/// streams for two fixed-duty pulse channels and one NR43-style LFSR noise
/// channel. A cry is a short rhythmic phrase of discrete notes, rests, pitch
/// jumps, duty changes, sweeps, and noise articulations rather than one long
/// oscillator note.
/// </summary>
public sealed class ClassicHandheldCryGenerator : ISoundGenerator<ClassicHandheldCryDescription>
{
    public const string Id = "playroma.classic-handheld-cry";

    public string GeneratorId => Id;

    public SoundGeneratorDescriptor Descriptor { get; } = DescriptorFactory.Create(
        Id,
        "Classic Handheld Cry",
        SoundGeneratorCategory.ClassicHandheldCry,
        "Original multi-syllable monster cries programmed as independent pulse and LFSR command streams.",
        0.08,
        2.5,
        ["handheld", "cartridge", "creature", "cry", "sequence", "pulse", "lfsr", "retro"]);

    public SoundGraphDefinition Generate(ClassicHandheldCryDescription description, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(context);

        var identityRandom = context.WithVariation(0).CreateRandom("classic-handheld.identity");
        var variationRandom = context.CreateRandom("classic-handheld.variation");
        ClassicHandheldCryArchetype archetype = ResolveArchetype(description.Archetype, ref identityRandom);
        ClassicHandheldCryContour contour = ResolveContour(description.Contour, ref identityRandom);

        float pitch = Semantic.Clamp01(description.Pitch);
        float length = Semantic.Clamp01(description.Length);
        float bodySize = Semantic.Clamp01(description.BodySize);
        float complexity = Semantic.Clamp01(description.Complexity);
        float sweep = Semantic.Clamp01(description.Sweep);
        float pulseBlend = Semantic.Clamp01(description.PulseBlend);
        float noise = Semantic.Clamp01(description.Noise);
        float roughness = Semantic.Clamp01(description.Roughness);
        float repetition = Semantic.Clamp01(description.Repetition);
        float punch = Semantic.Clamp01(description.Punch);
        int syllables = Math.Clamp(description.Syllables, 2, 16);

        int ornamentCount = (int)Math.Round(syllables * Semantic.Lerp(0.1, 0.9, complexity));
        int noteCount = Math.Clamp(syllables + ornamentCount, 3, 24);
        double requestedDuration = Semantic.Lerp(0.14, 1.5, MathF.Pow(length, 1.12f)) * DurationScale(archetype);
        double minimumCommandDuration = Semantic.Lerp(0.018, 0.042, bodySize);
        double duration = Math.Clamp(Math.Max(requestedDuration, noteCount * minimumCommandDuration), 0.08, 2.5);

        int basePeriod = BasePeriod(archetype, pitch, bodySize);
        double pitchRangeSemitones = Semantic.Lerp(5.0, 24.0, sweep);
        CryNote[] notes = BuildNotes(
            noteCount,
            duration,
            basePeriod,
            pitchRangeSemitones,
            contour,
            archetype,
            complexity,
            roughness,
            repetition,
            punch,
            ref identityRandom,
            ref variationRandom);

        List<PulseStep> pulse1 = BuildPrimaryPulse(notes, archetype, basePeriod, sweep, punch, ref identityRandom);
        List<PulseStep> pulse2 = BuildSecondaryPulse(
            notes,
            archetype,
            basePeriod,
            sweep,
            pulseBlend,
            roughness,
            ref identityRandom,
            ref variationRandom);
        List<NoiseStep> noiseSteps = BuildNoise(
            notes,
            archetype,
            bodySize,
            noise,
            roughness,
            punch,
            ref identityRandom,
            ref variationRandom);

        string pulse1Program = SerializePulse(pulse1);
        string pulse2Program = SerializePulse(pulse2);
        string noiseProgram = SerializeNoise(noiseSteps);

        var builder = new GeneratedGraphBuilder(
            Id,
            Descriptor.Version,
            context,
            $"handheld-cry.{archetype.ToString().ToLowerInvariant()}",
            $"{archetype} Classic Handheld Cry");

        string sequence = builder.AddNode(
            "handheld-cry-program",
            "core.handheld-cry-sequence",
            ("pulse1Program", pulse1Program),
            ("pulse2Program", pulse2Program),
            ("noiseProgram", noiseProgram),
            ("masterAmplitude", Semantic.Lerp(0.72, 1.08, punch)),
            ("noiseMix", Semantic.Lerp(0.28, 1.15, noise)),
            ("pitchShiftSemitones", 0));

        string current = sequence;
        string dcBlock = builder.AddNode(
            "handheld-output-high-pass",
            "dsp.biquad",
            ("filterType", "HighPass"),
            ("frequencyHz", Semantic.Lerp(34, 82, bodySize)),
            ("q", 0.707),
            ("gainDecibels", 0));
        builder.Connect(current, dcBlock);
        current = dcBlock;

        string speakerFilter = builder.AddNode(
            "handheld-speaker-low-pass",
            "dsp.biquad",
            ("filterType", "LowPass"),
            ("frequencyHz", Semantic.Lerp(6_800, 12_500, 1 - roughness)),
            ("q", 0.68),
            ("gainDecibels", 0));
        builder.Connect(current, speakerFilter);
        current = speakerFilter;
        current = builder.AddProtection(current, -1.0);

        builder.AddMacro(
            "pitch",
            "Pitch",
            pitch,
            GeneratedGraphBuilder.Binding(
                sequence,
                "pitchShiftSemitones",
                -24f * pitch,
                24f * (1f - pitch),
                MacroMappingCurve.Linear));
        builder.AddMacro(
            "punch",
            "Punch",
            punch,
            GeneratedGraphBuilder.Binding(sequence, "masterAmplitude", 0.5f, 1.3f, MacroMappingCurve.SmoothStep));
        builder.AddMacro(
            "noise",
            "Noise",
            noise,
            GeneratedGraphBuilder.Binding(sequence, "noiseMix", 0f, 1.5f, MacroMappingCurve.SmoothStep));

        return builder.Complete(current, new Dictionary<string, string>
        {
            ["archetype"] = archetype.ToString(),
            ["contour"] = contour.ToString(),
            ["basePeriod"] = basePeriod.ToString(CultureInfo.InvariantCulture),
            ["syllables"] = syllables.ToString(CultureInfo.InvariantCulture),
            ["notes"] = noteCount.ToString(CultureInfo.InvariantCulture),
            ["pulse1Commands"] = pulse1.Count.ToString(CultureInfo.InvariantCulture),
            ["pulse2Commands"] = pulse2.Count.ToString(CultureInfo.InvariantCulture),
            ["noiseCommands"] = noiseSteps.Count.ToString(CultureInfo.InvariantCulture),
            ["durationSeconds"] = duration.ToString("0.000", CultureInfo.InvariantCulture),
            ["hardwareModel"] = "independent 2-pulse command streams + 15/7-bit LFSR noise"
        });
    }

    private static CryNote[] BuildNotes(
        int noteCount,
        double duration,
        int basePeriod,
        double pitchRangeSemitones,
        ClassicHandheldCryContour contour,
        ClassicHandheldCryArchetype archetype,
        float complexity,
        float roughness,
        float repetition,
        float punch,
        ref Pcg32 identityRandom,
        ref Pcg32 variationRandom)
    {
        double[] motif = ArchetypeMotif(archetype);
        int motifLength = repetition switch
        {
            > 0.72f => Math.Min(3, motif.Length),
            > 0.38f => Math.Min(5, motif.Length),
            _ => motif.Length
        };
        double contourScale = Semantic.Lerp(0.45, 1.0, complexity);
        double jitterScale = Semantic.Lerp(0.25, 3.2, roughness);
        var weights = new double[noteCount];
        var gaps = new double[noteCount];
        var semitones = new double[noteCount];
        var slides = new double[noteCount];
        var accents = new float[noteCount];

        for (int index = 0; index < noteCount; index++)
        {
            double position = noteCount <= 1 ? 0.5 : index / (double)(noteCount - 1);
            int motifIndex = index % Math.Max(1, motifLength);
            double motifAmount = motif[motifIndex % motif.Length] * Semantic.Lerp(0.55, 1.0, complexity);
            double contourAmount = ContourSemitones(contour, position, index, pitchRangeSemitones, ref identityRandom) * contourScale;
            double identityJitter = GeneratorMath.Signed(ref identityRandom) * jitterScale;
            double variationJitter = GeneratorMath.Signed(ref variationRandom) * Semantic.Lerp(0.08, 0.65, roughness);
            semitones[index] = motifAmount + contourAmount + identityJitter + variationJitter;

            double nextDirection = index % 2 == 0 ? 1 : -1;
            slides[index] = nextDirection * Semantic.Lerp(0.1, 2.8, complexity * roughness);
            weights[index] = RhythmWeight(archetype, index, ref identityRandom);
            bool phraseBoundary = motifLength > 1 && (index + 1) % motifLength == 0 && index < noteCount - 1;
            double baseGap = Semantic.Lerp(0.002, 0.014, 1 - punch);
            double phraseGap = phraseBoundary ? Semantic.Lerp(0.008, 0.045, repetition) : 0;
            gaps[index] = index == noteCount - 1 ? 0 : baseGap + phraseGap;
            accents[index] = Math.Clamp(
                (float)(1.0 - (index / (double)Math.Max(2, noteCount + 2)) * Semantic.Lerp(0.06, 0.32, punch)
                    + (phraseBoundary ? 0.08 : 0)
                    + GeneratorMath.Signed(ref identityRandom) * 0.06),
                0.45f,
                1.15f);
        }

        double totalGap = gaps.Sum();
        double audibleDuration = Math.Max(duration * 0.62, duration - totalGap);
        double totalWeight = Math.Max(0.001, weights.Sum());
        var notes = new CryNote[noteCount];
        for (int index = 0; index < noteCount; index++)
        {
            double noteDuration = Math.Max(0.008, audibleDuration * weights[index] / totalWeight);
            int startPeriod = PeriodShiftSemitones(basePeriod, semitones[index]);
            int endPeriod = PeriodShiftSemitones(basePeriod, semitones[index] + slides[index]);
            notes[index] = new CryNote(
                noteDuration,
                gaps[index],
                startPeriod,
                endPeriod,
                accents[index],
                DutyFor(archetype, index, false, ref identityRandom),
                DutyFor(archetype, index, true, ref identityRandom));
        }

        return notes;
    }

    private static List<PulseStep> BuildPrimaryPulse(
        IReadOnlyList<CryNote> notes,
        ClassicHandheldCryArchetype archetype,
        int basePeriod,
        float sweep,
        float punch,
        ref Pcg32 identityRandom)
    {
        var steps = new List<PulseStep>(notes.Count * 2);
        for (int index = 0; index < notes.Count; index++)
        {
            CryNote note = notes[index];
            int startVolume = Math.Clamp((int)Math.Round(Semantic.Lerp(11, 15, punch) * note.Accent), 1, 15);
            int endVolume = Math.Clamp(startVolume - EnvelopeDrop(archetype, punch, index), 0, 15);
            int endPeriod = Math.Clamp(
                note.EndPeriod + (int)Math.Round(GeneratorMath.Signed(ref identityRandom) * Semantic.Lerp(0, 18, sweep)),
                0,
                2_047);
            steps.Add(new PulseStep(note.DurationSeconds, note.StartPeriod, endPeriod, startVolume, endVolume, note.PrimaryDuty));
            AddPulseRest(steps, note.GapSeconds, basePeriod, note.PrimaryDuty);
        }

        return steps;
    }

    private static List<PulseStep> BuildSecondaryPulse(
        IReadOnlyList<CryNote> notes,
        ClassicHandheldCryArchetype archetype,
        int basePeriod,
        float sweep,
        float pulseBlend,
        float roughness,
        ref Pcg32 identityRandom,
        ref Pcg32 variationRandom)
    {
        var steps = new List<PulseStep>(notes.Count * 3 + 1);
        double leadIn = Semantic.Lerp(0.003, 0.018, roughness);
        AddPulseRest(steps, leadIn, basePeriod, HandheldPulseDuty.Duty25);
        double interval = SecondaryInterval(archetype, ref identityRandom);

        for (int index = 0; index < notes.Count; index++)
        {
            CryNote note = notes[index];
            double counterMotion = index % 2 == 0
                ? Semantic.Lerp(-1.5, 2.5, roughness)
                : Semantic.Lerp(1.0, -2.0, roughness);
            int startPeriod = PeriodShiftSemitones(note.StartPeriod, interval + counterMotion);
            int endPeriod = PeriodShiftSemitones(note.EndPeriod, interval - counterMotion * Semantic.Lerp(0.2, 0.8, sweep));
            int startVolume = Math.Clamp(
                (int)Math.Round(Semantic.Lerp(3, 12, pulseBlend) * note.Accent),
                pulseBlend < 0.03f ? 0 : 1,
                15);
            int endVolume = Math.Clamp(startVolume - 2 - (index % 3), 0, 15);

            bool ornament = roughness > 0.42f && index % 3 == 1 && note.DurationSeconds >= 0.026;
            if (ornament)
            {
                double firstDuration = note.DurationSeconds * Semantic.Lerp(0.38, 0.62, variationRandom.NextFloat());
                double secondDuration = note.DurationSeconds - firstDuration;
                int middlePeriod = PeriodShiftSemitones(
                    startPeriod,
                    (index % 2 == 0 ? 1 : -1) * Semantic.Lerp(2.0, 6.0, roughness));
                steps.Add(new PulseStep(firstDuration, startPeriod, middlePeriod, startVolume, Math.Max(0, startVolume - 1), note.SecondaryDuty));
                steps.Add(new PulseStep(secondDuration, middlePeriod, endPeriod, Math.Max(0, startVolume - 1), endVolume, AlternateDuty(note.SecondaryDuty)));
            }
            else
            {
                steps.Add(new PulseStep(note.DurationSeconds, startPeriod, endPeriod, startVolume, endVolume, note.SecondaryDuty));
            }

            AddPulseRest(steps, note.GapSeconds, basePeriod, note.SecondaryDuty);
        }

        return steps;
    }

    private static List<NoiseStep> BuildNoise(
        IReadOnlyList<CryNote> notes,
        ClassicHandheldCryArchetype archetype,
        float bodySize,
        float noise,
        float roughness,
        float punch,
        ref Pcg32 identityRandom,
        ref Pcg32 variationRandom)
    {
        var steps = new List<NoiseStep>(notes.Count * 3);
        double archetypeAmount = NoiseArchetypeAmount(archetype);
        for (int index = 0; index < notes.Count; index++)
        {
            CryNote note = notes[index];
            double amount = noise * archetypeAmount * Semantic.Lerp(0.72, 1.22, roughness) * note.Accent;
            int startVolume = Math.Clamp((int)Math.Round(amount * 15), 0, 15);
            int endVolume = Math.Clamp(startVolume - Math.Max(1, (int)Math.Round(Semantic.Lerp(2, 8, punch))), 0, 15);
            byte polynomial = NoisePolynomial(archetype, bodySize, roughness, index, ref identityRandom, ref variationRandom);
            double burstRatio = NoiseBurstRatio(archetype, index, roughness);
            double burstDuration = note.DurationSeconds * burstRatio;
            double remainder = note.DurationSeconds - burstDuration;

            if (startVolume > 0 && burstDuration >= 0.001)
            {
                steps.Add(new NoiseStep(burstDuration, polynomial, startVolume, endVolume));
            }
            else
            {
                steps.Add(new NoiseStep(Math.Max(0.001, burstDuration), polynomial, 0, 0));
            }

            if (remainder > 0.001)
            {
                int sustainVolume = archetype is ClassicHandheldCryArchetype.Roar or ClassicHandheldCryArchetype.Croak
                    ? Math.Clamp(endVolume / 2, 0, 15)
                    : 0;
                steps.Add(new NoiseStep(remainder, polynomial, sustainVolume, 0));
            }

            if (note.GapSeconds > 0.001)
            {
                steps.Add(new NoiseStep(note.GapSeconds, polynomial, 0, 0));
            }
        }

        return steps;
    }

    private static string SerializePulse(IEnumerable<PulseStep> steps) => string.Join(
        "|",
        steps.Select(step => string.Join(
            ",",
            step.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            step.StartPeriod.ToString(CultureInfo.InvariantCulture),
            step.EndPeriod.ToString(CultureInfo.InvariantCulture),
            step.StartVolume.ToString(CultureInfo.InvariantCulture),
            step.EndVolume.ToString(CultureInfo.InvariantCulture),
            step.Duty.ToString())));

    private static string SerializeNoise(IEnumerable<NoiseStep> steps) => string.Join(
        "|",
        steps.Select(step => string.Join(
            ",",
            step.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            ((int)step.Polynomial).ToString(CultureInfo.InvariantCulture),
            step.StartVolume.ToString(CultureInfo.InvariantCulture),
            step.EndVolume.ToString(CultureInfo.InvariantCulture))));

    private static void AddPulseRest(List<PulseStep> steps, double duration, int period, HandheldPulseDuty duty)
    {
        if (duration > 0.001)
        {
            steps.Add(new PulseStep(duration, period, period, 0, 0, duty));
        }
    }

    private static double[] ArchetypeMotif(ClassicHandheldCryArchetype archetype) => archetype switch
    {
        ClassicHandheldCryArchetype.Squeak => [0, 5, 12, 7, 14, 9],
        ClassicHandheldCryArchetype.Chirp => [0, 7, 12, 5, 10, 15],
        ClassicHandheldCryArchetype.Buzz => [0, 1, -1, 2, -2, 1],
        ClassicHandheldCryArchetype.Croak => [0, -5, -2, -7, -4, -9],
        ClassicHandheldCryArchetype.Roar => [0, -2, -5, -3, -8, -12],
        ClassicHandheldCryArchetype.Wail => [0, 3, 7, 10, 7, 4, 1],
        ClassicHandheldCryArchetype.Rattle => [0, 9, -6, 7, -8, 4],
        ClassicHandheldCryArchetype.Electronic => [0, 12, -7, 5, -12, 17, -3],
        _ => [0, 4, -3, 7, 2]
    };

    private static double ContourSemitones(
        ClassicHandheldCryContour contour,
        double position,
        int index,
        double range,
        ref Pcg32 random) => contour switch
    {
        ClassicHandheldCryContour.Rising => (position - 0.5) * range,
        ClassicHandheldCryContour.Falling => (0.5 - position) * range,
        ClassicHandheldCryContour.Arch => (Math.Sin(Math.PI * position) - 0.45) * range,
        ClassicHandheldCryContour.Valley => (0.45 - Math.Sin(Math.PI * position)) * range,
        ClassicHandheldCryContour.Zigzag => (index % 2 == 0 ? -0.46 : 0.46) * range,
        ClassicHandheldCryContour.Trill => (index % 2 == 0 ? -0.24 : 0.24) * range,
        ClassicHandheldCryContour.Broken => GeneratorMath.Signed(ref random) * range * 0.62,
        _ => 0
    };

    private static double RhythmWeight(ClassicHandheldCryArchetype archetype, int index, ref Pcg32 random)
    {
        double[] pattern = archetype switch
        {
            ClassicHandheldCryArchetype.Squeak => [0.52, 0.38, 0.42, 0.85, 0.48, 1.2],
            ClassicHandheldCryArchetype.Chirp => [0.42, 0.36, 0.5, 0.38, 0.72, 1.05],
            ClassicHandheldCryArchetype.Buzz => [0.42, 0.35, 0.42, 0.35, 0.5],
            ClassicHandheldCryArchetype.Croak => [1.05, 0.42, 0.82, 0.48, 1.15],
            ClassicHandheldCryArchetype.Roar => [0.68, 0.78, 0.92, 1.05, 1.35],
            ClassicHandheldCryArchetype.Wail => [0.72, 0.9, 1.12, 1.35, 1.0],
            ClassicHandheldCryArchetype.Rattle => [0.28, 0.22, 0.34, 0.2, 0.4],
            ClassicHandheldCryArchetype.Electronic => [0.32, 0.56, 0.24, 0.72, 0.38],
            _ => [1.0]
        };
        return Math.Max(0.12, pattern[index % pattern.Length] * GeneratorMath.Range(ref random, 0.82, 1.18));
    }

    private static double SecondaryInterval(ClassicHandheldCryArchetype archetype, ref Pcg32 random) => archetype switch
    {
        ClassicHandheldCryArchetype.Squeak => -12,
        ClassicHandheldCryArchetype.Chirp => -7,
        ClassicHandheldCryArchetype.Buzz => -1,
        ClassicHandheldCryArchetype.Croak => -5,
        ClassicHandheldCryArchetype.Roar => -12,
        ClassicHandheldCryArchetype.Wail => -7,
        ClassicHandheldCryArchetype.Rattle => 5,
        ClassicHandheldCryArchetype.Electronic => random.NextFloat() > 0.5f ? 7 : -5,
        _ => -7
    };

    private static int EnvelopeDrop(ClassicHandheldCryArchetype archetype, float punch, int index)
    {
        int amount = archetype switch
        {
            ClassicHandheldCryArchetype.Squeak or ClassicHandheldCryArchetype.Chirp => 7,
            ClassicHandheldCryArchetype.Buzz or ClassicHandheldCryArchetype.Rattle => 5,
            ClassicHandheldCryArchetype.Roar or ClassicHandheldCryArchetype.Wail => 3,
            _ => 4
        };
        return Math.Clamp(amount + (index % 2) - (int)Math.Round(punch * 2), 1, 10);
    }

    private static double NoiseArchetypeAmount(ClassicHandheldCryArchetype archetype) => archetype switch
    {
        ClassicHandheldCryArchetype.Squeak => 0.2,
        ClassicHandheldCryArchetype.Chirp => 0.28,
        ClassicHandheldCryArchetype.Buzz => 0.76,
        ClassicHandheldCryArchetype.Croak => 0.72,
        ClassicHandheldCryArchetype.Roar => 0.96,
        ClassicHandheldCryArchetype.Wail => 0.38,
        ClassicHandheldCryArchetype.Rattle => 0.92,
        ClassicHandheldCryArchetype.Electronic => 0.68,
        _ => 0.5
    };

    private static double NoiseBurstRatio(ClassicHandheldCryArchetype archetype, int index, float roughness) => archetype switch
    {
        ClassicHandheldCryArchetype.Squeak => Semantic.Lerp(0.14, 0.35, roughness),
        ClassicHandheldCryArchetype.Chirp => Semantic.Lerp(0.2, 0.42, roughness),
        ClassicHandheldCryArchetype.Buzz => 0.78,
        ClassicHandheldCryArchetype.Croak => 0.72,
        ClassicHandheldCryArchetype.Roar => 0.88,
        ClassicHandheldCryArchetype.Wail => index % 3 == 0 ? 0.48 : 0.22,
        ClassicHandheldCryArchetype.Rattle => index % 2 == 0 ? 0.9 : 0.45,
        ClassicHandheldCryArchetype.Electronic => index % 3 == 1 ? 0.25 : 0.72,
        _ => 0.5
    };

    private static HandheldPulseDuty AlternateDuty(HandheldPulseDuty duty) => duty switch
    {
        HandheldPulseDuty.Duty12_5 => HandheldPulseDuty.Duty25,
        HandheldPulseDuty.Duty25 => HandheldPulseDuty.Duty50,
        HandheldPulseDuty.Duty50 => HandheldPulseDuty.Duty75,
        _ => HandheldPulseDuty.Duty12_5
    };

    private static int PeriodShiftSemitones(int period, double semitones)
    {
        double frequency = 131_072.0 / Math.Max(1, 2_048 - Math.Clamp(period, 0, 2_047));
        frequency *= Math.Pow(2, semitones / 12.0);
        double shiftedPeriod = 2_048 - (131_072.0 / Math.Max(1, frequency));
        return Math.Clamp((int)Math.Round(shiftedPeriod), 0, 2_047);
    }

    private static ClassicHandheldCryArchetype ResolveArchetype(ClassicHandheldCryArchetype value, ref Pcg32 random)
    {
        if (value != ClassicHandheldCryArchetype.Random)
        {
            return value;
        }

        ClassicHandheldCryArchetype[] values =
        [
            ClassicHandheldCryArchetype.Squeak,
            ClassicHandheldCryArchetype.Chirp,
            ClassicHandheldCryArchetype.Buzz,
            ClassicHandheldCryArchetype.Croak,
            ClassicHandheldCryArchetype.Roar,
            ClassicHandheldCryArchetype.Wail,
            ClassicHandheldCryArchetype.Rattle,
            ClassicHandheldCryArchetype.Electronic
        ];
        return values[random.NextInt(values.Length)];
    }

    private static ClassicHandheldCryContour ResolveContour(ClassicHandheldCryContour value, ref Pcg32 random)
    {
        if (value != ClassicHandheldCryContour.Random)
        {
            return value;
        }

        ClassicHandheldCryContour[] values =
        [
            ClassicHandheldCryContour.Rising,
            ClassicHandheldCryContour.Falling,
            ClassicHandheldCryContour.Arch,
            ClassicHandheldCryContour.Valley,
            ClassicHandheldCryContour.Zigzag,
            ClassicHandheldCryContour.Trill,
            ClassicHandheldCryContour.Broken
        ];
        return values[random.NextInt(values.Length)];
    }

    private static int BasePeriod(ClassicHandheldCryArchetype archetype, float pitch, float bodySize)
    {
        double value = Semantic.Lerp(1_080, 1_995, pitch) - Semantic.Lerp(0, 330, bodySize);
        value += archetype switch
        {
            ClassicHandheldCryArchetype.Squeak => 145,
            ClassicHandheldCryArchetype.Chirp => 105,
            ClassicHandheldCryArchetype.Buzz => 25,
            ClassicHandheldCryArchetype.Croak => -115,
            ClassicHandheldCryArchetype.Roar => -245,
            ClassicHandheldCryArchetype.Wail => -55,
            ClassicHandheldCryArchetype.Rattle => -35,
            ClassicHandheldCryArchetype.Electronic => 70,
            _ => 0
        };
        return Math.Clamp((int)Math.Round(value), 260, 2_015);
    }

    private static double DurationScale(ClassicHandheldCryArchetype archetype) => archetype switch
    {
        ClassicHandheldCryArchetype.Squeak => 0.72,
        ClassicHandheldCryArchetype.Chirp => 0.8,
        ClassicHandheldCryArchetype.Buzz => 0.92,
        ClassicHandheldCryArchetype.Croak => 1.08,
        ClassicHandheldCryArchetype.Roar => 1.22,
        ClassicHandheldCryArchetype.Wail => 1.3,
        ClassicHandheldCryArchetype.Rattle => 0.95,
        ClassicHandheldCryArchetype.Electronic => 0.9,
        _ => 1
    };

    private static HandheldPulseDuty DutyFor(
        ClassicHandheldCryArchetype archetype,
        int index,
        bool secondChannel,
        ref Pcg32 identityRandom) => archetype switch
    {
        ClassicHandheldCryArchetype.Squeak => secondChannel ? HandheldPulseDuty.Duty25 : HandheldPulseDuty.Duty12_5,
        ClassicHandheldCryArchetype.Chirp => index % 2 == 0 ? HandheldPulseDuty.Duty12_5 : HandheldPulseDuty.Duty25,
        ClassicHandheldCryArchetype.Buzz => secondChannel ? HandheldPulseDuty.Duty25 : HandheldPulseDuty.Duty50,
        ClassicHandheldCryArchetype.Croak => secondChannel ? HandheldPulseDuty.Duty50 : HandheldPulseDuty.Duty25,
        ClassicHandheldCryArchetype.Roar => secondChannel ? HandheldPulseDuty.Duty75 : HandheldPulseDuty.Duty50,
        ClassicHandheldCryArchetype.Wail => secondChannel ? HandheldPulseDuty.Duty25 : HandheldPulseDuty.Duty50,
        ClassicHandheldCryArchetype.Rattle => index % 2 == 0 ? HandheldPulseDuty.Duty12_5 : HandheldPulseDuty.Duty50,
        ClassicHandheldCryArchetype.Electronic => (HandheldPulseDuty)identityRandom.NextInt(4),
        _ => HandheldPulseDuty.Duty50
    };

    private static byte NoisePolynomial(
        ClassicHandheldCryArchetype archetype,
        float bodySize,
        float roughness,
        int index,
        ref Pcg32 identityRandom,
        ref Pcg32 variationRandom)
    {
        int baseShift = archetype switch
        {
            ClassicHandheldCryArchetype.Squeak => 2,
            ClassicHandheldCryArchetype.Chirp => 3,
            ClassicHandheldCryArchetype.Buzz => 4,
            ClassicHandheldCryArchetype.Croak => 7,
            ClassicHandheldCryArchetype.Roar => 9,
            ClassicHandheldCryArchetype.Wail => 6,
            ClassicHandheldCryArchetype.Rattle => 5,
            ClassicHandheldCryArchetype.Electronic => 3 + identityRandom.NextInt(6),
            _ => 5
        };
        int shift = Math.Clamp(
            baseShift + (int)Math.Round(bodySize * 2 - roughness * 2) + (index % 3) - 1,
            0,
            13);
        bool shortMode = archetype is ClassicHandheldCryArchetype.Buzz or ClassicHandheldCryArchetype.Rattle or ClassicHandheldCryArchetype.Electronic
            || (roughness > 0.72f && identityRandom.NextFloat() > 0.45f);
        int divider = Math.Clamp(
            1 + identityRandom.NextInt(7) + (int)Math.Round(GeneratorMath.Signed(ref variationRandom)),
            0,
            7);
        return (byte)((shift << 4) | (shortMode ? 0x08 : 0) | divider);
    }

    private readonly record struct CryNote(
        double DurationSeconds,
        double GapSeconds,
        int StartPeriod,
        int EndPeriod,
        float Accent,
        HandheldPulseDuty PrimaryDuty,
        HandheldPulseDuty SecondaryDuty);

    private readonly record struct PulseStep(
        double DurationSeconds,
        int StartPeriod,
        int EndPeriod,
        int StartVolume,
        int EndVolume,
        HandheldPulseDuty Duty);

    private readonly record struct NoiseStep(
        double DurationSeconds,
        byte Polynomial,
        int StartVolume,
        int EndVolume);
}
