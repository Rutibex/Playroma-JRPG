using System.Text.Json;
using Playroma.Sprite.Animation;
using Playroma.Sprite.Core;
using Playroma.Sprite.Formats;
using Playroma.Sprite.Pixel;
using Playroma.Sprite.RpgMakerMV;

namespace Playroma.Sprite.Automation;

public sealed class SpriteAutomationEngine
{
    private readonly SpriteExtensionRegistry? extensions;
    private readonly SpritePackageSerializer serializer = new();

    public SpriteAutomationEngine(SpriteExtensionRegistry? extensions = null) => this.extensions = extensions;

    public async Task<AutomationRunResult> RunAsync(SpriteAutomationPipeline pipeline, SpriteAutomationContext context)
    {
        ArgumentNullException.ThrowIfNull(pipeline); ArgumentNullException.ThrowIfNull(context);
        if (pipeline.Schema != "playroma-sprite-pipeline/1") throw new InvalidDataException("Unsupported automation pipeline schema.");
        CapabilityGuard.Demand(context, pipeline.RequiredCapabilities, pipeline.Name);
        AutomationRunResult result = new() { Name = pipeline.Name, DryRun = context.DryRun };
        SafeAutomationPaths paths = new(context.WorkspaceRoot);
        context.Variables["package"] = context.PackagePath ?? string.Empty;
        context.Log?.Invoke($"Running {pipeline.Name}{(context.DryRun ? " (dry run)" : string.Empty)}.");
        foreach (SpriteAutomationStep step in pipeline.Steps.Where(item => item.Enabled))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ExecuteStepAsync(step, context, paths, result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result.Diagnostics.Add(new(AutomationDiagnosticSeverity.Error, "PSPR-AUTO-STEP", exception.Message, step.Id));
                context.Log?.Invoke($"ERROR {step.Id}: {exception.Message}");
                if (!step.ContinueOnError && !pipeline.ContinueOnError) break;
            }
        }
        result.FinishedAt = DateTimeOffset.UtcNow;
        return result;
    }

    public Task<AutomationRunResult> RunScriptAsync(PlayromaSpriteScript script, SpriteAutomationContext context, IReadOnlyDictionary<string, string>? parameters = null)
    {
        CapabilityGuard.Demand(context, script.RequiredCapabilities, script.Name);
        if (parameters is not null) foreach ((string key, string value) in parameters) context.Variables[key] = value;
        return RunAsync(script.CreatePipeline(parameters), context);
    }

    public async Task<AutomationRunResult> RunPresetAsync(SpriteExportPreset preset, SpriteAutomationContext context)
    {
        ExportPresetStore.Validate(preset);
        SpriteAutomationStep step = new() { Id = "export", Command = preset.Format.Equals("atlas", StringComparison.OrdinalIgnoreCase) ? "export-atlas" : "export-preset" };
        step.Arguments["preset"] = JsonSerializer.SerializeToElement(preset, AutomationJson.Options);
        return await RunAsync(new SpriteAutomationPipeline
        {
            Name = preset.Name,
            RequiredCapabilities = SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles,
            Steps = [step]
        }, context);
    }

    private async Task ExecuteStepAsync(SpriteAutomationStep step, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result)
    {
        string command = step.Command.Trim().ToLowerInvariant();
        context.Log?.Invoke($"> {step.Id}: {command}");
        switch (command)
        {
            case "message":
                result.Diagnostics.Add(new(AutomationDiagnosticSeverity.Information, "PSPR-AUTO-MESSAGE", GetString(step, "text", step.Name ?? ""), step.Id));
                return;
            case "validate-document":
                CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadDocument, command);
                IReadOnlyList<SpriteValidationIssue> diagnostics = SpriteDocumentValidator.Validate(context.Package.Document);
                foreach (SpriteValidationIssue diagnostic in diagnostics)
                    result.Diagnostics.Add(new(diagnostic.Severity.Equals("error", StringComparison.OrdinalIgnoreCase) ? AutomationDiagnosticSeverity.Error : AutomationDiagnosticSeverity.Warning, diagnostic.Code, diagnostic.Message, step.Id));
                if (diagnostics.Count == 0) result.Diagnostics.Add(new(AutomationDiagnosticSeverity.Information, "PSPR-AUTO-VALID", "Document validation passed.", step.Id));
                if (diagnostics.Any(item => item.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) && GetBool(step, "failOnError", true))
                    throw new InvalidDataException("Document validation failed; later pipeline steps were not executed.");
                return;
            case "set-variable":
                context.Variables[GetRequiredString(step, "name")] = AutomationVariables.Expand(GetRequiredString(step, "value"), context); return;
            case "set-metadata":
                CapabilityGuard.Demand(context, SpriteAutomationCapability.ModifyDocument, command);
                string metadataKey = GetRequiredString(step, "key"), metadataValue = AutomationVariables.Expand(GetRequiredString(step, "value"), context);
                if (!context.DryRun) context.Package.Document.Metadata.UserData[metadataKey] = metadataValue;
                result.Operations.Add(new("modify", $"Set document metadata '{metadataKey}'.", Performed: !context.DryRun)); return;
            case "effect":
                CapabilityGuard.Demand(context, SpriteAutomationCapability.ModifyDocument, command);
                ApplyEffect(context.Package, GetRequiredString(step, "name"), GetDouble(step, "amount", 1d), context.DryRun);
                result.Operations.Add(new("modify", $"Applied {GetRequiredString(step, "name")} to all unique pixel contents.", Performed: !context.DryRun)); return;
            case "convert-mode":
                CapabilityGuard.Demand(context, SpriteAutomationCapability.ModifyDocument, command);
                if (!context.DryRun) ConvertMode(context.Package, GetRequiredString(step, "mode"), GetInt(step, "colors", 256), GetString(step, "dither", "none"));
                result.Operations.Add(new("modify", $"Converted document to {GetRequiredString(step, "mode")}.", Performed: !context.DryRun)); return;
            case "palette-generate":
                CapabilityGuard.Demand(context, SpriteAutomationCapability.ModifyDocument, command);
                if (!context.DryRun) PaletteDocumentOperations.GeneratePalette(context.Package, GetString(step, "name", "Generated Palette"), GetInt(step, "colors", 256));
                result.Operations.Add(new("modify", "Generated document palette.", Performed: !context.DryRun)); return;
            case "save-package":
                CapabilityGuard.Demand(context, SpriteAutomationCapability.WriteFiles, command);
                string packagePath = paths.ResolveWrite(ExpandPath(step, "path", context, context.PackagePath ?? "${output}/${document}.playromasprite"));
                if (!context.DryRun) { Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!); await serializer.SaveAsync(context.Package, packagePath, context.CancellationToken); context.PackagePath = packagePath; }
                result.Operations.Add(new("write", "Saved editable sprite package.", DestinationPath: packagePath, Performed: !context.DryRun)); result.Outputs[step.Id] = packagePath; return;
            case "export-png":
                await ExportPngAsync(step, context, paths, result); return;
            case "export-sequence":
                await ExportSequenceAsync(step, context, paths, result); return;
            case "export-gif":
                await ExportGifAsync(step, context, paths, result); return;
            case "export-atlas":
                await ExportAtlasAsync(step, context, paths, result); return;
            case "export-preset":
                SpriteExportPreset preset = GetPreset(step); await ExecutePresetAsync(preset, context, paths, result, step.Id); return;
            case "install-mv":
                InstallMv(step, context, result); return;
            case "import-format":
                await ImportCustomFormatAsync(step, context, paths, result); return;
            case "export-format":
                await ExportCustomFormatAsync(step, context, paths, result); return;
            case "extension":
                await RunExtensionAsync(step, context, result); return;
            default:
                if (extensions?.Commands.FirstOrDefault(item => item.Id.Equals(step.Command, StringComparison.OrdinalIgnoreCase)) is IPlayromaSpriteExtensionCommand extensionCommand)
                {
                    CapabilityGuard.Demand(context, extensionCommand.RequiredCapabilities, extensionCommand.Id);
                    if (context.DryRun)
                    {
                        result.Operations.Add(new("extension", $"Would run extension command {extensionCommand.Id}.", Performed: false));
                        return;
                    }
                    await extensionCommand.ExecuteAsync(new(context, result), step.Arguments);
                    return;
                }
                throw new InvalidDataException($"Unknown automation command '{step.Command}'.");
        }
    }

    private static async Task ExportPngAsync(SpriteAutomationStep step, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles, "export-png");
        int frame = Math.Clamp(GetInt(step, "frame", 0), 0, context.Package.Document.Frames.Count - 1);
        string path = paths.ResolveWrite(ExpandPath(step, "path", context, "${output}/${document}.png"));
        if (!context.DryRun) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); PngCodec.Save(AnimationFrameRenderer.RenderFrame(context.Package, frame), path); }
        result.Operations.Add(new("write", $"Exported frame {frame} as PNG.", DestinationPath: path, Performed: !context.DryRun)); result.Outputs[step.Id] = path; await Task.CompletedTask;
    }

    private static async Task ExportSequenceAsync(SpriteAutomationStep step, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles, "export-sequence");
        string directory = paths.ResolveWrite(ExpandPath(step, "directory", context, "${output}/${document}-frames")); string name = GetString(step, "baseName", AutomationVariables.Sanitize(context.Package.Document.Name));
        if (!context.DryRun)
        {
            Directory.CreateDirectory(directory);
            AnimationFrameImage[] frames = context.Package.Document.Frames.Select((frame, index) => new AnimationFrameImage(AnimationFrameRenderer.RenderFrame(context.Package, index), frame.DurationMilliseconds)).ToArray();
            AnimationImageSequence.Save(frames, directory, name);
        }
        result.Operations.Add(new("write", $"Exported {context.Package.Document.Frames.Count} PNG sequence frames.", DestinationPath: directory, Performed: !context.DryRun)); result.Outputs[step.Id] = directory; await Task.CompletedTask;
    }

    private static async Task ExportGifAsync(SpriteAutomationStep step, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles, "export-gif");
        string path = paths.ResolveWrite(ExpandPath(step, "path", context, "${output}/${document}.gif"));
        if (!context.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AnimationFrameImage[] frames = context.Package.Document.Frames.Select((frame, index) => new AnimationFrameImage(AnimationFrameRenderer.RenderFrame(context.Package, index), frame.DurationMilliseconds)).ToArray();
            GifCodec.Save(frames, path, GetInt(step, "loopCount", 0));
        }
        result.Operations.Add(new("write", "Exported animated GIF.", DestinationPath: path, Performed: !context.DryRun)); result.Outputs[step.Id] = path; await Task.CompletedTask;
    }

    private static async Task ExportAtlasAsync(SpriteAutomationStep step, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles, "export-atlas");
        SpriteExportPreset preset = step.Arguments.TryGetValue("preset", out JsonElement presetElement)
            ? presetElement.Deserialize<SpriteExportPreset>(AutomationJson.Options) ?? new SpriteExportPreset { Format = "atlas" }
            : new SpriteExportPreset
            {
                Name = step.Name ?? "Atlas Export", Format = "atlas", OutputPath = GetString(step, "path", "${output}/${document}-atlas.png"),
                Scale = GetInt(step, "scale", 1), Padding = GetInt(step, "padding", 0), Extrude = GetBool(step, "extrude", false),
                PowerOfTwo = GetBool(step, "powerOfTwo", false), TrimTransparent = GetBool(step, "trim", false),
                MaximumWidth = GetInt(step, "maximumWidth", 4096), MaximumHeight = GetInt(step, "maximumHeight", 4096)
            };
        await ExecutePresetAsync(preset, context, paths, result, step.Id);
    }

    private static async Task ExecutePresetAsync(SpriteExportPreset preset, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result, string stepId)
    {
        string path = paths.ResolveWrite(AutomationVariables.Expand(preset.OutputPath, context));
        string format = preset.Format.ToLowerInvariant();
        int[] frameIndexes = ResolvePresetFrameIndexes(context.Package, preset, context);
        if (format == "atlas")
        {
            SpriteAtlasResult atlas = SpriteAtlasPacker.Pack(context.Package, preset, frameIndexes);
            string metadata = Path.ChangeExtension(path, ".json");
            if (!context.DryRun) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); PngCodec.Save(atlas.Image, path); File.WriteAllText(metadata, atlas.MetadataJson); }
            result.Operations.Add(new("write", $"Exported {atlas.Frames.Count} atlas frames.", DestinationPath: path, Performed: !context.DryRun));
            result.Operations.Add(new("write", "Exported atlas metadata.", DestinationPath: metadata, Performed: !context.DryRun)); result.Outputs[stepId] = path;
        }
        else if (format == "png")
        {
            int frame = frameIndexes[0];
            if (!context.DryRun) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); PngCodec.Save(AnimationFrameRenderer.RenderFrame(context.Package, frame), path); }
            result.Operations.Add(new("write", $"Exported frame {frame} as PNG.", DestinationPath: path, Performed: !context.DryRun)); result.Outputs[stepId] = path;
        }
        else if (format == "gif")
        {
            if (!context.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                AnimationFrameImage[] frames = frameIndexes.Select(index => new AnimationFrameImage(AnimationFrameRenderer.RenderFrame(context.Package, index), context.Package.Document.Frames[index].DurationMilliseconds)).ToArray();
                GifCodec.Save(frames, path);
            }
            result.Operations.Add(new("write", $"Exported {frameIndexes.Length} GIF frames.", DestinationPath: path, Performed: !context.DryRun)); result.Outputs[stepId] = path;
        }
        else if (format is "sequence" or "png-sequence")
        {
            if (!context.DryRun)
            {
                Directory.CreateDirectory(path);
                AnimationFrameImage[] frames = frameIndexes.Select(index => new AnimationFrameImage(AnimationFrameRenderer.RenderFrame(context.Package, index), context.Package.Document.Frames[index].DurationMilliseconds)).ToArray();
                AnimationImageSequence.Save(frames, path, AutomationVariables.Sanitize(context.Package.Document.Name));
            }
            result.Operations.Add(new("write", $"Exported {frameIndexes.Length} PNG sequence frames.", DestinationPath: path, Performed: !context.DryRun)); result.Outputs[stepId] = path;
        }
        else throw new InvalidDataException($"Unsupported export preset format '{preset.Format}'.");
        await Task.CompletedTask;
    }

    private static int[] ResolvePresetFrameIndexes(SpritePackageData package, SpriteExportPreset preset, SpriteAutomationContext context)
    {
        int count = package.Document.Frames.Count;
        if (count == 0) throw new InvalidDataException("The document has no frames.");
        return preset.FrameMode switch
        {
            ExportFrameMode.All => Enumerable.Range(0, count).ToArray(),
            ExportFrameMode.Tag => ResolveTagFrames(package, preset.Tag),
            ExportFrameMode.Selected => ResolveSelectedFrames(context, count),
            _ => [Math.Clamp(context.Variables.TryGetValue("frame", out string? value) && int.TryParse(value, out int frame) ? frame : 0, 0, count - 1)]
        };
    }

    private static int[] ResolveTagFrames(SpritePackageData package, string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) throw new InvalidDataException("Tag frame mode requires a tag name.");
        SpriteAnimationTag tag = package.Document.Tags.FirstOrDefault(item => item.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Animation tag '{tagName}' was not found.");
        int from = Math.Clamp(Math.Min(tag.FromFrame, tag.ToFrame), 0, package.Document.Frames.Count - 1);
        int to = Math.Clamp(Math.Max(tag.FromFrame, tag.ToFrame), 0, package.Document.Frames.Count - 1);
        return Enumerable.Range(from, to - from + 1).ToArray();
    }

    private static int[] ResolveSelectedFrames(SpriteAutomationContext context, int count)
    {
        if (!context.Variables.TryGetValue("selectedFrames", out string? value) || string.IsNullOrWhiteSpace(value)) return [0];
        int[] indexes = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse).Where(index => index >= 0 && index < count).Distinct().Order().ToArray();
        return indexes.Length == 0 ? [0] : indexes;
    }

    private static void InstallMv(SpriteAutomationStep step, SpriteAutomationContext context, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.InstallRuntimeAsset, "install-mv");
        string projectRoot = AutomationVariables.Expand(GetRequiredString(step, "projectRoot"), context);
        string profileId = GetRequiredString(step, "profileId"), fileName = AutomationVariables.Expand(GetRequiredString(step, "fileName"), context);
        RpgMakerMvImageProfile profile = RpgMakerMvProfileRegistry.LoadBuiltIn().GetRequired(profileId);
        PixelBuffer pixels = AnimationFrameRenderer.RenderFrame(context.Package, GetInt(step, "frame", 0));
        if (!context.DryRun)
        {
            MvRuntimeInstallResult installed = MvRuntimeInstaller.Install(new(projectRoot, profile, fileName, pixels)); result.Outputs[step.Id] = installed.FullPath;
            result.Operations.Add(new("install", $"Installed {installed.RuntimeRelativePath}.", DestinationPath: installed.FullPath));
        }
        else result.Operations.Add(new("install", $"Would install {profile.AssetFolder}/{fileName}.", DestinationPath: Path.Combine(projectRoot, profile.AssetFolder, fileName), Performed: false));
    }


    private async Task ImportCustomFormatAsync(SpriteAutomationStep step, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadFiles | SpriteAutomationCapability.ModifyDocument | SpriteAutomationCapability.LoadExtensions, "import-format");
        if (extensions is null) throw new InvalidOperationException("No extension registry is active.");
        string providerId = GetRequiredString(step, "provider");
        IPlayromaSpriteFileFormatProvider provider = extensions.FormatProviders.FirstOrDefault(item => item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Extension format provider '{providerId}' is not registered.");
        if (!provider.CanRead) throw new InvalidOperationException($"Format provider '{providerId}' does not support reading.");
        string source = paths.ResolveRead(ExpandPath(step, "path", context, string.Empty));
        if (!context.DryRun)
        {
            await using FileStream stream = new(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            SpritePackageData imported = await provider.ReadAsync(stream, Path.GetFileName(source), context.CancellationToken);
            SpriteDocumentValidator.ThrowIfInvalid(imported.Document); context.Package = imported;
        }
        result.Operations.Add(new("read", $"Imported {provider.DisplayName} document.", SourcePath: source, Performed: !context.DryRun));
    }

    private async Task ExportCustomFormatAsync(SpriteAutomationStep step, SpriteAutomationContext context, SafeAutomationPaths paths, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.ReadDocument | SpriteAutomationCapability.WriteFiles | SpriteAutomationCapability.LoadExtensions, "export-format");
        if (extensions is null) throw new InvalidOperationException("No extension registry is active.");
        string providerId = GetRequiredString(step, "provider");
        IPlayromaSpriteFileFormatProvider provider = extensions.FormatProviders.FirstOrDefault(item => item.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Extension format provider '{providerId}' is not registered.");
        if (!provider.CanWrite) throw new InvalidOperationException($"Format provider '{providerId}' does not support writing.");
        string destination = paths.ResolveWrite(ExpandPath(step, "path", context, "${output}/${document}" + provider.Extensions.FirstOrDefault()));
        bool overwrite = GetBool(step, "overwrite", false);
        if (!context.DryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination) && !overwrite) throw new IOException($"Custom-format output already exists: {destination}");
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await provider.WriteAsync(context.Package, stream, context.CancellationToken);
                }
                File.Move(temporary, destination, overwrite);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        result.Operations.Add(new("write", $"Exported {provider.DisplayName} document.", DestinationPath: destination, Performed: !context.DryRun)); result.Outputs[step.Id] = destination;
    }

    private async Task RunExtensionAsync(SpriteAutomationStep step, SpriteAutomationContext context, AutomationRunResult result)
    {
        CapabilityGuard.Demand(context, SpriteAutomationCapability.LoadExtensions, "extension");
        if (extensions is null) throw new InvalidOperationException("No extension registry is active.");
        string id = GetRequiredString(step, "command");
        IPlayromaSpriteExtensionCommand command = extensions.Commands.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Extension command '{id}' is not registered.");
        CapabilityGuard.Demand(context, command.RequiredCapabilities, id);
        if (context.DryRun) { result.Operations.Add(new("extension", $"Would run extension command {id}.", Performed: false)); return; }
        await command.ExecuteAsync(new(context, result), step.Arguments);
    }

    private static void ApplyEffect(SpritePackageData package, string name, double amount, bool dryRun)
    {
        if (dryRun) return;
        foreach (PixelBuffer buffer in package.PixelBuffers.Values.Distinct()) switch (name.ToLowerInvariant())
        {
            case "invert": AdvancedPixelEffects.Invert(buffer); break;
            case "grayscale" or "desaturate": AdvancedPixelEffects.ConvertToGrayscale(buffer); break;
            case "blur": AdvancedPixelEffects.GaussianBlur(buffer, Math.Max(1, (int)Math.Round(amount))); break;
            case "sharpen": AdvancedPixelEffects.Sharpen(buffer, amount); break;
            case "pixelate": AdvancedPixelEffects.Pixelate(buffer, Math.Max(2, (int)Math.Round(amount))); break;
            case "alpha-cleanup": AdvancedPixelEffects.AlphaThreshold(buffer, (byte)Math.Clamp((int)Math.Round(amount), 0, 255)); break;
            default: throw new InvalidDataException($"Unsupported automation effect '{name}'.");
        }
    }

    private static void ConvertMode(SpritePackageData package, string mode, int colors, string dither)
    {
        switch (mode.ToLowerInvariant())
        {
            case "rgba": PaletteDocumentOperations.ConvertToRgba(package); break;
            case "grayscale": PaletteDocumentOperations.ConvertToGrayscale(package); break;
            case "indexed":
                SpritePalette palette = package.Document.ActivePaletteId is Guid id
                    ? package.Document.Palettes.First(item => item.Id == id)
                    : PaletteDocumentOperations.GeneratePalette(package, $"{colors}-Color Palette", Math.Clamp(colors, 2, 256));
                PaletteDocumentOperations.ApplyPalette(package, palette, dither.ToLowerInvariant() switch { "ordered" => PaletteDitherMode.OrderedBayer4x4, "floyd" => PaletteDitherMode.FloydSteinberg, _ => PaletteDitherMode.None }); break;
            default: throw new InvalidDataException($"Unknown color mode '{mode}'.");
        }
    }

    private static SpriteExportPreset GetPreset(SpriteAutomationStep step) => step.Arguments.TryGetValue("preset", out JsonElement element)
        ? element.Deserialize<SpriteExportPreset>(AutomationJson.Options) ?? throw new InvalidDataException("Export preset is empty.")
        : throw new InvalidDataException("The export-preset step requires a preset object.");
    private static string ExpandPath(SpriteAutomationStep step, string name, SpriteAutomationContext context, string fallback) => AutomationVariables.Expand(GetString(step, name, fallback), context);
    private static string GetRequiredString(SpriteAutomationStep step, string name) => GetString(step, name, null) ?? throw new InvalidDataException($"Step '{step.Id}' requires argument '{name}'.");
    private static string? GetString(SpriteAutomationStep step, string name, string? fallback) => step.Arguments.TryGetValue(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : fallback;
    private static int GetInt(SpriteAutomationStep step, string name, int fallback) => step.Arguments.TryGetValue(name, out JsonElement value) && value.TryGetInt32(out int parsed) ? parsed : fallback;
    private static double GetDouble(SpriteAutomationStep step, string name, double fallback) => step.Arguments.TryGetValue(name, out JsonElement value) && value.TryGetDouble(out double parsed) ? parsed : fallback;
    private static bool GetBool(SpriteAutomationStep step, string name, bool fallback) => step.Arguments.TryGetValue(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
}
