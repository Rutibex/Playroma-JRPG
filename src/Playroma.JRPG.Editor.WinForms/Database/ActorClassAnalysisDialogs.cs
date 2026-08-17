using System.Globalization;
using Playroma.JRPG.DatabaseEditing;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class EquipmentLegalityReportDialog : Form
{
    public EquipmentLegalityReportDialog(ActorEquipmentLegalityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Text = $"Actor {report.ActorId:D4} Equipment Legality";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 420);
        Size = new Size(980, 560);

        Label summary = new()
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(8),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"Class {report.ClassId:D4} • {(report.IsDualWield ? "Dual wield" : "Normal slots")} • {report.ProblemCount} problem(s)"
        };
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        grid.Columns.Add("Slot", "Slot");
        grid.Columns.Add("Expected", "Expected Type");
        grid.Columns.Add("Item", "Assigned Item");
        grid.Columns.Add("Kind", "Kind");
        grid.Columns.Add("Status", "Status");
        grid.Columns[0].FillWeight = 12;
        grid.Columns[1].FillWeight = 22;
        grid.Columns[2].FillWeight = 25;
        grid.Columns[3].FillWeight = 12;
        grid.Columns[4].FillWeight = 45;
        foreach (ActorEquipmentLegalityEntry entry in report.Entries)
        {
            int row = grid.Rows.Add(
                entry.SlotIndex + 1,
                $"{entry.ExpectedEquipTypeId}: {entry.ExpectedEquipTypeName}",
                entry.ItemId == 0 ? "None" : $"{entry.ItemId:D4} {entry.ItemName}",
                entry.ItemKind,
                entry.Message);
            if (!entry.IsLegal)
            {
                grid.Rows[row].DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        }

        Button close = new() { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(close);
        Controls.Add(grid);
        Controls.Add(summary);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
    }
}

public sealed class ClassComparisonDialog : Form
{
    private readonly MvClassRecordSnapshot _current;
    private readonly IReadOnlyDictionary<int, MvClassRecordSnapshot> _classes;
    private readonly ComboBox _comparison = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    public ClassComparisonDialog(
        MvClassRecordSnapshot current,
        IReadOnlyDictionary<int, MvClassRecordSnapshot> classes)
    {
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _classes = classes ?? throw new ArgumentNullException(nameof(classes));
        Text = $"Compare Class {_current.Id:D4} {_current.Name}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 520);
        Size = new Size(980, 680);

        _grid.Columns.Add("Metric", "Metric");
        _grid.Columns.Add("Current", $"{_current.Id:D4} {_current.Name}");
        _grid.Columns.Add("Other", "Comparison Class");
        _grid.Columns.Add("Delta", "Delta");
        _grid.Columns[0].FillWeight = 42;
        _grid.Columns[1].FillWeight = 24;
        _grid.Columns[2].FillWeight = 24;
        _grid.Columns[3].FillWeight = 18;

        foreach (MvClassRecordSnapshot item in classes.Values.OrderBy(item => item.Id))
        {
            if (item.Id != current.Id) _comparison.Items.Add(new ClassChoice(item.Id, item.Name));
        }
        _comparison.SelectedIndexChanged += (_, _) => Populate();

        TableLayoutPanel header = new()
        {
            Dock = DockStyle.Top,
            Height = 48,
            ColumnCount = 2,
            Padding = new Padding(8)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label { Text = "Compare against", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        header.Controls.Add(_comparison, 1, 0);

        Button close = new() { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(close);
        Controls.Add(_grid);
        Controls.Add(header);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
        if (_comparison.Items.Count > 0) _comparison.SelectedIndex = 0;
    }

    private void Populate()
    {
        _grid.Rows.Clear();
        if (_comparison.SelectedItem is not ClassChoice choice || !_classes.TryGetValue(choice.Id, out MvClassRecordSnapshot? other)) return;
        AddText("Name", _current.Name, other.Name);
        AddNumber("Trait entries", _current.TraitCount, other.TraitCount);
        AddNumber("Skill learnings", _current.Learnings.Count, other.Learnings.Count);
        foreach (int level in new[] { 10, 30, 50, 70, 99 })
        {
            long currentExp = ActorClassMutationService.ExperienceForLevel(level, _current.ExpParameters);
            long otherExp = ActorClassMutationService.ExperienceForLevel(level, other.ExpParameters);
            AddNumber($"Total EXP at level {level}", currentExp, otherExp);
        }
        foreach (int level in new[] { 1, 50, 99 })
        {
            for (int parameter = 0; parameter < 8; parameter++)
            {
                int currentValue = CurveValue(_current, parameter, level);
                int otherValue = CurveValue(other, parameter, level);
                AddNumber($"{MvParameterNames.Get(parameter)} at level {level}", currentValue, otherValue);
            }
        }
    }

    private void AddText(string metric, string current, string other) =>
        _grid.Rows.Add(metric, current, other, string.Equals(current, other, StringComparison.Ordinal) ? "Same" : "Different");

    private void AddNumber(string metric, long current, long other) =>
        _grid.Rows.Add(
            metric,
            current.ToString("N0", CultureInfo.CurrentCulture),
            other.ToString("N0", CultureInfo.CurrentCulture),
            (other - current).ToString("+0;-0;0", CultureInfo.CurrentCulture));

    private static int CurveValue(MvClassRecordSnapshot snapshot, int parameter, int level) =>
        parameter >= 0 && parameter < snapshot.ParameterCurves.Count
        && level >= 0 && level < snapshot.ParameterCurves[parameter].Count
            ? snapshot.ParameterCurves[parameter][level]
            : 0;

    private sealed record ClassChoice(int Id, string Name)
    {
        public override string ToString() => $"{Id:D4} {Name}";
    }
}

public sealed class SkillLearningTimelineControl : Control
{
    private IReadOnlyList<MvClassLearningSnapshot> _learnings = [];
    private IReadOnlyDictionary<int, string> _skillNames = new Dictionary<int, string>();

    public SkillLearningTimelineControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SystemColors.Window;
        ForeColor = SystemColors.WindowText;
        MinimumSize = new Size(300, 120);
    }

    public void SetData(
        IReadOnlyList<MvClassLearningSnapshot> learnings,
        IReadOnlyList<DatabaseChoice> skills)
    {
        _learnings = learnings.OrderBy(item => item.Level).ThenBy(item => item.SkillId).ToArray();
        _skillNames = skills.ToDictionary(item => item.Id, item => item.DisplayName);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        Rectangle track = new(45, 38, Math.Max(1, ClientSize.Width - 90), 2);
        using Pen axis = new(SystemColors.ControlDark, 2f);
        using Pen marker = new(SystemColors.Highlight, 2f);
        using Brush markerBrush = new SolidBrush(SystemColors.Highlight);
        e.Graphics.DrawLine(axis, track.Left, track.Top, track.Right, track.Top);
        TextRenderer.DrawText(e.Graphics, "1", Font, new Point(track.Left - 4, track.Top + 8), ForeColor);
        TextRenderer.DrawText(e.Graphics, "99", Font, new Point(track.Right - 12, track.Top + 8), ForeColor);
        int labelY = 62;
        foreach (MvClassLearningSnapshot learning in _learnings.Take(12))
        {
            int x = track.Left + (Math.Clamp(learning.Level, 1, 99) - 1) * track.Width / 98;
            e.Graphics.DrawLine(marker, x, track.Top - 8, x, track.Top + 8);
            e.Graphics.FillEllipse(markerBrush, x - 3, track.Top - 3, 6, 6);
            string name = _skillNames.TryGetValue(learning.SkillId, out string? found)
                ? found
                : $"Skill {learning.SkillId}";
            string text = $"Lv {learning.Level}: {name}";
            TextRenderer.DrawText(
                e.Graphics,
                text,
                Font,
                new Rectangle(8, labelY, Math.Max(1, ClientSize.Width - 16), Font.Height + 3),
                ForeColor,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            labelY += Font.Height + 4;
            if (labelY > ClientSize.Height - Font.Height) break;
        }
        if (_learnings.Count > 12)
        {
            TextRenderer.DrawText(e.Graphics, $"+ {_learnings.Count - 12} more learning(s)", Font, new Point(8, Math.Max(62, ClientSize.Height - Font.Height - 6)), SystemColors.GrayText);
        }
        if (_learnings.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "No learned skills", Font, new Point(8, 62), SystemColors.GrayText);
        }
    }
}
