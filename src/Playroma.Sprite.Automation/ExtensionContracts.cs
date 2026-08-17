using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Playroma.Sprite.Formats;

namespace Playroma.Sprite.Automation;

public sealed class SpriteExtensionManifest
{
    public string Schema { get; set; } = "playroma-sprite-extension/1";
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public string? Author { get; set; }
    public string EntryAssembly { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public SpriteAutomationCapability RequestedCapabilities { get; set; } = SpriteAutomationCapability.ReadDocument;
    public List<string> Commands { get; set; } = [];
    public List<string> FormatProviders { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record SpriteExtensionDescriptor(string DirectoryPath, string ManifestPath, SpriteExtensionManifest Manifest, bool Trusted, bool Enabled, string? Error = null);

public interface IPlayromaSpriteExtension : IDisposable
{
    void Initialize(IPlayromaSpriteExtensionHost host);
}

public interface IPlayromaSpriteExtensionHost
{
    SpriteAutomationCapability GrantedCapabilities { get; }
    void RegisterCommand(IPlayromaSpriteExtensionCommand command);
    void RegisterFormatProvider(IPlayromaSpriteFileFormatProvider provider);
    void Log(string message);
}

public interface IPlayromaSpriteExtensionCommand
{
    string Id { get; }
    string DisplayName { get; }
    SpriteAutomationCapability RequiredCapabilities { get; }
    Task ExecuteAsync(SpriteExtensionCommandContext context, IReadOnlyDictionary<string, JsonElement> arguments);
}

public sealed record SpriteExtensionCommandContext(SpriteAutomationContext Automation, AutomationRunResult Result);

public interface IPlayromaSpriteFileFormatProvider
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyCollection<string> Extensions { get; }
    bool CanRead { get; }
    bool CanWrite { get; }
    Task<SpritePackageData> ReadAsync(Stream stream, string sourceName, CancellationToken cancellationToken);
    Task WriteAsync(SpritePackageData package, Stream stream, CancellationToken cancellationToken);
}

public sealed class SpriteExtensionRegistry : IPlayromaSpriteExtensionHost, IDisposable
{
    private readonly Dictionary<string, IPlayromaSpriteExtensionCommand> commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IPlayromaSpriteFileFormatProvider> formats = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IPlayromaSpriteExtension> loaded = [];
    private readonly List<AssemblyLoadContext> loadContexts = [];
    private readonly Action<string>? logger;

    public SpriteExtensionRegistry(SpriteAutomationCapability grantedCapabilities, Action<string>? logger = null)
    { GrantedCapabilities = grantedCapabilities; this.logger = logger; }

    public SpriteAutomationCapability GrantedCapabilities { get; }
    public IReadOnlyCollection<IPlayromaSpriteExtensionCommand> Commands => commands.Values;
    public IReadOnlyCollection<IPlayromaSpriteFileFormatProvider> FormatProviders => formats.Values;

    public void RegisterCommand(IPlayromaSpriteExtensionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if ((GrantedCapabilities & command.RequiredCapabilities) != command.RequiredCapabilities)
            throw new UnauthorizedAccessException($"Extension command '{command.Id}' requests capabilities that were not granted.");
        if (!commands.TryAdd(command.Id, command)) throw new InvalidOperationException($"Extension command '{command.Id}' is already registered.");
    }

    public void RegisterFormatProvider(IPlayromaSpriteFileFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!formats.TryAdd(provider.Id, provider)) throw new InvalidOperationException($"Extension format provider '{provider.Id}' is already registered.");
    }

    public void Log(string message) => logger?.Invoke(message);

    public void Load(SpriteExtensionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.Enabled || !descriptor.Trusted) throw new UnauthorizedAccessException("Extensions must be explicitly enabled and trusted before loading.");
        SpriteExtensionManifest manifest = descriptor.Manifest;
        if ((GrantedCapabilities & manifest.RequestedCapabilities) != manifest.RequestedCapabilities)
            throw new UnauthorizedAccessException($"Extension '{manifest.Id}' requests capabilities that are not granted.");
        string assemblyPath = Path.GetFullPath(Path.Combine(descriptor.DirectoryPath, manifest.EntryAssembly));
        string root = Path.GetFullPath(descriptor.DirectoryPath) + Path.DirectorySeparatorChar;
        if (!assemblyPath.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Extension entry assembly escapes its extension directory.");
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Extension entry assembly not found.", assemblyPath);
        AssemblyLoadContext context = new($"PlayromaSpriteExtension:{manifest.Id}:{manifest.Version}", isCollectible: true);
        Assembly assembly = context.LoadFromAssemblyPath(assemblyPath);
        Type type = assembly.GetType(manifest.EntryType, throwOnError: true, ignoreCase: false)!;
        if (Activator.CreateInstance(type) is not IPlayromaSpriteExtension extension)
            throw new InvalidDataException($"Extension entry type '{manifest.EntryType}' does not implement IPlayromaSpriteExtension.");
        extension.Initialize(this); loaded.Add(extension); loadContexts.Add(context); Log($"Loaded extension {manifest.Name} {manifest.Version}.");
    }

    public void Dispose()
    {
        foreach (IPlayromaSpriteExtension extension in loaded.AsEnumerable().Reverse()) extension.Dispose();
        loaded.Clear(); commands.Clear(); formats.Clear();
        foreach (AssemblyLoadContext context in loadContexts) context.Unload();
        loadContexts.Clear();
    }
}

public static class SpriteExtensionCatalog
{
    public static IReadOnlyList<SpriteExtensionDescriptor> Discover(string root, ISet<string>? trusted = null, ISet<string>? enabled = null)
    {
        trusted ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase); enabled ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return [];
        List<SpriteExtensionDescriptor> result = [];
        foreach (string manifestPath in Directory.EnumerateFiles(root, "playroma-sprite-extension.json", SearchOption.AllDirectories))
        {
            try
            {
                SpriteExtensionManifest manifest = JsonSerializer.Deserialize<SpriteExtensionManifest>(File.ReadAllText(manifestPath), AutomationJson.Options)
                    ?? throw new InvalidDataException("Extension manifest is empty.");
                ValidateManifest(manifest);
                result.Add(new(Path.GetDirectoryName(manifestPath)!, manifestPath, manifest, trusted.Contains(manifest.Id), enabled.Contains(manifest.Id)));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
            {
                result.Add(new(Path.GetDirectoryName(manifestPath)!, manifestPath, new SpriteExtensionManifest { Id = Path.GetFileName(Path.GetDirectoryName(manifestPath)), Name = "Invalid extension" }, false, false, exception.Message));
            }
        }
        return result.OrderBy(item => item.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static void ValidateManifest(SpriteExtensionManifest manifest)
    {
        if (manifest.Schema != "playroma-sprite-extension/1") throw new InvalidDataException("Unsupported extension manifest schema.");
        if (string.IsNullOrWhiteSpace(manifest.Id) || manifest.Id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')))
            throw new InvalidDataException("Extension IDs may contain only letters, digits, dots, dashes, and underscores.");
        if (string.IsNullOrWhiteSpace(manifest.Name)) throw new InvalidDataException("Extension name is required.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || Path.IsPathRooted(manifest.EntryAssembly)) throw new InvalidDataException("Extension entryAssembly must be relative.");
        if (string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidDataException("Extension entryType is required.");
    }
}

public sealed class SpriteExtensionSettings
{
    public HashSet<string> Trusted { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Enabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SpriteExtensionSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        return JsonSerializer.Deserialize<SpriteExtensionSettings>(File.ReadAllText(path), AutomationJson.Options) ?? new();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(this, AutomationJson.Options)); File.Move(temporary, path, true);
    }
}
