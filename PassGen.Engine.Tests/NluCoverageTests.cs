using PassGen.Engine;
using PassGen.Tlm;
using Xunit;

namespace PassGen.Engine.Tests;

/// <summary>
/// Broad linguistic coverage matrix for the TLM-driven resolver. Each row is an English
/// phrasing + a tiny expectation DSL (semicolon-separated):
///   len=N len.min=N len.max=N
///   up/lo/nu/sy . {min|max|exact} =N    |  up=on  up=off   |  up.min1+   (>= 1)
///   excl=chars  excl!=chars  incl=chars   ambig   unsup
/// (up=uppercase lo=lowercase nu=numeric sy=symbol)
/// </summary>
public class NluCoverageTests
{
    private static readonly TlmNlu Nlu = new(FindBundle());

    [Theory]
    // ── length: number-first ────────────────────────────────────────────────
    [InlineData("16 characters", "len=16")]
    [InlineData("16 chars", "len=16")]
    [InlineData("a 16 char password", "len=16")]
    [InlineData("16 long", "len=16")]
    [InlineData("a 16-character password", "len=16")]
    [InlineData("exactly 16 characters", "len=16")]
    [InlineData("sixteen characters", "len=16")]
    [InlineData("twenty-four characters", "len=24")]
    // ── length: noun-first ──────────────────────────────────────────────────
    [InlineData("length 16", "len=16")]
    [InlineData("length of 20", "len=20")]
    [InlineData("max length 17", "len=17")]
    [InlineData("maximum length of 24", "len=24")]
    // ── length: bounds & ranges ─────────────────────────────────────────────
    [InlineData("at least 12 characters", "len.min=12")]
    [InlineData("no fewer than 10 characters", "len.min=10")]
    [InlineData("8 or more characters", "len.min=8")]
    [InlineData("at most 20 characters", "len=20")]          // pure max -> target
    [InlineData("max 12", "len=12")]
    [InlineData("max 99", "len=99")]
    [InlineData("between 12 and 20 characters", "len.min=12;len.max=20")]
    [InlineData("12 to 20 chars", "len.min=12;len.max=20")]
    [InlineData("12-16 chars", "len.min=12;len.max=16")]
    [InlineData("from 8 to 16 characters", "len.min=8;len.max=16")]
    [InlineData("at least 8 and at most 20 characters", "len.min=8;len.max=20")]
    [InlineData("10 max", "len=10")]            // postfix bare length
    [InlineData("8 min", "len.min=8")]
    [InlineData("no numbers, 12 max", "len=12;nu=off")]
    [InlineData("no special characters, 3 min lower, 3 min upper, no numbers, 10 max", "len=10;up.min=3;lo.min=3;nu=off;sy=off")]
    // number + group word -> length N + restrict to that group
    [InlineData("10 letters", "len=10;up=on;lo=on;nu=off;sy=off")]
    [InlineData("10 letters and numbers", "len=10;up=on;lo=on;nu=on;sy=off")]
    [InlineData("max 10 letters", "len=10;up=on;lo=on;nu=off")]
    [InlineData("at least 10 letters", "len.min=10;nu=off;sy=off")]
    // ── per-class minimum ───────────────────────────────────────────────────
    [InlineData("at least 2 uppercase, 16 chars", "up.min=2;len=16")]
    [InlineData("2 uppercase, 16 chars", "up.min=2")]
    [InlineData("minimum 2 symbols, 16 chars", "sy.min=2")]
    [InlineData("2+ digits, 16 chars", "nu.min=2")]
    [InlineData("at least one digit, 16 chars", "nu.min=1")]
    [InlineData("must contain a digit, 16 chars", "nu.min1+")]
    [InlineData("no fewer than 2 digits, 16 chars", "nu.min=2")]
    [InlineData("at least 2 caps, 16 chars", "up.min=2")]
    [InlineData("2 capital letters, 16 chars", "up.min=2")]
    [InlineData("2 uppercase minimum, 16 chars", "up.min=2")]
    // ── per-class maximum ───────────────────────────────────────────────────
    [InlineData("at most 3 symbols, 16 chars", "sy.max=3")]
    [InlineData("no more than 3 symbols, 16 chars", "sy.max=3")]
    [InlineData("up to 3 symbols, 16 chars", "sy.max=3")]
    [InlineData("maximum 3 digits, 16 chars", "nu.max=3")]
    [InlineData("3 symbols maximum, 16 chars", "sy.max=3")]
    [InlineData("only 2 digits, 16 chars", "nu.max=2")]
    [InlineData("3 or fewer symbols, 16 chars", "sy.max=3")]
    // ── per-class exact ─────────────────────────────────────────────────────
    [InlineData("exactly 2 uppercase, 16 chars", "up.exact=2")]
    [InlineData("precisely 2 digits, 16 chars", "nu.exact=2")]
    [InlineData("2 digits exactly, 16 chars", "nu.exact=2")]
    [InlineData("exactly one of each, 12 chars", "up.min=1;lo.min=1;nu.min=1;sy.min=1")]
    [InlineData("1 of each, 12 chars", "up.min=1;lo.min=1;nu.min=1;sy.min=1")]
    // ── restriction (only / group / multi) ──────────────────────────────────
    [InlineData("letters only, 16 chars", "up=on;lo=on;nu=off;sy=off")]
    [InlineData("only letters, 16 chars", "up=on;lo=on;nu=off;sy=off")]
    [InlineData("just letters, 16 chars", "up=on;lo=on;nu=off;sy=off")]
    [InlineData("mixed case, 16 chars", "up=on;lo=on;nu=off;sy=off")]
    [InlineData("alphanumeric, 16 chars", "up=on;lo=on;nu=on;sy=off")]
    [InlineData("alphanumeric only, 16 chars", "nu=on;sy=off")]
    [InlineData("digits only, 8 chars", "nu=on;up=off;lo=off;sy=off")]
    [InlineData("numbers only, 8 chars", "nu=on;up=off")]
    [InlineData("uppercase only, 16 chars", "up=on;lo=off;nu=off;sy=off")]
    [InlineData("lowercase only, 16 chars", "lo=on;up=off")]
    [InlineData("just lowercase, 16 chars", "lo=on;up=off;nu=off;sy=off")]
    [InlineData("symbols only, 16 chars", "sy=on;up=off;lo=off;nu=off")]
    [InlineData("all caps, 16 chars", "up=on;lo=off")]
    [InlineData("uppercase and numbers only, 16 chars", "up=on;nu=on;lo=off;sy=off")]
    [InlineData("letters and numbers only, 16 chars", "up=on;lo=on;nu=on;sy=off")]
    [InlineData("only uppercase and lowercase, 16 chars", "up=on;lo=on;nu=off;sy=off")]
    // ── denial (single + multi, and/or) ─────────────────────────────────────
    [InlineData("no symbols, 16 chars", "sy=off")]
    [InlineData("no special characters, 16 chars", "sy=off")]
    [InlineData("no punctuation, 16 chars", "sy=off")]
    [InlineData("no digits, 16 chars", "nu=off")]
    [InlineData("without numbers, 16 chars", "nu=off")]
    [InlineData("no caps, 16 chars", "up=off")]
    [InlineData("exclude lowercase, 16 chars", "lo=off")]
    [InlineData("drop symbols and digits, 16 chars", "sy=off;nu=off")]
    [InlineData("no symbols or digits, 16 chars", "sy=off;nu=off")]
    [InlineData("without uppercase or symbols, 16 chars", "up=off;sy=off")]
    [InlineData("no letters, 16 chars", "up=off;lo=off;nu=on;sy=on")]
    [InlineData("no letters or numbers, 16 chars", "up=off;lo=off;nu=off")]
    // ── number-then-quantifier-then-class ordering ──────────────────────────
    [InlineData("2 min lowercase, 3 min uppercase, 9 chars", "lo.min=2;up.min=3;len=9")]
    [InlineData("3 max symbols, 16 chars", "sy.max=3")]
    [InlineData("1 max symbols, 16 chars", "sy.max=1")]
    // ── include / exclude characters (case-preserved) ───────────────────────
    [InlineData("exclude 0 O 1 l I, 16 chars", "excl=0O1lI;excl!=o")]
    [InlineData("exclude \"0Oo1lI\", 16 chars", "excl=0Oo1lI")]
    [InlineData("must contain @ and #, 16 chars", "incl=@#;incl!=a")]
    [InlineData("include \"@#%\", 16 chars", "incl=@#%")]
    // ── ambiguous synonyms ──────────────────────────────────────────────────
    [InlineData("no ambiguous, 16 chars", "ambig")]
    [InlineData("no ambiguous characters, 16 chars", "ambig")]
    [InlineData("unambiguous, 16 chars", "ambig")]
    [InlineData("readable, 16 chars", "ambig")]
    [InlineData("no look-alikes, 16 chars", "ambig")]
    [InlineData("no confusables, 16 chars", "ambig")]
    [InlineData("memorable, 16 chars", "ambig")]
    // ── presets ─────────────────────────────────────────────────────────────
    [InlineData("4 digit pin", "nu=on;up=off;lo=off;sy=off;len=4")]
    [InlineData("6 digit pin", "nu=on;len=6")]
    [InlineData("pin", "nu=on;up=off;len=4")]
    // ── compound sentences ──────────────────────────────────────────────────
    [InlineData("16 char password with at least 2 uppercase and no ambiguous", "len=16;up.min=2;ambig")]
    [InlineData("min 4 uppercase, max 5 lowercase, no special characters, max length 17", "len=17;up.min=4;lo.max=5;sy=off")]
    [InlineData("20 chars, 2 to 4 symbols, no look-alikes", "len=20;sy.min=2;sy.max=4;ambig")]
    // ── out-of-scope (graceful note) ────────────────────────────────────────
    [InlineData("must start with a capital, 16 chars", "unsup")]
    [InlineData("no repeated characters, 16 chars", "unsup")]
    [InlineData("a pronounceable password", "unsup")]
    [InlineData("a passphrase of 4 words", "unsup")]
    [InlineData("a 32 char hex string", "unsup")]
    [InlineData("base64, 24 chars", "unsup")]
    [InlineData("max 10 letters, 16 chars", "unsup")]          // count on whole letters group -> note
    [InlineData("at least 5 alphanumeric, 16 chars", "unsup")] // count on whole alphanumeric group -> note
    public void Coverage(string english, string expect)
    {
        var r = Nlu.Resolve(english);
        var s = r.Args.ToSpec();
        foreach (var clause in expect.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Check(s, r, clause, english);
    }

    // ════════════════════════════════════════════════════════════════════════
    // SYSTEMATIC coverage: generated FROM the TLM vocabulary itself, so every cue
    // synonym and class alias is exercised in a canonical frame. Auto-covers any
    // new synonym added to DatasetAuthor. A failure here = a real vocabulary gap.
    // ════════════════════════════════════════════════════════════════════════
    [Theory]
    [MemberData(nameof(Vocabulary))]
    public void EveryVocabularyItem(string english, string expect)
    {
        var r = Nlu.Resolve(english);
        var s = r.Args.ToSpec();
        foreach (var clause in expect.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Check(s, r, clause, english);
    }

    public static IEnumerable<object[]> Vocabulary()
    {
        var dir = FindBundle();
        var compiler = new TlmCompiler();
        var bySignal = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var classAlias = new List<(string Pre, string Alias)>();
        foreach (var f in Directory.GetFiles(dir, "rs-*.tlmz"))
        {
            var pkg = compiler.Deserialize(File.ReadAllBytes(f));
            foreach (var cue in pkg.Cues)
            {
                if (string.IsNullOrWhiteSpace(cue.Signal)) continue;
                var list = bySignal.TryGetValue(cue.Signal, out var l) ? l : (bySignal[cue.Signal] = new());
                list.AddRange(cue.Trigger.Split('/').Select(x => x.Trim()).Where(x => x.Length > 0));
            }
            foreach (var c in pkg.Concepts.Where(c => c.Category == "CharClass"))
            {
                var pre = Pre(c.Label);
                if (pre is null) continue;
                classAlias.Add((pre, c.Label));
                foreach (var a in c.Aliases) classAlias.Add((pre, a));
            }
        }
        List<string> Syn(string sig) => bySignal.TryGetValue(sig, out var l) ? l : new();
        bool Prefixable(string s) => !s.Contains(" or ");   // skip postfix-only "or more/fewer/less"

        var rows = new List<object[]>();
        void Add(string english, string expect) => rows.Add(new object[] { english, expect });

        // quantifiers (prefix frame), all classes touched via a representative alias
        foreach (var s in Syn("q.min").Where(Prefixable)) Add($"{s} 2 uppercase, 16 chars", "up.min=2");
        foreach (var s in Syn("q.max").Where(Prefixable)) Add($"{s} 3 symbols, 16 chars", "sy.max=3");
        foreach (var s in Syn("q.exact")) Add($"{s} 2 digits, 16 chars", "nu.exact=2");

        // every class alias -> "at least 2 <alias>"
        foreach (var (pre, alias) in classAlias.Distinct()) Add($"at least 2 {alias}, 16 chars", $"{pre}.min=2");

        // length nouns
        foreach (var n in Syn("target.length").Distinct()) Add($"16 {n}", "len=16");

        // readability synonyms
        foreach (var s in Syn("no_ambiguous").Distinct()) Add($"{s}, 16 chars", "ambig");

        // group restrictions (each trigger phrase, standalone)
        foreach (var (sig, phrases) in bySignal)
            if (sig.StartsWith("only.", StringComparison.OrdinalIgnoreCase))
            {
                var csv = sig[5..].Split('+');
                var expect = string.Join(";",
                    csv.Select(c => $"{Pre(c)}=on").Concat(
                    new[] { "uppercase", "lowercase", "numeric", "symbol" }.Where(c => !csv.Contains(c)).Select(c => $"{Pre(c)}=off")));
                foreach (var p in phrases.Distinct()) Add($"{p}, 16 chars", expect);
            }

        // exclude verbs -> deny a class ; include verbs -> include a literal char
        foreach (var v in Syn("exclude").Distinct()) Add($"{v} symbols, 16 chars", "sy=off");
        foreach (var v in Syn("include").Distinct()) Add($"{v} @, 16 chars", "incl=@");

        // out-of-scope triggers -> graceful note
        foreach (var (sig, phrases) in bySignal)
            if (sig.StartsWith("unsupported", StringComparison.OrdinalIgnoreCase))
                foreach (var p in phrases.Distinct()) Add($"{p}, 16 chars", "unsup");

        return rows;
    }

    private static string? Pre(string classLabel) => classLabel.ToLowerInvariant() switch
    {
        "uppercase" => "up", "lowercase" => "lo", "numeric" => "nu", "symbol" => "sy", _ => null
    };

    private static void Check(ConstraintSpec s, TlmNlu.Result r, string clause, string english)
    {
        string Why(string what) => $"[{english}] expected {clause} ({what})";

        if (clause == "ambig") { Assert.True(s.ExcludeChars.Contains('0'), Why("ambiguous excluded")); return; }
        if (clause == "unsup") { Assert.True(r.Unsupported.Count > 0, Why("out-of-scope note")); return; }

        var (key, val) = clause.Contains('=') ? (clause[..clause.IndexOf('=')], clause[(clause.IndexOf('=') + 1)..])
                                              : (clause, "");

        // length
        if (key == "len") { Assert.True(s.Length.Exact == int.Parse(val), Why($"Length.Exact={s.Length.Exact}")); return; }
        if (key == "len.min") { Assert.True(s.Length.Min == int.Parse(val), Why($"Length.Min={s.Length.Min}")); return; }
        if (key == "len.max") { Assert.True(s.Length.Max == int.Parse(val), Why($"Length.Max={s.Length.Max}")); return; }

        // include / exclude characters
        if (key == "excl") { foreach (var c in val) Assert.True(s.ExcludeChars.Contains(c), Why($"exclude has '{c}'")); return; }
        if (key == "excl!") { foreach (var c in val.TrimStart('=')) Assert.True(!s.ExcludeChars.Contains(c), Why($"exclude lacks '{c}'")); return; }
        if (key == "incl") { foreach (var c in val) Assert.True(s.IncludeChars.Contains(c), Why($"include has '{c}'")); return; }
        if (key == "incl!") { foreach (var c in val.TrimStart('=')) Assert.True(!s.IncludeChars.Contains(c), Why($"include lacks '{c}'")); return; }

        // per-class: <cls>.<field>=N  /  <cls>=on|off  /  <cls>.min1+
        var cls = Map(key[..2]);
        var cc = s.Classes[cls];
        if (key.EndsWith(".min1+")) { Assert.True(cc.Min >= 1, Why($"min={cc.Min}")); return; }
        if (!key.Contains('.')) { Assert.True(cc.Allowed == (val == "on"), Why($"allowed={cc.Allowed}")); return; }
        var field = key[3..];
        int n = int.Parse(val);
        if (field == "min") Assert.True(cc.Min == n, Why($"min={cc.Min}"));
        else if (field == "max") Assert.True(cc.Max == n, Why($"max={cc.Max}"));
        else if (field == "exact") Assert.True(cc.Min == n && cc.Max == n, Why($"min={cc.Min},max={cc.Max}"));
        else Assert.Fail($"unknown clause '{clause}'");
    }

    private static CharacterClass Map(string p) => p switch
    {
        "up" => CharacterClass.Uppercase,
        "lo" => CharacterClass.Lowercase,
        "nu" => CharacterClass.Numeric,
        "sy" => CharacterClass.Symbol,
        _ => throw new ArgumentException($"bad class prefix '{p}'"),
    };

    private static string FindBundle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var c = Path.Combine(dir.FullName, "dataset", "compiled");
            if (Directory.Exists(c) && Directory.GetFiles(c, "rs-*.tlmz").Length > 0) return c;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("dataset/compiled not found");
    }
}
