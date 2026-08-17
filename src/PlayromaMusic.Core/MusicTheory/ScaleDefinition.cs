namespace PlayromaMusic.Core.MusicTheory;

public enum ScaleKind
{
    Major,
    NaturalMinor,
    Dorian,
    Phrygian,
    Lydian,
    Mixolydian,
    Locrian,
    MajorPentatonic,
    MinorPentatonic,
    Blues,
    HarmonicMinor,
    MelodicMinor,
    DoubleHarmonic,
    WholeTone,
    Diminished,
    Hirajoshi,
    InSen,
    Yo,
    Iwato,
    Chromatic
}

public sealed record ScaleDefinition(ScaleKind Kind, string Name, IReadOnlyList<int> Intervals)
{
    public bool ContainsPitchClass(PitchClass root, int absolutePitchClass)
    {
        int relative = PitchClassUtilities.Normalize(absolutePitchClass - (int)root);
        return Intervals.Contains(relative);
    }

    public int MidiPitchForDegree(PitchClass root, int degree, int baseOctave)
    {
        int count = Intervals.Count;
        int normalizedDegree = Mod(degree, count);
        int octaveOffset = FloorDiv(degree, count);
        int pitch = ((baseOctave + 1) * 12) + (int)root + Intervals[normalizedDegree] + (12 * octaveOffset);
        return Math.Clamp(pitch, 0, 127);
    }

    public int NearestPitch(PitchClass root, int midiPitch)
    {
        for (int distance = 0; distance <= 12; distance++)
        {
            int down = midiPitch - distance;
            if (down >= 0 && ContainsPitchClass(root, down % 12))
            {
                return down;
            }

            int up = midiPitch + distance;
            if (up <= 127 && ContainsPitchClass(root, up % 12))
            {
                return up;
            }
        }

        return Math.Clamp(midiPitch, 0, 127);
    }

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }
}

public static class ScaleLibrary
{
    private static readonly IReadOnlyDictionary<ScaleKind, ScaleDefinition> Scales =
        new Dictionary<ScaleKind, ScaleDefinition>
        {
            [ScaleKind.Major] = new(ScaleKind.Major, "Major", new[] { 0, 2, 4, 5, 7, 9, 11 }),
            [ScaleKind.NaturalMinor] = new(ScaleKind.NaturalMinor, "Natural Minor", new[] { 0, 2, 3, 5, 7, 8, 10 }),
            [ScaleKind.Dorian] = new(ScaleKind.Dorian, "Dorian", new[] { 0, 2, 3, 5, 7, 9, 10 }),
            [ScaleKind.Phrygian] = new(ScaleKind.Phrygian, "Phrygian", new[] { 0, 1, 3, 5, 7, 8, 10 }),
            [ScaleKind.Lydian] = new(ScaleKind.Lydian, "Lydian", new[] { 0, 2, 4, 6, 7, 9, 11 }),
            [ScaleKind.Mixolydian] = new(ScaleKind.Mixolydian, "Mixolydian", new[] { 0, 2, 4, 5, 7, 9, 10 }),
            [ScaleKind.Locrian] = new(ScaleKind.Locrian, "Locrian", new[] { 0, 1, 3, 5, 6, 8, 10 }),
            [ScaleKind.MajorPentatonic] = new(ScaleKind.MajorPentatonic, "Major Pentatonic", new[] { 0, 2, 4, 7, 9 }),
            [ScaleKind.MinorPentatonic] = new(ScaleKind.MinorPentatonic, "Minor Pentatonic", new[] { 0, 3, 5, 7, 10 }),
            [ScaleKind.Blues] = new(ScaleKind.Blues, "Blues", new[] { 0, 3, 5, 6, 7, 10 }),
            [ScaleKind.HarmonicMinor] = new(ScaleKind.HarmonicMinor, "Harmonic Minor", new[] { 0, 2, 3, 5, 7, 8, 11 }),
            [ScaleKind.MelodicMinor] = new(ScaleKind.MelodicMinor, "Melodic Minor", new[] { 0, 2, 3, 5, 7, 9, 11 }),
            [ScaleKind.DoubleHarmonic] = new(ScaleKind.DoubleHarmonic, "Double Harmonic", new[] { 0, 1, 4, 5, 7, 8, 11 }),
            [ScaleKind.WholeTone] = new(ScaleKind.WholeTone, "Whole Tone", new[] { 0, 2, 4, 6, 8, 10 }),
            [ScaleKind.Diminished] = new(ScaleKind.Diminished, "Diminished", new[] { 0, 2, 3, 5, 6, 8, 9, 11 }),
            [ScaleKind.Hirajoshi] = new(ScaleKind.Hirajoshi, "Hirajoshi", new[] { 0, 2, 3, 7, 8 }),
            [ScaleKind.InSen] = new(ScaleKind.InSen, "In Sen", new[] { 0, 1, 5, 7, 10 }),
            [ScaleKind.Yo] = new(ScaleKind.Yo, "Yo", new[] { 0, 2, 5, 7, 9 }),
            [ScaleKind.Iwato] = new(ScaleKind.Iwato, "Iwato", new[] { 0, 1, 5, 6, 10 }),
            [ScaleKind.Chromatic] = new(ScaleKind.Chromatic, "Chromatic", Enumerable.Range(0, 12).ToArray())
        };

    public static ScaleDefinition Get(ScaleKind kind) => Scales[kind];

    public static IReadOnlyCollection<ScaleDefinition> All { get; } = Scales.Values.ToArray();
}
