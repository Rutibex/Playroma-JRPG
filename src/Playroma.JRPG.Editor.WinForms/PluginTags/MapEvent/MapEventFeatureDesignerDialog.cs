using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class MapEventFeatureDesignerDialog : Form
{
    private readonly MvProjectSession project;
    private readonly PluginTagCatalog catalog;
    private readonly PluginTextEditingSession session;
    private readonly MapEventAuthoringWorkspace workspace;
    private readonly string editorId;
    private readonly TextBox search = new() { Width = 250, PlaceholderText = "Search features…" };
    private readonly CheckBox currentOnly = new() { Text = "Current only", AutoSize = true };
    private readonly CheckBox installedOnly = new() { Text = "Installed only", AutoSize = true, Checked = true };
    private readonly CheckBox advanced = new() { Text = "Advanced", AutoSize = true };
    private readonly ComboBox area = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
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

    public MapEventFeatureDesignerDialog(
        MvProjectSession project,
        PluginTagCatalog catalog,
        PluginTagContext context,
        string currentText,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability,
        string editorId)
    {
        this.project = project;
        this.catalog = catalog;
        this.editorId = editorId;
        session = new(catalog, context, currentText);
        workspace = new(catalog, session, YanflyMapEventFeatureDefinitions.Classify, availability.Values);
        Text = YanflyMapEventFeatureDefinitions.FriendlyEditor(editorId);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimumSize = new Size(820, 600);
        Size = new Size(1050, 740);

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Used", HeaderText = "Used", Width = 48 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Feature", HeaderText = "Feature", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Plugin", HeaderText = "Plugin", Width = 180 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 100 });
        area.Items.Add("All areas");
        foreach (MapEventAuthoringArea value in Enum.GetValues<MapEventAuthoringArea>()) area.Items.Add(YanflyMapEventFeatureDefinitions.FriendlyArea(value));
        area.SelectedIndex = 0;

        FlowLayoutPanel filters = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6), WrapContents = false };
        filters.Controls.AddRange([
            new Label { Text = "Find:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, search,
            new Label { Text = "Area:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, area,
            currentOnly, installedOnly, advanced]);

        SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 650 };
        split.Panel1.Controls.Add(grid);
        split.Panel2.Controls.Add(detail);

        FlowLayoutPanel actions = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), WrapContents = false };
        Button add = new() { Text = "Add", AutoSize = true };
        Button edit = new() { Text = "Edit", AutoSize = true };
        Button remove = new() { Text = "Remove", AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button ok = new() { Text = "Use Changes", AutoSize = true };
        actions.Controls.AddRange([add, edit, remove, new Label { Width = 24 }, ok, cancel]);
        Controls.Add(split);
        Controls.Add(filters);
        Controls.Add(actions);
        AcceptButton = ok;
        CancelButton = cancel;

        search.TextChanged += (_, _) => RefreshGrid();
        area.SelectedIndexChanged += (_, _) => RefreshGrid();
        currentOnly.CheckedChanged += (_, _) => RefreshGrid();
        installedOnly.CheckedChanged += (_, _) => RefreshGrid();
        advanced.CheckedChanged += (_, _) => RefreshGrid();
        grid.SelectionChanged += (_, _) => RefreshDetail();
        grid.CellDoubleClick += (_, _) => EditSelected();
        add.Click += (_, _) => AddSelected();
        edit.Click += (_, _) => EditSelected();
        remove.Click += (_, _) => RemoveSelected();
        ok.Click += (_, _) => { ResultText = session.WorkingText; DialogResult = DialogResult.OK; Close(); };
        session.ParsedDocumentChanged += (_, _) => RefreshGrid();
        RefreshGrid();
    }

    public string ResultText { get; private set; } = string.Empty;

    private MapEventFeatureItem? Selected => grid.CurrentRow?.Tag as MapEventFeatureItem;

    private void RefreshGrid()
    {
        MapEventAuthoringArea? selectedArea = area.SelectedIndex <= 0 ? null : Enum.GetValues<MapEventAuthoringArea>()[area.SelectedIndex - 1];
        IReadOnlyList<MapEventFeatureItem> items = workspace.Query(new(
            SearchText: search.Text,
            SpecializedEditorId: editorId,
            CurrentOnly: currentOnly.Checked,
            InstalledOnly: installedOnly.Checked,
            IncludeAdvanced: advanced.Checked,
            Area: selectedArea));
        PluginFeatureKey? selected = Selected?.Definition.Key;
        grid.Rows.Clear();
        foreach (MapEventFeatureItem item in items)
        {
            int row = grid.Rows.Add(item.IsPresent ? "Yes" : string.Empty, item.Definition.DisplayName,
                item.Definition.PluginId, item.Availability.State.ToString());
            grid.Rows[row].Tag = item;
        }
        if (grid.Rows.Count > 0)
        {
            DataGridViewRow? match = selected.HasValue
                ? grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => (row.Tag as MapEventFeatureItem)?.Definition.Key == selected.Value)
                : null;
            grid.CurrentCell = (match ?? grid.Rows[0]).Cells[1];
        }
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        MapEventFeatureItem? item = Selected;
        if (item is null) { detail.Clear(); return; }
        detail.Text = item.Definition.DisplayName + Environment.NewLine +
            item.Definition.Summary + Environment.NewLine + Environment.NewLine +
            "Area: " + YanflyMapEventFeatureDefinitions.FriendlyArea(item.Area) + Environment.NewLine +
            "Plugin: " + item.Definition.PluginId + Environment.NewLine +
            "Status: " + item.Availability.Message + Environment.NewLine + Environment.NewLine +
            "Syntax:" + Environment.NewLine + item.Definition.Syntax.CanonicalTemplate;
    }

    private void AddSelected() => EditFeature(Selected, isEditing: false);
    private void EditSelected() => EditFeature(Selected, isEditing: true);

    private void EditFeature(MapEventFeatureItem? item, bool isEditing)
    {
        if (item is null) return;
        if (!item.CanAuthor)
        {
            MessageBox.Show(this, item.Availability.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        PluginFeatureValues? existing = isEditing ? ValuesOf(session.ParsedDocument.Find(item.Definition.Key).FirstOrDefault()) : null;
        using PluginFeatureEditorDialog editor = new(project, item.Definition, existing, isEditing);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        PluginTagChangePreview preview;
        try
        {
            preview = workspace.PreviewWrite(new(item.Definition.Key, editor.Values, editor.WriteMode, editor.ConflictResolution));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        using PluginTagChangePreviewDialog confirm = new("Confirm Plugin Tag Change", preview);
        if (confirm.ShowDialog(this) == DialogResult.OK) session.ReplaceText(preview.After);
    }

    private void RemoveSelected()
    {
        MapEventFeatureItem? item = Selected;
        if (item is null || !item.IsPresent) return;
        PluginTagChangePreview preview = workspace.PreviewRemove(item.Definition.Key, removeAll: true);
        using PluginTagChangePreviewDialog confirm = new("Remove Plugin Tag", preview);
        if (confirm.ShowDialog(this) == DialogResult.OK) session.ReplaceText(preview.After);
    }

    private static PluginFeatureValues? ValuesOf(PluginTextNode? node) => node switch
    {
        RecognizedTagNode tag => tag.Values,
        RecognizedBlockNode block => block.Values,
        _ => null
    };
}
