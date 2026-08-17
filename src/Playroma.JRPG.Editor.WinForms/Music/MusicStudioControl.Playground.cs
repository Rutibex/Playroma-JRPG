using System.Security.Cryptography;
using PlayromaMusic.Core.Model;
using PlayromaMusic.Core.MusicTheory;
using PlayromaMusic.Core.Orchestration;
using PlayromaMusic.Core.Persistence;
using PlayromaMusic.Core.StructuredComposition;
using PlayromaMusic.Core.StylePacks;
using PlayromaMusic.Generators.Structured;

namespace Playroma.JRPG.Editor.WinForms.Music;

public sealed partial class MusicStudioControl
{
    private readonly Panel _transportBar = new() { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(8, 5, 8, 5) };
    private readonly Button _transportPlay = new() { Text = "▶ Play", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _transportPause = new() { Text = "Ⅱ Pause", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _transportStop = new() { Text = "■ Stop", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly CheckBox _transportLoop = new() { Text = "Loop", AutoSize = true };
    private readonly ProgressBar _transportProgress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, Height = 10 };
    private readonly Label _transportTitle = new() { Text = "Now Playing: Untitled", AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _transportSection = new() { Text = "Ready", AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _transportTime = new() { Text = "00:00 / 00:00", AutoSize = true, TextAlign = ContentAlignment.MiddleRight };

    // Create / playground
    private readonly FlowLayoutPanel _scenePanel = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = true,
        Padding = new Padding(4)
    };
    private readonly ComboBox _moodBox = DropDown(220);
    private readonly ComboBox _soundWorldBox = DropDown(220);
    private readonly ComboBox _lengthBox = DropDown(220);
    private readonly TrackBar _energySlider = new() { Minimum = 0, Maximum = 4, Value = 2, TickStyle = TickStyle.BottomRight, Width = 220 };
    private readonly TrackBar _complexitySlider = new() { Minimum = 0, Maximum = 3, Value = 1, TickStyle = TickStyle.BottomRight, Width = 220 };
    private readonly CheckBox _recipeLoop = new() { Text = "Make it a seamless game loop", Checked = true, AutoSize = true };
    private readonly Label _recipeSummary = new() { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(12), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _nowPlaying = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11f, FontStyle.Bold) };
    private readonly MusicPlaygroundPreviewControl _playgroundPreview = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _recentVersionStrip = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(2) };
    private readonly Dictionary<string, Button> _sceneButtons = new(StringComparer.OrdinalIgnoreCase);
    private MusicScenePreset _selectedScene = MusicPlaygroundCatalog.Scenes.First(scene => scene.Id == "town");
    private bool _playgroundChoicesPopulated;

    // Arrange
    private readonly MusicSectionTimelineControl _sectionTimeline = new() { Dock = DockStyle.Fill };
    private readonly FlowLayoutPanel _instrumentGroups = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        WrapContents = false,
        FlowDirection = FlowDirection.TopDown,
        Padding = new Padding(4)
    };
    private readonly Label _selectedSectionLabel = new() { AutoSize = true, Text = "Select a section above." };

    // Library
    private readonly ListView _versionList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false
    };
    private readonly TextBox _styleSearch = new() { Width = 260, PlaceholderText = "Search styles…" };
    private readonly ListBox _libraryStyleList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label _libraryStyleDescription = new() { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(8), AutoEllipsis = true };
    private readonly List<MusicVersionSnapshot> _versions = [];

    private sealed class MusicVersionSnapshot
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public required string StyleName { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required ProjectDocument Project { get; init; }
        public bool Favorite { get; set; }
        public string? FilePath { get; set; }
    }

    private sealed record InstrumentChoice(string Name, int Program);

    private enum QuickMusicChange
    {
        MoreEpic,
        MoreMysterious,
        Faster,
        Slower,
        Simpler,
        Busier,
        CatchierMelody,
        HeavierDrums,
        MoreRetro,
        MoreEmotional,
        BigFinish,
        ChangeInstruments,
        Remix,
        SmoothLoop
    }

    private TabPage BuildCreateTab()
    {
        TabPage page = new("Create");
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));

        Label heading = new()
        {
            Text = "MAKE MUSIC FOR YOUR GAME",
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 16f, FontStyle.Bold),
            Padding = new Padding(4, 2, 4, 8)
        };
        root.Controls.Add(heading, 0, 0);

        SplitContainer main = new() { Dock = DockStyle.Fill, SplitterDistance = 390, FixedPanel = FixedPanel.Panel1 };
        GroupBox scenes = new() { Text = "1. What is happening in the game?", Dock = DockStyle.Fill, Padding = new Padding(8) };
        BuildSceneButtons();
        scenes.Controls.Add(_scenePanel);
        main.Panel1.Controls.Add(scenes);

        // Keep the generation controls in a fixed, always-visible row.  The previous
        // AutoSize row could collapse to zero height on some Windows scaling/layout
        // combinations, leaving the Playground with no visible way to create music.
        TableLayoutPanel creator = new() { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(8, 0, 0, 0) };
        creator.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        creator.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        creator.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        creator.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        creator.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        GroupBox recipe = new() { Text = "2. Choose a simple music recipe", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10) };
        TableLayoutPanel recipeGrid = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 3 };
        recipeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        recipeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        recipeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        recipeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        recipeGrid.Controls.Add(Labeled("Mood", _moodBox), 0, 0);
        recipeGrid.Controls.Add(Labeled("Sound", _soundWorldBox), 1, 0);
        recipeGrid.Controls.Add(Labeled("Length", _lengthBox), 2, 0);
        recipeGrid.Controls.Add(Labeled("Energy: calm → intense", _energySlider), 0, 1);
        recipeGrid.Controls.Add(Labeled("Complexity: minimal → rich", _complexitySlider), 1, 1);
        recipeGrid.Controls.Add(_recipeLoop, 2, 1);
        recipeGrid.SetColumnSpan(_recipeLoop, 2);
        _recipeSummary.BackColor = Color.FromArgb(244, 247, 252);
        recipeGrid.Controls.Add(_recipeSummary, 0, 2);
        recipeGrid.SetColumnSpan(_recipeSummary, 4);
        recipe.Controls.Add(recipeGrid);
        creator.Controls.Add(recipe, 0, 0);

        FlowLayoutPanel primaryButtons = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 5, 0, 3),
            BackColor = Color.FromArgb(232, 240, 255)
        };
        Button makeMusic = BigActionButton("✨  MAKE MUSIC", () => Safe(() => GenerateFromPlayground(surprise: false, playAfter: true)));
        makeMusic.BackColor = Color.FromArgb(215, 236, 214);
        makeMusic.UseVisualStyleBackColor = false;
        makeMusic.AccessibleDescription = "Generate a completely new song using the selected scene and recipe.";
        Button surpriseMe = BigActionButton("🎲  SURPRISE ME", () => Safe(() => GenerateFromPlayground(surprise: true, playAfter: true)));
        surpriseMe.AccessibleDescription = "Choose a random recipe and generate a completely new song.";
        Button newVersion = BigActionButton("↻  NEW VERSION", () => Safe(() => GenerateFromPlayground(surprise: false, playAfter: true)));
        newVersion.AccessibleDescription = "Generate another new song using the same recipe.";
        Button keepThis = BigActionButton("★  KEEP THIS ONE", () => Safe(KeepCurrentVersion));
        keepThis.AccessibleDescription = "Save the current song as a favorite in the Library.";
        primaryButtons.Controls.AddRange([makeMusic, surpriseMe, newVersion, keepThis]);
        creator.Controls.Add(primaryButtons, 0, 1);

        creator.Controls.Add(_playgroundPreview, 0, 2);

        _nowPlaying.Text = "Now playing: choose a scene and press Make Music";
        creator.Controls.Add(_nowPlaying, 0, 3);
        FlowLayoutPanel historyRow = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        historyRow.Controls.Add(new Label { Text = "Recent:", AutoSize = true, Margin = new Padding(2, 9, 5, 0) });
        historyRow.Controls.Add(_recentVersionStrip);
        creator.Controls.Add(historyRow, 0, 4);
        main.Panel2.Controls.Add(creator);
        root.Controls.Add(main, 0, 1);

        GroupBox changes = new() { Text = "3. Press fun buttons until it feels right", Dock = DockStyle.Fill, Padding = new Padding(8) };
        FlowLayoutPanel quick = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        quick.Controls.AddRange([
            QuickButton("More Epic", QuickMusicChange.MoreEpic),
            QuickButton("More Mysterious", QuickMusicChange.MoreMysterious),
            QuickButton("Faster", QuickMusicChange.Faster),
            QuickButton("Slower", QuickMusicChange.Slower),
            QuickButton("Simpler", QuickMusicChange.Simpler),
            QuickButton("Busier", QuickMusicChange.Busier),
            QuickButton("Catchier Melody", QuickMusicChange.CatchierMelody),
            QuickButton("Heavier Drums", QuickMusicChange.HeavierDrums),
            QuickButton("More Retro", QuickMusicChange.MoreRetro),
            QuickButton("More Emotional", QuickMusicChange.MoreEmotional),
            QuickButton("Big Finish", QuickMusicChange.BigFinish),
            QuickButton("Change Instruments", QuickMusicChange.ChangeInstruments),
            QuickButton("Remix This", QuickMusicChange.Remix),
            QuickButton("Smoother Loop", QuickMusicChange.SmoothLoop)
        ]);
        changes.Controls.Add(quick);
        root.Controls.Add(changes, 0, 2);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildArrangeTab()
    {
        TabPage page = new("Arrange");
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        GroupBox structure = new() { Text = "Song Structure — click a section to work on it", Dock = DockStyle.Fill, Padding = new Padding(8) };
        structure.Controls.Add(_sectionTimeline);
        root.Controls.Add(structure, 0, 0);

        FlowLayoutPanel sectionCommands = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(2) };
        sectionCommands.Controls.Add(_selectedSectionLabel);
        sectionCommands.Controls.AddRange([
            Button("Make Section Stronger", () => Safe(() => AdjustSelectedSection(0.16, 12))),
            Button("Make Section Calmer", () => Safe(() => AdjustSelectedSection(-0.16, -12))),
            Button("Duplicate Section", () => Safe(DuplicateSelectedSection)),
            Button("Add Gentle Intro", () => Safe(AddGentleIntro)),
            Button("Add Climax", () => Safe(AddClimax)),
            Button("New Song Structure", () => Safe(() => { _session.RegenerateStructure(); AddVersion("New song structure"); })),
            Button("Make Seamless Loop", () => Safe(() => ApplyQuickChange(QuickMusicChange.SmoothLoop)))
        ]);
        root.Controls.Add(sectionCommands, 0, 1);

        GroupBox instruments = new() { Text = "Instrument Groups — simple controls for what you hear", Dock = DockStyle.Fill, Padding = new Padding(8) };
        instruments.Controls.Add(_instrumentGroups);
        root.Controls.Add(instruments, 0, 2);
        page.Controls.Add(root);

        _sectionTimeline.SelectedSectionChanged += (_, _) => RefreshSelectedSectionLabel();
        return page;
    }

    private TabPage BuildLibraryTab()
    {
        TabPage page = new("Library");
        SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 720 };

        TableLayoutPanel versions = new() { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        versions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        versions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        versions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        versions.Controls.Add(new Label
        {
            Text = "RECENT VERSIONS AND FAVORITES",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 13f, FontStyle.Bold),
            Padding = new Padding(0, 0, 0, 8)
        }, 0, 0);
        _versionList.Columns.Add("Song", 260);
        _versionList.Columns.Add("What changed", 190);
        _versionList.Columns.Add("Style", 190);
        _versionList.Columns.Add("Time", 90);
        _versionList.DoubleClick += (_, _) => Safe(RestoreSelectedVersion);
        versions.Controls.Add(_versionList, 0, 1);
        FlowLayoutPanel versionButtons = Flow();
        versionButtons.Controls.AddRange([
            Button("Restore Selected", () => Safe(RestoreSelectedVersion)),
            Button("Keep as Favorite", () => Safe(FavoriteSelectedVersion)),
            Button("Delete from History", () => Safe(DeleteSelectedVersion))
        ]);
        versions.Controls.Add(versionButtons, 0, 2);
        split.Panel1.Controls.Add(versions);

        TableLayoutPanel styles = new() { Dock = DockStyle.Fill, RowCount = 5, Padding = new Padding(10) };
        styles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        styles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        styles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        styles.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        styles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        styles.Controls.Add(new Label
        {
            Text = "STYLE BROWSER",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 13f, FontStyle.Bold)
        }, 0, 0);
        styles.Controls.Add(_styleSearch, 0, 1);
        _styleSearch.TextChanged += (_, _) => RefreshLibraryStyles();
        _libraryStyleList.DisplayMember = nameof(StructuredStyleDefinition.Name);
        _libraryStyleList.SelectedIndexChanged += (_, _) => RefreshLibraryStyleDescription();
        styles.Controls.Add(_libraryStyleList, 0, 2);
        FlowLayoutPanel styleButtons = Flow();
        styleButtons.Controls.AddRange([
            Button("Make New Song in This Style", () => Safe(GenerateSelectedLibraryStyle)),
            Button("Apply Style to Current Song", () => Safe(ApplySelectedLibraryStyle))
        ]);
        styles.Controls.Add(_libraryStyleDescription, 0, 3);
        styles.Controls.Add(styleButtons, 0, 4);
        split.Panel2.Controls.Add(styles);
        page.Controls.Add(split);
        return page;
    }

    private TabPage BuildAdvancedToolsTab()
    {
        TabPage page = new("Advanced");
        TabControl advanced = new() { Dock = DockStyle.Fill, Multiline = true };
        advanced.TabPages.Add(BuildLabTab());
        advanced.TabPages.Add(BuildStudioTab());
        advanced.TabPages.Add(BuildStyleTab());
        advanced.TabPages.Add(BuildAdvancedTab());
        advanced.TabPages.Add(BuildOrchestrationTab());
        advanced.TabPages.Add(BuildReferenceTab());
        page.Controls.Add(advanced);
        return page;
    }

    private void BuildTransportBar()
    {
        _transportBar.BackColor = Color.FromArgb(239, 242, 247);
        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(_transportTitle, 0, 0);
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.AddRange([_transportPlay, _transportPause, _transportStop]);
        layout.Controls.Add(buttons, 1, 0);
        FlowLayoutPanel settings = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        settings.Controls.Add(_transportTime);
        settings.Controls.Add(_transportSection);
        settings.Controls.Add(new Label { Text = "Volume", AutoSize = true, Margin = new Padding(12, 7, 2, 0) });
        settings.Controls.Add(_masterVolume);
        settings.Controls.Add(_transportLoop);
        layout.Controls.Add(settings, 2, 0);
        layout.Controls.Add(_transportProgress, 0, 1);
        layout.SetColumnSpan(_transportProgress, 3);
        _transportBar.Controls.Add(layout);

        _transportPlay.Click += (_, _) => Safe(() => _playback.Play(_session.Project));
        _transportPause.Click += (_, _) => Safe(_playback.Pause);
        _transportStop.Click += (_, _) => Safe(() => { _playback.Stop(); UpdateTransportDiagnostics(); });
        _transportLoop.CheckedChanged += (_, _) =>
        {
            if (_refreshing) return;
            _session.Edit("Change loop mode", project => project.Audio.LoopEnabled = _transportLoop.Checked);
        };
        _masterVolume.ValueChanged += (_, _) =>
        {
            if (_refreshing) return;
            _session.Edit("Change master volume", project => project.Audio.MasterVolume = decimal.ToDouble(_masterVolume.Value) / 100d);
        };
    }

    private void PopulatePlaygroundChoices()
    {
        if (_playgroundChoicesPopulated) return;
        _playgroundChoicesPopulated = true;

        _moodBox.Items.AddRange(Enum.GetValues<PlaygroundMood>().Cast<object>().ToArray());
        _soundWorldBox.Items.AddRange(Enum.GetValues<PlaygroundSoundWorld>().Cast<object>().ToArray());
        _lengthBox.Items.AddRange(Enum.GetValues<PlaygroundLength>().Cast<object>().ToArray());
        _moodBox.Format += (_, e) => { if (e.ListItem is PlaygroundMood value) e.Value = MusicPlaygroundCatalog.FriendlyName(value); };
        _soundWorldBox.Format += (_, e) => { if (e.ListItem is PlaygroundSoundWorld value) e.Value = MusicPlaygroundCatalog.FriendlyName(value); };
        _lengthBox.Format += (_, e) => { if (e.ListItem is PlaygroundLength value) e.Value = MusicPlaygroundCatalog.FriendlyName(value); };

        _moodBox.SelectedItem = _selectedScene.DefaultMood;
        _soundWorldBox.SelectedItem = PlaygroundSoundWorld.ClassicJrpg;
        _lengthBox.SelectedItem = PlaygroundLength.NormalLoop;
        _energySlider.Value = _selectedScene.DefaultEnergy;
        _complexitySlider.Value = 1;

        _moodBox.SelectedIndexChanged += (_, _) => UpdateRecipeSummary();
        _soundWorldBox.SelectedIndexChanged += (_, _) => UpdateRecipeSummary();
        _lengthBox.SelectedIndexChanged += (_, _) =>
        {
            _recipeLoop.Enabled = SelectedLength() != PlaygroundLength.Jingle;
            if (!_recipeLoop.Enabled) _recipeLoop.Checked = false;
            UpdateRecipeSummary();
        };
        _energySlider.ValueChanged += (_, _) => UpdateRecipeSummary();
        _complexitySlider.ValueChanged += (_, _) => UpdateRecipeSummary();
        _recipeLoop.CheckedChanged += (_, _) => UpdateRecipeSummary();
    }

    private void InitializePlayground()
    {
        SelectScene(_selectedScene, applyDefaults: true);
        AddVersion("Starting song", favorite: false);
        LoadSavedFavorites();
        RefreshPlayground();
        RefreshLibrary();
    }

    private void ResetPlaygroundRecipe()
    {
        MusicScenePreset scene = MusicPlaygroundCatalog.Scenes.First(item => item.Id == "town");
        SelectScene(scene, applyDefaults: true);
        _soundWorldBox.SelectedItem = PlaygroundSoundWorld.ClassicJrpg;
        _lengthBox.SelectedItem = PlaygroundLength.NormalLoop;
        _complexitySlider.Value = 1;
        _recipeLoop.Checked = true;
        UpdateRecipeSummary();
    }

    private void BuildSceneButtons()
    {
        if (_sceneButtons.Count > 0) return;
        foreach (MusicScenePreset scene in MusicPlaygroundCatalog.Scenes)
        {
            Button button = new()
            {
                Text = $"{scene.Glyph}  {scene.Name}\r\n{scene.Description}",
                Tag = scene,
                Width = 170,
                Height = 78,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8),
                Margin = new Padding(4),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = SystemColors.ControlLightLight,
                AccessibleName = scene.Name,
                AccessibleDescription = scene.Description
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(185, 191, 204);
            button.Click += (_, _) => SelectScene(scene, applyDefaults: true);
            _sceneButtons[scene.Id] = button;
            _scenePanel.Controls.Add(button);
        }
    }

    private void SelectScene(MusicScenePreset scene, bool applyDefaults)
    {
        _selectedScene = scene;
        foreach ((string id, Button button) in _sceneButtons)
        {
            bool selected = string.Equals(id, scene.Id, StringComparison.OrdinalIgnoreCase);
            button.BackColor = selected ? Color.FromArgb(218, 231, 255) : SystemColors.ControlLightLight;
            button.FlatAppearance.BorderSize = selected ? 2 : 1;
            button.FlatAppearance.BorderColor = selected ? Color.FromArgb(62, 104, 190) : Color.FromArgb(185, 191, 204);
        }
        if (applyDefaults && _playgroundChoicesPopulated)
        {
            _moodBox.SelectedItem = scene.DefaultMood;
            _energySlider.Value = Math.Clamp(scene.DefaultEnergy, _energySlider.Minimum, _energySlider.Maximum);
            _lengthBox.SelectedItem = scene.Id is "victory" or "defeat" ? PlaygroundLength.Jingle : PlaygroundLength.NormalLoop;
            _recipeLoop.Checked = scene.Id is not ("victory" or "defeat");
        }
        _playgroundPreview.SetProject(_session.Project, _selectedScene);
        UpdateRecipeSummary();
    }

    private void UpdateRecipeSummary()
    {
        if (!_playgroundChoicesPopulated) return;
        string energy = _energySlider.Value switch
        {
            0 => "ambient",
            1 => "gentle",
            2 => "active",
            3 => "intense",
            _ => "overwhelming"
        };
        string complexity = _complexitySlider.Value switch
        {
            0 => "minimal",
            1 => "simple",
            2 => "rich",
            _ => "wild"
        };
        _recipeSummary.Text =
            $"YOUR MUSIC RECIPE   •   {_selectedScene.Name}   •   {MusicPlaygroundCatalog.FriendlyName(SelectedMood())}   •   " +
            $"{MusicPlaygroundCatalog.FriendlyName(SelectedSoundWorld())}   •   {energy}, {complexity}   •   " +
            $"{MusicPlaygroundCatalog.FriendlyName(SelectedLength())}" + (_recipeLoop.Checked ? "   •   seamless loop" : string.Empty);
    }

    private void GenerateFromPlayground(bool surprise, bool playAfter)
    {
        if (surprise)
        {
            MusicScenePreset scene = MusicPlaygroundCatalog.Scenes[Random.Shared.Next(MusicPlaygroundCatalog.Scenes.Count)];
            SelectScene(scene, applyDefaults: false);
            _moodBox.SelectedItem = Enum.GetValues<PlaygroundMood>()[Random.Shared.Next(Enum.GetValues<PlaygroundMood>().Length)];
            PlaygroundSoundWorld[] worlds = Enum.GetValues<PlaygroundSoundWorld>();
            _soundWorldBox.SelectedItem = worlds[Random.Shared.Next(worlds.Length)];
            _energySlider.Value = Random.Shared.Next(_energySlider.Minimum, _energySlider.Maximum + 1);
            _complexitySlider.Value = Random.Shared.Next(_complexitySlider.Minimum, _complexitySlider.Maximum + 1);
            _lengthBox.SelectedItem = _selectedScene.Id is "victory" or "defeat" ? PlaygroundLength.Jingle :
                (Random.Shared.NextDouble() < 0.25 ? PlaygroundLength.ShortScene : PlaygroundLength.NormalLoop);
            _recipeLoop.Checked = SelectedLength() != PlaygroundLength.Jingle;
        }

        string styleId = ResolveRecipeStyle();
        ulong seed = CreateRandomSeed();
        string title = BuildGeneratedTitle(seed, _selectedScene, SelectedMood());
        PlaygroundMood mood = SelectedMood();
        PlaygroundSoundWorld sound = SelectedSoundWorld();
        PlaygroundLength length = SelectedLength();
        int energy = _energySlider.Value;
        int complexity = _complexitySlider.Value;
        bool loop = _recipeLoop.Checked && length != PlaygroundLength.Jingle;

        _session.NewStructuredConfiguredAndRebuilt(
            seed,
            styleId,
            project => ConfigurePlaygroundRecipe(project, _selectedScene, mood, sound, length, energy, complexity, loop, title),
            project => FinalizePlaygroundRecipe(project, length, energy),
            $"Created {_selectedScene.Name.ToLowerInvariant()} music");
        AddVersion(surprise ? "Surprise creation" : "New version");
        SetStatus($"Your {_selectedScene.Name.ToLowerInvariant()} theme is ready. Press the change buttons to shape it.");
        if (playAfter) _playback.Play(_session.Project);
    }

    private string ResolveRecipeStyle()
    {
        HashSet<string> available = StructuredStyleLibrary.All.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return MusicPlaygroundCatalog.CandidateStyles(_selectedScene, SelectedMood(), SelectedSoundWorld())
            .FirstOrDefault(available.Contains)
            ?? StructuredCompositionService.DefaultStyleId;
    }

    private static void ConfigurePlaygroundRecipe(
        ProjectDocument project,
        MusicScenePreset scene,
        PlaygroundMood mood,
        PlaygroundSoundWorld sound,
        PlaygroundLength length,
        int energy,
        int complexity,
        bool loop,
        string title)
    {
        project.Title = title;
        project.Song.Title = title;
        project.Audio.LoopEnabled = loop;
        project.Tags.RemoveAll(tag => tag.StartsWith("playground:", StringComparison.OrdinalIgnoreCase));
        project.Tags.Add($"playground:scene={scene.Id}");
        project.Tags.Add($"playground:mood={mood}");
        project.Tags.Add($"playground:sound={sound}");
        project.Tags.Add($"playground:length={length}");
        project.Tags.Add($"playground:energy={energy}");
        project.Tags.Add($"playground:complexity={complexity}");

        if (project.Song.TempoMap.Count == 0) project.Song.TempoMap.Add(new TempoChange { Tick = 0, BeatsPerMinute = 120d });
        double moodTempo = mood switch
        {
            PlaygroundMood.Peaceful => 0.88,
            PlaygroundMood.Sad => 0.82,
            PlaygroundMood.Romantic => 0.9,
            PlaygroundMood.Urgent => 1.16,
            PlaygroundMood.Cheerful => 1.06,
            _ => 1d
        };
        double energyTempo = 0.76 + energy * 0.13;
        project.Song.TempoMap[0].BeatsPerMinute = Math.Clamp(project.Song.TempoMap[0].BeatsPerMinute * moodTempo * energyTempo, 44d, 220d);
        project.Song.Scale = mood switch
        {
            PlaygroundMood.Peaceful => ScaleKind.MajorPentatonic,
            PlaygroundMood.Cheerful => ScaleKind.Major,
            PlaygroundMood.Mysterious => ScaleKind.HarmonicMinor,
            PlaygroundMood.Dark => ScaleKind.Phrygian,
            PlaygroundMood.Heroic => ScaleKind.Mixolydian,
            PlaygroundMood.Sad => ScaleKind.NaturalMinor,
            PlaygroundMood.Magical => ScaleKind.Lydian,
            PlaygroundMood.Romantic => ScaleKind.Major,
            PlaygroundMood.Playful => ScaleKind.MajorPentatonic,
            PlaygroundMood.Urgent => ScaleKind.HarmonicMinor,
            _ => project.Song.Scale
        };

        int densityOffset = ((complexity - 1) * 15) + ((energy - 2) * 5);
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            role.Density = Math.Clamp(role.Density + densityOffset, 8, 100);
            if (complexity == 0 && role.Role is TrackRole.Arpeggio or TrackRole.CounterMelody or TrackRole.Texture) role.Enabled = false;
            if (complexity == 1 && role.Role == TrackRole.CounterMelody) role.Enabled = false;
            if (energy >= 3 && role.Role is TrackRole.Drums or TrackRole.Bass or TrackRole.Chords) role.Enabled = true;

            SongTrack? roleTrack = StructuredCompositionService.GetTrack(project, role.Id);
            if (roleTrack is not null) roleTrack.Muted = !role.Enabled;
        }

        ApplySoundWorld(project, sound);
    }

    private static void FinalizePlaygroundRecipe(ProjectDocument project, PlaygroundLength length, int energy)
    {
        foreach (SongSection section in project.Song.Sections)
        {
            section.Energy = Math.Clamp(section.Energy * 0.6 + energy / 5d, 0.05, 1d);
        }

        int velocityDelta = (energy - 2) * 7;
        foreach (AbsoluteNote absolute in project.Song.EnumerateNotes())
        {
            absolute.Note.Velocity = Math.Clamp(absolute.Note.Velocity + velocityDelta, 18, 127);
        }

        double lengthFactor = length switch
        {
            PlaygroundLength.Jingle => 0.42,
            PlaygroundLength.ShortScene => 0.7,
            PlaygroundLength.NormalLoop => 1d,
            PlaygroundLength.LongTheme => 1.35,
            _ => 1d
        };
        if (Math.Abs(lengthFactor - 1d) > 0.001) ScaleTimeline(project, lengthFactor);
        TrimNotesToSong(project);
    }

    private static void ApplySoundWorld(ProjectDocument project, PlaygroundSoundWorld sound)
    {
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles)
        {
            int? program = ProgramForSoundWorld(sound, role.Role);
            if (program is null) continue;
            ApplyRoleInstrument(project, role, program.Value,
                $"{MusicPlaygroundCatalog.FriendlyName(sound)} GM {program.Value + 1}");
        }

        if (sound is PlaygroundSoundWorld.Piano or PlaygroundSoundWorld.MusicBox)
        {
            foreach (CompositionRoleSettings drums in project.StructuredComposition.Roles.Where(item => item.Role == TrackRole.Drums))
            {
                drums.Enabled = false;
                SongTrack? track = StructuredCompositionService.GetTrack(project, drums.Id);
                if (track is not null) track.Muted = true;
            }
        }
    }

    private static void ApplyRoleInstrument(
        ProjectDocument project,
        CompositionRoleSettings role,
        int program,
        string presetName)
    {
        role.GeneralMidiProgram = Math.Clamp(program, 0, 127);
        InstrumentPresetDescriptor preset = ResolvePlaygroundPreset(project, role, role.GeneralMidiProgram, presetName);

        foreach (SectionOrchestrationPlan sectionPlan in project.Orchestration.SectionPlans)
        {
            foreach (InstrumentAssignmentPlan assignment in sectionPlan.Assignments.Where(item => item.RoleId == role.Id))
            {
                assignment.Bank = preset.Bank;
                assignment.Program = preset.Program;
                assignment.PresetKey = preset.Key;
                assignment.PresetName = preset.Name;
                assignment.SoundFontId = preset.SoundFontId;
            }
        }

        SongTrack? track = StructuredCompositionService.GetTrack(project, role.Id);
        if (track is null) return;
        track.MidiBank = preset.Bank;
        track.GeneralMidiProgram = preset.Program;
        track.PresetKey = preset.Key;
        track.SoundFontId = preset.SoundFontId;
        foreach (ProgramChangeEvent programChange in track.Clips.SelectMany(clip => clip.ProgramChanges))
        {
            programChange.Bank = preset.Bank;
            programChange.Program = preset.Program;
            programChange.PresetKey = preset.Key;
            programChange.PresetName = preset.Name;
        }
    }

    private static InstrumentPresetDescriptor ResolvePlaygroundPreset(
        ProjectDocument project,
        CompositionRoleSettings role,
        int program,
        string fallbackName)
    {
        ProceduralOrchestrationDocument orchestration = project.Orchestration ??= new ProceduralOrchestrationDocument();
        orchestration.Catalog ??= PresetCatalogFactory.CreateGeneralMidiFallback();
        InstrumentCatalogDocument catalog = orchestration.Catalog;
        int bank = role.IsPercussion ? 128 : 0;

        InstrumentPresetDescriptor? preset = catalog.Presets.FirstOrDefault(item =>
            item.IsEnabled && !item.IsExcluded && item.IsDrumKit == role.IsPercussion &&
            item.Bank == bank && item.Program == program);
        preset ??= catalog.Presets.FirstOrDefault(item =>
            item.IsDrumKit == role.IsPercussion && item.Bank == bank && item.Program == program);
        if (preset is not null) return preset;

        InstrumentCatalogDocument generalMidi = PresetCatalogFactory.CreateGeneralMidiFallback();
        InstrumentPresetDescriptor fallback = generalMidi.Presets.FirstOrDefault(item =>
                item.IsDrumKit == role.IsPercussion && item.Bank == bank && item.Program == program)
            ?? PresetCatalogFactory.Classify(
                "gm-fallback",
                "General MIDI complete fallback",
                bank,
                program,
                fallbackName,
                role.IsPercussion);

        InstrumentPresetDescriptor? existing = catalog.Presets.FirstOrDefault(item =>
            item.Key.Equals(fallback.Key, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        fallback = fallback.Clone();
        catalog.Presets.Add(fallback);
        catalog.Diagnostics.Add($"Added General MIDI fallback preset '{fallback.Name}' for the Music Playground sound-world recipe.");
        return fallback;
    }

    private static int? ProgramForSoundWorld(PlaygroundSoundWorld sound, TrackRole role) => sound switch
    {
        PlaygroundSoundWorld.EightBit => role switch
        {
            TrackRole.Lead => 80,
            TrackRole.CounterMelody => 81,
            TrackRole.Chords or TrackRole.Arpeggio => 82,
            TrackRole.Bass => 38,
            TrackRole.Texture => 88,
            _ => null
        },
        PlaygroundSoundWorld.SixteenBit => role switch
        {
            TrackRole.Lead => 81,
            TrackRole.CounterMelody => 84,
            TrackRole.Chords or TrackRole.Arpeggio => 89,
            TrackRole.Bass => 39,
            TrackRole.Texture => 94,
            _ => null
        },
        PlaygroundSoundWorld.Orchestral => role switch
        {
            TrackRole.Lead => 40,
            TrackRole.CounterMelody => 73,
            TrackRole.Chords => 48,
            TrackRole.Arpeggio => 46,
            TrackRole.Bass => 43,
            TrackRole.Texture => 52,
            _ => null
        },
        PlaygroundSoundWorld.Piano => role switch
        {
            TrackRole.Lead or TrackRole.Chords or TrackRole.Arpeggio or TrackRole.CounterMelody => 0,
            TrackRole.Bass => 32,
            TrackRole.Texture => 48,
            _ => null
        },
        PlaygroundSoundWorld.MusicBox => role switch
        {
            TrackRole.Lead or TrackRole.Arpeggio => 10,
            TrackRole.Chords => 8,
            TrackRole.Bass => 32,
            TrackRole.CounterMelody => 9,
            TrackRole.Texture => 88,
            _ => null
        },
        PlaygroundSoundWorld.Rock => role switch
        {
            TrackRole.Lead => 29,
            TrackRole.CounterMelody => 30,
            TrackRole.Chords or TrackRole.Arpeggio => 27,
            TrackRole.Bass => 34,
            TrackRole.Texture => 89,
            _ => null
        },
        PlaygroundSoundWorld.Electronic => role switch
        {
            TrackRole.Lead => 81,
            TrackRole.CounterMelody => 84,
            TrackRole.Chords => 90,
            TrackRole.Arpeggio => 87,
            TrackRole.Bass => 38,
            TrackRole.Texture => 95,
            _ => null
        },
        PlaygroundSoundWorld.Ancient => role switch
        {
            TrackRole.Lead => 73,
            TrackRole.CounterMelody => 74,
            TrackRole.Chords => 48,
            TrackRole.Arpeggio => 46,
            TrackRole.Bass => 42,
            TrackRole.Texture => 52,
            _ => null
        },
        PlaygroundSoundWorld.Folk => role switch
        {
            TrackRole.Lead => 40,
            TrackRole.CounterMelody => 73,
            TrackRole.Chords or TrackRole.Arpeggio => 24,
            TrackRole.Bass => 32,
            TrackRole.Texture => 75,
            _ => null
        },
        PlaygroundSoundWorld.Haunted => role switch
        {
            TrackRole.Lead => 19,
            TrackRole.CounterMelody => 52,
            TrackRole.Chords => 48,
            TrackRole.Arpeggio => 10,
            TrackRole.Bass => 43,
            TrackRole.Texture => 99,
            _ => null
        },
        PlaygroundSoundWorld.Dreamlike => role switch
        {
            TrackRole.Lead => 88,
            TrackRole.CounterMelody => 10,
            TrackRole.Chords => 89,
            TrackRole.Arpeggio => 98,
            TrackRole.Bass => 39,
            TrackRole.Texture => 94,
            _ => null
        },
        _ => null
    };

    private static void ScaleTimeline(ProjectDocument project, double factor)
    {
        static long Scale(long value, double multiplier, bool allowZero) =>
            Math.Max(allowZero ? 0L : 1L, (long)Math.Round(value * multiplier, MidpointRounding.AwayFromZero));

        foreach (SongSection section in project.Song.Sections)
        {
            long scaledStart = Scale(section.StartTick, factor, allowZero: true);
            long scaledEnd = Scale(section.EndTick, factor, allowZero: false);
            section.StartTick = scaledStart;
            section.LengthTicks = Math.Max(1, scaledEnd - scaledStart);
        }
        foreach (TempoChange tempo in project.Song.TempoMap) tempo.Tick = Scale(tempo.Tick, factor, allowZero: true);
        foreach (MeterChange meter in project.Song.MeterMap) meter.Tick = Scale(meter.Tick, factor, allowZero: true);
        foreach (SongTrack track in project.Song.Tracks)
        {
            foreach (SongClip clip in track.Clips)
            {
                long scaledStart = Scale(clip.StartTick, factor, allowZero: true);
                long scaledEnd = Scale(clip.EndTick, factor, allowZero: false);
                clip.StartTick = scaledStart;
                clip.LengthTicks = Math.Max(1, scaledEnd - scaledStart);
                foreach (NoteEvent note in clip.Notes)
                {
                    note.StartTick = Scale(note.StartTick, factor, allowZero: true);
                    note.DurationTicks = Scale(note.DurationTicks, factor, allowZero: false);
                    if (note.StartTick >= clip.LengthTicks) note.StartTick = Math.Max(0, clip.LengthTicks - 1);
                    note.DurationTicks = Math.Min(note.DurationTicks, Math.Max(1, clip.LengthTicks - note.StartTick));
                }
                foreach (ControlChangeEvent control in clip.ControlChanges)
                    control.StartTick = Math.Min(Scale(control.StartTick, factor, allowZero: true), Math.Max(0, clip.LengthTicks - 1));
                foreach (ProgramChangeEvent program in clip.ProgramChanges)
                    program.StartTick = Math.Min(Scale(program.StartTick, factor, allowZero: true), Math.Max(0, clip.LengthTicks - 1));
            }
        }
    }

    private void ApplyQuickChange(QuickMusicChange change)
    {
        if (change == QuickMusicChange.MoreRetro)
        {
            PlaygroundSoundWorld retro = SelectedSoundWorld() == PlaygroundSoundWorld.EightBit
                ? PlaygroundSoundWorld.SixteenBit
                : PlaygroundSoundWorld.EightBit;
            _soundWorldBox.SelectedItem = retro;
            _session.Edit("Made more retro", project =>
            {
                ApplySoundWorld(project, retro);
                SetPlaygroundTag(project, "sound", retro.ToString());
            });
            AddVersion("Made more retro");
            _playback.Play(_session.Project);
            return;
        }
        if (change == QuickMusicChange.ChangeInstruments)
        {
            _session.Randomize(PlayromaMusic.Core.Orchestration.RandomizationDomain.Instruments);
            AddVersion("Changed instruments");
            _playback.Play(_session.Project);
            return;
        }
        if (change == QuickMusicChange.Remix)
        {
            _session.RegenerateUnlockedRoles();
            AddVersion("Remixed current song");
            _playback.Play(_session.Project);
            return;
        }
        if (change == QuickMusicChange.CatchierMelody)
        {
            _session.RegenerateMotif();
            AddVersion("Catchier melody");
            _playback.Play(_session.Project);
            return;
        }

        string label = change switch
        {
            QuickMusicChange.MoreEpic => "Made more epic",
            QuickMusicChange.MoreMysterious => "Made more mysterious",
            QuickMusicChange.Faster => "Made faster",
            QuickMusicChange.Slower => "Made slower",
            QuickMusicChange.Simpler => "Made simpler",
            QuickMusicChange.Busier => "Made busier",
            QuickMusicChange.HeavierDrums => "Added heavier drums",
            QuickMusicChange.MoreEmotional => "Made more emotional",
            QuickMusicChange.BigFinish => "Added a big finish",
            QuickMusicChange.SmoothLoop => "Smoothed the loop",
            _ => "Changed song"
        };

        Action<ProjectDocument> setup = project =>
        {
            switch (change)
            {
                case QuickMusicChange.MoreEpic:
                    MultiplyTempo(project, 1.09);
                    AdjustRoleDensity(project, 10, TrackRole.Drums, TrackRole.Bass, TrackRole.Chords);
                    SetRoleEnabled(project, TrackRole.Drums, true);
                    SetRoleEnabled(project, TrackRole.CounterMelody, true);
                    foreach (SongSection section in project.Song.Sections) section.Energy = Math.Clamp(section.Energy + 0.12, 0d, 1d);
                    break;
                case QuickMusicChange.MoreMysterious:
                    MultiplyTempo(project, 0.94);
                    project.Song.Scale = ScaleKind.HarmonicMinor;
                    SetRoleEnabled(project, TrackRole.Texture, true);
                    AdjustRoleDensity(project, 12, TrackRole.Texture, TrackRole.Arpeggio);
                    break;
                case QuickMusicChange.Faster:
                    MultiplyTempo(project, 1.12);
                    break;
                case QuickMusicChange.Slower:
                    MultiplyTempo(project, 0.88);
                    break;
                case QuickMusicChange.Simpler:
                    AdjustRoleDensity(project, -18, Enum.GetValues<TrackRole>());
                    SetRoleEnabled(project, TrackRole.CounterMelody, false);
                    SetRoleEnabled(project, TrackRole.Arpeggio, false);
                    break;
                case QuickMusicChange.Busier:
                    AdjustRoleDensity(project, 16, Enum.GetValues<TrackRole>());
                    SetRoleEnabled(project, TrackRole.CounterMelody, true);
                    SetRoleEnabled(project, TrackRole.Arpeggio, true);
                    break;
                case QuickMusicChange.HeavierDrums:
                    SetRoleEnabled(project, TrackRole.Drums, true);
                    AdjustRoleDensity(project, 20, TrackRole.Drums);
                    break;
                case QuickMusicChange.MoreEmotional:
                    MultiplyTempo(project, 0.92);
                    project.Song.Scale = ScaleKind.NaturalMinor;
                    break;
                case QuickMusicChange.BigFinish:
                    SongSection? last = project.Song.Sections.OrderBy(item => item.StartTick).LastOrDefault();
                    if (last is not null)
                    {
                        last.Energy = 1d;
                        AdjustNotesInRange(project, last.StartTick, last.EndTick, 18);
                    }
                    break;
                case QuickMusicChange.SmoothLoop:
                    project.Audio.LoopEnabled = true;
                    TrimNotesToSong(project);
                    break;
            }
        };

        bool rebuildParts = change is QuickMusicChange.MoreEpic
            or QuickMusicChange.MoreMysterious
            or QuickMusicChange.Simpler
            or QuickMusicChange.Busier
            or QuickMusicChange.HeavierDrums
            or QuickMusicChange.MoreEmotional;

        if (rebuildParts)
        {
            _session.EditAndRegenerateUnlockedRoles(label, setup, project =>
            {
                if (change == QuickMusicChange.MoreEpic) AdjustAllVelocities(project, 9);
                if (change == QuickMusicChange.MoreMysterious) AdjustRoleVolume(project, 0.1, TrackRole.Texture);
                if (change == QuickMusicChange.HeavierDrums)
                {
                    AdjustRoleVolume(project, 0.2, TrackRole.Drums);
                    foreach (AbsoluteNote note in project.Song.EnumerateNotes().Where(item => item.Track.Role == TrackRole.Drums))
                        note.Note.Velocity = Math.Clamp(note.Note.Velocity + 18, 1, 127);
                }
                if (change == QuickMusicChange.MoreEmotional)
                {
                    AdjustRoleVolume(project, 0.16, TrackRole.Lead, TrackRole.CounterMelody);
                    AdjustRoleVolume(project, -0.08, TrackRole.Drums);
                }
            });
        }
        else
        {
            _session.Edit(label, setup);
        }
        AddVersion(label);
        _playback.Play(_session.Project);
    }

    private static void MultiplyTempo(ProjectDocument project, double factor)
    {
        if (project.Song.TempoMap.Count == 0) project.Song.TempoMap.Add(new TempoChange { Tick = 0, BeatsPerMinute = 120d });
        foreach (TempoChange tempo in project.Song.TempoMap)
            tempo.BeatsPerMinute = Math.Clamp(tempo.BeatsPerMinute * factor, 30d, 300d);
    }

    private static void AdjustAllVelocities(ProjectDocument project, int delta)
    {
        foreach (AbsoluteNote note in project.Song.EnumerateNotes()) note.Note.Velocity = Math.Clamp(note.Note.Velocity + delta, 1, 127);
    }

    private static void AdjustRoleDensity(ProjectDocument project, int delta, params TrackRole[] roles)
    {
        HashSet<TrackRole> selected = roles.ToHashSet();
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(item => selected.Contains(item.Role)))
            role.Density = Math.Clamp(role.Density + delta, 0, 100);
    }

    private static void SetRoleEnabled(ProjectDocument project, TrackRole roleKind, bool enabled)
    {
        foreach (CompositionRoleSettings role in project.StructuredComposition.Roles.Where(item => item.Role == roleKind))
        {
            role.Enabled = enabled;
            SongTrack? track = StructuredCompositionService.GetTrack(project, role.Id);
            if (track is not null) track.Muted = !enabled;
        }
    }

    private static void AdjustRoleVolume(ProjectDocument project, double delta, params TrackRole[] roles)
    {
        HashSet<TrackRole> selected = roles.ToHashSet();
        foreach (SongTrack track in project.Song.Tracks.Where(item => selected.Contains(item.Role)))
            track.Volume = Math.Clamp(track.Volume + delta, 0d, 1.5d);
    }

    private static void AdjustNotesInRange(ProjectDocument project, long startTick, long endTick, int velocityDelta)
    {
        foreach (AbsoluteNote note in project.Song.EnumerateNotes().Where(item => item.AbsoluteStartTick >= startTick && item.AbsoluteStartTick < endTick))
            note.Note.Velocity = Math.Clamp(note.Note.Velocity + velocityDelta, 1, 127);
    }

    private static void TrimNotesToSong(ProjectDocument project)
    {
        long length = project.Song.LengthTicks;
        foreach (SongTrack track in project.Song.Tracks)
        {
            foreach (SongClip clip in track.Clips)
            {
                long remainingClip = Math.Max(1, length - clip.StartTick);
                clip.LengthTicks = Math.Min(clip.LengthTicks, remainingClip);
                clip.Notes.RemoveAll(note => note.StartTick >= clip.LengthTicks);
                foreach (NoteEvent note in clip.Notes)
                    note.DurationTicks = Math.Min(note.DurationTicks, Math.Max(1, clip.LengthTicks - note.StartTick));
            }
        }
    }

    private void RefreshPlayground()
    {
        ProjectDocument project = _session.Project;
        RestoreRecipeFromProjectTags(project);
        _playgroundPreview.SetProject(project, _selectedScene);
        string styleName = StyleName(project.StructuredComposition.ActiveStyleId);
        _nowPlaying.Text = $"Now playing: {project.Title}   •   {styleName}";
        _transportTitle.Text = $"Now Playing: {project.Title}";
        UpdateRecipeSummary();
        RefreshRecentVersionStrip();
    }

    private void RestoreRecipeFromProjectTags(ProjectDocument project)
    {
        if (!_playgroundChoicesPopulated) return;

        string? sceneId = PlaygroundTag(project, "scene");
        MusicScenePreset? scene = MusicPlaygroundCatalog.Scenes.FirstOrDefault(item =>
            string.Equals(item.Id, sceneId, StringComparison.OrdinalIgnoreCase));
        if (scene is not null && !string.Equals(scene.Id, _selectedScene.Id, StringComparison.OrdinalIgnoreCase))
            SelectScene(scene, applyDefaults: false);

        if (Enum.TryParse(PlaygroundTag(project, "mood"), ignoreCase: true, out PlaygroundMood mood))
            _moodBox.SelectedItem = mood;
        if (Enum.TryParse(PlaygroundTag(project, "sound"), ignoreCase: true, out PlaygroundSoundWorld sound))
            _soundWorldBox.SelectedItem = sound;
        if (Enum.TryParse(PlaygroundTag(project, "length"), ignoreCase: true, out PlaygroundLength length))
            _lengthBox.SelectedItem = length;
        if (int.TryParse(PlaygroundTag(project, "energy"), out int energy))
            _energySlider.Value = Math.Clamp(energy, _energySlider.Minimum, _energySlider.Maximum);
        if (int.TryParse(PlaygroundTag(project, "complexity"), out int complexity))
            _complexitySlider.Value = Math.Clamp(complexity, _complexitySlider.Minimum, _complexitySlider.Maximum);
        _recipeLoop.Checked = project.Audio.LoopEnabled;
    }

    private static string? PlaygroundTag(ProjectDocument project, string key)
    {
        string prefix = $"playground:{key}=";
        string? tag = project.Tags.LastOrDefault(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return tag is null ? null : tag[prefix.Length..];
    }

    private static void SetPlaygroundTag(ProjectDocument project, string key, string value)
    {
        string prefix = $"playground:{key}=";
        project.Tags.RemoveAll(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        project.Tags.Add(prefix + value);
    }

    private void RefreshArrange()
    {
        _sectionTimeline.Project = _session.Project;
        RefreshSelectedSectionLabel();
        RebuildInstrumentGroups();
    }

    private void RefreshSelectedSectionLabel()
    {
        SongSection? section = _sectionTimeline.SelectedSection;
        _selectedSectionLabel.Text = section is null
            ? "Select a section above."
            : $"Selected: {section.Name} (energy {section.Energy:P0})";
    }

    private void RebuildInstrumentGroups()
    {
        int previousScroll = -_instrumentGroups.AutoScrollPosition.Y;
        _instrumentGroups.SuspendLayout();
        _instrumentGroups.Controls.Clear();

        List<CompositionRoleSettings> roles = _session.Project.StructuredComposition.Roles.ToList();
        if (roles.Count == 0)
        {
            foreach (SongTrack track in _session.Project.Song.Tracks)
                _instrumentGroups.Controls.Add(BuildImportedTrackRow(track));
        }
        else
        {
            foreach (CompositionRoleSettings role in roles)
                _instrumentGroups.Controls.Add(BuildInstrumentRoleRow(role));
        }

        _instrumentGroups.ResumeLayout();
        if (previousScroll > 0) _instrumentGroups.AutoScrollPosition = new Point(0, previousScroll);
    }

    private Control BuildInstrumentRoleRow(CompositionRoleSettings role)
    {
        SongTrack? track = StructuredCompositionService.GetTrack(_session.Project, role.Id);
        TableLayoutPanel row = InstrumentRowShell();
        CheckBox enabled = new() { Checked = role.Enabled && track?.Muted != true, AutoSize = true, Text = "On" };
        Label name = new() { Text = FriendlyRoleName(role.Role), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 8, 4, 0) };
        ComboBox instrument = DropDown(220);
        List<InstrumentChoice> choices = InstrumentChoicesFor(role.Role).ToList();
        if (choices.All(choice => choice.Program != role.GeneralMidiProgram))
            choices.Insert(0, new InstrumentChoice($"Current instrument (GM {role.GeneralMidiProgram + 1})", role.GeneralMidiProgram));
        instrument.Items.AddRange(choices.Cast<object>().ToArray());
        instrument.DisplayMember = nameof(InstrumentChoice.Name);
        instrument.SelectedItem = choices.First(choice => choice.Program == role.GeneralMidiProgram);

        TrackBar volume = new() { Minimum = 0, Maximum = 120, Value = Math.Clamp((int)Math.Round((track?.Volume ?? 1d) * 100d), 0, 120), Width = 150, TickFrequency = 20 };
        TrackBar activity = new() { Minimum = 0, Maximum = 100, Value = Math.Clamp(role.Density, 0, 100), Width = 150, TickFrequency = 20 };
        Button newPart = new() { Text = "New Part", AutoSize = true };
        Button surprise = new() { Text = "Surprise Instrument", AutoSize = true };

        row.Controls.Add(enabled, 0, 0);
        row.Controls.Add(name, 1, 0);
        row.Controls.Add(instrument, 2, 0);
        row.Controls.Add(LabeledCompact("Volume", volume), 3, 0);
        row.Controls.Add(LabeledCompact("Activity", activity), 4, 0);
        row.Controls.Add(newPart, 5, 0);
        row.Controls.Add(surprise, 6, 0);

        enabled.CheckedChanged += (_, _) =>
        {
            if (_refreshing) return;
            double preservedVolume = track?.Volume ?? 1d;
            Safe(() => _session.EditAndRegenerateRole(
                $"Turn {FriendlyRoleName(role.Role)} {(enabled.Checked ? "on" : "off")}",
                role.Id,
                project => project.StructuredComposition.Roles.First(item => item.Id == role.Id).Enabled = enabled.Checked,
                project =>
                {
                    SongTrack? rebuilt = StructuredCompositionService.GetTrack(project, role.Id);
                    if (rebuilt is not null) rebuilt.Volume = preservedVolume;
                }));
        };
        instrument.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing || instrument.SelectedItem is not InstrumentChoice choice) return;
            double preservedVolume = track?.Volume ?? 1d;
            Safe(() => _session.EditAndRegenerateRole(
                $"Change {FriendlyRoleName(role.Role)} instrument",
                role.Id,
                project =>
                {
                    CompositionRoleSettings current = project.StructuredComposition.Roles.First(item => item.Id == role.Id);
                    ApplyRoleInstrument(project, current, choice.Program, choice.Name);
                },
                project =>
                {
                    SongTrack? rebuilt = StructuredCompositionService.GetTrack(project, role.Id);
                    if (rebuilt is not null) rebuilt.Volume = preservedVolume;
                }));
        };
        volume.MouseUp += (_, _) =>
        {
            if (_refreshing) return;
            Safe(() => _session.Edit($"Change {FriendlyRoleName(role.Role)} volume", project =>
            {
                SongTrack? currentTrack = StructuredCompositionService.GetTrack(project, role.Id);
                if (currentTrack is not null) currentTrack.Volume = volume.Value / 100d;
            }));
        };
        activity.MouseUp += (_, _) =>
        {
            if (_refreshing) return;
            double preservedVolume = track?.Volume ?? 1d;
            Safe(() => _session.EditAndRegenerateRole(
                $"Change {FriendlyRoleName(role.Role)} activity",
                role.Id,
                project => project.StructuredComposition.Roles.First(item => item.Id == role.Id).Density = activity.Value,
                project =>
                {
                    SongTrack? rebuilt = StructuredCompositionService.GetTrack(project, role.Id);
                    if (rebuilt is not null) rebuilt.Volume = preservedVolume;
                }));
        };
        newPart.Click += (_, _) => Safe(() => { _session.RegenerateRole(role.Id); AddVersion($"New {FriendlyRoleName(role.Role)} part"); _playback.Play(_session.Project); });
        surprise.Click += (_, _) =>
        {
            InstrumentChoice choice = choices[Random.Shared.Next(choices.Count)];
            instrument.SelectedItem = choice;
        };
        return row;
    }

    private Control BuildImportedTrackRow(SongTrack track)
    {
        TableLayoutPanel row = InstrumentRowShell();
        CheckBox enabled = new() { Checked = !track.Muted, Text = "On", AutoSize = true };
        Label name = new() { Text = string.IsNullOrWhiteSpace(track.Name) ? FriendlyRoleName(track.Role) : track.Name, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 8, 4, 0) };
        Label instrument = new() { Text = $"GM Program {track.GeneralMidiProgram + 1}", AutoSize = true, Margin = new Padding(4, 8, 4, 0) };
        TrackBar volume = new() { Minimum = 0, Maximum = 120, Value = Math.Clamp((int)Math.Round(track.Volume * 100d), 0, 120), Width = 150 };
        row.Controls.Add(enabled, 0, 0);
        row.Controls.Add(name, 1, 0);
        row.Controls.Add(instrument, 2, 0);
        row.Controls.Add(LabeledCompact("Volume", volume), 3, 0);
        Label imported = new() { Text = "Imported MIDI track — use Compose for note editing", AutoSize = true, Margin = new Padding(8) };
        row.Controls.Add(imported, 4, 0);
        row.SetColumnSpan(imported, 3);
        enabled.CheckedChanged += (_, _) =>
        {
            if (_refreshing) return;
            Safe(() => _session.Edit("Mute imported track", project => project.Song.Tracks.First(item => item.Id == track.Id).Muted = !enabled.Checked));
        };
        volume.MouseUp += (_, _) =>
        {
            if (_refreshing) return;
            Safe(() => _session.Edit("Change imported track volume", project => project.Song.Tracks.First(item => item.Id == track.Id).Volume = volume.Value / 100d));
        };
        return row;
    }

    private static TableLayoutPanel InstrumentRowShell()
    {
        TableLayoutPanel row = new()
        {
            Width = 1120,
            Height = 72,
            ColumnCount = 7,
            RowCount = 1,
            Margin = new Padding(2, 2, 2, 6),
            Padding = new Padding(8),
            BackColor = Color.FromArgb(246, 248, 252)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return row;
    }

    private static Control LabeledCompact(string caption, Control control)
    {
        FlowLayoutPanel panel = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(2) };
        panel.Controls.Add(new Label { Text = caption, AutoSize = true });
        panel.Controls.Add(control);
        return panel;
    }

    private static IReadOnlyList<InstrumentChoice> InstrumentChoicesFor(TrackRole role) => role switch
    {
        TrackRole.Lead =>
        [
            new("Piano", 0), new("Music Box", 10), new("Violin", 40), new("Strings", 48), new("Trumpet", 56),
            new("Flute", 73), new("Square-Wave Lead", 80), new("Saw Lead", 81), new("Dream Pad", 88), new("Overdrive Guitar", 29)
        ],
        TrackRole.CounterMelody =>
        [
            new("Flute", 73), new("Oboe", 68), new("Clarinet", 71), new("Violin", 40), new("Choir", 52),
            new("Music Box", 10), new("Synth Lead", 84), new("Warm Pad", 89)
        ],
        TrackRole.Chords =>
        [
            new("Piano", 0), new("Electric Piano", 4), new("Acoustic Guitar", 24), new("Clean Guitar", 27),
            new("Strings", 48), new("Choir", 52), new("Warm Pad", 89), new("Synth Pad", 90)
        ],
        TrackRole.Arpeggio =>
        [
            new("Piano", 0), new("Music Box", 10), new("Harp", 46), new("Acoustic Guitar", 24),
            new("Square Synth", 80), new("Synth Pluck", 82), new("Crystal", 98)
        ],
        TrackRole.Bass =>
        [
            new("Acoustic Bass", 32), new("Finger Bass", 33), new("Pick Bass", 34), new("Fretless Bass", 35),
            new("Synth Bass", 38), new("Contrabass", 43), new("Tuba", 58)
        ],
        TrackRole.Texture =>
        [
            new("Strings", 48), new("Choir", 52), new("Warm Pad", 89), new("New Age Pad", 88),
            new("Atmosphere", 99), new("Brightness", 100), new("Rain", 96), new("Goblins", 101)
        ],
        _ => [new("Standard", 0)]
    };

    private void AdjustSelectedSection(double energyDelta, int velocityDelta)
    {
        SongSection selected = SelectedSectionOrThrow();
        _session.Edit(energyDelta >= 0 ? "Make section stronger" : "Make section calmer", project =>
        {
            SongSection section = project.Song.Sections.First(item => item.Id == selected.Id);
            section.Energy = Math.Clamp(section.Energy + energyDelta, 0d, 1d);
            AdjustNotesInRange(project, section.StartTick, section.EndTick, velocityDelta);
        });
        AddVersion(energyDelta >= 0 ? "Stronger section" : "Calmer section");
        _playback.Play(_session.Project);
    }

    private void DuplicateSelectedSection()
    {
        SongSection selected = SelectedSectionOrThrow();
        string newId = string.Empty;
        _session.Edit("Duplicate song section", project =>
        {
            SongSection source = project.Song.Sections.First(item => item.Id == selected.Id);
            newId = AppendSectionCopy(project, source, source.Name + " Reprise", source.Energy);
        });
        _sectionTimeline.SelectedSectionId = newId;
        AddVersion("Duplicated section");
    }

    private void AddClimax()
    {
        string newId = string.Empty;
        _session.Edit("Add climax", project =>
        {
            SongSection source = project.Song.Sections.OrderBy(item => item.StartTick).LastOrDefault()
                ?? throw new InvalidOperationException("The song has no section to turn into a climax.");
            newId = AppendSectionCopy(project, source, "Climax", 1d);
            SongSection climax = project.Song.Sections.First(item => item.Id == newId);
            AdjustNotesInRange(project, climax.StartTick, climax.EndTick, 18);
        });
        _sectionTimeline.SelectedSectionId = newId;
        AddVersion("Added climax");
    }

    private void AddGentleIntro()
    {
        string newId = string.Empty;
        _session.Edit("Add gentle intro", project =>
        {
            SongSection source = project.Song.Sections.OrderBy(item => item.StartTick).FirstOrDefault()
                ?? throw new InvalidOperationException("The song has no section to use as an intro.");
            long sourceStart = source.StartTick;
            long sourceEnd = source.EndTick;
            long length = source.LengthTicks;
            Dictionary<string, List<NoteEvent>> notesByTrack = [];
            foreach (SongTrack track in project.Song.Tracks)
            {
                List<NoteEvent> notes = [];
                foreach (AbsoluteNote note in track.Clips.SelectMany(clip => clip.Notes.Select(item => new AbsoluteNote(track, clip, item, clip.StartTick + item.StartTick)))
                             .Where(item => item.AbsoluteStartTick >= sourceStart && item.AbsoluteStartTick < sourceEnd))
                {
                    notes.Add(new NoteEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Pitch = note.Note.Pitch,
                        StartTick = note.AbsoluteStartTick - sourceStart,
                        DurationTicks = Math.Min(note.Note.DurationTicks, Math.Max(1, sourceEnd - note.AbsoluteStartTick)),
                        Velocity = Math.Clamp(note.Note.Velocity - 20, 1, 127)
                    });
                }
                if (notes.Count > 0) notesByTrack[track.Id] = notes;
            }

            double introTempo = project.Song.TempoMap.OrderBy(item => item.Tick).FirstOrDefault()?.BeatsPerMinute ?? 120d;
            MeterChange? initialMeter = project.Song.MeterMap.OrderBy(item => item.Tick).FirstOrDefault();
            int introNumerator = initialMeter?.Numerator ?? 4;
            int introDenominator = initialMeter?.Denominator ?? 4;

            foreach (SongSection section in project.Song.Sections) section.StartTick += length;
            foreach (SongTrack track in project.Song.Tracks)
                foreach (SongClip clip in track.Clips) clip.StartTick += length;
            foreach (TempoChange tempo in project.Song.TempoMap) tempo.Tick += length;
            foreach (MeterChange meter in project.Song.MeterMap) meter.Tick += length;
            project.Song.TempoMap.Add(new TempoChange { Tick = 0, BeatsPerMinute = introTempo });
            project.Song.MeterMap.Add(new MeterChange { Tick = 0, Numerator = introNumerator, Denominator = introDenominator });
            project.Song.TempoMap = project.Song.TempoMap.OrderBy(item => item.Tick).ToList();
            project.Song.MeterMap = project.Song.MeterMap.OrderBy(item => item.Tick).ToList();

            newId = Guid.NewGuid().ToString("N");
            project.Song.Sections.Add(new SongSection { Id = newId, Name = "Gentle Intro", StartTick = 0, LengthTicks = length, Energy = Math.Min(0.28, source.Energy) });
            foreach (SongTrack track in project.Song.Tracks)
            {
                if (!notesByTrack.TryGetValue(track.Id, out List<NoteEvent>? notes)) continue;
                track.Clips.Add(new SongClip { Id = Guid.NewGuid().ToString("N"), Name = "Gentle Intro", StartTick = 0, LengthTicks = length, Notes = notes });
                track.Clips = track.Clips.OrderBy(item => item.StartTick).ToList();
            }
            project.Song.Sections = project.Song.Sections.OrderBy(item => item.StartTick).ToList();
        });
        _sectionTimeline.SelectedSectionId = newId;
        AddVersion("Added gentle intro");
    }

    private static string AppendSectionCopy(ProjectDocument project, SongSection source, string name, double energy)
    {
        long targetStart = project.Song.LengthTicks;
        string id = Guid.NewGuid().ToString("N");
        project.Song.Sections.Add(new SongSection
        {
            Id = id,
            Name = name,
            StartTick = targetStart,
            LengthTicks = source.LengthTicks,
            Energy = Math.Clamp(energy, 0d, 1d)
        });
        foreach (SongTrack track in project.Song.Tracks)
        {
            SongClip copy = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                StartTick = targetStart,
                LengthTicks = source.LengthTicks
            };
            foreach (SongClip clip in track.Clips)
            {
                foreach (NoteEvent note in clip.Notes)
                {
                    long absolute = clip.StartTick + note.StartTick;
                    if (absolute < source.StartTick || absolute >= source.EndTick) continue;
                    copy.Notes.Add(new NoteEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Pitch = note.Pitch,
                        StartTick = absolute - source.StartTick,
                        DurationTicks = Math.Min(note.DurationTicks, Math.Max(1, source.EndTick - absolute)),
                        Velocity = note.Velocity
                    });
                }
                foreach (ControlChangeEvent control in clip.ControlChanges)
                {
                    long absolute = clip.StartTick + control.StartTick;
                    if (absolute < source.StartTick || absolute >= source.EndTick) continue;
                    copy.ControlChanges.Add(new ControlChangeEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        ControllerNumber = control.ControllerNumber,
                        Value = control.Value,
                        StartTick = absolute - source.StartTick
                    });
                }
                foreach (ProgramChangeEvent program in clip.ProgramChanges)
                {
                    long absolute = clip.StartTick + program.StartTick;
                    if (absolute < source.StartTick || absolute >= source.EndTick) continue;
                    copy.ProgramChanges.Add(new ProgramChangeEvent
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Bank = program.Bank,
                        Program = program.Program,
                        StartTick = absolute - source.StartTick,
                        PresetKey = program.PresetKey,
                        PresetName = program.PresetName
                    });
                }
            }
            if (copy.Notes.Count > 0 || copy.ControlChanges.Count > 0 || copy.ProgramChanges.Count > 0)
            {
                track.Clips.Add(copy);
                track.Clips = track.Clips.OrderBy(item => item.StartTick).ToList();
            }
        }
        project.Song.Sections = project.Song.Sections.OrderBy(item => item.StartTick).ToList();
        return id;
    }

    private SongSection SelectedSectionOrThrow() => _sectionTimeline.SelectedSection
        ?? throw new InvalidOperationException("Select a song section first.");

    private void AddVersion(string label, bool favorite = false, ProjectDocument? snapshot = null, string? filePath = null)
    {
        ProjectDocument project = snapshot is null ? _session.CaptureSnapshot() : ProjectSerializer.Clone(snapshot);
        MusicVersionSnapshot version = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = label,
            StyleName = StyleName(project.StructuredComposition.ActiveStyleId),
            CreatedAt = DateTime.Now,
            Project = project,
            Favorite = favorite,
            FilePath = filePath
        };
        _versions.Insert(0, version);
        while (_versions.Count > 36)
        {
            int removable = _versions.FindLastIndex(item => !item.Favorite);
            if (removable < 0) break;
            _versions.RemoveAt(removable);
        }
        RefreshRecentVersionStrip();
        RefreshLibraryVersions();
    }

    private void KeepCurrentVersion()
    {
        MusicVersionSnapshot version = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Label = "Favorite",
            StyleName = StyleName(_session.Project.StructuredComposition.ActiveStyleId),
            CreatedAt = DateTime.Now,
            Project = _session.CaptureSnapshot(),
            Favorite = true
        };
        SaveFavoriteSnapshot(version);
        _versions.Insert(0, version);
        RefreshRecentVersionStrip();
        RefreshLibraryVersions();
        SetStatus($"Saved “{version.Project.Title}” to the Music Favorites library.");
    }

    private void SaveFavoriteSnapshot(MusicVersionSnapshot version)
    {
        string folder = FavoritesFolder();
        Directory.CreateDirectory(folder);
        string baseName = MusicExportService.SanitizeFileName(version.Project.Title);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Favorite Music";
        string path = Path.Combine(folder, $"{baseName}-{DateTime.Now:yyyyMMdd-HHmmssfff}.playroma-music.json");
        ProjectSerializer.Save(version.Project, path);
        version.FilePath = path;
        version.Favorite = true;
    }

    private void LoadSavedFavorites()
    {
        string folder = FavoritesFolder();
        if (!Directory.Exists(folder)) return;
        foreach (string path in Directory.EnumerateFiles(folder, "*.playroma-music.json", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(File.GetLastWriteTimeUtc).Take(50))
        {
            try
            {
                ProjectDocument project = ProjectSerializer.Load(path);
                AddVersion("Favorite", favorite: true, snapshot: project, filePath: path);
            }
            catch
            {
                // A damaged favorite should not prevent the studio from opening.
            }
        }
    }

    private string FavoritesFolder() => Path.Combine(_session.ProjectRoot, "data", "PlayromaMusic", "Favorites");

    private void RefreshRecentVersionStrip()
    {
        _recentVersionStrip.SuspendLayout();
        _recentVersionStrip.Controls.Clear();
        foreach (MusicVersionSnapshot version in _versions.Take(8))
        {
            Button button = new()
            {
                Text = (version.Favorite ? "★ " : string.Empty) + version.Project.Title,
                AutoSize = true,
                MaximumSize = new Size(190, 34),
                AutoEllipsis = true,
                Tag = version,
                Margin = new Padding(3),
                AccessibleDescription = $"Restore {version.Label} from {version.CreatedAt:t}"
            };
            button.Click += (_, _) => Safe(() => RestoreVersion(version));
            _recentVersionStrip.Controls.Add(button);
        }
        _recentVersionStrip.ResumeLayout();
    }

    private void RefreshLibrary()
    {
        RefreshLibraryVersions();
        RefreshLibraryStyles();
    }

    private void RefreshLibraryVersions()
    {
        if (_versionList.Columns.Count == 0) return;
        string? selectedId = _versionList.SelectedItems.Count > 0 && _versionList.SelectedItems[0].Tag is MusicVersionSnapshot selected ? selected.Id : null;
        _versionList.BeginUpdate();
        _versionList.Items.Clear();
        foreach (MusicVersionSnapshot version in _versions)
        {
            ListViewItem item = new((version.Favorite ? "★ " : string.Empty) + version.Project.Title)
            {
                Tag = version
            };
            item.SubItems.Add(version.Label);
            item.SubItems.Add(version.StyleName);
            item.SubItems.Add(version.CreatedAt.ToString("t"));
            _versionList.Items.Add(item);
            if (string.Equals(version.Id, selectedId, StringComparison.Ordinal)) item.Selected = true;
        }
        _versionList.EndUpdate();
    }

    private void RefreshLibraryStyles()
    {
        if (_libraryStyleList.IsDisposed) return;
        string? selectedId = (_libraryStyleList.SelectedItem as StructuredStyleDefinition)?.Id;
        string query = _styleSearch.Text.Trim();
        StructuredStyleDefinition[] styles = StructuredStyleLibrary.All
            .Where(style => query.Length == 0 || style.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || style.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(style => style.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _libraryStyleList.BeginUpdate();
        _libraryStyleList.Items.Clear();
        _libraryStyleList.Items.AddRange(styles.Cast<object>().ToArray());
        _libraryStyleList.EndUpdate();
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            for (int index = 0; index < _libraryStyleList.Items.Count; index++)
                if (_libraryStyleList.Items[index] is StructuredStyleDefinition style && string.Equals(style.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    _libraryStyleList.SelectedIndex = index;
        }
        if (_libraryStyleList.SelectedIndex < 0 && _libraryStyleList.Items.Count > 0) _libraryStyleList.SelectedIndex = 0;
        RefreshLibraryStyleDescription();
    }

    private void RefreshLibraryStyleDescription()
    {
        _libraryStyleDescription.Text = _libraryStyleList.SelectedItem is StructuredStyleDefinition style
            ? $"{style.Name}\r\n{style.Description}"
            : "Choose a style to hear a new song or apply it to the current composition.";
    }

    private void RestoreSelectedVersion()
    {
        MusicVersionSnapshot version = SelectedVersionOrThrow();
        RestoreVersion(version);
    }

    private void RestoreVersion(MusicVersionSnapshot version)
    {
        _session.RestoreSnapshot(version.Project, $"Restored {version.Label.ToLowerInvariant()}");
        SetStatus($"Restored “{version.Project.Title}”. Your later versions are still available in Library.");
        _playback.Play(_session.Project);
    }

    private void FavoriteSelectedVersion()
    {
        MusicVersionSnapshot version = SelectedVersionOrThrow();
        if (!version.Favorite || string.IsNullOrWhiteSpace(version.FilePath) || !File.Exists(version.FilePath))
            SaveFavoriteSnapshot(version);
        version.Favorite = true;
        RefreshRecentVersionStrip();
        RefreshLibraryVersions();
        SetStatus($"Saved “{version.Project.Title}” as a favorite.");
    }

    private void DeleteSelectedVersion()
    {
        MusicVersionSnapshot version = SelectedVersionOrThrow();
        if (version.Favorite && !string.IsNullOrWhiteSpace(version.FilePath) && File.Exists(version.FilePath))
        {
            DialogResult result = MessageBox.Show(this,
                "This version is saved in the Favorites folder. Delete the saved favorite too?",
                "Delete Music Version", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Cancel) return;
            if (result == DialogResult.Yes) File.Delete(version.FilePath);
        }
        _versions.Remove(version);
        RefreshRecentVersionStrip();
        RefreshLibraryVersions();
    }

    private MusicVersionSnapshot SelectedVersionOrThrow() =>
        _versionList.SelectedItems.Count > 0 && _versionList.SelectedItems[0].Tag is MusicVersionSnapshot version
            ? version
            : throw new InvalidOperationException("Select a version first.");

    private void GenerateSelectedLibraryStyle()
    {
        StructuredStyleDefinition style = SelectedLibraryStyleOrThrow();
        ulong seed = CreateRandomSeed();
        string title = BuildGeneratedTitle(seed, _selectedScene, SelectedMood());
        _session.NewStructuredConfigured(seed, style.Id, project =>
        {
            project.Title = title;
            project.Song.Title = title;
            project.Audio.LoopEnabled = true;
        }, $"Created {style.Name}");
        AddVersion($"New {style.Name} song");
        _playback.Play(_session.Project);
    }

    private void ApplySelectedLibraryStyle()
    {
        StructuredStyleDefinition style = SelectedLibraryStyleOrThrow();
        _session.ApplyStyle(style.Id);
        AddVersion($"Applied {style.Name}");
        _playback.Play(_session.Project);
    }

    private StructuredStyleDefinition SelectedLibraryStyleOrThrow() => _libraryStyleList.SelectedItem as StructuredStyleDefinition
        ?? throw new InvalidOperationException("Choose a style first.");

    private void UpdateTransportDiagnostics()
    {
        var diagnostics = _playback.GetDiagnostics();
        if (diagnostics is null)
        {
            _transportProgress.Value = 0;
            _transportTime.Text = "00:00 / 00:00";
            _transportSection.Text = "Ready";
            _playgroundPreview.SetPlayback(0d, false);
            return;
        }
        double progress = diagnostics.LengthSamples > 0 ? diagnostics.PositionSamples / (double)diagnostics.LengthSamples : 0d;
        _transportProgress.Value = Math.Clamp((int)Math.Round(progress * 1000d), 0, 1000);
        _transportTime.Text = $"{diagnostics.Position:mm\\:ss} / {diagnostics.Length:mm\\:ss}";
        long tick = (long)Math.Round(progress * Math.Max(0, _session.Project.Song.LengthTicks));
        SongSection? section = _session.Project.Song.Sections.FirstOrDefault(item => tick >= item.StartTick && tick < item.EndTick);
        _transportSection.Text = section?.Name ?? (_playback.IsPlaying ? "Playing" : "Ready");
        _playgroundPreview.SetPlayback(progress, _playback.IsPlaying);
    }

    private PlaygroundMood SelectedMood() => _moodBox.SelectedItem is PlaygroundMood value ? value : _selectedScene.DefaultMood;
    private PlaygroundSoundWorld SelectedSoundWorld() => _soundWorldBox.SelectedItem is PlaygroundSoundWorld value ? value : PlaygroundSoundWorld.ClassicJrpg;
    private PlaygroundLength SelectedLength() => _lengthBox.SelectedItem is PlaygroundLength value ? value : PlaygroundLength.NormalLoop;

    private static string FriendlyRoleName(TrackRole role) => role switch
    {
        TrackRole.Lead => "Melody",
        TrackRole.CounterMelody => "Second Melody",
        TrackRole.Chords => "Background",
        TrackRole.Arpeggio => "Motion",
        TrackRole.Bass => "Bass",
        TrackRole.Drums => "Rhythm",
        TrackRole.Texture => "Atmosphere",
        TrackRole.Harmony => "Harmony",
        _ => role.ToString()
    };

    private static ulong CreateRandomSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static string BuildGeneratedTitle(ulong seed, MusicScenePreset scene, PlaygroundMood mood)
    {
        string[] adjectives = mood switch
        {
            PlaygroundMood.Peaceful => ["Gentle", "Quiet", "Golden", "Restful", "Soft"],
            PlaygroundMood.Cheerful => ["Bright", "Dancing", "Happy", "Sunlit", "Merry"],
            PlaygroundMood.Mysterious => ["Hidden", "Silver", "Forgotten", "Veiled", "Secret"],
            PlaygroundMood.Dark => ["Black", "Dread", "Hollow", "Ashen", "Cursed"],
            PlaygroundMood.Heroic => ["Brave", "Rising", "Noble", "Radiant", "Unbroken"],
            PlaygroundMood.Sad => ["Fading", "Last", "Distant", "Lonely", "Fallen"],
            PlaygroundMood.Magical => ["Starlit", "Enchanted", "Crystal", "Moonlit", "Arcane"],
            PlaygroundMood.Romantic => ["Tender", "Beloved", "Warm", "Secret", "Everlasting"],
            PlaygroundMood.Playful => ["Bouncy", "Mischievous", "Tiny", "Wobbly", "Curious"],
            PlaygroundMood.Urgent => ["Burning", "Final", "Racing", "Desperate", "Storming"],
            _ => ["New"]
        };
        string[] nouns = scene.Category switch
        {
            MusicSceneCategory.Battle => ["Blades", "Valor", "Clash", "Challenge", "Last Stand"],
            MusicSceneCategory.Place => ["Horizon", "Path", "Sanctuary", "Ruins", "Kingdom"],
            MusicSceneCategory.Travel => ["Journey", "Sky", "Road", "Voyage", "Frontier"],
            MusicSceneCategory.Celebration => ["Triumph", "Festival", "Fanfare", "Promise", "Cheers"],
            MusicSceneCategory.Danger => ["Depths", "Shadows", "Labyrinth", "Warning", "Unknown"],
            _ => ["Memory", "Story", "Dream", "Promise", "Revelation"]
        };
        Random random = new(unchecked((int)(seed ^ (seed >> 32))));
        return $"{adjectives[random.Next(adjectives.Length)]} {nouns[random.Next(nouns.Length)]}";
    }

    private static string StyleName(string? styleId) => StructuredStyleLibrary.All.FirstOrDefault(style =>
        string.Equals(style.Id, styleId, StringComparison.OrdinalIgnoreCase))?.Name ?? (styleId ?? "Custom Style");

    private static Button BigActionButton(string text, Action action)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(165, 46),
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold),
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(5)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private Button QuickButton(string text, QuickMusicChange change)
    {
        Button button = new()
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(112, 34),
            Padding = new Padding(7, 3, 7, 3),
            Margin = new Padding(4)
        };
        button.Click += (_, _) => Safe(() => ApplyQuickChange(change));
        return button;
    }
}
