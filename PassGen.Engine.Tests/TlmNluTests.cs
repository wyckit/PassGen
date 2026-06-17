using PassGen.Engine;
using Xunit;

namespace PassGen.Engine.Tests;

/// <summary>
/// Hand-written paraphrase tests for the TLM-driven resolver (the TLM is the model).
/// These assert the resolved ConstraintSpec — proving graph-driven understanding of
/// per-class min/max/exact, ranges, only-as-max, of-each, groups, pin, no-ambiguous,
/// and CASE-PRESERVED include/exclude.
/// </summary>
public class TlmNluTests
{
    private static readonly TlmNlu Nlu = new(FindBundle());

    private static ConstraintSpec Spec(string english) => Nlu.Resolve(english).Args.ToSpec();

    private static ClassConstraint C(ConstraintSpec s, CharacterClass c) => s.Classes[c];

    [Fact]
    public void Length_and_perclass_min_and_no_ambiguous()
    {
        var s = Spec("make me a 16 character password with at least 2 uppercase and no ambiguous");
        Assert.Equal(16, s.Length.Exact);
        Assert.Equal(2, C(s, CharacterClass.Uppercase).Min);
        Assert.Contains('0', s.ExcludeChars);   // ambiguous set applied
        Assert.Contains('O', s.ExcludeChars);
    }

    [Fact]
    public void Max_and_min_across_classes_with_postfix_plus()
    {
        var s = Spec("a 20-char password, at most 2 lowercase, 3+ digits");
        Assert.Equal(20, s.Length.Exact);
        Assert.Equal(2, C(s, CharacterClass.Lowercase).Max);
        Assert.Equal(3, C(s, CharacterClass.Numeric).Min);
    }

    [Fact]
    public void Only_N_class_means_max()
    {
        var s = Spec("only 2 digits, 12 long");
        Assert.Equal(12, s.Length.Exact);
        Assert.Equal(2, C(s, CharacterClass.Numeric).Max);
    }

    [Fact]
    public void Per_class_range()
    {
        var s = Spec("2 to 4 symbols, 16 chars");
        Assert.Equal(16, s.Length.Exact);
        Assert.Equal(2, C(s, CharacterClass.Symbol).Min);
        Assert.Equal(4, C(s, CharacterClass.Symbol).Max);
    }

    [Fact]
    public void Exactly_one_of_each()
    {
        var s = Spec("exactly one of each, 12 characters");
        Assert.Equal(12, s.Length.Exact);
        Assert.Equal(1, C(s, CharacterClass.Uppercase).Min);
        Assert.Equal(1, C(s, CharacterClass.Lowercase).Min);
        Assert.Equal(1, C(s, CharacterClass.Numeric).Min);
        Assert.Equal(1, C(s, CharacterClass.Symbol).Min);
    }

    [Fact]
    public void Letters_only_and_mixed_case()
    {
        var s = Spec("letters only, mixed case, 24 long");
        Assert.Equal(24, s.Length.Exact);
        Assert.True(C(s, CharacterClass.Uppercase).Allowed);
        Assert.True(C(s, CharacterClass.Lowercase).Allowed);
        Assert.False(C(s, CharacterClass.Numeric).Allowed);
        Assert.False(C(s, CharacterClass.Symbol).Allowed);
    }

    [Fact]
    public void Memorable_pin()
    {
        var s = Spec("a memorable pin");
        Assert.True(C(s, CharacterClass.Numeric).Allowed);
        Assert.False(C(s, CharacterClass.Uppercase).Allowed);
        Assert.False(C(s, CharacterClass.Symbol).Allowed);
        Assert.Equal(4, s.Length.Exact);
    }

    [Fact]
    public void Exclude_chars_are_case_preserved()
    {
        var s = Spec("16 char password exclude 0 O 1 l I");
        Assert.Equal(16, s.Length.Exact);
        Assert.Contains('O', s.ExcludeChars);   // uppercase O kept
        Assert.Contains('I', s.ExcludeChars);   // uppercase I kept
        Assert.Contains('l', s.ExcludeChars);
        Assert.DoesNotContain('o', s.ExcludeChars);
    }

    [Fact]
    public void Include_chars_drop_connectors()
    {
        var s = Spec("must contain @ and #, 16 chars");
        Assert.Contains('@', s.IncludeChars);
        Assert.Contains('#', s.IncludeChars);
        Assert.DoesNotContain('a', s.IncludeChars);   // 'and' must not leak
        Assert.DoesNotContain('n', s.IncludeChars);
    }

    [Fact]
    public void Bare_quantifier_defaults_to_length()
    {
        var s = Spec("no look-alikes, at least 16");
        Assert.Equal(16, s.Length.Min);
        Assert.Contains('0', s.ExcludeChars);   // no-ambiguous
    }

    [Fact]
    public void No_class_means_deny()
    {
        var s = Spec("no symbols, 20 long");
        Assert.Equal(20, s.Length.Exact);
        Assert.False(C(s, CharacterClass.Symbol).Allowed);
    }

    // Corpus harvested from the retired Python tests/test_parser_paraphrases.py — kept as a
    // regression benchmark for the TLM-driven resolver. (Two legacy phrasings are known gaps:
    // "just lowercase" (bare, no number) and "drop A and B" (multi-class deny) — see backlog.)
    [Fact]
    public void Migrated_legacy_paraphrase_corpus()
    {
        var a = Spec("at least 5 upper and 2 lower, max 16");
        Assert.Equal(5, C(a, CharacterClass.Uppercase).Min);
        Assert.Equal(2, C(a, CharacterClass.Lowercase).Min);
        Assert.Equal(16, a.Length.Max);

        var b = Spec("16 character password with no symbols and at least 3 numbers");
        Assert.Equal(16, b.Length.Exact);
        Assert.False(C(b, CharacterClass.Symbol).Allowed);
        Assert.Equal(3, C(b, CharacterClass.Numeric).Min);

        var c = Spec("only digits, exactly 8 characters");
        Assert.True(C(c, CharacterClass.Numeric).Allowed);
        Assert.False(C(c, CharacterClass.Uppercase).Allowed);
        Assert.Equal(8, c.Length.Exact);

        var d = Spec("a 12-char token, at least 2 caps, up to 3 specials");
        Assert.Equal(12, d.Length.Exact);
        Assert.Equal(2, C(d, CharacterClass.Uppercase).Min);   // caps -> uppercase
        Assert.Equal(3, C(d, CharacterClass.Symbol).Max);      // specials -> symbol, up to -> max

        var e = Spec("letters only, between 10 and 14 characters");
        Assert.False(C(e, CharacterClass.Numeric).Allowed);
        Assert.Equal(10, e.Length.Min);
        Assert.Equal(14, e.Length.Max);

        var f = Spec("no symbols, no ambiguous characters, minimum 8 long");
        Assert.False(C(f, CharacterClass.Symbol).Allowed);
        Assert.Equal(8, f.Length.Min);
        Assert.Contains('0', f.ExcludeChars);

        var g = Spec("exactly 3 uppercase and 3 digits, 12 chars");
        Assert.Equal(3, C(g, CharacterClass.Uppercase).Min);
        Assert.Equal(3, C(g, CharacterClass.Uppercase).Max);
        Assert.Equal(3, C(g, CharacterClass.Numeric).Min);
        Assert.Equal(12, g.Length.Exact);

        var h = Spec("alphanumeric only, max 20");
        Assert.False(C(h, CharacterClass.Symbol).Allowed);
        Assert.True(C(h, CharacterClass.Numeric).Allowed);
        Assert.Equal(20, h.Length.Max);

        var i = Spec("give me a 24 character password, at least 4 symbols and 4 digits");
        Assert.Equal(24, i.Length.Exact);
        Assert.Equal(4, C(i, CharacterClass.Symbol).Min);
        Assert.Equal(4, C(i, CharacterClass.Numeric).Min);

        var j = Spec("exclude lowercase, max 6");
        Assert.False(C(j, CharacterClass.Lowercase).Allowed);
        Assert.Equal(6, j.Length.Max);
    }

    // Gaps closed after the engram/expert research pass.
    [Fact]
    public void Closed_gaps()
    {
        var a = Spec("uppercase and numbers only, 8 to 12 characters");   // multi-class only
        Assert.True(C(a, CharacterClass.Uppercase).Allowed);
        Assert.True(C(a, CharacterClass.Numeric).Allowed);
        Assert.False(C(a, CharacterClass.Lowercase).Allowed);
        Assert.False(C(a, CharacterClass.Symbol).Allowed);
        Assert.Equal(8, a.Length.Min); Assert.Equal(12, a.Length.Max);

        var b = Spec("just lowercase, 15 long");                          // bare single only
        Assert.True(C(b, CharacterClass.Lowercase).Allowed);
        Assert.False(C(b, CharacterClass.Uppercase).Allowed);
        Assert.Equal(15, b.Length.Exact);

        var c = Spec("drop symbols and digits, at least 10 characters");  // multi-class deny (and)
        Assert.False(C(c, CharacterClass.Symbol).Allowed);
        Assert.False(C(c, CharacterClass.Numeric).Allowed);
        Assert.Equal(10, c.Length.Min);

        var d = Spec("no symbols or digits, 16 chars");                   // multi-class deny (or)
        Assert.False(C(d, CharacterClass.Symbol).Allowed);
        Assert.False(C(d, CharacterClass.Numeric).Allowed);

        var e = Spec("at least 8 and at most 20 characters");            // separate length min+max
        Assert.Equal(8, e.Length.Min); Assert.Equal(20, e.Length.Max);

        var f = Spec("3 symbols maximum, 16 chars");                      // postfix max
        Assert.Equal(3, C(f, CharacterClass.Symbol).Max);

        var g = Spec("2 digits exactly, 12 chars");                       // postfix exact
        Assert.Equal(2, C(g, CharacterClass.Numeric).Min);
        Assert.Equal(2, C(g, CharacterClass.Numeric).Max);

        var h = Spec("must contain a digit, 16 chars");                   // article -> min 1
        Assert.True(C(h, CharacterClass.Numeric).Min >= 1);

        var i = Spec("12-16 chars, no symbols");                          // hyphen range
        Assert.Equal(12, i.Length.Min); Assert.Equal(16, i.Length.Max);
        Assert.False(C(i, CharacterClass.Symbol).Allowed);
    }

    [Fact]
    public void Out_of_scope_asks_are_flagged()
    {
        Assert.NotEmpty(Nlu.Resolve("must start with a capital, 16 chars").Unsupported);
        Assert.NotEmpty(Nlu.Resolve("a pronounceable 12 character password").Unsupported);
        Assert.NotEmpty(Nlu.Resolve("no repeated characters, 16 long").Unsupported);
        Assert.NotEmpty(Nlu.Resolve("a hex string, 32 chars").Unsupported);
        Assert.Empty(Nlu.Resolve("16 char password, 2 uppercase").Unsupported);   // normal req: none
    }

    [Fact]
    public void Length_noun_first_and_max_only_target()
    {
        // noun-first length phrasings
        Assert.Equal(16, Spec("length 16").Length.Exact);
        Assert.Equal(24, Spec("max length of 24").Length.Exact);
        var a = Spec("min 4 uppercase, max 5 lowercase, no special characters, max length 17");
        Assert.Equal(17, a.Length.Exact);
        Assert.Equal(4, C(a, CharacterClass.Uppercase).Min);
        Assert.Equal(5, C(a, CharacterClass.Lowercase).Max);
        Assert.False(C(a, CharacterClass.Symbol).Allowed);

        // pure max-only -> generated AT the max (keeps entropy honest, predictable)
        Assert.Equal(10, Spec("max 10").Length.Exact);
        Assert.Equal(99, Spec("max 99").Length.Exact);
        Assert.Equal(16, Spec("at most 16 characters").Length.Exact);

        // but a min+max stays a true range
        var r = Spec("min 8 max 20 characters");
        Assert.Equal(8, r.Length.Min); Assert.Equal(20, r.Length.Max); Assert.Null(r.Length.Exact);
    }

    private static string FindBundle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var c = Path.Combine(dir.FullName, "dataset", "compiled");
            if (Directory.Exists(c) && Directory.GetFiles(c, "rs-*.tlmz").Length > 0) return c;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("dataset/compiled (rs-*.tlmz) not found above " + AppContext.BaseDirectory);
    }
}
