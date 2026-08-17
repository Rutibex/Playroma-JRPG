using System.Reflection;
using System.Text.Json;
using Playroma.JRPG.Core;

namespace Playroma.JRPG.Specifications;

public sealed class MvSpecificationCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private MvSpecificationCatalog(
        IReadOnlyList<EventCommandSpecification> eventCommands,
        IReadOnlyList<MovementRouteCommandSpecification> movementRouteCommands,
        IReadOnlyList<DatabaseFileSpecification> databaseFiles)
    {
        EventCommands = eventCommands;
        MovementRouteCommands = movementRouteCommands;
        DatabaseFiles = databaseFiles;
    }

    public IReadOnlyList<EventCommandSpecification> EventCommands { get; }

    public IReadOnlyList<MovementRouteCommandSpecification> MovementRouteCommands { get; }

    public IReadOnlyList<DatabaseFileSpecification> DatabaseFiles { get; }

    public static MvSpecificationCatalog Load(string? repositoryRoot = null)
    {
        string? specificationRoot = ResolveSpecificationRoot(repositoryRoot);
        EventCommandFile eventFile = specificationRoot is null
            ? DeserializeEmbedded<EventCommandFile>("mv-event-commands.json")
            : DeserializeFile<EventCommandFile>(Path.Combine(specificationRoot, "mv-event-commands.json"));
        MovementRouteCommandFile routeFile = specificationRoot is null
            ? DeserializeEmbedded<MovementRouteCommandFile>("mv-movement-route-commands.json")
            : DeserializeFile<MovementRouteCommandFile>(Path.Combine(specificationRoot, "mv-movement-route-commands.json"));
        DatabaseFile databaseFile = specificationRoot is null
            ? DeserializeEmbedded<DatabaseFile>("mv-database-files.json")
            : DeserializeFile<DatabaseFile>(Path.Combine(specificationRoot, "mv-database-files.json"));

        return new MvSpecificationCatalog(
            eventFile.Commands,
            routeFile.Commands,
            databaseFile.Files);
    }

    private static string? ResolveSpecificationRoot(string? repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            string explicitRoot = Path.Combine(repositoryRoot, "specifications");
            return Directory.Exists(explicitRoot) ? explicitRoot : null;
        }

        try
        {
            string discovered = Path.Combine(RepositoryRootLocator.Find(), "specifications");
            return Directory.Exists(discovered) ? discovered : null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static T DeserializeFile<T>(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Deserialize<T>(stream, path);
    }

    private static T DeserializeEmbedded<T>(string fileName)
    {
        Assembly assembly = typeof(MvSpecificationCatalog).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Embedded MV specification was not found: {fileName}");
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded MV specification could not be opened: {resourceName}");
        return Deserialize<T>(stream, resourceName);
    }

    private static T Deserialize<T>(Stream stream, string sourceName)
    {
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Specification deserialized to null: {sourceName}");
    }

    private sealed record EventCommandFile(
        int SchemaVersion,
        string RuntimeFamily,
        List<EventCommandSpecification> Commands);

    private sealed record MovementRouteCommandFile(
        int SchemaVersion,
        List<MovementRouteCommandSpecification> Commands);

    private sealed record DatabaseFile(
        int SchemaVersion,
        List<DatabaseFileSpecification> Files);
}
