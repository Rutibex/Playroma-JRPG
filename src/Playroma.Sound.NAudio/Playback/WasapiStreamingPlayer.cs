using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Playroma.Sound.Analysis.Realtime;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.IO;
using Playroma.Sound.NAudio.Buffering;
using Playroma.Sound.NAudio.Devices;
using Playroma.Sound.NAudio.Recording;
using Playroma.Sound.Streaming.Runtime;

namespace Playroma.Sound.NAudio.Playback;

/// <summary>
/// Windows WASAPI host for any <see cref="IStreamingAudioSource"/>. Playroma rendering occurs on a
/// dedicated producer thread and never inside the NAudio callback. The callback consumes a bounded
/// float ring through <see cref="PlayromaSampleProvider"/> and emits silence when starved.
/// </summary>
public sealed class WasapiStreamingPlayer : IDisposable
{
    private readonly object _gate = new();
    private readonly IStreamingAudioSource _source;
    private readonly WasapiPlaybackOptions _options;
    private readonly AudioDeviceCatalog _deviceCatalog;
    private readonly PlaybackRingBuffer _ring;
    private readonly float[] _renderBuffer;
    private readonly PlaybackTelemetry _telemetry = new();
    private AudioDeviceSelection _selection;
    private CancellationTokenSource? _producerCancellation;
    private Thread? _producerThread;
    private WasapiOut? _output;
    private MMDevice? _device;
    private PlayromaSampleProvider? _provider;
    private MasterRecorder? _recorder;
    private AudioAnalysisTap? _analysisTap;
    private int _outputSampleRate;
    private int _outputChannels;
    private float _masterGain = 1f;
    private int _suppressPlaybackStopped;
    private int _recoveryScheduled;
    private int _disposed;

    public WasapiStreamingPlayer(
        IStreamingAudioSource source,
        WasapiPlaybackOptions? options = null,
        AudioDeviceCatalog? deviceCatalog = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? new WasapiPlaybackOptions();
        _options.Validate();
        _deviceCatalog = deviceCatalog ?? new AudioDeviceCatalog();
        _selection = _options.Device;
        int capacityFrames = Math.Max(
            source.Format.BlockFrames * 2,
            checked((int)Math.Ceiling(source.Format.SampleRate * (_options.BufferMilliseconds / 1_000.0))));
        _ring = new PlaybackRingBuffer(source.Format.Channels, capacityFrames);
        _renderBuffer = new float[source.Format.SamplesPerBlock];
        _telemetry.ConfigureSource(source.Format.SampleRate, source.Format.Channels, _ring.CapacityFrames, () => _ring.AvailableReadFrames, () => _ring.FillRatio);
    }

    public event EventHandler<PlaybackHostFaultEventArgs>? Faulted;
    public event EventHandler<PlaybackDeviceEventArgs>? DeviceRecovered;
    public event EventHandler<PlaybackDeviceEventArgs>? DeviceChanged;

    public IStreamingAudioSource Source => _source;
    public PlaybackTelemetry Telemetry => _telemetry;
    public PlaybackTelemetrySnapshot TelemetrySnapshot => _telemetry.Capture();
    public WasapiPlaybackHostState State => TelemetrySnapshot.State;
    public bool IsPlaying => State == WasapiPlaybackHostState.Playing;
    public bool IsRecording => _recorder?.IsRecording == true;
    public AudioDeviceSelection DeviceSelection => Volatile.Read(ref _selection);

    public float MasterGain
    {
        get => Volatile.Read(ref _masterGain);
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > 8f) throw new ArgumentOutOfRangeException(nameof(value));
            Volatile.Write(ref _masterGain, value);
            PlayromaSampleProvider? provider = Volatile.Read(ref _provider);
            if (provider is not null) provider.MasterGain = value;
        }
    }

    public void Start()
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_telemetry.Capture().State is WasapiPlaybackHostState.Playing or WasapiPlaybackHostState.Starting) return;
            _telemetry.SetState(WasapiPlaybackHostState.Starting);
            _telemetry.ClearError();
            _ring.Clear();
            try
            {
                CreateOutput(_selection, preserveOutputFormat: false);
                if (_options.StartSourceAutomatically && !_source.IsRunning) _source.Start();
                StartProducer();
            }
            catch
            {
                _telemetry.SetState(WasapiPlaybackHostState.Faulted);
                DisposeOutput();
                throw;
            }
        }

        WaitForPrebuffer();
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_producerThread?.IsAlive != true || _telemetry.Capture().State == WasapiPlaybackHostState.Faulted)
            {
                var exception = new InvalidOperationException("The Playroma render producer failed before WASAPI playback could start.");
                _telemetry.SetError(exception);
                _telemetry.SetState(WasapiPlaybackHostState.Faulted);
                StopProducer();
                DisposeOutput();
                throw exception;
            }
            try
            {
                _output!.Play();
                _telemetry.SetState(WasapiPlaybackHostState.Playing);
            }
            catch (Exception exception)
            {
                _telemetry.SetError(exception);
                _telemetry.SetState(WasapiPlaybackHostState.Faulted);
                StopProducer();
                DisposeOutput();
                throw;
            }
        }
    }

    public void Stop()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return;
        WasapiOut? output;
        Thread? producer;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_telemetry.Capture().State == WasapiPlaybackHostState.Stopped && _output is null && _producerThread is null) return;
            _telemetry.SetState(WasapiPlaybackHostState.Stopped);
            Volatile.Write(ref _suppressPlaybackStopped, 1);
            output = _output;
            cancellation = _producerCancellation;
            producer = _producerThread;
            cancellation?.Cancel();
        }

        try
        {
            output?.Stop();
        }
        catch (Exception exception)
        {
            _telemetry.SetError(exception);
        }
        producer?.Join();

        lock (_gate) _recorder?.Stop();
        StopSourceAtBoundary();

        lock (_gate)
        {
            _producerThread = null;
            _producerCancellation?.Dispose();
            _producerCancellation = null;
            DisposeOutput();
            _ring.Clear();
            Volatile.Write(ref _suppressPlaybackStopped, 0);
            Volatile.Write(ref _recoveryScheduled, 0);
            _telemetry.SetRecoveryAttempt(0);
        }
    }

    public void ChangeDevice(AudioDeviceSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ThrowIfDisposed();
        WasapiOut? previousOutput;
        AudioDeviceSelection previousSelection;
        bool resume;
        lock (_gate)
        {
            if (_recorder?.IsRecording == true) throw new InvalidOperationException("Stop master recording before changing output devices.");
            resume = _telemetry.Capture().State == WasapiPlaybackHostState.Playing;
            previousSelection = _selection;
            if (!resume)
            {
                Volatile.Write(ref _selection, selection);
                return;
            }
            _telemetry.SetState(WasapiPlaybackHostState.Starting);
            Volatile.Write(ref _suppressPlaybackStopped, 1);
            previousOutput = _output;
        }

        try
        {
            previousOutput?.Stop();
            lock (_gate)
            {
                DisposeOutput();
                CreateOutput(selection, preserveOutputFormat: true);
                _output!.Play();
                Volatile.Write(ref _selection, selection);
                _telemetry.ClearError();
                _telemetry.SetState(WasapiPlaybackHostState.Playing);
                Volatile.Write(ref _suppressPlaybackStopped, 0);
            }
            RaiseDeviceEvent(DeviceChanged);
        }
        catch (Exception changeFailure)
        {
            Exception failure = changeFailure;
            bool restored = false;
            try
            {
                lock (_gate)
                {
                    DisposeOutput();
                    CreateOutput(previousSelection, preserveOutputFormat: true);
                    _output!.Play();
                    Volatile.Write(ref _selection, previousSelection);
                    _telemetry.SetState(WasapiPlaybackHostState.Playing);
                    Volatile.Write(ref _suppressPlaybackStopped, 0);
                    restored = true;
                }
            }
            catch (Exception restoreFailure)
            {
                failure = new AggregateException("The requested device failed and the previous output device could not be restored.", changeFailure, restoreFailure);
                lock (_gate)
                {
                    DisposeOutput();
                    Volatile.Write(ref _selection, selection);
                    Volatile.Write(ref _suppressPlaybackStopped, 0);
                    _telemetry.SetError(failure);
                    bool recover = _options.AutoRecoverDevice && _options.MaximumRecoveryAttempts > 0;
                    _telemetry.SetState(recover ? WasapiPlaybackHostState.Recovering : WasapiPlaybackHostState.Faulted);
                    if (recover && Interlocked.CompareExchange(ref _recoveryScheduled, 1, 0) == 0)
                        ThreadPool.QueueUserWorkItem(_ => RecoverDevice());
                }
            }

            _telemetry.SetError(failure);
            SafeRaiseFault(failure, recoveryWillBeAttempted: !restored && _options.AutoRecoverDevice && _options.MaximumRecoveryAttempts > 0);
            throw failure;
        }
    }

    public AudioAnalysisTap AttachAnalysis(AudioAnalysisOptions? options = null)
    {
        ThrowIfDisposed();
        AudioAnalysisTap? previous;
        AudioAnalysisTap replacement;
        lock (_gate)
        {
            int sampleRate = _outputSampleRate != 0 ? _outputSampleRate : _options.OutputSampleRate ?? _source.Format.SampleRate;
            int channels = _outputChannels != 0 ? _outputChannels : _options.OutputChannels ?? _source.Format.Channels;
            replacement = new AudioAnalysisTap(new AudioFormat(sampleRate, channels, _source.Format.BlockFrames), options);
            previous = _analysisTap;
            _analysisTap = replacement;
            _provider?.SetAnalysisTap(replacement);
        }

        // Dispose only after the provider no longer references the previous tap.
        // This avoids racing the WASAPI callback with a disposed analysis worker.
        previous?.Dispose();
        return replacement;
    }

    public void DetachAnalysis()
    {
        AudioAnalysisTap? tap;
        lock (_gate)
        {
            _provider?.SetAnalysisTap(null);
            tap = _analysisTap;
            _analysisTap = null;
        }
        tap?.Dispose();
    }

    public void StartRecording(string path, WavEncoding encoding = WavEncoding.Pcm16)
    {
        ThrowIfDisposed();
        lock (_gate)
        {
            if (_recorder is null) throw new InvalidOperationException("Start playback before starting a master recording.");
            _recorder.Start(path, encoding);
        }
    }

    public void StopRecording()
    {
        lock (_gate) _recorder?.Stop();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopAfterDisposeFlag();
        _telemetry.SetState(WasapiPlaybackHostState.Disposed);
    }

    private void StopAfterDisposeFlag()
    {
        WasapiOut? output;
        Thread? producer;
        lock (_gate)
        {
            Volatile.Write(ref _suppressPlaybackStopped, 1);
            _producerCancellation?.Cancel();
            output = _output;
            producer = _producerThread;
        }
        try { output?.Stop(); } catch { }
        producer?.Join();
        StopSourceAtBoundary();
        lock (_gate)
        {
            _recorder?.Dispose();
            _recorder = null;
            _analysisTap?.Dispose();
            _analysisTap = null;
            _producerCancellation?.Dispose();
            _producerCancellation = null;
            _producerThread = null;
            DisposeOutput();
        }
    }

    private void StartProducer()
    {
        if (_producerThread is not null) throw new InvalidOperationException("The playback producer is already running.");
        _producerCancellation = new CancellationTokenSource();
        _producerThread = new Thread(() => ProducerLoop(_producerCancellation.Token))
        {
            IsBackground = true,
            Name = "Playroma Sound render producer",
            Priority = ThreadPriority.AboveNormal
        };
        _producerThread.Start();
    }

    private void StopProducer()
    {
        _producerCancellation?.Cancel();
        _producerThread?.Join();
        _producerThread = null;
        _producerCancellation?.Dispose();
        _producerCancellation = null;
    }

    private void ProducerLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int requestedFrames = _source.Format.BlockFrames;
                if (_ring.AvailableWriteFrames < requestedFrames)
                {
                    if (_source is StreamingSceneHost fullHost) fullHost.DrainRetiredInstances();
                    IdleProducer();
                    continue;
                }

                long started = Stopwatch.GetTimestamp();
                int rendered = _source.Render(_renderBuffer, requestedFrames);
                if (rendered < 0 || rendered > requestedFrames) throw new InvalidOperationException("The streaming source returned an invalid frame count.");
                int samples = checked(rendered * _source.Format.Channels);
                long nonFinite = 0;
                for (int i = 0; i < samples; i++)
                {
                    if (float.IsFinite(_renderBuffer[i])) continue;
                    _renderBuffer[i] = 0f;
                    nonFinite++;
                }
                if (nonFinite > 0) _telemetry.AddNonFiniteSamples(nonFinite);

                int written = _ring.Write(_renderBuffer, rendered);
                if (written < rendered) _telemetry.AddProducerOverrun(rendered - written);
                _telemetry.AddSourceFrames(written);
                _telemetry.RecordProducerDuration(Stopwatch.GetTimestamp() - started);
                if (_source is StreamingSceneHost host) host.DrainRetiredInstances();
                if (rendered == 0) IdleProducer();
            }
        }
        catch (Exception exception)
        {
            _telemetry.SetError(exception);
            _telemetry.SetState(WasapiPlaybackHostState.Faulted);
            SafeRaiseFault(exception, recoveryWillBeAttempted: false);
        }
    }

    private void CreateOutput(AudioDeviceSelection selection, bool preserveOutputFormat)
    {
        MMDevice device = _deviceCatalog.OpenDevice(selection);
        try
        {
            using AudioClient audioClient = device.AudioClient;
            WaveFormat mix = audioClient.MixFormat;
            int sampleRate = preserveOutputFormat && _outputSampleRate != 0
                ? _outputSampleRate
                : _options.OutputSampleRate ?? mix.SampleRate;
            int channels = preserveOutputFormat && _outputChannels != 0
                ? _outputChannels
                : _options.OutputChannels ?? Math.Max(1, Math.Min(2, mix.Channels));

            var provider = new PlayromaSampleProvider(_ring, _source.Format, sampleRate, channels, _telemetry)
            {
                MasterGain = Volatile.Read(ref _masterGain)
            };
            if (_recorder is null || _recorder.Format.SampleRate != sampleRate || _recorder.Format.Channels != channels)
            {
                if (_recorder?.IsRecording == true) throw new InvalidOperationException("The logical output format cannot change while master recording is active.");
                _recorder?.Dispose();
                _recorder = new MasterRecorder(new AudioFormat(sampleRate, channels, _source.Format.BlockFrames), _options.RecordingBufferMilliseconds, _telemetry);
            }
            provider.SetRecorder(_recorder);
            provider.SetAnalysisTap(_analysisTap);

            var output = new WasapiOut(
                device,
                _options.ShareMode == WasapiPlaybackShareMode.Shared ? AudioClientShareMode.Shared : AudioClientShareMode.Exclusive,
                _options.UseEventSynchronization,
                _options.LatencyMilliseconds);
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(provider);

            _device = device;
            _provider = provider;
            _output = output;
            _outputSampleRate = sampleRate;
            _outputChannels = channels;
            _telemetry.ConfigureOutput(device.ID, device.FriendlyName, sampleRate, channels);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    private void DisposeOutput()
    {
        WasapiOut? output = _output;
        _output = null;
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
        }
        _provider = null;
        _device?.Dispose();
        _device = null;
    }

    private void WaitForPrebuffer()
    {
        int target = Math.Min(_ring.CapacityFrames - 1, checked((int)Math.Ceiling(_source.Format.SampleRate * (_options.PrebufferMilliseconds / 1_000.0))));
        if (target <= 0) return;
        long deadline = Environment.TickCount64 + Math.Max(250, (_options.PrebufferMilliseconds * 4) + _options.LatencyMilliseconds);
        while (_ring.AvailableReadFrames < target && Environment.TickCount64 < deadline)
        {
            if (_producerThread?.IsAlive != true) break;
            Thread.Sleep(1);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _suppressPlaybackStopped) != 0 || Volatile.Read(ref _disposed) != 0) return;
        Exception? exception = eventArgs.Exception;
        if (exception is null)
        {
            _telemetry.SetState(WasapiPlaybackHostState.Stopped);
            ThreadPool.QueueUserWorkItem(_ => Stop());
            return;
        }

        _telemetry.AddDeviceFailure();
        _telemetry.SetError(exception);
        bool recover = _options.AutoRecoverDevice && _options.MaximumRecoveryAttempts > 0;
        _telemetry.SetState(recover ? WasapiPlaybackHostState.Recovering : WasapiPlaybackHostState.Faulted);
        SafeRaiseFault(exception, recover);
        if (recover && Interlocked.CompareExchange(ref _recoveryScheduled, 1, 0) == 0)
            ThreadPool.QueueUserWorkItem(_ => RecoverDevice());
    }

    private void RecoverDevice()
    {
        Exception? lastFailure = null;
        for (int attempt = 1; attempt <= _options.MaximumRecoveryAttempts; attempt++)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _telemetry.SetRecoveryAttempt(attempt);
            if (_options.DeviceRecoveryDelayMilliseconds > 0) Thread.Sleep(_options.DeviceRecoveryDelayMilliseconds);
            try
            {
                lock (_gate)
                {
                    if (_telemetry.Capture().State == WasapiPlaybackHostState.Stopped || Volatile.Read(ref _disposed) != 0) return;
                    Volatile.Write(ref _suppressPlaybackStopped, 1);
                    DisposeOutput();
                    AudioDeviceSelection recoverySelection = _selection;
                    try
                    {
                        CreateOutput(recoverySelection, preserveOutputFormat: true);
                    }
                    catch when (_options.FallBackToDefaultDevice && !recoverySelection.UsesDefaultDevice)
                    {
                        recoverySelection = AudioDeviceSelection.Default();
                        Volatile.Write(ref _selection, recoverySelection);
                        CreateOutput(recoverySelection, preserveOutputFormat: true);
                    }
                    _output!.Play();
                    _telemetry.AddDeviceRecovery();
                    _telemetry.SetRecoveryAttempt(0);
                    _telemetry.ClearError();
                    _telemetry.SetState(WasapiPlaybackHostState.Playing);
                    Volatile.Write(ref _suppressPlaybackStopped, 0);
                    Volatile.Write(ref _recoveryScheduled, 0);
                    RaiseDeviceEvent(DeviceRecovered);
                    return;
                }
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                _telemetry.SetError(exception);
            }
        }

        Volatile.Write(ref _recoveryScheduled, 0);
        Volatile.Write(ref _suppressPlaybackStopped, 0);
        _telemetry.SetState(WasapiPlaybackHostState.Faulted);
        if (lastFailure is not null) SafeRaiseFault(lastFailure, recoveryWillBeAttempted: false);
    }

    private void RaiseDeviceEvent(EventHandler<PlaybackDeviceEventArgs>? handler)
    {
        if (handler is null) return;
        PlaybackTelemetrySnapshot snapshot = _telemetry.Capture();
        var eventArgs = new PlaybackDeviceEventArgs(snapshot.DeviceId, snapshot.DeviceName);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { handler(this, eventArgs); } catch { }
        });
    }

    private void SafeRaiseFault(Exception exception, bool recoveryWillBeAttempted)
    {
        EventHandler<PlaybackHostFaultEventArgs>? handler = Faulted;
        if (handler is null) return;
        var eventArgs = new PlaybackHostFaultEventArgs(exception, recoveryWillBeAttempted);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { handler(this, eventArgs); } catch { }
        });
    }

    private void StopSourceAtBoundary()
    {
        if (!_options.StopSourceAutomatically || !_source.IsRunning) return;
        try
        {
            StreamingSourceLifecycle.StopAtBoundary(_source, _renderBuffer, _options.SourceStopDrainMilliseconds);
        }
        catch (Exception exception)
        {
            _telemetry.SetError(exception);
        }
    }

    private void IdleProducer()
    {
        if (_options.ProducerIdleSleepMilliseconds == 0) Thread.Yield();
        else Thread.Sleep(_options.ProducerIdleSleepMilliseconds);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
