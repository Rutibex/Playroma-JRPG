using Playroma.JRPG.DatabaseEditing;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class DatabaseMaximumDialog : Form
{
    private readonly NumericUpDown _maximum = new() { Dock = DockStyle.Top, Minimum = 0, Maximum = 999999 };
    private readonly ListView _preview = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private readonly Label _warning = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(6), AutoEllipsis = true };
    private readonly Button _apply = new() { Text = "Apply", AutoSize = true };
    private readonly Button _remap = new() { Text = "Remap Exact References…", AutoSize = true, Visible = false };
    private readonly Func<int, DatabaseMaximumChangePlan> _planner;
    private Dictionary<int, int> _remappings = [];

    public DatabaseMaximumDialog(int currentMaximum, Func<int, DatabaseMaximumChangePlan> planner)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        Text = "Change Database Maximum";
        Width = 780;
        Height = 580;
        StartPosition = FormStartPosition.CenterParent;
        _maximum.Value = currentMaximum;
        Label prompt = new() { Text = $"Current maximum: {currentMaximum:N0}. Enter the new highest record ID:", Dock = DockStyle.Top, Height = 28 };
        _preview.Columns.Add("ID", 80);
        _preview.Columns.Add("State", 140);
        _preview.Columns.Add("Unknown fields", 110);
        _preview.Columns.Add("Raw preview", 390);
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(_apply);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_remap);
        Controls.Add(_preview);
        Controls.Add(_maximum);
        Controls.Add(prompt);
        Controls.Add(_warning);
        Controls.Add(buttons);
        CancelButton = cancel;
        _maximum.ValueChanged += (_, _) => { _remappings.Clear(); RefreshPlan(); };
        _remap.Click += (_, _) => EditRemappings();
        _apply.Click += (_, _) =>
        {
            Plan = _planner(decimal.ToInt32(_maximum.Value));
            if (!AllExactReferencesMapped(Plan)) return;
            DialogResult = DialogResult.OK;
        };
        RefreshPlan();
    }

    public DatabaseMaximumChangePlan? Plan { get; private set; }
    public IReadOnlyDictionary<int, int> Remappings => _remappings;

    private void EditRemappings()
    {
        DatabaseMaximumChangePlan plan = _planner(decimal.ToInt32(_maximum.Value));
        using DatabaseReferenceRemapDialog dialog = new(plan, _remappings);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _remappings = dialog.Remappings.ToDictionary(pair => pair.Key, pair => pair.Value);
        RefreshPlan();
    }

    private void RefreshPlan()
    {
        DatabaseMaximumChangePlan plan = _planner(decimal.ToInt32(_maximum.Value));
        RemoveInvalidRemappings(plan);
        _preview.BeginUpdate();
        _preview.Items.Clear();
        foreach (int id in plan.AddedSlots.Take(500)) _preview.Items.Add(new ListViewItem([id.ToString(), "New empty slot", "0", "null"]));
        foreach (RemovedDatabaseSlot slot in plan.RemovedSlots.Take(500))
        {
            string raw = slot.RawJson.Length <= 200 ? slot.RawJson : slot.RawJson[..200] + "…";
            string state = slot.IsPopulated ? "Populated removal" : "Empty removal";
            if (_remappings.TryGetValue(slot.Id, out int destination)) state += $" → {destination}";
            _preview.Items.Add(new ListViewItem([slot.Id.ToString(), state, slot.UnknownPropertyCount.ToString(), raw]));
        }
        _preview.EndUpdate();

        int unmapped = UnmappedExactSourceIds(plan).Count;
        _remap.Visible = plan.ExactReferences.Count > 0;
        _remap.Enabled = plan.IsReduction && plan.RequestedMaximum > 0;
        if (unmapped > 0)
        {
            _warning.Text = $"Blocked by {plan.ExactReferences.Count:N0} exact reference(s) across {unmapped:N0} removed ID(s). Remap every removed ID before applying.";
            _warning.ForeColor = Color.DarkRed;
            _apply.Enabled = false;
        }
        else if (plan.ExactReferences.Count > 0)
        {
            _warning.Text = $"{plan.ExactReferences.Count:N0} exact reference(s) will be remapped transactionally. Heuristic references remain report-only.";
            _warning.ForeColor = Color.DarkOrange;
            _apply.Enabled = true;
        }
        else if (plan.HasPopulatedRemovals)
        {
            _warning.Text = "This removes populated records. Their exact raw data will remain available to Undo, but the operation requires explicit confirmation.";
            _warning.ForeColor = Color.DarkOrange;
            _apply.Enabled = true;
        }
        else
        {
            _warning.Text = plan.IsExpansion ? $"This adds {plan.AddedSlots.Count:N0} empty slot(s)." : plan.IsReduction ? $"This removes {plan.RemovedSlots.Count:N0} empty slot(s)." : "No change.";
            _warning.ForeColor = SystemColors.ControlText;
            _apply.Enabled = plan.RequestedMaximum != plan.CurrentMaximum;
        }
    }

    private void RemoveInvalidRemappings(DatabaseMaximumChangePlan plan)
    {
        HashSet<int> removed = plan.RemovedSlots.Select(slot => slot.Id).ToHashSet();
        foreach (int key in _remappings.Keys.Where(key => !removed.Contains(key) || _remappings[key] < 1 || _remappings[key] > plan.RequestedMaximum).ToArray())
        {
            _remappings.Remove(key);
        }
    }

    private IReadOnlyList<int> UnmappedExactSourceIds(DatabaseMaximumChangePlan plan) => plan.ExactReferences
        .Select(reference => reference.Target.Id)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .Distinct()
        .Where(id => !_remappings.ContainsKey(id))
        .OrderBy(id => id)
        .ToArray();

    private bool AllExactReferencesMapped(DatabaseMaximumChangePlan plan) => UnmappedExactSourceIds(plan).Count == 0;
}

internal sealed class DatabaseReferenceRemapDialog : Form
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly int _maximum;

    public DatabaseReferenceRemapDialog(DatabaseMaximumChangePlan plan, IReadOnlyDictionary<int, int> existing)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(existing);
        _maximum = plan.RequestedMaximum;
        Text = "Remap References from Removed IDs";
        Width = 680;
        Height = 460;
        StartPosition = FormStartPosition.CenterParent;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Removed ID", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "References", HeaderText = "Exact references", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Destination", HeaderText = $"Destination ID (1–{_maximum})" });
        foreach (IGrouping<int, Playroma.JRPG.Indexing.ProjectReference> group in plan.ExactReferences
                     .Where(reference => reference.Target.Id.HasValue)
                     .GroupBy(reference => reference.Target.Id!.Value)
                     .OrderBy(group => group.Key))
        {
            int row = _grid.Rows.Add(group.Key, group.Count(), existing.TryGetValue(group.Key, out int destination) ? destination : 1);
            _grid.Rows[row].Tag = group.Key;
        }
        Label explanation = new()
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(8),
            Text = "Choose a surviving destination for each removed ID. Only exact indexed references are changed; heuristic note, script and plugin references are never rewritten automatically."
        };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button ok = new() { Text = "Use Remapping", AutoSize = true };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Accept();
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        Controls.Add(_grid);
        Controls.Add(explanation);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    public IReadOnlyDictionary<int, int> Remappings { get; private set; } = new Dictionary<int, int>();

    private void Accept()
    {
        _grid.EndEdit();
        Dictionary<int, int> result = [];
        foreach (DataGridViewRow row in _grid.Rows)
        {
            int source = (int)(row.Tag ?? 0);
            if (!int.TryParse(Convert.ToString(row.Cells["Destination"].Value, System.Globalization.CultureInfo.InvariantCulture), out int destination)
                || destination < 1 || destination > _maximum)
            {
                MessageBox.Show(this, $"Destination for removed ID {source} must be between 1 and {_maximum}.", "Invalid remapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            result[source] = destination;
        }
        Remappings = result;
        DialogResult = DialogResult.OK;
    }
}
