using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class PluginTagBatchEditorDialog : Form
{
    private readonly MvProjectSession projectSession;
    private readonly PluginTagCatalog catalog;
    private readonly PluginTagContext context;
    private readonly IReadOnlyList<PluginTagBatchRecord> records;
    private readonly IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability;
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search generic plugin tags…" };
    private readonly ListBox featureList = new() { Dock = DockStyle.Fill };
    private readonly DataGridView preview = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly Label summary = new() { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), AutoEllipsis = true };
    private readonly Button configure = new() { Text = "Configure Tag…", AutoSize = true };
    private readonly Button accept = new() { Text = "Apply Batch", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };
    private IReadOnlyList<PluginFeatureDefinition> visible = [];
    private PluginTagBatchPlan? plan;

    public PluginTagBatchEditorDialog(
        MvProjectSession projectSession,
        PluginTagCatalog catalog,
        PluginTagContext context,
        IReadOnlyList<PluginTagBatchRecord> records,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability>? availability = null)
    {
        this.projectSession = projectSession ?? throw new ArgumentNullException(nameof(projectSession));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.records = records ?? throw new ArgumentNullException(nameof(records));
        this.availability = availability ?? new Dictionary<PluginFeatureKey, PluginFeatureAvailability>();
        Text = $"Batch Plugin Tags — {records.Count} records";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        Size = new Size(1080, 720);
        MinimizeBox = false;
        MaximizeBox = false;

        preview.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Record", Width = 100 });
        preview.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Result", Width = 90 });
        preview.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Detail", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

        SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 330 };
        Panel left = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(featureList);
        left.Controls.Add(search);
        split.Panel1.Controls.Add(left);
        Panel right = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        right.Controls.Add(preview);
        right.Controls.Add(summary);
        split.Panel2.Controls.Add(right);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        buttons.Controls.Add(configure);
        Controls.Add(split);
        Controls.Add(buttons);
        AcceptButton = accept;
        CancelButton = cancel;

        search.TextChanged += (_, _) => RefreshFeatures();
        featureList.SelectedIndexChanged += (_, _) => RefreshSelection();
        configure.Click += (_, _) => ConfigureSelected();
        accept.Click += (_, _) =>
        {
            if (plan?.CanApply == true) return;
            DialogResult = DialogResult.None;
        };
        RefreshFeatures();
    }

    public PluginTagBatchPlan Plan => plan ?? throw new InvalidOperationException("No batch plan has been configured.");

    private void RefreshFeatures()
    {
        string? selected = SelectedFeature()?.FeatureId;
        visible = catalog.Query(context, search.Text)
            .Where(feature => !PluginGenericEditorCoverage.CreatePlan(feature).RequiresSpecializedEditor)
            .Where(feature => !availability.TryGetValue(feature.Key, out PluginFeatureAvailability? state) || state.CanAuthor)
            .OrderBy(feature => feature.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        featureList.BeginUpdate();
        featureList.Items.Clear();
        foreach (PluginFeatureDefinition feature in visible) featureList.Items.Add(feature.DisplayName + "  [" + feature.PluginId + "]");
        featureList.EndUpdate();
        int index = visible.ToList().FindIndex(feature => feature.FeatureId == selected);
        featureList.SelectedIndex = index >= 0 ? index : visible.Count > 0 ? 0 : -1;
        plan = null;
        preview.Rows.Clear();
        accept.Enabled = false;
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        PluginFeatureDefinition? feature = SelectedFeature();
        summary.Text = feature is null
            ? "No generic feature matches the current filter."
            : feature.Summary + "   Multiplicity: " + feature.Multiplicity;
        configure.Enabled = feature is not null;
    }

    private void ConfigureSelected()
    {
        PluginFeatureDefinition? feature = SelectedFeature();
        if (feature is null) return;
        using PluginFeatureEditorDialog editor = new(projectSession, feature);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        PluginTagWriteRequest request = new(feature.Key, editor.Values, editor.WriteMode, editor.ConflictResolution);
        plan = new PluginTagBatchPlanner(catalog).Plan(records, request);
        preview.Rows.Clear();
        foreach (PluginTagBatchRecordChange change in plan.Records)
        {
            string result = change.Error is not null ? "Error" : change.Changed ? "Changed" : "Unchanged";
            string detail = change.Error ?? string.Join("; ", change.Warnings);
            int row = preview.Rows.Add(change.RecordKey, result, detail);
            if (change.Error is not null) preview.Rows[row].DefaultCellStyle.ForeColor = Color.Firebrick;
        }
        summary.Text = $"{feature.DisplayName}: {plan.ChangedCount} changed, {plan.UnchangedCount} unchanged, {plan.ErrorCount} error(s).";
        accept.Enabled = plan.CanApply && plan.ChangedCount > 0;
    }

    private PluginFeatureDefinition? SelectedFeature() =>
        featureList.SelectedIndex >= 0 && featureList.SelectedIndex < visible.Count ? visible[featureList.SelectedIndex] : null;
}
