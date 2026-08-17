using System.Globalization;
using System.Text.Json;
using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Narrative;
using Playroma.Text.Generators.Narrative;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class StoryletLabControl : UserControl
{
    private readonly TextStudioSession _session;
    private readonly ListBox _storylets = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _id = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, Height = 52, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _enabled = new() { Text = "Enabled", AutoSize = true };
    private readonly CheckBox _once = new() { Text = "Once only", AutoSize = true };
    private readonly NumericUpDown _weight = Number(1, 0, 100, 2);
    private readonly NumericUpDown _salience = Number(1, 0, 100, 2);
    private readonly NumericUpDown _cooldown = new() { Minimum = 0, Maximum = 100000, DecimalPlaces = 0, Dock = DockStyle.Fill };
    private readonly TextBox _prerequisites = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 80 };
    private readonly TextBox _tags = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _recipe = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _voice = new() { Dock = DockStyle.Fill, PlaceholderText = "optional profile id" };
    private readonly TextBox _act = new() { Dock = DockStyle.Fill, PlaceholderText = "optional dialogue act" };
    private readonly TextBox _emotion = new() { Dock = DockStyle.Fill, PlaceholderText = "optional emotion" };
    private readonly DataGridView _bindings = Grid();
    private readonly DataGridView _effects = Grid();
    private readonly DataGridView _realizationParameters = Grid();
    private readonly DataGridView _state = Grid();
    private readonly TextBox _stateTags = new() { Dock = DockStyle.Fill, PlaceholderText = "runtime tags, comma separated" };
    private readonly NumericUpDown _turn = new() { Minimum = 0, Maximum = int.MaxValue, Dock = DockStyle.Fill };
    private readonly ListView _evaluation = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly TextBox _preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 9F) };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 6, 0) };
    private readonly Button _save = new() { Text = "Save", AutoSize = true };
    private readonly Button _delete = new() { Text = "Delete", AutoSize = true };
    private readonly Button _new = new() { Text = "New", AutoSize = true };
    private readonly Button _clone = new() { Text = "Clone", AutoSize = true };
    private readonly Button _evaluateButton = new() { Text = "Evaluate", AutoSize = true };
    private readonly Button _selectRealize = new() { Text = "Select && Realize", AutoSize = true };
    private readonly Button _realizeSelected = new() { Text = "Realize Selected", AutoSize = true };
    private readonly Button _commitEffects = new() { Text = "Commit Effects", AutoSize = true, Enabled = false };
    private readonly Button _sendPlayground = new() { Text = "Open in Playground", AutoSize = true, Enabled = false };
    private readonly Button _simulate = new() { Text = "Simulate 25", AutoSize = true };
    private readonly Button _resetState = new() { Text = "Reset State", AutoSize = true };

    private StoryletDefinition? _baseline;
    private StoryletDefinition? _working;
    private string? _path;
    private bool _loading;
    private bool _dirty;
    private StoryletRuntimeState _runtime = new();
    private StoryletRealizationResult? _lastRealization;

    public StoryletLabControl(TextStudioSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Dock = DockStyle.Fill;

        _bindings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Binding", Width = 110 });
        _bindings.Columns.Add(new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "Kind", Width = 100, DataSource = Enum.GetNames<StoryletBindingKind>() });
        _bindings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Candidate context key", Width = 190 });
        _bindings.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Required", HeaderText = "Required", Width = 65 });
        _bindings.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Distinct", HeaderText = "Distinct", Width = 65 });
        _bindings.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tags", HeaderText = "Required tags", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _effects.Columns.Add(new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "Effect", Width = 105, DataSource = Enum.GetNames<StoryletEffectKind>() });
        _effects.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "State key / tag", Width = 180 });
        _effects.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _realizationParameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "Recipe parameter", Width = 190 });
        _realizationParameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _state.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "World-state key", Width = 180 });
        _state.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        _evaluation.Columns.Add("Eligible", 62);
        _evaluation.Columns.Add("Score", 62);
        _evaluation.Columns.Add("Storylet", 190);
        _evaluation.Columns.Add("Reason / bindings", 460);

        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, WrapContents = false, Padding = new Padding(3) };
        leftButtons.Controls.AddRange([_new, _clone, _save, _delete]);
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(5) };
        left.Controls.Add(_storylets); left.Controls.Add(leftButtons);

        var editorTabs = new TabControl { Dock = DockStyle.Fill };
        editorTabs.TabPages.Add(new TabPage("Definition") { Controls = { BuildDefinitionEditor() } });
        editorTabs.TabPages.Add(new TabPage("Bindings") { Controls = { _bindings } });
        editorTabs.TabPages.Add(new TabPage("Effects") { Controls = { _effects } });
        editorTabs.TabPages.Add(new TabPage("Realization Parameters") { Controls = { _realizationParameters } });
        editorTabs.TabPages.Add(new TabPage("Simulation State") { Controls = { BuildStateEditor() } });

        var simButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, Padding = new Padding(4) };
        simButtons.Controls.AddRange([_evaluateButton, _selectRealize, _realizeSelected, _commitEffects, _sendPlayground, _simulate, _resetState]);
        var simulationTabs = new TabControl { Dock = DockStyle.Fill };
        simulationTabs.TabPages.Add(new TabPage("Eligibility") { Controls = { _evaluation } });
        simulationTabs.TabPages.Add(new TabPage("Realization") { Controls = { _preview } });
        var simulation = new Panel { Dock = DockStyle.Fill }; simulation.Controls.Add(simulationTabs); simulation.Controls.Add(simButtons);

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 380 };
        right.Panel1.Controls.Add(editorTabs); right.Panel2.Controls.Add(simulation);
        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 240, FixedPanel = FixedPanel.Panel1 };
        main.Panel1.Controls.Add(left); main.Panel2.Controls.Add(right);
        Controls.Add(main); Controls.Add(_status);

        _storylets.SelectedIndexChanged += (_, _) => { if (!_loading) LoadSelected(); };
        _new.Click += (_, _) => NewStorylet();
        _clone.Click += (_, _) => CloneCurrent();
        _save.Click += async (_, _) => await SaveCurrentAsync().ConfigureAwait(true);
        _delete.Click += (_, _) => DeleteCurrent();
        _evaluateButton.Click += (_, _) => EvaluateAll();
        _selectRealize.Click += async (_, _) => await SelectAndRealizeAsync().ConfigureAwait(true);
        _realizeSelected.Click += async (_, _) => await RealizeSelectedAsync().ConfigureAwait(true);
        _commitEffects.Click += (_, _) => CommitLastEffects();
        _sendPlayground.Click += (_, _) => SendToPlayground();
        _simulate.Click += async (_, _) => await SimulateAsync(25).ConfigureAwait(true);
        _resetState.Click += (_, _) => { _runtime = new StoryletRuntimeState(); BindRuntime(); EvaluateAll(); SetStatus("Simulation state reset."); };

        foreach (TextBox text in new[] { _id, _name, _description, _prerequisites, _tags, _voice, _act, _emotion })
            text.TextChanged += (_, _) => MarkDirtyFromUi();
        foreach (CheckBox check in new[] { _enabled, _once }) check.CheckedChanged += (_, _) => MarkDirtyFromUi();
        foreach (NumericUpDown number in new[] { _weight, _salience, _cooldown }) number.ValueChanged += (_, _) => MarkDirtyFromUi();
        _recipe.SelectedIndexChanged += (_, _) => MarkDirtyFromUi();
        _bindings.CellValueChanged += (_, _) => MarkDirtyFromUi();
        _bindings.RowsAdded += (_, _) => MarkDirtyFromUi();
        _bindings.RowsRemoved += (_, _) => MarkDirtyFromUi();
        _effects.CellValueChanged += (_, _) => MarkDirtyFromUi();
        _effects.RowsAdded += (_, _) => MarkDirtyFromUi();
        _effects.RowsRemoved += (_, _) => MarkDirtyFromUi();
        _realizationParameters.CellValueChanged += (_, _) => MarkDirtyFromUi();
        _realizationParameters.RowsAdded += (_, _) => MarkDirtyFromUi();
        _realizationParameters.RowsRemoved += (_, _) => MarkDirtyFromUi();
    }

    public bool HasUnsavedChanges => _dirty;
    public event EventHandler? DirtyStateChanged;
    public event EventHandler? PlaygroundResultReady;

    public async Task InitializeAsync(CancellationToken cancellationToken = default) => await ReloadFromDiskAsync(false, cancellationToken).ConfigureAwait(true);

    public async Task ReloadFromDiskAsync(bool discardUnsavedChanges, CancellationToken cancellationToken = default)
    {
        if (_dirty && !discardUnsavedChanges) return;
        await _session.ReloadStoryletsAsync(cancellationToken).ConfigureAwait(true);
        RebuildList(_working?.Id);
    }

    public async Task SaveCurrentAsync(CancellationToken cancellationToken = default)
    {
        StoryletDefinition? storylet = ReadEditor();
        if (storylet is null) return;
        var errors = StoryletDefinitionValidator.Validate(storylet);
        if (errors.Count > 0) { MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Storylet cannot be saved", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_session.IsBuiltInStorylet(storylet.Id))
        {
            storylet = storylet with { Id = storylet.Id + ".custom", Name = storylet.Name + " (Custom)" };
            _path = null;
        }
        _path = await _session.SaveStoryletAsync(storylet, _path, cancellationToken).ConfigureAwait(true);
        _baseline = storylet; _working = storylet; SetDirty(false);
        RebuildList(storylet.Id);
        SetStatus("Saved " + Path.GetFileName(_path));
    }

    public void DiscardUnsavedChanges()
    {
        if (_baseline is not null) BindStorylet(_baseline, _path);
        else LoadSelected();
        SetDirty(false);
    }

    public async Task<bool> PrepareForCloseAsync(CancellationToken cancellationToken = default)
    {
        if (!_dirty) return true;
        var choice = MessageBox.Show(this, "Storylet Lab has unsaved changes. Save them before closing Text Studio?", "Playroma Text Studio", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return false;
        if (choice == DialogResult.Yes)
        {
            await SaveCurrentAsync(cancellationToken).ConfigureAwait(true);
            return !_dirty;
        }
        DiscardUnsavedChanges(); return true;
    }

    public StoryletLabRecoveryState? CaptureRecoveryState()
    {
        if (!_dirty) return null;
        StoryletDefinition? current = ReadEditor();
        if (current is null) return null;
        return new StoryletLabRecoveryState(
            JsonSerializer.Serialize(current, RecipeJson.Options),
            _path,
            _baseline is null ? null : JsonSerializer.Serialize(_baseline, RecipeJson.Options),
            _runtime.Capture());
    }

    public void RestoreRecoveryState(StoryletLabRecoveryState state)
    {
        StoryletDefinition? working = JsonSerializer.Deserialize<StoryletDefinition>(state.StoryletJson, RecipeJson.Options);
        StoryletDefinition? baseline = string.IsNullOrWhiteSpace(state.BaselineStoryletJson) ? null : JsonSerializer.Deserialize<StoryletDefinition>(state.BaselineStoryletJson, RecipeJson.Options);
        if (working is null) return;
        _runtime = StoryletRuntimeState.FromSnapshot(state.Runtime);
        _baseline = baseline; _working = working; _path = state.Path;
        BindStorylet(working, state.Path); BindRuntime(); SetDirty(true);
    }

    private Control BuildDefinitionEditor()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(8) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Add(table, "ID", _id); Add(table, "Name", _name); Add(table, "Description", _description);
        var flags = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 28 }; flags.Controls.AddRange([_enabled, _once]); Add(table, "Flags", flags);
        Add(table, "Weight", _weight); Add(table, "Salience", _salience); Add(table, "Cooldown turns", _cooldown);
        Add(table, "Prerequisites", _prerequisites); Add(table, "Tags", _tags); Add(table, "Realization recipe", _recipe);
        Add(table, "Voice profile", _voice); Add(table, "Dialogue act", _act); Add(table, "Emotion", _emotion);
        return table;
    }

    private Control BuildStateEditor()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 64, ColumnCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Add(top, "Turn", _turn); Add(top, "Tags", _stateTags);
        panel.Controls.Add(_state); panel.Controls.Add(top); return panel;
    }

    private static void Add(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        table.Controls.Add(control, 1, row);
    }

    private static NumericUpDown Number(decimal value, decimal min, decimal max, int decimals) => new() { Value = value, Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : .05M, Dock = DockStyle.Fill };
    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect };

    private async void LoadSelected()
    {
        if (_storylets.SelectedItem is not StoryletListItem item) return;
        if (_dirty)
        {
            var choice = MessageBox.Show(this, "Discard unsaved changes to this storylet?", "Storylet Lab", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) { RebuildList(_working?.Id); return; }
        }
        string? path = ProjectPathFor(item.Storylet.Id);
        BindStorylet(item.Storylet, File.Exists(path) ? path : null);
        await Task.CompletedTask;
    }

    private void BindStorylet(StoryletDefinition storylet, string? path)
    {
        _loading = true;
        try
        {
            _baseline = storylet; _working = storylet; _path = path;
            _id.Text = storylet.Id; _name.Text = storylet.Name; _description.Text = storylet.Description ?? string.Empty;
            _enabled.Checked = storylet.Enabled; _once.Checked = storylet.OnceOnly;
            _weight.Value = ClampDecimal(storylet.Weight, _weight); _salience.Value = ClampDecimal(storylet.Salience, _salience); _cooldown.Value = Math.Clamp(storylet.CooldownTurns, 0, (int)_cooldown.Maximum);
            _prerequisites.Text = string.Join(Environment.NewLine, storylet.EffectivePrerequisites); _tags.Text = string.Join(", ", storylet.EffectiveTags);
            ReloadRecipeChoices(storylet.Realization?.RecipeId);
            _voice.Text = storylet.Realization?.VoiceProfileId ?? string.Empty; _act.Text = storylet.Realization?.DialogueAct ?? string.Empty; _emotion.Text = storylet.Realization?.Emotion ?? string.Empty;
            _bindings.Rows.Clear();
            foreach (var binding in storylet.EffectiveBindings) _bindings.Rows.Add(binding.Id, binding.Kind.ToString(), binding.CandidateContextKey, binding.Required, binding.Distinct, string.Join(",", binding.EffectiveRequiredTags));
            _effects.Rows.Clear();
            foreach (var effect in storylet.EffectiveEffects) _effects.Rows.Add(effect.Kind.ToString(), effect.Key, SerializeValue(effect.Value));
            _realizationParameters.Rows.Clear();
            if (storylet.Realization is not null)
                foreach (var pair in storylet.Realization.EffectiveParameters.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    _realizationParameters.Rows.Add(pair.Key, SerializeValue(pair.Value));
            _lastRealization = null; _preview.Clear(); _commitEffects.Enabled = false; _sendPlayground.Enabled = false;
            SetDirty(false); EvaluateAll();
        }
        finally { _loading = false; }
    }

    private StoryletDefinition? ReadEditor()
    {
        try
        {
            var bindings = new List<StoryletBindingDefinition>();
            foreach (DataGridViewRow row in _bindings.Rows)
            {
                if (row.IsNewRow) continue;
                string id = Cell(row, "Id"); if (string.IsNullOrWhiteSpace(id)) continue;
                Enum.TryParse(Cell(row, "Kind"), true, out StoryletBindingKind kind);
                bindings.Add(new StoryletBindingDefinition(id, kind, Cell(row, "Source"), BoolCell(row, "Required", true), BoolCell(row, "Distinct", true), SplitList(Cell(row, "Tags"))));
            }
            var effects = new List<StoryletEffectDefinition>();
            foreach (DataGridViewRow row in _effects.Rows)
            {
                if (row.IsNewRow) continue;
                string key = Cell(row, "Key"); if (string.IsNullOrWhiteSpace(key)) continue;
                Enum.TryParse(Cell(row, "Kind"), true, out StoryletEffectKind kind);
                effects.Add(new StoryletEffectDefinition(kind, key, ParseValue(Cell(row, "Value"))));
            }
            var realizationParameters = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _realizationParameters.Rows)
            {
                if (row.IsNewRow) continue;
                string key = Cell(row, "Key");
                if (!string.IsNullOrWhiteSpace(key)) realizationParameters[key] = ParseValue(Cell(row, "Value"));
            }
            string recipeId = _recipe.SelectedItem?.ToString() ?? _recipe.Text;
            return new StoryletDefinition(
                _id.Text.Trim(), 1, _name.Text.Trim(), EmptyNull(_description.Text), _enabled.Checked,
                (double)_weight.Value, (double)_salience.Value, _once.Checked, (int)_cooldown.Value,
                SplitLines(_prerequisites.Text), bindings, effects,
                new StoryletRealizationDefinition(recipeId, EmptyNull(_voice.Text), EmptyNull(_act.Text), EmptyNull(_emotion.Text), realizationParameters), SplitList(_tags.Text));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid storylet", MessageBoxButtons.OK, MessageBoxIcon.Warning); return null;
        }
    }

    private void NewStorylet()
    {
        var storylet = new StoryletDefinition("project.storylet.new", 1, "New Storylet", Weight: 1, Salience: 1,
            Realization: new StoryletRealizationDefinition(_session.Recipes.FirstOrDefault()?.Recipe.Id ?? "phase10.dialogue.small-talk"));
        BindStorylet(storylet, null); _baseline = null; SetDirty(true);
    }

    private void CloneCurrent()
    {
        StoryletDefinition? current = ReadEditor(); if (current is null) return;
        string id = current.Id.EndsWith(".custom", StringComparison.OrdinalIgnoreCase) ? current.Id + "2" : current.Id + ".custom";
        BindStorylet(current with { Id = id, Name = current.Name + " (Custom)" }, null); _baseline = null; SetDirty(true);
    }

    private void DeleteCurrent()
    {
        if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) { MessageBox.Show(this, "Built-in storylets cannot be deleted. Clone or override them instead.", "Storylet Lab", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show(this, $"Delete '{_name.Text}'?", "Storylet Lab", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _session.DeleteStorylet(_path); _path = null; _baseline = null; _working = null; SetDirty(false); _ = ReloadFromDiskAsync(true);
    }

    private void EvaluateAll()
    {
        SyncRuntime();
        IReadOnlyList<StoryletEvaluation> evaluations = _session.EvaluateStorylets(_runtime);
        _evaluation.BeginUpdate(); _evaluation.Items.Clear();
        foreach (StoryletEvaluation evaluation in evaluations.OrderByDescending(x => x.Eligible).ThenByDescending(x => x.Score))
        {
            string bindings = evaluation.EffectiveBindings.Count == 0 ? string.Empty : string.Join(", ", evaluation.EffectiveBindings.Select(x => $"{x.Id}={x.Candidate.Name}"));
            string reason = evaluation.Eligible ? bindings : evaluation.Reason ?? string.Empty;
            var item = new ListViewItem(evaluation.Eligible ? "Yes" : "No") { Tag = evaluation.Storylet };
            item.SubItems.Add(evaluation.Score.ToString("0.###", CultureInfo.InvariantCulture)); item.SubItems.Add(evaluation.Storylet.Name); item.SubItems.Add(reason); _evaluation.Items.Add(item);
        }
        _evaluation.EndUpdate(); SetStatus($"{evaluations.Count(x => x.Eligible)} of {evaluations.Count} storylets eligible at turn {_runtime.Turn}.");
    }

    private async Task SelectAndRealizeAsync()
    {
        SyncRuntime();
        StoryletRealizationResult? result = await _session.SelectAndRealizeStoryletAsync(_runtime).ConfigureAwait(true);
        if (result is null) { _preview.Text = "No eligible storylet."; SetStatus("No eligible storylet."); return; }
        ShowRealization(result);
    }

    private async Task RealizeSelectedAsync()
    {
        SyncRuntime();
        StoryletDefinition? storylet = (_evaluation.SelectedItems.Count > 0 ? _evaluation.SelectedItems[0].Tag as StoryletDefinition : null) ?? ReadEditor();
        if (storylet is null) return;
        try { ShowRealization(await _session.RealizeStoryletAsync(storylet, _runtime).ConfigureAwait(true)); }
        catch (Exception ex) { _preview.Text = ex.Message; SetStatus(ex.Message); }
    }

    private void ShowRealization(StoryletRealizationResult result)
    {
        _lastRealization = result;
        var lines = new List<string> { result.Bundle.PrimaryText, "", $"Storylet: {result.Selection.Storylet.Name}", $"Score: {result.Selection.Score:0.###}", $"Turn: {result.Selection.Turn}" };
        if (result.Selection.Bindings.Count > 0) lines.Add("Bindings: " + string.Join(", ", result.Selection.Bindings.Select(x => $"{x.Id}={x.Candidate.Name}")));
        if (result.Selection.Storylet.EffectiveEffects.Count > 0) lines.Add("Pending effects: " + string.Join("; ", result.Selection.Storylet.EffectiveEffects.Select(DescribeEffect)));
        _preview.Text = string.Join(Environment.NewLine, lines); _commitEffects.Enabled = true; _sendPlayground.Enabled = true;
        SetStatus("Realized " + result.Selection.Storylet.Name + ". Effects are pending until committed.");
    }

    private void CommitLastEffects()
    {
        if (_lastRealization is null) return;
        _session.ApplyStoryletEffects(_lastRealization.Selection, _runtime); BindRuntime(); EvaluateAll(); _commitEffects.Enabled = false;
        SetStatus("Applied storylet effects and advanced to turn " + _runtime.Turn + ".");
    }

    private void SendToPlayground()
    {
        if (_lastRealization is null) return;
        _session.AdoptStoryletResult(_lastRealization); PlaygroundResultReady?.Invoke(this, EventArgs.Empty); SetStatus("Storylet realization opened in Playground.");
    }

    private async Task SimulateAsync(int turns)
    {
        SyncRuntime();
        var clone = StoryletRuntimeState.FromSnapshot(_runtime.Capture());
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var log = new List<string>();
        for (int i = 0; i < turns; i++)
        {
            ulong seed = Playroma.Text.Core.Random.SeedFactory.Derive(_session.Seed, "storylet-sim:" + i.ToString(CultureInfo.InvariantCulture));
            StoryletRealizationResult? result = await _session.SelectAndRealizeStoryletAsync(clone, seed).ConfigureAwait(true);
            if (result is null) { log.Add($"Turn {clone.Turn}: no eligible storylet"); clone.Turn++; continue; }
            string id = result.Selection.Storylet.Id; counts[id] = counts.TryGetValue(id, out int n) ? n + 1 : 1;
            log.Add($"Turn {clone.Turn}: {result.Selection.Storylet.Name} — {OneLine(result.Bundle.PrimaryText)}");
            _session.ApplyStoryletEffects(result.Selection, clone);
        }
        log.Add(""); log.Add("Frequency:");
        foreach (var pair in counts.OrderByDescending(x => x.Value)) log.Add($"{pair.Value,3} × {pair.Key}");
        _preview.Text = string.Join(Environment.NewLine, log); SetStatus($"Simulated {turns} narrative turns without changing live simulation state.");
    }

    private void SyncRuntime()
    {
        _runtime.Turn = (int)_turn.Value;
        _runtime.Tags.Clear(); foreach (string tag in SplitList(_stateTags.Text)) _runtime.Tags.Add(tag);
        _runtime.Values.Clear();
        foreach (DataGridViewRow row in _state.Rows)
        {
            if (row.IsNewRow) continue; string key = Cell(row, "Key"); if (!string.IsNullOrWhiteSpace(key)) _runtime.Values[key] = ParseValue(Cell(row, "Value"));
        }
    }

    private void BindRuntime()
    {
        _loading = true;
        try
        {
            _turn.Value = Math.Clamp(_runtime.Turn, 0, (int)_turn.Maximum); _stateTags.Text = string.Join(", ", _runtime.Tags.Order(StringComparer.OrdinalIgnoreCase));
            _state.Rows.Clear(); foreach (var pair in _runtime.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) _state.Rows.Add(pair.Key, SerializeValue(pair.Value));
        }
        finally { _loading = false; }
    }

    private void RebuildList(string? selectedId)
    {
        _loading = true;
        try
        {
            _storylets.BeginUpdate(); _storylets.Items.Clear();
            foreach (StoryletDefinition storylet in _session.Storylets) _storylets.Items.Add(new StoryletListItem(storylet, _session.IsBuiltInStorylet(storylet.Id)));
            StoryletListItem? selected = _storylets.Items.Cast<StoryletListItem>().FirstOrDefault(x => x.Storylet.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) _storylets.SelectedItem = selected; else if (_storylets.Items.Count > 0) _storylets.SelectedIndex = 0;
            _storylets.EndUpdate();
        }
        finally { _loading = false; }
        if (_storylets.SelectedItem is StoryletListItem item) BindStorylet(item.Storylet, File.Exists(ProjectPathFor(item.Storylet.Id)) ? ProjectPathFor(item.Storylet.Id) : null);
    }

    private void ReloadRecipeChoices(string? selected)
    {
        _recipe.Items.Clear(); foreach (var recipe in _session.Recipes.OrderBy(x => x.Recipe.Name)) _recipe.Items.Add(recipe.Recipe.Id);
        if (!string.IsNullOrWhiteSpace(selected) && _recipe.Items.Contains(selected)) _recipe.SelectedItem = selected;
        else if (!string.IsNullOrWhiteSpace(selected)) { _recipe.Items.Add(selected); _recipe.SelectedItem = selected; }
        else if (_recipe.Items.Count > 0) _recipe.SelectedIndex = 0;
    }

    private string ProjectPathFor(string id) => Path.Combine(_session.Options.ProjectRoot, "data", "PlayromaText", "storylets", SafeFileName(id) + ".playstorylet.json");
    private static string SafeFileName(string value) => string.Join("-", value.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static decimal ClampDecimal(double value, NumericUpDown control) => Math.Clamp((decimal)value, control.Minimum, control.Maximum);
    private static string Cell(DataGridViewRow row, string name) => Convert.ToString(row.Cells[name].Value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    private static bool BoolCell(DataGridViewRow row, string name, bool fallback) => row.Cells[name].Value is bool value ? value : fallback;
    private static IReadOnlyList<string> SplitList(string? text) => (text ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private static IReadOnlyList<string> SplitLines(string? text) => (text ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string? EmptyNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static object? ParseValue(string? text)
    {
        string value = (text ?? string.Empty).Trim(); if (value.Length == 0) return string.Empty;
        if (bool.TryParse(value, out bool b)) return b;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)) return i;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
        if ((value.StartsWith("[") && value.EndsWith("]")) || (value.StartsWith("{") && value.EndsWith("}")))
        {
            try { return JsonSerializer.Deserialize<object>(value, RecipeJson.Options); } catch (JsonException) { }
        }
        return value;
    }

    private static string SerializeValue(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => JsonSerializer.Serialize(value, RecipeJson.Options)
    };

    private static string DescribeEffect(StoryletEffectDefinition effect) => effect.Kind switch
    {
        StoryletEffectKind.Set => $"set {effect.Key}={SerializeValue(effect.Value)}",
        StoryletEffectKind.Increment => $"increment {effect.Key} by {SerializeValue(effect.Value)}",
        StoryletEffectKind.AddTag => $"add tag {effect.Key}",
        StoryletEffectKind.RemoveTag => $"remove tag {effect.Key}",
        _ => effect.Key
    };

    private void MarkDirtyFromUi() { if (_loading || _working is null) return; _working = ReadEditor(); SetDirty(true); }
    private void SetDirty(bool dirty) { if (_dirty == dirty) return; _dirty = dirty; DirtyStateChanged?.Invoke(this, EventArgs.Empty); }
    private void SetStatus(string text) => _status.Text = text;
    private static string OneLine(string text) { string value = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim(); return value.Length <= 90 ? value : value[..87] + "…"; }
    private sealed record StoryletListItem(StoryletDefinition Storylet, bool BuiltIn)
    {
        public override string ToString() => (BuiltIn ? "◆ " : "  ") + Storylet.Name;
    }
}
