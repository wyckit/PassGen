using PassGen.Engine;
using Xunit;

namespace PassGen.Engine.Tests;

/// <summary>
/// Locks the two Symbolic Intent Architecture demo scenarios rendered by `passgen --trace`.
/// These assert the PIPELINE behavior (resolve -> validate -> execute -> verify), which is the
/// real contract the five panels visualize — not the console formatting.
/// </summary>
public class TraceScenarioTests
{
    private static readonly TlmNlu Nlu = new(FindBundle());

    [Fact]
    public void Success_scenario_resolves_validates_executes_and_verifies()
    {
        // "give me a 20-character password with 3 numbers, 2 symbols, and no confusing characters"
        var r = Nlu.Resolve("give me a 20-character password with 3 numbers, 2 symbols, and no confusing characters");
        var spec = r.Args.ToSpec();

        // [3] Validation must pass (satisfiable) BEFORE any generation.
        SpecValidator.Validate(spec);

        // [2] Resolved intent is what we expect.
        Assert.Equal(20, spec.Length.Exact);
        Assert.True(spec.Classes[CharacterClass.Numeric].Min >= 3);
        Assert.True(spec.Classes[CharacterClass.Symbol].Min >= 2);
        Assert.NotEmpty(spec.ExcludeChars);                       // ambiguous look-alikes excluded
        Assert.Empty(r.Unsupported);

        // [4] Execution + [5] Verification: output satisfies the spec.
        var result = RandomStringTool.Execute(r.Args, seed: 1);
        Assert.Equal(20, result.Value.Length);
        Assert.True(result.Value.Count(char.IsDigit) >= 3);
        var (ok, violations) = SpecValidator.CheckString(result.Value, result.Spec);
        Assert.True(ok, string.Join("; ", violations));
    }

    [Fact]
    public void Failclosed_scenario_is_rejected_at_validation_so_the_tool_never_runs()
    {
        // "make me a 4-character password with 10 uppercase letters" — impossible.
        var r = Nlu.Resolve("make me a 4-character password with 10 uppercase letters");
        var spec = r.Args.ToSpec();

        // [3] Validation REJECTS — and because Execute validates first, the generator
        // is never reached (fail-closed): the same SpecException guards both paths.
        Assert.Throws<SpecException>(() => SpecValidator.Validate(spec));
        Assert.Throws<SpecException>(() => RandomStringTool.Execute(r.Args, seed: 1));
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
        throw new DirectoryNotFoundException("dataset/compiled not found");
    }
}
