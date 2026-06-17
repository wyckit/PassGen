using System.Text.Json;
using System.Text.Json.Serialization;

namespace PassGen.Engine;

/// <summary>
/// Exposes the deterministic generator as an LLM-callable tool, mirroring sage-rsrm's
/// function-schema pattern (see <c>ChatSemanticRouting.update_cognitive_graph</c>). The LLM
/// fills the constraint arguments from the user's English; this deterministic engine — a
/// "compiled" unit, like a TLM — produces and validates the actual string. No neural model
/// is embedded, so the self-generated-corpus capability cap does not apply.
/// </summary>
public static class RandomStringTool
{
    public const string Name = "generate_random_string";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The function schema to register with the LLM (serialized JSON).</summary>
    public static readonly string SchemaJson = JsonSerializer.Serialize(BuildSchema());

    /// <summary>Run the tool from the LLM's function-call arguments (JSON object).</summary>
    public static ToolResult Execute(string argumentsJson, int? seed = null)
    {
        var args = JsonSerializer.Deserialize<GenerateArgs>(argumentsJson, JsonOptions) ?? new GenerateArgs();
        return Execute(args, seed);
    }

    public static ToolResult Execute(GenerateArgs args, int? seed = null)
    {
        var spec = args.ToSpec();
        SpecValidator.Validate(spec);
        var value = StringGenerator.Generate(spec, seed);
        double bits = Entropy.Bits(spec);
        return new ToolResult(
            Value: value,
            EntropyBits: Math.Round(bits, 1),
            Strength: Entropy.StrengthLabel(bits),
            CharsetSize: Entropy.CharsetSize(spec),
            Spec: spec);
    }

    private static object BuildSchema()
    {
        static object ClassSchema(string name) => new
        {
            type = "object",
            description = $"constraints on {name} characters",
            properties = new
            {
                allowed = new { type = "boolean" },
                min = new { type = "integer", minimum = 0 },
                max = new { type = new[] { "integer", "null" }, minimum = 0 },
            },
        };

        return new
        {
            name = Name,
            description =
                "Generate a random string/password from a constraint spec. All four character " +
                "classes (uppercase, lowercase, numeric, symbol) are allowed by default; set " +
                "allowed=false to forbid a class ('only'/'exclude'), and use per-class min/max for " +
                "rules like 'at least 5 uppercase'. Length is exact, or a min/max range.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    length = new
                    {
                        type = "object",
                        properties = new
                        {
                            exact = new { type = new[] { "integer", "null" }, minimum = 1 },
                            min = new { type = new[] { "integer", "null" }, minimum = 0 },
                            max = new { type = new[] { "integer", "null" }, minimum = 1 },
                        },
                    },
                    classes = new
                    {
                        type = "object",
                        properties = new
                        {
                            uppercase = ClassSchema("uppercase"),
                            lowercase = ClassSchema("lowercase"),
                            numeric = ClassSchema("numeric"),
                            symbol = ClassSchema("symbol"),
                        },
                    },
                    exclude_chars = new { type = "array", items = new { type = "string", maxLength = 1 } },
                    include_chars = new { type = "array", items = new { type = "string", maxLength = 1 } },
                    exclude_ambiguous = new { type = "boolean", description = "drop look-alikes 0 O o 1 l I" },
                },
            },
        };
    }
}

/// <summary>Result returned to the caller / LLM.</summary>
public sealed record ToolResult(
    string Value,
    double EntropyBits,
    string Strength,
    int CharsetSize,
    ConstraintSpec Spec);

// ---- DTOs for the LLM's function-call arguments (snake_case JSON) ----

public sealed class GenerateArgs
{
    [JsonPropertyName("length")] public LengthArgs? Length { get; set; }

    [JsonPropertyName("classes")] public Dictionary<string, ClassArgs>? Classes { get; set; }

    [JsonPropertyName("exclude_chars")] public List<string>? ExcludeChars { get; set; }

    [JsonPropertyName("include_chars")] public List<string>? IncludeChars { get; set; }

    [JsonPropertyName("exclude_ambiguous")] public bool? ExcludeAmbiguous { get; set; }

    public ConstraintSpec ToSpec()
    {
        var classes = new Dictionary<CharacterClass, ClassConstraint>();
        foreach (var c in Alphabet.All)
        {
            classes[c] = new ClassConstraint();
        }

        if (Classes is not null)
        {
            foreach (var (key, value) in Classes)
            {
                if (TryParseClass(key, out var cls))
                {
                    classes[cls] = new ClassConstraint(
                        Allowed: value.Allowed ?? true,
                        Min: value.Min ?? 0,
                        Max: value.Max);
                }
            }
        }

        var exclude = new List<char>();
        if (ExcludeChars is not null)
        {
            exclude.AddRange(ExcludeChars.Where(s => s.Length > 0).Select(s => s[0]));
        }

        if (ExcludeAmbiguous == true)
        {
            exclude.AddRange(Alphabet.Ambiguous);
        }

        var include = (IncludeChars ?? [])
            .Where(s => s.Length > 0)
            .Select(s => s[0])
            .ToList();

        // a class with a positive minimum must be allowed
        foreach (var c in Alphabet.All)
        {
            if (classes[c].Min > 0 && !classes[c].Allowed)
            {
                classes[c] = classes[c] with { Allowed = true };
            }
        }

        return new ConstraintSpec
        {
            Length = new LengthConstraint(Length?.Exact, Length?.Min, Length?.Max),
            Classes = classes,
            ExcludeChars = exclude.Distinct().ToList(),
            IncludeChars = include,
        };
    }

    private static bool TryParseClass(string key, out CharacterClass cls)
    {
        switch (key.Trim().ToLowerInvariant())
        {
            case "uppercase" or "upper": cls = CharacterClass.Uppercase; return true;
            case "lowercase" or "lower": cls = CharacterClass.Lowercase; return true;
            case "numeric" or "number" or "digit" or "digits": cls = CharacterClass.Numeric; return true;
            case "symbol" or "symbols" or "special": cls = CharacterClass.Symbol; return true;
            default: cls = default; return false;
        }
    }
}

public sealed class LengthArgs
{
    [JsonPropertyName("exact")] public int? Exact { get; set; }

    [JsonPropertyName("min")] public int? Min { get; set; }

    [JsonPropertyName("max")] public int? Max { get; set; }
}

public sealed class ClassArgs
{
    [JsonPropertyName("allowed")] public bool? Allowed { get; set; }

    [JsonPropertyName("min")] public int? Min { get; set; }

    [JsonPropertyName("max")] public int? Max { get; set; }
}
