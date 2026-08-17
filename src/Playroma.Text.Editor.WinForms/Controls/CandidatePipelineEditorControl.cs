using Playroma.Text.Core.Generation;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class CandidatePipelineEditorControl : UserControl
{
    private readonly CheckBox _enabled = new() { Text = "Enable hidden-candidate pipeline", AutoSize = true };
    private readonly NumericUpDown _count = new() { Minimum = 1, Maximum = 512, Value = 12, Width = 70 };
    private readonly NumericUpDown _diversity = new() { Minimum = 0, Maximum = 10, DecimalPlaces = 2, Increment = .05M, Width = 70 };
    private readonly NumericUpDown _minimumDiversity = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = .05M, Width = 70 };
    private readonly CheckBox _rejectErrors = new() { Text = "Reject candidates with generation errors", Checked = true, AutoSize = true };
    private readonly DataGridView _filters = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly DataGridView _scorers = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly ComboBox _searchMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly NumericUpDown _population = new() { Minimum = 2, Maximum = 128, Value = 24, Width = 70 };
    private readonly NumericUpDown _iterations = new() { Minimum = 1, Maximum = 24, Value = 4, Width = 70 };
    private readonly NumericUpDown _elites = new() { Minimum = 1, Maximum = 128, Value = 4, Width = 70 };
    private readonly NumericUpDown _beam = new() { Minimum = 1, Maximum = 128, Value = 6, Width = 70 };
    private readonly NumericUpDown _bins = new() { Minimum = 2, Maximum = 32, Value = 8, Width = 70 };
    private readonly NumericUpDown _novelty = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 2, Increment = .05M, Value = .25M, Width = 70 };
    private readonly NumericUpDown _immigrants = new() { Minimum = 0, Maximum = .75M, DecimalPlaces = 2, Increment = .05M, Value = .15M, Width = 70 };
    private bool _rebuilding;

    public CandidatePipelineEditorControl()
    {
        _searchMode.Items.AddRange(Enum.GetNames<CandidateSearchMode>()); _searchMode.SelectedItem = CandidateSearchMode.RandomPool.ToString();

        _filters.Columns.Add("Id", "Filter ID");
        _filters.Columns.Add("Predicate", "Predicate");
        _filters.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Required", HeaderText = "Required", FillWeight = 45 });

        _scorers.Columns.Add("Id", "Score ID");
        var kind = new DataGridViewComboBoxColumn { Name = "Kind", HeaderText = "Kind", FillWeight = 70 };
        kind.Items.AddRange(Enum.GetNames<CandidateScorerKind>()); _scorers.Columns.Add(kind);
        _scorers.Columns.Add("Weight", "Weight");
        _scorers.Columns.Add("Predicate", "Predicate");
        _scorers.Columns.Add("Target", "Target");
        _scorers.Columns.Add("Ideal", "Ideal");
        _scorers.Columns.Add("Minimum", "Min");
        _scorers.Columns.Add("Maximum", "Max");
        _scorers.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Invert", HeaderText = "Invert", FillWeight = 35 });

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(5), WrapContents = false };
        top.Controls.AddRange([_enabled,
            new Label { Text = "Candidates", AutoSize = true, Margin = new Padding(12, 7, 2, 0) }, _count,
            new Label { Text = "Diversity weight", AutoSize = true, Margin = new Padding(12, 7, 2, 0) }, _diversity,
            new Label { Text = "Min diversity", AutoSize = true, Margin = new Padding(12, 7, 2, 0) }, _minimumDiversity,
            _rejectErrors]);

        var filterButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(3) };
        var addFilter = new Button { Text = "+ Filter", AutoSize = true }; var removeFilter = new Button { Text = "− Filter", AutoSize = true };
        filterButtons.Controls.AddRange([addFilter, removeFilter]);
        var filterPanel = new Panel { Dock = DockStyle.Fill }; filterPanel.Controls.Add(_filters); filterPanel.Controls.Add(filterButtons);

        var scoreButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(3) };
        var addScore = new Button { Text = "+ Scorer", AutoSize = true }; var removeScore = new Button { Text = "− Scorer", AutoSize = true };
        scoreButtons.Controls.AddRange([addScore, removeScore]);
        var scorePanel = new Panel { Dock = DockStyle.Fill }; scorePanel.Controls.Add(_scorers); scorePanel.Controls.Add(scoreButtons);

        var searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        searchPanel.Controls.Add(new Label { Text = "Seed-space search (optional)", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        searchPanel.Controls.Add(Row("Mode", _searchMode));
        searchPanel.Controls.Add(Row("Population", _population));
        searchPanel.Controls.Add(Row("Iterations", _iterations));
        searchPanel.Controls.Add(Row("Elite count", _elites));
        searchPanel.Controls.Add(Row("Beam width", _beam));
        searchPanel.Controls.Add(Row("Quality/diversity archive bins", _bins));
        searchPanel.Controls.Add(Row("Novelty weight", _novelty));
        searchPanel.Controls.Add(Row("Random immigrant rate", _immigrants));
        searchPanel.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(700, 0), Text = "Random Pool uses the standard candidate-selection behavior. Beam/Evolutionary search deterministic seed neighborhoods; Quality Diversity keeps the strongest result in distinct length/vocabulary niches." });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var filtersTab = new TabPage("Filters") { Padding = new Padding(4) }; filtersTab.Controls.Add(filterPanel);
        var scorersTab = new TabPage("Scorers") { Padding = new Padding(4) }; scorersTab.Controls.Add(scorePanel);
        var searchTab = new TabPage("Advanced Search") { Padding = new Padding(4) }; searchTab.Controls.Add(searchPanel);
        tabs.TabPages.AddRange([filtersTab, scorersTab, searchTab]);
        Controls.Add(tabs); Controls.Add(top);

        _enabled.CheckedChanged += (_, _) => Changed(); _count.ValueChanged += (_, _) => Changed(); _diversity.ValueChanged += (_, _) => Changed();
        _minimumDiversity.ValueChanged += (_, _) => Changed(); _rejectErrors.CheckedChanged += (_, _) => Changed();
        _searchMode.SelectedIndexChanged += (_, _) => Changed(); _population.ValueChanged += (_, _) => Changed(); _iterations.ValueChanged += (_, _) => Changed(); _elites.ValueChanged += (_, _) => Changed(); _beam.ValueChanged += (_, _) => Changed(); _bins.ValueChanged += (_, _) => Changed(); _novelty.ValueChanged += (_, _) => Changed(); _immigrants.ValueChanged += (_, _) => Changed();
        _filters.CellEndEdit += (_, _) => Changed(); _filters.CellValueChanged += (_, _) => Changed();
        _scorers.CellEndEdit += (_, _) => Changed(); _scorers.CellValueChanged += (_, _) => Changed();
        addFilter.Click += (_, _) => { _filters.Rows.Add("semantic-match", "feature.tone == context.tone", true); Changed(); };
        removeFilter.Click += (_, _) => { RemoveSelected(_filters); Changed(); };
        addScore.Click += (_, _) => { _scorers.Rows.Add("quality", CandidateScorerKind.UniqueWordRatio.ToString(), "1", "", "", "", "", "", false); Changed(); };
        removeScore.Click += (_, _) => { RemoveSelected(_scorers); Changed(); };
        UpdateEnabledState();
    }

    public event EventHandler? PipelineChanged;
    public CandidatePipelineDefinition? Pipeline => !_enabled.Checked ? null : BuildPipeline();

    public void Bind(CandidatePipelineDefinition? pipeline)
    {
        _rebuilding = true;
        try
        {
            _enabled.Checked = pipeline is not null;
            var p = pipeline ?? new CandidatePipelineDefinition(12);
            _count.Value = Math.Clamp(p.EffectiveCandidateCount, 1, 512);
            _diversity.Value = (decimal)Math.Clamp(p.EffectiveDiversityWeight, 0, 10);
            _minimumDiversity.Value = (decimal)Math.Clamp(p.EffectiveMinimumDiversity, 0, 1);
            _rejectErrors.Checked = p.RejectGenerationErrors;
            var search = p.Search ?? new CandidateSearchDefinition();
            _searchMode.SelectedItem = search.Mode.ToString();
            _population.Value = search.EffectivePopulationSize; _iterations.Value = search.EffectiveIterations; _elites.Value = search.EffectiveEliteCount; _beam.Value = search.EffectiveBeamWidth; _bins.Value = search.EffectiveArchiveBins;
            _novelty.Value = (decimal)search.EffectiveNoveltyWeight; _immigrants.Value = (decimal)search.EffectiveRandomImmigrantRate;
            _filters.Rows.Clear();
            foreach (var filter in p.EffectiveFilters) _filters.Rows.Add(filter.Id, filter.Predicate, filter.Required);
            _scorers.Rows.Clear();
            foreach (var scorer in p.EffectiveScorers)
                _scorers.Rows.Add(scorer.Id, scorer.Kind.ToString(), scorer.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture), scorer.Predicate ?? "", scorer.Target ?? "",
                    Num(scorer.Ideal), Num(scorer.Minimum), Num(scorer.Maximum), scorer.Invert);
        }
        finally { _rebuilding = false; }
        UpdateEnabledState();
    }

    private CandidatePipelineDefinition BuildPipeline()
    {
        var filters = new List<CandidateFilterDefinition>();
        foreach (DataGridViewRow row in _filters.Rows)
        {
            var id = Convert.ToString(row.Cells[0].Value)?.Trim() ?? string.Empty;
            var predicate = Convert.ToString(row.Cells[1].Value)?.Trim() ?? string.Empty;
            if (id.Length == 0 && predicate.Length == 0) continue;
            filters.Add(new CandidateFilterDefinition(id.Length == 0 ? "filter" : id, predicate, Convert.ToBoolean(row.Cells[2].Value ?? true)));
        }
        var scorers = new List<CandidateScorerDefinition>();
        foreach (DataGridViewRow row in _scorers.Rows)
        {
            var id = Convert.ToString(row.Cells[0].Value)?.Trim() ?? string.Empty;
            if (id.Length == 0) continue;
            if (!Enum.TryParse<CandidateScorerKind>(Convert.ToString(row.Cells[1].Value), true, out var kind)) kind = CandidateScorerKind.UniqueWordRatio;
            var weight = ParseDouble(row.Cells[2].Value) ?? 1;
            var predicate = EmptyToNull(row.Cells[3].Value); var target = EmptyToNull(row.Cells[4].Value);
            scorers.Add(new CandidateScorerDefinition(id, kind, weight, predicate, target,
                ParseDouble(row.Cells[5].Value), ParseDouble(row.Cells[6].Value), ParseDouble(row.Cells[7].Value), Convert.ToBoolean(row.Cells[8].Value ?? false)));
        }
        var mode = Enum.TryParse<CandidateSearchMode>(Convert.ToString(_searchMode.SelectedItem), true, out var parsedMode) ? parsedMode : CandidateSearchMode.RandomPool;
        CandidateSearchDefinition? search = mode == CandidateSearchMode.RandomPool ? null : new CandidateSearchDefinition(mode, (int)_population.Value, (int)_iterations.Value, (int)_elites.Value, (int)_beam.Value, (int)_bins.Value, (double)_novelty.Value, (double)_immigrants.Value);
        return new CandidatePipelineDefinition((int)_count.Value, filters, scorers, (double)_diversity.Value, (double)_minimumDiversity.Value, _rejectErrors.Checked, search);
    }

    private void Changed()
    {
        if (_rebuilding) return;
        UpdateEnabledState(); PipelineChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEnabledState()
    {
        var enabled = _enabled.Checked;
        _count.Enabled = enabled; _diversity.Enabled = enabled; _minimumDiversity.Enabled = enabled; _rejectErrors.Enabled = enabled;
        _filters.Enabled = enabled; _scorers.Enabled = enabled; _searchMode.Enabled = enabled; _population.Enabled = enabled; _iterations.Enabled = enabled; _elites.Enabled = enabled; _beam.Enabled = enabled; _bins.Enabled = enabled; _novelty.Enabled = enabled; _immigrants.Enabled = enabled;
    }

    private static Control Row(string label, Control control)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
        panel.Controls.Add(new Label { Text = label, Width = 210, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 8, 0) });
        panel.Controls.Add(control);
        return panel;
    }

    private static void RemoveSelected(DataGridView grid)
    {
        foreach (DataGridViewRow row in grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(x => x.Index)) if (!row.IsNewRow) grid.Rows.RemoveAt(row.Index);
    }
    private static string Num(double? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    private static double? ParseDouble(object? value) => double.TryParse(Convert.ToString(value), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    private static string? EmptyToNull(object? value) { var s = Convert.ToString(value)?.Trim(); return string.IsNullOrWhiteSpace(s) ? null : s; }
}
