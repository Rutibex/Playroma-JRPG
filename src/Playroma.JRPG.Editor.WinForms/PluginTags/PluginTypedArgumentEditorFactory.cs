using System.Globalization;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.PluginTags;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.PluginTags;

internal sealed record PluginArgumentEditorBinding(
    Control Control,
    Func<string> Read,
    Action<string> Write,
    Action<EventHandler> SubscribeChanged);

internal sealed class PluginTypedArgumentEditorFactory
{
    private readonly MvProjectSession projectSession;

    public PluginTypedArgumentEditorFactory(MvProjectSession projectSession)
    {
        this.projectSession = projectSession ?? throw new ArgumentNullException(nameof(projectSession));
    }

    public PluginArgumentEditorBinding Create(PluginArgumentEditorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.EditorKind switch
        {
            PluginGenericEditorKind.Toggle => CreateBoolean(),
            PluginGenericEditorKind.Integer => CreateNumber(descriptor.Argument, 0),
            PluginGenericEditorKind.Decimal => CreateNumber(descriptor.Argument, 4),
            PluginGenericEditorKind.Percentage => CreatePercentage(descriptor.Argument),
            PluginGenericEditorKind.Choice or PluginGenericEditorKind.TargetSelector or
            PluginGenericEditorKind.ParameterSelector or PluginGenericEditorKind.ComparisonOperator or
            PluginGenericEditorKind.ElementSelector or PluginGenericEditorKind.ActionSequenceTarget => CreateChoice(descriptor),
            PluginGenericEditorKind.DatabaseReference => CreateDatabaseReference(descriptor),
            PluginGenericEditorKind.DatabaseReferenceList => CreateIdList(descriptor),
            PluginGenericEditorKind.DatabaseReferenceRange => CreateIdRange(descriptor),
            PluginGenericEditorKind.SwitchReference or PluginGenericEditorKind.VariableReference or
            PluginGenericEditorKind.RegionReference => CreateNumber(descriptor.Argument, 0),
            PluginGenericEditorKind.IconReference => CreateIcon(),
            PluginGenericEditorKind.AssetReference => CreateAsset(descriptor, audio: false),
            PluginGenericEditorKind.AudioReference => CreateAsset(descriptor, audio: true),
            PluginGenericEditorKind.Color => CreateColor(),
            PluginGenericEditorKind.Coordinate => CreateCoordinate(),
            PluginGenericEditorKind.MultilineText or PluginGenericEditorKind.Formula or
            PluginGenericEditorKind.JavaScriptExpression or PluginGenericEditorKind.JavaScriptBlock or
            PluginGenericEditorKind.ActionSequenceCommand => CreateText(multiline: true, code: true),
            _ => CreateText(multiline: false, code: false)
        };
    }

    private static PluginArgumentEditorBinding CreateBoolean()
    {
        CheckBox editor = new() { AutoSize = true, Text = "Enabled" };
        return new(editor,
            () => editor.Checked ? "true" : "false",
            value => editor.Checked = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1",
            handler => editor.CheckedChanged += handler);
    }

    private static PluginArgumentEditorBinding CreateNumber(PluginArgumentDefinition argument, int decimals)
    {
        decimal minimum = argument.Minimum ?? (IsIdentifier(argument.Kind) ? 0m : -1_000_000_000m);
        decimal maximum = argument.Maximum ?? 1_000_000_000m;
        if (minimum > maximum) (minimum, maximum) = (maximum, minimum);
        NumericUpDown editor = new()
        {
            DecimalPlaces = decimals,
            ThousandsSeparator = true,
            Increment = decimals == 0 ? 1m : 0.1m,
            Minimum = minimum,
            Maximum = maximum,
            Dock = DockStyle.Top
        };
        return new(editor,
            () => editor.Value.ToString(CultureInfo.InvariantCulture),
            value => editor.Value = Math.Clamp(ParseDecimal(value), editor.Minimum, editor.Maximum),
            handler => editor.ValueChanged += handler);
    }

    private static PluginArgumentEditorBinding CreatePercentage(PluginArgumentDefinition argument)
    {
        PluginArgumentEditorBinding number = CreateNumber(argument, 3);
        Panel panel = new() { Height = 29, Dock = DockStyle.Top };
        Label suffix = new() { Text = "%", Dock = DockStyle.Right, Width = 24, TextAlign = ContentAlignment.MiddleCenter };
        number.Control.Dock = DockStyle.Fill;
        panel.Controls.Add(number.Control);
        panel.Controls.Add(suffix);
        return new(panel, number.Read, number.Write, number.SubscribeChanged);
    }

    private static PluginArgumentEditorBinding CreateChoice(PluginArgumentEditorDescriptor descriptor)
    {
        ComboBox editor = new()
        {
            DropDownStyle = descriptor.Argument.Kind == PluginArgumentKind.Enumeration && descriptor.EffectiveChoices is { Count: > 0 }
                ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown,
            Dock = DockStyle.Top
        };
        foreach (string choice in descriptor.EffectiveChoices ?? []) editor.Items.Add(choice);
        return new(editor,
            () => Convert.ToString(editor.SelectedItem, CultureInfo.InvariantCulture) ?? editor.Text,
            value =>
            {
                int index = editor.Items.Cast<object>().ToList().FindIndex(item =>
                    string.Equals(Convert.ToString(item, CultureInfo.InvariantCulture), value, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) editor.SelectedIndex = index; else editor.Text = value;
            },
            handler => { editor.SelectedIndexChanged += handler; editor.TextChanged += handler; });
    }

    private PluginArgumentEditorBinding CreateDatabaseReference(PluginArgumentEditorDescriptor descriptor)
    {
        if (!TryMapDocumentKind(descriptor.ReferenceDocumentKind, out MvDocumentKind kind))
            return CreateNumber(descriptor.Argument, 0);
        DatabaseIdPicker editor = new(projectSession, kind)
        {
            Dock = DockStyle.Top,
            AllowNone = !descriptor.Argument.IsRequired
        };
        return new(editor,
            () => editor.SelectedId.ToString(CultureInfo.InvariantCulture),
            value => editor.SelectedId = ParseInt(value),
            handler => editor.ValueChanged += handler);
    }

    private PluginArgumentEditorBinding CreateIdList(PluginArgumentEditorDescriptor descriptor)
    {
        TextBox text = new() { Dock = DockStyle.Fill, PlaceholderText = "1, 2, 3" };
        Button browse = new() { Text = "Select…", Dock = DockStyle.Right, Width = 78 };
        Panel panel = new() { Dock = DockStyle.Top, Height = 29 };
        panel.Controls.Add(text);
        panel.Controls.Add(browse);
        if (TryMapDocumentKind(descriptor.ReferenceDocumentKind, out MvDocumentKind kind))
            browse.Click += (_, _) => BrowseIds(text, kind, multiple: true);
        else browse.Enabled = false;
        return new(panel,
            () => string.Join(", ", PluginTypedValueCodec.ParseIdentifiers(text.Text)),
            value => text.Text = value,
            handler => text.TextChanged += handler);
    }

    private PluginArgumentEditorBinding CreateIdRange(PluginArgumentEditorDescriptor descriptor)
    {
        NumericUpDown first = new() { Minimum = 0, Maximum = 999999, Width = 110 };
        NumericUpDown last = new() { Minimum = 0, Maximum = 999999, Width = 110 };
        FlowLayoutPanel panel = new() { Dock = DockStyle.Top, Height = 31, WrapContents = false };
        panel.Controls.Add(first);
        panel.Controls.Add(new Label { Text = "to", AutoSize = true, Padding = new Padding(4, 6, 4, 0) });
        panel.Controls.Add(last);
        return new(panel,
            () => $"{decimal.ToInt32(first.Value)} to {decimal.ToInt32(last.Value)}",
            value =>
            {
                IReadOnlyList<int> ids = PluginTypedValueCodec.ParseIdentifiers(value);
                if (ids.Count > 0) first.Value = Math.Clamp(ids[0], 0, 999999);
                if (ids.Count > 1) last.Value = Math.Clamp(ids[1], 0, 999999);
            },
            handler => { first.ValueChanged += handler; last.ValueChanged += handler; });
    }

    private PluginArgumentEditorBinding CreateIcon()
    {
        IconPicker editor = new(projectSession) { Dock = DockStyle.Top };
        return new(editor,
            () => editor.Value.ToString(CultureInfo.InvariantCulture),
            value => editor.Value = ParseInt(value),
            handler => editor.ValueChanged += handler);
    }

    private PluginArgumentEditorBinding CreateAsset(PluginArgumentEditorDescriptor descriptor, bool audio)
    {
        AssetCategory category = ResolveAssetCategory(descriptor.AssetCategoryHint, audio);
        AssetPicker editor = new(projectSession, category) { Dock = DockStyle.Top };
        return new(editor,
            () => editor.Value,
            value => editor.Value = value,
            handler => editor.ValueChanged += handler);
    }

    private static PluginArgumentEditorBinding CreateColor()
    {
        TextBox text = new() { Dock = DockStyle.Fill, Text = "#FFFFFF" };
        Button browse = new() { Text = "Choose…", Dock = DockStyle.Right, Width = 78 };
        Panel panel = new() { Dock = DockStyle.Top, Height = 29 };
        panel.Controls.Add(text);
        panel.Controls.Add(browse);
        browse.Click += (_, _) =>
        {
            using ColorDialog dialog = new() { FullOpen = true };
            try { dialog.Color = ColorTranslator.FromHtml(text.Text); } catch (Exception) { }
            if (dialog.ShowDialog(panel) == DialogResult.OK) text.Text = ColorTranslator.ToHtml(dialog.Color);
        };
        return new(panel, () => text.Text, value => text.Text = value,
            handler => text.TextChanged += handler);
    }

    private static PluginArgumentEditorBinding CreateCoordinate()
    {
        NumericUpDown x = new() { Minimum = -999999, Maximum = 999999, DecimalPlaces = 2, Width = 120 };
        NumericUpDown y = new() { Minimum = -999999, Maximum = 999999, DecimalPlaces = 2, Width = 120 };
        FlowLayoutPanel panel = new() { Dock = DockStyle.Top, Height = 31, WrapContents = false };
        panel.Controls.Add(new Label { Text = "X", AutoSize = true, Padding = new Padding(0, 6, 2, 0) });
        panel.Controls.Add(x);
        panel.Controls.Add(new Label { Text = "Y", AutoSize = true, Padding = new Padding(8, 6, 2, 0) });
        panel.Controls.Add(y);
        return new(panel,
            () => $"{x.Value.ToString(CultureInfo.InvariantCulture)}, {y.Value.ToString(CultureInfo.InvariantCulture)}",
            value =>
            {
                string[] parts = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length > 0) x.Value = Math.Clamp(ParseDecimal(parts[0]), x.Minimum, x.Maximum);
                if (parts.Length > 1) y.Value = Math.Clamp(ParseDecimal(parts[1]), y.Minimum, y.Maximum);
            },
            handler => { x.ValueChanged += handler; y.ValueChanged += handler; });
    }

    private static PluginArgumentEditorBinding CreateText(bool multiline, bool code)
    {
        TextBox editor = new()
        {
            Dock = multiline ? DockStyle.Fill : DockStyle.Top,
            Multiline = multiline,
            AcceptsReturn = multiline,
            AcceptsTab = multiline,
            ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
            WordWrap = !code,
            Height = multiline ? 110 : 27,
            Font = code ? new Font(FontFamily.GenericMonospace, 9f) : SystemFonts.MessageBoxFont
        };
        return new(editor, () => editor.Text, value => editor.Text = value,
            handler => editor.TextChanged += handler);
    }

    private void BrowseIds(TextBox target, MvDocumentKind kind, bool multiple)
    {
        MvDatabaseFileSummary? file = projectSession.Workspace.Snapshot.Database.Find(kind);
        if (file is null) return;
        using DatabaseMultiIdPickerDialog dialog = new(projectSession, kind, PluginTypedValueCodec.ParseIdentifiers(target.Text), multiple);
        if (dialog.ShowDialog(target) == DialogResult.OK)
            target.Text = string.Join(", ", dialog.SelectedIds);
    }

    private static bool TryMapDocumentKind(PluginDocumentKind source, out MvDocumentKind target)
    {
        target = source switch
        {
            PluginDocumentKind.Actor => MvDocumentKind.Actors,
            PluginDocumentKind.Animation => MvDocumentKind.Animations,
            PluginDocumentKind.Armor => MvDocumentKind.Armors,
            PluginDocumentKind.Class => MvDocumentKind.Classes,
            PluginDocumentKind.CommonEvent => MvDocumentKind.CommonEvents,
            PluginDocumentKind.Enemy => MvDocumentKind.Enemies,
            PluginDocumentKind.Item => MvDocumentKind.Items,
            PluginDocumentKind.Skill => MvDocumentKind.Skills,
            PluginDocumentKind.State => MvDocumentKind.States,
            PluginDocumentKind.Tileset => MvDocumentKind.Tilesets,
            PluginDocumentKind.Troop => MvDocumentKind.Troops,
            PluginDocumentKind.Weapon => MvDocumentKind.Weapons,
            _ => MvDocumentKind.Unknown
        };
        return target != MvDocumentKind.Unknown;
    }

    private static AssetCategory ResolveAssetCategory(string? hint, bool audio)
    {
        if (!string.IsNullOrWhiteSpace(hint) && Enum.TryParse(hint, true, out AssetCategory parsed)) return parsed;
        return audio ? AssetCategory.SoundEffects : AssetCategory.Pictures;
    }

    private static int ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
    private static decimal ParseDecimal(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : 0m;

    private static bool IsIdentifier(PluginArgumentKind kind) => kind is
        PluginArgumentKind.DatabaseId or PluginArgumentKind.SwitchId or PluginArgumentKind.VariableId or
        PluginArgumentKind.CommonEventId or PluginArgumentKind.AnimationId or PluginArgumentKind.StateId or
        PluginArgumentKind.SkillId or PluginArgumentKind.ItemId or PluginArgumentKind.WeaponId or
        PluginArgumentKind.ArmorId or PluginArgumentKind.ClassId or PluginArgumentKind.ActorId or
        PluginArgumentKind.EnemyId or PluginArgumentKind.RegionId or PluginArgumentKind.IconId;
}

internal sealed class DatabaseMultiIdPickerDialog : Form
{
    private readonly RecordListControl list = new() { Dock = DockStyle.Fill };

    public DatabaseMultiIdPickerDialog(MvProjectSession session, MvDocumentKind kind, IReadOnlyList<int> selected, bool multiple)
    {
        Text = "Select " + kind;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(540, 620);
        MinimizeBox = false;
        MaximizeBox = false;
        list.MultiSelect = multiple;
        Controls.Add(list);
        Button accept = new() { Text = "Select", Dock = DockStyle.Bottom, Height = 36, DialogResult = DialogResult.OK };
        Controls.Add(accept);
        AcceptButton = accept;
        Button cancel = new() { Text = "Cancel", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.Cancel };
        Controls.Add(cancel);
        CancelButton = cancel;
        MvDatabaseFileSummary? file = session.Workspace.Snapshot.Database.Find(kind);
        if (file is not null) _ = BindAsync(session, file, selected);
    }

    public IReadOnlyList<int> SelectedIds => list.SelectedRecordIds;

    private async Task BindAsync(MvProjectSession session, MvDatabaseFileSummary file, IReadOnlyList<int> selected)
    {
        await using IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(file.RelativePath).ConfigureAwait(true);
        if (lease.Document is not Playroma.JRPG.Documents.Mutable.MutableJsonProjectDocument document) return;
        list.SetSource(new Playroma.JRPG.DatabaseEditing.DatabaseRecordIndex(document));
        list.SelectRecords(selected);
    }
}
