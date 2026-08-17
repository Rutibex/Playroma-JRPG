using Playroma.Sound.Streaming.Runtime;

namespace Playroma.Sound.NAudio.Playback;

/// <summary>
/// Applies a queued Playroma source stop after the producer thread has exited. It renders the
/// configured tail into scratch memory, then uses Reset as a bounded emergency fallback.
/// </summary>
internal static class StreamingSourceLifecycle
{
    public static void StopAtBoundary(IStreamingAudioSource source, Span<float> scratch, int maximumDrainMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumDrainMilliseconds is < 0 or > 60_000) throw new ArgumentOutOfRangeException(nameof(maximumDrainMilliseconds));
        if (!source.IsRunning) return;
        if (scratch.Length < source.Format.SamplesPerBlock) throw new ArgumentException("Scratch storage is smaller than one source block.", nameof(scratch));

        source.Stop();
        long maximumFrames = checked((long)Math.Ceiling(source.Format.SampleRate * (maximumDrainMilliseconds / 1_000.0)));
        long drainedFrames = 0;
        while (source.IsRunning && drainedFrames < maximumFrames)
        {
            int rendered = source.Render(scratch, source.Format.BlockFrames);
            if (rendered < 0 || rendered > source.Format.BlockFrames)
                throw new InvalidOperationException("The streaming source returned an invalid frame count while stopping.");
            if (rendered == 0) break;
            drainedFrames += rendered;
        }

        if (source.IsRunning)
        {
            source.Reset();
            source.Render(Span<float>.Empty, 0);
        }
        if (source.IsRunning)
            throw new InvalidOperationException("The streaming source did not stop within the configured drain limit.");
    }
}
