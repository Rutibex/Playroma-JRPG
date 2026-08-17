using System.ComponentModel;
using PlayromaMusic.Audio.SoundFonts;
using PlayromaMusic.Core.GenerationLab;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.ReferenceAnalysis;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Generators.Presets;
using PlayromaMusic.Generators.ReferenceAnalysis;
using PlayromaMusic.Generators.Structured;
using PlayromaMusic.Midi;
using PlayromaMusic.Midi.ReferenceAnalysis;

namespace Playroma.JRPG.Editor.WinForms.Music;

/// <summary>
/// Native WinForms authoring surface for the complete Playroma Music engine.
/// </summary>
public sealed partial class MusicStudioControl : UserControl
{
    private readonly MusicStudioSession _session;
    private readonly MusicExportService _exporter = new();
    private readonly WinFormsMusicPlaybackHost _playback = new();
    private readonly ToolStrip _toolbar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
    private readonly StatusStrip _statusStrip = new() { Dock = DockStyle.Bottom };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _songSummary = new();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStripButton _undoButton = new("Undo");
    private readonly ToolStripButton _redoButton = new("Redo");
    private readonly ToolStripButton _playButton = new("Play");
    private readonly ToolStripButton _pauseButton = new("Pause");
    private readonly ToolStripButton _stopButton = new("Stop");
    private readonly ToolStripButton _loopButton = new("Loop") { CheckOnClick = true };
    private readonly ToolStripProgressBar _progress = new() { Minimum = 0, Maximum = 1000, Visible = false, AutoSize = false, Width = 130 };
    private readonly NumericUpDown _masterVolume = new() { Minimum = 0, Maximum = 100, Value = 82, Width = 58 };

    // Composer
    private readonly TextBox _titleBox = new() { Width = 250 };
    private readonly NumericUpDown _seedBox = new()
    {
        // NumericUpDown's hexadecimal formatter converts Value through Int64.
        // Store the raw 64 seed bits in the signed Int64 range and reinterpret
        // them at the UI boundary so every possible UInt64 seed is supported.
        Minimum = long.MinValue,
        Maximum = long.MaxValue,
        Width = 170,
        Hexadecimal = true
    };
    private readonly ComboBox _styleBox = DropDown(260);
    private readonly NumericUpDown _tempoBox = new() { Minimum = 20, Maximum = 400, DecimalPlaces = 1, Increment = 0.5m, Width = 80 };
    private readonly ComboBox _keyBox = DropDown(105);
    private readonly ComboBox _scaleBox = DropDown(145);
    private readonly DataGridView _roleGrid = Grid();
    private readonly PianoRollControl _pianoRoll = new() { Dock = DockStyle.Fill };

    // Lab
    private readonly ComboBox _labPresetBox = DropDown(300);
    private readonly DataGridView _voiceGrid = Grid();
    private readonly PropertyGrid _voiceProperties = Inspector();
    private readonly PropertyGrid _labProperties = Inspector();

    // Studio
    private readonly DataGridView _mixerGrid = Grid();
    private readonly TextBox _soundFontBox = new() { Dock = DockStyle.Fill };
    private readonly PropertyGrid _audioProperties = Inspector();
    private readonly DataGridView _presetGrid = Grid();
    private readonly TextBox _audioDiagnostics = ReadOnlyText();

    // Style packs
    private readonly ListBox _styleList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid _styleProperties = Inspector();
    private readonly DataGridView _styleRolesGrid = Grid();
    private readonly ComboBox _hybridSecondary = DropDown(250);
    private readonly TrackBar _hybridWeight = new() { Minimum = 0, Maximum = 100, Value = 35, TickFrequency = 10, Width = 180 };
    private readonly CheckBox _blendForms = Check("Forms", true);
    private readonly CheckBox _blendHarmony = Check("Harmony", true);
    private readonly CheckBox _blendRhythms = Check("Rhythms", true);
    private readonly CheckBox _blendInstruments = Check("Instruments", true);
    private readonly CheckBox _blendScoring = Check("Scoring", true);

    // Advanced
    private readonly PropertyGrid _advancedProperties = Inspector();
    private readonly DataGridView _tensionGrid = Grid(readOnly: true);
    private readonly DataGridView _arrangementGrid = Grid(readOnly: true);
    private readonly DataGridView _motifGrid = Grid(readOnly: true);
    private readonly TextBox _advancedDiagnostics = ReadOnlyText();

    // Orchestration
    private readonly PropertyGrid _orchestrationProperties = Inspector();
    private readonly ComboBox _randomDomain = DropDown(190);
    private readonly DataGridView _catalogGrid = Grid();
    private readonly PropertyGrid _genomeProperties = Inspector();

    // Reference MIDI
    private readonly PropertyGrid _blueprintProperties = Inspector();
    private readonly DataGridView _referenceTrackGrid = Grid();
    private readonly ComboBox _descendantMode = DropDown(180);
    private readonly ComboBox _descendantStyle = DropDown(240);
    private readonly NumericUpDown _descendantSeed = new()
    {
        Minimum = long.MinValue,
        Maximum = long.MaxValue,
        Hexadecimal = true,
        Width = 165
    };
    private readonly NumericUpDown _candidateCount = new() { Minimum = 1, Maximum = 64, Value = 12, Width = 60 };
    private readonly TextBox _referenceReport = ReadOnlyText();
    private ReferenceCorpusAnalysisResult? _lastCorpus;

    private bool _refreshing;
    private bool _disposed;

    public MusicStudioControl(string projectRoot)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        _session = new MusicStudioSession(projectRoot);
        _session.ProjectChanged += OnProjectChanged;
        _session.StatusChanged += SetStatus;
        _playback.DiagnosticsChanged += OnPlaybackDiagnosticsChanged;
        _pianoRoll.EditRequested += OnPianoRollEditRequested;

        BuildToolbar();
        BuildTabs();
        BuildTransportBar();
        _statusStrip.Items.AddRange([_status, _songSummary]);
        Controls.Add(_tabs);
        Controls.Add(_toolbar);
        Controls.Add(_transportBar);
        Controls.Add(_statusStrip);

        PopulateStaticChoices();
        RefreshAll();
        InitializePlayground();
        SetStatus("Choose a game scene, press Make Music, and start experimenting.");
    }

    public bool IsDirty => _session.IsDirty;
    public string? ProjectFilePath => _session.FilePath;

    public bool PromptSave(IWin32Window owner)
    {
        if (!IsDirty) return true;
        DialogResult result = MessageBox.Show(owner,
            "The current Playroma Music composition has unsaved changes. Save it before closing?",
            "Playroma Music Studio", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (result == DialogResult.Cancel) return false;
        if (result == DialogResult.Yes) return SaveProject(owner);
        return true;
    }

    public bool SaveProject(IWin32Window owner)
    {
        try
        {
            string? destination = _session.FilePath;
            if (string.IsNullOrWhiteSpace(destination))
            {
                using SaveFileDialog dialog = new()
                {
                    Title = "Save Playroma Music Project",
                    Filter = "Playroma Music project (*.playroma-music.json)|*.playroma-music.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
                    FileName = MusicExportService.SanitizeFileName(_session.Project.Title) + ".playroma-music.json",
                    InitialDirectory = Path.Combine(_session.ProjectRoot, "data", "PlayromaMusic")
                };
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                destination = dialog.FileName;
            }
            _session.Save(destination);
            return true;
        }
        catch (Exception exception)
        {
            ShowError(owner, "Could not save the Playroma Music project.", exception);
            return false;
        }
    }

    private void BuildToolbar()
    {
        ToolStripDropDownButton create = new("New Song");
        ToolStripMenuItem generateCurrentRecipe = new("Make Music from Current Recipe", null,
            (_, _) => Safe(() => GenerateFromPlayground(surprise: false, playAfter: true)))
        {
            ShortcutKeys = Keys.Control | Keys.G
        };
        create.DropDownItems.Add(generateCurrentRecipe);
        create.DropDownItems.Add("Reset Playground Recipe", null, (_, _) =>
        {
            _tabs.SelectedIndex = 0;
            ResetPlaygroundRecipe();
        });
        create.DropDownItems.Add("Surprise Me", null, (_, _) => Safe(() => GenerateFromPlayground(surprise: true, playAfter: true)));
        create.DropDownItems.Add(new ToolStripSeparator());
        create.DropDownItems.Add("Advanced structured composition…", null, (_, _) => Safe(NewStructured));
        create.DropDownItems.Add("Generation laboratory…", null, (_, _) => Safe(NewLab));
        create.DropDownItems.Add("New musical universe", null, (_, _) => Safe(_session.NewUniverse));

        ToolStripButton makeMusic = new("✨ Make Music")
        {
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            ToolTipText = "Generate a new song from the current Playground recipe (Ctrl+G)."
        };
        ToolStripButton surpriseMe = new("🎲 Surprise Me")
        {
            ToolTipText = "Choose a random recipe and generate a new song."
        };
        ToolStripButton open = new("Open");
        ToolStripButton save = new("Save");
        ToolStripButton importMidi = new("Import MIDI");
        ToolStripButton exportGame = new("Export to Game");
        ToolStripDropDownButton moreExport = new("More Export Options");
        moreExport.DropDownItems.Add("Standard MIDI file…", null, async (_, _) => await ExportMidiAsync());
        moreExport.DropDownItems.Add("Rendered WAV file…", null, async (_, _) => await ExportWaveAsync());
        moreExport.DropDownItems.Add("RPG Maker MV audio database…", null, async (_, _) => await ExportMvAsync());

        makeMusic.Click += (_, _) => Safe(() =>
        {
            _tabs.SelectedIndex = 0;
            GenerateFromPlayground(surprise: false, playAfter: true);
        });
        surpriseMe.Click += (_, _) => Safe(() =>
        {
            _tabs.SelectedIndex = 0;
            GenerateFromPlayground(surprise: true, playAfter: true);
        });
        open.Click += (_, _) => OpenProject();
        save.Click += (_, _) => SaveProject(this);
        _undoButton.Click += (_, _) => Safe(_session.Undo);
        _redoButton.Click += (_, _) => Safe(_session.Redo);
        importMidi.Click += (_, _) => ImportMidi();
        exportGame.Click += async (_, _) => await ExportMvAsync();

        _toolbar.Items.AddRange([
            create, makeMusic, surpriseMe,
            new ToolStripSeparator(), open, save,
            new ToolStripSeparator(), _undoButton, _redoButton,
            new ToolStripSeparator(), importMidi,
            new ToolStripSeparator(), exportGame, moreExport,
            new ToolStripSeparator(), _progress
        ]);
    }

    private void BuildTabs()
    {
        _tabs.Multiline = false;
        _tabs.Padding = new Point(18, 6);
        _tabs.TabPages.Add(BuildCreateTab());
        _tabs.TabPages.Add(BuildArrangeTab());
        TabPage compose = BuildComposerTab();
        compose.Text = "Compose";
        _tabs.TabPages.Add(compose);
        _tabs.TabPages.Add(BuildLibraryTab());
        _tabs.TabPages.Add(BuildAdvancedToolsTab());
    }

    private TabPage BuildComposerTab()
    {
        TabPage page = new("Composer");
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        FlowLayoutPanel settings = Flow();
        settings.Controls.AddRange([
            Labeled("Title", _titleBox), Labeled("Seed (hex)", _seedBox), Labeled("Style", _styleBox),
            Labeled("Tempo", _tempoBox), Labeled("Key", _keyBox), Labeled("Scale", _scaleBox)
        ]);
        Button applyStyle = Button("Apply Style", () => Safe(() => _session.ApplyStyle(SelectedStyleId())));
        Button newSong = Button("Generate New", () => Safe(NewStructured));
        Button structure = Button("Regenerate Form + Harmony", () => Safe(_session.RegenerateStructure));
        Button unlocked = Button("Regenerate Unlocked", () => Safe(_session.RegenerateUnlockedRoles));
        Button motif = Button("New Motif", () => Safe(_session.RegenerateMotif));
        Button selected = Button("Regenerate Selected Role", () => Safe(RegenerateSelectedRole));
        settings.Controls.AddRange([applyStyle, newSong, structure, unlocked, motif, selected]);
        layout.Controls.Add(settings, 0, 0);

        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 475, FixedPanel = FixedPanel.Panel1 };
        ConfigureRoleGrid();
        split.Panel1.Controls.Add(_roleGrid);
        split.Panel2.Controls.Add(_pianoRoll);
        layout.Controls.Add(split, 0, 1);
        page.Controls.Add(layout);

        _titleBox.Validated += (_, _) =>
        {
            if (_refreshing || _titleBox.Text == _session.Project.Title) return;
            _session.Edit("Rename composition", project => { project.Title = _titleBox.Text.Trim(); project.Song.Title = project.Title; });
        };
        _tempoBox.ValueChanged += (_, _) =>
        {
            if (_refreshing) return;
            _session.Edit("Change tempo", project =>
            {
                if (project.Song.TempoMap.Count == 0) project.Song.TempoMap.Add(new TempoChange { Tick = 0 });
                project.Song.TempoMap[0].BeatsPerMinute = decimal.ToDouble(_tempoBox.Value);
            });
        };
        _keyBox.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing || _keyBox.SelectedItem is not PitchClass key) return;
            _session.Edit("Change key", project => project.Song.KeyRoot = key);
        };
        _scaleBox.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing || _scaleBox.SelectedItem is not ScaleKind scale) return;
            _session.Edit("Change scale", project => project.Song.Scale = scale);
        };
        _roleGrid.SelectionChanged += (_, _) =>
        {
            _pianoRoll.TrackId = SelectedRole()?.Id is { } roleId
                ? StructuredCompositionService.GetTrack(_session.Project, roleId)?.Id
                : null;
        };
        return page;
    }

    private TabPage BuildLabTab()
    {
        TabPage page = new("Generation Lab");
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        FlowLayoutPanel commands = Flow();
        commands.Controls.AddRange([
            Labeled("Preset", _labPresetBox),
            Button("Create Lab Song", () => Safe(NewLab)),
            Button("Apply Preset", () => Safe(() => _session.ApplyLabPreset(SelectedLabPresetId()))),
            Button("Regenerate Selected Voice", () => Safe(RegenerateSelectedVoice)),
            Button("Regenerate Unlocked", () => Safe(_session.RegenerateUnlockedVoices)),
            Button("Rebuild All", () => Safe(_session.RebuildLab))
        ]);
        layout.Controls.Add(commands, 0, 0);

        SplitContainer outer = new() { Dock = DockStyle.Fill, SplitterDistance = 500, FixedPanel = FixedPanel.Panel1 };
        ConfigureVoiceGrid();
        outer.Panel1.Controls.Add(_voiceGrid);
        TabControl inspectors = new() { Dock = DockStyle.Fill };
        inspectors.TabPages.Add(Tab("Selected Voice", _voiceProperties));
        inspectors.TabPages.Add(Tab("Laboratory Settings", _labProperties));
        outer.Panel2.Controls.Add(inspectors);
        layout.Controls.Add(outer, 0, 1);
        page.Controls.Add(layout);

        _voiceGrid.SelectionChanged += (_, _) => _voiceProperties.SelectedObject = SelectedVoice();
        WirePropertyGrid(_voiceProperties, "Edit generation voice", () => Safe(RegenerateSelectedVoice));
        WirePropertyGrid(_labProperties, "Edit generation laboratory", () => { });
        return page;
    }

    private TabPage BuildStudioTab()
    {
        TabPage page = new("Studio + Mixer");
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 650 };
        ConfigureMixerGrid();
        split.Panel1.Controls.Add(_mixerGrid);

        TableLayoutPanel right = new() { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(6) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        FlowLayoutPanel sf = Flow();
        _soundFontBox.Width = 420;
        sf.Controls.AddRange([
            Labeled("SoundFont 2", _soundFontBox),
            Button("Browse…", () => BrowseSoundFont()),
            Button("Use Included SF2", () => UseDefaultSoundFont()),
            Button("Scan Presets", () => ScanSoundFont())
        ]);
        right.Controls.Add(sf, 0, 0);
        right.Controls.Add(_audioProperties, 0, 1);
        ConfigurePresetGrid();
        right.Controls.Add(_presetGrid, 0, 2);
        right.Controls.Add(_audioDiagnostics, 0, 3);
        split.Panel2.Controls.Add(right);
        page.Controls.Add(split);
        WirePropertyGrid(_audioProperties, "Edit audio settings", () => _playback.Stop());
        _soundFontBox.Validated += (_, _) =>
        {
            if (_refreshing) return;
            string path = _soundFontBox.Text.Trim();
            if (File.Exists(path)) _session.Edit("Change SoundFont", project => project.Audio.SoundFontPath = Path.GetFullPath(path));
        };
        return page;
    }

    private TabPage BuildStyleTab()
    {
        TabPage page = new("Style Packs");
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 320, FixedPanel = FixedPanel.Panel1 };
        TableLayoutPanel left = new() { Dock = DockStyle.Fill, RowCount = 2 };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(_styleList, 0, 0);
        FlowLayoutPanel reload = Flow();
        reload.Controls.AddRange([
            Button("Apply Selected", () => Safe(() => _session.ApplyStyle(SelectedWorkbenchStyle().Id))),
            Button("Reload JSON Packs", () => Safe(() => { _session.ReloadStylePacks(); PopulateStyles(); RefreshAll(); }))
        ]);
        left.Controls.Add(reload, 0, 1);
        split.Panel1.Controls.Add(left);

        TableLayoutPanel right = new() { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(6) };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.Controls.Add(_styleProperties, 0, 0);
        ConfigureStyleRolesGrid();
        right.Controls.Add(_styleRolesGrid, 0, 1);
        FlowLayoutPanel hybrid = Flow();
        hybrid.Controls.AddRange([
            Labeled("Secondary", _hybridSecondary), Labeled("Weight", _hybridWeight),
            _blendForms, _blendHarmony, _blendRhythms, _blendInstruments, _blendScoring,
            Button("Apply Hybrid Style", () => Safe(ApplyHybrid))
        ]);
        right.Controls.Add(hybrid, 0, 2);
        split.Panel2.Controls.Add(right);
        page.Controls.Add(split);
        _styleList.SelectedIndexChanged += (_, _) => RefreshSelectedStyle();
        return page;
    }

    private TabPage BuildAdvancedTab()
    {
        TabPage page = new("Advanced Composition");
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        FlowLayoutPanel commands = Flow();
        commands.Controls.AddRange([
            Button("Run Advanced Search", () => Safe(_session.RegenerateAdvanced)),
            Button("Rebuild from Current Plans", () => Safe(_session.RebuildCurrentPlans))
        ]);
        layout.Controls.Add(commands, 0, 0);
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 470, FixedPanel = FixedPanel.Panel1 };
        split.Panel1.Controls.Add(_advancedProperties);
        TabControl detail = new() { Dock = DockStyle.Fill };
        detail.TabPages.Add(Tab("Tension Curve", _tensionGrid));
        detail.TabPages.Add(Tab("Arrangement", _arrangementGrid));
        detail.TabPages.Add(Tab("Motif Plan", _motifGrid));
        detail.TabPages.Add(Tab("Diagnostics", _advancedDiagnostics));
        split.Panel2.Controls.Add(detail);
        layout.Controls.Add(split, 0, 1);
        page.Controls.Add(layout);
        WirePropertyGrid(_advancedProperties, "Edit advanced composition", () => { });
        return page;
    }

    private TabPage BuildOrchestrationTab()
    {
        TabPage page = new("Orchestration + Randomizer");
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        FlowLayoutPanel commands = Flow();
        commands.Controls.AddRange([
            Labeled("Randomize", _randomDomain),
            Button("Randomize Domain", () => Safe(() => _session.Randomize((RandomizationDomain)_randomDomain.SelectedItem!))),
            Button("New Universe", () => Safe(_session.NewUniverse))
        ]);
        layout.Controls.Add(commands, 0, 0);
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 470, FixedPanel = FixedPanel.Panel1 };
        TabControl left = new() { Dock = DockStyle.Fill };
        left.TabPages.Add(Tab("Orchestration Settings", _orchestrationProperties));
        left.TabPages.Add(Tab("Composition Genome", _genomeProperties));
        split.Panel1.Controls.Add(left);
        ConfigureCatalogGrid();
        split.Panel2.Controls.Add(_catalogGrid);
        layout.Controls.Add(split, 0, 1);
        page.Controls.Add(layout);
        WirePropertyGrid(_orchestrationProperties, "Edit orchestration settings", () => { });
        WirePropertyGrid(_genomeProperties, "Edit composition genome", () => { });
        return page;
    }

    private TabPage BuildReferenceTab()
    {
        TabPage page = new("Reference MIDI");
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        FlowLayoutPanel commands = Flow();
        commands.Controls.AddRange([
            Button("Analyze MIDI…", AnalyzeReference),
            Button("Load Blueprint…", LoadReferenceBlueprint),
            Button("Save Blueprint…", SaveReferenceBlueprint),
            Labeled("Descendant Mode", _descendantMode), Labeled("Target Style", _descendantStyle),
            Labeled("Seed", _descendantSeed), Labeled("Candidates", _candidateCount),
            Button("Generate Descendant", () => Safe(GenerateDescendant)),
            Button("Analyze MIDI Corpus…", () => AnalyzeCorpus()),
            Button("Export Corpus Style Pack…", () => Safe(ExportCorpusStyle))
        ]);
        layout.Controls.Add(commands, 0, 0);
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 500 };
        split.Panel1.Controls.Add(_blueprintProperties);
        TabControl detail = new() { Dock = DockStyle.Fill };
        ConfigureReferenceTrackGrid();
        detail.TabPages.Add(Tab("Source Tracks", _referenceTrackGrid));
        detail.TabPages.Add(Tab("Analysis / Descendants", _referenceReport));
        split.Panel2.Controls.Add(detail);
        layout.Controls.Add(split, 0, 1);
        page.Controls.Add(layout);
        WirePropertyGrid(_blueprintProperties, "Edit reference blueprint", () => { });
        return page;
    }

    private void PopulateStaticChoices()
    {
        _keyBox.Items.AddRange(Enum.GetValues<PitchClass>().Cast<object>().ToArray());
        _scaleBox.Items.AddRange(Enum.GetValues<ScaleKind>().Cast<object>().ToArray());
        _randomDomain.Items.AddRange(Enum.GetValues<RandomizationDomain>().Cast<object>().ToArray());
        _randomDomain.SelectedItem = RandomizationDomain.Everything;
        _descendantMode.Items.AddRange(Enum.GetValues<ReferenceGenerationMode>().Cast<object>().ToArray());
        _descendantMode.SelectedItem = ReferenceGenerationMode.InspiredBy;
        _labPresetBox.Items.AddRange(LabPresetLibrary.All.Cast<object>().ToArray());
        _labPresetBox.DisplayMember = nameof(LabPresetDefinition.Name);
        PopulatePlaygroundChoices();
        PopulateStyles();
    }

    private void PopulateStyles()
    {
        string? selected = (_styleBox.SelectedItem as StructuredStyleDefinition)?.Id;
        string? workbench = (_styleList.SelectedItem as StructuredStyleDefinition)?.Id;
        string? secondary = (_hybridSecondary.SelectedItem as StructuredStyleDefinition)?.Id;
        string? descendant = (_descendantStyle.SelectedItem as StructuredStyleDefinition)?.Id;
        StructuredStyleDefinition[] styles = StructuredStyleLibrary.All.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        FillStyleCombo(_styleBox, styles, selected);
        FillStyleCombo(_hybridSecondary, styles, secondary);
        FillStyleCombo(_descendantStyle, styles, descendant);
        _styleList.BeginUpdate();
        _styleList.Items.Clear();
        _styleList.DisplayMember = nameof(StructuredStyleDefinition.Name);
        _styleList.Items.AddRange(styles.Cast<object>().ToArray());
        _styleList.EndUpdate();
        SelectById(_styleList, workbench ?? _session.Project.StructuredComposition.ActiveStyleId);
    }

    private static void FillStyleCombo(ComboBox combo, StructuredStyleDefinition[] styles, string? selectedId)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        combo.DisplayMember = nameof(StructuredStyleDefinition.Name);
        combo.Items.AddRange(styles.Cast<object>().ToArray());
        combo.EndUpdate();
        SelectById(combo, selectedId);
        if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void RefreshAll()
    {
        _refreshing = true;
        try
        {
            ProjectDocument project = _session.Project;
            _titleBox.Text = project.Title;
            _seedBox.Value = DecimalFromUlong(project.MasterSeed);
            SelectById(_styleBox, project.StructuredComposition.ActiveStyleId);
            _tempoBox.Value = ClampDecimal((decimal)(project.Song.TempoMap.FirstOrDefault()?.BeatsPerMinute ?? 120), _tempoBox.Minimum, _tempoBox.Maximum);
            _keyBox.SelectedItem = project.Song.KeyRoot;
            _scaleBox.SelectedItem = project.Song.Scale;
            _pianoRoll.Project = project;
            RefreshRoles();
            RefreshLab();
            RefreshStudio();
            RefreshSelectedStyle();
            RefreshAdvanced();
            RefreshOrchestration();
            RefreshReference();
            RefreshPlayground();
            RefreshArrange();
            RefreshLibrary();
            _undoButton.Enabled = _session.CanUndo;
            _undoButton.ToolTipText = _session.NextUndoLabel ?? "Nothing to undo";
            _redoButton.Enabled = _session.CanRedo;
            _redoButton.ToolTipText = _session.NextRedoLabel ?? "Nothing to redo";
            _loopButton.Checked = project.Audio.LoopEnabled;
            _transportLoop.Checked = project.Audio.LoopEnabled;
            _masterVolume.Value = ClampDecimal((decimal)(project.Audio.MasterVolume * 100d), _masterVolume.Minimum, _masterVolume.Maximum);
            _songSummary.Text = $"{project.Song.Tracks.Count} tracks · {project.Song.EnumerateNotes().Count()} notes · {project.Song.Sections.Count} sections";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshRoles()
    {
        _roleGrid.Rows.Clear();
        foreach (CompositionRoleSettings role in _session.Project.StructuredComposition.Roles)
        {
            SongTrack? track = StructuredCompositionService.GetTrack(_session.Project, role.Id);
            int row = _roleGrid.Rows.Add(role.Enabled, role.Locked, role.Role, role.Name, role.MidiChannel + 1,
                role.GeneralMidiProgram + 1, role.MinimumPitch, role.MaximumPitch, role.Density, role.RhythmPatternId, track?.NoteCount ?? 0);
            _roleGrid.Rows[row].Tag = role;
        }
    }

    private void RefreshLab()
    {
        ProjectDocument project = _session.Project;
        SelectById(_labPresetBox, project.GenerationLab.ActivePresetId);
        _voiceGrid.Rows.Clear();
        foreach (AlgorithmVoiceDefinition voice in project.GenerationLab.Voices)
        {
            SongTrack? track = PlayromaMusic.Generators.Lab.LabGenerationService.GetTrack(project, voice.Id);
            int row = _voiceGrid.Rows.Add(voice.Enabled, voice.Locked, voice.Name, voice.Algorithm, voice.Role,
                voice.MidiChannel + 1, voice.GeneralMidiProgram + 1, voice.ParameterPresetId, track?.NoteCount ?? 0);
            _voiceGrid.Rows[row].Tag = voice;
        }
        _labProperties.SelectedObject = project.GenerationLab;
        _voiceProperties.SelectedObject = SelectedVoice() ?? project.GenerationLab.Voices.FirstOrDefault();
    }

    private void RefreshStudio()
    {
        ProjectDocument project = _session.Project;
        _soundFontBox.Text = project.Audio.SoundFontPath;
        _audioProperties.SelectedObject = project.Audio;
        _mixerGrid.Rows.Clear();
        foreach (SongTrack track in project.Song.Tracks)
        {
            int row = _mixerGrid.Rows.Add(track.Muted, track.Solo, track.Name, track.Role, track.MidiChannel + 1,
                track.MidiBank, track.GeneralMidiProgram + 1, Math.Round(track.Volume * 100), Math.Round(track.Pan * 100), track.NoteCount);
            _mixerGrid.Rows[row].Tag = track;
        }
        RefreshPresetGrid(project.Orchestration.Catalog);
        UpdateAudioDiagnostics();
    }

    private void RefreshSelectedStyle()
    {
        if (_styleList.SelectedItem is not StructuredStyleDefinition style)
        {
            _styleProperties.SelectedObject = null;
            _styleRolesGrid.Rows.Clear();
            return;
        }
        _styleProperties.SelectedObject = style;
        _styleRolesGrid.Rows.Clear();
        foreach (CompositionRoleSettings role in style.Roles)
        {
            _styleRolesGrid.Rows.Add(role.Role, role.Name, role.MidiChannel + 1, role.GeneralMidiProgram + 1,
                role.MinimumPitch + "-" + role.MaximumPitch, role.Density, role.RhythmPatternId, role.GeneratorId);
        }
    }

    private void RefreshAdvanced()
    {
        var advanced = _session.Project.AdvancedComposition;
        _advancedProperties.SelectedObject = advanced;
        BindReadOnly(_tensionGrid, advanced.TensionCurve);
        BindReadOnly(_arrangementGrid, advanced.ArrangementPlan);
        BindReadOnly(_motifGrid, advanced.MotifOccurrences);
        _advancedDiagnostics.Text = string.Join(Environment.NewLine, advanced.Diagnostics);
    }

    private void RefreshOrchestration()
    {
        var orchestration = _session.Project.Orchestration;
        _orchestrationProperties.SelectedObject = orchestration;
        _genomeProperties.SelectedObject = orchestration.Genome;
        RefreshPresetGrid(orchestration.Catalog, _catalogGrid);
    }

    private void RefreshReference()
    {
        ReferenceCompositionBlueprint blueprint = _session.Project.ReferenceAnalysis.ActiveBlueprint;
        _blueprintProperties.SelectedObject = string.IsNullOrWhiteSpace(blueprint.Id) ? null : blueprint;
        _referenceTrackGrid.Rows.Clear();
        foreach (ReferenceTrackProfile track in blueprint.Instrumentation.Tracks)
        {
            int row = _referenceTrackGrid.Rows.Add(track.Ignored, track.TrackName, track.InferredRole, track.RoleConfidence,
                track.MidiChannel + 1, track.Bank, track.Program + 1, track.NoteCount, Math.Round(track.AveragePitch, 1), track.PresetName);
            _referenceTrackGrid.Rows[row].Tag = track;
        }
        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(blueprint.Id))
        {
            lines.Add($"Reference: {blueprint.Name}");
            lines.Add($"Source: {blueprint.Source.FileName}");
            lines.Add($"Tracks: {blueprint.Source.TrackCount}; notes: {blueprint.Source.NoteCount}; bars: {blueprint.Source.BarCount}; tempo: {blueprint.Source.PrimaryTempo:0.#}");
            lines.Add($"Confidence: {blueprint.Confidence.Overall:P1}; form {blueprint.Confidence.Form:P1}; harmony {blueprint.Confidence.Harmony:P1}; rhythm {blueprint.Confidence.Rhythm:P1}");
            lines.AddRange(blueprint.Observations.Select(item => "• " + item));
        }
        if (_session.Project.ReferenceAnalysis.RecentDescendants.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("RECENT DESCENDANTS");
            lines.AddRange(_session.Project.ReferenceAnalysis.RecentDescendants.Take(40).Select(item =>
                $"{item.Mode,-18} quality {item.QualityScore:0.000} novelty {item.NoveltyScore:0.000} originality {(item.PassedOriginality ? "PASS" : "FAIL")}"));
        }
        _referenceReport.Text = string.Join(Environment.NewLine, lines);
    }

    private void ConfigureRoleGrid()
    {
        AddCheck(_roleGrid, "Enabled"); AddCheck(_roleGrid, "Locked"); AddText(_roleGrid, "Role", readOnly: true);
        AddText(_roleGrid, "Name"); AddNumber(_roleGrid, "Channel"); AddNumber(_roleGrid, "Program");
        AddNumber(_roleGrid, "Low"); AddNumber(_roleGrid, "High"); AddNumber(_roleGrid, "Density");
        AddText(_roleGrid, "Rhythm"); AddNumber(_roleGrid, "Notes", readOnly: true);
        _roleGrid.CellValueChanged += (_, args) =>
        {
            if (_refreshing || args.RowIndex < 0 || _roleGrid.Rows[args.RowIndex].Tag is not CompositionRoleSettings role) return;
            Safe(() =>
            {
                DataGridViewRow row = _roleGrid.Rows[args.RowIndex];
                _session.Edit("Edit composition role", _ =>
                {
                    role.Enabled = Bool(row, 0); role.Locked = Bool(row, 1); role.Name = Text(row, 3);
                    role.MidiChannel = Clamp(Int(row, 4) - 1, 0, 15); role.GeneralMidiProgram = Clamp(Int(row, 5) - 1, 0, 127);
                    role.MinimumPitch = Clamp(Int(row, 6), 0, 127); role.MaximumPitch = Clamp(Int(row, 7), role.MinimumPitch, 127);
                    role.Density = Clamp(Int(row, 8), 0, 100); role.RhythmPatternId = Text(row, 9);
                });
            });
        };
        _roleGrid.CurrentCellDirtyStateChanged += CommitDirtyCell;
    }

    private void ConfigureVoiceGrid()
    {
        AddCheck(_voiceGrid, "Enabled"); AddCheck(_voiceGrid, "Locked"); AddText(_voiceGrid, "Name");
        AddText(_voiceGrid, "Algorithm", readOnly: true); AddText(_voiceGrid, "Role", readOnly: true);
        AddNumber(_voiceGrid, "Channel"); AddNumber(_voiceGrid, "Program"); AddText(_voiceGrid, "Parameter Preset");
        AddNumber(_voiceGrid, "Notes", readOnly: true);
        _voiceGrid.CellValueChanged += (_, args) =>
        {
            if (_refreshing || args.RowIndex < 0 || _voiceGrid.Rows[args.RowIndex].Tag is not AlgorithmVoiceDefinition voice) return;
            DataGridViewRow row = _voiceGrid.Rows[args.RowIndex];
            Safe(() => _session.Edit("Edit generation voice", _ =>
            {
                voice.Enabled = Bool(row, 0); voice.Locked = Bool(row, 1); voice.Name = Text(row, 2);
                voice.MidiChannel = Clamp(Int(row, 5) - 1, 0, 15); voice.GeneralMidiProgram = Clamp(Int(row, 6) - 1, 0, 127);
                voice.ParameterPresetId = Text(row, 7);
            }));
        };
        _voiceGrid.CurrentCellDirtyStateChanged += CommitDirtyCell;
    }

    private void ConfigureMixerGrid()
    {
        AddCheck(_mixerGrid, "Mute"); AddCheck(_mixerGrid, "Solo"); AddText(_mixerGrid, "Track"); AddText(_mixerGrid, "Role", true);
        AddNumber(_mixerGrid, "Channel"); AddNumber(_mixerGrid, "Bank"); AddNumber(_mixerGrid, "Program");
        AddNumber(_mixerGrid, "Volume %"); AddNumber(_mixerGrid, "Pan %"); AddNumber(_mixerGrid, "Notes", true);
        _mixerGrid.CellValueChanged += (_, args) =>
        {
            if (_refreshing || args.RowIndex < 0 || _mixerGrid.Rows[args.RowIndex].Tag is not SongTrack track) return;
            DataGridViewRow row = _mixerGrid.Rows[args.RowIndex];
            Safe(() => _session.Edit("Edit mixer track", _ =>
            {
                track.Muted = Bool(row, 0); track.Solo = Bool(row, 1); track.Name = Text(row, 2);
                track.MidiChannel = Clamp(Int(row, 4) - 1, 0, 15); track.MidiBank = Clamp(Int(row, 5), 0, 16383);
                track.GeneralMidiProgram = Clamp(Int(row, 6) - 1, 0, 127);
                track.Volume = Clamp(Int(row, 7), 0, 100) / 100d; track.Pan = Clamp(Int(row, 8), -100, 100) / 100d;
            }));
        };
        _mixerGrid.CurrentCellDirtyStateChanged += CommitDirtyCell;
    }

    private void ConfigurePresetGrid()
    {
        AddCheck(_presetGrid, "Enabled"); AddCheck(_presetGrid, "Favorite"); AddCheck(_presetGrid, "Excluded");
        AddText(_presetGrid, "Name"); AddText(_presetGrid, "Family", true); AddNumber(_presetGrid, "Bank", true);
        AddNumber(_presetGrid, "Program", true); AddText(_presetGrid, "Tags");
        _presetGrid.CellValueChanged += (_, args) => UpdatePresetFromRow(_presetGrid, args.RowIndex);
        _presetGrid.CurrentCellDirtyStateChanged += CommitDirtyCell;
    }

    private void ConfigureCatalogGrid()
    {
        AddCheck(_catalogGrid, "Enabled"); AddCheck(_catalogGrid, "Favorite"); AddCheck(_catalogGrid, "Excluded");
        AddText(_catalogGrid, "Name"); AddText(_catalogGrid, "Family", true); AddNumber(_catalogGrid, "Bank", true);
        AddNumber(_catalogGrid, "Program", true); AddText(_catalogGrid, "Tags"); AddNumber(_catalogGrid, "Low"); AddNumber(_catalogGrid, "High");
        _catalogGrid.CellValueChanged += (_, args) => UpdatePresetFromRow(_catalogGrid, args.RowIndex);
        _catalogGrid.CurrentCellDirtyStateChanged += CommitDirtyCell;
    }

    private void ConfigureStyleRolesGrid()
    {
        AddText(_styleRolesGrid, "Role", true); AddText(_styleRolesGrid, "Track", true); AddNumber(_styleRolesGrid, "Channel", true);
        AddNumber(_styleRolesGrid, "Program", true); AddText(_styleRolesGrid, "Range", true); AddNumber(_styleRolesGrid, "Density", true);
        AddText(_styleRolesGrid, "Rhythm", true); AddText(_styleRolesGrid, "Generator", true);
    }

    private void ConfigureReferenceTrackGrid()
    {
        AddCheck(_referenceTrackGrid, "Ignore"); AddText(_referenceTrackGrid, "Track", true);
        DataGridViewComboBoxColumn role = new() { HeaderText = "Inferred Role", DataSource = Enum.GetValues<TrackRole>() };
        _referenceTrackGrid.Columns.Add(role);
        AddText(_referenceTrackGrid, "Confidence", true); AddNumber(_referenceTrackGrid, "Channel", true); AddNumber(_referenceTrackGrid, "Bank", true);
        AddNumber(_referenceTrackGrid, "Program", true); AddNumber(_referenceTrackGrid, "Notes", true); AddNumber(_referenceTrackGrid, "Mean Pitch", true); AddText(_referenceTrackGrid, "Preset", true);
        _referenceTrackGrid.CellValueChanged += (_, args) =>
        {
            if (_refreshing || args.RowIndex < 0 || _referenceTrackGrid.Rows[args.RowIndex].Tag is not ReferenceTrackProfile track) return;
            DataGridViewRow row = _referenceTrackGrid.Rows[args.RowIndex];
            _session.NotifyPropertyGridEdit("Edited reference track classification.");
            track.Ignored = Bool(row, 0);
            if (row.Cells[2].Value is TrackRole selectedRole)
            {
                track.InferredRole = selectedRole;
                track.UserOverrodeRole = true;
            }
        };
        _referenceTrackGrid.CurrentCellDirtyStateChanged += CommitDirtyCell;
    }

    private void UpdatePresetFromRow(DataGridView grid, int rowIndex)
    {
        if (_refreshing || rowIndex < 0 || grid.Rows[rowIndex].Tag is not InstrumentPresetDescriptor preset) return;
        DataGridViewRow row = grid.Rows[rowIndex];
        _session.NotifyPropertyGridEdit("Edited instrument preset catalog.");
        preset.IsEnabled = Bool(row, 0); preset.IsFavorite = Bool(row, 1); preset.IsExcluded = Bool(row, 2);
        preset.Name = Text(row, 3); preset.Tags = Text(row, 7).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (grid.Columns.Count > 8)
        {
            preset.SuggestedLowNote = Clamp(Int(row, 8), 0, 127);
            preset.SuggestedHighNote = Clamp(Int(row, 9), preset.SuggestedLowNote, 127);
        }
    }

    private void NewStructured()
    {
        ulong seed = UlongFromDecimal(_seedBox.Value);
        _session.NewStructured(seed, SelectedStyleId());
    }

    private void NewLab()
    {
        ulong seed = UlongFromDecimal(_seedBox.Value);
        _session.NewLaboratory(seed, SelectedLabPresetId());
    }

    private void OpenProject()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Open Playroma Music Project",
            Filter = "Playroma Music project (*.playroma-music.json;*.json)|*.playroma-music.json;*.json|All files (*.*)|*.*",
            InitialDirectory = Path.Combine(_session.ProjectRoot, "data", "PlayromaMusic")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!PromptSave(this)) return;
        Safe(() => _session.Load(dialog.FileName));
    }

    private void ImportMidi()
    {
        using OpenFileDialog dialog = new() { Title = "Import Standard MIDI", Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!PromptSave(this)) return;
        Safe(() => _session.ImportMidi(dialog.FileName));
    }

    private async Task ExportMidiAsync()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Export Standard MIDI",
            Filter = "MIDI file (*.mid)|*.mid",
            FileName = MusicExportService.SanitizeFileName(_session.Project.Title) + ".mid"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunLongTaskAsync("Exporting MIDI…", () => _exporter.ExportMidi(_session.Project, dialog.FileName),
            result => $"Exported {result.NoteCount} notes to {result.FilePath}");
    }

    private async Task ExportWaveAsync()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Render WAV",
            Filter = "Wave audio (*.wav)|*.wav",
            FileName = MusicExportService.SanitizeFileName(_session.Project.Title) + ".wav"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await RunLongTaskAsync("Rendering WAV…", () => _exporter.ExportWave(_session.Project, dialog.FileName, progress: Progress()),
            result => $"Rendered {result.AudioDuration:mm\\:ss} with {result.BackendName} to {result.OutputPath}");
    }

    private async Task ExportMvAsync()
    {
        using MvMusicExportDialog dialog = new(_session.Project.Title);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ProjectDocument exportProject = _session.CaptureSnapshot();
        exportProject.Audio.LoopEnabled = dialog.MakeSeamlessLoop;
        MvMusicExportRequest request = new(exportProject, _session.ProjectRoot, dialog.FileNameValue,
            dialog.DatabaseKind, dialog.Formats, dialog.SaveAuthoringProject, dialog.AacBitRate, dialog.VorbisQuality);
        await RunLongTaskAsync("Rendering and encoding RPG Maker MV audio…",
            () => _exporter.ExportToMvDatabase(request, progress: Progress()),
            result => "Exported MV audio: " + string.Join(", ", result.AudioFiles.Select(Path.GetFileName)));
    }

    private void BrowseSoundFont()
    {
        using OpenFileDialog dialog = new() { Title = "Choose SoundFont 2", Filter = "SoundFont files (*.sf2;*.sf3)|*.sf2;*.sf3|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _session.Edit("Change SoundFont", project => project.Audio.SoundFontPath = dialog.FileName);
        ScanSoundFont();
    }

    private void UseDefaultSoundFont()
    {
        Safe(() =>
        {
            if (!File.Exists(_session.DefaultSoundFontPath)) throw new FileNotFoundException("The included default SoundFont was not copied to the output folder.", _session.DefaultSoundFontPath);
            _session.Edit("Use included SoundFont", project => project.Audio.SoundFontPath = _session.DefaultSoundFontPath);
            ScanSoundFont();
        });
    }

    private void ScanSoundFont()
    {
        Safe(() =>
        {
            string path = _session.Project.Audio.SoundFontPath;
            InstrumentCatalogDocument catalog = new SoundFontPresetCatalogReader().Read(path);
            _session.Edit("Scan SoundFont preset catalog", project => project.Orchestration.Catalog = catalog);
        });
    }

    private void RegenerateSelectedRole()
    {
        CompositionRoleSettings role = SelectedRole() ?? throw new InvalidOperationException("Select a composition role first.");
        _session.RegenerateRole(role.Id);
    }

    private void RegenerateSelectedVoice()
    {
        AlgorithmVoiceDefinition voice = SelectedVoice() ?? throw new InvalidOperationException("Select a generation-lab voice first.");
        _session.RegenerateVoice(voice.Id);
    }

    private void ApplyHybrid()
    {
        StructuredStyleDefinition primary = SelectedWorkbenchStyle();
        StructuredStyleDefinition secondary = _hybridSecondary.SelectedItem as StructuredStyleDefinition
            ?? throw new InvalidOperationException("Choose a secondary style.");
        if (primary.Id == secondary.Id) throw new InvalidOperationException("Choose two different styles for a hybrid.");
        _session.ApplyHybrid(new StyleBlendSettings
        {
            Enabled = true,
            PrimaryPackId = primary.Id,
            SecondaryPackId = secondary.Id,
            SecondaryWeight = _hybridWeight.Value,
            BlendForms = _blendForms.Checked,
            BlendHarmony = _blendHarmony.Checked,
            BlendRhythms = _blendRhythms.Checked,
            BlendInstruments = _blendInstruments.Checked,
            BlendScoring = _blendScoring.Checked
        });
    }

    private void AnalyzeReference()
    {
        using OpenFileDialog dialog = new() { Title = "Analyze Reference MIDI", Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() => _session.AnalyzeReference(dialog.FileName));
    }


    private void LoadReferenceBlueprint()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Load Reference Blueprint",
            Filter = "Reference blueprint (*.reference.json;*.json)|*.reference.json;*.json|All files (*.*)|*.*",
            InitialDirectory = Path.Combine(_session.ProjectRoot, "data", "PlayromaMusic", "References")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() => _session.LoadBlueprint(dialog.FileName));
    }

    private void SaveReferenceBlueprint()
    {
        ReferenceCompositionBlueprint blueprint = _session.Project.ReferenceAnalysis.ActiveBlueprint;
        if (string.IsNullOrWhiteSpace(blueprint.Id))
        {
            throw new InvalidOperationException("Analyze or load a reference MIDI blueprint first.");
        }

        using SaveFileDialog dialog = new()
        {
            Title = "Save Reference Blueprint",
            Filter = "Reference blueprint (*.reference.json)|*.reference.json|JSON files (*.json)|*.json",
            FileName = MusicExportService.SanitizeFileName(blueprint.Name) + ".reference.json",
            InitialDirectory = Path.Combine(_session.ProjectRoot, "data", "PlayromaMusic", "References")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() => _session.SaveActiveBlueprint(dialog.FileName));
    }

    private void GenerateDescendant()
    {
        ReferenceGenerationMode mode = _descendantMode.SelectedItem is ReferenceGenerationMode selected ? selected : ReferenceGenerationMode.InspiredBy;
        string style = (_descendantStyle.SelectedItem as StructuredStyleDefinition)?.Id ?? string.Empty;
        ReferenceDescendantGenerationResult result = _session.GenerateDescendant(mode, style,
            UlongFromDecimal(_descendantSeed.Value), decimal.ToInt32(_candidateCount.Value));
        _referenceReport.Text = $"Generated {result.Evaluation.Mode}\r\nQuality: {result.Evaluation.QualityScore:0.000}\r\nNovelty: {result.Evaluation.NoveltyScore:0.000}\r\nOriginality: {(result.Evaluation.PassedOriginality ? "PASS" : "FAIL")}";
    }

    private void AnalyzeCorpus()
    {
        using FolderBrowserDialog dialog = new() { Description = "Choose a folder containing MIDI reference files", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() =>
        {
            _lastCorpus = _session.AnalyzeCorpus(dialog.SelectedPath, 3);
            _session.Project.ReferenceAnalysis.Corpus = _lastCorpus.Corpus;
            _session.NotifyPropertyGridEdit("Analyzed reference MIDI corpus.");
            _referenceReport.Text = $"CORPUS: {_lastCorpus.Corpus.Name}\r\nBlueprints: {_lastCorpus.Blueprints.Count}\r\nClusters: {_lastCorpus.Corpus.Clusters.Count}\r\nMean tempo: {_lastCorpus.Corpus.MeanTempo:0.#}\r\nMean bars: {_lastCorpus.Corpus.MeanBars:0.#}\r\nMean confidence: {_lastCorpus.Corpus.MeanConfidence:P1}\r\n\r\n" +
                string.Join("\r\n", _lastCorpus.Diagnostics);
        });
    }

    private void ExportCorpusStyle()
    {
        if (_lastCorpus is null) throw new InvalidOperationException("Analyze a MIDI corpus first.");
        using SaveFileDialog dialog = new()
        {
            Title = "Export Corpus Style Pack",
            Filter = "Playroma style pack (*.style.json)|*.style.json|JSON files (*.json)|*.json",
            FileName = MusicExportService.SanitizeFileName(_lastCorpus.Corpus.Name).ToLowerInvariant() + ".style.json",
            InitialDirectory = Path.Combine(_session.ProjectRoot, "StylePacks")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Safe(() =>
        {
            string styleId = Path.GetFileName(dialog.FileName).Replace(".style.json", string.Empty, StringComparison.OrdinalIgnoreCase);
            string json = ReferenceCorpusStyleExporter.ExportStylePackJson(_lastCorpus.Corpus, _lastCorpus.Blueprints, styleId);
            Directory.CreateDirectory(Path.GetDirectoryName(dialog.FileName)!);
            File.WriteAllText(dialog.FileName, json);
            SetStatus("Exported corpus style pack: " + dialog.FileName);
        });
    }

    private async Task RunLongTaskAsync<T>(string message, Func<T> work, Func<T, string> completion)
    {
        UseWaitCursor = true;
        _progress.Visible = true;
        _progress.Value = 0;
        _toolbar.Enabled = false;
        SetStatus(message);
        try
        {
            T result = await Task.Run(work);
            SetStatus(completion(result));
        }
        catch (Exception exception)
        {
            ShowError(this, message.TrimEnd('…', '.'), exception);
        }
        finally
        {
            _toolbar.Enabled = true;
            _progress.Visible = false;
            UseWaitCursor = false;
        }
    }

    private IProgress<double> Progress() => new Progress<double>(value =>
    {
        int scaled = Clamp(checked((int)Math.Round(value * 1000d)), 0, 1000);
        _progress.Value = scaled;
    });

    private void RefreshPresetGrid(InstrumentCatalogDocument catalog, DataGridView? target = null)
    {
        DataGridView grid = target ?? _presetGrid;
        grid.Rows.Clear();
        foreach (InstrumentPresetDescriptor preset in catalog.Presets.Take(4096))
        {
            object[] values = grid.Columns.Count > 8
                ? [preset.IsEnabled, preset.IsFavorite, preset.IsExcluded, preset.Name, preset.Family, preset.Bank, preset.Program + 1, string.Join(", ", preset.Tags), preset.SuggestedLowNote, preset.SuggestedHighNote]
                : [preset.IsEnabled, preset.IsFavorite, preset.IsExcluded, preset.Name, preset.Family, preset.Bank, preset.Program + 1, string.Join(", ", preset.Tags)];
            int row = grid.Rows.Add(values);
            grid.Rows[row].Tag = preset;
        }
    }

    private void UpdateAudioDiagnostics()
    {
        var diagnostics = _playback.GetDiagnostics();
        string catalog = _session.Project.Orchestration.Catalog.Diagnostics.Count == 0
            ? string.Empty
            : Environment.NewLine + string.Join(Environment.NewLine, _session.Project.Orchestration.Catalog.Diagnostics);
        _audioDiagnostics.Text = diagnostics is null
            ? $"Backend not started. SoundFont: {_session.Project.Audio.SoundFontPath}{catalog}"
            : $"Backend: {diagnostics.BackendName}\r\nQueued: {diagnostics.PreparedBufferCount}; rendered: {diagnostics.RenderedBufferCount}; underruns: {diagnostics.UnderrunCount}{catalog}";
    }


    private void OnPianoRollEditRequested(object? sender, PianoRollEditEventArgs eventArgs)
    {
        PianoRollEditRequest request = eventArgs.Request;
        string label = request.Operation switch
        {
            PianoRollEditOperation.Add => "Add piano-roll note",
            PianoRollEditOperation.Move => "Move piano-roll note",
            PianoRollEditOperation.Delete => "Delete piano-roll note",
            PianoRollEditOperation.ChangeVelocity => "Change piano-roll note velocity",
            _ => "Edit piano-roll note"
        };

        Safe(() => _session.Edit(label, project =>
        {
            SongTrack track = project.Song.Tracks.FirstOrDefault(item =>
                string.Equals(item.Id, request.TrackId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The selected piano-roll track no longer exists.");
            SongClip clip = track.Clips.FirstOrDefault(item =>
                string.Equals(item.Id, request.ClipId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The selected piano-roll clip no longer exists.");

            switch (request.Operation)
            {
                case PianoRollEditOperation.Add:
                {
                    long relativeTick = Math.Clamp(request.AbsoluteTick - clip.StartTick, 0, Math.Max(0, clip.LengthTicks - 1));
                    long snap = Math.Max(1, project.Song.TicksPerQuarterNote / 2L);
                    clip.Notes.Add(new NoteEvent
                    {
                        Id = request.NoteId,
                        Pitch = Math.Clamp(request.Pitch, 0, 127),
                        StartTick = relativeTick,
                        DurationTicks = Math.Min(snap, Math.Max(1, clip.LengthTicks - relativeTick)),
                        Velocity = 88
                    });
                    break;
                }
                case PianoRollEditOperation.Move:
                {
                    NoteEvent note = FindPianoRollNote(clip, request.NoteId);
                    long maximum = Math.Max(0, clip.LengthTicks - Math.Max(1, note.DurationTicks));
                    note.StartTick = Math.Clamp(request.AbsoluteTick - clip.StartTick, 0, maximum);
                    note.Pitch = Math.Clamp(request.Pitch, 0, 127);
                    break;
                }
                case PianoRollEditOperation.Delete:
                {
                    NoteEvent note = FindPianoRollNote(clip, request.NoteId);
                    clip.Notes.Remove(note);
                    break;
                }
                case PianoRollEditOperation.ChangeVelocity:
                {
                    NoteEvent note = FindPianoRollNote(clip, request.NoteId);
                    note.Velocity = Math.Clamp(note.Velocity + request.VelocityDelta, 1, 127);
                    break;
                }
            }

            clip.Notes = clip.Notes.OrderBy(item => item.StartTick).ThenBy(item => item.Pitch).ToList();
        }));
    }

    private static NoteEvent FindPianoRollNote(SongClip clip, string noteId) =>
        clip.Notes.FirstOrDefault(item => string.Equals(item.Id, noteId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException("The selected piano-roll note no longer exists.");

    private void OnPlaybackDiagnosticsChanged(object? sender, EventArgs eventArgs)
    {
        if (IsDisposed) return;
        UpdateAudioDiagnostics();
        UpdateTransportDiagnostics();
    }

    private void OnProjectChanged(object? sender, EventArgs eventArgs)
    {
        _playback.Stop();
        RefreshAll();
    }

    private void WirePropertyGrid(PropertyGrid grid, string label, Action postEdit)
    {
        grid.PropertyValueChanged += (_, _) =>
        {
            if (_refreshing) return;
            _session.NotifyPropertyGridEdit(label);
            postEdit();
        };
    }

    private void SetStatus(string message) => _status.Text = message;

    private void Safe(Action action)
    {
        try { action(); }
        catch (Exception exception) { ShowError(this, "Playroma Music operation failed.", exception); }
    }

    private static void ShowError(IWin32Window owner, string message, Exception exception) => MessageBox.Show(owner,
        message + Environment.NewLine + Environment.NewLine + exception.Message,
        "Playroma Music Studio", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private string SelectedStyleId() => (_styleBox.SelectedItem as StructuredStyleDefinition)?.Id
        ?? StructuredCompositionService.DefaultStyleId;
    private string SelectedLabPresetId() => (_labPresetBox.SelectedItem as LabPresetDefinition)?.Id
        ?? PlayromaMusic.Generators.Lab.LabGenerationService.DefaultPresetId;
    private StructuredStyleDefinition SelectedWorkbenchStyle() => _styleList.SelectedItem as StructuredStyleDefinition
        ?? throw new InvalidOperationException("Choose a style pack first.");
    private CompositionRoleSettings? SelectedRole() => _roleGrid.CurrentRow?.Tag as CompositionRoleSettings;
    private AlgorithmVoiceDefinition? SelectedVoice() => _voiceGrid.CurrentRow?.Tag as AlgorithmVoiceDefinition;

    private static ComboBox DropDown(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
    private static CheckBox Check(string text, bool value) => new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(5, 8, 5, 3) };
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(2) };
    private static Control Labeled(string caption, Control control)
    {
        FlowLayoutPanel panel = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3) };
        panel.Controls.Add(new Label { Text = caption, AutoSize = true });
        panel.Controls.Add(control);
        return panel;
    }
    private static Button Button(string text, Action click)
    {
        Button button = new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(4, 19, 4, 3), Padding = new Padding(6, 2, 6, 2) };
        button.Click += (_, _) => click();
        return button;
    }
    private static TabPage Tab(string name, Control content)
    {
        TabPage page = new(name);
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }
    private static PropertyGrid Inspector() => new() { Dock = DockStyle.Fill, ToolbarVisible = true, HelpVisible = true, PropertySort = PropertySort.Categorized };
    private static TextBox ReadOnlyText() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f) };
    private static DataGridView Grid(bool readOnly = false) => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToOrderColumns = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        ReadOnly = readOnly,
        EditMode = DataGridViewEditMode.EditOnEnter
    };
    private static void AddText(DataGridView grid, string header, bool readOnly = false) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, ReadOnly = readOnly, SortMode = DataGridViewColumnSortMode.Automatic });
    private static void AddNumber(DataGridView grid, string header, bool readOnly = false) => AddText(grid, header, readOnly);
    private static void AddCheck(DataGridView grid, string header) => grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = header, SortMode = DataGridViewColumnSortMode.Automatic });
    private void CommitDirtyCell(object? sender, EventArgs eventArgs)
    {
        if (sender is DataGridView grid && grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
    }
    private static void BindReadOnly<T>(DataGridView grid, IEnumerable<T> values)
    {
        grid.DataSource = new BindingList<T>(values.ToList());
    }
    private static bool Bool(DataGridViewRow row, int column) => Convert.ToBoolean(row.Cells[column].Value ?? false, System.Globalization.CultureInfo.InvariantCulture);
    private static int Int(DataGridViewRow row, int column) => int.TryParse(Convert.ToString(row.Cells[column].Value, System.Globalization.CultureInfo.InvariantCulture), out int value) ? value : 0;
    private static string Text(DataGridViewRow row, int column) => Convert.ToString(row.Cells[column].Value, System.Globalization.CultureInfo.CurrentCulture)?.Trim() ?? string.Empty;
    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    private static decimal ClampDecimal(decimal value, decimal min, decimal max) => Math.Clamp(value, min, max);
    private static decimal DecimalFromUlong(ulong value)
    {
        // WinForms NumericUpDown formats hexadecimal values through Int64.
        // Reinterpret, rather than numerically convert, so seeds with bit 63
        // set remain lossless and never overflow the control's formatter.
        return unchecked((long)value);
    }

    private static ulong UlongFromDecimal(decimal value)
    {
        long signedBits = decimal.ToInt64(value);
        return unchecked((ulong)signedBits);
    }

    private static void SelectById(ComboBox combo, string? id)
    {
        for (int index = 0; index < combo.Items.Count; index++)
        {
            string? itemId = combo.Items[index] switch
            {
                StructuredStyleDefinition style => style.Id,
                LabPresetDefinition preset => preset.Id,
                _ => null
            };
            if (string.Equals(itemId, id, StringComparison.OrdinalIgnoreCase)) { combo.SelectedIndex = index; return; }
        }
    }
    private static void SelectById(ListBox list, string? id)
    {
        for (int index = 0; index < list.Items.Count; index++)
        {
            if (list.Items[index] is StructuredStyleDefinition style && string.Equals(style.Id, id, StringComparison.OrdinalIgnoreCase))
            { list.SelectedIndex = index; return; }
        }
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _session.ProjectChanged -= OnProjectChanged;
            _session.StatusChanged -= SetStatus;
            _playback.DiagnosticsChanged -= OnPlaybackDiagnosticsChanged;
            _pianoRoll.EditRequested -= OnPianoRollEditRequested;
            _playback.Dispose();
        }
        base.Dispose(disposing);
    }
}
