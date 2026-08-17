using System.Reflection;
using System.Runtime.Loader;
using PlayromaMusic.Core.Extensions;
using PlayromaMusic.Generators.Neural;

namespace PlayromaMusic.Generators.Extensions;

public sealed class GeneratorPluginHost
{
    private readonly object _sync = new();
    private readonly List<IMusicGeneratorPlugin> _generators = new();
    private readonly List<IStylePackSourcePlugin> _styleProviders = new();
    private readonly List<PluginDiagnostic> _diagnostics = new();
    private readonly Dictionary<IMusicGeneratorPlugin, object> _executionGates = new();

    public static GeneratorPluginHost Default { get; } = CreateDefault();

    public IReadOnlyList<IMusicGeneratorPlugin> Generators
    {
        get { lock (_sync) return _generators.ToArray(); }
    }

    public IReadOnlyList<StylePackTextSource> StyleSources
    {
        get
        {
            IStylePackSourcePlugin[] providers;
            lock (_sync) providers = _styleProviders.ToArray();

            List<StylePackTextSource> result = new();
            foreach (IStylePackSourcePlugin provider in providers)
            {
                try
                {
                    IEnumerable<StylePackTextSource> sources = provider.GetStylePacks() ?? Array.Empty<StylePackTextSource>();
                    result.AddRange(sources.Where(source => !string.IsNullOrWhiteSpace(source.Json)));
                }
                catch (Exception exception)
                {
                    AddDiagnostic(new PluginDiagnostic(
                        SafePluginId(provider),
                        "Error",
                        "Style provider failed: " + exception.Message,
                        SourceOf(provider.GetType())));
                }
            }
            return result;
        }
    }

    public IReadOnlyList<PluginDiagnostic> Diagnostics
    {
        get { lock (_sync) return _diagnostics.ToArray(); }
    }

    public void Register(IMusicGeneratorPlugin generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        PluginMetadata metadata = generator.Metadata;
        string source = SourceOf(generator.GetType());
        ValidateMetadata(metadata, source);
        RegisterGenerator(generator, source, "Generator plugin registered programmatically.");
    }

    public void Register(IStylePackSourcePlugin provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        PluginMetadata metadata = provider.Metadata;
        string source = SourceOf(provider.GetType());
        ValidateMetadata(metadata, source);
        RegisterStyleProvider(provider, source, "Style provider registered programmatically.");
    }

    public void Register(INeuralSequenceModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ValidateMetadata(model.Metadata, SourceOf(model.GetType()));
        Register(new NeuralGeneratorAdapter(model));
    }

    public void Reload(IEnumerable<string>? searchPaths = null)
    {
        lock (_sync)
        {
            _generators.Clear();
            _styleProviders.Clear();
            _diagnostics.Clear();
            _executionGates.Clear();
        }

        IEnumerable<string> paths;
        try
        {
            paths = BuildSearchPaths(searchPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception)
        {
            AddDiagnostic(new PluginDiagnostic("host", "Error", "Invalid plugin search path: " + exception.Message, "configuration"));
            return;
        }

        foreach (string directory in paths)
        {
            if (!Directory.Exists(directory)) continue;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                AddDiagnostic(new PluginDiagnostic("host", "Warning", exception.Message, directory));
                continue;
            }

            foreach (string file in files) LoadAssembly(file);
        }
    }

    public bool TryGenerate(GeneratorRequest request, GeneratorContext context, out GeneratorResult result, out string pluginId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        IMusicGeneratorPlugin[] candidates;
        lock (_sync) candidates = _generators.ToArray();
        IMusicGeneratorPlugin? generator = null;
        string selectedPluginId = string.Empty;
        foreach (IMusicGeneratorPlugin candidate in candidates)
        {
            try
            {
                PluginMetadata metadata = candidate.Metadata;
                if (!string.IsNullOrWhiteSpace(request.GeneratorId) &&
                    !metadata.Id.Equals(request.GeneratorId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object gate = GetExecutionGate(candidate);
                bool supported;
                lock (gate) supported = candidate.CanGenerate(request);
                if (!supported) continue;
                generator = candidate;
                selectedPluginId = metadata.Id;
                break;
            }
            catch (Exception exception)
            {
                AddDiagnostic(new PluginDiagnostic(SafePluginId(candidate), "Error", "Plugin capability check failed: " + exception.Message, SourceOf(candidate.GetType())));
            }
        }

        if (generator is null)
        {
            result = new GeneratorResult();
            pluginId = string.Empty;
            return false;
        }

        pluginId = selectedPluginId;
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            lock (GetExecutionGate(generator))
            {
                result = generator.Generate(context) ?? new GeneratorResult();
            }
            context.CancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            result = new GeneratorResult { Diagnostics = { "Plugin generation was cancelled." } };
            return false;
        }
        catch (Exception exception)
        {
            AddDiagnostic(new PluginDiagnostic(selectedPluginId, "Error", exception.ToString(), SourceOf(generator.GetType())));
            result = new GeneratorResult { Diagnostics = { "Plugin failed: " + exception.Message } };
            return false;
        }
    }

    private void LoadAssembly(string file)
    {
        try
        {
            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file));
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
                foreach (Exception? loader in exception.LoaderExceptions)
                {
                    if (loader is not null) AddDiagnostic(new PluginDiagnostic("unknown", "Warning", loader.Message, file));
                }
            }

            foreach (Type type in types.Where(IsDiscoverablePluginType))
            {
                try
                {
                    object instance = Activator.CreateInstance(type)!;
                    bool recognized = false;
                    if (instance is IMusicGeneratorPlugin generator)
                    {
                        ValidateMetadata(generator.Metadata, file);
                        RegisterGenerator(generator, file, "Generator plugin loaded.");
                        recognized = true;
                    }
                    if (instance is IStylePackSourcePlugin provider)
                    {
                        ValidateMetadata(provider.Metadata, file);
                        RegisterStyleProvider(provider, file, "Style provider plugin loaded.");
                        recognized = true;
                    }
                    if (instance is INeuralSequenceModel model)
                    {
                        ValidateMetadata(model.Metadata, file);
                        NeuralGeneratorAdapter adapter = new(model);
                        RegisterGenerator(adapter, file, "Optional neural model loaded through the common generator interface.");
                        recognized = true;
                    }
                    if (!recognized)
                    {
                        AddDiagnostic(new PluginDiagnostic(type.FullName ?? type.Name, "Warning", "Discoverable type did not implement a supported extension contract.", file));
                    }
                }
                catch (Exception exception)
                {
                    AddDiagnostic(new PluginDiagnostic(type.FullName ?? type.Name, "Error", "Plugin type failed to load: " + exception.Message, file));
                }
            }
        }
        catch (Exception exception)
        {
            AddDiagnostic(new PluginDiagnostic(Path.GetFileNameWithoutExtension(file), "Error", exception.Message, file));
        }
    }

    private static bool IsDiscoverablePluginType(Type type) =>
        !type.IsAbstract &&
        !type.IsInterface &&
        type.GetConstructor(Type.EmptyTypes) is not null &&
        (typeof(IMusicGeneratorPlugin).IsAssignableFrom(type) ||
         typeof(IStylePackSourcePlugin).IsAssignableFrom(type) ||
         typeof(INeuralSequenceModel).IsAssignableFrom(type));

    private void RegisterGenerator(IMusicGeneratorPlugin generator, string source, string message)
    {
        string id = generator.Metadata.Id;
        IMusicGeneratorPlugin[] snapshot;
        lock (_sync) snapshot = _generators.ToArray();
        IMusicGeneratorPlugin[] replaced = snapshot
            .Where(item => SafePluginId(item).Equals(id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        lock (_sync)
        {
            _generators.RemoveAll(item => replaced.Contains(item));
            foreach (IMusicGeneratorPlugin item in replaced) _executionGates.Remove(item);
            _generators.Add(generator);
            _executionGates[generator] = new object();
            _diagnostics.Add(new PluginDiagnostic(id, "Information", message, source));
        }
    }

    private void RegisterStyleProvider(IStylePackSourcePlugin provider, string source, string message)
    {
        string id = provider.Metadata.Id;
        IStylePackSourcePlugin[] snapshot;
        lock (_sync) snapshot = _styleProviders.ToArray();
        IStylePackSourcePlugin[] replaced = snapshot
            .Where(item => SafePluginId(item).Equals(id, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        lock (_sync)
        {
            _styleProviders.RemoveAll(item => replaced.Contains(item));
            _styleProviders.Add(provider);
            _diagnostics.Add(new PluginDiagnostic(id, "Information", message, source));
        }
    }

    private void AddDiagnostic(PluginDiagnostic diagnostic)
    {
        lock (_sync) _diagnostics.Add(diagnostic);
    }

    private object GetExecutionGate(IMusicGeneratorPlugin generator)
    {
        lock (_sync)
        {
            if (!_executionGates.TryGetValue(generator, out object? gate))
            {
                gate = new object();
                _executionGates[generator] = gate;
            }
            return gate;
        }
    }

    private static string SafePluginId(object plugin)
    {
        try
        {
            return plugin switch
            {
                IMusicGeneratorPlugin generator => generator.Metadata.Id,
                IStylePackSourcePlugin provider => provider.Metadata.Id,
                INeuralSequenceModel model => model.Metadata.Id,
                _ => plugin.GetType().FullName ?? plugin.GetType().Name
            };
        }
        catch
        {
            return plugin.GetType().FullName ?? plugin.GetType().Name;
        }
    }

    private static void ValidateMetadata(PluginMetadata metadata, string source)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.Id)) throw new InvalidDataException($"Plugin in {source} has an empty ID.");
        if (metadata.ApiVersion != 1) throw new NotSupportedException($"Plugin {metadata.Id} targets API {metadata.ApiVersion}; this host supports API 1.");
    }

    private static IEnumerable<string> BuildSearchPaths(IEnumerable<string>? additional)
    {
        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Plugins"));
        yield return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Plugins"));
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlayromaMusic", "Plugins");
        if (additional is not null)
        {
            foreach (string path in additional.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                yield return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            }
        }
    }

    private static string SourceOf(Type type) =>
        string.IsNullOrWhiteSpace(type.Assembly.Location) ? type.Assembly.GetName().Name ?? "in-memory" : type.Assembly.Location;

    private static GeneratorPluginHost CreateDefault()
    {
        GeneratorPluginHost host = new();
        host.Reload();
        return host;
    }
}
