using System.Text.Json;
using PassGen.Tlm;

// ─────────────────────────────────────────────────────────────────────────────
// tlm — standalone compiler / decompiler for the RSRM random-string TLM dataset.
//
//   tlm compile   [name|all]   source/*.source.json   -> compiled/*.tlmz
//   tlm decompile [name|all]   compiled/*.tlmz         -> decompiled/*.json
//   tlm validate  [name|all]   checksum + health over compiled/*.tlmz
//   tlm verify                 full lossless round-trip integrity over all
//   tlm list                   list the dataset's source TLMs
//   tlm stats     [name|all]   concept/relation/parameter counts
//
// --root <dir> overrides dataset-root autodetection (looks for a `dataset/`
// folder walking up from CWD).
// ─────────────────────────────────────────────────────────────────────────────

var (command, target, root) = ParseArgs(args);
if (command is null)
{
    PrintUsage();
    return 1;
}

var datasetRoot = ResolveRoot(root);
var sourceDir = Path.Combine(datasetRoot, "source");
var compiledDir = Path.Combine(datasetRoot, "compiled");
var decompiledDir = Path.Combine(datasetRoot, "decompiled");
Directory.CreateDirectory(sourceDir);
Directory.CreateDirectory(compiledDir);
Directory.CreateDirectory(decompiledDir);

var compiler = new TlmCompiler();
var validator = new TlmValidator();
var indented = new JsonSerializerOptions { WriteIndented = true };

Console.WriteLine($"dataset root: {datasetRoot}");

try
{
    return command switch
    {
        "author" => Author(),
        "compile" => Compile(target),
        "decompile" => Decompile(target),
        "validate" => Validate(target),
        "verify" => Verify(),
        "list" => List(),
        "stats" => Stats(target),
        _ => Unknown(command),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FATAL: {ex.Message}");
    return 1;
}

// ── commands ─────────────────────────────────────────────────────────────────

int Author()
{
    Console.WriteLine($"Authoring dataset sources -> {sourceDir}");
    PassGen.Tlm.Cli.DatasetAuthor.Author(sourceDir);
    Console.WriteLine("Authored. Run `tlm compile all` next (or build-dataset.ps1).");
    return 0;
}

int Compile(string? name)
{
    var sources = ResolveSources(name);
    if (sources.Count == 0) { Console.Error.WriteLine("No matching source files."); return 1; }

    int ok = 0, fail = 0;
    foreach (var src in sources)
    {
        var baseName = BaseName(src);
        try
        {
            var pkg = LoadSource(src);
            var artifact = compiler.Compile(pkg);
            var bytes = compiler.Serialize(artifact);
            File.WriteAllBytes(Path.Combine(compiledDir, $"{baseName}.tlmz"), bytes);
            Console.WriteLine($"  [OK]   {baseName,-22} {artifact.Concepts.Count,4} concepts {artifact.Relations.Count,4} relations  {bytes.Length,6} B  {artifact.Manifest.Metadata.Checksum[..12]}…");
            ok++;
        }
        catch (Exception ex) { Console.Error.WriteLine($"  [FAIL] {baseName}: {ex.Message}"); fail++; }
    }
    Console.WriteLine($"Compiled: {ok} ok, {fail} failed.");
    return fail == 0 ? 0 : 1;
}

int Decompile(string? name)
{
    var artifacts = ResolveCompiled(name);
    if (artifacts.Count == 0) { Console.Error.WriteLine("No matching .tlmz files. Run `tlm compile all` first."); return 1; }

    int ok = 0, fail = 0;
    foreach (var path in artifacts)
    {
        var baseName = Path.GetFileNameWithoutExtension(path);
        try
        {
            var pkg = compiler.Deserialize(File.ReadAllBytes(path));
            var json = JsonSerializer.Serialize(pkg, indented);
            File.WriteAllText(Path.Combine(decompiledDir, $"{baseName}.decompiled.json"), json);
            Console.WriteLine($"  [OK]   {baseName}.tlmz -> {baseName}.decompiled.json ({pkg.Concepts.Count} concepts, {pkg.Relations.Count} relations)");
            ok++;
        }
        catch (Exception ex) { Console.Error.WriteLine($"  [FAIL] {baseName}: {ex.Message}"); fail++; }
    }
    Console.WriteLine($"Decompiled: {ok} ok, {fail} failed.");
    return fail == 0 ? 0 : 1;
}

int Validate(string? name)
{
    var artifacts = ResolveCompiled(name);
    if (artifacts.Count == 0) { Console.Error.WriteLine("No matching .tlmz files."); return 1; }

    int valid = 0, invalid = 0;
    foreach (var path in artifacts)
    {
        var baseName = Path.GetFileNameWithoutExtension(path);
        var pkg = compiler.Deserialize(File.ReadAllBytes(path));
        var r = validator.Validate(pkg);
        if (r.IsValid)
        {
            Console.WriteLine($"  VALID    {baseName,-22} {pkg.Concepts.Count} concepts, {pkg.Relations.Count} relations" +
                              (r.Warnings.Count > 0 ? $"  ({r.Warnings.Count} warnings)" : ""));
            valid++;
        }
        else
        {
            Console.WriteLine($"  INVALID  {baseName}");
            foreach (var e in r.Errors) Console.WriteLine($"             error: {e}");
            invalid++;
        }
        foreach (var w in r.Warnings) Console.WriteLine($"             warn:  {w}");
    }
    Console.WriteLine($"Validated: {valid} valid, {invalid} invalid.");
    return invalid == 0 ? 0 : 1;
}

int Verify()
{
    var sources = ResolveSources(null);
    if (sources.Count == 0) { Console.Error.WriteLine("No source files."); return 1; }

    int pass = 0, failCount = 0;
    long totalConcepts = 0, totalRelations = 0, totalParams = 0;
    Console.WriteLine("Round-trip integrity (source -> compile -> decompile -> recompress == identity):");

    foreach (var src in sources)
    {
        var baseName = BaseName(src);
        try
        {
            // 1. source -> compiled bytes A
            var pkg = LoadSource(src);
            var artifact = compiler.Compile(pkg);
            var bytesA = compiler.Serialize(artifact);

            // 2. decompile A -> package B
            var pkgB = compiler.Deserialize(bytesA);

            // 3. recompress B -> bytes A2; idempotent envelope+Brotli+JSON round-trip
            var bytesA2 = compiler.Serialize(pkgB);

            bool bytesIdentical = bytesA.AsSpan().SequenceEqual(bytesA2);
            bool checksumValid = validator.Validate(pkgB).IsValid;
            bool checksumStable = pkgB.Manifest.Metadata.Checksum == TlmHasher.CalculateChecksum(pkgB);

            if (bytesIdentical && checksumValid && checksumStable)
            {
                Console.WriteLine($"  PASS  {baseName,-22} {bytesA.Length,6} B  checksum {artifact.Manifest.Metadata.Checksum[..12]}…");
                totalConcepts += artifact.Concepts.Count;
                totalRelations += artifact.Relations.Count;
                totalParams += artifact.ParameterCount;
                pass++;
            }
            else
            {
                Console.WriteLine($"  FAIL  {baseName}: bytesIdentical={bytesIdentical} checksumValid={checksumValid} checksumStable={checksumStable}");
                failCount++;
            }
        }
        catch (Exception ex) { Console.WriteLine($"  FAIL  {baseName}: {ex.Message}"); failCount++; }
    }

    Console.WriteLine();
    Console.WriteLine($"Round-trip: {pass} pass, {failCount} fail.");
    Console.WriteLine($"Dataset totals: {totalConcepts} concepts, {totalRelations} relations, {totalParams} parameters across {sources.Count} TLMs.");
    return failCount == 0 ? 0 : 1;
}

int List()
{
    var sources = ResolveSources(null);
    Console.WriteLine($"Source TLMs ({sources.Count}):");
    foreach (var src in sources)
    {
        var baseName = BaseName(src);
        var pkg = LoadSource(src);
        var m = pkg.Manifest.Metadata;
        var compiled = File.Exists(Path.Combine(compiledDir, $"{baseName}.tlmz"));
        Console.WriteLine($"  {(compiled ? "[OK]" : "[--]")} {m.TlmId,-22} v{m.Version}  {m.Role,-11} prio {m.Priority,-4} " +
                          $"{pkg.Concepts.Count,4}c {pkg.Relations.Count,4}r" +
                          (pkg.Manifest.Imports.Count > 0 ? $"  imports: {string.Join(",", pkg.Manifest.Imports)}" : ""));
    }
    return 0;
}

int Stats(string? name)
{
    var sources = ResolveSources(name);
    foreach (var src in sources)
    {
        var pkg = LoadSource(src);
        var cats = pkg.Concepts.GroupBy(c => c.Category).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}({g.Count()})");
        var rels = pkg.Relations.GroupBy(r => r.Type).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}({g.Count()})");
        Console.WriteLine($"{pkg.Manifest.Metadata.TlmId}:");
        Console.WriteLine($"  concepts={pkg.Concepts.Count} relations={pkg.Relations.Count} params={pkg.ParameterCount}");
        Console.WriteLine($"  categories: {string.Join(", ", cats)}");
        Console.WriteLine($"  relation types: {string.Join(", ", rels)}");
        if (pkg.Policies.Count > 0) Console.WriteLine($"  policies={pkg.Policies.Count} cues={pkg.Cues.Count} dimensions={pkg.Dimensions.Count}");
    }
    return 0;
}

int Unknown(string cmd) { Console.Error.WriteLine($"Unknown command: {cmd}"); PrintUsage(); return 1; }

// ── helpers ──────────────────────────────────────────────────────────────────

TlmPackage LoadSource(string path)
    => JsonSerializer.Deserialize<TlmPackage>(File.ReadAllText(path))
       ?? throw new InvalidDataException($"Failed to parse {Path.GetFileName(path)}");

List<string> ResolveSources(string? name)
{
    if (string.IsNullOrEmpty(name) || name.Equals("all", StringComparison.OrdinalIgnoreCase))
        return Directory.GetFiles(sourceDir, "*.source.json").OrderBy(f => f).ToList();
    var file = Path.Combine(sourceDir, name.EndsWith(".source.json") ? name : $"{name}.source.json");
    return File.Exists(file) ? new List<string> { file } : new List<string>();
}

List<string> ResolveCompiled(string? name)
{
    if (string.IsNullOrEmpty(name) || name.Equals("all", StringComparison.OrdinalIgnoreCase))
        return Directory.GetFiles(compiledDir, "*.tlmz").OrderBy(f => f).ToList();
    var file = Path.Combine(compiledDir, name.EndsWith(".tlmz") ? name : $"{name}.tlmz");
    return File.Exists(file) ? new List<string> { file } : new List<string>();
}

static string BaseName(string sourcePath)
    => Path.GetFileNameWithoutExtension(sourcePath).Replace(".source", "");

static (string? cmd, string? target, string? root) ParseArgs(string[] a)
{
    string? cmd = null, target = null, root = null;
    for (int i = 0; i < a.Length; i++)
    {
        if (a[i] == "--root" && i + 1 < a.Length) { root = a[++i]; continue; }
        if (cmd is null) cmd = a[i].ToLowerInvariant();
        else if (target is null) target = a[i];
    }
    return (cmd, target, root);
}

static string ResolveRoot(string? explicitRoot)
{
    if (!string.IsNullOrEmpty(explicitRoot)) return Path.GetFullPath(explicitRoot);

    // Walk up from CWD looking for a `dataset` directory.
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, "dataset");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return Path.GetFullPath("dataset");
}

static void PrintUsage()
{
    Console.WriteLine("""
        tlm — RSRM random-string TLM dataset compiler/decompiler

        Usage:
          tlm compile   [name|all]   compile source/*.source.json -> compiled/*.tlmz
          tlm decompile [name|all]   decompile compiled/*.tlmz    -> decompiled/*.json
          tlm validate  [name|all]   checksum + health validation
          tlm verify                 full lossless round-trip integrity check
          tlm list                   list source TLMs
          tlm stats     [name|all]   concept/relation/parameter breakdown

        Options:
          --root <dir>   dataset root (default: nearest `dataset/` walking up from CWD)
        """);
}
