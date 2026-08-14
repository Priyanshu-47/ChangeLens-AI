using ChangeLens.Application.Common;

namespace ChangeLens.UnitTests.Common;

public sealed class RepositoryUrlValidatorTests
{
    [Theory]
    [InlineData("https://github.com/org/repo.git")]
    [InlineData("http://gitlab.local/team/service")]
    [InlineData("git@github.com:org/repo.git")]
    [InlineData("file:///srv/repos/demo")]
    [InlineData("repos/demo-service")]
    [InlineData("demo-service")]
    public void IsValid_AcceptsCommonRepositoryLocations(string url)
    {
        Assert.True(RepositoryUrlValidator.IsValid(url));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url with spaces")]
    [InlineData("ftp://example.com/repo")]
    [InlineData("C:\\Users\\me\\repos\\demo")]
    [InlineData("/absolute/path/repo")]
    [InlineData("../outside")]
    [InlineData("https://")]
    public void IsValid_RejectsInvalidLocations(string url)
    {
        Assert.False(RepositoryUrlValidator.IsValid(url));
    }

    [Fact]
    public void IsValid_RejectsOverlongUrl()
    {
        Assert.False(RepositoryUrlValidator.IsValid(new string('a', 501)));
    }
}
