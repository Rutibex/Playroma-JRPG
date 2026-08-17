using System.Text;
using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Documents.Saving;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Validation;

namespace Playroma.JRPG.Editor.WinForms.Documents;

/// <summary>
/// Live project dashboard. It deliberately summarizes the shared session instead of opening
/// private document copies, so pending transactions and validation results remain visible.
/// </summary>
public sealed class ProjectSummaryControl : UserControl
{
    private readonly IWorkspaceService _workspace;
    private readonly Font _boldFont;
    private readonly TableLayoutPanel _facts = new()
    {
        AutoSize = true,
        ColumnCount = 2,
        Dock = DockStyle.Top,
        Padding = new Padding(10)
    };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _maps = CreateGrid();
    private readonly DataGridView _database = CreateGrid();
    private readonly DataGridView _assets = CreateGrid();
    private readonly DataGridView _plugins = CreateGrid();
    private readonly DataGridView _runtime = CreateGrid();
    private readonly DataGridView _diagnostics = CreateGrid();
    private readonly DataGridView _pending = CreateGrid();

    private MvProjectSession? _displayedSession;
    private bool _referencesReady;
    private bool _hasUnsavedChanges;
    private int _validationCount;

    public ProjectSummaryControl(IWorkspaceService workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _boldFont = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
        Dock = DockStyle.Fill;
        _facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ConfigureMapsGrid();
        ConfigureDatabaseGrid();
        ConfigureAssetsGrid();
        ConfigurePluginsGrid();
        ConfigureRuntimeGrid();
        ConfigureDiagnosticsGrid();
        ConfigurePendingGrid();

        _tabs.TabPages.Add(new TabPage("Maps") { Controls = { _maps } });
        _tabs.TabPages.Add(new TabPage("Database") { Controls = { _database } });
        _tabs.TabPages.Add(new TabPage("Assets") { Controls = { _assets } });
        _tabs.TabPages.Add(new TabPage("Plugins") { Controls = { _plugins } });
        _tabs.TabPages.Add(new TabPage("Runtime & Compatibility") { Controls = { _runtime } });
        _tabs.TabPages.Add(new TabPage("Diagnostics") { Controls = { _diagnostics } });
        _tabs.TabPages.Add(new TabPage("Pending Changes") { Controls = { _pending } });

        Controls.Add(_tabs);
        Controls.Add(_facts);
        Controls.Add(ControlHelpers.Heading("Project Summary"));
        _workspace.StateChanged += OnWorkspaceChanged;
        _workspace.ValidationChanged += OnValidationChanged;
        Rebuild();
    }

    public int SelectedTabIndex
    {
        get => _tabs.SelectedIndex;
        set
        {
            if (_tabs.TabPages.Count == 0) return;
            _tabs.SelectedIndex = Math.Clamp(value, 0, _tabs.TabPages.Count - 1);
        }
    }

    public event EventHandler? SelectedTabChanged
    {
        add => _tabs.SelectedIndexChanged += value;
        remove => _tabs.SelectedIndexChanged -= value;
    }

    public string BuildTextSummary()
    {
        MvProjectSession? session = _workspace.CurrentSession;
        if (session is null) return "No project is open.";
        MvProjectSnapshot snapshot = session.Workspace.Snapshot;
        ProjectValidationResult? validation = session.LastValidation;
        int modifiedDocuments = session.Workspace.Documents.Count(document => document.State == MutableDocumentState.Modified);
        int fileMutations = session.Editing.Files.PendingMutations.Count;
        StringBuilder text = new();
        text.AppendLine($"Project: {snapshot.Identity.ProjectTitle ?? Path.GetFileName(snapshot.Identity.ProjectRoot)}");
        text.AppendLine($"Root: {snapshot.Identity.ProjectRoot}");
        text.AppendLine($"Layout: {snapshot.Layout.Kind}");
        text.AppendLine($"Editability: {snapshot.Editability}");
        text.AppendLine($"Maps: {snapshot.Maps.Count:N0}");
        text.AppendLine($"Events: {snapshot.Maps.Sum(map => map.EventCount ?? 0):N0}");
        text.AppendLine($"Event pages: {snapshot.Maps.Sum(map => map.EventPageCount ?? 0):N0}");
        text.AppendLine($"Event commands: {snapshot.Maps.Sum(map => map.EventCommandCount ?? 0):N0}");
        text.AppendLine($"Database records: {snapshot.Database.Files.Sum(file => file.PopulatedRecordCount):N0}");
        text.AppendLine($"Assets: {session.Assets.Statistics.LogicalAssetCount:N0} logical / {session.Assets.Statistics.PhysicalVariantCount:N0} physical ({ControlHelpers.FormatBytes(session.Assets.Statistics.TotalBytes)})");
        text.AppendLine($"Missing assets: {session.Assets.Statistics.MissingAssetCount:N0}");
        text.AppendLine($"Plugins: {snapshot.Plugins.Entries.Count:N0} ({snapshot.Plugins.EnabledCount:N0} enabled)");
        text.AppendLine($"Runtime: {snapshot.Runtime.Layout}; CoreScript {snapshot.Runtime.CoreScriptVersion ?? "unknown"}; NW.js {snapshot.Runtime.NwjsVersion ?? "unknown"}");
        text.AppendLine($"References: {(session.ReferencesReady ? session.References.Statistics.TotalReferenceCount.ToString("N0") : "building")}");
        text.AppendLine($"Validation: {(validation is null ? "not run" : $"{validation.ErrorCount:N0} errors, {validation.WarningCount:N0} warnings")}");
        text.AppendLine($"Pending changes: {modifiedDocuments:N0} modified document(s), {fileMutations:N0} file operation(s)");
        text.AppendLine($"Unsaved changes: {(session.State.HasUnsavedChanges ? "Yes" : "No")}");
        return text.ToString().TrimEnd();
    }

    public void Rebuild()
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(new Action(Rebuild));
            return;
        }

        _displayedSession = _workspace.CurrentSession;
        _referencesReady = _displayedSession?.ReferencesReady == true;
        _hasUnsavedChanges = _displayedSession?.State.HasUnsavedChanges == true;
        _validationCount = _displayedSession?.LastValidation?.Diagnostics.Count ?? 0;
        ClearFacts();
        ClearRows();

        MvProjectSession? session = _displayedSession;
        if (session is null)
        {
            AddFact("Status", "No project is open.");
            return;
        }

        MvProjectSnapshot snapshot = session.Workspace.Snapshot;
        ProjectValidationResult? validation = session.LastValidation;
        AddFact("Title", snapshot.Identity.ProjectTitle ?? "(untitled)");
        AddFact("Project root", snapshot.Identity.ProjectRoot);
        AddFact("Layout", snapshot.Layout.Kind.ToString());
        AddFact("Editability", snapshot.Editability.ToString());
        AddFact("Maps and events", $"{snapshot.Maps.Count:N0} maps · {snapshot.Maps.Sum(map => map.EventCount ?? 0):N0} events · {snapshot.Maps.Sum(map => map.EventCommandCount ?? 0):N0} command rows");
        AddFact("Database", $"{snapshot.Database.Files.Sum(file => file.PopulatedRecordCount):N0} populated records in {snapshot.Database.Files.Count:N0} files");
        AddFact("Assets", $"{session.Assets.Statistics.LogicalAssetCount:N0} logical assets · {session.Assets.Statistics.PhysicalVariantCount:N0} variants · {ControlHelpers.FormatBytes(session.Assets.Statistics.TotalBytes)}");
        AddFact("Plugins", $"{snapshot.Plugins.Entries.Count:N0} entries · {snapshot.Plugins.EnabledCount:N0} enabled");
        AddFact("References", session.ReferencesReady ? $"{session.References.Statistics.TotalReferenceCount:N0} indexed" : "Building in background…");
        AddFact("Validation", validation is null ? "Not run" : $"{validation.ErrorCount:N0} errors · {validation.WarningCount:N0} warnings · completed {validation.CompletedUtc.ToLocalTime():g}");
        AddFact("Pending changes", session.State.HasUnsavedChanges
            ? $"{session.Workspace.Documents.Count(document => document.State == MutableDocumentState.Modified):N0} modified documents · {session.Editing.Files.PendingMutations.Count:N0} file operations"
            : "None");

        PopulateMaps(snapshot);
        PopulateDatabase(snapshot);
        PopulateAssets(snapshot, session);
        PopulatePlugins(snapshot);
        PopulateRuntime(snapshot);
        PopulateDiagnostics(snapshot, validation);
        PopulatePending(session);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _workspace.StateChanged -= OnWorkspaceChanged;
            _workspace.ValidationChanged -= OnValidationChanged;
            _boldFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnWorkspaceChanged(sender, e))) return;
        // A transaction can change maps, assets, plugins, or database rows while the project
        // remains dirty, so state flags alone cannot safely suppress a dashboard refresh.
        Rebuild();
    }

    private void OnValidationChanged(object? sender, ValidationChangedEventArgs e) => Rebuild();

    private void PopulateMaps(MvProjectSnapshot snapshot)
    {
        foreach (MvMapDescriptor map in snapshot.Maps.OrderBy(map => map.Order).ThenBy(map => map.Id))
        {
            _maps.Rows.Add(
                map.Id,
                map.Name,
                map.Width.HasValue && map.Height.HasValue ? $"{map.Width} × {map.Height}" : string.Empty,
                map.EventCount ?? 0,
                map.EventPageCount ?? 0,
                map.EventCommandCount ?? 0,
                map.FileExists ? "Found" : "Missing",
                map.Diagnostics.Count,
                map.RelativePath);
        }
    }

    private void PopulateDatabase(MvProjectSnapshot snapshot)
    {
        foreach (MvDatabaseFileSummary file in snapshot.Database.Files.OrderBy(file => file.Kind))
        {
            _database.Rows.Add(
                file.Kind,
                file.PopulatedRecordCount,
                file.ArrayLength,
                file.NullSlotCount,
                file.HighestRecordId,
                file.IsValidJson ? "Valid" : "Invalid",
                file.Diagnostics.Count,
                file.RelativePath);
        }
    }

    private void PopulateAssets(MvProjectSnapshot snapshot, MvProjectSession session)
    {
        foreach (var category in snapshot.Assets.Categories.OrderBy(category => category.Category.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            int logicalCount = session.Assets.Assets.Count(asset => asset.Key.Category == category.Category);
            int missing = session.Assets.Assets.Count(asset => asset.Key.Category == category.Category && asset.IsMissing);
            int diagnostics = session.Assets.Assets.Where(asset => asset.Key.Category == category.Category).Sum(asset => asset.Diagnostics.Count);
            _assets.Rows.Add(category.Category, logicalCount, category.Count, missing, diagnostics, ControlHelpers.FormatBytes(category.TotalBytes));
        }
    }

    private void PopulatePlugins(MvProjectSnapshot snapshot)
    {
        foreach (MvPluginEntry plugin in snapshot.Plugins.Entries.OrderBy(plugin => plugin.LoadOrder))
        {
            List<string> warnings = [];
            if (!plugin.ScriptExists) warnings.Add("missing script");
            if (plugin.IsDuplicate) warnings.Add("duplicate");
            if (plugin.AppearsNodeDependent) warnings.Add("Node/NW.js");
            _plugins.Rows.Add(
                plugin.LoadOrder + 1,
                plugin.Enabled,
                plugin.Name,
                plugin.Parameters.Count,
                plugin.ScriptExists ? "Found" : "Missing",
                string.Join(", ", warnings),
                plugin.ScriptRelativePath ?? string.Empty);
        }
    }

    private void PopulateRuntime(MvProjectSnapshot snapshot)
    {
        AddRuntime("Layout", snapshot.Runtime.Layout);
        AddRuntime("Core script version", snapshot.Runtime.CoreScriptVersion ?? "Unknown");
        AddRuntime("NW.js version", snapshot.Runtime.NwjsVersion ?? "Unknown");
        AddRuntime("index.html", snapshot.Runtime.HasIndexHtml ? "Found" : "Missing");
        AddRuntime("package.json", snapshot.Runtime.HasPackageJson ? "Found" : "Missing");
        AddRuntime("Embedded executable", snapshot.Runtime.HasEmbeddedExecutable ? "Found" : "Not found");
        AddRuntime("Browser compatible", snapshot.Runtime.AppearsBrowserCompatible ? "Likely" : "Not confirmed");
        AddRuntime("Node dependent", snapshot.Runtime.AppearsNodeDependent ? "Yes" : "No known dependency");
        AddRuntime("Runtime signature", snapshot.Runtime.RuntimeSignature ?? "Unknown");
        AddRuntime("Runtime files", snapshot.Runtime.Files.Count.ToString("N0"));
        AddRuntime("Runtime diagnostics", snapshot.Runtime.Diagnostics.Count.ToString("N0"));
        AddRuntime("Project files", $"{snapshot.Files.Count:N0} · {ControlHelpers.FormatBytes(snapshot.Files.TotalBytes)}");
        AddRuntime("Encrypted assets", snapshot.Assets.EncryptedAssetCount.ToString("N0"));
    }

    private void PopulateDiagnostics(MvProjectSnapshot snapshot, ProjectValidationResult? validation)
    {
        foreach (ProjectDiagnostic diagnostic in snapshot.Diagnostics
                     .Concat(snapshot.Runtime.Diagnostics)
                     .OrderByDescending(diagnostic => SeverityRank(diagnostic.Severity))
                     .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal))
        {
            _diagnostics.Rows.Add(
                "Load",
                diagnostic.Severity,
                diagnostic.Code,
                diagnostic.Location.RelativePath ?? string.Empty,
                diagnostic.Message,
                diagnostic.TechnicalDetail ?? string.Empty);
        }

        if (validation is null) return;
        foreach (ProjectValidationDiagnostic item in validation.Diagnostics
                     .OrderByDescending(item => SeverityRank(item.Severity))
                     .ThenBy(item => item.Code, StringComparer.Ordinal))
        {
            _diagnostics.Rows.Add(
                "Validation",
                item.Severity,
                item.Code,
                item.Diagnostic.Location.RelativePath ?? string.Empty,
                item.Diagnostic.Message,
                item.Fix?.Title ?? item.Diagnostic.TechnicalDetail ?? string.Empty);
        }
    }

    private void PopulatePending(MvProjectSession session)
    {
        foreach (ProjectDocumentHandle document in session.Workspace.Documents
                     .Where(document => document.State is MutableDocumentState.Modified
                         or MutableDocumentState.ExternallyModified
                         or MutableDocumentState.SaveFailed)
                     .OrderBy(document => document.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            _pending.Rows.Add("Document", document.State, document.RelativePath, string.Empty, ControlHelpers.FormatBytes(document.Size));
        }

        foreach (PendingFileMutation mutation in session.Editing.Files.PendingMutations)
        {
            string path = mutation.DestinationRelativePath ?? mutation.SourceRelativePath ?? string.Empty;
            _pending.Rows.Add("File operation", mutation.Kind, path, mutation.Description, ControlHelpers.FormatBytes(mutation.StagedLength));
        }

        if (_pending.Rows.Count == 0) _pending.Rows.Add("Status", "Clean", "No pending project changes", string.Empty, string.Empty);
    }

    private void AddRuntime(string property, object value) => _runtime.Rows.Add(property, value);

    private void AddFact(string name, string value)
    {
        int row = _facts.RowCount++;
        _facts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _facts.Controls.Add(new Label
        {
            Text = name,
            AutoSize = true,
            Font = _boldFont,
            Margin = new Padding(3, 4, 12, 4)
        }, 0, row);
        _facts.Controls.Add(new Label
        {
            Text = value,
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            Margin = new Padding(3, 4, 3, 4)
        }, 1, row);
    }

    private void ClearFacts()
    {
        foreach (Control control in _facts.Controls.Cast<Control>().ToArray()) control.Dispose();
        _facts.Controls.Clear();
        _facts.RowStyles.Clear();
        _facts.RowCount = 0;
    }

    private void ClearRows()
    {
        foreach (DataGridView grid in new[] { _maps, _database, _assets, _plugins, _runtime, _diagnostics, _pending })
            grid.Rows.Clear();
    }

    private void ConfigureMapsGrid()
    {
        AddColumns(_maps,
            ("ID", 55), ("Name", 190), ("Size", 90), ("Events", 70), ("Pages", 70),
            ("Commands", 90), ("File", 70), ("Issues", 65), ("Path", 260));
        _maps.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void ConfigureDatabaseGrid()
    {
        AddColumns(_database,
            ("Category", 120), ("Records", 80), ("Array Length", 90), ("Null Slots", 80),
            ("Highest ID", 80), ("JSON", 65), ("Issues", 65), ("File", 260));
        _database.Columns[7].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void ConfigureAssetsGrid()
    {
        AddColumns(_assets,
            ("Category", 150), ("Logical", 75), ("Files", 75), ("Missing", 75), ("Issues", 65), ("Size", 110));
        _assets.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void ConfigurePluginsGrid()
    {
        AddColumns(_plugins,
            ("Order", 60), ("Enabled", 65), ("Name", 190), ("Parameters", 85),
            ("Script", 75), ("Warnings", 160), ("Path", 260));
        _plugins.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void ConfigureRuntimeGrid()
    {
        AddColumns(_runtime, ("Property", 220), ("Value", 480));
        _runtime.Columns[1].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void ConfigureDiagnosticsGrid()
    {
        AddColumns(_diagnostics,
            ("Source", 85), ("Severity", 85), ("Code", 100), ("File", 210), ("Message", 420), ("Detail/Fix", 260));
        _diagnostics.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private void ConfigurePendingGrid()
    {
        AddColumns(_pending, ("Kind", 105), ("State", 100), ("Path", 300), ("Description", 380), ("Size", 100));
        _pending.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
    }

    private static DataGridView CreateGrid()
    {
        DataGridView grid = ControlHelpers.CreateReadOnlyGrid();
        grid.Dock = DockStyle.Fill;
        return grid;
    }

    private static void AddColumns(DataGridView grid, params (string Name, int Width)[] columns)
    {
        foreach ((string name, int width) in columns) grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = name, Width = width });
    }

    private static int SeverityRank(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Fatal => 4,
        DiagnosticSeverity.Error => 3,
        DiagnosticSeverity.Warning => 2,
        _ => 1
    };
}
