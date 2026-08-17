using System.Globalization;
using Playroma.Sound.Core.Random;
using Playroma.Sound.Generators.Building;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Descriptions;

namespace Playroma.Sound.Generators.Composition;

/// <summary>A scheduled note/syllable in a compact sound-effect phrase.</summary>
public sealed record PhraseEvent(
    double StartSeconds,
    double DurationSeconds,
    double SemitoneOffset,
    double Velocity,
    PhraseArticulation Articulation,
    string PitchContour,
    string AmplitudeContour,
    double Pan);

/// <summary>Semantic controls used by the reusable short-phrase composer.</summary>
public sealed record PhrasePlanOptions
{
    public PhraseShape Shape { get; init; } = PhraseShape.Rising;
    public PhraseMood Mood { get; init; } = PhraseMood.Mysterious;
    public PhraseScale Scale { get; init; } = PhraseScale.MajorPentatonic;
    public int DesiredNotes { get; init; } = 5;
    public double DurationSeconds { get; init; } = 1.2;
    public double StartOffsetSeconds { get; init; }
    public float Speed { get; init; } = 0.5f;
    public float Complexity { get; init; } = 0.5f;
    public float PitchRange { get; init; } = 0.5f;
    public float Repetition { get; init; }
    public float PitchMovement { get; init; } = 0.35f;
    public float Musicality { get; init; } = 0.8f;
    public float OrganicAmount { get; init; } = 0.2f;
}

/// <summary>
/// Deterministic motif and phrase composition for sound effects. Melody, rhythm,
/// articulation and contour use independent random streams so a caller can retain
/// one dimension while changing another through context seeds.
/// </summary>
public static class PhraseComposer
{
    public static IReadOnlyList<PhraseEvent> Compose(PhrasePlanOptions options, SoundGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        int count = Math.Clamp(options.DesiredNotes, 1, context.Budget.MaximumPhraseNotes);
        double duration = Math.Clamp(options.DurationSeconds, 0.04, 60);
        float speed = Semantic.Clamp01(options.Speed);
        float complexity = Semantic.Clamp01(options.Complexity);
        float pitchRange = Semantic.Clamp01(options.PitchRange);
        float repetition = Semantic.Clamp01(options.Repetition);
        float movement = Semantic.Clamp01(options.PitchMovement);
        float musicality = Semantic.Clamp01(options.Musicality);
        float organic = Semantic.Clamp01(options.OrganicAmount);

        Pcg32 melody = context.CreateRandom("phrase.melody");
        Pcg32 rhythm = context.CreateRandom("phrase.rhythm");
        Pcg32 articulation = context.CreateRandom("phrase.articulation");
        Pcg32 contour = context.CreateRandom("phrase.contour");

        double[] weights = new double[count];
        double weightSum = 0;
        for (int i = 0; i < count; i++)
        {
            double syncopation = 1 + (GeneratorMath.Signed(ref rhythm) * complexity * 0.42);
            double speedBias = i == count - 1 ? Semantic.Lerp(1.15, .82, speed) : 1;
            weights[i] = Math.Max(.2, syncopation * speedBias);
            weightSum += weights[i];
        }

        double cursor = options.StartOffsetSeconds;
        var result = new List<PhraseEvent>(count);
        double previousPitch = 0;
        for (int i = 0; i < count; i++)
        {
            double slot = duration * (weights[i] / weightSum);
            double normalized = count == 1 ? 0.5 : i / (double)(count - 1);
            double shape = ShapeValue(options.Shape, normalized, i, count, ref melody);
            double rangeSemitones = Semantic.Lerp(3, 24, pitchRange);
            double pitch = shape * rangeSemitones;
            pitch += GeneratorMath.Signed(ref melody) * complexity * Semantic.Lerp(.4, 3.2, 1 - musicality);

            if (i > 0 && melody.NextFloat() < repetition * .72f)
                pitch = previousPitch;
            else if (musicality > .05f)
                pitch = ScaleQuantizer.Quantize(pitch, options.Scale);

            PhraseArticulation noteArticulation = ChooseArticulation(options.Mood, speed, complexity, ref articulation);
            double durationRatio = noteArticulation switch
            {
                PhraseArticulation.Staccato => .42,
                PhraseArticulation.Pluck => .62,
                PhraseArticulation.Legato => 1.16,
                PhraseArticulation.Smooth => 1.02,
                PhraseArticulation.Tremolo => .95,
                _ => .78
            };
            double noteDuration = Math.Clamp(slot * durationRatio, .025, Math.Max(.03, duration - (cursor - options.StartOffsetSeconds)));
            double velocity = Math.Clamp(.68 + (GeneratorMath.Signed(ref articulation) * .16) + AccentForShape(options.Shape, i, count), .2, 1.2);
            string pitchContour = BuildPitchContour(options.Shape, i, count, movement, organic, ref contour, context.Budget.MaximumGesturePoints);
            string amplitudeContour = BuildAmplitudeContour(noteArticulation);
            double pan = Math.Clamp(GeneratorMath.Signed(ref articulation) * complexity * .55, -1, 1);

            result.Add(new PhraseEvent(cursor, noteDuration, pitch, velocity, noteArticulation, pitchContour, amplitudeContour, pan));
            previousPitch = pitch;
            cursor += slot;
        }

        return result;
    }

    private static double ShapeValue(PhraseShape shape, double t, int index, int count, ref Pcg32 random) => shape switch
    {
        PhraseShape.Rising => t - .5,
        PhraseShape.Falling => .5 - t,
        PhraseShape.Arch => Math.Sin(Math.PI * t) - .5,
        PhraseShape.Valley => .5 - Math.Sin(Math.PI * t),
        PhraseShape.Zigzag => ((index & 1) == 0 ? -.42 : .42) + ((t - .5) * .25),
        PhraseShape.Trill => (index & 1) == 0 ? -.22 : .22,
        PhraseShape.CallAndResponse => index < Math.Ceiling(count / 2d)
            ? (t * 1.4) - .35
            : (((index - Math.Ceiling(count / 2d)) / Math.Max(1, Math.Floor(count / 2d))) * 1.4) - .25,
        PhraseShape.RepeatedWarning => (index & 1) == 0 ? .28 : -.18,
        PhraseShape.Question => t < .72 ? (t * .35) - .2 : (t - .72) * 1.8,
        PhraseShape.Answer => t < .35 ? .18 : .35 - t,
        PhraseShape.Chaotic => GeneratorMath.Signed(ref random) * .55,
        PhraseShape.ChargeRelease => t < .78 ? (t * .95) - .42 : .55 - ((t - .78) * 3.2),
        PhraseShape.Cascade => .5 - t + (((index % 3) - 1) * .12),
        _ => t - .5
    };

    private static PhraseArticulation ChooseArticulation(PhraseMood mood, float speed, float complexity, ref Pcg32 random)
    {
        float roll = random.NextFloat();
        if (mood is PhraseMood.Dreamlike or PhraseMood.Holy && roll < .55f) return PhraseArticulation.Legato;
        if (mood is PhraseMood.Mechanical && roll < .65f) return PhraseArticulation.Staccato;
        if (mood is PhraseMood.Sinister or PhraseMood.Ancient && roll < .42f) return PhraseArticulation.Tremolo;
        if (mood is PhraseMood.Cute && roll < .65f) return PhraseArticulation.Pluck;
        if (speed > .72f && roll < .7f) return PhraseArticulation.Staccato;
        if (complexity > .65f && roll < .32f) return PhraseArticulation.Accent;
        return roll < .5f ? PhraseArticulation.Smooth : PhraseArticulation.Pluck;
    }

    private static double AccentForShape(PhraseShape shape, int index, int count)
    {
        if (shape == PhraseShape.ChargeRelease && index == count - 1) return .25;
        if (shape == PhraseShape.CallAndResponse && index == (count + 1) / 2) return .15;
        if (index == 0) return .08;
        return 0;
    }

    private static string BuildPitchContour(
        PhraseShape shape,
        int index,
        int count,
        float movement,
        float organic,
        ref Pcg32 random,
        int maximumPoints)
    {
        int points = Math.Clamp(maximumPoints, 2, 5);
        double amount = Semantic.Lerp(.1, 7.5, movement);
        double attackBend = GeneratorMath.Signed(ref random) * amount * (.35 + organic * .55);
        double middle = GeneratorMath.Signed(ref random) * amount * .22;
        double tail = shape switch
        {
            PhraseShape.Rising or PhraseShape.Question or PhraseShape.ChargeRelease => amount * .34,
            PhraseShape.Falling or PhraseShape.Answer or PhraseShape.Cascade => -amount * .42,
            PhraseShape.Trill => ((index & 1) == 0 ? 1 : -1) * amount * .28,
            PhraseShape.Chaotic => GeneratorMath.Signed(ref random) * amount * .7,
            _ => GeneratorMath.Signed(ref random) * amount * .2
        };

        var values = new List<(double Position, double Value, string Curve)>
        {
            (0, attackBend, "EaseOut"),
            (.18, 0, "SmoothStep")
        };
        if (points >= 4) values.Add((.58, middle, "SmoothStep"));
        if (points >= 5) values.Add((.82, -middle * .45, "SmoothStep"));
        values.Add((1, tail, "EaseIn"));
        return string.Join(';', values.Select(value => FormattableString.Invariant($"{value.Position:0.###}:{value.Value:0.###}:{value.Curve}")));
    }

    private static string BuildAmplitudeContour(PhraseArticulation articulation) => articulation switch
    {
        PhraseArticulation.Staccato => "0:0:EaseOut;0.04:1:EaseOut;0.32:0.78:SmoothStep;1:0:EaseIn",
        PhraseArticulation.Pluck => "0:0:EaseOut;0.025:1:EaseOut;0.25:0.58:SmoothStep;1:0:EaseIn",
        PhraseArticulation.Legato => "0:0:EaseOut;0.12:0.88:SmoothStep;0.82:0.8:SmoothStep;1:0:EaseIn",
        PhraseArticulation.Accent => "0:0:EaseOut;0.02:1.15:EaseOut;0.18:0.72:SmoothStep;1:0:EaseIn",
        PhraseArticulation.Tremolo => "0:0:EaseOut;0.08:0.9:SmoothStep;0.88:0.82:SmoothStep;1:0:EaseIn",
        _ => "0:0:EaseOut;0.08:0.92:SmoothStep;0.72:0.72:SmoothStep;1:0:EaseIn"
    };
}

public sealed record PhraseVoiceSettings
{
    public string Waveform { get; init; } = "Sine";
    public double RootFrequencyHz { get; init; } = 440;
    public double Amplitude { get; init; } = .3;
    public double VibratoDepthSemitones { get; init; }
    public double VibratoRateHz { get; init; } = 5.5;
    public double TremoloDepth { get; init; }
    public double TremoloRateHz { get; init; } = 5;
    public double DriftDepthSemitones { get; init; }
    public double DriftRateHz { get; init; } = 1.4;
    public double AttackSeconds { get; init; } = .002;
    public double ReleaseSeconds { get; init; } = .05;
    public string Quantization { get; init; } = "None";
    public double PulseWidth { get; init; } = .5;
}

public sealed record PhraseRenderResult(
    IReadOnlyList<string> Outputs,
    IReadOnlyList<string> SourceNodes,
    double EndSeconds);

internal static class PhraseGraphRenderer
{
    public static PhraseRenderResult AddPhrase(
        GeneratedGraphBuilder builder,
        string stem,
        IReadOnlyList<PhraseEvent> events,
        PhraseVoiceSettings voice,
        double semitoneTranspose = 0,
        double amplitudeScale = 1,
        bool usePerNotePan = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(voice);
        var outputs = new List<string>(events.Count);
        var sources = new List<string>(events.Count);
        double end = 0;

        for (int i = 0; i < events.Count; i++)
        {
            PhraseEvent item = events[i];
            double frequency = voice.RootFrequencyHz * Math.Pow(2, (item.SemitoneOffset + semitoneTranspose) / 12);
            double tremolo = item.Articulation == PhraseArticulation.Tremolo
                ? Math.Max(.18, voice.TremoloDepth)
                : voice.TremoloDepth;
            string source = LayerBuilders.AddGestureTone(builder, $"{stem}-{i + 1}", voice.Waveform,
                frequency, voice.Amplitude * item.Velocity * amplitudeScale, item.DurationSeconds,
                item.PitchContour, item.AmplitudeContour,
                voice.VibratoDepthSemitones, voice.VibratoRateHz, item.DurationSeconds * .18,
                tremolo, voice.TremoloRateHz,
                voice.DriftDepthSemitones, voice.DriftRateHz,
                Math.Min(voice.AttackSeconds, item.DurationSeconds * .3),
                Math.Min(voice.ReleaseSeconds, item.DurationSeconds * .65),
                voice.Quantization, voice.PulseWidth);
            sources.Add(source);
            string output = item.StartSeconds > .0001
                ? LayerBuilders.AddDelayed(builder, source, $"{stem}-{i + 1}-offset", item.StartSeconds)
                : source;

            if (usePerNotePan && Math.Abs(item.Pan) > .02 && builder.NodeCount + 1 < builder.Budget.MaximumNodes)
            {
                string pan = builder.AddNode($"{stem}-{i + 1}-pan", "core.pan", ("pan", item.Pan));
                builder.Connect(output, pan);
                output = pan;
            }

            outputs.Add(output);
            end = Math.Max(end, item.StartSeconds + item.DurationSeconds);
        }

        return new PhraseRenderResult(outputs, sources, end);
    }
}

internal static class ScaleQuantizer
{
    private static readonly IReadOnlyDictionary<PhraseScale, int[]> Intervals = new Dictionary<PhraseScale, int[]>
    {
        [PhraseScale.Chromatic] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
        [PhraseScale.Major] = [0, 2, 4, 5, 7, 9, 11],
        [PhraseScale.NaturalMinor] = [0, 2, 3, 5, 7, 8, 10],
        [PhraseScale.MajorPentatonic] = [0, 2, 4, 7, 9],
        [PhraseScale.MinorPentatonic] = [0, 3, 5, 7, 10],
        [PhraseScale.WholeTone] = [0, 2, 4, 6, 8, 10],
        [PhraseScale.Diminished] = [0, 2, 3, 5, 6, 8, 9, 11]
    };

    public static double Quantize(double semitones, PhraseScale scale)
    {
        int[] intervals = Intervals[scale];
        double best = semitones;
        double distance = double.MaxValue;
        for (int octave = -5; octave <= 5; octave++)
        {
            foreach (int interval in intervals)
            {
                double candidate = (octave * 12) + interval;
                double candidateDistance = Math.Abs(candidate - semitones);
                if (candidateDistance < distance)
                {
                    distance = candidateDistance;
                    best = candidate;
                }
            }
        }
        return best;
    }
}
