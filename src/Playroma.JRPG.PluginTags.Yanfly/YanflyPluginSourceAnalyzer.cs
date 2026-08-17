using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Playroma.JRPG.PluginTags.Yanfly;

internal sealed class YanflyPluginSourceAnalyzer
{
    private static readonly Regex EnglishPluginBlock = new(
        @"/\*:(?![A-Za-z])(?<body>.*?)\*/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex PlugindescVersion = new(
        @"@plugindesc\s+v?(?<version>\d+(?:\.\d+){1,3}[A-Za-z]?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex SourceVersion = new(
        @"\.version\s*=\s*['""]?(?<version>\d+(?:\.\d+){1,3}[A-Za-z]?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ImportedAlias = new(
        @"\bImported\.(?<id>[A-Za-z0-9_]+)\s*=\s*true\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AngleBracketExample = new(
        @"<[^>\r\n]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DependencyToken = new(
        @"\bYEP_[A-Za-z0-9_]+(?:\.js)?\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex DependencyCue = new(
        @"\b(require|requires|required|located\s+(?:under|below)|place(?:d)?\s+(?:under|below))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly IReadOnlyDictionary<string, string[]> ContextTerms =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Actor"] = ["actor"],
            ["Class"] = ["class"],
            ["Skill"] = ["skill"],
            ["Item"] = ["item"],
            ["Weapon"] = ["weapon"],
            ["Armor"] = ["armor", "armour"],
            ["Enemy"] = ["enemy"],
            ["State"] = ["state"],
            ["Map"] = ["map"],
            ["Tileset"] = ["tileset"],
            ["Event"] = ["event"],
            ["Troop"] = ["troop"],
            ["CommonEvent"] = ["common event"]
        };

    public YanflySourceAnalysis Analyze(string relativePath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(bytes);

        string normalizedPath = NormalizePath(relativePath);
        string fileName = Path.GetFileName(normalizedPath);
        string pluginId = Path.GetFileNameWithoutExtension(fileName);
        string source = Decode(bytes);
        Match blockMatch = EnglishPluginBlock.Match(source);
        string pluginBlock = blockMatch.Success ? blockMatch.Groups["body"].Value : string.Empty;
        string help = ExtractHelp(pluginBlock);
        string? version = ExtractVersion(pluginBlock, source);
        string displayName = ExtractDisplayName(source, pluginId);
        bool hasParameters = Regex.IsMatch(pluginBlock, @"^\s*\*?\s*@param\b", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        List<YanflyAuthoringSurface> surfaces = ExtractSurfaces(help, hasParameters);
        List<string> contexts = ExtractDeclaredContexts(help);
        List<YanflyUnresolvedDependency> dependencies = ExtractDependencies(pluginBlock, help);
        List<string> aliases = ImportedAlias.Matches(source)
            .Cast<Match>()
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new YanflySourceAnalysis(
            fileName,
            pluginId,
            displayName,
            fileName.StartsWith("KELYEP_", StringComparison.OrdinalIgnoreCase)
                ? YanflySourceFamily.KelDragonBones
                : YanflySourceFamily.YanflyEnginePlugins,
            version,
            normalizedPath,
            ExtractCategory(normalizedPath),
            bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            normalizedPath.Contains("/Free Plugins/", StringComparison.OrdinalIgnoreCase),
            blockMatch.Success && help.Length > 0,
            hasParameters,
            AngleBracketExample.Matches(help).Count,
            aliases,
            surfaces,
            contexts,
            dependencies);
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string ExtractCategory(string relativePath)
    {
        string[] parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : "Uncategorized";
    }

    private static string ExtractHelp(string pluginBlock)
    {
        int index = pluginBlock.IndexOf("@help", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? string.Empty : pluginBlock[(index + "@help".Length)..];
    }

    private static string? ExtractVersion(string pluginBlock, string source)
    {
        Match plugindesc = PlugindescVersion.Match(pluginBlock);
        if (plugindesc.Success) return plugindesc.Groups["version"].Value;
        Match sourceVersion = SourceVersion.Match(source);
        return sourceVersion.Success ? sourceVersion.Groups["version"].Value : null;
    }

    private static string ExtractDisplayName(string source, string pluginId)
    {
        foreach (string rawLine in source.Split('\n').Take(16))
        {
            string line = rawLine.Trim().TrimEnd('\r');
            if (!line.StartsWith("//", StringComparison.Ordinal)) continue;
            string candidate = line[2..].Trim();
            if (candidate.Length == 0 || candidate.All(character => character is '=' or '-')) continue;
            if (candidate.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) continue;
            const string prefix = "Yanfly Engine Plugins -";
            if (candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                candidate = candidate[prefix.Length..].Trim();
            if (candidate.Length > 0) return candidate;
        }

        return pluginId
            .Replace("KELYEP_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("YEP_X_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("YEP_Z_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("YEP_", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static List<YanflyAuthoringSurface> ExtractSurfaces(string help, bool hasParameters)
    {
        HashSet<YanflyAuthoringSurface> surfaces = [];
        if (Contains(help, @"\bnote\s*tags?\b|\bnotetags?\b")) surfaces.Add(YanflyAuthoringSurface.DatabaseNote);
        if (Contains(help, @"\bmap\s+note\s*tags?\b|\bmap\s+notetags?\b")) surfaces.Add(YanflyAuthoringSurface.MapNote);
        if (Contains(help, @"\btileset\s+note\s*tags?\b|\btileset\s+notetags?\b")) surfaces.Add(YanflyAuthoringSurface.TilesetNote);
        if (Contains(help, @"\bevent\s+note\s*tags?\b|\bevent\s+notetags?\b")) surfaces.Add(YanflyAuthoringSurface.EventNote);
        if (Contains(help, @"\bcomment\s+tags?\b|\bevent\s+comments?\b")) surfaces.Add(YanflyAuthoringSurface.EventPageComment);
        if (Contains(help, @"\bplugin\s+commands?\b")) surfaces.Add(YanflyAuthoringSurface.PluginCommand);
        if (Contains(help, @"\baction\s+sequences?\b")) surfaces.Add(YanflyAuthoringSurface.ActionSequence);
        if (Contains(help, @"\bmove(?:ment)?\s+routes?\b")) surfaces.Add(YanflyAuthoringSurface.MoveRouteScript);
        if (Contains(help, @"\btext\s+codes?\b|\bescape\s+codes?\b")) surfaces.Add(YanflyAuthoringSurface.MessageTextCode);
        if (Contains(help, @"\bswitch\s+names?\b")) surfaces.Add(YanflyAuthoringSurface.SwitchNameMetadata);
        if (Contains(help, @"\bvariable\s+names?\b")) surfaces.Add(YanflyAuthoringSurface.VariableNameMetadata);
        if (Contains(help, @"\bscript\s+calls?\b")) surfaces.Add(YanflyAuthoringSurface.ScriptCall);
        if (Contains(help, @"\blunatic\s+mode\b")) surfaces.Add(YanflyAuthoringSurface.LunaticMode);
        if (hasParameters) surfaces.Add(YanflyAuthoringSurface.PluginParameter);
        return surfaces.OrderBy(surface => surface).ToList();
    }

    private static bool Contains(string text, string pattern) =>
        Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static List<string> ExtractDeclaredContexts(string help)
    {
        string[] lines = help.Split('\n')
            .Select(line => Regex.Replace(line, @"^\s*\*\s?", string.Empty).Trim())
            .ToArray();
        HashSet<string> contexts = new(StringComparer.Ordinal);

        for (int index = 0; index < lines.Length; index++)
        {
            if (!Regex.IsMatch(lines[index], @"\bnote\s*tags?\b|\bnotetags?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            string context = string.Join(" ", lines.Skip(Math.Max(0, index - 2)).Take(Math.Min(4, index + 2)));
            foreach ((string name, string[] terms) in ContextTerms)
            {
                if (terms.Any(term => Regex.IsMatch(context, $@"\b{Regex.Escape(term)}s?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                    contexts.Add(name);
            }
        }

        return contexts.OrderBy(value => value, StringComparer.Ordinal).ToList();
    }

    private static List<YanflyUnresolvedDependency> ExtractDependencies(string pluginBlock, string help)
    {
        string description = ExtractDirectiveBody(pluginBlock, "@plugindesc");
        string introduction = ExtractIntroduction(help);
        Dictionary<string, YanflyUnresolvedDependency> dependencies = new(StringComparer.OrdinalIgnoreCase);

        AddDependencyEvidence(dependencies, description, YanflyDependencyKind.Required, YanflyEvidenceLocation.PluginDescription);
        AddDependencyEvidence(dependencies, introduction, YanflyDependencyKind.Required, YanflyEvidenceLocation.Introduction);
        AddDependencyEvidence(dependencies, help, YanflyDependencyKind.OptionalFeature, YanflyEvidenceLocation.HelpBody);

        return dependencies.Values
            .OrderBy(dependency => dependency.RequestedPluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractDirectiveBody(string pluginBlock, string directive)
    {
        Match match = Regex.Match(
            pluginBlock,
            Regex.Escape(directive) + @"(?<body>.*?)(?=\n\s*\*?\s*@(param|help|author)\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        return match.Success ? match.Groups["body"].Value : string.Empty;
    }

    private static string ExtractIntroduction(string help)
    {
        string[] lines = help.Split('\n')
            .Select(line => Regex.Replace(line, @"^\s*\*\s?", string.Empty).TrimEnd('\r'))
            .ToArray();
        int heading = Array.FindIndex(lines, line =>
            line.Trim().Equals("Introduction", StringComparison.OrdinalIgnoreCase) ||
            line.Trim().Equals("Requirements", StringComparison.OrdinalIgnoreCase) ||
            line.Trim().Equals("Required Plugins", StringComparison.OrdinalIgnoreCase));
        if (heading < 0) return string.Join("\n", lines.Take(80));

        int end = Math.Min(lines.Length, heading + 120);
        for (int index = heading + 3; index + 2 < lines.Length; index++)
        {
            if (IsSeparator(lines[index]) && lines[index + 1].Length > 0 && IsSeparator(lines[index + 2]))
            {
                end = index;
                break;
            }
        }

        return string.Join("\n", lines.Skip(heading).Take(end - heading));
    }

    private static bool IsSeparator(string value) => value.Length >= 12 && value.All(character => character == '=');

    private static void AddDependencyEvidence(
        IDictionary<string, YanflyUnresolvedDependency> target,
        string text,
        YanflyDependencyKind kind,
        YanflyEvidenceLocation location)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string[] evidenceLines = text.Split('\n')
            .Select(line => Regex.Replace(line, @"^\s*\*\s?", string.Empty).Trim())
            .Where(line => line.Length > 0 && DependencyCue.IsMatch(line))
            .ToArray();

        foreach (string line in evidenceLines)
        {
            foreach (Match token in DependencyToken.Matches(line))
            {
                string requested = token.Value.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                    ? token.Value[..^3]
                    : token.Value;
                YanflyUnresolvedDependency candidate = new(requested, kind, location, CollapseWhitespace(line));
                if (!target.TryGetValue(requested, out YanflyUnresolvedDependency? existing) ||
                    existing.Kind == YanflyDependencyKind.OptionalFeature && kind == YanflyDependencyKind.Required)
                {
                    target[requested] = candidate;
                }
            }
        }
    }

    private static string CollapseWhitespace(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
