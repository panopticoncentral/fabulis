using Fabulis.Server.Api;
using Xunit;

namespace Fabulis.Server.Tests;

public class NameValidationTests
{
    [Theory]
    [InlineData("Fables")]
    [InlineData("A Night In The Fens")]
    [InlineData("café stories")]
    [InlineData("dashes-and_underscores")]
    [InlineData("-Leading dash kept")]
    public void AcceptsOrdinaryNames(string name)
    {
        Assert.True(NameValidation.IsValidPathSegment(name));
    }

    [Theory]
    [InlineData("with/slash")]
    [InlineData("with\\backslash")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectsUnsafeOrEmptyNames(string? name)
    {
        Assert.False(NameValidation.IsValidPathSegment(name));
    }

    [Fact]
    public void RejectsControlCharacters()
    {
        Assert.False(NameValidation.IsValidPathSegment("a" + (char)1 + "b"));
        Assert.False(NameValidation.IsValidPathSegment("line\nbreak"));
    }

    [Fact]
    public void RejectsRelativePathSegments()
    {
        Assert.False(NameValidation.IsValidPathSegment("."));
        Assert.False(NameValidation.IsValidPathSegment(".."));
    }

    // These mirror characters that ArchiveLayout.Sanitize (Fabulis.Cli) would
    // strip or rewrite. A name accepted here but changed by Sanitize on export
    // is exactly the on-disk/in-database desync this task exists to prevent —
    // see NameValidationAgreementTests in Fabulis.Cli.Tests for the pinned
    // cross-project agreement.
    [Theory]
    [InlineData("My Story.")]
    [InlineData("Foo-")]
    [InlineData("Trailing space ")]
    [InlineData("...")]
    [InlineData("---")]
    [InlineData("Trailing NBSP\u00A0")]
    public void RejectsNamesThatSanitizeWouldChange(string name)
    {
        Assert.False(NameValidation.IsValidPathSegment(name));
    }

    [Fact]
    public void ErrorNamesTheOffendingField()
    {
        Assert.Contains("name", NameValidation.Error("name"));
    }

    [Fact]
    public void ErrorRendersExactMessage()
    {
        Assert.Equal(
            "name must not contain '/', '\\' or control characters, start with whitespace, " +
            "or end with whitespace, '.' or '-'",
            NameValidation.Error("name"));
    }
}
