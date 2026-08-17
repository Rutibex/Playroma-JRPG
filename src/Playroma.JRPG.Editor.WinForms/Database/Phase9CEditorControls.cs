using System.Globalization;
using System.Text;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class EnemyEditorControl : UserControl, IDatabaseEditorBody
{
    private static readonly string[] ParameterNames = ["Max HP", "Max MP", "Attack", "Defense", "M.Attack", "M.Defense", "Agility", "Luck"];
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly DatabaseMutationService _mutations;
    private readonly Phase9CMutationService _phase9C;
    private readonly Label _selectionMessage = Phase9BEditorUi.SelectionLabel();
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly AssetIndexSelectionControl _battler;
    private readonly ComboBox _assetMode = Phase9BEditorUi.OptionCombo(new Dictionary<int, string> { [0] = "Front-view enemy", [1] = "Side-view enemy" });
    private readonly NumericUpDown _hue = Phase9BEditorUi.Number(0, 360);
    private readonly NumericUpDown _exp = Phase9BEditorUi.Number(0, int.MaxValue);
    private readonly NumericUpDown _gold = Phase9BEditorUi.Number(0, int.MaxValue);
    private readonly PictureBox _battlerPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(48, 52, 61) };
    private readonly DataGridView _parameters = Grid("Parameter", "Value");
    private readonly DataGridView _drops = Grid("Slot", "Kind", "Record", "Chance", "Raw");
    private readonly DataGridView _actions = Grid("#", "Skill", "Condition", "Rating", "Weight", "Chance");
    private readonly NumericUpDown _scenarioTurn = Phase9BEditorUi.Number(0, 999999);
    private readonly NumericUpDown _scenarioHp = Phase9BEditorUi.Number(0, 100);
    private readonly NumericUpDown _scenarioMp = Phase9BEditorUi.Number(0, 100);
    private readonly NumericUpDown _scenarioLevel = Phase9BEditorUi.Number(1, 9999);
    private readonly NumericUpDown _scenarioCurrentMp = Phase9BEditorUi.Number(0, 999999);
    private readonly TextBox _scenarioStates = new() { Dock = DockStyle.Fill, PlaceholderText = "State IDs, comma-separated" };
    private readonly TextBox _scenarioSwitches = new() { Dock = DockStyle.Fill, PlaceholderText = "ON switch IDs, comma-separated" };
    private readonly DataGridView _evaluation = Grid("Action", "Eligible", "Affordable", "Included", "Weight", "Probability", "Explanation");
    private readonly NumericUpDown _rewardBattles = Phase9BEditorUi.Number(1, 1000000);
    private readonly CheckBox _doubleDrops = new() { Text = "Double drops", AutoSize = true };
    private readonly TextBox _rewardSummary = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly List<MvEnemyDropSnapshot> _dropItems = [];
    private readonly List<MvEnemyActionSnapshot> _actionItems = [];
    private Phase9CProjectCatalog _catalog = Phase9CProjectCatalog.Empty;
    private int _recordId;
    private bool _loading;
    private CancellationTokenSource? _previewCancellation;

    public EnemyEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        Func<DocumentRequest, Task>? documentOpener = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(definition);
        _mutations = new DatabaseMutationService(session.Editing);
        _phase9C = new Phase9CMutationService(session.Editing);
        _battler = new AssetIndexSelectionControl(session, AssetCategory.Enemies, false, hasHue: true,
            assetEditor: documentOpener is null ? null : (category, selection) => OpenSpriteEditorAsync(documentOpener, category, selection));
        Dock = DockStyle.Fill;
        InitializeParameterGrid();
        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("General") { Controls = { BuildGeneralPage() } });
        tabs.TabPages.Add(new TabPage("Parameters") { Controls = { BuildParametersPage() } });
        tabs.TabPages.Add(new TabPage("Drops") { Controls = { BuildDropsPage() } });
        tabs.TabPages.Add(new TabPage("Actions") { Controls = { BuildActionsPage() } });
        tabs.TabPages.Add(new TabPage("Action Lab") { Controls = { BuildActionLabPage() } });
        tabs.TabPages.Add(new TabPage("Rewards") { Controls = { BuildRewardsPage() } });
        Controls.Add(tabs);
        Controls.Add(_selectionMessage);
        _battler.SelectionChanged += async (_, _) =>
        {
            int selectedHue = _battler.Hue;
            if (decimal.ToInt32(_hue.Value) != selectedHue)
            {
                _hue.Value = selectedHue;
                return;
            }
            await PreviewBattlerAsync().ConfigureAwait(true);
        };
        _assetMode.SelectedIndexChanged += async (_, _) =>
        {
            _battler.Category = SelectedBattlerCategory();
            await PreviewBattlerAsync().ConfigureAwait(true);
        };
        _hue.ValueChanged += async (_, _) =>
        {
            _battler.Hue = decimal.ToInt32(_hue.Value);
            await PreviewBattlerAsync().ConfigureAwait(true);
        };
    }

    public Control View => this;
    public bool OwnsTraits => false;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;
    public async ValueTask BindAsync(IReadOnlyList<DatabaseRecordHandle> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            if (records.Count != 1 || _document.Syntax.Resolve(records[0].Path) is not JsonObjectSyntax)
            {
                _recordId = 0;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0 ? "Select an enemy to edit it." : "The complete Enemy editor requires one populated record.";
                return;
            }
            Enabled = true;
            _recordId = records[0].Id;
            _catalog = await Phase9CProjectCatalogLoader.LoadAsync(_session, cancellationToken).ConfigureAwait(true);
            MvEnemyRecordSnapshot snapshot = Phase9CRecordReader.ReadEnemy(_document, _recordId);
            LoadSnapshot(snapshot);
            _selectionMessage.Text = $"Enemy {_recordId:D4} — battler, hue, parameters, rewards, drops, actions, traits, notes, and extension fields are preserved.";
            await PreviewBattlerAsync().ConfigureAwait(true);
        }
        finally
        {
            _loading = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewCancellation?.Cancel();
            _previewCancellation?.Dispose();
            _battlerPreview.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    private Task OpenSpriteEditorAsync(
        Func<DocumentRequest, Task> documentOpener,
        AssetCategory category,
        AssetIndexSelection selection)
    {
        if (string.IsNullOrWhiteSpace(selection.AssetName)) return Task.CompletedTask;
        string relativePath = AssetCategoryRegistry.BuildRuntimeRelativePath(new AssetKey(category, selection.AssetName), ".png");
        DocumentRequest request = new(
            EditorDocumentKind.ProjectFile,
            Identity: "sprite:" + relativePath,
            RelativePath: relativePath,
            DatabaseKind: MvDocumentKind.Enemies,
            RecordId: _recordId,
            DisplayTitle: Path.GetFileName(relativePath),
            EventSurface: "battlerName",
            EventPageIndex: selection.Index);
        return documentOpener(request);
    }

    private Control BuildGeneralPage()
    {
        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 470, 360, 260);
        TableLayoutPanel table = Phase9BEditorUi.Table();
        int row = 0;
        Phase9BEditorUi.AddRow(table, row++, "Name", _name);
        Phase9BEditorUi.AddRow(table, row++, "Asset Mode", _assetMode);
        Phase9BEditorUi.AddRow(table, row++, "Battler", _battler);
        Phase9BEditorUi.AddRow(table, row++, "Hue", _hue);
        Phase9BEditorUi.AddRow(table, row++, "Experience", _exp);
        Phase9BEditorUi.AddRow(table, row++, "Gold", _gold);
        Button apply = new() { Text = "Apply General Settings", AutoSize = true };
        apply.Click += async (_, _) => await ApplyGeneralAsync().ConfigureAwait(true);
        Phase9BEditorUi.AddRow(table, row, string.Empty, apply);
        split.Panel1.Controls.Add(Phase9BEditorUi.Scroll(table));
        split.Panel2.Controls.Add(_battlerPreview);
        return split;
    }

    private Control BuildParametersPage()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        ToolStrip tools = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton zero = new("Zero");
        ToolStripButton plus = new("+10");
        ToolStripButton scale = new("Scale 110%");
        ToolStripButton apply = new("Apply Parameters");
        zero.Click += (_, _) => TransformParameters(_ => 0);
        plus.Click += (_, _) => TransformParameters(value => value + 10);
        scale.Click += (_, _) => TransformParameters(value => (int)Math.Round(value * 1.1));
        apply.Click += async (_, _) => await ApplyParametersAsync().ConfigureAwait(true);
        tools.Items.AddRange([zero, plus, scale, new ToolStripSeparator(), apply]);
        panel.Controls.Add(_parameters);
        panel.Controls.Add(tools);
        return panel;
    }

    private Control BuildDropsPage()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        ToolStrip tools = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton add = new("Add Extended Drop");
        ToolStripButton edit = new("Edit");
        ToolStripButton clear = new("Clear Slot");
        ToolStripButton remove = new("Remove Extended Slot");
        ToolStripButton apply = new("Apply Drops");
        add.Click += (_, _) => AddDrop();
        edit.Click += (_, _) => EditDrop();
        clear.Click += (_, _) => ClearDrop();
        remove.Click += (_, _) => RemoveDrop();
        apply.Click += async (_, _) => await ApplyDropsAsync().ConfigureAwait(true);
        tools.Items.AddRange([add, edit, clear, remove, new ToolStripSeparator(), apply]);
        _drops.CellDoubleClick += (_, _) => EditDrop();
        panel.Controls.Add(_drops);
        panel.Controls.Add(tools);
        return panel;
    }

    private Control BuildActionsPage()
    {
        Panel panel = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        ToolStrip tools = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton add = new("Add");
        ToolStripButton edit = new("Edit");
        ToolStripButton duplicate = new("Duplicate");
        ToolStripButton remove = new("Remove");
        ToolStripButton up = new("Move Up");
        ToolStripButton down = new("Move Down");
        ToolStripButton apply = new("Apply Actions");
        add.Click += (_, _) => AddAction();
        edit.Click += (_, _) => EditAction();
        duplicate.Click += (_, _) => DuplicateAction();
        remove.Click += (_, _) => RemoveAction();
        up.Click += (_, _) => MoveAction(-1);
        down.Click += (_, _) => MoveAction(1);
        apply.Click += async (_, _) => await ApplyActionsAsync().ConfigureAwait(true);
        tools.Items.AddRange([add, edit, duplicate, remove, up, down, new ToolStripSeparator(), apply]);
        _actions.CellDoubleClick += (_, _) => EditAction();
        panel.Controls.Add(_actions);
        panel.Controls.Add(tools);
        return panel;
    }

    private Control BuildActionLabPage()
    {
        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Horizontal, 210, 170, 180);
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Turn", _scenarioTurn);
        Phase9BEditorUi.AddRow(table, 1, "Enemy HP (%)", _scenarioHp);
        Phase9BEditorUi.AddRow(table, 2, "Enemy MP (%)", _scenarioMp);
        Phase9BEditorUi.AddRow(table, 3, "Current MP", _scenarioCurrentMp);
        Phase9BEditorUi.AddRow(table, 4, "Highest Party Level", _scenarioLevel);
        Phase9BEditorUi.AddRow(table, 5, "Active States", _scenarioStates);
        Phase9BEditorUi.AddRow(table, 6, "ON Switches", _scenarioSwitches);
        Button evaluate = new() { Text = "Evaluate Action Rules", AutoSize = true };
        evaluate.Click += (_, _) => EvaluateActions();
        Phase9BEditorUi.AddRow(table, 7, string.Empty, evaluate);
        split.Panel1.Controls.Add(Phase9BEditorUi.Scroll(table));
        split.Panel2.Controls.Add(_evaluation);
        return split;
    }

    private Control BuildRewardsPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Victories", _rewardBattles);
        Phase9BEditorUi.AddRow(table, 1, "Options", _doubleDrops);
        Button calculate = new() { Text = "Calculate and Simulate", AutoSize = true };
        calculate.Click += (_, _) => CalculateRewards();
        Phase9BEditorUi.AddRow(table, 2, string.Empty, calculate);
        _rewardSummary.Height = 360;
        table.Controls.Add(_rewardSummary, 1, 3);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    private void InitializeParameterGrid()
    {
        _parameters.Columns[0].ReadOnly = true;
        _parameters.Columns[1].ValueType = typeof(int);
        _parameters.Rows.Clear();
        foreach (string name in ParameterNames) _parameters.Rows.Add(name, 0);
    }

    private void LoadSnapshot(MvEnemyRecordSnapshot snapshot)
    {
        _name.Text = snapshot.Name;
        bool hasFront = _session.Assets.Find(new AssetKey(AssetCategory.Enemies, snapshot.BattlerName)) is not null;
        bool hasSide = _session.Assets.Find(new AssetKey(AssetCategory.SideViewEnemies, snapshot.BattlerName)) is not null;
        Phase9BEditorUi.SelectOption(_assetMode, !hasFront && hasSide ? 1 : 0);
        _battler.Category = SelectedBattlerCategory();
        _battler.Selection = new AssetIndexSelection(snapshot.BattlerName, 0);
        _battler.Hue = snapshot.BattlerHue;
        Phase9BEditorUi.Set(_hue, snapshot.BattlerHue);
        Phase9BEditorUi.Set(_exp, snapshot.Experience);
        Phase9BEditorUi.Set(_gold, snapshot.Gold);
        for (int index = 0; index < 8; index++) _parameters.Rows[index].Cells[1].Value = index < snapshot.Parameters.Count ? snapshot.Parameters[index] : 0;
        _dropItems.Clear();
        _dropItems.AddRange(snapshot.Drops);
        while (_dropItems.Count < 3) _dropItems.Add(new MvEnemyDropSnapshot(0, 1, 1, "{}"));
        _actionItems.Clear();
        _actionItems.AddRange(snapshot.Actions);
        _scenarioHp.Value = 100;
        _scenarioMp.Value = 100;
        _scenarioCurrentMp.Value = snapshot.Parameters.Count > 1 ? Math.Max(0, snapshot.Parameters[1]) : 0;
        _scenarioLevel.Value = 1;
        RefreshDrops();
        RefreshActions();
        EvaluateActions();
        CalculateRewards();
    }

    private async Task ApplyGeneralAsync()
    {
        if (_recordId <= 0) return;
        try
        {
            JsonPath root = JsonPath.Root.Append(_recordId);
            List<IEditorCommand> commands =
            [
                new SetJsonStringCommand(_document.RelativePath, root.Append("name"), _name.Text, "Set enemy name"),
                new SetJsonStringCommand(_document.RelativePath, root.Append("battlerName"), _battler.Selection.AssetName, "Set enemy battler"),
                new SetJsonInt32Command(_document.RelativePath, root.Append("battlerHue"), decimal.ToInt32(_hue.Value), "Set enemy hue"),
                new SetJsonInt32Command(_document.RelativePath, root.Append("exp"), decimal.ToInt32(_exp.Value), "Set enemy experience"),
                new SetJsonInt32Command(_document.RelativePath, root.Append("gold"), decimal.ToInt32(_gold.Value), "Set enemy gold")
            ];
            await _mutations.ExecuteAsync(new CompositeEditorCommand($"Change enemy {_recordId} general settings", commands)).ConfigureAwait(true);
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyParametersAsync()
    {
        try { await _phase9C.SetEnemyParametersAsync(_document.RelativePath, _recordId, ReadParameterValues()).ConfigureAwait(true); }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyDropsAsync()
    {
        try { await _phase9C.SetEnemyDropsAsync(_document.RelativePath, _recordId, _dropItems).ConfigureAwait(true); }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyActionsAsync()
    {
        try { await _phase9C.SetEnemyActionsAsync(_document.RelativePath, _recordId, _actionItems).ConfigureAwait(true); }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private IReadOnlyList<int> ReadParameterValues()
    {
        List<int> values = [];
        for (int index = 0; index < 8; index++)
        {
            values.Add(int.TryParse(Convert.ToString(_parameters.Rows[index].Cells[1].Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0);
        }
        return values;
    }

    private void TransformParameters(Func<int, int> transform)
    {
        IReadOnlyList<int> values = ReadParameterValues();
        for (int index = 0; index < 8; index++) _parameters.Rows[index].Cells[1].Value = transform(values[index]);
    }

    private void RefreshDrops()
    {
        _drops.Rows.Clear();
        for (int index = 0; index < _dropItems.Count; index++)
        {
            MvEnemyDropSnapshot drop = _dropItems[index];
            string kind = drop.Kind switch { 1 => "Item", 2 => "Weapon", 3 => "Armor", _ => "None" };
            string chance = drop.Kind == 0 ? "—" : $"1/{Math.Max(1, drop.Denominator)} ({drop.BaseProbability:P4})";
            _drops.Rows.Add(index < 3 ? $"Standard {index + 1}" : $"Extended {index + 1}", kind, _catalog.ResolveDrop(drop.Kind, drop.DataId), chance, drop.RawJson == "{}" ? string.Empty : "preserved");
        }
    }

    private void AddDrop()
    {
        using EnemyDropEditDialog dialog = new(_catalog, new MvEnemyDropSnapshot(0, 1, 1, "{}"));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _dropItems.Add(dialog.Result);
        RefreshDrops();
    }

    private void EditDrop()
    {
        int index = SelectedRow(_drops);
        if (index < 0 || index >= _dropItems.Count) return;
        using EnemyDropEditDialog dialog = new(_catalog, _dropItems[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _dropItems[index] = dialog.Result;
        RefreshDrops();
        _drops.Rows[index].Selected = true;
    }

    private void ClearDrop()
    {
        int index = SelectedRow(_drops);
        if (index < 0 || index >= _dropItems.Count) return;
        _dropItems[index] = _dropItems[index] with { Kind = 0, DataId = 1, Denominator = 1 };
        RefreshDrops();
    }

    private void RemoveDrop()
    {
        int index = SelectedRow(_drops);
        if (index < 3 || index >= _dropItems.Count)
        {
            MessageBox.Show(this, "The three standard MV drop slots are retained. Clear a standard slot instead of removing it.", "Enemy Drops", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _dropItems.RemoveAt(index);
        RefreshDrops();
    }

    private void RefreshActions()
    {
        _actions.Rows.Clear();
        IReadOnlyList<EnemyActionEvaluation> evaluation = EnemyActionRuleEvaluator.Evaluate(_actionItems, ReadScenario());
        for (int index = 0; index < _actionItems.Count; index++)
        {
            MvEnemyActionSnapshot action = _actionItems[index];
            EnemyActionEvaluation result = evaluation[index];
            _actions.Rows.Add(index + 1, _catalog.Shared.Resolve(ReferenceTargetKind.Skill, action.SkillId), FormatCondition(action), action.Rating, result.Weight, result.Probability.ToString("P2", CultureInfo.InvariantCulture));
        }
    }

    private void AddAction()
    {
        using EnemyActionEditDialog dialog = new(_catalog, new MvEnemyActionSnapshot(1, 0, 0, 0, 5, "{}"));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _actionItems.Add(dialog.Result);
        RefreshActions();
    }

    private void EditAction()
    {
        int index = SelectedRow(_actions);
        if (index < 0 || index >= _actionItems.Count) return;
        using EnemyActionEditDialog dialog = new(_catalog, _actionItems[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _actionItems[index] = dialog.Result;
        RefreshActions();
        _actions.Rows[index].Selected = true;
    }

    private void DuplicateAction()
    {
        int index = SelectedRow(_actions);
        if (index < 0 || index >= _actionItems.Count) return;
        MvEnemyActionSnapshot source = _actionItems[index];
        _actionItems.Insert(index + 1, source with { RawJson = source.RawJson });
        RefreshActions();
    }

    private void RemoveAction()
    {
        int index = SelectedRow(_actions);
        if (index < 0 || index >= _actionItems.Count) return;
        _actionItems.RemoveAt(index);
        RefreshActions();
    }

    private void MoveAction(int delta)
    {
        int index = SelectedRow(_actions);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _actionItems.Count) return;
        (_actionItems[index], _actionItems[target]) = (_actionItems[target], _actionItems[index]);
        RefreshActions();
        _actions.Rows[target].Selected = true;
    }

    private void EvaluateActions()
    {
        IReadOnlyList<EnemyActionEvaluation> results = EnemyActionRuleEvaluator.Evaluate(_actionItems, ReadScenario());
        _evaluation.Rows.Clear();
        foreach (EnemyActionEvaluation result in results)
        {
            _evaluation.Rows.Add(
                _catalog.Shared.Resolve(ReferenceTargetKind.Skill, result.Action.SkillId),
                result.ConditionMet,
                result.Affordable,
                result.IncludedByRating,
                result.Weight,
                result.Probability.ToString("P2", CultureInfo.InvariantCulture),
                result.Explanation);
        }
        RefreshActions();
    }

    private EnemyActionScenario ReadScenario()
    {
        HashSet<int> states = ParseIds(_scenarioStates.Text);
        Dictionary<int, bool> switches = ParseIds(_scenarioSwitches.Text).ToDictionary(id => id, _ => true);
        IReadOnlyDictionary<int, int> costs = _catalog.SkillMpCosts;
        return new EnemyActionScenario(
            decimal.ToInt32(_scenarioTurn.Value),
            decimal.ToDouble(_scenarioHp.Value) / 100d,
            decimal.ToDouble(_scenarioMp.Value) / 100d,
            states,
            decimal.ToInt32(_scenarioLevel.Value),
            switches,
            decimal.ToInt32(_scenarioCurrentMp.Value),
            costs);
    }

    private void CalculateRewards()
    {
        if (_recordId <= 0) return;
        int battles = decimal.ToInt32(_rewardBattles.Value);
        MvEnemyRecordSnapshot snapshot = new(
            _recordId,
            _name.Text,
            _battler.Selection.AssetName,
            decimal.ToInt32(_hue.Value),
            ReadParameterValues(),
            decimal.ToInt32(_exp.Value),
            decimal.ToInt32(_gold.Value),
            _dropItems.ToArray(),
            _actionItems.ToArray(),
            0,
            string.Empty,
            new Dictionary<string, string>());
        EnemyRewardSummary summary = EnemyRewardCalculator.CalculateEnemy(snapshot, battles, _doubleDrops.Checked);
        IReadOnlyDictionary<(int Kind, int DataId), int> simulation = EnemyRewardCalculator.SimulateDrops(snapshot.Drops, battles, seed: 12345, doubleDrops: _doubleDrops.Checked);
        StringBuilder text = new();
        text.AppendLine($"EXP per victory:  {summary.Experience:N0}");
        text.AppendLine($"Gold per victory: {summary.Gold:N0}");
        text.AppendLine($"Victories:        {battles:N0}");
        text.AppendLine($"Total EXP:        {(long)summary.Experience * battles:N0}");
        text.AppendLine($"Total Gold:       {(long)summary.Gold * battles:N0}");
        text.AppendLine();
        foreach (DropExpectation drop in summary.Drops)
        {
            simulation.TryGetValue((drop.Drop.Kind, drop.Drop.DataId), out int actual);
            text.AppendLine(_catalog.ResolveDrop(drop.Drop.Kind, drop.Drop.DataId));
            text.AppendLine($"  Chance/battle: {drop.Probability:P4}");
            text.AppendLine($"  Expected:      {drop.ExpectedPerBattle:N2}");
            text.AppendLine($"  Simulated:     {actual:N0} (seed 12345)");
            text.AppendLine($"  ≥1 copy:       {drop.ProbabilityAtLeastOne:P4}");
        }
        _rewardSummary.Text = text.ToString();
    }

    private AssetCategory SelectedBattlerCategory() =>
        Phase9BEditorUi.SelectedOption(_assetMode) == 1 ? AssetCategory.SideViewEnemies : AssetCategory.Enemies;

    private async Task PreviewBattlerAsync()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _previewCancellation.Token;
        Bitmap? replacement = null;
        try
        {
            string name = _battler.Selection.AssetName;
            if (string.IsNullOrWhiteSpace(name)) { ReplacePreview(null); return; }
            AssetCategory category = SelectedBattlerCategory();
            AssetRecord? record = _session.Assets.Find(new AssetKey(category, name));
            AssetVariant? variant = record?.PreferredVariant;
            if (variant is null || variant.IsEncrypted) { ReplacePreview(null); return; }
            using Bitmap source = await AssetImageLoader.LoadOwnedBitmapAsync(_session.AssetContent, variant.RelativePath, cancellationToken).ConfigureAwait(true);
            replacement = ApplyHue(source, decimal.ToInt32(_hue.Value));
            cancellationToken.ThrowIfCancellationRequested();
            ReplacePreview(replacement);
            replacement = null;
        }
        catch (OperationCanceledException) { replacement?.Dispose(); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            replacement?.Dispose();
            _battlerPreview.Image?.Dispose();
            _battlerPreview.Image = null;
            _selectionMessage.Text = $"Battler preview: {exception.Message}";
        }
    }

    private void ReplacePreview(Bitmap? image)
    {
        Image? previous = _battlerPreview.Image;
        _battlerPreview.Image = image;
        previous?.Dispose();
    }

    internal static Bitmap ApplyHue(Bitmap source, int hueDegrees)
    {
        hueDegrees %= 360;
        Bitmap output = new(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                Color color = source.GetPixel(x, y);
                if (color.A == 0) { output.SetPixel(x, y, color); continue; }
                float hue = (color.GetHue() + hueDegrees) % 360f;
                output.SetPixel(x, y, FromAhsb(color.A, hue, color.GetSaturation(), color.GetBrightness()));
            }
        }
        return output;
    }

    private static Color FromAhsb(int alpha, float hue, float saturation, float brightness)
    {
        if (saturation <= 0.0001f)
        {
            int gray = Math.Clamp((int)Math.Round(brightness * 255f), 0, 255);
            return Color.FromArgb(alpha, gray, gray, gray);
        }
        double h = hue / 60d;
        int sector = (int)Math.Floor(h) % 6;
        double fraction = h - Math.Floor(h);
        double value = brightness <= 0.5f ? brightness * (1d + saturation) : brightness + saturation - brightness * saturation;
        double minimum = 2d * brightness - value;
        double middleUp = minimum + (value - minimum) * fraction;
        double middleDown = value - (value - minimum) * fraction;
        (double r, double g, double b) = sector switch
        {
            0 => (value, middleUp, minimum),
            1 => (middleDown, value, minimum),
            2 => (minimum, value, middleUp),
            3 => (minimum, middleDown, value),
            4 => (middleUp, minimum, value),
            _ => (value, minimum, middleDown)
        };
        return Color.FromArgb(alpha, ToByte(r), ToByte(g), ToByte(b));
    }

    private static int ToByte(double value) => Math.Clamp((int)Math.Round(value * 255d), 0, 255);

    private static string FormatCondition(MvEnemyActionSnapshot action) => action.ConditionType switch
    {
        0 => "Always",
        1 => $"Turn {(int)action.ConditionParam1} + {(int)action.ConditionParam2}×X",
        2 => $"HP {action.ConditionParam1:P0}–{action.ConditionParam2:P0}",
        3 => $"MP {action.ConditionParam1:P0}–{action.ConditionParam2:P0}",
        4 => $"State #{(int)action.ConditionParam1}",
        5 => $"Party Level ≥ {(int)action.ConditionParam1}",
        6 => $"Switch #{(int)action.ConditionParam1} ON",
        _ => $"Unknown condition {action.ConditionType}"
    };

    private static HashSet<int> ParseIds(string text) => text
        .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ? id : 0)
        .Where(id => id > 0)
        .ToHashSet();

    private static int SelectedRow(DataGridView grid) => grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Index : grid.CurrentCell?.RowIndex ?? -1;

    internal static DataGridView Grid(params string[] columns)
    {
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        foreach (string column in columns) grid.Columns.Add(column.Replace(" ", string.Empty), column);
        return grid;
    }
}

public sealed class TroopEditorControl : UserControl, IDatabaseEditorBody, IDatabasePendingEditSource
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly Phase9CMutationService _phase9C;
    private readonly Func<DocumentRequest, Task>? _documentOpener;
    private readonly Label _selectionMessage = Phase9BEditorUi.SelectionLabel();
    private const string TroopPageClipboardFormat = "Playroma.JRPG.TroopEventPage";
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TroopFormationCanvas _canvas = new() { Dock = DockStyle.Fill, AccessibleName = "Troop formation battlefield" };
    private readonly DataGridView _members = EnemyEditorControl.Grid("#", "Enemy", "X", "Y", "Hidden", "References");
    private readonly DataGridView _enemyPalette = EnemyEditorControl.Grid("ID", "Enemy");
    private readonly DataGridView _pages = EnemyEditorControl.Grid("Page", "Conditions", "Span", "Commands", "Warnings");
    private readonly TabControl _battlePageTabs = new() { Dock = DockStyle.Top, Height = 30, AccessibleName = "Battle event pages" };
    private readonly TextBox _battleConditions = new() { Dock = DockStyle.Fill, ReadOnly = true, AccessibleName = "Battle event conditions" };
    private readonly ComboBox _battleSpan = Phase9BEditorUi.OptionCombo(new Dictionary<int, string> { [0] = "Battle", [1] = "Turn", [2] = "Moment" });
    private readonly DataGridView _battleCommands = EnemyEditorControl.Grid("#", "Contents", "Code");
    private readonly EventCommandRegistry _eventRegistry;
    private readonly EventStructureProjector _eventProjector = new();
    private readonly EventCommandFormatter _eventFormatter;
    private readonly NumericUpDown _logicalWidth = Phase9BEditorUi.Number(320, 4096);
    private readonly NumericUpDown _logicalHeight = Phase9BEditorUi.Number(240, 4096);
    private readonly TextBox _rewardSummary = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9) };
    private readonly List<MvTroopMemberSnapshot> _memberItems = [];
    private readonly List<MvTroopPageSnapshot> _pageItems = [];
    private readonly Dictionary<int, Bitmap> _memberImages = [];
    private readonly Dictionary<int, string> _memberImageFailures = [];
    private Bitmap? _battleback1Image;
    private Bitmap? _battleback2Image;
    private string _battleback1Name = string.Empty;
    private string _battleback2Name = string.Empty;
    private Phase9CProjectCatalog _catalog = Phase9CProjectCatalog.Empty;
    private MvTroopRecordSnapshot? _snapshot;
    private int _recordId;
    private bool _loading;
    private bool _pendingCanvasChanges;
    private bool _pendingPageChanges;
    private bool _pendingNameChanges;
    private bool _syncingMemberSelection;
    private bool _syncingBattlePageUi;

    public TroopEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        Func<DocumentRequest, Task>? documentOpener = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        ArgumentNullException.ThrowIfNull(definition);
        _phase9C = new Phase9CMutationService(session.Editing);
        _documentOpener = documentOpener;
        _eventRegistry = EventCommandRegistry.CreateBuiltIn();
        _eventFormatter = new EventCommandFormatter(_eventRegistry);
        Dock = DockStyle.Fill;
        _members.MultiSelect = true;
        _enemyPalette.MultiSelect = false;
        _enemyPalette.AccessibleName = "Enemy palette";
        _battleCommands.MultiSelect = false;
        _battleCommands.AccessibleName = "Battle event commands";
        _battleCommands.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _battleCommands.Columns[0].Width = 58;
        _battleCommands.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _battleCommands.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _battleCommands.Columns[2].Width = 64;
        _enemyPalette.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _enemyPalette.Columns[0].Width = 58;
        _enemyPalette.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        Phase9BEditorUi.Set(_logicalWidth, BattleCanvasProfile.MvDefault.LogicalWidth);
        Phase9BEditorUi.Set(_logicalHeight, BattleCanvasProfile.MvDefault.LogicalHeight);
        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("Troop") { Controls = { BuildFormationPage() } });
        tabs.TabPages.Add(new TabPage("Rewards") { Controls = { BuildRewardsPage() } });
        tabs.TabPages.Add(new TabPage("Battle Test") { Controls = { BuildBattleTestPage() } });
        Controls.Add(tabs);
        Controls.Add(_selectionMessage);
        _canvas.SelectionChanged += (_, _) => SyncMemberSelectionFromCanvas();
        _canvas.MembersChanged += async (_, e) =>
        {
            ReplaceMembers(e.Members);
            SetPendingCanvas(true);
            if (e.CommitRequested) await CommitFormationAsync().ConfigureAwait(true);
        };
        _members.SelectionChanged += (_, _) => SyncCanvasSelectionFromGrid();
        _members.CellDoubleClick += (_, _) => EditSelectedMember();
        _enemyPalette.CellDoubleClick += async (_, _) => await AddSelectedPaletteEnemyAsync().ConfigureAwait(true);
        _battlePageTabs.SelectedIndexChanged += (_, _) => RefreshBattleEventEditor();
        _battleSpan.SelectedIndexChanged += (_, _) => BattleSpanChanged();
        _battleCommands.CellDoubleClick += async (_, _) => await OpenPageCommandsAsync().ConfigureAwait(true);
        _logicalWidth.ValueChanged += (_, _) => UpdateCanvasProfile();
        _logicalHeight.ValueChanged += (_, _) => UpdateCanvasProfile();
        _name.TextChanged += (_, _) => { if (!_loading) SetPendingName(true); };
    }

    public Control View => this;
    public bool OwnsTraits => false;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;
    public bool HasPendingChanges => _pendingCanvasChanges || _pendingPageChanges || _pendingNameChanges;
    public event EventHandler? PendingChangesChanged;

    public async ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pendingNameChanges) await ApplyNameAsync().ConfigureAwait(true);
        if (_pendingCanvasChanges) await CommitFormationAsync().ConfigureAwait(true);
        if (_pendingPageChanges) await ApplyPagesAsync().ConfigureAwait(true);
    }

    public async ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_recordId <= 0) return;
        await BindAsync(
            [new DatabaseRecordHandle(MvDocumentKind.Troops, _document.RelativePath, _recordId)],
            cancellationToken).ConfigureAwait(true);
    }

    public async ValueTask BindAsync(IReadOnlyList<DatabaseRecordHandle> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            // Detach the canvas from the previous troop's GDI+ resources before the
            // asynchronous rebind disposes them. Otherwise queued paints can retain
            // references to disposed Bitmap instances and image.Width throws
            // "Parameter is not valid" while the new troop is loading.
            _canvas.ClearVisuals();
            DisposeMemberImages();
            DisposeBattlebackImages();
            if (records.Count != 1 || _document.Syntax.Resolve(records[0].Path) is not JsonObjectSyntax)
            {
                _recordId = 0;
                _snapshot = null;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0 ? "Select a troop to edit it." : "The complete Troop editor requires one populated record.";
                return;
            }
            Enabled = true;
            _recordId = records[0].Id;
            _catalog = await Phase9CProjectCatalogLoader.LoadAsync(_session, cancellationToken).ConfigureAwait(true);
            _snapshot = Phase9CRecordReader.ReadTroop(_document, _recordId);
            _name.Text = _snapshot.Name;
            _memberItems.Clear();
            _memberItems.AddRange(_snapshot.Members);
            _pageItems.Clear();
            _pageItems.AddRange(_snapshot.Pages);
            SetPendingCanvas(false);
            _pendingPageChanges = false;
            _pendingNameChanges = false;
            PendingChangesChanged?.Invoke(this, EventArgs.Empty);
            await LoadMemberImagesAsync(cancellationToken).ConfigureAwait(true);
            await LoadBattlebacksAsync(cancellationToken).ConfigureAwait(true);
            RefreshEnemyPalette();
            RefreshMembers();
            RefreshPages();
            RefreshCanvas();
            CalculateRewards();
            int distinctBattlers = _memberItems.Select(member => member.EnemyId).Distinct().Count();
            string artworkStatus = distinctBattlers == 0
                ? "No troop members."
                : $"Battler artwork: {_memberImages.Count:N0}/{distinctBattlers:N0} loaded.";
            if (_memberImageFailures.Count > 0)
            {
                string failures = string.Join("; ", _memberImageFailures
                    .OrderBy(pair => pair.Key)
                    .Take(3)
                    .Select(pair => $"{_catalog.Shared.Resolve(ReferenceTargetKind.Enemy, pair.Key)}: {pair.Value}"));
                if (_memberImageFailures.Count > 3) failures += $"; +{_memberImageFailures.Count - 3} more";
                artworkStatus += " " + failures;
            }
            _selectionMessage.Text = $"Troop {_recordId:D4} — member order is gameplay-significant. {artworkStatus}";
        }
        finally { _loading = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeMemberImages();
            DisposeBattlebackImages();
        }
        base.Dispose(disposing);
    }

    private Control BuildFormationPage()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 46));

        TableLayoutPanel general = new() { Dock = DockStyle.Fill, ColumnCount = 6, Padding = new Padding(8) };
        general.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int index = 2; index < 6; index++) general.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Label nameLabel = new() { Text = "Name", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) };
        Button autoName = new() { Text = "Auto-name", AutoSize = true };
        Button changeBackground = new() { Text = "Change BG…", AutoSize = true, AccessibleName = "Change troop battle background" };
        Button battleTest = new() { Text = "Battle Test…", AutoSize = true };
        Button applyName = new() { Text = "Apply Name", AutoSize = true };
        autoName.Click += (_, _) => _name.Text = GenerateTroopName();
        changeBackground.Click += async (_, _) => await ChangeBattleBackgroundAsync().ConfigureAwait(true);
        battleTest.Click += async (_, _) => await ShowBattleTestProfileAsync().ConfigureAwait(true);
        applyName.Click += async (_, _) => await ApplyNameAsync().ConfigureAwait(true);
        general.Controls.Add(nameLabel, 0, 0);
        general.Controls.Add(_name, 1, 0);
        general.Controls.Add(autoName, 2, 0);
        general.Controls.Add(changeBackground, 3, 0);
        general.Controls.Add(battleTest, 4, 0);
        general.Controls.Add(applyName, 5, 0);
        GroupBox generalGroup = new() { Text = "General Settings", Dock = DockStyle.Fill, Padding = new Padding(4) };
        generalGroup.Controls.Add(general);
        root.Controls.Add(generalGroup, 0, 0);

        SplitContainer formation = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 650, 420, 250, splitterWidth: 6);
        Panel canvasPanel = new() { Dock = DockStyle.Fill };
        ToolStrip canvasTools = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton editMember = new("Edit Member");
        ToolStripButton duplicate = new("Duplicate");
        ToolStripButton hidden = new("Toggle Hidden");
        ToolStripDropDownButton presets = new("Formation");
        foreach (TroopFormationPreset preset in Enum.GetValues<TroopFormationPreset>())
        {
            ToolStripMenuItem item = new(preset.ToString()) { Tag = preset };
            item.Click += (_, _) => ApplyFormation((TroopFormationPreset)(item.Tag ?? TroopFormationPreset.HorizontalLine));
            presets.DropDownItems.Add(item);
        }
        ToolStripButton applyFormation = new("Apply Formation");
        editMember.Click += (_, _) => EditSelectedMember();
        duplicate.Click += (_, _) => DuplicateSelectedMembers();
        hidden.Click += (_, _) => ToggleHidden();
        applyFormation.Click += async (_, _) => await CommitFormationAsync().ConfigureAwait(true);
        canvasTools.Items.AddRange([
            editMember, duplicate, hidden, presets, new ToolStripSeparator(), applyFormation,
            new ToolStripSeparator(), new ToolStripLabel("Canvas:"),
            new ToolStripControlHost(_logicalWidth) { AutoSize = false, Width = 76 },
            new ToolStripLabel("×"),
            new ToolStripControlHost(_logicalHeight) { AutoSize = false, Width = 76 }
        ]);
        canvasPanel.Controls.Add(_canvas);
        canvasPanel.Controls.Add(canvasTools);
        formation.Panel1.Controls.Add(canvasPanel);

        TableLayoutPanel palette = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(6) };
        palette.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        palette.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        palette.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        TabControl lists = new() { Dock = DockStyle.Fill };
        lists.TabPages.Add(new TabPage("Enemies") { Controls = { _enemyPalette } });
        lists.TabPages.Add(new TabPage("Members") { Controls = { _members } });
        FlowLayoutPanel paletteButtons = new() { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Button add = new() { Text = "< Add", Width = 118 };
        Button remove = new() { Text = "Remove >", Width = 118 };
        Button clear = new() { Text = "Clear", Width = 118 };
        Button align = new() { Text = "Align", Width = 118 };
        add.Click += async (_, _) => await AddSelectedPaletteEnemyAsync().ConfigureAwait(true);
        remove.Click += async (_, _) => await RemoveSelectedMembersAsync().ConfigureAwait(true);
        clear.Click += async (_, _) => await ClearMembersAsync().ConfigureAwait(true);
        align.Click += (_, _) => ApplyFormation(TroopFormationPreset.HorizontalLine);
        paletteButtons.Controls.AddRange([add, remove, clear, align]);
        palette.Controls.Add(paletteButtons, 0, 0);
        palette.Controls.Add(lists, 1, 0);
        formation.Panel2.Controls.Add(palette);
        GroupBox formationGroup = new() { Text = "Battle Formation", Dock = DockStyle.Fill, Padding = new Padding(6) };
        formationGroup.Controls.Add(formation);
        root.Controls.Add(formationGroup, 0, 1);

        root.Controls.Add(BuildPagesPage(), 0, 2);
        return root;
    }

    private Control BuildPagesPage()
    {
        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 120, 96, 360, splitterWidth: 6);
        FlowLayoutPanel pageButtons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(4)
        };
        Button add = PageButton("New Event Page");
        Button copy = PageButton("Copy Event Page");
        Button paste = PageButton("Paste Event Page");
        Button remove = PageButton("Delete Event Page");
        Button clear = PageButton("Clear Event Page");
        add.Click += (_, _) => AddPage();
        copy.Click += (_, _) => CopyPage();
        paste.Click += (_, _) => PastePage();
        remove.Click += (_, _) => RemovePage();
        clear.Click += (_, _) => ClearPageCommands();
        pageButtons.Controls.AddRange([add, copy, paste, remove, clear]);
        split.Panel1.Controls.Add(pageButtons);

        Panel editor = new() { Dock = DockStyle.Fill, Padding = new Padding(4) };
        TableLayoutPanel settings = new() { Dock = DockStyle.Top, Height = 42, ColumnCount = 5 };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Button editConditions = new() { Text = "…", Width = 34, AccessibleName = "Edit battle event conditions" };
        Button applyPages = new() { Text = "Apply Pages", AutoSize = true };
        editConditions.Click += (_, _) => EditPage();
        applyPages.Click += async (_, _) => await ApplyPagesAsync().ConfigureAwait(true);
        settings.Controls.Add(new Label { Text = "Conditions:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        settings.Controls.Add(_battleConditions, 1, 0);
        settings.Controls.Add(editConditions, 2, 0);
        settings.Controls.Add(new Label { Text = "Span:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 4, 3) }, 3, 0);
        settings.Controls.Add(_battleSpan, 4, 0);

        ToolStrip commandTools = new() { Dock = DockStyle.Bottom, GripStyle = ToolStripGripStyle.Hidden };
        ToolStripButton commandEdit = new("Edit Commands…");
        ToolStripButton commandClear = new("Clear Commands");
        ToolStripButton waves = new("Compile Hidden as Wave 2");
        ToolStripControlHost applyHost = new(applyPages);
        commandEdit.Click += async (_, _) => await OpenPageCommandsAsync().ConfigureAwait(true);
        commandClear.Click += (_, _) => ClearPageCommands();
        waves.Click += (_, _) => CompileHiddenAsSecondWave();
        commandTools.Items.AddRange([commandEdit, commandClear, waves, new ToolStripSeparator(), applyHost]);

        editor.Controls.Add(_battleCommands);
        editor.Controls.Add(commandTools);
        editor.Controls.Add(settings);
        editor.Controls.Add(_battlePageTabs);
        split.Panel2.Controls.Add(editor);
        GroupBox group = new() { Text = "Battle Event", Dock = DockStyle.Fill, Padding = new Padding(6) };
        group.Controls.Add(split);
        return group;
    }

    private static Button PageButton(string text) => new() { Text = text, Width = 104, Height = 42, Margin = new Padding(2, 3, 2, 3) };

    private Control BuildRewardsPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Button calculate = new() { Text = "Calculate Troop Rewards", AutoSize = true };
        calculate.Click += async (_, _) => { await ReloadEnemyDataAndCalculateAsync().ConfigureAwait(true); };
        Phase9BEditorUi.AddRow(table, 0, string.Empty, calculate);
        _rewardSummary.Height = 420;
        table.Controls.Add(_rewardSummary, 1, 1);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    private Control BuildBattleTestPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Label explanation = new()
        {
            Text = "Battle-test configuration belongs to System.json, not Troops.json. The profile editor updates the standard test troop, test party, and battleback fields without extending Troops.json.",
            AutoSize = true,
            MaximumSize = new Size(500, 0)
        };
        Phase9BEditorUi.AddRow(table, 0, "Architecture", explanation);
        Button configure = new() { Text = "Prepare Battle Test Profile…", AutoSize = true };
        configure.Click += async (_, _) => await ShowBattleTestProfileAsync().ConfigureAwait(true);
        Phase9BEditorUi.AddRow(table, 1, string.Empty, configure);
        return Phase9BEditorUi.Scroll(table);
    }

    private void RefreshMembers()
    {
        TroopMemberReferenceReport references = _snapshot is null ? new TroopMemberReferenceReport([]) : TroopMemberReferenceAnalyzer.Analyze(_snapshot with { Members = _memberItems.ToArray(), Pages = _pageItems.ToArray() });
        _syncingMemberSelection = true;
        try
        {
            _members.Rows.Clear();
            foreach (MvTroopMemberSnapshot member in _memberItems)
            {
                int count = references.References.Count(reference => reference.MemberIndex == member.Index);
                _members.Rows.Add(member.Index + 1, _catalog.Shared.Resolve(ReferenceTargetKind.Enemy, member.EnemyId), member.X, member.Y, member.Hidden, count);
            }
        }
        finally
        {
            _syncingMemberSelection = false;
        }
    }

    private void RefreshEnemyPalette()
    {
        _enemyPalette.Rows.Clear();
        foreach (DatabaseChoice enemy in _catalog.Shared.Enemies)
        {
            _enemyPalette.Rows.Add(enemy.Id.ToString("D4", CultureInfo.InvariantCulture), enemy.DisplayName);
        }
        if (_enemyPalette.Rows.Count > 0)
        {
            _enemyPalette.Rows[0].Selected = true;
            _enemyPalette.CurrentCell = _enemyPalette.Rows[0].Cells[1];
        }
    }

    private void RefreshPages()
    {
        int selected = SelectedPageIndex();
        _pages.Rows.Clear();
        foreach (MvTroopPageSnapshot page in _pageItems)
        {
            List<string> warnings = [];
            if (!page.Conditions.HasAnyCondition) warnings.Add("will not run");
            if (page.Span == (int)TroopPageSpan.Moment) warnings.Add("Moment repeat risk");
            _pages.Rows.Add(page.Index + 1, FormatConditions(page.Conditions), ((TroopPageSpan)page.Span).ToString(), page.CommandCount, string.Join(", ", warnings));
        }

        _syncingBattlePageUi = true;
        try
        {
            _battlePageTabs.TabPages.Clear();
            for (int index = 0; index < _pageItems.Count; index++)
            {
                _battlePageTabs.TabPages.Add(new TabPage((index + 1).ToString(CultureInfo.InvariantCulture)));
            }
            if (_battlePageTabs.TabPages.Count > 0)
            {
                _battlePageTabs.SelectedIndex = Math.Clamp(selected < 0 ? 0 : selected, 0, _battlePageTabs.TabPages.Count - 1);
            }
        }
        finally
        {
            _syncingBattlePageUi = false;
        }
        RefreshBattleEventEditor();
    }

    private void RefreshCanvas()
    {
        Dictionary<int, string> names = _memberItems.Select(member => member.EnemyId).Distinct().ToDictionary(enemyId => enemyId, enemyId => _catalog.Shared.Resolve(ReferenceTargetKind.Enemy, enemyId));
        Dictionary<int, Bitmap> images = _memberItems
            .Where(member => _memberImages.ContainsKey(member.EnemyId))
            .ToDictionary(member => member.Index, member => _memberImages[member.EnemyId]);
        _canvas.SetMembers(_memberItems.ToArray(), names, images);
    }

    private void ReplaceMembers(IReadOnlyList<MvTroopMemberSnapshot> members)
    {
        _memberItems.Clear();
        _memberItems.AddRange(members.OrderBy(member => member.Index));
        RefreshMembers();
        SyncMemberSelectionFromCanvas();
    }

    private async Task LoadMemberImagesAsync(CancellationToken cancellationToken)
    {
        DisposeMemberImages();
        _memberImageFailures.Clear();
        MvDatabaseFileSummary? enemySummary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.Enemies);
        if (enemySummary is null) return;
        await using Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(enemySummary.RelativePath, cancellationToken).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument enemies) return;
        foreach (int enemyId in _memberItems.Select(member => member.EnemyId).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            (Bitmap? image, string? failure) = await LoadEnemyBattlerBitmapAsync(enemies, enemyId, cancellationToken).ConfigureAwait(true);
            if (image is not null)
            {
                _memberImages[enemyId] = image;
            }
            else
            {
                _memberImageFailures[enemyId] = string.IsNullOrWhiteSpace(failure) ? "image unavailable" : failure;
            }
        }
    }

    private async ValueTask<(Bitmap? Image, string? Failure)> LoadEnemyBattlerBitmapAsync(
        MutableJsonProjectDocument enemies,
        int enemyId,
        CancellationToken cancellationToken)
    {
        JsonPath root = JsonPath.Root.Append(enemyId);
        string rawName = enemies.GetString(root.Append("battlerName")) ?? string.Empty;
        int hue = enemies.GetInt32(root.Append("battlerHue")) ?? 0;
        string logicalName = NormalizeBattlerLogicalName(rawName);
        if (string.IsNullOrWhiteSpace(logicalName)) return (null, "no battler is assigned");

        IReadOnlyList<(AssetRecord Record, AssetVariant Variant)> candidates = FindReadableBattlerCandidates(logicalName);
        if (candidates.Count == 0) return (null, $"asset '{logicalName}' was not found or has no readable variant");

        List<string> errors = [];
        foreach ((AssetRecord record, AssetVariant variant) in candidates)
        {
            try
            {
                using Bitmap source = await AssetImageLoader.LoadOwnedBitmapAsync(
                    _session.AssetContent,
                    variant.RelativePath,
                    cancellationToken).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();

                Bitmap output;
                if (hue % 360 == 0)
                {
                    output = source.Clone(
                        new Rectangle(0, 0, source.Width, source.Height),
                        System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                }
                else
                {
                    try
                    {
                        output = EnemyEditorControl.ApplyHue(source, hue);
                    }
                    catch (Exception exception) when (exception is ArgumentException or OutOfMemoryException)
                    {
                        // Showing the unshifted battler is preferable to replacing it with
                        // a placeholder if a malformed source defeats hue conversion.
                        errors.Add($"{record.Key.Category}: hue preview failed ({exception.Message})");
                        output = source.Clone(
                            new Rectangle(0, 0, source.Width, source.Height),
                            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    }
                }

                if (output.Width > 0 && output.Height > 0) return (output, null);
                output.Dispose();
                errors.Add($"{record.Key.Category}: decoded to an empty image");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OutOfMemoryException)
            {
                errors.Add($"{record.Key.Category}/{Path.GetFileName(variant.RelativePath)}: {exception.Message}");
            }
        }

        return (null, errors.Count == 0 ? "image could not be decoded" : string.Join(" | ", errors.Take(2)));
    }

    private IReadOnlyList<(AssetRecord Record, AssetVariant Variant)> FindReadableBattlerCandidates(string logicalName)
    {
        AssetCategory[] categories = [AssetCategory.Enemies, AssetCategory.SideViewEnemies];
        List<AssetRecord> records = [];
        foreach (AssetCategory category in categories)
        {
            AssetRecord? exact = _session.Assets.Find(new AssetKey(category, logicalName));
            if (exact is not null) records.Add(exact);
        }

        // Imported and legacy projects can retain filename case or extension differences.
        // Search the catalogue as a fallback instead of silently showing placeholders.
        foreach (AssetRecord record in _session.Assets.Assets)
        {
            if (!categories.Contains(record.Key.Category)) continue;
            if (!BattlerNamesMatch(record.Key.LogicalName, logicalName)) continue;
            if (records.Any(existing =>
                    existing.Key.Category == record.Key.Category
                    && string.Equals(existing.Key.LogicalName, record.Key.LogicalName, StringComparison.OrdinalIgnoreCase))) continue;
            records.Add(record);
        }

        return records
            .SelectMany(record => record.Variants
                .Where(variant => !variant.IsEncrypted && variant.Status != AssetStatus.PendingDeletion)
                .OrderBy(variant => AssetCategoryRegistry.ExtensionPreference(variant.Extension))
                .ThenBy(variant => variant.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(variant => (Record: record, Variant: variant)))
            .ToArray();
    }

    private static bool BattlerNamesMatch(string left, string right)
    {
        string normalizedLeft = NormalizeBattlerLogicalName(left);
        string normalizedRight = NormalizeBattlerLogicalName(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBattlerLogicalName(string value)
    {
        string normalized = AssetKey.NormalizeLogicalName(value ?? string.Empty);
        string extension = Path.GetExtension(normalized);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".rpgmvp", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^extension.Length];
        }
        return normalized;
    }

    private void DisposeMemberImages()
    {
        // Release the canvas-owned display copies first, then release the editor's
        // source cache. This keeps rebinding deterministic and bounds GDI+ usage.
        _canvas.ClearImages();
        foreach (Bitmap image in _memberImages.Values) image.Dispose();
        _memberImages.Clear();
        _memberImageFailures.Clear();
    }

    private async Task LoadBattlebacksAsync(CancellationToken cancellationToken)
    {
        DisposeBattlebackImages();
        MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.System);
        if (summary is null)
        {
            _battleback1Name = string.Empty;
            _battleback2Name = string.Empty;
            _canvas.SetBattlebacks(null, null);
            return;
        }

        await using Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(summary.RelativePath, cancellationToken).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument system) return;
        BattleTestProfile profile = BattleTestProfileReader.Read(system, _recordId);
        _battleback1Name = profile.Battleback1Name;
        _battleback2Name = profile.Battleback2Name;
        _battleback1Image = await LoadAssetBitmapAsync(AssetCategory.Battlebacks1, _battleback1Name, cancellationToken).ConfigureAwait(true);
        _battleback2Image = await LoadAssetBitmapAsync(AssetCategory.Battlebacks2, _battleback2Name, cancellationToken).ConfigureAwait(true);
        UpdateCanvasBattlebacks();
    }

    private async ValueTask<Bitmap?> LoadAssetBitmapAsync(AssetCategory category, string logicalName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logicalName)) return null;
        AssetRecord? record = _session.Assets.Find(new AssetKey(category, logicalName));
        AssetVariant? variant = record?.PreferredVariant;
        if (variant is null || variant.IsEncrypted) return null;
        try
        {
            return await AssetImageLoader.LoadOwnedBitmapAsync(_session.AssetContent, variant.RelativePath, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private void DisposeBattlebackImages()
    {
        // Detach borrowed references before disposing the underlying images.
        _canvas.SetBattlebacks(null, null);
        _battleback1Image?.Dispose();
        _battleback2Image?.Dispose();
        _battleback1Image = null;
        _battleback2Image = null;
    }

    private void UpdateCanvasBattlebacks()
    {
        string caption = string.Join(" + ", new[] { _battleback1Name, _battleback2Name }.Where(name => !string.IsNullOrWhiteSpace(name)));
        _canvas.SetBattlebacks(_battleback1Image, _battleback2Image, caption);
    }

    private async Task ChangeBattleBackgroundAsync()
    {
        using TroopBattlebackDialog dialog = new(_session, _battleback1Name, _battleback2Name);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.System);
        if (summary is null)
        {
            MessageBox.Show(this, "System.json is not available.", "Change Battle Background", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            await _phase9C.SetBattlebacksAsync(
                summary.RelativePath,
                dialog.Result.Battleback1Name,
                dialog.Result.Battleback2Name).ConfigureAwait(true);
            _battleback1Name = dialog.Result.Battleback1Name;
            _battleback2Name = dialog.Result.Battleback2Name;
            DisposeBattlebackImages();
            _battleback1Image = await LoadAssetBitmapAsync(AssetCategory.Battlebacks1, _battleback1Name, CancellationToken.None).ConfigureAwait(true);
            _battleback2Image = await LoadAssetBitmapAsync(AssetCategory.Battlebacks2, _battleback2Name, CancellationToken.None).ConfigureAwait(true);
            UpdateCanvasBattlebacks();
        }
        catch (Exception exception)
        {
            Phase9BEditorUi.ShowError(this, exception);
        }
    }

    private async Task AddSelectedPaletteEnemyAsync()
    {
        int row = SelectedRow(_enemyPalette);
        if (row < 0 || row >= _catalog.Shared.Enemies.Count) return;
        await AddMemberByEnemyIdAsync(_catalog.Shared.Enemies[row].Id).ConfigureAwait(true);
    }

    private async Task AddMemberByEnemyIdAsync(int enemyId)
    {
        if (enemyId <= 0) return;
        int index = _memberItems.Count;
        BattleCanvasProfile profile = CurrentProfile();
        int x = Math.Clamp(profile.LogicalWidth / 2 + ((index % 3) - 1) * 96, 0, profile.LogicalWidth);
        int y = Math.Clamp((int)(profile.LogicalHeight * 0.78f) - (index / 3) * 64, 0, profile.LogicalHeight);
        _memberItems.Add(new MvTroopMemberSnapshot(index, enemyId, x, y, false, "{}"));
        if (!_memberImages.ContainsKey(enemyId)) await LoadMemberImageAsync(enemyId, CancellationToken.None).ConfigureAwait(true);
        SetPendingCanvas(true);
        RefreshMembers();
        RefreshCanvas();
        _canvas.SelectOnly(index);
    }

    private async Task LoadMemberImageAsync(int enemyId, CancellationToken cancellationToken)
    {
        MvDatabaseFileSummary? enemySummary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.Enemies);
        if (enemySummary is null) return;
        await using Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(enemySummary.RelativePath, cancellationToken).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument enemies) return;
        (Bitmap? image, string? failure) = await LoadEnemyBattlerBitmapAsync(enemies, enemyId, cancellationToken).ConfigureAwait(true);
        if (image is not null)
        {
            if (_memberImages.Remove(enemyId, out Bitmap? previous)) previous.Dispose();
            _memberImages[enemyId] = image;
            _memberImageFailures.Remove(enemyId);
        }
        else
        {
            _memberImageFailures[enemyId] = string.IsNullOrWhiteSpace(failure) ? "image unavailable" : failure;
        }
    }

    private async Task ClearMembersAsync()
    {
        if (_memberItems.Count == 0) return;
        if (MessageBox.Show(this, "Remove every member from this troop? Battle-event enemy references may need to be repaired.", "Clear Troop", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _memberItems.Clear();
        SetPendingCanvas(true);
        RefreshMembers();
        RefreshCanvas();
        await Task.CompletedTask;
    }

    private void EditSelectedMember()
    {
        int row = SelectedRow(_members);
        if (row < 0 || row >= _memberItems.Count) return;
        MvTroopMemberSnapshot member = _memberItems[row];
        using TroopMemberEditDialog dialog = new(_catalog, member);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _memberItems[row] = dialog.Result with { Index = member.Index };
        SetPendingCanvas(true);
        RefreshMembers();
        RefreshCanvas();
    }

    private void DuplicateSelectedMembers()
    {
        int[] selected = SelectedRows(_members);
        if (selected.Length == 0) return;
        foreach (int row in selected.OrderBy(value => value))
        {
            MvTroopMemberSnapshot source = _memberItems[row];
            _memberItems.Add(source with { Index = _memberItems.Count, X = source.X + 24, Y = source.Y + 24 });
        }
        SetPendingCanvas(true);
        RefreshMembers();
        RefreshCanvas();
    }

    private async Task RemoveSelectedMembersAsync()
    {
        int[] rows = SelectedRows(_members);
        if (rows.Length == 0 || _snapshot is null) return;
        int first = rows.Min();
        TroopMemberReferenceReport report = TroopMemberReferenceAnalyzer.AnalyzeRemoval(_snapshot with { Members = _memberItems.ToArray(), Pages = _pageItems.ToArray() }, first);
        StringBuilder message = new();
        message.AppendLine($"Remove {rows.Length} troop member(s)?");
        message.AppendLine("Member array indexes are gameplay references. Remaining members will be reindexed.");
        if (report.HasReferences)
        {
            message.AppendLine();
            message.AppendLine($"{report.References.Count} page condition or battle command reference(s) may require remapping:");
            foreach (TroopMemberReference reference in report.References.Take(12)) message.AppendLine($"• {reference.JsonPath} — {reference.Description}");
            if (report.References.Count > 12) message.AppendLine($"• …and {report.References.Count - 12} more");
        }
        if (MessageBox.Show(this, message.ToString(), "Remove Troop Members", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        foreach (int row in rows.OrderByDescending(value => value)) _memberItems.RemoveAt(row);
        for (int index = 0; index < _memberItems.Count; index++) _memberItems[index] = _memberItems[index] with { Index = index };
        SetPendingCanvas(true);
        RefreshMembers();
        RefreshCanvas();
        await Task.CompletedTask;
    }

    private void ToggleHidden()
    {
        int[] rows = SelectedRows(_members);
        foreach (int row in rows) _memberItems[row] = _memberItems[row] with { Hidden = !_memberItems[row].Hidden };
        SetPendingCanvas(true);
        RefreshMembers();
        RefreshCanvas();
    }

    private void ApplyFormation(TroopFormationPreset preset)
    {
        _memberItems.Clear();
        _memberItems.AddRange(TroopFormationPlanner.Plan(_canvas.Members, preset, CurrentProfile(), seed: 12345));
        SetPendingCanvas(true);
        RefreshMembers();
        RefreshCanvas();
    }

    private async Task CommitFormationAsync()
    {
        if (_recordId <= 0 || !_pendingCanvasChanges) return;
        try
        {
            await _phase9C.SetTroopMembersAsync(_document.RelativePath, _recordId, _memberItems).ConfigureAwait(true);
            SetPendingCanvas(false);
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyNameAsync()
    {
        try { await _phase9C.SetTroopNameAsync(_document.RelativePath, _recordId, _name.Text).ConfigureAwait(true); SetPendingName(false); }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private string GenerateTroopName()
    {
        return string.Join(", ", _memberItems.GroupBy(member => member.EnemyId).Select(group =>
        {
            string name = _catalog.Shared.Resolve(ReferenceTargetKind.Enemy, group.Key);
            int space = name.IndexOf(' ');
            if (space > 0 && int.TryParse(name.AsSpan(0, space), out _)) name = name[(space + 1)..];
            return group.Count() == 1 ? name : $"{name} ×{group.Count()}";
        }));
    }

    private int SelectedPageIndex()
    {
        if (_battlePageTabs.SelectedIndex >= 0) return _battlePageTabs.SelectedIndex;
        return SelectedRow(_pages);
    }

    private void SelectPage(int index)
    {
        if (_battlePageTabs.TabPages.Count == 0) return;
        _battlePageTabs.SelectedIndex = Math.Clamp(index, 0, _battlePageTabs.TabPages.Count - 1);
        if (index >= 0 && index < _pages.Rows.Count)
        {
            _pages.ClearSelection();
            _pages.Rows[index].Selected = true;
            _pages.CurrentCell = _pages.Rows[index].Cells[0];
        }
        RefreshBattleEventEditor();
    }

    private void RefreshBattleEventEditor()
    {
        if (_syncingBattlePageUi) return;
        int index = SelectedPageIndex();
        _syncingBattlePageUi = true;
        try
        {
            _battleCommands.Rows.Clear();
            if (index < 0 || index >= _pageItems.Count)
            {
                _battleConditions.Text = string.Empty;
                _battleSpan.SelectedIndex = -1;
                return;
            }

            MvTroopPageSnapshot page = _pageItems[index];
            _battleConditions.Text = page.Conditions.HasAnyCondition ? FormatConditions(page.Conditions) : "Don't Run";
            Phase9BEditorUi.SelectOption(_battleSpan, page.Span);
            foreach ((RawEventCommand command, ProjectedEventRow? projected) in ProjectBattleCommands(page, index))
            {
                EventCommandSummary summary = _eventFormatter.Format(command);
                string prefix = projected is null ? new string(' ', Math.Max(0, command.Indent) * 3) : new string(' ', Math.Max(0, projected.VisualDepth) * 3);
                string contents = prefix + "◆ " + summary.PrimaryText;
                if (!string.IsNullOrWhiteSpace(summary.SecondaryText)) contents += ": " + summary.SecondaryText;
                _battleCommands.Rows.Add(command.RawIndex.ToString("D4", CultureInfo.InvariantCulture), contents, command.Code);
            }
            _battleCommands.Rows.Add(string.Empty, "◆ Insert event command here…", string.Empty);
        }
        finally
        {
            _syncingBattlePageUi = false;
        }
    }

    private IEnumerable<(RawEventCommand Command, ProjectedEventRow? Projected)> ProjectBattleCommands(MvTroopPageSnapshot page, int pageIndex)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(page.CommandListRawJson); }
        catch (JsonException) { yield break; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) yield break;
            List<RawEventCommand> commands = [];
            int rawIndex = 0;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                commands.Add(RawEventCommand.Parse(rawIndex++, element.GetRawText()));
            }
            EventListAddress address = EventListAddress.ForTroopEvent(_document.RelativePath, _recordId, pageIndex);
            EventListSnapshot snapshot = new(
                address,
                commands,
                page.CommandListRawJson,
                EventStructureProjector.Fingerprint(commands),
                DateTimeOffset.UtcNow);
            EventStructureDocument structure = _eventProjector.Project(snapshot);
            Dictionary<int, ProjectedEventRow> rows = EventRowProjector.Build(structure)
                .GroupBy(row => row.RawIndex)
                .ToDictionary(group => group.Key, group => group.First());
            foreach (RawEventCommand command in commands)
            {
                if (command.Code == 0) continue;
                rows.TryGetValue(command.RawIndex, out ProjectedEventRow? projected);
                yield return (command, projected);
            }
        }
    }

    private void BattleSpanChanged()
    {
        if (_loading || _syncingBattlePageUi) return;
        int index = SelectedPageIndex();
        if (index < 0 || index >= _pageItems.Count) return;
        int span = Phase9BEditorUi.SelectedOption(_battleSpan);
        if (_pageItems[index].Span == span) return;
        _pageItems[index] = _pageItems[index] with { Span = span };
        SetPendingPages(true);
        RefreshPages();
        SelectPage(index);
    }

    private void CopyPage()
    {
        int index = SelectedPageIndex();
        if (index < 0 || index >= _pageItems.Count) return;
        string json = JsonSerializer.Serialize(_pageItems[index]);
        DataObject data = new();
        data.SetData(TroopPageClipboardFormat, json);
        data.SetText(json);
        Clipboard.SetDataObject(data, true);
    }

    private void PastePage()
    {
        string? json = Clipboard.ContainsData(TroopPageClipboardFormat)
            ? Clipboard.GetData(TroopPageClipboardFormat) as string
            : Clipboard.ContainsText() ? Clipboard.GetText() : null;
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            MvTroopPageSnapshot page = JsonSerializer.Deserialize<MvTroopPageSnapshot>(json)
                ?? throw new InvalidDataException("The clipboard does not contain a troop event page.");
            int selected = SelectedPageIndex();
            int insertAt = selected < 0 ? _pageItems.Count : selected + 1;
            _pageItems.Insert(insertAt, page with { Index = insertAt });
            ReindexPages();
            SetPendingPages(true);
            RefreshPages();
            SelectPage(insertAt);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            MessageBox.Show(this, exception.Message, "Paste Event Page", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddPage()
    {
        int index = _pageItems.Count;
        MvTroopPageConditionSnapshot conditions = new(false, false, 0, 0, false, 0, 50, false, 1, 50, false, 1, "{}");
        _pageItems.Add(new MvTroopPageSnapshot(index, conditions, 0, 1, "[{\"code\":0,\"indent\":0,\"parameters\":[]}]", "{}"));
        SetPendingPages(true);
        RefreshPages();
        SelectPage(index);
    }

    private void EditPage()
    {
        int index = SelectedPageIndex();
        if (index < 0 || index >= _pageItems.Count || _snapshot is null) return;
        MvTroopRecordSnapshot working = _snapshot with { Members = _memberItems.ToArray(), Pages = _pageItems.ToArray() };
        using TroopPageEditDialog dialog = new(_catalog, working, _pageItems[index]);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        _pageItems[index] = dialog.Result;
        SetPendingPages(true);
        RefreshPages();
        SelectPage(index);
    }

    private void DuplicatePage()
    {
        int index = SelectedPageIndex();
        if (index < 0 || index >= _pageItems.Count) return;
        MvTroopPageSnapshot source = _pageItems[index];
        _pageItems.Insert(index + 1, source with { Index = index + 1 });
        ReindexPages();
        SetPendingPages(true);
        RefreshPages();
        SelectPage(index + 1);
    }

    private void RemovePage()
    {
        int index = SelectedPageIndex();
        if (index < 0 || index >= _pageItems.Count) return;
        if (_pageItems.Count == 1)
        {
            MessageBox.Show(this, "A troop should retain at least one battle-event page. Clear its conditions and commands instead.", "Troop Event Pages", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _pageItems.RemoveAt(index);
        ReindexPages();
        SetPendingPages(true);
        RefreshPages();
        SelectPage(Math.Min(index, _pageItems.Count - 1));
    }

    private void MovePage(int delta)
    {
        int index = SelectedPageIndex();
        int target = index + delta;
        if (index < 0 || target < 0 || target >= _pageItems.Count) return;
        (_pageItems[index], _pageItems[target]) = (_pageItems[target], _pageItems[index]);
        ReindexPages();
        SetPendingPages(true);
        RefreshPages();
        SelectPage(target);
    }

    private void ReindexPages()
    {
        for (int index = 0; index < _pageItems.Count; index++) _pageItems[index] = _pageItems[index] with { Index = index };
    }

    private async Task OpenPageCommandsAsync()
    {
        int index = SelectedPageIndex();
        if (index < 0 || index >= _pageItems.Count) return;
        try
        {
            if (HasPendingChanges)
            {
                DialogResult decision = MessageBox.Show(
                    this,
                    "This troop contains unapplied name, formation, or battle-page changes. Apply them before opening the dedicated Event Editor?",
                    "Apply Troop Changes",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (decision == DialogResult.Cancel || decision == DialogResult.No) return;
                await ApplyPendingAsync().ConfigureAwait(true);
                if (HasPendingChanges)
                {
                    MessageBox.Show(
                        this,
                        "One or more troop changes could not be applied. Resolve those changes before opening the Event Editor.",
                        "Troop Changes Not Applied",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            if (_documentOpener is null)
            {
                MessageBox.Show(
                    this,
                    "The dedicated Event Editor is unavailable from this legacy host. Open the troop through the Database window.",
                    "Event Editor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DocumentRequest request = new(
                EditorDocumentKind.EventEditor,
                $"event:troop:{_recordId}",
                _document.RelativePath,
                MvDocumentKind.Troops,
                RecordId: _recordId,
                DisplayTitle: $"Troop {_recordId:D4} — Battle Event",
                EventSurface: "TroopEvent",
                EventPageIndex: index);
            await _documentOpener(request).ConfigureAwait(true);
            await BindAsync(
                [new DatabaseRecordHandle(MvDocumentKind.Troops, _document.RelativePath, _recordId)],
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Phase9BEditorUi.ShowError(this, exception);
        }
    }

    private void ClearPageCommands()
    {
        int index = SelectedPageIndex();
        if (index < 0 || index >= _pageItems.Count) return;
        if (MessageBox.Show(this, "Replace this page's complete command list with a single terminator command? This operation is undoable after Apply Pages.", "Clear Battle Commands", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        _pageItems[index] = _pageItems[index] with { CommandCount = 1, CommandListRawJson = "[{\"code\":0,\"indent\":0,\"parameters\":[]}]" };
        SetPendingPages(true);
        RefreshPages();
        SelectPage(index);
    }

    private void CompileHiddenAsSecondWave()
    {
        int[] opening = _memberItems.Where(member => !member.Hidden).Select(member => member.Index).ToArray();
        int[] reinforcements = _memberItems.Where(member => member.Hidden).Select(member => member.Index).ToArray();
        if (opening.Length == 0 || reinforcements.Length == 0)
        {
            MessageBox.Show(this, "Mark the opening members as visible and the second-wave members as hidden before compiling waves.", "Compile Waves", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        MultiWaveCompilationResult result = MultiWaveCompiler.Compile(
            _memberItems.ToArray(),
            [new TroopWaveDefinition("Opening", opening), new TroopWaveDefinition("Reinforcements", reinforcements)],
            _pageItems.Count);
        if (result.GeneratedPages.Count == 0)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, result.Warnings), "Compile Waves", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        ReplaceMembers(result.Members);
        _pageItems.AddRange(result.GeneratedPages);
        ReindexPages();
        SetPendingPages(true);
        RefreshMembers();
        RefreshPages();
        RefreshCanvas();
        MessageBox.Show(this, string.Join(Environment.NewLine, result.Warnings), "Wave Pages Generated", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ApplyPagesAsync()
    {
        try { await _phase9C.SetTroopPagesAsync(_document.RelativePath, _recordId, _pageItems).ConfigureAwait(true); SetPendingPages(false); }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ReloadEnemyDataAndCalculateAsync()
    {
        await CalculateRewardsAsync().ConfigureAwait(true);
    }

    private void CalculateRewards() => _ = CalculateRewardsAsync();

    private async Task CalculateRewardsAsync()
    {
        MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.Enemies);
        if (summary is null) return;
        await using Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(summary.RelativePath).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument enemies) return;
        Dictionary<int, MvEnemyRecordSnapshot> enemyRecords = [];
        foreach (int id in _memberItems.Select(member => member.EnemyId).Distinct())
        {
            if (enemies.Syntax.Resolve(JsonPath.Root.Append(id)) is JsonObjectSyntax) enemyRecords[id] = Phase9CRecordReader.ReadEnemy(enemies, id);
        }
        MvTroopRecordSnapshot troop = _snapshot is null
            ? new MvTroopRecordSnapshot(_recordId, _name.Text, _memberItems.ToArray(), _pageItems.ToArray(), new Dictionary<string, string>())
            : _snapshot with { Name = _name.Text, Members = _memberItems.ToArray(), Pages = _pageItems.ToArray() };
        TroopRewardSummary reward = EnemyRewardCalculator.CalculateTroop(troop, enemyRecords);
        TroopDifficultyEstimate difficulty = TroopDifficultyEstimator.EstimateTroop(troop, enemyRecords);
        StringBuilder text = new();
        text.AppendLine($"Troop: {troop.Name}");
        text.AppendLine($"Members: {troop.Members.Count}");
        text.AppendLine($"Total EXP: {reward.Experience:N0}");
        text.AppendLine($"Total Gold: {reward.Gold:N0}");
        text.AppendLine();
        text.AppendLine("Difficulty — MV Core Approximation:");
        text.AppendLine($"  Classification:       {difficulty.Classification}");
        text.AppendLine($"  Composite threat:     {difficulty.CompositeThreat:N1}");
        text.AppendLine($"  Durability pressure:  {difficulty.Durability:N1}");
        text.AppendLine($"  Damage pressure:      {difficulty.DamagePressure:N1}");
        text.AppendLine($"  Speed pressure:       {difficulty.SpeedPressure:N1}");
        text.AppendLine($"  Control pressure:     {difficulty.ControlPressure:N1}");
        text.AppendLine($"  Reward/threat ratio:  {difficulty.RewardToThreatRatio:N3}");
        text.AppendLine($"  {difficulty.Assumptions}");
        text.AppendLine();
        text.AppendLine("Expected drops per victory:");
        foreach (((int kind, int id), double count) in reward.ExpectedDrops.OrderByDescending(pair => pair.Value))
        {
            text.AppendLine($"  {_catalog.ResolveDrop(kind, id),-42} {count:N4}");
        }
        _rewardSummary.Text = text.ToString();
    }

    private async Task ShowBattleTestProfileAsync()
    {
        MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.System);
        if (summary is null)
        {
            MessageBox.Show(this, "System.json is not available.", "Battle Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        await using Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(summary.RelativePath).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument system)
        {
            MessageBox.Show(this, "System.json is not available as a mutable document.", "Battle Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        BattleTestProfile profile = BattleTestProfileReader.Read(system, _recordId);
        using BattleTestProfileDialog dialog = new(_catalog, profile);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
        try
        {
            await _phase9C.SetBattleTestProfileAsync(summary.RelativePath, dialog.Result).ConfigureAwait(true);
            _battleback1Name = dialog.Result.Battleback1Name;
            _battleback2Name = dialog.Result.Battleback2Name;
            DisposeBattlebackImages();
            _battleback1Image = await LoadAssetBitmapAsync(AssetCategory.Battlebacks1, _battleback1Name, CancellationToken.None).ConfigureAwait(true);
            _battleback2Image = await LoadAssetBitmapAsync(AssetCategory.Battlebacks2, _battleback2Name, CancellationToken.None).ConfigureAwait(true);
            UpdateCanvasBattlebacks();
            MessageBox.Show(this,
                "The standard System.json battle-test fields were updated transactionally.",
                "Battle Test Profile Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            Phase9BEditorUi.ShowError(this, exception);
        }
    }

    private void UpdateCanvasProfile()
    {
        _canvas.SetProfile(CurrentProfile());
    }

    private BattleCanvasProfile CurrentProfile() => new(decimal.ToInt32(_logicalWidth.Value), decimal.ToInt32(_logicalHeight.Value), "Playroma workspace override");

    private void SyncMemberSelectionFromCanvas()
    {
        if (_loading || _syncingMemberSelection) return;
        int[] selectedIndexes = _canvas.SelectedIndexes.ToArray();
        _syncingMemberSelection = true;
        try
        {
            _members.ClearSelection();
            foreach (int index in selectedIndexes)
            {
                if (index >= 0 && index < _members.Rows.Count) _members.Rows[index].Selected = true;
            }
            if (selectedIndexes.Length == 1 && selectedIndexes[0] >= 0 && selectedIndexes[0] < _members.Rows.Count)
            {
                _members.CurrentCell = _members.Rows[selectedIndexes[0]].Cells[0];
            }
        }
        finally
        {
            _syncingMemberSelection = false;
        }
    }

    private void SyncCanvasSelectionFromGrid()
    {
        if (_loading || _syncingMemberSelection) return;
        int[] rows = SelectedRows(_members);
        _syncingMemberSelection = true;
        try
        {
            if (rows.Length == 1) _canvas.SelectOnly(rows[0]);
            else if (rows.Length == 0) _canvas.SelectOnly(null);
        }
        finally
        {
            _syncingMemberSelection = false;
        }
    }

    private void SetPendingCanvas(bool value)
    {
        if (_pendingCanvasChanges == value) return;
        _pendingCanvasChanges = value;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPendingPages(bool value)
    {
        if (_pendingPageChanges == value) return;
        _pendingPageChanges = value;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPendingName(bool value)
    {
        if (_pendingNameChanges == value) return;
        _pendingNameChanges = value;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string FormatConditions(MvTroopPageConditionSnapshot condition)
    {
        List<string> values = [];
        if (condition.TurnEnding) values.Add("Turn End");
        if (condition.TurnValid) values.Add($"Turn {condition.TurnA}+{condition.TurnB}×X");
        if (condition.EnemyValid) values.Add($"Enemy #{condition.EnemyIndex + 1} HP ≤ {condition.EnemyHp}%");
        if (condition.ActorValid) values.Add($"Actor #{condition.ActorId} HP ≤ {condition.ActorHp}%");
        if (condition.SwitchValid) values.Add($"Switch #{condition.SwitchId} ON");
        return values.Count == 0 ? "No conditions" : string.Join(" AND ", values);
    }

    private static int SelectedRow(DataGridView grid) => grid.SelectedRows.Count > 0 ? grid.SelectedRows[0].Index : grid.CurrentCell?.RowIndex ?? -1;
    private static int[] SelectedRows(DataGridView grid) => grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).Distinct().OrderBy(value => value).ToArray();
}

public sealed class DatabaseChoiceDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search…" };
    private readonly IReadOnlyList<DatabaseChoice> _choices;

    public DatabaseChoiceDialog(string title, IReadOnlyList<DatabaseChoice> choices)
    {
        _choices = choices ?? [];
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(520, 620);
        FlowLayoutPanel buttons = EnemyDropEditDialog.Buttons(out Button accept, out Button cancel);
        buttons.Dock = DockStyle.Bottom;
        Controls.Add(_list);
        Controls.Add(_search);
        Controls.Add(buttons);
        AcceptButton = accept;
        CancelButton = cancel;
        _search.TextChanged += (_, _) => ApplyFilter();
        _list.DoubleClick += (_, _) => AcceptSelection();
        accept.Click += (_, _) => AcceptSelection();
        ApplyFilter();
    }

    public int SelectedId { get; private set; }

    private void ApplyFilter()
    {
        string text = _search.Text.Trim();
        _list.Items.Clear();
        foreach (DatabaseChoice choice in _choices)
        {
            if (text.Length > 0 && !choice.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
            _list.Items.Add(choice);
        }
    }

    private void AcceptSelection()
    {
        if (_list.SelectedItem is not DatabaseChoice choice) return;
        SelectedId = choice.Id;
        DialogResult = DialogResult.OK;
    }
}

public sealed class TroopMemberEditDialog : Form
{
    private readonly NamedIdPicker _enemy = new();
    private readonly NumericUpDown _x = Phase9BEditorUi.Number(0, 4096);
    private readonly NumericUpDown _y = Phase9BEditorUi.Number(0, 4096);
    private readonly CheckBox _hidden = new() { Text = "Hidden at battle start", AutoSize = true };
    private readonly MvTroopMemberSnapshot _source;

    public TroopMemberEditDialog(Phase9CProjectCatalog catalog, MvTroopMemberSnapshot member)
    {
        _source = member;
        Text = $"Troop Member #{member.Index + 1}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(520, 320);
        _enemy.SetChoices(catalog.Shared.Enemies, "Enemy", includeNone: false);
        _enemy.SelectedId = member.EnemyId;
        Phase9BEditorUi.Set(_x, member.X);
        Phase9BEditorUi.Set(_y, member.Y);
        _hidden.Checked = member.Hidden;
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Enemy", _enemy);
        Phase9BEditorUi.AddRow(table, 1, "X", _x);
        Phase9BEditorUi.AddRow(table, 2, "Y", _y);
        Phase9BEditorUi.AddRow(table, 3, "Initial State", _hidden);
        FlowLayoutPanel buttons = EnemyDropEditDialog.Buttons(out Button accept, out Button cancel);
        Phase9BEditorUi.AddRow(table, 4, string.Empty, buttons);
        Controls.Add(table);
        AcceptButton = accept;
        CancelButton = cancel;
        accept.Click += (_, _) =>
        {
            Result = _source with
            {
                EnemyId = _enemy.SelectedId,
                X = decimal.ToInt32(_x.Value),
                Y = decimal.ToInt32(_y.Value),
                Hidden = _hidden.Checked
            };
            DialogResult = DialogResult.OK;
        };
    }

    public MvTroopMemberSnapshot? Result { get; private set; }
}
