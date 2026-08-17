namespace PlayromaMusic.Core.Generation;

public sealed record GenerationSettings
{
    public int Bars { get; init; } = 16;
    public int BeatsPerBar { get; init; } = 4;
    public int BeatUnit { get; init; } = 4;
    public int TicksPerQuarterNote { get; init; } = BuildInfo.DefaultTicksPerQuarterNote;
    public int MinimumTempo { get; init; } = 92;
    public int MaximumTempo { get; init; } = 142;

    public void Validate()
    {
        if (Bars < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(Bars), "At least four bars are required.");
        }

        if (BeatsPerBar <= 0 || BeatUnit <= 0 || (BeatUnit & (BeatUnit - 1)) != 0)
        {
            throw new ArgumentException("Meter must have a positive numerator and a power-of-two denominator.");
        }

        if (TicksPerQuarterNote is < 24 or > 32767)
        {
            throw new ArgumentOutOfRangeException(nameof(TicksPerQuarterNote));
        }

        if (MinimumTempo < 20 || MaximumTempo > 300 || MaximumTempo < MinimumTempo)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumTempo));
        }
    }
}
