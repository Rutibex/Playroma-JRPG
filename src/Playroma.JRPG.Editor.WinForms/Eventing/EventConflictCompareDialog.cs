namespace Playroma.JRPG.Editor.WinForms.Eventing;

public sealed class EventConflictCompareDialog : Form
{
    public EventConflictCompareDialog(EventExternalConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        Text = "Compare Event Changes";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 560);
        Size = new Size(1280, 760);
        AutoScaleMode = AutoScaleMode.Dpi;

        TableLayoutPanel columns = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(8) };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        columns.Controls.Add(BuildPanel("Baseline", conflict.BaselineJson), 0, 0);
        columns.Controls.Add(BuildPanel("My Changes", conflict.WorkingJson), 1, 0);
        columns.Controls.Add(BuildPanel("External Version", conflict.ExternalJson), 2, 0);

        Button close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(close);
        Controls.Add(columns);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;
    }

    private static Control BuildPanel(string title, string text)
    {
        TextBox box = new()
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9),
            Text = text
        };
        GroupBox group = new() { Text = title, Dock = DockStyle.Fill, Padding = new Padding(6) };
        group.Controls.Add(box);
        return group;
    }
}
