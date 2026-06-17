using System.Numerics;

namespace PassGen.Engine.Tests;

public sealed class EntropyTests
{
    [Fact]
    public void Unconstrained_EqualsLengthTimesLog2Charset()
    {
        // 16 chars, all classes -> 16 * log2(75)
        var spec = ConstraintSpec.Default().WithLength(new LengthConstraint(Exact: 16));
        Assert.Equal(75, Entropy.CharsetSize(spec));
        double expected = 16 * Math.Log2(75);
        Assert.Equal(expected, Entropy.Bits(spec), precision: 6);
    }

    [Fact]
    public void FourDigitPin_Is_Log2_TenThousand()
    {
        var spec = ConstraintSpec.OnlyAllow(CharacterClass.Numeric)
            .WithLength(new LengthConstraint(Exact: 4));
        Assert.Equal(new BigInteger(10000), Entropy.ValidStringCount(spec));
        Assert.Equal(Math.Log2(10000), Entropy.Bits(spec), precision: 6);
    }

    [Fact]
    public void MinimumConstraints_ReduceEntropy()
    {
        var unconstrained = ConstraintSpec.Default().WithLength(new LengthConstraint(Exact: 16));
        var constrained = unconstrained
            .With(CharacterClass.Uppercase, new ClassConstraint(Min: 2))
            .With(CharacterClass.Lowercase, new ClassConstraint(Min: 2))
            .With(CharacterClass.Numeric, new ClassConstraint(Min: 2))
            .With(CharacterClass.Symbol, new ClassConstraint(Min: 2));

        // forcing "at least 2 of each" shrinks the valid space -> slightly LESS entropy
        Assert.True(Entropy.Bits(constrained) < Entropy.Bits(unconstrained));
        Assert.True(Entropy.Bits(constrained) > Entropy.Bits(unconstrained) - 3);
    }

    [Fact]
    public void NoAmbiguous_CostsAboutTwoBits()
    {
        var full = ConstraintSpec.Default().WithLength(new LengthConstraint(Exact: 16));
        var readable = full.WithExcludeAmbiguous();
        Assert.Equal(69, Entropy.CharsetSize(readable)); // 75 - 6 look-alikes
        double delta = Entropy.Bits(full) - Entropy.Bits(readable);
        Assert.InRange(delta, 1.5, 2.5);
    }

    [Fact]
    public void StrengthLabels_MatchThresholds()
    {
        Assert.Equal("very weak", Entropy.StrengthLabel(13));
        Assert.Equal("reasonable", Entropy.StrengthLabel(50));
        Assert.Equal("very strong", Entropy.StrengthLabel(100));
    }
}
