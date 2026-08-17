using PlayromaMusic.Audio.Backends;
using PlayromaMusic.Audio.Scheduling;

namespace PlayromaMusic.Audio.Engine;

internal sealed class SnapshotSynthesizer
{
    private readonly ISynthBackend _backend;
    private readonly Dictionary<string, PlaybackTrackState> _trackStates = new(StringComparer.Ordinal);
    private SongPlaybackSnapshot? _snapshot;
    private int _eventIndex;

    public SnapshotSynthesizer(ISynthBackend backend)
    {
        _backend = backend;
    }

    public long PositionSamples { get; private set; }
    public long LengthSamples => _snapshot?.TotalLengthSamples ?? 0;
    public long MusicalLengthSamples => _snapshot?.MusicalLengthSamples ?? 0;
    public bool LoopEnabled { get; set; }

    public void SetSnapshot(SongPlaybackSnapshot snapshot, long positionSamples = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
        _trackStates.Clear();
        foreach ((string id, PlaybackTrackState state) in snapshot.Tracks)
        {
            _trackStates[id] = state;
        }

        Seek(positionSamples);
    }

    public void UpdateTrack(string trackId, bool muted, bool solo, double volume, double pan)
    {
        if (!_trackStates.TryGetValue(trackId, out PlaybackTrackState? previous))
        {
            return;
        }

        PlaybackTrackState updated = previous with
        {
            Muted = muted,
            Solo = solo,
            Volume = Math.Clamp(volume, 0.0, 1.5),
            Pan = Math.Clamp(pan, -1.0, 1.0)
        };
        _trackStates[trackId] = updated;
        _backend.ControlChange(updated.MidiChannel, 7, SongPlaybackSnapshot.ToMidiVolume(updated.Volume));
        _backend.ControlChange(updated.MidiChannel, 10, SongPlaybackSnapshot.ToMidiPan(updated.Pan));
        if (updated.Muted || (HasSoloTracks() && !updated.Solo))
        {
            _backend.ControlChange(updated.MidiChannel, 123, 0);
        }
    }

    public void Seek(long sample)
    {
        PositionSamples = Math.Clamp(sample, 0, LengthSamples);
        _backend.Reset();
        _eventIndex = 0;
        if (_snapshot is null)
        {
            return;
        }

        // Restore programs and controllers up to the target. Notes deliberately restart at the seek point.
        while (_eventIndex < _snapshot.Events.Count && _snapshot.Events[_eventIndex].Sample < PositionSamples)
        {
            PlaybackEvent playbackEvent = _snapshot.Events[_eventIndex];
            if (playbackEvent.Kind is PlaybackEventKind.BankSelect or PlaybackEventKind.ProgramChange or PlaybackEventKind.ControlChange)
            {
                ApplyEvent(playbackEvent);
            }

            _eventIndex++;
        }

        while (_eventIndex < _snapshot.Events.Count && _snapshot.Events[_eventIndex].Sample == PositionSamples)
        {
            ApplyEvent(_snapshot.Events[_eventIndex]);
            _eventIndex++;
        }
    }

    public void Stop()
    {
        _backend.AllNotesOff();
        Seek(0);
    }

    public bool Render(Span<float> interleavedStereo, int frameCount)
    {
        if (frameCount < 0 || interleavedStereo.Length < frameCount * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        Span<float> output = interleavedStereo[..(frameCount * 2)];
        output.Clear();
        if (_snapshot is null || frameCount == 0)
        {
            return false;
        }

        int writtenFrames = 0;
        bool producedTimelineAudio = false;
        while (writtenFrames < frameCount)
        {
            if (PositionSamples >= LengthSamples)
            {
                if (LoopEnabled && MusicalLengthSamples > 0)
                {
                    Seek(0);
                }
                else
                {
                    return producedTimelineAudio;
                }
            }

            long blockEnd = Math.Min(LengthSamples, PositionSamples + (frameCount - writtenFrames));
            while (PositionSamples < blockEnd)
            {
                long nextEventSample = _eventIndex < _snapshot.Events.Count
                    ? _snapshot.Events[_eventIndex].Sample
                    : long.MaxValue;
                long segmentEnd = Math.Min(blockEnd, nextEventSample);
                int segmentFrames = checked((int)(segmentEnd - PositionSamples));
                if (segmentFrames > 0)
                {
                    _backend.Render(output.Slice(writtenFrames * 2, segmentFrames * 2), segmentFrames);
                    PositionSamples += segmentFrames;
                    writtenFrames += segmentFrames;
                    producedTimelineAudio = true;
                }

                while (_eventIndex < _snapshot.Events.Count && _snapshot.Events[_eventIndex].Sample <= PositionSamples)
                {
                    ApplyEvent(_snapshot.Events[_eventIndex]);
                    _eventIndex++;
                }

                if (segmentFrames == 0 && nextEventSample == long.MaxValue)
                {
                    int remaining = checked((int)(blockEnd - PositionSamples));
                    if (remaining <= 0)
                    {
                        break;
                    }

                    _backend.Render(output.Slice(writtenFrames * 2, remaining * 2), remaining);
                    PositionSamples += remaining;
                    writtenFrames += remaining;
                    producedTimelineAudio = true;
                }
            }
        }

        return producedTimelineAudio;
    }

    private void ApplyEvent(PlaybackEvent playbackEvent)
    {
        switch (playbackEvent.Kind)
        {
            case PlaybackEventKind.ProgramChange:
                _backend.ProgramChange(playbackEvent.Channel, playbackEvent.Data1);
                break;
            case PlaybackEventKind.BankSelect:
            case PlaybackEventKind.ControlChange:
                _backend.ControlChange(playbackEvent.Channel, playbackEvent.Data1, playbackEvent.Data2);
                break;
            case PlaybackEventKind.NoteOff:
                _backend.NoteOff(playbackEvent.Channel, playbackEvent.Data1);
                break;
            case PlaybackEventKind.NoteOn:
                if (IsTrackAudible(playbackEvent.TrackId))
                {
                    _backend.NoteOn(playbackEvent.Channel, playbackEvent.Data1, playbackEvent.Data2);
                }
                break;
        }
    }

    private bool IsTrackAudible(string trackId)
    {
        if (!_trackStates.TryGetValue(trackId, out PlaybackTrackState? state) || state.Muted)
        {
            return false;
        }

        return !HasSoloTracks() || state.Solo;
    }

    private bool HasSoloTracks() => _trackStates.Values.Any(track => track.Solo && !track.Muted);
}
