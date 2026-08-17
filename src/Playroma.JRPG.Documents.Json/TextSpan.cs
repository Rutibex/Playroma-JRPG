namespace Playroma.JRPG.Documents.Json;

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => checked(Start + Length);

    public bool Contains(int position) => position >= Start && position < End;

    public bool Contains(TextSpan other) => other.Start >= Start && other.End <= End;

    public bool Overlaps(TextSpan other) => Start < other.End && other.Start < End;

    public static TextSpan FromBounds(int start, int end)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        if (end < start) throw new ArgumentOutOfRangeException(nameof(end));
        return new TextSpan(start, end - start);
    }
}
