using Playroma.JRPG.AnimationEditing;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;

namespace Playroma.JRPG.Editor.WinForms.Database;

internal sealed class AnimationCellDialog : Form
{
    private readonly NumericUpDown _pattern = Number(-1, 999, 0);
    private readonly NumericUpDown _x = Number(-99999, 99999, 2);
    private readonly NumericUpDown _y = Number(-99999, 99999, 2);
    private readonly NumericUpDown _scale = Number(-9999, 9999, 2);
    private readonly NumericUpDown _rotation = Number(-99999, 99999, 2);
    private readonly CheckBox _mirror = new() { Text = "Mirror horizontally", AutoSize = true };
    private readonly NumericUpDown _opacity = Number(-9999, 9999, 2);
    private readonly ComboBox _blend = Phase9BEditorUi.OptionCombo(new Dictionary<int, string>
    {
        [0] = "Normal", [1] = "Additive", [2] = "Multiply", [3] = "Screen"
    });

    public AnimationCellDialog(MvAnimationCellSnapshot cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        Text = "Edit Animation Cell";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Width = 470;
        Height = 490;
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Pattern", _pattern);
        Phase9BEditorUi.AddRow(table, 1, "X", _x);
        Phase9BEditorUi.AddRow(table, 2, "Y", _y);
        Phase9BEditorUi.AddRow(table, 3, "Scale (%)", _scale);
        Phase9BEditorUi.AddRow(table, 4, "Rotation (°)", _rotation);
        Phase9BEditorUi.AddRow(table, 5, "Mirror", _mirror);
        Phase9BEditorUi.AddRow(table, 6, "Opacity", _opacity);
        Phase9BEditorUi.AddRow(table, 7, "Blend", _blend);
        Controls.Add(table);
        Controls.Add(BuildButtons());
        _pattern.Value = cell.Pattern;
        _x.Value = Clamp(_x, cell.X);
        _y.Value = Clamp(_y, cell.Y);
        _scale.Value = Clamp(_scale, cell.Scale);
        _rotation.Value = Clamp(_rotation, cell.Rotation);
        _mirror.Checked = cell.Mirror;
        _opacity.Value = Clamp(_opacity, cell.Opacity);
        Phase9BEditorUi.SelectOption(_blend, cell.BlendMode);
        Cell = cell;
    }

    public MvAnimationCellSnapshot Cell { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Cell = Cell with
            {
                Pattern = decimal.ToInt32(_pattern.Value),
                X = decimal.ToDouble(_x.Value),
                Y = decimal.ToDouble(_y.Value),
                Scale = decimal.ToDouble(_scale.Value),
                Rotation = decimal.ToDouble(_rotation.Value),
                Mirror = _mirror.Checked,
                Opacity = decimal.ToDouble(_opacity.Value),
                BlendMode = Phase9BEditorUi.SelectedOption(_blend)
            };
        }
        base.OnFormClosing(e);
    }

    private Control BuildButtons()
    {
        FlowLayoutPanel panel = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.Add(new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK });
        panel.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
        return panel;
    }

    private static NumericUpDown Number(decimal minimum, decimal maximum, int decimals) => new()
    {
        Dock = DockStyle.Fill,
        Minimum = minimum,
        Maximum = maximum,
        DecimalPlaces = decimals,
        Increment = decimals == 0 ? 1 : 0.1M
    };

    private static decimal Clamp(NumericUpDown control, double value) => Math.Clamp((decimal)value, control.Minimum, control.Maximum);
}

internal sealed class AnimationTweenDialog : Form
{
    private readonly NumericUpDown _start;
    private readonly NumericUpDown _end;
    private readonly TextBox _lanes = new() { Dock = DockStyle.Fill, PlaceholderText = "Example: 0,1,2 or blank for all" };
    private readonly CheckedListBox _properties = new() { Dock = DockStyle.Fill, Height = 120 };
    private readonly ComboBox _easing = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _rotation = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _createMissing = new() { Text = "Create missing lanes by fading from/to zero opacity", AutoSize = true };
    private readonly CheckBox _overwrite = new() { Text = "Overwrite active intermediate cells", AutoSize = true, Checked = true };
    private readonly NumericUpDown _rotations = Phase9BEditorUi.Number(0, 20);

    public AnimationTweenDialog(int frameCount, int currentFrame)
    {
        Text = "Tween Animation Frames";
        StartPosition = FormStartPosition.CenterParent;
        Width = 590;
        Height = 620;
        _start = Phase9BEditorUi.Number(1, Math.Max(1, frameCount));
        _end = Phase9BEditorUi.Number(1, Math.Max(1, frameCount));
        _start.Value = Math.Clamp(currentFrame + 1, 1, Math.Max(1, frameCount));
        _end.Value = Math.Clamp(currentFrame + 2, 1, Math.Max(1, frameCount));
        _properties.Items.AddRange(["X", "Y", "Scale", "Rotation", "Opacity"]);
        for (int index = 0; index < _properties.Items.Count; index++) _properties.SetItemChecked(index, true);
        _easing.Items.AddRange(Enum.GetNames<AnimationInterpolationMode>());
        _easing.SelectedIndex = 0;
        _rotation.Items.AddRange(Enum.GetNames<AnimationRotationMode>());
        _rotation.SelectedItem = nameof(AnimationRotationMode.ShortestPath);
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Start Frame", _start);
        Phase9BEditorUi.AddRow(table, 1, "End Frame", _end);
        Phase9BEditorUi.AddRow(table, 2, "Cell Lanes", _lanes);
        Phase9BEditorUi.AddRow(table, 3, "Properties", _properties);
        Phase9BEditorUi.AddRow(table, 4, "Easing", _easing);
        Phase9BEditorUi.AddRow(table, 5, "Rotation Path", _rotation);
        Phase9BEditorUi.AddRow(table, 6, "Extra Full Rotations", _rotations);
        Phase9BEditorUi.AddRow(table, 7, "Missing Lanes", _createMissing);
        Phase9BEditorUi.AddRow(table, 8, "Intermediate Cells", _overwrite);
        Controls.Add(table);
        Controls.Add(Buttons());
    }

    public AnimationTweenRequest Request
    {
        get
        {
            HashSet<int> lanes = ParseIndexes(_lanes.Text);
            return new AnimationTweenRequest(
                decimal.ToInt32(_start.Value) - 1,
                decimal.ToInt32(_end.Value) - 1,
                lanes,
                TweenX: _properties.GetItemChecked(0),
                TweenY: _properties.GetItemChecked(1),
                TweenScale: _properties.GetItemChecked(2),
                TweenRotation: _properties.GetItemChecked(3),
                TweenOpacity: _properties.GetItemChecked(4),
                Interpolation: Enum.Parse<AnimationInterpolationMode>(_easing.SelectedItem?.ToString() ?? nameof(AnimationInterpolationMode.Linear)),
                RotationMode: Enum.Parse<AnimationRotationMode>(_rotation.SelectedItem?.ToString() ?? nameof(AnimationRotationMode.ShortestPath)),
                CreateMissingLanes: _createMissing.Checked,
                OverwriteIntermediate: _overwrite.Checked,
                AdditionalRotations: decimal.ToInt32(_rotations.Value));
        }
    }

    private static HashSet<int> ParseIndexes(string value) => value.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
        .Select(part => int.TryParse(part, out int parsed) ? parsed : -1).Where(index => index >= 0).ToHashSet();

    private static Control Buttons()
    {
        FlowLayoutPanel panel = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.Add(new Button { Text = "Apply Tween", AutoSize = true, DialogResult = DialogResult.OK });
        panel.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
        return panel;
    }
}

internal sealed class AnimationBatchDialog : Form
{
    private readonly NumericUpDown _x = Number(-99999, 99999, 2, 0);
    private readonly NumericUpDown _y = Number(-99999, 99999, 2, 0);
    private readonly NumericUpDown _scale = Number(0.01M, 100M, 3, 1);
    private readonly NumericUpDown _rotation = Number(-99999, 99999, 2, 0);
    private readonly CheckBox _setOpacity = new() { Text = "Set opacity", AutoSize = true };
    private readonly NumericUpDown _opacity = Number(-9999, 9999, 2, 255);
    private readonly CheckBox _mirror = new() { Text = "Toggle mirror", AutoSize = true };
    private readonly CheckBox _replacePattern = new() { Text = "Replace pattern", AutoSize = true };
    private readonly NumericUpDown _pattern = Number(-1, 999, 0, -1);
    private readonly CheckBox _setBlend = new() { Text = "Set blend mode", AutoSize = true };
    private readonly ComboBox _blend = Phase9BEditorUi.OptionCombo(new Dictionary<int, string> { [0] = "Normal", [1] = "Additive", [2] = "Multiply", [3] = "Screen" });

    public AnimationBatchDialog()
    {
        Text = "Batch Edit Animation Cells";
        StartPosition = FormStartPosition.CenterParent;
        Width = 540;
        Height = 600;
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Offset X", _x);
        Phase9BEditorUi.AddRow(table, 1, "Offset Y", _y);
        Phase9BEditorUi.AddRow(table, 2, "Scale Multiplier", _scale);
        Phase9BEditorUi.AddRow(table, 3, "Rotation Offset", _rotation);
        Phase9BEditorUi.AddRow(table, 4, "Opacity", Labeled(_setOpacity, _opacity));
        Phase9BEditorUi.AddRow(table, 5, "Mirror", _mirror);
        Phase9BEditorUi.AddRow(table, 6, "Pattern", Labeled(_replacePattern, _pattern));
        Phase9BEditorUi.AddRow(table, 7, "Blend", Labeled(_setBlend, _blend));
        Controls.Add(table);
        Controls.Add(Buttons());
    }

    public AnimationBatchEdit Edit => new(
        decimal.ToDouble(_x.Value),
        decimal.ToDouble(_y.Value),
        decimal.ToDouble(_scale.Value),
        decimal.ToDouble(_rotation.Value),
        _setOpacity.Checked ? decimal.ToDouble(_opacity.Value) : null,
        _mirror.Checked,
        _replacePattern.Checked ? decimal.ToInt32(_pattern.Value) : null,
        _setBlend.Checked ? Phase9BEditorUi.SelectedOption(_blend) : null);

    private static Control Labeled(Control first, Control second)
    {
        FlowLayoutPanel panel = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        panel.Controls.Add(first);
        second.Width = 130;
        panel.Controls.Add(second);
        return panel;
    }

    private static NumericUpDown Number(decimal min, decimal max, int decimals, decimal value) => new() { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = value, Width = 150 };
    private static Control Buttons()
    {
        FlowLayoutPanel panel = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.Add(new Button { Text = "Apply", AutoSize = true, DialogResult = DialogResult.OK });
        panel.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
        return panel;
    }
}

internal sealed class AnimationTimingDialog : Form
{
    private readonly MvProjectSession _session;
    private readonly NumericUpDown _frame;
    private readonly ComboBox _scope = Phase9BEditorUi.OptionCombo(new Dictionary<int, string> { [0] = "None", [1] = "Target", [2] = "Screen", [3] = "Hide Target" });
    private readonly NumericUpDown _red = Phase9BEditorUi.Number(-9999, 9999);
    private readonly NumericUpDown _green = Phase9BEditorUi.Number(-9999, 9999);
    private readonly NumericUpDown _blue = Phase9BEditorUi.Number(-9999, 9999);
    private readonly NumericUpDown _alpha = Phase9BEditorUi.Number(-9999, 9999);
    private readonly NumericUpDown _duration = Phase9BEditorUi.Number(-9999, 9999);
    private readonly AssetIndexSelectionControl _sound;
    private readonly NumericUpDown _volume = Phase9BEditorUi.Number(0, 100);
    private readonly NumericUpDown _pitch = Phase9BEditorUi.Number(1, 999);
    private readonly NumericUpDown _pan = Phase9BEditorUi.Number(-100, 100);
    private WindowsAudioPreviewSession? _preview;

    public AnimationTimingDialog(MvProjectSession session, MvAnimationTimingSnapshot timing, int frameCount)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Timing = timing ?? throw new ArgumentNullException(nameof(timing));
        _frame = Phase9BEditorUi.Number(1, Math.Max(1, frameCount));
        _sound = new AssetIndexSelectionControl(session, AssetCategory.SoundEffects, false);
        Text = "Animation Timing";
        StartPosition = FormStartPosition.CenterParent;
        Width = 590;
        Height = 680;
        _frame.Value = Math.Clamp(timing.Frame + 1, 1, Math.Max(1, frameCount));
        Phase9BEditorUi.SelectOption(_scope, timing.FlashScope);
        SetColor(timing.FlashColor);
        Phase9BEditorUi.Set(_duration, timing.FlashDuration);
        _sound.Selection = new AssetIndexSelection(timing.SoundEffect?.Name ?? string.Empty, 0);
        Phase9BEditorUi.Set(_volume, timing.SoundEffect?.Volume ?? 90);
        Phase9BEditorUi.Set(_pitch, timing.SoundEffect?.Pitch ?? 100);
        Phase9BEditorUi.Set(_pan, timing.SoundEffect?.Pan ?? 0);
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Frame", _frame);
        Phase9BEditorUi.AddRow(table, 1, "Flash Scope", _scope);
        Phase9BEditorUi.AddRow(table, 2, "Flash Red", _red);
        Phase9BEditorUi.AddRow(table, 3, "Flash Green", _green);
        Phase9BEditorUi.AddRow(table, 4, "Flash Blue", _blue);
        Phase9BEditorUi.AddRow(table, 5, "Flash Alpha", _alpha);
        Phase9BEditorUi.AddRow(table, 6, "Flash Duration", _duration);
        Phase9BEditorUi.AddRow(table, 7, "Sound Effect", _sound);
        Phase9BEditorUi.AddRow(table, 8, "Volume", _volume);
        Phase9BEditorUi.AddRow(table, 9, "Pitch", _pitch);
        Phase9BEditorUi.AddRow(table, 10, "Pan", _pan);
        FlowLayoutPanel previewButtons = new() { Dock = DockStyle.Fill, AutoSize = true };
        Button play = new() { Text = "Preview SE", AutoSize = true };
        Button stop = new() { Text = "Stop", AutoSize = true };
        play.Click += async (_, _) => await PreviewAsync().ConfigureAwait(true);
        stop.Click += (_, _) => _preview?.Stop();
        previewButtons.Controls.AddRange([play, stop]);
        Phase9BEditorUi.AddRow(table, 11, string.Empty, previewButtons);
        Controls.Add(table);
        Controls.Add(Buttons());
    }

    public MvAnimationTimingSnapshot Timing { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            MvAnimationAudioSnapshot? se = string.IsNullOrWhiteSpace(_sound.Selection.AssetName)
                ? null
                : new MvAnimationAudioSnapshot(_sound.Selection.AssetName, decimal.ToInt32(_volume.Value), decimal.ToInt32(_pitch.Value), decimal.ToInt32(_pan.Value), Timing.SoundEffect?.RawJson ?? "{}");
            Timing = Timing with
            {
                Frame = decimal.ToInt32(_frame.Value) - 1,
                FlashScope = Phase9BEditorUi.SelectedOption(_scope),
                FlashColor = [decimal.ToDouble(_red.Value), decimal.ToDouble(_green.Value), decimal.ToDouble(_blue.Value), decimal.ToDouble(_alpha.Value)],
                FlashDuration = decimal.ToInt32(_duration.Value),
                SoundEffect = se
            };
        }
        _preview?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _preview = null;
        base.OnFormClosing(e);
    }

    private async ValueTask PreviewAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sound.Selection.AssetName)) return;
            AssetRecord? record = _session.Assets.Find(new AssetKey(AssetCategory.SoundEffects, _sound.Selection.AssetName));
            AssetVariant? variant = record?.PreferredVariant;
            if (variant is null) return;
            if (_preview is not null) await _preview.DisposeAsync().ConfigureAwait(true);
            _preview = await Task.Run(() => new WindowsAudioPreviewSession(_session.AssetContent.GetFullPath(variant.RelativePath), variant.Extension)).ConfigureAwait(true);
            _preview.Volume = decimal.ToSingle(_volume.Value) / 100f;
            _preview.Play();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException)
        {
            Phase9BEditorUi.ShowError(this, exception);
        }
    }

    private void SetColor(IReadOnlyList<double> color)
    {
        Phase9BEditorUi.Set(_red, (int)Math.Round(color.ElementAtOrDefault(0)));
        Phase9BEditorUi.Set(_green, (int)Math.Round(color.ElementAtOrDefault(1)));
        Phase9BEditorUi.Set(_blue, (int)Math.Round(color.ElementAtOrDefault(2)));
        Phase9BEditorUi.Set(_alpha, (int)Math.Round(color.ElementAtOrDefault(3)));
    }

    private static Control Buttons()
    {
        FlowLayoutPanel panel = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        panel.Controls.Add(new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK });
        panel.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
        return panel;
    }
}

internal sealed class AnimationPreviewTargetDialog : Form
{
    private readonly NumericUpDown _x = Number(-10000, 10000);
    private readonly NumericUpDown _y = Number(-10000, 10000);
    private readonly NumericUpDown _width = Number(1, 10000);
    private readonly NumericUpDown _height = Number(1, 10000);
    private readonly NumericUpDown _logicalWidth = Number(1, 10000);
    private readonly NumericUpDown _logicalHeight = Number(1, 10000);
    private readonly CheckBox _mirror = new() { Text = "Mirror the animation preview", AutoSize = true };

    public AnimationPreviewTargetDialog(AnimationPreviewTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Text = "Animation Preview Target";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        Width = 520;
        Height = 500;
        Set(_x, target.X);
        Set(_y, target.Y);
        Set(_width, target.Width);
        Set(_height, target.Height);
        Set(_logicalWidth, target.LogicalWidth);
        Set(_logicalHeight, target.LogicalHeight);
        _mirror.Checked = target.MirrorAnimation;
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Target X", _x);
        Phase9BEditorUi.AddRow(table, 1, "Target Y", _y);
        Phase9BEditorUi.AddRow(table, 2, "Target Width", _width);
        Phase9BEditorUi.AddRow(table, 3, "Target Height", _height);
        Phase9BEditorUi.AddRow(table, 4, "Logical Width", _logicalWidth);
        Phase9BEditorUi.AddRow(table, 5, "Logical Height", _logicalHeight);
        Phase9BEditorUi.AddRow(table, 6, "Mirroring", _mirror);
        Controls.Add(table);
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(new Button { Text = "Apply", AutoSize = true, DialogResult = DialogResult.OK });
        buttons.Controls.Add(new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel });
        Controls.Add(buttons);
    }

    public AnimationPreviewTarget Target => new(
        decimal.ToDouble(_x.Value),
        decimal.ToDouble(_y.Value),
        decimal.ToDouble(_width.Value),
        decimal.ToDouble(_height.Value),
        decimal.ToInt32(_logicalWidth.Value),
        decimal.ToInt32(_logicalHeight.Value),
        _mirror.Checked);

    private static NumericUpDown Number(decimal minimum, decimal maximum) => new()
    {
        Dock = DockStyle.Fill,
        Minimum = minimum,
        Maximum = maximum,
        DecimalPlaces = 2,
        Increment = 1
    };

    private static void Set(NumericUpDown control, double value) =>
        control.Value = Math.Clamp((decimal)value, control.Minimum, control.Maximum);
}
