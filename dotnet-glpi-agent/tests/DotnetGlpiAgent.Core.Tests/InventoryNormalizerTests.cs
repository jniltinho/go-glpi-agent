using DotnetGlpiAgent.Core.Normalization;

namespace DotnetGlpiAgent.Core.Tests;

public sealed class InventoryNormalizerTests
{
    [Theory]
    [InlineData(" To   Be Filled By O.E.M. ")]
    [InlineData("System Serial Number")]
    [InlineData("unknown")]
    [InlineData("   ")]
    public void CleanIdentity_RemovesPlaceholderValues(string value)
    {
        Assert.Null(InventoryNormalizer.CleanIdentity(value));
    }

    [Theory]
    [InlineData(" AMD64 ", "x86_64")]
    [InlineData("x86_64", "x86_64")]
    [InlineData("aarch64", "arm64")]
    [InlineData("i686", "x86")]
    public void NormalizeArchitecture_ReturnsCanonicalValue(string value, string expected)
    {
        Assert.Equal(expected, InventoryNormalizer.NormalizeArchitecture(value));
    }

    [Theory]
    [InlineData("enabled", true)]
    [InlineData("YES", true)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void NormalizeBoolean_RecognizesCompatibleValues(string value, bool expected)
    {
        Assert.Equal(expected, InventoryNormalizer.NormalizeBoolean(value));
    }

    [Fact]
    public void NormalizeIdentifier_CanonicalizesGuid()
    {
        const string input = "{4DB2772D-97D0-47C3-BFE1-597E4E65BCAF}";

        string? result = InventoryNormalizer.NormalizeIdentifier(input);

        Assert.Equal("4db2772d-97d0-47c3-bfe1-597e4e65bcaf", result);
    }

    [Fact]
    public void NormalizeDate_ParsesWmiTimestamp()
    {
        DateTimeOffset? result = InventoryNormalizer.NormalizeDate("20260709123456.000000-180");

        Assert.True(result.HasValue);
        DateTimeOffset value = result.GetValueOrDefault();
        Assert.Equal(2026, value.Year);
        Assert.Equal(7, value.Month);
        Assert.Equal(9, value.Day);
    }

    [Fact]
    public void BytesFromKibibytes_RejectsOverflow()
    {
        Assert.Null(InventoryNormalizer.BytesFromKibibytes(ulong.MaxValue));
        Assert.Equal(2048UL, InventoryNormalizer.BytesFromKibibytes(2));
    }

    [Fact]
    public void StableKey_CollapsesWhitespaceAndIgnoresCase()
    {
        string first = InventoryNormalizer.StableKey(" Example   App ", "1.0", "X64");
        string second = InventoryNormalizer.StableKey("example app", "1.0", "x64");

        Assert.Equal(first, second);
    }
}
