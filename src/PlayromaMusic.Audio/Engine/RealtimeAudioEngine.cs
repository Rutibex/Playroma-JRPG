using PlayromaMusic.Audio.Scheduling;
using PlayromaMusic.Core.Audio;
using PlayromaMusic.Core.Model;

namespace PlayromaMusic.Audio.Engine;

public sealed class RealtimeAudioEngine : IDisposable
{
    private readonly AudioRenderWorker _worker;
    private bool _disposed;

    public RealtimeAudioEngine(AudioProjectSettings settings, string? fluidSynthLibraryPath = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _worker = new AudioRenderWorker(settings, fluidSynthLibraryPath);
    }

    public int SampleRate => _worker.SampleRate;
    public int BufferFrames => _worker.BufferFrames;
    public int QueueTarget => _worker.QueueTarget;

    public void ReplaceSong(ProjectDocument project, bool preservePosition = true, long? auditionFromTick = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(project);
        SongPlaybackSnapshot snapshot = SongPlaybackSnapshot.Create(
            project.Song,
            project.Audio.SampleRate,
            project.Audio.RenderTailSeconds);
        long? auditionSample = auditionFromTick.HasValue
            ? snapshot.TickToSample(auditionFromTick.Value)
            : null;
        _worker.Enqueue(new ReplaceSnapshotCommand(snapshot, preservePosition, auditionSample));
    }

    public void Play() => _worker.Enqueue(new PlayCommand());
    public void Pause() => _worker.Enqueue(new PauseCommand());
    public void Stop() => _worker.Enqueue(new StopCommand());
    public void SeekSamples(long sample) => _worker.Enqueue(new SeekCommand(Math.Max(0, sample)));
    public void SeekTicks(long tick, SongPlaybackSnapshot snapshot) => SeekSamples(snapshot.TickToSample(tick));
    public void SetLoop(bool enabled) => _worker.Enqueue(new SetLoopCommand(enabled));
    public void SetMasterVolume(double volume) => _worker.Enqueue(new SetMasterVolumeCommand(volume));

    public void SetTrackMix(SongTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        _worker.Enqueue(new SetTrackMixCommand(track.Id, track.Muted, track.Solo, track.Volume, track.Pan));
    }

    public void RecreateBackend(AudioProjectSettings settings, string? fluidSynthLibraryPath = null) =>
        _worker.Enqueue(new RecreateBackendCommand(settings, fluidSynthLibraryPath));

    public bool TryDequeuePcmBlock(out PreparedAudioBlock? block) => _worker.TryDequeue(out block);
    public void ReportSubmittedBuffer() => _worker.Enqueue(new ReportSubmittedCommand());
    public void ReportUnderrun() => _worker.Enqueue(new ReportUnderrunCommand());
    public AudioEngineDiagnostics GetDiagnostics() => _worker.GetDiagnostics();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _worker.Dispose();
    }
}
