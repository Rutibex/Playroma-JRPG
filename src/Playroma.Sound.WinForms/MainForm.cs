using System.Text.Json;
using System.Text.Json.Nodes;
using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Authoring.Projects;
using Playroma.Sound.Authoring.Services;
using Playroma.Sound.Authoring.Workspace;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.NAudio.Devices;
using Playroma.Sound.NAudio.Playback;
using Playroma.Sound.WinForms.Application;
using Playroma.Sound.WinForms.Controls;
using Playroma.Sound.WinForms.Dialogs;
using Playroma.Sound.WinForms.Models;
using Playroma.Sound.WinForms.Playback;
using Playroma.Sound.WinForms.Integration;

namespace Playroma.Sound.WinForms;

public sealed class MainForm : Form
{
    private readonly EditorApplicationPaths _paths;
    private readonly EditorSettingsStore _settingsStore;
    private readonly EditorSettings _settings;
    private readonly string[] _startupArguments;
    private readonly SoundEditorIntegrationOptions? _integrationOptions;
    private readonly RecentProjectStore _recentProjects;
    private readonly AuthoringAutosaveService _autosaveService;
    private readonly DocumentPreviewSession _preview = new();
    private readonly Dictionary<IAuthoringDocument, TabPage> _documentTabs = new(ReferenceEqualityComparer.Instance);
    private readonly System.Windows.Forms.Timer _telemetryTimer;

    private QuickSoundLabForm? _quickSoundLabForm;

    private readonly MenuStrip _menu;
    private readonly ToolStrip _mainTools;
    private readonly ToolStripMenuItem _recentMenu;
    private readonly ToolStripMenuItem _saveDocumentMenu;
    private readonly ToolStripMenuItem _saveAllMenu;
    private readonly ToolStripMenuItem _closeDocumentMenu;
    private readonly ToolStripMenuItem _undoMenu;
    private readonly ToolStripMenuItem _redoMenu;
    private readonly ToolStripMenuItem _validateDocumentMenu;
    private readonly ToolStripMenuItem _validateWorkspaceMenu;
    private readonly ToolStripMenuItem _compileMenu;
    private readonly ToolStripMenuItem _playMenu;
    private readonly ToolStripMenuItem _stopMenu;
    private readonly ToolStripMenuItem _exportMvMenu;
    private readonly ToolStripButton _saveButton;
    private readonly ToolStripButton _undoButton;
    private readonly ToolStripButton _redoButton;
    private readonly ToolStripButton _validateButton;
    private readonly ToolStripButton _compileButton;
    private readonly ToolStripButton _playButton;
    private readonly ToolStripButton _stopButton;
    private readonly ToolStripButton _makeSoundButton;
    private readonly ToolStripButton _tryAnotherButton;
    private readonly ToolStripButton _keepNextButton;
    private readonly ToolStripButton _exportMvButton;

    private readonly SplitContainer _mainSplit;
    private readonly SplitContainer _workspaceSplit;
    private readonly SplitContainer _documentSplit;
    private readonly ProjectExplorerControl _projectExplorer;
    private readonly TabControl _documentTabsControl;
    private readonly PropertyGrid _inspector;
    private readonly DiagnosticsControl _diagnostics;
    private readonly AnalysisProfilerControl _analysisProfiler;
    private readonly TabControl _bottomTabs;
    private readonly TransportControl _transport;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _statusLabel;
    private readonly ToolStripStatusLabel _projectStatusLabel;
    private readonly ToolStripStatusLabel _compileStatusLabel;

    private AuthoringWorkspace? _workspace;
    private AuthoringAutosaveCoordinator? _autosaveCoordinator;
    private CancellationTokenSource? _compileCancellation;
    private bool _shuttingDown;
    private bool _busy;

    public MainForm(EditorApplicationPaths paths, EditorSettingsStore settingsStore, EditorSettings settings, string[] startupArguments)
        : this(paths, settingsStore, settings, startupArguments, integrationOptions: null)
    {
    }

    public MainForm(
        EditorApplicationPaths paths,
        EditorSettingsStore settingsStore,
        EditorSettings settings,
        string[] startupArguments,
        SoundEditorIntegrationOptions? integrationOptions)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _startupArguments = startupArguments ?? [];
        _integrationOptions = integrationOptions;
        _paths.EnsureCreated();
        _recentProjects = new RecentProjectStore(_paths.RecentProjectsFilePath);
        _autosaveService = new AuthoringAutosaveService(_paths.AutosaveDirectory);

        Text = integrationOptions is null ? "Playroma Sound" : $"Playroma Sound Studio — {integrationOptions.HostProjectTitle}";
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.Manual;
        Bounds = RestoreWindowBounds(_settings);
        if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;

        _menu = new MenuStrip();
        MainMenuStrip = _menu;
        ToolStripMenuItem fileMenu = new("&File");
        fileMenu.DropDownItems.Add(Item("&New Project…", Keys.Control | Keys.Shift | Keys.N, (_, _) => NewProject()));
        fileMenu.DropDownItems.Add(Item("&Open Project…", Keys.Control | Keys.O, (_, _) => OpenProjectDialog()));
        _recentMenu = new ToolStripMenuItem("Open &Recent");
        fileMenu.DropDownItems.Add(_recentMenu);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(Item("New Sound &Graph…", Keys.Control | Keys.Alt | Keys.G, (_, _) => NewAsset(AuthoringDocumentKind.SoundGraph)));
        fileMenu.DropDownItems.Add(Item("New Generator &Request…", Keys.Control | Keys.Alt | Keys.R, (_, _) => NewAsset(AuthoringDocumentKind.GeneratorRequest)));
        fileMenu.DropDownItems.Add(Item("New Streaming &Scene…", Keys.Control | Keys.Alt | Keys.S, (_, _) => NewAsset(AuthoringDocumentKind.StreamingScene)));
        fileMenu.DropDownItems.Add(Item("Add Existing Asset…", Keys.None, (_, _) => AddExistingAsset()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        _saveDocumentMenu = Item("&Save Document", Keys.Control | Keys.S, (_, _) => SaveActiveDocument());
        _saveAllMenu = Item("Save A&ll", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveAll());
        fileMenu.DropDownItems.Add(_saveDocumentMenu);
        fileMenu.DropDownItems.Add(_saveAllMenu);
        fileMenu.DropDownItems.Add(Item("Save Project &As…", Keys.None, (_, _) => SaveProjectAs()));
        fileMenu.DropDownItems.Add(Item("&Export Project…", Keys.Control | Keys.E, (_, _) => ExportProject()));
        _exportMvMenu = Item(
            "Export Active Sound to RPG Maker &MV…",
            Keys.Control | Keys.Shift | Keys.E,
            async (_, _) => await ExportActiveSoundToMvAsync());
        _exportMvMenu.Visible = _integrationOptions is not null;
        fileMenu.DropDownItems.Add(_exportMvMenu);
        _closeDocumentMenu = Item("Close Document", Keys.Control | Keys.W, (_, _) => CloseActiveDocument());
        fileMenu.DropDownItems.Add(_closeDocumentMenu);
        fileMenu.DropDownItems.Add(Item("Close Project", Keys.Control | Keys.Shift | Keys.W, (_, _) => CloseWorkspaceWithPrompt()));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(Item(_integrationOptions is null ? "E&xit" : "&Close Sound Studio", Keys.Alt | Keys.F4, (_, _) => RequestClose()));

        ToolStripMenuItem soundMenu = new("&Sound");
        soundMenu.DropDownItems.Add(Item("&Make a New Sound", Keys.Control | Keys.N, (_, _) => CreateQuickSound()));
        soundMenu.DropDownItems.Add(Item("&Try Another Variation", Keys.F4, (_, _) => TryAnotherQuickSound()));
        soundMenu.DropDownItems.Add(Item("&Keep This Sound + Next", Keys.Control | Keys.K, (_, _) => KeepQuickSoundAndContinue()));
        soundMenu.DropDownItems.Add(new ToolStripSeparator());
        soundMenu.DropDownItems.Add(Item("&Surprise Me", Keys.Control | Keys.Shift | Keys.R, (_, _) => SurpriseQuickSound()));
        if (_integrationOptions is not null)
        {
            soundMenu.DropDownItems.Add(new ToolStripSeparator());
            soundMenu.DropDownItems.Add(Item("Export Current Sound to RPG Maker MV…", Keys.None, async (_, _) => await ExportActiveSoundToMvAsync()));
        }

        ToolStripMenuItem editMenu = new("&Edit");
        _undoMenu = Item("&Undo", Keys.Control | Keys.Z, (_, _) => Undo());
        _redoMenu = Item("&Redo", Keys.Control | Keys.Y, (_, _) => Redo());
        editMenu.DropDownItems.Add(_undoMenu);
        editMenu.DropDownItems.Add(_redoMenu);
        editMenu.DropDownItems.Add(new ToolStripSeparator());
        editMenu.DropDownItems.Add(Item("Apply JSON Edits", Keys.Control | Keys.Enter, (_, _) => ApplyActiveEditor()));
        editMenu.DropDownItems.Add(Item("Format JSON", Keys.Control | Keys.Shift | Keys.F, (_, _) => FormatActiveEditor()));

        ToolStripMenuItem buildMenu = new("&Build");
        _validateDocumentMenu = Item("Validate Active Document", Keys.F6, (_, _) => ValidateActiveDocument());
        _validateWorkspaceMenu = Item("Validate Workspace", Keys.Control | Keys.F6, (_, _) => ValidateWorkspace());
        _compileMenu = Item("Compile Active Document", Keys.F7, async (_, _) => await CompileActiveDocumentAsync());
        buildMenu.DropDownItems.Add(_validateDocumentMenu);
        buildMenu.DropDownItems.Add(_validateWorkspaceMenu);
        buildMenu.DropDownItems.Add(new ToolStripSeparator());
        buildMenu.DropDownItems.Add(_compileMenu);

        ToolStripMenuItem playbackMenu = new("&Playback");
        _playMenu = Item("&Play Active Document", Keys.F5, async (_, _) => await PlayActiveDocumentAsync());
        _stopMenu = Item("&Stop", Keys.Shift | Keys.F5, (_, _) => StopPlayback());
        playbackMenu.DropDownItems.Add(_playMenu);
        playbackMenu.DropDownItems.Add(_stopMenu);
        playbackMenu.DropDownItems.Add(Item("Record Master Output…", Keys.None, (_, _) => ToggleRecording()));
        playbackMenu.DropDownItems.Add(new ToolStripSeparator());
        playbackMenu.DropDownItems.Add(Item("Refresh Audio Devices", Keys.None, (_, _) => LoadAudioDevices()));

        ToolStripMenuItem viewMenu = new("&View");
        viewMenu.DropDownItems.Add(Item("Focus Project Explorer", Keys.Control | Keys.Alt | Keys.P, (_, _) => FocusProjectExplorer()));
        viewMenu.DropDownItems.Add(Item("Focus Inspector", Keys.Control | Keys.Alt | Keys.I, (_, _) => FocusInspector()));
        viewMenu.DropDownItems.Add(Item("Focus Diagnostics", Keys.Control | Keys.Alt | Keys.D, (_, _) => FocusBottomTab(0)));
        viewMenu.DropDownItems.Add(Item("Focus Analysis and Profiler", Keys.Control | Keys.Alt | Keys.A, (_, _) => FocusBottomTab(1)));

        ToolStripMenuItem helpMenu = new("&Help");
        helpMenu.DropDownItems.Add(Item("&About Playroma Sound", Keys.None, (_, _) => new AboutDialog().ShowDialog(this)));
        _menu.Items.AddRange([fileMenu, soundMenu, editMenu, buildMenu, playbackMenu, viewMenu, helpMenu]);

        _mainTools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        _makeSoundButton = Button("MAKE SOUND", (_, _) => CreateQuickSound());
        _tryAnotherButton = Button("TRY ANOTHER", (_, _) => TryAnotherQuickSound());
        _keepNextButton = Button("KEEP + NEXT", (_, _) => KeepQuickSoundAndContinue());
        _mainTools.Items.Add(_makeSoundButton);
        _mainTools.Items.Add(_tryAnotherButton);
        _mainTools.Items.Add(_keepNextButton);
        _mainTools.Items.Add(new ToolStripSeparator());
        _mainTools.Items.Add(Button("New Project", (_, _) => NewProject()));
        _mainTools.Items.Add(Button("Open", (_, _) => OpenProjectDialog()));
        _saveButton = Button("Save", (_, _) => SaveActiveDocument());
        _mainTools.Items.Add(_saveButton);
        _exportMvButton = Button("Export to MV", async (_, _) => await ExportActiveSoundToMvAsync());
        _exportMvButton.ToolTipText = "Render the active sound into the open RPG Maker MV project's audio database";
        _exportMvButton.Visible = _integrationOptions is not null;
        _mainTools.Items.Add(_exportMvButton);
        _mainTools.Items.Add(new ToolStripSeparator());
        _undoButton = Button("Undo", (_, _) => Undo());
        _redoButton = Button("Redo", (_, _) => Redo());
        _mainTools.Items.Add(_undoButton);
        _mainTools.Items.Add(_redoButton);
        _mainTools.Items.Add(new ToolStripSeparator());
        _validateButton = Button("Validate", (_, _) => ValidateActiveDocument());
        _compileButton = Button("Compile", async (_, _) => await CompileActiveDocumentAsync());
        _playButton = Button("Play", async (_, _) => await PlayActiveDocumentAsync());
        _stopButton = Button("Stop", (_, _) => StopPlayback());
        _mainTools.Items.Add(_validateButton);
        _mainTools.Items.Add(_compileButton);
        _mainTools.Items.Add(_playButton);
        _mainTools.Items.Add(_stopButton);

        _projectExplorer = new ProjectExplorerControl();
        _documentTabsControl = new TabControl { Dock = DockStyle.Fill, HotTrack = true };
        _inspector = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = true };
        _diagnostics = new DiagnosticsControl();
        _analysisProfiler = new AnalysisProfilerControl();
        _bottomTabs = new TabControl { Dock = DockStyle.Fill };
        var diagnosticsPage = new TabPage("Diagnostics");
        diagnosticsPage.Controls.Add(_diagnostics);
        var analysisPage = new TabPage("Analysis & Profiler");
        analysisPage.Controls.Add(_analysisProfiler);
        _bottomTabs.TabPages.Add(diagnosticsPage);
        _bottomTabs.TabPages.Add(analysisPage);
        _transport = new TransportControl();

        _documentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1200, 700),
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 240,
            Panel2MinSize = 120
        };
        _documentSplit.Panel1.Controls.Add(_documentTabsControl);
        _documentSplit.Panel2.Controls.Add(_bottomTabs);

        _workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1200, 700),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            Panel1MinSize = 420,
            Panel2MinSize = 220
        };
        _workspaceSplit.Panel1.Controls.Add(_documentSplit);
        _workspaceSplit.Panel2.Controls.Add(_inspector);

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1200, 700),
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 180,
            Panel2MinSize = 640
        };
        _mainSplit.Panel1.Controls.Add(_projectExplorer);
        _mainSplit.Panel2.Controls.Add(_workspaceSplit);

        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _projectStatusLabel = new ToolStripStatusLabel("No project");
        _compileStatusLabel = new ToolStripStatusLabel("");
        _statusStrip.Items.AddRange([_statusLabel, _compileStatusLabel, _projectStatusLabel]);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_menu, 0, 0);
        root.Controls.Add(_mainTools, 0, 1);
        root.Controls.Add(_mainSplit, 0, 2);
        root.Controls.Add(_transport, 0, 3);
        root.Controls.Add(_statusStrip, 0, 4);
        Controls.Add(root);

        _projectExplorer.AssetActivated += (_, eventArgs) => OpenAsset(eventArgs.AssetId);
        _projectExplorer.SelectedAssetChanged += (_, eventArgs) =>
        {
            if (_workspace is null) _inspector.SelectedObject = null;
            else _inspector.SelectedObject = eventArgs.Asset is null ? new ProjectInspectorModel(_workspace) : new AssetInspectorModel(eventArgs.Asset, _workspace);
        };
        _documentTabsControl.SelectedIndexChanged += (_, _) => OnActiveTabChanged();
        _documentTabsControl.MouseUp += OnDocumentTabsMouseUp;
        _diagnostics.DiagnosticActivated += (_, eventArgs) => ActivateDiagnostic(eventArgs.Diagnostic);
        _transport.PlayRequested += async (_, _) => await PlayActiveDocumentAsync();
        _transport.StopRequested += (_, _) => StopPlayback();
        _transport.RecordRequested += (_, _) => ToggleRecording();
        _transport.GainChanged += (_, eventArgs) =>
        {
            _settings.MasterGain = eventArgs.Gain;
            _preview.MasterGain = eventArgs.Gain;
        };
        _transport.DeviceSelectionChanged += (_, eventArgs) => ChangeAudioDevice(eventArgs.Selection);
        _preview.StateChanged += (_, _) => BeginInvoke((Action)UpdateCommandState);
        _preview.Faulted += (_, eventArgs) => BeginInvoke((Action)(() =>
        {
            _statusLabel.Text = eventArgs.Exception.Message;
            MessageBox.Show(this, eventArgs.Exception.Message, "Audio playback fault", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }));

        _telemetryTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _telemetryTimer.Tick += (_, _) =>
        {
            _transport.UpdateTelemetry(_preview.Telemetry);
            if (_preview.IsPlaying) _preview.CaptureProfilerSample();
            _analysisProfiler.UpdateFrom(_preview);
            UpdateCommandState();
        };
        _telemetryTimer.Start();
        _transport.SetGain(_settings.MasterGain);
        _preview.MasterGain = _settings.MasterGain;

        Load += OnMainFormLoad;
        Shown += (_, _) => ApplySplitterSettings();
        FormClosing += OnMainFormClosing;
        ShowWelcomeTab();
        RefreshRecentProjectsMenu();
        UpdateCommandState();
    }

    public AuthoringWorkspace? Workspace => _workspace;
    public IAuthoringDocument? ActiveDocument => ActiveEditor?.Document;
    public bool HasUnsavedChanges => _workspace?.HasUnsavedChanges == true
        || _documentTabs.Values.Select(tab => tab.Tag).OfType<IAuthoringEditorControl>().Any(editor => editor.HasUnappliedChanges)
        || _quickSoundLabForm?.CurrentEditor?.HasUnappliedChanges == true;
    private IAuthoringEditorControl? ActiveEditor => _documentTabsControl.SelectedTab?.Tag as IAuthoringEditorControl;
    private GeneratorRequestEditorControl? CurrentQuickSoundEditor => _quickSoundLabForm?.CurrentEditor ?? ActiveEditor as GeneratorRequestEditorControl;

    private void OnMainFormLoad(object? sender, EventArgs eventArgs)
    {
        LoadAudioDevices();
        string? startupProject = _startupArguments.FirstOrDefault(path => path.EndsWith(".playproject", StringComparison.OrdinalIgnoreCase) && File.Exists(path));
        if (startupProject is null && !string.IsNullOrWhiteSpace(_settings.LastProjectPath) && File.Exists(_settings.LastProjectPath))
            startupProject = _settings.LastProjectPath;
        if (startupProject is not null) OpenWorkspace(startupProject);
    }

    private void FocusProjectExplorer() => _projectExplorer.Focus();

    private void FocusInspector() => _inspector.Focus();

    private void FocusBottomTab(int index)
    {
        if (index < 0 || index >= _bottomTabs.TabPages.Count) return;
        _bottomTabs.SelectedIndex = index;
        _bottomTabs.TabPages[index].Controls.Cast<Control>().FirstOrDefault()?.Focus();
    }

    private void ExportProject()
    {
        if (_workspace is null) return;
        if (!CommitAllEditors()) return;
        using var dialog = new ExportProjectDialog(_workspace);
        dialog.ShowDialog(this);
        if (!string.IsNullOrEmpty(dialog.CompletedOutputRoot)) _statusLabel.Text = $"Export complete: {dialog.CompletedOutputRoot}";
    }

    private async Task ExportActiveSoundToMvAsync()
    {
        if (_integrationOptions is null)
        {
            MessageBox.Show(this, "RPG Maker MV export is available when Playroma Sound is opened from Playroma jRPG.",
                "RPG Maker MV Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (_workspace is null)
        {
            MessageBox.Show(this, "Create or open a Playroma Sound project first.",
                "RPG Maker MV Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        IAuthoringDocument? document = _quickSoundLabForm is { Visible: true, CurrentDocument: not null }
            ? _quickSoundLabForm.CurrentDocument
            : ActiveDocument ?? _quickSoundLabForm?.CurrentDocument;
        if (document is null)
        {
            MessageBox.Show(this, "Open or create a sound, generator request, or streaming scene first.",
                "RPG Maker MV Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!CommitAllEditors()) return;

        using var dialog = new MvSoundExportDialog(document.DisplayName, document.Kind);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string category = dialog.DatabaseKind.ToString().ToLowerInvariant();
        string outputDirectory = Path.Combine(_integrationOptions.NormalizedProjectRoot, "audio", category);
        string[] proposed = dialog.Formats switch
        {
            MvSoundExportFormats.Both =>
            [
                Path.Combine(outputDirectory, dialog.FileNameValue + ".ogg"),
                Path.Combine(outputDirectory, dialog.FileNameValue + ".m4a")
            ],
            MvSoundExportFormats.Ogg => [Path.Combine(outputDirectory, dialog.FileNameValue + ".ogg")],
            MvSoundExportFormats.M4A => [Path.Combine(outputDirectory, dialog.FileNameValue + ".m4a")],
            _ => []
        };
        string[] existing = proposed.Where(File.Exists).ToArray();
        if (existing.Length > 0)
        {
            DialogResult overwrite = MessageBox.Show(
                this,
                "The following RPG Maker MV audio file(s) already exist and will be replaced:\n\n" +
                string.Join("\n", existing.Select(Path.GetFileName)),
                "Replace Existing Audio?",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (overwrite != DialogResult.OK) return;
        }

        using var cancellation = new CancellationTokenSource();
        using var progressDialog = new Form
        {
            Text = "Exporting Playroma Sound",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(430, 108),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ControlBox = false,
            ShowInTaskbar = false
        };
        ProgressBar progressBar = new() { Dock = DockStyle.Top, Height = 24, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous };
        Label progressLabel = new() { Dock = DockStyle.Top, Height = 36, Text = "Compiling and rendering…", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 10, 0) };
        Button cancelButton = new() { Text = "Cancel", Dock = DockStyle.Right, Width = 88 };
        Panel buttonPanel = new() { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(0, 4, 8, 4) };
        cancelButton.Click += (_, _) => { cancelButton.Enabled = false; progressLabel.Text = "Cancelling…"; cancellation.Cancel(); };
        buttonPanel.Controls.Add(cancelButton);
        progressDialog.Controls.Add(buttonPanel);
        progressDialog.Controls.Add(progressBar);
        progressDialog.Controls.Add(progressLabel);

        var progress = new Progress<double>(value =>
        {
            int percent = Math.Clamp((int)Math.Round(value * 100), 0, 100);
            progressBar.Value = percent;
            progressLabel.Text = percent < 60 ? "Compiling and rendering…" : percent < 90 ? "Encoding RPG Maker MV audio…" : "Writing project metadata…";
        });

        SetBusy(true, $"Exporting {document.DisplayName} to RPG Maker MV…");
        try
        {
            Task<MvSoundExportResult> exportTask = new MvSoundExportService().ExportAsync(
                _workspace,
                document,
                new MvSoundExportRequest(
                    _integrationOptions.NormalizedProjectRoot,
                    dialog.FileNameValue,
                    dialog.DatabaseKind,
                    dialog.Formats,
                    dialog.MaximumDurationSeconds,
                    dialog.SaveAuthoringSource,
                    dialog.AacBitRate,
                    dialog.VorbisQuality),
                cancellation.Token,
                progress);

            progressDialog.Shown += async (_, _) =>
            {
                try { await exportTask.ConfigureAwait(true); }
                catch { /* The caller below reports the failure after the modal progress window closes. */ }
                finally { if (!progressDialog.IsDisposed) progressDialog.Close(); }
            };
            progressDialog.ShowDialog(this);
            MvSoundExportResult result = await exportTask.ConfigureAwait(true);
            _diagnostics.SetDiagnostics(result.Compilation.Validation.Diagnostics);
            _statusLabel.Text = $"Exported {document.DisplayName} to audio/{category}";
            MessageBox.Show(
                this,
                $"Playroma Sound exported successfully.\n\n" +
                $"Duration: {result.RenderedDuration.TotalSeconds:0.###} seconds\n" +
                $"Audio files:\n{string.Join("\n", result.AudioFiles.Select(path => "  " + Path.GetRelativePath(_integrationOptions.NormalizedProjectRoot, path)))}" +
                (result.AuthoringSource is null ? string.Empty : $"\n\nEditable source:\n  {Path.GetRelativePath(_integrationOptions.NormalizedProjectRoot, result.AuthoringSource)}"),
                "RPG Maker MV Export Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "RPG Maker MV audio export cancelled";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception) || exception is InvalidOperationException)
        {
            ShowError("RPG Maker MV audio export failed", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RequestClose()
    {
        if (_integrationOptions?.RequestHostClose is Action requestHostClose) requestHostClose();
        else Close();
    }

    private void NewProject()
    {
        using var dialog = new NewProjectDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!PrepareToReplaceWorkspace()) return;
        try
        {
            Directory.CreateDirectory(dialog.ProjectDirectory);
            AuthoringWorkspace workspace = AuthoringWorkspace.Create(dialog.ProjectFilePath, dialog.ProjectId, dialog.ProjectDisplayName);
            AttachWorkspace(workspace);
            _statusLabel.Text = $"Created {workspace.Project.DisplayName}";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not create project", exception);
        }
    }

    private void OpenProjectDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Playroma Sound project (*.playproject)|*.playproject|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Open Playroma Sound Project"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenWorkspace(dialog.FileName);
    }

    private void OpenWorkspace(string projectFilePath)
    {
        if (!PrepareToReplaceWorkspace()) return;
        try
        {
            AttachWorkspace(AuthoringWorkspace.Open(projectFilePath));
            RecoverAutosaves();
            _statusLabel.Text = $"Opened {_workspace!.Project.DisplayName}";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not open project", exception);
        }
    }

    private void AttachWorkspace(AuthoringWorkspace workspace)
    {
        DetachWorkspace();
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _settings.LastProjectPath = workspace.ProjectFilePath;
        _recentProjects.Touch(workspace.ProjectFilePath, workspace.Project.DisplayName);
        RefreshRecentProjectsMenu();
        _projectExplorer.SetWorkspace(workspace);
        _documentTabsControl.TabPages.Clear();
        _documentTabs.Clear();
        _diagnostics.Clear();
        _inspector.SelectedObject = new ProjectInspectorModel(workspace);
        StartAutosave(workspace);
        UpdateTitleAndState();
    }

    private void DetachWorkspace()
    {
        StopPlayback();
        StopAutosave();
        _compileCancellation?.Cancel();
        _compileCancellation?.Dispose();
        _compileCancellation = null;
        foreach ((IAuthoringDocument document, TabPage tab) in _documentTabs.ToArray())
        {
            document.Changed -= OnOpenDocumentChanged;
            if (tab.Tag is IAuthoringEditorControl editor && editor is IDisposable disposable) disposable.Dispose();
            tab.Dispose();
        }
        _documentTabs.Clear();
        _documentTabsControl.TabPages.Clear();
        if (_quickSoundLabForm is not null)
        {
            _quickSoundLabForm.Reset();
            _quickSoundLabForm.Dispose();
            _quickSoundLabForm = null;
        }
        _workspace?.Dispose();
        _workspace = null;
        _projectExplorer.SetWorkspace(null);
        _diagnostics.Clear();
        _inspector.SelectedObject = null;
        ShowWelcomeTab();
        UpdateTitleAndState();
    }

    private bool PrepareToReplaceWorkspace()
    {
        if (_workspace is null) return true;
        if (!PrepareUnappliedEditors()) return false;
        if (_workspace.HasUnsavedChanges)
        {
            DialogResult result = MessageBox.Show(this, "Save changes to the current project before closing it?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.Yes && !SaveAll()) return false;
        }
        DetachWorkspace();
        return true;
    }

    private void CloseWorkspaceWithPrompt()
    {
        if (!PrepareToReplaceWorkspace()) return;
        ShowWelcomeTab();
    }

    private GeneratorRequestDocument? CreateQuickSound(
        string generatorId = "playroma.ui",
        JsonObject? description = null,
        SoundQualityMode qualityMode = SoundQualityMode.RealtimeHigh,
        bool autoPlay = true)
    {
        if (_workspace is null)
        {
            MessageBox.Show(this, "Create or open a project first. Then MAKE SOUND will create a ready-to-audition sound immediately.",
                "Make a Sound", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        try
        {
            int number = 1;
            string id;
            string relativePath;
            do
            {
                id = $"sound.quick.{number:0000}";
                relativePath = $"Generators/quick-sound-{number:0000}.playgen";
                number++;
            }
            while (_workspace.Project.Assets.Any(item => StringComparer.Ordinal.Equals(item.Id, id)));

            string familyName;
            try { familyName = SoundGeneratorCatalog.CreateDefault().GetRequired(generatorId).Descriptor.DisplayName; }
            catch (KeyNotFoundException) { generatorId = "playroma.ui"; familyName = "Interface Sound"; }

            var document = _workspace.CreateGeneratorRequest(id, $"New {familyName}", generatorId, relativePath);
            document.Mutate("Initialize quick sound", model =>
            {
                model.RootSeed = NewQuickSoundSeed();
                model.FamilySeed = NewQuickSoundSeed();
                model.VariationIndex = 0;
                model.QualityMode = qualityMode;
                if (description is not null) model.Description = description.DeepClone() as JsonObject ?? new JsonObject();
            });
            OpenDocument(document, autoPlay);
            _projectExplorer.RefreshTree();
            UpdateTitleAndState();
            _statusLabel.Text = "New sound ready — listen, keep it, or try another";
            return document;
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not create a quick sound", exception);
            return null;
        }
    }

    private void TryAnotherQuickSound()
    {
        if (CurrentQuickSoundEditor is GeneratorRequestEditorControl quickEditor) quickEditor.TryAnother();
        else CreateQuickSound();
    }

    private void KeepQuickSoundAndContinue()
    {
        if (CurrentQuickSoundEditor is GeneratorRequestEditorControl quickEditor) quickEditor.KeepAndContinue();
        else MessageBox.Show(this, "Open a generated sound in the Sound Lab first.", "Keep Sound", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SurpriseQuickSound()
    {
        if (CurrentQuickSoundEditor is GeneratorRequestEditorControl quickEditor) quickEditor.SurpriseMe();
        else CreateQuickSound();
    }

    private static ulong NewQuickSoundSeed() => BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);

    private void NewAsset(AuthoringDocumentKind kind)
    {
        if (_workspace is null) return;
        using var dialog = new NewAssetDialog(kind);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            IAuthoringDocument document = dialog.DocumentKind switch
            {
                AuthoringDocumentKind.SoundGraph => _workspace.CreateGraph(dialog.AssetId, dialog.DisplayNameValue, dialog.RelativePath),
                AuthoringDocumentKind.GeneratorRequest => _workspace.CreateGeneratorRequest(dialog.AssetId, dialog.DisplayNameValue, dialog.GeneratorId, dialog.RelativePath),
                AuthoringDocumentKind.StreamingScene => _workspace.CreateScene(dialog.AssetId, dialog.DisplayNameValue, dialog.RelativePath),
                _ => throw new NotSupportedException($"Document kind {dialog.DocumentKind} is not supported.")
            };
            OpenDocument(document);
            _projectExplorer.RefreshTree();
            UpdateTitleAndState();
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not create asset", exception);
        }
    }

    private void AddExistingAsset()
    {
        if (_workspace is null) return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Playroma Sound assets (*.playsound;*.playgen;*.playstream)|*.playsound;*.playgen;*.playstream|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Add Existing Playroma Sound Asset"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AuthoringAssetKind kind = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
            {
                ".playsound" => AuthoringAssetKind.SoundGraph,
                ".playgen" => AuthoringAssetKind.GeneratorRequest,
                ".playstream" => AuthoringAssetKind.StreamingScene,
                _ => throw new NotSupportedException("Only .playsound, .playgen, and .playstream files can be imported by this command.")
            };
            IAuthoringDocument loaded = kind switch
            {
                AuthoringAssetKind.SoundGraph => SoundGraphDocument.Load(dialog.FileName),
                AuthoringAssetKind.GeneratorRequest => GeneratorRequestDocument.Load(dialog.FileName),
                AuthoringAssetKind.StreamingScene => StreamingSceneDocument.Load(dialog.FileName),
                _ => throw new NotSupportedException()
            };
            string relativePath = CopyIntoProjectIfRequired(dialog.FileName, kind);
            _workspace.AddExistingAsset(loaded.DocumentId, loaded.DisplayName, kind, relativePath);
            _workspace.SaveProject();
            _projectExplorer.RefreshTree();
            OpenAsset(loaded.DocumentId);
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not add asset", exception);
        }
    }

    private string CopyIntoProjectIfRequired(string sourcePath, AuthoringAssetKind kind)
    {
        if (_workspace is null) throw new InvalidOperationException("No workspace is open.");
        string source = Path.GetFullPath(sourcePath);
        string relative = Path.GetRelativePath(_workspace.ProjectRoot, source);
        if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && relative != ".." && !Path.IsPathRooted(relative))
            return relative.Replace('\\', '/');

        string folder = kind switch
        {
            AuthoringAssetKind.SoundGraph => "Sounds",
            AuthoringAssetKind.GeneratorRequest => "Generators",
            AuthoringAssetKind.StreamingScene => "StreamingScenes",
            _ => "Assets"
        };
        string destinationDirectory = Path.Combine(_workspace.ProjectRoot, folder);
        Directory.CreateDirectory(destinationDirectory);
        string destination = Path.Combine(destinationDirectory, Path.GetFileName(source));
        if (File.Exists(destination))
        {
            string name = Path.GetFileNameWithoutExtension(source);
            string extension = Path.GetExtension(source);
            int suffix = 2;
            do destination = Path.Combine(destinationDirectory, $"{name}-{suffix++}{extension}");
            while (File.Exists(destination));
        }
        File.Copy(source, destination);
        return Path.GetRelativePath(_workspace.ProjectRoot, destination).Replace('\\', '/');
    }

    private void OpenAsset(string assetId)
    {
        if (_workspace is null) return;
        try
        {
            ProjectAssetEntry asset = _workspace.GetRequiredAsset(assetId);
            if (asset.Kind is not (AuthoringAssetKind.SoundGraph or AuthoringAssetKind.GeneratorRequest or AuthoringAssetKind.StreamingScene))
            {
                string path = Path.GetFullPath(Path.Combine(_workspace.ProjectRoot, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }
            OpenDocument(_workspace.OpenDocument(assetId));
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not open asset", exception);
        }
    }

    private void OpenDocument(IAuthoringDocument document, bool autoPlay = false)
    {
        if (document is GeneratorRequestDocument generatorDocument)
        {
            OpenQuickSoundLab(generatorDocument, autoPlay);
            return;
        }

        if (_documentTabs.TryGetValue(document, out TabPage? existing))
        {
            _documentTabsControl.SelectedTab = existing;
            return;
        }
        RemoveWelcomeTab();
        IAuthoringEditorControl editor = document switch
        {
            StreamingSceneDocument sceneDocument => new StreamingSceneEditorControl(sceneDocument, _preview),
            SoundGraphDocument graphDocument => new SoundGraphEditorControl(graphDocument, _workspace ?? throw new InvalidOperationException("Open a project before opening graph documents."), _preview),
            GeneratorRequestDocument fallbackGeneratorDocument => new GeneratorRequestEditorControl(fallbackGeneratorDocument),
            _ => new DocumentEditorControl(document)
        };
        var editorControl = (Control)editor;
        var tab = new TabPage(DocumentTabTitle(document)) { Tag = editor, ToolTipText = document.FilePath };
        tab.Controls.Add(editorControl);
        _documentTabs.Add(document, tab);
        _documentTabsControl.TabPages.Add(tab);
        _documentTabsControl.SelectedTab = tab;

        editor.ValidateRequested += (_, eventArgs) => ValidateDocument(eventArgs.Document);
        editor.CompileRequested += async (_, eventArgs) => await CompileDocumentAsync(eventArgs.Document);
        editor.PlayRequested += async (_, eventArgs) => await PlayDocumentAsync(eventArgs.Document);
        editor.EditorStateChanged += (_, _) =>
        {
            UpdateTabTitle(document);
            _projectExplorer.RefreshTree();
            UpdateTitleAndState();
        };
        document.Changed += OnOpenDocumentChanged;
        ValidateDocument(document);
        UpdateTitleAndState();
    }

    private void OpenQuickSoundLab(GeneratorRequestDocument document, bool autoPlay)
    {
        if (_workspace is null) return;

        _quickSoundLabForm ??= new QuickSoundLabForm(
            eventArgs => CreateQuickSound(eventArgs.GeneratorId, eventArgs.Description, eventArgs.QualityMode, false),
            ValidateDocument,
            CompileDocumentAsync,
            PlayDocumentAsync,
            currentDocument =>
            {
                _projectExplorer.RefreshTree();
                _inspector.SelectedObject = new DocumentInspectorModel(currentDocument);
                UpdateTitleAndState();
            });

        _quickSoundLabForm.Owner = this;
        _quickSoundLabForm.LoadDocument(document, autoPlay);
        _projectExplorer.RefreshTree();
        _inspector.SelectedObject = new DocumentInspectorModel(document);
        UpdateTitleAndState();
    }

    private void OnOpenDocumentChanged(object? sender, AuthoringDocumentChangedEventArgs eventArgs)
    {
        if (sender is not IAuthoringDocument document || IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => OnOpenDocumentChanged(sender, eventArgs)));
            return;
        }
        UpdateTabTitle(document);
        _projectExplorer.RefreshTree();
        if (ReferenceEquals(ActiveDocument, document)) _inspector.SelectedObject = new DocumentInspectorModel(document);
        UpdateTitleAndState();
    }

    private void CloseActiveDocument()
    {
        if (_quickSoundLabForm is { Visible: true, CurrentDocument: not null })
        {
            _quickSoundLabForm.Hide();
            return;
        }

        if (ActiveDocument is IAuthoringDocument document) CloseDocument(document);
    }

    private bool CloseDocument(IAuthoringDocument document)
    {
        if (_workspace is null || !_documentTabs.TryGetValue(document, out TabPage? tab)) return true;
        IAuthoringEditorControl? editor = tab.Tag as IAuthoringEditorControl;
        if (editor?.HasUnappliedChanges == true)
        {
            DialogResult apply = MessageBox.Show(this, $"Apply the JSON edits in '{document.DisplayName}' before closing?", "Unapplied edits", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (apply == DialogResult.Cancel) return false;
            if (apply == DialogResult.Yes && !editor.TryApplyEdits()) return false;
        }
        if (document.IsDirty)
        {
            DialogResult save = MessageBox.Show(this, $"Save changes to '{document.DisplayName}'?", "Unsaved document", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (save == DialogResult.Cancel) return false;
            if (save == DialogResult.Yes)
            {
                try { document.Save(); }
                catch (Exception exception) when (IsExpectedEditorException(exception)) { ShowError("Could not save document", exception); return false; }
            }
        }
        if (_preview.IsPreviewing(document)) StopPlayback();
        document.Changed -= OnOpenDocumentChanged;
        _workspace.CloseDocument(document.DocumentId, true);
        _documentTabs.Remove(document);
        _documentTabsControl.TabPages.Remove(tab);
        if (editor is IDisposable disposable) disposable.Dispose();
        tab.Dispose();
        if (_documentTabsControl.TabPages.Count == 0) ShowProjectOverviewTab();
        _projectExplorer.RefreshTree();
        UpdateTitleAndState();
        return true;
    }

    private void SaveActiveDocument()
    {
        IAuthoringEditorControl? editor = ActiveEditor ?? _quickSoundLabForm?.CurrentEditor;
        if (editor is null) return;
        if (!editor.TryApplyEdits()) return;
        try
        {
            editor.Document.Save();
            if (_workspace is not null) _autosaveService.Delete(_workspace.Project.Id, editor.Document.DocumentId);
            editor.RefreshFromDocument(true);
            _statusLabel.Text = $"Saved {editor.Document.DisplayName}";
            UpdateTitleAndState();
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not save document", exception);
        }
    }

    private bool SaveAll()
    {
        if (_workspace is null) return true;
        if (!CommitAllEditors()) return false;
        try
        {
            _workspace.SaveAll();
            foreach (IAuthoringDocument document in _workspace.OpenDocuments)
                _autosaveService.Delete(_workspace.Project.Id, document.DocumentId);
            foreach (TabPage tab in _documentTabsControl.TabPages)
                if (tab.Tag is IAuthoringEditorControl editor) editor.RefreshFromDocument(true);
            _projectExplorer.RefreshTree();
            _statusLabel.Text = "Saved all project documents";
            UpdateTitleAndState();
            return true;
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not save project", exception);
            return false;
        }
    }

    private void SaveProjectAs()
    {
        if (_workspace is null || !CommitAllEditors()) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "Playroma Sound project (*.playproject)|*.playproject",
            AddExtension = true,
            DefaultExt = "playproject",
            FileName = Path.GetFileName(_workspace.ProjectFilePath),
            InitialDirectory = _workspace.ProjectRoot,
            Title = "Save Playroma Sound Project As"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _workspace.SaveProjectAs(dialog.FileName);
            _settings.LastProjectPath = _workspace.ProjectFilePath;
            _recentProjects.Touch(_workspace.ProjectFilePath, _workspace.Project.DisplayName);
            RefreshRecentProjectsMenu();
            _projectExplorer.RefreshTree();
            foreach (IAuthoringDocument openDocument in _documentTabs.Keys.ToArray()) UpdateTabTitle(openDocument);
            _statusLabel.Text = "Project relocated successfully";
            UpdateTitleAndState();
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not save project as", exception);
        }
    }

    private void Undo()
    {
        IAuthoringEditorControl? editor = ActiveEditor;
        if (editor is null || editor.HasUnappliedChanges) return;
        if (editor.Document.Undo()) editor.RefreshFromDocument(true);
        UpdateTitleAndState();
    }

    private void Redo()
    {
        IAuthoringEditorControl? editor = ActiveEditor;
        if (editor is null || editor.HasUnappliedChanges) return;
        if (editor.Document.Redo()) editor.RefreshFromDocument(true);
        UpdateTitleAndState();
    }

    private void ApplyActiveEditor()
    {
(ActiveEditor ?? _quickSoundLabForm?.CurrentEditor)?.TryApplyEdits();
        UpdateTitleAndState();
    }

    private void FormatActiveEditor() => (ActiveEditor ?? _quickSoundLabForm?.CurrentEditor)?.FormatJson();

    private void ValidateActiveDocument()
    {
        if (ActiveDocument is IAuthoringDocument document) ValidateDocument(document);
        else if (_quickSoundLabForm?.CurrentDocument is IAuthoringDocument popupDocument) ValidateDocument(popupDocument);
    }

    private void ValidateDocument(IAuthoringDocument document)
    {
        if (GetEditor(document) is IAuthoringEditorControl editor && !editor.TryApplyEdits()) return;
        try
        {
            ValidationSummary summary = document.Validate();
            _diagnostics.SetDiagnostics(summary.Diagnostics);
            GetEditor(document)?.SetValidation(summary);
            _statusLabel.Text = summary.HasErrors ? "Validation found errors" : "Document validation completed";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Validation failed", exception);
        }
        UpdateTitleAndState();
    }

    private void ValidateWorkspace()
    {
        if (_workspace is null || !CommitAllEditors()) return;
        SetBusy(true, "Validating workspace…");
        try
        {
            ValidationSummary summary = _workspace.ValidateWorkspace();
            _diagnostics.SetDiagnostics(summary.Diagnostics);
            _statusLabel.Text = summary.HasErrors ? "Workspace validation found errors" : "Workspace validation completed";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Workspace validation failed", exception);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CompileActiveDocumentAsync()
    {
        if (ActiveDocument is IAuthoringDocument document) await CompileDocumentAsync(document);
        else if (_quickSoundLabForm?.CurrentDocument is IAuthoringDocument popupDocument) await CompileDocumentAsync(popupDocument);
    }

    private async Task<AuthoringCompilationResult?> CompileDocumentAsync(IAuthoringDocument document)
    {
        if (_workspace is null || GetEditor(document) is not IAuthoringEditorControl editor || !editor.TryApplyEdits()) return null;
        _compileCancellation?.Cancel();
        _compileCancellation?.Dispose();
        _compileCancellation = new CancellationTokenSource();
        SetBusy(true, $"Compiling {document.DisplayName}…");
        _compileStatusLabel.Text = "Compiling";
        try
        {
            AuthoringCompilationResult result = await document.CompileAsync(
                _workspace.CreateCompilationContext(new Playroma.Sound.Core.Audio.AudioFormat(48_000, 2, 512)),
                _compileCancellation.Token);
            editor.SetCompilationState(result);
            _diagnostics.SetDiagnostics(result.Validation.Diagnostics);
            _compileStatusLabel.Text = $"{result.State} {result.Duration.TotalMilliseconds:0} ms";
            _statusLabel.Text = result.Succeeded ? "Compilation succeeded" : $"Compilation {result.State.ToString().ToLowerInvariant()}";
            return result;
        }
        catch (OperationCanceledException)
        {
            _compileStatusLabel.Text = "Cancelled";
            return null;
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Compilation failed", exception);
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task PlayActiveDocumentAsync()
    {
        if (ActiveDocument is IAuthoringDocument document) await PlayDocumentAsync(document);
        else if (_quickSoundLabForm?.CurrentDocument is IAuthoringDocument popupDocument) await PlayDocumentAsync(popupDocument);
    }

    private async Task PlayDocumentAsync(IAuthoringDocument document)
    {
        if (_workspace is null || GetEditor(document) is not IAuthoringEditorControl editor || !editor.TryApplyEdits()) return;
        SetBusy(true, $"Preparing preview for {document.DisplayName}…");
        try
        {
            AuthoringCompilationResult result = await _preview.PlayAsync(_workspace, document);
            editor.SetCompilationState(result);
            _diagnostics.SetDiagnostics(result.Validation.Diagnostics);
            _statusLabel.Text = $"Previewing {document.DisplayName}";
        }
        catch (PreviewCompilationException exception)
        {
            editor.SetCompilationState(exception.Result);
            _diagnostics.SetDiagnostics(exception.Result.Validation.Diagnostics);
            ShowError("The document could not be previewed", exception);
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Playback could not start", exception);
        }
        finally
        {
            SetBusy(false);
            UpdateCommandState();
        }
    }

    private void StopPlayback()
    {
        try
        {
            _preview.Stop();
            _statusLabel.Text = "Playback stopped";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not stop playback cleanly", exception);
        }
        UpdateCommandState();
    }

    private void ToggleRecording()
    {
        try
        {
            if (_preview.Telemetry.IsRecording)
            {
                _preview.StopRecording();
                _statusLabel.Text = "Master recording finalized";
                return;
            }
            if (!_preview.IsPlaying)
            {
                MessageBox.Show(this, "Start a preview before recording the master output.", "Record", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var dialog = new SaveFileDialog
            {
                Filter = "Wave audio (*.wav)|*.wav",
                AddExtension = true,
                DefaultExt = "wav",
                FileName = $"{ActiveDocument?.DocumentId ?? "playroma-preview"}.wav",
                Title = "Record Playroma Sound Master Output"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _preview.StartRecording(dialog.FileName);
            _statusLabel.Text = $"Recording to {dialog.FileName}";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Recording failed", exception);
        }
    }

    private void LoadAudioDevices()
    {
        try
        {
            IReadOnlyList<AudioDeviceInfo> devices = new AudioDeviceCatalog().GetRenderDevices();
            _transport.SetDevices(devices, _settings.LastAudioDeviceId);
            if (!string.IsNullOrWhiteSpace(_settings.LastAudioDeviceId) && devices.Any(item => StringComparer.Ordinal.Equals(item.Id, _settings.LastAudioDeviceId)))
                _preview.DeviceSelection = AudioDeviceSelection.Specific(_settings.LastAudioDeviceId);
            else
                _preview.DeviceSelection = AudioDeviceSelection.Default();
        }
        catch (Exception exception) when (exception is PlatformNotSupportedException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _ = exception;
            _transport.SetDevices([], "");
            _statusLabel.Text = "No Windows audio endpoint is currently available";
        }
    }

    private void ChangeAudioDevice(AudioDeviceSelection selection)
    {
        try
        {
            _preview.DeviceSelection = selection;
            _settings.LastAudioDeviceId = selection.DeviceId ?? "";
        }
        catch (Exception exception) when (IsExpectedEditorException(exception))
        {
            ShowError("Could not change audio device", exception);
            LoadAudioDevices();
        }
    }

    private void ActivateDiagnostic(AuthoringDiagnostic diagnostic)
    {
        if (_workspace is null) return;
        if (!string.IsNullOrWhiteSpace(diagnostic.DocumentId) && _workspace.Project.Assets.Any(item => StringComparer.Ordinal.Equals(item.Id, diagnostic.DocumentId)))
            OpenAsset(diagnostic.DocumentId);
        ActiveEditor?.SelectDiagnostic(diagnostic);
    }

    private void RecoverAutosaves()
    {
        if (_workspace is null) return;
        AutosaveEnvelope[] entries = _autosaveService.Scan(_workspace.Project.Id)
            .Where(item => _workspace.Project.Assets.Any(asset => StringComparer.Ordinal.Equals(asset.Id, item.DocumentId)))
            .ToArray();
        if (entries.Length == 0) return;
        using var dialog = new RecoveryDialog(entries);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (AutosaveEnvelope envelope in dialog.SelectedEntries)
        {
            try
            {
                IAuthoringDocument document = _workspace.OpenDocument(envelope.DocumentId);
                RecoveryDocumentFactory.Apply(document, envelope);
                OpenDocument(document);
            }
            catch (Exception exception) when (IsExpectedEditorException(exception))
            {
                MessageBox.Show(this, $"Could not recover '{envelope.DocumentId}': {exception.Message}", "Recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        _projectExplorer.RefreshTree();
        UpdateTitleAndState();
    }

    private void StartAutosave(AuthoringWorkspace workspace)
    {
        StopAutosave();
        _autosaveCoordinator = new AuthoringAutosaveCoordinator(
            _autosaveService,
            workspace.Project.Id,
            () => workspace.OpenDocuments,
            TimeSpan.FromSeconds(_settings.AutosaveSeconds));
        _autosaveCoordinator.AutosaveFailed += OnAutosaveFailed;
        _autosaveCoordinator.Start();
    }

    private void StopAutosave()
    {
        if (_autosaveCoordinator is null) return;
        _autosaveCoordinator.AutosaveFailed -= OnAutosaveFailed;
        _autosaveCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _autosaveCoordinator = null;
    }

    private void OnAutosaveFailed(object? sender, AutosaveFailedEventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke((Action)(() => _statusLabel.Text = $"Autosave failed for {eventArgs.DocumentId}: {eventArgs.Exception.Message}"));
    }

    private bool PrepareUnappliedEditors()
    {
        List<IAuthoringEditorControl> editors = _documentTabs.Values.Select(tab => tab.Tag).OfType<IAuthoringEditorControl>().Where(editor => editor.HasUnappliedChanges).ToList();
        if (_quickSoundLabForm?.CurrentEditor is IAuthoringEditorControl popupEditor && popupEditor.HasUnappliedChanges) editors.Add(popupEditor);
        if (editors.Count == 0) return true;
        DialogResult result = MessageBox.Show(this, $"Apply pending JSON edits in {editors.Count} document(s)?", "Unapplied edits", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.No)
        {
            foreach (IAuthoringEditorControl editor in editors) editor.RefreshFromDocument(true);
            return true;
        }
        foreach (IAuthoringEditorControl editor in editors)
        {
            if (!editor.TryApplyEdits())
            {
                SelectDocument(editor.Document);
                return false;
            }
        }
        return true;
    }

    private bool CommitAllEditors()
    {
        IEnumerable<IAuthoringEditorControl> allEditors = _documentTabs.Values.Select(tab => tab.Tag).OfType<IAuthoringEditorControl>();
        if (_quickSoundLabForm?.CurrentEditor is IAuthoringEditorControl popupEditor)
            allEditors = allEditors.Concat(new[] { popupEditor });

        foreach (IAuthoringEditorControl editor in allEditors)
        {
            if (!editor.TryApplyEdits())
            {
                SelectDocument(editor.Document);
                return false;
            }
        }
        return true;
    }

    private void OnActiveTabChanged()
    {
        if (ActiveDocument is IAuthoringDocument document)
        {
            _inspector.SelectedObject = new DocumentInspectorModel(document);
            ValidationSummary summary = document.Validate();
            _diagnostics.SetDiagnostics(summary.Diagnostics);
            GetEditor(document)?.SetValidation(summary);
        }
        else if (_workspace is not null)
        {
            _inspector.SelectedObject = new ProjectInspectorModel(_workspace);
        }
        UpdateTitleAndState();
    }

    private void OnDocumentTabsMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Middle) return;
        for (int i = 0; i < _documentTabsControl.TabCount; i++)
        {
            if (!_documentTabsControl.GetTabRect(i).Contains(eventArgs.Location)) continue;
            if (_documentTabsControl.TabPages[i].Tag is IAuthoringEditorControl editor) CloseDocument(editor.Document);
            break;
        }
    }

    private void ShowWelcomeTab()
    {
        if (_workspace is not null || _documentTabsControl.TabPages.Cast<TabPage>().Any(page => page.Name == "Welcome")) return;
        var page = new TabPage("Welcome") { Name = "Welcome" };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(40) };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        var title = new Label
        {
            Text = "Playroma Sound",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomCenter,
            Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 26f, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = "Procedural audio authoring environment",
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Padding = new Padding(0, 8, 0, 18)
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None };
        var newButton = new Button { Text = "New Project…", AutoSize = true, Height = 34 };
        var openButton = new Button { Text = "Open Project…", AutoSize = true, Height = 34 };
        newButton.Click += (_, _) => NewProject();
        openButton.Click += (_, _) => OpenProjectDialog();
        buttons.Controls.Add(newButton);
        buttons.Controls.Add(openButton);
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        panel.Controls.Add(buttons, 0, 2);
        page.Controls.Add(panel);
        _documentTabsControl.TabPages.Add(page);
        _documentTabsControl.SelectedTab = page;
    }

    private void ShowProjectOverviewTab()
    {
        if (_workspace is null) { ShowWelcomeTab(); return; }
        if (_documentTabsControl.TabPages.Cast<TabPage>().Any(page => page.Name == "ProjectOverview")) return;
        var page = new TabPage("Project Overview") { Name = "ProjectOverview" };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(36),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = $"{_workspace.Project.DisplayName}\n\n{_workspace.Project.Assets.Count} assets\n\nDouble-click an asset in the Project Explorer to open it."
        };
        page.Controls.Add(label);
        _documentTabsControl.TabPages.Add(page);
        _documentTabsControl.SelectedTab = page;
    }

    private void RemoveWelcomeTab()
    {
        foreach (TabPage page in _documentTabsControl.TabPages.Cast<TabPage>().Where(page => page.Tag is null).ToArray())
        {
            _documentTabsControl.TabPages.Remove(page);
            page.Dispose();
        }
    }

    private void SelectDocument(IAuthoringDocument document)
    {
        if (_documentTabs.TryGetValue(document, out TabPage? tab)) _documentTabsControl.SelectedTab = tab;
        else if (_quickSoundLabForm?.CurrentDocument is not null && ReferenceEquals(_quickSoundLabForm.CurrentDocument, document))
        {
            if (!_quickSoundLabForm.Visible) _quickSoundLabForm.Show(this);
            _quickSoundLabForm.BringToFront();
            _quickSoundLabForm.Activate();
        }
    }

    private IAuthoringEditorControl? GetEditor(IAuthoringDocument document) =>
        _documentTabs.TryGetValue(document, out TabPage? tab) ? tab.Tag as IAuthoringEditorControl
        : _quickSoundLabForm?.CurrentDocument is not null && ReferenceEquals(_quickSoundLabForm.CurrentDocument, document) ? _quickSoundLabForm.CurrentEditor
        : null;

    private void UpdateTabTitle(IAuthoringDocument document)
    {
        if (_documentTabs.TryGetValue(document, out TabPage? tab))
        {
            tab.Text = DocumentTabTitle(document);
            tab.ToolTipText = document.FilePath;
        }
    }

    private static string DocumentTabTitle(IAuthoringDocument document) => document.DisplayName + (document.IsDirty ? " *" : "");

    private void UpdateTitleAndState()
    {
        string project = _workspace?.Project.DisplayName ?? "No project";
        string dirty = _workspace?.HasUnsavedChanges == true ? " *" : "";
        string productTitle = _integrationOptions is null
            ? "Playroma Sound"
            : $"Playroma Sound Studio — {_integrationOptions.HostProjectTitle}";
        Text = $"{project}{dirty} — {productTitle}";
        _projectStatusLabel.Text = _workspace is null ? "No project" : $"{_workspace.Project.Assets.Count} assets | {_workspace.OpenDocuments.Count} open";
        UpdateCommandState();
    }

    private void UpdateCommandState()
    {
        IAuthoringDocument? document = ActiveDocument;
        bool hasWorkspace = _workspace is not null;
        bool hasDocument = document is not null;
        bool unapplied = ActiveEditor?.HasUnappliedChanges == true;
        bool playing = _preview.IsPlaying;
        bool enabled = !_busy;

        _saveDocumentMenu.Enabled = enabled && hasDocument;
        _saveAllMenu.Enabled = enabled && hasWorkspace;
        _closeDocumentMenu.Enabled = enabled && hasDocument;
        _undoMenu.Enabled = enabled && hasDocument && !unapplied && document!.CanUndo;
        _redoMenu.Enabled = enabled && hasDocument && !unapplied && document!.CanRedo;
        _undoMenu.Text = document?.CanUndo == true ? $"Undo {document.UndoDescription}" : "Undo";
        _redoMenu.Text = document?.CanRedo == true ? $"Redo {document.RedoDescription}" : "Redo";
        _validateDocumentMenu.Enabled = enabled && hasDocument;
        _validateWorkspaceMenu.Enabled = enabled && hasWorkspace;
        _compileMenu.Enabled = enabled && hasDocument;
        _playMenu.Enabled = enabled && hasDocument && !playing;
        _stopMenu.Enabled = playing;
        IAuthoringDocument? exportDocument = _quickSoundLabForm is { Visible: true, CurrentDocument: not null }
            ? _quickSoundLabForm.CurrentDocument
            : document ?? _quickSoundLabForm?.CurrentDocument;
        _exportMvMenu.Enabled = enabled && _integrationOptions is not null && hasWorkspace && exportDocument is not null;

        _saveButton.Enabled = enabled && hasDocument;
        _undoButton.Enabled = _undoMenu.Enabled;
        _redoButton.Enabled = _redoMenu.Enabled;
        _validateButton.Enabled = enabled && hasDocument;
        _compileButton.Enabled = enabled && hasDocument;
        _playButton.Enabled = enabled && hasDocument && !playing;
        _stopButton.Enabled = playing;
        _makeSoundButton.Enabled = enabled && hasWorkspace;
        bool quickSoundActive = CurrentQuickSoundEditor is GeneratorRequestEditorControl;
        _tryAnotherButton.Enabled = enabled && quickSoundActive;
        _keepNextButton.Enabled = enabled && quickSoundActive;
        _exportMvButton.Enabled = _exportMvMenu.Enabled;
    }

    private void SetBusy(bool value, string? status = null)
    {
        _busy = value;
        UseWaitCursor = value;
        if (!string.IsNullOrWhiteSpace(status)) _statusLabel.Text = status;
        UpdateCommandState();
    }

    private void RefreshRecentProjectsMenu()
    {
        _recentMenu.DropDownItems.Clear();
        RecentProjectEntry[] entries = _recentProjects.Load().Where(item => File.Exists(item.Path)).ToArray();
        if (entries.Length == 0)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("No recent projects") { Enabled = false });
            return;
        }
        foreach (RecentProjectEntry entry in entries)
        {
            var item = new ToolStripMenuItem(entry.DisplayName) { ToolTipText = entry.Path, Tag = entry.Path };
            item.Click += (_, _) => OpenWorkspace((string)item.Tag!);
            _recentMenu.DropDownItems.Add(item);
        }
    }

    public bool PrepareForHostClose()
    {
        if (_shuttingDown) return true;
        if (!PrepareToReplaceWorkspace()) return false;
        _shuttingDown = true;
        _telemetryTimer.Stop();
        SaveWindowSettings();
        try { _settingsStore.Save(_settings); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { _ = exception; }
        _preview.Dispose();
        return true;
    }

    private void OnMainFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!PrepareForHostClose()) eventArgs.Cancel = true;
    }


    private void ApplySplitterSettings()
    {
        SetSplitterDistance(_mainSplit, _settings.ExplorerWidth);
        SetSplitterDistance(_workspaceSplit, _workspaceSplit.Width - _settings.InspectorWidth - _workspaceSplit.SplitterWidth);
        SetSplitterDistance(_documentSplit, _documentSplit.Height - _settings.DiagnosticsHeight - _documentSplit.SplitterWidth);
    }

    private static void SetSplitterDistance(SplitContainer split, int requested)
    {
        int extent = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
        int minimum = split.Panel1MinSize;
        int maximum = extent - split.Panel2MinSize - split.SplitterWidth;
        if (maximum < minimum) return;
        split.SplitterDistance = Math.Clamp(requested, minimum, maximum);
    }

    private void SaveWindowSettings()
    {
        // An embedded child form is docked to the jRPG tool window. Persisting its child bounds
        // would corrupt the standalone Playroma Sound window placement.
        if (_integrationOptions is null)
        {
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _settings.WindowX = bounds.X;
            _settings.WindowY = bounds.Y;
            _settings.WindowWidth = bounds.Width;
            _settings.WindowHeight = bounds.Height;
            _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        }
        _settings.ExplorerWidth = _mainSplit.SplitterDistance;
        _settings.InspectorWidth = _workspaceSplit.Width - _workspaceSplit.SplitterDistance;
        _settings.DiagnosticsHeight = _documentSplit.Height - _documentSplit.SplitterDistance;
        _settings.LastProjectPath = _workspace?.ProjectFilePath ?? _settings.LastProjectPath;
    }

    private static Rectangle RestoreWindowBounds(EditorSettings settings)
    {
        var bounds = new Rectangle(settings.WindowX, settings.WindowY, settings.WindowWidth, settings.WindowHeight);
        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds))
            ? bounds
            : new Rectangle(80, 80, settings.WindowWidth, settings.WindowHeight);
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        item.Click += handler;
        return item;
    }

    private static ToolStripButton Button(string text, EventHandler handler)
    {
        var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
        button.Click += handler;
        return button;
    }

    private static bool IsExpectedEditorException(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or
        ArgumentException or InvalidOperationException or KeyNotFoundException or PlatformNotSupportedException or
        System.Runtime.InteropServices.COMException or TimeoutException;

    private void ShowError(string title, Exception exception)
    {
        _statusLabel.Text = exception.Message;
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _telemetryTimer.Dispose();
            _compileCancellation?.Cancel();
            _compileCancellation?.Dispose();
            StopAutosave();
            _preview.Dispose();
            _workspace?.Dispose();
        }
        base.Dispose(disposing);
    }
}
