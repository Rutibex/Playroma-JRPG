using Playroma.JRPG.Editor.Shell;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public enum RecoveryDecision { Cancel, Open, Discard }
public sealed class RecoveryDialog : Form
{
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    public RecoveryDialog(IReadOnlyList<RecoveryCandidate> candidates)
    {
        Text = "Recovered Work"; StartPosition = FormStartPosition.CenterParent; Size = new Size(680, 420); MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        Label message = new() { Text = "Playroma found unsaved work from a previous session. Select a recovery workspace.", Dock = DockStyle.Top, Height = 48, Padding = new Padding(12), AutoEllipsis = true };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8), WrapContents = false };
        Button cancel = new() { Text = "Later", AutoSize = true }; Button discard = new() { Text = "Discard", AutoSize = true }; Button open = new() { Text = "Open Recovered Workspace", AutoSize = true };
        buttons.Controls.AddRange([cancel, discard, open]); Controls.Add(_list); Controls.Add(buttons); Controls.Add(message);
        foreach (RecoveryCandidate candidate in candidates) _list.Items.Add(new RecoveryItem(candidate)); if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        cancel.Click += (_, _) => { Decision = RecoveryDecision.Cancel; DialogResult = DialogResult.Cancel; };
        discard.Click += (_, _) => { if (Selected is not null) { Decision = RecoveryDecision.Discard; DialogResult = DialogResult.OK; } };
        open.Click += (_, _) => { if (Selected is not null) { Decision = RecoveryDecision.Open; DialogResult = DialogResult.OK; } };
        AcceptButton = open; CancelButton = cancel;
    }
    public RecoveryDecision Decision { get; private set; }
    public RecoveryCandidate? Selected => (_list.SelectedItem as RecoveryItem)?.Candidate;
    private sealed record RecoveryItem(RecoveryCandidate Candidate)
    {
        public override string ToString() => $"{Candidate.Manifest.ProjectTitle ?? Path.GetFileName(Candidate.Manifest.ProjectRoot)} — {Candidate.Manifest.CreatedUtc.LocalDateTime:g} — {Candidate.Manifest.Documents.Count} changed files, {Candidate.Manifest.PrivateSessionCount} editor sessions\n{Candidate.Manifest.ProjectRoot}";
    }
}
