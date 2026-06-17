using System.Security.Cryptography;

namespace PassGen.Engine;

/// <summary>Minimal RNG abstraction so the generator can be secure-by-default yet reproducible.</summary>
internal interface IRng
{
    /// <summary>Uniform integer in [0, maxExclusive).</summary>
    int Next(int maxExclusive);
}

/// <summary>Cryptographically secure RNG (System.Security.Cryptography). The default.</summary>
internal sealed class CryptoRng : IRng
{
    public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}

/// <summary>Reproducible RNG (Mersenne-Twister-like <see cref="Random"/>). For tests/demos only.</summary>
internal sealed class SeededRng(int seed) : IRng
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}

internal static class RngExtensions
{
    public static T Choice<T>(this IRng rng, IReadOnlyList<T> items) => items[rng.Next(items.Count)];

    /// <summary>Uniform integer in [lo, hi] inclusive.</summary>
    public static int NextInclusive(this IRng rng, int lo, int hi) => lo + rng.Next(hi - lo + 1);

    public static void Shuffle<T>(this IRng rng, IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
