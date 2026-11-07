using Reliquary.BuildingBlocks.Versioning;

namespace Reliquary.BuildingBlocks.Tests;

public sealed class VersioningTests
{
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("10.20.30", true)]
    [InlineData("1.0.0-beta.1", true)]
    [InlineData("1.0.0+build.5", true)]
    [InlineData("1.0", false)]
    [InlineData("01.0.0", false)]
    [InlineData("1.0.0-", false)]
    [InlineData("v1.0.0", false)]
    public void VersionFormat(string text, bool valid) => Assert.Equal(valid, SemanticVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10")]
    [InlineData("1.0.0-alpha.10", "1.0.0-beta")]
    public void VersionOrder(string lower, string higher) =>
        Assert.True(SemanticVersion.Parse(lower) < SemanticVersion.Parse(higher));

    [Theory]
    [InlineData("^1.0", "1.0.0", true)]
    [InlineData("^1.0", "1.9.3", true)]
    [InlineData("^1.0", "2.0.0", false)]
    [InlineData("^1.2.3", "1.2.2", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("~1.2", "1.2.9", true)]
    [InlineData("~1.2", "1.3.0", false)]
    [InlineData(">=1.0.0 <2.0.0", "1.5.0", true)]
    [InlineData(">=1.0.0 <2.0.0", "2.0.0", false)]
    [InlineData(">1", "1.9.9", false)]
    [InlineData(">1", "2.0.0", true)]
    [InlineData("<=1.2", "1.2.7", true)]
    [InlineData("1.x", "1.4.0", true)]
    [InlineData("1.x", "2.0.0", false)]
    [InlineData("1.2", "1.2.5", true)]
    [InlineData("1.0.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("*", "7.0.0", true)]
    [InlineData("^2.0 || ^1.1", "1.4.0", true)]
    [InlineData("^2.0 || ^1.1", "1.0.0", false)]
    public void RangeContainsVersion(string range, string version, bool expected)
    {
        Assert.True(VersionRange.TryParse(range, out var parsed));
        Assert.Equal(expected, parsed!.Contains(SemanticVersion.Parse(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("^")]
    [InlineData("^1.a")]
    [InlineData("latest")]
    [InlineData("1.0 ||")]
    public void IncorrectRangeIsRefused(string range) => Assert.False(VersionRange.TryParse(range, out _));
}
