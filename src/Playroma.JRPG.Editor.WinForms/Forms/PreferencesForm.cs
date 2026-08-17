using System.Diagnostics;
using Playroma.JRPG.Editor.Shell;
using Playroma.JRPG.Editor.WinForms.Accessibility;
using Playroma.JRPG.Editor.WinForms.Windowing;

namespace Playroma.JRPG.Editor.WinForms.Forms;

/// <summary>Application-scoped preferences dialog with Apply/OK/Cancel semantics.</summary>
public sealed class PreferencesForm : EditorToolForm
{
    private readonly ISettingsService _settings;
    private readonly WinFormsThemeService _theme;
    private readonly INotificationService _notifications;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
    private readonly ComboBox _themeChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumericUpDown _fontSize = new() { Minimum = 7, Maximum = 24, Width = 90 };
    private readonly ComboBox _uiScale = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox _density = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly CheckBox _followWindowsTextSize = new() { Text = "Use Windows text-size preferences as the base", AutoSize = true };
    private readonly CheckBox _showUtilitiesInTaskbar = new() { Text = "Show utility windows separately on the taskbar", AutoSize = true };
    private readonly CheckBox _reopenLastProject = new() { Text = "Reopen the last project on startup", AutoSize = true };
    private readonly CheckBox _restoreDocuments = new() { Text = "Restore editor documents after recovery", AutoSize = true };
    private readonly CheckBox _restoreWindows = new() { Text = "Restore project utility windows", AutoSize = true };
    private readonly TextBox _defaultProjectDirectory = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _confirmRaw = new() { Text = "Confirm before enabling advanced Raw Data editing", AutoSize = true };
    private readonly CheckBox _confirmPlugins = new() { Text = "Confirm destructive Plugin Manager operations", AutoSize = true };
    private readonly CheckBox _openOutputOnFailure = new() { Text = "Open Output when a background task fails", AutoSize = true };
    private readonly CheckBox _openValidationOnErrors = new() { Text = "Open Validation when validation finds errors", AutoSize = true };
    private readonly Label _settingsPath = new() { AutoSize = true, MaximumSize = new Size(720, 0) };
    private readonly Label _logsPath = new() { AutoSize = true, MaximumSize = new Size(720, 0) };
    private readonly Label _recoveryPath = new() { AutoSize = true, MaximumSize = new Size(720, 0) };
    private readonly Label _cachePath = new() { AutoSize = true, MaximumSize = new Size(720, 0) };
    private readonly Panel _buttons = new() { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(8) };
    private readonly Button _ok = new() { Text = "OK", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 30) };
    private readonly Button _apply = new() { Text = "Apply", AutoSize = true, MinimumSize = new Size(88, 30) };
    private EditorSettings _original = new();
    private EditorTheme _lastAppliedTheme;
    private bool _loaded;
    private bool _applying;
    private bool _resetWindowLayoutPending;

    public PreferencesForm(
        WindowRequest request,
        ISettingsService settings,
        WinFormsThemeService theme,
        INotificationService notifications)
        : base(request)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Name = nameof(PreferencesForm);
        Text = "Preferences";
        MinimumSize = new Size(720, 570);
        Size = new Size(840, 650);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BuildUi();
        WireEvents();
    }

    protected override ValueTask OnLoadWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _original = _settings.Current;
        _lastAppliedTheme = _original.Theme;
        LoadValues(_original);
        Panel root = new() { Dock = DockStyle.Fill };
        root.Controls.Add(_tabs);
        root.Controls.Add(_buttons);
        InstallContent(root);
        _loaded = true;
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RestoreUnappliedTheme();
        return ValueTask.FromResult(true);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RestoreUnappliedTheme();
        base.OnFormClosed(e);
    }

    private void BuildUi()
    {
        _themeChoice.Items.AddRange(Enum.GetNames<EditorTheme>());
        _uiScale.Items.AddRange(["100%", "110%", "125%", "150%"]);
        _density.Items.AddRange(Enum.GetNames<EditorUiDensity>());
        _tabs.TabPages.Add(BuildAppearancePage());
        _tabs.TabPages.Add(BuildStartupPage());
        _tabs.TabPages.Add(BuildEditingPage());
        _tabs.TabPages.Add(BuildAdvancedPage());

        FlowLayoutPanel flow = new()
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        flow.Controls.AddRange([_ok, _cancel, _apply]);
        _buttons.Controls.Add(flow);
        AcceptButton = _ok;
        CancelButton = _cancel;
    }

    private TabPage BuildAppearancePage()
    {
        TableLayoutPanel table = CreateSettingsTable();
        AddSetting(table, "&Theme", _themeChoice, "Changes are previewed immediately. Cancel restores the last applied theme.");
        AddSetting(table, "UI text &scale", _uiScale, "Scales labels, controls, toolbar icons, and spacing without changing project content.");
        AddSetting(table, "UI &density", _density, "Comfortable is recommended for touch and large displays; Compact shows more editor content.");
        AddSetting(table, "Windows text size", _followWindowsTextSize, "Use the current Windows UI font and accessibility text-size preference as Playroma's baseline.");
        AddSetting(table, "&Monospace editor font", _fontSize, "Font size used by Raw Data, Output details, and other advanced text views.");
        AddSetting(table, "Window &grouping", _showUtilitiesInTaskbar, "When disabled, utility windows do not receive separate taskbar buttons.");
        return new TabPage("Appearance") { Controls = { Wrap(table) } };
    }

    private TabPage BuildStartupPage()
    {
        TableLayoutPanel table = CreateSettingsTable();
        AddSetting(table, "Startup", _reopenLastProject, "Open the last successfully opened project when Playroma starts.");
        AddSetting(table, "Recovery", _restoreDocuments, "Restore document locations recorded by a recovery session.");
        AddSetting(table, "Windows", _restoreWindows, "Reopen project utility windows when window restoration is available.");
        FlowLayoutPanel directory = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        _defaultProjectDirectory.Width = 430;
        Button browse = new() { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => BrowseDefaultDirectory();
        directory.Controls.AddRange([_defaultProjectDirectory, browse]);
        AddSetting(table, "Default project directory", directory, "Initial folder used by project-opening and creation workflows.");
        return new TabPage("Startup") { Controls = { Wrap(table) } };
    }

    private TabPage BuildEditingPage()
    {
        TableLayoutPanel table = CreateSettingsTable();
        AddSetting(table, "Raw Data", _confirmRaw, "Require an explicit confirmation before a file becomes editable as raw JSON.");
        AddSetting(table, "Plugins", _confirmPlugins, "Confirm removal and other destructive plugin operations.");
        AddSetting(table, "Task failures", _openOutputOnFailure, "Bring the Output console forward when a background task fails.");
        AddSetting(table, "Validation", _openValidationOnErrors, "Bring the Validation Center forward after an error-producing validation run.");
        return new TabPage("Editing") { Controls = { Wrap(table) } };
    }

    private TabPage BuildAdvancedPage()
    {
        TableLayoutPanel table = CreateSettingsTable();
        _settingsPath.Text = _settings.SettingsPath;
        _logsPath.Text = AppDataPaths.Logs;
        _recoveryPath.Text = AppDataPaths.Recovery;
        _cachePath.Text = AppDataPaths.Cache;
        AddPathSetting(table, "Settings", _settingsPath, Path.GetDirectoryName(_settings.SettingsPath)!);
        AddPathSetting(table, "Logs", _logsPath, AppDataPaths.Logs);
        AddPathSetting(table, "Recovery", _recoveryPath, AppDataPaths.Recovery);
        AddPathSetting(table, "Cache", _cachePath, AppDataPaths.Cache);

        FlowLayoutPanel actions = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
        Button resetLayout = new() { Text = "Reset Window Layout", AutoSize = true };
        Button clearCache = new() { Text = "Clear Disposable Cache", AutoSize = true };
        Button exportSettings = new() { Text = "Export Settings…", AutoSize = true };
        Button importSettings = new() { Text = "Import Settings…", AutoSize = true };
        resetLayout.Click += (_, _) => ResetWindowLayout();
        clearCache.Click += (_, _) => ClearCache();
        exportSettings.Click += async (_, _) => await ExportSettingsAsync().ConfigureAwait(true);
        importSettings.Click += async (_, _) => await ImportSettingsAsync().ConfigureAwait(true);
        actions.Controls.AddRange([resetLayout, clearCache, exportSettings, importSettings]);
        AddSetting(table, "Maintenance", actions, "These operations do not modify the open RPG Maker project.");
        return new TabPage("Advanced") { Controls = { Wrap(table) } };
    }

    private void WireEvents()
    {
        _themeChoice.SelectedIndexChanged += (_, _) => PreviewTheme();
        _ok.Click += async (_, _) =>
        {
            if (!await ApplyAsync().ConfigureAwait(true)) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        _apply.Click += async (_, _) => await ApplyAsync().ConfigureAwait(true);
        _cancel.Click += (_, _) =>
        {
            RestoreUnappliedTheme();
            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    private async Task<bool> ApplyAsync()
    {
        if (_applying) return false;
        _applying = true;
        try
        {
            EditorSettings updated = BuildSettings(_settings.Current);
            if (_resetWindowLayoutPending) updated = ResetLayouts(updated);
            _settings.Update(_ => updated);
            await _settings.SaveAsync().ConfigureAwait(true);
            _lastAppliedTheme = updated.Theme;
            _original = updated;
            _theme.SetTheme(updated.Theme);
            _theme.NotifyVisualPreferencesChanged();
            _resetWindowLayoutPending = false;
            _notifications.Success("Preferences applied", "Application preferences were saved.");
            return true;
        }
        catch (Exception exception)
        {
            _notifications.Error("Preferences could not be saved", exception.Message, exception);
            MessageBox.Show(this, exception.Message, "Preferences", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally { _applying = false; }
    }

    private EditorSettings BuildSettings(EditorSettings current)
    {
        EditorTheme theme = Enum.TryParse(_themeChoice.SelectedItem?.ToString(), out EditorTheme parsed)
            ? parsed
            : current.Theme;
        int scale = int.TryParse((_uiScale.SelectedItem?.ToString() ?? "100%").TrimEnd('%'), out int parsedScale)
            ? Math.Clamp(parsedScale, 100, 150)
            : current.EffectivePreferences.UiScalePercent;
        EditorUiDensity density = Enum.TryParse(_density.SelectedItem?.ToString(), out EditorUiDensity parsedDensity)
            ? parsedDensity
            : current.EffectivePreferences.UiDensity;
        ApplicationPreferenceSettings preferences = new(
            ShowUtilityWindowsInTaskbar: _showUtilitiesInTaskbar.Checked,
            RestoreProjectWindows: _restoreWindows.Checked,
            ConfirmAdvancedRawJsonEditing: _confirmRaw.Checked,
            ConfirmDestructivePluginOperations: _confirmPlugins.Checked,
            OpenOutputOnTaskFailure: _openOutputOnFailure.Checked,
            OpenValidationWhenErrorsFound: _openValidationOnErrors.Checked,
            DefaultProjectDirectory: string.IsNullOrWhiteSpace(_defaultProjectDirectory.Text) ? null : _defaultProjectDirectory.Text.Trim(),
            MonospaceEditorFontSize: Decimal.ToInt32(_fontSize.Value),
            UiScalePercent: scale,
            UiDensity: density,
            FollowWindowsTextSize: _followWindowsTextSize.Checked,
            Version: 2);
        return current with
        {
            Theme = theme,
            ReopenLastProjectOnStartup = _reopenLastProject.Checked,
            RestoreDocuments = _restoreDocuments.Checked,
            Preferences = preferences,
            Version = EditorSettingsMigrator.CurrentVersion
        };
    }

    private void LoadValues(EditorSettings value)
    {
        ApplicationPreferenceSettings preferences = value.EffectivePreferences;
        _themeChoice.SelectedItem = value.Theme.ToString();
        _fontSize.Value = Math.Clamp(preferences.MonospaceEditorFontSize, (int)_fontSize.Minimum, (int)_fontSize.Maximum);
        string scale = $"{Math.Clamp(preferences.UiScalePercent, 100, 150)}%";
        _uiScale.SelectedItem = _uiScale.Items.Contains(scale) ? scale : "100%";
        _density.SelectedItem = preferences.UiDensity.ToString();
        _followWindowsTextSize.Checked = preferences.FollowWindowsTextSize;
        _showUtilitiesInTaskbar.Checked = preferences.ShowUtilityWindowsInTaskbar;
        _reopenLastProject.Checked = value.ReopenLastProjectOnStartup;
        _restoreDocuments.Checked = value.RestoreDocuments;
        _restoreWindows.Checked = preferences.RestoreProjectWindows;
        _defaultProjectDirectory.Text = preferences.DefaultProjectDirectory ?? value.LastOpenDirectory ?? string.Empty;
        _confirmRaw.Checked = preferences.ConfirmAdvancedRawJsonEditing;
        _confirmPlugins.Checked = preferences.ConfirmDestructivePluginOperations;
        _openOutputOnFailure.Checked = preferences.OpenOutputOnTaskFailure;
        _openValidationOnErrors.Checked = preferences.OpenValidationWhenErrorsFound;
    }

    private void PreviewTheme()
    {
        if (!_loaded || _applying) return;
        if (Enum.TryParse(_themeChoice.SelectedItem?.ToString(), out EditorTheme theme)) _theme.SetTheme(theme);
    }

    private void RestoreUnappliedTheme()
    {
        if (!_loaded || _theme.Theme == _lastAppliedTheme) return;
        _theme.SetTheme(_lastAppliedTheme);
    }

    private void BrowseDefaultDirectory()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select the default project directory",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_defaultProjectDirectory.Text) ? _defaultProjectDirectory.Text : null
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _defaultProjectDirectory.Text = dialog.SelectedPath;
    }

    private void ResetWindowLayout()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Reset all remembered window positions and internal tool layouts? The new defaults take effect the next time each window opens.",
            "Reset Window Layout",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        _resetWindowLayoutPending = true;
        _notifications.Information("Window layout reset queued", "Choose Apply or OK to clear remembered window and tool layouts. Cancel leaves them unchanged.");
    }

    private static EditorSettings ResetLayouts(EditorSettings current) => current with
    {
        WindowPlacements = [],
        MapEditorLayout = new MapEditorLayoutSettings(),
        DatabaseWindowLayout = new DatabaseWindowLayoutSettings(),
        ResourceManagerLayout = new ResourceManagerWindowLayoutSettings(),
        ValidationWindowLayout = new ValidationWindowLayoutSettings(),
        ReferenceExplorerWindowLayout = new ReferenceExplorerWindowLayoutSettings(),
        OutputWindowLayout = new OutputWindowLayoutSettings(),
        BackgroundTasksWindowLayout = new BackgroundTasksWindowLayoutSettings(),
        RawJsonWindowLayout = new RawJsonWindowLayoutSettings(),
        PluginManagerWindowLayout = new PluginManagerWindowLayoutSettings(),
        ProjectSummaryWindowLayout = new ProjectSummaryWindowLayoutSettings(),
        Version = EditorSettingsMigrator.CurrentVersion
    };

    private void ClearCache()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Delete disposable cached thumbnails and analysis data?",
            "Clear Cache",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        try
        {
            if (Directory.Exists(AppDataPaths.Cache)) Directory.Delete(AppDataPaths.Cache, recursive: true);
            Directory.CreateDirectory(AppDataPaths.Cache);
            _notifications.Success("Cache cleared", AppDataPaths.Cache);
        }
        catch (Exception exception)
        {
            _notifications.Error("Cache could not be cleared", exception.Message, exception);
        }
    }

    private async Task ExportSettingsAsync()
    {
        using SaveFileDialog dialog = new()
        {
            Title = "Export Playroma Settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "playroma-settings.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await _settings.SaveAsync().ConfigureAwait(true);
        File.Copy(_settings.SettingsPath, dialog.FileName, overwrite: true);
        _notifications.Success("Settings exported", dialog.FileName);
    }

    private async Task ImportSettingsAsync()
    {
        using OpenFileDialog dialog = new()
        {
            Title = "Import Playroma Settings",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        DialogResult answer = MessageBox.Show(
            this,
            "Replace the current application settings with the selected file?",
            "Import Settings",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_settings.SettingsPath)!);
        File.Copy(dialog.FileName, _settings.SettingsPath, overwrite: true);
        await _settings.LoadAsync().ConfigureAwait(true);
        _original = _settings.Current;
        _lastAppliedTheme = _original.Theme;
        LoadValues(_original);
        _theme.SetTheme(_original.Theme);
        _notifications.Success("Settings imported", dialog.FileName);
    }

    private static TableLayoutPanel CreateSettingsTable()
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(14)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return table;
    }

    private static Control Wrap(Control content)
    {
        Panel panel = new() { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(content);
        return panel;
    }

    private static void AddSetting(TableLayoutPanel table, string label, Control control, string description)
    {
        int row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label caption = AccessibilityMetadata.CreateLabel(label, control, marginTop: 9);
        caption.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
        caption.Margin = new Padding(3, 9, 12, 9);
        table.Controls.Add(caption, 0, row);
        FlowLayoutPanel host = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(3, 5, 3, 10)
        };
        control.Margin = new Padding(0, 0, 0, 3);
        host.Controls.Add(control);
        host.Controls.Add(new Label { Text = description, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(540, 0) });
        table.Controls.Add(host, 1, row);
    }

    private static void AddPathSetting(TableLayoutPanel table, string label, Label pathLabel, string folder)
    {
        FlowLayoutPanel host = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        Button open = new() { Text = "Open Folder", AutoSize = true };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        };
        host.Controls.Add(pathLabel);
        host.Controls.Add(open);
        AddSetting(table, label, host, string.Empty);
    }
}
