using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playroma.Tile.Materials.Buffers;
using Playroma.Tile.Materials.Models;
using Playroma.Tile.Materials.Validation;

namespace Playroma.Tile.Materials.Graph;

public sealed class MaterialGraphEvaluator
{
    private readonly MaterialNodeRegistry _registry;
    public MaterialGraphEvaluator(MaterialNodeRegistry? registry = null) => _registry = registry ?? new MaterialNodeRegistry();

    public MaterialResult Evaluate(MaterialRecipe recipe, MaterialEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var issues = new MaterialGraphValidator(_registry).Validate(recipe.Graph);
        if (issues.Any(x => x.Severity == MaterialIssueSeverity.Error))
            return new MaterialResult { Seed = context.Seed, RecipeHash = ComputeRecipeHash(recipe), Issues = issues };

        var cache = new Dictionary<Guid, MaterialNodeResult>();
        var active = new HashSet<Guid>();
        MaterialNodeResult EvaluateNode(Guid nodeId)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (cache.TryGetValue(nodeId, out var cached)) return cached;
            if (!active.Add(nodeId)) throw new InvalidOperationException("The material graph contains a cycle.");
            var instance = recipe.Graph.Find(nodeId) ?? throw new InvalidOperationException($"Node {nodeId} was not found.");
            var definition = _registry.Get(instance.TypeId);
            if (instance.NodeVersion < definition.CurrentVersion)
            {
                definition.Upgrade(instance.NodeVersion, instance.Parameters);
                instance.NodeVersion = definition.CurrentVersion;
            }
            var inputs = new Dictionary<string, MaterialValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var connection in instance.Inputs)
            {
                var source = EvaluateNode(connection.Value.SourceNodeId);
                inputs[connection.Key] = source.Get(connection.Value.SourcePort);
            }
            context.Progress?.Report(new MaterialProgress($"Evaluating {definition.DisplayName}", cache.Count / (float)Math.Max(1, recipe.Graph.Nodes.Count), nodeId));
            var result = definition.Evaluate(new MaterialNodeContext(instance, context, inputs));
            cache[nodeId] = result;
            active.Remove(nodeId);
            return result;
        }

        var outputId = recipe.Graph.OutputNodeId != Guid.Empty
            ? recipe.Graph.OutputNodeId
            : recipe.Graph.Nodes.LastOrDefault(x => string.Equals(x.TypeId, "Material.Output", StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.Empty;
        if (outputId == Guid.Empty) throw new InvalidOperationException("The material graph has no output node.");
        var output = EvaluateNode(outputId);
        var material = new MaterialResult { Seed = context.Seed, RecipeHash = ComputeRecipeHash(recipe), Issues = issues };
        foreach (var pair in output.Outputs)
        {
            if (Enum.TryParse<MaterialChannel>(pair.Key, true, out var channel)) material.Channels[channel] = pair.Value;
        }
        if (!material.Channels.ContainsKey(MaterialChannel.BaseColor) && output.Outputs.TryGetValue("Value", out var value))
            material.Channels[MaterialChannel.BaseColor] = value;
        context.Progress?.Report(new MaterialProgress("Complete", 1f));
        return material;
    }

    public async Task<MaterialResult> EvaluateAsync(MaterialRecipe recipe, MaterialEvaluationContext context) =>
        await Task.Run(() => Evaluate(recipe, context), context.CancellationToken).ConfigureAwait(false);

    public static string ComputeRecipeHash(MaterialRecipe recipe)
    {
        var json = JsonSerializer.Serialize(recipe, new JsonSerializerOptions { WriteIndented = false });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static string ComputeBufferHash(ColorBuffer buffer)
    {
        var bytes = new byte[buffer.Width * buffer.Height * 4]; var i = 0;
        foreach (var p in buffer.ReadOnlyPixels) { bytes[i++] = p.R; bytes[i++] = p.G; bytes[i++] = p.B; bytes[i++] = p.A; }
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
