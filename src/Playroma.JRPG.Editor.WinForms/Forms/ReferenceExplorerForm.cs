using System.Text;
using System.Text.Json;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Navigation;
using Playroma.JRPG.Editor.WinForms.Windowing;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Incoming/outgoing reference explorer that can follow the active selection or pin a target.</summary>
public sealed class ReferenceExplorerForm : EditorToolForm, IWorkspaceStateContributor
{
    private readonly IWorkspaceService _workspace;
    private readonly ISelectionService _selection;
    private readonly IEditorNavigationCoordinator _navigation;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly CancellationTokenSource _lifetime = new();

    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
    private readonly ToolStripLabel _targetLabel = new("Target: none") { AutoSize = false, Width = 300, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripButton _follow = new("Follow Selection") { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _pin = new("Pin") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _exact = new("Exact") { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _heuristic = new("Heuristic") { CheckOnClick = true, Checked = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _refresh = new("Refresh Index") { DisplayStyle = ToolStripItemDisplayStyle.Text };
    private readonly ToolStripButton _openSource = new("Open Source") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _openTarget = new("Open Target") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _openRaw = new("Open Raw") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripButton _copyJsonPath = new("Copy JSON Path") { DisplayStyle = ToolStripItemDisplayStyle.Text, Enabled = false };
    private readonly ToolStripTextBox _search = new() { AutoSize = false, Width = 220, ToolTipText = "Filter references" };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _incoming = CreateGrid("Source");
    private readonly DataGridView _outgoing = CreateGrid("Target");
    private readonly SplitContainer _split = ControlHelpers.CreateSplitContainer(
        Orientation.Horizontal, 450, 220, 100, splitterWidth: 6);
    private readonly TextBox _details = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = true,
        Font = new Font(FontFamily.GenericMonospace, 9f)
    };
    private readonly StatusStrip _status = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _statusText = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    private EditorSelection _targetSelection = new EmptySelection();
    private EditorSelection? _pinnedSelection;
    private bool _disposed;
    private bool _restoring;

    public ReferenceExplorerForm(
        WindowRequest request,
        IWorkspaceService workspace,
        ISelectionService selection,
        IEditorNavigationCoordinator navigation,
        ISettingsService settings,
        INotificationService notifications)
        : base(request)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Name = nameof(ReferenceExplorerForm);
        Text = "Reference Explorer";
        MinimumSize = new Size(820, 560);
        Size = new Size(1080, 720);
        KeyPreview = true;
        ShowInTaskbar = _settings.Current.EffectivePreferences.ShowUtilityWindowsInTaskbar;
        BuildUi();
        WireEvents();
    }

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        if (_workspace.CurrentSession is null)
            throw new InvalidOperationException("Reference Explorer cannot open without an active project.");
        RestoreSettings();
        _targetSelection = _selection.Current;
        InstallContent(BuildRoot());
        Rebuild();
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        CaptureSettings();
        await _settings.SaveAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    public ValueTask<string?> CaptureWorkspaceStateJsonAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EditorSelection persisted = _pinnedSelection ?? _targetSelection;
        ReferenceExplorerWorkspacePayload payload = ReferenceExplorerWorkspacePayload.From(
            persisted,
            _follow.Checked,
            _exact.Checked,
            _heuristic.Checked,
            _tabs.SelectedIndex,
            _search.Text);
        return ValueTask.FromResult<string?>(JsonSerializer.Serialize(payload));
    }

    public ValueTask RestoreWorkspaceStateJsonAsync(
        string? stateJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(stateJson)) return ValueTask.CompletedTask;
        ReferenceExplorerWorkspacePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ReferenceExplorerWorkspacePayload>(stateJson);
        }
        catch (JsonException)
        {
            return ValueTask.CompletedTask;
        }
        if (payload is null || payload.Version > 1) return ValueTask.CompletedTask;

        _restoring = true;
        try
        {
            _exact.Checked = payload.IncludeExact;
            _heuristic.Checked = payload.IncludeHeuristic;
            _search.Text = payload.SearchText ?? string.Empty;
            if (_tabs.TabCount > 0)
            {
                _tabs.SelectedIndex = Math.Clamp(payload.SelectedTab, 0, _tabs.TabCount - 1);
            }
            EditorSelection restored = payload.ToSelection();
            _targetSelection = restored;
            _pinnedSelection = payload.FollowSelection ? null : restored;
            _follow.Checked = payload.FollowSelection;
            _pin.Text = _pinnedSelection is null ? "Pin" : "Unpin";
        }
        finally
        {
            _restoring = false;
        }
        Rebuild();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposeWindowAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _selection.SelectionChanged -= OnSelectionChanged;
        _workspace.StateChanged -= OnWorkspaceChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }

    public override EditorCommandStatus QueryCommand(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) return EditorCommandStatus.Enabled("Find Reference");
        if (command == EditorCommandIds.ViewRefresh || command == EditorCommandIds.ProjectRefreshReferences)
            return EditorCommandStatus.Enabled("Refresh References");
        return base.QueryCommand(command, context);
    }

    public override async ValueTask ExecuteCommandAsync(EditorCommandId command, EditorCommandContext context)
    {
        if (command == EditorCommandIds.EditFind) _search.Focus();
        else if (command == EditorCommandIds.ViewRefresh || command == EditorCommandIds.ProjectRefreshReferences)
            await RefreshIndexAsync().ConfigureAwait(true);
        else await base.ExecuteCommandAsync(command, context).ConfigureAwait(true);
        NotifyCommandStateChanged();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5) { _ = RefreshIndexAsync(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); return true; }
        if (keyData == Keys.Enter) { _ = _tabs.SelectedIndex == 0 ? NavigateSourceAsync() : NavigateTargetAsync(); return true; }
        if (keyData == (Keys.Control | Keys.P)) { TogglePin(); return true; }
        if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildUi()
    {
        _search.TextBox.PlaceholderText = "Search references…";
        _toolbar.Items.AddRange([
            _targetLabel, new ToolStripSeparator(), _follow, _pin,
            new ToolStripSeparator(), _exact, _heuristic,
            new ToolStripSeparator(), _refresh,
            new ToolStripSeparator(), _openSource, _openTarget, _openRaw, _copyJsonPath,
            new ToolStripSeparator(), new ToolStripLabel("Search:"), _search]);
        _tabs.TabPages.Add(new TabPage("Incoming") { Controls = { _incoming } });
        _tabs.TabPages.Add(new TabPage("Outgoing") { Controls = { _outgoing } });
        _split.Panel1.Controls.Add(_tabs);
        _split.Panel2.Controls.Add(_details);
        _status.Items.Add(_statusText);
    }

    private Control BuildRoot()
    {
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_split);
        root.Controls.Add(_status);
        root.Controls.Add(_toolbar);
        return root;
    }

    private void WireEvents()
    {
        _selection.SelectionChanged += OnSelectionChanged;
        _workspace.StateChanged += OnWorkspaceChanged;
        _follow.CheckedChanged += (_, _) =>
        {
            if (_follow.Checked)
            {
                _pinnedSelection = null;
                _targetSelection = _selection.Current;
            }
            Rebuild();
        };
        _pin.Click += (_, _) => TogglePin();
        _exact.CheckedChanged += (_, _) => Rebuild();
        _heuristic.CheckedChanged += (_, _) => Rebuild();
        _refresh.Click += async (_, _) => await RefreshIndexAsync().ConfigureAwait(true);
        _openSource.Click += async (_, _) => await NavigateSourceAsync().ConfigureAwait(true);
        _openTarget.Click += async (_, _) => await NavigateTargetAsync().ConfigureAwait(true);
        _openRaw.Click += async (_, _) => await OpenRawSourceAsync().ConfigureAwait(true);
        _copyJsonPath.Click += (_, _) => CopySelectedJsonPath();
        _search.TextChanged += (_, _) => Rebuild();
        _tabs.SelectedIndexChanged += (_, _) => UpdateDetails();
        _incoming.SelectionChanged += (_, _) => UpdateDetails();
        _outgoing.SelectionChanged += (_, _) => UpdateDetails();
        _incoming.CellDoubleClick += async (_, _) => await NavigateSourceAsync().ConfigureAwait(true);
        _outgoing.CellDoubleClick += async (_, _) => await NavigateTargetAsync().ConfigureAwait(true);
        _split.SplitterMoved += (_, _) => { if (!_restoring) CaptureSettings(); };
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (!_follow.Checked || _pinnedSelection is not null) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnSelectionChanged(sender, e))) return;
        EditorSelection current = _selection.Current;
        if (current is ReferenceSelection or DiagnosticSelection) return;
        _targetSelection = current;
        Rebuild();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceStateChangedEventArgs e)
    {
        if (ControlHelpers.BeginInvokeIfRequired(this, () => OnWorkspaceChanged(sender, e))) return;
        Rebuild();
    }

    private void TogglePin()
    {
        if (_pinnedSelection is null)
        {
            _pinnedSelection = _targetSelection;
            _follow.Checked = false;
            _pin.Text = "Unpin";
        }
        else
        {
            _pinnedSelection = null;
            _targetSelection = _selection.Current;
            _follow.Checked = true;
            _pin.Text = "Pin";
        }
        Rebuild();
    }

    private void Rebuild()
    {
        if (_disposed || IsDisposed) return;
        if (ControlHelpers.BeginInvokeIfRequired(this, Rebuild)) return;
        var session = _workspace.CurrentSession;
        _incoming.Rows.Clear();
        _outgoing.Rows.Clear();
        if (session is null)
        {
            _statusText.Text = "No project open.";
            _targetLabel.Text = "Target: none";
            return;
        }
        if (!session.ReferencesReady)
        {
            _statusText.Text = "Reference index is being built…";
            _targetLabel.Text = "Target: " + DescribeSelection(_targetSelection);
            return;
        }

        ReferenceTarget? target = ToTarget(_targetSelection);
        string? sourcePath = ToSourcePath(_targetSelection);
        int? recordId = (_targetSelection as DatabaseRecordSelection)?.RecordId;
        _targetLabel.Text = "Target: " + DescribeSelection(_targetSelection);
        string search = _search.Text.Trim();

        if (target is not null)
        {
            IEnumerable<ProjectReference> incoming = session.References.FindInbound(target, includeHeuristic: true);
            foreach (ProjectReference reference in ApplyFilters(incoming, search))
            {
                int row = _incoming.Rows.Add(
                    reference.Confidence,
                    reference.Source.RelativePath,
                    BuildLocation(reference.Source),
                    reference.Usage,
                    reference.Context ?? string.Empty);
                _incoming.Rows[row].Tag = reference;
            }
        }

        if (sourcePath is not null)
        {
            IEnumerable<ProjectReference> outgoing = recordId.HasValue
                ? session.References.FindFromRecord(sourcePath, recordId.Value, includeHeuristic: true)
                : session.References.FindOutbound(sourcePath, includeHeuristic: true);
            foreach (ProjectReference reference in ApplyFilters(outgoing, search))
            {
                int row = _outgoing.Rows.Add(
                    reference.Confidence,
                    reference.Target,
                    BuildLocation(reference.Source),
                    reference.Usage,
                    reference.Context ?? string.Empty);
                _outgoing.Rows[row].Tag = reference;
            }
        }

        _statusText.Text = $"{_incoming.Rows.Count:N0} incoming · {_outgoing.Rows.Count:N0} outgoing · " +
                           $"{session.References.Statistics.TotalReferenceCount:N0} references indexed";
        UpdateDetails();
    }

    private IEnumerable<ProjectReference> ApplyFilters(IEnumerable<ProjectReference> references, string search)
    {
        IEnumerable<ProjectReference> query = references.Where(reference =>
            (reference.Confidence == ReferenceConfidence.Exact && _exact.Checked)
            || (reference.Confidence == ReferenceConfidence.Heuristic && _heuristic.Checked));
        if (search.Length > 0)
        {
            query = query.Where(reference =>
                reference.Source.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase)
                || reference.Source.JsonPath.Contains(search, StringComparison.OrdinalIgnoreCase)
                || reference.Target.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || reference.Usage.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                || (reference.Context?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        return query.OrderBy(reference => reference.Confidence)
            .ThenBy(reference => reference.Source.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.Source.JsonPath, StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateDetails()
    {
        ProjectReference? reference = SelectedReference();
        _openSource.Enabled = reference is not null;
        _openTarget.Enabled = reference is not null && EditorNavigationCoordinator.FromReferenceTarget(reference.Target) is not null;
        _openRaw.Enabled = reference is not null && !string.IsNullOrWhiteSpace(reference.Source.RelativePath);
        _copyJsonPath.Enabled = reference is not null && !string.IsNullOrWhiteSpace(reference.Source.JsonPath);
        if (reference is null)
        {
            _details.Clear();
            return;
        }
        _selection.Select(new ReferenceSelection(reference));
        StringBuilder text = new();
        text.AppendLine($"{reference.Confidence} {reference.Usage}");
        text.AppendLine();
        text.AppendLine("Source: " + reference.Source.RelativePath);
        text.AppendLine("Location: " + BuildLocation(reference.Source));
        text.AppendLine("JSON path: " + reference.Source.JsonPath);
        text.AppendLine("Target: " + reference.Target);
        if (!string.IsNullOrWhiteSpace(reference.Context))
        {
            text.AppendLine();
            text.AppendLine("Context:");
            text.AppendLine(reference.Context);
        }
        _details.Text = text.ToString();
    }

    private async Task NavigateSourceAsync()
    {
        ProjectReference? reference = SelectedReference();
        if (reference is null) return;
        try
        {
            NavigationLocation location = EditorNavigationCoordinator.FromReferenceSource(reference.Source);
            await _navigation.NavigateAsync(location, this, cancellationToken: _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not open reference source", exception.Message, exception);
        }
    }

    private async Task NavigateTargetAsync()
    {
        ProjectReference? reference = SelectedReference();
        NavigationLocation? location = reference is null
            ? null
            : EditorNavigationCoordinator.FromReferenceTarget(reference.Target);
        if (location is null)
        {
            _notifications.Information("Target navigation unavailable", "This reference target does not currently map to a dedicated editor location.");
            return;
        }
        try
        {
            await _navigation.NavigateAsync(location, this, cancellationToken: _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not open reference target", exception.Message, exception);
        }
    }

    private async Task OpenRawSourceAsync()
    {
        ProjectReference? reference = SelectedReference();
        if (reference is null || string.IsNullOrWhiteSpace(reference.Source.RelativePath)) return;
        try
        {
            DocumentRequest document = new(
                EditorDocumentKind.RawJson,
                "file:" + reference.Source.RelativePath,
                reference.Source.RelativePath,
                RecordId: reference.Source.RecordId,
                DisplayTitle: Path.GetFileName(reference.Source.RelativePath));
            await _navigation.NavigateAsync(
                new NavigationLocation(document, reference.Source.JsonPath, reference.Source.RecordId, reference.Source.MapId),
                this,
                cancellationToken: _lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _notifications.Error("Could not open raw source", exception.Message, exception);
        }
    }

    private void CopySelectedJsonPath()
    {
        ProjectReference? reference = SelectedReference();
        if (reference is null || string.IsNullOrWhiteSpace(reference.Source.JsonPath)) return;
        Clipboard.SetText(reference.Source.JsonPath);
    }

    private async Task RefreshIndexAsync()
    {
        try
        {
            _refresh.Enabled = false;
            _statusText.Text = "Refreshing reference index…";
            int count = await _workspace.RefreshReferencesAsync(_lifetime.Token).ConfigureAwait(true);
            _notifications.Success("References refreshed", $"{count:N0} document(s) were refreshed.");
            Rebuild();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _notifications.Error("Reference refresh failed", exception.Message, exception);
        }
        finally { _refresh.Enabled = true; }
    }

    private ProjectReference? SelectedReference()
    {
        DataGridView grid = _tabs.SelectedIndex == 0 ? _incoming : _outgoing;
        return grid.SelectedRows.Count == 1 ? grid.SelectedRows[0].Tag as ProjectReference : null;
    }

    private void RestoreSettings()
    {
        _restoring = true;
        try
        {
            ReferenceExplorerWindowLayoutSettings saved = _settings.Current.EffectiveReferenceExplorerWindowLayout;
            _follow.Checked = saved.FollowSelection;
            _exact.Checked = saved.IncludeExact;
            _heuristic.Checked = saved.IncludeHeuristic;
            _search.Text = saved.SearchText;
            _tabs.SelectedIndex = Math.Clamp(saved.SelectedTab, 0, 1);
            ControlHelpers.SetSplitterDistanceSafe(_split, Height - saved.DetailsPanelHeight - 150);
        }
        finally { _restoring = false; }
    }

    private void CaptureSettings()
    {
        if (_restoring || IsDisposed) return;
        ReferenceExplorerWindowLayoutSettings layout = new(
            FollowSelection: _follow.Checked,
            IncludeExact: _exact.Checked,
            IncludeHeuristic: _heuristic.Checked,
            SearchText: _search.Text,
            DetailsPanelHeight: _split.Panel2.Height,
            SelectedTab: _tabs.SelectedIndex);
        _settings.Update(current => current with { ReferenceExplorerWindowLayout = layout, Version = EditorSettingsMigrator.CurrentVersion });
    }

    private static DataGridView CreateGrid(string endpointHeader)
    {
        DataGridView grid = ControlHelpers.CreateReadOnlyGrid();
        grid.Columns.Add("Confidence", "Confidence");
        grid.Columns.Add("Endpoint", endpointHeader);
        grid.Columns.Add("Location", "Location");
        grid.Columns.Add("Usage", "Usage");
        grid.Columns.Add("Context", "Context");
        grid.Columns[4].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        return grid;
    }

    private static string DescribeSelection(EditorSelection selection) => selection switch
    {
        MapSelection map => $"Map {map.MapId}",
        DatabaseRecordSelection record => $"{record.Kind} #{record.RecordId}",
        DatabaseCategorySelection category => category.Kind.ToString(),
        AssetSelection asset => asset.Key.ToString(),
        PluginSelection plugin => plugin.Name,
        ProjectFileSelection file => file.RelativePath,
        _ => "none"
    };

    private static string BuildLocation(ReferenceSource source)
    {
        List<string> parts = [];
        if (source.MapId.HasValue) parts.Add("Map " + source.MapId.Value);
        if (source.RecordId.HasValue) parts.Add("Record " + source.RecordId.Value);
        if (source.EventId.HasValue) parts.Add("Event " + source.EventId.Value);
        if (source.PageIndex.HasValue) parts.Add("Page " + (source.PageIndex.Value + 1));
        if (source.CommandIndex.HasValue) parts.Add("Command " + source.CommandIndex.Value);
        if (parts.Count == 0) parts.Add(source.JsonPath);
        return string.Join(" / ", parts);
    }

    private static ReferenceTarget? ToTarget(EditorSelection selection) => selection switch
    {
        MapSelection map => new ReferenceTarget(ReferenceTargetKind.Map, map.MapId),
        DatabaseRecordSelection record => new ReferenceTarget(ToTargetKind(record.Kind), record.RecordId),
        AssetSelection asset => new ReferenceTarget(ReferenceTargetKind.Asset, Name: asset.Key.LogicalName, AssetCategory: asset.Key.Category),
        PluginSelection plugin => new ReferenceTarget(ReferenceTargetKind.Plugin, Name: plugin.Name),
        _ => null
    };

    private static string? ToSourcePath(EditorSelection selection) => selection switch
    {
        MapSelection map => $"data/Map{map.MapId:D3}.json",
        DatabaseRecordSelection record => record.RelativePath,
        ProjectFileSelection file => file.RelativePath,
        _ => null
    };

    private sealed record ReferenceExplorerWorkspacePayload(
        bool FollowSelection,
        bool IncludeExact,
        bool IncludeHeuristic,
        int SelectedTab,
        string? SearchText,
        string SelectionKind,
        int? Number,
        string? Text,
        string? RelativePath,
        string? DatabaseKind,
        int Version = 1)
    {
        public static ReferenceExplorerWorkspacePayload From(
            EditorSelection selection,
            bool followSelection,
            bool includeExact,
            bool includeHeuristic,
            int selectedTab,
            string? searchText)
        {
            ReferenceExplorerWorkspacePayload Create(
                string kind,
                int? number = null,
                string? text = null,
                string? relativePath = null,
                string? databaseKind = null) => new(
                    followSelection,
                    includeExact,
                    includeHeuristic,
                    selectedTab,
                    searchText,
                    kind,
                    number,
                    text,
                    relativePath,
                    databaseKind);

            return selection switch
            {
                MapSelection map => Create("map", map.MapId),
                DatabaseRecordSelection record => Create(
                    "database-record",
                    record.RecordId,
                    relativePath: record.RelativePath,
                    databaseKind: record.Kind.ToString()),
                DatabaseCategorySelection category => Create(
                    "database-category",
                    databaseKind: category.Kind.ToString()),
                AssetSelection asset => Create("asset", text: asset.RelativePath),
                PluginSelection plugin => Create("plugin", plugin.LoadOrder, plugin.Name),
                ProjectFileSelection file => Create("project-file", relativePath: file.RelativePath),
                _ => Create("empty")
            };
        }

        public EditorSelection ToSelection()
        {
            if (string.Equals(SelectionKind, "map", StringComparison.OrdinalIgnoreCase) && Number.HasValue)
                return new MapSelection(Number.Value);
            if (string.Equals(SelectionKind, "database-record", StringComparison.OrdinalIgnoreCase)
                && Number.HasValue
                && Enum.TryParse(DatabaseKind, ignoreCase: true, out MvDocumentKind recordKind))
                return new DatabaseRecordSelection(recordKind, Number.Value, RelativePath ?? string.Empty);
            if (string.Equals(SelectionKind, "database-category", StringComparison.OrdinalIgnoreCase)
                && Enum.TryParse(DatabaseKind, ignoreCase: true, out MvDocumentKind categoryKind))
                return new DatabaseCategorySelection(categoryKind);
            if (string.Equals(SelectionKind, "asset", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(Text))
                return new AssetSelection(Text);
            if (string.Equals(SelectionKind, "plugin", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(Text))
                return new PluginSelection(Number ?? 0, Text);
            if (string.Equals(SelectionKind, "project-file", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(RelativePath))
                return new ProjectFileSelection(RelativePath);
            return new EmptySelection();
        }
    }

    private static ReferenceTargetKind ToTargetKind(MvDocumentKind kind) => kind switch
    {
        MvDocumentKind.Actors => ReferenceTargetKind.Actor,
        MvDocumentKind.Classes => ReferenceTargetKind.Class,
        MvDocumentKind.Skills => ReferenceTargetKind.Skill,
        MvDocumentKind.Items => ReferenceTargetKind.Item,
        MvDocumentKind.Weapons => ReferenceTargetKind.Weapon,
        MvDocumentKind.Armors => ReferenceTargetKind.Armor,
        MvDocumentKind.Enemies => ReferenceTargetKind.Enemy,
        MvDocumentKind.Troops => ReferenceTargetKind.Troop,
        MvDocumentKind.States => ReferenceTargetKind.State,
        MvDocumentKind.Animations => ReferenceTargetKind.Animation,
        MvDocumentKind.Tilesets => ReferenceTargetKind.Tileset,
        MvDocumentKind.CommonEvents => ReferenceTargetKind.CommonEvent,
        _ => ReferenceTargetKind.Unknown
    };
}
