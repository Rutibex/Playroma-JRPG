using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Generators.Stages;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class RecipeDesignerControl : UserControl
{
    private readonly TextBox _id = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _kind = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, Height = 56 };
    private readonly ListBox _stages = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _addStageKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly CheckBox _stageEnabled = new() { Text = "Enabled", AutoSize = true };
    private readonly TextBox _stageId = new() { Width = 155 };
    private readonly RichTextBox _settings = new() { Dock = DockStyle.Fill, AcceptsTab = true, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly RichTextBox _raw = new() { Dock = DockStyle.Fill, AcceptsTab = true, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly TraceryGrammarEditorControl _grammarEditor = new() { Dock = DockStyle.Fill };
    private readonly CandidatePipelineEditorControl _pipelineEditor = new() { Dock = DockStyle.Fill };
    private readonly ListBox _validation = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
    private readonly Button _save = new() { Text = "Save Project Recipe", AutoSize = true };
    private RecipeEditorDocument? _document;
    private TextStudioRecipeEntry? _entry;
    private bool _rebuilding;
    private bool _lastReportedDirty;

    public RecipeDesignerControl()
    {
        _kind.Items.AddRange(Enum.GetNames<TextContentKind>());
        _addStageKind.Items.AddRange(new object[]
        {
            RandomListStage.StageKind, WeightedTableStage.StageKind, NestedTableStage.StageKind, ShuffleBagStage.StageKind,
            TemplateStage.StageKind, AdLibStage.StageKind, AffixComposeStage.StageKind, TransformStage.StageKind, ConstraintStage.StageKind,
            NameGeneratorStage.StageKind, NameSetStage.StageKind, NameCollisionStage.StageKind, TraceryGrammarStage.StageKind, MarkovGenerationStage.StageKind,
            SemanticAnnotateStage.StageKind, FeatureUnifyStage.StageKind, SemanticConstraintStage.StageKind, AgreementStage.StageKind, SemanticScoreStage.StageKind,
            DialogueVoiceStage.StageKind, NeuralTextStage.StageKind
        });
        _addStageKind.SelectedIndex = 0;

        var metadata = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(8) };
        metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddMeta(metadata, 0, "ID", _id, "Name", _name);
        AddMeta(metadata, 1, "Kind", _kind, "Description", _description);

        var stagePanel = new Panel { Dock = DockStyle.Fill };
        var stageButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false, Padding = new Padding(4) };
        var add = new Button { Text = "+ Stage", AutoSize = true };
        var remove = new Button { Text = "− Stage", AutoSize = true };
        var up = new Button { Text = "↑", Width = 34 };
        var down = new Button { Text = "↓", Width = 34 };
        stageButtons.Controls.AddRange([_addStageKind, add, remove, up, down]);
        stagePanel.Controls.Add(_stages); stagePanel.Controls.Add(stageButtons);

        var settingsPanel = new Panel { Dock = DockStyle.Fill };
        var stageHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = new Padding(4) };
        var applySettings = new Button { Text = "Apply Settings", AutoSize = true };
        stageHeader.Controls.AddRange([new Label { Text = "Stage ID", AutoSize = true, Margin = new Padding(3, 8, 2, 0) }, _stageId, _stageEnabled, applySettings]);
        settingsPanel.Controls.Add(_settings); settingsPanel.Controls.Add(stageHeader);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230 };
        split.Panel1.Controls.Add(stagePanel);
        split.Panel2.Controls.Add(settingsPanel);

        var stageTab = new TabPage("Pipeline") { Padding = new Padding(0) }; stageTab.Controls.Add(split);
        var grammarTab = new TabPage("Tracery Grammar") { Padding = new Padding(0) }; grammarTab.Controls.Add(_grammarEditor);
        var pipelineTab = new TabPage("Candidate Pipeline") { Padding = new Padding(0) }; pipelineTab.Controls.Add(_pipelineEditor);
        var rawTab = new TabPage("Raw Recipe JSON") { Padding = new Padding(0) };
        var rawApply = new Button { Text = "Apply Raw JSON", Dock = DockStyle.Bottom, Height = 32 };
        rawTab.Controls.Add(_raw); rawTab.Controls.Add(rawApply);
        var validationTab = new TabPage("Validation") { Padding = new Padding(0) }; validationTab.Controls.Add(_validation);
        var tabs = new TabControl { Dock = DockStyle.Fill }; tabs.TabPages.AddRange([stageTab, grammarTab, pipelineTab, rawTab, validationTab]);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5), WrapContents = false };
        var revert = new Button { Text = "Revert", AutoSize = true };
        var newRecipe = new Button { Text = "New Recipe", AutoSize = true };
        bottom.Controls.AddRange([_save, revert, newRecipe]);

        Controls.Add(tabs); Controls.Add(metadata); Controls.Add(bottom); Controls.Add(_status);

        _stages.SelectedIndexChanged += (_, _) => LoadSelectedStage();
        _id.TextChanged += (_, _) => CommitMetadata(); _name.TextChanged += (_, _) => CommitMetadata();
        _kind.SelectedIndexChanged += (_, _) => CommitMetadata(); _description.TextChanged += (_, _) => CommitMetadata();
        add.Click += (_, _) => AddStage(); remove.Click += (_, _) => RemoveStage(); up.Click += (_, _) => MoveStage(-1); down.Click += (_, _) => MoveStage(1);
        applySettings.Click += (_, _) => TryApplyStageSettings(); _stageEnabled.CheckedChanged += (_, _) => ApplyStageHeader(); _stageId.Leave += (_, _) => ApplyStageHeader();
        rawApply.Click += (_, _) => ApplyRaw(); revert.Click += (_, _) =>
        {
            if (_entry is not null) Bind(_entry);
            else if (_document is not null) { _document = new RecipeEditorDocument(_document.Original, false, null); RebuildAll(); }
        };
        newRecipe.Click += (_, _) => NewRecipe();
        _settings.Leave += (_, _) => TryApplyStageSettings();
        _save.Click += (_, _) => { CommitMetadata(); if (TryApplyStageSettings()) SaveRequested?.Invoke(this, EventArgs.Empty); };
        _grammarEditor.GrammarChanged += (_, _) => CommitGrammar();
        _pipelineEditor.PipelineChanged += (_, _) => CommitPipeline();
    }

    public event EventHandler? SaveRequested;
    public event EventHandler? DirtyStateChanged;
    public RecipeEditorDocument? Document => _document;

    public RecipeDesignerRecoveryState? CaptureRecoveryState() =>
        _document?.IsDirty == true
            ? new RecipeDesignerRecoveryState(_document.Original, _document.Working, _document.IsBuiltIn, _document.Path)
            : null;

    public void RestoreRecoveryState(RecipeDesignerRecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _entry = null;
        _document = new RecipeEditorDocument(state.Original, state.Working, state.IsBuiltIn, state.Path);
        RebuildAll();
    }

    public void DiscardUnsavedChanges()
    {
        if (_entry is not null) Bind(_entry);
        else
        {
            _document = null;
            RebuildAll();
        }
    }

    public void Bind(TextStudioRecipeEntry? entry)
    {
        _entry = entry;
        _document = entry is null ? null : new RecipeEditorDocument(entry.Recipe, entry.IsBuiltIn, entry.Path);
        RebuildAll();
    }

    public void NewRecipe()
    {
        _entry = null;
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["template"] = JsonSerializer.SerializeToElement("New generated text"),
            ["field"] = JsonSerializer.SerializeToElement("text"),
            ["isPrimary"] = JsonSerializer.SerializeToElement(true)
        };
        var recipe = new TextRecipe("custom.new-recipe", 1, "New Recipe", TextContentKind.Custom,
            [new TextRecipeStage("generate", TemplateStage.StageKind, Settings: settings)],
            Description: "Project-authored Text Studio recipe.");
        _document = new RecipeEditorDocument(recipe, false, null);
        RebuildAll();
        _id.SelectAll(); _id.Focus();
    }

    public TextRecipe? RecipeForSave => _document?.CreateProjectCopy();
    public string? ExistingPathForSave => _document is { IsBuiltIn: false } ? _document.Path : null;

    private void RebuildAll()
    {
        _rebuilding = true;
        try
        {
            _stages.Items.Clear(); _settings.Clear(); _raw.Clear(); _validation.Items.Clear(); _grammarEditor.Bind(null); _pipelineEditor.Bind(null);
            if (_document is null)
            {
                _id.Clear(); _name.Clear(); _description.Clear(); _kind.SelectedIndex = -1; _status.Text = "Select a recipe to inspect or edit.";
            }
            else
            {
                var recipe = _document.Working;
                _id.Text = recipe.Id; _name.Text = recipe.Name; _description.Text = recipe.Description ?? string.Empty; _kind.SelectedItem = recipe.Kind.ToString();
                foreach (var stage in recipe.Stages) _stages.Items.Add(new StageListItem(stage));
                if (_stages.Items.Count > 0) _stages.SelectedIndex = 0;
                _pipelineEditor.Bind(recipe.CandidatePipeline);
                _raw.Text = _document.ToJson();
                ValidateWorking();
                _save.Text = _document.IsBuiltIn ? "Save as Project Copy" : "Save Project Recipe";
                _status.Text = _document.IsBuiltIn ? "Built-in recipes are read-only; saving creates a project override copy." : (_document.Path ?? "Project recipe");
            }
        }
        finally { _rebuilding = false; }
        LoadSelectedStage();
        ReportDirtyStateIfChanged();
    }

    private void LoadSelectedStage()
    {
        if (_document is null || _stages.SelectedItem is not StageListItem item) { _settings.Clear(); _grammarEditor.Bind(null); return; }
        _rebuilding = true;
        try
        {
            var stage = _document.Working.Stages.First(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            _stageId.Text = stage.Id; _stageEnabled.Checked = stage.Enabled;
            _settings.Text = SettingsJson(stage.Settings);
            _grammarEditor.Bind(_document.TryGetInlineGrammar(stage.Id));
        }
        finally { _rebuilding = false; }
    }

    private void CommitMetadata()
    {
        if (_rebuilding || _document is null) return;
        if (!Enum.TryParse<TextContentKind>(_kind.SelectedItem?.ToString(), out var kind)) kind = _document.Working.Kind;
        _document.SetMetadata(_id.Text, _name.Text, kind, _description.Text);
        RefreshRawAndValidation();
    }

    private void AddStage()
    {
        if (_document is null || _addStageKind.SelectedItem is null) return;
        _document.AddStage(_addStageKind.SelectedItem.ToString()!); RebuildStages(selectLast: true); RefreshRawAndValidation();
    }

    private void RemoveStage()
    {
        if (_document is null || _stages.SelectedItem is not StageListItem item) return;
        _document.RemoveStage(item.Id); RebuildStages(); RefreshRawAndValidation();
    }

    private void MoveStage(int delta)
    {
        if (_document is null || _stages.SelectedItem is not StageListItem item) return;
        _document.MoveStage(item.Id, delta); RebuildStages(selectId: item.Id); RefreshRawAndValidation();
    }

    private void ApplyStageHeader()
    {
        if (_rebuilding || _document is null || _stages.SelectedItem is not StageListItem item) return;
        var stage = _document.Working.Stages.First(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        var newId = string.IsNullOrWhiteSpace(_stageId.Text) ? stage.Id : _stageId.Text.Trim();
        var stages = _document.Working.Stages.ToList();
        var index = stages.FindIndex(x => string.Equals(x.Id, stage.Id, StringComparison.OrdinalIgnoreCase));
        stages[index] = stage with { Id = newId, Enabled = _stageEnabled.Checked };
        _document.ApplyRawJson(System.Text.Json.JsonSerializer.Serialize(_document.Working with { Stages = stages }, RecipeJson.Options));
        RebuildStages(selectId: newId); RefreshRawAndValidation();
    }

    private bool TryApplyStageSettings()
    {
        if (_document is null || _stages.SelectedItem is not StageListItem item) return true;
        try
        {
            _document.SetStageSettings(item.Id, _settings.Text);
            _status.Text = "Stage settings applied.";
            RefreshRawAndValidation();
            LoadSelectedStage();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _status.Text = "Settings error: " + ex.Message;
            return false;
        }
    }

    private void CommitGrammar()
    {
        if (_rebuilding || _document is null || _grammarEditor.Grammar is null || _stages.SelectedItem is not StageListItem item) return;
        _document.SetInlineGrammar(item.Id, _grammarEditor.Grammar); RefreshRawAndValidation(updateSettings: true);
    }

    private void CommitPipeline()
    {
        if (_rebuilding || _document is null) return;
        _document.SetCandidatePipeline(_pipelineEditor.Pipeline);
        RefreshRawAndValidation();
    }

    private void ApplyRaw()
    {
        if (_document is null) return;
        try { _document.ApplyRawJson(_raw.Text); RebuildAll(); _status.Text = "Raw recipe JSON applied."; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { _status.Text = "Recipe JSON error: " + ex.Message; }
    }

    private void RebuildStages(bool selectLast = false, string? selectId = null)
    {
        if (_document is null) return;
        _rebuilding = true;
        try
        {
            _stages.Items.Clear();
            foreach (var stage in _document.Working.Stages) _stages.Items.Add(new StageListItem(stage));
            if (_stages.Items.Count == 0) return;
            if (!string.IsNullOrWhiteSpace(selectId))
            {
                for (var i = 0; i < _stages.Items.Count; i++) if ((_stages.Items[i] as StageListItem)?.Id.Equals(selectId, StringComparison.OrdinalIgnoreCase) == true) { _stages.SelectedIndex = i; break; }
            }
            else _stages.SelectedIndex = selectLast ? _stages.Items.Count - 1 : 0;
        }
        finally { _rebuilding = false; }
        LoadSelectedStage();
    }

    private void RefreshRawAndValidation(bool updateSettings = false)
    {
        if (_document is null) { ReportDirtyStateIfChanged(); return; }
        _raw.Text = _document.ToJson(); ValidateWorking();
        if (updateSettings && _stages.SelectedItem is StageListItem item)
        {
            var stage = _document.Working.Stages.First(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            _settings.Text = SettingsJson(stage.Settings);
        }
        ReportDirtyStateIfChanged();
    }

    private void ReportDirtyStateIfChanged()
    {
        var dirty = _document?.IsDirty == true;
        if (dirty == _lastReportedDirty) return;
        _lastReportedDirty = dirty;
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ValidateWorking()
    {
        _validation.Items.Clear();
        if (_document is null) return;
        var diagnostics = TextRecipeValidator.Validate(_document.Working);
        if (diagnostics.Count == 0) _validation.Items.Add("No recipe validation problems found.");
        else foreach (var d in diagnostics) _validation.Items.Add($"{d.Severity} {d.Code}: {d.Message}");
    }

    private static string SettingsJson(IReadOnlyDictionary<string, JsonElement>? settings)
    {
        var dictionary = settings?.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, JsonElement>();
        return JsonSerializer.Serialize(dictionary, RecipeJson.Options);
    }

    private static void AddMeta(TableLayoutPanel panel, int row, string leftLabel, Control left, string rightLabel, Control right)
    {
        panel.RowCount = Math.Max(panel.RowCount, row + 1); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = leftLabel, AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, row); panel.Controls.Add(left, 1, row);
        panel.Controls.Add(new Label { Text = rightLabel, AutoSize = true, Margin = new Padding(8, 8, 3, 3) }, 2, row); panel.Controls.Add(right, 3, row);
    }

    private sealed class StageListItem
    {
        public StageListItem(TextRecipeStage stage) { Id = stage.Id; Kind = stage.Kind; Enabled = stage.Enabled; }
        public string Id { get; } public string Kind { get; } public bool Enabled { get; }
        public override string ToString() => $"{(Enabled ? "●" : "○")} {Id}  [{Kind}]";
    }
}
