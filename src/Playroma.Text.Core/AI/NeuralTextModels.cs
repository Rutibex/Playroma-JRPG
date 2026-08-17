using Playroma.Text.Core.Generation;

namespace Playroma.Text.Core.AI;

public sealed record NeuralProviderCapabilities(
    bool SupportsSeed = false,
    bool SupportsSystemPrompt = true,
    int MaximumPromptCharacters = 32_000,
    int MaximumOutputCharacters = 8_000);

public sealed record NeuralTextRequest(
    string Prompt,
    string? SystemPrompt = null,
    ulong? Seed = null,
    int MaximumOutputCharacters = 2_000,
    IReadOnlyDictionary<string, object?>? Context = null);

public sealed record NeuralTextResponse(
    string Text,
    string ProviderId,
    string? Model = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface INeuralTextProvider
{
    string Id { get; }
    string DisplayName { get; }
    NeuralProviderCapabilities Capabilities { get; }
    ValueTask<NeuralTextResponse> GenerateAsync(NeuralTextRequest request, CancellationToken cancellationToken = default);
}

public interface INeuralTextProviderResolver
{
    bool TryResolve(string id, out INeuralTextProvider provider);
    IReadOnlyList<INeuralTextProvider> Providers { get; }
}

public sealed class EmptyNeuralTextProviderResolver : INeuralTextProviderResolver
{
    public static EmptyNeuralTextProviderResolver Instance { get; } = new();
    public IReadOnlyList<INeuralTextProvider> Providers => Array.Empty<INeuralTextProvider>();
    public bool TryResolve(string id, out INeuralTextProvider provider) { provider = null!; return false; }
}
