namespace PassGen.Engine.Tests;

public sealed class GeneratorTests
{
    [Fact]
    public void HeadlineExample_SatisfiesSpec_AcrossSeeds()
    {
        // "at least 5 upper and 2 lower, max 16"
        var spec = ConstraintSpec.Default()
            .With(CharacterClass.Uppercase, new ClassConstraint(Min: 5))
            .With(CharacterClass.Lowercase, new ClassConstraint(Min: 2))
            .WithLength(new LengthConstraint(Max: 16));

        for (int seed = 0; seed < 50; seed++)
        {
            var s = StringGenerator.Generate(spec, seed);
            var (ok, violations) = SpecValidator.CheckString(s, spec);
            Assert.True(ok, $"seed {seed}: {s} -> {string.Join("; ", violations)}");
            Assert.True(s.Length <= 16);
            Assert.True(s.Count(char.IsUpper) >= 5);
            Assert.True(s.Count(char.IsLower) >= 2);
        }
    }

    [Fact]
    public void OnlyDigits_ExactLength_ProducesDigits()
    {
        var spec = ConstraintSpec.OnlyAllow(CharacterClass.Numeric)
            .WithLength(new LengthConstraint(Exact: 6));
        var s = StringGenerator.Generate(spec, seed: 1);
        Assert.Equal(6, s.Length);
        Assert.All(s, ch => Assert.True(char.IsDigit(ch)));
    }

    [Fact]
    public void ExcludeAmbiguous_NeverEmitsLookAlikes()
    {
        var spec = ConstraintSpec.Default().WithExcludeAmbiguous()
            .WithLength(new LengthConstraint(Exact: 40));
        for (int seed = 0; seed < 20; seed++)
        {
            var s = StringGenerator.Generate(spec, seed);
            Assert.DoesNotContain(s, ch => Alphabet.Ambiguous.Contains(ch));
        }
    }

    [Fact]
    public void ExactCounts_AreRespected()
    {
        // exactly 2 digits, at most 1 symbol, length 10
        var spec = ConstraintSpec.Default()
            .With(CharacterClass.Numeric, new ClassConstraint(Min: 2, Max: 2))
            .With(CharacterClass.Symbol, new ClassConstraint(Max: 1))
            .WithLength(new LengthConstraint(Exact: 10));

        for (int seed = 0; seed < 30; seed++)
        {
            var s = StringGenerator.Generate(spec, seed);
            Assert.Equal(10, s.Length);
            Assert.Equal(2, s.Count(char.IsDigit));
            Assert.True(s.Count(ch => Alphabet.Symbol.Contains(ch)) <= 1);
        }
    }

    [Fact]
    public void SeededGeneration_IsReproducible()
    {
        var spec = ConstraintSpec.Default().WithLength(new LengthConstraint(Exact: 20));
        Assert.Equal(StringGenerator.Generate(spec, 42), StringGenerator.Generate(spec, 42));
    }

    [Fact]
    public void SecureDefault_ProducesDistinct_ValidStrings()
    {
        var spec = ConstraintSpec.Default().WithLength(new LengthConstraint(Exact: 24));
        var set = new HashSet<string>();
        for (int i = 0; i < 200; i++)
        {
            var s = StringGenerator.Generate(spec); // no seed -> CSPRNG
            Assert.True(SpecValidator.CheckString(s, spec).Ok);
            set.Add(s);
        }

        Assert.True(set.Count > 190, "secure generator should produce near-unique strings");
    }

    [Fact]
    public void Fill_IsFlatUniform_NoPerClassBias()
    {
        // all classes allowed; every character should be ~equally likely (~1/75)
        var spec = ConstraintSpec.Default().WithLength(new LengthConstraint(Exact: 12));
        var counts = new Dictionary<char, int>();
        const int n = 30000;
        for (int seed = 0; seed < n; seed++)
        {
            foreach (var ch in StringGenerator.Generate(spec, seed))
            {
                counts[ch] = counts.GetValueOrDefault(ch) + 1;
            }
        }

        double total = counts.Values.Sum();
        double digitAvg = "0123456789".Average(ch => counts[ch] / total);
        double upperAvg = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Average(ch => counts[ch] / total);

        // before the fix digits were ~2.6x more likely than letters; now within ~15%
        Assert.True(Math.Abs(digitAvg - upperAvg) / upperAvg < 0.15,
            $"per-char bias too high: digit {digitAvg:P3} vs upper {upperAvg:P3}");
    }

    [Fact]
    public void InfeasibleSpec_Throws()
    {
        var spec = ConstraintSpec.Default()
            .With(CharacterClass.Uppercase, new ClassConstraint(Min: 10))
            .WithLength(new LengthConstraint(Exact: 4));
        Assert.Throws<SpecException>(() => StringGenerator.Generate(spec, 0));
    }

    [Fact]
    public void NothingAllowed_Throws()
    {
        var spec = ConstraintSpec.OnlyAllow(); // allow nothing
        Assert.Throws<SpecException>(() => StringGenerator.Generate(spec, 0));
    }
}
