namespace PlayromaMusic.Core.MusicTheory;

public sealed record ChordDefinition(
    PitchClass Root,
    IReadOnlyList<int> MidiPitches,
    int ScaleDegree,
    bool IsSeventh)
{
    public string DisplayName => $"{Root.ToDisplayName()} degree {ScaleDegree + 1}{(IsSeventh ? " 7" : string.Empty)}";
}

public static class ChordBuilder
{
    public static ChordDefinition BuildDiatonic(
        PitchClass keyRoot,
        ScaleDefinition scale,
        int scaleDegree,
        int baseOctave,
        bool includeSeventh = false)
    {
        if (scale.Intervals.Count < 5)
        {
            throw new ArgumentException("Diatonic chords require a scale with at least five notes.", nameof(scale));
        }

        int[] offsets = includeSeventh ? new[] { 0, 2, 4, 6 } : new[] { 0, 2, 4 };
        int[] pitches = offsets
            .Select(offset => scale.MidiPitchForDegree(keyRoot, scaleDegree + offset, baseOctave))
            .ToArray();

        return new ChordDefinition(
            (PitchClass)(pitches[0] % 12),
            pitches,
            Mod(scaleDegree, scale.Intervals.Count),
            includeSeventh);
    }

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
