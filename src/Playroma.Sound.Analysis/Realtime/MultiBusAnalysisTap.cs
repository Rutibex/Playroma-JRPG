using Playroma.Sound.Core.Audio;
using Playroma.Sound.Streaming.Runtime;

namespace Playroma.Sound.Analysis.Realtime;

/// <summary>Routes selected named streaming buses into independent worker-based analysis taps.</summary>
public sealed class MultiBusAnalysisTap : IStreamingBusTap, IDisposable
{
    private sealed record Entry(string Id, AudioAnalysisTap Tap);
    private readonly object _gate = new();
    private readonly AudioFormat _format;
    private readonly AudioAnalysisOptions _options;
    private Entry[] _entries = [];

    public MultiBusAnalysisTap(AudioFormat format, AudioAnalysisOptions? options = null)
    {
        _format = format;
        _options = options ?? new AudioAnalysisOptions();
        _options.Validate();
    }

    public IReadOnlyList<string> EnabledBusIds => Volatile.Read(ref _entries).Select(entry => entry.Id).ToArray();

    public void SetEnabledBuses(IEnumerable<string> busIds)
    {
        ArgumentNullException.ThrowIfNull(busIds);
        string[] ids = busIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        Entry[] retired;
        lock (_gate)
        {
            Entry[] current = Volatile.Read(ref _entries);
            var byId = current.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            Entry[] replacement = ids.Select(id => byId.TryGetValue(id, out Entry? existing) ? existing : new Entry(id, new AudioAnalysisTap(_format, _options))).ToArray();
            retired = current.Where(entry => !ids.Contains(entry.Id, StringComparer.Ordinal)).ToArray();
            Volatile.Write(ref _entries, replacement);
        }
        foreach (Entry entry in retired) entry.Tap.Dispose();
    }

    public bool TryWriteBus(string busId, ReadOnlySpan<float> interleavedSamples, int frameCount, AudioFormat format)
    {
        if (format != _format) return false;
        foreach (Entry entry in Volatile.Read(ref _entries))
            if (StringComparer.Ordinal.Equals(entry.Id, busId)) return entry.Tap.TryWrite(interleavedSamples, frameCount);
        return false;
    }

    public AudioAnalysisSnapshot? Capture(string busId)
    {
        foreach (Entry entry in Volatile.Read(ref _entries))
            if (StringComparer.Ordinal.Equals(entry.Id, busId)) return entry.Tap.Snapshot;
        return null;
    }

    public void Dispose()
    {
        Entry[] entries;
        lock (_gate) { entries = Volatile.Read(ref _entries); Volatile.Write(ref _entries, []); }
        foreach (Entry entry in entries) entry.Tap.Dispose();
    }
}
