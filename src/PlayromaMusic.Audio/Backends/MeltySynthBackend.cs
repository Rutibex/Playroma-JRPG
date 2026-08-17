using MeltySynth;

namespace PlayromaMusic.Audio.Backends;

public sealed class MeltySynthBackend : ISynthBackend
{
    private Synthesizer? _synthesizer;
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();

    public MeltySynthBackend(int sampleRate)
    {
        SampleRate = sampleRate;
    }

    public SynthBackendKind Kind => SynthBackendKind.MeltySynth;
    public string DisplayName => "MeltySynth (managed fallback)";
    public int SampleRate { get; }
    public bool IsReady => _synthesizer is not null;
    public string StatusMessage { get; private set; } = "Waiting for a SoundFont.";
    public string SoundFontPath { get; private set; } = string.Empty;

    public void LoadSoundFont(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("SoundFont file was not found.", fullPath);
        }

        _synthesizer = new Synthesizer(fullPath, SampleRate);
        SoundFontPath = fullPath;
        StatusMessage = $"Loaded {Path.GetFileName(fullPath)}";
        Reset();
    }

    public void ProgramChange(int channel, int program) =>
        RequireSynth().ProcessMidiMessage(ClampChannel(channel), 0xC0, Math.Clamp(program, 0, 127), 0);

    public void ControlChange(int channel, int controller, int value) =>
        RequireSynth().ProcessMidiMessage(ClampChannel(channel), 0xB0, Math.Clamp(controller, 0, 127), Math.Clamp(value, 0, 127));

    public void NoteOn(int channel, int pitch, int velocity) =>
        RequireSynth().NoteOn(ClampChannel(channel), Math.Clamp(pitch, 0, 127), Math.Clamp(velocity, 0, 127));

    public void NoteOff(int channel, int pitch) =>
        RequireSynth().NoteOff(ClampChannel(channel), Math.Clamp(pitch, 0, 127));

    public void AllNotesOff()
    {
        if (_synthesizer is null)
        {
            return;
        }

        for (int channel = 0; channel < 16; channel++)
        {
            _synthesizer.ProcessMidiMessage(channel, 0xB0, 64, 0);
            _synthesizer.ProcessMidiMessage(channel, 0xB0, 120, 0);
            _synthesizer.ProcessMidiMessage(channel, 0xB0, 123, 0);
        }
    }

    public void Reset()
    {
        AllNotesOff();
        for (int channel = 0; channel < 16; channel++)
        {
            ControlChange(channel, 7, 100);
            ControlChange(channel, 10, 64);
            ControlChange(channel, 11, 127);
        }
    }

    public void Render(Span<float> interleavedStereo, int frameCount)
    {
        if (frameCount < 0 || interleavedStereo.Length < frameCount * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        if (_synthesizer is null)
        {
            interleavedStereo[..(frameCount * 2)].Clear();
            return;
        }

        EnsureBuffers(frameCount);
        Span<float> left = _left.AsSpan(0, frameCount);
        Span<float> right = _right.AsSpan(0, frameCount);
        left.Clear();
        right.Clear();
        _synthesizer.Render(left, right);

        for (int i = 0; i < frameCount; i++)
        {
            interleavedStereo[i * 2] = left[i];
            interleavedStereo[(i * 2) + 1] = right[i];
        }
    }

    public void Dispose()
    {
        AllNotesOff();
        _synthesizer = null;
    }

    private Synthesizer RequireSynth() =>
        _synthesizer ?? throw new InvalidOperationException("No SoundFont has been loaded into MeltySynth.");

    private void EnsureBuffers(int frameCount)
    {
        if (_left.Length >= frameCount)
        {
            return;
        }

        _left = new float[frameCount];
        _right = new float[frameCount];
    }

    private static int ClampChannel(int channel) => Math.Clamp(channel, 0, 15);
}
