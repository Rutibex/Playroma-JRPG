using System.Drawing.Drawing2D;
using System.Globalization;
using Playroma.JRPG.Application;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Assets;
using Playroma.JRPG.Assets.Windows;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.RpgMakerMV;
using Playroma.JRPG.Workspaces;

namespace Playroma.JRPG.Editor.WinForms.Database;

public sealed class ActorEditorControl : UserControl, IDatabaseEditorBody, IDatabasePendingEditSource
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly DatabaseMutationService _mutations;
    private readonly ActorClassMutationService _actorClassMutations;
    private readonly IDatabaseEditorDefinition _definition;
    private readonly Label _selectionMessage = new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8),
        ForeColor = SystemColors.GrayText
    };
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _nickname = new() { Dock = DockStyle.Fill };
    private readonly TextBox _profile = new() { Dock = DockStyle.Fill, Multiline = true, Height = 78, ScrollBars = ScrollBars.Vertical, AccessibleName = "Actor profile" };
    private readonly TextBox _note = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, AcceptsReturn = true, AcceptsTab = true, AccessibleName = "Actor note" };
    private readonly DatabaseIdPicker _class;
    private readonly NumericUpDown _initialLevel = new() { Minimum = 1, Maximum = 99, Dock = DockStyle.Fill };
    private readonly NumericUpDown _maximumLevel = new() { Minimum = 1, Maximum = 99, Dock = DockStyle.Fill };
    private readonly AssetIndexSelectionControl _face;
    private readonly AssetIndexSelectionControl _character;
    private readonly AssetIndexSelectionControl _battler;
    private readonly DatabaseAssetPreviewPanel _facePreview;
    private readonly DatabaseAssetPreviewPanel _characterPreview;
    private readonly DatabaseAssetPreviewPanel _battlerPreview;
    private readonly ActorEquipmentEditor _equipment;
    private readonly TraitListEditor _traits = new();
    private readonly ErrorProvider _errors = new();
    private int _actorId;
    private Phase9BProjectCatalog _catalog = Phase9BProjectCatalog.Empty;
    private bool _loading;

    public ActorEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        Func<DocumentRequest, Task>? documentOpener = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _mutations = new DatabaseMutationService(session.Editing);
        _actorClassMutations = new ActorClassMutationService(session.Editing);
        _class = new DatabaseIdPicker(session, MvDocumentKind.Classes) { AllowNone = false, Dock = DockStyle.Fill };
        _face = new AssetIndexSelectionControl(session, AssetCategory.Faces, hasIndex: true,
            assetEditor: documentOpener is null ? null : (category, selection) => OpenSpriteEditorAsync(documentOpener, category, selection, "faceName")) { Dock = DockStyle.Fill };
        _character = new AssetIndexSelectionControl(session, AssetCategory.Characters, hasIndex: true,
            assetEditor: documentOpener is null ? null : (category, selection) => OpenSpriteEditorAsync(documentOpener, category, selection, "characterName")) { Dock = DockStyle.Fill };
        _battler = new AssetIndexSelectionControl(session, AssetCategory.SideViewActors, hasIndex: false,
            assetEditor: documentOpener is null ? null : (category, selection) => OpenSpriteEditorAsync(documentOpener, category, selection, "battlerName")) { Dock = DockStyle.Fill };
        _facePreview = new DatabaseAssetPreviewPanel(session, AssetCategory.Faces, DatabaseAssetPreviewMode.Face)
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Actor face preview"
        };
        _characterPreview = new DatabaseAssetPreviewPanel(session, AssetCategory.Characters, DatabaseAssetPreviewMode.Character)
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Actor character preview"
        };
        _battlerPreview = new DatabaseAssetPreviewPanel(session, AssetCategory.SideViewActors, DatabaseAssetPreviewMode.SideViewBattler)
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Actor side-view battler preview"
        };
        _equipment = new ActorEquipmentEditor(session, _actorClassMutations) { Dock = DockStyle.Fill };
        _equipment.PendingChangesChanged += (_, _) => PendingChangesChanged?.Invoke(this, EventArgs.Empty);
        _errors.ContainerControl = this;
        Dock = DockStyle.Fill;

        Controls.Add(BuildWorkspace());
        Controls.Add(_selectionMessage);
        AccessibilityMetadata.Name(this, "Actor editor", "Edit the selected actor's general settings, images, equipment, traits, profile, and note.", AccessibleRole.Pane);

        WireField(_name, "name");
        WireField(_nickname, "nickname");
        WireField(_profile, "profile");
        WireField(_note, "note");
        _class.ValueChanged += async (_, _) =>
        {
            if (!_loading) await CommitFieldAsync("classId", _class.SelectedId).ConfigureAwait(true);
        };
        _initialLevel.ValueChanged += async (_, _) =>
        {
            if (!_loading) await CommitLevelAsync(initialChanged: true).ConfigureAwait(true);
        };
        _maximumLevel.ValueChanged += async (_, _) =>
        {
            if (!_loading) await CommitLevelAsync(initialChanged: false).ConfigureAwait(true);
        };
        _face.SelectionChanged += async (_, _) =>
        {
            _facePreview.Selection = _face.Selection;
            await CommitGraphicAsync(
                "faceName", _face.Selection.AssetName, "faceIndex", _face.Selection.Index).ConfigureAwait(true);
        };
        _character.SelectionChanged += async (_, _) =>
        {
            _characterPreview.Selection = _character.Selection;
            await CommitGraphicAsync(
                "characterName", _character.Selection.AssetName, "characterIndex", _character.Selection.Index).ConfigureAwait(true);
        };
        _battler.SelectionChanged += async (_, _) =>
        {
            _battlerPreview.Selection = _battler.Selection;
            await CommitGraphicAsync(
                "battlerName", _battler.Selection.AssetName, null, 0).ConfigureAwait(true);
        };
    }

    public Control View => this;
    public bool OwnsTraits => true;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;
    public bool HasPendingChanges => _equipment.HasPendingChanges;
    public event EventHandler? PendingChangesChanged;

    public ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default) =>
        _equipment.ApplyPendingAsync(cancellationToken);

    public ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default) =>
        _equipment.DiscardPendingAsync(cancellationToken);

    public async ValueTask BindAsync(
        IReadOnlyList<DatabaseRecordHandle> records,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            if (records.Count != 1 || _document.Syntax.Resolve(records[0].Path) is not JsonObjectSyntax)
            {
                _actorId = 0;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0
                    ? "Select an actor to edit it."
                    : "The complete Actor editor requires one populated actor. Common batch fields remain available through the database toolbar.";
                _traits.ClearBinding();
                _equipment.ClearBinding();
                _face.Selection = new AssetIndexSelection(string.Empty, 0);
                _character.Selection = new AssetIndexSelection(string.Empty, 0);
                _battler.Selection = new AssetIndexSelection(string.Empty, 0);
                _facePreview.Selection = _face.Selection;
                _characterPreview.Selection = _character.Selection;
                _battlerPreview.Selection = _battler.Selection;
                return;
            }

            Enabled = true;
            _actorId = records[0].Id;
            _catalog = await Phase9BProjectCatalogLoader.LoadAsync(_session, cancellationToken).ConfigureAwait(true);
            _selectionMessage.Text = $"Actor {_actorId:D4} — edits are raw-backed, transactional, and undoable.";
            MvActorRecordSnapshot actor = ActorClassRecordReader.ReadActor(_document, _actorId);
            _name.Text = actor.Name;
            _nickname.Text = actor.Nickname;
            _profile.Text = actor.Profile;
            _note.Text = actor.Note;
            _name.Tag = _name.Text;
            _nickname.Tag = _nickname.Text;
            _profile.Tag = _profile.Text;
            _note.Tag = _note.Text;
            _class.SelectedId = actor.ClassId;
            _initialLevel.Value = Math.Clamp(actor.InitialLevel, 1, 99);
            _maximumLevel.Value = Math.Clamp(actor.MaximumLevel, 1, 99);
            _face.Selection = new AssetIndexSelection(actor.FaceName, actor.FaceIndex);
            _character.Selection = new AssetIndexSelection(actor.CharacterName, actor.CharacterIndex);
            _battler.Selection = new AssetIndexSelection(actor.BattlerName, 0);
            _facePreview.Selection = _face.Selection;
            _characterPreview.Selection = _character.Selection;
            _battlerPreview.Selection = _battler.Selection;
            JsonPath root = JsonPath.Root.Append(_actorId);
            if (_document.Syntax.Resolve(root.Append("traits")) is JsonArraySyntax)
            {
                _traits.Bind(_session, _document, root.Append("traits"), _catalog);
            }
            else
            {
                _traits.ClearBinding();
            }
            await _equipment.BindAsync(_document.RelativePath, _actorId, actor.EquipmentIds, cancellationToken).ConfigureAwait(true);
            ValidateLevels();
        }
        finally
        {
            _loading = false;
        }
    }

    private Task OpenSpriteEditorAsync(
        Func<DocumentRequest, Task> documentOpener,
        AssetCategory category,
        AssetIndexSelection selection,
        string propertyName)
    {
        if (string.IsNullOrWhiteSpace(selection.AssetName)) return Task.CompletedTask;
        string relativePath = AssetCategoryRegistry.BuildRuntimeRelativePath(new AssetKey(category, selection.AssetName), ".png");
        DocumentRequest request = new(
            EditorDocumentKind.ProjectFile,
            Identity: "sprite:" + relativePath,
            RelativePath: relativePath,
            DatabaseKind: MvDocumentKind.Actors,
            RecordId: _actorId,
            DisplayTitle: Path.GetFileName(relativePath),
            EventSurface: propertyName,
            EventPageIndex: selection.Index);
        return documentOpener(request);
    }

    private Control BuildWorkspace()
    {
        TableLayoutPanel workspace = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8),
            Height = 660,
            MinimumSize = new Size(920, 660)
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));

        TableLayoutPanel left = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 4, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        left.Controls.Add(BuildGeneralGroup(), 0, 0);
        left.Controls.Add(BuildImagesGroup(), 0, 1);
        left.Controls.Add(Group("Initial &Equipment", _equipment), 0, 2);

        TableLayoutPanel right = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(4, 0, 0, 0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        _traits.Dock = DockStyle.Fill;
        right.Controls.Add(Group("&Traits", _traits), 0, 0);
        right.Controls.Add(Group("&Note", _note), 0, 1);

        workspace.Controls.Add(left, 0, 0);
        workspace.Controls.Add(right, 1, 0);
        Panel scroll = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AutoScrollMinSize = new Size(920, 660),
            AccessibleName = "Actor editor workspace"
        };
        scroll.Controls.Add(workspace);
        scroll.Resize += (_, _) =>
        {
            int available = Math.Max(920, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            workspace.Width = available;
        };
        return scroll;
    }

    private Control BuildGeneralGroup()
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            Padding = new Padding(7)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddRow(table, 0, "&Name", _name, "Nic&kname", _nickname);
        AddRow(table, 1, "&Class", _class, "&Initial Level", _initialLevel);
        AddRow(table, 2, "&Maximum Level", _maximumLevel, string.Empty, new Panel { TabStop = false });
        AddWideRow(table, 3, "&Profile", _profile);
        return Group("&General Settings", table);
    }

    private Control BuildImagesGroup()
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(7)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        table.Controls.Add(LabeledAsset("&Face", _facePreview, _face), 0, 0);
        table.Controls.Add(LabeledAsset("&Character", _characterPreview, _character), 0, 1);
        table.Controls.Add(LabeledAsset("&Side-view Battler", _battlerPreview, _battler), 0, 2);
        return Group("&Images", table);
    }

    private static Control LabeledAsset(string label, Control preview, Control asset)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = new Padding(0, 0, 0, 6)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(AccessibilityMetadata.CreateLabel(label, asset), 0, 0);
        row.Controls.Add(preview, 1, 0);
        row.Controls.Add(asset, 2, 0);
        preview.Margin = new Padding(3, 3, 12, 3);
        asset.Margin = new Padding(3, 3, 3, 3);
        return row;
    }

    private static GroupBox Group(string title, Control content)
    {
        GroupBox group = new()
        {
            Text = title,
            Dock = DockStyle.Fill,
            Padding = new Padding(7),
            Margin = new Padding(3),
            AccessibleName = AccessibilityMetadata.StripMnemonic(title)
        };
        content.Dock = DockStyle.Fill;
        group.Controls.Add(content);
        return group;
    }

    private void WireField(TextBox control, string key)
    {
        control.Validated += async (_, _) =>
        {
            if (_loading || string.Equals(control.Tag as string, control.Text, StringComparison.Ordinal)) return;
            await CommitFieldAsync(key, control.Text).ConfigureAwait(true);
            control.Tag = control.Text;
        };
        control.KeyDown += async (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                await CommitFieldAsync(key, control.Text).ConfigureAwait(true);
                control.Tag = control.Text;
                e.SuppressKeyPress = true;
            }
        };
    }

    private async ValueTask CommitFieldAsync(string key, object value)
    {
        if (_loading || _actorId <= 0) return;
        try
        {
            DatabaseFieldDescriptor field = _definition.RecordSchema.FindField(key);
            await _mutations.SetFieldAsync(
                [new DatabaseRecordHandle(MvDocumentKind.Actors, _document.RelativePath, _actorId)],
                field,
                value,
                allowHistoryMerge: value is string).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Actor edit failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async ValueTask CommitLevelAsync(bool initialChanged)
    {
        if (_loading || _actorId <= 0) return;
        if (!ValidateLevels()) return;
        await CommitFieldAsync(
            initialChanged ? "initialLevel" : "maxLevel",
            initialChanged ? decimal.ToInt32(_initialLevel.Value) : decimal.ToInt32(_maximumLevel.Value)).ConfigureAwait(true);
    }

    private bool ValidateLevels()
    {
        bool valid = _initialLevel.Value <= _maximumLevel.Value;
        _errors.SetError(_initialLevel, valid ? string.Empty : "Initial level cannot exceed maximum level.");
        _errors.SetError(_maximumLevel, valid ? string.Empty : "Maximum level cannot be below initial level.");
        return valid;
    }

    private async ValueTask CommitGraphicAsync(
        string nameProperty,
        string assetName,
        string? indexProperty,
        int index)
    {
        if (_loading || _actorId <= 0) return;
        try
        {
            await _actorClassMutations.SetActorGraphicsAsync(
                _document.RelativePath,
                _actorId,
                nameProperty,
                assetName,
                indexProperty,
                index).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Graphic update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void AddRow(
        TableLayoutPanel table,
        int row,
        string label1,
        Control control1,
        string label2,
        Control control2)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(AccessibilityMetadata.CreateLabel(label1, control1), 0, row);
        table.Controls.Add(control1, 1, row);
        table.Controls.Add(string.IsNullOrWhiteSpace(label2) ? new Label { AutoSize = true, TabStop = false } : AccessibilityMetadata.CreateLabel(label2, control2), 2, row);
        table.Controls.Add(control2, 3, row);
        control1.Margin = new Padding(3, 3, 12, 8);
        control2.Margin = new Padding(3, 3, 3, 8);
    }

    private static void AddWideRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(AccessibilityMetadata.CreateLabel(label, control), 0, row);
        table.Controls.Add(control, 1, row);
        table.SetColumnSpan(control, 3);
        control.Margin = new Padding(3, 3, 3, 8);
    }

}


internal enum DatabaseAssetPreviewMode
{
    Face,
    Character,
    SideViewBattler
}

internal sealed class DatabaseAssetPreviewPanel : UserControl
{
    private readonly MvProjectSession _session;
    private readonly AssetCategory _category;
    private readonly DatabaseAssetPreviewMode _mode;
    private readonly PreviewCanvas _canvas = new();
    private readonly Label _caption = new()
    {
        Dock = DockStyle.Bottom,
        Height = 18,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.GrayText,
        AutoEllipsis = true
    };

    private CancellationTokenSource? _loadCancellation;
    private Bitmap? _bitmap;
    private string _loadedAssetName = string.Empty;
    private string _status = "No image";
    private AssetIndexSelection _selection = new(string.Empty, 0);

    public DatabaseAssetPreviewPanel(MvProjectSession session, AssetCategory category, DatabaseAssetPreviewMode mode)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _category = category;
        _mode = mode;
        MinimumSize = new Size(108, 84);
        Size = new Size(108, 84);
        Padding = new Padding(0);
        Margin = new Padding(3);
        Controls.Add(_canvas);
        Controls.Add(_caption);
        _canvas.Paint += PaintPreview;
        _canvas.Dock = DockStyle.Fill;
        UpdateCaption();
    }

    public AssetIndexSelection Selection
    {
        get => _selection;
        set
        {
            _selection = value ?? new AssetIndexSelection(string.Empty, 0);
            UpdateCaption();
            _ = RefreshAsync();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelLoad();
            DisposeBitmap();
        }

        base.Dispose(disposing);
    }

    private async Task RefreshAsync()
    {
        if (IsDisposed) return;
        if (string.IsNullOrWhiteSpace(_selection.AssetName))
        {
            _status = _mode switch
            {
                DatabaseAssetPreviewMode.Face => "No face",
                DatabaseAssetPreviewMode.Character => "No character",
                _ => "No battler"
            };
            CancelLoad();
            DisposeBitmap();
            _loadedAssetName = string.Empty;
            _canvas.Invalidate();
            return;
        }

        if (_bitmap is not null && string.Equals(_loadedAssetName, _selection.AssetName, StringComparison.OrdinalIgnoreCase))
        {
            _status = string.Empty;
            _canvas.Invalidate();
            return;
        }

        AssetRecord? record = _session.Assets.Find(new AssetKey(_category, _selection.AssetName));
        AssetVariant? variant = record?.PreferredVariant;
        if (record is null || variant is null)
        {
            _status = "Missing image";
            DisposeBitmap();
            _loadedAssetName = string.Empty;
            _canvas.Invalidate();
            return;
        }
        if (variant.IsEncrypted)
        {
            _status = "Encrypted image";
            DisposeBitmap();
            _loadedAssetName = string.Empty;
            _canvas.Invalidate();
            return;
        }

        CancelLoad();
        _status = "Loading…";
        _canvas.Invalidate();
        _loadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellation.Token;
        try
        {
            Bitmap bitmap = await AssetImageLoader.LoadOwnedBitmapAsync(
                _session.AssetContent,
                variant.RelativePath,
                cancellationToken).ConfigureAwait(true);
            if (IsDisposed || cancellationToken.IsCancellationRequested)
            {
                bitmap.Dispose();
                return;
            }

            DisposeBitmap();
            _bitmap = bitmap;
            _loadedAssetName = _selection.AssetName;
            _status = string.Empty;
            _canvas.Invalidate();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            DisposeBitmap();
            _loadedAssetName = string.Empty;
            _status = exception.Message;
            _canvas.Invalidate();
        }
    }

    private void PaintPreview(object? sender, PaintEventArgs e)
    {
        Rectangle bounds = _canvas.ClientRectangle;
        using HatchBrush brush = new(HatchStyle.LargeCheckerBoard, SystemColors.ControlLight, SystemColors.Window);
        e.Graphics.FillRectangle(brush, bounds);

        if (_bitmap is not null && TryGetSourceRectangle(_bitmap, out Rectangle source))
        {
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.SmoothingMode = SmoothingMode.None;
            Rectangle available = Rectangle.Inflate(bounds, -4, -4);
            float scale = Math.Min(available.Width / (float)source.Width, available.Height / (float)source.Height);
            if (scale <= 0f) return;
            int width = Math.Max(1, (int)Math.Round(source.Width * scale));
            int height = Math.Max(1, (int)Math.Round(source.Height * scale));
            Rectangle destination = new(
                bounds.X + (bounds.Width - width) / 2,
                bounds.Y + (bounds.Height - height) / 2,
                width,
                height);
            e.Graphics.DrawImage(_bitmap, destination, source, GraphicsUnit.Pixel);
            return;
        }

        TextRenderer.DrawText(
            e.Graphics,
            _status,
            Font,
            bounds,
            SystemColors.ControlText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }

    private bool TryGetSourceRectangle(Bitmap bitmap, out Rectangle source)
    {
        source = Rectangle.Empty;
        switch (_mode)
        {
            case DatabaseAssetPreviewMode.Face:
            {
                GridSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeFaces(bitmap.Width, bitmap.Height);
                if (!descriptor.IsValid || descriptor.Cells.Count == 0) return false;
                SheetCell cell = descriptor.Cells[Math.Clamp(_selection.Index, 0, descriptor.Cells.Count - 1)];
                source = new Rectangle(cell.X, cell.Y, cell.Width, cell.Height);
                return true;
            }
            case DatabaseAssetPreviewMode.Character:
            {
                CharacterSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeCharacter(_selection.AssetName, bitmap.Width, bitmap.Height);
                if (!descriptor.IsValid) return false;
                int characterCount = descriptor.CharacterColumns * descriptor.CharacterRows;
                int index = Math.Clamp(_selection.Index, 0, Math.Max(0, characterCount - 1));
                int characterColumn = index % descriptor.CharacterColumns;
                int characterRow = index / descriptor.CharacterColumns;
                int x = (characterColumn * descriptor.FramesPerCharacterX + 1) * descriptor.CellWidth;
                int y = characterRow * descriptor.FramesPerCharacterY * descriptor.CellHeight;
                source = new Rectangle(x, y, descriptor.CellWidth, descriptor.CellHeight);
                return source.Width > 0 && source.Height > 0;
            }
            case DatabaseAssetPreviewMode.SideViewBattler:
            {
                GridSheetDescriptor descriptor = MvSpriteSheetDescriptors.DescribeSideViewActor(bitmap.Width, bitmap.Height);
                if (!descriptor.IsValid || descriptor.Cells.Count == 0) return false;
                int waitMiddleIndex = Math.Min(descriptor.Cells.Count - 1, 4);
                SheetCell cell = descriptor.Cells[waitMiddleIndex];
                source = new Rectangle(cell.X, cell.Y, cell.Width, cell.Height);
                return true;
            }
            default:
                return false;
        }
    }

    private void UpdateCaption()
    {
        _caption.Text = string.IsNullOrWhiteSpace(_selection.AssetName)
            ? string.Empty
            : _mode == DatabaseAssetPreviewMode.SideViewBattler
                ? _selection.AssetName
                : $"{_selection.AssetName} [{_selection.Index}]";
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private void DisposeBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private sealed class PreviewCanvas : Panel
    {
        public PreviewCanvas()
        {
            DoubleBuffered = true;
            BorderStyle = BorderStyle.FixedSingle;
            ResizeRedraw = true;
            BackColor = SystemColors.Window;
        }
    }
}

public sealed class ActorEquipmentEditor : UserControl, IDatabasePendingEditSource
{
    private static readonly string[] FallbackSlotNames =
        ["Weapon", "Shield", "Head", "Body", "Accessory"];

    private readonly MvProjectSession _session;
    private readonly ActorClassMutationService _mutations;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.CellSelect
    };
    private readonly Button _apply = new() { Text = "Apply Equipment", AutoSize = true };
    private readonly Button _reload = new() { Text = "Reload", AutoSize = true };
    private readonly Button _report = new() { Text = "Equipment Report…", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Padding = new Padding(8, 6, 8, 6) };
    private string? _relativePath;
    private int _actorId;
    private IReadOnlyList<int> _equipment = [];
    private bool _loading;
    private bool _pending;

    public ActorEquipmentEditor(MvProjectSession session, ActorClassMutationService mutations)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Slot", HeaderText = "Slot", ReadOnly = true, FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Item", HeaderText = "Initial Equipment", FillWeight = 70 });
        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6), WrapContents = false };
        toolbar.Controls.AddRange([_apply, _reload, _report, _status]);
        Controls.Add(_grid);
        Controls.Add(toolbar);
        _apply.Click += async (_, _) => await ApplyAsync().ConfigureAwait(true);
        _reload.Click += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        _report.Click += async (_, _) => await ShowLegalityReportAsync().ConfigureAwait(true);
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) =>
        {
            if (!_loading && e.RowIndex >= 0 && e.ColumnIndex == 1) SetPending(true);
        };
    }

    public bool HasPendingChanges => _pending;
    public event EventHandler? PendingChangesChanged;

    public async ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ApplyAsync().ConfigureAwait(true);
    }

    public async ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    public async ValueTask BindAsync(
        string relativePath,
        int actorId,
        IReadOnlyList<int> equipment,
        CancellationToken cancellationToken = default)
    {
        _relativePath = relativePath;
        _actorId = actorId;
        _equipment = equipment.ToArray();
        await PopulateAsync(cancellationToken).ConfigureAwait(true);
        SetPending(false);
    }

    public void ClearBinding()
    {
        _relativePath = null;
        _actorId = 0;
        _equipment = [];
        _grid.Rows.Clear();
        SetPending(false);
        _status.Text = "No actor selected.";
    }

    private async ValueTask ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (_relativePath is null || _actorId <= 0) return;
        MvDatabaseFileSummary? actors = _session.Workspace.Snapshot.Database.Find(MvDocumentKind.Actors);
        if (actors is null) return;
        await using IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(actors.RelativePath, cancellationToken).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument document) return;
        _equipment = ActorClassRecordReader.ReadActor(document, _actorId).EquipmentIds;
        await PopulateAsync(cancellationToken).ConfigureAwait(true);
        SetPending(false);
    }

    private async ValueTask PopulateAsync(CancellationToken cancellationToken)
    {
        _loading = true;
        try
        {
            _grid.Rows.Clear();
            IReadOnlyList<EquipmentSlotDefinition> slots = await LoadSlotDefinitionsAsync(cancellationToken).ConfigureAwait(true);
            IReadOnlyList<DatabaseChoice> weapons = await DatabaseChoiceLoader.LoadAsync(
                _session, MvDocumentKind.Weapons, cancellationToken).ConfigureAwait(true);
            IReadOnlyList<DatabaseChoice> armors = await DatabaseChoiceLoader.LoadAsync(
                _session, MvDocumentKind.Armors, cancellationToken).ConfigureAwait(true);
            int count = Math.Max(_equipment.Count, Math.Max(1, slots.Count));
            for (int slot = 0; slot < count; slot++)
            {
                int selectedId = slot < _equipment.Count ? _equipment[slot] : 0;
                EquipmentSlotDefinition definition = slot < slots.Count
                    ? slots[slot]
                    : new EquipmentSlotDefinition(slot + 1, slot < FallbackSlotNames.Length ? FallbackSlotNames[slot] : $"Slot {slot + 1}");
                string slotName = definition.DisplayName;
                IReadOnlyList<DatabaseChoice> source = definition.EquipTypeId == 1 ? weapons : armors;
                List<DatabaseChoice> choices = [new DatabaseChoice(0, "None"), .. source];
                if (selectedId > 0 && choices.All(item => item.Id != selectedId))
                {
                    choices.Add(new DatabaseChoice(selectedId, $"(Missing record {selectedId})"));
                }
                int rowIndex = _grid.Rows.Add(slotName, null);
                DataGridViewComboBoxCell combo = new()
                {
                    DisplayMember = nameof(DatabaseChoice.DisplayName),
                    ValueMember = nameof(DatabaseChoice.Id),
                    DataSource = choices,
                    FlatStyle = FlatStyle.Flat
                };
                _grid.Rows[rowIndex].Cells[1] = combo;
                combo.Value = selectedId;
                _grid.Rows[rowIndex].Tag = slot;
            }
            int weaponSlots = slots.Count(item => item.EquipTypeId == 1);
            _status.Text = $"{count} equipment slot(s); {weaponSlots} weapon slot(s). Slot types reflect System.json and dual-wield traits.";
        }
        finally
        {
            _loading = false;
        }
    }

    private async ValueTask<IReadOnlyList<EquipmentSlotDefinition>> LoadSlotDefinitionsAsync(CancellationToken cancellationToken)
    {
        List<IProjectDocumentLease> leases = [];
        try
        {
            async ValueTask<MutableJsonProjectDocument?> OpenAsync(MvDocumentKind kind)
            {
                MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(kind);
                if (summary is null) return null;
                IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(
                    summary.RelativePath, cancellationToken).ConfigureAwait(true);
                leases.Add(lease);
                return lease.Document as MutableJsonProjectDocument;
            }

            MutableJsonProjectDocument? actors = await OpenAsync(MvDocumentKind.Actors).ConfigureAwait(true);
            MutableJsonProjectDocument? classes = await OpenAsync(MvDocumentKind.Classes).ConfigureAwait(true);
            MutableJsonProjectDocument? system = await OpenAsync(MvDocumentKind.System).ConfigureAwait(true);
            if (actors is not null && classes is not null)
            {
                ActorEquipmentLegalityReport report = ActorEquipmentLegalityAnalyzer.Analyze(
                    actors, classes, system, weapons: null, armors: null, actorId: _actorId);
                return report.Entries
                    .Select(entry => new EquipmentSlotDefinition(entry.ExpectedEquipTypeId, entry.ExpectedEquipTypeName))
                    .ToArray();
            }
        }
        finally
        {
            for (int index = leases.Count - 1; index >= 0; index--)
            {
                await leases[index].DisposeAsync().ConfigureAwait(true);
            }
        }

        return FallbackSlotNames
            .Select((name, index) => new EquipmentSlotDefinition(index + 1, name))
            .ToArray();
    }



    private async ValueTask ShowLegalityReportAsync()
    {
        if (_relativePath is null || _actorId <= 0) return;
        List<IProjectDocumentLease> leases = [];
        try
        {
            async ValueTask<MutableJsonProjectDocument?> OpenAsync(MvDocumentKind kind)
            {
                MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(kind);
                if (summary is null) return null;
                IProjectDocumentLease lease = await _session.Workspace.AcquireDocumentAsync(summary.RelativePath).ConfigureAwait(true);
                leases.Add(lease);
                return lease.Document as MutableJsonProjectDocument;
            }

            MutableJsonProjectDocument? actors = await OpenAsync(MvDocumentKind.Actors).ConfigureAwait(true);
            MutableJsonProjectDocument? classes = await OpenAsync(MvDocumentKind.Classes).ConfigureAwait(true);
            MutableJsonProjectDocument? system = await OpenAsync(MvDocumentKind.System).ConfigureAwait(true);
            MutableJsonProjectDocument? weapons = await OpenAsync(MvDocumentKind.Weapons).ConfigureAwait(true);
            MutableJsonProjectDocument? armors = await OpenAsync(MvDocumentKind.Armors).ConfigureAwait(true);
            if (actors is null || classes is null)
            {
                MessageBox.Show(this, "Actors.json or Classes.json is unavailable.", "Equipment report", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ActorEquipmentLegalityReport report = ActorEquipmentLegalityAnalyzer.Analyze(
                actors, classes, system, weapons, armors, _actorId);
            using EquipmentLegalityReportDialog dialog = new(report);
            dialog.ShowDialog(this);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Equipment report failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            for (int index = leases.Count - 1; index >= 0; index--)
            {
                await leases[index].DisposeAsync().ConfigureAwait(true);
            }
        }
    }

    private async ValueTask ApplyAsync()
    {
        if (_relativePath is null || _actorId <= 0) return;
        try
        {
            int[] equipment = new int[_grid.Rows.Count];
            for (int row = 0; row < _grid.Rows.Count; row++)
            {
                object? value = _grid.Rows[row].Cells[1].Value;
                equipment[row] = value is int id
                    ? id
                    : int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                        ? parsed
                        : 0;
            }
            await _mutations.SetActorEquipmentAsync(_relativePath, _actorId, equipment).ConfigureAwait(true);
            _equipment = equipment;
            SetPending(false);
            _status.Text = "Initial equipment applied as one undoable transaction.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Equipment update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetPending(bool value)
    {
        if (_pending == value) return;
        _pending = value;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record EquipmentSlotDefinition(int EquipTypeId, string DisplayName);
}

public sealed record DatabaseChoice(int Id, string DisplayName)
{
    public override string ToString() => Id == 0 ? DisplayName : $"{Id:D4} {DisplayName}";
}

public static class DatabaseChoiceLoader
{
    public static async ValueTask<IReadOnlyList<DatabaseChoice>> LoadAsync(
        MvProjectSession session,
        MvDocumentKind kind,
        CancellationToken cancellationToken = default)
    {
        MvDatabaseFileSummary? summary = session.Workspace.Snapshot.Database.Find(kind);
        if (summary is null) return [];
        await using IProjectDocumentLease lease = await session.Workspace.AcquireDocumentAsync(
            summary.RelativePath,
            cancellationToken).ConfigureAwait(true);
        if (lease.Document is not MutableJsonProjectDocument document
            || document.Syntax.Root is not JsonArraySyntax array)
        {
            return [];
        }
        List<DatabaseChoice> result = [];
        for (int id = 1; id < array.Elements.Count; id++)
        {
            if (array.Elements[id] is not JsonObjectSyntax) continue;
            result.Add(new DatabaseChoice(id, document.GetString(JsonPath.Root.Append(id).Append("name")) ?? $"Record {id}"));
        }
        return result;
    }
}
