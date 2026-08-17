using System.Globalization;
using Playroma.Text.Authoring.Corpora;
using Playroma.Text.Core.Corpora;
using Playroma.Text.Core.Random;
using Playroma.Text.Generators.Markov;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class CorpusLabControl : UserControl
{
    private readonly CorpusLabSession _session;
    private readonly ListBox _corpora = new() { Dock = DockStyle.Fill };
    private readonly TextBox _id = new() { Dock = DockStyle.Fill };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _author = new() { Dock = DockStyle.Fill };
    private readonly TextBox _license = new() { Dock = DockStyle.Fill };
    private readonly TextBox _sourceUri = new() { Dock = DockStyle.Fill };
    private readonly TextBox _notes = new() { Dock = DockStyle.Fill, Multiline = true, Height = 52 };
    private readonly RichTextBox _text = new() { Dock = DockStyle.Fill, AcceptsTab = true, Font = new Font(FontFamily.GenericMonospace, 9f), DetectUrls = false };
    private readonly RichTextBox _normalized = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font(FontFamily.GenericMonospace, 9f), DetectUrls = false };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly NumericUpDown _order = new() { Minimum = 1, Maximum = 8, Value = 3, Width = 55 };
    private readonly NumericUpDown _minOrder = new() { Minimum = 1, Maximum = 8, Value = 1, Width = 55 };
    private readonly CheckBox _backoff = new() { Text = "Variable-order backoff", Checked = true, AutoSize = true };
    private readonly CheckBox _case = new() { Text = "Preserve case", Checked = true, AutoSize = true };
    private readonly CheckBox _punctuation = new() { Text = "Keep punctuation", Checked = true, AutoSize = true };
    private readonly CheckBox _exact = new() { Text = "Reject exact corpus matches", Checked = true, AutoSize = true };
    private readonly NumericUpDown _minTokens = new() { Minimum = 1, Maximum = 10000, Value = 8, Width = 70 };
    private readonly NumericUpDown _maxTokens = new() { Minimum = 1, Maximum = 10000, Value = 40, Width = 70 };
    private readonly NumericUpDown _similarity = new() { DecimalPlaces = 2, Increment = .01M, Minimum = 0, Maximum = 1, Value = .86M, Width = 70 };
    private readonly NumericUpDown _attempts = new() { Minimum = 1, Maximum = 512, Value = 64, Width = 70 };
    private readonly CheckBox _trim = new() { Text = "Trim lines", Checked = true, AutoSize = true };
    private readonly CheckBox _empty = new() { Text = "Remove empty lines", Checked = true, AutoSize = true };
    private readonly CheckBox _collapse = new() { Text = "Collapse whitespace", Checked = true, AutoSize = true };
    private readonly CheckBox _duplicates = new() { Text = "Remove duplicate lines", AutoSize = true };
    private readonly CheckBox _lower = new() { Text = "Lowercase corpus", AutoSize = true };
    private readonly ListBox _samples = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox _stats = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private readonly ToolStripLabel _status = new("Ready");
    private readonly ToolStripTextBox _seed = new() { Width = 165 };
    private string? _currentPath;
    private string? _sourceFileName;
    private string? _sourceSha256;
    private DateTimeOffset? _sourceImportedAtUtc;
    private DateTimeOffset? _createdAtUtc;
    private HashSet<string> _corpusTags = new(StringComparer.OrdinalIgnoreCase);
    private bool _loading;
    private bool _dirty;

    public bool HasUnsavedChanges => _dirty;
    public string? CurrentCorpusId => string.IsNullOrWhiteSpace(_id.Text) ? null : _id.Text.Trim();
    public event EventHandler? DirtyStateChanged;

    public CorpusLabControl(string projectRoot)
    {
        _session = new CorpusLabSession(projectRoot);
        Dock = DockStyle.Fill;
        _mode.Items.AddRange(Enum.GetNames<CorpusTokenMode>()); _mode.SelectedItem = CorpusTokenMode.Word.ToString();

        var toolbar = BuildToolbar();
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
        left.Controls.Add(_corpora); left.Controls.Add(new Label { Text = "CORPORA", Dock = DockStyle.Top, Height = 28, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 5, 0, 0) });
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 230, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(BuildEditor());
        Controls.Add(split); Controls.Add(toolbar);

        _corpora.SelectedIndexChanged += (_, _) => { if (!_loading && _corpora.SelectedItem is CorpusItem item) LoadCorpus(item.Entry); };
        _samples.DoubleClick += (_, _) => { if (_samples.SelectedItem is SampleItem s) Clipboard.SetText(s.Text); };
        foreach (var c in new Control[] { _trim, _empty, _collapse, _duplicates, _lower })
            if (c is CheckBox cb) cb.CheckedChanged += (_, _) => RefreshNormalizedPreview();
        _text.TextChanged += (_, _) => { if (!_loading) { SetDirty(true); RefreshNormalizedPreview(); } };
        foreach (var control in new Control[] { _id, _name, _author, _license, _sourceUri, _notes })
            if (control is TextBox box) box.TextChanged += (_, _) => { if (!_loading) SetDirty(true); };
        foreach (var cb in new[] { _trim, _empty, _collapse, _duplicates, _lower }) cb.CheckedChanged += (_, _) => { if (!_loading) SetDirty(true); };
        foreach (var cb in new[] { _backoff, _case, _punctuation, _exact }) cb.CheckedChanged += (_, _) => { if (!_loading) SetDirty(true); };
        _mode.SelectedIndexChanged += (_, _) => { if (!_loading) SetDirty(true); };
        foreach (var numeric in new[] { _order, _minOrder, _minTokens, _maxTokens, _similarity, _attempts }) numeric.ValueChanged += (_, _) => { if (!_loading) SetDirty(true); };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _seed.Text = SeedFactory.CreateSeed().ToString(CultureInfo.InvariantCulture);
        await ReloadCorpusListAsync(cancellationToken).ConfigureAwait(true);
        if (_corpora.Items.Count == 0) NewCorpus(false); else _corpora.SelectedIndex = 0;
    }

    private ToolStrip BuildToolbar()
    {
        var strip = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        var add = new ToolStripButton("New / Paste"); add.Click += (_, _) => NewCorpus(true);
        var import = new ToolStripButton("Import TXT/CSV/JSON"); import.Click += async (_, _) => await ImportAsync().ConfigureAwait(true);
        var save = new ToolStripButton("Save Corpus"); save.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        var train = new ToolStripButton("Train"); train.Click += async (_, _) => await TrainAsync(force: true).ConfigureAwait(true);
        var sample = new ToolStripButton("Sample 10"); sample.Click += async (_, _) => await SampleAsync().ConfigureAwait(true);
        var newSeed = new ToolStripButton("New Seed"); newSeed.Click += (_, _) => _seed.Text = SeedFactory.CreateSeed().ToString(CultureInfo.InvariantCulture);
        strip.Items.AddRange([add, import, save, new ToolStripSeparator(), train, sample, new ToolStripSeparator(), new ToolStripLabel("Seed"), _seed, newSeed, new ToolStripSeparator(), _status]);
        return strip;
    }

    private Control BuildEditor()
    {
        var meta = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(6) };
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        meta.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddMeta(meta, 0, "ID", _id, "Name", _name); AddMeta(meta, 1, "Author", _author, "License", _license); AddMeta(meta, 2, "Source", _sourceUri, "Notes", _notes);

        var sourceTab = new TabPage("Corpus Text"); sourceTab.Controls.Add(_text);
        var trainingTab = new TabPage("Training & Cleaning");
        var settings = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 118, AutoScroll = true, Padding = new Padding(6), WrapContents = true };
        settings.Controls.AddRange([
            LabelFor("Mode"), _mode, LabelFor("Order"), _order, LabelFor("Min order"), _minOrder, _backoff, _case, _punctuation,
            LabelFor("Min tokens"), _minTokens, LabelFor("Max tokens"), _maxTokens, LabelFor("Max similarity"), _similarity, LabelFor("Attempts"), _attempts, _exact,
            _trim, _empty, _collapse, _duplicates, _lower]);
        trainingTab.Controls.Add(_normalized); trainingTab.Controls.Add(settings);
        var samplesTab = new TabPage("Live Samples"); samplesTab.Controls.Add(_samples);
        var statsTab = new TabPage("Model Statistics"); statsTab.Controls.Add(_stats);
        var tabs = new TabControl { Dock = DockStyle.Fill }; tabs.TabPages.AddRange([sourceTab, trainingTab, samplesTab, statsTab]);
        var panel = new Panel { Dock = DockStyle.Fill }; panel.Controls.Add(tabs); panel.Controls.Add(meta); return panel;
    }

    private async Task ReloadCorpusListAsync(CancellationToken cancellationToken = default)
    {
        var selected = _id.Text;
        await _session.ReloadAsync(cancellationToken).ConfigureAwait(true);
        _loading = true;
        try
        {
            _corpora.Items.Clear();
            foreach (var entry in _session.Corpora) _corpora.Items.Add(new CorpusItem(entry));
            for (var i = 0; i < _corpora.Items.Count; i++) if ((_corpora.Items[i] as CorpusItem)?.Entry.Corpus.Id.Equals(selected, StringComparison.OrdinalIgnoreCase) == true) { _corpora.SelectedIndex = i; break; }
        }
        finally { _loading = false; }
    }

    private void NewCorpus(bool markDirty)
    {
        _loading = true;
        try
        {
            _currentPath = null; _sourceFileName = null; _sourceSha256 = null; _sourceImportedAtUtc = null; _createdAtUtc = DateTimeOffset.UtcNow; _corpusTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "project" }; _id.Text = "custom-corpus"; _name.Text = "New Corpus"; _author.Clear(); _license.Text = "Project-authored"; _sourceUri.Clear(); _notes.Clear(); _text.Clear(); _samples.Items.Clear(); _stats.Clear();
        }
        finally { _loading = false; }
        SetDirty(markDirty); RefreshNormalizedPreview(); _text.Focus();
    }

    private void LoadCorpus(TextCorpusLibraryEntry entry)
    {
        _loading = true;
        try
        {
            _currentPath = entry.Path; var c = entry.Corpus; var source = c.Sources.FirstOrDefault();
            _sourceFileName = source?.OriginalFileName; _sourceSha256 = source?.Sha256; _sourceImportedAtUtc = source?.ImportedAtUtc; _createdAtUtc = c.CreatedAtUtc; _corpusTags = c.Tags is null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : new HashSet<string>(c.Tags, StringComparer.OrdinalIgnoreCase);
            _id.Text = c.Id; _name.Text = c.Name; _author.Text = source?.Author ?? string.Empty; _license.Text = source?.License ?? string.Empty; _sourceUri.Text = source?.SourceUri ?? string.Empty; _notes.Text = source?.Notes ?? string.Empty; _text.Text = c.Text;
            _trim.Checked = c.EffectiveNormalization.TrimLines; _empty.Checked = c.EffectiveNormalization.RemoveEmptyLines; _collapse.Checked = c.EffectiveNormalization.CollapseWhitespace; _duplicates.Checked = c.EffectiveNormalization.RemoveDuplicateLines; _lower.Checked = c.EffectiveNormalization.Lowercase;
            ApplyTrainingOptions(c.DefaultTraining ?? new MarkovTrainingOptions(CorpusTokenMode.Word, 2));
            _samples.Items.Clear(); _stats.Clear(); SetDirty(false);
        }
        finally { _loading = false; }
        RefreshNormalizedPreview();
    }

    private async Task ImportAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "Corpus files (*.txt;*.csv;*.json)|*.txt;*.csv;*.json|Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var optionsDialog = new CorpusImportDialog(dialog.FileName);
            if (optionsDialog.ShowDialog(this) != DialogResult.OK) return;
            _status.Text = "Importing…";
            var imported = await _session.ImportAsync(dialog.FileName, optionsDialog.Options).ConfigureAwait(true);
            _currentPath = null; LoadCorpus(new TextCorpusLibraryEntry(imported, string.Empty)); _currentPath = null; SetDirty(true); _status.Text = "Imported — review provenance, then Save Corpus.";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Corpus import failed", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "Import failed"; }
    }

    private async Task SaveAsync()
    {
        try
        {
            var corpus = BuildCorpus();
            _currentPath = await _session.SaveAsync(corpus, _currentPath).ConfigureAwait(true);
            await ReloadCorpusListAsync().ConfigureAwait(true); SetDirty(false); _status.Text = "Corpus saved";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Corpus save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task TrainAsync(bool force)
    {
        try
        {
            _status.Text = "Training model…";
            var model = await _session.TrainAsync(BuildCorpus(), BuildTrainingOptions(), force).ConfigureAwait(true);
            ShowStatistics(model.Statistics); _status.Text = _session.LastCacheWriteSucceeded ? "Model trained and cached" : "Model trained (cache write unavailable)";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Markov training failed", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "Training failed"; }
    }

    private async Task SampleAsync()
    {
        try
        {
            var corpus = BuildCorpus(); var options = BuildTrainingOptions();
            await _session.TrainAsync(corpus, options).ConfigureAwait(true);
            if (!ulong.TryParse(_seed.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) seed = SeedFactory.CreateSeed();
            var samples = _session.GenerateSamples(corpus, options, seed, 10);
            _samples.Items.Clear(); foreach (var sample in samples) _samples.Items.Add(new SampleItem(sample.Text, sample.Similarity));
            ShowStatistics(_session.Statistics!); _status.Text = samples.Count == 0 ? "No candidates passed originality checks" : $"{samples.Count} samples — double-click to copy";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Sampling failed", MessageBoxButtons.OK, MessageBoxIcon.Error); _status.Text = "Sampling failed"; }
    }

    public async Task<bool> PrepareForCloseAsync(CancellationToken cancellationToken = default)
    {
        if (!HasUnsavedChanges) return true;
        var choice = MessageBox.Show(this, "The Corpus Lab has unsaved corpus or training-setting changes. Save them before closing Text Studio?",
            "Playroma Corpus Lab", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return false;
        if (choice == DialogResult.Yes)
        {
            await SaveAsync().ConfigureAwait(true);
            if (HasUnsavedChanges) return false;
        }
        return true;
    }

    public async Task SaveCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SaveAsync().ConfigureAwait(true);
    }

    public CorpusLabRecoveryState? CaptureRecoveryState() =>
        HasUnsavedChanges ? new CorpusLabRecoveryState(System.Text.Json.JsonSerializer.Serialize(BuildCorpus(), CorpusJson.Options), _currentPath) : null;

    public void RestoreRecoveryState(CorpusLabRecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var corpus = System.Text.Json.JsonSerializer.Deserialize<TextCorpus>(state.CorpusJson, CorpusJson.Options)
            ?? throw new InvalidDataException("Recovered corpus payload was empty.");
        LoadCorpus(new TextCorpusLibraryEntry(corpus, state.Path ?? string.Empty));
        _currentPath = state.Path;
        SetDirty(true);
        _status.Text = "Recovered unsaved corpus changes";
    }

    public async Task DiscardUnsavedChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = CurrentCorpusId;
        var path = _currentPath;
        await _session.ReloadAsync(cancellationToken).ConfigureAwait(true);
        TextCorpusLibraryEntry? entry = null;
        if (!string.IsNullOrWhiteSpace(path))
            entry = _session.Corpora.FirstOrDefault(x => PathsEqual(x.Path, path));
        if (entry is null && !string.IsNullOrWhiteSpace(id))
            entry = _session.Corpora.FirstOrDefault(x => x.Corpus.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) NewCorpus(false);
        else LoadCorpus(entry);
        await ReloadCorpusListAsync(cancellationToken).ConfigureAwait(true);
        SetDirty(false);
        _status.Text = "Unsaved corpus changes discarded";
    }

    public async Task<bool> ReloadFromDiskAsync(bool discardUnsavedChanges, CancellationToken cancellationToken = default)
    {
        if (HasUnsavedChanges && !discardUnsavedChanges) return false;
        if (HasUnsavedChanges) await DiscardUnsavedChangesAsync(cancellationToken).ConfigureAwait(true);
        else await ReloadCorpusListAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    private void SetDirty(bool value)
    {
        if (_dirty == value) return;
        _dirty = value;
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }
    }

    private void ApplyTrainingOptions(MarkovTrainingOptions options)
    {
        options = options.Normalize();
        _mode.SelectedItem = options.Mode.ToString(); _order.Value = options.Order; _minOrder.Value = options.MinimumOrder;
        _backoff.Checked = options.VariableOrderBackoff; _case.Checked = options.PreserveCase; _punctuation.Checked = options.IncludePunctuation;
        _minTokens.Value = Math.Clamp(options.MinimumOutputTokens, (int)_minTokens.Minimum, (int)_minTokens.Maximum);
        _maxTokens.Value = Math.Clamp(options.MaximumOutputTokens, (int)_maxTokens.Minimum, (int)_maxTokens.Maximum);
        _similarity.Value = (decimal)Math.Clamp(options.MaximumCorpusSimilarity, 0, 1); _attempts.Value = options.CandidateAttempts; _exact.Checked = options.PreventExactCorpusMatches;
    }

    private TextCorpus BuildCorpus()
    {
        var id = string.IsNullOrWhiteSpace(_id.Text) ? "custom-corpus" : _id.Text.Trim();
        var name = string.IsNullOrWhiteSpace(_name.Text) ? id : _name.Text.Trim();
        var source = new CorpusSourceMetadata(name, _sourceFileName, EmptyToNull(_author.Text), EmptyToNull(_license.Text), EmptyToNull(_sourceUri.Text), EmptyToNull(_notes.Text), _sourceSha256, _sourceImportedAtUtc ?? DateTimeOffset.UtcNow);
        return new TextCorpus(id, 1, name, _text.Text, [source], BuildNormalization(), BuildTrainingOptions(), Tags: _corpusTags, CreatedAtUtc: _createdAtUtc ?? DateTimeOffset.UtcNow, UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private CorpusNormalizationOptions BuildNormalization() => new(true, true, _trim.Checked, _empty.Checked, _collapse.Checked, _duplicates.Checked, _lower.Checked);

    private MarkovTrainingOptions BuildTrainingOptions()
    {
        Enum.TryParse<CorpusTokenMode>(_mode.SelectedItem?.ToString(), true, out var mode);
        return new MarkovTrainingOptions(mode, (int)_order.Value, (int)_minOrder.Value, _backoff.Checked, _case.Checked, _punctuation.Checked,
            (int)_minTokens.Value, (int)_maxTokens.Value, _exact.Checked, (double)_similarity.Value, (int)_attempts.Value).Normalize();
    }

    private void RefreshNormalizedPreview()
    {
        if (_loading) return;
        try { _normalized.Text = CorpusNormalizer.Normalize(_text.Text, BuildNormalization()); } catch { _normalized.Clear(); }
    }

    private void ShowStatistics(MarkovModelStatistics stats)
    {
        _stats.Text = $"Mode: {stats.Mode}\r\nOrder: {stats.Order}\r\nInput characters: {stats.InputCharacters:N0}\r\nSequences: {stats.SequenceCount:N0}\r\nTokens: {stats.TokenCount:N0}\r\nUnique tokens: {stats.UniqueTokenCount:N0}\r\nStates: {stats.StateCount:N0}\r\nTransitions: {stats.TransitionCount:N0}\r\nTrained (UTC): {stats.TrainedAtUtc:u}\r\n\r\nCorpus fingerprint:\r\n{stats.CorpusFingerprint}\r\n\r\nModel fingerprint:\r\n{stats.ModelFingerprint}";
    }

    private static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 7, 2, 0) };
    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void AddMeta(TableLayoutPanel panel, int row, string l1, Control c1, string l2, Control c2)
    {
        panel.RowCount = Math.Max(panel.RowCount, row + 1); panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { Text = l1, AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, row); panel.Controls.Add(c1, 1, row);
        panel.Controls.Add(new Label { Text = l2, AutoSize = true, Margin = new Padding(8, 8, 3, 3) }, 2, row); panel.Controls.Add(c2, 3, row);
    }

    private sealed record CorpusItem(TextCorpusLibraryEntry Entry) { public override string ToString() => Entry.Corpus.Name; }
    private sealed record SampleItem(string Text, CorpusSimilarityReport Similarity) { public override string ToString() => $"{Text}   [similarity {Similarity.CombinedSimilarity:0.00}]"; }
}
