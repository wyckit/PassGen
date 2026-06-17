using PassGen.Engine;
using Xunit;

namespace PassGen.Engine.Tests;

/// <summary>
/// "Tell them when it doesn't make sense": contradictory/impossible requests must be rejected
/// (SpecException) rather than silently producing a wrong string; feasible ones must not throw.
/// </summary>
public class InfeasibilityTests
{
    private static readonly TlmNlu Nlu = new(FindBundle());
    private static void Run(string english) => RandomStringTool.Execute(Nlu.Resolve(english).Args, seed: 1);

    [Theory]
    [InlineData("no letters, no digits, no symbols, 12 chars")]          // every class denied
    [InlineData("2 min lowercase, 3 min uppercase, max length 4")]       // class mins (5) exceed length
    [InlineData("at least 5 uppercase, at most 2 uppercase, 16 chars")]  // class min > max
    [InlineData("at least 20 and at most 10 characters")]                // length min > max
    [InlineData("exactly 4 characters, 3 uppercase, 3 lowercase")]       // exact length below required mins
    public void Impossible_requests_are_rejected(string english)
        => Assert.Throws<SpecException>(() => Run(english));

    [Theory]
    [InlineData("2 min lowercase, 3 min uppercase, max length 6")]
    [InlineData("16 chars, 2 uppercase, no ambiguous")]
    [InlineData("letters only, between 10 and 14 characters")]
    [InlineData("only 2 digits, 12 long")]
    public void Feasible_requests_do_not_throw(string english)
    {
        var ex = Record.Exception(() => Run(english));
        Assert.Null(ex);
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
