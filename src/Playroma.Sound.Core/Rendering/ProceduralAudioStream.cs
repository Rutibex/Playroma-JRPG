using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.Nodes;

namespace Playroma.Sound.Core.Rendering;

/// <summary>
/// Pull adapter for live engines. The caller owns the destination buffer and decides when audio is requested.
/// </summary>
public sealed class ProceduralAudioStream
{
    private readonly IAudioNode _root;

    public ProceduralAudioStream(IAudioNode root, AudioFormat format)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        Format = format;
        _root.Prepare(format);
    }

    public AudioFormat Format { get; }
    public bool IsComplete => _root.IsComplete;
    public long FramesRendered { get; private set; }

    public void Reset()
    {
        _root.Reset();
        FramesRendered = 0;
    }

    public int Read(Span<float> destination, int requestedFrames)
    {
        if (requestedFrames < 0 || requestedFrames > Format.BlockFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        }

        int requiredSamples = checked(requestedFrames * Format.Channels);
        if (destination.Length < requiredSamples)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        if (_root.IsComplete)
        {
            destination[..requiredSamples].Clear();
            return 0;
        }

        _root.Process(destination[..requiredSamples], requestedFrames);
        FramesRendered += requestedFrames;
        return requestedFrames;
    }
}
