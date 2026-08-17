using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Mapping;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.Editor.WinForms.PluginTags;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Documents;

/// <summary>Transactional editor for every standard MV map property, plus resize planning.</summary>
public sealed class MapPropertiesControl : UserControl
{
    private static readonly PropertyDefinition[] Definitions =
    [
        new("Display name", "displayName", PropertyKind.String),
        new("Tileset ID", "tilesetId", PropertyKind.Integer),
        new("Width", "width", PropertyKind.Integer, false),
        new("Height", "height", PropertyKind.Integer, false),
        new("Scroll type", "scrollType", PropertyKind.Integer),
        new("Encounter step", "encounterStep", PropertyKind.Integer),
        new("Disable dashing", "disableDashing", PropertyKind.Boolean),
        new("Specify battleback", "specifyBattleback", PropertyKind.Boolean),
        new("Battleback 1", "battleback1Name", PropertyKind.String),
        new("Battleback 2", "battleback2Name", PropertyKind.String),
        new("Autoplay BGM", "autoplayBgm", PropertyKind.Boolean),
        new("BGM definition", "bgm", PropertyKind.RawJson),
        new("Autoplay BGS", "autoplayBgs", PropertyKind.Boolean),
        new("BGS definition", "bgs", PropertyKind.RawJson),
        new("Parallax image", "parallaxName", PropertyKind.String),
        new("Parallax loop X", "parallaxLoopX", PropertyKind.Boolean),
        new("Parallax loop Y", "parallaxLoopY", PropertyKind.Boolean),
        new("Parallax speed X", "parallaxSx", PropertyKind.Integer),
        new("Parallax speed Y", "parallaxSy", PropertyKind.Integer),
        new("Parallax show zero", "parallaxShow", PropertyKind.Boolean),
        new("Encounter list", "encounterList", PropertyKind.RawJson)
    ];

    private readonly IMutableProjectDocument _document;
    private readonly MvMapDescriptor _descriptor;
    private readonly DataGridView _properties = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly ListView _transfers = ControlHelpers.CreateDetailsList(("Direction", 80), ("Map", 80), ("Source", 260), ("Location", 180));
    private readonly ListView _events = ControlHelpers.CreateDetailsList(("ID", 60), ("Name", 220), ("Position", 90), ("Pages", 60), ("Commands", 80));
    private readonly TextBox _note = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 9), AcceptsTab = true };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Panel _pluginTagsHost = new() { Dock = DockStyle.Fill };
    private PluginTagsEditorControl? _pluginTags;
    private PluginTextEditingSession? _noteSession;
    private bool _syncingNote;
    private readonly Button _apply = new() { Text = "Apply", AutoSize = true };
    private readonly Button _reload = new() { Text = "Reload", AutoSize = true };
    private readonly Button _resize = new() { Text = "Resize Map…", AutoSize = true };
    private readonly Button _generateText = new() { Text = "✨ Generate Text…", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
    private readonly Dictionary<string, string> _originalRaw = new(StringComparer.Ordinal);
    private MvProjectSession? _session;
    private JsonDocument? _loadedJson;

    public MapPropertiesControl(IMutableProjectDocument document, MvMapDescriptor descriptor)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Dock = DockStyle.Fill;
        _properties.Columns.Add(new DataGridViewTextBoxColumn { Name = "Property", HeaderText = "Property", ReadOnly = true, FillWeight = 35 });
        _properties.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 65 });

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6), WrapContents = false };
        buttons.Controls.AddRange([_apply, _reload, _resize, _generateText, _status]);
        _tabs.TabPages.Add(new TabPage("Properties") { Controls = { _properties } });
        _tabs.TabPages.Add(new TabPage("Events") { Controls = { _events } });
        _tabs.TabPages.Add(new TabPage("Map Transfers") { Controls = { _transfers } });
        _tabs.TabPages.Add(new TabPage("Note") { Controls = { _note } });
        _tabs.TabPages.Add(new TabPage("Plugin Tags") { Controls = { _pluginTagsHost } });
        Controls.Add(_tabs);
        Controls.Add(buttons);

        _apply.Click += async (_, _) => await ApplyAsync().ConfigureAwait(true);
        _reload.Click += async (_, _) =>
        {
            if (_session is not null) await LoadAsync(_session).ConfigureAwait(true);
        };
        _resize.Click += async (_, _) => await ResizeAsync().ConfigureAwait(true);
        _generateText.Click += (_, _) => ShowGenerateMenu();
        _note.TextChanged += (_, _) =>
        {
            if (!_syncingNote && _noteSession is not null) _noteSession.ReplaceText(_note.Text);
        };
    }


    public event EventHandler<MapTextGenerationRequestedEventArgs>? TextGenerationRequested;

    private void ShowGenerateMenu()
    {
        ContextMenuStrip menu = new();
        menu.Items.Add("Editor Name", null, async (_, _) => await RequestTextGenerationAsync("editorName").ConfigureAwait(true));
        menu.Items.Add("Display Name", null, async (_, _) => await RequestTextGenerationAsync("displayName").ConfigureAwait(true));
        menu.Items.Add("Map Note", null, async (_, _) => await RequestTextGenerationAsync("note").ConfigureAwait(true));
        menu.Closed += (_, _) => menu.Dispose();
        menu.Show(_generateText, new Point(0, _generateText.Height));
    }

    private async Task RequestTextGenerationAsync(string fieldKey)
    {
        if (_session is null) return;
        if (HasPendingTextEdits())
        {
            DialogResult choice = MessageBox.Show(this,
                "This map has unapplied property or Note edits. Apply them before Text Studio opens so generation uses the values you currently see?",
                "Apply Map Changes First", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) return;
            await ApplyAsync().ConfigureAwait(true);
            if (HasPendingTextEdits()) return;
        }
        TextGenerationRequested?.Invoke(this, new MapTextGenerationRequestedEventArgs(_descriptor.Id, _descriptor.RelativePath, fieldKey));
    }

    private bool HasPendingTextEdits()
    {
        foreach (DataGridViewRow row in _properties.Rows)
        {
            if (row.Tag is not PropertyDefinition definition || !definition.Editable) continue;
            string display = Convert.ToString(row.Cells[1].Value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
            string raw;
            try { raw = FromDisplay(definition.Kind, display); }
            catch { return true; }
            if (!_originalRaw.TryGetValue(definition.JsonName, out string? original) || !JsonSemanticallyEqual(original, raw)) return true;
        }
        string noteRaw = JsonSerializer.Serialize(_noteSession?.WorkingText ?? _note.Text);
        return !_originalRaw.TryGetValue("note", out string? originalNote) || !JsonSemanticallyEqual(originalNote, noteRaw);
    }

    public async ValueTask LoadAsync(MvProjectSession session, CancellationToken cancellationToken = default)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _loadedJson?.Dispose();
        ReadOnlyMemory<byte> bytes = await _document.GenerateBytesAsync(cancellationToken).ConfigureAwait(true);
        _loadedJson = JsonDocument.Parse(bytes);
        JsonElement root = _loadedJson.RootElement;
        _originalRaw.Clear();
        _properties.Rows.Clear();
        foreach (PropertyDefinition definition in Definitions)
        {
            string raw = root.TryGetProperty(definition.JsonName, out JsonElement value) ? value.GetRawText() : DefaultRaw(definition.Kind);
            _originalRaw[definition.JsonName] = raw;
            string display = ToDisplay(definition.Kind, raw);
            int rowIndex = _properties.Rows.Add(definition.DisplayName, display);
            DataGridViewRow row = _properties.Rows[rowIndex];
            row.Tag = definition;
            row.Cells[1].ReadOnly = !definition.Editable;
            if (!definition.Editable) row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }
        string note = root.TryGetProperty("note", out JsonElement noteElement) && noteElement.ValueKind == JsonValueKind.String
            ? noteElement.GetString() ?? string.Empty
            : string.Empty;
        _syncingNote = true;
        try { _note.Text = note; }
        finally { _syncingNote = false; }
        _originalRaw["note"] = JsonSerializer.Serialize(note);
        await BindPluginTagsAsync(session, note, cancellationToken).ConfigureAwait(true);
        PopulateEvents(root);
        PopulateTransfers(session);
        _status.Text = $"Map {_descriptor.Id:D3} — {_descriptor.Name}";
    }

    private async Task BindPluginTagsAsync(MvProjectSession session, string note, CancellationToken cancellationToken)
    {
        if (_noteSession is not null)
        {
            _noteSession.WorkingTextChanged -= OnPluginNoteChanged;
        }
        PluginTagContext context = new(
            PluginAuthoringSurface.MapNote,
            PluginDocumentKind.Map,
            session.Workspace.Snapshot.Identity.ProjectRoot,
            _descriptor.RelativePath,
            _descriptor.Id);
        _noteSession = new PluginTextEditingSession(PluginTagCatalogHost.Catalog, context, note);
        _noteSession.WorkingTextChanged += OnPluginNoteChanged;

        if (_pluginTags is null)
        {
            _pluginTags = new PluginTagsEditorControl(PluginTagCatalogHost.Catalog, session);
            _pluginTags.ConfigureHost("Apply Map", "Open Map Note", showRecipes: false);
            _pluginTags.ApplyRequested += async (_, _) => await ApplyAsync().ConfigureAwait(true);
            _pluginTags.OpenNotesRequested += (_, _) => _tabs.SelectedIndex = 3;
            _pluginTagsHost.Controls.Add(_pluginTags);
        }
        _pluginTags.Bind(_noteSession, null, null, "Scanning installed plugins…");
        PluginTagProjectIntelligence intelligence = await YanflyPluginTagProjectCache
            .GetAsync(session.Workspace.Snapshot.Identity.ProjectRoot, PluginTagCatalogHost.Catalog)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);
        if (_noteSession.Context == context) _pluginTags.SetProjectIntelligence(intelligence);
    }

    private void OnPluginNoteChanged(object? sender, PluginTextChangedEventArgs args)
    {
        if (_syncingNote || string.Equals(_note.Text, args.CurrentText, StringComparison.Ordinal)) return;
        _syncingNote = true;
        try
        {
            int selection = _note.SelectionStart;
            _note.Text = args.CurrentText;
            _note.SelectionStart = Math.Min(selection, _note.TextLength);
        }
        finally { _syncingNote = false; }
    }

    private async Task ApplyAsync()
    {
        MvProjectSession? session = _session;
        if (session is null) return;
        List<IEditorCommand> commands = [];
        try
        {
            foreach (DataGridViewRow row in _properties.Rows)
            {
                if (row.Tag is not PropertyDefinition definition || !definition.Editable) continue;
                string display = Convert.ToString(row.Cells[1].Value, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
                string raw = FromDisplay(definition.Kind, display);
                if (!_originalRaw.TryGetValue(definition.JsonName, out string? original) || !JsonSemanticallyEqual(original, raw))
                {
                    commands.Add(new SetJsonValueCommand(_descriptor.RelativePath, JsonPath.Root.Append(definition.JsonName), raw, $"Change map {definition.DisplayName}"));
                }
            }
            string noteRaw = JsonSerializer.Serialize(_noteSession?.WorkingText ?? _note.Text);
            if (!_originalRaw.TryGetValue("note", out string? originalNote) || !JsonSemanticallyEqual(originalNote, noteRaw))
            {
                commands.Add(new SetJsonValueCommand(_descriptor.RelativePath, JsonPath.Root.Append("note"), noteRaw, "Change map note"));
            }
            if (commands.Count == 0)
            {
                _status.Text = "No property changes.";
                return;
            }
            await using ProjectTransaction transaction = session.BeginTransaction("Edit map properties");
            foreach (IEditorCommand command in commands) transaction.Add(command);
            await transaction.CommitAsync().ConfigureAwait(true);
            _status.Text = $"Applied {commands.Count} property change(s).";
            await LoadAsync(session).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Map property update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ResizeAsync()
    {
        MvProjectSession? session = _session;
        JsonDocument? json = _loadedJson;
        if (session is null || json is null) return;
        JsonElement root = json.RootElement;
        int width = root.GetProperty("width").GetInt32();
        int height = root.GetProperty("height").GetInt32();
        if (!root.TryGetProperty("data", out JsonElement dataElement) || dataElement.ValueKind != JsonValueKind.Array) return;
        int[] data = dataElement.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        int layerCount = data.Length / checked(width * height);
        EditableMapBuffer buffer = new(width, height, layerCount, data);
        List<MapEventPosition> events = ReadEventPositions(root);
        using MapResizeDialog dialog = new(width, height);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        MapResizePlan plan = new MapResizePlanner().CreatePlan(
            buffer,
            dialog.MapWidth,
            dialog.MapHeight,
            dialog.Anchor,
            events,
            dialog.DeleteCroppedEvents);
        int unresolved = plan.Events.Count(item => !item.IsDeleted && item.After is null);
        string summary = $"Preserved {plan.PreservedSourceBounds.Width}×{plan.PreservedSourceBounds.Height} cells.\n"
            + $"Cropped cells: {plan.CroppedCellCount:N0}\nAdded cells: {plan.AddedCellCount:N0}\n"
            + $"Relocated events: {plan.Events.Count(item => item.After is not null):N0}\n"
            + $"Deleted events: {plan.Events.Count(item => item.IsDeleted):N0}\n"
            + $"Events requiring cancellation: {unresolved:N0}\n\nApply this resize as one undoable transaction?";
        if (unresolved > 0)
        {
            MessageBox.Show(this, "One or more events would be cropped. Enable deletion of cropped events or choose another anchor.", "Resize blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, summary, "Resize Map", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            await using ProjectTransaction transaction = session.BeginTransaction("Resize map");
            transaction.Add(new ResizeMapCommand(_descriptor.RelativePath, plan));
            await transaction.CommitAsync().ConfigureAwait(true);
            _status.Text = $"Map resized to {dialog.MapWidth}×{dialog.MapHeight}.";
            await LoadAsync(session).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Resize failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PopulateEvents(JsonElement root)
    {
        _events.Items.Clear();
        if (!root.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement eventElement in events.EnumerateArray())
        {
            if (eventElement.ValueKind != JsonValueKind.Object) continue;
            int id = TryInt(eventElement, "id");
            string name = eventElement.TryGetProperty("name", out JsonElement nameValue) ? nameValue.GetString() ?? string.Empty : string.Empty;
            int x = TryInt(eventElement, "x");
            int y = TryInt(eventElement, "y");
            int pages = eventElement.TryGetProperty("pages", out JsonElement pagesValue) && pagesValue.ValueKind == JsonValueKind.Array ? pagesValue.GetArrayLength() : 0;
            int commands = 0;
            if (pagesValue.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement page in pagesValue.EnumerateArray())
                {
                    if (page.ValueKind == JsonValueKind.Object && page.TryGetProperty("list", out JsonElement list) && list.ValueKind == JsonValueKind.Array) commands += list.GetArrayLength();
                }
            }
            _events.Items.Add(new ListViewItem([id.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), name, $"{x}, {y}", pages.ToString(System.Globalization.CultureInfo.InvariantCulture), commands.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        }
    }

    private void PopulateTransfers(MvProjectSession session)
    {
        _transfers.Items.Clear();
        foreach (Playroma.JRPG.Indexing.ProjectReference reference in session.References.All.Where(reference => reference.Target.Kind == Playroma.JRPG.Indexing.ReferenceTargetKind.Map && reference.Target.Id == _descriptor.Id))
        {
            _transfers.Items.Add(new ListViewItem(["Incoming", _descriptor.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), reference.Source.RelativePath, reference.Source.JsonPath]));
        }
        foreach (Playroma.JRPG.Indexing.ProjectReference reference in session.References.FindOutbound(_descriptor.RelativePath).Where(reference => reference.Target.Kind == Playroma.JRPG.Indexing.ReferenceTargetKind.Map))
        {
            _transfers.Items.Add(new ListViewItem(["Outgoing", reference.Target.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?", reference.Source.RelativePath, reference.Source.JsonPath]));
        }
    }

    private static List<MapEventPosition> ReadEventPositions(JsonElement root)
    {
        List<MapEventPosition> result = [];
        if (!root.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array) return result;
        foreach (JsonElement eventElement in events.EnumerateArray())
        {
            if (eventElement.ValueKind != JsonValueKind.Object) continue;
            result.Add(new MapEventPosition(
                TryInt(eventElement, "id"),
                TryInt(eventElement, "x"),
                TryInt(eventElement, "y"),
                eventElement.TryGetProperty("name", out JsonElement name) ? name.GetString() ?? string.Empty : string.Empty));
        }
        return result;
    }

    private static int TryInt(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement property) && property.TryGetInt32(out int result) ? result : 0;

    private static string DefaultRaw(PropertyKind kind) => kind switch
    {
        PropertyKind.String => "\"\"",
        PropertyKind.Integer => "0",
        PropertyKind.Boolean => "false",
        PropertyKind.RawJson => "null",
        _ => "null"
    };

    private static string ToDisplay(PropertyKind kind, string raw) => kind switch
    {
        PropertyKind.String => JsonSerializer.Deserialize<string>(raw) ?? string.Empty,
        _ => raw
    };

    private static string FromDisplay(PropertyKind kind, string display) => kind switch
    {
        PropertyKind.String => JsonSerializer.Serialize(display),
        PropertyKind.Integer => int.Parse(display, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture),
        PropertyKind.Boolean => bool.Parse(display).ToString().ToLowerInvariant(),
        PropertyKind.RawJson => ValidateRawJson(display),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string ValidateRawJson(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);
        return document.RootElement.GetRawText();
    }

    private static bool JsonSemanticallyEqual(string first, string second)
    {
        using JsonDocument left = JsonDocument.Parse(first);
        using JsonDocument right = JsonDocument.Parse(second);
        return JsonElement.DeepEquals(left.RootElement, right.RootElement);
    }

    private enum PropertyKind { String, Integer, Boolean, RawJson }
    private sealed record PropertyDefinition(string DisplayName, string JsonName, PropertyKind Kind, bool Editable = true);
}

internal sealed class MapResizeDialog : Form
{
    private readonly NumericUpDown _width = new() { Minimum = 1, Maximum = 999, Width = 100 };
    private readonly NumericUpDown _height = new() { Minimum = 1, Maximum = 999, Width = 100 };
    private readonly ComboBox _anchor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly CheckBox _deleteEvents = new() { Text = "Delete events cropped by the new bounds", AutoSize = true };

    public MapResizeDialog(int width, int height)
    {
        Text = "Resize Map";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _width.Value = width;
        _height.Value = height;
        _anchor.DataSource = Enum.GetValues<MapResizeAnchor>();
        _anchor.SelectedItem = MapResizeAnchor.Centre;
        TableLayoutPanel layout = new() { AutoSize = true, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
        layout.Controls.Add(new Label { Text = "Width:", AutoSize = true }, 0, 0);
        layout.Controls.Add(_width, 1, 0);
        layout.Controls.Add(new Label { Text = "Height:", AutoSize = true }, 0, 1);
        layout.Controls.Add(_height, 1, 1);
        layout.Controls.Add(new Label { Text = "Anchor:", AutoSize = true }, 0, 2);
        layout.Controls.Add(_anchor, 1, 2);
        layout.Controls.Add(_deleteEvents, 0, 3);
        layout.SetColumnSpan(_deleteEvents, 2);
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public int MapWidth => decimal.ToInt32(_width.Value);
    public int MapHeight => decimal.ToInt32(_height.Value);
    public MapResizeAnchor Anchor => _anchor.SelectedItem is MapResizeAnchor anchor ? anchor : MapResizeAnchor.Centre;
    public bool DeleteCroppedEvents => _deleteEvents.Checked;
}
