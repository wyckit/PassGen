namespace PassGen.Engine;

/// <summary>
/// Deterministic generator: <see cref="ConstraintSpec"/> in, string out.
///
/// Randomness: <paramref name="seed"/> null (default) =&gt; cryptographically secure RNG
/// (use for real passwords); an int seed =&gt; reproducible <see cref="Random"/> (tests only).
///
/// Uniformity: the fill step samples each character uniformly from the union of all allowed,
/// non-maxed characters, so every allowed character is equally likely. The output is
/// guaranteed to satisfy every minimum and never violate a maximum / allowed-set / exclusion.
/// </summary>
public static class StringGenerator
{
    public const int DefaultLength = 16;

    public static string Generate(ConstraintSpec spec, int? seed = null)
    {
        SpecValidator.Validate(spec);
        IRng rng = seed is null ? new CryptoRng() : new SeededRng(seed.Value);

        var pools = BuildPools(spec);
        var allowed = Alphabet.All.Where(c => spec.Classes[c].Allowed).ToList();
        var counts = allowed.ToDictionary(c => c, _ => 0);
        var output = new List<char>();

        // 1) satisfy mandatory minimums (must be of that class -> uniform within the class)
        foreach (var c in allowed)
        {
            for (int k = 0; k < spec.Classes[c].Min; k++)
            {
                output.Add(rng.Choice(pools[c]));
                counts[c]++;
            }
        }

        // 2) place required include_chars (count them against their class)
        foreach (var ch in spec.IncludeChars)
        {
            output.Add(ch);
            if (Alphabet.ClassOf(ch) is { } cls && counts.ContainsKey(cls))
            {
                counts[cls]++;
            }
        }

        // 3) resolve target length and fill the rest uniformly over every eligible character
        int need = output.Count;
        int target = Math.Max(ResolveLength(spec, need, rng), need);

        while (output.Count < target)
        {
            var eligible = new List<char>();
            foreach (var c in allowed)
            {
                if (HasCapacity(spec, counts, pools, c))
                {
                    eligible.AddRange(pools[c]);
                }
            }

            if (eligible.Count == 0)
            {
                break; // every allowed class hit its max; validation bounds this case
            }

            var ch = rng.Choice(eligible);
            output.Add(ch);
            counts[Alphabet.ClassOf(ch)!.Value]++;
        }

        rng.Shuffle(output);
        return new string(output.ToArray());
    }

    public static IReadOnlyList<string> GenerateMany(ConstraintSpec spec, int count, int? seed = null)
    {
        if (seed is null)
        {
            return Enumerable.Range(0, count).Select(_ => Generate(spec)).ToList();
        }

        var rng = new Random(seed.Value);
        return Enumerable.Range(0, count).Select(_ => Generate(spec, rng.Next())).ToList();
    }

    private static bool HasCapacity(
        ConstraintSpec spec,
        IReadOnlyDictionary<CharacterClass, int> counts,
        IReadOnlyDictionary<CharacterClass, List<char>> pools,
        CharacterClass c)
    {
        var max = spec.Classes[c].Max;
        return (max is null || counts[c] < max) && pools[c].Count > 0;
    }

    private static Dictionary<CharacterClass, List<char>> BuildPools(ConstraintSpec spec)
    {
        var excluded = new HashSet<char>(spec.ExcludeChars);
        return Alphabet.All.ToDictionary(
            c => c,
            c => Alphabet.Pool(c).Where(ch => !excluded.Contains(ch)).ToList());
    }

    private static int ResolveLength(ConstraintSpec spec, int need, IRng rng)
    {
        var length = spec.Length;
        if (length.Exact is { } exact)
        {
            return exact;
        }

        int? lo = length.Min;
        int? hi = length.Max;

        int low;
        if (lo is null)
        {
            low = hi is null ? Math.Max(need, DefaultLength) : Math.Min(need == 0 ? 1 : need, hi.Value);
        }
        else
        {
            low = lo.Value;
        }

        low = Math.Max(low, need);
        int high = hi ?? Math.Max(low, DefaultLength);
        high = Math.Max(high, low);
        return rng.NextInclusive(low, high);
    }
}
