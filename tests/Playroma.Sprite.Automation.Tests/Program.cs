using System.Text.Json;
using Playroma.Sprite.Automation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;

int assertions = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); assertions++; }
string root = Path.Combine(Path.GetTempPath(), "PlayromaSpriteAutomationTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    SpritePackageData package = SpritePackageData.CreateEmpty("Automation Hero", 8, 8);
    package.GetRequired(package.Document.PixelContents[0].Id).SetPixel(2, 3, new Rgba32(255, 0, 0, 255));
    SafeAutomationPaths paths = new(root);
    Check(paths.Resolve("exports/test.png").StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase), "Workspace path did not resolve inside root.");
    bool escaped = false; try { paths.Resolve("../escape.png"); } catch (UnauthorizedAccessException) { escaped = true; } Check(escaped, "Workspace traversal was not rejected.");

    SpriteAutomationContext context = new()
    {
        Package = package,
        WorkspaceRoot = root,
        OutputRoot = "exports",
        GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.ReadFiles,
        DryRun = true
    };
    Check(AutomationVariables.Expand("${output}/${document}-${width}x${height}.png", context).Contains("Automation Hero-8x8", StringComparison.Ordinal), "Automation variables did not expand.");

    SpriteExportPreset atlasPreset = new() { Name = "Test Atlas", Format = "atlas", OutputPath = "exports/test-atlas.png", Padding = 1, PowerOfTwo = true, Extrude = true, TrimTransparent = true };
    SpriteAtlasResult atlas = SpriteAtlasPacker.Pack(package, atlasPreset);
    Check((atlas.Image.Width & (atlas.Image.Width - 1)) == 0, "Atlas width is not power-of-two.");
    Check((atlas.Image.Height & (atlas.Image.Height - 1)) == 0, "Atlas height is not power-of-two.");
    Check(atlas.Frames.Count == 1 && atlas.MetadataJson.Contains("playroma-sprite-atlas/1", StringComparison.Ordinal), "Atlas metadata contract failed.");

    SpriteAutomationPipeline dryPipeline = new()
    {
        Name = "Dry Run",
        RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles,
        Steps = [new SpriteAutomationStep { Id = "png", Command = "export-png", Arguments = new() { ["path"] = JsonSerializer.SerializeToElement("exports/dry.png") } }]
    };
    AutomationRunResult dryResult = await new SpriteAutomationEngine().RunAsync(dryPipeline, context);
    Check(dryResult.Success, "Dry-run pipeline failed.");
    Check(!File.Exists(Path.Combine(root, "exports", "dry.png")), "Dry run wrote a file.");
    Check(dryResult.Operations.Single().Performed == false, "Dry-run operation was not marked as planned.");

    SpriteAutomationContext liveContext = new()
    {
        Package = package,
        WorkspaceRoot = root,
        OutputRoot = "exports",
        GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.ReadFiles
    };
    SpriteAutomationPipeline livePipeline = new()
    {
        Name = "Live Export",
        RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles,
        Steps = [new SpriteAutomationStep { Id = "png", Command = "export-png", Arguments = new() { ["path"] = JsonSerializer.SerializeToElement("exports/live.png") } }]
    };
    AutomationRunResult liveResult = await new SpriteAutomationEngine().RunAsync(livePipeline, liveContext);
    Check(liveResult.Success && File.Exists(Path.Combine(root, "exports", "live.png")), "Live PNG export failed.");

    PlayromaSpriteScript script = PlayromaSpriteScript.Parse(SpriteScriptTemplates.Starter);
    Check(script.Schema == "playroma-sprite-script/1" && script.Steps.Count == 2, "Starter script did not parse.");
    SpriteAutomationPipeline scriptPipeline = script.CreatePipeline(new Dictionary<string, string> { ["output"] = "exports/script-atlas.png" });
    Check(scriptPipeline.Steps.Any(step => step.Command == "export-atlas"), "Script did not create an atlas pipeline.");

    ExportPresetStore store = new(Path.Combine(root, "presets")); string presetPath = store.Save(atlasPreset);
    Check(File.Exists(presetPath) && store.LoadAll().Count == 1, "Export preset store did not round-trip.");

    string extensionDirectory = Path.Combine(root, "extensions", "sample"); Directory.CreateDirectory(extensionDirectory);
    SpriteExtensionManifest manifest = new() { Id = "sample.extension", Name = "Sample Extension", Version = "1.2.3", EntryAssembly = "Sample.dll", EntryType = "Sample.Extension" };
    File.WriteAllText(Path.Combine(extensionDirectory, "playroma-sprite-extension.json"), JsonSerializer.Serialize(manifest, AutomationJson.Options));
    HashSet<string> trusted = new(StringComparer.OrdinalIgnoreCase) { manifest.Id }; HashSet<string> enabled = new(StringComparer.OrdinalIgnoreCase) { manifest.Id };
    SpriteExtensionDescriptor descriptor = SpriteExtensionCatalog.Discover(Path.Combine(root, "extensions"), trusted, enabled).Single();
    Check(descriptor.Trusted && descriptor.Enabled && descriptor.Error is null, "Extension discovery/trust state failed.");

    SpriteExtensionSettings settings = new(); settings.Trusted.Add(manifest.Id); settings.Enabled.Add(manifest.Id); string settingsPath = Path.Combine(root, "settings.json"); settings.Save(settingsPath);
    SpriteExtensionSettings loadedSettings = SpriteExtensionSettings.Load(settingsPath); Check(loadedSettings.Trusted.Contains(manifest.Id) && loadedSettings.Enabled.Contains(manifest.Id), "Extension settings did not round-trip.");

    using (SpriteExtensionRegistry registry = new(SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions))
    {
        registry.RegisterFormatProvider(new TextSpriteFormatProvider());
        CountingExtensionCommand extensionCommand = new(); registry.RegisterCommand(extensionCommand);
        SpriteAutomationContext formatContext = new() { Package = package, WorkspaceRoot = root, GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions };
        SpriteAutomationStep exportFormat = new() { Id = "custom-export", Command = "export-format", Arguments = new() { ["provider"] = JsonSerializer.SerializeToElement("test-text"), ["path"] = JsonSerializer.SerializeToElement("exports/custom.sprite.txt") } };
        AutomationRunResult formatExportResult = await new SpriteAutomationEngine(registry).RunAsync(new SpriteAutomationPipeline { Name = "Custom Export", RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions, Steps = [exportFormat] }, formatContext);
        Check(formatExportResult.Success && File.Exists(Path.Combine(root, "exports", "custom.sprite.txt")), "Custom format provider export failed.");
        SpriteAutomationStep importFormat = new() { Id = "custom-import", Command = "import-format", Arguments = new() { ["provider"] = JsonSerializer.SerializeToElement("test-text"), ["path"] = JsonSerializer.SerializeToElement("exports/custom.sprite.txt") } };
        AutomationRunResult formatImportResult = await new SpriteAutomationEngine(registry).RunAsync(new SpriteAutomationPipeline { Name = "Custom Import", RequiredCapabilities = SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.LoadExtensions, Steps = [importFormat] }, formatContext);
        Check(formatImportResult.Success && formatContext.Package.Document.Name == "Automation Hero", "Custom format provider import failed.");
        SpriteAutomationContext extensionDryContext = new() { Package = package, WorkspaceRoot = root, DryRun = true, GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.LoadExtensions };
        SpriteAutomationStep extensionStep = new() { Id = "extension", Command = "extension", Arguments = new() { ["command"] = JsonSerializer.SerializeToElement(extensionCommand.Id) } };
        AutomationRunResult extensionDryResult = await new SpriteAutomationEngine(registry).RunAsync(new SpriteAutomationPipeline { Name = "Extension Dry Run", RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.LoadExtensions, Steps = [extensionStep] }, extensionDryContext);
        Check(extensionDryResult.Success && extensionCommand.ExecutionCount == 0, "Dry run executed extension code.");
        SpriteAutomationStep directExtensionStep = new() { Id = "direct-extension", Command = extensionCommand.Id };
        AutomationRunResult directExtensionDryResult = await new SpriteAutomationEngine(registry).RunAsync(new SpriteAutomationPipeline { Name = "Direct Extension Dry Run", RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.LoadExtensions, Steps = [directExtensionStep] }, extensionDryContext);
        Check(directExtensionDryResult.Success && extensionCommand.ExecutionCount == 0, "Dry run executed a directly addressed extension command.");

        AutomationRunResult overwriteResult = await new SpriteAutomationEngine(registry).RunAsync(new SpriteAutomationPipeline { Name = "Custom Export Overwrite Guard", RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions, Steps = [exportFormat] }, formatContext);
        Check(!overwriteResult.Success, "Custom format export overwrote an existing file without explicit permission.");
    }

    SpritePackageData invalidPackage = package.DeepClone(); invalidPackage.Document.Frames[0].DurationMilliseconds = 0;
    SpriteAutomationStep validateStep = new() { Id = "validate", Command = "validate-document" };
    SpriteAutomationStep blockedExport = new() { Id = "blocked", Command = "export-png", Arguments = new() { ["path"] = JsonSerializer.SerializeToElement("exports/blocked.png") } };
    AutomationRunResult invalidResult = await new SpriteAutomationEngine().RunAsync(new SpriteAutomationPipeline { Name = "Validation Gate", RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles, Steps = [validateStep, blockedExport] }, new SpriteAutomationContext { Package = invalidPackage, WorkspaceRoot = root, GrantedCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles });
    Check(!invalidResult.Success && !File.Exists(Path.Combine(root, "exports", "blocked.png")), "Validation errors did not stop the pipeline.");

    bool capabilityRejected = false;
    try
    {
        await new SpriteAutomationEngine().RunAsync(new SpriteAutomationPipeline { Name = "Denied", RequiredCapabilities = SpriteAutomationCapability.WriteFiles }, new SpriteAutomationContext { Package = package, WorkspaceRoot = root, GrantedCapabilities = SpriteAutomationCapability.ReadDocument });
    }
    catch (UnauthorizedAccessException) { capabilityRejected = true; }
    Check(capabilityRejected, "Capability guard did not reject write access.");

    Console.WriteLine($"PASS Playroma Sprite Automation contracts ({assertions} assertions)");
    return 0;
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}


sealed class TextSpriteFormatProvider : IPlayromaSpriteFileFormatProvider
{
    public string Id => "test-text";
    public string DisplayName => "Test Text Sprite";
    public IReadOnlyCollection<string> Extensions { get; } = [".sprite.txt"];
    public bool CanRead => true;
    public bool CanWrite => true;

    public async Task<SpritePackageData> ReadAsync(Stream stream, string sourceName, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8, true, 1024, true);
        string value = await reader.ReadToEndAsync(cancellationToken);
        string[] parts = value.Split('|'); string[] size = parts[1].Split('x');
        return SpritePackageData.CreateEmpty(parts[0], int.Parse(size[0]), int.Parse(size[1]));
    }

    public async Task WriteAsync(SpritePackageData package, Stream stream, CancellationToken cancellationToken)
    {
        await using StreamWriter writer = new(stream, System.Text.Encoding.UTF8, 1024, true);
        await writer.WriteAsync($"{package.Document.Name}|{package.Document.Canvas.Width}x{package.Document.Canvas.Height}".AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}


sealed class CountingExtensionCommand : IPlayromaSpriteExtensionCommand
{
    public string Id => "test.count";
    public string DisplayName => "Counting Test Command";
    public SpriteAutomationCapability RequiredCapabilities => SpriteAutomationCapability.ReadDocument;
    public int ExecutionCount { get; private set; }
    public Task ExecuteAsync(SpriteExtensionCommandContext context, IReadOnlyDictionary<string, JsonElement> arguments) { ExecutionCount++; return Task.CompletedTask; }
}
