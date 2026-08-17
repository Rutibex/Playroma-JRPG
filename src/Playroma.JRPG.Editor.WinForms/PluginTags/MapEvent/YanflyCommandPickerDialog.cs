using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class YanflyCommandPickerDialog : Form
{
    private readonly MvProjectSession project;
    private readonly PluginTagCatalog catalog;
    private readonly PluginAuthoringSurface surface;
    private readonly IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability;
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Search YEP commands…" };
    private readonly CheckBox installedOnly = new() { Text = "Installed only", AutoSize = true, Checked = true };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoGenerateColumns = false
    };
    private readonly TextBox detail = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };

    public YanflyCommandPickerDialog(
        MvProjectSession project,
        PluginTagCatalog catalog,
        PluginAuthoringSurface surface,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability)
    {
        this.project = project;
        this.catalog = catalog;
        this.surface = surface;
        this.availability = availability;
        Text = surface == PluginAuthoringSurface.PluginCommand
            ? "YEP Plugin Command Builder"
            : "YEP Move Route Helpers";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(760, 520);
        Size = new Size(980, 680);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Command", HeaderText = "Command", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Plugin", HeaderText = "Plugin", Width = 190 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 110 });

        FlowLayoutPanel top = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6), WrapContents = false };
        search.Width = 300;
        top.Controls.AddRange([new Label { Text = "Find:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, search, installedOnly]);
        SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 600 };
        split.Panel1.Controls.Add(grid);
        split.Panel2.Controls.Add(detail);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button use = new() { Text = "Configure Command…", AutoSize = true };
        buttons.Controls.AddRange([cancel, use]);
        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(buttons);
        CancelButton = cancel;

        search.TextChanged += (_, _) => RefreshGrid();
        installedOnly.CheckedChanged += (_, _) => RefreshGrid();
        grid.SelectionChanged += (_, _) => RefreshDetail();
        grid.CellDoubleClick += (_, _) => ConfigureSelected();
        use.Click += (_, _) => ConfigureSelected();
        RefreshGrid();
    }

    public string ResultText { get; private set; } = string.Empty;
    private PluginFeatureDefinition? Selected => grid.CurrentRow?.Tag as PluginFeatureDefinition;

    private void RefreshGrid()
    {
        string query = search.Text.Trim();
        PluginFeatureKey? selected = Selected?.Key;
        IEnumerable<PluginFeatureDefinition> source = catalog.Features.Where(feature =>
            feature.Surface == surface &&
            (surface != PluginAuthoringSurface.PluginCommand ||
             feature.SpecializedEditorId?.Equals(YanflyMapEventFeatureDefinitions.PluginCommandEditorId, StringComparison.OrdinalIgnoreCase) == true));
        if (query.Length > 0)
        {
            source = source.Where(feature => string.Join(" ", feature.DisplayName, feature.PluginId, feature.Summary,
                feature.Syntax.CanonicalTemplate, string.Join(" ", feature.EffectiveSearchTerms))
                .Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        grid.Rows.Clear();
        foreach (PluginFeatureDefinition feature in source.OrderBy(feature => feature.PluginId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(feature => feature.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            PluginFeatureAvailability state = State(feature);
            if (installedOnly.Checked && state.State == PluginFeatureAvailabilityState.OwnerPluginMissing) continue;
            int row = grid.Rows.Add(feature.DisplayName, feature.PluginId, state.State.ToString());
            grid.Rows[row].Tag = feature;
        }
        if (grid.Rows.Count > 0)
        {
            DataGridViewRow? match = selected.HasValue
                ? grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => (row.Tag as PluginFeatureDefinition)?.Key == selected.Value)
                : null;
            grid.CurrentCell = (match ?? grid.Rows[0]).Cells[0];
        }
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        PluginFeatureDefinition? feature = Selected;
        if (feature is null) { detail.Clear(); return; }
        PluginFeatureAvailability state = State(feature);
        detail.Text = feature.DisplayName + Environment.NewLine + feature.Summary + Environment.NewLine + Environment.NewLine +
            "Plugin: " + feature.PluginId + Environment.NewLine +
            "Availability: " + state.Message + Environment.NewLine + Environment.NewLine +
            "Canonical syntax:" + Environment.NewLine + feature.Syntax.CanonicalTemplate;
    }

    private void ConfigureSelected()
    {
        PluginFeatureDefinition? feature = Selected;
        if (feature is null) return;
        PluginFeatureAvailability state = State(feature);
        if (!state.CanAuthor)
        {
            MessageBox.Show(this, state.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using PluginFeatureEditorDialog editor = new(project, feature);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        ResultText = surface == PluginAuthoringSurface.PluginCommand
            ? PluginCommandLineBuilder.Render(feature, editor.Values).CommandLine
            : MoveRouteScriptBuilder.Render(feature, editor.Values);
        DialogResult = DialogResult.OK;
        Close();
    }

    private PluginFeatureAvailability State(PluginFeatureDefinition feature) =>
        availability.TryGetValue(feature.Key, out PluginFeatureAvailability? found)
            ? found
            : new(feature.Key, PluginFeatureAvailabilityState.Available,
                "Installed-plugin analysis has not reported a blocking issue.", []);
}
