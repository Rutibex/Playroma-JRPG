using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class EnemyDropEditDialog : Form
{
    private readonly ComboBox _kind = Phase9BEditorUi.OptionCombo(new Dictionary<int, string>
    {
        [0] = "None", [1] = "Item", [2] = "Weapon", [3] = "Armor"
    });
    private readonly NamedIdPicker _target = new();
    private readonly NumericUpDown _denominator = Phase9BEditorUi.Number(1, 999999);
    private readonly Label _probability = new() { AutoSize = true };
    private readonly Phase9CProjectCatalog _catalog;
    private readonly string _raw;

    public EnemyDropEditDialog(Phase9CProjectCatalog catalog, MvEnemyDropSnapshot drop)
    {
        _catalog = catalog ?? Phase9CProjectCatalog.Empty;
        _raw = string.IsNullOrWhiteSpace(drop.RawJson) ? "{}" : drop.RawJson;
        Text = "Enemy Drop";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(520, 290);
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Kind", _kind);
        Phase9BEditorUi.AddRow(table, 1, "Database Record", _target);
        Phase9BEditorUi.AddRow(table, 2, "Denominator", _denominator);
        Phase9BEditorUi.AddRow(table, 3, "Probability", _probability);
        FlowLayoutPanel buttons = Buttons(out Button accept, out Button cancel);
        Phase9BEditorUi.AddRow(table, 4, string.Empty, buttons);
        Controls.Add(table);
        AcceptButton = accept;
        CancelButton = cancel;
        accept.Click += (_, _) => { Result = ReadResult(); DialogResult = DialogResult.OK; };
        _kind.SelectedIndexChanged += (_, _) => { PopulateTarget(); UpdateProbability(); };
        _denominator.ValueChanged += (_, _) => UpdateProbability();
        Phase9BEditorUi.SelectOption(_kind, drop.Kind);
        PopulateTarget();
        _target.SelectedId = drop.DataId;
        Phase9BEditorUi.Set(_denominator, Math.Max(1, drop.Denominator));
        UpdateProbability();
    }

    public MvEnemyDropSnapshot? Result { get; private set; }

    private void PopulateTarget()
    {
        int kind = Phase9BEditorUi.SelectedOption(_kind);
        _target.SetChoices(_catalog.ChoicesForDropKind(kind), kind switch { 1 => "Item", 2 => "Weapon", 3 => "Armor", _ => "Record" }, includeNone: kind == 0);
        _target.Enabled = kind != 0;
        if (kind == 0) _target.SelectedId = 0;
    }

    private void UpdateProbability()
    {
        int kind = Phase9BEditorUi.SelectedOption(_kind);
        double probability = kind == 0 ? 0 : 1d / Math.Max(1, decimal.ToInt32(_denominator.Value));
        _probability.Text = $"{probability:P4} per victory • {probability * 1000:N2} expected per 1,000 victories";
    }

    private MvEnemyDropSnapshot ReadResult()
    {
        int kind = Phase9BEditorUi.SelectedOption(_kind);
        return new MvEnemyDropSnapshot(kind, kind == 0 ? 1 : _target.SelectedId, decimal.ToInt32(_denominator.Value), _raw);
    }

    internal static FlowLayoutPanel Buttons(out Button accept, out Button cancel)
    {
        FlowLayoutPanel panel = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        accept = new Button { Text = "OK", AutoSize = true };
        cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        panel.Controls.AddRange([accept, cancel]);
        return panel;
    }
}

public sealed class EnemyActionEditDialog : Form
{
    private readonly NamedIdPicker _skill = new();
    private readonly ComboBox _condition = Phase9BEditorUi.OptionCombo(new Dictionary<int, string>
    {
        [0] = "Always", [1] = "Turn", [2] = "HP", [3] = "MP", [4] = "State", [5] = "Party Level", [6] = "Switch"
    });
    private readonly NumericUpDown _first = Phase9BEditorUi.Number(0, 999999);
    private readonly NumericUpDown _second = Phase9BEditorUi.Number(0, 999999);
    private readonly NamedIdPicker _reference = new();
    private readonly NumericUpDown _rating = Phase9BEditorUi.Number(1, 9);
    private readonly Label _firstLabel = new() { AutoSize = true };
    private readonly Label _secondLabel = new() { AutoSize = true };
    private readonly Phase9CProjectCatalog _catalog;
    private readonly string _raw;

    public EnemyActionEditDialog(Phase9CProjectCatalog catalog, MvEnemyActionSnapshot action)
    {
        _catalog = catalog ?? Phase9CProjectCatalog.Empty;
        _raw = string.IsNullOrWhiteSpace(action.RawJson) ? "{}" : action.RawJson;
        Text = "Enemy Action Pattern";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(590, 390);
        _skill.SetChoices(_catalog.Shared.Skills, "Skill", includeNone: false);
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Skill", _skill);
        Phase9BEditorUi.AddRow(table, 1, "Condition", _condition);
        Phase9BEditorUi.AddRow(table, 2, "Condition Value 1", BuildLabeled(_firstLabel, _first));
        Phase9BEditorUi.AddRow(table, 3, "Condition Value 2", BuildLabeled(_secondLabel, _second));
        Phase9BEditorUi.AddRow(table, 4, "Condition Reference", _reference);
        Phase9BEditorUi.AddRow(table, 5, "Rating", _rating);
        Label explanation = new()
        {
            Text = "Ratings are relative weights. Only actions within two rating points of the highest eligible action participate in selection.",
            AutoSize = true,
            MaximumSize = new Size(330, 0),
            ForeColor = SystemColors.GrayText
        };
        Phase9BEditorUi.AddRow(table, 6, string.Empty, explanation);
        FlowLayoutPanel buttons = EnemyDropEditDialog.Buttons(out Button accept, out Button cancel);
        Phase9BEditorUi.AddRow(table, 7, string.Empty, buttons);
        Controls.Add(table);
        AcceptButton = accept;
        CancelButton = cancel;
        _condition.SelectedIndexChanged += (_, _) => ConfigureCondition();
        accept.Click += (_, _) => { Result = ReadResult(); DialogResult = DialogResult.OK; };
        _skill.SelectedId = action.SkillId;
        Phase9BEditorUi.SelectOption(_condition, action.ConditionType);
        Phase9BEditorUi.Set(_rating, action.Rating);
        ConfigureCondition();
        SetConditionValues(action);
    }

    public MvEnemyActionSnapshot? Result { get; private set; }

    private static Control BuildLabeled(Label label, NumericUpDown value)
    {
        TableLayoutPanel panel = new() { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(value, 1, 0);
        return panel;
    }

    private void ConfigureCondition()
    {
        int condition = Phase9BEditorUi.SelectedOption(_condition);
        _reference.Enabled = condition is 4 or 6;
        _first.Enabled = condition is 1 or 2 or 3 or 5;
        _second.Enabled = condition is 1 or 2 or 3;
        _first.DecimalPlaces = condition is 2 or 3 ? 2 : 0;
        _second.DecimalPlaces = condition is 2 or 3 ? 2 : 0;
        _first.Increment = condition is 2 or 3 ? 0.05m : 1m;
        _second.Increment = condition is 2 or 3 ? 0.05m : 1m;
        _first.Maximum = condition is 2 or 3 ? 1m : 999999m;
        _second.Maximum = condition is 2 or 3 ? 1m : 999999m;
        _firstLabel.Text = condition switch
        {
            1 => "First turn",
            2 or 3 => "Minimum ratio",
            5 => "Minimum party level",
            _ => "Unused"
        };
        _secondLabel.Text = condition switch
        {
            1 => "Repeat interval",
            2 or 3 => "Maximum ratio",
            _ => "Unused"
        };
        if (condition == 4) _reference.SetChoices(_catalog.Shared.States, "State", includeNone: false);
        else if (condition == 6)
        {
            _reference.SetChoices(Enumerable.Range(1, 999).Select(id => new NamedDatabaseValue(id, $"Switch {id}")), "Switch", includeNone: false);
        }
        else _reference.SetChoices(Array.Empty<NamedDatabaseValue>(), "Reference", includeNone: true);
    }

    private void SetConditionValues(MvEnemyActionSnapshot action)
    {
        int condition = action.ConditionType;
        if (condition is 4 or 6) _reference.SelectedId = (int)Math.Round(action.ConditionParam1);
        else
        {
            _first.Value = Math.Clamp((decimal)action.ConditionParam1, _first.Minimum, _first.Maximum);
            _second.Value = Math.Clamp((decimal)action.ConditionParam2, _second.Minimum, _second.Maximum);
        }
    }

    private MvEnemyActionSnapshot ReadResult()
    {
        int condition = Phase9BEditorUi.SelectedOption(_condition);
        double first = condition is 4 or 6 ? _reference.SelectedId : decimal.ToDouble(_first.Value);
        double second = condition is 4 or 6 or 5 ? 0d : decimal.ToDouble(_second.Value);
        return new MvEnemyActionSnapshot(
            _skill.SelectedId,
            condition,
            first,
            second,
            decimal.ToInt32(_rating.Value),
            _raw);
    }
}

public sealed class TroopPageEditDialog : Form
{
    private readonly CheckBox _turnEnding = new() { Text = "Turn End", AutoSize = true };
    private readonly CheckBox _turnValid = new() { Text = "Turn", AutoSize = true };
    private readonly NumericUpDown _turnA = Phase9BEditorUi.Number(0, 999999);
    private readonly NumericUpDown _turnB = Phase9BEditorUi.Number(0, 999999);
    private readonly CheckBox _enemyValid = new() { Text = "Enemy HP", AutoSize = true };
    private readonly ComboBox _enemyIndex = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _enemyHp = Phase9BEditorUi.Number(0, 100);
    private readonly CheckBox _actorValid = new() { Text = "Actor HP", AutoSize = true };
    private readonly NamedIdPicker _actor = new();
    private readonly NumericUpDown _actorHp = Phase9BEditorUi.Number(0, 100);
    private readonly CheckBox _switchValid = new() { Text = "Switch ON", AutoSize = true };
    private readonly NumericUpDown _switchId = Phase9BEditorUi.Number(1, 999999);
    private readonly ComboBox _span = Phase9BEditorUi.OptionCombo(new Dictionary<int, string>
    {
        [0] = "Battle", [1] = "Turn", [2] = "Moment"
    });
    private readonly Label _warning = new() { AutoSize = true, MaximumSize = new Size(390, 0), ForeColor = Color.DarkOrange };
    private readonly MvTroopPageSnapshot _source;

    public TroopPageEditDialog(
        Phase9CProjectCatalog catalog,
        MvTroopRecordSnapshot troop,
        MvTroopPageSnapshot page)
    {
        _source = page;
        Text = $"Troop Event Page {page.Index + 1}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(660, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        _actor.SetChoices(catalog.Shared.Actors, "Actor", includeNone: false);
        foreach (MvTroopMemberSnapshot member in troop.Members)
        {
            string name = catalog.Shared.Resolve(Playroma.JRPG.Indexing.ReferenceTargetKind.Enemy, member.EnemyId);
            _enemyIndex.Items.Add(new MemberOption(member.Index, $"#{member.Index + 1} {name}"));
        }
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Condition", _turnEnding);
        Phase9BEditorUi.AddRow(table, 1, "Turn", Row(_turnValid, _turnA, _turnB, InlineLabel("A + B×X")));
        Phase9BEditorUi.AddRow(table, 2, "Enemy HP", Row(_enemyValid, _enemyIndex, _enemyHp, InlineLabel("% or below")));
        Phase9BEditorUi.AddRow(table, 3, "Actor HP", Row(_actorValid, _actor, _actorHp, InlineLabel("% or below")));
        Phase9BEditorUi.AddRow(table, 4, "Switch", Row(_switchValid, _switchId, new Label { Text = "is ON", AutoSize = true }));
        Phase9BEditorUi.AddRow(table, 5, "Span", _span);
        Phase9BEditorUi.AddRow(table, 6, "Warnings", _warning);
        Label commandInfo = new()
        {
            Text = $"This page contains {page.CommandCount} command row(s). Use Edit Commands in the Troop editor to open the complete structured battle-event command editor.",
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            ForeColor = SystemColors.GrayText
        };
        Phase9BEditorUi.AddRow(table, 7, "Commands", commandInfo);
        FlowLayoutPanel buttons = EnemyDropEditDialog.Buttons(out Button accept, out Button cancel);
        Phase9BEditorUi.AddRow(table, 8, string.Empty, buttons);
        Controls.Add(table);
        AcceptButton = accept;
        CancelButton = cancel;
        accept.Click += (_, _) => { Result = ReadResult(); DialogResult = DialogResult.OK; };
        foreach (Control control in new Control[] { _turnEnding, _turnValid, _enemyValid, _actorValid, _switchValid, _span })
        {
            if (control is CheckBox check) check.CheckedChanged += (_, _) => UpdateState();
            else if (control is ComboBox combo) combo.SelectedIndexChanged += (_, _) => UpdateState();
        }
        LoadPage(page);
        UpdateState();
    }

    public MvTroopPageSnapshot? Result { get; private set; }

    private static Label InlineLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        FlowLayoutPanel panel = new() { AutoSize = true, WrapContents = true };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private void LoadPage(MvTroopPageSnapshot page)
    {
        MvTroopPageConditionSnapshot condition = page.Conditions;
        _turnEnding.Checked = condition.TurnEnding;
        _turnValid.Checked = condition.TurnValid;
        Phase9BEditorUi.Set(_turnA, condition.TurnA);
        Phase9BEditorUi.Set(_turnB, condition.TurnB);
        _enemyValid.Checked = condition.EnemyValid;
        int enemyOption = _enemyIndex.Items.Cast<MemberOption>().ToList().FindIndex(item => item.Index == condition.EnemyIndex);
        if (enemyOption >= 0) _enemyIndex.SelectedIndex = enemyOption;
        else if (_enemyIndex.Items.Count > 0) _enemyIndex.SelectedIndex = 0;
        Phase9BEditorUi.Set(_enemyHp, condition.EnemyHp);
        _actorValid.Checked = condition.ActorValid;
        _actor.SelectedId = condition.ActorId;
        Phase9BEditorUi.Set(_actorHp, condition.ActorHp);
        _switchValid.Checked = condition.SwitchValid;
        Phase9BEditorUi.Set(_switchId, condition.SwitchId);
        Phase9BEditorUi.SelectOption(_span, page.Span);
    }

    private void UpdateState()
    {
        _turnA.Enabled = _turnB.Enabled = _turnValid.Checked;
        _enemyIndex.Enabled = _enemyHp.Enabled = _enemyValid.Checked;
        _actor.Enabled = _actorHp.Enabled = _actorValid.Checked;
        _switchId.Enabled = _switchValid.Checked;
        bool any = _turnEnding.Checked || _turnValid.Checked || _enemyValid.Checked || _actorValid.Checked || _switchValid.Checked;
        int span = Phase9BEditorUi.SelectedOption(_span);
        List<string> warnings = [];
        if (!any) warnings.Add("No conditions are enabled. This page will not execute.");
        if (span == 2) warnings.Add("Moment pages may repeat continuously while their conditions remain true.");
        _warning.Text = warnings.Count == 0 ? "No page-level warnings." : string.Join(Environment.NewLine, warnings);
    }

    private MvTroopPageSnapshot ReadResult()
    {
        MvTroopPageConditionSnapshot old = _source.Conditions;
        MvTroopPageConditionSnapshot condition = old with
        {
            TurnEnding = _turnEnding.Checked,
            TurnValid = _turnValid.Checked,
            TurnA = decimal.ToInt32(_turnA.Value),
            TurnB = decimal.ToInt32(_turnB.Value),
            EnemyValid = _enemyValid.Checked,
            EnemyIndex = (_enemyIndex.SelectedItem as MemberOption)?.Index ?? 0,
            EnemyHp = decimal.ToInt32(_enemyHp.Value),
            ActorValid = _actorValid.Checked,
            ActorId = _actor.SelectedId,
            ActorHp = decimal.ToInt32(_actorHp.Value),
            SwitchValid = _switchValid.Checked,
            SwitchId = decimal.ToInt32(_switchId.Value)
        };
        return _source with { Conditions = condition, Span = Phase9BEditorUi.SelectedOption(_span) };
    }

    private sealed record MemberOption(int Index, string Name)
    {
        public override string ToString() => Name;
    }
}


public sealed record TroopBattlebackSelection(string Battleback1Name, string Battleback2Name);

public sealed class TroopBattlebackDialog : Form
{
    private readonly AssetPicker _battleback1;
    private readonly AssetPicker _battleback2;
    private readonly Label _summary = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        MaximumSize = new Size(460, 0),
        ForeColor = SystemColors.GrayText
    };

    public TroopBattlebackDialog(MvProjectSession session, string battleback1Name, string battleback2Name)
    {
        ArgumentNullException.ThrowIfNull(session);
        _battleback1 = new AssetPicker(session, AssetCategory.Battlebacks1) { Dock = DockStyle.Fill };
        _battleback2 = new AssetPicker(session, AssetCategory.Battlebacks2) { Dock = DockStyle.Fill };
        _battleback1.Value = battleback1Name ?? string.Empty;
        _battleback2.Value = battleback2Name ?? string.Empty;

        Text = "Change Troop Battle Background";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(650, 300);

        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Battleback 1", _battleback1);
        Phase9BEditorUi.AddRow(table, 1, "Battleback 2", _battleback2);
        _summary.Text = "The formation preview uses the standard System.json battleback pair. These are also the default backgrounds used by the built-in battle-test profile.";
        Phase9BEditorUi.AddRow(table, 2, "Storage", _summary);
        FlowLayoutPanel buttons = EnemyDropEditDialog.Buttons(out Button accept, out Button cancel);
        Phase9BEditorUi.AddRow(table, 3, string.Empty, buttons);
        Controls.Add(table);
        AcceptButton = accept;
        CancelButton = cancel;
        accept.Click += (_, _) =>
        {
            Result = new TroopBattlebackSelection(_battleback1.Value.Trim(), _battleback2.Value.Trim());
            DialogResult = DialogResult.OK;
        };
    }

    public TroopBattlebackSelection? Result { get; private set; }
}

public sealed class RawCommandListDialog : Form
{
    public RawCommandListDialog(string title, string rawJson)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(850, 650);
        CodeTextEditorControl editor = new() { Dock = DockStyle.Fill, ReadOnly = true };
        editor.SetLanguage(CodeEditorLanguage.PlainText);
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            editor.Text = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            editor.Text = rawJson;
        }
        Controls.Add(editor);
    }
}

public sealed class BattleTestBattlerEditDialog : Form
{
    private readonly NamedIdPicker _actor = new();
    private readonly NumericUpDown _level = Phase9BEditorUi.Number(1, 99);
    private readonly TextBox _equipment = new() { Dock = DockStyle.Fill, PlaceholderText = "Equipment IDs, comma-separated" };
    private readonly string _raw;

    public BattleTestBattlerEditDialog(Phase9CProjectCatalog catalog, BattleTestBattlerSnapshot battler)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(battler);
        _raw = string.IsNullOrWhiteSpace(battler.RawJson) ? "{}" : battler.RawJson;
        Text = "Battle Test Actor";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(570, 300);
        _actor.SetChoices(catalog.Shared.Actors, "Actor", includeNone: false);
        _actor.SelectedId = battler.ActorId;
        Phase9BEditorUi.Set(_level, battler.Level);
        _equipment.Text = string.Join(", ", battler.Equipment);
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Actor", _actor);
        Phase9BEditorUi.AddRow(table, 1, "Level", _level);
        Phase9BEditorUi.AddRow(table, 2, "Equipment", _equipment);
        FlowLayoutPanel buttons = EnemyDropEditDialog.Buttons(out Button accept, out Button cancel);
        Phase9BEditorUi.AddRow(table, 3, string.Empty, buttons);
        Controls.Add(table);
        AcceptButton = accept;
        CancelButton = cancel;
        accept.Click += (_, _) =>
        {
            Result = new BattleTestBattlerSnapshot(
                _actor.SelectedId,
                decimal.ToInt32(_level.Value),
                _equipment.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(text => int.TryParse(text, out int value) ? Math.Max(0, value) : 0)
                    .ToArray(),
                _raw);
            DialogResult = DialogResult.OK;
        };
    }

    public BattleTestBattlerSnapshot? Result { get; private set; }
}

public sealed class BattleTestProfileDialog : Form
{
    private readonly TextBox _battleback1 = new() { Dock = DockStyle.Fill };
    private readonly TextBox _battleback2 = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _battlers = EnemyEditorControl.Grid("Slot", "Actor", "Level", "Equipment");
    private readonly List<BattleTestBattlerSnapshot> _items = [];
    private readonly Phase9CProjectCatalog _catalog;
    private readonly BattleTestProfile _source;

    public BattleTestProfileDialog(Phase9CProjectCatalog catalog, BattleTestProfile profile)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _source = profile ?? throw new ArgumentNullException(nameof(profile));
        Text = $"Battle Test — Troop {profile.TroopId:D4}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(780, 600);
        _battleback1.Text = profile.Battleback1Name;
        _battleback2.Text = profile.Battleback2Name;
        _items.AddRange(profile.Battlers);
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        TableLayoutPanel backgrounds = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(backgrounds, 0, "Battleback 1", _battleback1);
        Phase9BEditorUi.AddRow(backgrounds, 1, "Battleback 2", _battleback2);
        ToolStrip tools = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        ToolStripButton add = new("Add Actor");
        ToolStripButton edit = new("Edit");
        ToolStripButton remove = new("Remove");
        add.Click += (_, _) => AddBattler();
        edit.Click += (_, _) => EditBattler();
        remove.Click += (_, _) => RemoveBattler();
        tools.Items.AddRange([add, edit, remove]);
        Panel listPanel = new() { Dock = DockStyle.Fill };
        listPanel.Controls.Add(_battlers);
        listPanel.Controls.Add(tools);
        Label note = new()
        {
            Text = "These values are written to the standard System.json testTroopId, testBattlers, battleback1Name, and battleback2Name fields. Troops.json is not extended.",
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            ForeColor = SystemColors.GrayText
        };
        FlowLayoutPanel buttons = EnemyDropEditDialog.Buttons(out Button accept, out Button cancel);
        root.Controls.Add(backgrounds, 0, 0);
        root.Controls.Add(listPanel, 0, 1);
        root.Controls.Add(note, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = accept;
        CancelButton = cancel;
        accept.Click += (_, _) =>
        {
            Result = _source with
            {
                Battleback1Name = _battleback1.Text.Trim(),
                Battleback2Name = _battleback2.Text.Trim(),
                Battlers = _items.ToArray()
            };
            DialogResult = DialogResult.OK;
        };
        _battlers.CellDoubleClick += (_, _) => EditBattler();
        RefreshRows();
    }

    public BattleTestProfile? Result { get; private set; }

    private void AddBattler()
    {
        if (_items.Count >= 4)
        {
            MessageBox.Show(this, "RPG Maker MV battle tests support up to four test battlers.", "Battle Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using BattleTestBattlerEditDialog dialog = new(_catalog, new BattleTestBattlerSnapshot(1, 1, [], "{}"));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _items.Add(dialog.Result);
        RefreshRows();
    }

    private void EditBattler()
    {
        int index = _battlers.SelectedRows.Count > 0 ? _battlers.SelectedRows[0].Index : _battlers.CurrentCell?.RowIndex ?? -1;
        if (index < 0 || index >= _items.Count) return;
        using BattleTestBattlerEditDialog dialog = new(_catalog, _items[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _items[index] = dialog.Result;
        RefreshRows();
        _battlers.Rows[index].Selected = true;
    }

    private void RemoveBattler()
    {
        int index = _battlers.SelectedRows.Count > 0 ? _battlers.SelectedRows[0].Index : _battlers.CurrentCell?.RowIndex ?? -1;
        if (index < 0 || index >= _items.Count) return;
        _items.RemoveAt(index);
        RefreshRows();
    }

    private void RefreshRows()
    {
        _battlers.Rows.Clear();
        for (int index = 0; index < _items.Count; index++)
        {
            BattleTestBattlerSnapshot item = _items[index];
            _battlers.Rows.Add(index + 1, _catalog.Shared.Resolve(Playroma.JRPG.Indexing.ReferenceTargetKind.Actor, item.ActorId), item.Level, string.Join(", ", item.Equipment));
        }
    }
}
