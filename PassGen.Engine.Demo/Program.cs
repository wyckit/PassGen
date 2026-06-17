using PassGen.Engine;

Console.WriteLine("=== PassGen.Engine — deterministic engine ===\n");

// 1) generate from specs (the LLM would build these)
(string label, ConstraintSpec spec)[] demos =
[
    ("at least 5 upper and 2 lower, max 16",
        ConstraintSpec.Default()
            .With(CharacterClass.Uppercase, new ClassConstraint(Min: 5))
            .With(CharacterClass.Lowercase, new ClassConstraint(Min: 2))
            .WithLength(new LengthConstraint(Max: 16))),
    ("only digits, exactly 6",
        ConstraintSpec.OnlyAllow(CharacterClass.Numeric).WithLength(new LengthConstraint(Exact: 6))),
    ("16 chars, all classes, no ambiguous",
        ConstraintSpec.Default().WithExcludeAmbiguous().WithLength(new LengthConstraint(Exact: 16))),
];

foreach (var (label, spec) in demos)
{
    var sample = StringGenerator.Generate(spec, seed: 1); // seeded for a stable demo
    double bits = Entropy.Bits(spec);
    Console.WriteLine($"  {label}");
    Console.WriteLine($"    -> {sample}   [{Entropy.CharsetSize(spec)} charset, {bits:F1} bits, {Entropy.StrengthLabel(bits)}]\n");
}

// 2) the LLM-facing tool path: function-call arguments -> deterministic result
Console.WriteLine("=== tool path (sage-rsrm LLM emits these arguments) ===\n");
const string llmArgs = """{ "length": { "max": 16 }, "classes": { "uppercase": { "min": 5 }, "lowercase": { "min": 2 } } }""";
var result = RandomStringTool.Execute(llmArgs); // no seed -> cryptographically secure
Console.WriteLine($"  args: {llmArgs}");
Console.WriteLine($"  -> {result.Value}   [{result.EntropyBits} bits, {result.Strength}]\n");

// 3) the schema sage-rsrm registers with its LLM
Console.WriteLine("=== tool schema (register like ChatSemanticRouting.update_cognitive_graph) ===");
Console.WriteLine(RandomStringTool.SchemaJson);
