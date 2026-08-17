using System.Text.Json;
using Playroma.Sprite.Automation;

namespace Playroma.Sprite.Editor.WinForms;

internal sealed class AutomationPanel : UserControl
{
    private readonly SpriteEditorSession session;
    private readonly string workspaceRoot;
    private readonly Func<IReadOnlyCollection<int>> selectedFrames;
    private readonly string automationRoot;
    private readonly ExportPresetStore presetStore;
    private readonly ListBox presetList = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly PropertyGrid presetProperties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly TextBox scriptEditor = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, AcceptsTab = true, Font = new Font(FontFamily.GenericMonospace, 9f), WordWrap = false };
    private readonly ListView extensionList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, CheckBoxes = true };
    private readonly TextBox output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 9f), WordWrap = false };
    private readonly CheckBox dryRun = new() { Text = "Dry run — report operations without changing files", AutoSize = true, Checked = true };
    private readonly Label documentLabel = new() { AutoSize = true };
    private SpriteExtensionRegistry? extensionRegistry;
    private SpriteExtensionSettings extensionSettings;
    private readonly string extensionSettingsPath;
    private string? currentScriptPath;
    private bool suppressExtensionChecks;

    public AutomationPanel(SpriteEditorSession session, string? projectRoot, Func<IReadOnlyCollection<int>>? selectedFrames = null)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.selectedFrames = selectedFrames ?? (() => [session.ActiveFrameIndex]);
        workspaceRoot = ResolveWorkspaceRoot(projectRoot);
        automationRoot = Path.Combine(workspaceRoot, "data", "PlayromaSprite", "Automation");
        presetStore = new(Path.Combine(automationRoot, "Presets"));
        extensionSettingsPath = Path.Combine(automationRoot, "extension-settings.json");
        extensionSettings = SpriteExtensionSettings.Load(extensionSettingsPath);
        Dock = DockStyle.Fill;
        Controls.Add(BuildLayout());
        RefreshPresets(); RefreshExtensions(); RefreshDocument();
        scriptEditor.Text = SpriteScriptTemplates.Starter;
        Disposed += (_, _) => extensionRegistry?.Dispose();
    }

    public void RefreshDocument() => documentLabel.Text = $"Document: {session.Document.Name}  |  {session.Document.Canvas.Width}×{session.Document.Canvas.Height}  |  {session.Document.Frames.Count} frame(s)";

    public void LoadPipelineFromDialog(IWin32Window owner)
    {
        using OpenFileDialog dialog = new() { Filter = "Playroma pipeline (*.playroma-pipeline.json;*.json)|*.playroma-pipeline.json;*.json|All files (*.*)|*.*", Title = "Run Automation Pipeline" };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        _ = RunPipelineFileAsync(dialog.FileName, dryRun.Checked);
    }

    public void ExportAtlasFromDialog(IWin32Window owner)
    {
        using SaveFileDialog dialog = new() { Filter = "PNG atlas (*.png)|*.png", FileName = AutomationVariables.Sanitize(session.Document.Name) + "-atlas.png", Title = "Export Sprite Atlas" };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        SpriteExportPreset preset = new() { Name = "One-time Atlas", Format = "atlas", OutputPath = Path.GetRelativePath(workspaceRoot, dialog.FileName), Padding = 1, Extrude = true, PowerOfTwo = true, TrimTransparent = true };
        _ = RunPresetAsync(preset, dryRun.Checked);
    }

    public void FocusScriptEditor() => scriptEditor.Focus();

    private Control BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 68)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        FlowLayoutPanel header = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        header.Controls.Add(documentLabel); header.Controls.Add(new Label { Width = 12 }); header.Controls.Add(dryRun);
        TabControl tabs = new() { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildPresetsPage()); tabs.TabPages.Add(BuildScriptsPage()); tabs.TabPages.Add(BuildExtensionsPage());
        GroupBox outputGroup = new() { Dock = DockStyle.Fill, Text = "Automation Output" }; outputGroup.Controls.Add(output);
        root.Controls.Add(header, 0, 0); root.Controls.Add(tabs, 0, 1); root.Controls.Add(outputGroup, 0, 2); return root;
    }

    private TabPage BuildPresetsPage()
    {
        TabPage page = new("Export Presets"); SplitContainer split = new() { Dock = DockStyle.Fill, SplitterDistance = 260 };
        FlowLayoutPanel buttons = Buttons(); Button add = Button("New"); Button save = Button("Save"); Button remove = Button("Delete"); Button run = Button("Run"); Button refresh = Button("Refresh");
        add.Click += (_, _) => { SpriteExportPreset preset = new() { Name = "New Atlas Preset", Format = "atlas", OutputPath = "exports/${document}-atlas.png", Padding = 1, Extrude = true, PowerOfTwo = true }; presetProperties.SelectedObject = preset; };
        save.Click += (_, _) => { if (presetProperties.SelectedObject is SpriteExportPreset preset) { string path = presetStore.Save(preset); Append($"Saved preset {path}"); RefreshPresets(); } };
        remove.Click += (_, _) => { if (presetList.SelectedItem is PresetListItem item && File.Exists(item.Path)) { File.Delete(item.Path); RefreshPresets(); } };
        run.Click += async (_, _) => { if (presetProperties.SelectedObject is SpriteExportPreset preset) await RunPresetAsync(preset, dryRun.Checked); };
        refresh.Click += (_, _) => RefreshPresets(); buttons.Controls.AddRange([add, save, remove, run, refresh]);
        Panel left = new() { Dock = DockStyle.Fill }; left.Controls.Add(presetList); left.Controls.Add(buttons); buttons.Dock = DockStyle.Top;
        presetList.SelectedIndexChanged += (_, _) => presetProperties.SelectedObject = (presetList.SelectedItem as PresetListItem)?.Preset;
        split.Panel1.Controls.Add(left); split.Panel2.Controls.Add(presetProperties); page.Controls.Add(split); return page;
    }

    private TabPage BuildScriptsPage()
    {
        TabPage page = new("Scripts & Pipelines"); FlowLayoutPanel buttons = Buttons();
        Button starter = Button("Starter"); Button open = Button("Open..."); Button save = Button("Save..."); Button run = Button("Run Script"); Button pipeline = Button("Run Pipeline..."); Button validate = Button("Validate");
        starter.Click += (_, _) => { currentScriptPath = null; scriptEditor.Text = SpriteScriptTemplates.Starter; };
        open.Click += (_, _) => OpenScript(); save.Click += (_, _) => SaveScript(); run.Click += async (_, _) => await RunScriptTextAsync(dryRun.Checked); pipeline.Click += (_, _) => LoadPipelineFromDialog(this); validate.Click += (_, _) => ValidateScriptText();
        buttons.Controls.AddRange([starter, open, save, run, pipeline, validate]); Panel panel = new() { Dock = DockStyle.Fill }; panel.Controls.Add(scriptEditor); panel.Controls.Add(buttons); buttons.Dock = DockStyle.Top; page.Controls.Add(panel); return page;
    }

    private TabPage BuildExtensionsPage()
    {
        TabPage page = new("Extensions"); extensionList.Columns.Add("Extension", 190); extensionList.Columns.Add("Version", 75); extensionList.Columns.Add("Trusted", 70); extensionList.Columns.Add("Status", 180);
        FlowLayoutPanel buttons = Buttons(); Button refresh = Button("Discover"); Button trust = Button("Trust Selected"); Button untrust = Button("Remove Trust"); Button load = Button("Load Enabled");
        refresh.Click += (_, _) => RefreshExtensions(); trust.Click += (_, _) => SetSelectedTrust(true); untrust.Click += (_, _) => SetSelectedTrust(false); load.Click += (_, _) => LoadEnabledExtensions(); buttons.Controls.AddRange([refresh, trust, untrust, load]);
        extensionList.ItemChecked += (_, e) => { if (suppressExtensionChecks) return; if (e.Item.Tag is SpriteExtensionDescriptor descriptor) { if (e.Item.Checked) extensionSettings.Enabled.Add(descriptor.Manifest.Id); else extensionSettings.Enabled.Remove(descriptor.Manifest.Id); SaveExtensionSettings(); } };
        Panel panel = new() { Dock = DockStyle.Fill }; panel.Controls.Add(extensionList); panel.Controls.Add(buttons); buttons.Dock = DockStyle.Top; page.Controls.Add(panel); return page;
    }

    private async Task RunPresetAsync(SpriteExportPreset preset, bool isDryRun)
    {
        try
        {
            ClearOutput(); AutomationRunResult result = await new SpriteAutomationEngine(extensionRegistry).RunPresetAsync(preset, CreateContext(isDryRun)); ShowResult(result);
        }
        catch (Exception exception) { Append("ERROR: " + exception.Message); }
    }

    private async Task RunScriptTextAsync(bool isDryRun)
    {
        try
        {
            ClearOutput(); PlayromaSpriteScript script = PlayromaSpriteScript.Parse(scriptEditor.Text); SpriteAutomationContext context = CreateContext(isDryRun); AutomationRunResult result = await new SpriteAutomationEngine(extensionRegistry).RunScriptAsync(script, context); ShowResult(result); if (!isDryRun && !ReferenceEquals(context.Package, session.Package)) session.ReplacePackage(context.Package, dirty: true); else if (!isDryRun && result.Operations.Any(item => item.Kind == "modify" && item.Performed)) session.MarkDirty();
        }
        catch (Exception exception) { Append("ERROR: " + exception.Message); }
    }

    private async Task RunPipelineFileAsync(string path, bool isDryRun)
    {
        try
        {
            ClearOutput(); SpriteAutomationPipeline pipeline = JsonSerializer.Deserialize<SpriteAutomationPipeline>(File.ReadAllText(path), AutomationJson.Options) ?? throw new InvalidDataException("Pipeline file is empty."); SpriteAutomationContext context = CreateContext(isDryRun); AutomationRunResult result = await new SpriteAutomationEngine(extensionRegistry).RunAsync(pipeline, context); ShowResult(result); if (!isDryRun && !ReferenceEquals(context.Package, session.Package)) session.ReplacePackage(context.Package, dirty: true); else if (!isDryRun && result.Operations.Any(item => item.Kind == "modify" && item.Performed)) session.MarkDirty();
        }
        catch (Exception exception) { Append("ERROR: " + exception.Message); }
    }

    private SpriteAutomationContext CreateContext(bool isDryRun)
    {
        SpriteAutomationContext context = new()
        {
            Package = session.Package,
            PackagePath = session.PackagePath,
            WorkspaceRoot = workspaceRoot,
            OutputRoot = Path.Combine("data", "PlayromaSprite", "Exports"),
            DryRun = isDryRun,
            GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.InstallRuntimeAsset | (extensionRegistry is null ? SpriteAutomationCapability.None : SpriteAutomationCapability.LoadExtensions),
            Log = Append
        };
        context.Variables["frame"] = session.ActiveFrameIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Variables["selectedFrames"] = string.Join(",", selectedFrames().Distinct().Order());
        return context;
    }

    private void RefreshPresets()
    {
        presetList.BeginUpdate(); presetList.Items.Clear(); foreach ((string path, SpriteExportPreset preset, string? error) in presetStore.LoadAll()) presetList.Items.Add(new PresetListItem(path, preset, error)); presetList.EndUpdate();
        if (presetList.Items.Count == 0)
        {
            SpriteExportPreset atlas = new() { Name = "Power-of-Two Atlas", Format = "atlas", OutputPath = "data/PlayromaSprite/Exports/${document}-atlas.png", Padding = 1, Extrude = true, TrimTransparent = true, PowerOfTwo = true };
            SpriteExportPreset png = new() { Name = "Current Frame PNG", Format = "png", OutputPath = "data/PlayromaSprite/Exports/${document}.png" };
            presetStore.Save(atlas); presetStore.Save(png); RefreshPresets(); return;
        }
        presetList.SelectedIndex = 0;
    }

    private void RefreshExtensions()
    {
        string root = Path.Combine(automationRoot, "Extensions"); suppressExtensionChecks = true; extensionList.BeginUpdate(); extensionList.Items.Clear();
        foreach (SpriteExtensionDescriptor descriptor in SpriteExtensionCatalog.Discover(root, extensionSettings.Trusted, extensionSettings.Enabled))
        {
            ListViewItem item = new(descriptor.Manifest.Name) { Tag = descriptor, Checked = descriptor.Enabled };
            item.SubItems.Add(descriptor.Manifest.Version); item.SubItems.Add(descriptor.Trusted ? "Yes" : "No"); item.SubItems.Add(descriptor.Error ?? (descriptor.Enabled ? "Enabled" : "Disabled")); extensionList.Items.Add(item);
        }
        extensionList.EndUpdate(); suppressExtensionChecks = false;
    }

    private void SetSelectedTrust(bool trusted)
    {
        if (extensionList.SelectedItems.Count == 0 || extensionList.SelectedItems[0].Tag is not SpriteExtensionDescriptor descriptor) return;
        if (trusted) extensionSettings.Trusted.Add(descriptor.Manifest.Id); else { extensionSettings.Trusted.Remove(descriptor.Manifest.Id); extensionSettings.Enabled.Remove(descriptor.Manifest.Id); }
        SaveExtensionSettings(); RefreshExtensions();
    }

    private void LoadEnabledExtensions()
    {
        try
        {
            extensionRegistry?.Dispose(); extensionRegistry = new(SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.WriteFiles, Append);
            string root = Path.Combine(automationRoot, "Extensions"); int loaded = 0;
            foreach (SpriteExtensionDescriptor descriptor in SpriteExtensionCatalog.Discover(root, extensionSettings.Trusted, extensionSettings.Enabled).Where(item => item.Trusted && item.Enabled && item.Error is null)) { extensionRegistry.Load(descriptor); loaded++; }
            Append($"Loaded {loaded} trusted extension(s), {extensionRegistry.Commands.Count} command(s), and {extensionRegistry.FormatProviders.Count} format provider(s).");
        }
        catch (Exception exception) { Append("ERROR: " + exception.Message); }
    }

    private void ValidateScriptText()
    {
        try { PlayromaSpriteScript script = PlayromaSpriteScript.Parse(scriptEditor.Text); Append($"Script '{script.Name}' is valid with {script.Steps.Count} step(s)."); }
        catch (Exception exception) { Append("ERROR: " + exception.Message); }
    }

    private void OpenScript()
    {
        using OpenFileDialog dialog = new() { Filter = "Playroma Sprite script (*.playroma-sprite-script.json;*.json)|*.playroma-sprite-script.json;*.json|All files (*.*)|*.*" }; if (dialog.ShowDialog(this) != DialogResult.OK) return; currentScriptPath = dialog.FileName; scriptEditor.Text = File.ReadAllText(dialog.FileName);
    }

    private void SaveScript()
    {
        string? path = currentScriptPath; if (path is null) { using SaveFileDialog dialog = new() { Filter = "Playroma Sprite script (*.playroma-sprite-script.json)|*.playroma-sprite-script.json", FileName = "sprite-script.playroma-sprite-script.json" }; if (dialog.ShowDialog(this) != DialogResult.OK) return; path = dialog.FileName; currentScriptPath = path; }
        PlayromaSpriteScript.Parse(scriptEditor.Text); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, scriptEditor.Text); Append("Saved " + path);
    }

    private void ShowResult(AutomationRunResult result)
    {
        foreach (AutomationDiagnostic diagnostic in result.Diagnostics) Append($"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code}: {diagnostic.Message}");
        foreach (AutomationOperation operation in result.Operations) Append($"{(operation.Performed ? "DONE" : "PLAN")} {operation.Kind}: {operation.Description} {operation.DestinationPath}");
        Append(result.Success ? "Automation completed successfully." : "Automation completed with errors.");
    }

    private void SaveExtensionSettings() { extensionSettings.Save(extensionSettingsPath); }
    private void ClearOutput() => output.Clear();
    private void Append(string message) { if (InvokeRequired) { BeginInvoke(new Action(() => Append(message))); return; } output.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}"); }
    private static FlowLayoutPanel Buttons() => new() { AutoSize = true, WrapContents = true, Padding = new Padding(3) };
    private static Button Button(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3) };
    private static string ResolveWorkspaceRoot(string? projectRoot) => !string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot) ? Path.GetFullPath(projectRoot) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playroma", "SpriteStudio", "Workspace");
    private sealed record PresetListItem(string Path, SpriteExportPreset Preset, string? Error) { public override string ToString() => Error is null ? Preset.Name : $"{Preset.Name} ⚠"; }
}
