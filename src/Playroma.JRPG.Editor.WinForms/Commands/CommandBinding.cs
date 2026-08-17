using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Commands;

public sealed class CommandBinding : IDisposable
{
    private readonly ToolStripItem _item;
    private readonly EditorCommandId _command;
    private readonly IEditorCommandRouter _router;
    private bool _disposed;

    public CommandBinding(ToolStripItem item, EditorCommandId command, IEditorCommandRouter router)
    {
        _item = item;
        _command = command;
        _router = router;
        _item.Click += OnClick;
        _router.CommandStatesChanged += OnStateChanged;
        Refresh();
    }

    public void Refresh()
    {
        EditorCommandStatus status = _router.Query(_command);
        _item.Enabled = status.Handling == CommandHandling.Enabled;
        _item.Text = status.DisplayName;
        _item.ToolTipText = status.DisabledReason ?? status.Description ?? string.Empty;
        if (_item is ToolStripMenuItem menu) menu.Checked = status.IsChecked;
    }

    private void OnClick(object? sender, EventArgs e) =>
        _ = _router.ExecuteAsync(_command, CommandInvocationSource.Menu);
    private void OnStateChanged(object? sender, EventArgs e) => Refresh();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _item.Click -= OnClick;
        _router.CommandStatesChanged -= OnStateChanged;
    }
}
