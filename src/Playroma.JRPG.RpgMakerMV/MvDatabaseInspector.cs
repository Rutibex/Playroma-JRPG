using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;
using Playroma.JRPG.Specifications;

namespace Playroma.JRPG.RpgMakerMV;

internal sealed partial class MvDatabaseInspector
{
    private readonly IReadOnlyProjectFileSystem _fileSystem;
    private readonly FileFingerprintService _fingerprints;
    private readonly MvSpecificationCatalog _specifications;

    public MvDatabaseInspector(
        IReadOnlyProjectFileSystem fileSystem,
        FileFingerprintService fingerprints,
        MvSpecificationCatalog specifications)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
        _specifications = specifications ?? throw new ArgumentNullException(nameof(specifications));
    }

    public async Task<DatabaseInspectionResult> InspectAsync(
        MvProjectCandidate candidate,
        ProjectLoadOptions options,
        IProgress<ProjectLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ProjectPathPolicy projectPaths = new(candidate.ProjectRoot);
        List<ProjectDiagnostic> diagnostics = [];
        List<MvDatabaseFileSummary> summaries = [];
        string? title = null;
        string? markerVersion = await ReadMarkerVersionAsync(candidate.ProjectMarkerPath, cancellationToken).ConfigureAwait(false);

        DatabaseFileSpecification[] standardFiles = _specifications.DatabaseFiles
            .Where(specification => !string.Equals(specification.Name, "MapNNN.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        int completed = 0;
        foreach (DatabaseFileSpecification specification in standardFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.Combine(candidate.DataRoot, specification.Name);
            string relativePath = projectPaths.GetRelativePath(fullPath);
            List<ProjectDiagnostic> fileDiagnostics = [];

            if (!_fileSystem.FileExists(fullPath))
            {
                if (specification.Required)
                {
                    ProjectDiagnostic missing = ProjectDiagnostic.Warning(
                        DiagnosticCodes.StandardDataFileMissing,
                        $"Standard MV data file is missing: {specification.Name}",
                        new ProjectLocation(relativePath));
                    fileDiagnostics.Add(missing);
                    diagnostics.Add(missing);
                }

                summaries.Add(new MvDatabaseFileSummary(
                    ParseDocumentKind(specification),
                    relativePath,
                    0,
                    0,
                    0,
                    0,
                    false,
                    false,
                    fileDiagnostics));
                completed++;
                progress?.Report(new ProjectLoadProgress(ProjectLoadStage.LoadingDatabase, completed, standardFiles.Length, relativePath));
                continue;
            }

            try
            {
                ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(fullPath, true, cancellationToken).ConfigureAwait(false);
                ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(snapshot, relativePath);
                if (changed is not null)
                {
                    fileDiagnostics.Add(changed);
                    diagnostics.Add(changed);
                }

                using JsonDocument document = JsonDocument.Parse(snapshot.Bytes);
                MvDocumentKind kind = ParseDocumentKind(specification);
                MvDatabaseFileSummary summary = InspectStandardDocument(
                    kind,
                    relativePath,
                    specification.Root,
                    document.RootElement,
                    fileDiagnostics);
                summaries.Add(summary);
                diagnostics.AddRange(fileDiagnostics.Where(diagnostic => !diagnostics.Contains(diagnostic)));

                if (kind == MvDocumentKind.System
                    && document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("gameTitle", out JsonElement gameTitle)
                    && gameTitle.ValueKind == JsonValueKind.String)
                {
                    title = gameTitle.GetString();
                }
            }
            catch (JsonException exception)
            {
                ProjectDiagnostic invalid = ProjectDiagnostic.Error(
                    DiagnosticCodes.InvalidJson,
                    $"Invalid JSON in {specification.Name}.",
                    new ProjectLocation(relativePath, exception.Path),
                    $"Byte position {exception.BytePositionInLine}: {exception.Message}");
                fileDiagnostics.Add(invalid);
                diagnostics.Add(invalid);
                summaries.Add(new MvDatabaseFileSummary(
                    ParseDocumentKind(specification),
                    relativePath,
                    0,
                    0,
                    0,
                    0,
                    false,
                    false,
                    fileDiagnostics));
                if (!options.ContinueAfterErrors) break;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                ProjectDiagnostic failed = ProjectDiagnostic.Error(
                    exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                    $"Could not read {specification.Name}.",
                    new ProjectLocation(relativePath),
                    exception.Message);
                fileDiagnostics.Add(failed);
                diagnostics.Add(failed);
                summaries.Add(new MvDatabaseFileSummary(
                    ParseDocumentKind(specification),
                    relativePath,
                    0,
                    0,
                    0,
                    0,
                    false,
                    false,
                    fileDiagnostics));
                if (!options.ContinueAfterErrors) break;
            }

            completed++;
            progress?.Report(new ProjectLoadProgress(ProjectLoadStage.LoadingDatabase, completed, standardFiles.Length, relativePath));
        }

        IReadOnlyList<MvMapDescriptor> maps = [];
        if (options.Depth >= ProjectLoadDepth.StandardData)
        {
            maps = await InspectMapsAsync(candidate, options, progress, diagnostics, cancellationToken).ConfigureAwait(false);
        }

        return new DatabaseInspectionResult(
            new MvDatabaseInventory(summaries, title),
            maps,
            diagnostics,
            markerVersion);
    }

    private MvDatabaseFileSummary InspectStandardDocument(
        MvDocumentKind kind,
        string relativePath,
        string expectedRoot,
        JsonElement root,
        List<ProjectDiagnostic> diagnostics)
    {
        bool expectedArray = expectedRoot.Contains("array", StringComparison.OrdinalIgnoreCase);
        if (expectedArray && root.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.UnexpectedJsonRoot,
                "The MV data file should contain a JSON array.",
                new ProjectLocation(relativePath, "$")));
            return new MvDatabaseFileSummary(kind, relativePath, 0, 0, 0, 0, false, true, diagnostics.ToArray());
        }

        if (!expectedArray && root.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.UnexpectedJsonRoot,
                "The MV data file should contain a JSON object.",
                new ProjectLocation(relativePath, "$")));
            return new MvDatabaseFileSummary(kind, relativePath, 0, 0, 0, 0, false, true, diagnostics.ToArray());
        }

        if (!expectedArray)
        {
            return new MvDatabaseFileSummary(kind, relativePath, 1, 1, 0, 0, false, true, diagnostics.ToArray());
        }

        int arrayLength = root.GetArrayLength();
        int populated = 0;
        int nullSlots = 0;
        int highestId = 0;
        int index = 0;
        bool hasIndexZeroNull = arrayLength > 0 && root[0].ValueKind == JsonValueKind.Null;

        foreach (JsonElement element in root.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                nullSlots++;
            }
            else
            {
                populated++;
                int id = index;
                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("id", out JsonElement idElement)
                    && idElement.TryGetInt32(out int explicitId))
                {
                    id = explicitId;
                }

                highestId = Math.Max(highestId, id);
            }

            index++;
        }

        if (!hasIndexZeroNull)
        {
            diagnostics.Add(ProjectDiagnostic.Warning(
                DiagnosticCodes.MissingIndexZeroNull,
                "MV one-based database arrays normally contain null at index zero.",
                new ProjectLocation(relativePath, "$[0]")));
        }

        return new MvDatabaseFileSummary(
            kind,
            relativePath,
            arrayLength,
            populated,
            nullSlots,
            highestId,
            hasIndexZeroNull,
            true,
            diagnostics.ToArray());
    }

    private async Task<IReadOnlyList<MvMapDescriptor>> InspectMapsAsync(
        MvProjectCandidate candidate,
        ProjectLoadOptions options,
        IProgress<ProjectLoadProgress>? progress,
        List<ProjectDiagnostic> projectDiagnostics,
        CancellationToken cancellationToken)
    {
        ProjectPathPolicy projectPaths = new(candidate.ProjectRoot);
        Dictionary<int, MvMapDescriptor> maps = [];
        string mapInfosPath = Path.Combine(candidate.DataRoot, "MapInfos.json");
        if (_fileSystem.FileExists(mapInfosPath))
        {
            string mapInfosRelative = projectPaths.GetRelativePath(mapInfosPath);
            try
            {
                ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(mapInfosPath, true, cancellationToken).ConfigureAwait(false);
                ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(snapshot, mapInfosRelative);
                if (changed is not null)
                {
                    projectDiagnostics.Add(changed);
                }

                using JsonDocument document = JsonDocument.Parse(snapshot.Bytes);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    int index = 0;
                    foreach (JsonElement element in document.RootElement.EnumerateArray())
                    {
                        if (index == 0 || element.ValueKind == JsonValueKind.Null)
                        {
                            index++;
                            continue;
                        }

                        int id = GetInt32(element, "id", index);
                        string name = GetString(element, "name") ?? string.Create(CultureInfo.InvariantCulture, $"Map {id:D3}");
                        int parentId = GetInt32(element, "parentId", 0);
                        int order = GetInt32(element, "order", id);
                        string mapPath = Path.Combine(candidate.DataRoot, string.Create(CultureInfo.InvariantCulture, $"Map{id:D3}.json"));
                        string relativePath = projectPaths.GetRelativePath(mapPath);
                        bool exists = _fileSystem.FileExists(mapPath);
                        List<ProjectDiagnostic> mapDiagnostics = [];
                        if (!exists)
                        {
                            ProjectDiagnostic missing = ProjectDiagnostic.Warning(
                                DiagnosticCodes.MapFileMissing,
                                $"MapInfos.json lists map {id}, but {Path.GetFileName(mapPath)} is missing.",
                                new ProjectLocation(relativePath, MapId: id));
                            mapDiagnostics.Add(missing);
                            projectDiagnostics.Add(missing);
                        }

                        if (maps.ContainsKey(id))
                        {
                            ProjectDiagnostic duplicate = ProjectDiagnostic.Warning(
                                DiagnosticCodes.DuplicateMapId,
                                $"MapInfos.json contains more than one entry for map ID {id}.",
                                new ProjectLocation(mapInfosRelative, MapId: id));
                            projectDiagnostics.Add(duplicate);
                        }

                        maps[id] = new MvMapDescriptor(
                            id,
                            name,
                            parentId,
                            order,
                            relativePath,
                            exists,
                            null,
                            null,
                            null,
                            null,
                            null,
                            [],
                            mapDiagnostics);
                        index++;
                    }
                }
                else
                {
                    projectDiagnostics.Add(ProjectDiagnostic.Error(
                        DiagnosticCodes.UnexpectedJsonRoot,
                        "MapInfos.json should contain a JSON array.",
                        new ProjectLocation(mapInfosRelative, "$")));
                }
            }
            catch (JsonException exception)
            {
                projectDiagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.InvalidJson,
                    "MapInfos.json is invalid JSON.",
                    new ProjectLocation(mapInfosRelative, exception.Path),
                    exception.Message));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                projectDiagnostics.Add(ProjectDiagnostic.Error(
                    exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                    "MapInfos.json could not be read.",
                    new ProjectLocation(mapInfosRelative),
                    exception.Message));
            }
        }

        IEnumerable<string> mapFiles;
        try
        {
            mapFiles = _fileSystem.EnumerateFiles(candidate.DataRoot, "Map*.json", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            projectDiagnostics.Add(ProjectDiagnostic.Error(
                exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                "Map files could not be enumerated.",
                new ProjectLocation(projectPaths.GetRelativePath(candidate.DataRoot)),
                exception.Message));
            return maps.Values.OrderBy(map => map.Order).ThenBy(map => map.Id).ToArray();
        }

        foreach (string mapFile in mapFiles)
        {
            Match match = MapFileRegex().Match(Path.GetFileName(mapFile));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id)) continue;
            if (maps.ContainsKey(id)) continue;

            string relativePath = projectPaths.GetRelativePath(mapFile);
            ProjectDiagnostic unlisted = ProjectDiagnostic.Information(
                DiagnosticCodes.MapFileNotListed,
                $"{Path.GetFileName(mapFile)} exists but is not listed in MapInfos.json.",
                new ProjectLocation(relativePath, MapId: id));
            projectDiagnostics.Add(unlisted);
            maps[id] = new MvMapDescriptor(
                id,
                string.Create(CultureInfo.InvariantCulture, $"Map {id:D3}"),
                0,
                int.MaxValue,
                relativePath,
                true,
                null,
                null,
                null,
                null,
                null,
                [],
                [unlisted]);
        }

        MvMapDescriptor[] ordered = maps.Values.OrderBy(map => map.Order).ThenBy(map => map.Id).ToArray();
        if (options.Depth != ProjectLoadDepth.Complete)
        {
            return ordered;
        }

        ConcurrentBag<ProjectDiagnostic> concurrentDiagnostics = [];
        using SemaphoreSlim semaphore = new(options.EffectiveParallelReads);
        int completed = 0;
        Task<MvMapDescriptor>[] tasks = ordered.Select(async map =>
        {
            if (!map.FileExists) return map;
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                MvMapDescriptor scanned = await ScanMapAsync(candidate.ProjectRoot, map, cancellationToken).ConfigureAwait(false);
                foreach (ProjectDiagnostic diagnostic in scanned.Diagnostics) concurrentDiagnostics.Add(diagnostic);
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new ProjectLoadProgress(ProjectLoadStage.LoadingMaps, current, ordered.Length, map.RelativePath));
                return scanned;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        MvMapDescriptor[] scannedMaps = await Task.WhenAll(tasks).ConfigureAwait(false);
        projectDiagnostics.AddRange(concurrentDiagnostics.OrderBy(diagnostic => diagnostic.Location.RelativePath, StringComparer.OrdinalIgnoreCase));
        return scannedMaps.OrderBy(map => map.Order).ThenBy(map => map.Id).ToArray();
    }

    private async Task<MvMapDescriptor> ScanMapAsync(
        string projectRoot,
        MvMapDescriptor map,
        CancellationToken cancellationToken)
    {
        ProjectPathPolicy paths = new(projectRoot);
        string fullPath = paths.ResolveRelative(map.RelativePath);
        List<ProjectDiagnostic> diagnostics = map.Diagnostics.ToList();
        try
        {
            ReadFileSnapshot snapshot = await _fingerprints.ReadAsync(fullPath, true, cancellationToken).ConfigureAwait(false);
            ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(snapshot, map.RelativePath);
            if (changed is not null) diagnostics.Add(changed);
            using JsonDocument document = JsonDocument.Parse(snapshot.Bytes);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.UnexpectedJsonRoot,
                    "An MV map file should contain a JSON object.",
                    new ProjectLocation(map.RelativePath, "$", map.Id)));
                return map with { Diagnostics = diagnostics };
            }

            int width = GetInt32(root, "width", 0);
            int height = GetInt32(root, "height", 0);
            if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
            {
                long expectedLength = (long)width * height * 6;
                if (data.GetArrayLength() != expectedLength)
                {
                    diagnostics.Add(ProjectDiagnostic.Warning(
                        DiagnosticCodes.MapDataLengthMismatch,
                        string.Create(CultureInfo.InvariantCulture, $"Map data contains {data.GetArrayLength():N0} values; {expectedLength:N0} are expected for {width}×{height}×6 layers."),
                        new ProjectLocation(map.RelativePath, "$.data", map.Id)));
                }
            }

            int eventCount = 0;
            int pageCount = 0;
            int commandCount = 0;
            HashSet<int> knownCodes = _specifications.EventCommands.Select(command => command.Code).ToHashSet();
            SortedSet<int> unknownCodes = [];

            if (root.TryGetProperty("events", out JsonElement events) && events.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement eventElement in events.EnumerateArray())
                {
                    if (eventElement.ValueKind != JsonValueKind.Object) continue;
                    eventCount++;
                    if (!eventElement.TryGetProperty("pages", out JsonElement pages) || pages.ValueKind != JsonValueKind.Array) continue;
                    pageCount += pages.GetArrayLength();
                    foreach (JsonElement page in pages.EnumerateArray())
                    {
                        if (page.ValueKind != JsonValueKind.Object
                            || !page.TryGetProperty("list", out JsonElement list)
                            || list.ValueKind != JsonValueKind.Array)
                        {
                            continue;
                        }

                        commandCount += list.GetArrayLength();
                        foreach (JsonElement command in list.EnumerateArray())
                        {
                            if (command.ValueKind != JsonValueKind.Object
                                || !command.TryGetProperty("code", out JsonElement codeElement)
                                || !codeElement.TryGetInt32(out int code))
                            {
                                continue;
                            }

                            if (!knownCodes.Contains(code)) unknownCodes.Add(code);
                        }
                    }
                }
            }

            foreach (int unknownCode in unknownCodes)
            {
                diagnostics.Add(ProjectDiagnostic.Information(
                    DiagnosticCodes.UnknownEventCommand,
                    $"Map {map.Id} contains unknown event command code {unknownCode}; it will be preserved by later phases.",
                    new ProjectLocation(map.RelativePath, MapId: map.Id)));
            }

            return map with
            {
                Width = width,
                Height = height,
                EventCount = eventCount,
                EventPageCount = pageCount,
                EventCommandCount = commandCount,
                UnknownCommandCodes = unknownCodes.ToArray(),
                Diagnostics = diagnostics
            };
        }
        catch (JsonException exception)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.InvalidJson,
                "The map file contains invalid JSON.",
                new ProjectLocation(map.RelativePath, exception.Path, map.Id),
                exception.Message));
            return map with { Diagnostics = diagnostics };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                "The map file could not be read.",
                new ProjectLocation(map.RelativePath, MapId: map.Id),
                exception.Message));
            return map with { Diagnostics = diagnostics };
        }
    }

    private async Task<string?> ReadMarkerVersionAsync(string? markerPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(markerPath) || !_fileSystem.FileExists(markerPath)) return null;
        try
        {
            byte[] bytes = await _fileSystem.ReadAllBytesAsync(markerPath, cancellationToken).ConfigureAwait(false);
            if (!Utf8TextDecoder.TryDecode(bytes, out string markerText)) return null;
            Match match = MarkerVersionRegex().Match(markerText);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static MvDocumentKind ParseDocumentKind(DatabaseFileSpecification specification)
    {
        string value = specification.DocumentKind ?? specification.RecordType;
        return Enum.TryParse(value, ignoreCase: true, out MvDocumentKind kind)
            ? kind
            : MvDocumentKind.Unknown;
    }

    private static int GetInt32(JsonElement element, string propertyName, int defaultValue) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement property)
        && property.TryGetInt32(out int value)
            ? value
            : defaultValue;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    [GeneratedRegex(@"^Map([0-9]+)\.json$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MapFileRegex();

    [GeneratedRegex(@"RPGMV\s+([0-9]+(?:\.[0-9]+){1,3})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerVersionRegex();
}
