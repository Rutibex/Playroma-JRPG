using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Dialogs;

/// <summary>
/// One project-close decision for all private edit sessions and shared project mutations.
/// Merely showing this dialog does not mutate any editor state.
/// </summary>
public sealed class CoordinatedShutdownDialog : Form
{
    private readonly RadioButton _localApply = new()
    {
        Text = "Apply all local editor sessions to the shared project",
        AutoSize = true,
        Checked = true
    };
    private readonly RadioButton _localDiscard = new()
    {
        Text = "Discard all unapplied local editor sessions",
        AutoSize = true
    };
    private readonly RadioButton _localRecover = new()
    {
        Text = "Preserve local editor sessions in recovery",
        AutoSize = true
    };
    private readonly RadioButton _projectSave = new()
    {
        Text = "Save the shared project changes",
        AutoSize = true,
        Checked = true
    };
    private readonly RadioButton _projectDiscard = new()
    {
        Text = "Discard the shared project changes",
        AutoSize = true
    };
    private readonly RadioButton _projectRecover = new()
    {
        Text = "Preserve the shared project changes in recovery",
        AutoSize = true
    };

    public CoordinatedShutdownDialog(
        string projectTitle,
        CoordinatedShutdownPlan plan,
        bool hasSharedProjectChanges,
        bool exitingApplication)
    {
        ArgumentNullException.ThrowIfNull(plan);
        Text = exitingApplication ? "Exit Playroma" : "Close Project";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(700, 520);
        Size = new Size(780, 620);

        Label heading = new()
        {
            Text = exitingApplication
                ? $"Choose how to close '{projectTitle}' before exiting Playroma."
                : $"Choose how to close '{projectTitle}'.",
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Padding = new Padding(12)
        };

        ListView pending = new()
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false
        };
        pending.Columns.Add("Editor", 250);
        pending.Columns.Add("Pending work", 390);
        foreach (ShutdownContribution contribution in plan.Contributions)
        {
            ListViewItem item = new(contribution.Title);
            item.SubItems.Add(contribution.Description);
            pending.Items.Add(item);
        }
        if (plan.Contributions.Count == 0)
        {
            ListViewItem item = new("No private editor sessions");
            item.SubItems.Add("All editor-local changes have already been applied or discarded.");
            pending.Items.Add(item);
        }

        GroupBox localGroup = new()
        {
            Text = "Unapplied editor sessions",
            Dock = DockStyle.Top,
            Height = 118,
            Padding = new Padding(12)
        };
        FlowLayoutPanel localChoices = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        localChoices.Controls.AddRange([_localApply, _localDiscard, _localRecover]);
        localGroup.Controls.Add(localChoices);
        localGroup.Enabled = plan.HasLocalChanges;
        _localApply.Enabled = plan.CanApplyAll;
        _localDiscard.Enabled = plan.CanDiscardAll;
        _localRecover.Enabled = plan.CanRecoverAll;
        if (!plan.CanApplyAll) _localApply.Text += " (not available for every open editor)";
        if (!plan.CanDiscardAll) _localDiscard.Text += " (not available for every open editor)";
        if (!plan.CanRecoverAll) _localRecover.Text += " (not available for every open editor)";
        if (plan.HasLocalChanges)
        {
            if (plan.CanApplyAll) _localApply.Checked = true;
            else if (plan.CanRecoverAll) _localRecover.Checked = true;
            else if (plan.CanDiscardAll) _localDiscard.Checked = true;
        }

        GroupBox projectGroup = new()
        {
            Text = "Shared project state",
            Dock = DockStyle.Top,
            Height = 118,
            Padding = new Padding(12)
        };
        FlowLayoutPanel projectChoices = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        projectChoices.Controls.AddRange([_projectSave, _projectDiscard, _projectRecover]);
        projectGroup.Controls.Add(projectChoices);
        projectGroup.Enabled = hasSharedProjectChanges || plan.HasLocalChanges;

        Button proceed = new()
        {
            Text = exitingApplication ? "Continue and Exit" : "Continue and Close",
            DialogResult = DialogResult.OK,
            AutoSize = true
        };
        Button cancel = new()
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true
        };
        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(10)
        };
        buttons.Controls.AddRange([cancel, proceed]);

        Panel body = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
        body.Controls.Add(pending);
        body.Controls.Add(projectGroup);
        body.Controls.Add(localGroup);
        Controls.Add(body);
        Controls.Add(buttons);
        Controls.Add(heading);
        AcceptButton = proceed;
        CancelButton = cancel;
    }

    public ShutdownLocalAction LocalAction => _localRecover.Checked
        ? ShutdownLocalAction.PreserveRecovery
        : _localDiscard.Checked
            ? ShutdownLocalAction.Discard
            : ShutdownLocalAction.Apply;

    public ShutdownProjectAction ProjectAction => _projectRecover.Checked
        ? ShutdownProjectAction.PreserveRecovery
        : _projectDiscard.Checked
            ? ShutdownProjectAction.Discard
            : ShutdownProjectAction.Save;
}
