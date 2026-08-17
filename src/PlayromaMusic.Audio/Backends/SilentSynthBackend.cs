namespace PlayromaMusic.Audio.Backends;

public sealed class SilentSynthBackend : ISynthBackend
{
    public SilentSynthBackend(int sampleRate, string statusMessage)
    {
        SampleRate = sampleRate;
        StatusMessage = statusMessage;
    }

    public SynthBackendKind Kind => SynthBackendKind.Silent;
    public string DisplayName => "Silent diagnostic backend";
    public int SampleRate { get; }
    public bool IsReady => false;
    public string StatusMessage { get; }
    public string SoundFontPath => string.Empty;

    public void LoadSoundFont(string path) { }
    public void ProgramChange(int channel, int program) { }
    public void ControlChange(int channel, int controller, int value) { }
    public void NoteOn(int channel, int pitch, int velocity) { }
    public void NoteOff(int channel, int pitch) { }
    public void AllNotesOff() { }
    public void Reset() { }

    public void Render(Span<float> interleavedStereo, int frameCount) =>
        interleavedStereo[..Math.Min(interleavedStereo.Length, checked(frameCount * 2))].Clear();

    public void Dispose() { }
}
