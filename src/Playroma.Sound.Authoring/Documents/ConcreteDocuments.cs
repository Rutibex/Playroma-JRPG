using System.Diagnostics;
using System.Text.Json;
using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Graph.Compilation;
using Playroma.Sound.Graph.Definitions;
using Playroma.Sound.Graph.Runtime;
using Playroma.Sound.Graph.Serialization;
using Playroma.Sound.Graph.Validation;
using Playroma.Sound.Streaming.Compilation;
using Playroma.Sound.Streaming.Definitions;
using Playroma.Sound.Streaming.Serialization;
using Playroma.Sound.Streaming.Validation;

namespace Playroma.Sound.Authoring.Documents;

public sealed class SoundGraphDocument : AuthoringDocumentBase<SoundGraphDefinition>
{
    private readonly SoundGraphSerializer _serializer;

    public SoundGraphDocument(string filePath, SoundGraphDefinition model, SoundGraphSerializer? serializer = null) : base(filePath, model)
    {
        _serializer = serializer ?? new SoundGraphSerializer();
        InitializeSavedState();
    }
    public override AuthoringDocumentKind Kind => AuthoringDocumentKind.SoundGraph;
    public override string DocumentId => CurrentModel.Id;
    public override string DisplayName => CurrentModel.DisplayName;

    public static SoundGraphDocument Load(string path, SoundGraphSerializer? serializer = null)
    {
        serializer ??= new SoundGraphSerializer();
        return new SoundGraphDocument(path, serializer.LoadFile(path), serializer);
    }

    public override ValidationSummary Validate()
    {
        GraphValidationReport report = new SoundGraphCompiler(Playroma.Sound.Graph.BuiltIns.BuiltInGraphRegistry.CreateDefault()).Validate(CurrentModel);
        return new ValidationSummary(report.Diagnostics.Select(item => new AuthoringDiagnostic(
            item.Code,
            item.Severity switch
            {
                GraphDiagnosticSeverity.Error => AuthoringDiagnosticSeverity.Error,
                GraphDiagnosticSeverity.Warning => AuthoringDiagnosticSeverity.Warning,
                _ => AuthoringDiagnosticSeverity.Information
            },
            item.Message,
            DocumentId,
            item.NodeId ?? "",
            item.PortId ?? "",
            FilePath)));
    }

    public override Task<AuthoringCompilationResult> CompileAsync(AuthoringCompilationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        long revision = Revision;
        SoundGraphDefinition snapshot = _serializer.Load(_serializer.Serialize(CurrentModel));
        return Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                GraphValidationReport report = new GraphValidator(context.GraphRegistry).Validate(snapshot);
                ValidationSummary summary = Convert(report);
                if (summary.HasErrors) return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Failed, summary, null, timer.Elapsed);
                cancellationToken.ThrowIfCancellationRequested();
                CompiledSoundGraph artifact = new SoundGraphCompiler(context.GraphRegistry).Compile(snapshot, context.Format, context.GraphOptions);
                return new AuthoringCompilationResult(snapshot.Id, revision, Revision == revision ? AuthoringCompilationState.Succeeded : AuthoringCompilationState.Stale, summary, artifact, timer.Elapsed);
            }
            catch (OperationCanceledException exception) { return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Cancelled, ValidationSummary.Empty, null, timer.Elapsed, exception); }
            catch (Exception exception) { return Failure(snapshot.Id, revision, timer.Elapsed, exception); }
        });
    }

    protected override string SerializeModel(SoundGraphDefinition model) => _serializer.Serialize(model);
    protected override SoundGraphDefinition DeserializeModel(string content) => _serializer.Load(content);

    private ValidationSummary Convert(GraphValidationReport report) => new(report.Diagnostics.Select(item => new AuthoringDiagnostic(item.Code, item.Severity == GraphDiagnosticSeverity.Error ? AuthoringDiagnosticSeverity.Error : item.Severity == GraphDiagnosticSeverity.Warning ? AuthoringDiagnosticSeverity.Warning : AuthoringDiagnosticSeverity.Information, item.Message, DocumentId, item.NodeId ?? "", item.PortId ?? "", FilePath)));
    private static AuthoringCompilationResult Failure(string id, long revision, TimeSpan duration, Exception exception) => new(id, revision, AuthoringCompilationState.Failed, new ValidationSummary([new AuthoringDiagnostic("COMPILE001", AuthoringDiagnosticSeverity.Error, exception.Message, id)]), null, duration, exception);
}

public sealed class GeneratorRequestDocument : AuthoringDocumentBase<EditableGeneratorRequest>
{
    private readonly EditableGeneratorRequestSerializer _serializer;
    private readonly SoundGeneratorCatalog _catalog;

    public GeneratorRequestDocument(string filePath, EditableGeneratorRequest model, SoundGeneratorCatalog? catalog = null, EditableGeneratorRequestSerializer? serializer = null) : base(filePath, model)
    {
        _serializer = serializer ?? new EditableGeneratorRequestSerializer();
        _catalog = catalog ?? SoundGeneratorCatalog.CreateDefault();
        InitializeSavedState();
    }

    public override AuthoringDocumentKind Kind => AuthoringDocumentKind.GeneratorRequest;
    public override string DocumentId => CurrentModel.Id;
    public override string DisplayName => CurrentModel.DisplayName;

    public static GeneratorRequestDocument Load(string path, SoundGeneratorCatalog? catalog = null, EditableGeneratorRequestSerializer? serializer = null)
    {
        serializer ??= new EditableGeneratorRequestSerializer();
        return new GeneratorRequestDocument(path, serializer.Deserialize(File.ReadAllText(path)), catalog, serializer);
    }

    public override ValidationSummary Validate()
    {
        var diagnostics = new List<AuthoringDiagnostic>();
        if (CurrentModel.FormatVersion != Playroma.Sound.Generators.Presets.SoundGeneratorRequestSerializer.CurrentFormatVersion) Error("GEN001", $"Generator request format {CurrentModel.FormatVersion} is not supported.");
        if (string.IsNullOrWhiteSpace(CurrentModel.Id)) Error("GEN002", "Generator request ID is required.");
        if (string.IsNullOrWhiteSpace(CurrentModel.DisplayName)) Error("GEN003", "Generator display name is required.");
        try { _serializer.Generate(CurrentModel, _catalog); }
        catch (Exception exception) when (exception is KeyNotFoundException or JsonException or NotSupportedException or InvalidDataException)
        {
            Error("GEN004", exception.Message);
        }
        return new ValidationSummary(diagnostics);
        void Error(string code, string message) => diagnostics.Add(new AuthoringDiagnostic(code, AuthoringDiagnosticSeverity.Error, message, DocumentId, SourcePath: FilePath));
    }

    public override Task<AuthoringCompilationResult> CompileAsync(AuthoringCompilationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        long revision = Revision;
        EditableGeneratorRequest snapshot = _serializer.Deserialize(_serializer.Serialize(CurrentModel));
        return Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var diagnostics = new List<AuthoringDiagnostic>();
                SoundGraphDefinition graph;
                try { graph = _serializer.Generate(snapshot, context.GeneratorCatalog); }
                catch (Exception exception)
                {
                    diagnostics.Add(new AuthoringDiagnostic("GEN004", AuthoringDiagnosticSeverity.Error, exception.Message, snapshot.Id, SourcePath: FilePath));
                    return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Failed, new ValidationSummary(diagnostics), null, timer.Elapsed, exception);
                }
                GraphValidationReport graphReport = new GraphValidator(context.GraphRegistry).Validate(graph);
                diagnostics.AddRange(graphReport.Diagnostics.Select(item => new AuthoringDiagnostic("GENERATED_" + item.Code, item.Severity == GraphDiagnosticSeverity.Error ? AuthoringDiagnosticSeverity.Error : item.Severity == GraphDiagnosticSeverity.Warning ? AuthoringDiagnosticSeverity.Warning : AuthoringDiagnosticSeverity.Information, item.Message, snapshot.Id, item.NodeId ?? "", item.PortId ?? "", FilePath)));
                ValidationSummary combined = new(diagnostics);
                if (combined.HasErrors) return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Failed, combined, graph, timer.Elapsed);
                cancellationToken.ThrowIfCancellationRequested();
                CompiledSoundGraph compiled = new SoundGraphCompiler(context.GraphRegistry).Compile(graph, context.Format, context.GraphOptions);
                return new AuthoringCompilationResult(snapshot.Id, revision, Revision == revision ? AuthoringCompilationState.Succeeded : AuthoringCompilationState.Stale, combined, new GeneratedSoundArtifact(graph, compiled), timer.Elapsed);
            }
            catch (OperationCanceledException exception) { return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Cancelled, ValidationSummary.Empty, null, timer.Elapsed, exception); }
            catch (Exception exception) { return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Failed, new ValidationSummary([new AuthoringDiagnostic("GEN_COMPILE", AuthoringDiagnosticSeverity.Error, exception.Message, snapshot.Id)]), null, timer.Elapsed, exception); }
        });
    }

    protected override string SerializeModel(EditableGeneratorRequest model) => _serializer.Serialize(model);
    protected override EditableGeneratorRequest DeserializeModel(string content) => _serializer.Deserialize(content);
}

public sealed record GeneratedSoundArtifact(SoundGraphDefinition Definition, CompiledSoundGraph CompiledGraph);

public sealed class StreamingSceneDocument : AuthoringDocumentBase<SoundSceneDefinition>
{
    private readonly SoundSceneSerializer _serializer;

    public StreamingSceneDocument(string filePath, SoundSceneDefinition model, SoundSceneSerializer? serializer = null) : base(filePath, model)
    {
        _serializer = serializer ?? new SoundSceneSerializer();
        InitializeSavedState();
    }
    public override AuthoringDocumentKind Kind => AuthoringDocumentKind.StreamingScene;
    public override string DocumentId => CurrentModel.Id;
    public override string DisplayName => CurrentModel.DisplayName;

    public static StreamingSceneDocument Load(string path, SoundSceneSerializer? serializer = null)
    {
        serializer ??= new SoundSceneSerializer();
        return new StreamingSceneDocument(path, serializer.Load(path), serializer);
    }

    public override ValidationSummary Validate()
    {
        SceneValidationReport report = new SoundSceneValidator().Validate(CurrentModel);
        return Convert(report);
    }

    public override Task<AuthoringCompilationResult> CompileAsync(AuthoringCompilationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        long revision = Revision;
        SoundSceneDefinition snapshot = _serializer.Deserialize(_serializer.Serialize(CurrentModel));
        return Task.Run(() =>
        {
            var timer = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SceneValidationReport report = new SoundSceneValidator().Validate(snapshot);
                ValidationSummary summary = Convert(report);
                if (summary.HasErrors) return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Failed, summary, null, timer.Elapsed);
                cancellationToken.ThrowIfCancellationRequested();
                PreparedSoundScene prepared = new SoundSceneCompiler(context.GraphRegistry).Compile(snapshot, context.Format, context.SoundAssets, context.SceneOptions);
                return new AuthoringCompilationResult(snapshot.Id, revision, Revision == revision ? AuthoringCompilationState.Succeeded : AuthoringCompilationState.Stale, summary, prepared, timer.Elapsed);
            }
            catch (OperationCanceledException exception) { return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Cancelled, ValidationSummary.Empty, null, timer.Elapsed, exception); }
            catch (Exception exception) { return new AuthoringCompilationResult(snapshot.Id, revision, AuthoringCompilationState.Failed, new ValidationSummary([new AuthoringDiagnostic("SCENE_COMPILE", AuthoringDiagnosticSeverity.Error, exception.Message, snapshot.Id)]), null, timer.Elapsed, exception); }
        });
    }

    protected override string SerializeModel(SoundSceneDefinition model) => _serializer.Serialize(model);
    protected override SoundSceneDefinition DeserializeModel(string content) => _serializer.Deserialize(content);

    private ValidationSummary Convert(SceneValidationReport report) => new(report.Diagnostics.Select(item => new AuthoringDiagnostic(item.Code, item.Severity == SceneDiagnosticSeverity.Error ? AuthoringDiagnosticSeverity.Error : item.Severity == SceneDiagnosticSeverity.Warning ? AuthoringDiagnosticSeverity.Warning : AuthoringDiagnosticSeverity.Information, item.Message, DocumentId, item.ObjectId, SourcePath: FilePath)));
}
