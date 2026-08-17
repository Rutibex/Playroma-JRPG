using Playroma.Sound.Authoring.Export;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Authoring.Workspace;

namespace Playroma.Sound.WinForms.Dialogs;

public sealed class ExportProjectDialog : Form
{
    private readonly AuthoringWorkspace _workspace;
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _output = new();
    private readonly NumericUpDown _sampleRate = new() { Minimum = 8_000, Maximum = 384_000, Value = 48_000, Increment = 1_000 };
    private readonly NumericUpDown _channels = new() { Minimum = 1, Maximum = 8, Value = 2 };
    private readonly NumericUpDown _blockSize = new() { Minimum = 16, Maximum = 16_384, Value = 512, Increment = 16 };
    private readonly NumericUpDown _duration = new() { Minimum = 1, Maximum = 86_400, Value = 60, DecimalPlaces = 1 };
    private readonly NumericUpDown _variations = new() { Minimum = 1, Maximum = 10_000, Value = 1 };
    private readonly ComboBox _encoding = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _stems = new() { Text = "Render scene bus stems", AutoSize = true };
    private readonly CheckBox _sources = new() { Text = "Include source definitions with rendered WAVs", AutoSize = true, Checked = true };
    private readonly CheckedListBox _assets = new() { Dock = DockStyle.Fill, CheckOnClick = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000 };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Ready" };
    private readonly Button _export = new() { Text = "Export", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
    private CancellationTokenSource? _cancellation;

    public ExportProjectDialog(AuthoringWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        Text = "Export Playroma Sound Project";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 620);
        Size = new Size(820, 700);
        _mode.Items.AddRange(Enum.GetValues<AuthoringExportMode>().Cast<object>().ToArray());
        _mode.SelectedItem = AuthoringExportMode.RenderedAudio;
        _encoding.Items.AddRange(Enum.GetValues<AuthoringWaveEncoding>().Cast<object>().ToArray());
        _encoding.SelectedItem = AuthoringWaveEncoding.Pcm16;
        _output.Text = "Exports/Rendered";
        PopulateAssets(renderedOnly: true);

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddField(fields, "Mode", _mode);
        AddField(fields, "Output directory", _output);
        AddField(fields, "Sample rate", _sampleRate);
        AddField(fields, "Channels", _channels);
        AddField(fields, "Block size", _blockSize);
        AddField(fields, "Scene/max duration", _duration);
        AddField(fields, "Variation count", _variations);
        AddField(fields, "WAV encoding", _encoding);
        fields.Controls.Add(_stems, 1, fields.RowCount++);
        fields.Controls.Add(_sources, 1, fields.RowCount++);

        var assetsGroup = new GroupBox { Text = "Assets", Dock = DockStyle.Fill, Padding = new Padding(8) };
        assetsGroup.Controls.Add(_assets);
        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 72, ColumnCount = 3, Padding = new Padding(10) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var progressPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        progressPanel.Controls.Add(_progress, 0, 0);
        progressPanel.Controls.Add(_status, 0, 1);
        footer.Controls.Add(progressPanel, 0, 0);
        footer.Controls.Add(_export, 1, 0);
        footer.Controls.Add(_cancel, 2, 0);
        var root = new Panel { Dock = DockStyle.Fill };
        root.Controls.Add(assetsGroup);
        root.Controls.Add(fields);
        root.Controls.Add(footer);
        Controls.Add(root);
        AcceptButton = _export;
        CancelButton = _cancel;
        _mode.SelectedIndexChanged += (_, _) => UpdateMode();
        _export.Click += async (_, _) => await ExportAsync();
        FormClosing += (_, eventArgs) =>
        {
            if (_cancellation is null) return;
            _cancellation.Cancel();
            eventArgs.Cancel = true;
        };
        UpdateMode();
    }

    public string? CompletedOutputRoot { get; private set; }

    private async Task ExportAsync()
    {
        if (_cancellation is not null) { _cancellation.Cancel(); return; }
        var selectedAssets = _assets.CheckedItems.Cast<AssetItem>().Select(item => item.Asset.Id).ToList();
        if (selectedAssets.Count == 0) { MessageBox.Show(this, "Select at least one asset.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        AuthoringExportMode selectedMode = (AuthoringExportMode)_mode.SelectedItem!;
        var profile = new AuthoringExportProfile
        {
            Id = "interactive.export",
            DisplayName = "Interactive Export",
            Mode = selectedMode,
            OutputDirectory = _output.Text,
            AssetIds = selectedAssets,
            SampleRate = Decimal.ToInt32(_sampleRate.Value),
            Channels = Decimal.ToInt32(_channels.Value),
            BlockSize = Decimal.ToInt32(_blockSize.Value),
            SceneDurationSeconds = Decimal.ToDouble(_duration.Value),
            VariationCount = Decimal.ToInt32(_variations.Value),
            WaveEncoding = (AuthoringWaveEncoding)_encoding.SelectedItem!,
            RenderStems = _stems.Checked,
            IncludeSourceDefinitions = selectedMode == AuthoringExportMode.RuntimePackage || _sources.Checked
        };
        AuthoringExportPlan plan = _workspace.CreateExportPlan(profile);
        if (!plan.CanExport)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, plan.Validation.Diagnostics.Select(diagnostic => diagnostic.Message)), "Export validation failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _cancellation = new CancellationTokenSource();
        _export.Text = "Cancel";
        _cancel.Enabled = false;
        SetFieldsEnabled(false);
        var progress = new Progress<AuthoringExportProgress>(update =>
        {
            _progress.Value = Math.Clamp((int)Math.Round(update.Fraction * 1000), 0, 1000);
            _status.Text = string.IsNullOrEmpty(update.AssetId) ? update.Message : $"{update.AssetId}: {update.Message}";
        });
        try
        {
            AuthoringExportExecutionResult result = await new AuthoringExportExecutor().ExecuteAsync(_workspace, plan, progress, _cancellation.Token);
            if (!result.Succeeded)
            {
                if (result.Exception is OperationCanceledException) _status.Text = "Export cancelled";
                else MessageBox.Show(this, result.Exception?.Message ?? "Export failed.", "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            CompletedOutputRoot = result.OutputRoot;
            _progress.Value = 1000;
            _status.Text = $"Exported {result.RenderedOutputs.Count + result.CopiedFiles.Count} files to {result.OutputRoot}";
            MessageBox.Show(this, _status.Text, "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _export.Text = "Export";
            _cancel.Enabled = true;
            SetFieldsEnabled(true);
        }
    }

    private void SetFieldsEnabled(bool enabled)
    {
        _mode.Enabled = enabled; _output.Enabled = enabled; _sampleRate.Enabled = enabled; _channels.Enabled = enabled;
        _blockSize.Enabled = enabled; _duration.Enabled = enabled; _variations.Enabled = enabled; _encoding.Enabled = enabled;
        _stems.Enabled = enabled && (AuthoringExportMode)_mode.SelectedItem! == AuthoringExportMode.RenderedAudio;
        _sources.Enabled = enabled && (AuthoringExportMode)_mode.SelectedItem! == AuthoringExportMode.RenderedAudio; _assets.Enabled = enabled;
    }

    private void UpdateMode()
    {
        bool rendered = (AuthoringExportMode)_mode.SelectedItem! == AuthoringExportMode.RenderedAudio;
        _sampleRate.Enabled = rendered; _channels.Enabled = rendered; _blockSize.Enabled = rendered; _duration.Enabled = rendered;
        _variations.Enabled = rendered; _encoding.Enabled = rendered; _stems.Enabled = rendered; _sources.Enabled = rendered;
        PopulateAssets(rendered);
        if (_output.Text is "Exports/Rendered" or "Exports/Runtime") _output.Text = rendered ? "Exports/Rendered" : "Exports/Runtime";
    }

    private void PopulateAssets(bool renderedOnly)
    {
        var checkedIds = _assets.CheckedItems.Cast<AssetItem>().Select(item => item.Asset.Id).ToHashSet(StringComparer.Ordinal);
        bool firstPopulation = _assets.Items.Count == 0;
        _assets.BeginUpdate();
        _assets.Items.Clear();
        foreach (ProjectAssetEntry asset in _workspace.Project.Assets.Where(asset => !renderedOnly || asset.Kind is AuthoringAssetKind.SoundGraph or AuthoringAssetKind.GeneratorRequest or AuthoringAssetKind.StreamingScene))
            _assets.Items.Add(new AssetItem(asset), firstPopulation || checkedIds.Contains(asset.Id));
        _assets.EndUpdate();
    }

    private static void AddField(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, row);
        control.Dock = DockStyle.Fill;
        table.Controls.Add(control, 1, row);
    }

    private sealed record AssetItem(ProjectAssetEntry Asset)
    {
        public override string ToString() => $"{Asset.DisplayName}  ({Asset.Id})";
    }
}
