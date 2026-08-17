using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public sealed class ProjectProgressForm : Form, IProgress<ProjectOpenProgress>
{
    private readonly Label _message = new() { Dock = DockStyle.Top, Height = 42, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 12, 0) };
    private readonly Label _item = new() { Dock = DockStyle.Top, Height = 24, AutoEllipsis = true, Padding = new Padding(12, 0, 12, 0), ForeColor = SystemColors.GrayText };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 22, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25 };
    private readonly Button _cancel = new() { Text = "Cancel", Dock = DockStyle.Right, Width = 90 };
    private readonly CancellationTokenSource _cancellation = new();
    public ProjectProgressForm(string title)
    {
        Text = title; StartPosition = FormStartPosition.CenterParent; Size = new Size(560, 180); FormBorderStyle = FormBorderStyle.FixedDialog; MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false; ControlBox = false;
        Panel buttons = new() { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) }; buttons.Controls.Add(_cancel);
        Controls.Add(buttons); Controls.Add(_progress); Controls.Add(_item); Controls.Add(_message);
        _cancel.Click += (_, _) => { _cancel.Enabled = false; _message.Text = "Cancelling…"; _cancellation.Cancel(); };
    }
    public CancellationToken CancellationToken => _cancellation.Token;
    public void Report(ProjectOpenProgress value)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => Report(value))); return; }
        _message.Text = value.Message ?? value.Stage.ToString(); _item.Text = value.CurrentItem ?? string.Empty;
        if (value.Total > 0) { _progress.Style = ProgressBarStyle.Continuous; _progress.Maximum = value.Total; _progress.Value = Math.Clamp(value.Completed, 0, value.Total); }
        else { _progress.Style = ProgressBarStyle.Marquee; }
    }
    protected override void Dispose(bool disposing) { if (disposing) _cancellation.Dispose(); base.Dispose(disposing); }
}
