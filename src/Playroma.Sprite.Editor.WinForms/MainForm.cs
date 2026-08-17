using Playroma.Sprite.Animation;
using Playroma.Sprite.Automation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;
using Playroma.Sprite.Resilience;

namespace Playroma.Sprite.Editor.WinForms;

public sealed class MainForm : Form
{
    private readonly SpriteEditorIntegrationOptions options;
    private readonly SpriteEditorSession session = new();
    private readonly RpgMakerMvProfileRegistry profiles;
    private readonly SpriteCanvasControl canvas = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel coordinateStatus = new("Ready");
    private readonly ToolStripStatusLabel documentStatus = new() { Spring = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly ComboBox profileBox = DropDown();
    private readonly ListBox validationList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private LayersPanel? layersPanel;
    private SelectionTransformPanel? selectionPanel;
    private AnimationTimelinePanel? timelinePanel;
    private AnimationInspectorPanel? animationInspector;
    private AnimationPreviewPanel? animationPreview;
    private ColorPalettePanel? colorPalettePanel;
    private EffectsPanel? effectsPanel;
    private SystemAssetEditorPanel? systemAssetPanel;
    private TilemapsSlicesPanel? tilemapsSlicesPanel;
    private TabPage? tilemapsSlicesPage;
    private AutomationPanel? automationPanel;
    private TabPage? automationPage;
    private ResiliencePanel? resiliencePanel;
    private TabPage? resiliencePage;
    private SplitContainer? workspaceSplit;
    private SplitContainer? canvasTimelineSplit;
    private readonly string settingsRoot;
    private readonly SpritePreferencesStore preferencesStore;
    private readonly SpriteWorkspaceProfileStore workspaceStore;
    private readonly SpriteRecoveryService recoveryService;
    private readonly SpriteEditorSessionLease sessionLease;
    private readonly ResilientBackgroundJobQueue backgroundJobs;
    private readonly SpritePerformanceMonitor performanceMonitor = new();
    private SpriteEditorPreferences preferences;
    private readonly HashSet<Guid> largeDocumentWarningsShown = [];
    private TabControl? inspectorTabs;
    private TabPage? systemAssetPage;
    private readonly Button foregroundButton = new() { Text = "Foreground", Height = 38, Dock = DockStyle.Fill };
    private readonly Button backgroundButton = new() { Text = "Background", Height = 38, Dock = DockStyle.Fill };
    private readonly NumericUpDown brushSize = Number(1, 128, 1);
    private readonly NumericUpDown opacityBox = Number(1, 255, 255);
    private readonly ComboBox brushShapeBox = DropDown();
    private readonly ComboBox inkModeBox = DropDown();
    private readonly ComboBox patternBox = DropDown();
    private readonly ComboBox patternAlignmentBox = DropDown();
    private readonly ComboBox ditherMatrixBox = DropDown();
    private readonly ComboBox presetBox = DropDown();
    private readonly CheckBox pixelPerfectBox = Check("Pixel-perfect freehand");
    private readonly CheckBox filledShapesBox = Check("Fill shapes and contours");
    private readonly CheckBox horizontalSymmetryBox = Check("Mirror left/right");
    private readonly CheckBox verticalSymmetryBox = Check("Mirror top/bottom");
    private readonly CheckBox tiledXBox = Check("Wrap X");
    private readonly CheckBox tiledYBox = Check("Wrap Y");
    private readonly CheckBox tiledPreviewBox = Check("Show 3×3 tiled preview");
    private readonly CheckBox pressureSizeBox = Check("Pressure affects size");
    private readonly CheckBox pressureOpacityBox = Check("Pressure affects opacity");
    private readonly CheckBox velocitySizeBox = Check("Velocity affects size");
    private readonly CheckBox velocityOpacityBox = Check("Velocity affects opacity");
    private readonly CheckBox contiguousFillBox = Check("Contiguous fill", true);
    private readonly CheckBox shadeLighterBox = Check("Shade toward lighter ramp color", true);
    private readonly CheckBox guidesBox = Check("Show custom guides", true);
    private readonly CheckBox snapBox = Check("Snap to custom grid");
    private readonly NumericUpDown symmetryXBox = Number(-4096, 4096, 0, 1);
    private readonly NumericUpDown symmetryYBox = Number(-4096, 4096, 0, 1);
    private readonly NumericUpDown guideWidthBox = Number(1, 2048, 16);
    private readonly NumericUpDown guideHeightBox = Number(1, 2048, 16);
    private readonly NumericUpDown guideOriginXBox = Number(-4096, 4096, 0);
    private readonly NumericUpDown guideOriginYBox = Number(-4096, 4096, 0);
    private readonly NumericUpDown fillToleranceBox = Number(0, 255, 0);
    private readonly NumericUpDown roundedRadiusBox = Number(0, 512, 4);
    private readonly NumericUpDown curveBendBox = Number(-200, 200, 35);
    private readonly NumericUpDown ditherAmountBox = Number(0, 100, 50);
    private readonly NumericUpDown minimumSizeBox = Number(1, 100, 20);
    private readonly NumericUpDown minimumOpacityBox = Number(1, 100, 15);
    private readonly NumericUpDown pressureCurveBox = Number(10, 800, 100, 10);
    private readonly NumericUpDown velocityCurveBox = Number(10, 800, 100, 10);
    private readonly System.Windows.Forms.Timer autosaveTimer = new() { Interval = 60_000 };
    private readonly Dictionary<PixelToolKind, ToolStripButton> toolButtons = [];
    private readonly List<BrushPreset> presets = [];
    private const int BuiltInPresetCount = 5;
    private BrushDefinition baseBrush = BrushDefinition.CreateBuiltIn(BrushShape.Square, 1);
    private bool closePrepared;
    private bool suppressProfileChange;
    private bool suppressSettings;
    private bool canvasOnlyMode;
    private bool inspectorMirrored;

    public MainForm() : this(new SpriteEditorIntegrationOptions()) { }

    public MainForm(SpriteEditorIntegrationOptions integrationOptions)
    {
        options = integrationOptions ?? throw new ArgumentNullException(nameof(integrationOptions));
        profiles = MvProjectProfileProvider.LoadForProject(options.ProjectRoot);
        settingsRoot = Path.GetFullPath(options.SettingsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playroma", "jRPG", "SpriteStudio"));
        preferencesStore = new SpritePreferencesStore(Path.Combine(settingsRoot, "preferences.json"));
        workspaceStore = new SpriteWorkspaceProfileStore(Path.Combine(settingsRoot, "Workspaces"));
        recoveryService = new SpriteRecoveryService(Path.Combine(settingsRoot, "Recovery"));
        preferences = preferencesStore.LoadOrDefault();
        backgroundJobs = new ResilientBackgroundJobQueue(preferences.BackgroundExportConcurrency);
        sessionLease = new SpriteEditorSessionLease(Path.Combine(settingsRoot, "active-session.json"));
        session.History.MemoryLimitBytes = preferences.UndoMemoryLimitBytes;
        autosaveTimer.Interval = checked(preferences.AutosaveSeconds * 1000);
        Text = "Playroma Sprite Studio";
        Name = nameof(MainForm);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1120, 720);
        Size = new Size(1540, 940);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        InitializeChoices();
        MainMenuStrip = BuildMenu();
        Controls.Add(BuildWorkspace());
        Controls.Add(BuildToolStrip());
        Controls.Add(MainMenuStrip);

        session.Changed += (_, _) => RefreshDocumentState();
        canvas.CompositeProvider = () => animationInspector?.OnionSettings.Enabled == true && session.Document.Frames.Count > 1
            ? OnionSkinRenderer.CompositeWithCurrent(session.Package, session.ActiveFrameIndex, animationInspector.OnionSettings, animationInspector.SelectedTag)
            : session.CompositeCurrentFrame(animationInspector?.OnionSettings.IncludeReferenceLayers == true);
        canvas.EditCommitted += (_, e) =>
        {
            if (e.Description.Equals("Move Selection", StringComparison.Ordinal)) session.CommitSelectionTransform(e.Description, e.Before, canvas.Selection.Clone());
            else session.CommitPixelEdit(e.Description, e.Before);
        };
        canvas.ColorPicked += (_, e) => { canvas.ForegroundColor = e.Color; UpdateColorButtons(); RebuildPattern(); };
        canvas.CursorChanged += (_, e) => coordinateStatus.Text = e.Inside ? $"X {e.X}, Y {e.Y}" : "Outside canvas";
        canvas.ViewChanged += (_, _) => RefreshDocumentState();
        canvas.SelectionChanged += (_, e) => session.SetSelection(e.Selection);
        profileBox.SelectedIndexChanged += (_, _) => ChangeProfile();
        foregroundButton.Click += (_, _) => PickColor(true);
        backgroundButton.Click += (_, _) => PickColor(false);
        HookSettingsEvents();
        autosaveTimer.Tick += async (_, _) => await AutosaveAsync();
        autosaveTimer.Start();
        FormClosing += MainForm_FormClosing;
        FormClosed += MainForm_FormClosed;
        DpiChanged += (_, _) => ApplyDpiPolish();
        Shown += async (_, _) =>
        {
            InitializeSplitterLayout();
            await InitializeAsync();
        };

        PopulateProfiles();
        UpdateColorButtons();
        BindDocument();
        UpdateCanvasSettings();
        ApplyPreferences(preferences);
        ApplyAccessibilityMetadata(this);
    }

    public bool HasUnsavedChanges => session.IsDirty;

    public void SaveIntegratedProject()
    {
        string? path = session.PackagePath ?? options.AuthoringProjectPath;
        if (string.IsNullOrWhiteSpace(path)) { SavePackageAs(); return; }
        session.SaveAsync(path).GetAwaiter().GetResult();
    }

    public bool PrepareForHostClose()
    {
        if (!ConfirmSaveChanges()) return false;
        if (!ConfirmBackgroundJobsCanClose()) return false;
        PersistWorkspaceAndPreferences();
        closePrepared = true;
        return true;
    }

    private async Task InitializeAsync()
    {
        try
        {
            ApplyWorkspaceProfile(workspaceStore.Load(preferences.ActiveWorkspaceId));
            MvSpriteEditorDeepLink? deepLink = options.DeepLink;
            string? initialImage = deepLink?.ImagePath ?? options.InitialImagePath;
            if (!string.IsNullOrWhiteSpace(initialImage) && File.Exists(initialImage))
            {
                string? profileId = deepLink?.ProfileId;
                RpgMakerMvImageProfile? profile = profileId is not null && profiles.TryGet(profileId, out RpgMakerMvImageProfile? requested) ? requested : null;
                if (profile is null)
                {
                    PngImageInfo info = PngHeaderReader.Read(initialImage);
                    string? relativeFolder = ResolveProjectRelativeFolder(initialImage);
                    profile = new MvAssetProfileDetector(profiles).DetectBest(new MvProfileDetectionInput(Path.GetFileName(initialImage), relativeFolder, info.Width, info.Height))?.Profile;
                }
                if (profile?.Grid is not null && deepLink?.CellIndex is int cellIndex)
                {
                    session.ImportMvSheet(initialImage, profile);
                    int targetFrame = ResolveDeepLinkFrame(profile, cellIndex);
                    if (targetFrame >= 0 && targetFrame < session.Document.Frames.Count) session.SetActiveFrame(targetFrame);
                }
                else
                {
                    session.ImportPng(initialImage, profile?.Id);
                }
                BindDocument();
                SelectProfile(profile?.Id);
                if (deepLink?.FrameIndex is int frameIndex && frameIndex >= 0 && frameIndex < session.Document.Frames.Count) session.SetActiveFrame(frameIndex);
                return;
            }
            if (!string.IsNullOrWhiteSpace(options.AuthoringProjectPath) && File.Exists(options.AuthoringProjectPath))
            {
                await session.OpenPackageAsync(options.AuthoringProjectPath); BindDocument();
            }
            else if (sessionLease.PreviousSessionWasUnclean)
            {
                await OfferCrashRecoveryAsync();
            }
        }
        catch (Exception exception) { ShowError("Sprite Studio could not open the document", exception); }
        finally { resiliencePanel?.RefreshAll(); }
    }

    private static int ResolveDeepLinkFrame(RpgMakerMvImageProfile profile, int cellIndex)
    {
        if (cellIndex < 0) return 0;
        if (profile.Id.Equals("mv-character-standard", StringComparison.OrdinalIgnoreCase))
        {
            int character = Math.Clamp(cellIndex, 0, 7);
            int blockColumn = character % 4;
            int blockRow = character / 4;
            return blockRow * 4 * 12 + blockColumn * 3;
        }
        if (profile.Id.Equals("mv-character-single", StringComparison.OrdinalIgnoreCase)) return 0;
        return cellIndex;
    }

    private void InitializeChoices()
    {
        brushShapeBox.Items.AddRange(Enum.GetNames<BrushShape>().Where(name => name != nameof(BrushShape.Custom)).Cast<object>().ToArray());
        brushShapeBox.Items.Add("Custom"); brushShapeBox.SelectedItem = nameof(BrushShape.Square);
        inkModeBox.Items.AddRange(Enum.GetNames<InkMode>().Cast<object>().ToArray()); inkModeBox.SelectedItem = nameof(InkMode.AlphaComposite);
        patternBox.Items.AddRange(["Solid Color", "Checker 2×2", "Bayer 4×4", "Diagonal", "Clipboard Pattern"]); patternBox.SelectedIndex = 0;
        patternAlignmentBox.Items.AddRange(Enum.GetNames<PatternAlignment>().Cast<object>().ToArray()); patternAlignmentBox.SelectedItem = nameof(PatternAlignment.Canvas);
        ditherMatrixBox.Items.AddRange(Enum.GetNames<DitherMatrixKind>().Cast<object>().ToArray()); ditherMatrixBox.SelectedItem = nameof(DitherMatrixKind.Bayer4x4);
        presets.AddRange([
            new BrushPreset("1px Pixel", BrushDefinition.CreateBuiltIn(BrushShape.Square, 1)),
            new BrushPreset("3px Circle", BrushDefinition.CreateBuiltIn(BrushShape.Circle, 3)),
            new BrushPreset("5px Diamond", BrushDefinition.CreateBuiltIn(BrushShape.Diamond, 5)),
            new BrushPreset("Horizontal 5", BrushDefinition.CreateBuiltIn(BrushShape.HorizontalLine, 5)),
            new BrushPreset("Vertical 5", BrushDefinition.CreateBuiltIn(BrushShape.VerticalLine, 5))
        ]);
        presets.AddRange(BrushPresetStore.Load(GetBrushPresetPath()));
        RefreshPresetBox();
    }

    private MenuStrip BuildMenu()
    {
        MenuStrip menu = new();
        ToolStripMenuItem file = new("&File");
        file.DropDownItems.Add("&New...", null, (_, _) => NewDocument());
        file.DropDownItems.Add("Open &PNG...", null, (_, _) => OpenPng());
        file.DropDownItems.Add("Import PNG Sequence...", null, (_, _) => ImportPngSequence());
        file.DropDownItems.Add("Import Animated GIF...", null, (_, _) => ImportGif());
        file.DropDownItems.Add("Open Playroma Sprite...", null, async (_, _) => await OpenPackageAsync());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Save", null, (_, _) => SaveIntegratedProject()) { ShortcutKeys = Keys.Control | Keys.S });
        file.DropDownItems.Add("Save &As...", null, (_, _) => SavePackageAs());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Export Current Frame PNG...", null, (_, _) => ExportPng());
        file.DropDownItems.Add("Export PNG Sequence...", null, (_, _) => ExportPngSequence());
        file.DropDownItems.Add("Export Animated GIF...", null, (_, _) => ExportGif());
        file.DropDownItems.Add("Export to RPG Maker MV Project...", null, async (_, _) => await ExportToMvProjectAsync());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => RequestClose());

        ToolStripMenuItem edit = new("&Edit");
        edit.DropDownItems.Add(new ToolStripMenuItem("&Undo", null, (_, _) => session.Undo()) { ShortcutKeys = Keys.Control | Keys.Z });
        edit.DropDownItems.Add(new ToolStripMenuItem("&Redo", null, (_, _) => session.Redo()) { ShortcutKeys = Keys.Control | Keys.Y });
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(new ToolStripMenuItem("Cu&t", null, (_, _) => selectionPanel?.Cut()) { ShortcutKeys = Keys.Control | Keys.X });
        edit.DropDownItems.Add(new ToolStripMenuItem("&Copy", null, (_, _) => selectionPanel?.Copy()) { ShortcutKeys = Keys.Control | Keys.C });
        edit.DropDownItems.Add(new ToolStripMenuItem("&Paste", null, (_, _) => selectionPanel?.Paste()) { ShortcutKeys = Keys.Control | Keys.V });
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Clear Active Layer", null, (_, _) => ClearCanvas());
        edit.DropDownItems.Add("Outline Opaque Pixels...", null, (_, _) => ApplyOutline());
        edit.DropDownItems.Add("Ordered Dither Entire Canvas", null, (_, _) => ApplyOrderedDither());
        edit.DropDownItems.Add("Replace Background with Foreground", null, (_, _) => ReplaceBackgroundColor());

        ToolStripMenuItem selectionMenu = new("&Selection");
        selectionMenu.DropDownItems.Add(new ToolStripMenuItem("Select &All", null, (_, _) => selectionPanel?.SelectAll()) { ShortcutKeys = Keys.Control | Keys.A });
        selectionMenu.DropDownItems.Add(new ToolStripMenuItem("Select &None", null, (_, _) => selectionPanel?.Deselect()) { ShortcutKeys = Keys.Control | Keys.D });
        selectionMenu.DropDownItems.Add(new ToolStripMenuItem("&Invert", null, (_, _) => selectionPanel?.Invert()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.I });
        selectionMenu.DropDownItems.Add("Select Opaque Pixels", null, (_, _) => selectionPanel?.SelectOpaque());
        selectionMenu.DropDownItems.Add("Feather Selection...", null, (_, _) => selectionPanel?.Feather());
        selectionMenu.DropDownItems.Add(new ToolStripSeparator());
        selectionMenu.DropDownItems.Add("Flip Horizontally", null, (_, _) => selectionPanel?.FlipHorizontal());
        selectionMenu.DropDownItems.Add("Flip Vertically", null, (_, _) => selectionPanel?.FlipVertical());
        selectionMenu.DropDownItems.Add("Rotate 90°", null, (_, _) => selectionPanel?.Rotate90());
        selectionMenu.DropDownItems.Add("Rotate 180°", null, (_, _) => selectionPanel?.Rotate180());
        selectionMenu.DropDownItems.Add("Rotate 270°", null, (_, _) => selectionPanel?.Rotate270());
        selectionMenu.DropDownItems.Add("Rotate Selection...", null, (_, _) => selectionPanel?.RotateArbitrary());
        selectionMenu.DropDownItems.Add("Scale Selection...", null, (_, _) => selectionPanel?.ScaleSelection());
        selectionMenu.DropDownItems.Add(new ToolStripSeparator());
        selectionMenu.DropDownItems.Add("Copy Selection to New Layer", null, (_, _) => TryLayerAction(() => session.MoveSelectionToNewLayer(false)));
        selectionMenu.DropDownItems.Add("Cut Selection to New Layer", null, (_, _) => TryLayerAction(() => session.MoveSelectionToNewLayer(true)));
        selectionMenu.DropDownItems.Add("Copy Merged Selection to New Layer", null, (_, _) => TryLayerAction(() => session.MoveSelectionToNewLayer(false, true)));

        ToolStripMenuItem animationMenu = new("&Animation");
        animationMenu.DropDownItems.Add("Add Empty Frame", null, (_, _) => session.AddFrame(FrameInsertionMode.Empty));
        animationMenu.DropDownItems.Add("Add Copied Frame", null, (_, _) => session.AddFrame(FrameInsertionMode.CopyIndependent));
        animationMenu.DropDownItems.Add("Add Linked Frame", null, (_, _) => session.AddFrame(FrameInsertionMode.CopyLinked));
        animationMenu.DropDownItems.Add("Duplicate Frame", null, (_, _) => session.DuplicateFrame());
        animationMenu.DropDownItems.Add("Delete Selected Frames", null, (_, _) => TryAnimationAction(() => session.RemoveFrames((timelinePanel?.SelectedFrameIndexes ?? [session.ActiveFrameIndex]).ToArray())));
        animationMenu.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem playPauseMenuItem = new("Play / Pause", null, (_, _) => timelinePanel?.TogglePlayback())
        {
            // ToolStripMenuItem rejects an unmodified Space key as a ShortcutKeys value.
            // ProcessCmdKey handles Space globally below, while this keeps the menu hint visible.
            ShortcutKeyDisplayString = "Space"
        };
        animationMenu.DropDownItems.Add(playPauseMenuItem);
        animationMenu.DropDownItems.Add("Previous Frame", null, (_, _) => timelinePanel?.PreviousFrame());
        animationMenu.DropDownItems.Add("Next Frame", null, (_, _) => timelinePanel?.NextFrame());
        animationMenu.DropDownItems.Add("Reverse Selected Range", null, (_, _) => ReverseSelectedFrames());
        animationMenu.DropDownItems.Add("Set Selected Frame Duration...", null, (_, _) => SetSelectedFrameDuration());
        animationMenu.DropDownItems.Add(new ToolStripSeparator());
        animationMenu.DropDownItems.Add("Link Selected Cels", null, (_, _) => TryAnimationAction(() => session.LinkCels((timelinePanel?.SelectedCels ?? []).ToArray())));
        animationMenu.DropDownItems.Add("Unlink Selected Cels", null, (_, _) => TryAnimationAction(() => session.UnlinkCels((timelinePanel?.SelectedCels ?? []).ToArray())));
        animationMenu.DropDownItems.Add("Clear Selected Cels", null, (_, _) => TryAnimationAction(() => session.ClearCels((timelinePanel?.SelectedCels ?? []).ToArray())));

        ToolStripMenuItem layerMenu = new("&Layer");
        layerMenu.DropDownItems.Add("New Image Layer", null, (_, _) => session.AddLayer(SpriteLayerKind.Image));
        layerMenu.DropDownItems.Add("New Background Layer", null, (_, _) => session.AddLayer(SpriteLayerKind.Background));
        layerMenu.DropDownItems.Add("New Group", null, (_, _) => session.AddLayer(SpriteLayerKind.Group));
        layerMenu.DropDownItems.Add("New Reference Layer", null, (_, _) => session.AddLayer(SpriteLayerKind.Reference));
        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        layerMenu.DropDownItems.Add("Duplicate Layer", null, (_, _) => session.DuplicateActiveLayer());
        layerMenu.DropDownItems.Add("Delete Layer", null, (_, _) => session.DeleteActiveLayer());
        layerMenu.DropDownItems.Add("Group Selected Layer", null, (_, _) => session.GroupActiveLayer());
        layerMenu.DropDownItems.Add("Raise Layer", null, (_, _) => session.MoveActiveLayer(1));
        layerMenu.DropDownItems.Add("Lower Layer", null, (_, _) => session.MoveActiveLayer(-1));
        layerMenu.DropDownItems.Add(new ToolStripSeparator());
        layerMenu.DropDownItems.Add("Merge Down", null, (_, _) => TryLayerAction(session.MergeDown));
        layerMenu.DropDownItems.Add("Flatten Image", null, (_, _) => session.Flatten());

        ToolStripMenuItem spriteMenu = new("S&prite");
        spriteMenu.DropDownItems.Add("Resize Sprite...", null, (_, _) => ResizeSprite());
        spriteMenu.DropDownItems.Add("Resize Canvas...", null, (_, _) => ResizeCanvas());
        spriteMenu.DropDownItems.Add("Crop to Selection", null, (_, _) => CropToSelection());
        spriteMenu.DropDownItems.Add("Trim Transparent Edges", null, (_, _) => TrimTransparent());

        ToolStripMenuItem brushMenu = new("&Brush");
        brushMenu.DropDownItems.Add("Design Custom Brush...", null, (_, _) => DesignCustomBrush());
        brushMenu.DropDownItems.Add("Capture Brush Mask from Clipboard", null, (_, _) => CaptureBrushFromClipboard());
        brushMenu.DropDownItems.Add("Capture Pattern from Clipboard", null, (_, _) => CapturePatternFromClipboard());
        brushMenu.DropDownItems.Add(new ToolStripSeparator());
        brushMenu.DropDownItems.Add("Rotate Brush 90°", null, (_, _) => TransformBrush(brush => brush.RotateClockwise()));
        brushMenu.DropDownItems.Add("Flip Brush Horizontally", null, (_, _) => TransformBrush(brush => brush.FlipHorizontal()));
        brushMenu.DropDownItems.Add("Flip Brush Vertically", null, (_, _) => TransformBrush(brush => brush.FlipVertical()));
        brushMenu.DropDownItems.Add(new ToolStripSeparator());
        brushMenu.DropDownItems.Add("Save Current Preset...", null, (_, _) => SaveCurrentPreset());

        ToolStripMenuItem view = new("&View");
        view.DropDownItems.Add(new ToolStripMenuItem("Zoom In", null, (_, _) => canvas.ZoomIn()) { ShortcutKeys = Keys.Control | Keys.Oemplus });
        view.DropDownItems.Add(new ToolStripMenuItem("Zoom Out", null, (_, _) => canvas.ZoomOut()) { ShortcutKeys = Keys.Control | Keys.OemMinus });
        ToolStripMenuItem pixelGrid = new("Pixel Grid") { Checked = canvas.ShowPixelGrid, CheckOnClick = true };
        pixelGrid.CheckedChanged += (_, _) => { canvas.ShowPixelGrid = pixelGrid.Checked; canvas.Invalidate(); };
        ToolStripMenuItem templateGrid = new("RPG Maker MV Template Grid") { Checked = canvas.ShowTemplateGrid, CheckOnClick = true };
        templateGrid.CheckedChanged += (_, _) => { canvas.ShowTemplateGrid = templateGrid.Checked; canvas.Invalidate(); };
        ToolStripMenuItem symmetryAxes = new("Symmetry Axes") { Checked = canvas.ShowSymmetryAxes, CheckOnClick = true };
        symmetryAxes.CheckedChanged += (_, _) => { canvas.ShowSymmetryAxes = symmetryAxes.Checked; canvas.Invalidate(); };
        ToolStripMenuItem selectionOutline = new("Selection Outline") { Checked = canvas.ShowSelection, CheckOnClick = true };
        selectionOutline.CheckedChanged += (_, _) => { canvas.ShowSelection = selectionOutline.Checked; canvas.Invalidate(); };
        view.DropDownItems.AddRange([pixelGrid, templateGrid, symmetryAxes, selectionOutline]);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add("Command Palette...", null, (_, _) => ShowCommandPalette());
        view.DropDownItems.Add("Canvas-Only Mode", null, (_, _) => ToggleCanvasOnlyMode());
        view.DropDownItems.Add("Resilience && Polish Workspace", null, (_, _) => OpenResilienceWorkspace());
        view.DropDownItems.Add("Save Current Workspace", null, (_, _) => SaveCurrentWorkspace());

        ToolStripMenuItem colorMenu = new("&Color");
        colorMenu.DropDownItems.Add("Convert to RGBA", null, (_, _) => TryColorAction(() => session.ConvertColorMode(SpriteColorMode.Rgba)));
        colorMenu.DropDownItems.Add("Convert to Indexed...", null, (_, _) => ConvertToIndexed());
        colorMenu.DropDownItems.Add("Convert to Grayscale", null, (_, _) => TryColorAction(() => session.ConvertColorMode(SpriteColorMode.Grayscale)));
        colorMenu.DropDownItems.Add(new ToolStripSeparator());
        colorMenu.DropDownItems.Add("Generate 16-Color Palette", null, (_, _) => TryColorAction(() => session.GeneratePalette("16-Color Palette", 16)));
        colorMenu.DropDownItems.Add("Generate 32-Color Palette", null, (_, _) => TryColorAction(() => session.GeneratePalette("32-Color Palette", 32)));
        colorMenu.DropDownItems.Add("Generate 256-Color Palette", null, (_, _) => TryColorAction(() => session.GeneratePalette("256-Color Palette", 256)));
        colorMenu.DropDownItems.Add("Apply Active Palette", null, (_, _) => TryColorAction(() => session.ApplyActivePalette(PaletteDitherMode.None)));
        colorMenu.DropDownItems.Add("Apply Active Palette with Floyd-Steinberg", null, (_, _) => TryColorAction(() => session.ApplyActivePalette(PaletteDitherMode.FloydSteinberg)));
        colorMenu.DropDownItems.Add(new ToolStripSeparator());
        colorMenu.DropDownItems.Add("Assign sRGB Profile", null, (_, _) => TryColorAction(() => session.AssignColorProfile("srgb")));
        colorMenu.DropDownItems.Add("Convert Pixels to sRGB", null, (_, _) => TryColorAction(() => session.ConvertColorProfile("srgb")));
        colorMenu.DropDownItems.Add("Convert Pixels to Linear sRGB", null, (_, _) => TryColorAction(() => session.ConvertColorProfile("linear-srgb")));
        colorMenu.DropDownItems.Add(new ToolStripSeparator());
        colorMenu.DropDownItems.Add("Sort Palette by Hue", null, (_, _) => TryColorAction(() => session.SortActivePalette(PaletteSortMode.Hue)));
        colorMenu.DropDownItems.Add("Sort Palette by Luminance", null, (_, _) => TryColorAction(() => session.SortActivePalette(PaletteSortMode.Luminance)));

        ToolStripMenuItem effectsMenu = new("E&ffects");
        effectsMenu.DropDownItems.Add("Invert Active Cel", null, (_, _) => ApplyQuickEffect("Invert", buffer => AdvancedPixelEffects.Invert(buffer, false, session.Selection)));
        effectsMenu.DropDownItems.Add("Desaturate Active Cel", null, (_, _) => ApplyQuickEffect("Desaturate", buffer => AdvancedPixelEffects.ConvertToGrayscale(buffer, GrayscaleMethod.Luminance, session.Selection)));
        effectsMenu.DropDownItems.Add("Blur Active Cel...", null, (_, _) => ApplyPromptedRadiusEffect("Gaussian Blur", radius => buffer => AdvancedPixelEffects.GaussianBlur(buffer, radius, session.Selection)));
        effectsMenu.DropDownItems.Add("Sharpen Active Cel", null, (_, _) => ApplyQuickEffect("Sharpen", buffer => AdvancedPixelEffects.Sharpen(buffer, 1d, session.Selection)));
        effectsMenu.DropDownItems.Add("Pixelate Active Cel...", null, (_, _) => ApplyPromptedRadiusEffect("Pixelate", radius => buffer => AdvancedPixelEffects.Pixelate(buffer, Math.Max(2, radius), session.Selection)));
        effectsMenu.DropDownItems.Add("Silhouette Active Cel", null, (_, _) => ApplyQuickEffect("Silhouette", buffer => AdvancedPixelEffects.Silhouette(buffer, canvas.ForegroundColor, session.Selection)));
        effectsMenu.DropDownItems.Add("Remove Isolated Pixels", null, (_, _) => ApplyQuickEffect("Remove Isolated Pixels", buffer => AdvancedPixelEffects.RemoveIsolatedPixels(buffer, 2, session.Selection)));

        ToolStripMenuItem tilemaps = new("&Tilemaps && Slices");
        tilemaps.DropDownItems.Add("Open Tilemaps / Slices Workspace", null, (_, _) => OpenTilemapsSlicesWorkspace());
        tilemaps.DropDownItems.Add("Import Reusable Tileset PNG...", null, (_, _) => tilemapsSlicesPanel?.PromptImportTileset());
        tilemaps.DropDownItems.Add("Add Tilemap Layer...", null, (_, _) => tilemapsSlicesPanel?.PromptAddTilemapLayer());
        tilemaps.DropDownItems.Add(new ToolStripSeparator());
        tilemaps.DropDownItems.Add("Add Slice from Selection", null, (_, _) => tilemapsSlicesPanel?.PromptAddSliceFromSelection());
        tilemaps.DropDownItems.Add("Export Slice Metadata...", null, (_, _) => tilemapsSlicesPanel?.ExportSliceMetadata());


        ToolStripMenuItem automation = new("&Automation");
        automation.DropDownItems.Add("Open Automation Workspace", null, (_, _) => OpenAutomationWorkspace());
        automation.DropDownItems.Add("Run Pipeline File...", null, (_, _) => automationPanel?.LoadPipelineFromDialog(this));
        automation.DropDownItems.Add("Run Script Console", null, (_, _) => { OpenAutomationWorkspace(); automationPanel?.FocusScriptEditor(); });
        automation.DropDownItems.Add(new ToolStripSeparator());
        automation.DropDownItems.Add("Export Power-of-Two Atlas...", null, (_, _) => automationPanel?.ExportAtlasFromDialog(this));

        ToolStripMenuItem mv = new("RPG Maker &MV");
        ToolStripMenuItem systemTemplates = new("Specialized System Asset Templates");
        foreach (MvSystemAssetDefinition definition in MvSystemAssetCatalog.Definitions)
        {
            MvSystemAssetDefinition captured = definition;
            systemTemplates.DropDownItems.Add(captured.DisplayName, null, (_, _) => CreateSystemAssetTemplate(captured));
        }
        mv.DropDownItems.Add("Open Specialized System Asset Editor", null, (_, _) => OpenSystemAssetEditor());
        mv.DropDownItems.Add(systemTemplates);
        mv.DropDownItems.Add(new ToolStripSeparator());
        mv.DropDownItems.Add("Auto-detect Asset Profile", null, (_, _) => AutoDetectMvProfile());
        mv.DropDownItems.Add("Validate Current Asset", null, (_, _) => ShowMvValidationReport());
        mv.DropDownItems.Add(new ToolStripSeparator());
        mv.DropDownItems.Add("Split Current Sheet into Frames", null, (_, _) => SplitMvSheetIntoFrames());
        mv.DropDownItems.Add("Assemble Frames into Sheet", null, (_, _) => AssembleFramesIntoMvSheet());
        mv.DropDownItems.Add("Export Assembled Sheet...", null, (_, _) => ExportAssembledMvSheet());
        mv.DropDownItems.Add(new ToolStripSeparator());
        mv.DropDownItems.Add("Install into RPG Maker MV Project...", null, async (_, _) => await InstallIntoMvProjectAsync());
        mv.DropDownItems.Add("Create Project Profile Override File", null, (_, _) => CreateProjectProfileOverride());

        ToolStripMenuItem help = new("&Help");
        help.DropDownItems.Add("Sprite Studio Features", null, (_, _) => MessageBox.Show(this,
            "Pixel drawing, animation, layers, selections, color modes, palettes, effects, RPG Maker MV profiles and system assets, tilemaps, slices, automation, export pipelines, recovery, diagnostics, accessibility options, and large-document safeguards are available.",
            "Playroma Sprite Studio", MessageBoxButtons.OK, MessageBoxIcon.Information));

        menu.Items.AddRange([file, edit, selectionMenu, animationMenu, layerMenu, spriteMenu, brushMenu, colorMenu, effectsMenu, tilemaps, automation, view, mv, help]);
        return menu;
    }

    private ToolStrip BuildToolStrip()
    {
        ToolStrip strip = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, ImageScalingSize = new Size(24, 24), CanOverflow = true };
        AddTool(strip, "Pencil", PixelToolKind.Pencil, true);
        AddTool(strip, "Eraser", PixelToolKind.Eraser);
        AddTool(strip, "Line", PixelToolKind.Line);
        AddTool(strip, "Curve", PixelToolKind.Curve);
        AddTool(strip, "Rect", PixelToolKind.Rectangle);
        AddTool(strip, "Round Rect", PixelToolKind.RoundedRectangle);
        AddTool(strip, "Ellipse", PixelToolKind.Ellipse);
        AddTool(strip, "Polygon", PixelToolKind.Polygon);
        AddTool(strip, "Contour", PixelToolKind.Contour);
        AddTool(strip, "Fill", PixelToolKind.Fill);
        AddTool(strip, "Gradient", PixelToolKind.Gradient);
        AddTool(strip, "Dither", PixelToolKind.Dither);
        AddTool(strip, "Pick", PixelToolKind.Eyedropper);
        strip.Items.Add(new ToolStripSeparator());
        AddTool(strip, "Select Rect", PixelToolKind.SelectionRectangle);
        AddTool(strip, "Select Ellipse", PixelToolKind.SelectionEllipse);
        AddTool(strip, "Select Polygon", PixelToolKind.SelectionPolygon);
        AddTool(strip, "Select Lasso", PixelToolKind.SelectionLasso);
        AddTool(strip, "Wand", PixelToolKind.MagicWand);
        AddTool(strip, "Move Selection", PixelToolKind.MoveSelection);
        strip.Items.Add(new ToolStripSeparator());
        ToolStripButton previousFrame = new("◀ Frame"); previousFrame.Click += (_, _) => timelinePanel?.PreviousFrame(); strip.Items.Add(previousFrame);
        ToolStripButton play = new("Play"); play.Click += (_, _) => timelinePanel?.TogglePlayback(); strip.Items.Add(play);
        ToolStripButton nextFrame = new("Frame ▶"); nextFrame.Click += (_, _) => timelinePanel?.NextFrame(); strip.Items.Add(nextFrame);
        ToolStripButton addFrame = new("+ Frame"); addFrame.Click += (_, _) => session.AddFrame(FrameInsertionMode.Empty); strip.Items.Add(addFrame);
        strip.Items.Add(new ToolStripSeparator());
        ToolStripButton undo = new("Undo"); undo.Click += (_, _) => session.Undo(); strip.Items.Add(undo);
        ToolStripButton redo = new("Redo"); redo.Click += (_, _) => session.Redo(); strip.Items.Add(redo);
        strip.Items.Add(new ToolStripSeparator());
        ToolStripButton zoomOut = new("−"); zoomOut.Click += (_, _) => canvas.ZoomOut(); strip.Items.Add(zoomOut);
        strip.Items.Add(new ToolStripLabel("Zoom"));
        ToolStripButton zoomIn = new("+"); zoomIn.Click += (_, _) => canvas.ZoomIn(); strip.Items.Add(zoomIn);
        return strip;
    }

    private void AddTool(ToolStrip strip, string text, PixelToolKind tool, bool selected = false)
    {
        ToolStripButton button = new(text) { CheckOnClick = true, Checked = selected, Tag = tool, Overflow = ToolStripItemOverflow.AsNeeded };
        button.Click += (_, _) => SelectTool(tool);
        toolButtons[tool] = button;
        strip.Items.Add(button);
    }

    private void SelectTool(PixelToolKind tool)
    {
        if (canvas.HasActivePolygon && tool != PixelToolKind.Polygon) canvas.CompletePolygon();
        foreach ((PixelToolKind kind, ToolStripButton button) in toolButtons) button.Checked = kind == tool;
        canvas.Tool = tool;
        coordinateStatus.Text = tool.ToString();
    }

    private Control BuildWorkspace()
    {
        StatusStrip status = new(); status.Items.Add(coordinateStatus); status.Items.Add(documentStatus);
        timelinePanel = new AnimationTimelinePanel(session);
        animationInspector = new AnimationInspectorPanel(session, timelinePanel);
        animationInspector.OnionSettingsChanged += (_, _) => canvas.Invalidate();
        animationInspector.ActiveTagChanged += (_, _) => canvas.Invalidate();

        TabControl tabs = new() { Dock = DockStyle.Fill };
        inspectorTabs = tabs;
        layersPanel = new LayersPanel(session); TabPage layersPage = new("Layers"); layersPage.Controls.Add(layersPanel); tabs.TabPages.Add(layersPage);
        TabPage animationPage = new("Animation"); animationPage.Controls.Add(animationInspector); tabs.TabPages.Add(animationPage);
        animationPreview = new AnimationPreviewPanel(session, animationInspector); TabPage previewPage = new("Preview"); previewPage.Controls.Add(animationPreview); tabs.TabPages.Add(previewPage);
        selectionPanel = new SelectionTransformPanel(session, canvas); TabPage selectionPage = new("Selection"); selectionPage.Controls.Add(selectionPanel); tabs.TabPages.Add(selectionPage);
        TabPage historyPage = new("History"); historyPage.Controls.Add(new HistoryPanel(session)); tabs.TabPages.Add(historyPage);
        tabs.TabPages.Add(BuildBrushTab());
        tabs.TabPages.Add(BuildDynamicsTab());
        tabs.TabPages.Add(BuildSymmetryTab());
        tabs.TabPages.Add(BuildGuidesTab());
        colorPalettePanel = new ColorPalettePanel(session); TabPage colorPage = new("Color & Palette"); colorPage.Controls.Add(colorPalettePanel); tabs.TabPages.Add(colorPage);
        effectsPanel = new EffectsPanel(session, () => timelinePanel?.SelectedFrameIndexes ?? [session.ActiveFrameIndex], () => canvas.ForegroundColor, () => canvas.BackgroundColor); TabPage effectsPage = new("Effects"); effectsPage.Controls.Add(effectsPanel); tabs.TabPages.Add(effectsPage);
        systemAssetPanel = new SystemAssetEditorPanel(session, profileId => SelectProfile(profileId)); systemAssetPage = new TabPage("MV System Assets"); systemAssetPage.Controls.Add(systemAssetPanel); tabs.TabPages.Add(systemAssetPage);
        tilemapsSlicesPanel = new TilemapsSlicesPanel(session, options.ProjectRoot, () => canvas.ForegroundColor, () => timelinePanel?.SelectedFrameIndexes ?? [session.ActiveFrameIndex]); tilemapsSlicesPage = new TabPage("Tilemaps & Slices"); tilemapsSlicesPage.Controls.Add(tilemapsSlicesPanel); tabs.TabPages.Add(tilemapsSlicesPage);
        automationPanel = new AutomationPanel(session, options.ProjectRoot, () => timelinePanel?.SelectedFrameIndexes ?? [session.ActiveFrameIndex]); automationPage = new TabPage("Automation"); automationPage.Controls.Add(automationPanel); tabs.TabPages.Add(automationPage);
        resiliencePanel = new ResiliencePanel(session, preferences, preferencesStore, workspaceStore, recoveryService, backgroundJobs, performanceMonitor, CaptureWorkspaceProfile, ApplyWorkspaceProfile, ApplyPreferences);
        resiliencePage = new TabPage("Resilience & Polish"); resiliencePage.Controls.Add(resiliencePanel); tabs.TabPages.Add(resiliencePage);
        TabPage validationPage = new("MV Validation"); validationPage.Padding = new Padding(8); validationPage.Controls.Add(validationList); tabs.TabPages.Add(validationPage);

        // SplitterDistance cannot be assigned safely until WinForms has laid out the controls.
        // At construction time a SplitContainer still has its small design-time size, so large
        // pixel distances (for example 600 or 1130) can throw InvalidOperationException on
        // high-DPI systems before the editor window is even shown.
        canvasTimelineSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2
        };
        canvasTimelineSplit.Panel1.Controls.Add(canvas); canvasTimelineSplit.Panel2.Controls.Add(timelinePanel);
        workspaceSplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
        workspaceSplit.Panel1.Controls.Add(canvasTimelineSplit); workspaceSplit.Panel2.Controls.Add(tabs);
        Panel root = new() { Dock = DockStyle.Fill }; root.Controls.Add(workspaceSplit); root.Controls.Add(status); status.Dock = DockStyle.Bottom;
        return root;
    }

    private TabPage BuildBrushTab()
    {
        TableLayoutPanel table = PropertyTable(); int row = 0;
        AddRow(table, ref row, "MV asset profile", profileBox);
        AddRow(table, ref row, "Brush preset", presetBox);
        AddRow(table, ref row, "Brush shape", brushShapeBox);
        AddRow(table, ref row, "Brush size", brushSize);
        AddRow(table, ref row, "Opacity", opacityBox);
        AddRow(table, ref row, "Ink mode", inkModeBox);
        AddRow(table, ref row, "Pattern", patternBox);
        AddRow(table, ref row, "Pattern alignment", patternAlignmentBox);
        AddRow(table, ref row, "Foreground", foregroundButton);
        AddRow(table, ref row, "Background", backgroundButton);
        AddFullRow(table, ref row, pixelPerfectBox);
        AddFullRow(table, ref row, filledShapesBox);
        AddRow(table, ref row, "Curve bend %", curveBendBox);
        AddRow(table, ref row, "Rounded radius", roundedRadiusBox);
        AddFullRow(table, ref row, contiguousFillBox);
        AddFullRow(table, ref row, shadeLighterBox);
        AddRow(table, ref row, "Fill tolerance", fillToleranceBox);
        AddRow(table, ref row, "Dither amount %", ditherAmountBox);
        AddRow(table, ref row, "Dither matrix", ditherMatrixBox);
        TabPage page = new("Brush"); page.Controls.Add(WrapScrollable(table)); return page;
    }

    private TabPage BuildDynamicsTab()
    {
        TableLayoutPanel table = PropertyTable(); int row = 0;
        AddFullRow(table, ref row, pressureSizeBox);
        AddFullRow(table, ref row, pressureOpacityBox);
        AddFullRow(table, ref row, velocitySizeBox);
        AddFullRow(table, ref row, velocityOpacityBox);
        AddRow(table, ref row, "Minimum size %", minimumSizeBox);
        AddRow(table, ref row, "Minimum opacity %", minimumOpacityBox);
        AddRow(table, ref row, "Pressure curve %", pressureCurveBox);
        AddRow(table, ref row, "Velocity curve %", velocityCurveBox);
        Label note = new() { Text = "Windows Pointer pressure is read from compatible pen devices. Mouse strokes use full pressure but still support velocity dynamics.", AutoSize = true, MaximumSize = new Size(290, 0), ForeColor = SystemColors.GrayText };
        AddFullRow(table, ref row, note);
        TabPage page = new("Dynamics"); page.Controls.Add(WrapScrollable(table)); return page;
    }

    private TabPage BuildSymmetryTab()
    {
        TableLayoutPanel table = PropertyTable(); int row = 0;
        AddFullRow(table, ref row, horizontalSymmetryBox);
        AddFullRow(table, ref row, verticalSymmetryBox);
        AddRow(table, ref row, "Horizontal axis X", symmetryXBox);
        AddRow(table, ref row, "Vertical axis Y", symmetryYBox);
        AddFullRow(table, ref row, tiledXBox);
        AddFullRow(table, ref row, tiledYBox);
        AddFullRow(table, ref row, tiledPreviewBox);
        Label note = new() { Text = "Wrapped strokes cross the sprite boundary. Symmetry and tiling can be combined for seamless mirrored patterns.", AutoSize = true, MaximumSize = new Size(290, 0), ForeColor = SystemColors.GrayText };
        AddFullRow(table, ref row, note);
        TabPage page = new("Symmetry & Tile"); page.Controls.Add(WrapScrollable(table)); return page;
    }

    private TabPage BuildGuidesTab()
    {
        TableLayoutPanel table = PropertyTable(); int row = 0;
        AddFullRow(table, ref row, guidesBox);
        AddFullRow(table, ref row, snapBox);
        AddRow(table, ref row, "Grid width", guideWidthBox);
        AddRow(table, ref row, "Grid height", guideHeightBox);
        AddRow(table, ref row, "Grid origin X", guideOriginXBox);
        AddRow(table, ref row, "Grid origin Y", guideOriginYBox);
        Label note = new() { Text = "Custom guides are independent of the RPG Maker MV template overlay. Snapping applies to stroke and shape coordinates.", AutoSize = true, MaximumSize = new Size(290, 0), ForeColor = SystemColors.GrayText };
        AddFullRow(table, ref row, note);
        TabPage page = new("Guides"); page.Controls.Add(WrapScrollable(table)); return page;
    }

    private void HookSettingsEvents()
    {
        foreach (Control control in new Control[] { brushShapeBox, inkModeBox, patternBox, patternAlignmentBox, ditherMatrixBox, presetBox })
            ((ComboBox)control).SelectedIndexChanged += (_, _) => SettingsChanged(control);
        foreach (NumericUpDown control in new[] { brushSize, opacityBox, symmetryXBox, symmetryYBox, guideWidthBox, guideHeightBox, guideOriginXBox, guideOriginYBox, fillToleranceBox, roundedRadiusBox, curveBendBox, ditherAmountBox, minimumSizeBox, minimumOpacityBox, pressureCurveBox, velocityCurveBox })
            control.ValueChanged += (_, _) => SettingsChanged(control);
        foreach (CheckBox control in new[] { pixelPerfectBox, filledShapesBox, horizontalSymmetryBox, verticalSymmetryBox, tiledXBox, tiledYBox, tiledPreviewBox, pressureSizeBox, pressureOpacityBox, velocitySizeBox, velocityOpacityBox, contiguousFillBox, shadeLighterBox, guidesBox, snapBox })
            control.CheckedChanged += (_, _) => SettingsChanged(control);
    }

    private void SettingsChanged(Control source)
    {
        if (suppressSettings) return;
        if (ReferenceEquals(source, presetBox)) { ApplySelectedPreset(); return; }
        if (ReferenceEquals(source, brushShapeBox)) SelectBrushShape();
        else if (ReferenceEquals(source, brushSize)) ResizeCurrentBrush();
        else if (ReferenceEquals(source, patternBox)) RebuildPattern();
        UpdateCanvasSettings();
    }

    private void UpdateCanvasSettings()
    {
        if (suppressSettings) return;
        canvas.Brush = baseBrush;
        canvas.BrushOpacity = (byte)opacityBox.Value;
        canvas.InkMode = ParseEnum(inkModeBox, InkMode.AlphaComposite);
        canvas.ReplaceColor = canvas.BackgroundColor;
        canvas.ShadeDirection = shadeLighterBox.Checked ? 1 : -1;
        canvas.ShadeRamp = BuildShadeRamp(canvas.BackgroundColor, canvas.ForegroundColor);
        canvas.PatternAlignment = ParseEnum(patternAlignmentBox, PatternAlignment.Canvas);
        canvas.PixelPerfect = pixelPerfectBox.Checked;
        canvas.FilledShapes = filledShapesBox.Checked;
        canvas.RoundedRadius = Decimal.ToInt32(roundedRadiusBox.Value);
        canvas.CurveBendPercent = Decimal.ToInt32(curveBendBox.Value);
        canvas.ContiguousFill = contiguousFillBox.Checked;
        canvas.FillTolerance = Decimal.ToInt32(fillToleranceBox.Value);
        canvas.DitherAmount = Decimal.ToDouble(ditherAmountBox.Value) / 100d;
        canvas.DitherMatrix = ParseEnum(ditherMatrixBox, DitherMatrixKind.Bayer4x4);
        canvas.Symmetry = (horizontalSymmetryBox.Checked ? SymmetryMode.Horizontal : SymmetryMode.None) | (verticalSymmetryBox.Checked ? SymmetryMode.Vertical : SymmetryMode.None);
        canvas.SymmetryAxisX = Decimal.ToDouble(symmetryXBox.Value);
        canvas.SymmetryAxisY = Decimal.ToDouble(symmetryYBox.Value);
        canvas.TiledMode = (tiledXBox.Checked ? TiledMode.X : TiledMode.None) | (tiledYBox.Checked ? TiledMode.Y : TiledMode.None);
        canvas.ShowTiledPreview = tiledPreviewBox.Checked;
        canvas.Dynamics = new StrokeDynamicsSettings
        {
            PressureAffectsSize = pressureSizeBox.Checked,
            PressureAffectsOpacity = pressureOpacityBox.Checked,
            VelocityAffectsSize = velocitySizeBox.Checked,
            VelocityAffectsOpacity = velocityOpacityBox.Checked,
            MinimumSizeFactor = Decimal.ToDouble(minimumSizeBox.Value) / 100d,
            MinimumOpacityFactor = Decimal.ToDouble(minimumOpacityBox.Value) / 100d,
            PressureCurveExponent = Decimal.ToDouble(pressureCurveBox.Value) / 100d,
            VelocityCurveExponent = Decimal.ToDouble(velocityCurveBox.Value) / 100d
        };
        canvas.ShowGuides = guidesBox.Checked;
        canvas.SnapToGrid = snapBox.Checked;
        canvas.GuideWidth = Decimal.ToInt32(guideWidthBox.Value);
        canvas.GuideHeight = Decimal.ToInt32(guideHeightBox.Value);
        canvas.GuideOriginX = Decimal.ToInt32(guideOriginXBox.Value);
        canvas.GuideOriginY = Decimal.ToInt32(guideOriginYBox.Value);
        canvas.SetZoom(canvas.Zoom);
        canvas.Invalidate();
    }

    private void SelectBrushShape()
    {
        string selected = brushShapeBox.SelectedItem?.ToString() ?? nameof(BrushShape.Square);
        if (selected == "Custom") { DesignCustomBrush(); return; }
        if (Enum.TryParse(selected, out BrushShape shape)) baseBrush = BrushDefinition.CreateBuiltIn(shape, Decimal.ToInt32(brushSize.Value));
    }

    private void ResizeCurrentBrush()
    {
        int size = Decimal.ToInt32(brushSize.Value);
        baseBrush = baseBrush.Shape == BrushShape.Custom ? baseBrush.Resize(size) : BrushDefinition.CreateBuiltIn(baseBrush.Shape, size);
    }

    private void DesignCustomBrush()
    {
        using CustomBrushDialog dialog = new(baseBrush.Shape == BrushShape.Custom ? baseBrush : null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        baseBrush = dialog.Result;
        suppressSettings = true;
        try { brushShapeBox.SelectedItem = "Custom"; brushSize.Value = Math.Clamp(Math.Max(baseBrush.Width, baseBrush.Height), (int)brushSize.Minimum, (int)brushSize.Maximum); }
        finally { suppressSettings = false; }
        UpdateCanvasSettings();
    }

    private void CaptureBrushFromClipboard()
    {
        if (!Clipboard.ContainsImage()) { MessageBox.Show(this, "Copy an image to the clipboard first.", "No clipboard image", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using Image image = Clipboard.GetImage()!;
        using Bitmap bitmap = new(image);
        PixelBuffer pixels = BitmapToPixelBuffer(bitmap, 128);
        baseBrush = BrushDefinition.FromAlpha(pixels);
        suppressSettings = true;
        try { brushShapeBox.SelectedItem = "Custom"; brushSize.Value = Math.Clamp(Math.Max(baseBrush.Width, baseBrush.Height), (int)brushSize.Minimum, (int)brushSize.Maximum); }
        finally { suppressSettings = false; }
        UpdateCanvasSettings();
    }

    private void CapturePatternFromClipboard()
    {
        if (!Clipboard.ContainsImage()) { MessageBox.Show(this, "Copy an image to the clipboard first.", "No clipboard image", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using Image image = Clipboard.GetImage()!;
        using Bitmap bitmap = new(image);
        canvas.Pattern = new BrushPattern("Clipboard", BitmapToPixelBuffer(bitmap, 256));
        suppressSettings = true;
        try { patternBox.SelectedItem = "Clipboard Pattern"; }
        finally { suppressSettings = false; }
        canvas.Invalidate();
    }

    private void TransformBrush(Func<BrushDefinition, BrushDefinition> transform)
    {
        baseBrush = transform(baseBrush);
        suppressSettings = true;
        try { brushShapeBox.SelectedItem = "Custom"; }
        finally { suppressSettings = false; }
        UpdateCanvasSettings();
    }

    private void RebuildPattern()
    {
        string name = patternBox.SelectedItem?.ToString() ?? "Solid Color";
        canvas.Pattern = name switch
        {
            "Checker 2×2" => MakePattern("Checker", 2, 2, (x, y) => ((x + y) & 1) == 0 ? canvas.ForegroundColor : canvas.BackgroundColor),
            "Bayer 4×4" => MakePattern("Bayer", 4, 4, (x, y) => PixelDrawing.DitherThreshold(x, y, DitherMatrixKind.Bayer4x4) < .5 ? canvas.ForegroundColor : canvas.BackgroundColor),
            "Diagonal" => MakePattern("Diagonal", 4, 4, (x, y) => ((x + y) % 4) < 2 ? canvas.ForegroundColor : canvas.BackgroundColor),
            "Clipboard Pattern" => canvas.Pattern,
            _ => null
        };
        canvas.Invalidate();
    }

    private static BrushPattern MakePattern(string name, int width, int height, Func<int, int, Rgba32> color)
    {
        PixelBuffer pixels = new(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels.SetPixel(x, y, color(x, y));
        return new BrushPattern(name, pixels);
    }

    private void SaveCurrentPreset()
    {
        using TextPromptDialog dialog = new("Save Brush Preset", "Preset name", $"Brush {presets.Count + 1}");
        if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.Value)) return;
        presets.Add(new BrushPreset(dialog.Value, baseBrush, canvas.Pattern, canvas.InkMode, canvas.BrushOpacity));
        BrushPresetStore.Save(GetBrushPresetPath(), presets.Skip(BuiltInPresetCount));
        RefreshPresetBox();
        presetBox.SelectedIndex = presets.Count - 1;
    }

    private void RefreshPresetBox()
    {
        suppressSettings = true;
        try
        {
            string? current = presetBox.SelectedItem?.ToString();
            presetBox.Items.Clear();
            presetBox.Items.AddRange(presets.Select(preset => (object)preset.Name).ToArray());
            presetBox.SelectedItem = current;
            if (presetBox.SelectedIndex < 0 && presetBox.Items.Count > 0) presetBox.SelectedIndex = 0;
        }
        finally { suppressSettings = false; }
    }

    private void ApplySelectedPreset()
    {
        int index = presetBox.SelectedIndex;
        if (index < 0 || index >= presets.Count) return;
        BrushPreset preset = presets[index];
        baseBrush = preset.Brush;
        canvas.Pattern = preset.Pattern;
        suppressSettings = true;
        try
        {
            brushSize.Value = Math.Clamp(Math.Max(baseBrush.Width, baseBrush.Height), (int)brushSize.Minimum, (int)brushSize.Maximum);
            brushShapeBox.SelectedItem = baseBrush.Shape == BrushShape.Custom ? "Custom" : baseBrush.Shape.ToString();
            inkModeBox.SelectedItem = preset.Mode.ToString();
            opacityBox.Value = preset.Opacity;
        }
        finally { suppressSettings = false; }
        UpdateCanvasSettings();
    }

    private void PopulateProfiles()
    {
        profileBox.Items.Add(new ProfileChoice(string.Empty, "General Sprite", null));
        foreach (RpgMakerMvImageProfile profile in profiles.Profiles.OrderBy(item => item.AssetFolder).ThenBy(item => item.DisplayName))
            profileBox.Items.Add(new ProfileChoice(profile.Id, profile.DisplayName, profile));
        profileBox.DisplayMember = nameof(ProfileChoice.DisplayName);
    }

    private void BindDocument()
    {
        suppressProfileChange = true;
        suppressSettings = true;
        try
        {
            string? profileId = session.Document.Metadata.RpgMakerMvProfileId;
            ProfileChoice? choice = profileBox.Items.Cast<ProfileChoice>().FirstOrDefault(item => string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase));
            profileBox.SelectedItem = choice ?? profileBox.Items[0];
            symmetryXBox.Value = ClampDecimal((session.ActiveBuffer.Width - 1) / 2m, symmetryXBox.Minimum, symmetryXBox.Maximum);
            symmetryYBox.Value = ClampDecimal((session.ActiveBuffer.Height - 1) / 2m, symmetryYBox.Minimum, symmetryYBox.Maximum);
        }
        finally { suppressProfileChange = false; suppressSettings = false; }
        canvas.SetDocument(session.ActiveBuffer, CreateGuideCells());
        canvas.SetSelection(session.Selection); canvas.CanEditPixels = !session.ActiveLayer.Locked;
        UpdateCanvasSettings();
        systemAssetPanel?.SetProfile(SelectedProfile()?.Id);
        if (systemAssetPage is not null && inspectorTabs is not null && MvSystemAssetCatalog.TryGet(SelectedProfile()?.Id, out _)) inspectorTabs.SelectedTab = systemAssetPage;
        RefreshDocumentState();
    }

    private IReadOnlyList<IntRect> CreateGuideCells()
    {
        List<IntRect> guides = [];
        RpgMakerMvImageProfile? profile = SelectedProfile();
        if (profile is not null)
        {
            if (MvSystemAssetCatalog.TryGet(profile.Id, out MvSystemAssetDefinition? systemDefinition))
                guides.AddRange(MvSystemAssetCatalog.GetRegions(systemDefinition, session.Document.Canvas.Width, session.Document.Canvas.Height).Select(region => region.Bounds));
            else if (profile.Grid is not null)
            {
                try { guides.AddRange(MvGuideLayout.CreateCellRects(profile, session.Document.Canvas.Width, session.Document.Canvas.Height)); } catch { }
            }
        }
        foreach (SpriteSlice slice in session.Document.Slices)
        {
            SpriteSliceKey key = slice.Resolve(session.ActiveFrameIndex); guides.Add(key.Bounds); if (key.Center is IntRect center) guides.Add(center);
        }
        return guides;
    }

    private void OpenAutomationWorkspace()
    {
        if (inspectorTabs is null || automationPage is null) return; inspectorTabs.SelectedTab = automationPage; automationPanel?.RefreshDocument();
    }

    private void OpenTilemapsSlicesWorkspace()
    {
        if (inspectorTabs is null || tilemapsSlicesPage is null) return; inspectorTabs.SelectedTab = tilemapsSlicesPage;
        if (session.SelectedLayer.Kind == SpriteLayerKind.Tilemap) tilemapsSlicesPanel?.ActivateTilemapWorkspace();
    }

    private void RefreshDocumentState()
    {
        Text = $"Playroma Sprite Studio — {session.Document.Name}{(session.IsDirty ? " *" : string.Empty)}";
        canvas.SetDocument(session.ActiveBuffer, CreateGuideCells(), fit: false);
        canvas.SetSelection(session.Selection); canvas.CanEditPixels = !session.ActiveLayer.Locked;
        string selected = session.Selection.IsEmpty ? "No selection" : $"{session.Selection.CountSelected():N0} px selected";
        string palette = session.ActivePalette is null ? "No palette" : session.ActivePalette.Name;
        documentStatus.Text = $"{session.Document.Canvas.Width}×{session.Document.Canvas.Height}  |  {session.Document.ColorMode} / {palette}  |  Frame {session.ActiveFrameIndex + 1}/{session.Document.Frames.Count} ({session.ActiveFrame.DurationMilliseconds} ms)  |  {canvas.Zoom}00%  |  Layer: {session.SelectedLayer.Name}  |  {selected}  |  Undo {session.History.UndoCount}";
        canvas.Invalidate(); RefreshValidation(); systemAssetPanel?.RefreshFromSession(); automationPanel?.RefreshDocument(); resiliencePanel?.RefreshDocumentState();
        WarnForLargeDocumentIfNeeded();
    }

    private void ChangeProfile()
    {
        if (suppressProfileChange) return;
        session.Document.Metadata.RpgMakerMvProfileId = SelectedProfile()?.Id;
        session.MarkDirty(); canvas.SetDocument(session.ActiveBuffer, CreateGuideCells()); UpdateCanvasSettings();
        systemAssetPanel?.SetProfile(SelectedProfile()?.Id);
    }

    private RpgMakerMvImageProfile? SelectedProfile() => (profileBox.SelectedItem as ProfileChoice)?.Profile;

    private void RefreshValidation()
    {
        validationList.BeginUpdate(); validationList.Items.Clear();
        RpgMakerMvImageProfile? profile = SelectedProfile();
        if (profile is null) validationList.Items.Add("General sprite: no MV restrictions.");
        else
        {
            string fileName = SuggestedRuntimeFileName(profile);
            IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(profile, new MvAssetValidationContext(fileName, profile.AssetFolder, session.Document.Canvas.Width, session.Document.Canvas.Height));
            if (issues.Count == 0) validationList.Items.Add("✓ Valid for RPG Maker MV");
            foreach (MvAssetValidationIssue issue in issues) validationList.Items.Add($"{issue.Severity.ToUpperInvariant()}: {issue.Message}");
        }
        validationList.EndUpdate();
    }

    private void NewDocument()
    {
        if (!ConfirmSaveChanges()) return;
        using NewAssetDialog dialog = new(profiles.Profiles);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MvSystemAssetCatalog.TryGet(dialog.ProfileId, out MvSystemAssetDefinition? definition)
            && dialog.CanvasWidth == definition.StockWidth && dialog.CanvasHeight == definition.StockHeight)
            session.ReplaceWithStaticImage(dialog.DocumentName, MvSystemAssetTemplateFactory.Create(definition), definition.ProfileId, dirty: true);
        else session.NewDocument(dialog.DocumentName, dialog.CanvasWidth, dialog.CanvasHeight, dialog.ProfileId);
        BindDocument();
    }

    private void OpenPng()
    {
        if (!ConfirmSaveChanges()) return;
        using OpenFileDialog dialog = new() { Filter = "PNG images (*.png)|*.png|All files (*.*)|*.*", Title = "Open PNG Sprite" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { session.ImportPng(dialog.FileName, SelectedProfile()?.Id); BindDocument(); }
        catch (Exception exception) { ShowError("PNG import failed", exception); }
    }

    private void ImportPngSequence()
    {
        if (!ConfirmSaveChanges()) return;
        using OpenFileDialog dialog = new() { Filter = "PNG images (*.png)|*.png", Title = "Import PNG Animation Sequence", Multiselect = true };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0) return;
        try { session.ImportPngSequence(dialog.FileNames.OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase), SelectedProfile()?.Id); BindDocument(); }
        catch (Exception exception) { ShowError("PNG sequence import failed", exception); }
    }

    private void ImportGif()
    {
        if (!ConfirmSaveChanges()) return;
        using OpenFileDialog dialog = new() { Filter = "Animated GIF (*.gif)|*.gif|All files (*.*)|*.*", Title = "Import Animated GIF" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { session.ImportGif(dialog.FileName, SelectedProfile()?.Id); BindDocument(); }
        catch (Exception exception) { ShowError("GIF import failed", exception); }
    }

    private async Task OpenPackageAsync()
    {
        if (!ConfirmSaveChanges()) return;
        using OpenFileDialog dialog = new() { Filter = "Playroma Sprite (*.playromasprite)|*.playromasprite|All files (*.*)|*.*", Title = "Open Playroma Sprite" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { await session.OpenPackageAsync(dialog.FileName); BindDocument(); }
        catch (Exception exception) { ShowError("Sprite package could not be opened", exception); }
    }

    private void SavePackageAs()
    {
        using SaveFileDialog dialog = new() { Filter = "Playroma Sprite (*.playromasprite)|*.playromasprite", FileName = SanitizeFileName(session.Document.Name) + SpriteFormatVersion.PackageExtension, Title = "Save Editable Sprite" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { session.SaveAsync(dialog.FileName).GetAwaiter().GetResult(); }
        catch (Exception exception) { ShowError("Sprite package could not be saved", exception); }
    }

    private void ExportPng()
    {
        using SaveFileDialog dialog = new() { Filter = "PNG image (*.png)|*.png", FileName = SuggestedRuntimeFileName(SelectedProfile()), Title = "Export RPG Maker MV PNG" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { using (performanceMonitor.Measure("PNG export")) session.ExportPng(dialog.FileName); }
        catch (Exception exception) { ShowError("PNG export failed", exception); }
    }

    private void ExportPngSequence()
    {
        using FolderBrowserDialog folder = new() { Description = "Choose a folder for the PNG animation sequence", UseDescriptionForTitle = true };
        if (folder.ShowDialog(this) != DialogResult.OK) return;
        using TextPromptDialog name = new("Export PNG Sequence", "Base filename", SanitizeFileName(session.Document.Name));
        if (name.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(name.Value)) return;
        SpritePackageData snapshot = session.Package.DeepClone();
        int[] frames = timelinePanel?.SelectedFrameIndexes.Count > 1 ? timelinePanel.SelectedFrameIndexes.ToArray() : Enumerable.Range(0, snapshot.Document.Frames.Count).ToArray();
        string directory = Path.GetFullPath(folder.SelectedPath); string baseName = SanitizeFileName(name.Value);
        backgroundJobs.Enqueue($"PNG sequence — {snapshot.Document.Name}", async (progress, cancellationToken) =>
        {
            using IDisposable measure = performanceMonitor.Measure("Background PNG sequence export");
            Directory.CreateDirectory(directory);
            for (int position = 0; position < frames.Length; position++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int frame = frames[position];
                PixelBuffer pixels = AnimationFrameRenderer.RenderFrame(snapshot, frame);
                string path = Path.Combine(directory, $"{baseName}-{position + 1:D4}.png");
                PngCodec.Save(pixels, path);
                progress.Report(new SpriteBackgroundProgress((position + 1) * 100 / Math.Max(1, frames.Length), $"Frame {position + 1} of {frames.Length}"));
                await Task.Yield();
            }
        });
        coordinateStatus.Text = "PNG sequence queued in background";
        OpenResilienceWorkspace();
    }

    private void ExportGif()
    {
        using SaveFileDialog dialog = new() { Filter = "Animated GIF (*.gif)|*.gif", FileName = SanitizeFileName(session.Document.Name) + ".gif", Title = "Export Animated GIF" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SpritePackageData snapshot = session.Package.DeepClone();
        int[] frames = timelinePanel?.SelectedFrameIndexes.Count > 1 ? timelinePanel.SelectedFrameIndexes.ToArray() : Enumerable.Range(0, snapshot.Document.Frames.Count).ToArray();
        string output = Path.GetFullPath(dialog.FileName);
        backgroundJobs.Enqueue($"Animated GIF — {snapshot.Document.Name}", async (progress, cancellationToken) =>
        {
            using IDisposable measure = performanceMonitor.Measure("Background GIF export");
            AnimationFrameImage[] images = new AnimationFrameImage[frames.Length];
            for (int position = 0; position < frames.Length; position++)
            {
                cancellationToken.ThrowIfCancellationRequested(); int frame = frames[position];
                images[position] = new AnimationFrameImage(AnimationFrameRenderer.RenderFrame(snapshot, frame), snapshot.Document.Frames[frame].DurationMilliseconds);
                progress.Report(new SpriteBackgroundProgress((position + 1) * 80 / Math.Max(1, frames.Length), $"Rendering frame {position + 1} of {frames.Length}"));
                await Task.Yield();
            }
            cancellationToken.ThrowIfCancellationRequested(); GifCodec.Save(images, output, 0); progress.Report(new SpriteBackgroundProgress(100, "GIF written"));
        });
        coordinateStatus.Text = "Animated GIF queued in background";
        OpenResilienceWorkspace();
    }

    private Task ExportToMvProjectAsync() => InstallIntoMvProjectAsync();

    private void AutoDetectMvProfile()
    {
        try
        {
            string fileName = session.ExportPath is not null ? Path.GetFileName(session.ExportPath) : SuggestedRuntimeFileName(SelectedProfile());
            string? folder = session.ExportPath is not null ? ResolveProjectRelativeFolder(session.ExportPath) : null;
            IReadOnlyList<MvProfileDetectionCandidate> candidates = new MvAssetProfileDetector(profiles).Detect(
                new MvProfileDetectionInput(fileName, folder, session.Document.Canvas.Width, session.Document.Canvas.Height), 6);
            if (candidates.Count == 0) return;
            MvProfileDetectionCandidate best = candidates[0];
            SelectProfile(best.Profile.Id);
            string details = string.Join(Environment.NewLine, candidates.Select(candidate =>
                $"{candidate.Profile.DisplayName}: score {candidate.Score}, confidence {candidate.Confidence:P0}"));
            MessageBox.Show(this, $"Selected {best.Profile.DisplayName}.\n\n{details}", "MV profile detection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { ShowError("MV profile detection failed", exception); }
    }

    private void ShowMvValidationReport()
    {
        RpgMakerMvImageProfile? profile = SelectedProfile();
        if (profile is null) { MessageBox.Show(this, "Choose or auto-detect an MV profile first.", "No MV profile", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string fileName = session.ExportPath is not null && Path.GetExtension(session.ExportPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(session.ExportPath)
            : SuggestedRuntimeFileName(profile);
        PixelBuffer validationImage = profile.Grid is not null && session.Document.Frames.Count > 1
            ? session.AssembleMvSheet(profile, null, new MvSheetAssemblyOptions(
                RequireExactProfileCellCount: profile.Id != "mv-animation-sheet" && profile.Id != "mv-iconset"))
            : session.CompositeCurrentFrame();
        IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(profile,
            new MvAssetValidationContext(fileName, profile.AssetFolder, validationImage.Width, validationImage.Height));
        string report = issues.Count == 0
            ? "The current canvas or assembled animation satisfies all RPG Maker MV profile rules."
            : string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Severity.ToUpperInvariant()} {issue.Code}: {issue.Message}"));
        MessageBox.Show(this, report, $"MV Validation — {profile.DisplayName}", MessageBoxButtons.OK,
            issues.Any(issue => issue.Severity == "error") ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    private void SplitMvSheetIntoFrames()
    {
        RpgMakerMvImageProfile? profile = SelectedProfile();
        if (profile?.Grid is null) { MessageBox.Show(this, "The selected profile does not define a splittable grid.", "No sheet grid", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!ConfirmSaveChanges()) return;
        try
        {
            SpritePackageData split = MvSheetExchange.SplitToAnimationPackage(session.CompositeCurrentFrame(), profile, session.Document.Name + " Cells");
            session.ReplacePackage(split, dirty: true);
            BindDocument();
            MessageBox.Show(this, $"Split the sheet into {session.Document.Frames.Count} editable frames and added MV semantic tags.", "Sheet split", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { ShowError("Sheet split failed", exception); }
    }

    private void AssembleFramesIntoMvSheet()
    {
        RpgMakerMvImageProfile? profile = SelectedProfile();
        if (profile?.Grid is null) { MessageBox.Show(this, "The selected profile does not define an assembly grid.", "No sheet grid", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            PixelBuffer sheet = session.AssembleMvSheet(profile, null, new MvSheetAssemblyOptions(RequireExactProfileCellCount: profile.Id != "mv-animation-sheet" && profile.Id != "mv-iconset"));
            if (!ConfirmSaveChanges()) return;
            session.ReplaceWithStaticImage(session.Document.Name + " Sheet", sheet, profile.Id, dirty: true);
            BindDocument();
        }
        catch (Exception exception) { ShowError("Sheet assembly failed", exception); }
    }

    private void ExportAssembledMvSheet()
    {
        RpgMakerMvImageProfile? profile = SelectedProfile();
        if (profile?.Grid is null) { MessageBox.Show(this, "The selected profile does not define an assembly grid.", "No sheet grid", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using SaveFileDialog dialog = new() { Filter = "PNG image (*.png)|*.png", FileName = SuggestedRuntimeFileName(profile), Title = $"Export {profile.DisplayName}" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            PixelBuffer sheet = session.AssembleMvSheet(profile, null, new MvSheetAssemblyOptions(RequireExactProfileCellCount: profile.Id != "mv-animation-sheet" && profile.Id != "mv-iconset"));
            IReadOnlyList<MvAssetValidationIssue> issues = MvAssetValidator.Validate(profile, new MvAssetValidationContext(Path.GetFileName(dialog.FileName), profile.AssetFolder, sheet.Width, sheet.Height));
            if (issues.Any(issue => issue.Severity == "error")) throw new MvAssetValidationException(profile.Id, issues);
            PngCodec.Save(sheet, dialog.FileName);
        }
        catch (Exception exception) { ShowError("MV sheet export failed", exception); }
    }

    private async Task InstallIntoMvProjectAsync()
    {
        if (string.IsNullOrWhiteSpace(options.ProjectRoot) || !Directory.Exists(options.ProjectRoot))
        {
            MessageBox.Show(this, "Open Sprite Studio from a PlayromaJRPG project to install assets directly.", "No RPG Maker MV project", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        RpgMakerMvImageProfile? profile = SelectedProfile();
        if (profile is null) { MessageBox.Show(this, "Choose an RPG Maker MV profile first.", "Asset profile required", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string fileName = SuggestedRuntimeFileName(profile);
        using TextPromptDialog prompt = new("Install RPG Maker MV Asset", $"Filename in {profile.AssetFolder}", fileName);
        if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(prompt.Value)) return;
        try
        {
            PixelBuffer pixels = profile.Grid is not null && session.Document.Frames.Count > 1
                ? session.AssembleMvSheet(profile, null, new MvSheetAssemblyOptions(RequireExactProfileCellCount: profile.Id != "mv-animation-sheet" && profile.Id != "mv-iconset"))
                : session.CompositeCurrentFrame();
            MvRuntimeInstallResult result = MvRuntimeInstaller.Install(new MvRuntimeInstallRequest(options.ProjectRoot, profile, prompt.Value.Trim(), pixels));
            session.Document.Metadata.RuntimeRelativePath = result.RuntimeRelativePath;
            session.MarkDirty();
            if (options.AssetInstalled is not null) await options.AssetInstalled(result, CancellationToken.None);
            string backup = result.BackupPath is null ? string.Empty : $"\nBackup: {result.BackupPath}";
            MessageBox.Show(this, $"Installed {result.RuntimeRelativePath}.{backup}", "RPG Maker MV asset installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { ShowError("Project installation failed", exception); }
    }


    private void OpenSystemAssetEditor()
    {
        if (systemAssetPage is null || inspectorTabs is null) return;
        inspectorTabs.SelectedTab = systemAssetPage;
        if (!MvSystemAssetCatalog.TryGet(SelectedProfile()?.Id, out _)) SelectProfile("mv-iconset");
    }

    private void CreateSystemAssetTemplate(MvSystemAssetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!ConfirmSaveChanges()) return;
        session.ReplaceWithStaticImage(Path.GetFileNameWithoutExtension(definition.ReservedFileName), MvSystemAssetTemplateFactory.Create(definition), definition.ProfileId, dirty: true);
        BindDocument();
        OpenSystemAssetEditor();
    }

    private void CreateProjectProfileOverride()
    {
        if (string.IsNullOrWhiteSpace(options.ProjectRoot)) { MessageBox.Show(this, "Open a project first.", "No project", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            MvProjectProfileProvider.WriteProjectTemplate(options.ProjectRoot);
            MessageBox.Show(this, MvProjectProfileProvider.ProjectProfileRelativePath + " is ready for plugin-specific profile overrides.", "Project profile file", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { ShowError("Could not create the project profile file", exception); }
    }

    private void SelectProfile(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId)) return;
        for (int index = 0; index < profileBox.Items.Count; index++)
        {
            if (profileBox.Items[index] is ProfileChoice choice && choice.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            {
                profileBox.SelectedIndex = index;
                return;
            }
        }
    }

    private string? ResolveProjectRelativeFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(options.ProjectRoot)) return null;
        string fullRoot = Path.GetFullPath(options.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(options.ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!fullPath.StartsWith(fullRoot, comparison)) return null;
        string relative = Path.GetRelativePath(options.ProjectRoot, Path.GetDirectoryName(fullPath)!).Replace('\\', '/');
        return relative;
    }

    private void ConvertToIndexed()
    {
        using TextPromptDialog dialog = new("Indexed Color", "Maximum colors (2-256)", "32");
        if (dialog.ShowDialog(this) != DialogResult.OK || !int.TryParse(dialog.Value, out int colors)) return;
        TryColorAction(() => session.ConvertColorMode(SpriteColorMode.Indexed, Math.Clamp(colors, 2, 256), PaletteDitherMode.FloydSteinberg));
    }

    private void TryColorAction(Action action)
    {
        try { action(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Color operation", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ApplyQuickEffect(string description, Action<PixelBuffer> effect)
    {
        if (session.ActiveLayer.Locked) return;
        BatchEffectSelection selection = new(BatchEffectScope.ActiveCel, session.ActiveLayerId, session.ActiveFrameId);
        try { session.ApplyBatchEffect(description, selection, effect); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Effect", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ApplyPromptedRadiusEffect(string description, Func<int, Action<PixelBuffer>> factory)
    {
        using TextPromptDialog dialog = new(description, "Radius / block size", "2");
        if (dialog.ShowDialog(this) != DialogResult.OK || !int.TryParse(dialog.Value, out int radius)) return;
        ApplyQuickEffect(description, factory(Math.Clamp(radius, 1, 64)));
    }

    private void ClearCanvas()
    {
        if (session.ActiveLayer.Locked) return;
        ApplyBufferOperation("Clear", buffer =>
        {
            if (session.Selection.IsEmpty) buffer.Clear(Rgba32.Transparent);
            else PixelSelectionTransforms.Clear(buffer, session.Selection);
        });
    }

    private void ApplyOutline()
    {
        using TextPromptDialog dialog = new("Outline", "Thickness in pixels", "1");
        if (dialog.ShowDialog(this) != DialogResult.OK || !int.TryParse(dialog.Value, out int thickness)) return;
        ApplyBufferOperation("Outline", buffer => PixelEffects.Outline(buffer, canvas.ForegroundColor, Math.Clamp(thickness, 1, 64), true, false));
    }

    private void ApplyOrderedDither() => ApplyBufferOperation("Ordered Dither", buffer => PixelEffects.ApplyOrderedDither(buffer, canvas.ForegroundColor, canvas.BackgroundColor, ParseEnum(ditherMatrixBox, DitherMatrixKind.Bayer4x4)));
    private void ReplaceBackgroundColor() => ApplyBufferOperation("Replace Color", buffer => PixelEffects.ReplaceColor(buffer, canvas.BackgroundColor, canvas.ForegroundColor, Decimal.ToInt32(fillToleranceBox.Value)));

    private void ApplyBufferOperation(string description, Action<PixelBuffer> operation)
    {
        if (session.ActiveLayer.Locked) return;
        PixelBuffer before = session.ActiveBuffer.Clone(); operation(session.ActiveBuffer); session.CommitPixelEdit(description, before);
    }

    private void TryLayerAction(Action action)
    {
        try { action(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Layer operation", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private void TryAnimationAction(Action action)
    {
        try { action(); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Animation operation", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private void ReverseSelectedFrames()
    {
        IReadOnlyList<int> frames = timelinePanel?.SelectedFrameIndexes ?? [session.ActiveFrameIndex];
        if (frames.Count == 0) return;
        TryAnimationAction(() => session.ReverseFrames(frames.Min(), frames.Max()));
    }

    private void SetSelectedFrameDuration()
    {
        using TextPromptDialog dialog = new("Frame Duration", "Duration in milliseconds", session.ActiveFrame.DurationMilliseconds.ToString());
        if (dialog.ShowDialog(this) != DialogResult.OK || !int.TryParse(dialog.Value, out int duration) || duration <= 0) return;
        IReadOnlyList<int> frames = timelinePanel?.SelectedFrameIndexes ?? [session.ActiveFrameIndex];
        TryAnimationAction(() => session.SetFrameDuration(frames.ToArray(), duration));
    }

    private void ResizeSprite()
    {
        using TextPromptDialog dialog = new("Resize Sprite", "New size as WIDTHxHEIGHT (nearest-neighbor)", $"{session.Document.Canvas.Width}x{session.Document.Canvas.Height}");
        if (dialog.ShowDialog(this) != DialogResult.OK || !TryParseSize(dialog.Value, out int width, out int height)) return;
        session.ResizeSprite(width, height, PixelResamplingMode.NearestNeighbor); BindDocument();
    }

    private void ResizeCanvas()
    {
        using TextPromptDialog dialog = new("Resize Canvas", "WIDTHxHEIGHT@SOURCE_X,SOURCE_Y", $"{session.Document.Canvas.Width}x{session.Document.Canvas.Height}@0,0");
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string[] main = dialog.Value.Split('@', 2); if (!TryParseSize(main[0], out int width, out int height)) return;
        int x = 0, y = 0;
        if (main.Length == 2)
        {
            string[] origin = main[1].Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (origin.Length != 2 || !int.TryParse(origin[0], out x) || !int.TryParse(origin[1], out y)) return;
        }
        session.ResizeCanvas(new IntRect(x, y, width, height)); BindDocument();
    }

    private void CropToSelection()
    {
        IntRect? bounds = session.Selection.GetBounds(); if (bounds is null) return; session.ResizeCanvas(bounds.Value); BindDocument();
    }

    private void TrimTransparent()
    {
        if (session.TrimTransparent()) BindDocument();
    }

    private static bool TryParseSize(string value, out int width, out int height)
    {
        width = 0;
        height = 0;
        string[] parts = value.Split(['x', 'X', ',', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], out width)
            && int.TryParse(parts[1], out height)
            && width > 0
            && height > 0;
    }

    private void PickColor(bool foreground)
    {
        Rgba32 source = foreground ? canvas.ForegroundColor : canvas.BackgroundColor;
        using ColorDialog dialog = new() { FullOpen = true, Color = Color.FromArgb(source.A, source.R, source.G, source.B) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Rgba32 selected = new(dialog.Color.R, dialog.Color.G, dialog.Color.B, dialog.Color.A);
        if (foreground) canvas.ForegroundColor = selected; else canvas.BackgroundColor = selected;
        UpdateColorButtons(); RebuildPattern();
    }

    private void UpdateColorButtons()
    {
        SetColorButton(foregroundButton, "Foreground", canvas.ForegroundColor);
        SetColorButton(backgroundButton, "Background", canvas.BackgroundColor);
    }

    private static void SetColorButton(Button button, string label, Rgba32 color)
    {
        button.BackColor = Color.FromArgb(color.A, color.R, color.G, color.B);
        button.ForeColor = color.R + color.G + color.B < 330 ? Color.White : Color.Black;
        button.Text = $"{label}  #{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    }

    private async Task AutosaveAsync()
    {
        if (!session.IsDirty) return;
        try
        {
            using (performanceMonitor.Measure("Recovery snapshot"))
                await recoveryService.CaptureAsync(session.Package, session.PackagePath, session.ExportPath, "autosave", preferences.RecoverySnapshotsPerDocument);
            coordinateStatus.Text = "Recovery snapshot " + DateTime.Now.ToShortTimeString();
            resiliencePanel?.RefreshAll();
        }
        catch (Exception exception) { coordinateStatus.Text = "Autosave failed: " + exception.Message; }
    }

    private bool ConfirmSaveChanges()
    {
        if (!session.IsDirty) return true;
        DialogResult result = MessageBox.Show(this, "Save changes to the current sprite?", "Unsaved sprite", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes) { SaveIntegratedProject(); return !session.IsDirty; }
        return true;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (closePrepared) return;
        if (!ConfirmSaveChanges()) { e.Cancel = true; return; }
        if (!ConfirmBackgroundJobsCanClose()) { e.Cancel = true; return; }
        PersistWorkspaceAndPreferences();
        closePrepared = true;
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        try { backgroundJobs.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { sessionLease.MarkCleanExit(); } catch { }
        sessionLease.Dispose();
    }

    private bool ConfirmBackgroundJobsCanClose()
    {
        int active = backgroundJobs.Jobs.Count(item => item.Status is SpriteBackgroundJobStatus.Queued or SpriteBackgroundJobStatus.Running);
        if (active == 0) return true;
        DialogResult result = MessageBox.Show(this,
            $"{active} background export job{(active == 1 ? string.Empty : "s")} still running. Cancel the active work and close Sprite Studio?",
            "Background exports are active", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return false;
        backgroundJobs.CancelAll();
        return true;
    }

    private void RequestClose() { if (options.RequestHostClose is not null) options.RequestHostClose(); else Close(); }

    private string SuggestedRuntimeFileName(RpgMakerMvImageProfile? profile)
    {
        if (profile?.Metadata.TryGetValue("expectedFileName", out string? expectedFileName) == true && !string.IsNullOrWhiteSpace(expectedFileName))
            return expectedFileName;
        string name = SanitizeFileName(session.Document.Name);
        if (profile?.FilenameRules.RequireDollarPrefix == true && !name.StartsWith('$')) name = "$" + name;
        return name + ".png";
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string clean = new(name.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "Sprite" : clean;
    }

    private void ShowError(string title, Exception exception) => MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (TryExecuteConfiguredShortcut(keyData)) return true;
        if (keyData == (Keys.Control | Keys.C)) { selectionPanel?.Copy(); return true; }
        if (keyData == (Keys.Control | Keys.X)) { selectionPanel?.Cut(); return true; }
        if (keyData == (Keys.Control | Keys.V)) { selectionPanel?.Paste(); return true; }
        if (keyData == (Keys.Control | Keys.A)) { selectionPanel?.SelectAll(); return true; }
        if (keyData == (Keys.Control | Keys.D)) { selectionPanel?.Deselect(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.I)) { selectionPanel?.Invert(); return true; }
        if (keyData == Keys.Left && !session.Selection.IsEmpty) { selectionPanel?.Nudge(-1, 0); return true; }
        if (keyData == Keys.Right && !session.Selection.IsEmpty) { selectionPanel?.Nudge(1, 0); return true; }
        if (keyData == Keys.Up && !session.Selection.IsEmpty) { selectionPanel?.Nudge(0, -1); return true; }
        if (keyData == Keys.Down && !session.Selection.IsEmpty) { selectionPanel?.Nudge(0, 1); return true; }
        if (keyData == Keys.Enter && canvas.HasActivePolygon) { canvas.CompletePolygon(); return true; }
        if (keyData == Keys.Escape && canvas.HasActivePolygon) { canvas.CancelPolygon(); return true; }
        if (keyData == Keys.Space) { timelinePanel?.TogglePlayback(); return true; }
        if (keyData == Keys.Oemcomma) { timelinePanel?.PreviousFrame(); return true; }
        if (keyData == Keys.OemPeriod) { timelinePanel?.NextFrame(); return true; }
        if (keyData == Keys.B) { SelectTool(PixelToolKind.Pencil); return true; }
        if (keyData == Keys.E) { SelectTool(PixelToolKind.Eraser); return true; }
        if (keyData == Keys.L) { SelectTool(PixelToolKind.Line); return true; }
        if (keyData == Keys.C) { SelectTool(PixelToolKind.Curve); return true; }
        if (keyData == Keys.R) { SelectTool(PixelToolKind.Rectangle); return true; }
        if (keyData == Keys.O) { SelectTool(PixelToolKind.Ellipse); return true; }
        if (keyData == Keys.P) { SelectTool(PixelToolKind.Polygon); return true; }
        if (keyData == Keys.G) { SelectTool(PixelToolKind.Fill); return true; }
        int keyValue = (int)keyData;
        if (keyValue >= (int)Keys.D1 && keyValue <= (int)Keys.D9)
        {
            int slot = keyValue - (int)Keys.D1;
            if (slot < presetBox.Items.Count) { presetBox.SelectedIndex = slot; return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OpenResilienceWorkspace()
    {
        if (inspectorTabs is null || resiliencePage is null) return;
        if (canvasOnlyMode) ToggleCanvasOnlyMode();
        inspectorTabs.SelectedTab = resiliencePage;
        resiliencePanel?.RefreshAll();
    }

    private SpriteWorkspaceProfile CaptureWorkspaceProfile()
    {
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        int inspectorWidth = workspaceSplit is null ? 410 : inspectorMirrored ? workspaceSplit.SplitterDistance : workspaceSplit.Width - workspaceSplit.SplitterDistance;
        int timelineHeight = canvasTimelineSplit is null ? 250 : canvasTimelineSplit.Height - canvasTimelineSplit.SplitterDistance;
        return new SpriteWorkspaceProfile
        {
            Id = preferences.ActiveWorkspaceId,
            Name = preferences.ActiveWorkspaceId.Equals("default", StringComparison.OrdinalIgnoreCase) ? "Default" : preferences.ActiveWorkspaceId,
            WindowX = bounds.X,
            WindowY = bounds.Y,
            WindowWidth = bounds.Width,
            WindowHeight = bounds.Height,
            Maximized = WindowState == FormWindowState.Maximized,
            MirrorInspector = inspectorMirrored,
            TimelineVisible = canvasTimelineSplit?.Panel2Collapsed != true,
            InspectorWidth = Math.Max(240, inspectorWidth),
            TimelineHeight = Math.Max(100, timelineHeight),
            ActiveInspectorTab = inspectorTabs?.SelectedTab?.Text ?? "Layers",
            CanvasZoom = canvas.Zoom,
            Panels =
            [
                new SpriteWorkspacePanelState { Key = "inspector", Dock = inspectorMirrored ? SpritePanelDock.Left : SpritePanelDock.Right, Visible = workspaceSplit is null || (inspectorMirrored ? !workspaceSplit.Panel1Collapsed : !workspaceSplit.Panel2Collapsed), SizeRatio = workspaceSplit is null || workspaceSplit.Width == 0 ? .27d : inspectorWidth / (double)workspaceSplit.Width },
                new SpriteWorkspacePanelState { Key = "timeline", Dock = SpritePanelDock.Bottom, Visible = canvasTimelineSplit?.Panel2Collapsed != true, SizeRatio = canvasTimelineSplit is null || canvasTimelineSplit.Height == 0 ? .25d : timelineHeight / (double)canvasTimelineSplit.Height }
            ]
        };
    }

    private void InitializeSplitterLayout()
    {
        if (workspaceSplit is not null)
        {
            // Keep a useful inspector width while remaining valid at every DPI and window size.
            int desiredInspectorWidth = Math.Clamp(360, 280, Math.Max(280, workspaceSplit.ClientSize.Width / 2));
            SetSplitterDistanceSafely(workspaceSplit, workspaceSplit.ClientSize.Width - desiredInspectorWidth);
        }

        if (canvasTimelineSplit is not null)
        {
            // Reserve a practical timeline area without assuming the final scaled form height.
            int desiredTimelineHeight = Math.Clamp(220, 170, Math.Max(170, canvasTimelineSplit.ClientSize.Height / 2));
            SetSplitterDistanceSafely(canvasTimelineSplit, canvasTimelineSplit.ClientSize.Height - desiredTimelineHeight);
        }
    }

    private static void SetSplitterDistanceSafely(SplitContainer split, int desiredDistance)
    {
        if (split.IsDisposed) return;

        int extent = split.Orientation == Orientation.Horizontal
            ? split.ClientSize.Height
            : split.ClientSize.Width;
        if (extent <= 0) return;

        int minimum = Math.Max(0, split.Panel1MinSize);
        int maximum = extent - split.SplitterWidth - Math.Max(0, split.Panel2MinSize);
        if (maximum < minimum) return;

        split.SplitterDistance = Math.Clamp(desiredDistance, minimum, maximum);
    }

    private void ApplyWorkspaceProfile(SpriteWorkspaceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        SpriteWorkspaceProfileStore.Validate(profile);
        SetInspectorMirrored(profile.MirrorInspector);
        Rectangle proposed = new(profile.WindowX, profile.WindowY, profile.WindowWidth, profile.WindowHeight);
        Rectangle working = Screen.AllScreens.Select(screen => screen.WorkingArea).FirstOrDefault(area => area.IntersectsWith(proposed));
        if (working == Rectangle.Empty) working = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        int width = Math.Min(profile.WindowWidth, working.Width);
        int height = Math.Min(profile.WindowHeight, working.Height);
        int x = Math.Clamp(profile.WindowX, working.Left, Math.Max(working.Left, working.Right - width));
        int y = Math.Clamp(profile.WindowY, working.Top, Math.Max(working.Top, working.Bottom - height));
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(x, y, width, height);
        if (workspaceSplit is not null && workspaceSplit.Width > 0)
        {
            int distance = inspectorMirrored ? profile.InspectorWidth : workspaceSplit.ClientSize.Width - profile.InspectorWidth;
            SetSplitterDistanceSafely(workspaceSplit, distance);
            SetInspectorVisible(true);
        }
        if (canvasTimelineSplit is not null && canvasTimelineSplit.Height > 0)
        {
            canvasTimelineSplit.Panel2Collapsed = !profile.TimelineVisible;
            if (profile.TimelineVisible)
            {
                int distance = canvasTimelineSplit.ClientSize.Height - profile.TimelineHeight;
                SetSplitterDistanceSafely(canvasTimelineSplit, distance);
            }
        }
        if (inspectorTabs is not null)
        {
            TabPage? tab = inspectorTabs.TabPages.Cast<TabPage>().FirstOrDefault(item => item.Text.Equals(profile.ActiveInspectorTab, StringComparison.OrdinalIgnoreCase));
            if (tab is not null) inspectorTabs.SelectedTab = tab;
        }
        canvas.SetZoom(profile.CanvasZoom);
        WindowState = profile.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
    }

    private void SetInspectorVisible(bool visible)
    {
        if (workspaceSplit is null) return;
        if (inspectorMirrored) workspaceSplit.Panel1Collapsed = !visible;
        else workspaceSplit.Panel2Collapsed = !visible;
    }

    private void SetInspectorMirrored(bool mirrored)
    {
        if (workspaceSplit is null || inspectorMirrored == mirrored || workspaceSplit.Panel1.Controls.Count == 0 || workspaceSplit.Panel2.Controls.Count == 0) { inspectorMirrored = mirrored; return; }
        Control first = workspaceSplit.Panel1.Controls[0];
        Control second = workspaceSplit.Panel2.Controls[0];
        workspaceSplit.Panel1.Controls.Clear(); workspaceSplit.Panel2.Controls.Clear();
        workspaceSplit.Panel1.Controls.Add(second); workspaceSplit.Panel2.Controls.Add(first);
        workspaceSplit.FixedPanel = mirrored ? FixedPanel.Panel1 : FixedPanel.Panel2;
        inspectorMirrored = mirrored;
    }

    private void SaveCurrentWorkspace()
    {
        SpriteWorkspaceProfile profile = CaptureWorkspaceProfile();
        profile.Id = string.IsNullOrWhiteSpace(preferences.ActiveWorkspaceId) ? "default" : preferences.ActiveWorkspaceId;
        profile.Name = profile.Id.Equals("default", StringComparison.OrdinalIgnoreCase) ? "Default" : profile.Id;
        workspaceStore.Save(profile);
        coordinateStatus.Text = "Workspace saved";
        resiliencePanel?.RefreshAll();
    }

    private void PersistWorkspaceAndPreferences()
    {
        try { SaveCurrentWorkspace(); } catch { }
        try { preferencesStore.Save(preferences); } catch { }
    }

    private void ApplyPreferences(SpriteEditorPreferences updated)
    {
        preferences = updated;
        SpritePreferencesStore.ValidateAndRepair(preferences);
        session.History.MemoryLimitBytes = preferences.UndoMemoryLimitBytes;
        autosaveTimer.Interval = checked(preferences.AutosaveSeconds * 1000);
        double fontScale = preferences.FontScale * preferences.UiScale;
        Font = new Font(SystemFonts.MessageBoxFont.FontFamily, (float)(SystemFonts.MessageBoxFont.Size * fontScale), SystemFonts.MessageBoxFont.Style);
        ApplyTheme(this, preferences.HighContrast ? SpriteUiTheme.HighContrast : preferences.Theme);
        if (preferences.ReduceMotion) timelinePanel?.StopPlayback();
        ApplyDpiPolish();
        canvas.Invalidate();
    }

    private void ApplyDpiPolish()
    {
        int imageSize = Math.Clamp((int)Math.Round(24d * DeviceDpi / 96d * preferences.UiScale), 16, 64);
        foreach (ToolStrip strip in FindControls<ToolStrip>(this)) strip.ImageScalingSize = new Size(imageSize, imageSize);
        MinimumSize = new Size((int)Math.Round(1120d * preferences.UiScale), (int)Math.Round(720d * preferences.UiScale));
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (T nested in FindControls<T>(child)) yield return nested;
        }
    }

    private static void ApplyTheme(Control root, SpriteUiTheme theme)
    {
        bool dark = theme == SpriteUiTheme.Dark;
        bool highContrast = theme == SpriteUiTheme.HighContrast;
        Color back = highContrast ? SystemColors.Window : dark ? Color.FromArgb(32, 34, 38) : SystemColors.Control;
        Color foreground = highContrast ? SystemColors.WindowText : dark ? Color.Gainsboro : SystemColors.ControlText;
        Color fieldBack = highContrast ? SystemColors.Window : dark ? Color.FromArgb(24, 26, 30) : SystemColors.Window;
        root.BackColor = back; root.ForeColor = foreground;
        foreach (Control child in root.Controls)
        {
            child.ForeColor = foreground;
            child.BackColor = child is TextBoxBase or ListBox or TreeView or ListView or DataGridView or ComboBox or NumericUpDown ? fieldBack : back;
            ApplyTheme(child, theme);
        }
    }

    private static void ApplyAccessibilityMetadata(Control root)
    {
        if (string.IsNullOrWhiteSpace(root.AccessibleName)) root.AccessibleName = !string.IsNullOrWhiteSpace(root.Text) ? root.Text.Replace("&", string.Empty) : root.Name;
        if (string.IsNullOrWhiteSpace(root.AccessibleDescription) && !string.IsNullOrWhiteSpace(root.AccessibleName)) root.AccessibleDescription = root.AccessibleName + " control";
        foreach (Control child in root.Controls) ApplyAccessibilityMetadata(child);
    }

    private async Task OfferCrashRecoveryAsync()
    {
        SpriteRecoverySnapshot? newest = recoveryService.ListSnapshots().FirstOrDefault();
        if (newest is null) return;
        DialogResult result = MessageBox.Show(this,
            $"Sprite Studio did not close cleanly last time. Restore the newest recovery snapshot for '{newest.DocumentName}' from {newest.CapturedAt.LocalDateTime:g}?",
            "Recovery available", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;
        SpritePackageData recovered = await recoveryService.LoadAsync(newest);
        session.ReplacePackage(recovered, newest.OriginalPackagePath, newest.RuntimeExportPath, dirty: true);
        BindDocument();
    }

    private void WarnForLargeDocumentIfNeeded()
    {
        if (!preferences.WarnOnLargeDocuments || largeDocumentWarningsShown.Contains(session.Document.Id)) return;
        SpriteLargeDocumentAssessment assessment = SpriteLargeDocumentPolicy.Assess(session.Package, preferences.LargeDocumentPixelThreshold);
        if (!assessment.IsLarge) return;
        largeDocumentWarningsShown.Add(session.Document.Id);
        string recommendations = string.Join(Environment.NewLine, assessment.Recommendations.Select(item => "• " + item));
        MessageBox.Show(this,
            $"This sprite is large enough to require performance safeguards. Estimated in-memory size: {assessment.EstimatedBytes / 1024d / 1024d:0.0} MiB.\n\n{recommendations}",
            "Large sprite document", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ToggleCanvasOnlyMode()
    {
        if (workspaceSplit is null || canvasTimelineSplit is null) return;
        canvasOnlyMode = !canvasOnlyMode;
        SetInspectorVisible(!canvasOnlyMode);
        canvasTimelineSplit.Panel2Collapsed = canvasOnlyMode;
        MainMenuStrip.Visible = !canvasOnlyMode;
        foreach (ToolStrip strip in Controls.OfType<ToolStrip>()) strip.Visible = !canvasOnlyMode;
        coordinateStatus.Text = canvasOnlyMode ? "Canvas-only mode — press Tab to restore panels" : "Workspace restored";
    }

    private void ShowCommandPalette()
    {
        using CommandPaletteDialog dialog = new(BuildEditorCommands());
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCommand is SpriteEditorCommand command) command.Execute();
    }

    private IReadOnlyList<SpriteEditorCommand> BuildEditorCommands() =>
    [
        new("file.new", "New Sprite", "File", NewDocument),
        new("file.open", "Open PNG", "File", OpenPng),
        new("file.save", "Save Editable Sprite", "File", SaveIntegratedProject),
        new("file.saveAs", "Save Editable Sprite As", "File", SavePackageAs),
        new("edit.undo", "Undo", "Edit", session.Undo),
        new("edit.redo", "Redo", "Edit", session.Redo),
        new("selection.all", "Select All", "Selection", () => selectionPanel?.SelectAll()),
        new("selection.clear", "Deselect", "Selection", () => selectionPanel?.Deselect()),
        new("animation.play", "Play or Pause Animation", "Animation", () => timelinePanel?.TogglePlayback()),
        new("view.fullscreenCanvas", "Toggle Canvas-Only Mode", "View", ToggleCanvasOnlyMode),
        new("view.resilience", "Open Resilience and Polish", "View", OpenResilienceWorkspace),
        new("workspace.save", "Save Current Workspace", "View", SaveCurrentWorkspace),
        new("recovery.capture", "Capture Recovery Snapshot", "Recovery", () => _ = AutosaveAsync()),
        new("export.png", "Export Current Frame as PNG", "Export", ExportPng),
        new("export.gif", "Queue Animated GIF Export", "Export", ExportGif),
        new("export.sequence", "Queue PNG Sequence Export", "Export", ExportPngSequence),
        new("automation.open", "Open Automation Workspace", "Automation", OpenAutomationWorkspace),
        new("mv.validate", "Validate Current MV Asset", "RPG Maker MV", ShowMvValidationReport)
    ];

    private bool TryExecuteConfiguredShortcut(Keys keyData)
    {
        string gesture = KeysToGesture(keyData);
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        if (IsTextInputFocused() && !gesture.Contains("Ctrl", StringComparison.OrdinalIgnoreCase) && !gesture.Contains("Alt", StringComparison.OrdinalIgnoreCase)) return false;
        SpriteShortcutBinding? binding = preferences.Shortcuts.FirstOrDefault(item => item.Enabled && SpriteShortcutRegistry.Normalize(item.Gesture).Equals(gesture, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return false;
        return ExecuteCommand(binding.Command);
    }

    private bool ExecuteCommand(string command)
    {
        switch (command.Trim().ToLowerInvariant())
        {
            case "file.new": NewDocument(); return true;
            case "file.open": OpenPng(); return true;
            case "file.save": SaveIntegratedProject(); return true;
            case "file.saveas": SavePackageAs(); return true;
            case "edit.undo": session.Undo(); return true;
            case "edit.redo": session.Redo(); return true;
            case "edit.copy": selectionPanel?.Copy(); return true;
            case "edit.cut": selectionPanel?.Cut(); return true;
            case "edit.paste": selectionPanel?.Paste(); return true;
            case "selection.all": selectionPanel?.SelectAll(); return true;
            case "selection.clear": selectionPanel?.Deselect(); return true;
            case "selection.invert": selectionPanel?.Invert(); return true;
            case "animation.play": timelinePanel?.TogglePlayback(); return true;
            case "frame.previous": timelinePanel?.PreviousFrame(); return true;
            case "frame.next": timelinePanel?.NextFrame(); return true;
            case "tool.pencil": SelectTool(PixelToolKind.Pencil); return true;
            case "tool.eraser": SelectTool(PixelToolKind.Eraser); return true;
            case "tool.line": SelectTool(PixelToolKind.Line); return true;
            case "tool.curve": SelectTool(PixelToolKind.Curve); return true;
            case "tool.rectangle": SelectTool(PixelToolKind.Rectangle); return true;
            case "tool.ellipse": SelectTool(PixelToolKind.Ellipse); return true;
            case "tool.polygon": SelectTool(PixelToolKind.Polygon); return true;
            case "tool.fill": SelectTool(PixelToolKind.Fill); return true;
            case "command.palette": ShowCommandPalette(); return true;
            case "view.fullscreencanvas": ToggleCanvasOnlyMode(); return true;
            case "view.resilience": OpenResilienceWorkspace(); return true;
            default: return false;
        }
    }

    private bool IsTextInputFocused()
    {
        Control? active = ActiveControl;
        while (active is ContainerControl container && container.ActiveControl is Control child) active = child;
        return active is TextBoxBase or ComboBox or NumericUpDown or DataGridView;
    }

    private static string KeysToGesture(Keys keyData)
    {
        List<string> parts = [];
        if ((keyData & Keys.Control) == Keys.Control) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) == Keys.Alt) parts.Add("Alt");
        if ((keyData & Keys.Shift) == Keys.Shift) parts.Add("Shift");
        Keys code = keyData & Keys.KeyCode;
        string key = code switch
        {
            Keys.Oemcomma => "Comma", Keys.OemPeriod => "Period", Keys.Oemplus => "Plus", Keys.OemMinus => "Minus",
            Keys.Escape => "Esc", Keys.Return => "Enter", Keys.Space => "Space", Keys.None => string.Empty,
            _ => code.ToString()
        };
        if (!string.IsNullOrWhiteSpace(key)) parts.Add(key.Length == 1 ? key.ToUpperInvariant() : key);
        return SpriteShortcutRegistry.Normalize(string.Join('+', parts));
    }

    private static PixelBuffer BitmapToPixelBuffer(Bitmap bitmap, int maximumDimension)
    {
        double scale = Math.Min(1d, maximumDimension / (double)Math.Max(bitmap.Width, bitmap.Height));
        int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        PixelBuffer result = new(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(bitmap.Width - 1, x * bitmap.Width / width);
                int sourceY = Math.Min(bitmap.Height - 1, y * bitmap.Height / height);
                Color color = bitmap.GetPixel(sourceX, sourceY);
                result.SetPixel(x, y, new Rgba32(color.R, color.G, color.B, color.A));
            }
        return result;
    }

    private static IReadOnlyList<Rgba32> BuildShadeRamp(Rgba32 first, Rgba32 second)
    {
        Rgba32 dark = Luminance(first) <= Luminance(second) ? first : second;
        Rgba32 light = dark == first ? second : first;
        return Enumerable.Range(0, 8).Select(index => Rgba32.Lerp(dark, light, index / 7d)).ToArray();
    }

    private static double Luminance(Rgba32 color) => color.R * .2126 + color.G * .7152 + color.B * .0722;

    private static string GetBrushPresetPath()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playroma", "jRPG", "SpriteStudio");
        return Path.Combine(directory, "brush-presets.json");
    }

    private static ComboBox DropDown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private static CheckBox Check(string text, bool value = false) => new() { Text = text, Checked = value, AutoSize = true, Dock = DockStyle.Fill };
    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, decimal increment = 1) => new() { Minimum = minimum, Maximum = maximum, Value = value, Increment = increment, Dock = DockStyle.Fill };
    private static T ParseEnum<T>(ComboBox box, T fallback) where T : struct, Enum => Enum.TryParse(box.SelectedItem?.ToString(), out T result) ? result : fallback;
    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum) => Math.Min(maximum, Math.Max(minimum, value));

    private static TableLayoutPanel PropertyTable() => new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        Padding = new Padding(8),
        GrowStyle = TableLayoutPanelGrowStyle.AddRows,
        ColumnStyles = { new ColumnStyle(SizeType.Percent, 44), new ColumnStyle(SizeType.Percent, 56) }
    };

    private static Control WrapScrollable(Control content)
    {
        Panel panel = new() { Dock = DockStyle.Fill, AutoScroll = true }; panel.Controls.Add(content); return panel;
    }

    private static void AddRow(TableLayoutPanel table, ref int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 6, 4, 4) }, 0, row);
        table.Controls.Add(control, 1, row); row++;
    }

    private static void AddFullRow(TableLayoutPanel table, ref int row, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2); row++;
    }

    private sealed record ProfileChoice(string Id, string DisplayName, RpgMakerMvImageProfile? Profile);
}
