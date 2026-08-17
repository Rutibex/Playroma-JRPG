using Playroma.Text.Core.Random;

namespace Playroma.Text.Generators.Selection;

public sealed class ShuffleBag<T>
{
    private readonly IReadOnlyList<T> _source;
    private readonly IDeterministicRandom _random;
    private readonly List<T> _bag = new();
    private T? _last;
    private bool _hasLast;

    public ShuffleBag(IEnumerable<T> source, IDeterministicRandom random)
    {
        _source = source?.ToArray() ?? throw new ArgumentNullException(nameof(source));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        if (_source.Count == 0) throw new ArgumentException("Shuffle bag requires at least one item.", nameof(source));
        Refill();
    }

    public T Draw()
    {
        if (_bag.Count == 0) Refill();
        var index = _bag.Count - 1;
        var item = _bag[index];
        _bag.RemoveAt(index);
        _last = item;
        _hasLast = true;
        return item;
    }

    public IReadOnlyList<T> DrawMany(int count)
    {
        if (count <= 0) return Array.Empty<T>();
        var result = new List<T>(count);
        for (var i = 0; i < count; i++) result.Add(Draw());
        return result;
    }

    private void Refill()
    {
        _bag.Clear();
        _bag.AddRange(_source);
        for (var i = _bag.Count - 1; i > 0; i--)
        {
            var j = _random.NextInt(i + 1);
            (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
        }

        // Avoid an immediate repeat at a cycle boundary when there is a choice.
        if (_hasLast && _bag.Count > 1 && EqualityComparer<T>.Default.Equals(_bag[^1], _last))
            (_bag[^1], _bag[0]) = (_bag[0], _bag[^1]);
    }
}
