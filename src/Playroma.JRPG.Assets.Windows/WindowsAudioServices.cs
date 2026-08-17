using NAudio.Vorbis;
using NAudio.Wave;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Assets.Windows;

public interface IAudioPreviewSession : IAsyncDisposable
{
    TimeSpan Duration { get; }
    TimeSpan Position { get; set; }
    float Volume { get; set; }
    bool IsPlaying { get; }
    bool LoopEnabled { get; set; }
    TimeSpan? LoopStart { get; set; }
    TimeSpan? LoopEnd { get; set; }
    void Play();
    void Pause();
    void Stop();
    event EventHandler? PositionChanged;
    event EventHandler? PlaybackStopped;
}

public sealed class WindowsAudioWaveformProvider : IAudioWaveformProvider
{
    private readonly IAssetContentSource _content;

    public WindowsAudioWaveformProvider(IAssetContentSource content) =>
        _content = content ?? throw new ArgumentNullException(nameof(content));

    public bool CanHandle(AssetRecord asset, AssetVariant variant) =>
        asset.MediaKind == AssetMediaKind.Audio && !variant.IsEncrypted;

    public async ValueTask<WaveformEnvelope> CreateAsync(
        AssetRecord asset,
        AssetVariant variant,
        int bucketCount,
        CancellationToken cancellationToken = default)
    {
        bucketCount = Math.Clamp(bucketCount, 64, 50_000);
        string path = _content.GetFullPath(variant.RelativePath);
        if (variant.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return await AssetMetadataReaders.AnalyseWaveformAsync(path, bucketCount, cancellationToken).ConfigureAwait(false);
        }

        return await Task.Run(() => AnalyseWithNAudio(path, variant.Extension, bucketCount, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private static WaveformEnvelope AnalyseWithNAudio(
        string path,
        string extension,
        int bucketCount,
        CancellationToken cancellationToken)
    {
        using WaveStream reader = AudioReaderFactory.Open(path, extension);
        ISampleProvider samples = reader.ToSampleProvider();
        int channels = samples.WaveFormat.Channels;
        int sampleRate = samples.WaveFormat.SampleRate;
        long estimatedFrames = reader.TotalTime == TimeSpan.Zero
            ? bucketCount
            : Math.Max(1L, (long)Math.Ceiling(reader.TotalTime.TotalSeconds * sampleRate));
        int effective = checked((int)Math.Min(bucketCount, estimatedFrames));
        float[] minimum = Enumerable.Repeat(1f, effective).ToArray();
        float[] maximum = Enumerable.Repeat(-1f, effective).ToArray();
        float[] buffer = new float[Math.Max(4096, channels * 2048)];
        long frameIndex = 0;
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = read / Math.Max(1, channels);
            for (int frame = 0; frame < frames; frame++)
            {
                float sample = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    sample = Math.Max(sample, Math.Abs(buffer[frame * channels + channel]));
                }
                int bucket = checked((int)Math.Min(effective - 1, frameIndex * effective / Math.Max(1, estimatedFrames)));
                minimum[bucket] = Math.Min(minimum[bucket], -sample);
                maximum[bucket] = Math.Max(maximum[bucket], sample);
                frameIndex++;
            }
        }

        WaveformBucket[] result = new WaveformBucket[effective];
        for (int index = 0; index < effective; index++)
        {
            result[index] = minimum[index] > maximum[index]
                ? new WaveformBucket(0f, 0f)
                : new WaveformBucket(minimum[index], maximum[index]);
        }
        return new WaveformEnvelope(reader.TotalTime, sampleRate, channels, result);
    }
}

public sealed class WindowsAudioPreviewSession : IAudioPreviewSession
{
    private readonly WaveStream _reader;
    private readonly WaveOutEvent _output;
    private readonly System.Threading.Timer _timer;
    private bool _disposed;

    public WindowsAudioPreviewSession(string path, string extension)
    {
        _reader = AudioReaderFactory.Open(path, extension);
        try
        {
            _output = new WaveOutEvent();
            try
            {
                _output.Init(_reader);
                _output.PlaybackStopped += OnPlaybackStopped;
                _timer = new System.Threading.Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
            }
            catch
            {
                _output.Dispose();
                throw;
            }
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public event EventHandler? PositionChanged;
    public event EventHandler? PlaybackStopped;

    public TimeSpan Duration => _reader.TotalTime;
    public TimeSpan Position
    {
        get => _reader.CurrentTime;
        set => _reader.CurrentTime = Clamp(value, TimeSpan.Zero, Duration);
    }

    public float Volume
    {
        get => _output.Volume;
        set => _output.Volume = Math.Clamp(value, 0f, 1f);
    }

    public bool IsPlaying => _output.PlaybackState == PlaybackState.Playing;
    public bool LoopEnabled { get; set; }
    public TimeSpan? LoopStart { get; set; }
    public TimeSpan? LoopEnd { get; set; }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Position >= Duration) Position = LoopStart ?? TimeSpan.Zero;
        _output.Play();
        _timer.Change(50, 50);
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _output.Pause();
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _output.Stop();
        Position = TimeSpan.Zero;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _timer.Dispose();
        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Dispose();
        _reader.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnTimer(object? state)
    {
        if (_disposed) return;
        TimeSpan end = LoopEnd ?? Duration;
        if (LoopEnabled && Position >= end)
        {
            Position = LoopStart ?? TimeSpan.Zero;
        }
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum) =>
        value < minimum ? minimum : value > maximum ? maximum : value;
}

public static class AudioReaderFactory
{
    public static WaveStream Open(string path, string extension)
    {
        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)) return new VorbisWaveReader(path);
        if (extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)) return new MediaFoundationReader(path);
        return new AudioFileReader(path);
    }
}
