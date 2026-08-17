using Playroma.Text.Core.Generation;
using Playroma.Text.Core.Tracery;
using Playroma.Text.Core.Semantics;
using Playroma.Text.Generators.Tracery;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class TraceryGrammarEditorControl : UserControl
{
    private readonly ListBox _symbols = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _rules = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true };
    private readonly TextBox _origin = new() { Width = 120 };
    private readonly ComboBox _compatibility = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly TextBox _diagnostics = new() { Dock = DockStyle.Bottom, Height = 110, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private TraceryGrammarDocument? _grammar;
    private bool _rebuilding;

    public TraceryGrammarEditorControl()
    {
        _compatibility.Items.AddRange(Enum.GetNames<TraceryCompatibilityMode>());
        _rules.Columns.Add("Text", "Rule text");
        _rules.Columns.Add("Weight", "Weight");
        _rules.Columns.Add("Condition", "Condition");
        _rules.Columns.Add("Metadata", "Metadata (key=value; ...)");
        _rules.Columns.Add("Features", "Semantic features (key=value; ...)");

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false, Padding = new Padding(4) };
        var addSymbol = new Button { Text = "+ Symbol", AutoSize = true };
        var removeSymbol = new Button { Text = "− Symbol", AutoSize = true };
        var addRule = new Button { Text = "+ Rule", AutoSize = true };
        var removeRule = new Button { Text = "− Rule", AutoSize = true };
        var analyze = new Button { Text = "Analyze", AutoSize = true };
        toolbar.Controls.AddRange([new Label { Text = "Origin", AutoSize = true, Margin = new Padding(3, 8, 2, 0) }, _origin,
            new Label { Text = "Mode", AutoSize = true, Margin = new Padding(8, 8, 2, 0) }, _compatibility, addSymbol, removeSymbol, addRule, removeRule, analyze]);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 180 };
        split.Panel1.Controls.Add(_symbols); split.Panel2.Controls.Add(_rules);
        Controls.Add(split); Controls.Add(_diagnostics); Controls.Add(toolbar);

        _symbols.SelectedIndexChanged += (_, _) => LoadRules();
        _rules.CellEndEdit += (_, _) => CommitRules();
        _origin.TextChanged += (_, _) => CommitHeader();
        _compatibility.SelectedIndexChanged += (_, _) => CommitHeader();
        addSymbol.Click += (_, _) => AddSymbol();
        removeSymbol.Click += (_, _) => RemoveSymbol();
        addRule.Click += (_, _) => AddRule();
        removeRule.Click += (_, _) => RemoveRule();
        analyze.Click += (_, _) => Analyze();
    }

    public event EventHandler? GrammarChanged;
    public TraceryGrammarDocument? Grammar => _grammar;

    public void Bind(TraceryGrammarDocument? grammar)
    {
        _rebuilding = true;
        try
        {
            _grammar = grammar;
            _symbols.Items.Clear(); _rules.Rows.Clear(); _diagnostics.Clear();
            if (grammar is null) { _origin.Text = string.Empty; _compatibility.SelectedIndex = -1; return; }
            _origin.Text = grammar.Origin;
            _compatibility.SelectedItem = grammar.Compatibility.ToString();
            foreach (var symbol in grammar.Symbols.Keys.Order(StringComparer.OrdinalIgnoreCase)) _symbols.Items.Add(symbol);
            if (_symbols.Items.Count > 0) _symbols.SelectedIndex = 0;
            Analyze();
        }
        finally { _rebuilding = false; }
        LoadRules();
    }

    private void LoadRules()
    {
        if (_rebuilding || _grammar is null || _symbols.SelectedItem is not string symbol) return;
        _rebuilding = true;
        try
        {
            _rules.Rows.Clear();
            foreach (var rule in _grammar.Symbols[symbol])
                _rules.Rows.Add(rule.Text, rule.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture), rule.Condition ?? string.Empty,
                    rule.Metadata is null ? string.Empty : string.Join("; ", rule.Metadata.Select(x => $"{x.Key}={x.Value}")),
                    rule.Features is null ? string.Empty : string.Join("; ", rule.Features.Select(x => $"{x.Key}={FormatFeature(x.Value)}")));
        }
        finally { _rebuilding = false; }
    }

    private void CommitRules()
    {
        if (_rebuilding || _grammar is null || _symbols.SelectedItem is not string symbol) return;
        var definitions = new List<TraceryRuleDefinition>();
        foreach (DataGridViewRow row in _rules.Rows)
        {
            var text = Convert.ToString(row.Cells[0].Value) ?? string.Empty;
            if (!double.TryParse(Convert.ToString(row.Cells[1].Value), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var weight) || weight <= 0) weight = 1;
            var condition = Convert.ToString(row.Cells[2].Value);
            var metadata = ParseMetadata(Convert.ToString(row.Cells[3].Value));
            var features = ParseFeatures(Convert.ToString(row.Cells[4].Value));
            definitions.Add(new TraceryRuleDefinition(text, weight, metadata.Count == 0 ? null : metadata, string.IsNullOrWhiteSpace(condition) ? null : condition, features.Count == 0 ? null : features));
        }
        if (definitions.Count == 0) definitions.Add(new TraceryRuleDefinition(string.Empty));
        var symbols = _grammar.Symbols.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        symbols[symbol] = definitions;
        _grammar = _grammar with { Symbols = symbols };
        Changed();
    }

    private void CommitHeader()
    {
        if (_rebuilding || _grammar is null) return;
        Enum.TryParse<TraceryCompatibilityMode>(_compatibility.SelectedItem?.ToString(), true, out var mode);
        _grammar = _grammar with { Origin = string.IsNullOrWhiteSpace(_origin.Text) ? "origin" : _origin.Text.Trim(), Compatibility = mode };
        Changed();
    }

    private void AddSymbol()
    {
        if (_grammar is null) return;
        var baseName = "symbol"; var name = baseName; var i = 2;
        while (_grammar.Symbols.ContainsKey(name)) name = baseName + i++;
        var symbols = _grammar.Symbols.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        symbols[name] = [new TraceryRuleDefinition("new rule")];
        _grammar = _grammar with { Symbols = symbols };
        _symbols.Items.Add(name); _symbols.SelectedItem = name; Changed();
    }

    private void RemoveSymbol()
    {
        if (_grammar is null || _symbols.SelectedItem is not string symbol || _grammar.Symbols.Count <= 1) return;
        var symbols = _grammar.Symbols.Where(x => !string.Equals(x.Key, symbol, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        _grammar = _grammar with { Symbols = symbols, Origin = string.Equals(_grammar.Origin, symbol, StringComparison.OrdinalIgnoreCase) ? symbols.Keys.First() : _grammar.Origin };
        Bind(_grammar); Changed();
    }

    private void AddRule()
    {
        if (_grammar is null || _symbols.SelectedItem is null) return;
        _rules.Rows.Add("new rule", "1", string.Empty, string.Empty, string.Empty); CommitRules();
    }

    private void RemoveRule()
    {
        if (_rules.SelectedRows.Count == 0) return;
        foreach (DataGridViewRow row in _rules.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(x => x.Index))
            if (!row.IsNewRow) _rules.Rows.RemoveAt(row.Index);
        CommitRules();
    }

    private void Analyze()
    {
        if (_grammar is null) { _diagnostics.Clear(); return; }
        var diagnostics = TraceryGrammarValidator.Validate(_grammar).Concat(TraceryGrammarAnalyzer.Analyze(_grammar)).ToArray();
        _diagnostics.Lines = diagnostics.Length == 0 ? ["Grammar analysis: no problems found."] : diagnostics.Select(x => $"{x.Severity}: {x.Message}").ToArray();
    }

    private void Changed() { Analyze(); GrammarChanged?.Invoke(this, EventArgs.Empty); }

    private static Dictionary<string, SemanticFeatureValue> ParseFeatures(string? text)
    {
        var result = new Dictionary<string, SemanticFeatureValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = item.IndexOf('='); if (eq <= 0) continue;
            var key = item[..eq].Trim(); var value = item[(eq + 1)..].Trim();
            if (value.StartsWith('[') && value.EndsWith(']')) result[key] = SemanticFeatureValue.FromStrings(value[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            else if (bool.TryParse(value, out var b)) result[key] = SemanticFeatureValue.FromBoolean(b);
            else if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i)) result[key] = SemanticFeatureValue.FromInteger(i);
            else if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) result[key] = SemanticFeatureValue.FromNumber(d);
            else result[key] = SemanticFeatureValue.FromString(value);
        }
        return result;
    }

    private static string FormatFeature(SemanticFeatureValue value) => value.Type == SemanticFeatureType.StringList
        ? "[" + string.Join(",", value.EffectiveValues) + "]" : value.DisplayText;

    private static Dictionary<string, string> ParseMetadata(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = item.IndexOf('='); if (eq <= 0) continue;
            result[item[..eq].Trim()] = item[(eq + 1)..].Trim();
        }
        return result;
    }
}
