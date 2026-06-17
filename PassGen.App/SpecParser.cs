using System.Text.RegularExpressions;
using PassGen.Engine;

namespace PassGen.App;

/// <summary>
/// Deterministic, rule-based English -> <see cref="GenerateArgs"/> parser. This is the
/// stand-in for sage-rsrm's LLM: it emits the SAME function-call arguments the
/// <see cref="RandomStringTool"/> consumes, so the contract is identical whether the
/// arguments come from this parser or from an LLM later. No neural model, no network.
///
/// Recognized vocabulary mirrors the rs-nl-vocabulary TLM:
///   length          : "16 characters", "16 chars", "16 long", "length 16", "exactly 16 ...",
///                      "at least 12 long", "at most 20 chars", "between 12 and 20"
///   per-class min/max: "at least 2 uppercase", "no more than 3 symbols", "exactly 1 digit",
///                      "2 uppercase" (bare -> minimum), "2 of each"
///   restriction      : "only letters", "letters only", "alphanumeric", "digits only",
///                      "no symbols", "without numbers", "lowercase only", "pin"
///   readability      : "no ambiguous", "unambiguous", "readable", "avoid look-alikes"
///   explicit chars   : 'include "@#"', 'must contain @', 'exclude "0O"', "without @ and #"
/// </summary>
public static class SpecParser
{
    public sealed record Result(GenerateArgs Args, List<string> Cues);

    private const string Num =
        @"(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|" +
        @"thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|" +
        @"twenty-?four|thirty|forty|fifty|sixty|sixty-?four|one\s*hundred)";

    private const string Cls =
        @"(?<cls>uppercase|upper(?:case)?|capitals?|lowercase|lower(?:case)?|" +
        @"digits?|numbers?|numeric|numerals?|symbols?|special(?:\s+characters?)?|punctuation)";

    private const string LenKw = @"(?:characters?|chars?|long|in\s+length|length)";

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <param name="applyDefaults">
    /// true for a fresh request (inject the default length when none is stated);
    /// false for a follow-up delta (only set what the user explicitly mentions, so
    /// it can be merged onto the previous spec without clobbering it).
    /// </param>
    public static Result Parse(string text, bool applyDefaults = true)
    {
        var t = " " + text.Trim().ToLowerInvariant() + " ";
        var cues = new List<string>();
        var args = new GenerateArgs { Classes = new Dictionary<string, ClassArgs>() };

        ParseReadability(t, args, cues);
        ParseRestriction(t, args, cues);
        ParsePerClass(t, args, cues);
        ParseOfEach(t, args, cues);
        ParseLength(t, args, cues, applyDefaults);
        ParseExplicitChars(t, args, cues);

        if (args.Classes!.Count == 0) args.Classes = null; // let ToSpec default to all-allowed
        return new Result(args, cues);
    }

    // ── readability ───────────────────────────────────────────────────────────
    private static void ParseReadability(string t, GenerateArgs a, List<string> cues)
    {
        if (Regex.IsMatch(t, @"\b(no|without|avoid|exclude|drop)\s+(ambiguous|confusing|look-?alike|similar)", Opt)
            || Regex.IsMatch(t, @"\b(unambiguous|readable|easy to read|legible)\b", Opt))
        {
            a.ExcludeAmbiguous = true;
            cues.Add("exclude_ambiguous=true");
        }
    }

    // ── class restriction (only X / no X) ───────────────────────────────────────
    private static void ParseRestriction(string t, GenerateArgs a, List<string> cues)
    {
        // "pin" -> digits only (length defaulted to 4 later if unspecified)
        if (Regex.IsMatch(t, @"\bpin\b", Opt))
        {
            RestrictTo(a, cues, "numeric");
            cues.Add("preset=pin");
        }

        // positive restrictions
        if (Regex.IsMatch(t, @"\b(only\s+)?letters\s+only\b|\bonly\s+letters\b|\b(alphabetic|alpha)\b", Opt))
            RestrictTo(a, cues, "uppercase", "lowercase");
        if (Regex.IsMatch(t, @"\balphanumeric\b|\bletters?\s+and\s+(numbers?|digits?)\b|\b(numbers?|digits?)\s+and\s+letters?\b", Opt))
            RestrictTo(a, cues, "uppercase", "lowercase", "numeric");
        if (Regex.IsMatch(t, @"\b(only\s+(digits?|numbers?))\b|\b(digits?|numbers?)\s+only\b|\bnumeric\s+only\b", Opt))
            RestrictTo(a, cues, "numeric");
        if (Regex.IsMatch(t, @"\b(only\s+symbols?)\b|\bsymbols?\s+only\b", Opt))
            RestrictTo(a, cues, "symbol");
        if (Regex.IsMatch(t, @"\blowercase\s+only\b|\bonly\s+lowercase\b", Opt))
            RestrictTo(a, cues, "lowercase");
        if (Regex.IsMatch(t, @"\buppercase\s+only\b|\bonly\s+uppercase\b", Opt))
            RestrictTo(a, cues, "uppercase");

        // negative restrictions: "no symbols", "without numbers" (but not "no more/fewer than")
        foreach (Match m in Regex.Matches(t, @"\b(no|without|exclude|drop|remove|avoid)\s+(?!more\b|fewer\b|ambiguous|confusing)" + Cls, Opt))
        {
            var key = ClassKey(m.Groups["cls"].Value);
            if (key is null) continue;
            Set(a, key).Allowed = false;
            cues.Add($"{key}.allowed=false");
        }

        // enable a class: "add symbols", "with symbols", "include numbers", "allow uppercase"
        foreach (Match m in Regex.Matches(t, @"\b(add|with|include|allow|use|plus)\s+(?:some\s+|a\s+|an\s+)?" + Cls, Opt))
        {
            var key = ClassKey(m.Groups["cls"].Value);
            if (key is null) continue;
            Set(a, key).Allowed = true;
            cues.Add($"{key}.allowed=true");
        }
    }

    // ── per-class min/max ───────────────────────────────────────────────────────
    private static void ParsePerClass(string t, GenerateArgs a, List<string> cues)
    {
        // <quantifier?> <N> <class>
        var rx = new Regex(
            @"(?<q>at\s+least|minimum(?:\s+of)?|min|no\s+fewer\s+than|at\s+most|no\s+more\s+than|" +
            @"up\s+to|maximum(?:\s+of)?|max|exactly|precisely)?\s*(?<n>" + Num + @")\s+" + Cls, Opt);

        foreach (Match m in rx.Matches(t))
        {
            var key = ClassKey(m.Groups["cls"].Value);
            if (key is null) continue;
            int n = ParseNum(m.Groups["n"].Value);
            var q = Regex.Replace(m.Groups["q"].Value.Trim(), @"\s+", " ");
            var c = Set(a, key);
            c.Allowed = true;

            if (q is "at most" or "no more than" or "up to" or "maximum" or "maximum of" or "max")
            {
                c.Max = n; cues.Add($"{key}.max={n}");
            }
            else if (q is "exactly" or "precisely")
            {
                c.Min = n; c.Max = n; cues.Add($"{key}.exact={n}");
            }
            else // at least / minimum / min / no fewer than / bare -> minimum
            {
                c.Min = n; cues.Add($"{key}.min={n}");
            }
        }
    }

    private static void ParseOfEach(string t, GenerateArgs a, List<string> cues)
    {
        var m = Regex.Match(t, @"(?<q>at\s+least\s+|minimum\s+)?(?<n>" + Num + @")\s+of\s+each", Opt);
        if (!m.Success) return;
        int n = ParseNum(m.Groups["n"].Value);
        foreach (var key in new[] { "uppercase", "lowercase", "numeric", "symbol" })
        {
            var c = Set(a, key);
            if (c.Allowed != false) { c.Allowed = true; c.Min = Math.Max(c.Min ?? 0, n); }
        }
        cues.Add($"each-class.min={n}");
    }

    // ── length ──────────────────────────────────────────────────────────────────
    private static void ParseLength(string t, GenerateArgs a, List<string> cues, bool applyDefaults)
    {
        // between N and M (characters?)
        var between = Regex.Match(t, @"\bbetween\s+(?<lo>" + Num + @")\s+and\s+(?<hi>" + Num + @")\s*" + LenKw + @"?", Opt);
        if (between.Success)
        {
            a.Length = new LengthArgs { Min = ParseNum(between.Groups["lo"].Value), Max = ParseNum(between.Groups["hi"].Value) };
            cues.Add($"length.min={a.Length.Min},max={a.Length.Max}");
            return;
        }

        // at least / at most N (characters|long)
        var atLeast = Regex.Match(t, @"\b(?:at\s+least|minimum(?:\s+of)?|min)\s+(?<n>" + Num + @")\s*" + LenKw + @"\b", Opt);
        var atMost = Regex.Match(t, @"\b(?:at\s+most|no\s+more\s+than|up\s+to|maximum(?:\s+of)?|max)\s+(?<n>" + Num + @")\s*" + LenKw + @"\b", Opt);
        if (atLeast.Success || atMost.Success)
        {
            a.Length = new LengthArgs
            {
                Min = atLeast.Success ? ParseNum(atLeast.Groups["n"].Value) : null,
                Max = atMost.Success ? ParseNum(atMost.Groups["n"].Value) : null,
            };
            if (a.Length.Min is { } lo) cues.Add($"length.min={lo}");
            if (a.Length.Max is { } hi) cues.Add($"length.max={hi}");
            return;
        }

        // exact: "16 characters", "16-character", "length 16", "exactly 16 chars/long".
        // The "exactly N" form REQUIRES a length unit so it doesn't swallow per-class
        // counts like "exactly 3 of each" or "exactly 3 digits".
        var exact = Regex.Match(t, @"\bexactly\s+(?<n>" + Num + @")\s*-?\s*" + LenKw + @"\b", Opt);
        if (!exact.Success)
            exact = Regex.Match(t, @"\blength\s*(?:of|=|:)?\s*(?<n>" + Num + @")\b", Opt);
        if (!exact.Success)
            exact = Regex.Match(t, @"\b(?<n>" + Num + @")\s*-?\s*" + LenKw + @"\b", Opt);
        if (exact.Success)
        {
            a.Length = new LengthArgs { Exact = ParseNum(exact.Groups["n"].Value) };
            cues.Add($"length.exact={a.Length.Exact}");
            return;
        }

        // pin: length is the stated digit count ("6 digit pin" -> 6), else the classic 4.
        if (cues.Contains("preset=pin"))
        {
            int n = a.Classes is not null && a.Classes.TryGetValue("numeric", out var nc) && nc.Min is > 0
                ? nc.Min.Value : 4;
            a.Length = new LengthArgs { Exact = n };
            cues.Add($"length.exact={n} (pin)");
            return;
        }

        if (!applyDefaults) return;   // follow-up delta: don't invent a length

        // No length stated: pin the generator's own default (16) so the reported
        // entropy matches what is actually produced. Without this, an unconstrained
        // spec generates 16 chars but Entropy counts length 1 (~6.2 bits).
        a.Length = new LengthArgs { Exact = StringGenerator.DefaultLength };
        cues.Add($"length.exact={StringGenerator.DefaultLength} (default)");
    }

    // ── explicit include/exclude characters ──────────────────────────────────────
    private static void ParseExplicitChars(string t, GenerateArgs a, List<string> cues)
    {
        // include: must contain / include / containing  followed by quoted run or symbol run
        foreach (Match m in Regex.Matches(t, @"(?:must\s+contain|include|containing|contain)\s+['""]?(?<chars>[!@#$%^&*\-_=+?a-z0-9]{1,16})['""]?", Opt))
        {
            var chars = ExtractChars(m.Groups["chars"].Value);
            if (chars.Count == 0) continue;
            (a.IncludeChars ??= new()).AddRange(chars.Select(c => c.ToString()));
            cues.Add($"include={string.Concat(chars)}");
        }

        // exclude specific quoted chars: exclude "0O"  /  without "@"
        foreach (Match m in Regex.Matches(t, @"(?:exclude|without|avoid|omit|drop|remove|skip)\s+['""](?<chars>[^'""]{1,16})['""]", Opt))
        {
            var chars = ExtractChars(m.Groups["chars"].Value);
            if (chars.Count == 0) continue;
            (a.ExcludeChars ??= new()).AddRange(chars.Select(c => c.ToString()));
            cues.Add($"exclude={string.Concat(chars)}");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────
    private static List<char> ExtractChars(string raw) =>
        raw.Where(ch => !char.IsWhiteSpace(ch)).Distinct().ToList();

    private static void RestrictTo(GenerateArgs a, List<string> cues, params string[] keep)
    {
        var all = new[] { "uppercase", "lowercase", "numeric", "symbol" };
        foreach (var key in all)
        {
            var c = Set(a, key);
            c.Allowed = Array.IndexOf(keep, key) >= 0;
        }
        cues.Add($"only={string.Join("+", keep)}");
    }

    private static ClassArgs Set(GenerateArgs a, string key)
    {
        a.Classes ??= new();
        if (!a.Classes.TryGetValue(key, out var c)) { c = new ClassArgs { Allowed = true }; a.Classes[key] = c; }
        return c;
    }

    private static string? ClassKey(string word)
    {
        word = word.Trim().ToLowerInvariant();
        if (word.StartsWith("upper") || word.StartsWith("capital")) return "uppercase";
        if (word.StartsWith("lower")) return "lowercase";
        if (word.StartsWith("digit") || word.StartsWith("number") || word.StartsWith("numeral") || word == "numeric") return "numeric";
        if (word.StartsWith("symbol") || word.StartsWith("special") || word.StartsWith("punctuation")) return "symbol";
        return null;
    }

    private static readonly Dictionary<string, int> Words = new()
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["twentyfour"] = 24, ["twenty-four"] = 24,
        ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["sixtyfour"] = 64,
        ["sixty-four"] = 64, ["onehundred"] = 100, ["one hundred"] = 100,
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
