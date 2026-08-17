using Playroma.Sound.Core.Random;

namespace Playroma.Sound.Generators.Building;

/// <summary>
/// Shared deterministic random-number helpers used by generator builders and
/// phrase composition. Keeping this helper in the Building namespace makes it
/// available to both the traditional generators and the phrase-composition code.
/// </summary>
internal static class GeneratorMath
{
    public static double Range(ref Pcg32 random, double minimum, double maximum) =>
        minimum + ((maximum - minimum) * random.NextFloat());

    public static double Signed(ref Pcg32 random) => random.NextSignedFloat();
}
