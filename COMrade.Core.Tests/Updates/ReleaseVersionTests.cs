using COMrade.Core.Updates;

namespace COMrade.Core.Tests.Updates;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.3.0", 0, 3, 0, null)]
    [InlineData("v1.12.4", 1, 12, 4, null)]
    [InlineData("0.3.1-alpha.0.3", 0, 3, 1, "alpha.0.3")]
    [InlineData("0.3.0+4f2c1a9", 0, 3, 0, null)]
    [InlineData(" 0.3.0 ", 0, 3, 0, null)]
    public void Reads_tags_and_build_versions(string text, int major, int minor, int patch, string? preRelease)
    {
        Assert.Equal(new ReleaseVersion(major, minor, patch, preRelease), ReleaseVersion.TryParse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("0.3")]
    [InlineData("0.3.0.1")]
    [InlineData("0.3.x")]
    [InlineData("0.3.0-")]
    [InlineData("-1.0.0")]
    public void Rejects_anything_else(string? text)
    {
        Assert.Null(ReleaseVersion.TryParse(text));
    }

    [Theory]
    [InlineData("0.3.0", "0.4.0")]
    [InlineData("0.3.9", "0.10.0")]
    [InlineData("0.9.0", "1.0.0")]
    [InlineData("0.3.0", "0.3.1")]
    [InlineData("0.4.0-alpha.0.1", "0.4.0")]
    public void Orders_versions(string older, string newer)
    {
        var a = ReleaseVersion.TryParse(older)!;
        var b = ReleaseVersion.TryParse(newer)!;

        Assert.True(a < b);
        Assert.True(b > a);
    }

    [Fact]
    public void A_build_after_a_release_is_not_older_than_it()
    {
        // MinVer numbers the commits after v0.3.0 as pre-releases of 0.3.1.
        Assert.True(ReleaseVersion.TryParse("0.3.1-alpha.0.3")! > ReleaseVersion.TryParse("0.3.0")!);
    }

    [Fact]
    public void Prints_like_it_was_written()
    {
        Assert.Equal("0.3.1-alpha.0.3", ReleaseVersion.TryParse("v0.3.1-alpha.0.3+abc")!.ToString());
    }
}
