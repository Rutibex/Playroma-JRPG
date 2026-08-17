namespace Playroma.Sound.Analysis.Profiling;

public sealed record RealtimeProfileSample(
    DateTimeOffset TimestampUtc,
    double ProducerMilliseconds,
    double CallbackMilliseconds,
    double AudioDeadlineMilliseconds,
    double RenderLoadRatio,
    double RingFillRatio,
    int ActiveVoices,
    int VirtualVoices,
    int CommandQueueCount,
    long UnderrunFrames,
    long OverrunFrames,
    long AllocationBytes,
    string State);

public sealed record RealtimeProfileSummary(
    int SampleCount,
    double AverageProducerMilliseconds,
    double P95ProducerMilliseconds,
    double PeakProducerMilliseconds,
    double AverageCallbackMilliseconds,
    double P95CallbackMilliseconds,
    double PeakCallbackMilliseconds,
    double AverageRenderLoadRatio,
    double PeakRenderLoadRatio,
    double MinimumRingFillRatio,
    int PeakActiveVoices,
    int PeakVirtualVoices,
    long LatestUnderrunFrames,
    long LatestOverrunFrames,
    long PeakAllocationBytes);

public sealed class RealtimeProfiler
{
    private readonly object _gate = new();
    private readonly RealtimeProfileSample[] _samples;
    private int _count;
    private int _next;

    public RealtimeProfiler(int capacity = 1_200)
    {
        if (capacity < 8) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new RealtimeProfileSample[capacity];
    }

    public int Capacity => _samples.Length;

    public void Add(RealtimeProfileSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        lock (_gate)
        {
            _samples[_next] = sample;
            _next = (_next + 1) % _samples.Length;
            _count = Math.Min(_samples.Length, _count + 1);
        }
    }

    public IReadOnlyList<RealtimeProfileSample> CaptureSamples()
    {
        lock (_gate)
        {
            var result = new RealtimeProfileSample[_count];
            int start = (_next - _count + _samples.Length) % _samples.Length;
            for (int i = 0; i < _count; i++) result[i] = _samples[(start + i) % _samples.Length];
            return result;
        }
    }

    public RealtimeProfileSummary CaptureSummary()
    {
        IReadOnlyList<RealtimeProfileSample> samples = CaptureSamples();
        if (samples.Count == 0) return new RealtimeProfileSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        double[] producer = samples.Select(item => item.ProducerMilliseconds).Order().ToArray();
        double[] callback = samples.Select(item => item.CallbackMilliseconds).Order().ToArray();
        return new RealtimeProfileSummary(
            samples.Count, producer.Average(), Percentile(producer, 0.95), producer[^1], callback.Average(), Percentile(callback, 0.95), callback[^1],
            samples.Average(item => item.RenderLoadRatio), samples.Max(item => item.RenderLoadRatio), samples.Min(item => item.RingFillRatio),
            samples.Max(item => item.ActiveVoices), samples.Max(item => item.VirtualVoices), samples[^1].UnderrunFrames, samples[^1].OverrunFrames,
            samples.Max(item => item.AllocationBytes));
    }

    public void Clear()
    {
        lock (_gate) { Array.Clear(_samples); _count = 0; _next = 0; }
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
