using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Playroma.JRPG.PluginTags;

namespace Playroma.JRPG.PluginTags.Yanfly;

public sealed record YanflyInstalledSourceInspection(
    string PluginId,
    string? Version,
    string SourceFingerprint,
    IReadOnlyList<string> ImportedAliases,
    bool AppearsToBeYanflyPlugin);

public sealed class YanflyInstalledSourceInspector
{
    private static readonly Regex PlugindescVersion = new(
        @"@plugindesc\s+v?(?<version>\d+(?:\.\d+){1,3}[A-Za-z]?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex SourceVersion = new(
        @"\.version\s*=\s*['""]?(?<version>\d+(?:\.\d+){1,3}[A-Za-z]?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ImportedAlias = new(
        @"\bImported\.(?<id>[A-Za-z0-9_]+)\s*=\s*true\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public YanflyInstalledSourceInspection Inspect(string pluginId, ReadOnlySpan<byte> sourceBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        byte[] bytes = sourceBytes.ToArray();
        string source;
        try { source = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { source = Encoding.Latin1.GetString(bytes); }

        Match version = PlugindescVersion.Match(source);
        if (!version.Success) version = SourceVersion.Match(source);
        string[] aliases = ImportedAlias.Matches(source)
            .Cast<Match>()
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string normalized = InstalledPluginIntelligenceAnalyzer.NormalizeId(pluginId);
        bool appearsYanfly = normalized.StartsWith("YEP_", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("KELYEP_", StringComparison.OrdinalIgnoreCase)
            || aliases.Any(alias => alias.StartsWith("YEP_", StringComparison.OrdinalIgnoreCase));

        return new(
            normalized,
            version.Success ? version.Groups["version"].Value : null,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            aliases,
            appearsYanfly);
    }

    public async ValueTask<YanflyInstalledSourceInspection> InspectFileAsync(
        string pluginId,
        string scriptPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        byte[] bytes = await File.ReadAllBytesAsync(scriptPath, cancellationToken).ConfigureAwait(false);
        return Inspect(pluginId, bytes);
    }
}
