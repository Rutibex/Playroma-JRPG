using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public sealed class CommandPaletteForm : Form
{
    private readonly ICommandService? _commands;
    private readonly IEditorCommandRouter? _router;
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Type a command…", Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 12f, SystemFonts.MessageBoxFont.Style), Height = 32 };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };

    public CommandPaletteForm(ICommandService commands) : this(commands, null) { }
    public CommandPaletteForm(IEditorCommandRouter router) : this(null, router) { }

    private CommandPaletteForm(ICommandService? commands, IEditorCommandRouter? router)
    {
        _commands = commands;
        _router = router;
        Text = "Command Palette";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 460);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Controls.Add(_list);
        Controls.Add(_search);
        _search.TextChanged += (_, _) => Rebuild();
        _search.KeyDown += OnKeyDown;
        _list.DoubleClick += async (_, _) => await ExecuteSelectedAsync().ConfigureAwait(true);
        Shown += (_, _) => { Rebuild(); _search.Focus(); };
        if (_router is not null) _router.CommandStatesChanged += OnCommandStateChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _router is not null) _router.CommandStatesChanged -= OnCommandStateChanged;
        base.Dispose(disposing);
    }

    private void OnCommandStateChanged(object? sender, EventArgs e)
    {
        if (Visible && !IsDisposed) BeginInvoke(new Action(Rebuild));
    }

    private void Rebuild()
    {
        string filter = _search.Text.Trim();
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            if (_router is not null)
            {
                foreach (EditorCommandDescriptor descriptor in _router.AvailableCommands
                    .Where(command => Matches(command.DisplayName, command.Id.Value, filter)))
                {
                    EditorCommandStatus status = _router.Query(descriptor.Id);
                    _list.Items.Add(new RoutedCommandItem(descriptor, status));
                }
            }
            else if (_commands is not null)
            {
                foreach (IShellCommand command in _commands.Commands.Where(command => Matches(command.DisplayName, command.Id, filter)))
                    _list.Items.Add(new ShellCommandItem(command, _commands.CanExecute(command.Id)));
            }
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }
        finally { _list.EndUpdate(); }
    }

    private static bool Matches(string name, string id, string filter) =>
        filter.Length == 0 || name.Contains(filter, StringComparison.CurrentCultureIgnoreCase) || id.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down) { if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Up) { if (_list.Items.Count > 0) _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; }
        else if (e.KeyCode == Keys.Enter) { await ExecuteSelectedAsync().ConfigureAwait(true); e.Handled = true; }
        else if (e.KeyCode == Keys.Escape) Close();
    }

    private async Task ExecuteSelectedAsync()
    {
        switch (_list.SelectedItem)
        {
            case RoutedCommandItem { Enabled: true } routed when _router is not null:
                Close();
                await _router.ExecuteAsync(routed.Descriptor.Id, CommandInvocationSource.CommandPalette).ConfigureAwait(true);
                break;
            case ShellCommandItem { Enabled: true } shell when _commands is not null:
                Close();
                await _commands.ExecuteAsync(shell.Command.Id).ConfigureAwait(true);
                break;
        }
    }

    private sealed record ShellCommandItem(IShellCommand Command, bool Enabled)
    {
        public override string ToString() => Enabled ? $"{Command.DisplayName}    {Command.DefaultShortcut}" : $"{Command.DisplayName}    (unavailable)";
    }

    private sealed record RoutedCommandItem(EditorCommandDescriptor Descriptor, EditorCommandStatus Status)
    {
        public bool Enabled => Status.Handling == CommandHandling.Enabled;
        public override string ToString()
        {
            string scope = string.IsNullOrWhiteSpace(Descriptor.Category) ? "Editor" : Descriptor.Category!;
            string shortcut = string.IsNullOrWhiteSpace(Descriptor.DefaultShortcut) ? string.Empty : "    " + Descriptor.DefaultShortcut;
            string availability = Enabled ? string.Empty : "    (unavailable" + (string.IsNullOrWhiteSpace(Status.DisabledReason) ? ")" : $": {Status.DisabledReason})");
            return $"[{scope}] {Status.DisplayName}{shortcut}{availability}";
        }
    }
}
