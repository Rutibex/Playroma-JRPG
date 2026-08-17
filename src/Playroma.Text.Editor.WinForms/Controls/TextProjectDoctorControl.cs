using Playroma.Text.Authoring.Release;

namespace Playroma.Text.Editor.WinForms.Controls;

public sealed class TextProjectDoctorControl : UserControl
{
    private readonly string _projectRoot;
    private readonly ListView _issues = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly ToolStripLabel _status = new("Ready to audit Text Studio project assets.");
    public TextProjectDoctorControl(string projectRoot)
    {
        _projectRoot = projectRoot;
        Dock = DockStyle.Fill;
        _issues.Columns.Add("Severity", 80); _issues.Columns.Add("Area", 120); _issues.Columns.Add("Message", 480); _issues.Columns.Add("Path", 360);
        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        var audit = new ToolStripButton("Run Project Doctor");
        var preview = new ToolStripButton("Preview Migrations");
        var migrate = new ToolStripButton("Apply Safe Migrations");
        audit.Click += async (_, _) => await AuditAsync().ConfigureAwait(true);
        preview.Click += async (_, _) => await PreviewMigrationsAsync().ConfigureAwait(true);
        migrate.Click += async (_, _) => await ApplyMigrationsAsync().ConfigureAwait(true);
        strip.Items.AddRange([audit, preview, migrate, new ToolStripSeparator(), _status]);
        Controls.Add(_issues); Controls.Add(strip);
        AccessibleName = "Text Studio Project Doctor";
        AccessibleDescription = "Audits Text Studio project assets and applies conservative backed-up migrations.";
    }

    public async Task AuditAsync(CancellationToken cancellationToken = default)
    {
        _status.Text = "Auditing…";
        var report = await new TextProjectDoctor().AuditAsync(_projectRoot, cancellationToken).ConfigureAwait(true);
        _issues.Items.Clear();
        foreach (var issue in report.Issues)
        {
            var item = new ListViewItem(issue.Severity.ToString());
            item.SubItems.Add(issue.Area); item.SubItems.Add(issue.Message); item.SubItems.Add(issue.Path ?? string.Empty);
            _issues.Items.Add(item);
        }
        _status.Text = report.IsReleaseReady ? $"Release-ready: {report.WarningCount} warning(s)." : $"Not release-ready: {report.ErrorCount} error(s), {report.WarningCount} warning(s).";
    }

    private async Task PreviewMigrationsAsync()
    {
        var report = await new TextAssetMigrationService().PreviewAsync(_projectRoot).ConfigureAwait(true);
        ShowMigration(report, "Migration preview");
    }

    private async Task ApplyMigrationsAsync()
    {
        if (MessageBox.Show(this, "Apply canonical recipe migrations? Changed files receive pre-migration backups.", "Apply Text Studio migrations", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var report = await new TextAssetMigrationService().ApplyAsync(_projectRoot).ConfigureAwait(true);
        ShowMigration(report, "Migrations applied");
        await AuditAsync().ConfigureAwait(true);
    }

    private void ShowMigration(TextAssetMigrationReport report, string title)
    {
        _issues.Items.Clear();
        foreach (var entry in report.Items)
        {
            var item = new ListViewItem(entry.Changed ? "Change" : "OK");
            item.SubItems.Add("Migration"); item.SubItems.Add(entry.Message); item.SubItems.Add(entry.Path);
            _issues.Items.Add(item);
        }
        _status.Text = $"{title}: {report.ChangedCount} recipe(s) require/change migration.";
    }
}
