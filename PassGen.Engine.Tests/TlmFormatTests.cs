using PassGen.Tlm;
using Xunit;

namespace PassGen.Engine.Tests;

/// <summary>
/// Coverage for the TLM format library (PassGen.Tlm): compiler round-trip, the .tlmz envelope,
/// the SHA-256 hasher (determinism + tamper sensitivity), and the validator.
/// </summary>
public class TlmFormatTests
{
    private static TlmPackage Sample(string label = "alpha")
    {
        var p = new TlmPackage
        {
            Manifest = new TlmManifest
            {
                Metadata = new TlmMetadata { TlmId = "t-sample", Role = TlmRole.Foundation, Priority = 100, Version = "1.0.0" },
                CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            Concepts =
            {
                new SymbolicConcept { Id = "a", Label = label, Category = "X", Description = "first" },
                new SymbolicConcept { Id = "b", Label = "beta", Category = "X", Description = "second" },
            },
            Relations = { new SymbolicRelation { SourceId = "a", TargetId = "b", Type = "Rel" } },
        };
        return p;
    }

    [Fact]
    public void Compile_serialize_deserialize_round_trips_and_validates()
    {
        var c = new TlmCompiler();
        var artifact = c.Compile(Sample());
        var bytes = c.Serialize(artifact);
        var back = c.Deserialize(bytes);

        Assert.Equal(2, back.Concepts.Count);
        Assert.Equal(1, back.Relations.Count);
        Assert.Equal("t-sample", back.Manifest.Metadata.TlmId);
        Assert.True(new TlmValidator().Validate(back).IsValid);          // checksum self-consistent
        Assert.True(bytes.AsSpan().SequenceEqual(c.Serialize(back)));    // re-serialize is identical (lossless)
    }

    [Fact]
    public void Envelope_has_magic_and_current_version()
    {
        var bytes = new TlmCompiler().Serialize(new TlmCompiler().Compile(Sample()));
        Assert.Equal((byte)'T', bytes[0]);
        Assert.Equal((byte)'L', bytes[1]);
        Assert.Equal((byte)'M', bytes[2]);
        Assert.Equal((byte)'Z', bytes[3]);
        var env = TlmzEnvelope.Peek(bytes);
        Assert.True(env.HasValue);
        Assert.Equal(TlmzEnvelope.CurrentMajorVersion, env!.Value.MajorVersion);
    }

    [Fact]
    public void Hasher_is_deterministic_and_content_sensitive()
    {
        var a = new TlmCompiler().Compile(Sample("alpha"));
        var b = new TlmCompiler().Compile(Sample("alpha"));
        var d = new TlmCompiler().Compile(Sample("DIFFERENT"));
        Assert.Equal(TlmHasher.CalculateChecksum(a), TlmHasher.CalculateChecksum(b));   // same content -> same hash
        Assert.NotEqual(TlmHasher.CalculateChecksum(a), TlmHasher.CalculateChecksum(d)); // changed content -> changed hash
    }

    [Fact]
    public void Tampering_after_compile_fails_validation()
    {
        var c = new TlmCompiler();
        var back = c.Deserialize(c.Serialize(c.Compile(Sample())));
        back.Concepts[0].Label = "tampered";                 // mutate content without re-stamping checksum
        var v = new TlmValidator().Validate(back);
        Assert.False(v.IsValid);
        Assert.Contains(v.Errors, e => e.Contains("Checksum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_flags_dangling_relations()
    {
        var pkg = Sample();
        pkg.Relations.Add(new SymbolicRelation { SourceId = "a", TargetId = "does-not-exist", Type = "Rel" });
        var artifact = new TlmCompiler().Compile(pkg);       // dangling is a warning, not a hard error (off strict)
        var v = new TlmValidator().Validate(artifact);
        Assert.Contains(v.Warnings, w => w.Contains("non-existent", StringComparison.OrdinalIgnoreCase));
    }
}
