using Playroma.Text.Core.AI;

namespace Playroma.Text.Generators.AI;

public sealed class NeuralTextProviderRegistry : INeuralTextProviderResolver
{
    private readonly Dictionary<string, INeuralTextProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<INeuralTextProvider> Providers => _providers.Values.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();

    public void Register(INeuralTextProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Id)) throw new ArgumentException("Provider id is required.", nameof(provider));
        _providers[provider.Id] = provider;
    }

    public bool Remove(string id) => _providers.Remove(id);
    public void Clear() => _providers.Clear();
    public bool TryResolve(string id, out INeuralTextProvider provider) => _providers.TryGetValue(id, out provider!);
}
