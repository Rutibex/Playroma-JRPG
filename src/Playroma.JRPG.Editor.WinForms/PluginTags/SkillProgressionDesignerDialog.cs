using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class SkillProgressionDesignerDialog : Form
{
    private readonly MvProjectSession projectSession;
    private readonly PluginTagCatalog catalog;
    private readonly PluginTextEditingSession session;
    private readonly SkillProgressionWorkspace workspace;
    private readonly IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability;
    private readonly bool progressionMode;
    private bool refreshing;

    private readonly TextBox search = new() { Width = 230, PlaceholderText = "Search skills and progression…" };
    private readonly CheckBox currentOnly = new() { Text = "Current only", AutoSize = true };
    private readonly CheckBox includeAdvanced = new() { Text = "Advanced", AutoSize = true };
    private readonly CheckBox installedOnly = new() { Text = "Installed only", AutoSize = true, Checked = true };
    private readonly TreeView areas = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly DataGridView features = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        AutoGenerateColumns = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false
    };
    private readonly Label title = new() { Dock = DockStyle.Top, Height = 30, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(8, 7, 0, 0) };
    private readonly TextBox description = new() { Dock = DockStyle.Top, Height = 92, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly TextBox syntax = CodeBox();
    private readonly TextBox diagnostics = CodeBox();
    private readonly Button add = new() { Text = "Add", AutoSize = true };
    private readonly Button edit = new() { Text = "Edit", AutoSize = true };
    private readonly Button remove = new() { Text = "Remove", AutoSize = true };
    private readonly Button presets = new() { Text = "Quick Setups…", AutoSize = true };
    private readonly Button accept = new() { Text = "Use These Notes", DialogResult = DialogResult.OK, AutoSize = true };

    public SkillProgressionDesignerDialog(
        MvProjectSession projectSession,
        PluginTagCatalog catalog,
        PluginTagContext context,
        string currentNotes,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability,
        bool progressionMode)
    {
        this.projectSession = projectSession ?? throw new ArgumentNullException(nameof(projectSession));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.availability = availability ?? new Dictionary<PluginFeatureKey, PluginFeatureAvailability>();
        this.progressionMode = progressionMode;
        session = new PluginTextEditingSession(catalog, context, currentNotes);
        workspace = new SkillProgressionWorkspace(catalog, session, YanflySkillProgressionFeatureDefinitions.Classify, this.availability.Values);

        Text = progressionMode ? "Yanfly Progression Designer" : "Yanfly Skill Designer";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1180;
        Height = 780;
        MinimumSize = new Size(900, 620);
        ShowIcon = false;
        AccessibleName = Text;
        BuildLayout();
        WireEvents();
        RefreshAll();
    }

    public string ResultNotes => session.WorkingText;

    private void BuildLayout()
    {
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top,
            Height = 40,
            WrapContents = false,
            Padding = new Padding(6)
        };
        toolbar.Controls.Add(new Label { Text = "Find:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        toolbar.Controls.Add(search);
        toolbar.Controls.Add(currentOnly);
        toolbar.Controls.Add(includeAdvanced);
        toolbar.Controls.Add(installedOnly);
        toolbar.Controls.Add(new Label
        {
            Text = progressionMode
                ? "Learning • JP • Classes • Subclasses • Stats • TP • Equipped Skills"
                : "Costs • Requirements • Execution • Cooldowns • Uses • Mastery • Rewards",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(12, 6, 0, 0)
        });

        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Used", HeaderText = "Used", Width = 48 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Feature", HeaderText = "Feature", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Plugin", HeaderText = "Plugin", Width = 160 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Editor", Width = 76 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 92 });

        SplitContainer browser = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 190 };
        browser.Panel1.Controls.Add(areas);
        browser.Panel2.Controls.Add(features);

        TabControl details = new() { Dock = DockStyle.Fill };
        Panel detailsPage = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        GroupBox syntaxGroup = new() { Text = "Current / canonical syntax", Dock = DockStyle.Fill, Padding = new Padding(8) };
        syntaxGroup.Controls.Add(syntax);
        detailsPage.Controls.Add(syntaxGroup);
        detailsPage.Controls.Add(description);
        detailsPage.Controls.Add(title);
        details.TabPages.Add(new TabPage("Details") { Controls = { detailsPage } });
        details.TabPages.Add(new TabPage("Diagnostics") { Controls = { diagnostics } });

        SplitContainer main = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 720 };
        main.Panel1.Controls.Add(browser);
        main.Panel2.Controls.Add(details);

        FlowLayoutPanel commands = new()
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            Padding = new Padding(7),
            WrapContents = false
        };
        commands.Controls.Add(add);
        commands.Controls.Add(edit);
        commands.Controls.Add(remove);
        commands.Controls.Add(presets);
        commands.Controls.Add(new Label { Width = 28 });
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        commands.Controls.Add(accept);
        commands.Controls.Add(cancel);

        Controls.Add(main);
        Controls.Add(commands);
        Controls.Add(toolbar);
        AcceptButton = accept;
        CancelButton = cancel;
    }

    private void WireEvents()
    {
        search.TextChanged += (_, _) => RefreshFeatures();
        currentOnly.CheckedChanged += (_, _) => RefreshFeatures();
        includeAdvanced.CheckedChanged += (_, _) => RefreshFeatures();
        installedOnly.CheckedChanged += (_, _) => RefreshFeatures();
        areas.AfterSelect += (_, _) => RefreshFeatures(preserveArea: true);
        features.SelectionChanged += (_, _) => RefreshDetails();
        features.CellDoubleClick += (_, _) => EditSelected();
        add.Click += (_, _) => AddSelected();
        edit.Click += (_, _) => EditSelected();
        remove.Click += (_, _) => RemoveSelected();
        presets.Click += (_, _) => ShowPresets();
        session.ParsedDocumentChanged += (_, _) => RefreshAll();
    }

    private void RefreshAll()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            string? selected = areas.SelectedNode?.Name;
            areas.BeginUpdate();
            areas.Nodes.Clear();
            TreeNode all = new("All") { Name = "all", Tag = null };
            areas.Nodes.Add(all);
            foreach (SkillProgressionArea area in workspace.Areas.Where(IsModeArea))
            {
                TreeNode node = new(YanflySkillProgressionFeatureDefinitions.FriendlyArea(area))
                {
                    Name = area.ToString(),
                    Tag = area
                };
                areas.Nodes.Add(node);
            }
            areas.EndUpdate();
            TreeNode? restore = selected is null ? null : areas.Nodes.Cast<TreeNode>().FirstOrDefault(node => node.Name == selected);
            areas.SelectedNode = restore ?? all;
            RefreshFeatures(preserveArea: true);
            RefreshDiagnostics();
        }
        finally { refreshing = false; }
    }

    private void RefreshFeatures(bool preserveArea = false)
    {
        if (refreshing && !preserveArea) return;
        PluginFeatureKey? selected = Selected()?.Definition.Key;
        SkillProgressionArea? area = areas.SelectedNode?.Tag is SkillProgressionArea selectedArea ? selectedArea : null;
        IReadOnlyList<SkillProgressionFeatureItem> items = workspace.Query(new(
            search.Text,
            area,
            currentOnly.Checked,
            includeAdvanced.Checked,
            installedOnly.Checked))
            .Where(item => IsModeFeature(item.Definition))
            .ToArray();

        features.Rows.Clear();
        foreach (SkillProgressionFeatureItem item in items)
        {
            int rowIndex = features.Rows.Add(item.IsPresent ? item.OccurrenceCount.ToString() : string.Empty,
                item.Definition.DisplayName,
                item.Definition.PluginId,
                item.Definition.Complexity == PluginFeatureComplexity.AdvancedCode ? "Code" : "Typed",
                Status(item.Availability));
            features.Rows[rowIndex].Tag = item;
            if (item.IsPresent) features.Rows[rowIndex].DefaultCellStyle.Font = new Font(features.Font, FontStyle.Bold);
            if (!item.CanAuthor) features.Rows[rowIndex].DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
        DataGridViewRow? restore = selected is null ? null : features.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(row => row.Tag is SkillProgressionFeatureItem item && item.Definition.Key == selected.Value);
        if (restore is not null) restore.Selected = true;
        else if (features.Rows.Count > 0) features.Rows[0].Selected = true;
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        SkillProgressionFeatureItem? item = Selected();
        if (item is null)
        {
            title.Text = "No feature selected";
            description.Text = string.Empty;
            syntax.Text = string.Empty;
            add.Enabled = edit.Enabled = remove.Enabled = false;
            return;
        }
        title.Text = item.Definition.DisplayName;
        description.Text = $"{YanflySkillProgressionFeatureDefinitions.FriendlyArea(item.Area)} • {item.Definition.PluginId}\r\n" +
            item.Definition.Summary + "\r\n" + item.Availability.Message;
        IReadOnlyList<PluginTextNode> nodes = session.ParsedDocument.Find(item.Definition.Key);
        syntax.Text = nodes.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, nodes.Select(node => node.RawText.TrimEnd('\r', '\n')))
            : item.Definition.Syntax.CanonicalTemplate +
              (item.Definition.Syntax.ClosingTemplate is null ? string.Empty : Environment.NewLine + "…" + Environment.NewLine + item.Definition.Syntax.ClosingTemplate);
        add.Enabled = item.CanAuthor && (item.Definition.Multiplicity != PluginFeatureMultiplicity.Singleton || !item.IsPresent);
        edit.Enabled = item.CanAuthor && item.IsPresent;
        remove.Enabled = item.IsPresent;
    }

    private void RefreshDiagnostics()
    {
        IReadOnlyList<PluginTagDiagnostic> issues = workspace.Validate();
        diagnostics.Text = issues.Count == 0
            ? "No skill or progression diagnostics for this record."
            : string.Join(Environment.NewLine, issues.Select(issue => $"[{issue.Severity}] {issue.Code}: {issue.Message}"));
    }

    private void AddSelected()
    {
        SkillProgressionFeatureItem? item = Selected();
        if (item is null || !item.CanAuthor) return;
        EditFeature(item, isEditing: false);
    }

    private void EditSelected()
    {
        SkillProgressionFeatureItem? item = Selected();
        if (item is null || !item.CanAuthor || !item.IsPresent) return;
        EditFeature(item, isEditing: true);
    }

    private void EditFeature(SkillProgressionFeatureItem item, bool isEditing)
    {
        PluginFeatureValues? existing = null;
        if (isEditing)
        {
            PluginTextNode node = session.ParsedDocument.Find(item.Definition.Key)[0];
            existing = node switch
            {
                RecognizedTagNode tag => tag.Values,
                RecognizedBlockNode block => block.Values,
                _ => PluginFeatureValues.Empty
            };
        }
        using PluginFeatureEditorDialog dialog = new(projectSession, item.Definition, existing, isEditing);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            PluginTagWriteRequest request = new(item.Definition.Key, dialog.Values, dialog.WriteMode, dialog.ConflictResolution);
            PluginTagChangePreview change = workspace.PreviewWrite(request);
            using PluginTagChangePreviewDialog preview = new(isEditing ? "Edit Skill / Progression Tag" : "Add Skill / Progression Tag", change);
            if (preview.ShowDialog(this) == DialogResult.OK) session.ReplaceText(change.After);
        }
        catch (Exception exception) when (exception is PluginTextMutationException or PluginTagRenderException)
        {
            MessageBox.Show(this, exception.Message, "Feature could not be changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RemoveSelected()
    {
        SkillProgressionFeatureItem? item = Selected();
        if (item is null || !item.IsPresent) return;
        bool all = item.OccurrenceCount == 1;
        if (item.OccurrenceCount > 1)
        {
            DialogResult answer = MessageBox.Show(this,
                $"Remove all {item.OccurrenceCount} occurrences? Choose No to remove only the first.",
                "Remove Feature", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            all = answer == DialogResult.Yes;
        }
        PluginTagChangePreview change = workspace.PreviewRemove(item.Definition.Key, 0, all);
        using PluginTagChangePreviewDialog preview = new("Remove Skill / Progression Tag", change);
        if (preview.ShowDialog(this) == DialogResult.OK) session.ReplaceText(change.After);
    }

    private void ShowPresets()
    {
        IReadOnlyList<SkillProgressionPreset> availablePresets = YanflySkillProgressionFeatureDefinitions
            .CreatePresets(catalog, session.Context.DocumentKind)
            .Where(preset => preset.Steps.Any(step =>
                catalog.GetRequiredFeature(step.Feature).SpecializedEditorId?.Equals(
                    progressionMode ? YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId : YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId,
                    StringComparison.OrdinalIgnoreCase) == true))
            .ToArray();
        if (availablePresets.Count == 0)
        {
            MessageBox.Show(this, "No quick setups are applicable to this record type.", "Quick Setups", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using Form picker = new()
        {
            Text = "Skill & Progression Quick Setups",
            StartPosition = FormStartPosition.CenterParent,
            Width = 620,
            Height = 420,
            ShowIcon = false
        };
        ListBox list = new() { Dock = DockStyle.Left, Width = 245, DisplayMember = nameof(SkillProgressionPreset.DisplayName) };
        list.Items.AddRange(availablePresets.Cast<object>().ToArray());
        TextBox info = CodeBox();
        Button use = new() { Text = "Preview Setup", Dock = DockStyle.Bottom, Height = 38 };
        picker.Controls.Add(info);
        picker.Controls.Add(list);
        picker.Controls.Add(use);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedItem is SkillProgressionPreset preset)
                info.Text = preset.DisplayName + Environment.NewLine + Environment.NewLine + preset.Description + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, preset.Steps.Select(step => "• " + catalog.GetRequiredFeature(step.Feature).DisplayName));
        };
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        use.Click += (_, _) => picker.DialogResult = DialogResult.OK;
        if (picker.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not SkillProgressionPreset selected) return;

        SkillProgressionPresetPreview presetPreview = workspace.PreviewPreset(selected);
        if (presetPreview.Warnings.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, presetPreview.Warnings), "Setup warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        PluginTagChangePreview change = new(presetPreview.Before, presetPreview.After, string.Empty, presetPreview.Changes);
        using PluginTagChangePreviewDialog preview = new("Apply " + selected.DisplayName, change);
        if (preview.ShowDialog(this) == DialogResult.OK) session.ReplaceText(presetPreview.After);
    }

    private bool IsModeFeature(PluginFeatureDefinition feature) => progressionMode
        ? feature.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true
        : feature.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true;

    private bool IsModeArea(SkillProgressionArea area)
    {
        HashSet<SkillProgressionArea> progression =
        [SkillProgressionArea.Learning, SkillProgressionArea.JobPoints, SkillProgressionArea.Classes,
         SkillProgressionArea.Subclasses, SkillProgressionArea.StatAllocation, SkillProgressionArea.TpModes,
         SkillProgressionArea.EquippedSkills, SkillProgressionArea.LevelGrowth, SkillProgressionArea.Advanced];
        return progressionMode ? progression.Contains(area) : !progression.Contains(area) || area == SkillProgressionArea.Advanced;
    }

    private SkillProgressionFeatureItem? Selected() => features.SelectedRows.Count == 1
        ? features.SelectedRows[0].Tag as SkillProgressionFeatureItem
        : null;

    private static string Status(PluginFeatureAvailability availability) => availability.State switch
    {
        PluginFeatureAvailabilityState.Available => "Ready",
        PluginFeatureAvailabilityState.SourceUnverified => "Unverified",
        PluginFeatureAvailabilityState.VersionUnverified => "Version?",
        PluginFeatureAvailabilityState.OwnerPluginMissing => "Missing",
        PluginFeatureAvailabilityState.OwnerPluginDisabled => "Disabled",
        PluginFeatureAvailabilityState.LoadOrderInvalid => "Order",
        PluginFeatureAvailabilityState.PluginConflict => "Conflict",
        _ => "Blocked"
    };

    private static TextBox CodeBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
}
