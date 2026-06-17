using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using PassGen.App;
using PassGen.Engine;

// ─────────────────────────────────────────────────────────────────────────────
// PassGen — self-contained password assistant.
//
//   The PassGen host layer: turns English into a ConstraintSpec with a deterministic
//   rule-based parser (NO LLM — that's pluggable later), dispatches the same
//   GenerateArgs an LLM would emit to RandomStringTool, and answers knowledge
//   questions from the pure 7-TLM random-string bundle.
//
//   Usage:  passgen                   interactive REPL
//           passgen <english request>  one-shot, prints the answer, exits
//   Commands: /recall <q>, /decompile, /spec, /seed [n], /help, /exit
// ─────────────────────────────────────────────────────────────────────────────

int? seed = null;                 // null => cryptographically secure RNG (the safe default)
GenerateArgs? lastArgs = null;
string? lastPassword = null;      // for /copy
bool mask = false;                // hide the value on screen, copy it instead

var compiledDir = KnowledgeBase.FindCompiledDir(Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
if (compiledDir is null)
{
    Console.Error.WriteLine("Could not locate dataset/compiled (the rs-*.tlmz bundle). Run build-dataset.ps1 first.");
    return 1;
}
var kb = new KnowledgeBase(compiledDir);
var nlu = new TlmNlu(compiledDir);   // TLM graph IS the language model

// One-shot modes.
if (args.Length > 0)
{
    // `passgen --decompile [name|all]` — dump the .tlmz knowledge back to readable JSON and exit.
    if (args[0] is "--decompile" or "-d")
    {
        Decompile(args.Length > 1 ? args[1] : "all");
        return 0;
    }
    Handle(Sanitize(string.Join(' ', args)));
    return 0;
}

// Interactive REPL.
Console.WriteLine($"PassGen — password assistant. Knowledge: {kb.TlmCount} TLMs, "
                  + $"{kb.ConceptCount} concepts, {kb.RelationCount} relations (random-string bundle only, no LLM).");
Console.WriteLine("Ask for a password, or about passwords/entropy. Each line is its own request.");
Console.WriteLine("Commands: /recall /decompile /spec /copy /mask /clear /seed /help /exit\n");

while (true)
{
    Console.Write("you> ");
    var line = Console.ReadLine();
    if (line is null) break;                       // EOF (piped input)
    line = Sanitize(line);
    if (line.Length == 0) continue;
    if (line is "/exit" or "/quit") break;
    Handle(line);
    Console.WriteLine();
}
return 0;

// Strip a leading UTF-8 BOM / zero-width chars (they appear on the first piped
// stdin line) and trim, so "/seed" etc. are recognized as commands.
static string Sanitize(string s) => s.Trim('﻿', '​', ' ', '\t', '\r', '\n');

// ── dispatch ───────────────────────────────────────────────────────────────────
void Handle(string input)
{
    if (input.StartsWith('/')) { Command(input); return; }

    if (WantsGeneration(input)) Generate(input);
    else Answer(input);
}

void Command(string input)
{
    var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
    switch (parts[0].ToLowerInvariant())
    {
        case "/help":
            Console.WriteLine("""
                Commands:
                  <english>        generate a password OR answer a question (auto-detected)
                  /recall <topic>  search the random-string knowledge graph
                  /decompile [n]   dump the .tlmz knowledge to readable JSON (name or 'all')
                  /spec            show the last parsed GenerateArgs (the 'LLM' function-call args)
                  /copy            copy the last password to the clipboard
                  /mask [on|off]   hide generated passwords on screen (copies them instead)
                  /clear           clear the screen (wipe passwords from scrollback)
                  /seed [n]        deterministic RNG seed for TESTING (omit n to clear -> secure RNG)
                  /help            this help
                  /exit            quit
                """);
            break;
        case "/recall":
            if (parts.Length < 2) { Console.WriteLine("usage: /recall <topic>"); break; }
            Answer(parts[1]);
            break;
        case "/decompile":
            Decompile(parts.Length < 2 ? "all" : parts[1]);
            break;
        case "/spec":
            Console.WriteLine(lastArgs is null
                ? "no spec parsed yet — ask for a password first."
                : JsonSerializer.Serialize(lastArgs, new JsonSerializerOptions { WriteIndented = true }));
            break;
        case "/copy":
            if (lastPassword is null) { Console.WriteLine("nothing to copy yet — generate a password first."); break; }
            Console.WriteLine(TryCopyToClipboard(lastPassword)
                ? "copied the last password to the clipboard."
                : "clipboard unavailable on this system.");
            break;
        case "/mask":
            mask = parts.Length < 2 ? !mask : parts[1].Trim().ToLowerInvariant() is "on" or "true" or "1";
            Console.WriteLine($"mask {(mask ? "ON — passwords hidden and copied to clipboard" : "OFF — passwords shown")}.");
            break;
        case "/clear":
            try { Console.Clear(); } catch { /* not a real console */ }
            break;
        case "/seed":
            if (parts.Length < 2) { seed = null; Console.WriteLine("seed cleared — using the secure RNG (real passwords)."); }
            else if (int.TryParse(parts[1], out var s))
            {
                seed = s;
                Console.WriteLine($"[INSECURE] seed = {s}: output is now reproducible — for testing only, NOT real passwords. /seed to clear.");
            }
            else Console.WriteLine("usage: /seed [integer]");
            break;
        default:
            Console.WriteLine($"unknown command: {parts[0]} (try /help)");
            break;
    }
}

// ── generation path ──────────────────────────────────────────────────────────
void Generate(string input)
{
    // TLM-driven resolver first (the TLM graph is the model); regex SpecParser as fallback.
    GenerateArgs argsParsed; List<string> cuesParsed; List<string> unsupported = new();
    if (nlu.Ready) { var r = nlu.Resolve(input); argsParsed = r.Args; cuesParsed = r.Fired; unsupported = r.Unsupported; }
    else { var p = SpecParser.Parse(input); argsParsed = p.Args; cuesParsed = p.Cues; }
    var parsed = (Args: argsParsed, Cues: cuesParsed);
    lastArgs = parsed.Args;

    foreach (var u in unsupported)
        Console.WriteLine($"  [note] can't enforce {u}; generating what I can.");
    try
    {
        var result = RandomStringTool.Execute(parsed.Args, seed);
        var (ok, violations) = SpecValidator.CheckString(result.Value, result.Spec);
        lastPassword = result.Value;

        // (a) seeded output is reproducible => unsafe as a real secret. Say so loudly.
        if (seed is not null)
            Console.WriteLine($"  [INSECURE] reproducible (seed={seed}) — testing only; do NOT use as a real password. Clear with /seed.");

        // (b) keep the value off-screen when masked; copy it to the clipboard instead.
        if (mask)
        {
            bool copied = TryCopyToClipboard(result.Value);
            Console.WriteLine($"password: (hidden — {result.Value.Length} chars" +
                              (copied ? ", copied to clipboard" : "; clipboard unavailable, /mask off to show") + ")");
        }
        else
        {
            Console.WriteLine($"password: {result.Value}");
        }

        Console.WriteLine($"  spec:    {SpecSummary(result.Spec)}");
        Console.WriteLine($"  entropy: {result.EntropyBits} bits ({result.Strength}), "
                          + $"charset {result.CharsetSize}, crack {CrackTimeText(result.EntropyBits)}");

        // (c) flag weak results with concrete advice instead of leaving it implicit.
        if (result.EntropyBits < 80)
            Console.WriteLine($"  [WEAK]   {WeaknessAdvice(result.EntropyBits)}");

        Console.WriteLine($"  cues:    {(parsed.Cues.Count > 0 ? string.Join(", ", parsed.Cues) : "(none — defaults)")}");
        Console.WriteLine($"  check:   {(ok ? "OK — satisfies the spec" : "FAILED: " + string.Join("; ", violations))}");
        if (!mask) Console.WriteLine("  (/copy to clipboard, /mask to hide, /clear to wipe screen)");
    }
    catch (SpecException ex)
    {
        Console.WriteLine($"That doesn't add up — {ex.Message}.");
        Console.WriteLine($"  I read: {string.Join(", ", parsed.Cues)}");
        Console.WriteLine("  Loosen one of those (raise the length, or lower a minimum) and I can do it.");
    }
}

// ── knowledge path ───────────────────────────────────────────────────────────
void Answer(string input)
{
    var hits = kb.Recall(input);
    if (hits.Count == 0)
    {
        Console.WriteLine("I don't have anything on that in the random-string knowledge base. "
                          + "Try asking about entropy, character classes, strength, or generation rules.");
        return;
    }

    foreach (var h in hits)
        Console.WriteLine($"- {h.Concept.Label} ({h.Concept.Category}) [{h.Tlm}]"
                          + (string.IsNullOrEmpty(h.Concept.Description) ? "" : $"\n    {h.Concept.Description}"));

    var rels = kb.RelationsFor(hits.Select(h => h.Concept.Id));
    if (rels.Count > 0)
    {
        Console.WriteLine("  related:");
        foreach (var r in rels) Console.WriteLine($"    {r}");
    }
}

// ── decompile path ─────────────────────────────────────────────────────────────
// Reads the compiled .tlmz bundle and writes readable JSON to a sibling
// `decompiled/` folder. Uses the same PassGen.Tlm compiler the `tlm` CLI uses.
void Decompile(string? name)
{
    var datasetDir = Directory.GetParent(kb.CompiledDir)?.FullName ?? kb.CompiledDir;
    var outDir = Path.Combine(datasetDir, "decompiled");
    Directory.CreateDirectory(outDir);

    var compiler = new PassGen.Tlm.TlmCompiler();
    var pretty = new JsonSerializerOptions { WriteIndented = true };

    List<string> files;
    if (string.IsNullOrEmpty(name) || name.Equals("all", StringComparison.OrdinalIgnoreCase))
        files = Directory.GetFiles(kb.CompiledDir, "*.tlmz").OrderBy(f => f).ToList();
    else
    {
        var f = Path.Combine(kb.CompiledDir, name.EndsWith(".tlmz") ? name : $"{name}.tlmz");
        files = File.Exists(f) ? new() { f } : new();
    }
    if (files.Count == 0)
    {
        Console.WriteLine($"No matching .tlmz in {kb.CompiledDir} (encrypted? run protect-tlms.ps1 -Mode decrypt).");
        return;
    }

    int ok = 0;
    foreach (var path in files)
    {
        var baseName = Path.GetFileNameWithoutExtension(path);
        try
        {
            var pkg = compiler.Deserialize(File.ReadAllBytes(path));
            var outPath = Path.Combine(outDir, $"{baseName}.decompiled.json");
            File.WriteAllText(outPath, JsonSerializer.Serialize(pkg, pretty));
            Console.WriteLine($"  {baseName}.tlmz -> decompiled/{baseName}.decompiled.json ({pkg.Concepts.Count} concepts, {pkg.Relations.Count} relations)");
            ok++;
        }
        catch (Exception ex) { Console.WriteLine($"  [FAIL] {baseName}: {ex.Message}"); }
    }
    Console.WriteLine($"Decompiled {ok}/{files.Count} -> {outDir}");
}

// ── helpers ──────────────────────────────────────────────────────────────────
bool WantsGeneration(string text)
{
    var t = text.ToLowerInvariant();
    bool genVerb = Regex.IsMatch(t, @"\b(generate|create|make|give|need|want|build|produce|gimme|new|get me)\b");
    bool subject = Regex.IsMatch(t, @"\b(password|passphrase|pass|string|secret|token|key|pin|code|credential)\b");
    bool isQuestion = t.TrimEnd().EndsWith('?')
                      || Regex.IsMatch(t, @"^\s*(what|why|how|which|when|who|explain|describe|define|tell|is|are|can|does|do)\b");
    // Only EXPLICIT constraints count as a generation signal — the auto-injected
    // default length ("...(default)") must not make every question look like a request.
    // Source the cues from the TLM-driven resolver (the model), regex parser as fallback.
    var fired = nlu.Ready ? nlu.Resolve(text).Fired : SpecParser.Parse(text).Cues;
    bool cues = fired.Any(c => !c.Contains("(default)"));

    if (subject && (genVerb || cues || !isQuestion)) return true;
    if (cues && !isQuestion) return true;
    return false;
}

// Friendly crack-time: below the 80-bit offline-attack line, show the concrete
// estimate; at/above it, say so plainly instead of printing astronomically large
// numbers (or the infinity symbol that renders as garbage in some consoles).
static string CrackTimeText(double bits) =>
    bits >= 80 ? "effectively uncrackable (>= 80 bits)" : $"~{Entropy.CrackTime(bits)} at 1e12 guesses/sec";

static string WeaknessAdvice(double bits)
{
    var gap = bits < 28 ? "very weak" : bits < 60 ? "weak" : "below the offline-attack-safe line";
    return $"{gap} ({bits:N1} bits). Aim for 80+ — add length or another character class.";
}

// Copy to the OS clipboard so the secret needn't sit in screen scrollback.
static bool TryCopyToClipboard(string text)
{
    try
    {
        var (file, fileArgs) =
            OperatingSystem.IsWindows() ? ("clip", "") :
            OperatingSystem.IsMacOS() ? ("pbcopy", "") :
            ("xclip", "-selection clipboard");
        var psi = new ProcessStartInfo(file, fileArgs)
        {
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return false;
        p.StandardInput.Write(text);
        p.StandardInput.Close();
        return p.WaitForExit(2000) && p.ExitCode == 0;
    }
    catch { return false; }
}

static string SpecSummary(ConstraintSpec spec)
{
    string len = spec.Length.Exact is { } e ? $"len={e}"
        : (spec.Length.Min, spec.Length.Max) switch
        {
            (int lo, int hi) => $"len {lo}..{hi}",
            (int lo, null) => $"len >={lo}",
            (null, int hi) => $"len <={hi}",
            _ => "len=auto(16)",
        };

    var allowed = new List<string>();
    var off = new List<string>();
    foreach (var c in Alphabet.All)
    {
        var cc = spec.Classes[c];
        var name = c.ToString().ToLowerInvariant();
        if (!cc.Allowed) { off.Add(name); continue; }
        var bits = new List<string>();
        if (cc.Min > 0) bits.Add($"min{cc.Min}");
        if (cc.Max is { } mx) bits.Add($"max{mx}");
        allowed.Add(bits.Count > 0 ? $"{name}({string.Join(",", bits)})" : name);
    }

    var parts = new List<string> { len, "allow: " + string.Join("+", allowed) };
    if (off.Count > 0) parts.Add("no: " + string.Join("+", off));

    var ambiguous = new HashSet<char>(Alphabet.Ambiguous);
    if (spec.ExcludeChars.Count > 0)
    {
        if (ambiguous.SetEquals(spec.ExcludeChars)) parts.Add("no-ambiguous");
        else parts.Add("exclude:" + new string(spec.ExcludeChars.ToArray()));
    }
    if (spec.IncludeChars.Count > 0) parts.Add("include:" + new string(spec.IncludeChars.ToArray()));
    return string.Join(", ", parts);
}
