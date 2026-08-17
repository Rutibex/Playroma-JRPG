using System.Collections.Concurrent;
using System.Diagnostics;
using PlayromaMusic.Audio.Backends;
using PlayromaMusic.Audio.Rendering;
using PlayromaMusic.Audio.Scheduling;
using PlayromaMusic.Core.Audio;

namespace PlayromaMusic.Audio.Engine;

internal sealed class AudioRenderWorker : IDisposable
{
    private readonly ConcurrentQueue<AudioCommand> _commands = new();
    private readonly ConcurrentQueue<PreparedAudioBlock> _prepared = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _diagnosticsLock = new();
    private readonly Thread _thread;

    private AudioProjectSettings _settings;
    private ISynthBackend _backend;
    private SnapshotSynthesizer _player;
    private SongPlaybackSnapshot? _snapshot;
    private AudioTransportState _transportState;
    private bool _shutdown;
    private int _queueHighWater;
    private long _renderedBuffers;
    private long _submittedBuffers;
    private long _underruns;
    private double _lastRenderMilliseconds;
    private double _peakLevel;
    private string _lastError = string.Empty;
    private bool _endOfStreamQueued;

    public AudioRenderWorker(AudioProjectSettings settings, string? fluidSynthLibraryPath)
    {
        _settings = CloneSettings(settings);
        SynthBackendCreationResult creation = SynthBackendFactory.Create(_settings, fluidSynthLibraryPath);
        _backend = creation.Backend;
        _player = new SnapshotSynthesizer(_backend)
        {
            LoopEnabled = _settings.LoopEnabled
        };
        _lastError = creation.UsedFallback ? creation.Message : string.Empty;

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Procedural MIDI audio render",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public int SampleRate => _settings.SampleRate;
    public int BufferFrames => _settings.BufferFrames;
    public int QueueTarget => _settings.QueuedBufferTarget;

    public void Enqueue(AudioCommand command)
    {
        _commands.Enqueue(command);
        _wake.Set();
    }

    public bool TryDequeue(out PreparedAudioBlock? block) => _prepared.TryDequeue(out block);

    public AudioEngineDiagnostics GetDiagnostics()
    {
        lock (_diagnosticsLock)
        {
            return new AudioEngineDiagnostics(
                _transportState,
                _backend.DisplayName,
                _backend.StatusMessage,
                _backend.IsReady,
                _player.PositionSamples,
                _player.LengthSamples,
                _settings.SampleRate,
                _prepared.Count,
                _queueHighWater,
                _renderedBuffers,
                _submittedBuffers,
                _underruns,
                _lastRenderMilliseconds,
                _peakLevel,
                _lastError);
        }
    }

    public void Dispose()
    {
        if (_shutdown)
        {
            return;
        }

        Enqueue(new ShutdownCommand());
        if (!_thread.Join(TimeSpan.FromSeconds(3)))
        {
            _shutdown = true;
        }

        _wake.Dispose();
    }

    private void ThreadMain()
    {
        try
        {
            while (!_shutdown)
            {
                ProcessCommands();
                if (_shutdown)
                {
                    break;
                }

                if (_transportState == AudioTransportState.Playing && !_endOfStreamQueued && _prepared.Count < _settings.QueuedBufferTarget)
                {
                    RenderOneBlock();
                    continue;
                }

                _wake.WaitOne(4);
            }
        }
        catch (Exception ex)
        {
            lock (_diagnosticsLock)
            {
                _lastError = ex.ToString();
                _transportState = AudioTransportState.Paused;
            }
        }
        finally
        {
            _backend.Dispose();
        }
    }

    private void ProcessCommands()
    {
        while (_commands.TryDequeue(out AudioCommand? command))
        {
            try
            {
                switch (command)
                {
                    case PlayCommand:
                        if (_snapshot is not null)
                        {
                            if (_player.PositionSamples >= _player.LengthSamples)
                            {
                                _player.Seek(0);
                            }

                            _endOfStreamQueued = false;
                            _transportState = AudioTransportState.Playing;
                        }
                        break;
                    case PauseCommand:
                        _transportState = AudioTransportState.Paused;
                        break;
                    case StopCommand:
                        _transportState = AudioTransportState.Stopped;
                        _endOfStreamQueued = false;
                        ClearPrepared();
                        _player.Stop();
                        break;
                    case SeekCommand seek:
                        _endOfStreamQueued = false;
                        ClearPrepared();
                        _player.Seek(seek.Sample);
                        break;
                    case ReplaceSnapshotCommand replace:
                    {
                        long target = replace.AuditionFromSample ??
                                      (replace.PreservePosition ? _player.PositionSamples : 0);
                        _snapshot = replace.Snapshot;
                        _endOfStreamQueued = false;
                        ClearPrepared();
                        _player.SetSnapshot(replace.Snapshot, target);
                        if (replace.AuditionFromSample.HasValue)
                        {
                            _transportState = AudioTransportState.Playing;
                        }
                        break;
                    }
                    case SetLoopCommand loop:
                        _settings.LoopEnabled = loop.Enabled;
                        _player.LoopEnabled = loop.Enabled;
                        break;
                    case SetMasterVolumeCommand volume:
                        _settings.MasterVolume = Math.Clamp(volume.Volume, 0.0, 1.5);
                        break;
                    case SetTrackMixCommand mix:
                        _endOfStreamQueued = false;
                        ClearPrepared();
                        _player.UpdateTrack(mix.TrackId, mix.Muted, mix.Solo, mix.Volume, mix.Pan);
                        break;
                    case RecreateBackendCommand recreate:
                        RecreateBackend(recreate.Settings, recreate.FluidSynthLibraryPath);
                        break;
                    case ReportSubmittedCommand:
                        _submittedBuffers++;
                        break;
                    case ReportUnderrunCommand:
                        _underruns++;
                        break;
                    case ShutdownCommand:
                        _shutdown = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }
    }

    private void RenderOneBlock()
    {
        int frames = _settings.BufferFrames;
        float[] samples = new float[frames * 2];
        byte[] pcm = new byte[frames * 2 * sizeof(short)];
        long startSample = _player.PositionSamples;

        Stopwatch stopwatch = Stopwatch.StartNew();
        bool active = _player.Render(samples, frames);
        double peak = Pcm16Converter.ConvertInterleavedFloatToPcm16(samples, pcm, _settings.MasterVolume);
        stopwatch.Stop();

        bool endOfStream = !active &&
                           _player.PositionSamples >= _player.LengthSamples &&
                           !_player.LoopEnabled;
        _endOfStreamQueued = endOfStream;

        _prepared.Enqueue(new PreparedAudioBlock(pcm, startSample, frames, peak, endOfStream));
        _renderedBuffers++;
        _queueHighWater = Math.Max(_queueHighWater, _prepared.Count);
        _lastRenderMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        _peakLevel = peak;
    }

    private void RecreateBackend(AudioProjectSettings settings, string? fluidSynthLibraryPath)
    {
        AudioTransportState previousState = _transportState;
        long previousPosition = _player.PositionSamples;
        _transportState = AudioTransportState.Paused;
        _endOfStreamQueued = false;
        ClearPrepared();

        _backend.AllNotesOff();
        _backend.Dispose();
        _settings = CloneSettings(settings);
        SynthBackendCreationResult creation = SynthBackendFactory.Create(_settings, fluidSynthLibraryPath);
        _backend = creation.Backend;
        _player = new SnapshotSynthesizer(_backend)
        {
            LoopEnabled = _settings.LoopEnabled
        };
        if (_snapshot is not null && _snapshot.SampleRate == _settings.SampleRate)
        {
            _player.SetSnapshot(_snapshot, previousPosition);
        }

        _lastError = creation.UsedFallback ? creation.Message : string.Empty;
        _transportState = previousState;
    }

    private void ClearPrepared()
    {
        while (_prepared.TryDequeue(out _))
        {
        }
    }

    private static AudioProjectSettings CloneSettings(AudioProjectSettings settings) => new()
    {
        PreferredBackend = settings.PreferredBackend,
        SoundFontPath = settings.SoundFontPath,
        SampleRate = settings.SampleRate,
        BufferFrames = settings.BufferFrames,
        QueuedBufferTarget = settings.QueuedBufferTarget,
        MasterVolume = settings.MasterVolume,
        LoopEnabled = settings.LoopEnabled,
        RenderTailSeconds = settings.RenderTailSeconds,
        AutoAuditionAfterEdit = settings.AutoAuditionAfterEdit
    };
}
