namespace PassGen.Engine;

/// <summary>
/// The atomic character data — the C# port of <c>data/characters/*.json</c>.
/// Symbol class is the 13 "safe" symbols; ambiguous look-alikes are listed for the
/// "exclude ambiguous" feature.
/// </summary>
public static class Alphabet
{
    public const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    public const string Numeric = "0123456789";
    public const string Symbol = "!@#$%^&*-_=+?"; // 13 safe symbols

    /// <summary>Look-alike characters dropped by "exclude ambiguous": 0 O o 1 l I.</summary>
    public static readonly IReadOnlySet<char> Ambiguous = new HashSet<char>("0Oo1lI");

    public static readonly IReadOnlyList<CharacterClass> All =
    [
        CharacterClass.Uppercase,
        CharacterClass.Lowercase,
        CharacterClass.Numeric,
        CharacterClass.Symbol,
    ];

    public static string Pool(CharacterClass c) => c switch
    {
        CharacterClass.Uppercase => Uppercase,
        CharacterClass.Lowercase => Lowercase,
        CharacterClass.Numeric => Numeric,
        CharacterClass.Symbol => Symbol,
        _ => throw new ArgumentOutOfRangeException(nameof(c), c, "unknown character class"),
    };

    /// <summary>Which class a character belongs to (classes are disjoint), or null.</summary>
    public static CharacterClass? ClassOf(char ch)
    {
        if (Uppercase.Contains(ch)) return CharacterClass.Uppercase;
        if (Lowercase.Contains(ch)) return CharacterClass.Lowercase;
        if (Numeric.Contains(ch)) return CharacterClass.Numeric;
        if (Symbol.Contains(ch)) return CharacterClass.Symbol;
        return null;
    }
}
