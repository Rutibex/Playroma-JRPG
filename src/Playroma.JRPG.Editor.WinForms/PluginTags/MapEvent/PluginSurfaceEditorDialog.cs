using Playroma.JRPG.Application;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

/// <summary>Hosts the shared Plugin Tags control for non-database text surfaces.</summary>
internal sealed class PluginSurfaceEditorDialog : Form
{
    private readonly PluginTextEditingSession session;
    private readonly PluginTagsEditorControl tags;
    private readonly TextBox raw = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private bool syncing;

    public PluginSurfaceEditorDialog(
        MvProjectSession project,
        PluginTagCatalog catalog,
        PluginTagContext context,
        string currentText,
        PluginTagProjectIntelligence intelligence,
        string title,
        string rawTabTitle)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(intelligence);
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = true;
        MinimumSize = new Size(900, 620);
        Size = new Size(1180, 790);

        session = new(catalog, context, currentText);
        tags = new(catalog, project);
        tags.ConfigureHost("Use Changes", "Open " + rawTabTitle, showRecipes: false);
        tags.Bind(session, intelligence.Analysis, intelligence.Availability, intelligence.ErrorMessage);
        raw.Text = currentText;

        tabs.TabPages.Add(new TabPage("Easy Plugin Tags") { Controls = { tags } });
        tabs.TabPages.Add(new TabPage(rawTabTitle) { Controls = { raw } });

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8),
            WrapContents = false
        };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        Button ok = new() { Text = "Use Changes", AutoSize = true };
        buttons.Controls.AddRange([cancel, ok]);
        Controls.Add(tabs);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        raw.TextChanged += (_, _) =>
        {
            if (syncing) return;
            syncing = true;
            try { session.ReplaceText(raw.Text); }
            finally { syncing = false; }
        };
        session.WorkingTextChanged += (_, args) =>
        {
            if (syncing || string.Equals(raw.Text, args.CurrentText, StringComparison.Ordinal)) return;
            syncing = true;
            try
            {
                int start = raw.SelectionStart;
                raw.Text = args.CurrentText;
                raw.SelectionStart = Math.Min(start, raw.TextLength);
            }
            finally { syncing = false; }
        };
        tags.OpenNotesRequested += (_, _) => tabs.SelectedIndex = 1;
        tags.ApplyRequested += (_, _) => Confirm();
        ok.Click += (_, _) => Confirm();
    }

    public string ResultText { get; private set; } = string.Empty;

    private void Confirm()
    {
        ResultText = session.WorkingText;
        DialogResult = DialogResult.OK;
        Close();
    }
}
