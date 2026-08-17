namespace PlayromaMusic.Audio.Backends;

public enum SynthBackendKind
{
    Silent,
    FluidSynth,
    MeltySynth
}

public interface ISynthBackend : IDisposable
{
    SynthBackendKind Kind { get; }
    string DisplayName { get; }
    int SampleRate { get; }
    bool IsReady { get; }
    string StatusMessage { get; }
    string SoundFontPath { get; }

    void LoadSoundFont(string path);
    void ProgramChange(int channel, int program);
    void ControlChange(int channel, int controller, int value);
    void NoteOn(int channel, int pitch, int velocity);
    void NoteOff(int channel, int pitch);
    void AllNotesOff();
    void Reset();
    void Render(Span<float> interleavedStereo, int frameCount);
}
