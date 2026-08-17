using System.Globalization;
using Playroma.JRPG.Application;
using Playroma.JRPG.Core;
using Playroma.JRPG.DatabaseEditing;
using Playroma.JRPG.Documents.Json;
using Playroma.JRPG.Documents.Mutable;
using Playroma.JRPG.Editing;
using Playroma.JRPG.FormulaSandbox;
using Playroma.JRPG.RpgMakerMV;

namespace Playroma.JRPG.Editor.WinForms.Database;

/// <summary>Complete raw-backed Phase 9B editor for RPG Maker MV Skills and Items.</summary>
public sealed class UsableItemEditorControl : UserControl, IDatabaseEditorBody
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly IDatabaseEditorDefinition _definition;
    private readonly MvDocumentKind _kind;
    private readonly Phase9BMutationService _phase9B;
    private readonly Label _selectionMessage = Phase9BEditorUi.SelectionLabel();
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, Height = 72, ScrollBars = ScrollBars.Vertical };
    private readonly IconPicker _icon = new() { Dock = DockStyle.Fill };
    private readonly NamedIdPicker _skillType = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _mpCost = Phase9BEditorUi.Number(0, 999999);
    private readonly NumericUpDown _tpCost = Phase9BEditorUi.Number(0, 999999);
    private readonly ComboBox _itemType = Phase9BEditorUi.OptionCombo(new Dictionary<int, string> { [1] = "Regular Item", [2] = "Key Item" });
    private readonly NumericUpDown _price = Phase9BEditorUi.Number(0, 99999999);
    private readonly CheckBox _consumable = new() { Text = "Consumed when used", AutoSize = true };
    private readonly ComboBox _scope = Phase9BEditorUi.OptionCombo(BuiltInDatabaseDefinitions.ScopeOptions);
    private readonly ComboBox _occasion = Phase9BEditorUi.OptionCombo(BuiltInDatabaseDefinitions.OccasionOptions);
    private readonly NumericUpDown _speed = Phase9BEditorUi.Number(-9999, 9999);
    private readonly NumericUpDown _success = Phase9BEditorUi.Number(0, 100);
    private readonly NumericUpDown _repeats = Phase9BEditorUi.Number(1, 99);
    private readonly NumericUpDown _tpGain = Phase9BEditorUi.Number(0, 100);
    private readonly ComboBox _hitType = Phase9BEditorUi.OptionCombo(BuiltInDatabaseDefinitions.HitTypeOptions);
    private readonly NamedIdPicker _animation = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _damageType = Phase9BEditorUi.OptionCombo(BuiltInDatabaseDefinitions.DamageTypeOptions);
    private readonly NamedIdPicker _element = new() { Dock = DockStyle.Fill, AllowNone = false };
    private readonly CodeTextEditorControl _formula = new() { Dock = DockStyle.Fill, MinimumSize = new Size(300, 120) };
    private readonly NumericUpDown _variance = Phase9BEditorUi.Number(0, 100);
    private readonly CheckBox _critical = new() { Text = "May score a critical hit", AutoSize = true };
    private readonly TextBox _message1 = new() { Dock = DockStyle.Fill };
    private readonly TextBox _message2 = new() { Dock = DockStyle.Fill };
    private readonly NamedIdPicker _requiredWeapon1 = new() { Dock = DockStyle.Fill };
    private readonly NamedIdPicker _requiredWeapon2 = new() { Dock = DockStyle.Fill };
    private readonly EffectListEditor _effects = new() { Dock = DockStyle.Fill };
    private readonly Label _usagePreview = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), AutoEllipsis = true };
    private Phase9BProjectCatalog _catalog = Phase9BProjectCatalog.Empty;
    private int _recordId;
    private bool _loading;

    public UsableItemEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        MvDocumentKind kind)
    {
        if (kind is not MvDocumentKind.Skills and not MvDocumentKind.Items)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _kind = kind;
        _phase9B = new Phase9BMutationService(session.Editing);
        Dock = DockStyle.Fill;
        _icon.AttachSession(session);
        _formula.SetLanguage(CodeEditorLanguage.JavaScript);

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("General") { Controls = { BuildGeneralPage() } });
        tabs.TabPages.Add(new TabPage("Invocation") { Controls = { BuildInvocationPage() } });
        tabs.TabPages.Add(new TabPage("Damage") { Controls = { BuildDamagePage() } });
        tabs.TabPages.Add(new TabPage("Effects") { Controls = { _effects } });
        Controls.Add(tabs);
        Controls.Add(_usagePreview);
        Controls.Add(_selectionMessage);
    }

    public Control View => this;
    public bool OwnsTraits => false;
    public bool OwnsEffects => true;
    public bool OwnsFormula => true;

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
                _recordId = 0;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0
                    ? $"Select a {_definition.SingularName.ToLowerInvariant()} to edit it."
                    : $"The complete {_definition.SingularName} editor requires one populated record.";
                _effects.ClearBinding();
                return;
            }

            Enabled = true;
            _recordId = records[0].Id;
            _catalog = await Phase9BProjectCatalogLoader.LoadAsync(_session, cancellationToken).ConfigureAwait(true);
            PopulateCatalogs();
            if (_kind == MvDocumentKind.Skills)
            {
                LoadSkill(Phase9BRecordReader.ReadSkill(_document, _recordId));
            }
            else
            {
                LoadItem(Phase9BRecordReader.ReadItem(_document, _recordId));
            }
            JsonPath effectsPath = JsonPath.Root.Append(_recordId).Append("effects");
            if (_document.Syntax.Resolve(effectsPath) is JsonArraySyntax) _effects.Bind(_session, _document, effectsPath, _catalog);
            else _effects.ClearBinding();
            _selectionMessage.Text = $"{_definition.SingularName} {_recordId:D4} — all standard fields, damage, effects, formulas, and unknown nested data are preserved.";
            UpdatePreview();
        }
        finally
        {
            _loading = false;
        }
    }

    private Control BuildGeneralPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        int row = 0;
        Phase9BEditorUi.AddRow(table, row++, "Name", _name);
        Phase9BEditorUi.AddRow(table, row++, "Description", _description);
        Phase9BEditorUi.AddRow(table, row++, "Icon", _icon);
        if (_kind == MvDocumentKind.Skills)
        {
            Phase9BEditorUi.AddRow(table, row++, "Skill Type", _skillType);
            Phase9BEditorUi.AddRow(table, row++, "MP Cost", _mpCost);
            Phase9BEditorUi.AddRow(table, row++, "TP Cost", _tpCost);
            Phase9BEditorUi.AddRow(table, row++, "Message 1", _message1);
            Phase9BEditorUi.AddRow(table, row++, "Message 2", _message2);
            FlowLayoutPanel presets = new() { AutoSize = true, WrapContents = true };
            foreach ((string label, string text) in new[]
                     {
                         ("Casts…", " casts %1!"),
                         ("Does…", " does %1!"),
                         ("Uses…", " uses %1!")
                     })
            {
                Button button = new() { Text = label, AutoSize = true, Tag = text };
                button.Click += (_, _) => _message1.Text = Convert.ToString(button.Tag, CultureInfo.InvariantCulture) ?? string.Empty;
                presets.Controls.Add(button);
            }
            Phase9BEditorUi.AddRow(table, row++, "Message Presets", presets);
            Phase9BEditorUi.AddRow(table, row++, "Required Weapon 1", _requiredWeapon1);
            Phase9BEditorUi.AddRow(table, row++, "Required Weapon 2", _requiredWeapon2);
        }
        else
        {
            Phase9BEditorUi.AddRow(table, row++, "Item Type", _itemType);
            Phase9BEditorUi.AddRow(table, row++, "Price", _price);
            Phase9BEditorUi.AddRow(table, row++, "Consumable", _consumable);
        }
        Button apply = new() { Text = "Apply General Settings", AutoSize = true };
        apply.Click += async (_, _) => await ApplyGeneralAsync().ConfigureAwait(true);
        Phase9BEditorUi.AddRow(table, row, string.Empty, apply);
        return Phase9BEditorUi.Scroll(table);
    }

    private Control BuildInvocationPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Scope", _scope);
        Phase9BEditorUi.AddRow(table, 1, "Occasion", _occasion);
        Phase9BEditorUi.AddRow(table, 2, "Speed", _speed);
        Phase9BEditorUi.AddRow(table, 3, "Success Rate", _success);
        Phase9BEditorUi.AddRow(table, 4, "Repeats", _repeats);
        Phase9BEditorUi.AddRow(table, 5, "TP Gain", _tpGain);
        Phase9BEditorUi.AddRow(table, 6, "Hit Type", _hitType);
        Phase9BEditorUi.AddRow(table, 7, "Animation", _animation);
        Button apply = new() { Text = "Apply Invocation Settings", AutoSize = true };
        apply.Click += async (_, _) => await ApplyInvocationAsync().ConfigureAwait(true);
        Phase9BEditorUi.AddRow(table, 8, string.Empty, apply);
        return Phase9BEditorUi.Scroll(table);
    }

    private Control BuildDamagePage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Damage Type", _damageType);
        Phase9BEditorUi.AddRow(table, 1, "Element", _element);
        Phase9BEditorUi.AddRow(table, 2, "Variance (%)", _variance);
        Phase9BEditorUi.AddRow(table, 3, "Critical", _critical);
        Label warning = new()
        {
            Text = "Formula testing uses an isolated MV Core Approximation worker. Plugin-modified combat behavior is not simulated.",
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            ForeColor = SystemColors.GrayText
        };
        Phase9BEditorUi.AddRow(table, 4, "Formula", warning);
        table.Controls.Add(_formula, 1, 5);
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = true };
        Button apply = new() { Text = "Apply Damage", AutoSize = true };
        Button test = new() { Text = "Test Formula…", AutoSize = true };
        apply.Click += async (_, _) => await ApplyDamageAsync().ConfigureAwait(true);
        test.Click += async (_, _) => await TestFormulaAsync().ConfigureAwait(true);
        buttons.Controls.AddRange([apply, test]);
        Phase9BEditorUi.AddRow(table, 6, string.Empty, buttons);
        return table;
    }

    private void PopulateCatalogs()
    {
        _skillType.SetChoices(_catalog.SystemTypes.SkillTypes, "Skill Type", includeNone: false);
        _animation.SetChoices(_catalog.Animations, "Animation", includeNone: true);
        _requiredWeapon1.SetChoices(_catalog.SystemTypes.WeaponTypes, "Weapon Type", includeNone: true);
        _requiredWeapon2.SetChoices(_catalog.SystemTypes.WeaponTypes, "Weapon Type", includeNone: true);
        List<NamedDatabaseValue> elements = [new(-1, "Normal Attack"), new(0, "None")];
        elements.AddRange(_catalog.SystemTypes.Elements.Where(value => value.Id > 0));
        _element.SetChoices(elements, "Element", includeNone: false);
    }

    private void LoadSkill(MvSkillRecordSnapshot snapshot)
    {
        LoadCommon(snapshot.Common);
        _skillType.SelectedId = snapshot.SkillTypeId;
        Phase9BEditorUi.Set(_mpCost, snapshot.MpCost);
        Phase9BEditorUi.Set(_tpCost, snapshot.TpCost);
        _message1.Text = snapshot.Message1;
        _message2.Text = snapshot.Message2;
        _requiredWeapon1.SelectedId = snapshot.RequiredWeaponType1;
        _requiredWeapon2.SelectedId = snapshot.RequiredWeaponType2;
    }

    private void LoadItem(MvItemRecordSnapshot snapshot)
    {
        LoadCommon(snapshot.Common);
        Phase9BEditorUi.SelectOption(_itemType, snapshot.ItemTypeId);
        Phase9BEditorUi.Set(_price, snapshot.Price);
        _consumable.Checked = snapshot.Consumable;
    }

    private void LoadCommon(MvUsableItemSnapshot common)
    {
        _name.Text = common.Name;
        _description.Text = common.Description;
        _icon.Value = common.IconIndex;
        Phase9BEditorUi.SelectOption(_scope, common.Scope);
        Phase9BEditorUi.SelectOption(_occasion, common.Occasion);
        Phase9BEditorUi.Set(_speed, common.Speed);
        Phase9BEditorUi.Set(_success, common.SuccessRate);
        Phase9BEditorUi.Set(_repeats, common.Repeats);
        Phase9BEditorUi.Set(_tpGain, common.TpGain);
        Phase9BEditorUi.SelectOption(_hitType, common.HitType);
        _animation.SelectedId = common.AnimationId;
        Phase9BEditorUi.SelectOption(_damageType, common.Damage.Type);
        _element.SelectedId = common.Damage.ElementId;
        _formula.Text = common.Damage.Formula;
        Phase9BEditorUi.Set(_variance, common.Damage.Variance);
        _critical.Checked = common.Damage.Critical;
    }

    private async Task ApplyGeneralAsync()
    {
        if (_loading || _recordId <= 0) return;
        try
        {
            List<(string Key, object Value)> values =
            [
                ("name", _name.Text),
                ("description", _description.Text),
                ("iconIndex", _icon.Value)
            ];
            if (_kind == MvDocumentKind.Skills)
            {
                values.AddRange([
                    ("stypeId", _skillType.SelectedId),
                    ("mpCost", decimal.ToInt32(_mpCost.Value)),
                    ("tpCost", decimal.ToInt32(_tpCost.Value)),
                    ("message1", _message1.Text),
                    ("message2", _message2.Text),
                    ("requiredWtypeId1", _requiredWeapon1.SelectedId),
                    ("requiredWtypeId2", _requiredWeapon2.SelectedId)
                ]);
            }
            else
            {
                values.AddRange([
                    ("itypeId", Phase9BEditorUi.SelectedOption(_itemType)),
                    ("price", decimal.ToInt32(_price.Value)),
                    ("consumable", _consumable.Checked)
                ]);
            }
            await ApplyFieldsAsync($"Change {_definition.SingularName} {_recordId} general settings", values).ConfigureAwait(true);
            UpdatePreview();
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyInvocationAsync()
    {
        if (_loading || _recordId <= 0) return;
        try
        {
            await ApplyFieldsAsync(
                $"Change {_definition.SingularName} {_recordId} invocation",
                [
                    ("scope", Phase9BEditorUi.SelectedOption(_scope)),
                    ("occasion", Phase9BEditorUi.SelectedOption(_occasion)),
                    ("speed", decimal.ToInt32(_speed.Value)),
                    ("successRate", decimal.ToInt32(_success.Value)),
                    ("repeats", decimal.ToInt32(_repeats.Value)),
                    ("tpGain", decimal.ToInt32(_tpGain.Value)),
                    ("hitType", Phase9BEditorUi.SelectedOption(_hitType)),
                    ("animationId", _animation.SelectedId)
                ]).ConfigureAwait(true);
            UpdatePreview();
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyDamageAsync()
    {
        if (_loading || _recordId <= 0) return;
        try
        {
            MvDamageSnapshot damage = new(
                Phase9BEditorUi.SelectedOption(_damageType),
                _element.SelectedId,
                _formula.Text,
                decimal.ToInt32(_variance.Value),
                _critical.Checked,
                "{}");
            await _phase9B.SetDamageAsync(_document.RelativePath, _recordId, damage).ConfigureAwait(true);
            UpdatePreview();
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task TestFormulaAsync()
    {
        if (_recordId <= 0) return;
        FormulaItemSnapshot item = new(
            _recordId,
            _name.Text,
            Phase9BEditorUi.SelectedOption(_damageType),
            _element.SelectedId,
            decimal.ToInt32(_variance.Value),
            _critical.Checked);
        await FormulaSandboxDialog.ShowAsync((IWin32Window?)FindForm() ?? this, _session, _formula.Text, item).ConfigureAwait(true);
    }

    private async Task ApplyFieldsAsync(string description, IReadOnlyList<(string Key, object Value)> values)
    {
        DatabaseRecordHandle handle = new(_kind, _document.RelativePath, _recordId);
        await using ProjectTransaction transaction = _session.Editing.BeginTransaction(description);
        foreach ((string key, object value) in values)
        {
            DatabaseFieldDescriptor field = _definition.RecordSchema.FindField(key);
            ProjectDiagnostic? diagnostic = field.ValidateObject(value);
            if (diagnostic is not null && diagnostic.Severity >= DiagnosticSeverity.Error)
            {
                throw new ArgumentException(diagnostic.Message, key);
            }
            transaction.Add(new SetJsonValueCommand(
                handle.RelativePath,
                DatabaseValueAccessor.Combine(handle.Path, field.RelativePath),
                field.EncodeObject(value),
                $"Set {field.DisplayName}"));
        }
        await transaction.CommitAsync().ConfigureAwait(true);
    }

    private void UpdatePreview()
    {
        string scope = Phase9BEditorUi.SelectedOptionName(_scope);
        string occasion = Phase9BEditorUi.SelectedOptionName(_occasion);
        string hit = Phase9BEditorUi.SelectedOptionName(_hitType);
        string damage = Phase9BEditorUi.SelectedOptionName(_damageType);
        string identity = _kind == MvDocumentKind.Skills
            ? _skillType.SelectedId == 0 ? "Unassigned Skill Type" : $"Skill Type {_skillType.SelectedId}"
            : $"{Phase9BEditorUi.SelectedOptionName(_itemType)} · {(_consumable.Checked ? "Consumable" : "Reusable")}";
        _usagePreview.Text = $"{identity}   |   {scope} · {occasion}   |   {hit} · {damage}";
    }
}

/// <summary>Complete raw-backed Phase 9B editor for RPG Maker MV Weapons and Armors.</summary>
public sealed class EquipmentEditorControl : UserControl, IDatabaseEditorBody
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly IDatabaseEditorDefinition _definition;
    private readonly MvDocumentKind _kind;
    private readonly Phase9BMutationService _phase9B;
    private readonly Label _selectionMessage = Phase9BEditorUi.SelectionLabel();
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly TextBox _description = new() { Dock = DockStyle.Fill, Multiline = true, Height = 72, ScrollBars = ScrollBars.Vertical };
    private readonly IconPicker _icon = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _price = Phase9BEditorUi.Number(0, 99999999);
    private readonly NamedIdPicker _equipmentType = new() { Dock = DockStyle.Fill, AllowNone = false };
    private readonly NamedIdPicker _itemType = new() { Dock = DockStyle.Fill, AllowNone = false };
    private readonly NamedIdPicker _animation = new() { Dock = DockStyle.Fill };
    private readonly EquipmentParameterEditor _parameters = new() { Dock = DockStyle.Fill };
    private readonly TraitListEditor _traits = new() { Dock = DockStyle.Fill };
    private Phase9BProjectCatalog _catalog = Phase9BProjectCatalog.Empty;
    private int _recordId;
    private bool _loading;

    public EquipmentEditorControl(
        MvProjectSession session,
        MutableJsonProjectDocument document,
        IDatabaseEditorDefinition definition,
        MvDocumentKind kind)
    {
        if (kind is not MvDocumentKind.Weapons and not MvDocumentKind.Armors) throw new ArgumentOutOfRangeException(nameof(kind));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _kind = kind;
        _phase9B = new Phase9BMutationService(session.Editing);
        Dock = DockStyle.Fill;
        _icon.AttachSession(session);

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("General") { Controls = { BuildGeneralPage() } });
        tabs.TabPages.Add(new TabPage("Parameters") { Controls = { BuildParameterPage() } });
        tabs.TabPages.Add(new TabPage("Traits") { Controls = { _traits } });
        Controls.Add(tabs);
        Controls.Add(_selectionMessage);
    }

    public Control View => this;
    public bool OwnsTraits => true;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;

    public async ValueTask BindAsync(IReadOnlyList<DatabaseRecordHandle> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            if (records.Count != 1 || _document.Syntax.Resolve(records[0].Path) is not JsonObjectSyntax)
            {
                _recordId = 0;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0 ? "Select equipment to edit it." : "The complete equipment editor requires one populated record.";
                _traits.ClearBinding();
                return;
            }
            Enabled = true;
            _recordId = records[0].Id;
            _catalog = await Phase9BProjectCatalogLoader.LoadAsync(_session, cancellationToken).ConfigureAwait(true);
            _equipmentType.SetChoices(_catalog.SystemTypes.EquipmentTypes, "Equipment Type", includeNone: false);
            _itemType.SetChoices(
                _kind == MvDocumentKind.Weapons ? _catalog.SystemTypes.WeaponTypes : _catalog.SystemTypes.ArmorTypes,
                _kind == MvDocumentKind.Weapons ? "Weapon Type" : "Armor Type",
                includeNone: false);
            _animation.SetChoices(_catalog.Animations, "Animation", includeNone: true);

            MvEquipmentRecordSnapshot snapshot = Phase9BRecordReader.ReadEquipment(_document, _recordId);
            _name.Text = snapshot.Name;
            _description.Text = snapshot.Description;
            _icon.Value = snapshot.IconIndex;
            Phase9BEditorUi.Set(_price, snapshot.Price);
            _equipmentType.SelectedId = snapshot.EquipmentTypeId;
            _itemType.SelectedId = snapshot.TypeId;
            _animation.SelectedId = snapshot.AnimationId;
            _parameters.Values = NormalizeParameters(snapshot.Parameters);
            JsonPath traitsPath = JsonPath.Root.Append(_recordId).Append("traits");
            if (_document.Syntax.Resolve(traitsPath) is JsonArraySyntax) _traits.Bind(_session, _document, traitsPath, _catalog);
            else _traits.ClearBinding();
            _selectionMessage.Text = $"{_definition.SingularName} {_recordId:D4} — project-defined equipment slots and types are resolved from System.json.";
        }
        finally { _loading = false; }
    }

    private Control BuildGeneralPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Name", _name);
        Phase9BEditorUi.AddRow(table, 1, "Description", _description);
        Phase9BEditorUi.AddRow(table, 2, "Icon", _icon);
        Phase9BEditorUi.AddRow(table, 3, "Price", _price);
        Phase9BEditorUi.AddRow(table, 4, "Equipment Slot", _equipmentType);
        Phase9BEditorUi.AddRow(table, 5, _kind == MvDocumentKind.Weapons ? "Weapon Type" : "Armor Type", _itemType);
        if (_kind == MvDocumentKind.Weapons) Phase9BEditorUi.AddRow(table, 6, "Attack Animation", _animation);
        FlowLayoutPanel actions = new() { AutoSize = true, WrapContents = true };
        Button apply = new() { Text = "Apply General Settings", AutoSize = true };
        Button compare = new() { Text = "Compare Equipment…", AutoSize = true };
        apply.Click += async (_, _) => await ApplyGeneralAsync().ConfigureAwait(true);
        compare.Click += (_, _) => ShowComparison();
        actions.Controls.AddRange([apply, compare]);
        Phase9BEditorUi.AddRow(table, 7, string.Empty, actions);
        return Phase9BEditorUi.Scroll(table);
    }

    private Control BuildParameterPage()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        Button apply = new() { Text = "Apply Parameters as One Undo Entry", Dock = DockStyle.Bottom, Height = 34 };
        apply.Click += async (_, _) => await ApplyParametersAsync().ConfigureAwait(true);
        panel.Controls.Add(_parameters);
        panel.Controls.Add(apply);
        return panel;
    }

    private async Task ApplyGeneralAsync()
    {
        if (_loading || _recordId <= 0) return;
        try
        {
            List<(string Key, object Value)> values =
            [
                ("name", _name.Text),
                ("description", _description.Text),
                ("iconIndex", _icon.Value),
                ("price", decimal.ToInt32(_price.Value)),
                ("etypeId", _equipmentType.SelectedId),
                (_kind == MvDocumentKind.Weapons ? "wtypeId" : "atypeId", _itemType.SelectedId)
            ];
            if (_kind == MvDocumentKind.Weapons) values.Add(("animationId", _animation.SelectedId));
            await ApplyFieldsAsync($"Change {_definition.SingularName} {_recordId} general settings", values).ConfigureAwait(true);
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyParametersAsync()
    {
        if (_recordId <= 0) return;
        try { await _phase9B.SetEquipmentParametersAsync(_document.RelativePath, _recordId, _parameters.Values).ConfigureAwait(true); }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyFieldsAsync(string description, IReadOnlyList<(string Key, object Value)> values)
    {
        DatabaseRecordHandle handle = new(_kind, _document.RelativePath, _recordId);
        await using ProjectTransaction transaction = _session.Editing.BeginTransaction(description);
        foreach ((string key, object value) in values)
        {
            DatabaseFieldDescriptor field = _definition.RecordSchema.FindField(key);
            ProjectDiagnostic? diagnostic = field.ValidateObject(value);
            if (diagnostic is not null && diagnostic.Severity >= DiagnosticSeverity.Error) throw new ArgumentException(diagnostic.Message, key);
            transaction.Add(new SetJsonValueCommand(handle.RelativePath, DatabaseValueAccessor.Combine(handle.Path, field.RelativePath), field.EncodeObject(value), $"Set {field.DisplayName}"));
        }
        await transaction.CommitAsync().ConfigureAwait(true);
    }

    private void ShowComparison()
    {
        if (_recordId <= 0) return;
        using EquipmentComparisonDialog dialog = new(_document, _kind, _recordId, _parameters.Values);
        dialog.ShowDialog(this);
    }

    private static IReadOnlyList<int> NormalizeParameters(IReadOnlyList<int> values)
    {
        int[] result = new int[8];
        for (int index = 0; index < result.Length && index < values.Count; index++) result[index] = values[index];
        return result;
    }
}

/// <summary>Complete raw-backed Phase 9B editor for RPG Maker MV States.</summary>
public sealed class StateEditorControl : UserControl, IDatabaseEditorBody
{
    private readonly MvProjectSession _session;
    private readonly MutableJsonProjectDocument _document;
    private readonly IDatabaseEditorDefinition _definition;
    private readonly Phase9BMutationService _phase9B;
    private readonly Label _selectionMessage = Phase9BEditorUi.SelectionLabel();
    private readonly TextBox _name = new() { Dock = DockStyle.Fill };
    private readonly IconPicker _icon = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _restriction = Phase9BEditorUi.OptionCombo(BuiltInDatabaseDefinitions.StateRestrictionOptions);
    private readonly NumericUpDown _priority = Phase9BEditorUi.Number(0, 100);
    private readonly ComboBox _motion = Phase9BEditorUi.OptionCombo(new Dictionary<int, string> { [0] = "Normal", [1] = "Abnormal", [2] = "Sleep", [3] = "Dead" });
    private readonly ComboBox _overlay = Phase9BEditorUi.OptionCombo(Enumerable.Range(0, 11).ToDictionary(value => value, value => value == 0 ? "None" : $"Overlay {value}"));
    private readonly CheckBox _removeBattleEnd = new() { Text = "Remove at battle end", AutoSize = true };
    private readonly CheckBox _removeRestriction = new() { Text = "Remove by restriction", AutoSize = true };
    private readonly ComboBox _autoTiming = Phase9BEditorUi.OptionCombo(BuiltInDatabaseDefinitions.AutoRemovalTimingOptions);
    private readonly NumericUpDown _minimumTurns = Phase9BEditorUi.Number(0, 999999);
    private readonly NumericUpDown _maximumTurns = Phase9BEditorUi.Number(0, 999999);
    private readonly CheckBox _removeDamage = new() { Text = "Remove by damage", AutoSize = true };
    private readonly NumericUpDown _damageChance = Phase9BEditorUi.Number(0, 100);
    private readonly CheckBox _removeWalking = new() { Text = "Remove by walking", AutoSize = true };
    private readonly NumericUpDown _steps = Phase9BEditorUi.Number(0, 999999);
    private readonly TextBox[] _messages = Enumerable.Range(0, 4).Select(_ => new TextBox { Dock = DockStyle.Fill }).ToArray();
    private readonly Label[] _messagePreviews = Enumerable.Range(0, 4).Select(_ => new Label { Dock = DockStyle.Fill, ForeColor = SystemColors.GrayText, AutoEllipsis = true }).ToArray();
    private readonly TraitListEditor _traits = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _extensions = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private Phase9BProjectCatalog _catalog = Phase9BProjectCatalog.Empty;
    private int _recordId;
    private bool _loading;

    public StateEditorControl(MvProjectSession session, MutableJsonProjectDocument document, IDatabaseEditorDefinition definition)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _phase9B = new Phase9BMutationService(session.Editing);
        Dock = DockStyle.Fill;
        _icon.AttachSession(session);
        _extensions.Columns.Add("Field", "Preserved Extension Field");
        _extensions.Columns.Add("Value", "Raw JSON Value");

        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(new TabPage("General") { Controls = { BuildGeneralPage() } });
        tabs.TabPages.Add(new TabPage("Removal") { Controls = { BuildRemovalPage() } });
        tabs.TabPages.Add(new TabPage("Messages") { Controls = { BuildMessagePage() } });
        tabs.TabPages.Add(new TabPage("Traits") { Controls = { _traits } });
        tabs.TabPages.Add(new TabPage("Extension Fields") { Controls = { BuildExtensionPage() } });
        Controls.Add(tabs);
        Controls.Add(_selectionMessage);

        _autoTiming.SelectedIndexChanged += (_, _) => UpdateRemovalAvailability();
        _removeDamage.CheckedChanged += (_, _) => UpdateRemovalAvailability();
        _removeWalking.CheckedChanged += (_, _) => UpdateRemovalAvailability();
        foreach (TextBox message in _messages) message.TextChanged += (_, _) => UpdateMessagePreviews();
    }

    public Control View => this;
    public bool OwnsTraits => true;
    public bool OwnsEffects => false;
    public bool OwnsFormula => false;

    public async ValueTask BindAsync(IReadOnlyList<DatabaseRecordHandle> records, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _loading = true;
        try
        {
            if (records.Count != 1 || _document.Syntax.Resolve(records[0].Path) is not JsonObjectSyntax)
            {
                _recordId = 0;
                Enabled = false;
                _selectionMessage.Text = records.Count == 0 ? "Select a state to edit it." : "The complete State editor requires one populated state.";
                _traits.ClearBinding();
                _extensions.Rows.Clear();
                return;
            }
            Enabled = true;
            _recordId = records[0].Id;
            _catalog = await Phase9BProjectCatalogLoader.LoadAsync(_session, cancellationToken).ConfigureAwait(true);
            MvStateRecordSnapshot state = Phase9BRecordReader.ReadState(_document, _recordId);
            _name.Text = state.Name;
            _icon.Value = state.IconIndex;
            Phase9BEditorUi.SelectOption(_restriction, state.Restriction);
            Phase9BEditorUi.Set(_priority, state.Priority);
            Phase9BEditorUi.SelectOption(_motion, state.Motion);
            Phase9BEditorUi.SelectOption(_overlay, state.Overlay);
            _removeBattleEnd.Checked = state.RemoveAtBattleEnd;
            _removeRestriction.Checked = state.RemoveByRestriction;
            Phase9BEditorUi.SelectOption(_autoTiming, state.AutoRemovalTiming);
            Phase9BEditorUi.Set(_minimumTurns, state.MinimumTurns);
            Phase9BEditorUi.Set(_maximumTurns, state.MaximumTurns);
            _removeDamage.Checked = state.RemoveByDamage;
            Phase9BEditorUi.Set(_damageChance, state.ChanceByDamage);
            _removeWalking.Checked = state.RemoveByWalking;
            Phase9BEditorUi.Set(_steps, state.StepsToRemove);
            _messages[0].Text = state.Message1;
            _messages[1].Text = state.Message2;
            _messages[2].Text = state.Message3;
            _messages[3].Text = state.Message4;
            JsonPath traitsPath = JsonPath.Root.Append(_recordId).Append("traits");
            if (_document.Syntax.Resolve(traitsPath) is JsonArraySyntax) _traits.Bind(_session, _document, traitsPath, _catalog);
            else _traits.ClearBinding();
            _extensions.Rows.Clear();
            foreach ((string field, string raw) in state.ExtensionFields.OrderBy(pair => pair.Key, StringComparer.Ordinal)) _extensions.Rows.Add(field, raw);
            _selectionMessage.Text = $"State {_recordId:D4} — inactive values and plugin-created fields remain preserved rather than normalized.";
            UpdateRemovalAvailability();
            UpdateMessagePreviews();
        }
        finally { _loading = false; }
    }

    private Control BuildGeneralPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Name", _name);
        Phase9BEditorUi.AddRow(table, 1, "Icon", _icon);
        Phase9BEditorUi.AddRow(table, 2, "Restriction", _restriction);
        Phase9BEditorUi.AddRow(table, 3, "Priority", _priority);
        Phase9BEditorUi.AddRow(table, 4, "Side-view Motion", _motion);
        Phase9BEditorUi.AddRow(table, 5, "Side-view Overlay", _overlay);
        Button apply = new() { Text = "Apply General Settings", AutoSize = true };
        apply.Click += async (_, _) => await ApplyGeneralAsync().ConfigureAwait(true);
        Phase9BEditorUi.AddRow(table, 6, string.Empty, apply);
        return Phase9BEditorUi.Scroll(table);
    }

    private Control BuildRemovalPage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        Phase9BEditorUi.AddRow(table, 0, "Battle End", _removeBattleEnd);
        Phase9BEditorUi.AddRow(table, 1, "Restriction", _removeRestriction);
        Phase9BEditorUi.AddRow(table, 2, "Auto-removal Timing", _autoTiming);
        Phase9BEditorUi.AddRow(table, 3, "Minimum Turns", _minimumTurns);
        Phase9BEditorUi.AddRow(table, 4, "Maximum Turns", _maximumTurns);
        Phase9BEditorUi.AddRow(table, 5, "Damage", _removeDamage);
        Phase9BEditorUi.AddRow(table, 6, "Damage Chance (%)", _damageChance);
        Phase9BEditorUi.AddRow(table, 7, "Walking", _removeWalking);
        Phase9BEditorUi.AddRow(table, 8, "Steps to Remove", _steps);
        Button apply = new() { Text = "Apply Removal Rules as One Undo Entry", AutoSize = true };
        apply.Click += async (_, _) => await ApplyRemovalAsync().ConfigureAwait(true);
        Phase9BEditorUi.AddRow(table, 9, string.Empty, apply);
        return Phase9BEditorUi.Scroll(table);
    }

    private Control BuildMessagePage()
    {
        TableLayoutPanel table = Phase9BEditorUi.Table();
        string[] labels = ["Actor Infliction", "Enemy Infliction", "State Persists", "State Removed"];
        int row = 0;
        for (int index = 0; index < _messages.Length; index++)
        {
            Phase9BEditorUi.AddRow(table, row++, labels[index], _messages[index]);
            Phase9BEditorUi.AddRow(table, row++, "Preview", _messagePreviews[index]);
        }
        Button apply = new() { Text = "Apply All Four Messages as One Undo Entry", AutoSize = true };
        apply.Click += async (_, _) => await ApplyMessagesAsync().ConfigureAwait(true);
        Phase9BEditorUi.AddRow(table, row, string.Empty, apply);
        return Phase9BEditorUi.Scroll(table);
    }

    private Control BuildExtensionPage()
    {
        Panel panel = new() { Dock = DockStyle.Fill };
        Label explanation = new()
        {
            Text = "These nonstandard fields are valid project data. Typed edits do not delete, rename, copy, or normalize them.",
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8),
            ForeColor = SystemColors.GrayText
        };
        panel.Controls.Add(_extensions);
        panel.Controls.Add(explanation);
        return panel;
    }

    private async Task ApplyGeneralAsync()
    {
        if (_loading || _recordId <= 0) return;
        try
        {
            await ApplyFieldsAsync(
                $"Change state {_recordId} general settings",
                [
                    ("name", _name.Text),
                    ("iconIndex", _icon.Value),
                    ("restriction", Phase9BEditorUi.SelectedOption(_restriction)),
                    ("priority", decimal.ToInt32(_priority.Value)),
                    ("motion", Phase9BEditorUi.SelectedOption(_motion)),
                    ("overlay", Phase9BEditorUi.SelectedOption(_overlay))
                ]).ConfigureAwait(true);
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyRemovalAsync()
    {
        if (_recordId <= 0) return;
        try
        {
            MvStateRemovalSnapshot removal = new(
                _removeBattleEnd.Checked,
                _removeRestriction.Checked,
                Phase9BEditorUi.SelectedOption(_autoTiming),
                decimal.ToInt32(_minimumTurns.Value),
                decimal.ToInt32(_maximumTurns.Value),
                _removeDamage.Checked,
                decimal.ToInt32(_damageChance.Value),
                _removeWalking.Checked,
                decimal.ToInt32(_steps.Value));
            await _phase9B.SetStateRemovalAsync(_document.RelativePath, _recordId, removal).ConfigureAwait(true);
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyMessagesAsync()
    {
        if (_recordId <= 0) return;
        try { await _phase9B.SetStateMessagesAsync(_document.RelativePath, _recordId, _messages.Select(message => message.Text).ToArray()).ConfigureAwait(true); }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private async Task ApplyFieldsAsync(string description, IReadOnlyList<(string Key, object Value)> values)
    {
        DatabaseRecordHandle handle = new(MvDocumentKind.States, _document.RelativePath, _recordId);
        await using ProjectTransaction transaction = _session.Editing.BeginTransaction(description);
        foreach ((string key, object value) in values)
        {
            DatabaseFieldDescriptor field = _definition.RecordSchema.FindField(key);
            ProjectDiagnostic? diagnostic = field.ValidateObject(value);
            if (diagnostic is not null && diagnostic.Severity >= DiagnosticSeverity.Error) throw new ArgumentException(diagnostic.Message, key);
            transaction.Add(new SetJsonValueCommand(handle.RelativePath, DatabaseValueAccessor.Combine(handle.Path, field.RelativePath), field.EncodeObject(value), $"Set {field.DisplayName}"));
        }
        await transaction.CommitAsync().ConfigureAwait(true);
    }

    private void UpdateRemovalAvailability()
    {
        bool timed = Phase9BEditorUi.SelectedOption(_autoTiming) != 0;
        _minimumTurns.Enabled = timed;
        _maximumTurns.Enabled = timed;
        _damageChance.Enabled = _removeDamage.Checked;
        _steps.Enabled = _removeWalking.Checked;
    }

    private void UpdateMessagePreviews()
    {
        for (int index = 0; index < _messages.Length; index++)
        {
            string prefix = index == 1 ? "Goblin" : "Harold";
            _messagePreviews[index].Text = prefix + _messages[index].Text;
        }
    }
}

public sealed class EquipmentParameterEditor : UserControl
{
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    public EquipmentParameterEditor()
    {
        _grid.Columns.Add("Parameter", "Parameter");
        _grid.Columns.Add("Value", "Value");
        _grid.Columns[0].ReadOnly = true;
        for (int index = 0; index < 8; index++) _grid.Rows.Add(MvParameterNames.Get(index), 0);
        FlowLayoutPanel tools = new() { Dock = DockStyle.Top, Height = 42, Padding = new Padding(4), WrapContents = false };
        Button clear = new() { Text = "Zero All", AutoSize = true };
        Button copy = new() { Text = "Copy", AutoSize = true };
        Button paste = new() { Text = "Paste", AutoSize = true };
        Button add = new() { Text = "Add…", AutoSize = true };
        Button scale = new() { Text = "Scale…", AutoSize = true };
        clear.Click += (_, _) => Values = new int[8];
        copy.Click += (_, _) => Clipboard.SetText(string.Join(',', Values));
        paste.Click += (_, _) => PasteValues();
        add.Click += (_, _) => AddValues();
        scale.Click += (_, _) => ScaleValues();
        tools.Controls.AddRange([clear, copy, paste, add, scale]);
        Controls.Add(_grid);
        Controls.Add(tools);
    }

    public IReadOnlyList<int> Values
    {
        get
        {
            int[] values = new int[8];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = int.TryParse(Convert.ToString(_grid.Rows[index].Cells[1].Value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
            }
            return values;
        }
        set
        {
            for (int index = 0; index < 8; index++) _grid.Rows[index].Cells[1].Value = index < value.Count ? value[index] : 0;
        }
    }

    private void PasteValues()
    {
        try
        {
            string[] pieces = Clipboard.GetText().Split([',', '\t', ' ', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pieces.Length != 8 || pieces.Any(piece => !int.TryParse(piece, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            {
                throw new InvalidDataException("The clipboard must contain exactly eight integers.");
            }
            Values = pieces.Select(piece => int.Parse(piece, CultureInfo.InvariantCulture)).ToArray();
        }
        catch (Exception exception) { Phase9BEditorUi.ShowError(this, exception); }
    }

    private void AddValues()
    {
        decimal amount = ReadTransformValue("Add to Parameters", "Amount to add", 0);
        Values = Values.Select(value => checked(value + decimal.ToInt32(amount))).ToArray();
    }

    private void ScaleValues()
    {
        decimal percent = ReadTransformValue("Scale Parameters", "Percentage", 100);
        Values = Values.Select(value => checked((int)Math.Round(value * percent / 100m, MidpointRounding.AwayFromZero))).ToArray();
    }


    private static decimal ReadTransformValue(string title, string prompt, decimal initial)
    {
        using Form dialog = new() { Text = title, StartPosition = FormStartPosition.CenterParent, Width = 360, Height = 160, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        NumericUpDown value = new() { Minimum = -100000, Maximum = 100000, Value = Math.Clamp(initial, -100000, 100000), Dock = DockStyle.Top, Margin = new Padding(8), DecimalPlaces = title.Contains("Scale", StringComparison.OrdinalIgnoreCase) ? 2 : 0 };
        Label label = new() { Text = prompt, Dock = DockStyle.Top, Height = 28, Padding = new Padding(8, 6, 8, 0) };
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        Button okay = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([okay, cancel]);
        dialog.Controls.Add(value);
        dialog.Controls.Add(label);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = okay;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog() == DialogResult.OK ? value.Value : initial;
    }
}

internal sealed class EquipmentComparisonDialog : Form
{
    public EquipmentComparisonDialog(MutableJsonProjectDocument document, MvDocumentKind kind, int currentId, IReadOnlyList<int> currentValues)
    {
        Text = "Compare Equipment";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 520);
        ComboBox records = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        grid.Columns.Add("Parameter", "Parameter");
        grid.Columns.Add("Current", "Current");
        grid.Columns.Add("Other", "Other");
        grid.Columns.Add("Delta", "Delta");
        if (document.Syntax.Root is JsonArraySyntax root)
        {
            for (int id = 1; id < root.Elements.Count; id++)
            {
                if (id == currentId || root.Elements[id] is not JsonObjectSyntax) continue;
                records.Items.Add(new CompareItem(id, document.GetString(JsonPath.Root.Append(id).Append("name")) ?? $"{kind} {id}"));
            }
        }
        records.SelectedIndexChanged += (_, _) =>
        {
            if (records.SelectedItem is not CompareItem item) return;
            IReadOnlyList<int> other = Phase9BRecordReader.ReadEquipment(document, item.Id).Parameters;
            grid.Rows.Clear();
            for (int index = 0; index < 8; index++)
            {
                int current = index < currentValues.Count ? currentValues[index] : 0;
                int compared = index < other.Count ? other[index] : 0;
                grid.Rows.Add(MvParameterNames.Get(index), current, compared, compared - current);
            }
        };
        Controls.Add(grid);
        Controls.Add(records);
        if (records.Items.Count > 0) records.SelectedIndex = 0;
    }

    private sealed record CompareItem(int Id, string Name)
    {
        public override string ToString() => $"{Id:D4} {Name}";
    }
}

internal static class Phase9BEditorUi
{
    public static Label SelectionLabel() => new()
    {
        Dock = DockStyle.Top,
        Height = 34,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8),
        ForeColor = SystemColors.GrayText
    };

    public static NumericUpDown Number(int minimum, int maximum) => new()
    {
        Dock = DockStyle.Fill,
        Minimum = minimum,
        Maximum = maximum,
        ThousandsSeparator = true
    };

    public static TableLayoutPanel Table()
    {
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(12) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    public static Control Scroll(Control child)
    {
        Panel panel = new() { Dock = DockStyle.Fill, AutoScroll = true };
        child.Dock = DockStyle.Top;
        child.AutoSize = true;
        panel.Controls.Add(child);
        return panel;
    }

    public static void AddRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label text = new() { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) };
        control.Margin = new Padding(3, 3, 3, 8);
        table.Controls.Add(text, 0, row);
        table.Controls.Add(control, 1, row);
    }

    public static ComboBox OptionCombo(IReadOnlyDictionary<int, string> options)
    {
        ComboBox combo = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach ((int id, string name) in options.OrderBy(pair => pair.Key)) combo.Items.Add(new OptionValue(id, name));
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        return combo;
    }

    public static int SelectedOption(ComboBox combo) => (combo.SelectedItem as OptionValue)?.Id ?? 0;
    public static string SelectedOptionName(ComboBox combo) => (combo.SelectedItem as OptionValue)?.Name ?? "Unknown";

    public static void SelectOption(ComboBox combo, int id)
    {
        int index = combo.Items.Cast<OptionValue>().ToList().FindIndex(value => value.Id == id);
        if (index < 0)
        {
            combo.Items.Add(new OptionValue(id, $"Unknown #{id}"));
            index = combo.Items.Count - 1;
        }
        combo.SelectedIndex = index;
    }

    public static void Set(NumericUpDown control, int value) => control.Value = Math.Clamp(value, decimal.ToInt32(control.Minimum), decimal.ToInt32(control.Maximum));

    public static void ShowError(IWin32Window owner, Exception exception) =>
        MessageBox.Show(owner, exception.Message, "Database editor", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private sealed record OptionValue(int Id, string Name)
    {
        public override string ToString() => Name;
    }
}
