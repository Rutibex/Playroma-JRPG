using Playroma.Text.Core.Random;

namespace Playroma.Text.Generators.Selection;

public sealed record WeightedText(string Text, double Weight = 1.0, string? Id = null, IReadOnlyDictionary<string, string>? Tags = null);

public static class WeightedSelector
{
    public static T Select<T>(IReadOnlyList<T> items, Func<T, double> weightSelector, IDeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(weightSelector);
        ArgumentNullException.ThrowIfNull(random);
        if (items.Count == 0) throw new InvalidOperationException("Cannot select from an empty weighted table.");

        var weights = new double[items.Count];
        var total = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            var weight = weightSelector(items[i]);
            if (double.IsNaN(weight) || double.IsInfinity(weight) || weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weightSelector), "Weights must be finite and non-negative.");
            weights[i] = weight;
            total += weight;
        }
        if (total <= 0) throw new InvalidOperationException("At least one weighted entry must have a positive weight.");

        var roll = random.NextDouble() * total;
        var cumulative = 0.0;
        for (var i = 0; i < items.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative) return items[i];
        }
        return items[^1];
    }

    public static IReadOnlyList<T> SelectWithoutReplacement<T>(IReadOnlyList<T> items, Func<T, double> weightSelector, int count, IDeterministicRandom random)
    {
        if (count <= 0 || items.Count == 0) return Array.Empty<T>();
        var pool = items.ToList();
        var result = new List<T>(Math.Min(count, pool.Count));
        while (pool.Count > 0 && result.Count < count)
        {
            var selected = Select(pool, weightSelector, random);
            result.Add(selected);
            pool.Remove(selected);
        }
        return result;
    }
}
