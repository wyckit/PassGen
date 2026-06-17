using System.Text.RegularExpressions;
using PassGen.Tlm;

namespace PassGen.Engine;

/// <summary>
/// TLM-driven natural-language resolver — "the TLM is the model."
///
/// All vocabulary is loaded from the compiled TLM bundle: class-word synonyms come
/// from rs-char-classes concept Aliases, and phrasings come from rs-nl-vocabulary
/// Cues (Trigger synonyms + a Signal). The grammar (binding numbers/classes/chars,
/// ranges, char capture) is generic code; the WORDS live in the graph, so adding a
/// synonym to the TLM data extends understanding with zero code change.
///
/// No neural model, no external LLM. Emits the same GenerateArgs the generator consumes.
/// </summary>
public sealed class TlmNlu
{
    public sealed record Result(GenerateArgs Args, List<string> Fired, bool HasExplicit, List<string> Unsupported);

    // signal -> trigger phrases (loaded from the TLM cues)
    private readonly Dictionary<string, List<string>> _bySignal = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _classAlias = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _qmin, _qmax, _qexact, _len, _classAlt, _exclV, _inclV, _ambig, _only, _pin, _each;
    private readonly List<(string Alt, string[] Classes)> _groups = new();

    private const string CharCls = @"!@#$%^&*\-_=+?A-Za-z0-9";

    public TlmNlu(string compiledDir)
    {
        var compiler = new TlmCompiler();
        foreach (var f in Directory.GetFiles(compiledDir, "rs-*.tlmz"))
        {
            var pkg = compiler.Deserialize(File.ReadAllBytes(f));
            foreach (var cue in pkg.Cues)
            {
                if (string.IsNullOrWhiteSpace(cue.Signal)) continue;
                var phrases = cue.Trigger.Split('/').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (!_bySignal.TryGetValue(cue.Signal, out var list)) { list = new(); _bySignal[cue.Signal] = list; }
                list.AddRange(phrases);
            }
            foreach (var c in pkg.Concepts.Where(c => c.Category == "CharClass"))
            {
                var key = c.Label.Trim().ToLowerInvariant();   // uppercase|lowercase|numeric|symbol
                _classAlias[key] = key;
                foreach (var a in c.Aliases) _classAlias[a.Trim().ToLowerInvariant()] = key;
            }
        }

        _qmin = Alt(_bySignal.GetValueOrDefault("q.min"));
        _qmax = Alt(Concat(_bySignal.GetValueOrDefault("q.max"), new[] { "only", "just", "nothing but" }));
        _qexact = Alt(_bySignal.GetValueOrDefault("q.exact"));
        _len = Alt(_bySignal.GetValueOrDefault("target.length"));
        _each = Alt(_bySignal.GetValueOrDefault("each.min"));
        _only = Alt(_bySignal.GetValueOrDefault("only"));
        _pin = Alt(_bySignal.GetValueOrDefault("preset.pin"));
        _ambig = Alt(_bySignal.GetValueOrDefault("no_ambiguous"));
        _exclV = Alt(_bySignal.GetValueOrDefault("exclude"));
        _inclV = Alt(_bySignal.GetValueOrDefault("include"));
        _classAlt = Alt(_classAlias.Keys);

        foreach (var (sig, phrases) in _bySignal)
        {
            if (sig.StartsWith("only.", StringComparison.OrdinalIgnoreCase))
            {
                var classes = sig.Substring(5).Split('+');
                _groups.Add((Alt(phrases), classes));
                foreach (var p in phrases)
                {
                    var w = p.Trim().ToLowerInvariant();
                    if (w.Length == 0 || w.Contains("only")) continue;
                    _groupPhrase[w] = classes;   // any non-"only" phrase, incl "letters and numbers"
                    if (!w.Contains(" and ") && !w.Contains(" or ")) _groupWord[w] = classes;  // bare word
                }
            }
            else if (sig.StartsWith("unsupported", StringComparison.OrdinalIgnoreCase))
                _unsupported.Add((Alt(phrases), sig.Contains(':') ? sig[(sig.IndexOf(':') + 1)..] : "an unsupported constraint"));
        }
        _groupWordAlt = _groupWord.Count > 0 ? Alt(_groupWord.Keys) : "";
        _groupPhraseAlt = _groupPhrase.Count > 0 ? Alt(_groupPhrase.Keys) : "";
        _multiGroupAlt = _groupWord.Any(kv => kv.Value.Length >= 2) ? Alt(_groupWord.Where(kv => kv.Value.Length >= 2).Select(kv => kv.Key)) : "";
    }

    private readonly List<(string Alt, string Reason)> _unsupported = new();
    private readonly Dictionary<string, string[]> _groupWord = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _groupPhrase = new(StringComparer.OrdinalIgnoreCase);
    private string _groupWordAlt = "";
    private string _groupPhraseAlt = "";
    private string _multiGroupAlt = "";   // group words spanning 2+ classes (letters/alphanumeric)
    private string QAll => $"{_qmin}|{_qmax}|{_qexact}";

    // a class/group token: a class alias (uppercase/digits/…) OR a group word (letters/alphanumeric/…)
    private string Tok => _groupWordAlt.Length > 0 ? $@"(?:{_classAlt}|{_groupWordAlt})" : $@"(?:{_classAlt})";
    private string ClassList => $@"{Tok}(?:\s*(?:,|and|or|&)\s*{Tok})*";
    private string ClassList2 => $@"{Tok}(?:\s*(?:,|and|or|&)\s*{Tok})+";  // 2+ tokens
    private IEnumerable<string> ClassesIn(string span) =>
        Rx($@"(?<!\w){Tok}(?!\w)").Matches(span)
          .SelectMany(m =>
          {
              var w = m.Value.ToLowerInvariant();
              return _classAlias.TryGetValue(w, out var k) ? new[] { k }
                   : _groupWord.TryGetValue(w, out var cs) ? cs
                   : Array.Empty<string>();
          })
          .Distinct();

    public bool Ready => _classAlt.Length > 0 && _bySignal.Count > 0;

    public Result Resolve(string text)
    {
        var orig = " " + text + " ";
        var t = orig.ToLowerInvariant();                 // same length as orig -> offsets align
        var args = new GenerateArgs { Classes = new() };
        var fired = new List<string>();

        // detect out-of-scope asks up front (scan the untouched lowered text)
        var unsupported = new List<string>();
        foreach (var (alt, reason) in _unsupported)
            if (Rx($@"(?<!\w)(?:{alt})").IsMatch(t) && !unsupported.Contains(reason)) unsupported.Add(reason);

        // a COUNT on a multi-class group ("max 10 letters", "at least 5 alphanumeric") is a
        // cross-class total the spec can't express — note it (parsing treats it as a length bound).
        if (_multiGroupAlt.Length > 0 &&
            (Rx($@"(?:{QAll})\s+\d+\s+(?:{_multiGroupAlt})\b").IsMatch(t) ||
             Rx($@"\b\d+\s+(?:{QAll})\s+(?:{_multiGroupAlt})\b").IsMatch(t) ||
             Rx($@"\b\d+\s+(?:{_multiGroupAlt})\s+(?:{QAll})\b").IsMatch(t)))
            unsupported.Add("a count on a whole letters/alphanumeric group (it spans classes) — use per-class limits like 'max 10 uppercase'; read here as a length bound");

        void Restrict(Match m)
        {
            var keys = ClassesIn(m.Groups["list"].Value).ToArray();
            if (keys.Length == 0) return;
            RestrictTo(args, keys); fired.Add("only." + string.Join("+", keys));
        }

        // 1. class deny/allow (verb + class/group list): "no letters", "without symbols or digits"
        if (_exclV.Length > 0)
            t = Consume(t, Rx($@"(?<!\w)(?:{_exclV})\s+(?:the\s+)?(?<list>{ClassList})(?!\w)"),
                m => { foreach (var k in ClassesIn(m.Groups["list"].Value)) { Set(args, k).Allowed = false; fired.Add("deny." + k); } });
        if (_inclV.Length > 0)
            t = Consume(t, Rx($@"(?<!\w)(?:{_inclV})\s+(?:the\s+)?(?<list>{ClassList})(?!\w)"),
                m => { foreach (var k in ClassesIn(m.Groups["list"].Value)) { Set(args, k).Allowed = true; fired.Add("allow." + k); } });

        // 2. literal include/exclude characters (CASE-PRESERVED)
        if (_exclV.Length > 0) CaptureChars(ref t, orig, _exclV, args, fired, include: false);
        if (_inclV.Length > 0) CaptureChars(ref t, orig, _inclV, args, fired, include: true);

        // 3. normalize postfix quantifiers ("2+", "2 or more|fewer")
        t = Regex.Replace(t, @"(\d+)\s*\+", "at least $1");
        t = Regex.Replace(t, $@"({Num})\s+or\s+more", "at least $1", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, $@"({Num})\s+or\s+(?:fewer|less)", "at most $1", RegexOptions.IgnoreCase);

        // 4. N of each
        if (_each.Length > 0)
            t = Consume(t, Rx($@"(?<q>{_qmin}\s+)?(?<n>{Num})\s+(?:{_each})"),
                m => { int n = ParseNum(m.Groups["n"].Value); foreach (var k in AllClasses) { var c = Set(args, k); if (c.Allowed != false) { c.Allowed = true; c.Min = Math.Max(c.Min ?? 0, n); } } fired.Add($"each.min={n}"); });

        // 5. ranges (before per-class so "2 to 4 symbols" binds as a range). Digit-only hyphen,
        //    so a word-number like "twenty-four" isn't split on its internal hyphen.
        t = Consume(t, Rx($@"between\s+(?<lo>{Num})\s+and\s+(?<hi>{Num})\s*(?:(?<cls>{_classAlt})|(?<len>{_len}))?"), m => ApplyRange(args, fired, m));
        t = Consume(t, Rx($@"(?<lo>{Num})\s*(?:to|through|thru)\s*(?<hi>{Num})\s+(?:(?<cls>{_classAlt})|(?<len>{_len}))"), m => ApplyRange(args, fired, m));
        t = Consume(t, Rx($@"(?<lo>\d+)\s*-\s*(?<hi>\d+)\s+(?:(?<cls>{_classAlt})|(?<len>{_len}))"), m => ApplyRange(args, fired, m));

        // 6. per-class (postfix / mid / prefix) — BEFORE bare-group restriction so a multiword
        //    alias like "small letters" binds as a class, not split by the "letters" group cue.
        t = Consume(t, Rx($@"(?<n>{Num})\s+(?<cls>{_classAlt})\s+(?:(?<qmax>{_qmax})|(?<qex>{_qexact})|(?<qmin>{_qmin}))(?!\w)"),
            m => ApplyPerClass(args, fired, m));                                       // "3 symbols maximum"
        t = Consume(t, Rx($@"(?<n>{Num})\s+(?:(?<qmin>{_qmin})|(?<qmax>{_qmax})|(?<qex>{_qexact}))\s+(?<cls>{_classAlt})(?!\w)"),
            m => ApplyPerClass(args, fired, m));                                       // "2 min lowercase"
        t = Consume(t, Rx($@"(?:(?<qmin>{_qmin})|(?<qmax>{_qmax})|(?<qex>{_qexact}))?\s*(?<n>{Num})\s+(?<cls>{_classAlt})(?!\w)"),
            m => ApplyPerClass(args, fired, m));                                       // "at least 2 uppercase" / bare

        // 7. indefinite article -> min 1: "must contain a digit", "an uppercase"
        t = Consume(t, Rx($@"(?<!\w)(?:a|an)\s+(?<cls>{_classAlt})(?!\w)"),
            m => { var k = _classAlias[m.Groups["cls"].Value.ToLowerInvariant()]; var c = Set(args, k); c.Allowed = true; c.Min = Math.Max(c.Min ?? 0, 1); fired.Add($"{k}.min>=1"); });

        // 7c. number + GROUP word with no quantifier: "10 letters", "10 letters and numbers"
        //     -> length N + restrict to that group (a 10-char letters/alphanumeric password).
        //     The lookbehind skips "max 10 letters" (that's a length bound, handled in step 12).
        if (_groupPhraseAlt.Length > 0)
            t = Consume(t, Rx($@"(?<!(?:{QAll})\s)(?<!\w)(?<n>{Num})\s+(?<g>{_groupPhraseAlt})(?!\w)"),
                m =>
                {
                    var classes = _groupPhrase[m.Groups["g"].Value.ToLowerInvariant()];
                    int n = ParseNum(m.Groups["n"].Value);
                    args.Length ??= new LengthArgs { Exact = n };
                    RestrictTo(args, classes);
                    fired.Add($"length.exact={n}+only.{string.Join("+", classes)}");
                });

        // 8. MULTI-class "only/just" (2+ class words): "uppercase and numbers only"
        t = Consume(t, Rx($@"(?<!\w)(?:{_only})\s+(?:the\s+)?(?<list>{ClassList2})(?!\w)"), Restrict);   // prefix
        t = Consume(t, Rx($@"(?<!\w)(?<list>{ClassList2})\s+(?:{_only})(?!\w)"), Restrict);              // postfix

        // 9. fixed group restrictions ("letters only", "alphanumeric", "all caps", ...) + pin
        foreach (var (alt, classes) in _groups.OrderByDescending(g => g.Alt.Length))
            t = Consume(t, Rx($@"(?<!\w)(?:{alt})(?!\w)"), _ => { RestrictTo(args, classes); fired.Add("only." + string.Join("+", classes)); });
        if (_pin.Length > 0)
            t = Consume(t, Rx($@"(?<!\w)(?:{_pin})(?!\w)"), _ => { RestrictTo(args, new[] { "numeric" }); fired.Add("preset.pin"); });

        // 10. single bare "only/just <class>" (no number): "just lowercase", "caps only"
        t = Consume(t, Rx($@"(?<!\w)(?:{_only})\s+(?:the\s+)?(?<list>{_classAlt})(?!\w)"), Restrict);
        t = Consume(t, Rx($@"(?<!\w)(?<list>{_classAlt})\s+(?:{_only})(?!\w)"), Restrict);

        // 11. readability
        if (_ambig.Length > 0)
            t = Consume(t, Rx($@"(?<!\w)(?:{_ambig})(?!\w)"), _ => { args.ExcludeAmbiguous = true; AddOnce(fired, "no_ambiguous"); });

        // 12. length: noun number-first, noun-first, then bare quantifier+number
        t = Consume(t, Rx($@"(?:(?<qmin>{_qmin})|(?<qmax>{_qmax})|(?<qex>{_qexact}))?\s*(?<n>{Num})\s*-?\s*(?:{_len})(?!\w)"),
            m => ApplyLength(args, fired, m));
        t = Consume(t, Rx($@"(?:(?<qmin>{_qmin})|(?<qmax>{_qmax})|(?<qex>{_qexact}))?\s*(?:{_len})\s*(?:of|=|:|is)?\s*(?<n>{Num})(?!\w)"),
            m => ApplyLength(args, fired, m));
        t = Consume(t, Rx($@"(?:(?<qmin>{_qmin})|(?<qmax>{_qmax})|(?<qex>{_qexact}))\s+(?<n>{Num})(?!\w)"),
            m => ApplyLength(args, fired, m));                                          // "max 20"
        t = Consume(t, Rx($@"(?<n>{Num})\s+(?:(?<qmax>{_qmax})|(?<qmin>{_qmin})|(?<qex>{_qexact}))(?!\w)"),
            m => ApplyLength(args, fired, m));                                          // "10 max", "8 min"

        if (args.Classes!.Count == 0) args.Classes = null;
        bool hasExplicit = fired.Count > 0;

        // pin: length is the stated digit count ("6 digit pin" -> 6), else the classic 4
        if (fired.Contains("preset.pin") && args.Length is null)
        {
            int n = args.Classes is not null && args.Classes.TryGetValue("numeric", out var nc) && nc.Min is > 0 ? nc.Min!.Value : 4;
            args.Length = new LengthArgs { Exact = n };
            fired.Add($"length.exact={n} (pin)");
        }

        // a pure max-only length ("max 20", "up to 17 chars") means "as long as allowed" —
        // generate AT the max (strongest within the limit, and keeps reported entropy honest
        // instead of a random short string with a max-length entropy number).
        if (args.Length is { Exact: null, Min: null, Max: { } mx })
        {
            args.Length.Exact = mx;
            fired.Add($"length.exact={mx} (max->target)");
        }

        // default length so reported entropy matches what is generated
        if (args.Length is null)
        {
            args.Length = new LengthArgs { Exact = StringGenerator.DefaultLength };
            fired.Add($"length.exact={StringGenerator.DefaultLength} (default)");
        }
        return new Result(args, fired, hasExplicit, unsupported);
    }

    private void ApplyPerClass(GenerateArgs a, List<string> fired, Match m)
    {
        var key = _classAlias[m.Groups["cls"].Value.ToLowerInvariant()];
        int n = ParseNum(m.Groups["n"].Value);
        var c = Set(a, key); c.Allowed = true;
        if (m.Groups["qmax"].Success) { c.Max = n; fired.Add($"{key}.max={n}"); }
        else if (m.Groups["qex"].Success) { c.Min = n; c.Max = n; fired.Add($"{key}.exact={n}"); }
        else { c.Min = n; fired.Add($"{key}.min={n}"); }
    }

    private void ApplyRange(GenerateArgs a, List<string> fired, Match m)
    {
        int lo = ParseNum(m.Groups["lo"].Value), hi = ParseNum(m.Groups["hi"].Value);
        if (m.Groups["cls"].Success)
        {
            var key = _classAlias[m.Groups["cls"].Value.ToLowerInvariant()];
            var c = Set(a, key); c.Allowed = true; c.Min = lo; c.Max = hi; fired.Add($"{key}.min={lo},max={hi}");
        }
        else { a.Length = new LengthArgs { Min = lo, Max = hi }; fired.Add($"length.min={lo},max={hi}"); }
    }

    private static void ApplyLength(GenerateArgs a, List<string> fired, Match m)
    {
        int n = ParseNum(m.Groups["n"].Value);
        // merge so separate clauses combine: "at least 8 and at most 20 characters"
        if (m.Groups["qmin"].Success) { (a.Length ??= new LengthArgs()).Min = n; a.Length.Exact = null; fired.Add($"length.min={n}"); }
        else if (m.Groups["qmax"].Success) { (a.Length ??= new LengthArgs()).Max = n; a.Length.Exact = null; fired.Add($"length.max={n}"); }
        else { a.Length = new LengthArgs { Exact = n }; fired.Add($"length.exact={n}"); }
    }

    // capture literal chars after include/exclude verbs (quoted run, or single-char tokens), case-preserved
    private void CaptureChars(ref string t, string orig, string verbs, GenerateArgs a, List<string> fired, bool include)
    {
        void Emit(List<char> chars)
        {
            if (chars.Count == 0) return;
            if (include) { (a.IncludeChars ??= new()).AddRange(chars.Select(c => c.ToString())); fired.Add("include=" + string.Concat(chars)); }
            else { (a.ExcludeChars ??= new()).AddRange(chars.Select(c => c.ToString())); fired.Add("exclude=" + string.Concat(chars)); }
        }
        // quoted: verb "..."  -> every non-space char inside the quotes
        var q = Rx($@"(?<!\w)(?:{verbs})\s+[""'](?<c>[^""']{{1,24}})[""']");
        t = Consume(t, q, m => Emit(orig.Substring(m.Groups["c"].Index, m.Groups["c"].Length)
                                        .Where(ch => !char.IsWhiteSpace(ch)).Distinct().ToList()));
        // single-char tokens: "0 O 1 l I" / "@ and #"  -> keep length-1 tokens, drop and/or connectors
        var single = Rx($@"(?<!\w)(?:{verbs})\s+(?<c>[{CharCls}](?:[ ,]+(?:and |or )?[{CharCls}])*)(?!\s*(?:{_classAlt}|{_len})\b)(?![{CharCls}])");
        t = Consume(t, single, m => Emit(ExtractRun(orig.Substring(m.Groups["c"].Index, m.Groups["c"].Length))));
    }

    // split a token run, drop and/or connectors, keep single-character tokens (case-preserved)
    private static List<char> ExtractRun(string raw) =>
        raw.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
           .Where(tok => tok.Length == 1 && !"and".Equals(tok, StringComparison.OrdinalIgnoreCase) && !"or".Equals(tok, StringComparison.OrdinalIgnoreCase))
           .Select(tok => tok[0]).Distinct().ToList();

    // ── helpers ──────────────────────────────────────────────────────────────
    private static readonly string[] AllClasses = { "uppercase", "lowercase", "numeric", "symbol" };

    private static void RestrictTo(GenerateArgs a, string[] keep)
    {
        foreach (var k in AllClasses) Set(a, k).Allowed = Array.IndexOf(keep, k) >= 0;
    }

    private static ClassArgs Set(GenerateArgs a, string key)
    {
        a.Classes ??= new();
        if (!a.Classes.TryGetValue(key, out var c)) { c = new ClassArgs { Allowed = true }; a.Classes[key] = c; }
        return c;
    }

    private static List<char> ExtractChars(string raw) =>
        raw.Where(ch => !char.IsWhiteSpace(ch) && ch != ',').Distinct().ToList();

    private static void AddOnce(List<string> l, string s) { if (!l.Contains(s)) l.Add(s); }

    private static string Consume(string t, Regex rx, Action<Match> on) =>
        rx.Replace(t, m => { on(m); return new string(' ', m.Length); });

    private static Regex Rx(string pat) => new(pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> Concat(IEnumerable<string>? a, IEnumerable<string> b) => (a ?? Enumerable.Empty<string>()).Concat(b);

    private static string Alt(IEnumerable<string>? phrases)
    {
        var list = (phrases ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct()
            .OrderByDescending(p => p.Length).Select(Regex.Escape).ToList();
        return list.Count == 0 ? "(?!x)x" : string.Join("|", list);   // never-match if empty
    }

    private const string Num =
        @"\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|" +
        @"fifteen|sixteen|seventeen|eighteen|nineteen|twenty-?four|twenty|thirty|forty|fifty|sixty-?four|sixty|hundred";

    private static readonly Dictionary<string, int> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18,
        ["nineteen"] = 19, ["twenty"] = 20, ["twentyfour"] = 24, ["twenty-four"] = 24, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["sixtyfour"] = 64, ["sixty-four"] = 64, ["hundred"] = 100,
    };

    private static int ParseNum(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (int.TryParse(s, out var v)) return v;
        if (Words.TryGetValue(s, out var w)) return w;
        if (Words.TryGetValue(s.Replace("-", "").Replace(" ", ""), out var w2)) return w2;
        return 0;
    }
}
