using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Eventing;
using Playroma.JRPG.Eventing;
using Playroma.JRPG.Editing;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class CommonEventEditorControl : UserControl, IDatabaseEditorBody
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly Func<DocumentRequest, Task>? _documentOpener;
    private readonly DatabaseMutationService _mutations;
    private readonly EventStructureProjector _projector = new();
    private readonly EventCommandRegistry _registry;
    private readonly ProjectRecordLookup _recordLookup;
    private EventCommandFormatter? _formatter;

    private readonly TextBox _name = new() { Dock = DockStyle.Fill, AccessibleName = "Common event name" };
    private readonly ComboBox _trigger = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Common event trigger" };
    private readonly NumericUpDown _switchId = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 999999, AccessibleName = "Common event switch" };
    private readonly VirtualEventCommandListControl _commands = new() { Dock = DockStyle.Fill };
    private readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 24, AutoEllipsis = true, Padding = new Padding(6, 0, 6, 0), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _openCommands = new() { Text = "Open Full Event Command Editor…", AutoSize = true };
    private readonly Button _refreshCommands = new() { Text = "Refresh", AutoSize = true };

    private int _recordId;
    private CommonEventSettingsSnapshot? _settings;
    private bool _loading;

    public CommonEventEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        Func<DocumentRequest, Task>? documentOpener = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(definition);

        _session = session;
        _document = document;
        _documentOpener = documentOpener;
        _mutations = new DatabaseMutationService(session.Editing);
        _registry = EventCommandRegistry.CreateBuiltIn(FindRepositoryRoot());
        _recordLookup = new ProjectRecordLookup(session);

        Dock = DockStyle.Fill;
        _trigger.Items.AddRange(["None", "Autorun", "Parallel"]);
        _openCommands.Enabled = documentOpener is not null;
        BuildLayout();
        WireEvents();
    }

    public Control View => this;
    public bool OwnsTraits => false;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;

    public async ValueTask BindAsync(IReadOnlyList<DatabaseRecordHandle> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            Enabled = records.Count == 1;
            if (records.Count != 1)
            {
                _recordId = 0;
                _settings = null;
                _name.Text = string.Empty;
                _trigger.SelectedIndex = 0;
                _switchId.Value = 1;
                _switchId.Enabled = false;
                _commands.SetRows([], _registry, _formatter ?? new EventCommandFormatter(_registry), selectInsertionRow: true);
                _summary.Text = records.Count == 0 ? "Select one common event." : "Common Events can only be edited one at a time.";
                return;
            }

            _recordId = records[0].Id;
            EventListAddress address = EventListAddress.ForCommonEvent(_document.RelativePath, _recordId);
            _settings = CommonEventSettingsSnapshot.Capture(_document, address);
            _name.Text = _settings.Name;
            _trigger.SelectedIndex = Math.Clamp(_settings.Trigger, 0, _trigger.Items.Count - 1);
            _switchId.Value = Math.Clamp(_settings.SwitchId, (int)_switchId.Minimum, (int)_switchId.Maximum);
            _switchId.Enabled = _trigger.SelectedIndex > 0;

            await _recordLookup.RefreshAsync(cancellationToken).ConfigureAwait(true);
            _formatter = new EventCommandFormatter(_registry, _recordLookup.Resolve);

            EventListSnapshot snapshot = EventListSnapshot.Capture(_document, address);
            EventStructureDocument structure = _projector.Project(snapshot);
            IReadOnlyList<ProjectedEventRow> rows = EventRowProjector.Build(structure);
            _commands.SetRows(rows, _registry, _formatter, rows.Count > 0 ? rows[0].RawIndex : null, selectInsertionRow: rows.Count == 0);
            int diagCount = structure.Diagnostics.Count(d => d.Severity >= EventDiagnosticSeverity.Warning);
            _summary.Text = $"{snapshot.Commands.Count:N0} command row(s). {(diagCount == 0 ? "No structural diagnostics." : $"{diagCount:N0} diagnostic(s) present.")}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(BuildSettingsGroup(), 0, 0);
        root.Controls.Add(BuildContentsGroup(), 0, 1);
        Controls.Add(root);
    }

    private Control BuildSettingsGroup()
    {
        GroupBox group = new() { Text = "General Settings", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 6, AutoSize = true };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        table.Controls.Add(new Label { Text = "Name:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        table.Controls.Add(_name, 1, 0);
        table.Controls.Add(new Label { Text = "Trigger:", Anchor = AnchorStyles.Left, AutoSize = true }, 2, 0);
        table.Controls.Add(_trigger, 3, 0);
        table.Controls.Add(new Label { Text = "Switch:", Anchor = AnchorStyles.Left, AutoSize = true }, 4, 0);
        table.Controls.Add(_switchId, 5, 0);
        group.Controls.Add(table);
        return group;
    }

    private Control BuildContentsGroup()
    {
        GroupBox group = new() { Text = "Contents", Dock = DockStyle.Fill, Padding = new Padding(8) };
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        toolbar.Controls.Add(_openCommands);
        toolbar.Controls.Add(_refreshCommands);
        group.Controls.Add(_commands);
        group.Controls.Add(_summary);
        group.Controls.Add(toolbar);
        return group;
    }

    private void WireEvents()
    {
        _name.Validated += async (_, _) => await CommitNameAsync().ConfigureAwait(true);
        _trigger.SelectedIndexChanged += async (_, _) =>
        {
            _switchId.Enabled = _trigger.SelectedIndex > 0;
            await CommitTriggerAsync().ConfigureAwait(true);
        };
        _switchId.ValueChanged += async (_, _) => await CommitSwitchAsync().ConfigureAwait(true);
        _refreshCommands.Click += async (_, _) => await BindCurrentAsync().ConfigureAwait(true);
        _openCommands.Click += async (_, _) => await OpenEventCommandsAsync().ConfigureAwait(true);
        _commands.CommandActivated += async (_, _) => await OpenEventCommandsAsync().ConfigureAwait(true);
        _commands.EditRequested += async (_, _) => await OpenEventCommandsAsync().ConfigureAwait(true);
        _commands.InsertRequested += async (_, _) => await OpenEventCommandsAsync().ConfigureAwait(true);
    }

    private async ValueTask BindCurrentAsync()
    {
        if (_recordId <= 0) return;
        await BindAsync([new DatabaseRecordHandle(MvDocumentKind.CommonEvents, _document.RelativePath, _recordId)]).ConfigureAwait(true);
    }

    private async ValueTask CommitNameAsync()
    {
        if (_loading || _recordId <= 0 || _settings is null) return;
        string next = _name.Text ?? string.Empty;
        if (string.Equals(next, _settings.Name, StringComparison.Ordinal)) return;
        await _mutations.ExecuteAsync(
            new SetJsonValueCommand(_document.RelativePath, JsonPath.Root.Append(_recordId).Append("name"), JsonSerializer.Serialize(next), $"Set common event {_recordId} name"),
            cancellationToken: default).ConfigureAwait(true);
        _settings = _settings with { Name = next };
    }

    private async ValueTask CommitTriggerAsync()
    {
        if (_loading || _recordId <= 0 || _settings is null) return;
        int next = Math.Max(0, _trigger.SelectedIndex);
        if (next == _settings.Trigger) return;
        await _mutations.ExecuteAsync(
            new SetJsonValueCommand(_document.RelativePath, JsonPath.Root.Append(_recordId).Append("trigger"), next.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Set common event {_recordId} trigger"),
            cancellationToken: default).ConfigureAwait(true);
        _settings = _settings with { Trigger = next };
    }

    private async ValueTask CommitSwitchAsync()
    {
        if (_loading || _recordId <= 0 || _settings is null) return;
        int next = decimal.ToInt32(_switchId.Value);
        if (next == _settings.SwitchId) return;
        await _mutations.ExecuteAsync(
            new SetJsonValueCommand(_document.RelativePath, JsonPath.Root.Append(_recordId).Append("switchId"), next.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Set common event {_recordId} switch"),
            cancellationToken: default).ConfigureAwait(true);
        _settings = _settings with { SwitchId = next };
    }

    private async Task OpenEventCommandsAsync()
    {
        if (_documentOpener is null || _recordId <= 0) return;
        string name = string.IsNullOrWhiteSpace(_name.Text) ? $"Common Event {_recordId:D4}" : _name.Text;
        DocumentRequest request = new(
            EditorDocumentKind.EventEditor,
            $"event:common:{_recordId}",
            _document.RelativePath,
            MvDocumentKind.CommonEvents,
            RecordId: _recordId,
            DisplayTitle: $"Common Event {_recordId:D4} — {name}",
            EventSurface: "CommonEvent",
            EventPageIndex: 0);
        await _documentOpener(request).ConfigureAwait(true);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int index = 0; index < 8 && current is not null; index++, current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "specifications"))) return current.FullName;
        }
        return null;
    }
}
