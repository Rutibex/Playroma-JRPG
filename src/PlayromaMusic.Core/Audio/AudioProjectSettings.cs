namespace PlayromaMusic.Core.Audio;

public enum SynthBackendPreference
{
    Auto,
    FluidSynth,
    MeltySynth
}

public sealed class AudioProjectSettings
{
    public SynthBackendPreference PreferredBackend { get; set; } = SynthBackendPreference.Auto;
    public string SoundFontPath { get; set; } = string.Empty;
    public int SampleRate { get; set; } = 44100;
    public int BufferFrames { get; set; } = 1024;
    public int QueuedBufferTarget { get; set; } = 4;
    public double MasterVolume { get; set; } = 0.82;
    public bool LoopEnabled { get; set; }
    public double RenderTailSeconds { get; set; } = 2.0;
    public bool AutoAuditionAfterEdit { get; set; } = true;
}
