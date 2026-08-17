namespace PlayromaMusic.Audio.Engine;

public enum AudioTransportState
{
    Stopped,
    Playing,
    Paused
}

public sealed record AudioEngineDiagnostics(
    AudioTransportState TransportState,
    string BackendName,
    string BackendStatus,
    bool BackendReady,
    long PositionSamples,
    long LengthSamples,
    int SampleRate,
    int PreparedBufferCount,
    int QueueHighWaterMark,
    long RenderedBufferCount,
    long SubmittedBufferCount,
    long UnderrunCount,
    double LastRenderMilliseconds,
    double PeakLevel,
    string LastError)
{
    public TimeSpan Position => SampleRate > 0 ? TimeSpan.FromSeconds(PositionSamples / (double)SampleRate) : TimeSpan.Zero;
    public TimeSpan Length => SampleRate > 0 ? TimeSpan.FromSeconds(LengthSamples / (double)SampleRate) : TimeSpan.Zero;
}
