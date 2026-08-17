using System.Globalization;
using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

public sealed class PluginTagsEditorControl : UserControl
{
    private readonly PluginTagCatalog catalog;
    private readonly MvProjectSession projectSession;
    private readonly Font presentFeatureFont = new(SystemFonts.DefaultFont, FontStyle.Bold);
    private PluginTextEditingSession? session;
    private PluginTagAuthoringWorkspace? workspace;
    private InstalledPluginAnalysis? analysis;
    private IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability> availability =
        new Dictionary<PluginFeatureKey, PluginFeatureAvailability>();
    private bool refreshing;

    private readonly TextBox search = new() { Width = 220, PlaceholderText = "Search plugin features…" };
    private readonly ComboBox scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox advanced = new() { Text = "Advanced", AutoSize = true };
    private readonly Label pluginStatus = new() { AutoSize = true, Padding = new Padding(8, 6, 4, 0) };
    private readonly TreeView categories = new() { Dock = DockStyle.Fill, HideSelection = false };
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
    private readonly TextBox summary = new() { Dock = DockStyle.Top, Height = 76, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None };
    private readonly TextBox exactSyntax = ReadOnlyCodeBox();
    private readonly TextBox diagnostics = ReadOnlyCodeBox();
    private readonly Label bindingMessage = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText };
    private readonly Button add = new() { Text = "Add", AutoSize = true };
    private readonly Button edit = new() { Text = "Edit", AutoSize = true };
    private readonly Button remove = new() { Text = "Remove", AutoSize = true };
    private readonly Button specialEditor = new() { Text = "Open Special Editor…", AutoSize = true };
    private readonly Button recipes = new() { Text = "Recipes…", AutoSize = true };
    private readonly Button apply = new() { Text = "Apply Notes", AutoSize = true };
    private readonly Button revert = new() { Text = "Revert", AutoSize = true };
    private readonly Button openNotes = new() { Text = "Open Notes", AutoSize = true };
    private readonly Panel content = new() { Dock = DockStyle.Fill };

    public PluginTagsEditorControl(PluginTagCatalog catalog, MvProjectSession projectSession)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.projectSession = projectSession ?? throw new ArgumentNullException(nameof(projectSession));
        Dock = DockStyle.Fill;
        AccessibleName = "Plugin Tags editor";
        scope.Items.AddRange(["Installed", "All", "Current"]);
        scope.SelectedIndex = 0;
        BuildLayout();
        WireEvents();
        ClearBinding("Select one supported database record to edit its plugin tags.");
    }

    public event EventHandler? ApplyRequested;
    public event EventHandler? OpenNotesRequested;
    public event EventHandler? PendingChangesChanged;

    public bool HasPendingChanges => session?.IsDirty == true;
    public PluginTextEditingSession? Session => session;

    public void ConfigureHost(string applyCaption, string rawCaption, bool showRecipes = true)
    {
        apply.Text = string.IsNullOrWhiteSpace(applyCaption) ? "Apply" : applyCaption;
        openNotes.Text = string.IsNullOrWhiteSpace(rawCaption) ? "Open Raw" : rawCaption;
        recipes.Visible = showRecipes;
    }

    public void Bind(
        PluginTextEditingSession editingSession,
        InstalledPluginAnalysis? installedAnalysis,
        IReadOnlyDictionary<PluginFeatureKey, PluginFeatureAvailability>? featureAvailability,
        string? intelligenceMessage = null)
    {
        ArgumentNullException.ThrowIfNull(editingSession);
        UnsubscribeSession();
        session = editingSession;
        analysis = installedAnalysis;
        availability = featureAvailability ?? new Dictionary<PluginFeatureKey, PluginFeatureAvailability>();
        workspace = new PluginTagAuthoringWorkspace(catalog, editingSession, availability.Values);
        session.ParsedDocumentChanged += SessionChanged;
        session.DirtyStateChanged += SessionDirtyChanged;
        bindingMessage.Visible = false;
        content.Visible = true;
        pluginStatus.Text = intelligenceMessage ?? BuildPluginStatusText(installedAnalysis);
        RefreshAll();
    }

    internal void SetProjectIntelligence(PluginTagProjectIntelligence intelligence)
    {
        ArgumentNullException.ThrowIfNull(intelligence);
        analysis = intelligence.Analysis;
        availability = intelligence.Availability;
        workspace?.SetAvailability(availability.Values);
        pluginStatus.Text = intelligence.ErrorMessage ?? BuildPluginStatusText(analysis);
        RefreshAll();
    }

    public void ClearBinding(string message)
    {
        UnsubscribeSession();
        session = null;
        workspace = null;
        analysis = null;
        features.Rows.Clear();
        categories.Nodes.Clear();
        title.Text = string.Empty;
        summary.Text = string.Empty;
        exactSyntax.Text = string.Empty;
        diagnostics.Text = string.Empty;
        bindingMessage.Text = message;
        bindingMessage.Visible = true;
        content.Visible = false;
    }

    public void RevertWorkingText() => session?.Revert();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeSession();
            presentFeatureFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top,
            Height = 38,
            WrapContents = false,
            Padding = new Padding(5)
        };
        toolbar.Controls.Add(new Label { Text = "Find:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        toolbar.Controls.Add(search);
        toolbar.Controls.Add(new Label { Text = "Show:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) });
        toolbar.Controls.Add(scope);
        toolbar.Controls.Add(advanced);
        toolbar.Controls.Add(pluginStatus);

        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Present", HeaderText = "Used", Width = 46 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Feature", HeaderText = "Feature", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Plugin", HeaderText = "Plugin", Width = 150 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Editor", HeaderText = "Editor", Width = 82 });
        features.Columns.Add(new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", Width = 95 });

        SplitContainer leftSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 180 };
        leftSplit.Panel1.Controls.Add(categories);
        leftSplit.Panel2.Controls.Add(features);

        TabControl detailTabs = new() { Dock = DockStyle.Fill };
        Panel detailsPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        GroupBox syntaxGroup = new() { Text = "Canonical / current syntax", Dock = DockStyle.Fill, Padding = new Padding(8) };
        syntaxGroup.Controls.Add(exactSyntax);
        detailsPanel.Controls.Add(syntaxGroup);
        detailsPanel.Controls.Add(summary);
        detailsPanel.Controls.Add(title);
        detailTabs.TabPages.Add(new TabPage("Details") { Controls = { detailsPanel } });
        detailTabs.TabPages.Add(new TabPage("Diagnostics") { Controls = { diagnostics } });

        SplitContainer mainSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 610 };
        mainSplit.Panel1.Controls.Add(leftSplit);
        mainSplit.Panel2.Controls.Add(detailTabs);

        FlowLayoutPanel commands = new()
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            Padding = new Padding(6),
            WrapContents = false
        };
        commands.Controls.Add(add);
        commands.Controls.Add(edit);
        commands.Controls.Add(remove);
        commands.Controls.Add(specialEditor);
        commands.Controls.Add(recipes);
        commands.Controls.Add(new Label { Width = 16 });
        commands.Controls.Add(apply);
        commands.Controls.Add(revert);
        commands.Controls.Add(openNotes);

        content.Controls.Add(mainSplit);
        content.Controls.Add(commands);
        content.Controls.Add(toolbar);
        Controls.Add(content);
        Controls.Add(bindingMessage);
    }

    private void WireEvents()
    {
        search.TextChanged += (_, _) => RefreshBrowser();
        scope.SelectedIndexChanged += (_, _) => RefreshBrowser();
        advanced.CheckedChanged += (_, _) => RefreshBrowser();
        categories.AfterSelect += (_, _) => RefreshBrowser(preserveCategories: true);
        features.SelectionChanged += (_, _) => RefreshSelectionDetails();
        features.CellDoubleClick += (_, _) => EditSelected();
        add.Click += (_, _) => AddSelected();
        edit.Click += (_, _) => EditSelected();
        remove.Click += (_, _) => RemoveSelected();
        specialEditor.Click += (_, _) => EditSpecializedSelected();
        recipes.Click += (_, _) => ShowRecipes();
        apply.Click += (_, _) => ApplyRequested?.Invoke(this, EventArgs.Empty);
        revert.Click += (_, _) => session?.Revert();
        openNotes.Click += (_, _) => OpenNotesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SessionChanged(object? sender, EventArgs e) => RefreshAll();
    private void SessionDirtyChanged(object? sender, EventArgs e)
    {
        RefreshCommandState();
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshAll()
    {
        if (session is null || workspace is null) return;
        RefreshBrowser();
        RefreshDiagnostics();
        RefreshCommandState();
    }

    private void RefreshBrowser(bool preserveCategories = false)
    {
        if (refreshing || session is null || workspace is null) return;
        refreshing = true;
        try
        {
            string? selectedCategory = categories.SelectedNode?.Tag as string;
            PluginFeatureKey? selectedKey = SelectedItem()?.Definition.Key;
            PluginFeatureBrowserQuery baseQuery = BuildQuery(category: null);
            if (!preserveCategories)
            {
                string[] categoryNames = workspace.Categories(baseQuery).ToArray();
                categories.BeginUpdate();
                categories.Nodes.Clear();
                TreeNode all = new("All categories") { Tag = null };
                categories.Nodes.Add(all);
                foreach (string category in categoryNames) categories.Nodes.Add(new TreeNode(category) { Tag = category });
                TreeNode? restore = categories.Nodes.Cast<TreeNode>().FirstOrDefault(node =>
                    string.Equals(node.Tag as string, selectedCategory, StringComparison.OrdinalIgnoreCase));
                categories.SelectedNode = restore ?? all;
                categories.EndUpdate();
            }

            string? categoryFilter = categories.SelectedNode?.Tag as string;
            IReadOnlyList<PluginFeatureBrowserItem> items = workspace.Query(BuildQuery(categoryFilter));
            features.Rows.Clear();
            foreach (PluginFeatureBrowserItem item in items)
            {
                int rowIndex = features.Rows.Add(
                    item.IsPresent ? item.OccurrenceCount.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    item.Definition.DisplayName,
                    item.Definition.PluginId,
                    PluginSpecializedEditorRouter.EditorLabel(item.Definition),
                    StatusText(item.Availability));
                DataGridViewRow row = features.Rows[rowIndex];
                row.Tag = item;
                if (!item.CanAuthor) row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                else if (item.IsPresent) row.DefaultCellStyle.Font = presentFeatureFont;
            }

            DataGridViewRow? selected = selectedKey.HasValue
                ? features.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => row.Tag is PluginFeatureBrowserItem item && item.Definition.Key == selectedKey.Value)
                : null;
            if (selected is not null) selected.Selected = true;
            else if (features.Rows.Count > 0) features.Rows[0].Selected = true;
            RefreshSelectionDetails();
        }
        finally { refreshing = false; }
    }

    private void RefreshSelectionDetails()
    {
        PluginFeatureBrowserItem? item = SelectedItem();
        if (item is null)
        {
            title.Text = "No feature selected";
            summary.Text = string.Empty;
            exactSyntax.Text = string.Empty;
            RefreshCommandState();
            return;
        }

        title.Text = item.Definition.DisplayName;
        summary.Text = item.Definition.Summary + Environment.NewLine +
            item.Availability.Message + Environment.NewLine + item.EditorPlan.CoverageMessage +
            (workspace!.FindConflicts(item.Definition.Key).Count == 0
                ? string.Empty
                : Environment.NewLine + "Conflicts with current tags: " + string.Join(", ", workspace.FindConflicts(item.Definition.Key).Select(feature => feature.DisplayName)));
        IReadOnlyList<PluginTextNode> nodes = session!.ParsedDocument.Find(item.Definition.Key);
        exactSyntax.Text = nodes.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, nodes.Select(node => node.RawText.TrimEnd('\r', '\n')))
            : item.Definition.Syntax.CanonicalTemplate +
              (item.Definition.Syntax.ClosingTemplate is null ? string.Empty : Environment.NewLine + "…" + Environment.NewLine + item.Definition.Syntax.ClosingTemplate);
        RefreshCommandState();
    }

    private void RefreshDiagnostics()
    {
        if (session is null) { diagnostics.Text = string.Empty; return; }
        IReadOnlyList<PluginTagDiagnostic> items = session.Validate(analysis?.Statuses);
        if (items.Count == 0)
        {
            diagnostics.Text = "No Plugin Tags diagnostics for this record.";
            return;
        }
        diagnostics.Text = string.Join(Environment.NewLine, items.Select(item =>
            $"[{item.Severity}] {item.Code}: {item.Message}"));
    }

    private void RefreshCommandState()
    {
        PluginFeatureBrowserItem? item = SelectedItem();
        add.Enabled = item is not null && item.CanAuthor &&
            (item.Definition.Multiplicity != PluginFeatureMultiplicity.Singleton || !item.IsPresent);
        edit.Enabled = item is not null && item.CanAuthor && item.IsPresent;
        remove.Enabled = item is not null && item.IsPresent;
        specialEditor.Enabled = item is not null && item.CanAuthor && PluginSpecializedEditorRouter.CanHandle(item.Definition);
        specialEditor.Text = item is not null && item.Definition.SpecializedEditorId?.Contains("battle-ai", StringComparison.OrdinalIgnoreCase) == true ? "Open Battle AI…" :
            item is not null && item.Definition.SpecializedEditorId?.Contains("action-sequence", StringComparison.OrdinalIgnoreCase) == true ? "Open Sequence…" :
            item is not null && item.Definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Open Skill Designer…" :
            item is not null && item.Definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Open Progression…" :
            item is not null && item.Definition.SpecializedEditorId?.Equals(YanflyItemCommerceFeatureDefinitions.DesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Open Items & Shops…" :
            item is not null && item.Definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Open Enemy Designer…" :
            item is not null && item.Definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Open State Lifecycle…" :
            item is not null && item.Definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Open Passive States & Auras…" :
            item is not null && item.Definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.FormulaLabEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Open Formula Lab…" : "Open Special Editor…";
        apply.Enabled = session?.IsDirty == true;
        revert.Enabled = session?.IsDirty == true;
        recipes.Enabled = session is not null;
    }

    private void AddSelected()
    {
        PluginFeatureBrowserItem? item = SelectedItem();
        if (item is null || workspace is null || session is null || !item.CanAuthor) return;
        if (TryEditSpecialized(item)) return;
        using PluginFeatureEditorDialog editor = new(projectSession, item.Definition);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            PluginTagWriteRequest request = new(item.Definition.Key, editor.Values, editor.WriteMode, editor.ConflictResolution);
            PluginTagChangePreview change = workspace.PreviewWrite(request);
            using PluginTagChangePreviewDialog previewDialog = new("Add Plugin Tag", change);
            if (previewDialog.ShowDialog(this) != DialogResult.OK) return;
            session.ReplaceText(change.After);
        }
        catch (Exception exception) when (exception is PluginTextMutationException or PluginTagRenderException)
        {
            MessageBox.Show(this, exception.Message, "Plugin tag could not be added", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EditSelected()
    {
        PluginFeatureBrowserItem? item = SelectedItem();
        if (item is null || workspace is null || session is null || !item.IsPresent || !item.CanAuthor) return;
        if (TryEditSpecialized(item)) return;
        PluginTextNode node = session.ParsedDocument.Find(item.Definition.Key)[0];
        PluginFeatureValues values = node switch
        {
            RecognizedTagNode tag => tag.Values,
            RecognizedBlockNode block => block.Values,
            _ => PluginFeatureValues.Empty
        };
        using PluginFeatureEditorDialog editor = new(projectSession, item.Definition, values, isEditing: true);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            PluginTagWriteRequest request = new(item.Definition.Key, editor.Values, editor.WriteMode, editor.ConflictResolution);
            PluginTagChangePreview change = workspace.PreviewWrite(request);
            using PluginTagChangePreviewDialog previewDialog = new("Edit Plugin Tag", change);
            if (previewDialog.ShowDialog(this) != DialogResult.OK) return;
            session.ReplaceText(change.After);
        }
        catch (Exception exception) when (exception is PluginTextMutationException or PluginTagRenderException)
        {
            MessageBox.Show(this, exception.Message, "Plugin tag could not be edited", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void EditSpecializedSelected()
    {
        PluginFeatureBrowserItem? item = SelectedItem();
        if (item is null) return;
        _ = TryEditSpecialized(item);
    }

    private bool TryEditSpecialized(PluginFeatureBrowserItem item)
    {
        if (session is null || !PluginSpecializedEditorRouter.CanHandle(item.Definition)) return false;
        string before = session.WorkingText;
        bool handled = PluginSpecializedEditorRouter.TryEdit(this, projectSession, catalog, item.Definition, session.Context, before, availability, out string after);
        if (!handled || string.Equals(before, after, StringComparison.Ordinal)) return handled;
        PluginTagChangePreview change = new(before, after, string.Empty, PluginTagTextDiff.Create(before, after));
        string previewTitle = item.Definition.SpecializedEditorId?.Contains("battle-ai", StringComparison.OrdinalIgnoreCase) == true ? "Apply Battle AI" :
            item.Definition.SpecializedEditorId?.Contains("action-sequence", StringComparison.OrdinalIgnoreCase) == true ? "Apply Action Sequence" :
            item.Definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.SkillDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Apply Skill Designer Changes" :
            item.Definition.SpecializedEditorId?.Equals(YanflySkillProgressionFeatureDefinitions.ProgressionDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Apply Progression Changes" :
            item.Definition.SpecializedEditorId?.Equals(YanflyItemCommerceFeatureDefinitions.DesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Apply Items, Equipment, Shops & Currencies Changes" :
            item.Definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.EnemyDesignerEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Apply Enemy Designer Changes" :
            item.Definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.StateLifecycleEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Apply State Lifecycle Changes" :
            item.Definition.SpecializedEditorId?.Equals(YanflyEnemyStateFormulaFeatureDefinitions.PassiveAuraEditorId, StringComparison.OrdinalIgnoreCase) == true ? "Apply Passive State & Aura Changes" :
            "Apply Formula Lab Changes";
        using PluginTagChangePreviewDialog previewDialog = new(previewTitle, change);
        if (previewDialog.ShowDialog(this) == DialogResult.OK) session.ReplaceText(after);
        return true;
    }

    private void RemoveSelected()
    {
        PluginFeatureBrowserItem? item = SelectedItem();
        if (item is null || workspace is null || session is null || !item.IsPresent) return;
        bool removeAll = false;
        if (item.OccurrenceCount > 1)
        {
            DialogResult choice = MessageBox.Show(this,
                $"Remove all {item.OccurrenceCount} occurrences?\n\nChoose No to remove only the first occurrence.",
                "Remove Plugin Tag", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) return;
            removeAll = choice == DialogResult.Yes;
        }
        try
        {
            PluginTagChangePreview change = workspace.PreviewRemove(item.Definition.Key, 0, removeAll);
            using PluginTagChangePreviewDialog previewDialog = new("Remove Plugin Tag", change);
            if (previewDialog.ShowDialog(this) != DialogResult.OK) return;
            session.RemoveFeature(item.Definition.Key, 0, removeAll);
        }
        catch (PluginTextMutationException exception)
        {
            MessageBox.Show(this, exception.Message, "Plugin tag could not be removed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }


    private void ShowRecipes()
    {
        if (session is null) return;
        using PluginRecipePickerDialog dialog = new(projectSession, catalog, session, availability);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        PluginTagRecipePreview recipe = dialog.SelectedPreview;
        PluginTagChangePreview change = new(recipe.Before, recipe.After, string.Empty, recipe.Lines);
        using PluginTagChangePreviewDialog previewDialog = new("Apply Plugin Tag Recipe", change);
        if (previewDialog.ShowDialog(this) != DialogResult.OK) return;
        session.ReplaceText(recipe.After);
    }

    private PluginFeatureBrowserQuery BuildQuery(string? category) => new(
        search.Text,
        category,
        scope.SelectedIndex switch
        {
            1 => PluginFeatureBrowserScope.All,
            2 => PluginFeatureBrowserScope.Current,
            _ => PluginFeatureBrowserScope.Installed
        },
        advanced.Checked);

    private PluginFeatureBrowserItem? SelectedItem() =>
        features.SelectedRows.Count == 1 ? features.SelectedRows[0].Tag as PluginFeatureBrowserItem : null;

    private void UnsubscribeSession()
    {
        if (session is null) return;
        session.ParsedDocumentChanged -= SessionChanged;
        session.DirtyStateChanged -= SessionDirtyChanged;
    }

    private static string StatusText(PluginFeatureAvailability value) => value.State switch
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

    private static string BuildPluginStatusText(InstalledPluginAnalysis? value)
    {
        if (value is null) return "Plugin scan pending";
        int errors = value.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Error);
        int warnings = value.Issues.Count(issue => issue.Severity == InstalledPluginIssueSeverity.Warning);
        return $"YEP: {value.Entries.Count} installed, {errors} error(s), {warnings} warning(s)";
    }

    private static TextBox ReadOnlyCodeBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
}
