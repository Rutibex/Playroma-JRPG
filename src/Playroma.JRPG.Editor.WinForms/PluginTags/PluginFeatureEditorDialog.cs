using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class PluginFeatureEditorDialog : Form
{
    private readonly PluginFeatureDefinition definition;
    private readonly PluginFeatureEditorPlan plan;
    private readonly Dictionary<string, PluginArgumentEditorBinding> editors = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBox? bodyEditor;
    private readonly ComboBox writeMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox conflictResolution = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly TextBox preview = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly Label validation = new() { Dock = DockStyle.Bottom, Height = 38, ForeColor = Color.DarkRed };
    private readonly Label coverage = new() { Dock = DockStyle.Top, Height = 34, ForeColor = SystemColors.GrayText, Padding = new Padding(12, 6, 12, 3) };
    private readonly Button accept = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };

    public PluginFeatureEditorDialog(
        MvProjectSession projectSession,
        PluginFeatureDefinition definition,
        PluginFeatureValues? existingValues = null,
        bool isEditing = false)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        plan = PluginGenericEditorCoverage.CreatePlan(definition);
        Text = existingValues is null ? $"Add {definition.DisplayName}" : $"Edit {definition.DisplayName}";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(720, 560);
        Size = new Size(880, 710);
        AccessibleName = Text;
        coverage.Text = plan.CoverageMessage;

        SplitContainer split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 405 };
        Panel formHost = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        int row = 0;
        writeMode.Items.AddRange(Enum.GetNames<PluginFeatureWriteMode>());
        writeMode.SelectedItem = isEditing ? nameof(PluginFeatureWriteMode.ReplaceFirst) : nameof(PluginFeatureWriteMode.Smart);
        writeMode.SelectedIndexChanged += (_, _) => RefreshPreview();
        AddRow(table, ref row, "Write behavior", writeMode);

        conflictResolution.Items.AddRange(Enum.GetNames<PluginConflictResolution>());
        conflictResolution.SelectedItem = nameof(PluginConflictResolution.Fail);
        conflictResolution.SelectedIndexChanged += (_, _) => RefreshPreview();
        AddRow(table, ref row, "If conflicts exist", conflictResolution);

        PluginTypedArgumentEditorFactory factory = new(projectSession);
        foreach (PluginArgumentEditorDescriptor descriptor in plan.Arguments)
        {
            PluginArgumentDefinition argument = descriptor.Argument;
            PluginArgumentEditorBinding binding = factory.Create(descriptor);
            binding.Control.Dock = binding.Control.Dock == DockStyle.Fill ? DockStyle.Top : binding.Control.Dock;
            binding.Control.AccessibleName = argument.DisplayName;
            binding.Control.AccessibleDescription = argument.Description;
            binding.Write(ExistingText(argument, existingValues));
            binding.SubscribeChanged((_, _) => RefreshPreview());
            editors[argument.Name] = binding;
            AddRow(table, ref row, argument.DisplayName + (argument.IsRequired ? " *" : string.Empty), binding.Control);
        }

        if (definition.Syntax.IsBlock)
        {
            bodyEditor = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Height = 180,
                Text = existingValues?.Body ?? string.Empty,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                AccessibleName = "Plugin tag block body"
            };
            bodyEditor.TextChanged += (_, _) => RefreshPreview();
            AddRow(table, ref row, plan.IsAdvancedCode ? "Formula / JavaScript body" : "Block body", bodyEditor);
        }

        formHost.Controls.Add(table);
        split.Panel1.Controls.Add(formHost);
        GroupBox previewGroup = new() { Text = "Exact generated syntax", Dock = DockStyle.Fill, Padding = new Padding(8) };
        previewGroup.Controls.Add(preview);
        previewGroup.Controls.Add(validation);
        split.Panel2.Controls.Add(previewGroup);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        Controls.Add(split);
        Controls.Add(coverage);
        Controls.Add(buttons);
        AcceptButton = accept;
        CancelButton = cancel;
        accept.Click += (_, _) =>
        {
            if (!TryReadValues(out _, out string? error))
            {
                DialogResult = DialogResult.None;
                validation.Text = error;
            }
        };
        RefreshPreview();
    }

    public PluginFeatureValues Values =>
        TryReadValues(out PluginFeatureValues? values, out string? error)
            ? values!
            : throw new InvalidOperationException(error);

    public PluginFeatureWriteMode WriteMode => Enum.TryParse(Convert.ToString(writeMode.SelectedItem), out PluginFeatureWriteMode parsed)
        ? parsed : PluginFeatureWriteMode.Smart;

    public PluginConflictResolution ConflictResolution => Enum.TryParse(Convert.ToString(conflictResolution.SelectedItem), out PluginConflictResolution parsed)
        ? parsed : PluginConflictResolution.Fail;

    private void RefreshPreview()
    {
        if (!TryReadValues(out PluginFeatureValues? values, out string? error))
        {
            preview.Text = string.Empty;
            validation.Text = error;
            accept.Enabled = false;
            return;
        }
        try
        {
            preview.Text = new PluginTextRenderer().RenderFeature(definition, values!, Environment.NewLine);
            IReadOnlyList<PluginFormulaIssue> formulaIssues = AnalyzeFormulaValues(values!);
            bool hasFormulaError = formulaIssues.Any(issue => issue.Severity == PluginFormulaIssueSeverity.Error);
            string formulaMessage = string.Join("  ", formulaIssues.Select(issue => issue.Message));
            validation.Text = formulaMessage.Length > 0
                ? formulaMessage
                : plan.RequiresSpecializedEditor
                    ? $"Generic typed/raw fallback. Planned specialized editor: {definition.SpecializedEditorId}."
                    : string.Empty;
            accept.Enabled = !hasFormulaError;
        }
        catch (PluginTagRenderException exception)
        {
            preview.Text = string.Empty;
            validation.Text = exception.Message;
            accept.Enabled = false;
        }
    }

    private IReadOnlyList<PluginFormulaIssue> AnalyzeFormulaValues(PluginFeatureValues values)
    {
        List<PluginFormulaIssue> issues = [];
        if (!string.IsNullOrWhiteSpace(values.Body) &&
            (plan.IsAdvancedCode || definition.Syntax.Kind == PluginSyntaxKind.ScriptBlock))
            issues.AddRange(PluginFormulaSafetyAnalyzer.Analyze(values.Body));
        foreach (PluginArgumentDefinition argument in definition.Arguments.Where(argument =>
                     argument.Kind is PluginArgumentKind.Formula or PluginArgumentKind.JavaScriptExpression or PluginArgumentKind.JavaScriptBlock))
        {
            if (values.TryGetValue(argument.Name, out PluginValue value) && !string.IsNullOrWhiteSpace(value.Text))
                issues.AddRange(PluginFormulaSafetyAnalyzer.Analyze(value.Text));
        }
        return issues;
    }

    private bool TryReadValues(out PluginFeatureValues? values, out string? error)
    {
        List<KeyValuePair<string, PluginValue>> arguments = [];
        foreach (PluginArgumentDefinition argument in definition.Arguments)
        {
            string text = editors[argument.Name].Read();
            PluginValueValidationResult parsed = PluginTypedValueCodec.Parse(argument, text);
            if (!parsed.IsValid)
            {
                values = null;
                error = parsed.Error;
                return false;
            }
            arguments.Add(new(argument.Name, parsed.Value));
        }
        values = new PluginFeatureValues(arguments, bodyEditor?.Text);
        error = null;
        return true;
    }

    private static string ExistingText(PluginArgumentDefinition argument, PluginFeatureValues? existing)
    {
        if (existing is not null && existing.TryGetValue(argument.Name, out PluginValue value)) return value.Text;
        return argument.DefaultValue ?? (argument.Kind == PluginArgumentKind.Boolean ? "false" : string.Empty);
    }

    private static void AddRow(TableLayoutPanel table, ref int row, string caption, Control editor)
    {
        Label label = new()
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 8, 3)
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(label, 0, row);
        table.Controls.Add(editor, 1, row++);
    }
}
