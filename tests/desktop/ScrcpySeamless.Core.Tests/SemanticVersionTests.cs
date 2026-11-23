using ScrcpySeamless.Core.Application.Updates;
using Xunit;

namespace ScrcpySeamless.Core.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("2.0.0")]
    [InlineData("2.0.0-alpha.1")]
    [InlineData("2.0.0-0.3.7")]
    [InlineData("2.0.0-x-y-z.--")]
    [InlineData("2.0.0+001.build")]
    [InlineData("2.0.0-beta.11+git.abc123")]
    [InlineData("999999999999999999999999.0.0")]
    public void AcceptsStrictBoundedSemver(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out SemanticVersion? version));
        Assert.Equal(value, version!.Value);
        Assert.Equal(value, version.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-beta.01")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3+a+b")]
    [InlineData("1.2.3-a..b")]
    [InlineData("1.2.3+a..b")]
    [InlineData("1.2.3-a_b")]
    [InlineData("1.2.3-α")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3\n")]
    [InlineData("+1.2.3")]
    public void RejectsCoercionAndMalformedVersions(string value)
    {
        Assert.False(SemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void EnforcesInputBoundWithoutLimitingNumericPrecision()
    {
        Assert.False(SemanticVersion.TryParse(null, out _));
        Assert.False(SemanticVersion.TryParse(new string('1', SemanticVersion.MaximumLength) + ".0.0", out _));
        SemanticVersion small = Parse("999999999999999999999999.0.0");
        SemanticVersion large = Parse("1000000000000000000000000.0.0");
        Assert.True(large.CompareTo(small) > 0);
        Assert.True(Parse("2.0.0-1000000000000000000000000").CompareTo(Parse("2.0.0-999999999999999999999999")) > 0);
    }

    [Fact]
    public void ImplementsOfficialPrereleaseOrdering()
    {
        string[] ordered = ["1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta",
            "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0"];

        for (int index = 1; index < ordered.Length; index++)
        {
            SemanticVersion previous = Parse(ordered[index - 1]);
            SemanticVersion next = Parse(ordered[index]);
            Assert.True(next.CompareTo(previous) > 0);
            Assert.True(previous.CompareTo(next) < 0);
        }
    }

    [Fact]
    public void BuildMetadataDoesNotChangePrecedence()
    {
        Assert.Equal(0, Parse("2.0.0+local.123").CompareTo(Parse("2.0.0+remote.456")));
        Assert.Equal(0, Parse("2.0.0-beta.1+local").CompareTo(Parse("2.0.0-beta.1")));
        Assert.True(Parse("2.0.0").CompareTo(null) > 0);
        Assert.True(Parse("2.1.0").CompareTo(Parse("2.0.99")) > 0);
    }

    [Theory]
    [InlineData("2.0.0", false, true)]
    [InlineData("2.0.0", true, false)]
    [InlineData("2.0.0-beta.1", false, false)]
    [InlineData("2.0.0-beta.1", true, false)]
    public void StableChannelRejectsEitherPrereleaseIndicator(string version, bool providerPrerelease, bool stable)
    {
        SemanticVersion parsed = Parse(version);
        Assert.Equal(stable, ReleaseChannelPolicy.Includes(parsed, providerPrerelease, ReleaseChannel.Stable));
        Assert.True(ReleaseChannelPolicy.Includes(parsed, providerPrerelease, ReleaseChannel.Prerelease));
        Assert.False(ReleaseChannelPolicy.Includes(parsed, providerPrerelease, (ReleaseChannel)42));
    }

    private static SemanticVersion Parse(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out SemanticVersion? version));
        return version!;
    }
}
