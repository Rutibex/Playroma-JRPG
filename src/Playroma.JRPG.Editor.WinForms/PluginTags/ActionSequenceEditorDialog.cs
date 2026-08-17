using System.ComponentModel;
using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class ActionSequenceEditorDialog : Form
{
    private readonly string originalNotes;
    private readonly PluginTagCatalog catalog;
    private readonly IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability;
    private readonly Dictionary<ActionSequencePhase, BindingList<ActionSequenceEntryRow>> rows = [];
    private readonly Dictionary<ActionSequencePhase, DataGridView> grids = [];
    private readonly TabControl phaseTabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Find commands…" };
    private readonly ComboBox category = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox palette = new() { Dock = DockStyle.Fill, DisplayMember = nameof(ActionSequenceCommandItem.DisplayText) };
    private readonly TextBox commandHelp = new() { Dock = DockStyle.Bottom, Height = 90, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly CheckBox useActionCopy = new() { Text = "Use Action Copy instead of custom phases", AutoSize = true };
    private readonly ComboBox copyType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly NumericUpDown copyId = new() { Minimum = 1, Maximum = 999999, Width = 95 };
    private readonly TextBox preview = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly TextBox validation = new() { Dock = DockStyle.Bottom, Height = 68, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly IReadOnlyList<ActionSequenceCommandDescriptor> commands;
    private readonly ActionSequenceModel parsed;
    private bool refreshing;

    public ActionSequenceEditorDialog(
        MvProjectSession projectSession,
        PluginTagCatalog catalog,
        string notes,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability)
    {
        _ = projectSession ?? throw new ArgumentNullException(nameof(projectSession));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.availability = availability ?? new Dictionary<PluginFeatureKey, PluginFeatureAvailability>();
        originalNotes = notes ?? string.Empty;
        parsed = ActionSequenceCodec.Parse(originalNotes);
        commands = YanflyActionSequenceCommandCatalog.Create(catalog);
        Text = "Action Sequence Editor";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1050, 720);
        Size = new Size(1240, 850);
        ShowInTaskbar = false;
        MinimizeBox = false;
        BuildLayout();
        LoadModel();
        RefreshPalette();
        RefreshPreview();
    }

    public string ResultNotes { get; private set; } = string.Empty;

    private void BuildLayout()
    {
        FlowLayoutPanel copyBar = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), WrapContents = false };
        copyType.Items.AddRange(["skill", "item"]);
        copyType.SelectedIndex = 0;
        copyBar.Controls.Add(useActionCopy);
        copyBar.Controls.Add(new Label { Text = "Type:", AutoSize = true, Padding = new Padding(14, 6, 3, 0) });
        copyBar.Controls.Add(copyType);
        copyBar.Controls.Add(new Label { Text = "ID:", AutoSize = true, Padding = new Padding(8, 6, 3, 0) });
        copyBar.Controls.Add(copyId);
        copyBar.Controls.Add(new Label { Text = "Action Copy overrides all custom sequence blocks at runtime.", AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(18, 6, 0, 0) });

        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>())
        {
            BindingList<ActionSequenceEntryRow> list = [];
            rows[phase] = list;
            DataGridView grid = CreateGrid(list);
            grids[phase] = grid;
            Panel host = new() { Dock = DockStyle.Fill };
            host.Controls.Add(grid);
            host.Controls.Add(CreatePhaseCommands(phase));
            phaseTabs.TabPages.Add(new TabPage(phase + " Action") { Tag = phase, Controls = { host } });
        }

        Panel paletteHost = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        paletteHost.Controls.Add(palette);
        paletteHost.Controls.Add(commandHelp);
        paletteHost.Controls.Add(category);
        paletteHost.Controls.Add(search);
        Button addCommand = new() { Text = "Add Selected Command", Dock = DockStyle.Bottom, Height = 34 };
        paletteHost.Controls.Add(addCommand);
        addCommand.Click += (_, _) => AddPaletteCommand();
        palette.DoubleClick += (_, _) => AddPaletteCommand();

        SplitContainer editingSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 790 };
        editingSplit.Panel1.Controls.Add(phaseTabs);
        editingSplit.Panel2.Controls.Add(paletteHost);

        Panel previewHost = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        previewHost.Controls.Add(preview);
        previewHost.Controls.Add(validation);
        TabControl mainTabs = new() { Dock = DockStyle.Fill };
        mainTabs.TabPages.Add(new TabPage("Visual Sequence") { Controls = { editingSplit } });
        mainTabs.TabPages.Add(new TabPage("Exact Notes Preview") { Controls = { previewHost } });

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button accept = new() { Text = "Use This Sequence", AutoSize = true };
        Button templates = new() { Text = "Apply Template…", AutoSize = true };
        Button clear = new() { Text = "Clear All Phases", AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        buttons.Controls.Add(new Label { Width = 20 });
        buttons.Controls.Add(templates);
        buttons.Controls.Add(clear);
        accept.Click += (_, _) => AcceptChanges();
        templates.Click += (_, _) => ApplyTemplate();
        clear.Click += (_, _) => ClearAll();

        Controls.Add(mainTabs);
        Controls.Add(copyBar);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = accept;

        search.TextChanged += (_, _) => RefreshPalette();
        category.SelectedIndexChanged += (_, _) => RefreshPalette();
        palette.SelectedIndexChanged += (_, _) => RefreshPaletteDetails();
        useActionCopy.CheckedChanged += (_, _) => { RefreshEnabledState(); RefreshPreview(); };
        copyType.SelectedIndexChanged += (_, _) => RefreshPreview();
        copyId.ValueChanged += (_, _) => RefreshPreview();
        phaseTabs.SelectedIndexChanged += (_, _) => RefreshPalette();
    }

    private static DataGridView CreateGrid(BindingList<ActionSequenceEntryRow> source)
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            DataSource = source,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ActionSequenceEntryRow.Index), HeaderText = "#", Width = 42, ReadOnly = true });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(ActionSequenceEntryRow.Enabled), HeaderText = "On", Width = 42 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ActionSequenceEntryRow.Command), HeaderText = "Command", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 310, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ActionSequenceEntryRow.Plugin), HeaderText = "Plugin", Width = 150, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ActionSequenceEntryRow.Kind), HeaderText = "Type", Width = 80, ReadOnly = true });
        return grid;
    }

    private Control CreatePhaseCommands(ActionSequencePhase phase)
    {
        FlowLayoutPanel panel = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(5), WrapContents = false };
        Button addRaw = new() { Text = "Add Raw…", AutoSize = true };
        Button edit = new() { Text = "Edit…", AutoSize = true };
        Button duplicate = new() { Text = "Duplicate", AutoSize = true };
        Button remove = new() { Text = "Remove", AutoSize = true };
        Button up = new() { Text = "Up", AutoSize = true };
        Button down = new() { Text = "Down", AutoSize = true };
        panel.Controls.AddRange([addRaw, edit, duplicate, remove, up, down]);
        addRaw.Click += (_, _) => AddRaw(phase);
        edit.Click += (_, _) => EditSelected(phase);
        duplicate.Click += (_, _) => DuplicateSelected(phase);
        remove.Click += (_, _) => RemoveSelected(phase);
        up.Click += (_, _) => MoveSelected(phase, -1);
        down.Click += (_, _) => MoveSelected(phase, 1);
        grids[phase].CellDoubleClick += (_, _) => EditSelected(phase);
        grids[phase].CellValueChanged += (_, _) => RefreshPreview();
        return panel;
    }

    private void LoadModel()
    {
        useActionCopy.Checked = !string.IsNullOrWhiteSpace(parsed.CopyType) && parsed.CopyId is > 0;
        if (!string.IsNullOrWhiteSpace(parsed.CopyType)) copyType.SelectedItem = parsed.CopyType;
        if (parsed.CopyId is > 0) copyId.Value = parsed.CopyId.Value;
        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>())
        {
            foreach (ActionSequenceEntry entry in parsed.Get(phase).Entries)
                rows[phase].Add(new(entry));
            Renumber(phase);
        }
        RefreshEnabledState();
    }

    private void RefreshPalette()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            string? previousCategory = category.SelectedItem as string;
            if (category.Items.Count == 0)
            {
                category.Items.Add("All Categories");
                foreach (string item in commands.Select(command => command.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) category.Items.Add(item);
                category.SelectedIndex = 0;
            }
            string query = search.Text.Trim();
            string selectedCategory = category.SelectedIndex <= 0 ? string.Empty : Convert.ToString(category.SelectedItem) ?? string.Empty;
            palette.Items.Clear();
            foreach (ActionSequenceCommandDescriptor command in commands)
            {
                if (selectedCategory.Length > 0 && !command.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase)) continue;
                if (query.Length > 0 && !Matches(command, query)) continue;
                palette.Items.Add(new ActionSequenceCommandItem(command, IsPluginAvailable(command.PluginId)));
            }
            if (palette.Items.Count > 0) palette.SelectedIndex = 0;
            _ = previousCategory;
        }
        finally { refreshing = false; }
    }

    private void RefreshPaletteDetails()
    {
        if (palette.SelectedItem is not ActionSequenceCommandItem item) { commandHelp.Text = string.Empty; return; }
        commandHelp.Text = $"{item.Descriptor.CanonicalLine}{Environment.NewLine}{item.Descriptor.PluginId} — {(item.Available ? "available" : "plugin missing or disabled")}{Environment.NewLine}{item.Descriptor.Description}";
    }

    private void AddPaletteCommand()
    {
        ActionSequencePhase phase = SelectedPhase();
        if (palette.SelectedItem is not ActionSequenceCommandItem item) return;
        using ActionSequenceCommandEditorDialog dialog = new(item.Descriptor.CanonicalLine, item.Descriptor.PluginId, item.Available);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        rows[phase].Add(new(ActionSequenceCodec.ParseEntry(dialog.CommandText) with { PluginId = item.Descriptor.PluginId }));
        Renumber(phase);
        SelectRow(phase, rows[phase].Count - 1);
        RefreshPreview();
    }

    private void AddRaw(ActionSequencePhase phase)
    {
        using ActionSequenceCommandEditorDialog dialog = new(string.Empty, null, true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        rows[phase].Add(new(ActionSequenceCodec.ParseEntry(dialog.CommandText)));
        Renumber(phase);
        SelectRow(phase, rows[phase].Count - 1);
        RefreshPreview();
    }

    private void EditSelected(ActionSequencePhase phase)
    {
        int index = SelectedIndex(phase);
        if (index < 0) return;
        ActionSequenceEntry source = rows[phase][index].ToEntry();
        using ActionSequenceCommandEditorDialog dialog = new(source.Text, source.PluginId, true);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        rows[phase][index] = new(ActionSequenceCodec.ParseEntry(dialog.CommandText) with { PluginId = source.PluginId, IsEnabled = source.IsEnabled });
        Renumber(phase);
        SelectRow(phase, index);
        RefreshPreview();
    }

    private void DuplicateSelected(ActionSequencePhase phase)
    {
        int index = SelectedIndex(phase);
        if (index < 0) return;
        rows[phase].Insert(index + 1, new(rows[phase][index].ToEntry()));
        Renumber(phase);
        SelectRow(phase, index + 1);
        RefreshPreview();
    }

    private void RemoveSelected(ActionSequencePhase phase)
    {
        int index = SelectedIndex(phase);
        if (index < 0) return;
        rows[phase].RemoveAt(index);
        Renumber(phase);
        SelectRow(phase, Math.Min(index, rows[phase].Count - 1));
        RefreshPreview();
    }

    private void MoveSelected(ActionSequencePhase phase, int direction)
    {
        int index = SelectedIndex(phase);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= rows[phase].Count) return;
        ActionSequenceEntryRow row = rows[phase][index];
        rows[phase].RemoveAt(index);
        rows[phase].Insert(target, row);
        Renumber(phase);
        SelectRow(phase, target);
        RefreshPreview();
    }

    private void ApplyTemplate()
    {
        using ActionSequenceTemplatePickerDialog dialog = new(ActionSequenceTemplates.BuiltIn, IsPluginAvailable);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (rows.Values.Any(list => list.Count > 0) && MessageBox.Show(this, "Replace all current phase commands with this template?", "Apply template", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>()) rows[phase].Clear();
        foreach ((ActionSequencePhase phase, IReadOnlyList<string> lines) in dialog.Template.Commands)
        {
            foreach (string line in lines) rows[phase].Add(new(ActionSequenceCodec.ParseEntry(line)));
            Renumber(phase);
        }
        useActionCopy.Checked = false;
        RefreshPreview();
    }

    private void ClearAll()
    {
        if (MessageBox.Show(this, "Remove every custom action-sequence phase?", "Clear sequences", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>()) rows[phase].Clear();
        useActionCopy.Checked = false;
        RefreshPreview();
    }

    private ActionSequenceModel BuildModel()
    {
        Dictionary<ActionSequencePhase, ActionSequencePhaseModel> phases = [];
        foreach (ActionSequencePhase phase in Enum.GetValues<ActionSequencePhase>())
            phases[phase] = new(phase, rows[phase].Select(row => row.ToEntry()).ToArray(), rows[phase].Count > 0);
        return new(phases,
            useActionCopy.Checked ? Convert.ToString(copyType.SelectedItem) : null,
            useActionCopy.Checked ? decimal.ToInt32(copyId.Value) : null);
    }

    private void RefreshPreview()
    {
        ActionSequenceModel model = BuildModel();
        preview.Text = ActionSequenceCodec.ReplaceSequencesInNotes(originalNotes, model, DetectLineEnding(originalNotes));
        IReadOnlyList<ActionSequenceValidationIssue> issues = ActionSequenceCodec.Validate(model);
        validation.ForeColor = issues.Any(issue => issue.Severity == "Error") ? Color.DarkRed : SystemColors.GrayText;
        validation.Text = issues.Count == 0
            ? $"Valid sequence. {rows.Values.Sum(list => list.Count)} command lines across five phases. Unknown/raw commands are preserved."
            : string.Join(Environment.NewLine, issues.Take(5).Select(issue => $"[{issue.Severity}] {issue.Phase}: {issue.Message}"));
    }

    private void AcceptChanges()
    {
        ActionSequenceModel model = BuildModel();
        IReadOnlyList<ActionSequenceValidationIssue> errors = ActionSequenceCodec.Validate(model).Where(issue => issue.Severity == "Error").ToArray();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(issue => $"{issue.Phase}: {issue.Message}")), "Sequence is not valid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ResultNotes = ActionSequenceCodec.ReplaceSequencesInNotes(originalNotes, model, DetectLineEnding(originalNotes));
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RefreshEnabledState()
    {
        phaseTabs.Enabled = !useActionCopy.Checked;
        palette.Enabled = !useActionCopy.Checked;
        search.Enabled = !useActionCopy.Checked;
        category.Enabled = !useActionCopy.Checked;
        copyType.Enabled = useActionCopy.Checked;
        copyId.Enabled = useActionCopy.Checked;
    }

    private bool IsPluginAvailable(string pluginId)
    {
        PluginFeatureDefinition? owner = catalog.Features.FirstOrDefault(feature => feature.PluginId.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
        if (owner is null) return true;
        return !availability.TryGetValue(owner.Key, out PluginFeatureAvailability? state) || state.CanAuthor;
    }

    private static bool Matches(ActionSequenceCommandDescriptor command, string query)
    {
        string haystack = string.Join(" ", command.DisplayName, command.CanonicalLine, command.PluginId, command.Category, string.Join(" ", command.SearchTerms));
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private ActionSequencePhase SelectedPhase() => phaseTabs.SelectedTab?.Tag is ActionSequencePhase phase ? phase : ActionSequencePhase.Setup;
    private int SelectedIndex(ActionSequencePhase phase) => grids[phase].SelectedRows.Count == 1 ? grids[phase].SelectedRows[0].Index : -1;
    private void SelectRow(ActionSequencePhase phase, int index)
    {
        grids[phase].ClearSelection();
        if (index >= 0 && index < grids[phase].Rows.Count) grids[phase].Rows[index].Selected = true;
    }
    private void Renumber(ActionSequencePhase phase) { for (int i = 0; i < rows[phase].Count; i++) rows[phase][i].Index = i + 1; grids[phase].Refresh(); }
    private static string DetectLineEnding(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : Environment.NewLine;

    private sealed record ActionSequenceCommandItem(ActionSequenceCommandDescriptor Descriptor, bool Available)
    {
        public string DisplayText => $"{Descriptor.DisplayName}  [{Descriptor.PluginId}]" + (Available ? string.Empty : "  ⚠");
    }

    private sealed class ActionSequenceEntryRow : INotifyPropertyChanged
    {
        private int index;
        private bool enabled;
        public ActionSequenceEntryRow(ActionSequenceEntry entry) { Source = entry; enabled = entry.IsEnabled; }
        public ActionSequenceEntry Source { get; }
        public int Index { get => index; set { index = value; PropertyChanged?.Invoke(this, new(nameof(Index))); } }
        public bool Enabled { get => enabled; set { enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); } }
        public string Command => Source.Text.Trim();
        public string Plugin => Source.PluginId ?? string.Empty;
        public string Kind => Source.Kind.ToString();
        public ActionSequenceEntry ToEntry() => Source with { IsEnabled = Enabled };
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

internal sealed class ActionSequenceCommandEditorDialog : Form
{
    private readonly TextBox text = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 10f) };
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(8), ForeColor = SystemColors.GrayText };

    public ActionSequenceCommandEditorDialog(string initial, string? pluginId, bool available)
    {
        Text = string.IsNullOrWhiteSpace(initial) ? "Add Raw Action Command" : "Edit Action Command";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(700, 300);
        MinimizeBox = false;
        MaximizeBox = false;
        text.Text = initial;
        status.Text = pluginId is null ? "Raw command. It will be preserved exactly." : $"Provided by {pluginId}. " + (available ? "The plugin is available." : "The plugin appears missing or disabled; the command can still be stored with a warning.");
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button accept = new() { Text = "Use Command", AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        accept.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(text.Text) || text.Text.Contains('\n') || text.Text.Contains('\r'))
            {
                MessageBox.Show(this, "Enter one action-sequence command line.", "Command required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(text);
        Controls.Add(status);
        Controls.Add(buttons);
        AcceptButton = accept;
        CancelButton = cancel;
    }

    public string CommandText => text.Text.Trim();
}

internal sealed class ActionSequenceTemplatePickerDialog : Form
{
    private readonly ListBox templates = new() { Dock = DockStyle.Fill, DisplayMember = nameof(TemplateItem.DisplayText) };
    private readonly TextBox description = new() { Dock = DockStyle.Bottom, Height = 120, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly Button accept = new() { Text = "Use Template", DialogResult = DialogResult.OK, AutoSize = true };

    public ActionSequenceTemplatePickerDialog(IReadOnlyList<ActionSequenceTemplate> items, Func<string, bool> isPluginAvailable)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isPluginAvailable);
        Text = "Action Sequence Templates";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(650, 500);
        foreach (ActionSequenceTemplate item in items)
        {
            string[] missing = (item.RequiredPlugins ?? []).Where(plugin => !isPluginAvailable(plugin)).ToArray();
            templates.Items.Add(new TemplateItem(item, missing));
        }
        templates.SelectedIndexChanged += (_, _) => RefreshDescription();
        if (templates.Items.Count > 0) templates.SelectedIndex = 0;
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        Controls.Add(templates);
        Controls.Add(description);
        Controls.Add(buttons);
        AcceptButton = accept;
        CancelButton = cancel;
    }

    public ActionSequenceTemplate Template => ((TemplateItem)templates.SelectedItem!).Template;

    private void RefreshDescription()
    {
        if (templates.SelectedItem is not TemplateItem item)
        {
            description.Text = string.Empty;
            accept.Enabled = false;
            return;
        }
        string requirements = string.Join(", ", item.Template.RequiredPlugins ?? []);
        string missing = item.MissingPlugins.Count == 0
            ? "All required plugins are available."
            : "Missing or disabled: " + string.Join(", ", item.MissingPlugins);
        description.Text = item.Template.Description + Environment.NewLine + "Requires: " + requirements + Environment.NewLine + missing;
        accept.Enabled = item.MissingPlugins.Count == 0;
    }

    private sealed record TemplateItem(ActionSequenceTemplate Template, IReadOnlyList<string> MissingPlugins)
    {
        public string DisplayText => Template.DisplayName + (MissingPlugins.Count == 0 ? string.Empty : "  ⚠ unavailable");
    }
}
