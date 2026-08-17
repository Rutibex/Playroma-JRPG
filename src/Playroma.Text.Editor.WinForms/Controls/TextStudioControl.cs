using System.Globalization;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Random;
using Playroma.Text.RpgMakerMV;
using Playroma.Text.Core.Dialogue;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class TextStudioControl : UserControl
{
    private readonly TextStudioSession _session;
    private readonly TextBox _search = new() { PlaceholderText = "Search recipes…", Dock = DockStyle.Top };
    private readonly ComboBox _category = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox _recipes = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _recipeInfo = new() { Dock = DockStyle.Bottom, Height = 72, AutoEllipsis = true, Padding = new Padding(6) };
    private readonly FlowLayoutPanel _results = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(8) };
    private readonly ParameterEditorControl _parameters = new() { Dock = DockStyle.Fill };
    private readonly TraceInspectorControl _trace = new() { Dock = DockStyle.Fill };
    private readonly MvMessagePreviewControl _mvPreview = new() { Dock = DockStyle.Fill };
    private readonly ProjectContextControl _projectContext = new() { Dock = DockStyle.Fill };
    private readonly RecipeDesignerControl _designer = new() { Dock = DockStyle.Fill };
    private readonly CorpusLabControl _corpusLab;
    private readonly DialogueVoiceLabControl _voiceLab;
    private readonly StoryletLabControl _storyletLab;
    private readonly QuestLabControl _questLab;
    private readonly BatchContentBuilderControl _batchBuilder;
    private readonly AiProviderLabControl _aiLab;
    private readonly TextProjectDoctorControl _projectDoctor;
    private readonly DialogueVoiceContextControl _voiceContext;
    private readonly ListBox _favorites = new() { Dock = DockStyle.Fill };
    private readonly ListBox _history = new() { Dock = DockStyle.Fill };
    private readonly TextBox _seed = new() { Width = 175 };
    private readonly CheckBox _seedLocked = new() { Text = "Lock Seed", AutoSize = true, Margin = new Padding(5, 7, 5, 0) };
    private readonly ToolStripButton _generate = new("Generate");
    private readonly ToolStripButton _generate20 = new("Generate 20");
    private readonly ToolStripButton _surprise = new("🎲 Surprise Me");
    private readonly ToolStripButton _cancel = new("Stop") { Enabled = false };
    private readonly ToolStripButton _advanced = new("Recipe Designer") { CheckOnClick = true };
    private readonly ToolStripButton _corpusButton = new("Corpus Lab") { CheckOnClick = true };
    private readonly ToolStripButton _voiceButton = new("Voice Lab") { CheckOnClick = true };
    private readonly ToolStripButton _storyletButton = new("Storylet Lab") { CheckOnClick = true };
    private readonly ToolStripButton _questButton = new("Quest Lab") { CheckOnClick = true };
    private readonly ToolStripButton _batchButton = new("Batch Builder") { CheckOnClick = true };
    private readonly ToolStripButton _aiButton = new("AI Providers") { CheckOnClick = true };
    private readonly ToolStripButton _doctorButton = new("Project Doctor") { CheckOnClick = true };
    private readonly ToolStripLabel _status = new("Ready") { TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripLabel _projectBadge = new("Project context") { Alignment = ToolStripItemAlignment.Right };
    private readonly ToolStripLabel _targetBadge = new("No project target") { Alignment = ToolStripItemAlignment.Right };
    private readonly Label _targetSummary = new() { Dock = DockStyle.Top, Height = 58, Padding = new Padding(8), AutoEllipsis = true, Text = "Open Text Studio from a database field, map property, or event command to import generated content directly." };
    private readonly ListView _targetFields = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly TabControl _centerTabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _playgroundTab = new("Playground");
    private readonly TabPage _designerTab = new("Recipe Designer");
    private readonly TabPage _corpusTab = new("Corpus Lab");
    private readonly TabPage _voiceTab = new("Voice Lab");
    private readonly TabPage _storyletTab = new("Storylet Lab");
    private readonly TabPage _questTab = new("Quest Lab");
    private readonly TabPage _batchTab = new("Batch Content Builder");
    private readonly TabPage _aiTab = new("Optional AI Providers");
    private readonly TabPage _doctorTab = new("Project Doctor");
    private TextStudioResultItem? _inspected;
    private bool _initializing;

    public TextStudioControl(TextStudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _session = new TextStudioSession(options);
        _corpusLab = new CorpusLabControl(options.ProjectRoot);
        _voiceLab = new DialogueVoiceLabControl(options.ProjectRoot);
        _storyletLab = new StoryletLabControl(_session);
        _questLab = new QuestLabControl(_session);
        _batchBuilder = new BatchContentBuilderControl(_session);
        _aiLab = new AiProviderLabControl(_session);
        _projectDoctor = new TextProjectDoctorControl(options.ProjectRoot);
        _voiceContext = new DialogueVoiceContextControl(options.ProjectRoot);
        Dock = DockStyle.Fill;
        MinimumSize = new Size(900, 600);
        AccessibleName = "Playroma Text Studio";
        AccessibleDescription = "Procedural RPG text authoring, dialogue, narrative planning, batching, optional AI, and release diagnostics.";
        _search.AccessibleName = "Search text recipes"; _category.AccessibleName = "Recipe category"; _recipes.AccessibleName = "Text recipes";
        _generate.ToolTipText = "Generate one result (Ctrl+Enter)"; _generate20.ToolTipText = "Generate twenty results (Ctrl+Shift+Enter)"; _cancel.ToolTipText = "Cancel generation (Escape)";

        var toolbar = BuildToolbar();
        var left = BuildRecipeBrowser();
        var right = BuildRightPanel();

        _playgroundTab.Controls.Add(_results);
        _designerTab.Controls.Add(_designer);
        _corpusTab.Controls.Add(_corpusLab);
        _voiceTab.Controls.Add(_voiceLab);
        _storyletTab.Controls.Add(_storyletLab);
        _questTab.Controls.Add(_questLab);
        _batchTab.Controls.Add(_batchBuilder);
        _aiTab.Controls.Add(_aiLab);
        _doctorTab.Controls.Add(_projectDoctor);
        _centerTabs.TabPages.AddRange([_playgroundTab, _designerTab, _corpusTab, _voiceTab, _storyletTab, _questTab, _batchTab, _aiTab, _doctorTab]);

        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 245, FixedPanel = FixedPanel.Panel1 };
        main.Panel1.Controls.Add(left);
        var inner = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 760 };
        inner.Panel1.Controls.Add(_centerTabs); inner.Panel2.Controls.Add(right);
        main.Panel2.Controls.Add(inner);
        Controls.Add(main); Controls.Add(toolbar);

        _parameters.Bind(_session);
        _session.RecipesChanged += (_, _) => Ui(RebuildRecipeBrowser);
        _session.SelectionChanged += (_, _) => Ui(OnRecipeSelectionChanged);
        _session.ResultsChanged += (_, _) => Ui(RebuildResults);
        _session.StateChanged += (_, _) => Ui(UpdateCommandState);
        _recipes.SelectedIndexChanged += (_, _) => { if (!_initializing) _session.SelectRecipe(_recipes.SelectedItem as TextStudioRecipeEntry); };
        _search.TextChanged += (_, _) => RebuildRecipeBrowser();
        _category.SelectedIndexChanged += (_, _) => RebuildRecipeBrowser();
        _seed.TextChanged += (_, _) => { if (ulong.TryParse(_seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) _session.Seed = value; };
        _seedLocked.CheckedChanged += (_, _) => _session.SeedLocked = _seedLocked.Checked;
        _generate.Click += async (_, _) => await GenerateAsync(1).ConfigureAwait(true);
        _generate20.Click += async (_, _) => await GenerateAsync(20).ConfigureAwait(true);
        _surprise.Click += async (_, _) => await SurpriseAsync().ConfigureAwait(true);
        _cancel.Click += (_, _) => _session.CancelGeneration();
        _advanced.CheckedChanged += (_, _) => { if (_advanced.Checked) _centerTabs.SelectedTab = _designerTab; else if (_centerTabs.SelectedTab == _designerTab) _centerTabs.SelectedTab = _playgroundTab; };
        _corpusButton.CheckedChanged += (_, _) => { if (_corpusButton.Checked) _centerTabs.SelectedTab = _corpusTab; else if (_centerTabs.SelectedTab == _corpusTab) _centerTabs.SelectedTab = _playgroundTab; };
        _voiceButton.CheckedChanged += (_, _) => { if (_voiceButton.Checked) _centerTabs.SelectedTab = _voiceTab; else if (_centerTabs.SelectedTab == _voiceTab) _centerTabs.SelectedTab = _playgroundTab; };
        _storyletButton.CheckedChanged += (_, _) => { if (_storyletButton.Checked) _centerTabs.SelectedTab = _storyletTab; else if (_centerTabs.SelectedTab == _storyletTab) _centerTabs.SelectedTab = _playgroundTab; };
        _questButton.CheckedChanged += (_, _) => { if (_questButton.Checked) _centerTabs.SelectedTab = _questTab; else if (_centerTabs.SelectedTab == _questTab) _centerTabs.SelectedTab = _playgroundTab; };
        _batchButton.CheckedChanged += (_, _) => { if (_batchButton.Checked) _centerTabs.SelectedTab = _batchTab; else if (_centerTabs.SelectedTab == _batchTab) _centerTabs.SelectedTab = _playgroundTab; };
        _aiButton.CheckedChanged += (_, _) => { if (_aiButton.Checked) _centerTabs.SelectedTab = _aiTab; else if (_centerTabs.SelectedTab == _aiTab) _centerTabs.SelectedTab = _playgroundTab; };
        _doctorButton.CheckedChanged += (_, _) => { if (_doctorButton.Checked) _centerTabs.SelectedTab = _doctorTab; else if (_centerTabs.SelectedTab == _doctorTab) _centerTabs.SelectedTab = _playgroundTab; };
        _centerTabs.SelectedIndexChanged += (_, _) => { _advanced.Checked = _centerTabs.SelectedTab == _designerTab; _corpusButton.Checked = _centerTabs.SelectedTab == _corpusTab; _voiceButton.Checked = _centerTabs.SelectedTab == _voiceTab; _storyletButton.Checked = _centerTabs.SelectedTab == _storyletTab; _questButton.Checked = _centerTabs.SelectedTab == _questTab; _batchButton.Checked = _centerTabs.SelectedTab == _batchTab; _aiButton.Checked = _centerTabs.SelectedTab == _aiTab; _doctorButton.Checked = _centerTabs.SelectedTab == _doctorTab; };
        _designer.SaveRequested += async (_, _) => await SaveCurrentRecipeAsync().ConfigureAwait(true);
        _designer.DirtyStateChanged += (_, _) => DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _corpusLab.DirtyStateChanged += (_, _) => DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _voiceLab.DirtyStateChanged += (_, _) => DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _storyletLab.DirtyStateChanged += (_, _) => DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _storyletLab.PlaygroundResultReady += (_, _) => { _centerTabs.SelectedTab = _playgroundTab; RebuildResults(); };
        _questLab.DirtyStateChanged += (_, _) => DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _questLab.PlaygroundResultReady += (_, _) => { _centerTabs.SelectedTab = _playgroundTab; RebuildResults(); };
        _batchBuilder.StatusChanged += (_, e) => SetStatus(e.Message, e.IsError);
        _aiLab.DirtyStateChanged += (_, _) => DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _voiceLab.ProfilesChanged += async (_, _) => await _voiceContext.ReloadProfilesAsync().ConfigureAwait(true);
        _results.Resize += (_, _) => ResizeResultCards();
        _favorites.DoubleClick += (_, _) => CopySelectedArchiveText(_favorites);
        _history.DoubleClick += (_, _) => CopySelectedArchiveText(_history);
    }

    public bool HasUnsavedChanges => _designer.Document?.IsDirty == true || _corpusLab.HasUnsavedChanges || _voiceLab.HasUnsavedChanges || _storyletLab.HasUnsavedChanges || _questLab.HasUnsavedChanges || _aiLab.HasUnsavedChanges;
    public bool IsGenerating => _session.IsGenerating;
    public TextStudioProjectContextSnapshot ProjectContext => _session.ProjectContext;

    public event EventHandler? DirtyStateChanged;
    public event EventHandler<TextStudioStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<TextStudioImportRequestedEventArgs>? ImportRequested;

    public TextRecordTargetDescriptor? ActiveTarget => _session.ActiveTarget;

    public void ActivateDatabaseTarget(TextRecordTargetDescriptor target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _session.ActivateTarget(target);
        _centerTabs.SelectedTab = _playgroundTab;
        RebuildTargetPanel();
        RebuildRecipeBrowser();
        OnRecipeSelectionChanged();
        SetStatus($"Project target: {target.DisplayName} / {target.SelectedField?.DisplayName}");
    }

    public void ClearDatabaseTarget()
    {
        _session.ActivateTarget(null);
        RebuildTargetPanel();
        SetStatus("Database target cleared.");
    }

    public void ApplyDialogueContext(DialogueVoiceContext context, string? preferredRecipeId = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        _session.ApplyDialogueContext(context);
        _voiceContext.SetContext(context);
        if (!string.IsNullOrWhiteSpace(preferredRecipeId)) _session.SelectRecipeById(preferredRecipeId);
        _centerTabs.SelectedTab = _playgroundTab;
        RebuildRecipeBrowser();
        OnRecipeSelectionChanged();
        SetStatus($"Character voice: {context.SpeakerName ?? "speaker"} / {context.ProfileId ?? "common"} / {context.Act}");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initializing = true;
        try
        {
            SetStatus("Loading recipes…");
            await _session.InitializeAsync(cancellationToken).ConfigureAwait(true);
            await _corpusLab.InitializeAsync(cancellationToken).ConfigureAwait(true);
            await _voiceLab.InitializeAsync(cancellationToken).ConfigureAwait(true);
            await _storyletLab.InitializeAsync(cancellationToken).ConfigureAwait(true);
            await _questLab.InitializeAsync(cancellationToken).ConfigureAwait(true);
            await _aiLab.InitializeAsync(cancellationToken).ConfigureAwait(true);
            await _voiceContext.InitializeAsync(_session, cancellationToken).ConfigureAwait(true);
            _seed.Text = _session.Seed.ToString(CultureInfo.InvariantCulture);
            RebuildCategories(); RebuildRecipeBrowser(); OnRecipeSelectionChanged(); RebuildArchiveLists();
            _projectContext.Bind(_session.ProjectContext);
            _projectBadge.Text = _session.ProjectContext.ProjectTitle;
            RebuildTargetPanel();
            SetStatus($"Ready — {_session.Recipes.Count} recipes");
        }
        finally { _initializing = false; }
    }

    public async Task SaveCurrentRecipeAsync(CancellationToken cancellationToken = default)
    {
        var recipe = _designer.RecipeForSave;
        if (recipe is null) return;
        var diagnostics = TextRecipeValidator.Validate(recipe);
        if (diagnostics.Any(x => x.Severity == GenerationDiagnosticSeverity.Error))
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, diagnostics.Where(x => x.Severity == GenerationDiagnosticSeverity.Error).Select(x => x.Message)),
                "Recipe cannot be saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            var path = await _session.SaveRecipeAsync(recipe, _designer.ExistingPathForSave, cancellationToken).ConfigureAwait(true);
            SetStatus("Saved " + Path.GetFileName(path));
            RebuildCategories(); RebuildRecipeBrowser();
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            SetStatus("Could not save recipe: " + ex.Message, true);
            MessageBox.Show(this, ex.Message, "Could not save recipe", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public async Task SaveAllAsync(CancellationToken cancellationToken = default)
    {
        if (_designer.Document?.IsDirty == true) await SaveCurrentRecipeAsync(cancellationToken).ConfigureAwait(true);
        if (_corpusLab.HasUnsavedChanges) await _corpusLab.SaveCurrentAsync(cancellationToken).ConfigureAwait(true);
        if (_voiceLab.HasUnsavedChanges) await _voiceLab.SaveCurrentAsync(cancellationToken).ConfigureAwait(true);
        if (_storyletLab.HasUnsavedChanges) await _storyletLab.SaveCurrentAsync(cancellationToken).ConfigureAwait(true);
        if (_questLab.HasUnsavedChanges) await _questLab.SaveCurrentAsync(cancellationToken).ConfigureAwait(true);
        if (_aiLab.HasUnsavedChanges) await _aiLab.SaveAsync(cancellationToken).ConfigureAwait(true);
        await _session.SaveStateAsync(cancellationToken).ConfigureAwait(true);
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        if (!HasUnsavedChanges) SetStatus("Text Studio authoring changes saved.");
    }

    public async Task DiscardUnsavedChangesAsync(CancellationToken cancellationToken = default)
    {
        _designer.DiscardUnsavedChanges();
        if (_corpusLab.HasUnsavedChanges) await _corpusLab.DiscardUnsavedChangesAsync(cancellationToken).ConfigureAwait(true);
        if (_voiceLab.HasUnsavedChanges) _voiceLab.DiscardUnsavedChanges();
        if (_storyletLab.HasUnsavedChanges) _storyletLab.DiscardUnsavedChanges();
        if (_questLab.HasUnsavedChanges) _questLab.DiscardUnsavedChanges();
        if (_aiLab.HasUnsavedChanges) _aiLab.DiscardUnsavedChanges();
        OnRecipeSelectionChanged();
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("Unsaved Text Studio authoring changes discarded.");
    }

    public async Task<bool> RefreshFromDiskAsync(bool promptForDirtyChanges = true, CancellationToken cancellationToken = default)
    {
        if (HasUnsavedChanges && promptForDirtyChanges)
        {
            var choice = MessageBox.Show(this, "Text Studio has unsaved recipe, corpus, voice, storylet, quest, or optional-AI settings. Discard them and reload project Text Studio assets?",
                "Refresh Text Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) return false;
        }
        if (HasUnsavedChanges) await DiscardUnsavedChangesAsync(cancellationToken).ConfigureAwait(true);
        var selectedId = _session.SelectedRecipe?.Recipe.Id;
        await _session.ReloadRecipesAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(selectedId)) _session.SelectRecipeById(selectedId);
        await _corpusLab.ReloadFromDiskAsync(discardUnsavedChanges: false, cancellationToken).ConfigureAwait(true);
        await _voiceLab.ReloadFromDiskAsync(discardUnsavedChanges: false, cancellationToken).ConfigureAwait(true);
        await _storyletLab.ReloadFromDiskAsync(discardUnsavedChanges: false, cancellationToken).ConfigureAwait(true);
        await _questLab.ReloadFromDiskAsync(discardUnsavedChanges: false, cancellationToken).ConfigureAwait(true);
        await _aiLab.InitializeAsync(cancellationToken).ConfigureAwait(true);
        await _voiceContext.ReloadProfilesAsync(cancellationToken).ConfigureAwait(true);
        RebuildCategories(); RebuildRecipeBrowser(); OnRecipeSelectionChanged(); RebuildArchiveLists();
        SetStatus($"Refreshed — {_session.Recipes.Count} recipes");
        return true;
    }

    public void ApplyProjectContext(TextStudioProjectContextSnapshot context)
    {
        _session.UpdateProjectContext(context);
        _projectContext.Bind(context);
        _projectBadge.Text = context.ProjectTitle;
        _parameters.Bind(_session);
        SetStatus($"Project context refreshed — {context.EffectiveSummary.Count} values");
    }

    public TextStudioLocalRecoveryState? CaptureLocalRecoveryState()
    {
        var recipe = _designer.CaptureRecoveryState();
        var corpus = _corpusLab.CaptureRecoveryState();
        var voice = _voiceLab.CaptureRecoveryState();
        var storylet = _storyletLab.CaptureRecoveryState();
        var quest = _questLab.CaptureRecoveryState();
        var ai = _aiLab.CaptureRecoveryState();
        if (recipe is null && corpus is null && voice is null && storylet is null && quest is null && ai is null) return null;
        return new TextStudioLocalRecoveryState(TextStudioLocalRecoveryState.CurrentVersion, recipe, corpus, CaptureWorkspaceState(), voice, storylet, quest, ai);
    }

    public void RestoreLocalRecoveryState(TextStudioLocalRecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        RestoreWorkspaceState(state.Workspace);
        if (state.Recipe is not null)
        {
            _designer.RestoreRecoveryState(state.Recipe);
            _centerTabs.SelectedTab = _designerTab;
        }
        if (state.Corpus is not null)
        {
            _corpusLab.RestoreRecoveryState(state.Corpus);
            if (state.Recipe is null) _centerTabs.SelectedTab = _corpusTab;
        }
        if (state.Voice is not null)
        {
            _voiceLab.RestoreRecoveryState(state.Voice);
            if (state.Recipe is null && state.Corpus is null) _centerTabs.SelectedTab = _voiceTab;
        }
        if (state.Storylet is not null)
        {
            _storyletLab.RestoreRecoveryState(state.Storylet);
            if (state.Recipe is null && state.Corpus is null && state.Voice is null) _centerTabs.SelectedTab = _storyletTab;
        }
        if (state.Quest is not null)
        {
            _questLab.RestoreRecoveryState(state.Quest);
            if (state.Recipe is null && state.Corpus is null && state.Voice is null && state.Storylet is null) _centerTabs.SelectedTab = _questTab;
        }
        if (state.AiProviders is not null)
        {
            _aiLab.RestoreRecoveryState(state.AiProviders);
            if (state.Recipe is null && state.Corpus is null && state.Voice is null && state.Storylet is null && state.Quest is null) _centerTabs.SelectedTab = _aiTab;
        }
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("Recovered unsaved Text Studio authoring session.");
    }

    public TextStudioWorkspaceState CaptureWorkspaceState() => new(
        1,
        _session.SelectedRecipe?.Recipe.Id,
        _search.Text,
        _category.SelectedItem?.ToString(),
        _centerTabs.SelectedTab == _designerTab ? TextStudioWorkspaceMode.RecipeDesigner : _centerTabs.SelectedTab == _corpusTab ? TextStudioWorkspaceMode.CorpusLab : _centerTabs.SelectedTab == _voiceTab ? TextStudioWorkspaceMode.VoiceLab : _centerTabs.SelectedTab == _storyletTab ? TextStudioWorkspaceMode.StoryletLab : _centerTabs.SelectedTab == _questTab ? TextStudioWorkspaceMode.QuestLab : _centerTabs.SelectedTab == _batchTab ? TextStudioWorkspaceMode.BatchBuilder : _centerTabs.SelectedTab == _aiTab ? TextStudioWorkspaceMode.AiProviders : _centerTabs.SelectedTab == _doctorTab ? TextStudioWorkspaceMode.ProjectDoctor : TextStudioWorkspaceMode.Playground,
        _session.Seed,
        _session.SeedLocked);

    public void RestoreWorkspaceState(TextStudioWorkspaceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _search.Text = state.SearchText ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(state.Category)) _category.SelectedItem = _category.Items.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), state.Category, StringComparison.OrdinalIgnoreCase)) ?? "All";
        if (state.Seed is { } seed) { _session.Seed = seed; _seed.Text = seed.ToString(CultureInfo.InvariantCulture); }
        _session.SeedLocked = state.SeedLocked; _seedLocked.Checked = state.SeedLocked;
        if (!string.IsNullOrWhiteSpace(state.SelectedRecipeId)) _session.SelectRecipeById(state.SelectedRecipeId);
        _centerTabs.SelectedTab = state.Mode switch { TextStudioWorkspaceMode.RecipeDesigner => _designerTab, TextStudioWorkspaceMode.CorpusLab => _corpusTab, TextStudioWorkspaceMode.VoiceLab => _voiceTab, TextStudioWorkspaceMode.StoryletLab => _storyletTab, TextStudioWorkspaceMode.QuestLab => _questTab, TextStudioWorkspaceMode.BatchBuilder => _batchTab, TextStudioWorkspaceMode.AiProviders => _aiTab, TextStudioWorkspaceMode.ProjectDoctor => _doctorTab, _ => _playgroundTab };
        RebuildRecipeBrowser();
    }

    public void FocusSearch() { _search.Focus(); _search.SelectAll(); }
    public void OpenStoryletLab() { _centerTabs.SelectedTab = _storyletTab; _storyletButton.Checked = true; }
    public void OpenQuestLab() { _centerTabs.SelectedTab = _questTab; _questButton.Checked = true; }
    public void OpenBatchBuilder() { _centerTabs.SelectedTab = _batchTab; _batchButton.Checked = true; }
    public void OpenAiProviders() { _centerTabs.SelectedTab = _aiTab; _aiButton.Checked = true; }
    public void OpenProjectDoctor() { _centerTabs.SelectedTab = _doctorTab; _doctorButton.Checked = true; }
    public void CancelGeneration() => _session.CancelGeneration();

    public async Task<bool> PrepareForCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_designer.Document?.IsDirty == true)
        {
            var choice = MessageBox.Show(this, "The Recipe Designer has unsaved changes. Save them before closing Text Studio?", "Playroma Text Studio",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) return false;
            if (choice == DialogResult.Yes)
            {
                await SaveCurrentRecipeAsync(cancellationToken).ConfigureAwait(true);
                if (_designer.Document?.IsDirty == true) return false;
            }
        }
        if (!await _corpusLab.PrepareForCloseAsync(cancellationToken).ConfigureAwait(true)) return false;
        if (_voiceLab.HasUnsavedChanges)
        {
            var voiceChoice = MessageBox.Show(this, "Voice Lab has unsaved changes. Save them before closing Text Studio?", "Playroma Text Studio", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (voiceChoice == DialogResult.Cancel) return false;
            if (voiceChoice == DialogResult.Yes)
            {
                await _voiceLab.SaveCurrentAsync(cancellationToken).ConfigureAwait(true);
                if (_voiceLab.HasUnsavedChanges) return false;
            }
            else _voiceLab.DiscardUnsavedChanges();
        }
        if (!await _storyletLab.PrepareForCloseAsync(cancellationToken).ConfigureAwait(true)) return false;
        if (!await _questLab.PrepareForCloseAsync(cancellationToken).ConfigureAwait(true)) return false;
        if (_aiLab.HasUnsavedChanges)
        {
            var aiChoice = MessageBox.Show(this, "Optional AI provider settings have unsaved changes. Save them before closing Text Studio?", "Playroma Text Studio", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (aiChoice == DialogResult.Cancel) return false;
            if (aiChoice == DialogResult.Yes) await _aiLab.SaveAsync(cancellationToken).ConfigureAwait(true); else _aiLab.DiscardUnsavedChanges();
        }
        await _session.SaveStateAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter)) { _ = GenerateAsync(1); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Enter)) { _ = GenerateAsync(20); return true; }
        if (keyData == Keys.Escape && _session.IsGenerating) { _session.CancelGeneration(); return true; }
        if (keyData == (Keys.Control | Keys.L)) { _seedLocked.Checked = !_seedLocked.Checked; return true; }
        if (keyData == (Keys.Control | Keys.D1)) { _centerTabs.SelectedTab = _playgroundTab; return true; }
        if (keyData == (Keys.Control | Keys.D2)) { _centerTabs.SelectedTab = _designerTab; return true; }
        if (keyData == (Keys.Control | Keys.D3)) { _centerTabs.SelectedTab = _corpusTab; return true; }
        if (keyData == (Keys.Control | Keys.D4)) { _centerTabs.SelectedTab = _voiceTab; return true; }
        if (keyData == (Keys.Control | Keys.D5)) { _centerTabs.SelectedTab = _storyletTab; return true; }
        if (keyData == (Keys.Control | Keys.D6)) { _centerTabs.SelectedTab = _questTab; return true; }
        if (keyData == (Keys.Control | Keys.D7)) { _centerTabs.SelectedTab = _batchTab; return true; }
        if (keyData == (Keys.Control | Keys.D8)) { _centerTabs.SelectedTab = _aiTab; return true; }
        if (keyData == (Keys.Control | Keys.D9)) { _centerTabs.SelectedTab = _doctorTab; return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _session.Dispose();
        base.Dispose(disposing);
    }

    private Control BuildRecipeBrowser()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var header = new Label { Text = "CREATE", Dock = DockStyle.Top, Height = 27, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 4, 0, 0) };
        panel.Controls.Add(_recipes); panel.Controls.Add(_recipeInfo); panel.Controls.Add(_search); panel.Controls.Add(_category); panel.Controls.Add(header);
        return panel;
    }

    private Control BuildRightPanel()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var input = new TabPage("Style & Inputs"); input.Controls.Add(_parameters);
        var voice = new TabPage("Character Voice"); voice.Controls.Add(_voiceContext);
        var target = new TabPage("Project Target");
        _targetFields.Columns.Add("Field", 135); _targetFields.Columns.Add("Current value", 310);
        target.Controls.Add(_targetFields); target.Controls.Add(_targetSummary);
        var mvPreview = new TabPage("MV Message Preview"); mvPreview.Controls.Add(_mvPreview);
        var trace = new TabPage("Why this result?"); trace.Controls.Add(_trace);
        var fav = new TabPage("★ Favorites"); fav.Controls.Add(_favorites);
        var history = new TabPage("History"); history.Controls.Add(_history);
        var context = new TabPage("Project Context"); context.Controls.Add(_projectContext);
        tabs.TabPages.AddRange([input, voice, target, mvPreview, context, trace, fav, history]);
        return tabs;
    }

    private ToolStrip BuildToolbar()
    {
        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Padding = new Padding(5, 3, 5, 3) };
        strip.Items.Add(_generate); strip.Items.Add(_generate20); strip.Items.Add(_surprise); strip.Items.Add(_cancel); strip.Items.Add(new ToolStripSeparator());
        var seedHost = new ToolStripControlHost(_seed) { ToolTipText = "64-bit deterministic seed" };
        var lockHost = new ToolStripControlHost(_seedLocked);
        var newSeed = new ToolStripButton("New Seed"); newSeed.Click += (_, _) => { _session.Seed = SeedFactory.CreateSeed(); _seed.Text = _session.Seed.ToString(CultureInfo.InvariantCulture); };
        strip.Items.Add(new ToolStripLabel("Seed")); strip.Items.Add(seedHost); strip.Items.Add(lockHost); strip.Items.Add(newSeed); strip.Items.Add(new ToolStripSeparator()); strip.Items.Add(_advanced); strip.Items.Add(_corpusButton); strip.Items.Add(_voiceButton); strip.Items.Add(_storyletButton); strip.Items.Add(_questButton); strip.Items.Add(_batchButton); strip.Items.Add(_aiButton); strip.Items.Add(_doctorButton); strip.Items.Add(_status); strip.Items.Add(_targetBadge); strip.Items.Add(_projectBadge);
        return strip;
    }

    private async Task GenerateAsync(int count)
    {
        SyncSeed(); SetStatus(count == 1 ? "Generating…" : $"Generating {count} candidates…");
        try { await _session.GenerateAsync(count).ConfigureAwait(true); SetStatus($"Generated {_session.Results.Count} result{(_session.Results.Count == 1 ? string.Empty : "s")}."); }
        catch (OperationCanceledException) { SetStatus("Generation cancelled."); }
        catch (Exception ex) { SetStatus("Generation failed: " + ex.Message, true); MessageBox.Show(this, ex.Message, "Text generation error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        RebuildArchiveLists();
    }

    private async Task SurpriseAsync()
    {
        SyncSeed(); SetStatus("Surprising you…");
        var category = _category.SelectedItem?.ToString(); if (string.Equals(category, "All", StringComparison.OrdinalIgnoreCase)) category = null;
        try { await _session.SurpriseMeAsync(category).ConfigureAwait(true); SetStatus($"Surprise: {_session.SelectedRecipe?.Recipe.Name}"); }
        catch (OperationCanceledException) { SetStatus("Generation cancelled."); }
        RebuildCategories(); RebuildRecipeBrowser(); RebuildArchiveLists();
    }

    private void SyncSeed()
    {
        if (!ulong.TryParse(_seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            parsed = SeedFactory.CreateSeed(); _seed.Text = parsed.ToString(CultureInfo.InvariantCulture);
        }
        _session.Seed = parsed; _session.SeedLocked = _seedLocked.Checked;
    }

    private void RebuildCategories()
    {
        var selected = _category.SelectedItem?.ToString() ?? "All";
        _category.BeginUpdate();
        try
        {
            _category.Items.Clear(); _category.Items.Add("All");
            foreach (var item in _session.Recipes.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)) _category.Items.Add(item);
            _category.SelectedItem = _category.Items.Cast<object>().FirstOrDefault(x => string.Equals(x.ToString(), selected, StringComparison.OrdinalIgnoreCase)) ?? "All";
        }
        finally { _category.EndUpdate(); }
    }

    private void RebuildRecipeBrowser()
    {
        if (_initializing && _session.Recipes.Count == 0) return;
        var selectedId = _session.SelectedRecipe?.Recipe.Id;
        var category = _category.SelectedItem?.ToString();
        var search = _search.Text.Trim();
        var items = _session.Recipes.Where(x => (string.IsNullOrWhiteSpace(category) || category == "All" || x.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) &&
            (search.Length == 0 || x.Recipe.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Recipe.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || (x.Recipe.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))).ToArray();
        TextStudioRecipeEntry? nextSelection = null;
        _initializing = true;
        try
        {
            _recipes.BeginUpdate(); _recipes.Items.Clear(); _recipes.Items.AddRange(items.Cast<object>().ToArray());
            var selected = items.FirstOrDefault(x => x.Recipe.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) _recipes.SelectedItem = selected;
            else if (_recipes.Items.Count > 0) { _recipes.SelectedIndex = 0; nextSelection = items[0]; }
            _recipes.EndUpdate();
        }
        finally { _initializing = false; }
        if (nextSelection is not null && !ReferenceEquals(_session.SelectedRecipe, nextSelection)) _session.SelectRecipe(nextSelection);
    }

    private void OnRecipeSelectionChanged()
    {
        var entry = _session.SelectedRecipe;
        if (entry is null) { _recipeInfo.Text = "No recipe selected."; _designer.Bind(null); return; }
        _recipeInfo.Text = $"{entry.Category} • {entry.Source}\r\n{entry.Recipe.Description ?? entry.Recipe.Id}";
        _designer.Bind(entry); RebuildResults();
        _initializing = true;
        try
        {
            foreach (var item in _recipes.Items.Cast<TextStudioRecipeEntry>()) if (item.Recipe.Id.Equals(entry.Recipe.Id, StringComparison.OrdinalIgnoreCase)) { _recipes.SelectedItem = item; break; }
        }
        finally { _initializing = false; }
    }

    private void RebuildResults()
    {
        _results.SuspendLayout();
        try
        {
            _results.Controls.Clear();
            if (_session.Results.Count == 0)
            {
                _results.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(Math.Max(400, _results.ClientSize.Width - 30), 0), Padding = new Padding(12),
                    Text = "Choose a generator and press Generate. Lock results you want to keep, use Vary to explore nearby alternatives, or press Surprise Me for a random generator." });
                _trace.Inspect(null); _mvPreview.Inspect(null, _session.ActiveTarget); return;
            }
            for (var i = 0; i < _session.Results.Count; i++) _results.Controls.Add(CreateResultCard(_session.Results[i], i + 1));
        }
        finally { _results.ResumeLayout(true); }
        ResizeResultCards(); RebuildArchiveLists();
    }

    private Control CreateResultCard(TextStudioResultItem item, int number)
    {
        var card = new Panel { Height = 132, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 3, 3, 8), Padding = new Padding(8), Tag = item.Id };
        var candidateScore = item.Bundle.Scores.LastOrDefault(x => x.Name.Equals("candidate.total", StringComparison.OrdinalIgnoreCase));
        var semanticSummary = item.Bundle.Features.Count > 0 ? $"   • {item.Bundle.Features.Count} features" : string.Empty;
        var scoreSummary = candidateScore is null ? string.Empty : $"   • quality {candidateScore.Value:0.00}";
        var title = new Label { Text = $"#{number}   Seed {item.Seed}" + (item.Variation > 0 ? $"   Variation {item.Variation}" : string.Empty) + scoreSummary + semanticSummary, Dock = DockStyle.Top, Height = 22, ForeColor = SystemColors.GrayText };
        var text = new TextBox { Text = item.Text, Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var inspect = new Button { Text = "Why?", AutoSize = true };
        var copy = new Button { Text = "Copy", AutoSize = true };
        var vary = new Button { Text = "Vary", AutoSize = true };
        var locked = new Button { Text = item.IsLocked ? "🔒 Locked" : "Lock", AutoSize = true };
        var favorite = new Button { Text = item.IsFavorite ? "★ Favorite" : "☆ Favorite", AutoSize = true };
        var import = new Button { Text = "✨ Import…", AutoSize = true, Visible = _session.ActiveTarget is not null };
        inspect.Click += (_, _) => { _inspected = item; _trace.Inspect(item); _mvPreview.Inspect(item.Bundle, _session.ActiveTarget); };
        copy.Click += (_, _) => { if (!string.IsNullOrEmpty(item.Text)) Clipboard.SetText(item.Text); SetStatus("Copied result."); };
        vary.Click += async (_, _) => { SetStatus("Generating variation…"); await _session.VaryAsync(item.Id).ConfigureAwait(true); SetStatus("Variation generated."); };
        locked.Click += (_, _) => _session.ToggleLock(item.Id);
        favorite.Click += async (_, _) => { await _session.ToggleFavoriteAsync(item.Id).ConfigureAwait(true); SetStatus(item.IsFavorite ? "Removed favorite." : "Added favorite."); RebuildArchiveLists(); };
        import.Click += (_, _) => { if (_session.ActiveTarget is { } target) ImportRequested?.Invoke(this, new TextStudioImportRequestedEventArgs(target, item)); };
        buttons.Controls.AddRange([inspect, copy, vary, locked, favorite, import]);
        card.Controls.Add(text); card.Controls.Add(buttons); card.Controls.Add(title);
        card.Click += (_, _) => { _inspected = item; _trace.Inspect(item); _mvPreview.Inspect(item.Bundle, _session.ActiveTarget); };
        text.Click += (_, _) => { _inspected = item; _trace.Inspect(item); _mvPreview.Inspect(item.Bundle, _session.ActiveTarget); };
        return card;
    }

    private void RebuildTargetPanel()
    {
        _targetFields.BeginUpdate();
        try
        {
            _targetFields.Items.Clear();
            if (_session.ActiveTarget is not { } target)
            {
                _targetSummary.Text = "Open Text Studio from a database field, map property, or event command to import generated content directly.";
                _targetBadge.Text = "No project target";
                return;
            }
            _targetSummary.Text = $"{target.DisplayName}\r\nSelected field: {target.SelectedField?.DisplayName ?? "(none)"}";
            _targetBadge.Text = "Target: " + target.DisplayName;
            foreach (var field in target.Fields)
            {
                string value = field.CurrentValue.Replace('\r', ' ').Replace('\n', ' ');
                if (value.Length > 120) value = value[..117] + "…";
                var item = new ListViewItem(field.DisplayName); item.SubItems.Add(value);
                if (ReferenceEquals(field, target.SelectedField) || field.SemanticField.Equals(target.SelectedSemanticField, StringComparison.OrdinalIgnoreCase)) item.Font = new Font(_targetFields.Font, FontStyle.Bold);
                _targetFields.Items.Add(item);
            }
        }
        finally { _targetFields.EndUpdate(); }
        _mvPreview.Inspect(_inspected?.Bundle, _session.ActiveTarget);
        RebuildResults();
    }

    private void ResizeResultCards()
    {
        var width = Math.Max(360, _results.ClientSize.Width - (_results.VerticalScroll.Visible ? 38 : 22));
        foreach (Control control in _results.Controls) control.Width = width;
    }

    private void RebuildArchiveLists()
    {
        _favorites.BeginUpdate(); _favorites.Items.Clear(); foreach (var item in _session.Favorites) _favorites.Items.Add(new ArchiveListItem(item.RecipeName, item.Text, item.Seed)); _favorites.EndUpdate();
        _history.BeginUpdate(); _history.Items.Clear(); foreach (var item in _session.History.Take(200)) _history.Items.Add(new ArchiveListItem(item.RecipeName, item.Text, item.Seed)); _history.EndUpdate();
    }

    private static void CopySelectedArchiveText(ListBox list)
    {
        if (list.SelectedItem is ArchiveListItem item && !string.IsNullOrWhiteSpace(item.Text)) Clipboard.SetText(item.Text);
    }

    private void SetStatus(string message, bool isError = false)
    {
        _status.Text = message;
        StatusChanged?.Invoke(this, new TextStudioStatusChangedEventArgs(message, isError));
    }

    private void UpdateCommandState()
    {
        _generate.Enabled = !_session.IsGenerating && _session.SelectedRecipe is not null;
        _generate20.Enabled = _generate.Enabled; _surprise.Enabled = !_session.IsGenerating && _session.Recipes.Count > 0; _cancel.Enabled = _session.IsGenerating;
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }

    private sealed record ArchiveListItem(string Recipe, string Text, ulong Seed)
    {
        public override string ToString()
        {
            var oneLine = Text.Replace('\r', ' ').Replace('\n', ' ');
            if (oneLine.Length > 72) oneLine = oneLine[..69] + "…";
            return $"{Recipe}: {oneLine}";
        }
    }
}
