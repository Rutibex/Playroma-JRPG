using NAudio.Wave;
using PlayromaMusic.Audio.Engine;
using PlayromaMusic.Core.Model;

namespace Playroma.JRPG.Editor.WinForms.Music;

public sealed class WinFormsMusicPlaybackHost : IDisposable
{
    private readonly System.Windows.Forms.Timer _pumpTimer;
    private RealtimeAudioEngine? _engine;
    private WaveOutEvent? _output;
    private BufferedWaveProvider? _buffer;
    private ProjectDocument? _project;
    private bool _disposed;

    public WinFormsMusicPlaybackHost()
    {
        _pumpTimer = new System.Windows.Forms.Timer { Interval = 12 };
        _pumpTimer.Tick += (_, _) => Pump();
    }

    public event EventHandler? DiagnosticsChanged;
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public void Load(ProjectDocument project)
    {
        ArgumentNullException.ThrowIfNull(project);
        Stop();
        DisposeAudio();
        _project = project;
        _engine = new RealtimeAudioEngine(project.Audio);
        _engine.ReplaceSong(project, preservePosition: false);
        _buffer = new BufferedWaveProvider(new WaveFormat(project.Audio.SampleRate, 16, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(3),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        _output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
        _output.Init(_buffer);
        _pumpTimer.Start();
    }

    public void Play(ProjectDocument project)
    {
        if (!ReferenceEquals(_project, project) || _engine is null || _output is null || _buffer is null) Load(project);
        _engine!.ReplaceSong(project, preservePosition: false);
        _engine.SetLoop(project.Audio.LoopEnabled);
        _engine.SetMasterVolume(project.Audio.MasterVolume);
        foreach (SongTrack track in project.Song.Tracks) _engine.SetTrackMix(track);
        _engine.Play();
        _output!.Play();
    }

    public void Pause()
    {
        _engine?.Pause();
        _output?.Pause();
    }

    public void Stop()
    {
        _engine?.Stop();
        _output?.Stop();
        _buffer?.ClearBuffer();
    }

    public AudioEngineDiagnostics? GetDiagnostics() => _engine?.GetDiagnostics();

    private void Pump()
    {
        if (_engine is null || _buffer is null) return;
        bool submitted = false;
        while (_buffer.BufferedDuration < TimeSpan.FromSeconds(1.5) && _engine.TryDequeuePcmBlock(out PreparedAudioBlock? block))
        {
            if (block is null) break;
            if (block.Pcm16Stereo.Length > 0)
            {
                _buffer.AddSamples(block.Pcm16Stereo, 0, block.Pcm16Stereo.Length);
                _engine.ReportSubmittedBuffer();
                submitted = true;
            }
            if (block.EndOfStream && _project?.Audio.LoopEnabled != true) break;
        }
        if (!submitted && IsPlaying && _buffer.BufferedBytes == 0) _engine.ReportUnderrun();
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeAudio()
    {
        _engine?.Dispose();
        _engine = null;
        _output?.Dispose();
        _output = null;
        _buffer = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pumpTimer.Stop();
        _pumpTimer.Dispose();
        DisposeAudio();
    }
}
