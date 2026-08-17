using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Audio.Scheduling;

public sealed class SongPlaybackSnapshot
{
    private SongPlaybackSnapshot(
        string title,
        int sampleRate,
        TempoTimeline tempo,
        IReadOnlyList<PlaybackEvent> events,
        IReadOnlyDictionary<string, PlaybackTrackState> tracks,
        long musicalLengthSamples,
        long totalLengthSamples)
    {
        Title = title;
        SampleRate = sampleRate;
        Tempo = tempo;
        Events = events;
        Tracks = tracks;
        MusicalLengthSamples = musicalLengthSamples;
        TotalLengthSamples = totalLengthSamples;
    }

    public string Title { get; }
    public int SampleRate { get; }
    public TempoTimeline Tempo { get; }
    public IReadOnlyList<PlaybackEvent> Events { get; }
    public IReadOnlyDictionary<string, PlaybackTrackState> Tracks { get; }
    public long MusicalLengthSamples { get; }
    public long TotalLengthSamples { get; }
    public TimeSpan Duration => TimeSpan.FromSeconds(TotalLengthSamples / (double)SampleRate);

    public long TickToSample(long tick) => Tempo.TickToSample(tick);
    public long SampleToTick(long sample) => Tempo.SampleToTick(sample);

    public static SongPlaybackSnapshot Create(SongDocument song, int sampleRate, double tailSeconds = 2.0)
    {
        ArgumentNullException.ThrowIfNull(song);
        TempoTimeline tempo = new(song, sampleRate);
        List<PlaybackEvent> events = new();
        Dictionary<string, PlaybackTrackState> tracks = new(StringComparer.Ordinal);

        foreach (SongTrack track in song.Tracks)
        {
            PlaybackTrackState trackState = new(
                track.Id,
                track.Name,
                Math.Clamp(track.MidiChannel, 0, 15),
                Math.Clamp(track.GeneralMidiProgram, 0, 127),
                Math.Clamp(track.MidiBank, 0, 16383),
                track.Muted,
                track.Solo,
                Math.Clamp(track.Volume, 0.0, 1.5),
                Math.Clamp(track.Pan, -1.0, 1.0));
            tracks[track.Id] = trackState;

            events.Add(new PlaybackEvent(0, PlaybackEventKind.BankSelect, track.Id, trackState.MidiChannel, 0, trackState.Bank / 128));
            events.Add(new PlaybackEvent(0, PlaybackEventKind.BankSelect, track.Id, trackState.MidiChannel, 32, trackState.Bank % 128));
            events.Add(new PlaybackEvent(0, PlaybackEventKind.ProgramChange, track.Id, trackState.MidiChannel, trackState.Program, 0));
            events.Add(new PlaybackEvent(0, PlaybackEventKind.ControlChange, track.Id, trackState.MidiChannel, 7, ToMidiVolume(trackState.Volume)));
            events.Add(new PlaybackEvent(0, PlaybackEventKind.ControlChange, track.Id, trackState.MidiChannel, 10, ToMidiPan(trackState.Pan)));

            foreach (SongClip clip in track.Clips)
            {
                foreach (NoteEvent note in clip.Notes)
                {
                    long startTick = clip.StartTick + note.StartTick;
                    long endTick = startTick + Math.Max(1, note.DurationTicks);
                    long startSample = tempo.TickToSample(startTick);
                    long endSample = Math.Max(startSample + 1, tempo.TickToSample(endTick));
                    events.Add(new PlaybackEvent(
                        startSample,
                        PlaybackEventKind.NoteOn,
                        track.Id,
                        trackState.MidiChannel,
                        Math.Clamp(note.Pitch, 0, 127),
                        Math.Clamp(note.Velocity, 1, 127)));
                    events.Add(new PlaybackEvent(
                        endSample,
                        PlaybackEventKind.NoteOff,
                        track.Id,
                        trackState.MidiChannel,
                        Math.Clamp(note.Pitch, 0, 127),
                        0));
                }

                foreach (ProgramChangeEvent programChange in clip.ProgramChanges)
                {
                    long sample = tempo.TickToSample(clip.StartTick + programChange.StartTick);
                    int bank = Math.Clamp(programChange.Bank, 0, 16383);
                    events.Add(new PlaybackEvent(sample, PlaybackEventKind.BankSelect, track.Id, trackState.MidiChannel, 0, bank / 128));
                    events.Add(new PlaybackEvent(sample, PlaybackEventKind.BankSelect, track.Id, trackState.MidiChannel, 32, bank % 128));
                    events.Add(new PlaybackEvent(sample, PlaybackEventKind.ProgramChange, track.Id, trackState.MidiChannel, Math.Clamp(programChange.Program, 0, 127), 0));
                }

                foreach (ControlChangeEvent control in clip.ControlChanges)
                {
                    events.Add(new PlaybackEvent(
                        tempo.TickToSample(clip.StartTick + control.StartTick),
                        PlaybackEventKind.ControlChange,
                        track.Id,
                        trackState.MidiChannel,
                        Math.Clamp(control.ControllerNumber, 0, 127),
                        Math.Clamp(control.Value, 0, 127)));
                }
            }
        }

        PlaybackEvent[] ordered = events
            .OrderBy(item => item.Sample)
            .ThenBy(item => PlaybackEvent.SortPriority(item.Kind))
            .ThenBy(item => item.Channel)
            .ThenBy(item => item.Data1)
            .ToArray();

        long musicalLength = tempo.TickToSample(song.LengthTicks);
        if (ordered.Length > 0)
        {
            musicalLength = Math.Max(musicalLength, ordered[^1].Sample);
        }

        long tail = checked((long)Math.Round(Math.Clamp(tailSeconds, 0.0, 30.0) * sampleRate));
        return new SongPlaybackSnapshot(
            song.Title,
            sampleRate,
            tempo,
            ordered,
            tracks,
            musicalLength,
            musicalLength + tail);
    }

    public static int ToMidiVolume(double volume) =>
        Math.Clamp((int)Math.Round(Math.Clamp(volume, 0.0, 1.5) / 1.5 * 127.0), 0, 127);

    public static int ToMidiPan(double pan) =>
        Math.Clamp((int)Math.Round((Math.Clamp(pan, -1.0, 1.0) + 1.0) * 63.5), 0, 127);
}
