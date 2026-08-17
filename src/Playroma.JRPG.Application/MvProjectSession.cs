using Playroma.JRPG.Assets;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.Validation;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Application;

public enum ProjectAnalysisLoadMode
{
    Blocking,
    Deferred
}

public sealed record ProjectSessionState(
    bool IsReady,
    bool IsBusy,
    bool HasUnsavedChanges,
    bool CanSave,
    bool ReferencesReady,
    bool ValidationReady,
    bool IsReadOnly,
    string? BusyOperation);

/// <summary>
/// Application service that keeps the mutable workspace, undo history,
/// reference index, dependency reports, validation, saving, and Phase 4 UI state synchronized.
/// </summary>
public sealed class MvProjectSession : IAsyncDisposable
{
    private readonly IncrementalReferenceIndexCoordinator _referenceCoordinator;
    private readonly SemaphoreSlim _analysisGate = new(1, 1);
    private readonly SemaphoreSlim _assetGate = new(1, 1);
    private bool _disposed;
    private bool _referencesReady;
    private ProjectValidationResult? _lastValidation;
    private string? _busyOperation;

    private MvProjectSession(
        MvEditingSession editing,
        ProjectReferenceIndex references,
        MvProjectReferenceIndexer indexer,
        ProjectValidationEngine validation,
        bool referencesReady)
    {
        Editing = editing;
        References = references;
        ReferenceIndexer = indexer;
        Validation = validation;
        _referencesReady = referencesReady;
        string projectRoot = editing.Workspace.Snapshot.Identity.ProjectRoot;
        string runtimeRoot = editing.Workspace.Snapshot.Identity.RuntimeRoot ?? projectRoot;
        string runtimePrefix = Path.GetRelativePath(projectRoot, runtimeRoot).Replace('\\', '/');
        if (runtimePrefix == ".") runtimePrefix = string.Empty;
        AssetContent = new ProjectAssetContentSource(editing.Files);
        Assets = new AssetCatalogueService(
            projectRoot,
            editing.Workspace.Snapshot.Assets,
            runtimePrefix,
            BuildAssetHints(references),
            contentSource: AssetContent);
        AssetOperations = new AssetOperationService(this);
        _referenceCoordinator = new IncrementalReferenceIndexCoordinator(
            editing.Context,
            editing.Workspace,
            references,
            indexer);
        Editing.Undo.HistoryChanged += OnSessionStateSourceChanged;
        Editing.Context.CommandApplied += OnCommandApplied;
        References.Changed += OnReferencesChanged;
        Assets.Changed += OnSessionStateSourceChanged;
    }

    public event EventHandler? StateChanged;
    public event EventHandler<ProjectValidationResult>? ValidationCompleted;
    public event EventHandler<ProjectSaveResult>? Saved;
    public event EventHandler<EditorCommandEvent>? DocumentChanged;

    public MvEditingSession Editing { get; }
    public IMvProjectWorkspace Workspace => Editing.Workspace;
    public UndoManager Undo => Editing.Undo;
    public ProjectReferenceIndex References { get; }
    public MvProjectReferenceIndexer ReferenceIndexer { get; }
    public ProjectValidationEngine Validation { get; }
    public ProjectAssetContentSource AssetContent { get; }
    public AssetCatalogueService Assets { get; }
    public AssetOperationService AssetOperations { get; }
    public IReadOnlyCollection<string> PendingReferenceDocuments => _referenceCoordinator.PendingDocuments;
    public ProjectValidationResult? LastValidation => _lastValidation;
    public bool ReferencesReady => _referencesReady;

    public ProjectSessionState State => new(
        IsReady: true,
        IsBusy: !string.IsNullOrWhiteSpace(_busyOperation),
        HasUnsavedChanges: Workspace.HasUnsavedChanges || Editing.Files.HasPendingChanges || !Undo.IsAtSavedState,
        CanSave: Workspace.Snapshot.Editability is Playroma.JRPG.RpgMakerMV.ProjectEditability.FullyEditableSource
            or Playroma.JRPG.RpgMakerMV.ProjectEditability.ProbablyEditable,
        ReferencesReady: _referencesReady,
        ValidationReady: _lastValidation is not null,
        IsReadOnly: Workspace.Snapshot.Editability is Playroma.JRPG.RpgMakerMV.ProjectEditability.ReadOnlyInspection
            or Playroma.JRPG.RpgMakerMV.ProjectEditability.UnsupportedEncryptedDeployment
            or Playroma.JRPG.RpgMakerMV.ProjectEditability.Invalid,
        BusyOperation: _busyOperation);

    public static async ValueTask<OperationResult<MvProjectSession>> OpenAsync(
        string projectPath,
        DocumentCacheOptions? cacheOptions = null,
        IProgress<ReferenceIndexProgress>? indexProgress = null,
        CancellationToken cancellationToken = default,
        ProjectAnalysisLoadMode analysisLoadMode = ProjectAnalysisLoadMode.Blocking)
    {
        OperationResult<MvEditingSession> opened = await MvEditingSession.OpenAsync(
            projectPath,
            cacheOptions,
            cancellationToken).ConfigureAwait(false);
        if (opened.Value is null)
        {
            return OperationResult<MvProjectSession>.Failure(opened.Diagnostics);
        }

        MvEditingSession editing = opened.Value;
        try
        {
            MvProjectReferenceIndexer indexer = new();
            ProjectReferenceIndex references;
            bool ready;
            if (analysisLoadMode == ProjectAnalysisLoadMode.Blocking)
            {
                references = await indexer.BuildAsync(
                    editing.Workspace,
                    indexProgress,
                    cancellationToken).ConfigureAwait(false);
                ready = true;
            }
            else
            {
                references = new ProjectReferenceIndex();
                ready = false;
            }

            MvProjectSession session = new(editing, references, indexer, new ProjectValidationEngine(), ready);
            await session.Assets.BuildAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return OperationResult<MvProjectSession>.Success(session, opened.Diagnostics);
        }
        catch (OperationCanceledException)
        {
            await editing.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await editing.DisposeAsync().ConfigureAwait(false);
            ProjectDiagnostic diagnostic = ProjectDiagnostic.Error(
                DiagnosticCodes.Phase3ReferenceIndexFailure,
                "The project opened, but its reference index could not be built.",
                technicalDetail: exception.ToString());
            return OperationResult<MvProjectSession>.Failure(opened.Diagnostics.Append(diagnostic));
        }
    }

    public async ValueTask InitializeReferencesAsync(
        IProgress<ReferenceIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _analysisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_referencesReady) return;
            SetBusy("Building project references");
            ProjectReferenceIndex built = await ReferenceIndexer.BuildAsync(
                Workspace,
                progress,
                cancellationToken).ConfigureAwait(false);
            References.ReplaceAll(built.All);
            _referencesReady = true;
            await RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(false);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            ClearBusy();
            _analysisGate.Release();
        }
    }

    public ProjectTransaction BeginTransaction(string description, bool allowHistoryMerge = false)
    {
        ThrowIfDisposed();
        return Editing.BeginTransaction(description, allowHistoryMerge);
    }

    public DependencyReport GetDependencies(ReferenceTarget target)
    {
        ThrowIfDisposed();
        return DependencyReportBuilder.Build(References, target);
    }

    public async ValueTask<int> RefreshReferencesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!_referencesReady)
        {
            await InitializeReferencesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return References.Statistics.DocumentCount;
        }

        return await _referenceCoordinator.RefreshPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProjectValidationResult> ValidateAsync(
        IProgress<ValidationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetBusy("Validating project");
        try
        {
            await RefreshReferencesAsync(cancellationToken).ConfigureAwait(false);
            ProjectValidationResult coreValidation = await Validation.ValidateAsync(
                Editing.Workspace,
                References,
                progress,
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<ProjectDiagnostic> assetDiagnostics = await Assets.ValidateAllAsync(
                cancellationToken: cancellationToken).ConfigureAwait(false);
            ProjectValidationDiagnostic[] assetValidation = assetDiagnostics
                .Select(diagnostic => new ProjectValidationDiagnostic(diagnostic, DocumentationKey: "phase5-assets"))
                .ToArray();
            _lastValidation = new ProjectValidationResult(
                coreValidation.Diagnostics.Concat(assetValidation)
                    .OrderBy(item => item.Diagnostic.Location.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Code, StringComparer.Ordinal)
                    .ToArray(),
                DateTimeOffset.UtcNow);
            ValidationCompleted?.Invoke(this, _lastValidation);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return _lastValidation;
        }
        finally
        {
            ClearBusy();
        }
    }

    public async ValueTask<TransactionPreview> PreviewFixAsync(
        ProjectValidationDiagnostic diagnostic,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        DiagnosticFix fix = diagnostic.Fix
            ?? throw new InvalidOperationException("The diagnostic does not provide a safe fix command.");
        await using ProjectTransaction transaction = BeginTransaction(fix.Title);
        transaction.Add(fix.Command);
        return await transaction.PreviewAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ApplyFixAsync(
        ProjectValidationDiagnostic diagnostic,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        DiagnosticFix fix = diagnostic.Fix
            ?? throw new InvalidOperationException("The diagnostic does not provide a safe fix command.");
        if (!fix.IsSafe)
        {
            throw new InvalidOperationException("The selected fix is not marked safe and cannot be applied automatically.");
        }

        await using ProjectTransaction transaction = BeginTransaction(fix.Title);
        transaction.Add(fix.Command);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await RefreshReferencesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ProjectSaveResult> SaveAsync(
        ProjectSaveOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetBusy("Saving project");
        try
        {
            ProjectSaveResult result = await Editing.SaveAsync(options, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                await RefreshAssetCatalogueAsync(cancellationToken).ConfigureAwait(false);
            }
            Saved?.Invoke(this, result);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally
        {
            ClearBusy();
        }
    }

    public async ValueTask DiscardAllChangesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Workspace.DiscardAllChanges();
        Editing.Files.DiscardAll();
        Undo.Clear();
        References.Clear();
        _referencesReady = false;
        _lastValidation = null;
        await InitializeReferencesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask RefreshAssetCatalogueAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _assetGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Assets.BuildAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            Assets.ReplaceReferences(BuildAssetHints(References));
        }
        finally
        {
            _assetGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        Editing.Undo.HistoryChanged -= OnSessionStateSourceChanged;
        Editing.Context.CommandApplied -= OnCommandApplied;
        References.Changed -= OnReferencesChanged;
        Assets.Changed -= OnSessionStateSourceChanged;
        _referenceCoordinator.Dispose();
        _analysisGate.Dispose();
        _assetGate.Dispose();
        await Assets.DisposeAsync().ConfigureAwait(false);
        await Editing.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void OnCommandApplied(object? sender, EditorCommandEvent e)
    {
        _lastValidation = null;
        DocumentChanged?.Invoke(this, e);
        if (e.Targets.Any(target => target.Kind == EditorMutationTargetKind.File))
        {
            _ = RefreshAssetCatalogueAfterCommandAsync();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshAssetCatalogueAfterCommandAsync()
    {
        try
        {
            await RefreshAssetCatalogueAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The project closed while the incremental refresh was queued.
        }
        catch (Exception)
        {
            // A later explicit refresh will rebuild the catalogue. Asset operations remain staged safely.
        }
    }

    private void OnReferencesChanged(object? sender, EventArgs e)
    {
        Assets.ReplaceReferences(BuildAssetHints(References));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IReadOnlyList<AssetReferenceHint> BuildAssetHints(ProjectReferenceIndex references) =>
        references.All
            .Where(reference => reference.Target.Kind == ReferenceTargetKind.Asset
                && reference.Target.AssetCategory.HasValue
                && !string.IsNullOrWhiteSpace(reference.Target.Name))
            .Select(reference => new AssetReferenceHint(
                new AssetKey(reference.Target.AssetCategory!.Value, reference.Target.Name!),
                reference.Source.NormalizedRelativePath,
                reference.Source.JsonPath,
                reference.Confidence == ReferenceConfidence.Exact,
                reference.Context))
            .ToArray();


    private void OnSessionStateSourceChanged(object? sender, EventArgs e) =>
        StateChanged?.Invoke(this, EventArgs.Empty);

    private void SetBusy(string operation)
    {
        _busyOperation = operation;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearBusy()
    {
        _busyOperation = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
