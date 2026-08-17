using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Playroma.Sound.Authoring.Diagnostics;
using Playroma.Sound.Authoring.Documents;
using Playroma.Sound.Generators.Contracts;
using Playroma.Sound.Generators.Presets;

namespace Playroma.Sound.WinForms.Controls;

public sealed class QuickSoundRequestedEventArgs : EventArgs
{
    public QuickSoundRequestedEventArgs(string generatorId, JsonObject description, SoundQualityMode qualityMode)
    {
        GeneratorId = generatorId;
        Description = description;
        QualityMode = qualityMode;
    }

    public string GeneratorId { get; }
    public JsonObject Description { get; }
    public SoundQualityMode QualityMode { get; }
}

/// <summary>
/// A fast, non-technical authoring surface for generator requests. The intended loop is:
/// choose a family, audition variations, keep the good one, and immediately continue.
/// </summary>
public sealed class GeneratorRequestEditorControl : UserControl, IAuthoringEditorControl
{
    private static readonly JsonSerializerOptions DescriptionJsonOptions = CreateDescriptionJsonOptions();

    private readonly GeneratorRequestDocument _document;
    private readonly SoundGeneratorCatalog _catalog = SoundGeneratorCatalog.CreateDefault();
    private readonly Dictionary<string, RadioButton> _generatorButtons = new(StringComparer.Ordinal);
    private readonly TableLayoutPanel _parameterTable;
    private readonly TextBox _nameBox;
    private readonly ComboBox _presetBox;
    private readonly ComboBox _qualityBox;
    private readonly CheckBox _autoPlayBox;
    private readonly Label _familyDescription;
    private readonly Label _variationLabel;
    private readonly Label _stateLabel;
    private readonly Label _diagnosticLabel;
    private readonly Button _playButton;
    private readonly Button _rejectButton;
    private readonly Button _keepButton;
    private readonly FlowLayoutPanel _quickTweaksPanel;
    private readonly System.Windows.Forms.Timer _previewDebounce;

    private Dictionary<string, GeneratorParameterDescriptor> _currentParameterDescriptors = new(StringComparer.OrdinalIgnoreCase);

    private bool _refreshing;
    private bool _internalMutation;

    public GeneratorRequestEditorControl(GeneratorRequestDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(12, 9, 12, 8),
            BackColor = SystemColors.ControlLight
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "SOUND LAB",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
            Padding = new Padding(0, 5, 12, 0)
        };
        _nameBox = new TextBox { Dock = DockStyle.Fill, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) };
        _qualityBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 132 };
        _qualityBox.Items.AddRange(Enum.GetValues<SoundQualityMode>().Cast<object>().ToArray());
        _autoPlayBox = new CheckBox { Text = "Auto-play changes", AutoSize = true, Checked = true, Padding = new Padding(8, 4, 0, 0) };
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_nameBox, 1, 0);
        header.Controls.Add(_qualityBox, 2, 0);
        header.Controls.Add(_autoPlayBox, 3, 0);

        var bodySplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Size = new Size(1100, 650),
            Orientation = Orientation.Vertical,
            SplitterDistance = 245,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 215,
            Panel2MinSize = 380
        };

        var familyPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        familyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        familyPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        familyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        familyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        familyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        familyPanel.Controls.Add(SectionLabel("1. WHAT KIND OF SOUND?"), 0, 0);

        var familyButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 7, 0, 4)
        };
        foreach (IUntypedSoundGenerator generator in _catalog.Generators.OrderBy(item => item.Descriptor.Category))
        {
            var button = new RadioButton
            {
                Appearance = Appearance.Button,
                Text = FamilyButtonText(generator.Descriptor.Category),
                TextAlign = ContentAlignment.MiddleCenter,
                Width = 101,
                Height = 48,
                Margin = new Padding(3),
                Tag = generator.GeneratorId,
                FlatStyle = FlatStyle.System
            };
            button.CheckedChanged += (_, _) =>
            {
                if (!_refreshing && button.Checked && button.Tag is string generatorId)
                    ChangeGenerator(generatorId);
            };
            _generatorButtons.Add(generator.GeneratorId, button);
            familyButtons.Controls.Add(button);
        }
        familyPanel.Controls.Add(familyButtons, 0, 1);

        familyPanel.Controls.Add(SectionLabel("STARTING POINT"), 0, 2);
        _presetBox = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(3, 5, 3, 8) };
        _presetBox.Items.Add(new PresetItem(null, "Default for this family"));
        foreach (IGeneratorPreset preset in BuiltInPresetCatalog.All.OrderBy(item => item.DisplayName))
            _presetBox.Items.Add(new PresetItem(preset, preset.DisplayName));
        _presetBox.SelectedIndex = 0;
        familyPanel.Controls.Add(_presetBox, 0, 3);

        var surprise = new Button { Text = "SURPRISE ME", Dock = DockStyle.Top, Height = 35, Margin = new Padding(3, 3, 3, 2) };
        surprise.Click += (_, _) => SurpriseMe();
        familyPanel.Controls.Add(surprise, 0, 4);

        bodySplit.Panel1.Controls.Add(familyPanel);

        var parameterHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12, 10, 12, 8)
        };
        parameterHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parameterHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        parameterHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parameterHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var parameterHeader = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        parameterHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        parameterHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _familyDescription = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(900, 0),
            Padding = new Padding(0, 2, 8, 8)
        };
        var advancedButton = new Button { Text = "Advanced JSON…", AutoSize = true };
        advancedButton.Click += (_, _) => OpenAdvancedJsonEditor();
        parameterHeader.Controls.Add(_familyDescription, 0, 0);
        parameterHeader.Controls.Add(advancedButton, 1, 0);
        parameterHost.Controls.Add(parameterHeader, 0, 0);

        var tweaksHost = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 6) };
        tweaksHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tweaksHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tweaksHost.Controls.Add(SectionLabel("QUICK TWEAKS"), 0, 0);
        _quickTweaksPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 3, 0, 0),
            Margin = Padding.Empty
        };
        AddQuickTweakButton("Shorter", () => ApplyQuickTweak(QuickTweak.Shorter));
        AddQuickTweakButton("Longer", () => ApplyQuickTweak(QuickTweak.Longer));
        AddQuickTweakButton("Brighter", () => ApplyQuickTweak(QuickTweak.Brighter));
        AddQuickTweakButton("Darker", () => ApplyQuickTweak(QuickTweak.Darker));
        AddQuickTweakButton("Softer", () => ApplyQuickTweak(QuickTweak.Softer));
        AddQuickTweakButton("Harder", () => ApplyQuickTweak(QuickTweak.Harder));
        AddQuickTweakButton("More Tonal", () => ApplyQuickTweak(QuickTweak.MoreTonal));
        AddQuickTweakButton("More Noisy", () => ApplyQuickTweak(QuickTweak.MoreNoisy));
        AddQuickTweakButton("More Punch", () => ApplyQuickTweak(QuickTweak.MorePunch));
        AddQuickTweakButton("Less Punch", () => ApplyQuickTweak(QuickTweak.LessPunch));
        AddQuickTweakButton("New Phrase", RegeneratePhrase);
        AddQuickTweakButton("New Voice", RegenerateVoice);
        AddQuickTweakButton("Mutate Phrase", TryAnother);
        AddQuickTweakButton("More Musical", () => ApplyExpressiveBias(true));
        AddQuickTweakButton("More Creature-like", () => ApplyExpressiveBias(false));
        AddQuickTweakButton("Add Syllable / Note", () => AdjustPhraseCount(1));
        AddQuickTweakButton("Remove Syllable / Note", () => AdjustPhraseCount(-1));
        AddQuickTweakButton("Big Finish", ApplyBigFinish);
        AddQuickTweakButton("Randomize Parameters", RandomizeCurrentDescription);
        tweaksHost.Controls.Add(_quickTweaksPanel, 0, 1);
        parameterHost.Controls.Add(tweaksHost, 0, 1);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
        _parameterTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(8)
        };
        _parameterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        _parameterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(_parameterTable);
        parameterHost.Controls.Add(scroll, 0, 2);

        var statePanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 6, 0, 0) };
        statePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _diagnosticLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
        _variationLabel = new Label { AutoSize = true, TextAlign = ContentAlignment.MiddleRight, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) };
        _stateLabel = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = SystemColors.GrayText };
        statePanel.Controls.Add(_diagnosticLabel, 0, 0);
        statePanel.Controls.Add(_variationLabel, 1, 0);
        statePanel.Controls.Add(_stateLabel, 0, 1);
        statePanel.SetColumnSpan(_stateLabel, 2);
        parameterHost.Controls.Add(statePanel, 0, 3);
        bodySplit.Panel2.Controls.Add(parameterHost);

        var decisions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(12, 9, 12, 10),
            BackColor = SystemColors.ControlDark
        };
        decisions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23));
        decisions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        decisions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 41));
        decisions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        decisions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var question = new Label
        {
            Text = "LISTEN TO IT. IS THIS SOUND GOOD?",
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
            ForeColor = SystemColors.ControlText,
            Padding = new Padding(2, 0, 0, 5)
        };
        decisions.Controls.Add(question, 0, 0);
        decisions.SetColumnSpan(question, 3);

        _playButton = DecisionButton("▶  PLAY AGAIN", SystemColors.ControlLight);
        _rejectButton = DecisionButton("NO — TRY ANOTHER", Color.MistyRose);
        _keepButton = DecisionButton("YES — KEEP + NEXT", Color.Honeydew);
        decisions.Controls.Add(_playButton, 0, 1);
        decisions.Controls.Add(_rejectButton, 1, 1);
        decisions.Controls.Add(_keepButton, 2, 1);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(bodySplit, 0, 1);
        root.Controls.Add(decisions, 0, 2);
        Controls.Add(root);

        _previewDebounce = new System.Windows.Forms.Timer { Interval = 325 };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            RequestPreview();
        };

        _nameBox.Validated += (_, _) => ApplyDisplayName();
        _nameBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                ApplyDisplayName();
                eventArgs.SuppressKeyPress = true;
            }
        };
        _qualityBox.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshing || _qualityBox.SelectedItem is not SoundQualityMode quality) return;
            Mutate("Change quality", model => model.QualityMode = quality, "generator-quality");
            SchedulePreview();
        };
        _presetBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_refreshing && _presetBox.SelectedItem is PresetItem { Preset: not null } item)
                ApplyPreset(item.Preset);
        };
        _playButton.Click += (_, _) => RequestPreview();
        _rejectButton.Click += (_, _) => TryAnother();
        _keepButton.Click += (_, _) => KeepAndContinue();

        _document.Changed += OnDocumentChanged;
        RefreshFromDocument(true);
    }

    public event EventHandler<DocumentActionEventArgs>? ValidateRequested;
    public event EventHandler<DocumentActionEventArgs>? CompileRequested;
    public event EventHandler<DocumentActionEventArgs>? PlayRequested;
    public event EventHandler? EditorStateChanged;
    public event EventHandler<QuickSoundRequestedEventArgs>? NewSoundRequested;

    public IAuthoringDocument Document => _document;
    public bool HasUnappliedChanges => false;

    public bool TryApplyEdits(bool showError = true)
    {
        ApplyDisplayName();
        return true;
    }

    public void RefreshFromDocument(bool force)
    {
        if (!force && _refreshing) return;
        EditableGeneratorRequest request = _document.CreateEditableCopy();
        _refreshing = true;
        try
        {
            _nameBox.Text = request.DisplayName;
            _qualityBox.SelectedItem = request.QualityMode;
            if (_generatorButtons.TryGetValue(request.GeneratorId, out RadioButton? selected)) selected.Checked = true;
            IUntypedSoundGenerator generator = ResolveGenerator(request.GeneratorId);
            _familyDescription.Text = $"2. SHAPE THE {generator.Descriptor.DisplayName.ToUpperInvariant()}\r\n{generator.Descriptor.Description}";
            BuildParameterControls(generator, request);
            _variationLabel.Text = $"Variation {request.VariationIndex + 1}";
            _presetBox.SelectedIndex = 0;
            UpdateStateLabels();
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void SetValidation(ValidationSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        int errors = summary.Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Error);
        int warnings = summary.Diagnostics.Count(item => item.Severity == AuthoringDiagnosticSeverity.Warning);
        _diagnosticLabel.Text = errors == 0 && warnings == 0 ? "Ready to audition" : $"{errors} error(s), {warnings} warning(s)";
        _diagnosticLabel.ForeColor = errors > 0 ? Color.Firebrick : warnings > 0 ? Color.DarkGoldenrod : Color.DarkGreen;
    }

    public void SetCompilationState(AuthoringCompilationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _diagnosticLabel.Text = result.Succeeded
            ? $"Playing — generated in {result.Duration.TotalMilliseconds:0} ms"
            : $"Could not generate: {result.State}";
        _diagnosticLabel.ForeColor = result.Succeeded ? Color.DarkGreen : Color.Firebrick;
    }

    public void SelectDiagnostic(AuthoringDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        _diagnosticLabel.Text = $"{diagnostic.Code}: {diagnostic.Message}";
        _diagnosticLabel.ForeColor = diagnostic.Severity == AuthoringDiagnosticSeverity.Error ? Color.Firebrick : Color.DarkGoldenrod;
    }

    public void FormatJson() => OpenAdvancedJsonEditor();

    /// <summary>Requests validation through the hosting authoring shell.</summary>
    public void RequestValidation() =>
        ValidateRequested?.Invoke(this, new DocumentActionEventArgs(_document));

    /// <summary>Requests compilation through the hosting authoring shell.</summary>
    public void RequestCompilation() =>
        CompileRequested?.Invoke(this, new DocumentActionEventArgs(_document));

    public void RequestPreview() => PlayRequested?.Invoke(this, new DocumentActionEventArgs(_document));

    public void TryAnother()
    {
        Mutate("Try another variation", model =>
        {
            model.VariationIndex = model.VariationIndex == int.MaxValue ? 0 : model.VariationIndex + 1;
            if (model.VariationIndex == 0)
            {
                model.RootSeed = NewSeed();
                model.FamilySeed = NewSeed();
            }
        });
        RequestPreview();
    }

    public void SurpriseMe()
    {
        IUntypedSoundGenerator generator = _catalog.Generators.ElementAt(Random.Shared.Next(_catalog.Generators.Count));
        JsonObject description = CreateRandomDescription(generator);
        Mutate("Surprise me", model =>
        {
            model.GeneratorId = generator.GeneratorId;
            model.Description = description;
            model.RootSeed = NewSeed();
            model.FamilySeed = NewSeed();
            model.VariationIndex = 0;
            if (IsGenericWorkingName(model.DisplayName))
                model.DisplayName = $"New {generator.Descriptor.DisplayName}";
        });
        RefreshFromDocument(true);
        RequestPreview();
    }

    public void KeepAndContinue()
    {
        ApplyDisplayName();
        try
        {
            _document.Save();
            EditableGeneratorRequest request = _document.CreateEditableCopy();
            _diagnosticLabel.Text = $"Kept '{request.DisplayName}'. Making the next sound…";
            _diagnosticLabel.ForeColor = Color.DarkGreen;
            EditorStateChanged?.Invoke(this, EventArgs.Empty);
            NewSoundRequested?.Invoke(this, new QuickSoundRequestedEventArgs(
                request.GeneratorId,
                request.Description.DeepClone() as JsonObject ?? new JsonObject(),
                request.QualityMode));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            MessageBox.Show(this, exception.Message, "Could not keep sound", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChangeGenerator(string generatorId)
    {
        IUntypedSoundGenerator generator = ResolveGenerator(generatorId);
        JsonObject defaults = CreateDefaultDescription(generator);
        Mutate("Change sound family", model =>
        {
            model.GeneratorId = generatorId;
            model.Description = defaults;
            model.RootSeed = NewSeed();
            model.FamilySeed = NewSeed();
            model.VariationIndex = 0;
            if (IsGenericWorkingName(model.DisplayName)) model.DisplayName = $"New {generator.Descriptor.DisplayName}";
        });
        RefreshFromDocument(true);
        SchedulePreview();
    }

    private void ApplyPreset(IGeneratorPreset preset)
    {
        IUntypedSoundGenerator generator = ResolveGenerator(preset.GeneratorId);
        object? presetDescription = preset.GetType().GetProperty("Description", BindingFlags.Instance | BindingFlags.Public)?.GetValue(preset);
        JsonObject description = presetDescription is null
            ? CreateDefaultDescription(generator)
            : JsonSerializer.SerializeToNode(presetDescription, preset.DescriptionType, DescriptionJsonOptions) as JsonObject ?? CreateDefaultDescription(generator);
        Mutate("Apply generator preset", model =>
        {
            model.GeneratorId = preset.GeneratorId;
            model.Description = description;
            model.RootSeed = NewSeed();
            model.FamilySeed = NewSeed();
            model.VariationIndex = 0;
            if (IsGenericWorkingName(model.DisplayName)) model.DisplayName = preset.DisplayName;
        });
        RefreshFromDocument(true);
        SchedulePreview();
    }

    private void BuildParameterControls(IUntypedSoundGenerator generator, EditableGeneratorRequest request)
    {
        _parameterTable.SuspendLayout();
        try
        {
            while (_parameterTable.Controls.Count > 0)
            {
                Control control = _parameterTable.Controls[0];
                _parameterTable.Controls.RemoveAt(0);
                control.Dispose();
            }
            _parameterTable.RowStyles.Clear();
            _parameterTable.RowCount = 0;

            object description = DeserializeDescription(generator, request.Description);
            var descriptors = generator.Descriptor.Parameters.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            _currentParameterDescriptors = descriptors;
            foreach (PropertyInfo property in generator.DescriptionType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                string propertyId = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                object? value = property.GetValue(description);
                GeneratorParameterDescriptor? descriptor = descriptors.GetValueOrDefault(propertyId);
                AddParameterRow(property, propertyId, value, descriptor);
            }
        }
        finally
        {
            _parameterTable.ResumeLayout(true);
        }
    }

    private void AddParameterRow(PropertyInfo property, string propertyId, object? value, GeneratorParameterDescriptor? descriptor)
    {
        int row = _parameterTable.RowCount++;
        _parameterTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        string labelText = descriptor?.DisplayName ?? Humanize(property.Name);
        if (!string.IsNullOrWhiteSpace(descriptor?.Unit)) labelText += $" ({descriptor.Unit})";
        var label = new Label
        {
            Text = labelText,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(3, 9, 8, 5),
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold)
        };
        Control editor = CreatePropertyEditor(property, propertyId, value, descriptor);
        _parameterTable.Controls.Add(label, 0, row);
        _parameterTable.Controls.Add(editor, 1, row);
    }

    private Control CreatePropertyEditor(PropertyInfo property, string propertyId, object? value, GeneratorParameterDescriptor? descriptor)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type.IsEnum)
        {
            var combo = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(3, 5, 3, 5) };
            combo.Items.AddRange(Enum.GetValues(type).Cast<object>().ToArray());
            combo.SelectedItem = value ?? Enum.GetValues(type).GetValue(0);
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (!_refreshing && combo.SelectedItem is object selected)
                {
                    ApplyDescriptionProperty(propertyId, selected, property.PropertyType, $"description:{propertyId}");
                    SchedulePreview();
                }
            };
            return combo;
        }

        if (type == typeof(bool))
        {
            var check = new CheckBox { Text = "Enabled", AutoSize = true, Checked = value is true, Padding = new Padding(3, 6, 0, 5) };
            check.CheckedChanged += (_, _) =>
            {
                if (!_refreshing)
                {
                    ApplyDescriptionProperty(propertyId, check.Checked, property.PropertyType, $"description:{propertyId}");
                    SchedulePreview();
                }
            };
            return check;
        }

        if (IsNumeric(type))
        {
            double minimum = descriptor?.Minimum ?? 0;
            double maximum = descriptor?.Maximum ?? 1;
            if (maximum <= minimum) maximum = minimum + 1;
            double current = Convert.ToDouble(value ?? descriptor?.DefaultValue ?? minimum, System.Globalization.CultureInfo.InvariantCulture);
            current = Math.Clamp(current, minimum, maximum);

            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            {
                var numeric = new NumericUpDown
                {
                    Dock = DockStyle.Top,
                    Minimum = (decimal)minimum,
                    Maximum = (decimal)maximum,
                    Value = (decimal)Math.Round(current),
                    DecimalPlaces = 0,
                    Margin = new Padding(3, 5, 3, 5)
                };
                numeric.ValueChanged += (_, _) =>
                {
                    if (_refreshing) return;
                    object converted = Convert.ChangeType(decimal.ToInt64(numeric.Value), type, System.Globalization.CultureInfo.InvariantCulture);
                    ApplyDescriptionProperty(propertyId, converted, property.PropertyType, $"description:{propertyId}");
                    SchedulePreview();
                };
                return numeric;
            }

            var host = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
            var slider = new TrackBar
            {
                Dock = DockStyle.Top,
                Minimum = 0,
                Maximum = 1000,
                TickStyle = TickStyle.None,
                Value = (int)Math.Round(((current - minimum) / (maximum - minimum)) * 1000),
                Height = 35,
                Margin = new Padding(0, 2, 3, 0)
            };
            var valueLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Text = FormatNumber(current),
                Padding = new Padding(0, 0, 3, 4)
            };
            slider.ValueChanged += (_, _) =>
            {
                double result = minimum + ((maximum - minimum) * slider.Value / 1000d);
                valueLabel.Text = FormatNumber(result);
                if (_refreshing) return;
                object converted = type == typeof(float) ? (object)(float)result : result;
                ApplyDescriptionProperty(propertyId, converted, property.PropertyType, $"description:{propertyId}");
                SchedulePreview();
            };
            host.Controls.Add(slider, 0, 0);
            host.Controls.Add(valueLabel, 1, 0);
            return host;
        }

        var text = new TextBox { Dock = DockStyle.Top, Text = value?.ToString() ?? "", Margin = new Padding(3, 5, 3, 5) };
        text.Validated += (_, _) =>
        {
            if (!_refreshing) ApplyDescriptionProperty(propertyId, text.Text, property.PropertyType, $"description:{propertyId}");
        };
        return text;
    }

    private void AddQuickTweakButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 29,
            Margin = new Padding(0, 0, 6, 6)
        };
        button.Click += (_, _) => action();
        _quickTweaksPanel.Controls.Add(button);
    }

    private void RegeneratePhrase()
    {
        Mutate("Create a new phrase", model =>
        {
            model.FamilySeed = NewSeed();
            model.VariationIndex = 0;
        });
        RefreshFromDocument(true);
        RequestPreview();
    }

    private void RegenerateVoice()
    {
        EditableGeneratorRequest request = _document.CreateEditableCopy();
        IUntypedSoundGenerator generator = ResolveGenerator(request.GeneratorId);
        JsonObject randomized = CreateRandomDescription(generator);
        JsonObject updated = request.Description.DeepClone() as JsonObject ?? new JsonObject();
        string[] voiceProperties =
        [
            "style", "archetype", "contour", "organicSynthetic", "smoothRaspy", "retroModern", "acousticSynthetic",
            "brightness", "organicTechnology", "stability", "technology", "throatLength", "pulseBlend", "noise", "roughness"
        ];
        bool changed = false;
        foreach (string property in voiceProperties)
        {
            if (!randomized.TryGetPropertyValue(property, out JsonNode? value) || value is null || !updated.ContainsKey(property)) continue;
            updated[property] = value.DeepClone();
            changed = true;
        }
        if (!changed)
        {
            _stateLabel.Text = "This family has no separate voice controls; trying another variation instead";
            TryAnother();
            return;
        }

        Mutate("Create a new voice", model =>
        {
            model.Description = updated;
            model.RootSeed = NewSeed();
            model.VariationIndex = 0;
        });
        RefreshFromDocument(true);
        RequestPreview();
    }

    private void ApplyExpressiveBias(bool moreMusical)
    {
        EditableGeneratorRequest request = _document.CreateEditableCopy();
        JsonObject updated = request.Description.DeepClone() as JsonObject ?? new JsonObject();
        bool changed = false;
        changed |= MoveProperty(updated, "musicalAnimal", moreMusical ? .14 : -.14);
        changed |= MoveProperty(updated, "musicality", moreMusical ? .14 : -.14);
        changed |= MoveProperty(updated, "tonalDissonant", moreMusical ? -.14 : .14);
        changed |= MoveProperty(updated, "pitchMovement", moreMusical ? -.04 : .12);
        changed |= MoveProperty(updated, "smoothRaspy", moreMusical ? -.08 : .12);
        if (!changed)
        {
            _stateLabel.Text = $"No expressive musicality control exists for this family";
            return;
        }

        Mutate(moreMusical ? "Make phrase more musical" : "Make phrase more creature-like", model =>
        {
            model.Description = updated;
            model.VariationIndex = 0;
        });
        RefreshFromDocument(true);
        SchedulePreview();
    }

    private void AdjustPhraseCount(int delta)
    {
        EditableGeneratorRequest request = _document.CreateEditableCopy();
        JsonObject updated = request.Description.DeepClone() as JsonObject ?? new JsonObject();
        string[] countProperties = ["syllables", "notes", "motifNotes", "noteCount", "steps", "events", "secondaryEvents"];
        string? matched = countProperties.FirstOrDefault(updated.ContainsKey);
        if (matched is null || !updated.TryGetPropertyValue(matched, out JsonNode? node) || !TryGetJsonNumber(node, out double current))
        {
            _stateLabel.Text = "This sound family does not use syllables or phrase notes";
            return;
        }

        GeneratorParameterDescriptor? descriptor = _currentParameterDescriptors.GetValueOrDefault(matched);
        double minimum = descriptor?.Minimum ?? 1;
        double maximum = descriptor?.Maximum ?? 32;
        double next = Math.Clamp(Math.Round(current) + delta, minimum, maximum);
        updated[matched] = (int)next;
        Mutate(delta > 0 ? "Add phrase event" : "Remove phrase event", model =>
        {
            model.Description = updated;
            model.VariationIndex = 0;
        });
        RefreshFromDocument(true);
        SchedulePreview();
    }

    private void ApplyBigFinish()
    {
        EditableGeneratorRequest request = _document.CreateEditableCopy();
        JsonObject updated = request.Description.DeepClone() as JsonObject ?? new JsonObject();
        bool changed = false;
        foreach (string property in new[] { "impact", "impactSize", "afterglow", "tail", "power", "spatialExtent", "pitchMovement" })
            changed |= MoveProperty(updated, property, property is "impact" or "impactSize" or "power" ? .16 : .12);
        if (!changed)
        {
            _stateLabel.Text = "This family has no finish or tail controls";
            return;
        }

        Mutate("Add a big finish", model =>
        {
            model.Description = updated;
            model.VariationIndex = 0;
        });
        RefreshFromDocument(true);
        SchedulePreview();
    }

    private bool MoveProperty(JsonObject description, string propertyId, double normalizedDelta)
    {
        if (!description.TryGetPropertyValue(propertyId, out JsonNode? node) || node is null || !TryGetJsonNumber(node, out double current))
            return false;
        GeneratorParameterDescriptor? descriptor = _currentParameterDescriptors.GetValueOrDefault(propertyId);
        double minimum = descriptor?.Minimum ?? 0;
        double maximum = descriptor?.Maximum ?? 1;
        if (maximum <= minimum) maximum = minimum + 1;
        double updated = MoveWithinRange(current, minimum, maximum, normalizedDelta);
        object numericValue = ConvertNumberToNodeType(updated, node);
        description[propertyId] = JsonSerializer.SerializeToNode(numericValue, numericValue.GetType(), DescriptionJsonOptions);
        return true;
    }

    private void RandomizeCurrentDescription()
    {
        IUntypedSoundGenerator generator = ResolveGenerator(_document.CreateEditableCopy().GeneratorId);
        JsonObject description = CreateRandomDescription(generator);
        Mutate("Randomize current parameters", model =>
        {
            model.Description = description;
            model.RootSeed = NewSeed();
            model.FamilySeed = NewSeed();
            model.VariationIndex = 0;
        });
        RefreshFromDocument(true);
        SchedulePreview();
    }

    private void ApplyQuickTweak(QuickTweak tweak)
    {
        EditableGeneratorRequest request = _document.CreateEditableCopy();
        JsonObject description = request.Description.DeepClone() as JsonObject ?? new JsonObject();
        if (!AdjustDescriptionForTweak(description, tweak))
        {
            _stateLabel.Text = $"No obvious '{QuickTweakText(tweak)}' controls for this sound family";
            return;
        }

        Mutate($"Quick tweak: {QuickTweakText(tweak)}", model =>
        {
            model.Description = description;
            model.VariationIndex = 0;
        });
        RefreshFromDocument(true);
        SchedulePreview();
    }

    private bool AdjustDescriptionForTweak(JsonObject description, QuickTweak tweak)
    {
        bool changed = false;
        foreach ((string propertyId, GeneratorParameterDescriptor descriptor) in _currentParameterDescriptors)
        {
            if (!description.TryGetPropertyValue(propertyId, out JsonNode? node) || node is null) continue;
            if (!TryGetJsonNumber(node, out double current)) continue;
            if (!QuickTweakMatches(propertyId, tweak)) continue;

            double minimum = descriptor.Minimum;
            double maximum = descriptor.Maximum;
            if (maximum <= minimum)
            {
                minimum = 0;
                maximum = Math.Max(1, current);
            }

            bool tonalBalance = string.Equals(propertyId, "tonalNoise", StringComparison.OrdinalIgnoreCase);
            double updated = tweak switch
            {
                QuickTweak.MoreTonal when tonalBalance => MoveWithinRange(current, minimum, maximum, 0.12),
                QuickTweak.MoreNoisy when tonalBalance => MoveWithinRange(current, minimum, maximum, -0.12),
                QuickTweak.Shorter or QuickTweak.Darker or QuickTweak.Softer or QuickTweak.MoreTonal or QuickTweak.LessPunch => MoveWithinRange(current, minimum, maximum, -0.12),
                QuickTweak.Longer or QuickTweak.Brighter or QuickTweak.Harder or QuickTweak.MoreNoisy or QuickTweak.MorePunch => MoveWithinRange(current, minimum, maximum, 0.12),
                _ => current
            };

            if (Math.Abs(updated - current) < 0.0001) continue;
            object numericValue = ConvertNumberToNodeType(updated, node);
            description[propertyId] = JsonSerializer.SerializeToNode(numericValue, numericValue.GetType(), DescriptionJsonOptions);
            changed = true;
        }

        return changed;
    }

    private static bool TryGetJsonNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue jsonValue) return false;
        return jsonValue.TryGetValue(out value)
            || jsonValue.TryGetValue(out int intValue) && (value = intValue) == intValue
            || jsonValue.TryGetValue(out float floatValue) && (value = floatValue) == floatValue
            || jsonValue.TryGetValue(out decimal decimalValue) && (value = (double)decimalValue) == (double)decimalValue;
    }

    private static object ConvertNumberToNodeType(double value, JsonNode node)
    {
        if (node is not JsonValue jsonValue) return value;
        if (jsonValue.TryGetValue(out int _)) return (int)Math.Round(value);
        if (jsonValue.TryGetValue(out long _)) return (long)Math.Round(value);
        if (jsonValue.TryGetValue(out short _)) return (short)Math.Round(value);
        if (jsonValue.TryGetValue(out byte _)) return (byte)Math.Clamp(Math.Round(value), byte.MinValue, byte.MaxValue);
        if (jsonValue.TryGetValue(out float _)) return (float)value;
        if (jsonValue.TryGetValue(out decimal _)) return (decimal)value;
        return value;
    }

    private static double MoveWithinRange(double value, double minimum, double maximum, double delta)
    {
        double range = maximum - minimum;
        if (range <= 0.0001) return value;
        return Math.Clamp(value + (range * delta), minimum, maximum);
    }

    private static bool QuickTweakMatches(string propertyId, QuickTweak tweak)
    {
        string id = propertyId.ToLowerInvariant();
        return tweak switch
        {
            QuickTweak.Shorter or QuickTweak.Longer => ContainsAny(id, "duration", "length", "decay", "release", "hold", "time"),
            QuickTweak.Brighter or QuickTweak.Darker => ContainsAny(id, "pitch", "frequency", "tone", "brightness", "cutoff", "treble"),
            QuickTweak.Softer or QuickTweak.Harder => ContainsAny(id, "attack", "sharp", "transient", "click", "edge", "hard"),
            QuickTweak.MoreTonal or QuickTweak.MoreNoisy => ContainsAny(id, "noise", "hiss", "static", "rough", "grit", "distortion", "tone", "harmonic"),
            QuickTweak.MorePunch or QuickTweak.LessPunch => ContainsAny(id, "impact", "power", "punch", "thump", "gain", "amount", "mix", "strength", "intensity", "force", "velocity"),
            _ => false
        };
    }

    private static bool ContainsAny(string value, params string[] tokens) => tokens.Any(value.Contains);

    private static string QuickTweakText(QuickTweak tweak) => tweak switch
    {
        QuickTweak.Shorter => "Shorter",
        QuickTweak.Longer => "Longer",
        QuickTweak.Brighter => "Brighter",
        QuickTweak.Darker => "Darker",
        QuickTweak.Softer => "Softer",
        QuickTweak.Harder => "Harder",
        QuickTweak.MoreTonal => "More Tonal",
        QuickTweak.MoreNoisy => "More Noisy",
        QuickTweak.MorePunch => "More Punch",
        QuickTweak.LessPunch => "Less Punch",
        _ => tweak.ToString()
    };

    private void ApplyDescriptionProperty(string propertyId, object value, Type declaredType, string mergeKey)
    {
        JsonNode? node = JsonSerializer.SerializeToNode(value, declaredType, DescriptionJsonOptions);
        Mutate($"Change {Humanize(propertyId)}", model => model.Description[propertyId] = node?.DeepClone(), mergeKey);
    }

    private void ApplyDisplayName()
    {
        if (_refreshing) return;
        string value = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            _nameBox.Text = _document.DisplayName;
            return;
        }
        Mutate("Rename sound", model => model.DisplayName = value, "generator-display-name");
    }

    private void OpenAdvancedJsonEditor()
    {
        using var dialog = new Form
        {
            Text = $"Advanced JSON — {_document.DisplayName}",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = true,
            ClientSize = new Size(780, 620)
        };
        var editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 10f),
            Text = _document.SerializeContent()
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        var apply = new Button { Text = "Apply", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        dialog.Controls.Add(editor);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _document.ReplaceSerializedContent(editor.Text, "Edit advanced generator JSON");
            RefreshFromDocument(true);
            SchedulePreview();
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, "Invalid generator JSON", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Mutate(string description, Action<EditableGeneratorRequest> mutation, string mergeKey = "")
    {
        _internalMutation = true;
        try
        {
            _document.Mutate(description, mutation, mergeKey);
        }
        finally
        {
            _internalMutation = false;
        }
        UpdateStateLabels();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SchedulePreview()
    {
        if (!_autoPlayBox.Checked || _refreshing) return;
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void OnDocumentChanged(object? sender, AuthoringDocumentChangedEventArgs eventArgs)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => OnDocumentChanged(sender, eventArgs)));
            return;
        }
        if (!_internalMutation) RefreshFromDocument(true);
        else UpdateStateLabels();
        EditorStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateStateLabels()
    {
        EditableGeneratorRequest request = _document.CreateEditableCopy();
        _variationLabel.Text = $"Variation {request.VariationIndex + 1}";
        _stateLabel.Text = _document.IsDirty
            ? "Working sound — press YES to keep it"
            : $"Saved in project: {_document.FilePath}";
        _keepButton.Enabled = true;
        _rejectButton.Enabled = true;
        _playButton.Enabled = true;
    }

    private IUntypedSoundGenerator ResolveGenerator(string generatorId)
    {
        try { return _catalog.GetRequired(generatorId); }
        catch (KeyNotFoundException) { return _catalog.Generators.First(); }
    }

    private static object DeserializeDescription(IUntypedSoundGenerator generator, JsonObject description)
    {
        try
        {
            return description.Deserialize(generator.DescriptionType, DescriptionJsonOptions)
                ?? Activator.CreateInstance(generator.DescriptionType)
                ?? throw new InvalidOperationException($"Could not create {generator.DescriptionType.Name}.");
        }
        catch (JsonException)
        {
            return Activator.CreateInstance(generator.DescriptionType)
                ?? throw new InvalidOperationException($"Could not create {generator.DescriptionType.Name}.");
        }
    }

    private static JsonObject CreateDefaultDescription(IUntypedSoundGenerator generator)
    {
        object instance = Activator.CreateInstance(generator.DescriptionType)
            ?? throw new InvalidOperationException($"Could not create {generator.DescriptionType.Name}.");
        return JsonSerializer.SerializeToNode(instance, generator.DescriptionType, DescriptionJsonOptions) as JsonObject ?? new JsonObject();
    }

    private static JsonObject CreateRandomDescription(IUntypedSoundGenerator generator)
    {
        JsonObject result = CreateDefaultDescription(generator);
        var descriptors = generator.Descriptor.Parameters.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (PropertyInfo property in generator.DescriptionType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            string id = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            object? value = null;
            if (type.IsEnum)
            {
                Array values = Enum.GetValues(type);
                value = values.GetValue(Random.Shared.Next(values.Length));
            }
            else if (type == typeof(bool)) value = Random.Shared.Next(2) == 0;
            else if (IsNumeric(type))
            {
                GeneratorParameterDescriptor? descriptor = descriptors.GetValueOrDefault(id);
                double minimum = descriptor?.Minimum ?? 0;
                double maximum = descriptor?.Maximum ?? 1;
                double number = minimum + ((maximum - minimum) * Random.Shared.NextDouble());
                value = ConvertRandomNumber(number, type);
            }
            if (value is not null)
                result[id] = JsonSerializer.SerializeToNode(value, type, DescriptionJsonOptions);
        }
        return result;
    }

    private static object ConvertRandomNumber(double number, Type type)
    {
        if (type == typeof(byte)) return (byte)Math.Clamp(Math.Round(number), byte.MinValue, byte.MaxValue);
        if (type == typeof(short)) return (short)Math.Clamp(Math.Round(number), short.MinValue, short.MaxValue);
        if (type == typeof(int)) return (int)Math.Clamp(Math.Round(number), int.MinValue, int.MaxValue);
        if (type == typeof(long)) return (long)Math.Clamp(Math.Round(number), long.MinValue, long.MaxValue);
        if (type == typeof(float)) return (float)number;
        if (type == typeof(decimal)) return (decimal)number;
        return number;
    }

    private static bool IsNumeric(Type type) => type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal);

    private static string FormatNumber(double value) => value is >= 0 and <= 1 ? $"{value * 100:0}%" : $"{value:0.##}";

    private static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var characters = new List<char>(value.Length + 8) { char.ToUpperInvariant(value[0]) };
        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if (char.IsUpper(character) && !char.IsWhiteSpace(value[index - 1])) characters.Add(' ');
            characters.Add(character);
        }
        return new string(characters.ToArray());
    }

    private static string FamilyButtonText(SoundGeneratorCategory category) => category switch
    {
        SoundGeneratorCategory.Interface => "UI / BLOOPS",
        SoundGeneratorCategory.Impact => "IMPACTS",
        SoundGeneratorCategory.Explosion => "EXPLOSIONS",
        SoundGeneratorCategory.Weapon => "WEAPONS",
        SoundGeneratorCategory.Footstep => "FOOTSTEPS",
        SoundGeneratorCategory.Magic => "MAGIC",
        SoundGeneratorCategory.Retro => "RETRO GAME",
        SoundGeneratorCategory.Creature => "CREATURE VOICE",
        SoundGeneratorCategory.CreatureCry => "CREATURE CRIES",
        SoundGeneratorCategory.ClassicHandheldCry => "HANDHELD CRIES",
        SoundGeneratorCategory.Melodic => "MELODIC EFFECTS",
        SoundGeneratorCategory.ComplexMagic => "COMPLEX MAGIC",
        SoundGeneratorCategory.CreatureAction => "CREATURE ACTIONS",
        SoundGeneratorCategory.CombatSequence => "COMBAT CHAINS",
        SoundGeneratorCategory.Destruction => "DESTRUCTION",
        SoundGeneratorCategory.MaterialFoley => "MATERIAL FOLEY",
        SoundGeneratorCategory.Movement => "MOVEMENT",
        SoundGeneratorCategory.Liquid => "WATER / LIQUID",
        SoundGeneratorCategory.ElementalEnergy => "ELEMENTS / ENERGY",
        SoundGeneratorCategory.Environment => "ENVIRONMENTS",
        SoundGeneratorCategory.Machine => "MACHINES",
        SoundGeneratorCategory.Vehicle => "VEHICLES",
        SoundGeneratorCategory.ScienceFiction => "SCI-FI TECH",
        SoundGeneratorCategory.Horror => "HORROR",
        SoundGeneratorCategory.Cartoon => "CARTOON",
        SoundGeneratorCategory.Household => "HOUSEHOLD",
        SoundGeneratorCategory.Signal => "SIGNALS / ALARMS",
        SoundGeneratorCategory.CrowdSports => "CROWDS / SPORTS",
        SoundGeneratorCategory.Transformation => "TRANSFORMATIONS",
        SoundGeneratorCategory.AbstractSynth => "SYNTH LAB",
        SoundGeneratorCategory.SoundSequence => "ACTION SEQUENCES",
        _ => category.ToString().ToUpperInvariant()
    };

    private static bool IsGenericWorkingName(string value) => value.StartsWith("New Sound", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("Quick Sound", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("New Interface Sound", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "Untitled Generator Request", StringComparison.OrdinalIgnoreCase);

    private static ulong NewSeed() => BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
        Padding = new Padding(3, 2, 0, 2)
    };

    private static Button DecisionButton(string text, Color backColor) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Margin = new Padding(4),
        Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
        BackColor = backColor,
        FlatStyle = FlatStyle.Flat
    };

    private static JsonSerializerOptions CreateDescriptionJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _document.Changed -= OnDocumentChanged;
            _previewDebounce.Stop();
            _previewDebounce.Dispose();
            _nameBox.Font.Dispose();
        }
        base.Dispose(disposing);
    }

    private enum QuickTweak
    {
        Shorter,
        Longer,
        Brighter,
        Darker,
        Softer,
        Harder,
        MoreTonal,
        MoreNoisy,
        MorePunch,
        LessPunch
    }

    private sealed record PresetItem(IGeneratorPreset? Preset, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
