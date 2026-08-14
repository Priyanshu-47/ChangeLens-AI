using ChangeLens.Application.Common;
using ChangeLens.Application.Exceptions;

namespace ChangeLens.UnitTests.Common;

public sealed class SlugifierTests
{
    [Theory]
    [InlineData("My Auth Service", "my-auth-service")]
    [InlineData("  Leading and trailing  ", "leading-and-trailing")]
    [InlineData("UPPER CASE", "upper-case")]
    [InlineData("Special !!! chars ???", "special-chars")]
    [InlineData("double--dash", "double-dash")]
    [InlineData("dots.and_underscores", "dots-and-underscores")]
    [InlineData("ünïcode", "unicode")]
    public void Slugify_ProducesExpectedSlug(string name, string expected)
    {
        Assert.Equal(expected, Slugifier.Slugify(name));
    }

    [Fact]
    public void Slugify_EmptyName_ThrowsValidation()
    {
        Assert.Throws<ValidationException>(() => Slugifier.Slugify("   "));
        Assert.Throws<ValidationException>(() => Slugifier.Slugify(string.Empty));
    }

    [Fact]
    public void Slugify_LongName_IsTruncatedToMaxLength()
    {
        var longName = new string('a', 300);
        var slug = Slugifier.Slugify(longName);

        Assert.Equal(140, slug.Length);
    }
}
