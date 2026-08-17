using System.Diagnostics;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;

namespace Playroma.JRPG.Editor.WinForms.Documents;

/// <summary>
/// Reusable content surface for the standalone Project Hub. It intentionally raises requests
/// instead of opening projects itself so application lifetime remains centralized.
/// </summary>
public sealed class StartCentreControl : UserControl
{
    private readonly IRecentProjectService _recent;
    private readonly IRecoveryService _recovery;
    private readonly FlowLayoutPanel _recentPanel = CreateListPanel();
    private readonly FlowLayoutPanel _recoveryPanel = CreateListPanel();
    private readonly Label _busyLabel = new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 12, 0, 0),
        Visible = false
    };
    private readonly List<Control> _actionControls = [];

    public StartCentreControl(IRecentProjectService recent, IRecoveryService recovery)
    {
        _recent = recent ?? throw new ArgumentNullException(nameof(recent));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(32)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildHeroPanel(), 0, 0);
        layout.Controls.Add(BuildProjectLists(), 1, 0);
        Controls.Add(layout);
        _recentPanel.ClientSizeChanged += (_, _) => RefreshCardWidths(_recentPanel);
        _recoveryPanel.ClientSizeChanged += (_, _) => RefreshCardWidths(_recoveryPanel);
        AccessibilityMetadata.Name(this, "Project Hub", "Open, create, import, recover, and manage Playroma projects.", AccessibleRole.Pane);
    }

    public event EventHandler? OpenProjectRequested;
    public event EventHandler? NewProjectRequested;
    public event EventHandler? ImportProjectRequested;
    public event EventHandler<string>? RecentProjectRequested;
    public event EventHandler<string>? RemoveRecentProjectRequested;
    public event EventHandler<(string OldPath, string NewPath)>? RelocateRecentProjectRequested;
    public event EventHandler<RecoveryCandidate>? RecoveryOpenRequested;
    public event EventHandler<RecoveryCandidate>? RecoveryDiscardRequested;
    public event EventHandler? DocumentationRequested;
    public event EventHandler? PreferencesRequested;
    public event EventHandler? ExitRequested;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshRecentProjects();
        IReadOnlyList<RecoveryCandidate> candidates = await _recovery.FindAsync(cancellationToken).ConfigureAwait(true);
        RefreshRecovery(candidates);
    }

    public void RefreshRecentProjects()
    {
        _recentPanel.SuspendLayout();
        DisposeChildren(_recentPanel);
        foreach (RecentProject project in _recent.RecentProjects)
        {
            _recentPanel.Controls.Add(BuildRecentCard(project));
        }
        if (_recentPanel.Controls.Count == 0)
        {
            _recentPanel.Controls.Add(EmptyLabel("No recent projects yet."));
        }
        _recentPanel.ResumeLayout();
        RefreshCardWidths(_recentPanel);
    }

    public void SetBusy(bool busy, string? message = null)
    {
        _actionControls.RemoveAll(control => control.IsDisposed);
        foreach (Control control in _actionControls) control.Enabled = !busy;
        _busyLabel.Text = message ?? "Working…";
        _busyLabel.Visible = busy;
        UseWaitCursor = busy;
    }

    private Control BuildHeroPanel()
    {
        FlowLayoutPanel hero = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 16, 24, 0)
        };
        Label title = new()
        {
            Text = "Playroma jRPG",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 28, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };
        Label subtitle = new()
        {
            Text = "Open-source RPG Maker MV-compatible authoring environment",
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 24)
        };

        Button open = ActionButton("&Open Project…", bold: true);
        Button create = ActionButton("&New Project…");
        Button import = ActionButton("&Import or Convert Project…");
        Button preferences = ActionButton("&Preferences");
        Button docs = ActionButton("Read &Documentation");
        Button exit = ActionButton("E&xit Playroma jRPG");
        open.Click += (_, _) => OpenProjectRequested?.Invoke(this, EventArgs.Empty);
        create.Click += (_, _) => NewProjectRequested?.Invoke(this, EventArgs.Empty);
        import.Click += (_, _) => ImportProjectRequested?.Invoke(this, EventArgs.Empty);
        preferences.Click += (_, _) => PreferencesRequested?.Invoke(this, EventArgs.Empty);
        docs.Click += (_, _) => DocumentationRequested?.Invoke(this, EventArgs.Empty);
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        hero.Controls.Add(title);
        hero.Controls.Add(subtitle);
        hero.Controls.Add(open);
        hero.Controls.Add(create);
        hero.Controls.Add(import);
        hero.Controls.Add(preferences);
        hero.Controls.Add(docs);
        hero.Controls.Add(exit);
        hero.Controls.Add(_busyLabel);
        return hero;
    }

    private Control BuildProjectLists()
    {
        TabControl tabs = new() { Dock = DockStyle.Fill };
        TabPage recent = new("Recent Projects") { Padding = new Padding(8) };
        TabPage recovery = new("Recovery") { Padding = new Padding(8) };
        recent.Controls.Add(_recentPanel);
        recovery.Controls.Add(_recoveryPanel);
        tabs.TabPages.Add(recent);
        tabs.TabPages.Add(recovery);
        return tabs;
    }

    private Control BuildRecentCard(RecentProject project)
    {
        Panel card = CreateResponsiveCard(project, minimumHeight: 118);
        TableLayoutPanel layout = CreateCardLayout();
        string displayName = project.Title ?? Path.GetFileName(project.ProjectRoot);
        Label name = new()
        {
            Text = displayName,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoEllipsis = true,
            AutoSize = true,
            Dock = DockStyle.Fill,
            AccessibleName = $"Project {displayName}"
        };
        Label path = CardDetail(project.ProjectRoot, project.PathExists ? SystemColors.GrayText : Color.IndianRed);
        Label time = CardDetail($"{project.LayoutKind} • {project.LastOpenedUtc.LocalDateTime:g}", SystemColors.GrayText);
        FlowLayoutPanel buttons = CreateCardButtons();
        Button primary = new()
        {
            Text = project.PathExists ? "&Open" : "&Locate…",
            AutoSize = true,
            MinimumSize = new Size(76, 30),
            AccessibleDescription = project.PathExists ? $"Open {displayName}" : $"Locate the moved project {displayName}"
        };
        Button reveal = new() { Text = "&Reveal", AutoSize = true, MinimumSize = new Size(76, 30), Enabled = project.PathExists };
        Button remove = new() { Text = "&Remove", AutoSize = true, MinimumSize = new Size(76, 30) };
        primary.Click += (_, _) =>
        {
            if (project.PathExists)
            {
                RecentProjectRequested?.Invoke(this, project.ProjectRoot);
                return;
            }
            LocateRecentProject(project);
        };
        reveal.Click += (_, _) => RevealPath(project.ProjectRoot);
        remove.Click += (_, _) => RemoveRecentProjectRequested?.Invoke(this, project.ProjectRoot);
        buttons.Controls.AddRange([primary, reveal, remove]);
        AddCardRow(layout, name, 0);
        AddCardRow(layout, path, 1);
        AddCardRow(layout, time, 2);
        AddCardRow(layout, buttons, 3);
        card.Controls.Add(layout);
        _actionControls.AddRange([primary, reveal, remove]);
        return card;
    }

    private void RefreshRecovery(IReadOnlyList<RecoveryCandidate> candidates)
    {
        _recoveryPanel.SuspendLayout();
        DisposeChildren(_recoveryPanel);
        foreach (RecoveryCandidate candidate in candidates)
        {
            _recoveryPanel.Controls.Add(BuildRecoveryCard(candidate));
        }
        if (_recoveryPanel.Controls.Count == 0)
        {
            _recoveryPanel.Controls.Add(EmptyLabel("No unsaved recovery sessions were found."));
        }
        _recoveryPanel.ResumeLayout();
        RefreshCardWidths(_recoveryPanel);
    }

    private Control BuildRecoveryCard(RecoveryCandidate candidate)
    {
        Panel card = CreateResponsiveCard(candidate, minimumHeight: 132);
        TableLayoutPanel layout = CreateCardLayout();
        string titleText = candidate.Manifest.ProjectTitle ?? Path.GetFileName(candidate.Manifest.ProjectRoot);
        Label title = new()
        {
            Text = titleText,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            AutoEllipsis = true,
            AutoSize = true,
            Dock = DockStyle.Fill,
            AccessibleName = $"Recovery session for {titleText}"
        };
        Label path = CardDetail(
            candidate.Manifest.ProjectRoot,
            Directory.Exists(candidate.Manifest.ProjectRoot) ? SystemColors.GrayText : Color.IndianRed);
        Label detail = CardDetail(
            $"{candidate.Manifest.CreatedUtc.LocalDateTime:g} • "
            + $"{candidate.Manifest.Documents.Count} documents • "
            + $"{candidate.Manifest.Files.Count} file operations • "
            + $"{candidate.Manifest.PrivateSessionCount} editor sessions",
            SystemColors.GrayText);
        FlowLayoutPanel buttons = CreateCardButtons();
        Button resume = new()
        {
            Text = "&Resume",
            AutoSize = true,
            MinimumSize = new Size(76, 30),
            Enabled = Directory.Exists(candidate.Manifest.ProjectRoot),
            AccessibleDescription = $"Restore the recovery session for {titleText}"
        };
        Button reveal = new() { Text = "&Reveal", AutoSize = true, MinimumSize = new Size(76, 30) };
        Button discard = new() { Text = "&Discard", AutoSize = true, MinimumSize = new Size(76, 30) };
        resume.Click += (_, _) => RecoveryOpenRequested?.Invoke(this, candidate);
        reveal.Click += (_, _) => RevealPath(candidate.DirectoryPath);
        discard.Click += (_, _) => RecoveryDiscardRequested?.Invoke(this, candidate);
        buttons.Controls.AddRange([resume, reveal, discard]);
        AddCardRow(layout, title, 0);
        AddCardRow(layout, path, 1);
        AddCardRow(layout, detail, 2);
        AddCardRow(layout, buttons, 3);
        card.Controls.Add(layout);
        _actionControls.AddRange([resume, reveal, discard]);
        return card;
    }

    private static Panel CreateResponsiveCard(object tag, int minimumHeight) => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(320, minimumHeight),
        Margin = new Padding(3, 3, 3, 10),
        Padding = new Padding(12, 10, 12, 10),
        BorderStyle = BorderStyle.FixedSingle,
        Tag = tag
    };

    private static TableLayoutPanel CreateCardLayout()
    {
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 4; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return layout;
    }

    private static Label CardDetail(string text, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = color,
        Margin = new Padding(0, 2, 0, 2)
    };

    private static FlowLayoutPanel CreateCardButtons() => new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = true,
        Margin = new Padding(0, 7, 0, 0)
    };

    private static void AddCardRow(TableLayoutPanel layout, Control control, int row)
    {
        control.Margin = row == 0 ? new Padding(0, 0, 0, 2) : control.Margin;
        layout.Controls.Add(control, 0, row);
    }

    private static void RefreshCardWidths(FlowLayoutPanel panel)
    {
        int width = Math.Max(320, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 12);
        foreach (Control card in panel.Controls.Cast<Control>().Where(control => control is Panel)) card.Width = width;
    }

    private void LocateRecentProject(RecentProject project)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Locate the RPG Maker MV project folder",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        RelocateRecentProjectRequested?.Invoke(this, (project.ProjectRoot, dialog.SelectedPath));
    }

    private Button ActionButton(string text, bool bold = false)
    {
        Button button = new()
        {
            Text = text,
            UseMnemonic = true,
            AutoSize = true,
            MinimumSize = new Size(220, 0),
            Padding = new Padding(14, 7, 14, 7),
            Margin = new Padding(0, 0, 0, 8),
            TextAlign = ContentAlignment.MiddleLeft
        };
        if (bold) button.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
        _actionControls.Add(button);
        return button;
    }

    private static FlowLayoutPanel CreateListPanel() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(6)
    };

    private static Label EmptyLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(6)
    };

    private static void RevealPath(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch
        {
            // Revealing a path is optional; project opening remains available.
        }
    }

    private static void DisposeChildren(Control parent)
    {
        Control[] children = parent.Controls.Cast<Control>().ToArray();
        parent.Controls.Clear();
        foreach (Control child in children) child.Dispose();
    }
}
