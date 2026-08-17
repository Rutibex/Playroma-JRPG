namespace PlayromaMusic.Generators.Rhythms;

public static class EuclideanRhythm
{
    public static IReadOnlyList<bool> Generate(int pulses, int steps, int rotation = 0)
    {
        if (steps is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(steps));
        }

        pulses = Math.Clamp(pulses, 0, steps);
        if (pulses == 0)
        {
            return Enumerable.Repeat(false, steps).ToArray();
        }

        if (pulses == steps)
        {
            return Enumerable.Repeat(true, steps).ToArray();
        }

        List<int> counts = new();
        List<int> remainders = new() { pulses };
        int divisor = steps - pulses;
        int level = 0;
        while (true)
        {
            counts.Add(divisor / remainders[level]);
            remainders.Add(divisor % remainders[level]);
            divisor = remainders[level];
            level++;
            if (remainders[level] <= 1)
            {
                break;
            }
        }

        counts.Add(divisor);
        List<bool> pattern = new(steps);
        Build(level, pattern, counts, remainders);
        while (pattern.Count < steps)
        {
            pattern.Add(false);
        }
        if (pattern.Count > steps)
        {
            pattern.RemoveRange(steps, pattern.Count - steps);
        }

        int firstPulse = pattern.FindIndex(value => value);
        if (firstPulse > 0)
        {
            pattern = Rotate(pattern, -firstPulse);
        }

        return Rotate(pattern, rotation);
    }

    private static void Build(int level, List<bool> output, IReadOnlyList<int> counts, IReadOnlyList<int> remainders)
    {
        if (level == -1)
        {
            output.Add(false);
            return;
        }
        if (level == -2)
        {
            output.Add(true);
            return;
        }
        for (int index = 0; index < counts[level]; index++)
        {
            Build(level - 1, output, counts, remainders);
        }
        if (remainders[level] != 0)
        {
            Build(level - 2, output, counts, remainders);
        }
    }

    private static List<bool> Rotate(IReadOnlyList<bool> source, int rotation)
    {
        int count = source.Count;
        int normalized = ((rotation % count) + count) % count;
        bool[] result = new bool[count];
        for (int index = 0; index < count; index++)
        {
            result[(index + normalized) % count] = source[index];
        }
        return result.ToList();
    }
}

public static class RhythmPatternLibrary
{
    private static readonly Dictionary<string, bool[]> Patterns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["quarters"] = new[] { true, false, false, false, true, false, false, false, true, false, false, false, true, false, false, false },
            ["eighths"] = new[] { true, false, true, false, true, false, true, false, true, false, true, false, true, false, true, false },
            ["syncopated"] = new[] { true, false, false, true, false, false, true, false, true, false, true, false, false, true, false, false },
            ["clave-3-2"] = new[] { true, false, false, true, false, false, true, false, false, false, true, false, true, false, false, false },
            ["broken-eighths"] = new[] { true, false, true, false, false, false, true, false, true, false, false, false, true, false, true, false },
            ["four-on-floor"] = new[] { true, false, false, false, true, false, false, false, true, false, false, false, true, false, false, false }
        };

    private static readonly object Sync = new();

    public static IReadOnlyList<bool> Get(string id)
    {
        lock (Sync)
        {
            return Patterns.TryGetValue(id, out bool[]? pattern) ? pattern.ToArray() : Patterns["eighths"].ToArray();
        }
    }

    public static bool Contains(string id)
    {
        lock (Sync)
        {
            return Patterns.ContainsKey(id);
        }
    }

    public static void Register(string id, IEnumerable<bool> steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(steps);
        bool[] pattern = steps.ToArray();
        if (pattern.Length is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(steps), "Rhythm patterns must contain between 1 and 256 steps.");
        }
        if (!pattern.Any(value => value))
        {
            throw new ArgumentException("A rhythm pattern must contain at least one onset.", nameof(steps));
        }
        lock (Sync)
        {
            Patterns[id] = pattern;
        }
    }

    public static void RegisterRange(IReadOnlyDictionary<string, bool[]> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        foreach ((string id, bool[] steps) in patterns)
        {
            Register(id, steps);
        }
    }

    public static IReadOnlyCollection<string> Ids
    {
        get
        {
            lock (Sync)
            {
                return Patterns.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
    }
}
