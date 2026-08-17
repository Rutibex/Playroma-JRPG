namespace Playroma.JRPG.Indexing;

public sealed class ProjectReferenceIndex
{
    private readonly object _sync = new();
    private readonly Dictionary<string, IReadOnlyList<ProjectReference>> _byDocument;
    private DateTimeOffset _builtUtc;

    public ProjectReferenceIndex()
    {
        _byDocument = new Dictionary<string, IReadOnlyList<ProjectReference>>(PathComparer);
        _builtUtc = DateTimeOffset.MinValue;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ProjectReference> All
    {
        get
        {
            lock (_sync)
            {
                return _byDocument.Values.SelectMany(value => value).ToArray();
            }
        }
    }

    public ReferenceIndexStatistics Statistics
    {
        get
        {
            lock (_sync)
            {
                int exact = _byDocument.Values.Sum(items => items.Count(item => item.Confidence == ReferenceConfidence.Exact));
                int heuristic = _byDocument.Values.Sum(items => items.Count(item => item.Confidence == ReferenceConfidence.Heuristic));
                return new ReferenceIndexStatistics(
                    _byDocument.Count,
                    exact,
                    heuristic,
                    exact + heuristic,
                    _builtUtc);
            }
        }
    }

    public IReadOnlyList<ProjectReference> FindInbound(
        ReferenceTarget target,
        bool includeHeuristic = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_sync)
        {
            return _byDocument.Values
                .SelectMany(value => value)
                .Where(reference => reference.Target == target
                    && (includeHeuristic || reference.Confidence == ReferenceConfidence.Exact))
                .OrderBy(reference => reference.Source.RelativePath, PathComparer)
                .ThenBy(reference => reference.Source.JsonPath, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public IReadOnlyList<ProjectReference> FindOutbound(
        string relativePath,
        bool includeHeuristic = true)
    {
        string normalized = Normalize(relativePath);
        lock (_sync)
        {
            if (!_byDocument.TryGetValue(normalized, out IReadOnlyList<ProjectReference> references)) return [];
            return references
                .Where(reference => includeHeuristic || reference.Confidence == ReferenceConfidence.Exact)
                .ToArray();
        }
    }

    public IReadOnlyList<ProjectReference> FindFromRecord(
        string relativePath,
        int recordId,
        bool includeHeuristic = true) =>
        FindOutbound(relativePath, includeHeuristic)
            .Where(reference => reference.Source.RecordId == recordId)
            .ToArray();


    public void ReplaceAll(IEnumerable<ProjectReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        Dictionary<string, IReadOnlyList<ProjectReference>> replacement = references
            .GroupBy(reference => Normalize(reference.Source.RelativePath), PathComparer)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ProjectReference>)group
                    .Distinct()
                    .OrderBy(reference => reference.Source.JsonPath, StringComparer.Ordinal)
                    .ThenBy(reference => reference.Target.Kind)
                    .ThenBy(reference => reference.Target.Id)
                    .ThenBy(reference => reference.Target.Name, StringComparer.Ordinal)
                    .ToArray(),
                PathComparer);
        lock (_sync)
        {
            _byDocument.Clear();
            foreach ((string path, IReadOnlyList<ProjectReference> items) in replacement)
            {
                _byDocument[path] = items;
            }
            _builtUtc = DateTimeOffset.UtcNow;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceDocument(string relativePath, IEnumerable<ProjectReference> references)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(references);
        string normalized = Normalize(relativePath);
        ProjectReference[] materialized = references
            .Distinct()
            .OrderBy(reference => reference.Source.JsonPath, StringComparer.Ordinal)
            .ThenBy(reference => reference.Target.Kind)
            .ThenBy(reference => reference.Target.Id)
            .ThenBy(reference => reference.Target.Name, StringComparer.Ordinal)
            .ToArray();
        lock (_sync)
        {
            _byDocument[normalized] = materialized;
            _builtUtc = DateTimeOffset.UtcNow;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveDocument(string relativePath)
    {
        bool removed;
        lock (_sync)
        {
            removed = _byDocument.Remove(Normalize(relativePath));
            if (removed) _builtUtc = DateTimeOffset.UtcNow;
        }

        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _byDocument.Clear();
            _builtUtc = DateTimeOffset.MinValue;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Normalize(string value) => value.Replace('\\', '/');
    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
