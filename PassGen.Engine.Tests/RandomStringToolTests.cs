using System.Text.Json;

namespace PassGen.Engine.Tests;

public sealed class RandomStringToolTests
{
    [Fact]
    public void Schema_IsValidJson_WithExpectedNameAndParams()
    {
        using var doc = JsonDocument.Parse(RandomStringTool.SchemaJson);
        var root = doc.RootElement;
        Assert.Equal("generate_random_string", root.GetProperty("name").GetString());
        var props = root.GetProperty("parameters").GetProperty("properties");
        Assert.True(props.TryGetProperty("length", out _));
        Assert.True(props.TryGetProperty("classes", out _));
        Assert.True(props.GetProperty("classes").GetProperty("properties").TryGetProperty("symbol", out _));
    }

    [Fact]
    public void Execute_FromLlmArguments_ProducesValidString()
    {
        // what sage-rsrm's LLM would emit for "at least 5 upper and 2 lower, max 16"
        const string args = """
        {
          "length": { "max": 16 },
          "classes": {
            "uppercase": { "allowed": true, "min": 5 },
            "lowercase": { "allowed": true, "min": 2 }
          }
        }
        """;

        var result = RandomStringTool.Execute(args, seed: 7);
        var (ok, violations) = SpecValidator.CheckString(result.Value, result.Spec);
        Assert.True(ok, string.Join("; ", violations));
        Assert.True(result.Value.Count(char.IsUpper) >= 5);
        Assert.True(result.Value.Count(char.IsLower) >= 2);
        Assert.True(result.Value.Length <= 16);
        Assert.True(result.EntropyBits > 0);
        Assert.False(string.IsNullOrEmpty(result.Strength));
    }

    [Fact]
    public void Execute_OnlyDigits_RestrictsClasses()
    {
        const string args = """
        {
          "length": { "exact": 8 },
          "classes": {
            "uppercase": { "allowed": false },
            "lowercase": { "allowed": false },
            "symbol": { "allowed": false }
          }
        }
        """;

        var result = RandomStringTool.Execute(args, seed: 3);
        Assert.Equal(8, result.Value.Length);
        Assert.All(result.Value, ch => Assert.True(char.IsDigit(ch)));
        Assert.Equal(10, result.CharsetSize);
    }

    [Fact]
    public void Execute_ExcludeAmbiguous_Flag_Works()
    {
        const string args = """{ "length": { "exact": 30 }, "exclude_ambiguous": true }""";
        var result = RandomStringTool.Execute(args, seed: 2);
        Assert.DoesNotContain(result.Value, ch => Alphabet.Ambiguous.Contains(ch));
    }

    [Fact]
    public void Execute_EmptyArguments_DefaultsToAllAllowed()
    {
        var result = RandomStringTool.Execute("{}", seed: 0);
        Assert.True(SpecValidator.CheckString(result.Value, result.Spec).Ok);
        Assert.Equal(75, result.CharsetSize);
    }
}
