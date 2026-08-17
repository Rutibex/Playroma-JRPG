namespace PlayromaMusic.Audio.Scheduling;

public enum PlaybackEventKind
{
    BankSelect,
    ProgramChange,
    ControlChange,
    NoteOff,
    NoteOn
}

public readonly record struct PlaybackEvent(
    long Sample,
    PlaybackEventKind Kind,
    string TrackId,
    int Channel,
    int Data1,
    int Data2)
{
    public static int SortPriority(PlaybackEventKind kind) => kind switch
    {
        PlaybackEventKind.BankSelect => 0,
        PlaybackEventKind.ProgramChange => 1,
        PlaybackEventKind.ControlChange => 2,
        PlaybackEventKind.NoteOff => 3,
        PlaybackEventKind.NoteOn => 4,
        _ => 4
    };
}

public sealed record PlaybackTrackState(
    string TrackId,
    string Name,
    int MidiChannel,
    int Program,
    int Bank,
    bool Muted,
    bool Solo,
    double Volume,
    double Pan);
