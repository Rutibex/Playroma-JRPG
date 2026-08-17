using System.Globalization;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Plugins;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>
/// Metadata-aware editor for RPG Maker MV plugin parameter strings. Unknown parameter types
/// deliberately fall back to a raw text editor so Playroma never discards unsupported values.
/// </summary>
public sealed class PluginParameterValueDialog : Form
{
    private readonly MvProjectSession _session;
    private readonly PluginParameterMetadata _metadata;
    private readonly string _originalValue;
    private readonly Panel _editorHost = new() { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 8) };
    private readonly Label _description = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        MaximumSize = new Size(720, 0),
        Padding = new Padding(12, 8, 12, 4)
    };
    private readonly Label _type = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(12, 0, 12, 4)
    };
    private readonly Label _validation = new()
    {
        Dock = DockStyle.Bottom,
        AutoSize = true,
        ForeColor = Color.Firebrick,
        Padding = new Padding(12, 4, 12, 0)
    };
    private readonly FlowLayoutPanel _buttons = new()
    {
        Dock = DockStyle.Bottom,
        Height = 48,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
        Padding = new Padding(8)
    };
    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _default = new() { Text = "Use Default", AutoSize = true, MinimumSize = new Size(96, 30) };

    private Control? _editor;
    private Func<string>? _readValue;
    private Action<string>? _writeValue;
    private Func<string, string?>? _validateValue;

    public PluginParameterValueDialog(
        MvProjectSession session,
        PluginParameterMetadata metadata,
        string currentValue)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _originalValue = currentValue ?? string.Empty;

        Text = "Plugin Parameter — " + metadata.DisplayName;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(560, 300);
        Size = new Size(760, 460);

        _description.Text = string.IsNullOrWhiteSpace(metadata.Description)
            ? metadata.Name
            : metadata.Description;
        _type.Text = $"Type: {metadata.Type}   Default: {metadata.DefaultValue}";
        _buttons.Controls.AddRange([_ok, _cancel, _default]);

        Controls.Add(_editorHost);
        Controls.Add(_validation);
        Controls.Add(_buttons);
        Controls.Add(_description);
        Controls.Add(_type);

        BuildEditor();
        _writeValue?.Invoke(_originalValue);
        ValidateEditor();

        _default.Click += (_, _) =>
        {
            _writeValue?.Invoke(_metadata.DefaultValue);
            ValidateEditor();
        };
        _ok.Click += (_, _) =>
        {
            string? error = _validateValue?.Invoke(_readValue?.Invoke() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(error)) return;
            DialogResult = DialogResult.None;
            _validation.Text = error;
        };

        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    public string Value => _readValue?.Invoke() ?? _originalValue;

    private void BuildEditor()
    {
        string type = NormalizeType(_metadata.Type);
        if (TryDatabaseKind(type, out MvDocumentKind databaseKind))
        {
            DatabaseIdPicker picker = new(_session, databaseKind)
            {
                Dock = DockStyle.Top,
                AllowNone = !_metadata.Required
            };
            picker.ValueChanged += (_, _) => ValidateEditor();
            InstallEditor(
                picker,
                () => picker.SelectedId.ToString(CultureInfo.InvariantCulture),
                value => picker.SelectedId = ParseInt(value),
                value => ValidateRequiredId(value));
            return;
        }

        if (type is "number" or "integer" or "float")
        {
            int decimals = Math.Clamp(_metadata.Decimals ?? (type == "integer" ? 0 : 4), 0, 8);
            decimal minimum = ParseDecimal(_metadata.Minimum, -1_000_000_000m);
            decimal maximum = ParseDecimal(_metadata.Maximum, 1_000_000_000m);
            if (minimum > maximum) (minimum, maximum) = (maximum, minimum);

            NumericUpDown number = new()
            {
                Dock = DockStyle.Top,
                DecimalPlaces = decimals,
                ThousandsSeparator = true,
                Increment = decimals == 0 ? 1m : 1m / Pow10(decimals),
                Height = 28
            };
            // Expand both bounds before applying metadata values. NumericUpDown defaults to
            // 0..100, so either a wholly negative range or a minimum above 100 can otherwise
            // throw while the control is being initialized.
            number.Maximum = decimal.MaxValue;
            number.Minimum = decimal.MinValue;
            number.Maximum = maximum;
            number.Minimum = minimum;
            number.ValueChanged += (_, _) => ValidateEditor();
            InstallEditor(
                number,
                () => number.Value.ToString(decimals == 0 ? "0" : "0." + new string('#', decimals), CultureInfo.InvariantCulture),
                value => number.Value = Math.Clamp(ParseDecimal(value, ParseDecimal(_metadata.DefaultValue, 0m)), number.Minimum, number.Maximum),
                _ => null);
            return;
        }

        if (type is "boolean" or "bool")
        {
            ComboBox boolean = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            IReadOnlyList<(string Text, string Value)> options = _metadata.EffectiveOptions.Count > 0
                ? _metadata.EffectiveOptions
                : [("On", "true"), ("Off", "false")];
            foreach ((string text, string value) in options) boolean.Items.Add(new OptionItem(text, value));
            boolean.SelectedIndexChanged += (_, _) => ValidateEditor();
            InstallEditor(
                boolean,
                () => (boolean.SelectedItem as OptionItem)?.Value ?? "false",
                value => SelectOption(boolean, value),
                value => _metadata.Required && string.IsNullOrWhiteSpace(value) ? "A value is required." : null);
            return;
        }

        if (type is "select" or "combo" || _metadata.EffectiveOptions.Count > 0)
        {
            ComboBox choices = new()
            {
                Dock = DockStyle.Top,
                DropDownStyle = type == "select" ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown
            };
            foreach ((string text, string value) in _metadata.EffectiveOptions) choices.Items.Add(new OptionItem(text, value));
            choices.SelectedIndexChanged += (_, _) => ValidateEditor();
            choices.TextChanged += (_, _) => ValidateEditor();
            InstallEditor(
                choices,
                () => choices.SelectedItem is OptionItem option ? option.Value : choices.Text,
                value => SelectOption(choices, value),
                ValidateRequiredText);
            return;
        }

        if (type is "file" or "filename" || type.StartsWith("file<", StringComparison.Ordinal))
        {
            TextBox path = new() { Dock = DockStyle.Fill };
            Button browse = new() { Text = "Browse…", Dock = DockStyle.Right, Width = 92 };
            Panel panel = new() { Dock = DockStyle.Top, Height = 30 };
            panel.Controls.Add(path);
            panel.Controls.Add(browse);
            path.TextChanged += (_, _) => ValidateEditor();
            browse.Click += (_, _) => BrowseFile(path);
            InstallEditor(panel, () => path.Text, value => path.Text = value, ValidateRequiredText);
            return;
        }

        bool multiline = type.Contains("note", StringComparison.Ordinal)
            || type.Contains("multiline", StringComparison.Ordinal)
            || type.StartsWith("struct<", StringComparison.Ordinal)
            || type.EndsWith("[]", StringComparison.Ordinal)
            || type is "json";
        TextBox textEditor = new()
        {
            Dock = DockStyle.Fill,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
            WordWrap = multiline,
            AcceptsReturn = multiline,
            AcceptsTab = multiline,
            Font = multiline ? new Font(FontFamily.GenericMonospace, 9f) : SystemFonts.MessageBoxFont
        };
        textEditor.TextChanged += (_, _) => ValidateEditor();
        InstallEditor(
            textEditor,
            () => textEditor.Text,
            value => textEditor.Text = value,
            value => ValidateRaw(type, value));
    }

    private void InstallEditor(
        Control editor,
        Func<string> readValue,
        Action<string> writeValue,
        Func<string, string?> validateValue)
    {
        _editor = editor;
        _readValue = readValue;
        _writeValue = writeValue;
        _validateValue = validateValue;
        _editorHost.Controls.Clear();
        _editorHost.Controls.Add(editor);
    }

    private void ValidateEditor()
    {
        string value = _readValue?.Invoke() ?? string.Empty;
        string? error = _validateValue?.Invoke(value);
        _validation.Text = error ?? string.Empty;
        _ok.Enabled = string.IsNullOrWhiteSpace(error);
    }

    private string? ValidateRequiredText(string value) =>
        _metadata.Required && string.IsNullOrWhiteSpace(value) ? "A value is required." : null;

    private string? ValidateRequiredId(string value)
    {
        if (!_metadata.Required) return null;
        return ParseInt(value) <= 0 ? "Select a database record." : null;
    }

    private string? ValidateRaw(string type, string value)
    {
        string? required = ValidateRequiredText(value);
        if (required is not null) return required;
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!type.StartsWith("struct<", StringComparison.Ordinal)
            && !type.EndsWith("[]", StringComparison.Ordinal)
            && type is not "json")
        {
            return null;
        }

        try
        {
            using JsonDocument _ = JsonDocument.Parse(value);
            return null;
        }
        catch (JsonException exception)
        {
            return $"The value must be valid JSON: {exception.Message}";
        }
    }

    private void BrowseFile(TextBox target)
    {
        string projectRoot = _session.Workspace.Snapshot.Identity.ProjectRoot;
        string? configuredDirectory = _metadata.Directory;
        string initial = string.IsNullOrWhiteSpace(configuredDirectory)
            ? projectRoot
            : Path.Combine(projectRoot, configuredDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(initial)) initial = projectRoot;
        using OpenFileDialog dialog = new()
        {
            Title = "Select Plugin Parameter File",
            InitialDirectory = initial,
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string relative = Path.GetRelativePath(projectRoot, dialog.FileName).Replace('\\', '/');
        if (!relative.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
        {
            relative = Path.ChangeExtension(relative, null)?.Replace('\\', '/') ?? relative;
            string? directory = configuredDirectory?.Trim('/').Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(directory)
                && relative.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative[(directory.Length + 1)..];
            }
            target.Text = relative;
        }
        else
        {
            target.Text = dialog.FileName;
        }
    }

    private static void SelectOption(ComboBox combo, string value)
    {
        for (int index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is OptionItem option
                && string.Equals(option.Value, value, StringComparison.Ordinal))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
        if (combo.DropDownStyle != ComboBoxStyle.DropDownList) combo.Text = value;
        else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static bool TryDatabaseKind(string type, out MvDocumentKind kind)
    {
        bool isArray = type.EndsWith("[]", StringComparison.Ordinal);
        string singular = isArray ? type[..^2] : type;
        kind = singular switch
        {
            "actor" => MvDocumentKind.Actors,
            "class" => MvDocumentKind.Classes,
            "skill" => MvDocumentKind.Skills,
            "item" => MvDocumentKind.Items,
            "weapon" => MvDocumentKind.Weapons,
            "armor" or "armour" => MvDocumentKind.Armors,
            "enemy" => MvDocumentKind.Enemies,
            "troop" => MvDocumentKind.Troops,
            "state" => MvDocumentKind.States,
            "animation" => MvDocumentKind.Animations,
            "tileset" => MvDocumentKind.Tilesets,
            "common_event" or "commonevent" => MvDocumentKind.CommonEvents,
            _ => MvDocumentKind.Unknown
        };
        return kind != MvDocumentKind.Unknown && !isArray;
    }

    private static string NormalizeType(string? type)
    {
        string normalized = (type ?? "string").Trim().ToLowerInvariant();
        int generic = normalized.IndexOf('<');
        return generic >= 0 && normalized.StartsWith("select<", StringComparison.Ordinal)
            ? "select"
            : normalized;
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static decimal ParseDecimal(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : fallback;

    private static decimal Pow10(int exponent)
    {
        decimal value = 1m;
        for (int index = 0; index < exponent; index++) value *= 10m;
        return value;
    }

    private sealed record OptionItem(string Text, string Value)
    {
        public override string ToString() => Text;
    }
}
