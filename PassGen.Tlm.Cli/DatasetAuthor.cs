using System.Text.Json;
using PassGen.Tlm;

namespace PassGen.Tlm.Cli;

/// <summary>
/// Authors the random-string TLM dataset source (*.source.json) — the C# port of the
/// retired Python author_sources.py. Faithful: same ids, order, and content, so the
/// compiled .tlmz are byte-identical (same checksums). Grow coverage by editing this.
/// </summary>
public static class DatasetAuthor
{
    private static readonly DateTime Created = new(2026, 6, 17, 0, 0, 0, DateTimeKind.Utc);

    private static SymbolicConcept Con(string id, string label, string cat, string desc,
        string[]? aliases = null, (string, string)[]? props = null)
    {
        var c = new SymbolicConcept { Id = id, Label = label, Category = cat, Description = desc };
        if (aliases != null) c.Aliases = aliases.ToList();
        if (props != null) foreach (var (k, v) in props) c.Properties[k] = v;
        return c;
    }

    private static SymbolicRelation Rel(string s, string t, string type, string desc = "", double strength = 1.0)
        => new() { SourceId = s, TargetId = t, Type = type, Description = desc, Strength = strength };

    private static TlmPackage Pkg(string id, TlmRole role, int prio, string[]? imports,
        List<SymbolicConcept> concepts, List<SymbolicRelation> relations,
        List<SymbolicDimension>? dims = null, List<SymbolicPolicy>? pols = null,
        List<SymbolicCue>? cues = null, List<SymbolicFitSignal>? fits = null, double stability = 0.0)
        => new()
        {
            Manifest = new TlmManifest
            {
                Metadata = new TlmMetadata
                {
                    TlmId = id, IsMutable = false, Role = role, Priority = prio, Version = "1.0.0",
                    Checksum = "", HotSwapPolicy = HotSwapPolicy.Safe, StabilityScore = stability
                },
                Imports = (imports ?? Array.Empty<string>()).ToList(),
                Derives = new(), CreatedUtc = Created, SchemaVersion = "1.0"
            },
            Concepts = concepts, Relations = relations,
            Dimensions = dims ?? new(), Policies = pols ?? new(), Cues = cues ?? new(), FitSignals = fits ?? new()
        };

    public static void Author(string sourceDir)
    {
        Directory.CreateDirectory(sourceDir);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        foreach (var p in new[] { CharClasses(), ConstraintSpec(), Operations(), Entropy(), Generation(), NlVocabulary(), Bundle() })
        {
            File.WriteAllText(Path.Combine(sourceDir, p.Manifest.Metadata.TlmId + ".source.json"),
                JsonSerializer.Serialize(p, opts));
            Console.WriteLine($"  authored {p.Manifest.Metadata.TlmId,-22} {p.Concepts.Count} concepts, {p.Relations.Count} relations");
        }
    }

    // 1 ───────────────────────────────────────────────────────────────────────
    private static TlmPackage CharClasses()
    {
        var c = new List<SymbolicConcept>(); var r = new List<SymbolicRelation>();
        c.Add(Con("cls-alphabet", "alphabet", "Domain",
            "The full character alphabet available to the random-string generator: four classes totalling 75 characters in the default safe configuration."));

        var classdef = new (string Name, string Chars, string Desc)[]
        {
            ("uppercase", "ABCDEFGHIJKLMNOPQRSTUVWXYZ", "Uppercase letters A-Z (26 characters)."),
            ("lowercase", "abcdefghijklmnopqrstuvwxyz", "Lowercase letters a-z (26 characters)."),
            ("numeric",   "0123456789", "Decimal digits 0-9 (10 characters)."),
            ("symbol",    "!@#$%^&*-_=+?", "Default safe symbol subset (13 characters)."),
        };
        var ambiguous = new HashSet<char>("0Oo1lI");

        c.Add(Con("set-ambiguous", "ambiguous characters", "CharSet",
            "Visually confusable characters (0 O o 1 l I) removed by the 'no ambiguous' option; dropping them shrinks the charset 75->69 and costs ~2 bits of entropy.",
            props: new[] { ("Chars", "0Oo1lI") }));
        c.Add(Con("pool-symbol-safe", "safe symbol pool", "CharPool",
            "Default symbol pool: !@#$%^&*-_=+? (13 chars) — shell/URL friendly.",
            props: new[] { ("Chars", "!@#$%^&*-_=+?"), ("Size", "13") }));
        c.Add(Con("pool-symbol-extended", "extended symbol pool", "CharPool",
            "Opt-in full ASCII punctuation pool (superset of the safe pool)."));

        var aliases = new Dictionary<string, string[]>
        {
            ["uppercase"] = new[] { "uppercase", "upper", "uppers", "capital", "capitals", "caps", "upper case", "upper-case", "uppercase letters", "capital letters" },
            ["lowercase"] = new[] { "lowercase", "lower", "lowers", "lower case", "lower-case", "lowercase letters", "small letters" },
            ["numeric"] = new[] { "numeric", "digit", "digits", "number", "numbers", "numeral", "numerals", "numerics" },
            ["symbol"] = new[] { "symbol", "symbols", "special", "specials", "special character", "special characters", "special chars", "punctuation", "punctuations", "sign", "signs" },
        };

        foreach (var (name, chars, desc) in classdef)
        {
            var cid = $"class-{name}";
            c.Add(Con(cid, name, "CharClass", desc, aliases: aliases[name],
                props: new[] { ("Chars", chars), ("Size", chars.Length.ToString()) }));
            r.Add(Rel(cid, "cls-alphabet", "MemberOf", $"{name} class is part of the alphabet"));
            foreach (var ch in chars)
            {
                var chid = $"ch-{name}-{(int)ch}";
                c.Add(Con(chid, ch.ToString(), "Character", $"Character '{ch}' of the {name} class.",
                    props: new[] { ("Class", name) }));
                r.Add(Rel(chid, cid, "MemberOf"));
                if (ambiguous.Contains(ch)) r.Add(Rel(chid, "set-ambiguous", "IsAmbiguous", $"'{ch}' is visually ambiguous"));
            }
        }

        var order = new[] { "class-uppercase", "class-lowercase", "class-numeric", "class-symbol" };
        for (int i = 0; i < order.Length - 1; i++) r.Add(Rel(order[i], order[i + 1], "SiblingOf", "co-equal character classes"));
        r.Add(Rel("class-symbol", "pool-symbol-safe", "DrawsFrom", "default symbols come from the safe pool"));
        r.Add(Rel("class-symbol", "pool-symbol-extended", "CanDrawFrom", "opt-in extended punctuation"));
        r.Add(Rel("pool-symbol-extended", "pool-symbol-safe", "Superset", "extended ⊇ safe"));
        r.Add(Rel("set-ambiguous", "cls-alphabet", "FilteredFrom", "removed by 'no ambiguous'"));

        return Pkg("rs-char-classes", TlmRole.Foundation, 100, null, c, r, stability: 1.0);
    }

    // 2 ───────────────────────────────────────────────────────────────────────
    private static TlmPackage ConstraintSpec()
    {
        var c = new List<SymbolicConcept>(); var r = new List<SymbolicRelation>();
        c.Add(Con("spec-root", "ConstraintSpec", "Schema",
            "The structured contract that is the single source of truth between the natural-language front end and the deterministic generator. JSON shape: { length:{exact,min,max}, classes:{<class>->{allowed,min,max}}, exclude_chars[], include_chars[] }."));

        foreach (var (fid, label, desc) in new[]
        {
            ("field-length", "length", "Length constraint: exact, or a [min,max] range."),
            ("field-classes", "classes", "Per-class allow/min/max constraints for the four character classes."),
            ("field-exclude", "exclude_chars", "Explicit characters the output must never contain."),
            ("field-include", "include_chars", "Explicit characters the output must contain at least once."),
        })
        { c.Add(Con(fid, label, "SpecField", desc)); r.Add(Rel("spec-root", fid, "HasField")); }

        foreach (var (sid, label, desc) in new[]
        {
            ("slot-length-exact", "length.exact", "Exact required length (mutually exclusive with min/max)."),
            ("slot-length-min", "length.min", "Minimum length bound."),
            ("slot-length-max", "length.max", "Maximum length bound (default cap, e.g. 16)."),
        })
        { c.Add(Con(sid, label, "SpecSlot", desc)); r.Add(Rel("field-length", sid, "HasSlot")); }

        foreach (var (sid, label, desc) in new[]
        {
            ("slot-allowed", "class.allowed", "Whether characters from this class may appear (boolean gate)."),
            ("slot-class-min", "class.min", "Guaranteed minimum count of characters from this class."),
            ("slot-class-max", "class.max", "Maximum count of characters from this class."),
        })
        { c.Add(Con(sid, label, "SpecSlot", desc)); }

        foreach (var name in new[] { "uppercase", "lowercase", "numeric", "symbol" })
        {
            var ccid = $"cc-{name}";
            c.Add(Con(ccid, $"{name} constraint", "ClassConstraint",
                $"Allow/min/max constraint record for the {name} class.", props: new[] { ("Class", name) }));
            r.Add(Rel("field-classes", ccid, "Contains"));
            r.Add(Rel(ccid, "slot-allowed", "HasSlot"));
            r.Add(Rel(ccid, "slot-class-min", "HasSlot"));
            r.Add(Rel(ccid, "slot-class-max", "HasSlot"));
        }

        c.Add(Con("spec-feasibility", "feasibility", "Invariant",
            "validate_spec checks the spec is satisfiable: sum of class minimums <= max length, every required class allowed, exclude/include not contradictory."));
        r.Add(Rel("spec-root", "spec-feasibility", "MustSatisfy", "a spec is only valid if feasible"));
        r.Add(Rel("spec-feasibility", "slot-class-min", "Checks"));
        r.Add(Rel("spec-feasibility", "slot-length-max", "Checks"));
        r.Add(Rel("field-exclude", "field-include", "MustNotContradict", "a char cannot be both excluded and required"));

        return Pkg("rs-constraint-spec", TlmRole.Logic, 120, new[] { "rs-char-classes" }, c, r, stability: 0.9);
    }

    // 3 ───────────────────────────────────────────────────────────────────────
    private static TlmPackage Operations()
    {
        var c = new List<SymbolicConcept>(); var r = new List<SymbolicRelation>();
        c.Add(Con("op-root", "operations", "OperationSet",
            "The vocabulary of operations a user can apply to narrow the default (all-classes, secure) spec."));

        foreach (var (eid, label, desc) in new[]
        {
            ("eff-restrict-classes", "restrict allowed classes", "Limit which character classes may appear."),
            ("eff-set-class-min", "set class minimum", "Guarantee a minimum count for a class."),
            ("eff-set-class-max", "set class maximum", "Cap the count for a class."),
            ("eff-set-length", "set length", "Set exact length or a [min,max] range."),
            ("eff-exclude", "exclude characters", "Add characters to exclude_chars."),
            ("eff-include", "include characters", "Add characters to include_chars."),
            ("eff-disable-ambiguous", "disable ambiguous", "Remove the ambiguous set from every class."),
        })
        { c.Add(Con(eid, label, "Effect", desc)); }

        foreach (var (oid, label, desc, eff) in new[]
        {
            ("op-only", "only", "Restrict the output to the named class(es) only.", "eff-restrict-classes"),
            ("op-exclude", "exclude", "Forbid specific characters.", "eff-exclude"),
            ("op-include", "include", "Require specific characters to appear.", "eff-include"),
            ("op-min", "minimum", "Require at least N characters of a class.", "eff-set-class-min"),
            ("op-max", "maximum", "Allow at most N characters of a class.", "eff-set-class-max"),
            ("op-length-exact", "exact length", "Fix the output to exactly N characters.", "eff-set-length"),
            ("op-length-range", "length range", "Bound length between min and max.", "eff-set-length"),
            ("op-no-ambiguous", "no ambiguous", "Drop visually confusable characters.", "eff-disable-ambiguous"),
            ("op-letters-only", "letters only", "Restrict to uppercase+lowercase.", "eff-restrict-classes"),
            ("op-alphanumeric", "alphanumeric", "Restrict to letters+digits (no symbols).", "eff-restrict-classes"),
            ("op-digits-only", "digits only", "Restrict to the numeric class.", "eff-restrict-classes"),
            ("op-symbols-only", "symbols only", "Restrict to the symbol class.", "eff-restrict-classes"),
        })
        { c.Add(Con(oid, label, "Operation", desc)); r.Add(Rel("op-root", oid, "Contains")); r.Add(Rel(oid, eff, "Produces")); }

        foreach (var spec in new[] { "op-letters-only", "op-alphanumeric", "op-digits-only", "op-symbols-only" })
            r.Add(Rel(spec, "op-only", "IsA", "a preset restriction"));
        r.Add(Rel("op-length-exact", "op-length-range", "SiblingOf"));
        r.Add(Rel("op-min", "op-max", "SiblingOf"));
        r.Add(Rel("op-min", "op-include", "RelatesTo", "minimum-of-class vs must-include-char"));

        return Pkg("rs-operations", TlmRole.Interface, 110, new[] { "rs-constraint-spec" }, c, r, stability: 0.8);
    }

    // 4 ───────────────────────────────────────────────────────────────────────
    private static TlmPackage Entropy()
    {
        var c = new List<SymbolicConcept>(); var r = new List<SymbolicRelation>();
        c.Add(Con("entropy-root", "password entropy", "Metric",
            "Strength model: entropy = log2(number of distinct valid strings) = the attacker search space, in bits."));

        foreach (var (mid, label, desc) in new[]
        {
            ("metric-bits", "bits", "log2 of the count of distinct valid strings the spec admits."),
            ("metric-charset", "charset size", "Number of distinct characters usable under the spec (N)."),
            ("metric-valid-count", "valid string count", "Exact count via the constrained-multinomial EGF; reduces to N^L when unconstrained."),
            ("metric-crack-time", "crack time", "bits -> wall-clock at a guess rate (default 1e12 guesses/sec)."),
        })
        { c.Add(Con(mid, label, "Metric", desc)); r.Add(Rel("entropy-root", mid, "HasMetric")); }
        r.Add(Rel("metric-bits", "metric-charset", "DependsOn"));
        r.Add(Rel("metric-bits", "metric-valid-count", "DependsOn"));
        r.Add(Rel("metric-crack-time", "metric-bits", "DerivedFrom"));
        r.Add(Rel("metric-valid-count", "metric-charset", "DependsOn"));

        var strengths = new (string Id, string Label, string Band, string Lo)[]
        {
            ("str-very-weak", "very weak", "<28 bits", "0"),
            ("str-weak", "weak", "28-35 bits", "28"),
            ("str-fair", "fair", "36-59 bits", "36"),
            ("str-strong", "strong", "60-79 bits", "60"),
            ("str-very-strong", "very strong", ">=80 bits", "80"),
        };
        string? prev = null;
        foreach (var (sid, label, band, lo) in strengths)
        {
            c.Add(Con(sid, label, "StrengthLabel", $"Strength band: {band}.", props: new[] { ("MinBits", lo) }));
            r.Add(Rel("metric-bits", sid, "MapsTo", $"bits in band {band}"));
            if (prev != null) r.Add(Rel(prev, sid, "SiblingOf"));
            prev = sid;
        }

        foreach (var (rid, label, bits, desc) in new[]
        {
            ("ref-pin4", "4-digit PIN", "13.3", "Four decimal digits: 10^4 = 13.3 bits."),
            ("ref-alnum8", "8-char alphanumeric", "47.6", "8 chars over 62 symbols."),
            ("ref-allclass8", "8-char all-classes", "49.8", "8 chars over the 75-char alphabet (~6.23 bits/char)."),
            ("ref-alnum16", "16-char alphanumeric", "95.3", "16 chars over 62 symbols."),
            ("ref-allclass16", "16-char all-classes", "99.7", "16 chars over the 75-char alphabet — the headline strong default."),
            ("ref-threshold80", "80-bit offline threshold", "80", "Rule of thumb: 80+ bits resists offline GPU attack."),
        })
        { c.Add(Con(rid, label, "ReferencePoint", desc, props: new[] { ("Bits", bits) })); r.Add(Rel(rid, "metric-bits", "MeasuredIn")); }

        foreach (var (iid, label, desc) in new[]
        {
            ("ins-length-dominates", "length dominates charset", "Doubling length (8->16) roughly doubles entropy (~50->100 bits); adding character variety adds far less."),
            ("ins-symbol-marginal", "symbols add little", "Adding the symbol class to a 16-char alphanumeric password adds only ~4 bits (95.3->99.7)."),
            ("ins-perclass-min-reduces", "per-class minimums reduce entropy", "Forcing structure ('at least 2 of each') slightly shrinks the valid space (99.7->98.6 bits)."),
            ("ins-no-ambiguous-costs", "no-ambiguous costs ~2 bits", "Dropping ambiguous chars shrinks the charset 75->69, costing ~2 bits."),
        })
        { c.Add(Con(iid, label, "Insight", desc)); r.Add(Rel(iid, "metric-bits", "Explains")); }
        r.Add(Rel("ins-length-dominates", "ref-allclass16", "EvidencedBy"));
        r.Add(Rel("ins-symbol-marginal", "ref-alnum16", "EvidencedBy"));
        r.Add(Rel("ins-no-ambiguous-costs", "str-strong", "Affects"));

        var dims = new List<SymbolicDimension>
        {
            new() { Id = "dim-bits", Label = "entropy bits", MinValue = 0.0, MaxValue = 128.0 },
            new() { Id = "dim-strength", Label = "normalized strength", MinValue = 0.0, MaxValue = 1.0 },
        };
        var fits = new List<SymbolicFitSignal> { new() { Id = "fit-strong-threshold", Threshold = 0.8 } };
        return Pkg("rs-entropy", TlmRole.Analysis, 90, new[] { "rs-constraint-spec" }, c, r, dims: dims, fits: fits, stability: 0.85);
    }

    // 5 ───────────────────────────────────────────────────────────────────────
    private static TlmPackage Generation()
    {
        var c = new List<SymbolicConcept>(); var r = new List<SymbolicRelation>();
        c.Add(Con("gen-root", "generation algorithm", "Algorithm",
            "Deterministic generator: validate spec -> place mandatory minimums -> uniform-fill the remainder -> validate output (-> repair if needed)."));
        foreach (var (sid, label, desc) in new[]
        {
            ("step-validate-spec", "validate spec", "Feasibility check before any sampling (validate_spec)."),
            ("step-place-minimums", "place minimums", "Place each class's guaranteed minimum first, sampled within-class uniformly."),
            ("step-fill-uniform", "uniform fill", "Fill remaining positions by sampling uniformly from the UNION of all allowed, non-maxed characters."),
            ("step-validate-output", "validate output", "Verify the produced string satisfies the spec (validator.check)."),
            ("step-repair", "repair", "Relax infeasible/over-constrained predictions and retry."),
        })
        { c.Add(Con(sid, label, "Step", desc)); r.Add(Rel("gen-root", sid, "HasStep")); }
        var chain = new[] { "step-validate-spec", "step-place-minimums", "step-fill-uniform", "step-validate-output" };
        for (int i = 0; i < chain.Length - 1; i++) r.Add(Rel(chain[i], chain[i + 1], "Precedes"));
        r.Add(Rel("step-validate-output", "step-repair", "TriggersOnFail"));

        c.Add(Con("rng-csprng", "CSPRNG", "Rng",
            "Cryptographically secure RNG (RandomNumberGenerator / secrets.SystemRandom). Used by default (seed is null) for real passwords."));
        c.Add(Con("rng-seeded", "seeded PRNG", "Rng",
            "Reproducible Mersenne-Twister RNG used ONLY when an explicit int seed is given (tests/demos)."));
        c.Add(Con("risk-mt-insecure", "Mersenne Twister is not secure", "Risk",
            "MT state is reconstructable from ~624 outputs — never use for real passwords."));
        c.Add(Con("bias-per-class", "per-class sampling bias", "Antipattern",
            "Picking a class first then a char within it over-represents small classes (digits ~2.6x). Avoided by flat union sampling."));
        r.Add(Rel("gen-root", "rng-csprng", "UsesByDefault"));
        r.Add(Rel("gen-root", "rng-seeded", "UsesForTests"));
        r.Add(Rel("rng-seeded", "risk-mt-insecure", "HasRisk"));
        r.Add(Rel("step-fill-uniform", "bias-per-class", "Avoids"));
        r.Add(Rel("step-place-minimums", "bias-per-class", "Avoids"));

        var pols = new List<SymbolicPolicy>
        {
            new() { Id = "pol-default-csprng", Rule = "seed is null (default)", Action = "use the CSPRNG (RandomNumberGenerator) for fresh entropy per string" },
            new() { Id = "pol-seeded-tests", Rule = "an explicit integer seed is supplied", Action = "use a reproducible Mersenne-Twister RNG — tests and demos only" },
            new() { Id = "pol-minimums-first", Rule = "the spec declares per-class minimums", Action = "place those mandatory characters first (within-class uniform), then uniform-fill" },
            new() { Id = "pol-uniform-fill", Rule = "filling the remaining positions", Action = "sample each character uniformly from the union of allowed, non-maxed characters" },
            new() { Id = "pol-repair", Rule = "the spec is infeasible or output fails validation", Action = "run a repair pass that relaxes the offending constraint, then retry" },
        };
        return Pkg("rs-generation", TlmRole.Policy, 130, new[] { "rs-constraint-spec", "rs-char-classes" }, c, r, pols: pols, stability: 0.9);
    }

    // 6 ───────────────────────────────────────────────────────────────────────
    private static TlmPackage NlVocabulary()
    {
        var c = new List<SymbolicConcept>(); var r = new List<SymbolicRelation>(); var cues = new List<SymbolicCue>();
        c.Add(Con("nl-root", "natural-language vocabulary", "Lexicon",
            "Maps free-form English to constraint signals the deterministic resolver executes. Each Cue carries Trigger synonyms (the vocabulary) and a Signal (the action). The TLM IS the model: add a synonym here and the resolver understands it - no code change. Numbers/classes/chars are bound from context by the resolver, never invented."));

        foreach (var (iid, label, desc) in new[]
        {
            ("intent-min", "class minimum", "Require at least N of a class."),
            ("intent-max", "class maximum", "Allow at most N of a class."),
            ("intent-exact", "exact count", "Require exactly N of a class."),
            ("intent-length", "length", "Set output length (exact, min, max, or range)."),
            ("intent-restrict", "restrict classes", "Limit which classes may appear."),
            ("intent-exclude", "exclude chars", "Forbid a class or specific characters."),
            ("intent-include", "include chars", "Require a class or specific characters."),
            ("intent-no-ambiguous", "no ambiguous", "Remove visually confusable characters."),
        })
        { c.Add(Con(iid, label, "Intent", desc)); r.Add(Rel("nl-root", iid, "Resolves")); }

        var cueDefs = new (string Id, string Trigger, string Signal, string Intent)[]
        {
            ("cue-qmin", "at least / minimum / min / at minimum / no fewer than / no less than / or more", "q.min", "intent-min"),
            ("cue-qmax", "at most / no more than / no greater than / up to / maximum / max / at maximum / or fewer / or less", "q.max", "intent-max"),
            ("cue-qexact", "exactly / precisely", "q.exact", "intent-exact"),
            ("cue-length", "characters / character / chars / char / long / in length / length", "target.length", "intent-length"),
            ("cue-each", "of each / of each type / of each kind / of each class", "each.min", "intent-min"),
            ("cue-only", "only / just / nothing but", "only", "intent-restrict"),
            ("cue-letters", "letters / alphabetic / alphabetical / mixed case / mixed-case / upper and lower / both cases", "only.uppercase+lowercase", "intent-restrict"),
            ("cue-alnum", "alphanumeric / alphanumerics / letters and numbers / letters and digits / numbers and letters / digits and letters", "only.uppercase+lowercase+numeric", "intent-restrict"),
            ("cue-digonly", "digits only / numbers only / numeric only / only digits / only numbers / just digits / just numbers", "only.numeric", "intent-restrict"),
            ("cue-symonly", "symbols only / only symbols / special only", "only.symbol", "intent-restrict"),
            ("cue-loweronly", "lowercase only / only lowercase / all lowercase", "only.lowercase", "intent-restrict"),
            ("cue-upperonly", "uppercase only / only uppercase / all uppercase / all caps", "only.uppercase", "intent-restrict"),
            ("cue-pin", "pin / pin code / pincode / passcode", "preset.pin", "intent-restrict"),
            ("cue-noambig", "no ambiguous / unambiguous / readable / no look-alikes / no look alikes / no lookalikes / no confusing / no confusables / no confusable characters / avoid confusing / easy to read / legible / memorable", "no_ambiguous", "intent-no-ambiguous"),
            ("cue-exclude", "exclude / excluding / without / avoid / omit / drop / remove / skip / no", "exclude", "intent-exclude"),
            ("cue-include", "include / including / must contain / contains / containing / with / plus / add / allow / use", "include", "intent-include"),
            // out-of-scope asks — the resolver surfaces these as graceful "can't express" notes
            ("cue-unsup-position", "start with / starts with / begin with / begins with / first character / first char / end with / ends with / last character / last char", "unsupported:character position (start/end) is not expressible", "intent-restrict"),
            ("cue-unsup-norepeat", "no repeat / no repeats / no repeated / no repeating / non-repeating / unique characters / all unique / no duplicate / no duplicates", "unsupported:no-repeat / all-unique characters", "intent-restrict"),
            ("cue-unsup-pronounce", "pronounceable / easy to pronounce", "unsupported:pronounceable output", "intent-restrict"),
            ("cue-unsup-words", "passphrase / pass phrase / random words / diceware / word-based / dictionary words", "unsupported:word/passphrase output (this is character-based)", "intent-restrict"),
            ("cue-unsup-alphabet", "hexadecimal / hex string / base64 / base 64", "unsupported:custom alphabet like hex or base64", "intent-restrict"),
            ("cue-unsup-charset", "only characters / only these characters / only the characters / only those characters / custom alphabet / character whitelist / from the character set / only from these characters / restricted to these characters", "unsupported:a custom character set (use class limits, or exclude/include specific characters instead)", "intent-restrict"),
        };
        foreach (var (cid, trigger, signal, intent) in cueDefs)
        {
            c.Add(Con(cid, trigger.Split(" / ")[0], "Phrase",
                $"Cue (signal {signal}); triggers: {trigger}.",
                aliases: trigger.Split('/').Select(t => t.Trim()).ToArray(),
                props: new[] { ("Triggers", trigger), ("Signal", signal) }));
            r.Add(Rel(cid, intent, "MapsTo", $"signal {signal}"));
            cues.Add(new SymbolicCue { Id = cid, Trigger = trigger, Signal = signal });
        }
        return Pkg("rs-nl-vocabulary", TlmRole.Interface, 105, new[] { "rs-operations" }, c, r, cues: cues, stability: 0.7);
    }

    // 7 ───────────────────────────────────────────────────────────────────────
    private static TlmPackage Bundle()
    {
        var c = new List<SymbolicConcept>(); var r = new List<SymbolicRelation>();
        c.Add(Con("domain-root", "random string generator", "Domain",
            "Constraint-based random string / password generator with a natural-language front end. This bundle indexes the full domain knowledge graph."));
        foreach (var (mid, label, tlm, desc) in new[]
        {
            ("mod-char-classes", "character classes", "rs-char-classes", "Alphabet & the four character classes."),
            ("mod-constraint-spec", "constraint spec", "rs-constraint-spec", "The ConstraintSpec contract / schema."),
            ("mod-operations", "operations", "rs-operations", "Operations that narrow the spec."),
            ("mod-entropy", "entropy", "rs-entropy", "Strength model and reference points."),
            ("mod-generation", "generation", "rs-generation", "The deterministic generation algorithm."),
            ("mod-nl-vocabulary", "NL vocabulary", "rs-nl-vocabulary", "English phrasing -> intents."),
        })
        { c.Add(Con(mid, label, "Module", desc, props: new[] { ("Tlm", tlm) })); r.Add(Rel("domain-root", mid, "ComposedOf")); }

        foreach (var (a, b) in new[]
        {
            ("mod-constraint-spec", "mod-char-classes"),
            ("mod-operations", "mod-constraint-spec"),
            ("mod-entropy", "mod-constraint-spec"),
            ("mod-generation", "mod-constraint-spec"),
            ("mod-generation", "mod-char-classes"),
            ("mod-nl-vocabulary", "mod-operations"),
        })
        { r.Add(Rel(a, b, "DependsOn")); }

        return Pkg("rs-bundle", TlmRole.Overlay, 1000,
            new[] { "rs-char-classes", "rs-constraint-spec", "rs-operations", "rs-entropy", "rs-generation", "rs-nl-vocabulary" },
            c, r, stability: 1.0);
    }
}
