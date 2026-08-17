using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Playroma.JRPG.Application;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed class BattleAiPriorityEditorDialog : Form
{
    private readonly string originalNotes;
    private readonly BindingList<BattleAiRuleRow> rows = [];
    private readonly NumericUpDown aiLevel = new() { Minimum = 0, Maximum = 100, Value = 100, Width = 90 };
    private readonly CheckBox considerTaunt = new() { Text = "Consider Taunt effects", AutoSize = true };
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false
    };
    private readonly TextBox preview = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly Label validation = new() { Dock = DockStyle.Bottom, Height = 42, ForeColor = Color.DarkRed, Padding = new Padding(6) };
    private readonly MvProjectSession projectSession;
    private readonly BattleAiPriorityModel parsed;

    public BattleAiPriorityEditorDialog(MvProjectSession projectSession, string notes)
    {
        this.projectSession = projectSession ?? throw new ArgumentNullException(nameof(projectSession));
        originalNotes = notes ?? string.Empty;
        parsed = BattleAiPriorityCodec.Parse(originalNotes);
        Text = "Battle AI Priority Editor";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 650);
        Size = new Size(1060, 760);
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        AccessibleName = Text;
        BuildLayout();
        LoadModel();
        RefreshPreview();
    }

    public string ResultNotes { get; private set; } = string.Empty;

    private void BuildLayout()
    {
        FlowLayoutPanel settings = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8), WrapContents = false };
        settings.Controls.Add(new Label { Text = "AI Level (strictness):", AutoSize = true, Padding = new Padding(0, 7, 3, 0) });
        settings.Controls.Add(aiLevel);
        settings.Controls.Add(new Label { Text = "%", AutoSize = true, Padding = new Padding(0, 7, 12, 0) });
        settings.Controls.Add(considerTaunt);
        settings.Controls.Add(new Label
        {
            Text = "Rules run from top to bottom; the first successful rule is used.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(18, 7, 0, 0)
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattleAiRuleRow.Priority), HeaderText = "#", Width = 42, ReadOnly = true });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(BattleAiRuleRow.Enabled), HeaderText = "On", Width = 40 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattleAiRuleRow.Condition), HeaderText = "Condition", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 250, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattleAiRuleRow.Skill), HeaderText = "Skill", Width = 150, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(BattleAiRuleRow.Target), HeaderText = "Target", Width = 145, ReadOnly = true });
        grid.DataSource = rows;
        grid.CellDoubleClick += (_, _) => EditSelected();
        grid.CellValueChanged += (_, _) => RefreshPreview();

        FlowLayoutPanel commands = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), WrapContents = false };
        Button add = new() { Text = "Add Rule…", AutoSize = true };
        Button edit = new() { Text = "Edit…", AutoSize = true };
        Button duplicate = new() { Text = "Duplicate", AutoSize = true };
        Button remove = new() { Text = "Remove", AutoSize = true };
        Button up = new() { Text = "Move Up", AutoSize = true };
        Button down = new() { Text = "Move Down", AutoSize = true };
        Button healer = new() { Text = "Add Healer Example", AutoSize = true };
        commands.Controls.AddRange([add, edit, duplicate, remove, up, down, new Label { Width = 16 }, healer]);
        add.Click += (_, _) => AddRule();
        edit.Click += (_, _) => EditSelected();
        duplicate.Click += (_, _) => DuplicateSelected();
        remove.Click += (_, _) => RemoveSelected();
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);
        healer.Click += (_, _) => AddHealerExample();

        Panel ruleHost = new() { Dock = DockStyle.Fill };
        ruleHost.Controls.Add(grid);
        ruleHost.Controls.Add(commands);

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Priority Rules") { Controls = { ruleHost } });
        Panel previewHost = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        previewHost.Controls.Add(preview);
        previewHost.Controls.Add(validation);
        tabs.TabPages.Add(new TabPage("Exact Notes Preview") { Controls = { previewHost } });

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button accept = new() { Text = "Use This AI", AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        accept.Click += (_, _) => AcceptChanges();
        aiLevel.ValueChanged += (_, _) => RefreshPreview();
        considerTaunt.CheckedChanged += (_, _) => RefreshPreview();

        Controls.Add(tabs);
        Controls.Add(settings);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = accept;
    }

    private void LoadModel()
    {
        aiLevel.Value = Math.Clamp(parsed.AiLevel, 0, 100);
        considerTaunt.Checked = parsed.ConsiderTaunt;
        foreach (BattleAiRule rule in parsed.Rules) rows.Add(new BattleAiRuleRow(rule));
        Renumber();
    }

    private BattleAiPriorityModel BuildModel() => new(
        decimal.ToInt32(aiLevel.Value),
        considerTaunt.Checked,
        rows.Select(row => row.ToRule()).ToArray(),
        parsed.EffectiveUnparsedLines);

    private void RefreshPreview()
    {
        BattleAiPriorityModel model = BuildModel();
        string lineEnding = DetectLineEnding(originalNotes);
        preview.Text = BattleAiPriorityCodec.ReplaceInNotes(originalNotes, model, lineEnding);
        IReadOnlyList<BattleAiValidationIssue> issues = BattleAiPriorityCodec.Validate(model);
        validation.Text = issues.Count == 0
            ? "Valid Battle AI configuration. Unknown lines from the original AI block are preserved."
            : string.Join("  ", issues.Take(3).Select(issue => $"{issue.Severity}: {issue.Message}"));
    }

    private void AddRule()
    {
        using BattleAiRuleEditorDialog dialog = new(projectSession, null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        rows.Add(new BattleAiRuleRow(dialog.Rule));
        Renumber();
        SelectRow(rows.Count - 1);
        RefreshPreview();
    }

    private void EditSelected()
    {
        int index = SelectedIndex();
        if (index < 0) return;
        BattleAiRule existing = rows[index].ToRule();
        using BattleAiRuleEditorDialog dialog = new(projectSession, existing);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        rows[index] = new BattleAiRuleRow(dialog.Rule) { Priority = index + 1 };
        SelectRow(index);
        RefreshPreview();
    }

    private void DuplicateSelected()
    {
        int index = SelectedIndex();
        if (index < 0) return;
        rows.Insert(index + 1, new BattleAiRuleRow(rows[index].ToRule()));
        Renumber();
        SelectRow(index + 1);
        RefreshPreview();
    }

    private void RemoveSelected()
    {
        int index = SelectedIndex();
        if (index < 0) return;
        rows.RemoveAt(index);
        Renumber();
        SelectRow(Math.Min(index, rows.Count - 1));
        RefreshPreview();
    }

    private void MoveSelected(int direction)
    {
        int index = SelectedIndex();
        int target = index + direction;
        if (index < 0 || target < 0 || target >= rows.Count) return;
        BattleAiRuleRow item = rows[index];
        rows.RemoveAt(index);
        rows.Insert(target, item);
        Renumber();
        SelectRow(target);
        RefreshPreview();
    }

    private void AddHealerExample()
    {
        rows.Add(new BattleAiRuleRow(new(
            new(BattleAiConditionKind.UserParameter, "HP%", "<=", "50%"),
            "Skill 1", "Lowest HP%")));
        Renumber();
        SelectRow(rows.Count - 1);
        RefreshPreview();
    }

    private void AcceptChanges()
    {
        BattleAiPriorityModel model = BuildModel();
        IReadOnlyList<BattleAiValidationIssue> errors = BattleAiPriorityCodec.Validate(model)
            .Where(issue => issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (errors.Count > 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, errors.Select(issue => issue.Message)), "Battle AI is not valid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ResultNotes = BattleAiPriorityCodec.ReplaceInNotes(originalNotes, model, DetectLineEnding(originalNotes));
        DialogResult = DialogResult.OK;
        Close();
    }

    private int SelectedIndex() => grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Index : -1;
    private void SelectRow(int index)
    {
        grid.ClearSelection();
        if (index >= 0 && index < grid.Rows.Count) grid.Rows[index].Selected = true;
    }
    private void Renumber() { for (int i = 0; i < rows.Count; i++) rows[i].Priority = i + 1; grid.Refresh(); }
    private static string DetectLineEnding(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : Environment.NewLine;

    private sealed class BattleAiRuleRow : INotifyPropertyChanged
    {
        private int priority;
        private bool enabled;
        public BattleAiRuleRow(BattleAiRule rule) { Source = rule; enabled = rule.IsEnabled; }
        public BattleAiRule Source { get; }
        public int Priority { get => priority; set { priority = value; PropertyChanged?.Invoke(this, new(nameof(Priority))); } }
        public bool Enabled { get => enabled; set { enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); } }
        public string Condition => BattleAiConditionCodec.Render(Source.Condition);
        public string Skill => Source.SkillToken;
        public string Target => Source.Target;
        public BattleAiRule ToRule() => Source with { IsEnabled = Enabled };
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

internal sealed class BattleAiRuleEditorDialog : Form
{
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly ComboBox operand = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Top };
    private readonly ComboBox comparison = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly TextBox value = new() { Dock = DockStyle.Top };
    private readonly TextBox conditionPreview = new() { Dock = DockStyle.Top, ReadOnly = true };
    private readonly ComboBox skillMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    private readonly DatabaseIdPicker skillId;
    private readonly TextBox skillName = new() { Dock = DockStyle.Top };
    private readonly ComboBox target = new() { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Top };
    private readonly Label help = new() { Dock = DockStyle.Top, Height = 54, ForeColor = SystemColors.GrayText };

    public BattleAiRuleEditorDialog(MvProjectSession session, BattleAiRule? existing)
    {
        skillId = new DatabaseIdPicker(session, MvDocumentKind.Skills) { Dock = DockStyle.Top, AllowNone = false };
        Text = existing is null ? "Add AI Rule" : "Edit AI Rule";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 560);
        MinimumSize = new Size(560, 500);
        MinimizeBox = false;
        MaximizeBox = false;
        kind.Items.AddRange(Enum.GetNames<BattleAiConditionKind>());
        comparison.Items.AddRange([">", ">=", "<", "<=", "===", "!=="]);
        skillMode.Items.AddRange(["Skill ID", "Skill Name"]);
        target.Items.AddRange(BattleAiTargets.All.Cast<object>().ToArray());
        BuildLayout();
        LoadRule(existing);
        WireEvents();
        RefreshConditionUi();
    }

    public BattleAiRule Rule { get; private set; } = new(new(BattleAiConditionKind.Always), "Skill 1", "Random");

    private void BuildLayout()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 10 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        int row = 0;
        Add(table, ref row, "Condition type", kind);
        Add(table, ref row, "Subject / ID", operand);
        Add(table, ref row, "Comparison", comparison);
        Add(table, ref row, "Value / expression", value);
        Add(table, ref row, "Generated condition", conditionPreview);
        Add(table, ref row, "Skill selection", skillMode);
        Add(table, ref row, "Skill ID", skillId);
        Add(table, ref row, "Skill name", skillName);
        Add(table, ref row, "Target priority", target);
        table.Controls.Add(help, 0, row);
        table.SetColumnSpan(help, 2);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button accept = new() { Text = "Save Rule", AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(accept);
        accept.Click += (_, _) => SaveRule();
        Controls.Add(table);
        Controls.Add(buttons);
        CancelButton = cancel;
        AcceptButton = accept;
    }

    private void LoadRule(BattleAiRule? rule)
    {
        BattleAiRule source = rule ?? new(new(BattleAiConditionKind.Always), "Skill 1", "Random");
        kind.SelectedItem = source.Condition.Kind.ToString();
        operand.Text = source.Condition.Operand;
        comparison.SelectedItem = string.IsNullOrWhiteSpace(source.Condition.Comparison) ? ">=" : source.Condition.Comparison;
        value.Text = source.Condition.Kind == BattleAiConditionKind.Multiple
            ? string.Join(" +++ ", source.Condition.EffectiveChildren.Select(BattleAiConditionCodec.Render))
            : source.Condition.Value;
        Match skillMatch = System.Text.RegularExpressions.Regex.Match(source.SkillToken, "(?:Skill\\s+)?(?<id>\\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        if (skillMatch.Success)
        {
            skillMode.SelectedItem = "Skill ID";
            skillId.SelectedId = int.Parse(skillMatch.Groups["id"].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            skillMode.SelectedItem = "Skill Name";
            skillName.Text = source.SkillToken;
        }
        target.Text = source.Target;
    }

    private void WireEvents()
    {
        kind.SelectedIndexChanged += (_, _) => RefreshConditionUi();
        operand.TextChanged += (_, _) => RefreshPreview();
        comparison.SelectedIndexChanged += (_, _) => RefreshPreview();
        value.TextChanged += (_, _) => RefreshPreview();
        skillMode.SelectedIndexChanged += (_, _) => RefreshSkillUi();
    }

    private void RefreshConditionUi()
    {
        BattleAiConditionKind selected = SelectedKind();
        bool needsOperand = selected is BattleAiConditionKind.Switch or BattleAiConditionKind.Variable or BattleAiConditionKind.ElementRate or BattleAiConditionKind.Parameter or BattleAiConditionKind.UserParameter;
        bool needsComparison = selected is BattleAiConditionKind.Turn or BattleAiConditionKind.Variable or BattleAiConditionKind.PartyAliveMembers or BattleAiConditionKind.PartyDeadMembers or BattleAiConditionKind.TroopAliveMembers or BattleAiConditionKind.TroopDeadMembers or BattleAiConditionKind.Parameter or BattleAiConditionKind.UserParameter or BattleAiConditionKind.HighestPartyLevel;
        bool needsValue = selected != BattleAiConditionKind.Always;
        operand.Enabled = needsOperand;
        comparison.Enabled = needsComparison;
        value.Enabled = needsValue;
        operand.Items.Clear();
        if (selected is BattleAiConditionKind.Parameter or BattleAiConditionKind.UserParameter)
            operand.Items.AddRange(["HP%", "MP%", "TP%", "MaxHP", "MaxMP", "ATK", "DEF", "MAT", "MDF", "AGI", "LUK", "Level"]);
        else if (selected == BattleAiConditionKind.ElementRate)
            operand.Items.AddRange(["Fire", "Ice", "Thunder", "Water", "Earth", "Wind", "Light", "Dark"]);
        if (selected == BattleAiConditionKind.ElementRate && string.IsNullOrWhiteSpace(value.Text)) value.Text = "Weakness";
        if (selected == BattleAiConditionKind.Switch && string.IsNullOrWhiteSpace(value.Text)) value.Text = "On";
        help.Text = selected switch
        {
            BattleAiConditionKind.Multiple => "Enter complete conditions separated by +++. Example: State === Blind +++ User HP% Param <= 50%.",
            BattleAiConditionKind.Eval => "Enter only the JavaScript expression after EVAL. It is stored but never executed by Playroma.",
            BattleAiConditionKind.Raw => "Enter the exact YEP condition text. This is useful for uncommon or extension-provided conditions.",
            _ => "The condition also determines the valid target group. Target priority chooses one member from that group."
        };
        RefreshPreview();
    }

    private void RefreshSkillUi()
    {
        bool id = Convert.ToString(skillMode.SelectedItem) == "Skill ID";
        skillId.Enabled = id;
        skillName.Enabled = !id;
    }

    private void RefreshPreview()
    {
        try { conditionPreview.Text = BattleAiConditionCodec.Render(BuildCondition()); }
        catch (Exception exception) { conditionPreview.Text = exception.Message; }
    }

    private BattleAiCondition BuildCondition()
    {
        BattleAiConditionKind selected = SelectedKind();
        if (selected == BattleAiConditionKind.Multiple)
            return new(selected, Children: value.Text.Split("+++", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(BattleAiConditionCodec.Parse).ToArray());
        return new(selected, operand.Text.Trim(), Convert.ToString(comparison.SelectedItem) ?? string.Empty, value.Text.Trim());
    }

    private void SaveRule()
    {
        string skill = Convert.ToString(skillMode.SelectedItem) == "Skill Name" ? skillName.Text.Trim() : $"Skill {skillId.SelectedId}";
        if (string.IsNullOrWhiteSpace(skill) || skill == "Skill 0")
        {
            MessageBox.Show(this, "Select a skill or enter its name.", "Skill required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string targetText = string.IsNullOrWhiteSpace(target.Text) ? "Random" : target.Text.Trim();
        Rule = new(BuildCondition(), skill, targetText);
        BattleAiValidationIssue? error = BattleAiPriorityCodec.Validate(new(100, false, [Rule])).FirstOrDefault(issue => issue.Severity == "Error");
        if (error is not null)
        {
            MessageBox.Show(this, error.Message, "Rule is not valid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private BattleAiConditionKind SelectedKind() => Enum.TryParse(Convert.ToString(kind.SelectedItem), out BattleAiConditionKind parsed) ? parsed : BattleAiConditionKind.Always;
    private static void Add(TableLayoutPanel table, ref int row, string caption, Control control)
    {
        control.Margin = new Padding(3, 4, 3, 4);
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 7, 0, 0) }, 0, row);
        table.Controls.Add(control, 1, row++);
    }
}
