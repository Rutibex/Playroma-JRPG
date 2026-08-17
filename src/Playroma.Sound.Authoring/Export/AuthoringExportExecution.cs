using System.Text.Json;
using System.Text.Json.Serialization;
using Playroma.Sound.Analysis.Offline;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Authoring.Workspace;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Core.IO;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Graph.BuiltIns;
using Playroma.Sound.Graph.Compilation;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Graph.Serialization;
using Playroma.Sound.Streaming.Compilation;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Runtime;
using Playroma.Sound.Streaming.Serialization;

namespace Playroma.Sound.Authoring.Export;

public enum AuthoringExportJobState { Queued, Compiling, Rendering, Writing, Completed, Failed, Cancelled }

public sealed record AuthoringExportProgress(
    AuthoringExportJobState State,
    int CompletedItems,
    int TotalItems,
    string AssetId,
    int VariationIndex,
    string Message,
    double Fraction);

public sealed record AuthoringRenderedOutput(
    string AssetId,
    int VariationIndex,
    string RelativePath,
    string BusId,
    OfflineAudioReport Analysis);

public sealed record AuthoringExportExecutionResult(
    bool Succeeded,
    string OutputRoot,
    string ManifestPath,
    IReadOnlyList<AuthoringRenderedOutput> RenderedOutputs,
    IReadOnlyList<string> CopiedFiles,
    TimeSpan Duration,
    Exception? Exception = null);

/// <summary>Executes validated export plans without any WinForms or audio-device dependency.</summary>
public sealed class AuthoringExportExecutor
{
    private readonly JsonSerializerOptions _jsonOptions;

    public AuthoringExportExecutor()
    {
        _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public Task<AuthoringExportExecutionResult> ExecuteAsync(
        AuthoringWorkspace workspace,
        AuthoringExportPlan plan,
        IProgress<AuthoringExportProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Execute(workspace, plan, progress, cancellationToken), cancellationToken);

    private AuthoringExportExecutionResult Execute(
        AuthoringWorkspace workspace,
        AuthoringExportPlan plan,
        IProgress<AuthoringExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanExport) throw new InvalidOperationException("The export plan contains validation errors.");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string outputRoot = Path.GetFullPath(plan.OutputRoot);
        string staging = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".staging-" + Guid.NewGuid().ToString("N");
        var rendered = new List<AuthoringRenderedOutput>();
        var copied = new List<string>();
        try
        {
            Directory.CreateDirectory(staging);
            int total = Math.Max(1, plan.Manifest.Items.Count * (plan.Profile.Mode == AuthoringExportMode.RenderedAudio ? plan.Profile.VariationCount : 1));
            int completed = 0;
            foreach (AuthoringExportItem item in plan.Manifest.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (plan.Profile.Mode == AuthoringExportMode.RuntimePackage)
                {
                    progress?.Report(new AuthoringExportProgress(AuthoringExportJobState.Writing, completed, total, item.AssetId, 0, "Copying runtime asset", completed / (double)total));
                    CopyRuntimeAsset(workspace, item, staging, copied);
                    completed++;
                    continue;
                }

                for (int variation = 0; variation < plan.Profile.VariationCount; variation++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(new AuthoringExportProgress(AuthoringExportJobState.Compiling, completed, total, item.AssetId, variation, "Compiling variation", completed / (double)total));
                    RenderExportItem(workspace, plan, item, variation, staging, rendered, progress, completed, total, cancellationToken);
                    completed++;
                }

                if (plan.Profile.IncludeSourceDefinitions)
                    CopySourceDefinition(workspace, item, staging, copied);
            }

            string manifestPath = Path.Combine(staging, "export-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
            {
                plan.Manifest,
                completedAtUtc = DateTimeOffset.UtcNow,
                renderedOutputs = rendered,
                copiedFiles = copied
            }, _jsonOptions));
            WriteIntegrationGuide(staging, plan);
            CommitDirectory(staging, outputRoot);
            progress?.Report(new AuthoringExportProgress(AuthoringExportJobState.Completed, total, total, "", 0, "Export complete", 1));
            return new AuthoringExportExecutionResult(true, outputRoot, Path.Combine(outputRoot, "export-manifest.json"), rendered, copied, timer.Elapsed);
        }
        catch (OperationCanceledException exception)
        {
            TryDeleteDirectory(staging);
            progress?.Report(new AuthoringExportProgress(AuthoringExportJobState.Cancelled, 0, 0, "", 0, "Export cancelled", 0));
            return new AuthoringExportExecutionResult(false, outputRoot, "", rendered, copied, timer.Elapsed, exception);
        }
        catch (Exception exception)
        {
            TryDeleteDirectory(staging);
            progress?.Report(new AuthoringExportProgress(AuthoringExportJobState.Failed, 0, 0, "", 0, exception.Message, 0));
            return new AuthoringExportExecutionResult(false, outputRoot, "", rendered, copied, timer.Elapsed, exception);
        }
    }

    private static void RenderExportItem(
        AuthoringWorkspace workspace,
        AuthoringExportPlan plan,
        AuthoringExportItem item,
        int variation,
        string staging,
        List<AuthoringRenderedOutput> outputs,
        IProgress<AuthoringExportProgress>? progress,
        int completed,
        int total,
        CancellationToken cancellationToken)
    {
        var format = new AudioFormat(plan.Profile.SampleRate, plan.Profile.Channels, plan.Profile.BlockSize);
        IAuthoringDocument document = workspace.OpenDocument(item.AssetId);
        progress?.Report(new AuthoringExportProgress(AuthoringExportJobState.Rendering, completed, total, item.AssetId, variation, "Rendering audio", completed / (double)total));
        string baseRelative = AddVariationSuffix(item.OutputRelativePath, variation, plan.Profile.VariationCount);
        switch (document)
        {
            case SoundGraphDocument graphDocument:
            {
                SoundGraphDefinition graph = new SoundGraphSerializer().Load(graphDocument.SerializeContent());
                graph.RootSeed = unchecked(graph.RootSeed + (ulong)variation);
                CompiledSoundGraph compiled = new SoundGraphCompiler(BuiltInGraphRegistry.CreateDefault()).Compile(graph, format);
                RenderedSound sound = RenderGraph(compiled, format, plan.Profile.SceneDurationSeconds, cancellationToken);
                WriteRendered(staging, baseRelative, sound, plan.Profile.WaveEncoding, item.AssetId, variation, "master", outputs);
                break;
            }
            case GeneratorRequestDocument generatorDocument:
            {
                var serializer = new EditableGeneratorRequestSerializer();
                EditableGeneratorRequest request = serializer.Deserialize(generatorDocument.SerializeContent());
                request.VariationIndex = checked(request.VariationIndex + variation);
                SoundGraphDefinition graph = serializer.Generate(request, SoundGeneratorCatalog.CreateDefault());
                CompiledSoundGraph compiled = new SoundGraphCompiler(BuiltInGraphRegistry.CreateDefault()).Compile(graph, format);
                RenderedSound sound = RenderGraph(compiled, format, plan.Profile.SceneDurationSeconds, cancellationToken);
                WriteRendered(staging, baseRelative, sound, plan.Profile.WaveEncoding, item.AssetId, variation, "master", outputs);
                break;
            }
            case StreamingSceneDocument sceneDocument:
            {
                SoundSceneDefinition scene = new SoundSceneSerializer().Deserialize(sceneDocument.SerializeContent());
                scene.RootSeed = unchecked(scene.RootSeed + (ulong)variation);
                PreparedSoundScene prepared = new SoundSceneCompiler().Compile(scene, format, workspace.SoundAssets);
                using StreamingSceneInstance instance = prepared.CreateInstance();
                OfflineStemTap? stems = plan.Profile.RenderStems ? new OfflineStemTap(format, scene.Buses.Select(bus => bus.Id)) : null;
                instance.SetBusTap(stems);
                RenderedSound master = RenderScene(instance, format, plan.Profile.SceneDurationSeconds, cancellationToken);
                WriteRendered(staging, baseRelative, master, plan.Profile.WaveEncoding, item.AssetId, variation, "master", outputs);
                if (stems is not null)
                {
                    foreach ((string busId, RenderedSound stem) in stems.CreateRenderedSounds())
                    {
                        string stemRelative = Path.ChangeExtension(baseRelative, null) + ".bus-" + SanitizeFileName(busId) + ".wav";
                        WriteRendered(staging, stemRelative, stem, plan.Profile.WaveEncoding, item.AssetId, variation, busId, outputs);
                    }
                }
                break;
            }
            default:
                throw new NotSupportedException($"Asset '{item.AssetId}' cannot be rendered to audio.");
        }
    }

    private static RenderedSound RenderGraph(CompiledSoundGraph graph, AudioFormat format, double maximumSeconds, CancellationToken cancellationToken)
    {
        int maximumFrames = format.FramesFromSeconds(maximumSeconds);
        float[] samples = new float[checked(maximumFrames * format.Channels)];
        graph.Reset();
        int position = 0;
        while (position < maximumFrames && !graph.IsComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = Math.Min(format.BlockFrames, maximumFrames - position);
            graph.Process(samples.AsSpan(position * format.Channels, frames * format.Channels), frames);
            position += frames;
        }
        if (graph.State == GraphActivityState.Infinite) position = maximumFrames;
        Array.Resize(ref samples, checked(position * format.Channels));
        return new RenderedSound(format, samples);
    }

    private static RenderedSound RenderScene(StreamingSceneInstance instance, AudioFormat format, double seconds, CancellationToken cancellationToken)
    {
        int totalFrames = format.FramesFromSeconds(seconds);
        float[] samples = new float[checked(totalFrames * format.Channels)];
        instance.Start();
        int position = 0;
        while (position < totalFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int frames = Math.Min(format.BlockFrames, totalFrames - position);
            instance.Render(samples.AsSpan(position * format.Channels, frames * format.Channels), frames);
            position += frames;
        }
        return new RenderedSound(format, samples);
    }

    private static void WriteRendered(
        string staging,
        string relativePath,
        RenderedSound sound,
        AuthoringWaveEncoding encoding,
        string assetId,
        int variation,
        string busId,
        List<AuthoringRenderedOutput> outputs)
    {
        string normalized = ProjectPath.NormalizeRelative(relativePath);
        string path = ProjectPath.Resolve(staging, normalized);
        WavWriter.Write(path, sound, ConvertEncoding(encoding));
        OfflineAudioReport report = OfflineAudioAnalyzer.Analyze(sound);
        File.WriteAllText(path + ".analysis.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        outputs.Add(new AuthoringRenderedOutput(assetId, variation, normalized, busId, report));
    }

    private static void CopyRuntimeAsset(AuthoringWorkspace workspace, AuthoringExportItem item, string staging, List<string> copied)
    {
        string destination = ProjectPath.Resolve(staging, item.OutputRelativePath);
        WriteCurrentAsset(workspace, item, destination);
        copied.Add(ProjectPath.NormalizeRelative(item.OutputRelativePath));
    }

    private static void CopySourceDefinition(AuthoringWorkspace workspace, AuthoringExportItem item, string staging, List<string> copied)
    {
        if (item.Kind is not (AuthoringAssetKind.SoundGraph or AuthoringAssetKind.GeneratorRequest or AuthoringAssetKind.StreamingScene)) return;
        string relative = ProjectPath.NormalizeRelative("Sources/" + item.SourceRelativePath);
        string destination = ProjectPath.Resolve(staging, relative);
        WriteCurrentAsset(workspace, item, destination);
        copied.Add(relative);
    }

    private static void WriteCurrentAsset(AuthoringWorkspace workspace, AuthoringExportItem item, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        IAuthoringDocument? open = workspace.OpenDocuments.FirstOrDefault(document => StringComparer.Ordinal.Equals(document.DocumentId, item.AssetId));
        if (open is not null && item.Kind is (AuthoringAssetKind.SoundGraph or AuthoringAssetKind.GeneratorRequest or AuthoringAssetKind.StreamingScene))
        {
            File.WriteAllText(destination, open.SerializeContent());
            return;
        }

        string source = ProjectPath.Resolve(workspace.ProjectRoot, item.SourceRelativePath);
        File.Copy(source, destination, true);
    }

    private static string AddVariationSuffix(string relativePath, int variation, int variationCount)
    {
        if (variationCount <= 1) return relativePath;
        string? directory = Path.GetDirectoryName(relativePath);
        string name = Path.GetFileNameWithoutExtension(relativePath) + $"_v{variation + 1:000}" + Path.GetExtension(relativePath);
        return string.IsNullOrEmpty(directory) ? name : Path.Combine(directory, name).Replace('\\', '/');
    }

    private static WavEncoding ConvertEncoding(AuthoringWaveEncoding encoding) => encoding switch
    {
        AuthoringWaveEncoding.Pcm16 => WavEncoding.Pcm16,
        AuthoringWaveEncoding.Pcm24 => WavEncoding.Pcm24,
        AuthoringWaveEncoding.Pcm32 => WavEncoding.Pcm32,
        AuthoringWaveEncoding.Float32 => WavEncoding.Float32,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding))
    };

    private static void CommitDirectory(string staging, string outputRoot)
    {
        string backup = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".backup";
        TryDeleteDirectory(backup);
        if (Directory.Exists(outputRoot)) Directory.Move(outputRoot, backup);
        try { Directory.Move(staging, outputRoot); TryDeleteDirectory(backup); }
        catch { if (Directory.Exists(backup) && !Directory.Exists(outputRoot)) Directory.Move(backup, outputRoot); throw; }
    }

    private static void WriteIntegrationGuide(string staging, AuthoringExportPlan plan)
    {
        string text = plan.Profile.Mode == AuthoringExportMode.RuntimePackage
            ? "Playroma Sound runtime package\n\nLoad .playsound and .playstream definitions through the Playroma.Sound.Graph and Playroma.Sound.Streaming serializers. See export-manifest.json for dependencies.\n"
            : "Playroma Sound rendered-audio export\n\nWAV files are accompanied by .analysis.json reports containing peak, RMS, loudness estimate, DC offset, clipping, and channel metrics.\n";
        File.WriteAllText(Path.Combine(staging, "README.txt"), text);
    }

    private static string SanitizeFileName(string value) => new(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character).ToArray());
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

    private sealed class OfflineStemTap : IStreamingBusTap
    {
        private readonly AudioFormat _format;
        private readonly Dictionary<string, List<float>> _samples;

        public OfflineStemTap(AudioFormat format, IEnumerable<string> busIds)
        {
            _format = format;
            _samples = busIds.Distinct(StringComparer.Ordinal).ToDictionary(id => id, _ => new List<float>(), StringComparer.Ordinal);
        }

        public bool TryWriteBus(string busId, ReadOnlySpan<float> interleavedSamples, int frameCount, AudioFormat format)
        {
            if (format != _format || !_samples.TryGetValue(busId, out List<float>? target)) return false;
            int count = checked(frameCount * format.Channels);
            for (int i = 0; i < count; i++) target.Add(interleavedSamples[i]);
            return true;
        }

        public IEnumerable<KeyValuePair<string, RenderedSound>> CreateRenderedSounds() =>
            _samples.Select(pair => new KeyValuePair<string, RenderedSound>(pair.Key, new RenderedSound(_format, pair.Value.ToArray())));
    }
}
