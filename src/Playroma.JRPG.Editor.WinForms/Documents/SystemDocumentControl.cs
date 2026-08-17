using System.Globalization;
using System.Text.Json;
using Playroma.JRPG.Application;
using Playroma.JRPG.Assets;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.Editor.WinForms.Database;
using Playroma.JRPG.Editor.WinForms.Dialogs;
using Playroma.JRPG.Indexing;
using Playroma.JRPG.RpgMakerMV;
using Playroma.Text.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Documents;

/// <summary>
/// Complete Phase 9E editor for RPG Maker MV System.json, including Types and Terms.
/// Every mutation is applied through the project transaction/undo infrastructure and
/// raw object serializers preserve unknown fields on vehicles, audio rows and test data.
/// </summary>
public sealed class SystemDocumentControl : UserControl, IDatabasePendingEditSource
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly Phase9EMutationService _mutations;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(8, 0, 8, 0), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private readonly TextBox _gameTitle = new() { Dock = DockStyle.Fill };
    private readonly TextBox _currency = new() { Dock = DockStyle.Fill };
    private readonly TextBox _locale = new() { Dock = DockStyle.Fill };
    private readonly TextBox _editMapId = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _versionId = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly NumericUpDown[] _windowTone = Enumerable.Range(0, 4).Select(_ => new NumericUpDown { Minimum = -255, Maximum = 255, Width = 74 }).ToArray();
    private readonly DataGridView _party = CreateGrid();
    private readonly Dictionary<string, CheckBox> _options = new(StringComparer.Ordinal);
    private readonly CheckBox[] _menuCommands = Enumerable.Range(0, MvPhase9EConstants.MenuCommandCount).Select(_ => new CheckBox { AutoSize = true }).ToArray();
    private readonly SystemPositionEditor _playerPosition;
    private readonly Dictionary<string, VehicleEditorPanel> _vehicles = new(StringComparer.Ordinal);
    private readonly DataGridView _audio = CreateGrid();
    private readonly AssetPicker _title1;
    private readonly AssetPicker _title2;
    private readonly AssetPicker _battleback1;
    private readonly AssetPicker _battleback2;
    private readonly AssetIndexSelectionControl _battler;
    private readonly NumericUpDown _battlerHue = new() { Minimum = 0, Maximum = 360, Width = 80 };
    private readonly DataGridView _attackMotions = CreateGrid();
    private readonly DataGridView _magicSkills = CreateGrid();
    private readonly Dictionary<string, SystemStringArrayEditor> _arrayEditors = new(StringComparer.Ordinal);
    private readonly TermsEditorControl _terms = new();
    private readonly NumericUpDown _testTroop = new() { Minimum = 0, Maximum = 999999, Width = 100 };
    private readonly DataGridView _testBattlers = CreateGrid();
    private MvSystemSnapshot? _snapshot;
    private readonly HashSet<string> _pendingBuffers = new(StringComparer.Ordinal);
    private bool _loading;
    private bool _applyingPending;

    public bool HasPendingChanges => _pendingBuffers.Count > 0;
    public event EventHandler? PendingChangesChanged;
    public event EventHandler? SectionChanged;
    public event EventHandler<DirectTextGenerationRequestedEventArgs>? TextGenerationRequested;
    public SystemEditorSection ActiveSection => _tabs.TabPages.Count == 0
        ? SystemEditorSection.General
        : (SystemEditorSection)Math.Clamp(_tabs.SelectedIndex, 0, _tabs.TabPages.Count - 1);

    public void SelectSection(SystemEditorSection section)
    {
        int index = Math.Clamp((int)section, 0, Math.Max(0, _tabs.TabPages.Count - 1));
        if (_tabs.TabPages.Count > 0) _tabs.SelectedIndex = index;
    }

    public async ValueTask ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _applyingPending = true;
        try
        {
            if (_pendingBuffers.Contains("WindowTone"))
            {
                await _mutations.SetSystemArrayAsync(
                    _document.RelativePath,
                    "windowTone",
                    _windowTone.Select(control => decimal.ToInt32(control.Value)).ToArray()).ConfigureAwait(true);
                ClearPending("WindowTone");
            }
            if (_pendingBuffers.Contains("Party")) await ApplyPartyAsync().ConfigureAwait(true);
            if (_pendingBuffers.Contains("MenuCommands"))
            {
                await _mutations.SetSystemArrayAsync(
                    _document.RelativePath,
                    "menuCommands",
                    _menuCommands.Select(item => item.Checked).ToArray()).ConfigureAwait(true);
                ClearPending("MenuCommands");
            }
            if (_pendingBuffers.Contains("Audio")) await ApplyAudioAsync().ConfigureAwait(true);
            if (_pendingBuffers.Contains("AttackMotions")) await ApplyAttackMotionsAsync().ConfigureAwait(true);
            if (_pendingBuffers.Contains("MagicSkills")) await ApplyMagicSkillsAsync().ConfigureAwait(true);
            if (_pendingBuffers.Contains("BattleTest")) await ApplyBattleTestAsync().ConfigureAwait(true);
        }
        finally
        {
            _applyingPending = false;
        }
        if (!HasPendingChanges) LoadValues();
    }

    public ValueTask DiscardPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadValues();
        return ValueTask.CompletedTask;
    }

    public SystemDocumentControl(MvProjectSession session, IMutableProjectDocument document)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document as MutableJsonProjectDocument ?? throw new NotSupportedException("System.json requires a mutable JSON document.");
        _mutations = new Phase9EMutationService(session.Editing);
        _title1 = new AssetPicker(session, AssetCategory.Titles1) { Dock = DockStyle.Fill };
        _title2 = new AssetPicker(session, AssetCategory.Titles2) { Dock = DockStyle.Fill };
        _battleback1 = new AssetPicker(session, AssetCategory.Battlebacks1) { Dock = DockStyle.Fill };
        _battleback2 = new AssetPicker(session, AssetCategory.Battlebacks2) { Dock = DockStyle.Fill };
        _battler = new AssetIndexSelectionControl(session, AssetCategory.SideViewEnemies, false) { Dock = DockStyle.Fill };
        _playerPosition = new SystemPositionEditor(session, "Player starting position");
        Dock = DockStyle.Fill;
        BuildTabs();
        Controls.Add(_tabs);
        Controls.Add(_status);
        WireEvents();
        _session.DocumentChanged += OnDocumentChanged;
        _tabs.SelectedIndexChanged += (_, _) => SectionChanged?.Invoke(this, EventArgs.Empty);
        LoadValues();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _session.DocumentChanged -= OnDocumentChanged;
        base.Dispose(disposing);
    }

    private void BuildTabs()
    {
        _tabs.TabPages.Add(new TabPage("General") { Controls = { BuildGeneralTab() } });
        _tabs.TabPages.Add(new TabPage("Options") { Controls = { BuildOptionsTab() } });
        _tabs.TabPages.Add(new TabPage("Positions & Vehicles") { Controls = { BuildPositionsTab() } });
        _tabs.TabPages.Add(new TabPage("Music & Sounds") { Controls = { BuildAudioTab() } });
        _tabs.TabPages.Add(new TabPage("Graphics & Motions") { Controls = { BuildGraphicsTab() } });
        _tabs.TabPages.Add(new TabPage("Types") { Controls = { BuildTypesTab() } });
        _tabs.TabPages.Add(new TabPage("Terms") { Controls = { BuildTermsTab() } });
        _tabs.TabPages.Add(new TabPage("Battle Test") { Controls = { BuildBattleTestTab() } });
    }

    private Control BuildGeneralTab()
    {
        Panel root = new() { Dock = DockStyle.Fill, AutoScroll = true };
        TableLayoutPanel fields = FormTable();
        AddRow(fields, "Game title", _gameTitle);
        AddRow(fields, "Currency", _currency);
        AddRow(fields, "Locale", _locale);
        AddRow(fields, "Last edited map ID", _editMapId);
        AddRow(fields, "Project version ID", _versionId);
        FlowLayoutPanel tone = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        string[] toneNames = ["Red", "Green", "Blue", "Grey"];
        for (int index = 0; index < _windowTone.Length; index++) { tone.Controls.Add(new Label { Text = toneNames[index], AutoSize = true, Margin = new Padding(8, 8, 2, 2) }); tone.Controls.Add(_windowTone[index]); _windowTone[index].ValueChanged += (_, _) => { if (!_loading) MarkPending("WindowTone"); }; }
        Button applyTone = new() { Text = "Apply tone", AutoSize = true };
        tone.Controls.Add(applyTone);
        applyTone.Click += async (_, _) => { await _mutations.SetSystemArrayAsync(_document.RelativePath, "windowTone", _windowTone.Select(control => decimal.ToInt32(control.Value)).ToArray()).ConfigureAwait(true); ClearPending("WindowTone"); };
        AddRow(fields, "Window tone", tone);

        GroupBox partyGroup = new() { Text = "Starting Party", Dock = DockStyle.Top, Height = 300, Padding = new Padding(8) };
        ConfigurePartyGrid();
        FlowLayoutPanel partyButtons = ButtonBar();
        Button add = new() { Text = "Add actor", AutoSize = true };
        Button remove = new() { Text = "Remove selected", AutoSize = true };
        Button apply = new() { Text = "Apply party", AutoSize = true };
        partyButtons.Controls.AddRange([add, remove, apply]);
        add.Click += (_, _) => { _party.Rows.Add(1, ResolveRecordName(MvDocumentKind.Actors, 1)); MarkPending("Party"); };
        remove.Click += (_, _) => { foreach (DataGridViewRow row in _party.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(row => row.Index)) if (!row.IsNewRow) _party.Rows.Remove(row); MarkPending("Party"); };
        apply.Click += async (_, _) => await ApplyPartyAsync().ConfigureAwait(true);
        partyGroup.Controls.Add(_party);
        partyGroup.Controls.Add(partyButtons);
        root.Controls.Add(partyGroup);
        root.Controls.Add(fields);
        return root;
    }

    private Control BuildOptionsTab()
    {
        Panel root = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        GroupBox options = new() { Text = "Runtime Options", Dock = DockStyle.Top, Height = 230, Padding = new Padding(10) };
        FlowLayoutPanel optionFlow = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = true };
        (string Key, string Label)[] definitions =
        [
            ("optSideView", "Use side-view battle"), ("optTransparent", "Start transparent"),
            ("optFollowers", "Show player followers"), ("optSlipDeath", "Knockout by slip damage"),
            ("optFloorDeath", "Knockout by floor damage"), ("optDisplayTp", "Display TP in battle"),
            ("optExtraExp", "Reserve members receive EXP"), ("optDrawTitle", "Draw game title")
        ];
        foreach ((string key, string label) in definitions)
        {
            CheckBox check = new() { Text = label, AutoSize = true, Tag = key, Margin = new Padding(6) };
            _options[key] = check;
            optionFlow.Controls.Add(check);
        }
        options.Controls.Add(optionFlow);

        GroupBox menu = new() { Text = "Menu Commands", Dock = DockStyle.Top, Height = 150, Padding = new Padding(10) };
        FlowLayoutPanel menuFlow = new() { Dock = DockStyle.Fill, WrapContents = true };
        string[] names = ["Item", "Skill", "Equip", "Status", "Formation", "Save"];
        for (int index = 0; index < _menuCommands.Length; index++) { _menuCommands[index].Text = names[index]; _menuCommands[index].Margin = new Padding(10); _menuCommands[index].CheckedChanged += (_, _) => { if (!_loading) MarkPending("MenuCommands"); }; menuFlow.Controls.Add(_menuCommands[index]); }
        Button applyMenu = new() { Text = "Apply menu commands", AutoSize = true, Margin = new Padding(12) };
        menuFlow.Controls.Add(applyMenu);
        applyMenu.Click += async (_, _) => { await _mutations.SetSystemArrayAsync(_document.RelativePath, "menuCommands", _menuCommands.Select(item => item.Checked).ToArray()).ConfigureAwait(true); ClearPending("MenuCommands"); };
        menu.Controls.Add(menuFlow);
        root.Controls.Add(menu);
        root.Controls.Add(options);
        return root;
    }

    private Control BuildPositionsTab()
    {
        Panel root = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        _playerPosition.Dock = DockStyle.Top;
        _playerPosition.ApplyRequested += async (_, position) => await ApplyPlayerPositionAsync(position).ConfigureAwait(true);
        root.Controls.Add(_playerPosition);
        foreach (string key in new[] { "airship", "ship", "boat" })
        {
            VehicleEditorPanel editor = new(_session, CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key));
            editor.Dock = DockStyle.Top;
            editor.ApplyRequested += async (_, vehicle) => await _mutations.SetVehicleAsync(_document.RelativePath, key, vehicle).ConfigureAwait(true);
            _vehicles[key] = editor;
            root.Controls.Add(editor);
        }
        return root;
    }

    private Control BuildAudioTab()
    {
        Panel root = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        _audio.Columns.Add(new DataGridViewTextBoxColumn { Name = "Slot", HeaderText = "Slot", ReadOnly = true, Width = 170 });
        _audio.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Asset Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _audio.Columns.Add(new DataGridViewTextBoxColumn { Name = "Volume", HeaderText = "Volume", Width = 75 });
        _audio.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pitch", HeaderText = "Pitch", Width = 75 });
        _audio.Columns.Add(new DataGridViewTextBoxColumn { Name = "Pan", HeaderText = "Pan", Width = 75 });
        FlowLayoutPanel buttons = ButtonBar();
        Button choose = new() { Text = "Choose / Preview Asset…", AutoSize = true };
        Button apply = new() { Text = "Apply all music and sounds", AutoSize = true };
        Button reset = new() { Text = "Reload", AutoSize = true };
        buttons.Controls.AddRange([choose, apply, reset]);
        choose.Click += (_, _) => ChooseAudioAsset();
        apply.Click += async (_, _) => await ApplyAudioAsync().ConfigureAwait(true);
        reset.Click += (_, _) => { LoadAudioRows(); ClearPending("Audio"); };
        root.Controls.Add(_audio);
        root.Controls.Add(buttons);
        return root;
    }

    private Control BuildGraphicsTab()
    {
        SplitContainer split = ControlHelpers.CreateSplitContainer(Orientation.Horizontal, 260, 220, 260);
        TableLayoutPanel fields = FormTable();
        AddRow(fields, "Title image 1", _title1);
        AddRow(fields, "Title image 2", _title2);
        AddRow(fields, "Battleback 1", _battleback1);
        AddRow(fields, "Battleback 2", _battleback2);
        AddRow(fields, "Battle-test battler", _battler);
        AddRow(fields, "Battler hue", _battlerHue);
        split.Panel1.Controls.Add(fields);

        TabControl lower = new() { Dock = DockStyle.Fill };
        Panel motions = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _attackMotions.Columns.Add(new DataGridViewTextBoxColumn { Name = "WeaponType", HeaderText = "Weapon Type", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _attackMotions.Columns.Add(new DataGridViewComboBoxColumn { Name = "Motion", HeaderText = "Motion", Width = 120, DataSource = new[] { new GridOption(0, "Thrust"), new GridOption(1, "Swing"), new GridOption(2, "Missile") }, DisplayMember = "Text", ValueMember = "Value" });
        _attackMotions.Columns.Add(new DataGridViewTextBoxColumn { Name = "Image", HeaderText = "Weapon Image ID", Width = 130 });
        FlowLayoutPanel motionButtons = ButtonBar();
        Button applyMotions = new() { Text = "Apply attack motions", AutoSize = true };
        motionButtons.Controls.Add(applyMotions);
        applyMotions.Click += async (_, _) => await ApplyAttackMotionsAsync().ConfigureAwait(true);
        motions.Controls.Add(_attackMotions); motions.Controls.Add(motionButtons);
        lower.TabPages.Add(new TabPage("Attack Motions") { Controls = { motions } });

        Panel magic = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        _magicSkills.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "Skill Type ID", Width = 110 });
        _magicSkills.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Skill Type", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        FlowLayoutPanel magicButtons = ButtonBar();
        Button addMagic = new() { Text = "Add", AutoSize = true };
        Button removeMagic = new() { Text = "Remove", AutoSize = true };
        Button applyMagic = new() { Text = "Apply magic skill types", AutoSize = true };
        magicButtons.Controls.AddRange([addMagic, removeMagic, applyMagic]);
        addMagic.Click += (_, _) => { _magicSkills.Rows.Add(1, ResolveSystemArrayName(_snapshot?.SkillTypes, 1)); MarkPending("MagicSkills"); };
        removeMagic.Click += (_, _) => { RemoveSelectedRows(_magicSkills); MarkPending("MagicSkills"); };
        applyMagic.Click += async (_, _) => await ApplyMagicSkillsAsync().ConfigureAwait(true);
        magic.Controls.Add(_magicSkills); magic.Controls.Add(magicButtons);
        lower.TabPages.Add(new TabPage("Magic Skill Types") { Controls = { magic } });
        split.Panel2.Controls.Add(lower);
        return split;
    }

    private Control BuildTypesTab()
    {
        TabControl types = new() { Dock = DockStyle.Fill };
        AddArrayEditor(types, "Elements", "elements", ReferenceTargetKind.Element, reserveZero: true);
        AddArrayEditor(types, "Skill Types", "skillTypes", ReferenceTargetKind.SkillType, reserveZero: true);
        AddArrayEditor(types, "Weapon Types", "weaponTypes", ReferenceTargetKind.WeaponType, reserveZero: true);
        AddArrayEditor(types, "Armor Types", "armorTypes", ReferenceTargetKind.ArmorType, reserveZero: true);
        AddArrayEditor(types, "Equipment Types", "equipTypes", ReferenceTargetKind.EquipmentType, reserveZero: true);
        AddArrayEditor(types, "Switches", "switches", ReferenceTargetKind.Switch, reserveZero: true);
        AddArrayEditor(types, "Variables", "variables", ReferenceTargetKind.Variable, reserveZero: true);
        return types;
    }

    private Control BuildTermsTab()
    {
        _terms.Dock = DockStyle.Fill;
        _terms.EntryCommitRequested += async (_, e) => await CommitTermAsync(e).ConfigureAwait(true);
        _terms.GenerateRequested += (_, e) => RequestTermTextGeneration(e);
        return _terms;
    }

    private Control BuildBattleTestTab()
    {
        Panel root = new() { Dock = DockStyle.Fill, Padding = new Padding(8) };
        FlowLayoutPanel top = new() { Dock = DockStyle.Top, Height = 50, Padding = new Padding(6), WrapContents = false };
        top.Controls.AddRange([new Label { Text = "Test troop ID", AutoSize = true, Margin = new Padding(4, 8, 4, 4) }, _testTroop]);
        _testBattlers.Columns.Add(new DataGridViewTextBoxColumn { Name = "ActorId", HeaderText = "Actor ID", Width = 90 });
        _testBattlers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Actor", HeaderText = "Actor", ReadOnly = true, Width = 160 });
        _testBattlers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Level", HeaderText = "Level", Width = 75 });
        for (int slot = 0; slot < 5; slot++) _testBattlers.Columns.Add(new DataGridViewTextBoxColumn { Name = "Equip" + slot, HeaderText = "Equip " + (slot + 1), Width = 80 });
        FlowLayoutPanel buttons = ButtonBar();
        Button add = new() { Text = "Add actor", AutoSize = true };
        Button remove = new() { Text = "Remove", AutoSize = true };
        Button apply = new() { Text = "Apply battle test", AutoSize = true };
        buttons.Controls.AddRange([add, remove, apply]);
        add.Click += (_, _) => { _testBattlers.Rows.Add(1, ResolveRecordName(MvDocumentKind.Actors, 1), 1, 0, 0, 0, 0, 0); MarkPending("BattleTest"); };
        remove.Click += (_, _) => { RemoveSelectedRows(_testBattlers); MarkPending("BattleTest"); };
        apply.Click += async (_, _) => await ApplyBattleTestAsync().ConfigureAwait(true);
        root.Controls.Add(_testBattlers); root.Controls.Add(buttons); root.Controls.Add(top);
        return root;
    }

    private void WireEvents()
    {
        WireText(_gameTitle, "gameTitle");
        WireText(_currency, "currencyUnit");
        WireText(_locale, "locale");
        foreach ((string key, CheckBox check) in _options)
        {
            check.CheckedChanged += async (_, _) => { if (!_loading) await _mutations.SetSystemBoolAsync(_document.RelativePath, key, check.Checked).ConfigureAwait(true); };
        }
        WireAsset(_title1, "title1Name"); WireAsset(_title2, "title2Name"); WireAsset(_battleback1, "battleback1Name"); WireAsset(_battleback2, "battleback2Name");
        _battler.SelectionChanged += async (_, _) => { if (!_loading) await _mutations.SetSystemStringAsync(_document.RelativePath, "battlerName", _battler.Selection.AssetName).ConfigureAwait(true); };
        _battlerHue.ValueChanged += async (_, _) => { if (!_loading) await _mutations.SetSystemIntAsync(_document.RelativePath, "battlerHue", decimal.ToInt32(_battlerHue.Value)).ConfigureAwait(true); };
        _party.CellEndEdit += (_, e) => { if (e.RowIndex >= 0) { UpdateNameCell(_party, e.RowIndex, 0, 1, MvDocumentKind.Actors); MarkPending("Party"); } };
        _magicSkills.CellEndEdit += (_, e) => { if (e.RowIndex >= 0) { UpdateSystemNameCell(_magicSkills, e.RowIndex, 0, 1, _snapshot?.SkillTypes); MarkPending("MagicSkills"); } };
        _testBattlers.CellEndEdit += (_, e) => { if (e.RowIndex >= 0) { if (e.ColumnIndex == 0) UpdateNameCell(_testBattlers, e.RowIndex, 0, 1, MvDocumentKind.Actors); MarkPending("BattleTest"); } };
        _audio.CellValueChanged += (_, _) => { if (!_loading) MarkPending("Audio"); };
        _attackMotions.CellValueChanged += (_, _) => { if (!_loading) MarkPending("AttackMotions"); };
        _magicSkills.CellValueChanged += (_, _) => { if (!_loading) MarkPending("MagicSkills"); };
        _testTroop.ValueChanged += (_, _) => { if (!_loading) MarkPending("BattleTest"); };
    }

    private void LoadValues()
    {
        _loading = true;
        try
        {
            _snapshot = MvPhase9EReader.ReadSystem(_document);
            _gameTitle.Text = _snapshot.GameTitle; _gameTitle.Tag = _gameTitle.Text;
            _currency.Text = _snapshot.CurrencyUnit; _currency.Tag = _currency.Text;
            _locale.Text = _snapshot.Locale; _locale.Tag = _locale.Text;
            _editMapId.Text = _snapshot.EditMapId.ToString(CultureInfo.InvariantCulture);
            _versionId.Text = _snapshot.VersionId.ToString(CultureInfo.InvariantCulture);
            for (int index = 0; index < _windowTone.Length; index++) _windowTone[index].Value = index < _snapshot.WindowTone.Count ? Math.Clamp(_snapshot.WindowTone[index], -255, 255) : 0;
            LoadParty(); LoadOptions(); LoadPositions(); LoadAudioRows(); LoadGraphics(); LoadArrayEditors(); _terms.Bind(_snapshot.Terms); LoadBattleTest();
            _pendingBuffers.Clear();
            PendingChangesChanged?.Invoke(this, EventArgs.Empty);
            _status.Text = "System.json, Types and Terms are edited transactionally. Null slots, unknown object fields and extension rows are preserved.";
        }
        catch (Exception exception)
        {
            _status.Text = exception.Message;
        }
        finally { _loading = false; }
    }

    private void LoadParty()
    {
        _party.Rows.Clear();
        if (_snapshot is null) return;
        foreach (int id in _snapshot.PartyMembers) _party.Rows.Add(id, ResolveRecordName(MvDocumentKind.Actors, id));
    }

    private void LoadOptions()
    {
        if (_snapshot is null) return;
        foreach ((string key, CheckBox check) in _options) check.Checked = _snapshot.Options.GetValueOrDefault(key);
        for (int index = 0; index < _menuCommands.Length; index++) _menuCommands[index].Checked = index < _snapshot.MenuCommands.Count && _snapshot.MenuCommands[index];
    }

    private void LoadPositions()
    {
        if (_snapshot is null) return;
        _playerPosition.Value = new SystemPosition(_snapshot.StartMapId, _snapshot.StartX, _snapshot.StartY);
        _vehicles["boat"].Value = _snapshot.Boat;
        _vehicles["ship"].Value = _snapshot.Ship;
        _vehicles["airship"].Value = _snapshot.Airship;
    }

    private void LoadAudioRows()
    {
        _audio.Rows.Clear();
        if (_snapshot is null) return;
        AddAudioRow("titleBgm", "Title BGM", AssetCategory.BackgroundMusic, _snapshot.TitleBgm);
        AddAudioRow("battleBgm", "Battle BGM", AssetCategory.BackgroundMusic, _snapshot.BattleBgm);
        AddAudioRow("victoryMe", "Victory ME", AssetCategory.MusicEffects, _snapshot.VictoryMe);
        AddAudioRow("defeatMe", "Defeat ME", AssetCategory.MusicEffects, _snapshot.DefeatMe);
        AddAudioRow("gameoverMe", "Game Over ME", AssetCategory.MusicEffects, _snapshot.GameoverMe);
        string[] names = ["Cursor", "OK", "Cancel", "Buzzer", "Equip", "Save", "Load", "Battle Start", "Escape", "Enemy Attack", "Enemy Damage", "Enemy Collapse", "Boss Collapse 1", "Boss Collapse 2", "Actor Damage", "Actor Collapse", "Recovery", "Miss", "Evasion", "Magic Evasion", "Reflection", "Shop", "Use Item", "Use Skill"];
        for (int index = 0; index < _snapshot.Sounds.Count; index++) AddAudioRow("sounds/" + index, index < names.Length ? names[index] : $"Sound {index + 1}", AssetCategory.SoundEffects, _snapshot.Sounds[index]);
    }

    private void AddAudioRow(string path, string label, AssetCategory category, MvAudioSettingSnapshot value)
    {
        int row = _audio.Rows.Add(label, value.Name, value.Volume, value.Pitch, value.Pan);
        _audio.Rows[row].Tag = new AudioRowTag(path, category, value);
    }

    private void ChooseAudioAsset()
    {
        if (_audio.SelectedRows.Count != 1 || _audio.SelectedRows[0].Tag is not AudioRowTag tag) return;
        DataGridViewRow row = _audio.SelectedRows[0];
        string currentName = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        AssetKey? current = string.IsNullOrWhiteSpace(currentName) ? null : new AssetKey(tag.Category, currentName);
        using AssetPickerDialog dialog = new(_session, new AssetPickerRequest(
            new HashSet<AssetCategory> { tag.Category }, current, AllowNone: true, MediaKind: AssetMediaKind.Audio,
            Title: "Choose " + Convert.ToString(row.Cells[0].Value, CultureInfo.InvariantCulture)));
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        row.Cells[1].Value = dialog.SelectedAsset?.LogicalName ?? string.Empty;
        MarkPending("Audio");
    }

    private void LoadGraphics()
    {
        if (_snapshot is null) return;
        _title1.Value = _snapshot.Title1Name; _title2.Value = _snapshot.Title2Name;
        _battleback1.Value = _snapshot.Battleback1Name; _battleback2.Value = _snapshot.Battleback2Name;
        _battler.Selection = new AssetIndexSelection(_snapshot.BattlerName, 0);
        _battlerHue.Value = Math.Clamp(_snapshot.BattlerHue, 0, 360);
        _attackMotions.Rows.Clear();
        int count = Math.Max(_snapshot.AttackMotions.Count, Math.Max(0, _snapshot.WeaponTypes.Count - 1));
        for (int index = 0; index < count; index++)
        {
            MvAttackMotionSnapshot motion = index < _snapshot.AttackMotions.Count ? _snapshot.AttackMotions[index] : new MvAttackMotionSnapshot(0, 0, "{}");
            int row = _attackMotions.Rows.Add(ResolveSystemArrayName(_snapshot.WeaponTypes, index + 1), motion.Type, motion.WeaponImageId);
            _attackMotions.Rows[row].Tag = motion;
        }
        _magicSkills.Rows.Clear();
        foreach (int id in _snapshot.MagicSkills) _magicSkills.Rows.Add(id, ResolveSystemArrayName(_snapshot.SkillTypes, id));
    }

    private void LoadArrayEditors()
    {
        if (_snapshot is null) return;
        _arrayEditors["elements"].Bind(_snapshot.Elements);
        _arrayEditors["skillTypes"].Bind(_snapshot.SkillTypes);
        _arrayEditors["weaponTypes"].Bind(_snapshot.WeaponTypes);
        _arrayEditors["armorTypes"].Bind(_snapshot.ArmorTypes);
        _arrayEditors["equipTypes"].Bind(_snapshot.EquipTypes);
        _arrayEditors["switches"].Bind(_snapshot.Switches);
        _arrayEditors["variables"].Bind(_snapshot.Variables);
    }

    private void LoadBattleTest()
    {
        _testBattlers.Rows.Clear();
        if (_snapshot is null) return;
        _testTroop.Value = Math.Clamp(_snapshot.TestTroopId, 0, decimal.ToInt32(_testTroop.Maximum));
        foreach (MvBattleTestBattlerSnapshot battler in _snapshot.TestBattlers)
        {
            int[] equips = battler.Equipment.Concat(Enumerable.Repeat(0, 5)).Take(5).ToArray();
            int row = _testBattlers.Rows.Add(battler.ActorId, ResolveRecordName(MvDocumentKind.Actors, battler.ActorId), battler.Level, equips[0], equips[1], equips[2], equips[3], equips[4]);
            _testBattlers.Rows[row].Tag = battler;
        }
    }

    private async ValueTask ApplyPartyAsync()
    {
        int[] values = _party.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row => ParseInt(row.Cells[0].Value)).Where(id => id > 0).ToArray();
        await _mutations.SetSystemArrayAsync(_document.RelativePath, "partyMembers", values).ConfigureAwait(true);
        ClearPending("Party");
    }

    private async ValueTask ApplyPlayerPositionAsync(SystemPosition position)
    {
        await using ProjectTransaction transaction = _session.BeginTransaction("Change player starting position");
        transaction.Add(new SetJsonInt32Command(_document.RelativePath, JsonPath.Root.Append("startMapId"), position.MapId));
        transaction.Add(new SetJsonInt32Command(_document.RelativePath, JsonPath.Root.Append("startX"), position.X));
        transaction.Add(new SetJsonInt32Command(_document.RelativePath, JsonPath.Root.Append("startY"), position.Y));
        await transaction.CommitAsync().ConfigureAwait(true);
    }

    private async ValueTask ApplyAudioAsync()
    {
        List<IEditorCommand> commands = [];
        foreach (DataGridViewRow row in _audio.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow))
        {
            if (row.Tag is not AudioRowTag tag) continue;
            MvAudioSettingSnapshot edited = tag.Original with { Name = Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture) ?? string.Empty, Volume = Math.Clamp(ParseInt(row.Cells[2].Value), 0, 100), Pitch = Math.Clamp(ParseInt(row.Cells[3].Value), 50, 150), Pan = Math.Clamp(ParseInt(row.Cells[4].Value), -100, 100) };
            JsonPath path = ParseRelativeJsonPath(tag.Path);
            commands.Add(new SetJsonValueCommand(_document.RelativePath, path, Phase9EMutationService.SerializeAudio(edited), "Change " + row.Cells[0].Value));
        }
        await using ProjectTransaction transaction = _session.BeginTransaction("Change System music and sounds");
        foreach (IEditorCommand command in commands) transaction.Add(command);
        await transaction.CommitAsync().ConfigureAwait(true);
        ClearPending("Audio");
    }

    private async ValueTask ApplyAttackMotionsAsync()
    {
        List<MvAttackMotionSnapshot> values = [];
        foreach (DataGridViewRow row in _attackMotions.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow))
        {
            MvAttackMotionSnapshot original = row.Tag as MvAttackMotionSnapshot ?? new MvAttackMotionSnapshot(0, 0, "{}");
            values.Add(original with { Type = Math.Clamp(ParseInt(row.Cells[1].Value), 0, 2), WeaponImageId = Math.Max(0, ParseInt(row.Cells[2].Value)) });
        }
        await _mutations.SetAttackMotionsAsync(_document.RelativePath, values).ConfigureAwait(true);
        ClearPending("AttackMotions");
    }

    private async ValueTask ApplyMagicSkillsAsync()
    {
        int[] values = _magicSkills.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow).Select(row => ParseInt(row.Cells[0].Value)).Where(id => id > 0).ToArray();
        await _mutations.SetSystemArrayAsync(_document.RelativePath, "magicSkills", values).ConfigureAwait(true);
        ClearPending("MagicSkills");
    }

    private async ValueTask ApplyBattleTestAsync()
    {
        List<MvBattleTestBattlerSnapshot> values = [];
        foreach (DataGridViewRow row in _testBattlers.Rows.Cast<DataGridViewRow>().Where(row => !row.IsNewRow))
        {
            MvBattleTestBattlerSnapshot original = row.Tag as MvBattleTestBattlerSnapshot ?? new MvBattleTestBattlerSnapshot(0, 1, [0, 0, 0, 0, 0], "{}");
            int[] equips = Enumerable.Range(3, 5).Select(index => Math.Max(0, ParseInt(row.Cells[index].Value))).ToArray();
            values.Add(original with { ActorId = Math.Max(0, ParseInt(row.Cells[0].Value)), Level = Math.Clamp(ParseInt(row.Cells[2].Value), 1, 99), Equipment = equips });
        }
        await using ProjectTransaction transaction = _session.BeginTransaction("Change battle test profile");
        transaction.Add(new SetJsonInt32Command(_document.RelativePath, JsonPath.Root.Append("testTroopId"), decimal.ToInt32(_testTroop.Value)));
        transaction.Add(new SetJsonValueCommand(_document.RelativePath, JsonPath.Root.Append("testBattlers"), Phase9EMutationService.SerializeBattleTestBattlers(values)));
        await transaction.CommitAsync().ConfigureAwait(true);
        ClearPending("BattleTest");
    }

    private async ValueTask CommitTermAsync(TermEntryCommitEventArgs e)
    {
        if (e.OriginalValue is not null)
        {
            IReadOnlyList<string> missing = MvTermPlaceholderValidator.Missing(e.OriginalValue, e.Value);
            if (missing.Count > 0)
            {
                DialogResult result = MessageBox.Show(this, $"The edited term removes required placeholder(s): {string.Join(", ", missing)}. Save anyway?", "Placeholder warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes) { _terms.Bind(_snapshot?.Terms); return; }
            }
        }
        if (e.Category == TermCategory.Messages) await _mutations.SetTermMessageAsync(_document.RelativePath, e.Key, e.Value).ConfigureAwait(true);
        else await _mutations.SetSystemArrayEntryAsync(_document.RelativePath, "terms/" + e.Category.ToPropertyName(), e.Index, e.Value).ConfigureAwait(true);
    }

    private void AddArrayEditor(TabControl tabs, string title, string property, ReferenceTargetKind kind, bool reserveZero)
    {
        SystemStringArrayEditor editor = new(title, reserveZero) { Dock = DockStyle.Fill };
        editor.EntryCommitRequested += async (_, e) => await _mutations.SetSystemArrayEntryAsync(_document.RelativePath, property, e.Index, e.Value).ConfigureAwait(true);
        editor.ResizeRequested += async (_, values) => await ResizeSystemArrayAsync(property, kind, values).ConfigureAwait(true);
        editor.GenerateRequested += (_, e) => RequestSystemArrayTextGeneration(title, property, e);
        _arrayEditors[property] = editor;
        tabs.TabPages.Add(new TabPage(title) { Controls = { editor } });
    }

    private void RequestSystemArrayTextGeneration(string title, string property, StringArrayGenerateEventArgs e)
    {
        if (e.Index == 0 && (property == "elements" || property == "skillTypes" || property == "weaponTypes" || property == "armorTypes" || property == "equipTypes")) return;
        var field = new TextTargetDescriptor(
            "system.types", _document.RelativePath, "system", $"/{property}/{e.Index}", "name",
            $"{title} #{e.Index}", e.CurrentValue ?? string.Empty, false, false, 80,
            new HashSet<TextImportMode>([TextImportMode.Replace, TextImportMode.FillIfBlank, TextImportMode.Append, TextImportMode.Prepend]),
            property, "System Types", ["phase8.mv.system-type-name"]);
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["system.typeCategory"] = property, ["system.typeIndex"] = e.Index, ["target.current"] = e.CurrentValue ?? string.Empty
        };
        TextGenerationRequested?.Invoke(this, new DirectTextGenerationRequestedEventArgs(
            new TextRecordTargetDescriptor("system.types", _document.RelativePath, "system", $"System / {title} / #{e.Index}", [field], "name", context)));
    }

    private void RequestTermTextGeneration(TermGenerateEventArgs e)
    {
        string path = e.Category == TermCategory.Messages
            ? $"/terms/messages/{e.Key}"
            : $"/terms/{e.Category.ToPropertyName()}/{e.Index}";
        var field = new TextTargetDescriptor(
            "system.terms", _document.RelativePath, "system", path, "term", e.Label, e.CurrentValue ?? string.Empty, false, false, 160,
            new HashSet<TextImportMode>([TextImportMode.Replace, TextImportMode.FillIfBlank, TextImportMode.Append, TextImportMode.Prepend]),
            e.Key, "System Terms", ["phase8.mv.system-term"]);
        var context = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["system.termCategory"] = e.Category.ToString(), ["system.termKey"] = e.Key, ["system.termMeaning"] = e.Label, ["target.current"] = e.CurrentValue ?? string.Empty
        };
        TextGenerationRequested?.Invoke(this, new DirectTextGenerationRequestedEventArgs(
            new TextRecordTargetDescriptor("system.terms", _document.RelativePath, "system", $"System / Terms / {e.Label}", [field], "term", context)));
    }

    private async ValueTask ResizeSystemArrayAsync(string property, ReferenceTargetKind kind, IReadOnlyList<string?> values)
    {
        IReadOnlyList<string?> current = GetSystemArray(property);
        Dictionary<int, int> remappings = [];
        List<ProjectReference> exactReferences = [];
        List<ProjectReference> heuristicReferences = [];
        if (values.Count < current.Count)
        {
            for (int id = values.Count; id < current.Count; id++)
            {
                foreach (ProjectReference reference in _session.References.FindInbound(new ReferenceTarget(kind, id)))
                {
                    if (reference.Confidence == ReferenceConfidence.Exact) exactReferences.Add(reference);
                    else heuristicReferences.Add(reference);
                }
            }
            exactReferences = exactReferences.Distinct().ToList();
            heuristicReferences = heuristicReferences.Distinct().ToList();

            if (exactReferences.Count > 0)
            {
                int survivingMaximum = values.Count - 1;
                if (survivingMaximum < 1)
                {
                    MessageBox.Show(this, $"The reduction is blocked by {exactReferences.Count:N0} exact reference(s), and no surviving ID is available for remapping.", "Change maximum blocked", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    LoadArrayEditors();
                    return;
                }
                using SystemArrayReferenceRemapDialog dialog = new(property, current, survivingMaximum, exactReferences, heuristicReferences.Count);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    LoadArrayEditors();
                    return;
                }
                remappings = dialog.Remappings.ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            else
            {
                string detail = heuristicReferences.Count == 0
                    ? "No exact or heuristic references are currently indexed."
                    : $"{heuristicReferences.Count:N0} probable script, note, or plugin reference(s) may mention removed IDs. They are report-only and will not be rewritten.\n\n" +
                      string.Join("\n", heuristicReferences.Take(12).Select(reference => $"• {reference.Source.RelativePath} {reference.Source.JsonPath}")) +
                      (heuristicReferences.Count > 12 ? $"\n• …and {heuristicReferences.Count - 12:N0} more" : string.Empty);
                DialogResult answer = MessageBox.Show(this, $"Reduce {property} from {current.Count - 1:N0} to {Math.Max(0, values.Count - 1):N0} usable entries? IDs are never shifted.\n\n{detail}", "Change maximum", MessageBoxButtons.YesNo, heuristicReferences.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                {
                    LoadArrayEditors();
                    return;
                }
            }
        }

        await using ProjectTransaction transaction = _session.BeginTransaction($"Change {property} maximum");
        HashSet<(string RelativePath, string JsonPath)> changedLocations = [];
        foreach (ProjectReference reference in exactReferences)
        {
            if (!reference.Target.Id.HasValue || !remappings.TryGetValue(reference.Target.Id.Value, out int destinationId))
                throw new InvalidOperationException($"Exact reference to removed {kind} ID {reference.Target.Id?.ToString(CultureInfo.InvariantCulture) ?? "?"} has no remapping.");
            string sourcePath = reference.Source.RelativePath.Replace('\\', '/');
            if (!changedLocations.Add((sourcePath, reference.Source.JsonPath))) continue;
            transaction.Add(new SetJsonValueCommand(
                sourcePath,
                new JsonPath(reference.Source.JsonPath),
                destinationId.ToString(CultureInfo.InvariantCulture),
                $"Remap {kind} {reference.Target.Id.Value} to {destinationId}"));
        }
        transaction.Add(new SetJsonValueCommand(
            _document.RelativePath,
            JsonPath.Root.Append(property),
            JsonSerializer.Serialize(values),
            $"Resize System {property}"));
        await transaction.CommitAsync().ConfigureAwait(true);
    }

    private IReadOnlyList<string?> GetSystemArray(string property) => property switch
    {
        "elements" => _snapshot?.Elements ?? [], "skillTypes" => _snapshot?.SkillTypes ?? [], "weaponTypes" => _snapshot?.WeaponTypes ?? [],
        "armorTypes" => _snapshot?.ArmorTypes ?? [], "equipTypes" => _snapshot?.EquipTypes ?? [], "switches" => _snapshot?.Switches ?? [], "variables" => _snapshot?.Variables ?? [], _ => []
    };

    private void WireText(TextBox control, string property)
    {
        control.Validated += async (_, _) =>
        {
            if (_loading || string.Equals(control.Tag as string, control.Text, StringComparison.Ordinal)) return;
            await _mutations.SetSystemStringAsync(_document.RelativePath, property, control.Text).ConfigureAwait(true);
        };
    }

    private void WireAsset(AssetPicker picker, string property) => picker.ValueChanged += async (_, _) => { if (!_loading) await _mutations.SetSystemStringAsync(_document.RelativePath, property, picker.Value).ConfigureAwait(true); };

    private void ConfigurePartyGrid()
    {
        _party.Columns.Add(new DataGridViewTextBoxColumn { Name = "ActorId", HeaderText = "Actor ID", Width = 100 });
        _party.Columns.Add(new DataGridViewTextBoxColumn { Name = "Actor", HeaderText = "Actor", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private string ResolveRecordName(MvDocumentKind kind, int id)
    {
        if (id <= 0) return "(none)";
        try
        {
            MvDatabaseFileSummary? summary = _session.Workspace.Snapshot.Database.Find(kind);
            if (summary is null) return $"{kind} {id}";
            IMutableProjectDocument document = _session.Workspace.OpenDocumentAsync(summary.RelativePath).AsTask().GetAwaiter().GetResult();
            return document is MutableJsonProjectDocument json ? json.GetString(JsonPath.Root.Append(id).Append("name")) ?? $"Missing #{id}" : $"{kind} {id}";
        }
        catch { return $"{kind} {id}"; }
    }

    private static string ResolveSystemArrayName(IReadOnlyList<string?>? values, int id) => values is not null && id >= 0 && id < values.Count ? values[id] ?? "(null)" : $"Missing #{id}";
    private void UpdateNameCell(DataGridView grid, int rowIndex, int idColumn, int nameColumn, MvDocumentKind kind) => grid.Rows[rowIndex].Cells[nameColumn].Value = ResolveRecordName(kind, ParseInt(grid.Rows[rowIndex].Cells[idColumn].Value));
    private static void UpdateSystemNameCell(DataGridView grid, int rowIndex, int idColumn, int nameColumn, IReadOnlyList<string?>? values) => grid.Rows[rowIndex].Cells[nameColumn].Value = ResolveSystemArrayName(values, ParseInt(grid.Rows[rowIndex].Cells[idColumn].Value));
    private static void RemoveSelectedRows(DataGridView grid) { foreach (DataGridViewRow row in grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(row => row.Index)) if (!row.IsNewRow) grid.Rows.Remove(row); }
    private static int ParseInt(object? value) => int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
    private static JsonPath ParseRelativeJsonPath(string relative)
    {
        JsonPath path = JsonPath.Root;
        foreach (string segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries)) path = int.TryParse(segment, out int index) ? path.Append(index) : path.Append(segment);
        return path;
    }

    private static DataGridView CreateGrid() => new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoGenerateColumns = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.Fixed3D };
    private static FlowLayoutPanel ButtonBar() => new() { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(5), WrapContents = false };
    private static TableLayoutPanel FormTable()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), ColumnCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table;
    }
    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 9, 8, 3) }, 0, row);
        control.Margin = new Padding(3, 3, 3, 8); table.Controls.Add(control, 1, row);
    }

    private void MarkPending(string buffer)
    {
        if (_loading || !_pendingBuffers.Add(buffer)) return;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearPending(string buffer)
    {
        if (!_pendingBuffers.Remove(buffer)) return;
        PendingChangesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, EditorCommandEvent e)
    {
        if (!e.Targets.Any(target => string.Equals(target.NormalizedRelativePath, _document.RelativePath, PathComparison))) return;
        if (_applyingPending) return;
        if (HasPendingChanges)
        {
            void PreservePending()
            {
                _status.Text = "System.json changed while this window has unapplied section buffers. The local buffers were preserved; apply or discard them before reloading.";
            }
            if (InvokeRequired) BeginInvoke((Action)PreservePending); else PreservePending();
            return;
        }
        if (InvokeRequired) BeginInvoke(new Action(LoadValues)); else LoadValues();
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record AudioRowTag(string Path, AssetCategory Category, MvAudioSettingSnapshot Original);
    private sealed record GridOption(int Value, string Text) { public override string ToString() => Text; }
}

public sealed record SystemPosition(int MapId, int X, int Y);

public sealed class SystemPositionEditor : GroupBox
{
    private readonly MapLocationPickerControl _location;
    public SystemPositionEditor(MvProjectSession session, string title)
    {
        Text = title; Height = 112; Padding = new Padding(8);
        _location = new MapLocationPickerControl(session) { Dock = DockStyle.Fill };
        Button apply = new() { Text = "Apply", Dock = DockStyle.Bottom, Height = 30 };
        Controls.Add(_location);
        Controls.Add(apply);
        apply.Click += (_, _) => ApplyRequested?.Invoke(this, Value);
    }
    public event EventHandler<SystemPosition>? ApplyRequested;
    public SystemPosition Value { get => _location.Value; set => _location.Value = value; }
}

public sealed class VehicleEditorPanel : GroupBox
{
    private readonly AssetIndexSelectionControl _graphic;
    private readonly SystemPositionEditor _position;
    private readonly AudioPicker _bgm;
    private MvVehicleSnapshot _value = new(string.Empty, 0, 0, 0, 0, MvAudioSettingSnapshot.Empty, "{}");
    public VehicleEditorPanel(MvProjectSession session, string title)
    {
        Text = title; Height = 270; Padding = new Padding(8);
        _graphic = new AssetIndexSelectionControl(session, AssetCategory.Characters, true) { Dock = DockStyle.Top };
        _position = new SystemPositionEditor(session, "Starting position") { Dock = DockStyle.Top };
        _bgm = new AudioPicker(session, AssetCategory.BackgroundMusic) { Dock = DockStyle.Top };
        Button apply = new() { Text = "Apply " + title, Dock = DockStyle.Bottom, Height = 30 };
        Controls.Add(_bgm); Controls.Add(_position); Controls.Add(_graphic); Controls.Add(apply);
        apply.Click += (_, _) => ApplyRequested?.Invoke(this, Value);
    }
    public event EventHandler<MvVehicleSnapshot>? ApplyRequested;
    public MvVehicleSnapshot Value
    {
        get => _value with { CharacterName = _graphic.Selection.AssetName, CharacterIndex = _graphic.Selection.Index, StartMapId = _position.Value.MapId, StartX = _position.Value.X, StartY = _position.Value.Y, Bgm = _value.Bgm with { Name = _bgm.AssetName, Volume = _bgm.Volume, Pitch = _bgm.Pitch, Pan = _bgm.Pan } };
        set { _value = value; _graphic.Selection = new AssetIndexSelection(value.CharacterName, value.CharacterIndex); _position.Value = new SystemPosition(value.StartMapId, value.StartX, value.StartY); _bgm.AssetName = value.Bgm.Name; _bgm.Volume = value.Bgm.Volume; _bgm.Pitch = value.Bgm.Pitch; _bgm.Pan = value.Bgm.Pan; }
    }
}

public sealed record StringArrayEntryCommitEventArgs(int Index, string? Value);
public sealed record StringArrayGenerateEventArgs(int Index, string? CurrentValue);

public sealed class SystemStringArrayEditor : UserControl
{
    private readonly string _title;
    private readonly bool _reserveZero;
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false };
    private IReadOnlyList<string?> _values = [];
    public SystemStringArrayEditor(string title, bool reserveZero)
    {
        _title = title; _reserveZero = reserveZero;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", ReadOnly = true, Width = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = title, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(5), WrapContents = false };
        Button add = new() { Text = "Increase Maximum", AutoSize = true };
        Button change = new() { Text = "Change Maximum…", AutoSize = true };
        Button blank = new() { Text = "Blank", AutoSize = true };
        Button setNull = new() { Text = "Set Null", AutoSize = true };
        Button generate = new() { Text = "✨ Generate…", AutoSize = true };
        buttons.Controls.AddRange([add, change, blank, setNull, generate]);
        Controls.Add(_grid); Controls.Add(buttons);
        _grid.CellBeginEdit += (_, e) =>
        {
            if (_reserveZero && e.RowIndex == 0) { e.Cancel = true; return; }
            if (e.RowIndex >= 0 && e.ColumnIndex == 1 && _grid.Rows[e.RowIndex].Tag is null)
            {
                _grid.Rows[e.RowIndex].Cells[1].Value = string.Empty;
                _grid.Rows[e.RowIndex].Cells[1].Style.ForeColor = SystemColors.ControlText;
            }
        };
        _grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            int index = Convert.ToInt32(_grid.Rows[e.RowIndex].Cells[0].Value, CultureInfo.InvariantCulture);
            EntryCommitRequested?.Invoke(this, new StringArrayEntryCommitEventArgs(index, Convert.ToString(_grid.Rows[e.RowIndex].Cells[1].Value, CultureInfo.InvariantCulture) ?? string.Empty));
        };
        add.Click += (_, _) => RequestResize(_values.Count + 1);
        change.Click += (_, _) => PromptMaximum();
        blank.Click += (_, _) => CommitSelected(string.Empty);
        setNull.Click += (_, _) => CommitSelected(null);
        generate.Click += (_, _) => RequestGenerateSelected();
    }
    public event EventHandler<StringArrayEntryCommitEventArgs>? EntryCommitRequested;
    public event EventHandler<IReadOnlyList<string?>>? ResizeRequested;
    public event EventHandler<StringArrayGenerateEventArgs>? GenerateRequested;
    public void Bind(IReadOnlyList<string?> values)
    {
        _values = values?.ToArray() ?? [];
        _grid.Rows.Clear();
        for (int index = 0; index < _values.Count; index++)
        {
            int row = _grid.Rows.Add(index, _values[index] ?? "(null)");
            _grid.Rows[row].Tag = _values[index];
            _grid.Rows[row].Cells[1].Style.ForeColor = _values[index] is null ? SystemColors.GrayText : SystemColors.ControlText;
        }
    }
    private void RequestGenerateSelected()
    {
        if (_grid.SelectedRows.Count != 1) return;
        int index = Convert.ToInt32(_grid.SelectedRows[0].Cells[0].Value, CultureInfo.InvariantCulture);
        if (_reserveZero && index == 0) return;
        string? current = index >= 0 && index < _values.Count ? _values[index] : null;
        GenerateRequested?.Invoke(this, new StringArrayGenerateEventArgs(index, current));
    }

    private void CommitSelected(string? value)
    {
        if (_grid.SelectedRows.Count != 1) return;
        int index = Convert.ToInt32(_grid.SelectedRows[0].Cells[0].Value, CultureInfo.InvariantCulture);
        if (_reserveZero && index == 0) return;
        EntryCommitRequested?.Invoke(this, new StringArrayEntryCommitEventArgs(index, value));
    }
    private void PromptMaximum()
    {
        using Form dialog = new() { Text = "Change " + _title + " Maximum", Width = 360, Height = 150, StartPosition = FormStartPosition.CenterParent };
        NumericUpDown number = new() { Minimum = _reserveZero ? 0 : 1, Maximum = 999999, Value = Math.Max(_reserveZero ? 0 : 1, _values.Count - (_reserveZero ? 1 : 0)), Dock = DockStyle.Top, Margin = new Padding(10) };
        Button okay = new() { Text = "Preview and Apply", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 34 };
        dialog.Controls.Add(number); dialog.Controls.Add(okay); dialog.AcceptButton = okay;
        if (dialog.ShowDialog(this) == DialogResult.OK) RequestResize(decimal.ToInt32(number.Value) + (_reserveZero ? 1 : 0));
    }
    private void RequestResize(int length)
    {
        List<string?> values = _values.Take(length).ToList();
        while (values.Count < length) values.Add(string.Empty);
        if (_reserveZero && values.Count > 0 && _values.Count > 0) values[0] = _values[0];
        ResizeRequested?.Invoke(this, values);
    }
}

internal sealed class SystemArrayReferenceRemapDialog : Form
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly int _maximum;

    public SystemArrayReferenceRemapDialog(
        string property,
        IReadOnlyList<string?> currentValues,
        int survivingMaximum,
        IReadOnlyList<ProjectReference> exactReferences,
        int heuristicReferenceCount)
    {
        _maximum = survivingMaximum;
        Text = "Remap References Before Reducing " + property;
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Removed ID", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Removed Name", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "References", HeaderText = "Exact References", ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Destination", HeaderText = $"Destination ID (1–{survivingMaximum})" });
        foreach (IGrouping<int, ProjectReference> group in exactReferences
                     .Where(reference => reference.Target.Id.HasValue)
                     .GroupBy(reference => reference.Target.Id!.Value)
                     .OrderBy(group => group.Key))
        {
            string name = group.Key >= 0 && group.Key < currentValues.Count ? currentValues[group.Key] ?? "(null)" : "(missing)";
            string locations = string.Join("; ", group.Take(3).Select(reference => $"{reference.Source.RelativePath} {reference.Source.JsonPath}"));
            if (group.Count() > 3) locations += $"; …and {group.Count() - 3:N0} more";
            int row = _grid.Rows.Add(group.Key, name, $"{group.Count():N0} — {locations}", 1);
            _grid.Rows[row].Tag = group.Key;
        }
        Label explanation = new()
        {
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(8),
            Text = $"Every exact reference to a removed ID must be redirected to a surviving ID. The remapping and array reduction are one undoable transaction. {heuristicReferenceCount:N0} probable script, note, or plugin reference(s) remain report-only and are never rewritten automatically."
        };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button use = new() { Text = "Use Remapping", AutoSize = true };
        Button cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        use.Click += (_, _) => AcceptRemapping();
        buttons.Controls.AddRange([use, cancel]);
        Controls.Add(_grid);
        Controls.Add(explanation);
        Controls.Add(buttons);
        CancelButton = cancel;
    }

    public IReadOnlyDictionary<int, int> Remappings { get; private set; } = new Dictionary<int, int>();

    private void AcceptRemapping()
    {
        _grid.EndEdit();
        Dictionary<int, int> result = [];
        foreach (DataGridViewRow row in _grid.Rows)
        {
            int source = (int)(row.Tag ?? 0);
            if (!int.TryParse(Convert.ToString(row.Cells["Destination"].Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int destination)
                || destination < 1 || destination > _maximum)
            {
                MessageBox.Show(this, $"Destination for removed ID {source} must be between 1 and {_maximum}.", "Invalid remapping", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            result[source] = destination;
        }
        Remappings = result;
        DialogResult = DialogResult.OK;
    }
}

public enum TermCategory { Basic, Parameters, Commands, Messages }
public static class TermCategoryExtensions
{
    public static string ToPropertyName(this TermCategory category) => category switch { TermCategory.Basic => "basic", TermCategory.Parameters => "params", TermCategory.Commands => "commands", TermCategory.Messages => "messages", _ => throw new ArgumentOutOfRangeException(nameof(category)) };
}
public sealed record TermEntryCommitEventArgs(TermCategory Category, int Index, string Key, string? OriginalValue, string? Value);
public sealed record TermGenerateEventArgs(TermCategory Category, int Index, string Key, string Label, string? CurrentValue);

public sealed class TermsEditorControl : UserControl
{
    private readonly ComboBox _category = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search terms…" };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private MvTermsSnapshot? _terms;
    private bool _loading;
    public TermsEditorControl()
    {
        _category.Items.AddRange(Enum.GetValues<TermCategory>().Cast<object>().ToArray()); _category.SelectedIndex = 0;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Key", HeaderText = "ID / Key", ReadOnly = true, Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Label", HeaderText = "Meaning", ReadOnly = true, Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Text", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(5), WrapContents = false };
        Button setNull = new() { Text = "Set Null", AutoSize = true }; Button restore = new() { Text = "Reload", AutoSize = true }; Button generate = new() { Text = "✨ Generate…", AutoSize = true };
        buttons.Controls.AddRange([setNull, restore, generate]);
        Controls.Add(_grid); Controls.Add(_search); Controls.Add(_category); Controls.Add(buttons);
        _category.SelectedIndexChanged += (_, _) => Rebuild();
        _search.TextChanged += (_, _) => Rebuild();
        _grid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 2 && _grid.Rows[e.RowIndex].Tag is TermRow { Original: null })
            {
                _grid.Rows[e.RowIndex].Cells[2].Value = string.Empty;
                _grid.Rows[e.RowIndex].Cells[2].Style.ForeColor = SystemColors.ControlText;
            }
        };
        _grid.CellEndEdit += (_, e) => CommitRow(e.RowIndex);
        setNull.Click += (_, _) => { if (_grid.SelectedRows.Count == 1) Commit(_grid.SelectedRows[0], null); };
        restore.Click += (_, _) => Rebuild();
        generate.Click += (_, _) => RequestGenerateSelected();
    }
    public event EventHandler<TermEntryCommitEventArgs>? EntryCommitRequested;
    public event EventHandler<TermGenerateEventArgs>? GenerateRequested;
    public void Bind(MvTermsSnapshot? terms) { _terms = terms; Rebuild(); }
    private void RequestGenerateSelected()
    {
        if (_grid.SelectedRows.Count != 1 || _grid.SelectedRows[0].Tag is not TermRow term) return;
        string label = Convert.ToString(_grid.SelectedRows[0].Cells["Label"].Value, CultureInfo.InvariantCulture) ?? term.Key;
        GenerateRequested?.Invoke(this, new TermGenerateEventArgs(term.Category, term.Index, term.Key, label, term.Original));
    }
    private void Rebuild()
    {
        _loading = true;
        try
        {
            _grid.Rows.Clear(); if (_terms is null) return;
            TermCategory category = _category.SelectedItem is TermCategory value ? value : TermCategory.Basic;
            string search = _search.Text.Trim();
            if (category == TermCategory.Messages)
            {
                foreach ((string key, string? text) in _terms.Messages.OrderBy(pair => pair.Key, StringComparer.Ordinal)) AddRow(-1, key, Humanize(key), text, category, search);
            }
            else
            {
                IReadOnlyList<string?> values = category switch { TermCategory.Basic => _terms.Basic, TermCategory.Parameters => _terms.Parameters, TermCategory.Commands => _terms.Commands, _ => [] };
                for (int index = 0; index < values.Count; index++) AddRow(index, index.ToString(CultureInfo.InvariantCulture), KnownLabel(category, index), values[index], category, search);
            }
        }
        finally { _loading = false; }
    }
    private void AddRow(int index, string key, string label, string? value, TermCategory category, string search)
    {
        if (search.Length > 0 && !key.Contains(search, StringComparison.OrdinalIgnoreCase) && !label.Contains(search, StringComparison.OrdinalIgnoreCase) && !(value?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)) return;
        int row = _grid.Rows.Add(key, label, value ?? "(null)");
        _grid.Rows[row].Tag = new TermRow(category, index, key, value);
        if (value is null) _grid.Rows[row].Cells[2].Style.ForeColor = SystemColors.GrayText;
    }
    private void CommitRow(int rowIndex) { if (!_loading && rowIndex >= 0) Commit(_grid.Rows[rowIndex], Convert.ToString(_grid.Rows[rowIndex].Cells[2].Value, CultureInfo.InvariantCulture) ?? string.Empty); }
    private void Commit(DataGridViewRow row, string? value)
    {
        if (row.Tag is not TermRow term) return;
        EntryCommitRequested?.Invoke(this, new TermEntryCommitEventArgs(term.Category, term.Index, term.Key, term.Original, value));
    }
    private static string KnownLabel(TermCategory category, int index)
    {
        string[][] labels =
        [
            ["Level", "Level (abbr.)", "HP", "HP (abbr.)", "MP", "MP (abbr.)", "TP", "TP (abbr.)", "EXP", "EXP (abbr.)"],
            ["Max HP", "Max MP", "Attack", "Defense", "M.Attack", "M.Defense", "Agility", "Luck", "Hit Rate", "Evasion Rate"],
            ["Fight", "Escape", "Attack", "Guard", "Item", "Skill", "Equip", "Status", "Formation", "Save", "Game End", "Options", "Weapon", "Armor", "Key Item", "Equip", "Optimize", "Clear", "New Game", "Continue", "(reserved)", "To Title", "Cancel", "(reserved)", "Buy", "Sell"],
            []
        ];
        string[] chosen = labels[(int)category]; return index >= 0 && index < chosen.Length ? chosen[index] : string.Empty;
    }
    private static string Humanize(string value) => string.Concat(value.Select((character, index) => index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
    private sealed record TermRow(TermCategory Category, int Index, string Key, string? Original);
}
