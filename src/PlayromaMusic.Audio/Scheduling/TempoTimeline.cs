using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Audio.Scheduling;

public sealed class TempoTimeline
{
    private readonly TempoSegment[] _segments;

    public TempoTimeline(SongDocument song, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        SampleRate = sampleRate;
        TicksPerQuarterNote = song.TicksPerQuarterNote;

        List<TempoChange> changes = song.TempoMap
            .Where(change => change.Tick >= 0 && change.BeatsPerMinute > 0 && double.IsFinite(change.BeatsPerMinute))
            .OrderBy(change => change.Tick)
            .GroupBy(change => change.Tick)
            .Select(group => group.Last())
            .ToList();

        if (changes.Count == 0 || changes[0].Tick != 0)
        {
            changes.Insert(0, new TempoChange { Tick = 0, BeatsPerMinute = 120.0 });
        }

        _segments = new TempoSegment[changes.Count];
        double startSample = 0;
        for (int i = 0; i < changes.Count; i++)
        {
            TempoChange change = changes[i];
            if (i > 0)
            {
                TempoChange previous = changes[i - 1];
                startSample += (change.Tick - previous.Tick) * SamplesPerTick(previous.BeatsPerMinute);
            }

            _segments[i] = new TempoSegment(change.Tick, startSample, change.BeatsPerMinute);
        }
    }

    public int SampleRate { get; }
    public int TicksPerQuarterNote { get; }

    public long TickToSample(long tick)
    {
        if (tick <= 0)
        {
            return 0;
        }

        TempoSegment segment = FindByTick(tick);
        double sample = segment.StartSample + ((tick - segment.StartTick) * SamplesPerTick(segment.BeatsPerMinute));
        return Math.Max(0, checked((long)Math.Round(sample, MidpointRounding.AwayFromZero)));
    }

    public long SampleToTick(long sample)
    {
        if (sample <= 0)
        {
            return 0;
        }

        TempoSegment segment = FindBySample(sample);
        double ticks = segment.StartTick + ((sample - segment.StartSample) / SamplesPerTick(segment.BeatsPerMinute));
        return Math.Max(0, checked((long)Math.Round(ticks, MidpointRounding.AwayFromZero)));
    }

    public TimeSpan TickToTime(long tick) => TimeSpan.FromSeconds(TickToSample(tick) / (double)SampleRate);

    private TempoSegment FindByTick(long tick)
    {
        int low = 0;
        int high = _segments.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (_segments[middle].StartTick <= tick)
            {
                if (middle == _segments.Length - 1 || _segments[middle + 1].StartTick > tick)
                {
                    return _segments[middle];
                }

                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _segments[0];
    }

    private TempoSegment FindBySample(long sample)
    {
        int low = 0;
        int high = _segments.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (_segments[middle].StartSample <= sample)
            {
                if (middle == _segments.Length - 1 || _segments[middle + 1].StartSample > sample)
                {
                    return _segments[middle];
                }

                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _segments[0];
    }

    private double SamplesPerTick(double beatsPerMinute) =>
        (SampleRate * 60.0) / (beatsPerMinute * TicksPerQuarterNote);

    private readonly record struct TempoSegment(long StartTick, double StartSample, double BeatsPerMinute);
}
