using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Review surface for recovered modal/private edit sessions. Event editors are not reopened
/// during startup; the user explicitly chooses when to recreate each recovered session.
/// </summary>
public sealed class RecoveredSessionsForm : EditorToolForm
{
    private readonly IWinFormsWorkspaceRecoveryCoordinator _recovery;
    private readonly ListView _sessions = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false
    };
    private readonly Button _open = new() { Text = "Open Session", AutoSize = true };
    private readonly Button _discard = new() { Text = "Discard", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true };
    private readonly Label _message = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(12),
        Text = "Playroma recovered unapplied editor sessions. Open one to continue editing, or discard it."
    };

    public RecoveredSessionsForm(
        WindowRequest request,
        IWinFormsWorkspaceRecoveryCoordinator recovery)
        : base(request)
    {
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        Name = nameof(RecoveredSessionsForm);
        Text = request.DisplayTitle ?? "Recovered Editing Sessions";
        MinimumSize = new Size(680, 380);
        Size = new Size(820, 520);
        ShowInTaskbar = false;

        _sessions.Columns.Add("Editor", 250);
        _sessions.Columns.Add("Recovered", 160);
        _sessions.Columns.Add("Type", 180);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        buttons.Controls.AddRange([_close, _discard, _open]);
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_sessions);
        root.Controls.Add(buttons);
        root.Controls.Add(_message);
        InstallContent(root);

        _open.Click += async (_, _) => await OpenSelectedAsync().ConfigureAwait(true);
        _discard.Click += (_, _) => DiscardSelected();
        _close.Click += (_, _) => Close();
        _sessions.DoubleClick += async (_, _) => await OpenSelectedAsync().ConfigureAwait(true);
        _sessions.SelectedIndexChanged += (_, _) => UpdateButtons();
        _recovery.PendingRecoveredSessionsChanged += OnSessionsChanged;
    }

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Rebuild();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposeWindowAsync()
    {
        _recovery.PendingRecoveredSessionsChanged -= OnSessionsChanged;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(true);
    }

    private async Task OpenSelectedAsync()
    {
        if (_sessions.SelectedItems.Count == 0
            || _sessions.SelectedItems[0].Tag is not PrivateSessionRecoveryState state) return;
        _open.Enabled = false;
        bool opened = await _recovery.OpenRecoveredSessionAsync(state, this).ConfigureAwait(true);
        if (opened && _recovery.PendingRecoveredSessions.Count == 0) Close();
        else Rebuild();
    }

    private void DiscardSelected()
    {
        if (_sessions.SelectedItems.Count == 0
            || _sessions.SelectedItems[0].Tag is not PrivateSessionRecoveryState state) return;
        DialogResult answer = MessageBox.Show(
            this,
            $"Discard the recovered unapplied session '{state.DisplayTitle}'?",
            "Discard Recovered Session",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        _recovery.DiscardRecoveredSession(state);
        if (_recovery.PendingRecoveredSessions.Count == 0) Close();
        else Rebuild();
    }

    private void Rebuild()
    {
        _sessions.BeginUpdate();
        try
        {
            _sessions.Items.Clear();
            foreach (PrivateSessionRecoveryState state in _recovery.PendingRecoveredSessions)
            {
                ListViewItem item = new(state.DisplayTitle) { Tag = state };
                item.SubItems.Add(state.CapturedUtc.ToLocalTime().ToString("g"));
                item.SubItems.Add(state.SessionType);
                _sessions.Items.Add(item);
            }
        }
        finally
        {
            _sessions.EndUpdate();
        }
        _message.Text = _sessions.Items.Count == 0
            ? "No recovered editing sessions remain."
            : "Playroma recovered unapplied editor sessions. Open one to continue editing, or discard it.";
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool selected = _sessions.SelectedItems.Count > 0;
        _open.Enabled = selected;
        _discard.Enabled = selected;
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action(Rebuild));
        else Rebuild();
    }
}
