namespace Playroma.JRPG.Editor.WinForms.Dialogs;

public enum UnsavedProjectDecision
{
    Cancel,
    SaveAndClose,
    CloseWithoutSaving
}

public sealed class UnsavedProjectChangesDialog : Form
{
    private readonly CheckBox _preserveRecovery = new()
    {
        Text = "Keep a recovery copy of these unsaved changes",
        AutoSize = true,
        Checked = false,
        Margin = new Padding(0, 12, 0, 0)
    };

    public UnsavedProjectChangesDialog(string projectTitle, bool exitingApplication)
    {
        Text = exitingApplication ? "Exit Playroma jRPG" : "Close Project";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(590, 235);

        Label heading = new()
        {
            Text = "This project contains unsaved changes.",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10)
        };
        Label explanation = new()
        {
            Text = exitingApplication
                ? $"Save changes to {projectTitle} before exiting Playroma jRPG?"
                : $"Save changes to {projectTitle} before closing the project?",
            AutoSize = true,
            MaximumSize = new Size(520, 0)
        };

        FlowLayoutPanel body = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(24, 22, 24, 8)
        };
        body.Controls.Add(heading);
        body.Controls.Add(explanation);
        body.Controls.Add(_preserveRecovery);

        Button save = new() { Text = "Save and Close", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Button discard = new() { Text = "Close Without Saving", AutoSize = true, Padding = new Padding(10, 4, 10, 4) };
        Button cancel = new() { Text = "Cancel", AutoSize = true, Padding = new Padding(10, 4, 10, 4), DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => Complete(UnsavedProjectDecision.SaveAndClose);
        discard.Click += (_, _) => Complete(UnsavedProjectDecision.CloseWithoutSaving);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 58,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(12)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(discard);
        buttons.Controls.Add(save);

        Controls.Add(body);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public UnsavedProjectDecision Decision { get; private set; } = UnsavedProjectDecision.Cancel;
    public bool PreserveRecovery => _preserveRecovery.Checked;

    private void Complete(UnsavedProjectDecision decision)
    {
        Decision = decision;
        DialogResult = DialogResult.OK;
        Close();
    }
}
