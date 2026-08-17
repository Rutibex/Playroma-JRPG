using Playroma.JRPG.PluginTags;
using Playroma.JRPG.PluginTags.Yanfly;

namespace Playroma.JRPG.Editor.WinForms.Plugins;

public sealed class YanflyPluginManagerIntelligenceService
{
    private readonly YanflyInstalledSourceInspector sourceInspector = new();
    private readonly YanflyInstalledPluginIntelligence intelligence = new();

    public PluginIntelligenceProfile Profile => intelligence.Profile;

    public async ValueTask<InstalledPluginAnalysis> AnalyzeAsync(
        IReadOnlyList<PluginEditEntry> entries,
        Func<string, string> scriptPathResolver,
        string pluginsDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(scriptPathResolver);
        List<InstalledPluginDescriptor> descriptors = [];
        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PluginEditEntry entry = entries[index];
            string path = scriptPathResolver(entry.Name);
            bool exists = File.Exists(path);
            YanflyInstalledSourceInspection? inspection = null;
            if (exists)
            {
                try { inspection = await sourceInspector.InspectFileAsync(entry.Name, path, cancellationToken).ConfigureAwait(false); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            descriptors.Add(new InstalledPluginDescriptor(
                index + 1,
                entry.Name,
                entry.Enabled,
                path,
                exists,
                inspection?.Version,
                inspection?.SourceFingerprint,
                inspection?.ImportedAliases,
                entry.Parameters.ToDictionary(pair => pair.Key, pair => entry.GetParameter(pair.Key), StringComparer.OrdinalIgnoreCase)));
        }

        string[] scripts = Directory.Exists(pluginsDirectory)
            ? Directory.EnumerateFiles(pluginsDirectory, "*.js", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray()
            : [];
        return intelligence.Analyze(descriptors, scripts);
    }

    public IReadOnlyList<string> CreateStableCorrectedOrder(IReadOnlyList<PluginEditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string[] current = entries.Select(entry => InstalledPluginIntelligenceAnalyzer.NormalizeId(entry.Name)).ToArray();
        Dictionary<string, int> originalOrder = current
            .Select((id, index) => (id, index))
            .GroupBy(item => item.id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, HashSet<string>> outgoing = current.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(id => id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> indegree = outgoing.Keys.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);

        IEnumerable<(string Before, string After)> rules = intelligence.Profile.LoadOrderRules
            .Select(rule => (rule.BeforePluginId, rule.AfterPluginId))
            .Concat(intelligence.Profile.Dependencies
                .Where(rule => rule.Strength == PluginDependencyStrength.Required)
                .Select(rule => (rule.RequiredPluginId, rule.PluginId)));
        foreach ((string beforeRaw, string afterRaw) in rules)
        {
            string before = InstalledPluginIntelligenceAnalyzer.NormalizeId(beforeRaw);
            string after = InstalledPluginIntelligenceAnalyzer.NormalizeId(afterRaw);
            if (!outgoing.ContainsKey(before) || !outgoing.ContainsKey(after) || before.Equals(after, StringComparison.OrdinalIgnoreCase)) continue;
            if (outgoing[before].Add(after)) indegree[after]++;
        }

        List<string> result = [];
        List<string> ready = indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key).OrderBy(id => originalOrder[id]).ToList();
        while (ready.Count > 0)
        {
            string id = ready[0];
            ready.RemoveAt(0);
            result.Add(id);
            foreach (string next in outgoing[id].OrderBy(value => originalOrder[value]))
            {
                indegree[next]--;
                if (indegree[next] == 0)
                {
                    ready.Add(next);
                    ready.Sort((left, right) => originalOrder[left].CompareTo(originalOrder[right]));
                }
            }
        }

        if (result.Count != outgoing.Count) return current;
        Dictionary<string, Queue<string>> originals = entries
            .GroupBy(entry => InstalledPluginIntelligenceAnalyzer.NormalizeId(entry.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new Queue<string>(group.Select(entry => entry.Name)), StringComparer.OrdinalIgnoreCase);
        return result.SelectMany(id => originals[id]).ToArray();
    }
}
