using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class ClassEditorControl : UserControl, IDatabaseEditorBody, IDatabasePendingEditSource
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly IDatabaseEditorDefinition _definition;
    private readonly DatabaseMutationService _mutations;
    private readonly ActorClassMutationService _actorClassMutations;
    private readonly Label _selectionMessage = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8),
        ForeColor = SystemColors.GrayText
    };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _basis = ExpNumber(1, 9999);
    private readonly NumericUpDown _extra = ExpNumber(0, 9999);
    private readonly NumericUpDown _accelerationA = ExpNumber(0, 100);
    private readonly NumericUpDown _accelerationB = ExpNumber(1, 100);
    private readonly Button _applyExp = new() { Text = "Apply EXP Curve", AutoSize = true };
    private readonly Button _compareClasses = new() { Text = "Compare Classes…", AutoSize = true };
    private readonly ExpCurveGraph _expGraph = new() { Dock = DockStyle.Fill, MinimumSize = new Size(300, 200) };
    private readonly DataGridView _levelPreview = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly ComboBox _parameter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ParameterCurveEditor _curve = new() { Dock = DockStyle.Fill };
    private readonly Button _applyCurve = new() { Text = "Apply Curve", AutoSize = true };
    private readonly Button _generateCurve = new() { Text = "Generate Smooth…", AutoSize = true };
    private readonly Button _copyCurve = new() { Text = "Copy From…", AutoSize = true };
    private readonly Label _curveStatus = new() { AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
    private readonly ClassLearningEditor _learnings;
    private readonly TraitListEditor _traits = new();
    private IReadOnlyList<IReadOnlyList<int>> _curves = [];
    private int[] _expParameters = [30, 20, 30, 30];
    private int _classId;
    private Phase9BProjectCatalog _catalog = Phase9BProjectCatalog.Empty;
    private bool _loading;
    private bool _expPending;
    private readonly HashSet<int> _pendingCurveIds = [];

    public ClassEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _mutations = new DatabaseMutationService(session.Editing);
        _actorClassMutations = new ActorClassMutationService(session.Editing);
        _learnings = new ClassLearningEditor(session, _actorClassMutations) { Dock = DockStyle.Fill };
        Dock = DockStyle.Fill;

        for (int id = 0; id < MvParameterNames.All.Count; id++) _parameter.Items.Add(new ParameterItem(id, MvParameterNames.Get(id)));
        _parameter.SelectedIndex = 0;
        BuildLevelPreviewColumns();

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("General and EXP") { Controls = { BuildGeneralPage() } });
        tabs.TabPages.Add(new TabPage("Parameter Curves") { Controls = { BuildParameterPage() } });
        tabs.TabPages.Add(new TabPage("Skill Learnings") { Controls = { _learnings } });
        tabs.TabPages.Add(new TabPage("Traits") { Controls = { _traits } });
        Controls.Add(tabs);
        Controls.Add(_selectionMessage);

        _name.Validated += async (_, _) =>
        {
            if (_loading || string.Equals(_name.Tag as string, _name.Text, StringComparison.Ordinal)) return;
            await CommitNameAsync().ConfigureAwait(true);
        };
        _name.KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                await CommitNameAsync().ConfigureAwait(true);
                e.SuppressKeyPress = true;
            }
        };
        _basis.ValueChanged += ExpValueChanged;
        _extra.ValueChanged += ExpValueChanged;
        _accelerationA.ValueChanged += ExpValueChanged;
        _accelerationB.ValueChanged += ExpValueChanged;
        _applyExp.Click += async (_, _) => await ApplyExpAsync().ConfigureAwait(true);
        _compareClasses.Click += async (_, _) => await CompareClassesAsync().ConfigureAwait(true);
        _parameter.SelectedIndexChanged += (_, _) => LoadSelectedCurve();
        _curve.ValueChanged += (_, _) =>
        {
            if (_loading || _parameter.SelectedItem is not ParameterItem item) return;
            SetWorkingCurve(item.Id, _curve.Values);
            _pendingCurveIds.Add(item.Id);
            _curveStatus.Text = "Curve changed; click Apply Curve to commit it as one undo entry.";
            PendingChangesChanged?.Invoke(this, EventArgs.Empty);
            PopulateLevelPreview();
        };
        _applyCurve.Click += async (_, _) => await ApplyCurveAsync().ConfigureAwait(true);
        _generateCurve.Click += (_, _) => GenerateCurve();
        _copyCurve.Click += async (_, _) => await CopyCurveAsync().ConfigureAwait(true);
    }

    public Control View => this;
    public bool OwnsTraits => true;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;
    public bool HasPendingChanges => _expPending || _pendingCurveIds.Count > 0;
    public event EventHandler? PendingChangesChanged;

    public async ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_classId <= 0) return;
        if (_expPending) await ApplyExpAsync().ConfigureAwait(true);
        foreach (int parameterId in _pendingCurveIds.OrderBy(id => id).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parameterId >= _curves.Count) continue;
            try
            {
                await _actorClassMutations.SetClassParameterCurveAsync(
                    _document.RelativePath,
                    _classId,
                    parameterId,
                    NormalizeCurve(_curves[parameterId])).ConfigureAwait(true);
                _pendingCurveIds.Remove(parameterId);
                PendingChangesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Parameter curve update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                break;
            }
        }
    }

    public async ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_classId <= 0) return;
        await BindAsync(
            [new DatabaseRecordHandle(MvDocumentKind.Classes, _document.RelativePath, _classId)],
            cancellationToken).ConfigureAwait(true);
    }

    public async ValueTask BindAsync(
        IReadOnlyList<DatabaseRecordHandle> records,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            if (records.Count != 1 || _document.Syntax.Resolve(records[0].Path) is not JsonObjectSyntax)
            {
                _classId = 0;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0
                    ? "Select a class to edit it."
                    : "The complete Class editor requires one populated class.";
                _traits.ClearBinding();
                _learnings.ClearBinding();
                return;
            }

            Enabled = true;
            _classId = records[0].Id;
            _catalog = await Phase9BProjectCatalogLoader.LoadAsync(_session, cancellationToken).ConfigureAwait(true);
            _selectionMessage.Text = $"Class {_classId:D4} — EXP, parameter curves, learnings, and traits are raw-backed and undoable.";
            MvClassRecordSnapshot snapshot = ActorClassRecordReader.ReadClass(_document, _classId);
            _name.Text = snapshot.Name;
            _name.Tag = _name.Text;
            _expParameters = NormalizeExp(snapshot.ExpParameters);
            SetExpControls(_expParameters);
            _curves = NormalizeCurves(snapshot.ParameterCurves);
            LoadSelectedCurve();
            PopulateLevelPreview();
            JsonPath root = JsonPath.Root.Append(_classId);
            if (_document.Syntax.Resolve(root.Append("traits")) is JsonArraySyntax)
            {
                _traits.Bind(_session, _document, root.Append("traits"), _catalog);
            }
            else
            {
                _traits.ClearBinding();
            }
            await _learnings.BindAsync(_document.RelativePath, _classId, snapshot.Learnings, cancellationToken).ConfigureAwait(true);
            _expPending = false;
            _pendingCurveIds.Clear();
            PendingChangesChanged?.Invoke(this, EventArgs.Empty);
            _curveStatus.Text = "Select a parameter to view or edit levels 1–99.";
        }
        finally
        {
            _loading = false;
        }
    }

    private Control BuildGeneralPage()
    {
        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Horizontal, 240, 190, 180);
        TableLayoutPanel fields = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(12)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddRow(fields, 0, "Name", _name, string.Empty, new Panel());
        AddRow(fields, 1, "Basis", _basis, "Extra", _extra);
        AddRow(fields, 2, "Acceleration A", _accelerationA, "Acceleration B", _accelerationB);
        FlowLayoutPanel actions = new() { AutoSize = true, WrapContents = false };
        actions.Controls.AddRange([_applyExp, _compareClasses]);
        fields.Controls.Add(actions, 1, 3);
        fields.SetColumnSpan(actions, 3);
        Panel fieldHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
        fieldHost.Controls.Add(fields);
        split.Panel1.Controls.Add(fieldHost);

        SplitContainer preview = ControlHelpers.CreateSplitContainer(Orientation.Vertical, 480, 280, 280);
        preview.Panel1.Controls.Add(_expGraph);
        preview.Panel2.Controls.Add(_levelPreview);
        split.Panel2.Controls.Add(preview);
        return split;
    }

    private Control BuildParameterPage()
    {
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(6),
            WrapContents = false
        };
        toolbar.Controls.AddRange([
            new Label { Text = "Parameter", AutoSize = true, Margin = new Padding(3, 8, 4, 3) },
            _parameter,
            _applyCurve,
            _generateCurve,
            _copyCurve,
            _curveStatus
        ]);
        Panel panel = new() { Dock = DockStyle.Fill };
        panel.Controls.Add(_curve);
        panel.Controls.Add(toolbar);
        return panel;
    }

    private async ValueTask CommitNameAsync()
    {
        if (_loading || _classId <= 0) return;
        try
        {
            await _mutations.SetFieldAsync(
                [new DatabaseRecordHandle(MvDocumentKind.Classes, _document.RelativePath, _classId)],
                _definition.RecordSchema.FindField("name"),
                _name.Text,
                allowHistoryMerge: true).ConfigureAwait(true);
            _name.Tag = _name.Text;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Class name update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    private async ValueTask CompareClassesAsync()
    {
        if (_classId <= 0) return;
        try
        {
            MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.Classes);
            if (summary is null) return;
            await using Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(summary.RelativePath).ConfigureAwait(true);
            if (lease.Document is not MutableJsonProjectDocument document || document.Syntax.Root is not JsonArraySyntax array) return;
            Dictionary<int, MvClassRecordSnapshot> classes = [];
            for (int id = 1; id < array.Elements.Count; id++)
            {
                if (array.Elements[id] is not JsonObjectSyntax) continue;
                classes[id] = ActorClassRecordReader.ReadClass(document, id);
            }
            if (!classes.TryGetValue(_classId, out MvClassRecordSnapshot? current) || classes.Count < 2)
            {
                MessageBox.Show(this, "At least two populated classes are required for comparison.", "Compare classes", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using ClassComparisonDialog dialog = new(current, classes);
            dialog.ShowDialog(this);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Class comparison failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExpValueChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _expParameters = CurrentExpParameters();
        _expGraph.Parameters = _expParameters;
        _expPending = true;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
        PopulateLevelPreview();
    }

    private async ValueTask ApplyExpAsync()
    {
        if (_classId <= 0) return;
        try
        {
            _expParameters = CurrentExpParameters();
            await _actorClassMutations.SetClassExpParametersAsync(
                _document.RelativePath,
                _classId,
                _expParameters).ConfigureAwait(true);
            _expPending = false;
            PendingChangesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "EXP curve update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSelectedCurve()
    {
        if (_parameter.SelectedItem is not ParameterItem item) return;
        bool previousLoading = _loading;
        _loading = true;
        try
        {
            int[] values = item.Id < _curves.Count
                ? NormalizeCurve(_curves[item.Id])
                : new int[100];
            _curve.Values = values;
            _curveStatus.Text = $"{item.Name}: level 1 = {values[1]:N0}; level 99 = {values[99]:N0}.";
        }
        finally
        {
            _loading = previousLoading;
        }
    }

    private async ValueTask ApplyCurveAsync()
    {
        if (_classId <= 0 || _parameter.SelectedItem is not ParameterItem item) return;
        try
        {
            int[] values = NormalizeCurve(_curve.Values);
            SetWorkingCurve(item.Id, values);
            await _actorClassMutations.SetClassParameterCurveAsync(
                _document.RelativePath,
                _classId,
                item.Id,
                values).ConfigureAwait(true);
            _pendingCurveIds.Remove(item.Id);
            PendingChangesChanged?.Invoke(this, EventArgs.Empty);
            _curveStatus.Text = $"{item.Name} curve applied as one undoable transaction.";
            PopulateLevelPreview();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Parameter curve update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void GenerateCurve()
    {
        if (_parameter.SelectedItem is not ParameterItem item) return;
        int[] current = _curve.Values;
        using SmoothCurveDialog dialog = new(item.Name, current.ElementAtOrDefault(1), current.ElementAtOrDefault(99));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _curve.Values = ActorClassMutationService.GenerateSmoothCurve(dialog.LevelOne, dialog.LevelNinetyNine, dialog.Exponent);
        _curveStatus.Text = "Generated curve preview; click Apply Curve to commit.";
    }

    private async ValueTask CopyCurveAsync()
    {
        if (_parameter.SelectedItem is not ParameterItem target) return;
        IReadOnlyList<DatabaseChoice> classes = await DatabaseChoiceLoader.LoadAsync(
            _session,
            MvDocumentKind.Classes).ConfigureAwait(true);
        using CurveCopyDialog dialog = new(classes, _classId, target.Id);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.Classes);
        if (summary is null) return;
        await using Playroma.JRPG.Workspaces.IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(summary.RelativePath).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument document) return;
        MvClassRecordSnapshot source = ActorClassRecordReader.ReadClass(document, dialog.SourceClassId);
        if (dialog.SourceParameterId >= source.ParameterCurves.Count)
        {
            MessageBox.Show(this, "The selected source curve is missing.", "Copy curve", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _curve.Values = NormalizeCurve(source.ParameterCurves[dialog.SourceParameterId]);
        _curveStatus.Text = $"Copied {MvParameterNames.Get(dialog.SourceParameterId)} from class {dialog.SourceClassId:D4}; click Apply Curve to commit.";
    }

    private void BuildLevelPreviewColumns()
    {
        _levelPreview.Columns.Add("Level", "Level");
        foreach (string parameter in MvParameterNames.All) _levelPreview.Columns.Add(parameter, parameter);
        _levelPreview.Columns.Add("EXP", "Total EXP");
    }

    private void PopulateLevelPreview()
    {
        _levelPreview.Rows.Clear();
        int[] levels = [1, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 99];
        foreach (int level in levels)
        {
            object[] values = new object[10];
            values[0] = level;
            for (int parameter = 0; parameter < 8; parameter++)
            {
                values[parameter + 1] = parameter < _curves.Count
                    ? NormalizeCurve(_curves[parameter])[level]
                    : 0;
            }
            values[9] = ActorClassMutationService.ExperienceForLevel(level, _expParameters).ToString("N0", CultureInfo.CurrentCulture);
            _levelPreview.Rows.Add(values);
        }
        _expGraph.Parameters = _expParameters;
    }

    private void SetWorkingCurve(int parameterId, IReadOnlyList<int> values)
    {
        List<IReadOnlyList<int>> updated = _curves.Select(curve => (IReadOnlyList<int>)NormalizeCurve(curve)).ToList();
        while (updated.Count < 8) updated.Add(new int[100]);
        updated[parameterId] = NormalizeCurve(values);
        _curves = updated;
    }

    private int[] CurrentExpParameters() =>
    [
        decimal.ToInt32(_basis.Value),
        decimal.ToInt32(_extra.Value),
        decimal.ToInt32(_accelerationA.Value),
        decimal.ToInt32(_accelerationB.Value)
    ];

    private void SetExpControls(IReadOnlyList<int> values)
    {
        _basis.Value = Math.Clamp(values[0], decimal.ToInt32(_basis.Minimum), decimal.ToInt32(_basis.Maximum));
        _extra.Value = Math.Clamp(values[1], decimal.ToInt32(_extra.Minimum), decimal.ToInt32(_extra.Maximum));
        _accelerationA.Value = Math.Clamp(values[2], decimal.ToInt32(_accelerationA.Minimum), decimal.ToInt32(_accelerationA.Maximum));
        _accelerationB.Value = Math.Clamp(values[3], decimal.ToInt32(_accelerationB.Minimum), decimal.ToInt32(_accelerationB.Maximum));
        _expGraph.Parameters = values;
    }

    private static int[] NormalizeExp(IReadOnlyList<int> source)
    {
        int[] result = [30, 20, 30, 30];
        for (int index = 0; index < Math.Min(4, source.Count); index++) result[index] = source[index];
        result[0] = Math.Clamp(result[0], 1, 9999);
        result[1] = Math.Clamp(result[1], 0, 9999);
        result[2] = Math.Clamp(result[2], 0, 100);
        result[3] = Math.Clamp(result[3], 1, 100);
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<int>> NormalizeCurves(IReadOnlyList<IReadOnlyList<int>> source)
    {
        List<IReadOnlyList<int>> result = [];
        for (int parameter = 0; parameter < 8; parameter++)
        {
            result.Add(parameter < source.Count ? NormalizeCurve(source[parameter]) : new int[100]);
        }
        return result;
    }

    private static int[] NormalizeCurve(IReadOnlyList<int> source)
    {
        int[] result = new int[100];
        for (int index = 0; index < Math.Min(100, source.Count); index++) result[index] = Math.Max(0, source[index]);
        return result;
    }

    private static NumericUpDown ExpNumber(int minimum, int maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill
    };

    private static void AddRow(
        TableLayoutPanel table,
        int row,
        string label1,
        Control control1,
        string label2,
        Control control2)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(LabelFor(label1), 0, row);
        table.Controls.Add(control1, 1, row);
        table.Controls.Add(LabelFor(label2), 2, row);
        table.Controls.Add(control2, 3, row);
        control1.Margin = new Padding(3, 3, 12, 8);
        control2.Margin = new Padding(3, 3, 3, 8);
    }

    private static Label LabelFor(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 8, 8, 3)
    };

    private sealed record ParameterItem(int Id, string Name)
    {
        public override string ToString() => Name;
    }
}

public sealed class ClassLearningEditor : UserControl
{
    private readonly MvProjectSession _session;
    private readonly ActorClassMutationService _mutations;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true
    };
    private readonly Button _add = new() { Text = "Add", AutoSize = true };
    private readonly Button _delete = new() { Text = "Delete", AutoSize = true };
    private readonly Button _duplicate = new() { Text = "Duplicate", AutoSize = true };
    private readonly Button _apply = new() { Text = "Apply Learnings", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
    private readonly SkillLearningTimelineControl _timeline = new() { Dock = DockStyle.Fill };
    private string? _relativePath;
    private int _classId;
    private List<MvClassLearningSnapshot> _source = [];
    private IReadOnlyList<DatabaseChoice> _skills = [];

    public ClassLearningEditor(MvProjectSession session, ActorClassMutationService mutations)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Level", HeaderText = "Level", FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Skill", HeaderText = "Skill", FillWeight = 80 });
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6), WrapContents = false };
        toolbar.Controls.AddRange([_add, _duplicate, _delete, _apply, _status]);
        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Horizontal, 300, 180, 120);
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_timeline);
        Controls.Add(split);
        Controls.Add(toolbar);
        _add.Click += (_, _) =>
        {
            int skillId = FirstSkillId();
            AddRow(new MvClassLearningSnapshot(1, skillId, $"{{\"level\":1,\"skillId\":{skillId.ToString(CultureInfo.InvariantCulture)}}}"));
            UpdateStatus();
        };
        _duplicate.Click += (_, _) => DuplicateRows();
        _delete.Click += (_, _) => DeleteRows();
        _apply.Click += async (_, _) => await ApplyAsync().ConfigureAwait(true);
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, _) => UpdateStatus();
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    public async ValueTask BindAsync(
        string relativePath,
        int classId,
        IReadOnlyList<MvClassLearningSnapshot> learnings,
        CancellationToken cancellationToken = default)
    {
        _relativePath = relativePath;
        _classId = classId;
        _source = learnings.ToList();
        _skills = await DatabaseChoiceLoader.LoadAsync(_session, MvDocumentKind.Skills, cancellationToken).ConfigureAwait(true);
        Populate();
    }

    public void ClearBinding()
    {
        _relativePath = null;
        _classId = 0;
        _source.Clear();
        _grid.Rows.Clear();
        _status.Text = "No class selected.";
        _timeline.SetData([], []);
    }

    private void Populate()
    {
        _grid.Rows.Clear();
        foreach (MvClassLearningSnapshot learning in _source) AddRow(learning);
        UpdateStatus();
    }

    private void AddRow(MvClassLearningSnapshot learning)
    {
        int rowIndex = _grid.Rows.Add();
        DataGridViewNumericUpDownCell level = new(1, 99, learning.Level);
        List<DatabaseChoice> choices = [new DatabaseChoice(0, "None"), .. _skills];
        if (learning.SkillId > 0 && choices.All(item => item.Id != learning.SkillId))
        {
            choices.Add(new DatabaseChoice(learning.SkillId, $"(Missing skill {learning.SkillId})"));
        }
        DataGridViewComboBoxCell skill = new()
        {
            DisplayMember = nameof(DatabaseChoice.DisplayName),
            ValueMember = nameof(DatabaseChoice.Id),
            DataSource = choices,
            FlatStyle = FlatStyle.Flat
        };
        _grid.Rows[rowIndex].Cells[0] = level;
        _grid.Rows[rowIndex].Cells[1] = skill;
        skill.Value = learning.SkillId;
        _grid.Rows[rowIndex].Tag = learning.RawJson;
    }

    private void DuplicateRows()
    {
        DataGridViewRow[] rows = _grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(row => row.Index).ToArray();
        foreach (DataGridViewRow row in rows)
        {
            AddRow(ReadRow(row));
        }
        UpdateStatus();
    }

    private void DeleteRows()
    {
        foreach (DataGridViewRow row in _grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(row => row.Index))
        {
            _grid.Rows.RemoveAt(row.Index);
        }
        UpdateStatus();
    }

    private async ValueTask ApplyAsync()
    {
        if (_relativePath is null || _classId <= 0) return;
        try
        {
            List<MvClassLearningSnapshot> learnings = _grid.Rows.Cast<DataGridViewRow>().Select(ReadRow).ToList();
            (int Level, int Skill)[] duplicates = learnings
                .GroupBy(item => (item.Level, item.SkillId))
                .Where(group => group.Count() > 1)
                .Select(group => (group.Key.Level, group.Key.SkillId))
                .ToArray();
            if (duplicates.Length > 0
                && MessageBox.Show(
                    this,
                    $"There are {duplicates.Length} duplicate level/skill learning pair(s). Preserve them?",
                    "Duplicate learnings",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            await _mutations.SetClassLearningsAsync(_relativePath, _classId, learnings).ConfigureAwait(true);
            _source = learnings;
            UpdateStatus();
            _status.Text = $"Applied {learnings.Count} learning(s) as one undoable transaction.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Learning update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private MvClassLearningSnapshot ReadRow(DataGridViewRow row)
    {
        int level = row.Cells[0] is DataGridViewNumericUpDownCell numeric
            ? numeric.IntegerValue
            : int.TryParse(Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture), out int parsedLevel) ? parsedLevel : 1;
        int skillId = row.Cells[1].Value is int id
            ? id
            : int.TryParse(Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture), out int parsedId) ? parsedId : 0;
        string raw = row.Tag as string ?? "{\"level\":1,\"skillId\":1}";
        return new MvClassLearningSnapshot(Math.Clamp(level, 1, 99), Math.Max(0, skillId), raw);
    }

    private int FirstSkillId() => _skills.FirstOrDefault()?.Id ?? 1;

    private void UpdateStatus()
    {
        _status.Text = $"{_grid.Rows.Count} learning(s). Unknown properties in existing learning entries are preserved.";
        IReadOnlyList<MvClassLearningSnapshot> timeline = _grid.Rows
            .Cast<DataGridViewRow>()
            .Select(ReadRow)
            .OrderBy(item => item.Level)
            .ThenBy(item => item.SkillId)
            .ToArray();
        _timeline.SetData(timeline, _skills);
    }
}

/// <summary>A numeric cell without a third-party dependency.</summary>
public sealed class DataGridViewNumericUpDownCell : DataGridViewTextBoxCell
{
    private readonly int _minimum;
    private readonly int _maximum;

    public DataGridViewNumericUpDownCell(int minimum, int maximum, int value)
    {
        _minimum = minimum;
        _maximum = maximum;
        Value = Math.Clamp(value, minimum, maximum);
        Style.Alignment = DataGridViewContentAlignment.MiddleRight;
    }

    public int IntegerValue => int.TryParse(
        Convert.ToString(Value, CultureInfo.InvariantCulture),
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out int value)
        ? Math.Clamp(value, _minimum, _maximum)
        : _minimum;
}

public sealed class ExpCurveGraph : Control
{
    private IReadOnlyList<int> _parameters = [30, 20, 30, 30];

    public ExpCurveGraph()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
    }

    public IReadOnlyList<int> Parameters
    {
        get => _parameters;
        set
        {
            _parameters = value?.ToArray() ?? [30, 20, 30, 30];
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        Rectangle plot = Rectangle.Inflate(ClientRectangle, -45, -28);
        if (plot.Width <= 10 || plot.Height <= 10) return;
        long[] experience = Enumerable.Range(1, 99)
            .Select(level => ActorClassMutationService.ExperienceForLevel(level, _parameters))
            .ToArray();
        long maximum = Math.Max(1, experience.Max());
        using Pen axis = new(SystemColors.ControlDark);
        using Pen curve = new(SystemColors.Highlight, 2f);
        e.Graphics.DrawLine(axis, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        e.Graphics.DrawLine(axis, plot.Left, plot.Top, plot.Left, plot.Bottom);
        PointF[] points = new PointF[99];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = new PointF(
                plot.Left + index * plot.Width / 98f,
                plot.Bottom - experience[index] * plot.Height / (float)maximum);
        }
        if (points.Length > 1) e.Graphics.DrawLines(curve, points);
        TextRenderer.DrawText(e.Graphics, "Level 1", Font, new Point(plot.Left, plot.Bottom + 4), ForeColor);
        TextRenderer.DrawText(e.Graphics, "Level 99", Font, new Point(plot.Right - 55, plot.Bottom + 4), ForeColor);
        TextRenderer.DrawText(e.Graphics, maximum.ToString("N0", CultureInfo.CurrentCulture), Font, new Point(2, plot.Top - 6), ForeColor);
        TextRenderer.DrawText(e.Graphics, "Total EXP", Font, new Point(plot.Left + 5, plot.Top + 4), ForeColor);
    }
}

public sealed class SmoothCurveDialog : Form
{
    private readonly NumericUpDown _levelOne = new() { Minimum = 0, Maximum = 9_999_999, ThousandsSeparator = true };
    private readonly NumericUpDown _levelNinetyNine = new() { Minimum = 0, Maximum = 9_999_999, ThousandsSeparator = true };
    private readonly NumericUpDown _exponent = new() { Minimum = 0.1m, Maximum = 8m, DecimalPlaces = 2, Increment = 0.05m, Value = 1m };

    public SmoothCurveDialog(string parameterName, int levelOne, int levelNinetyNine)
    {
        Text = $"Generate {parameterName} Curve";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 190);
        _levelOne.Value = Math.Clamp(levelOne, 0, decimal.ToInt32(_levelOne.Maximum));
        _levelNinetyNine.Value = Math.Clamp(levelNinetyNine, 0, decimal.ToInt32(_levelNinetyNine.Maximum));
        TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Add(table, 0, "Level 1 value", _levelOne);
        Add(table, 1, "Level 99 value", _levelNinetyNine);
        Add(table, 2, "Growth exponent", _exponent);
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft };
        Button okay = new() { Text = "Generate", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([okay, cancel]);
        Controls.Add(table);
        Controls.Add(buttons);
        AcceptButton = okay;
        CancelButton = cancel;
    }

    public int LevelOne => decimal.ToInt32(_levelOne.Value);
    public int LevelNinetyNine => decimal.ToInt32(_levelNinetyNine.Value);
    public double Exponent => decimal.ToDouble(_exponent.Value);

    private static void Add(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, 0, row);
        table.Controls.Add(control, 1, row);
    }
}

public sealed class CurveCopyDialog : Form
{
    private readonly ComboBox _class = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _parameter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };

    public CurveCopyDialog(IReadOnlyList<DatabaseChoice> classes, int currentClassId, int currentParameterId)
    {
        Text = "Copy Parameter Curve";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 150);
        foreach (DatabaseChoice item in classes) _class.Items.Add(item);
        for (int id = 0; id < 8; id++) _parameter.Items.Add(new ParameterChoice(id, MvParameterNames.Get(id)));
        if (_class.Items.Count > 0)
        {
            int classIndex = _class.Items.Cast<DatabaseChoice>().ToList().FindIndex(item => item.Id == currentClassId);
            _class.SelectedIndex = classIndex >= 0 ? classIndex : 0;
        }
        _parameter.SelectedIndex = Math.Clamp(currentParameterId, 0, 7);
        TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label { Text = "Source class", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_class, 1, 0);
        table.Controls.Add(new Label { Text = "Parameter", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_parameter, 1, 1);
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), FlowDirection = FlowDirection.RightToLeft };
        Button okay = new() { Text = "Copy", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([okay, cancel]);
        Controls.Add(table);
        Controls.Add(buttons);
        AcceptButton = okay;
        CancelButton = cancel;
    }

    public int SourceClassId => (_class.SelectedItem as DatabaseChoice)?.Id ?? 0;
    public int SourceParameterId => (_parameter.SelectedItem as ParameterChoice)?.Id ?? 0;

    private sealed record ParameterChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }
}
