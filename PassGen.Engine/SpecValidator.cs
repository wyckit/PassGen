namespace PassGen.Engine;

/// <summary>Raised when a <see cref="ConstraintSpec"/> cannot possibly be satisfied.</summary>
public sealed class SpecException(string message) : Exception(message);

public static class SpecValidator
{
    /// <summary>Throws <see cref="SpecException"/> if the spec is infeasible.</summary>
    public static void Validate(ConstraintSpec spec)
    {
        var allowed = Alphabet.All.Where(c => spec.Classes[c].Allowed).ToList();
        if (allowed.Count == 0)
        {
            throw new SpecException("no character class is allowed — nothing to generate from");
        }

        foreach (var c in Alphabet.All)
        {
            var cc = spec.Classes[c];
            if (cc.Min > 0 && !cc.Allowed)
            {
                throw new SpecException($"'{c}' has min={cc.Min} but is not allowed");
            }

            if (cc.Max is { } max && cc.Min > max)
            {
                throw new SpecException($"'{c}' has min={cc.Min} > max={max}");
            }
        }

        int sumMin = allowed.Sum(c => spec.Classes[c].Min);
        double sumMax = allowed.Sum(c => spec.Classes[c].Max is { } m ? (double)m : double.PositiveInfinity);

        var length = spec.Length;
        if (length.Exact is { } exact)
        {
            if (exact < sumMin || exact > sumMax)
            {
                var hi = double.IsInfinity(sumMax) ? "∞" : sumMax.ToString();
                throw new SpecException($"length {exact} is outside the feasible range [{sumMin}, {hi}]");
            }
        }
        else
        {
            if (length.Min is { } lo && length.Max is { } hi && lo > hi)
            {
                throw new SpecException($"length min {lo} > max {hi}");
            }

            if (length.Max is { } hiMax && hiMax < sumMin)
            {
                throw new SpecException($"length max {hiMax} < required minimum {sumMin}");
            }

            if (length.Min is { } loMin && loMin > sumMax)
            {
                throw new SpecException($"length min {loMin} exceeds capacity {sumMax}");
            }
        }

        var excluded = new HashSet<char>(spec.ExcludeChars);
        foreach (var ch in spec.IncludeChars)
        {
            var cls = Alphabet.ClassOf(ch);
            if (cls is null)
            {
                throw new SpecException($"include char '{ch}' is not in the alphabet");
            }

            if (!allowed.Contains(cls.Value))
            {
                throw new SpecException($"include char '{ch}' belongs to the disallowed class '{cls}'");
            }

            if (excluded.Contains(ch))
            {
                throw new SpecException($"include char '{ch}' is also in exclude_chars");
            }
        }
    }

    /// <summary>Verifies a produced string against its spec. Returns success and any violations.</summary>
    public static (bool Ok, IReadOnlyList<string> Violations) CheckString(string value, ConstraintSpec spec)
    {
        var violations = new List<string>();
        var allowed = Alphabet.All.Where(c => spec.Classes[c].Allowed).ToHashSet();
        var excluded = new HashSet<char>(spec.ExcludeChars);

        var length = spec.Length;
        if (length.Exact is { } exact && value.Length != exact)
        {
            violations.Add($"length {value.Length} != exact {exact}");
        }

        if (length.Min is { } lo && value.Length < lo)
        {
            violations.Add($"length {value.Length} < min {lo}");
        }

        if (length.Max is { } hi && value.Length > hi)
        {
            violations.Add($"length {value.Length} > max {hi}");
        }

        var counts = Alphabet.All.ToDictionary(c => c, _ => 0);
        foreach (var ch in value)
        {
            if (excluded.Contains(ch))
            {
                violations.Add($"contains excluded char '{ch}'");
            }

            var cls = Alphabet.ClassOf(ch);
            if (cls is null)
            {
                violations.Add($"char '{ch}' is outside the alphabet");
                continue;
            }

            counts[cls.Value]++;
            if (!allowed.Contains(cls.Value))
            {
                violations.Add($"char '{ch}' is from the disallowed class '{cls}'");
            }
        }

        foreach (var c in Alphabet.All)
        {
            var cc = spec.Classes[c];
            if (cc.Min > 0 && counts[c] < cc.Min)
            {
                violations.Add($"'{c}' count {counts[c]} < min {cc.Min}");
            }

            if (cc.Max is { } max && counts[c] > max)
            {
                violations.Add($"'{c}' count {counts[c]} > max {max}");
            }
        }

        foreach (var ch in spec.IncludeChars)
        {
            if (!value.Contains(ch))
            {
                violations.Add($"missing required include char '{ch}'");
            }
        }

        return (violations.Count == 0, violations);
    }
}
