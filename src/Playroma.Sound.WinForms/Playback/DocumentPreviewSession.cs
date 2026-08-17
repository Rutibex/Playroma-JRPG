using Playroma.Sound.Analysis.Profiling;
using Playroma.Sound.Analysis.Realtime;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Workspace;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.NAudio.Devices;
using Playroma.Sound.NAudio.Playback;
using Playroma.Sound.Graph.Compilation;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Validation;
using Playroma.Sound.Streaming.Compilation;
using Playroma.Sound.Streaming.Runtime;

namespace Playroma.Sound.WinForms.Playback;

public sealed class PreviewCompilationException : Exception
{
    public PreviewCompilationException(AuthoringCompilationResult result)
        : base(CreateMessage(result)) => Result = result;

    public AuthoringCompilationResult Result { get; }

    private static string CreateMessage(AuthoringCompilationResult result)
    {
        string diagnostics = string.Join(Environment.NewLine, result.Validation.Diagnostics.Select(item => $"{item.Code}: {item.Message}"));
        return string.IsNullOrWhiteSpace(diagnostics)
            ? result.Exception?.Message ?? "The document could not be compiled for preview."
            : diagnostics;
    }
}

/// <summary>Compiles authoring documents and owns exactly one production WASAPI preview host.</summary>
public sealed class DocumentPreviewSession : IDisposable
{
    private readonly object _gate = new();
    private WasapiStreamingPlayer? _player;
    private IStreamingAudioSource? _source;
    private StreamingSceneController? _sceneController;
    private IAuthoringDocument? _activeDocument;
    private AudioAnalysisTap? _masterAnalysis;
    private MultiBusAnalysisTap? _busAnalysis;
    private readonly RealtimeProfiler _profiler = new();
    private long _lastAllocatedBytes = GC.GetTotalAllocatedBytes(false);
    private bool _disposed;
    private float _masterGain = 1f;
    private AudioDeviceSelection _deviceSelection = AudioDeviceSelection.Default();

    public event EventHandler? StateChanged;
    public event EventHandler<PlaybackHostFaultEventArgs>? Faulted;

    public string ActiveDocumentId
    {
        get { lock (_gate) return _activeDocument?.DocumentId ?? ""; }
    }

    public bool IsPreviewing(IAuthoringDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate) return ReferenceEquals(_activeDocument, document);
    }

    public bool IsPlaying => Volatile.Read(ref _player)?.IsPlaying == true;
    public StreamingSceneController? SceneController { get { lock (_gate) return _sceneController; } }
    public bool IsStreamingScenePreview => SceneController is not null;
    public PlaybackTelemetrySnapshot Telemetry => Volatile.Read(ref _player)?.TelemetrySnapshot ?? PlaybackTelemetrySnapshot.Empty;
    public AudioAnalysisSnapshot MasterAnalysis => Volatile.Read(ref _masterAnalysis)?.Snapshot ?? AudioAnalysisSnapshot.Empty(new AudioFormat(48_000, 2, 512));
    public MultiBusAnalysisTap? BusAnalysis => Volatile.Read(ref _busAnalysis);
    public RealtimeProfiler Profiler => _profiler;

    public void CaptureProfilerSample()
    {
        PlaybackTelemetrySnapshot playback = Telemetry;
        var scene = SceneController?.Telemetry;
        long allocations = GC.GetTotalAllocatedBytes(false);
        long delta = Math.Max(0, allocations - Interlocked.Exchange(ref _lastAllocatedBytes, allocations));
        _profiler.Add(new RealtimeProfileSample(DateTimeOffset.UtcNow, playback.LastProducerRenderMilliseconds, playback.LastProviderCallbackMilliseconds,
            scene?.RenderDeadlineMilliseconds ?? 0, scene?.LastRenderLoad ?? 0, playback.RingFillRatio, scene?.ActiveVoices ?? 0, scene?.VirtualVoices ?? 0,
            scene?.CommandQueueCount ?? 0, playback.UnderrunFrames, playback.ProducerOverrunFrames, delta, playback.State.ToString()));
    }

    public float MasterGain
    {
        get => _masterGain;
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > 2f) throw new ArgumentOutOfRangeException(nameof(value));
            _masterGain = value;
            WasapiStreamingPlayer? player = Volatile.Read(ref _player);
            if (player is not null) player.MasterGain = value;
        }
    }

    public AudioDeviceSelection DeviceSelection
    {
        get => _deviceSelection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _deviceSelection = value;
            WasapiStreamingPlayer? player = Volatile.Read(ref _player);
            if (player is not null) player.ChangeDevice(value);
        }
    }

    public async Task<AuthoringCompilationResult> PlayAsync(
        AuthoringWorkspace workspace,
        IAuthoringDocument document,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(document);

        Stop();
        var format = new AudioFormat(48_000, 2, 512);
        AuthoringCompilationResult result = await document.CompileAsync(workspace.CreateCompilationContext(format), cancellationToken).ConfigureAwait(true);
        if (!result.Succeeded || result.Artifact is null) throw new PreviewCompilationException(result);

        await StartArtifactAsync(result.Artifact, document, cancellationToken).ConfigureAwait(true);
        return result;
    }

    public async Task<AuthoringCompilationResult> PlayGraphAsync(
        AuthoringWorkspace workspace,
        IAuthoringDocument owner,
        SoundGraphDefinition graph,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(graph);
        Stop();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var context = workspace.CreateCompilationContext(new AudioFormat(48_000, 2, 512));
        GraphValidationReport validation = new SoundGraphCompiler(context.GraphRegistry).Validate(graph);
        var diagnostics = validation.Diagnostics.Select(item => new Playroma.Sound.Authoring.Diagnostics.AuthoringDiagnostic(
            item.Code,
            item.Severity == GraphDiagnosticSeverity.Error ? Playroma.Sound.Authoring.Diagnostics.AuthoringDiagnosticSeverity.Error : item.Severity == GraphDiagnosticSeverity.Warning ? Playroma.Sound.Authoring.Diagnostics.AuthoringDiagnosticSeverity.Warning : Playroma.Sound.Authoring.Diagnostics.AuthoringDiagnosticSeverity.Information,
            item.Message, owner.DocumentId, item.NodeId ?? "", item.PortId ?? "", owner.FilePath));
        var summary = new Playroma.Sound.Authoring.Diagnostics.ValidationSummary(diagnostics);
        if (summary.HasErrors)
        {
            var failure = new AuthoringCompilationResult(owner.DocumentId, owner.Revision, AuthoringCompilationState.Failed, summary, null, timer.Elapsed);
            throw new PreviewCompilationException(failure);
        }
        CompiledSoundGraph artifact = await Task.Run(() => new SoundGraphCompiler(context.GraphRegistry).Compile(graph, context.Format, context.GraphOptions), cancellationToken).ConfigureAwait(true);
        var result = new AuthoringCompilationResult(owner.DocumentId, owner.Revision, AuthoringCompilationState.Succeeded, summary, artifact, timer.Elapsed);
        await StartArtifactAsync(artifact, owner, cancellationToken).ConfigureAwait(true);
        return result;
    }

    private async Task StartArtifactAsync(object artifact, IAuthoringDocument owner, CancellationToken cancellationToken)
    {
        IStreamingAudioSource source = CreateSource(artifact, out StreamingSceneController? sceneController);
        MultiBusAnalysisTap? busAnalysis = null;
        if (source is StreamingSceneInstance sceneInstance)
        {
            busAnalysis = new MultiBusAnalysisTap(source.Format);
            busAnalysis.SetEnabledBuses(sceneInstance.BusIds);
            sceneInstance.SetBusTap(busAnalysis);
        }
        var player = new WasapiStreamingPlayer(source, new WasapiPlaybackOptions
        {
            Device = _deviceSelection,
            LatencyMilliseconds = 80,
            BufferMilliseconds = 400,
            PrebufferMilliseconds = 100,
            StartSourceAutomatically = true,
            StopSourceAutomatically = true,
            AutoRecoverDevice = true,
            FallBackToDefaultDevice = true
        });
        player.MasterGain = _masterGain;
        AudioAnalysisTap masterAnalysis = player.AttachAnalysis();
        player.Faulted += OnPlayerFaulted;
        lock (_gate)
        {
            _source = source;
            _sceneController = sceneController;
            _player = player;
            _activeDocument = owner;
            _masterAnalysis = masterAnalysis;
            _busAnalysis = busAnalysis;
            _lastAllocatedBytes = GC.GetTotalAllocatedBytes(false);
        }
        try { await Task.Run(player.Start, cancellationToken).ConfigureAwait(true); }
        catch { Stop(); throw; }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        WasapiStreamingPlayer? player;
        IStreamingAudioSource? source;
        AudioAnalysisTap? masterAnalysis;
        MultiBusAnalysisTap? busAnalysis;
        lock (_gate)
        {
            player = _player;
            source = _source;
            _player = null;
            _source = null;
            _sceneController = null;
            _activeDocument = null;
            masterAnalysis = _masterAnalysis;
            busAnalysis = _busAnalysis;
            _masterAnalysis = null;
            _busAnalysis = null;
        }

        if (player is not null)
        {
            player.Faulted -= OnPlayerFaulted;
            try { player.Stop(); }
            finally { player.Dispose(); }
        }
        if (source is StreamingSceneInstance sceneInstance) sceneInstance.SetBusTap(null);
        masterAnalysis?.Dispose();
        busAnalysis?.Dispose();
        if (source is IDisposable disposable) disposable.Dispose();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StartRecording(string path)
    {
        WasapiStreamingPlayer player = Volatile.Read(ref _player) ?? throw new InvalidOperationException("Start a preview before recording.");
        player.StartRecording(path, Playroma.Sound.Core.IO.WavEncoding.Float32);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void StopRecording()
    {
        WasapiStreamingPlayer? player = Volatile.Read(ref _player);
        player?.StopRecording();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IStreamingAudioSource CreateSource(object artifact, out StreamingSceneController? controller)
    {
        controller = null;
        switch (artifact)
        {
            case Playroma.Sound.Graph.Runtime.CompiledSoundGraph graph:
                return new CompiledGraphStreamingSource(graph);
            case GeneratedSoundArtifact generated:
                return new CompiledGraphStreamingSource(generated.CompiledGraph);
            case PreparedSoundScene scene:
            {
                StreamingSceneInstance instance = scene.CreateInstance();
                controller = new StreamingSceneController(instance);
                return instance;
            }
            default:
                throw new NotSupportedException($"Compilation artifact '{artifact.GetType().Name}' cannot be previewed.");
        }
    }

    private void OnPlayerFaulted(object? sender, PlaybackHostFaultEventArgs eventArgs) => Faulted?.Invoke(this, eventArgs);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
