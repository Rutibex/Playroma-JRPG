using System.Globalization;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Generation;

namespace Playroma.Sound.Core.Nodes;

/// <summary>
/// Executes a compact three-channel cartridge cry program. Each pulse channel
/// has its own sequence of period, duty, volume, and duration commands while
/// the noise channel has an independent sequence of NR43-style LFSR commands.
/// This permits the rapid note changes and asynchronous counter-voice motion
/// characteristic of elaborate handheld cries without constructing one graph
/// node for every tiny command.
/// </summary>
public sealed class HandheldCrySequenceNode : AudioNodeBase
{
    private static readonly byte[][] DutyPatterns =
    [
        [0, 0, 0, 0, 0, 0, 0, 1],
        [1, 0, 0, 0, 0, 0, 0, 1],
        [1, 0, 0, 0, 0, 1, 1, 1],
        [0, 1, 1, 1, 1, 1, 1, 0]
    ];

    private readonly PulseCommand[] _pulse1Commands;
    private readonly PulseCommand[] _pulse2Commands;
    private readonly NoiseCommand[] _noiseCommands;

    private PulseRuntimeCommand[] _pulse1Runtime = [];
    private PulseRuntimeCommand[] _pulse2Runtime = [];
    private NoiseRuntimeCommand[] _noiseRuntime = [];

    private int _pulse1Index;
    private int _pulse2Index;
    private int _noiseIndex;
    private long _pulse1Frame;
    private long _pulse2Frame;
    private long _noiseFrame;
    private double _pulse1Phase;
    private double _pulse2Phase;
    private ushort _lfsr;
    private double _noiseClockAccumulator;
    private long _elapsedFrames;
    private long _durationFrames;
    private float _masterAmplitude;
    private float _noiseMix;
    private float _pitchShiftSemitones;

    public HandheldCrySequenceNode(
        string pulse1Program,
        string pulse2Program,
        string noiseProgram,
        float masterAmplitude = 0.9f,
        float noiseMix = 0.55f,
        float pitchShiftSemitones = 0)
    {
        Pulse1Program = pulse1Program ?? string.Empty;
        Pulse2Program = pulse2Program ?? string.Empty;
        NoiseProgram = noiseProgram ?? string.Empty;
        _pulse1Commands = ParsePulseProgram(Pulse1Program);
        _pulse2Commands = ParsePulseProgram(Pulse2Program);
        _noiseCommands = ParseNoiseProgram(NoiseProgram);
        MasterAmplitude = masterAmplitude;
        NoiseMix = noiseMix;
        PitchShiftSemitones = pitchShiftSemitones;
    }

    public string Pulse1Program { get; }
    public string Pulse2Program { get; }
    public string NoiseProgram { get; }

    public float MasterAmplitude
    {
        get => _masterAmplitude;
        set => _masterAmplitude = Math.Clamp(value, 0f, 2f);
    }

    public float NoiseMix
    {
        get => _noiseMix;
        set => _noiseMix = Math.Clamp(value, 0f, 2f);
    }

    public float PitchShiftSemitones
    {
        get => _pitchShiftSemitones;
        set => _pitchShiftSemitones = Math.Clamp(value, -24f, 24f);
    }

    public double DurationSeconds => Math.Max(
        SumDuration(_pulse1Commands),
        Math.Max(SumDuration(_pulse2Commands), SumDuration(_noiseCommands)));

    public int Pulse1CommandCount => _pulse1Commands.Length;
    public int Pulse2CommandCount => _pulse2Commands.Length;
    public int NoiseCommandCount => _noiseCommands.Length;

    public override bool IsComplete => _durationFrames == 0 || _elapsedFrames >= _durationFrames;

    protected override void OnPrepare(AudioFormat format)
    {
        _pulse1Runtime = PreparePulseCommands(_pulse1Commands, format.SampleRate);
        _pulse2Runtime = PreparePulseCommands(_pulse2Commands, format.SampleRate);
        _noiseRuntime = PrepareNoiseCommands(_noiseCommands, format.SampleRate);
        _durationFrames = Math.Max(
            SumFrames(_pulse1Runtime),
            Math.Max(SumFrames(_pulse2Runtime), SumFrames(_noiseRuntime)));
    }

    public override void Reset()
    {
        _pulse1Index = 0;
        _pulse2Index = 0;
        _noiseIndex = 0;
        _pulse1Frame = 0;
        _pulse2Frame = 0;
        _noiseFrame = 0;
        _pulse1Phase = 0;
        _pulse2Phase = 0;
        _lfsr = 0x7FFF;
        _noiseClockAccumulator = 0;
        _elapsedFrames = 0;
    }

    protected override void ProcessCore(Span<float> output, int frameCount)
    {
        output.Clear();
        if (frameCount == 0 || IsComplete)
        {
            return;
        }

        int channels = Format.Channels;
        int sampleIndex = 0;
        for (int frame = 0; frame < frameCount; frame++)
        {
            if (IsComplete)
            {
                sampleIndex += channels;
                continue;
            }

            float pulse1 = RenderPulse(_pulse1Runtime, ref _pulse1Index, ref _pulse1Frame, ref _pulse1Phase);
            float pulse2 = RenderPulse(_pulse2Runtime, ref _pulse2Index, ref _pulse2Frame, ref _pulse2Phase);
            float noise = RenderNoise();
            float value = (pulse1 + pulse2 + (noise * NoiseMix)) * MasterAmplitude * 0.36f;

            for (int channel = 0; channel < channels; channel++)
            {
                output[sampleIndex++] = value;
            }

            AdvancePulse(_pulse1Runtime, ref _pulse1Index, ref _pulse1Frame, ref _pulse1Phase);
            AdvancePulse(_pulse2Runtime, ref _pulse2Index, ref _pulse2Frame, ref _pulse2Phase);
            AdvanceNoise();
            _elapsedFrames++;
        }
    }

    private float RenderPulse(
        PulseRuntimeCommand[] commands,
        ref int commandIndex,
        ref long commandFrame,
        ref double phase)
    {
        if (commandIndex >= commands.Length)
        {
            return 0;
        }

        PulseRuntimeCommand command = commands[commandIndex];
        double position = command.DurationFrames <= 1
            ? 0
            : Math.Clamp(commandFrame / (double)(command.DurationFrames - 1), 0, 1);
        int period = (int)Math.Round(Lerp(command.StartPeriod, command.EndPeriod, position));
        double frequencyHz = PeriodToFrequency(period) * Math.Pow(2, PitchShiftSemitones / 12.0);
        double phaseIncrement = Math.Min(frequencyHz, Format.SampleRate * 0.49) / Format.SampleRate;
        int volume = Math.Clamp((int)Math.Round(Lerp(command.StartVolume, command.EndVolume, position)), 0, 15);
        byte[] pattern = DutyPatterns[Math.Clamp((int)command.Duty, 0, DutyPatterns.Length - 1)];
        int sequenceIndex = Math.Clamp((int)(phase * 8.0), 0, 7);
        float value = pattern[sequenceIndex] == 0 ? 0f : volume / 15f;

        phase += phaseIncrement;
        phase -= Math.Floor(phase);
        return value;
    }

    private float RenderNoise()
    {
        if (_noiseIndex >= _noiseRuntime.Length)
        {
            return 0;
        }

        NoiseRuntimeCommand command = _noiseRuntime[_noiseIndex];
        double position = command.DurationFrames <= 1
            ? 0
            : Math.Clamp(_noiseFrame / (double)(command.DurationFrames - 1), 0, 1);
        int volume = Math.Clamp((int)Math.Round(Lerp(command.StartVolume, command.EndVolume, position)), 0, 15);
        int shift = (command.Polynomial >> 4) & 0x0F;
        bool shortMode = (command.Polynomial & 0x08) != 0;
        int dividerCode = command.Polynomial & 0x07;
        double clockHz = NoiseClockFrequency(shift, dividerCode);
        _noiseClockAccumulator += clockHz / Format.SampleRate;
        int shifts = Math.Min(128, (int)_noiseClockAccumulator);
        _noiseClockAccumulator -= shifts;
        for (int index = 0; index < shifts; index++)
        {
            ClockLfsr(shortMode);
        }

        return (_lfsr & 1) == 0 ? 0f : volume / 15f;
    }

    private static void AdvancePulse(
        PulseRuntimeCommand[] commands,
        ref int commandIndex,
        ref long commandFrame,
        ref double phase)
    {
        if (commandIndex >= commands.Length)
        {
            return;
        }

        commandFrame++;
        if (commandFrame < commands[commandIndex].DurationFrames)
        {
            return;
        }

        commandIndex++;
        commandFrame = 0;
        phase = 0;
    }

    private void AdvanceNoise()
    {
        if (_noiseIndex >= _noiseRuntime.Length)
        {
            return;
        }

        _noiseFrame++;
        if (_noiseFrame < _noiseRuntime[_noiseIndex].DurationFrames)
        {
            return;
        }

        _noiseIndex++;
        _noiseFrame = 0;
        _lfsr = 0x7FFF;
        _noiseClockAccumulator = 0;
    }

    private void ClockLfsr(bool shortMode)
    {
        int feedback = (_lfsr ^ (_lfsr >> 1)) & 1;
        _lfsr = (ushort)((_lfsr >> 1) | (feedback << 14));
        if (shortMode)
        {
            _lfsr = (ushort)((_lfsr & ~(1 << 6)) | (feedback << 6));
        }
    }

    private static PulseCommand[] ParsePulseProgram(string program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return [];
        }

        string[] encodedCommands = program.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var commands = new List<PulseCommand>(Math.Min(encodedCommands.Length, 128));
        foreach (string encoded in encodedCommands.Take(128))
        {
            string[] fields = encoded.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length != 6)
            {
                throw new FormatException($"Invalid handheld pulse command '{encoded}'. Expected duration,startPeriod,endPeriod,startVolume,endVolume,duty.");
            }

            double duration = ParseDouble(fields[0], "pulse duration");
            int startPeriod = ParseInt(fields[1], "pulse start period");
            int endPeriod = ParseInt(fields[2], "pulse end period");
            int startVolume = ParseInt(fields[3], "pulse start volume");
            int endVolume = ParseInt(fields[4], "pulse end volume");
            if (!Enum.TryParse(fields[5], true, out HandheldPulseDuty duty))
            {
                throw new FormatException($"Invalid handheld pulse duty '{fields[5]}'.");
            }

            commands.Add(new PulseCommand(
                Math.Clamp(duration, 0.001, 10),
                Math.Clamp(startPeriod, 0, 2_047),
                Math.Clamp(endPeriod, 0, 2_047),
                Math.Clamp(startVolume, 0, 15),
                Math.Clamp(endVolume, 0, 15),
                duty));
        }

        return commands.ToArray();
    }

    private static NoiseCommand[] ParseNoiseProgram(string program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return [];
        }

        string[] encodedCommands = program.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var commands = new List<NoiseCommand>(Math.Min(encodedCommands.Length, 128));
        foreach (string encoded in encodedCommands.Take(128))
        {
            string[] fields = encoded.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length != 4)
            {
                throw new FormatException($"Invalid handheld noise command '{encoded}'. Expected duration,polynomial,startVolume,endVolume.");
            }

            double duration = ParseDouble(fields[0], "noise duration");
            int polynomial = ParseInt(fields[1], "noise polynomial");
            int startVolume = ParseInt(fields[2], "noise start volume");
            int endVolume = ParseInt(fields[3], "noise end volume");
            commands.Add(new NoiseCommand(
                Math.Clamp(duration, 0.001, 10),
                (byte)Math.Clamp(polynomial, 0, 255),
                Math.Clamp(startVolume, 0, 15),
                Math.Clamp(endVolume, 0, 15)));
        }

        return commands.ToArray();
    }

    private static PulseRuntimeCommand[] PreparePulseCommands(PulseCommand[] commands, int sampleRate) =>
        commands.Select(command => new PulseRuntimeCommand(
            Math.Max(1, checked((long)Math.Round(command.DurationSeconds * sampleRate))),
            command.StartPeriod,
            command.EndPeriod,
            command.StartVolume,
            command.EndVolume,
            command.Duty)).ToArray();

    private static NoiseRuntimeCommand[] PrepareNoiseCommands(NoiseCommand[] commands, int sampleRate) =>
        commands.Select(command => new NoiseRuntimeCommand(
            Math.Max(1, checked((long)Math.Round(command.DurationSeconds * sampleRate))),
            command.Polynomial,
            command.StartVolume,
            command.EndVolume)).ToArray();

    private static double SumDuration(IEnumerable<PulseCommand> commands) => commands.Sum(command => command.DurationSeconds);
    private static double SumDuration(IEnumerable<NoiseCommand> commands) => commands.Sum(command => command.DurationSeconds);
    private static long SumFrames(IEnumerable<PulseRuntimeCommand> commands) => commands.Sum(command => command.DurationFrames);
    private static long SumFrames(IEnumerable<NoiseRuntimeCommand> commands) => commands.Sum(command => command.DurationFrames);

    private static double ParseDouble(string value, string fieldName)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) || !double.IsFinite(result))
        {
            throw new FormatException($"Invalid {fieldName} '{value}'.");
        }

        return result;
    }

    private static int ParseInt(string value, string fieldName)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            throw new FormatException($"Invalid {fieldName} '{value}'.");
        }

        return result;
    }

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private static double PeriodToFrequency(int period) =>
        131_072.0 / Math.Max(1, 2_048 - Math.Clamp(period, 0, 2_047));

    private static double NoiseClockFrequency(int shift, int dividerCode)
    {
        if (shift >= 14)
        {
            return 0;
        }

        double divisor = dividerCode == 0 ? 0.5 : dividerCode;
        return 262_144.0 / divisor / Math.Pow(2, shift + 1);
    }

    private readonly record struct PulseCommand(
        double DurationSeconds,
        int StartPeriod,
        int EndPeriod,
        int StartVolume,
        int EndVolume,
        HandheldPulseDuty Duty);

    private readonly record struct NoiseCommand(
        double DurationSeconds,
        byte Polynomial,
        int StartVolume,
        int EndVolume);

    private readonly record struct PulseRuntimeCommand(
        long DurationFrames,
        int StartPeriod,
        int EndPeriod,
        int StartVolume,
        int EndVolume,
        HandheldPulseDuty Duty);

    private readonly record struct NoiseRuntimeCommand(
        long DurationFrames,
        byte Polynomial,
        int StartVolume,
        int EndVolume);
}
