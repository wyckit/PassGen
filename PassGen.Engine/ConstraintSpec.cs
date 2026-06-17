namespace PassGen.Engine;

/// <summary>The four character classes the generator draws from.</summary>
public enum CharacterClass
{
    Uppercase,
    Lowercase,
    Numeric,
    Symbol,
}

/// <summary>Per-class constraint: whether the class is allowed, and its min/max count.</summary>
public sealed record ClassConstraint(bool Allowed = true, int Min = 0, int? Max = null);

/// <summary>Length constraint: an exact length, or a Min/Max range, or neither (generator default).</summary>
public sealed record LengthConstraint(int? Exact = null, int? Min = null, int? Max = null);

/// <summary>
/// The contract between the language front-end and the deterministic generator.
/// By default every class is allowed; narrow it with <see cref="OnlyAllow"/> / per-class
/// constraints. This mirrors the Python <c>constraint-spec.schema.json</c>.
/// </summary>
public sealed record ConstraintSpec
{
    public LengthConstraint Length { get; init; } = new();

    public IReadOnlyDictionary<CharacterClass, ClassConstraint> Classes { get; init; } = AllAllowed();

    public IReadOnlyList<char> ExcludeChars { get; init; } = [];

    public IReadOnlyList<char> IncludeChars { get; init; } = [];

    public static ConstraintSpec Default() => new();

    private static Dictionary<CharacterClass, ClassConstraint> AllAllowed()
    {
        var d = new Dictionary<CharacterClass, ClassConstraint>();
        foreach (var c in Alphabet.All)
        {
            d[c] = new ClassConstraint();
        }
        return d;
    }

    // -- small immutable builders, handy for callers and tests --

    public ConstraintSpec With(CharacterClass c, ClassConstraint constraint) =>
        this with { Classes = new Dictionary<CharacterClass, ClassConstraint>(Classes) { [c] = constraint } };

    public ConstraintSpec WithLength(LengthConstraint length) => this with { Length = length };

    public ConstraintSpec WithExcludeAmbiguous() =>
        this with { ExcludeChars = Alphabet.Ambiguous.ToArray() };

    /// <summary>Allow only the named classes; forbid the rest.</summary>
    public static ConstraintSpec OnlyAllow(params CharacterClass[] allow)
    {
        var d = new Dictionary<CharacterClass, ClassConstraint>();
        foreach (var c in Alphabet.All)
        {
            d[c] = new ClassConstraint(Allowed: Array.IndexOf(allow, c) >= 0);
        }
        return new ConstraintSpec { Classes = d };
    }
}
