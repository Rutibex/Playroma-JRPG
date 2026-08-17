using Playroma.Text.Authoring.AI;
using Playroma.Text.Core.AI;
using Playroma.Text.Generators.AI;
using System.Text.Json;
using Playroma.Text.Core.Generation;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class AiProviderLabControl : UserControl
{
    private readonly TextStudioSession _session;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
    private readonly ToolStripLabel _status = new("Optional AI is disabled unless a provider is explicitly enabled.");
    private bool _loading;
    private NeuralProviderSettingsDocument _baseline = NeuralProviderSettingsDocument.Empty;
    public AiProviderLabControl(TextStudioSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Dock = DockStyle.Fill;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "Enabled", FillWeight = 45 });
        _grid.Columns.Add("Id", "Provider ID");
        _grid.Columns.Add("DisplayName", "Display name");
        _grid.Columns.Add("Executable", "Local executable");
        _grid.Columns.Add("Arguments", "Arguments");
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Timeout", HeaderText = "Timeout (s)", FillWeight = 55 });
        _grid.CellValueChanged += (_, _) => MarkDirty();
        _grid.UserDeletedRow += (_, _) => MarkDirty();
        _grid.RowsAdded += (_, _) => { if (!_loading) MarkDirty(); };

        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        var save = new ToolStripButton("Save Providers");
        var test = new ToolStripButton("Test Selected");
        var reload = new ToolStripButton("Reload");
        save.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        test.Click += async (_, _) => await TestSelectedAsync().ConfigureAwait(true);
        reload.Click += async (_, _) => await InitializeAsync().ConfigureAwait(true);
        strip.Items.AddRange([save, test, reload, new ToolStripSeparator(), _status]);
        Controls.Add(_grid); Controls.Add(strip);
        AccessibleName = "Optional AI Providers";
        AccessibleDescription = "Configure explicitly enabled local process-based text providers. Playroma remains fully functional without AI.";
    }

    public bool HasUnsavedChanges { get; private set; }
    public event EventHandler? DirtyStateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _loading = true;
        try
        {
            var doc = await _session.LoadNeuralProviderSettingsAsync(cancellationToken).ConfigureAwait(true);
            _baseline = doc;
            BindDocument(doc);
            HasUnsavedChanges = false;
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
            _status.Text = $"{doc.Providers.Count} configured; {_session.NeuralProviders.Count} enabled and loadable.";
        }
        finally { _loading = false; }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var providers = ReadRows();
        await _session.SaveNeuralProviderSettingsAsync(new NeuralProviderSettingsDocument(1, providers), cancellationToken).ConfigureAwait(true);
        _baseline = new NeuralProviderSettingsDocument(1, providers);
        HasUnsavedChanges = false;
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _status.Text = $"Saved. {_session.NeuralProviders.Count} provider(s) enabled and loadable.";
    }

    public void DiscardUnsavedChanges()
    {
        _loading = true;
        try { BindDocument(_baseline); HasUnsavedChanges = false; }
        finally { _loading = false; }
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _status.Text = "Unsaved optional-AI provider settings discarded.";
    }

    public AiProviderLabRecoveryState? CaptureRecoveryState()
        => HasUnsavedChanges ? new AiProviderLabRecoveryState(JsonSerializer.Serialize(new NeuralProviderSettingsDocument(1, ReadRows()), RecipeJson.Options)) : null;

    public void RestoreRecoveryState(AiProviderLabRecoveryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var doc = JsonSerializer.Deserialize<NeuralProviderSettingsDocument>(state.SettingsJson, RecipeJson.Options) ?? NeuralProviderSettingsDocument.Empty;
        _loading = true;
        try
        {
            BindDocument(doc);
            HasUnsavedChanges = true;
        }
        finally { _loading = false; }
        DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        _status.Text = "Recovered unsaved optional-AI provider settings.";
    }

    private void BindDocument(NeuralProviderSettingsDocument doc)
    {
        _grid.Rows.Clear();
        foreach (var p in doc.Providers) _grid.Rows.Add(p.Enabled, p.Id, p.DisplayName, p.ExecutablePath, p.Arguments, p.TimeoutSeconds);
    }

    private List<NeuralProviderSettings> ReadRows()
    {
        var result = new List<NeuralProviderSettings>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var id = Convert.ToString(row.Cells["Id"].Value)?.Trim();
            if (string.IsNullOrWhiteSpace(id)) continue;
            var enabled = row.Cells["Enabled"].Value is true;
            var display = Convert.ToString(row.Cells["DisplayName"].Value)?.Trim();
            var executable = Convert.ToString(row.Cells["Executable"].Value)?.Trim();
            var args = Convert.ToString(row.Cells["Arguments"].Value);
            var timeout = int.TryParse(Convert.ToString(row.Cells["Timeout"].Value), out var parsed) ? Math.Clamp(parsed, 1, 300) : 60;
            result.Add(new NeuralProviderSettings(id, string.IsNullOrWhiteSpace(display) ? id : display!, "process", enabled, executable, args, null, timeout));
        }
        return result;
    }

    private async Task TestSelectedAsync()
    {
        if (_grid.CurrentRow is null || _grid.CurrentRow.IsNewRow) return;
        var row = _grid.CurrentRow;
        var id = Convert.ToString(row.Cells["Id"].Value)?.Trim();
        var exe = Convert.ToString(row.Cells["Executable"].Value)?.Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(exe)) { _status.Text = "Select a provider with an executable path."; return; }
        try
        {
            var timeout = int.TryParse(Convert.ToString(row.Cells["Timeout"].Value), out var parsed) ? Math.Clamp(parsed, 1, 300) : 60;
            var provider = new ProcessNeuralTextProvider(id, Convert.ToString(row.Cells["DisplayName"].Value) ?? id, exe, Convert.ToString(row.Cells["Arguments"].Value), timeout: TimeSpan.FromSeconds(timeout));
            _status.Text = "Testing local provider…";
            var response = await provider.GenerateAsync(new NeuralTextRequest("Write one short fantasy NPC greeting.", "Return only the greeting.", 1, 400)).ConfigureAwait(true);
            _status.Text = "Test result: " + response.Text.Replace('\r', ' ').Replace('\n', ' ');
        }
        catch (Exception ex) { _status.Text = "Test failed: " + ex.Message; }
    }

    private void MarkDirty()
    {
        if (_loading) return;
        if (!HasUnsavedChanges) { HasUnsavedChanges = true; DirtyStateChanged?.Invoke(this, EventArgs.Empty); }
    }
}
