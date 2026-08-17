namespace PlayromaMusic.Core.MusicTheory;

public enum PitchClass
{
    C = 0,
    CSharp = 1,
    D = 2,
    DSharp = 3,
    E = 4,
    F = 5,
    FSharp = 6,
    G = 7,
    GSharp = 8,
    A = 9,
    ASharp = 10,
    B = 11
}

public static class PitchClassUtilities
{
    private static readonly string[] SharpNames =
    {
        "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
    };

    public static int Normalize(int value)
    {
        int result = value % 12;
        return result < 0 ? result + 12 : result;
    }

    public static PitchClass Transpose(this PitchClass pitchClass, int semitones) =>
        (PitchClass)Normalize((int)pitchClass + semitones);

    public static string ToDisplayName(this PitchClass pitchClass) => SharpNames[(int)pitchClass];

    public static string GetMidiNoteName(int midiPitch)
    {
        if (midiPitch is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(midiPitch), "MIDI pitch must be between 0 and 127.");
        }

        int octave = (midiPitch / 12) - 1;
        return $"{SharpNames[midiPitch % 12]}{octave}";
    }
}
