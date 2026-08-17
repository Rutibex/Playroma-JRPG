using System.Text.Json;
using Playroma.JRPG.Core;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Application;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.RpgMakerMV;
using Playroma.Text.RpgMakerMV;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Dialogue;
using Playroma.Text.Editor.WinForms;
using Playroma.Text.Editor.WinForms.Controls;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Project-scoped host for Playroma Text Studio. Phase 8 retains the Phase 7 lifecycle integration
/// and adds context-aware RPG Maker MV database targets plus undoable import transactions.
/// </summary>
public sealed class TextStudioForm : EditorToolForm, IShutdownParticipant, IWorkspaceStateContributor
{
    private const string RecoverySessionType = "Playroma.TextStudio.Authoring";
    private readonly IWorkspaceService _workspace;
    private readonly INotificationService _notifications;
    private readonly IOutputService _output;
    private TextStudioControl? _studio;
    private TextStudioDatabaseIntegrationService? _databaseIntegration;
    private TextStudioEventBinding? _activeEventBinding;
    private long _contextRevision;
    private bool _workspaceSubscribed;

    public TextStudioForm(
        WindowRequest request,
        IWorkspaceService workspace,
        INotificationService notifications,
        IOutputService output) : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        Name = nameof(TextStudioForm);
        Text = "Playroma Text Studio";
        MinimumSize = new Size(1080, 700);
        Size = new Size(1500, 900);
        KeyPreview = true;
    }

    public override string Title
    {
        get
        {
            var project = _workspace.CurrentSession?.Workspace.Snapshot.Identity.ProjectTitle ?? string.Empty;
            var dirty = IsDirty ? " *" : string.Empty;
            return string.IsNullOrWhiteSpace(project)
                ? "Playroma Text Studio" + dirty
                : $"Playroma Text Studio — {project}{dirty}";
        }
    }

    public override bool IsDirty => _studio?.HasUnsavedChanges == true;

    protected override async ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = _workspace.CurrentSession
            ?? throw new InvalidOperationException("Playroma Text Studio requires an open RPG Maker MV project.");
        var identity = session.Workspace.Snapshot.Identity;
        _databaseIntegration = new TextStudioDatabaseIntegrationService(session);
        _studio = new TextStudioControl(new TextStudioOptions(
            identity.ProjectRoot,
            identity.ProjectTitle ?? Path.GetFileName(identity.ProjectRoot),
            BatchHost: _databaseIntegration))
        {
            Dock = DockStyle.Fill
        };
        _studio.DirtyStateChanged += OnStudioDirtyStateChanged;
        _studio.StatusChanged += OnStudioStatusChanged;
        _studio.ImportRequested += OnImportRequested;
        InstallContent(_studio);
        await _studio.InitializeAsync(cancellationToken).ConfigureAwait(true);
        _studio.ApplyProjectContext(TextStudioProjectContextFactory.Create(session, ++_contextRevision));
        SubscribeWorkspace();
        UpdateWindowState();
    }

    public async ValueTask ActivateDatabaseTargetAsync(
        MvDocumentKind kind,
        string relativePath,
        int recordId,
        string? fieldKey = null,
        CancellationToken cancellationToken = default)
    {
        if (_studio is null || _databaseIntegration is null)
            throw new InvalidOperationException("Text Studio has not finished loading.");
        _activeEventBinding = null;
        TextRecordTargetDescriptor target = await _databaseIntegration
            .BuildTargetAsync(kind, relativePath, recordId, fieldKey, cancellationToken)
            .ConfigureAwait(true);
        _studio.ActivateDatabaseTarget(target);
        _output.Write("Text Studio", $"Activated database target {target.DisplayName} / {target.SelectedField?.DisplayName}.");
        if (!Visible) Show();
        Activate();
    }

    public void ActivateDirectTarget(TextRecordTargetDescriptor target)
    {
        if (_studio is null) throw new InvalidOperationException("Text Studio has not finished loading.");
        _activeEventBinding = null;
        _studio.ActivateDatabaseTarget(target);
        _output.Write("Text Studio", $"Activated target {target.DisplayName} / {target.SelectedField?.DisplayName}.");
        if (!Visible) Show();
        Activate();
    }

    public async ValueTask ActivateMapTargetAsync(
        int mapId,
        string relativePath,
        string? fieldKey = null,
        CancellationToken cancellationToken = default)
    {
        if (_studio is null || _databaseIntegration is null)
            throw new InvalidOperationException("Text Studio has not finished loading.");
        _activeEventBinding = null;
        TextRecordTargetDescriptor target = await _databaseIntegration
            .BuildMapTargetAsync(mapId, relativePath, fieldKey, cancellationToken)
            .ConfigureAwait(true);
        _studio.ActivateDatabaseTarget(target);
        _output.Write("Text Studio", $"Activated map target {target.DisplayName} / {target.SelectedField?.DisplayName}.");
        if (!Visible) Show();
        Activate();
    }

    internal void ActivateEventTarget(TextStudioEventBinding binding, DialogueVoiceContext? dialogueContext = null, string? preferredRecipeId = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (_studio is null) throw new InvalidOperationException("Text Studio has not finished loading.");
        _activeEventBinding = binding;
        _studio.ActivateDatabaseTarget(binding.Target);
        if (dialogueContext is not null) _studio.ApplyDialogueContext(dialogueContext, preferredRecipeId);
        else if (!string.IsNullOrWhiteSpace(preferredRecipeId)) _studio.ApplyDialogueContext(new DialogueVoiceContext(SpeakerName: binding.Target.DisplayName), preferredRecipeId);
        _output.Write("Text Studio", $"Activated event target {binding.Target.DisplayName}." + (dialogueContext is null ? string.Empty : $" Speaker={dialogueContext.SpeakerName}; Voice={dialogueContext.ProfileId}; Act={dialogueContext.Act}."));
        if (!Visible) Show();
        Activate();
    }

    internal void OpenStoryletLab()
    {
        if (_studio is null) throw new InvalidOperationException("Text Studio has not finished loading.");
        _studio.OpenStoryletLab();
        if (!Visible) Show();
        Activate();
    }

    internal void OpenQuestLab()
    {
        if (_studio is null) throw new InvalidOperationException("Text Studio has not finished loading.");
        _studio.OpenQuestLab();
        if (!Visible) Show();
        Activate();
    }

    internal void OpenBatchBuilder()
    {
        if (_studio is null) throw new InvalidOperationException("Text Studio has not finished loading.");
        _studio.OpenBatchBuilder();
        if (!Visible) Show();
        Activate();
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _studio is null || await _studio.PrepareForCloseAsync(cancellationToken).ConfigureAwait(true);
    }

    protected override ValueTask OnDisposeWindowAsync()
    {
        UnsubscribeWorkspace();
        if (_studio is not null)
        {
            _studio.DirtyStateChanged -= OnStudioDirtyStateChanged;
            _studio.StatusChanged -= OnStudioStatusChanged;
            _studio.ImportRequested -= OnImportRequested;
            _studio.Dispose();
            _studio = null;
        }
        _databaseIntegration = null;
        _activeEventBinding = null;
        return ValueTask.CompletedTask;
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.FileSaveProject && IsDirty)
            return EditorCommandStatus.Enabled("Save Text Studio", "Save the active recipe/corpus authoring changes.");
        if (command == EditorCommandIds.SessionApply && IsDirty)
            return EditorCommandStatus.Enabled("Apply Text Studio Changes");
        if (command == EditorCommandIds.SessionRevert && IsDirty)
            return EditorCommandStatus.Enabled("Discard Text Studio Changes");
        if (command == EditorCommandIds.ViewRefresh)
            return EditorCommandStatus.Enabled("Refresh Text Studio", "Reload project recipes/corpora and refresh the project context.");
        if (command == EditorCommandIds.EditFind)
            return EditorCommandStatus.Enabled("Find Text Recipe");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (_studio is null) return;
        if ((command == EditorCommandIds.FileSaveProject || command == EditorCommandIds.SessionApply) && IsDirty)
        {
            await SaveAuthoringAsync(context.CancellationToken).ConfigureAwait(true);
        }
        else if (command == EditorCommandIds.SessionRevert && IsDirty)
        {
            await _studio.DiscardUnsavedChangesAsync(context.CancellationToken).ConfigureAwait(true);
            UpdateWindowState();
        }
        else if (command == EditorCommandIds.ViewRefresh)
        {
            await RefreshStudioAsync(context.CancellationToken).ConfigureAwait(true);
        }
        else if (command == EditorCommandIds.EditFind)
        {
            _studio.FocusSearch();
        }
        else
        {
            await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        }
        NotifyCommandStateChanged();
    }

    public ValueTask<ShutdownContribution?> PrepareShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_studio?.HasUnsavedChanges != true) return ValueTask.FromResult<ShutdownContribution?>(null);
        TextStudioLocalRecoveryState? recovery = _studio.CaptureLocalRecoveryState();
        int pending = (recovery?.Recipe is null ? 0 : 1) + (recovery?.Corpus is null ? 0 : 1) + (recovery?.Voice is null ? 0 : 1) + (recovery?.Storylet is null ? 0 : 1);
        return ValueTask.FromResult<ShutdownContribution?>(new ShutdownContribution(
            Key,
            Title,
            pending == 1
                ? "Text Studio contains one unsaved authoring document."
                : $"Text Studio contains {pending} unsaved authoring documents.",
            CanApply: true,
            CanDiscard: true,
            CanRecover: true,
            PendingItemCount: Math.Max(1, pending)));
    }

    public async ValueTask<bool> CommitShutdownAsync(
        ShutdownLocalAction action,
        CancellationToken cancellationToken = default)
    {
        if (_studio is null || !_studio.HasUnsavedChanges) return true;
        try
        {
            switch (action)
            {
                case ShutdownLocalAction.Apply:
                    await _studio.SaveAllAsync(cancellationToken).ConfigureAwait(true);
                    return !_studio.HasUnsavedChanges;
                case ShutdownLocalAction.Discard:
                    await _studio.DiscardUnsavedChangesAsync(cancellationToken).ConfigureAwait(true);
                    return !_studio.HasUnsavedChanges;
                case ShutdownLocalAction.PreserveRecovery:
                    // Keep the in-memory drafts unchanged. The recovery coordinator captures
                    // them after this commit phase and before project-scoped windows are closed.
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not resolve Text Studio changes", exception.Message, exception);
            _output.Write("Text Studio", "Could not resolve local Text Studio changes during project shutdown.", DiagnosticSeverity.Error, exception.ToString());
            return false;
        }
        finally
        {
            UpdateWindowState();
        }
    }

    public ValueTask<PrivateSessionRecoveryState?> CaptureRecoveryStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_studio?.CaptureLocalRecoveryState() is not { } state)
            return ValueTask.FromResult<PrivateSessionRecoveryState?>(null);
        string json = JsonSerializer.Serialize(state, RecipeJson.Options);
        PrivateSessionRecoveryState recovery = new(
            RecoverySessionType,
            Request,
            Title,
            json,
            BaselineFingerprint: _studio.ProjectContext.ProjectRoot,
            CapturedUtc: DateTimeOffset.UtcNow,
            Version: TextStudioLocalRecoveryState.CurrentVersion);
        return ValueTask.FromResult<PrivateSessionRecoveryState?>(recovery);
    }

    public ValueTask RestoreRecoveryStateAsync(
        PrivateSessionRecoveryState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        if (_studio is null) throw new InvalidOperationException("Text Studio has not finished loading.");
        if (!string.Equals(state.SessionType, RecoverySessionType, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported Text Studio recovery session type '{state.SessionType}'.");
        TextStudioLocalRecoveryState recovery = JsonSerializer.Deserialize<TextStudioLocalRecoveryState>(state.DataJson, RecipeJson.Options)
            ?? throw new JsonException("The recovered Text Studio session was empty.");
        _studio.RestoreLocalRecoveryState(recovery);
        _workspace.RequestRecoverySnapshot();
        UpdateWindowState();
        _notifications.Information("Text Studio recovered", "Unsaved recipe/corpus/voice/storylet/quest/optional-AI authoring work was restored.");
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> CaptureWorkspaceStateJsonAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_studio is null) return ValueTask.FromResult<string?>(null);
        return ValueTask.FromResult<string?>(JsonSerializer.Serialize(_studio.CaptureWorkspaceState(), RecipeJson.Options));
    }

    public ValueTask RestoreWorkspaceStateJsonAsync(
        string? stateJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_studio is null || string.IsNullOrWhiteSpace(stateJson)) return ValueTask.CompletedTask;
        try
        {
            TextStudioWorkspaceState? state = JsonSerializer.Deserialize<TextStudioWorkspaceState>(stateJson, RecipeJson.Options);
            if (state is not null) _studio.RestoreWorkspaceState(state);
        }
        catch (JsonException exception)
        {
            _output.Write("Text Studio", "Ignored invalid saved Text Studio workspace state.", DiagnosticSeverity.Warning, exception.ToString());
        }
        return ValueTask.CompletedTask;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.S) && _studio is not null)
        {
            _ = SaveAuthoringAsync();
            return true;
        }
        if (keyData == Keys.F5 && _studio is not null)
        {
            _ = RefreshStudioAsync();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F) && _studio is not null)
        {
            _studio.FocusSearch();
            return true;
        }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task SaveAuthoringAsync(CancellationToken cancellationToken = default)
    {
        if (_studio is null || !_studio.HasUnsavedChanges) return;
        try
        {
            await _studio.SaveAllAsync(cancellationToken).ConfigureAwait(true);
            if (!_studio.HasUnsavedChanges)
            {
                _notifications.Success("Text Studio saved", "Recipe, corpus, voice, and storylet authoring changes were saved.");
                _output.Write("Text Studio", "Saved Text Studio authoring changes.");
            }
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not save Text Studio", exception.Message, exception);
            _output.Write("Text Studio", "Text Studio save failed.", DiagnosticSeverity.Error, exception.ToString());
        }
        finally
        {
            UpdateWindowState();
        }
    }

    private async Task RefreshStudioAsync(CancellationToken cancellationToken = default)
    {
        if (_studio is null) return;
        try
        {
            if (!await _studio.RefreshFromDiskAsync(promptForDirtyChanges: true, cancellationToken).ConfigureAwait(true)) return;
            if (_workspace.CurrentSession is { } session)
                _studio.ApplyProjectContext(TextStudioProjectContextFactory.Create(session, ++_contextRevision));
            _output.Write("Text Studio", "Refreshed recipes, corpora and project context.");
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not refresh Text Studio", exception.Message, exception);
            _output.Write("Text Studio", "Text Studio refresh failed.", DiagnosticSeverity.Error, exception.ToString());
        }
        finally
        {
            UpdateWindowState();
        }
    }

    private void SubscribeWorkspace()
    {
        if (_workspaceSubscribed) return;
        _workspaceSubscribed = true;
        _workspace.StateChanged += OnWorkspaceStateChanged;
    }

    private void UnsubscribeWorkspace()
    {
        if (!_workspaceSubscribed) return;
        _workspaceSubscribed = false;
        _workspace.StateChanged -= OnWorkspaceStateChanged;
    }

    private void OnWorkspaceStateChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (IsDisposed || _studio is null) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnWorkspaceStateChanged(sender, e)));
            return;
        }
        if (_workspace.CurrentSession is not { } session) return;
        try
        {
            _studio.ApplyProjectContext(TextStudioProjectContextFactory.Create(session, ++_contextRevision));
            UpdateWindowState();
        }
        catch (Exception exception)
        {
            _output.Write("Text Studio", "Could not refresh project-aware Text Studio context.", DiagnosticSeverity.Warning, exception.ToString());
        }
    }

    private async void OnImportRequested(object? sender, TextStudioImportRequestedEventArgs e)
    {
        if (_studio is null || _databaseIntegration is null) return;
        try
        {
            if ((_activeEventBinding is { } eventBinding && ReferenceEquals(eventBinding.Target, e.Target))
                || (_activeEventBinding is { } matching
                    && matching.Target.AdapterKind.Equals(e.Target.AdapterKind, StringComparison.OrdinalIgnoreCase)
                    && matching.Target.RecordId.Equals(e.Target.RecordId, StringComparison.OrdinalIgnoreCase)))
            {
                TextStudioEventBinding binding = _activeEventBinding!;
                using var eventDialog = new TextStudioEventImportPreviewDialog(binding, e.Result.Bundle);
                if (eventDialog.ShowDialog(this) != DialogResult.OK || eventDialog.Plan is not { } eventPlan) return;
                binding.Apply(eventPlan);
                var selected = binding.Target.SelectedField;
                TextRecordTargetDescriptor refreshed = binding.Target with
                {
                    Fields = binding.Target.Fields.Select(field => selected is not null && field.SemanticField.Equals(selected.SemanticField, StringComparison.OrdinalIgnoreCase)
                        ? field with { CurrentValue = eventPlan.PreviewText }
                        : field).ToArray()
                };
                _activeEventBinding = binding with { Target = refreshed };
                _studio.ActivateDatabaseTarget(refreshed);
                _notifications.Success("Generated event text imported", $"Applied generated {binding.Kind} as one local Event Editor undo step.");
                _output.Write("Text Studio", $"Generated {binding.Kind} compiled to {eventPlan.RawCommands.Count} MV command row(s) and applied to EventEditSession.");
                return;
            }

            using var dialog = new TextStudioImportPreviewDialog(_databaseIntegration, e.Target, e.Result.Bundle);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Plan is not { } plan) return;
            await _databaseIntegration.ApplyAsync(plan).ConfigureAwait(true);
            MvDocumentKind kind = e.Target.AdapterKind.StartsWith("database.", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(e.Target.AdapterKind["database.".Length..], ignoreCase: true, out MvDocumentKind parsedKind)
                    ? parsedKind
                    : MvDocumentKind.Unknown;
            if (kind != MvDocumentKind.Unknown && int.TryParse(e.Target.RecordId, out int sourceRecordId))
            {
                int recordId = plan.Creation?.RecordId ?? sourceRecordId;
                TextRecordTargetDescriptor refreshed = await _databaseIntegration.BuildTargetAsync(
                    kind, e.Target.DataFile, recordId, e.Target.SelectedField?.FieldKey).ConfigureAwait(true);
                _studio.ActivateDatabaseTarget(refreshed);
            }
            else
            {
                var changed = plan.Changes.ToDictionary(change => change.SemanticField, StringComparer.OrdinalIgnoreCase);
                TextRecordTargetDescriptor refreshed = e.Target with
                {
                    Fields = e.Target.Fields.Select(field => changed.TryGetValue(field.SemanticField, out var change)
                        ? field with { CurrentValue = change.NewValue }
                        : field).ToArray()
                };
                _studio.ActivateDatabaseTarget(refreshed);
            }
            _notifications.Success("Generated text imported", $"Applied {plan.Changes.Count} generated field change{(plan.Changes.Count == 1 ? string.Empty : "s")} as one undoable transaction.");
            _output.Write("Text Studio", $"{plan.Description}: {plan.Changes.Count} field change(s) committed through ProjectTransaction.");
            if (_workspace.CurrentSession is { } session)
                _studio.ApplyProjectContext(TextStudioProjectContextFactory.Create(session, ++_contextRevision));
            UpdateWindowState();
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not import generated text", exception.Message, exception);
            _output.Write("Text Studio", "Generated text import failed.", DiagnosticSeverity.Error, exception.ToString());
            MessageBox.Show(this, exception.Message, "Text Studio import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnStudioDirtyStateChanged(object? sender, EventArgs e)
    {
        UpdateWindowState();
        _workspace.RequestRecoverySnapshot();
    }

    private void OnStudioStatusChanged(object? sender, TextStudioStatusChangedEventArgs e)
    {
        if (e.IsError)
            _output.Write("Text Studio", e.Message, DiagnosticSeverity.Error);
    }

    private void UpdateWindowState()
    {
        Text = Title;
        RefreshWindowState();
    }
}
