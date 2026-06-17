using PassGen.Tlm;
using Xunit;

namespace PassGen.Engine.Tests;

/// <summary>
/// Every shipped .tlmz in the dataset must validate clean: checksum self-consistent and no
/// dangling relations. Guards against an authored/compiled artifact regressing.
/// </summary>
public class DatasetIntegrityTests
{
    public static IEnumerable<object[]> CompiledTlmz()
        => Directory.GetFiles(FindBundle(), "rs-*.tlmz").Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(CompiledTlmz))]
    public void Each_tlmz_is_valid_and_dangling_free(string path)
    {
        var pkg = new TlmCompiler().Deserialize(File.ReadAllBytes(path));
        var v = new TlmValidator().Validate(pkg);
        Assert.True(v.IsValid, $"{Path.GetFileName(path)} invalid: {string.Join("; ", v.Errors)}");
        Assert.DoesNotContain(v.Warnings, w => w.Contains("non-existent", StringComparison.OrdinalIgnoreCase));
        Assert.True(pkg.Concepts.Count > 0);
    }

    [Fact]
    public void Bundle_has_all_seven_tlms()
        => Assert.Equal(7, Directory.GetFiles(FindBundle(), "rs-*.tlmz").Length);

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
