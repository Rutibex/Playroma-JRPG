using Playroma.Sound.Core.Audio;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Streaming.Runtime;

namespace Playroma.Sound.WinForms.Playback;

/// <summary>Adapts a prepared short sound graph to the common streaming playback contract.</summary>
public sealed class CompiledGraphStreamingSource : IStreamingAudioSource
{
    private readonly CompiledSoundGraph _graph;
    private int _running;

    public CompiledGraphStreamingSource(CompiledSoundGraph graph) =>
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));

    public AudioFormat Format => _graph.CompiledFormat;
    public long CurrentSampleFrame => _graph.CurrentSampleFrame;
    public bool IsRunning => Volatile.Read(ref _running) != 0;

    public void Start()
    {
        if (_graph.IsComplete) _graph.Reset();
        Volatile.Write(ref _running, 1);
    }

    public void Stop() => Volatile.Write(ref _running, 0);

    public void Reset()
    {
        _graph.Reset();
        Volatile.Write(ref _running, 0);
    }

    public int Render(Span<float> destination, int requestedFrames)
    {
        if (requestedFrames < 0 || requestedFrames > Format.BlockFrames)
            throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        int requiredSamples = checked(requestedFrames * Format.Channels);
        if (destination.Length < requiredSamples)
            throw new ArgumentException("The destination is too small for the requested frame count.", nameof(destination));

        Span<float> output = destination[..requiredSamples];
        if (!IsRunning)
        {
            output.Clear();
            return requestedFrames;
        }

        _graph.Process(output, requestedFrames);
        if (_graph.IsComplete) Volatile.Write(ref _running, 0);
        return requestedFrames;
    }
}
