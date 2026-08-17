using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Core.Audio;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Graph.BuiltIns;
using Playroma.Sound.Graph.Compilation;
using Playroma.Sound.Graph.Registry;
using Playroma.Sound.Streaming.Assets;
using Playroma.Sound.Streaming.Compilation;

namespace Playroma.Sound.Authoring.Documents;

public enum AuthoringDocumentKind
{
    SoundGraph,
    GeneratorRequest,
    StreamingScene
}

public enum AuthoringCompilationState
{
    NotCompiled,
    Queued,
    Compiling,
    Succeeded,
    Failed,
    Cancelled,
    Stale
}

public sealed record AuthoringCompilationResult(
    string DocumentId,
    long Revision,
    AuthoringCompilationState State,
    ValidationSummary Validation,
    object? Artifact,
    TimeSpan Duration,
    Exception? Exception = null)
{
    public bool Succeeded => State == AuthoringCompilationState.Succeeded && !Validation.HasErrors;
}

public sealed class AuthoringCompilationContext
{
    public AuthoringCompilationContext(AudioFormat format, ISoundAssetResolver soundAssets)
    {
        Format = format;
        SoundAssets = soundAssets ?? throw new ArgumentNullException(nameof(soundAssets));
    }

    public AudioFormat Format { get; }
    public ISoundAssetResolver SoundAssets { get; }
    public GraphNodeRegistry GraphRegistry { get; init; } = BuiltInGraphRegistry.CreateDefault();
    public SoundGeneratorCatalog GeneratorCatalog { get; init; } = SoundGeneratorCatalog.CreateDefault();
    public GraphCompileOptions GraphOptions { get; init; } = new();
    public SoundSceneCompileOptions SceneOptions { get; init; } = new();
}

public sealed class AuthoringDocumentChangedEventArgs : EventArgs
{
    public AuthoringDocumentChangedEventArgs(string documentId, long revision, string reason)
    {
        DocumentId = documentId;
        Revision = revision;
        Reason = reason;
    }

    public string DocumentId { get; }
    public long Revision { get; }
    public string Reason { get; }
}

public interface IAuthoringDocument
{
    string DocumentId { get; }
    string DisplayName { get; }
    string FilePath { get; }
    AuthoringDocumentKind Kind { get; }
    bool IsDirty { get; }
    long Revision { get; }
    long SavedRevision { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    string UndoDescription { get; }
    string RedoDescription { get; }
    event EventHandler<AuthoringDocumentChangedEventArgs>? Changed;

    ValidationSummary Validate();
    Task<AuthoringCompilationResult> CompileAsync(AuthoringCompilationContext context, CancellationToken cancellationToken = default);
    string SerializeContent();
    void ReplaceSerializedContent(string serializedContent, string description);
    void Save();
    void SaveAs(string path);
    bool Undo();
    bool Redo();
    void MarkSaved();
}
