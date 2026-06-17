using System.Numerics;

namespace PassGen.Engine;

/// <summary>
/// Password strength = log2(number of distinct valid strings) — the attacker search space.
/// Per-class min/max counts make this a constrained multinomial, counted exactly here with
/// big-integer dynamic programming (reduces to N^L when unconstrained).
/// </summary>
public static class Entropy
{
    public static int CharsetSize(ConstraintSpec spec)
    {
        var excluded = new HashSet<char>(spec.ExcludeChars);
        return Alphabet.All
            .Where(c => spec.Classes[c].Allowed)
            .Sum(c => Alphabet.Pool(c).Count(ch => !excluded.Contains(ch)));
    }

    /// <summary>Exact count of distinct strings the spec permits, summed over its length range.</summary>
    public static BigInteger ValidStringCount(ConstraintSpec spec)
    {
        var excluded = new HashSet<char>(spec.ExcludeChars);
        var allowed = Alphabet.All.Where(c => spec.Classes[c].Allowed).ToList();
        var sizes = allowed.ToDictionary(c => c, c => Alphabet.Pool(c).Count(ch => !excluded.Contains(ch)));
        var los = allowed.ToDictionary(c => c, c => spec.Classes[c].Min);
        var his = allowed.ToDictionary(c => c, c => spec.Classes[c].Max ?? int.MaxValue);
        int sumMin = los.Values.Sum();

        IEnumerable<int> lengths;
        var length = spec.Length;
        if (length.Exact is { } exact)
        {
            lengths = [exact];
        }
        else
        {
            int lo = length.Min ?? Math.Max(sumMin, 1);
            int hi = length.Max ?? lo;
            int start = Math.Max(lo, sumMin);
            lengths = start <= hi ? Enumerable.Range(start, hi - start + 1) : [];
        }

        // Fast path: when no class carries a min/max, the count of length-n strings
        // is just charset^n — no multinomial. This avoids the O(length^3) big-integer
        // convolution that makes long unconstrained passwords (e.g. 1000 chars, all
        // classes) take tens of seconds; the closed form is milliseconds and exact.
        if (allowed.All(c => los[c] == 0 && his[c] == int.MaxValue))
        {
            int charset = sizes.Values.Sum();
            BigInteger fast = BigInteger.Zero;
            foreach (var n in lengths)
            {
                fast += BigInteger.Pow(charset, n);
            }

            return fast;
        }

        BigInteger total = BigInteger.Zero;
        foreach (var n in lengths)
        {
            if (n >= sumMin)
            {
                total += CountStrings(n, allowed, sizes, los, his);
            }
        }

        return total;
    }

    public static double Bits(ConstraintSpec spec)
    {
        var n = ValidStringCount(spec);
        return n > BigInteger.Zero ? BigInteger.Log(n, 2.0) : 0.0;
    }

    public static string StrengthLabel(double bits) => bits switch
    {
        < 28 => "very weak",
        < 36 => "weak",
        < 60 => "reasonable",
        < 80 => "strong",
        < 112 => "very strong",
        _ => "overkill",
    };

    /// <summary>Average brute-force time at a given guess rate (half the space).</summary>
    public static string CrackTime(double bits, double guessesPerSecond = 1e12)
    {
        double seconds = Math.Pow(2.0, bits) / 2.0 / guessesPerSecond;
        (string Unit, double Size)[] units =
        [
            ("years", 365.25 * 86400),
            ("days", 86400),
            ("hours", 3600),
            ("seconds", 1),
        ];

        foreach (var (unit, size) in units)
        {
            if (seconds >= size)
            {
                return $"{seconds / size:N1} {unit}";
            }
        }

        return $"{seconds:E2} seconds";
    }

    // number of length-L strings where each class appears within [lo, hi] times.
    // dp[n] = arrangements of length n using the classes processed so far;
    // add a class with `cnt` chars by choosing their positions: C(n+cnt, cnt) * size^cnt.
    private static BigInteger CountStrings(
        int length,
        List<CharacterClass> classes,
        IReadOnlyDictionary<CharacterClass, int> sizes,
        IReadOnlyDictionary<CharacterClass, int> los,
        IReadOnlyDictionary<CharacterClass, int> his)
    {
        var dp = new BigInteger[length + 1];
        dp[0] = BigInteger.One;

        foreach (var c in classes)
        {
            var next = new BigInteger[length + 1];
            int size = sizes[c];
            int lo = los[c];
            int hi = Math.Min(his[c], length);

            for (int n = 0; n <= length; n++)
            {
                if (dp[n].IsZero)
                {
                    continue;
                }

                for (int cnt = lo; cnt <= hi && n + cnt <= length; cnt++)
                {
                    next[n + cnt] += dp[n] * Binomial(n + cnt, cnt) * BigInteger.Pow(size, cnt);
                }
            }

            dp = next;
        }

        return dp[length];
    }

    private static BigInteger Binomial(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return BigInteger.Zero;
        }

        k = Math.Min(k, n - k);
        BigInteger result = BigInteger.One;
        for (int i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i; // exact: running value is always integral
        }

        return result;
    }
}
