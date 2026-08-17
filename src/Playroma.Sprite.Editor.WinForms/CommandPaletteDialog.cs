namespace Playroma.Sprite.Editor.WinForms;

internal sealed record SpriteEditorCommand(string Id, string Name, string Category, Action Execute)
{
    public override string ToString() => $"{Category}  ·  {Name}";
}

internal sealed class CommandPaletteDialog : Form
{
    private readonly TextBox searchBox = new() { Dock = DockStyle.Top, PlaceholderText = "Type a command name..." };
    private readonly ListBox commandList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly IReadOnlyList<SpriteEditorCommand> commands;
    public SpriteEditorCommand? SelectedCommand => commandList.SelectedItem as SpriteEditorCommand;

    public CommandPaletteDialog(IEnumerable<SpriteEditorCommand> commands)
    {
        this.commands = commands.OrderBy(item => item.Category).ThenBy(item => item.Name).ToArray();
        Text = "Command Palette";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimumSize = new Size(520, 420);
        Size = new Size(680, 560);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        Controls.Add(commandList);
        Controls.Add(searchBox);
        searchBox.TextChanged += (_, _) => RefreshCommands();
        searchBox.KeyDown += SearchBox_KeyDown;
        commandList.DoubleClick += (_, _) => AcceptSelection();
        commandList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AcceptSelection(); e.Handled = true; } };
        Shown += (_, _) => { RefreshCommands(); searchBox.Focus(); };
    }

    private void RefreshCommands()
    {
        string query = searchBox.Text.Trim();
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        SpriteEditorCommand? previous = commandList.SelectedItem as SpriteEditorCommand;
        commandList.BeginUpdate();
        try
        {
            commandList.Items.Clear();
            foreach (SpriteEditorCommand command in commands)
            {
                string searchable = command.Category + " " + command.Name + " " + command.Id;
                if (words.All(word => searchable.Contains(word, StringComparison.OrdinalIgnoreCase))) commandList.Items.Add(command);
            }
            if (previous is not null && commandList.Items.Contains(previous)) commandList.SelectedItem = previous;
            else if (commandList.Items.Count > 0) commandList.SelectedIndex = 0;
        }
        finally { commandList.EndUpdate(); }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && commandList.Items.Count > 0)
        {
            commandList.SelectedIndex = Math.Min(commandList.Items.Count - 1, commandList.SelectedIndex + 1);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Up && commandList.Items.Count > 0)
        {
            commandList.SelectedIndex = Math.Max(0, commandList.SelectedIndex - 1);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter) { AcceptSelection(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
    }

    private void AcceptSelection()
    {
        if (SelectedCommand is null) return;
        DialogResult = DialogResult.OK;
        Close();
    }
}
