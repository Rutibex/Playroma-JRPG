using System.Drawing.Drawing2D;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Eventing;

namespace Playroma.JRPG.Editor.WinForms.Eventing;

/// <summary>
/// RPG Maker-style map event page settings editor. It can operate in the legacy explicit-Apply
/// mode or as a passive, live-bound editor inside EventEditorForm.
/// </summary>
public sealed class EventPageSettingsControl : UserControl
{
    private static readonly int[] AnimatedPatternCycle = [0, 1, 2, 1];

    private readonly CheckBox _switch1 = Check("Switch");
    private readonly NumericUpDown _switch1Id = Number(1, 999999);
    private readonly CheckBox _switch2 = Check("Switch");
    private readonly NumericUpDown _switch2Id = Number(1, 999999);
    private readonly CheckBox _variable = Check("Variable");
    private readonly NumericUpDown _variableId = Number(1, 999999);
    private readonly NumericUpDown _variableValue = Number(-99999999, 99999999);
    private readonly CheckBox _selfSwitch = Check("Self Switch");
    private readonly ComboBox _selfSwitchCh = Combo(["A", "B", "C", "D"], 76);
    private readonly CheckBox _item = Check("Item");
    private readonly NumericUpDown _itemId = Number(1, 999999);
    private readonly CheckBox _actor = Check("Actor");
    private readonly NumericUpDown _actorId = Number(1, 999999);

    private readonly TextBox _character = new() { Width = 146, PlaceholderText = "Character sheet" };
    private readonly AssetIndexSelectionControl? _characterPicker;
    private readonly NumericUpDown _characterIndex = Number(0, 7);
    private readonly NumericUpDown _tileId = Number(0, 999999);
    private readonly ComboBox _direction = Combo(["Down", "Left", "Right", "Up"], 100);
    private readonly NumericUpDown _pattern = Number(0, 2);
    private readonly Panel _graphicPreview = new() { Size = new Size(86, 86), BorderStyle = BorderStyle.FixedSingle };

    private readonly ComboBox _moveType = Combo(["Fixed", "Random", "Approach", "Custom"], 150);
    private readonly ComboBox _speed = Combo(["1: x8 Slower", "2: x4 Slower", "3: x2 Slower", "4: Normal", "5: x2 Faster", "6: x4 Faster"], 150);
    private readonly ComboBox _frequency = Combo(["1: Lowest", "2: Lower", "3: Normal", "4: Higher", "5: Highest"], 150);
    private readonly CheckBox _walk = Check("Walking");
    private readonly CheckBox _step = Check("Stepping");
    private readonly CheckBox _directionFix = Check("Direction Fix");
    private readonly CheckBox _through = Check("Through");
    private readonly ComboBox _priority = Combo(["Below characters", "Same as characters", "Above characters"], 180);
    private readonly ComboBox _trigger = Combo(["Action Button", "Player Touch", "Event Touch", "Autorun", "Parallel"], 180);
    private readonly Button _route = new() { Text = "Route…", AutoSize = true };
    private readonly Button _apply = new() { Text = "Apply Page Settings", AutoSize = true };
    private readonly EventCommandRegistry _registry;
    private readonly MvProjectSession? _project;
    private readonly System.Windows.Forms.Timer _previewAnimationTimer = new() { Interval = 180 };
    private EventPageSettingsSnapshot? _snapshot;
    private MovementRouteModel? _routeValue;
    private CancellationTokenSource? _previewLoadCancellation;
    private Bitmap? _previewBitmap;
    private string _previewAssetName = string.Empty;
    private string _previewMessage = "No Image";
    private int _previewPatternCycleIndex;
    private bool _loading;

    public EventPageSettingsControl(EventCommandRegistry registry, MvProjectSession? project = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _project = project;
        if (project is not null)
        {
            _characterPicker = new AssetIndexSelectionControl(project, AssetCategory.Characters, hasIndex: true)
            {
                Width = 188
            };
            _characterPicker.SelectionChanged += (_, _) =>
            {
                if (_characterPicker.Selection.AssetName.Length > 0) _tileId.Value = 0;
                RaiseSnapshotChanged();
            };
        }
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Padding = new Padding(6);

        // A top-down FlowLayoutPanel with AutoSize can collapse its children to their
        // minimum preferred width when hosted inside a SplitContainer (especially at
        // non-100% DPI).  The result is the one-character-wide labels seen in the
        // event editor.  A single-column table keeps every section bound to the
        // available viewport width while AutoScroll handles the vertical overflow.
        TableLayoutPanel stack = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        AddSection(stack, BuildConditionsGroup());
        AddSection(stack, BuildGraphicGroup());
        AddSection(stack, BuildMovementGroup());
        AddSection(stack, BuildOptionsGroup());
        AddSection(stack, BuildBehaviorGroup());
        _apply.Anchor = AnchorStyles.Left;
        _apply.Margin = new Padding(0, 2, 0, 8);
        stack.Controls.Add(_apply, 0, stack.Controls.Count);
        Controls.Add(stack);

        _route.Click += (_, _) => EditRoute();
        _apply.Click += (_, _) =>
        {
            if (_snapshot is not null) ApplyRequested?.Invoke(this, BuildSnapshot());
        };
        WireValueEvents(this);
        _graphicPreview.Paint += DrawGraphicPreview;
        _previewAnimationTimer.Tick += (_, _) => AdvancePreviewAnimation();
    }

    public bool ShowApplyButton
    {
        get => _apply.Visible;
        set => _apply.Visible = value;
    }

    public EventPageSettingsSnapshot? CurrentSnapshot => _snapshot is null ? null : BuildSnapshot();

    public event EventHandler<EventPageSettingsSnapshot>? ApplyRequested;
    public event EventHandler<EventPageSettingsSnapshot>? SnapshotChanged;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewAnimationTimer.Stop();
            _previewAnimationTimer.Dispose();
            CancelPreviewLoad();
            DisposePreviewBitmap();
        }

        base.Dispose(disposing);
    }

    public void LoadSnapshot(EventPageSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _loading = true;
        try
        {
            _snapshot = snapshot;
            _routeValue = snapshot.MoveRoute;
            _switch1.Checked = snapshot.Conditions.Switch1Valid;
            _switch1Id.Value = Clamp(_switch1Id, snapshot.Conditions.Switch1Id);
            _switch2.Checked = snapshot.Conditions.Switch2Valid;
            _switch2Id.Value = Clamp(_switch2Id, snapshot.Conditions.Switch2Id);
            _variable.Checked = snapshot.Conditions.VariableValid;
            _variableId.Value = Clamp(_variableId, snapshot.Conditions.VariableId);
            _variableValue.Value = Clamp(_variableValue, snapshot.Conditions.VariableValue);
            _selfSwitch.Checked = snapshot.Conditions.SelfSwitchValid;
            _selfSwitchCh.SelectedItem = snapshot.Conditions.SelfSwitch;
            if (_selfSwitchCh.SelectedIndex < 0) _selfSwitchCh.SelectedIndex = 0;
            _item.Checked = snapshot.Conditions.ItemValid;
            _itemId.Value = Clamp(_itemId, snapshot.Conditions.ItemId);
            _actor.Checked = snapshot.Conditions.ActorValid;
            _actorId.Value = Clamp(_actorId, snapshot.Conditions.ActorId);
            _character.Text = snapshot.Graphic.CharacterName;
            _characterIndex.Value = Clamp(_characterIndex, snapshot.Graphic.CharacterIndex);
            if (_characterPicker is not null)
            {
                _characterPicker.Selection = new AssetIndexSelection(
                    snapshot.Graphic.CharacterName,
                    snapshot.Graphic.CharacterIndex);
            }
            _tileId.Value = Clamp(_tileId, snapshot.Graphic.TileId);
            _direction.SelectedIndex = snapshot.Graphic.Direction switch { 4 => 1, 6 => 2, 8 => 3, _ => 0 };
            _pattern.Value = Clamp(_pattern, snapshot.Graphic.Pattern);
            _moveType.SelectedIndex = Math.Clamp(snapshot.MoveType, 0, _moveType.Items.Count - 1);
            _speed.SelectedIndex = Math.Clamp(snapshot.MoveSpeed - 1, 0, _speed.Items.Count - 1);
            _frequency.SelectedIndex = Math.Clamp(snapshot.MoveFrequency - 1, 0, _frequency.Items.Count - 1);
            _walk.Checked = snapshot.WalkAnimation;
            _step.Checked = snapshot.StepAnimation;
            _directionFix.Checked = snapshot.DirectionFix;
            _through.Checked = snapshot.Through;
            _priority.SelectedIndex = Math.Clamp(snapshot.PriorityType, 0, _priority.Items.Count - 1);
            _trigger.SelectedIndex = Math.Clamp(snapshot.Trigger, 0, _trigger.Items.Count - 1);
            UpdateEnabledStates();
            _graphicPreview.Invalidate();
        }
        finally
        {
            _loading = false;
        }

        QueueGraphicPreviewRefresh();
    }

    private GroupBox BuildConditionsGroup()
    {
        TableLayoutPanel table = NewTable(3);
        AddCondition(table, _switch1, _switch1Id, new Label());
        AddCondition(table, _switch2, _switch2Id, new Label());
        AddCondition(table, _variable, _variableId, Pair(new Label { Text = "≥", AutoSize = true, Margin = new Padding(5, 7, 5, 0) }, _variableValue));
        AddCondition(table, _selfSwitch, _selfSwitchCh, new Label());
        AddCondition(table, _item, _itemId, new Label());
        AddCondition(table, _actor, _actorId, new Label());
        return Group("Conditions", table);
    }

    private Control BuildGraphicGroup()
    {
        TableLayoutPanel graphic = NewTable(2);
        graphic.ColumnStyles.Clear();
        graphic.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        graphic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _graphicPreview.BackColor = SystemColors.Window;
        _graphicPreview.Margin = new Padding(3, 3, 12, 3);
        graphic.Controls.Add(_graphicPreview, 0, 0);
        graphic.SetRowSpan(_graphicPreview, 4);

        Control character = (Control?)_characterPicker ?? _character;
        character.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        graphic.Controls.Add(character, 1, 0);
        if (_characterPicker is null)
        {
            graphic.Controls.Add(Pair(new Label { Text = "Index", AutoSize = true, Margin = new Padding(0, 7, 3, 0) }, _characterIndex), 1, 1);
        }
        graphic.Controls.Add(Pair(new Label { Text = "Tile", AutoSize = true, Margin = new Padding(0, 7, 3, 0) }, _tileId), 1, 2);
        graphic.Controls.Add(Pair(_direction, _pattern), 1, 3);
        return Group("Image", graphic);
    }

    private Control BuildMovementGroup()
    {
        TableLayoutPanel movement = NewTable(2);
        movement.ColumnStyles.Clear();
        movement.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        movement.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        AddLabelValue(movement, "Type", _moveType);
        AddLabelValue(movement, "Speed", _speed);
        AddLabelValue(movement, "Freq.", _frequency);
        AddLabelValue(movement, string.Empty, _route);
        return Group("Autonomous Movement", movement);
    }

    private Control BuildOptionsGroup()
    {
        FlowLayoutPanel options = new()
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        options.Controls.AddRange([_walk, _step, _directionFix, _through]);
        return Group("Options", options);
    }

    private Control BuildBehaviorGroup()
    {
        TableLayoutPanel behavior = NewTable(2);
        behavior.ColumnStyles.Clear();
        behavior.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        behavior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        AddLabelValue(behavior, "Priority", _priority);
        AddLabelValue(behavior, "Trigger", _trigger);
        return Group("Behavior", behavior);
    }

    private EventPageSettingsSnapshot BuildSnapshot()
    {
        EventPageSettingsSnapshot source = _snapshot
            ?? throw new InvalidOperationException("No event page is loaded.");
        AssetIndexSelection selectedCharacter = _characterPicker?.Selection
            ?? new AssetIndexSelection(_character.Text, (int)_characterIndex.Value);
        bool usesTile = _tileId.Value > 0;
        return source with
        {
            Conditions = new EventPageConditionModel(
                _switch1.Checked, (int)_switch1Id.Value,
                _switch2.Checked, (int)_switch2Id.Value,
                _variable.Checked, (int)_variableId.Value, (int)_variableValue.Value,
                _selfSwitch.Checked, _selfSwitchCh.Text,
                _item.Checked, (int)_itemId.Value,
                _actor.Checked, (int)_actorId.Value),
            Graphic = source.Graphic with
            {
                TileId = (int)_tileId.Value,
                CharacterName = usesTile ? string.Empty : selectedCharacter.AssetName,
                CharacterIndex = usesTile ? 0 : selectedCharacter.Index,
                Direction = _direction.SelectedIndex switch { 1 => 4, 2 => 6, 3 => 8, _ => 2 },
                Pattern = (int)_pattern.Value
            },
            MoveType = Math.Max(0, _moveType.SelectedIndex),
            MoveSpeed = Math.Max(1, _speed.SelectedIndex + 1),
            MoveFrequency = Math.Max(1, _frequency.SelectedIndex + 1),
            MoveRoute = _routeValue ?? source.MoveRoute,
            WalkAnimation = _walk.Checked,
            StepAnimation = _step.Checked,
            DirectionFix = _directionFix.Checked,
            Through = _through.Checked,
            PriorityType = Math.Max(0, _priority.SelectedIndex),
            Trigger = Math.Max(0, _trigger.SelectedIndex)
        };
    }

    private void EditRoute()
    {
        using MovementRouteEditorDialog dialog = new(_registry, _routeValue ?? MovementRouteModel.CreateDefault());
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _routeValue = dialog.Result;
        RaiseSnapshotChanged();
    }

    private void WireValueEvents(Control root)
    {
        foreach (Control control in root.Controls)
        {
            WireValueEvents(control);
            switch (control)
            {
                case CheckBox box:
                    box.CheckedChanged += (_, _) => RaiseSnapshotChanged();
                    break;
                case NumericUpDown number:
                    number.ValueChanged += (_, _) => RaiseSnapshotChanged();
                    break;
                case ComboBox combo:
                    combo.SelectedIndexChanged += (_, _) => RaiseSnapshotChanged();
                    break;
                case TextBox text:
                    text.TextChanged += (_, _) => RaiseSnapshotChanged();
                    break;
            }
        }
    }

    private void RaiseSnapshotChanged()
    {
        UpdateEnabledStates();
        QueueGraphicPreviewRefresh();
        if (_loading || _snapshot is null) return;
        SnapshotChanged?.Invoke(this, BuildSnapshot());
    }

    private void UpdateEnabledStates()
    {
        _switch1Id.Enabled = _switch1.Checked;
        _switch2Id.Enabled = _switch2.Checked;
        _variableId.Enabled = _variable.Checked;
        _variableValue.Enabled = _variable.Checked;
        _selfSwitchCh.Enabled = _selfSwitch.Checked;
        _itemId.Enabled = _item.Checked;
        _actorId.Enabled = _actor.Checked;
        _route.Enabled = _moveType.SelectedIndex == 3;
        UpdateAnimationState();
    }

    private void QueueGraphicPreviewRefresh()
    {
        _graphicPreview.Invalidate();
        _ = RefreshGraphicPreviewAsync();
    }

    private async Task RefreshGraphicPreviewAsync()
    {
        if (IsDisposed) return;

        AssetIndexSelection selection = _characterPicker?.Selection
            ?? new AssetIndexSelection(_character.Text, (int)_characterIndex.Value);

        if (_tileId.Value > 0)
        {
            _previewMessage = $"Tile {(int)_tileId.Value}";
            CancelPreviewLoad();
            DisposePreviewBitmap();
            _previewAssetName = string.Empty;
            UpdateAnimationState();
            _graphicPreview.Invalidate();
            return;
        }

        if (_project is null || string.IsNullOrWhiteSpace(selection.AssetName))
        {
            _previewMessage = "No Image";
            CancelPreviewLoad();
            DisposePreviewBitmap();
            _previewAssetName = string.Empty;
            UpdateAnimationState();
            _graphicPreview.Invalidate();
            return;
        }

        if (_previewBitmap is not null && string.Equals(_previewAssetName, selection.AssetName, StringComparison.OrdinalIgnoreCase))
        {
            _previewMessage = string.Empty;
            UpdateAnimationState();
            _graphicPreview.Invalidate();
            return;
        }

        CancelPreviewLoad();
        _previewMessage = "Loading…";
        _graphicPreview.Invalidate();
        _previewLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _previewLoadCancellation.Token;

        AssetRecord? record = _project.Assets.Find(new AssetKey(AssetCategory.Characters, selection.AssetName));
        AssetVariant? variant = record?.PreferredVariant;
        if (record is null || variant is null)
        {
            DisposePreviewBitmap();
            _previewAssetName = string.Empty;
            _previewMessage = "Missing image";
            UpdateAnimationState();
            _graphicPreview.Invalidate();
            return;
        }
        if (variant.IsEncrypted)
        {
            DisposePreviewBitmap();
            _previewAssetName = string.Empty;
            _previewMessage = "Encrypted image";
            UpdateAnimationState();
            _graphicPreview.Invalidate();
            return;
        }

        try
        {
            Bitmap bitmap = await AssetImageLoader.LoadOwnedBitmapAsync(
                _project.AssetContent,
                variant.RelativePath,
                cancellationToken).ConfigureAwait(true);
            if (IsDisposed || cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            DisposePreviewBitmap();
            _previewBitmap = bitmap;
            _previewAssetName = selection.AssetName;
            _previewMessage = string.Empty;
            UpdateAnimationState();
            _graphicPreview.Invalidate();
        }
        catch (OperationCanceledException)
        {
            // A newer request replaced this preview load.
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            DisposePreviewBitmap();
            _previewAssetName = string.Empty;
            _previewMessage = exception.Message;
            UpdateAnimationState();
            _graphicPreview.Invalidate();
        }
    }

    private void DrawGraphicPreview(object? sender, PaintEventArgs e)
    {
        Rectangle bounds = _graphicPreview.ClientRectangle;
        using HatchBrush brush = new(HatchStyle.LargeCheckerBoard, SystemColors.ControlLight, SystemColors.Window);
        e.Graphics.FillRectangle(brush, bounds);

        if (_previewBitmap is not null && TryGetPreviewSourceRectangle(_previewBitmap, out Rectangle source))
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.SmoothingMode = SmoothingMode.None;
            int padding = 6;
            Rectangle available = Rectangle.Inflate(bounds, -padding, -padding);
            float scale = Math.Min(available.Width / (float)source.Width, available.Height / (float)source.Height);
            scale = Math.Max(scale, 1f);
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            Rectangle destination = new(
                bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (bounds.Height - height) / 2,
                width,
                height);
            e.Graphics.DrawImage(_previewBitmap, destination, source, GraphicsUnit.Pixel);
            return;
        }

        string text = _previewMessage;
        if (string.IsNullOrWhiteSpace(text))
        {
            AssetIndexSelection selectedCharacter = _characterPicker?.Selection
                ?? new AssetIndexSelection(_character.Text, (int)_characterIndex.Value);
            text = string.IsNullOrWhiteSpace(selectedCharacter.AssetName) ? "No Image" : $"{selectedCharacter.AssetName}\n#{selectedCharacter.Index}";
        }

        TextRenderer.DrawText(e.Graphics, text, Font, bounds, SystemColors.ControlText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private bool TryGetPreviewSourceRectangle(Bitmap bitmap, out Rectangle source)
    {
        source = Rectangle.Empty;
        AssetIndexSelection selection = _characterPicker?.Selection
            ?? new AssetIndexSelection(_character.Text, (int)_characterIndex.Value);
        if (string.IsNullOrWhiteSpace(selection.AssetName)) return false;

        CharacterSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeCharacter(selection.AssetName, bitmap.Width, bitmap.Height);
        if (!descriptor.IsValid) return false;

        int characterCount = descriptor.CharacterColumns * descriptor.CharacterRows;
        int characterIndex = Math.Clamp(selection.Index, 0, Math.Max(0, characterCount - 1));
        int characterColumn = characterIndex % descriptor.CharacterColumns;
        int characterRow = characterIndex / descriptor.CharacterColumns;
        int directionRow = _direction.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 3, _ => 0 };
        int pattern = ShouldAnimatePreview()
            ? AnimatedPatternCycle[_previewPatternCycleIndex % AnimatedPatternCycle.Length]
            : (int)_pattern.Value;
        pattern = Math.Clamp(pattern, 0, 2);

        int frameWidth = descriptor.CellWidth;
        int frameHeight = descriptor.CellHeight;
        if (frameWidth <= 0 || frameHeight <= 0) return false;

        int x = (characterColumn * descriptor.FramesPerCharacterX + pattern) * frameWidth;
        int y = (characterRow * descriptor.FramesPerCharacterY + directionRow) * frameHeight;
        source = Rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height), new Rectangle(x, y, frameWidth, frameHeight));
        return source.Width > 0 && source.Height > 0;
    }

    private void AdvancePreviewAnimation()
    {
        if (!ShouldAnimatePreview())
        {
            UpdateAnimationState();
            return;
        }

        _previewPatternCycleIndex = (_previewPatternCycleIndex + 1) % AnimatedPatternCycle.Length;
        _graphicPreview.Invalidate();
    }

    private bool ShouldAnimatePreview() => _previewBitmap is not null && (_walk.Checked || _step.Checked) && _tileId.Value == 0;

    private void UpdateAnimationState()
    {
        bool animate = ShouldAnimatePreview();
        if (!animate)
        {
            _previewAnimationTimer.Stop();
            _previewPatternCycleIndex = 0;
            return;
        }

        if (!_previewAnimationTimer.Enabled)
        {
            _previewPatternCycleIndex = 0;
            _previewAnimationTimer.Start();
        }
    }

    private void CancelPreviewLoad()
    {
        _previewLoadCancellation?.Cancel();
        _previewLoadCancellation?.Dispose();
        _previewLoadCancellation = null;
    }

    private void DisposePreviewBitmap()
    {
        _previewBitmap?.Dispose();
        _previewBitmap = null;
    }

    private static GroupBox Group(string text, Control content)
    {
        GroupBox group = new()
        {
            Text = text,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(300, 0),
            Padding = new Padding(7),
            Margin = new Padding(0, 0, 0, 6)
        };
        content.Dock = DockStyle.Top;
        group.Controls.Add(content);
        return group;
    }

    private static void AddSection(TableLayoutPanel stack, Control section)
    {
        int row = stack.Controls.Count;
        section.Dock = DockStyle.Top;
        section.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        stack.Controls.Add(section, 0, row);
    }

    private static TableLayoutPanel NewTable(int columns)
    {
        TableLayoutPanel table = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = columns,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        for (int i = 0; i < columns; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return table;
    }

    private static void AddCondition(TableLayoutPanel table, Control check, Control value, Control suffix)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(check, 0, row);
        table.Controls.Add(value, 1, row);
        table.Controls.Add(suffix, 2, row);
    }

    private static void AddLabelValue(TableLayoutPanel table, string label, Control value)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 6, 0) }, 0, row);
        table.Controls.Add(value, 1, row);
    }

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 6, 3) };
    private static NumericUpDown Number(int min, int max) => new() { Minimum = min, Maximum = max, Width = 76 };
    private static ComboBox Combo(IEnumerable<string> values, int width)
    {
        ComboBox result = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
        result.Items.AddRange(values.Cast<object>().ToArray());
        result.SelectedIndex = result.Items.Count > 0 ? 0 : -1;
        return result;
    }
    private static Control Pair(Control first, Control second)
    {
        FlowLayoutPanel panel = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        panel.Controls.Add(first);
        panel.Controls.Add(second);
        return panel;
    }
    private static decimal Clamp(NumericUpDown control, int value) => Math.Clamp(value, (int)control.Minimum, (int)control.Maximum);
}
