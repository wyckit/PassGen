using PassGen.Tlm;

namespace PassGen.App;

/// <summary>
/// Loads the pure random-string TLM bundle (the 7 rs-*.tlmz) and answers knowledge
/// questions from it via keyword scoring + 1-hop relation expansion. This is the
/// deterministic analogue of RSRM's spreading activation, scoped to this dataset —
/// no external data, no LLM.
/// </summary>
public sealed class KnowledgeBase
{
    public sealed record Hit(string Tlm, SymbolicConcept Concept, int Score);

    private readonly List<(string Tlm, SymbolicConcept C)> _concepts = new();
    private readonly List<(string Tlm, SymbolicRelation R)> _relations = new();
    private readonly Dictionary<string, SymbolicConcept> _byId = new(StringComparer.OrdinalIgnoreCase);

    public int TlmCount { get; }
    public int ConceptCount => _concepts.Count;
    public int RelationCount => _relations.Count;
    public string CompiledDir { get; }

    public KnowledgeBase(string compiledDir)
    {
        CompiledDir = compiledDir;
        var compiler = new TlmCompiler();
        var files = Directory.GetFiles(compiledDir, "rs-*.tlmz").OrderBy(f => f).ToList();
        TlmCount = files.Count;
        foreach (var f in files)
        {
            var pkg = compiler.Deserialize(File.ReadAllBytes(f));
            var id = pkg.Manifest.Metadata.TlmId;
            foreach (var c in pkg.Concepts)
            {
                _concepts.Add((id, c));
                _byId[c.Id] = c;
            }
            foreach (var r in pkg.Relations)
                _relations.Add((id, r));
        }
    }

    /// <summary>Locate the dataset/compiled dir by walking up from the given start dirs.</summary>
    public static string? FindCompiledDir(params string[] starts)
    {
        foreach (var start in starts.Where(s => !string.IsNullOrEmpty(s)))
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var c = Path.Combine(dir.FullName, "dataset", "compiled");
                if (Directory.Exists(c) && Directory.GetFiles(c, "rs-*.tlmz").Length > 0) return c;
                dir = dir.Parent;
            }
        }
        return null;
    }

    public IReadOnlyList<Hit> Recall(string query, int top = 6)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0) return [];

        var scored = new List<Hit>();
        foreach (var (tlm, c) in _concepts)
        {
            var hay = (c.Id + " " + c.Label + " " + string.Join(" ", c.Aliases) + " " + c.Description)
                .ToLowerInvariant();
            int score = 0;
            foreach (var term in terms)
            {
                if (hay.Contains(term))
                {
                    // weight label/alias hits higher than description hits
                    score += (c.Label.ToLowerInvariant().Contains(term)
                              || c.Aliases.Any(a => a.ToLowerInvariant().Contains(term))) ? 3 : 1;
                }
            }
            if (score > 0) scored.Add(new Hit(tlm, c, score));
        }

        return scored
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Concept.Label.Length)
            .Take(top)
            .ToList();
    }

    /// <summary>Relations touching any of the given concept ids, rendered with labels.</summary>
    public IReadOnlyList<string> RelationsFor(IEnumerable<string> conceptIds, int max = 8)
    {
        var ids = new HashSet<string>(conceptIds, StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (var (_, r) in _relations)
        {
            if (!ids.Contains(r.SourceId) && !ids.Contains(r.TargetId)) continue;
            lines.Add($"{Label(r.SourceId)} --{r.Type}--> {Label(r.TargetId)}"
                      + (string.IsNullOrEmpty(r.Description) ? "" : $"  ({r.Description})"));
            if (lines.Count >= max) break;
        }
        return lines;
    }

    private string Label(string id) => _byId.TryGetValue(id, out var c) && !string.IsNullOrEmpty(c.Label) ? c.Label : id;

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","a","an","is","are","do","does","how","what","why","of","to","for","and","or",
        "with","can","i","you","me","my","please","tell","explain","about","it","that","this",
        "in","on","be","get","make","whats","what's","there","they","when","which",
    };

    private static List<string> Tokenize(string q) =>
        q.ToLowerInvariant()
         .Split(new[] { ' ', ',', '.', '?', '!', ';', ':', '"', '\'', '(', ')', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
         .Where(w => w.Length >= 3 && !Stop.Contains(w))
         .Distinct()
         .ToList();
}
