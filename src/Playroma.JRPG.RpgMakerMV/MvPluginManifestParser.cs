using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playroma.JRPG.Core;
using Playroma.JRPG.Documents;

namespace Playroma.JRPG.RpgMakerMV;

internal sealed class MvPluginManifestParser
{
    private readonly IReadOnlyProjectFileSystem _fileSystem;
    private readonly FileFingerprintService _fingerprints;

    public MvPluginManifestParser(
        IReadOnlyProjectFileSystem fileSystem,
        FileFingerprintService fingerprints)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
    }

    public async Task<MvPluginInventory> InspectAsync(
        string projectRoot,
        string? runtimeRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot))
        {
            return MvPluginInventory.Empty;
        }

        ProjectPathPolicy projectPaths = new(projectRoot);
        string manifestPath = Path.Combine(runtimeRoot, "js", "plugins.js");
        if (!_fileSystem.FileExists(manifestPath))
        {
            return new MvPluginInventory(
                null,
                null,
                [],
                0,
                [ProjectDiagnostic.Information(
                    DiagnosticCodes.NonstandardPluginManifest,
                    "No js/plugins.js manifest was found. The project may not use plugins or may have an incomplete runtime.")]);
        }

        string manifestRelative = projectPaths.GetRelativePath(manifestPath);
        List<ProjectDiagnostic> diagnostics = [];
        ReadFileSnapshot manifestSnapshot;
        try
        {
            manifestSnapshot = await _fingerprints.ReadAsync(manifestPath, true, cancellationToken).ConfigureAwait(false);
            ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(manifestSnapshot, manifestRelative);
            if (changed is not null) diagnostics.Add(changed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                "The plugin manifest could not be read.",
                new ProjectLocation(manifestRelative),
                exception.Message));
            return new MvPluginInventory(manifestRelative, null, [], 0, diagnostics);
        }

        string source;
        try
        {
            source = Utf8TextDecoder.Decode(manifestSnapshot.Bytes);
        }
        catch (DecoderFallbackException exception)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.InvalidTextEncoding,
                "The plugin manifest is not valid UTF-8.",
                new ProjectLocation(manifestRelative),
                exception.Message));
            return new MvPluginInventory(
                manifestRelative,
                manifestSnapshot.Fingerprint.Sha256,
                [],
                0,
                diagnostics);
        }

        if (!TryExtractPluginArray(source, out string arrayText, out string? extractionError))
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.NonstandardPluginManifest,
                "The $plugins array could not be extracted without executing JavaScript.",
                new ProjectLocation(manifestRelative),
                extractionError));
            return new MvPluginInventory(
                manifestRelative,
                manifestSnapshot.Fingerprint.Sha256,
                [],
                0,
                diagnostics);
        }

        List<MvPluginEntry> entries = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(arrayText);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add(ProjectDiagnostic.Error(
                    DiagnosticCodes.NonstandardPluginManifest,
                    "The extracted $plugins value is not a JSON array.",
                    new ProjectLocation(manifestRelative)));
            }
            else
            {
                int order = 0;
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    order++;
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        diagnostics.Add(ProjectDiagnostic.Warning(
                            DiagnosticCodes.NonstandardPluginManifest,
                            $"Plugin entry {order} is not an object and was skipped.",
                            new ProjectLocation(manifestRelative, $"$[{order - 1}]")));
                        continue;
                    }

                    string name = ReadString(element, "name") ?? string.Empty;
                    bool enabled = ReadBoolean(element, "status");
                    string description = ReadString(element, "description") ?? string.Empty;
                    Dictionary<string, string> parameters = ReadParameters(
                        element,
                        manifestRelative,
                        order,
                        diagnostics);

                    bool safeName = IsSafePluginName(name);
                    string? scriptRelative = null;
                    bool scriptExists = false;
                    string? scriptSha256 = null;
                    bool nodeDependent = false;
                    string? declaredVersion = null;
                    IReadOnlyList<string> importedAliases = [];
                    string? providerId = null;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        diagnostics.Add(ProjectDiagnostic.Warning(
                            DiagnosticCodes.NonstandardPluginManifest,
                            $"Plugin entry {order} has no name.",
                            new ProjectLocation(manifestRelative, $"$[{order - 1}].name")));
                    }
                    else if (!safeName)
                    {
                        diagnostics.Add(ProjectDiagnostic.Warning(
                            DiagnosticCodes.UnsafePluginPath,
                            $"Plugin name '{name}' contains path components and was not followed.",
                            new ProjectLocation(manifestRelative, $"$[{order - 1}].name")));
                    }
                    else
                    {
                        string scriptFileName = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                            ? name
                            : name + ".js";
                        string scriptPath = Path.Combine(runtimeRoot, "js", "plugins", scriptFileName);
                        scriptRelative = projectPaths.GetRelativePath(scriptPath);
                        scriptExists = _fileSystem.FileExists(scriptPath);
                        if (scriptExists)
                        {
                            try
                            {
                                ReadFileSnapshot script = await _fingerprints.ReadAsync(scriptPath, true, cancellationToken).ConfigureAwait(false);
                                scriptSha256 = script.Fingerprint.Sha256;
                                if (Utf8TextDecoder.TryDecode(script.Bytes, out string scriptSource))
                                {
                                    nodeDependent = AppearsNodeDependent(scriptSource);
                                    declaredVersion = ExtractPluginVersion(scriptSource);
                                    importedAliases = ExtractImportedAliases(scriptSource);
                                    providerId = DetectPluginProvider(name, importedAliases);
                                }
                                else
                                {
                                    diagnostics.Add(ProjectDiagnostic.Warning(
                                        DiagnosticCodes.InvalidTextEncoding,
                                        $"Plugin script '{name}' is not valid UTF-8; its hash was retained but source heuristics were skipped.",
                                        new ProjectLocation(scriptRelative)));
                                }
                                ProjectDiagnostic? changed = FileFingerprintService.CreateChangedDuringLoadDiagnostic(script, scriptRelative);
                                if (changed is not null) diagnostics.Add(changed);
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                            {
                                diagnostics.Add(ProjectDiagnostic.Error(
                                    exception is UnauthorizedAccessException ? DiagnosticCodes.AccessDenied : DiagnosticCodes.FileReadFailure,
                                    $"Plugin script '{name}' could not be read.",
                                    new ProjectLocation(scriptRelative),
                                    exception.Message));
                            }
                        }
                        else
                        {
                            diagnostics.Add(ProjectDiagnostic.Warning(
                                DiagnosticCodes.PluginFileMissing,
                                $"Plugin entry '{name}' does not have a matching script file.",
                                new ProjectLocation(scriptRelative)));
                        }
                    }

                    entries.Add(new MvPluginEntry(
                        order,
                        name,
                        enabled,
                        description,
                        parameters,
                        scriptRelative,
                        scriptExists,
                        scriptSha256,
                        false,
                        nodeDependent,
                        declaredVersion,
                        importedAliases,
                        providerId));
                }
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add(ProjectDiagnostic.Error(
                DiagnosticCodes.NonstandardPluginManifest,
                "The extracted $plugins array is not valid JSON.",
                new ProjectLocation(manifestRelative, exception.Path),
                exception.Message));
        }

        HashSet<string> duplicates = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (duplicates.Count > 0)
        {
            foreach (string duplicate in duplicates.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.DuplicatePluginEntry,
                    $"The plugin manifest contains multiple entries named '{duplicate}'. They are preserved in their original order.",
                    new ProjectLocation(manifestRelative)));
            }

            entries = entries.Select(entry => entry with { IsDuplicate = duplicates.Contains(entry.Name) }).ToList();
        }

        return new MvPluginInventory(
            manifestRelative,
            manifestSnapshot.Fingerprint.Sha256,
            entries,
            entries.Count(entry => entry.Enabled),
            diagnostics);
    }

    internal static bool TryExtractPluginArray(
        string source,
        out string arrayText,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        arrayText = string.Empty;
        error = null;
        int variable = source.IndexOf("$plugins", StringComparison.Ordinal);
        if (variable < 0)
        {
            error = "The source does not contain a $plugins identifier.";
            return false;
        }

        int start = source.IndexOf('[', variable);
        if (start < 0)
        {
            error = "The $plugins identifier is not followed by an array.";
            return false;
        }

        int depth = 0;
        bool inString = false;
        char quote = '\0';
        bool escaped = false;
        bool lineComment = false;
        bool blockComment = false;

        for (int index = start; index < source.Length; index++)
        {
            char current = source[index];
            char next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (lineComment)
            {
                if (current is '\r' or '\n') lineComment = false;
                continue;
            }

            if (blockComment)
            {
                if (current == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }
                continue;
            }

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                {
                    inString = false;
                }
                continue;
            }

            if (current == '/' && next == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }

            if (current is '"' or '\'')
            {
                inString = true;
                quote = current;
                continue;
            }

            if (current == '[')
            {
                depth++;
            }
            else if (current == ']')
            {
                depth--;
                if (depth == 0)
                {
                    arrayText = source[start..(index + 1)];
                    return true;
                }
            }
        }

        error = "The $plugins array does not have a matching closing bracket.";
        return false;
    }

    private static Dictionary<string, string> ReadParameters(
        JsonElement plugin,
        string manifestRelative,
        int order,
        ICollection<ProjectDiagnostic> diagnostics)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal);
        if (!plugin.TryGetProperty("parameters", out JsonElement value)) return parameters;
        if (value.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add(ProjectDiagnostic.Warning(
                DiagnosticCodes.InvalidPluginParameter,
                $"Plugin entry {order} has a non-object parameters value.",
                new ProjectLocation(manifestRelative, $"$[{order - 1}].parameters")));
            return parameters;
        }

        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                parameters[property.Name] = property.Value.GetString() ?? string.Empty;
            }
            else
            {
                parameters[property.Name] = property.Value.GetRawText();
                diagnostics.Add(ProjectDiagnostic.Warning(
                    DiagnosticCodes.InvalidPluginParameter,
                    $"Plugin parameter '{property.Name}' is not stored as a string; its raw JSON text was preserved in the report.",
                    new ProjectLocation(manifestRelative, $"$[{order - 1}].parameters.{property.Name}")));
            }
        }

        return parameters;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static bool IsSafePluginName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && !Path.IsPathRooted(name)
        && !name.Contains("..", StringComparison.Ordinal)
        && name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\']) < 0;


    private static readonly Regex PlugindescVersionPattern = new(
        @"@plugindesc\s+v?(?<version>\d+(?:\.\d+){1,3}[A-Za-z]?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex SourceVersionPattern = new(
        @"\.version\s*=\s*['""]?(?<version>\d+(?:\.\d+){1,3}[A-Za-z]?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ImportedAliasPattern = new(
        @"\bImported\.(?<id>[A-Za-z0-9_]+)\s*=\s*true\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static string? ExtractPluginVersion(string source)
    {
        Match match = PlugindescVersionPattern.Match(source);
        if (!match.Success) match = SourceVersionPattern.Match(source);
        return match.Success ? match.Groups["version"].Value : null;
    }

    private static IReadOnlyList<string> ExtractImportedAliases(string source) =>
        ImportedAliasPattern.Matches(source)
            .Cast<Match>()
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? DetectPluginProvider(string pluginName, IReadOnlyList<string> aliases)
    {
        if (pluginName.StartsWith("YEP_", StringComparison.OrdinalIgnoreCase)
            || pluginName.StartsWith("KELYEP_", StringComparison.OrdinalIgnoreCase)
            || aliases.Any(alias => alias.StartsWith("YEP_", StringComparison.OrdinalIgnoreCase)))
            return "YEP-MV";
        return null;
    }

    private static bool AppearsNodeDependent(string source) =>
        source.Contains("require(", StringComparison.Ordinal)
        || source.Contains("require (", StringComparison.Ordinal)
        || source.Contains("process.", StringComparison.Ordinal)
        || source.Contains("nw.", StringComparison.Ordinal)
        || source.Contains("nw.Window", StringComparison.Ordinal);
}
