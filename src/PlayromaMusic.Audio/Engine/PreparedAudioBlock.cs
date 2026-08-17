namespace PlayromaMusic.Audio.Engine;

public sealed record PreparedAudioBlock(
    byte[] Pcm16Stereo,
    long StartSample,
    int FrameCount,
    double PeakLevel,
    bool EndOfStream);
