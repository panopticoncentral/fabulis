using Fabulis.Cli.Archive;
using Xunit;

namespace Fabulis.Cli.Tests;

public class ArchiveLayoutTests
{
    [Theory]
    [InlineData("Plain Name", "Plain Name")]
    [InlineData("with/slash", "with-slash")]
    [InlineData("with\\backslash", "with-backslash")]
    [InlineData("a/b\\c", "a-b-c")]
    [InlineData("collapse///runs", "collapse-runs")]
    [InlineData("  padded  ", "padded")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("\ttab\tinside", "tab-inside")]
    [InlineData("-Leading dash kept", "-Leading dash kept")]
    [InlineData("///", "Untitled")]
    [InlineData("", "Untitled")]
    [InlineData("name.-", "name")]
    [InlineData("name.-.", "name")]
    [InlineData("name -", "name")]
    public void SanitizesPathSegments(string input, string expected)
    {
        Assert.Equal(expected, ArchiveLayout.Sanitize(input));
    }

    [Fact]
    public void SanitizeReplacesControlCharacters()
    {
        var withControlChar = "a" + (char)1 + "b";

        Assert.Equal("a-b", ArchiveLayout.Sanitize(withControlChar));
        Assert.Equal("a-b", ArchiveLayout.Sanitize("a\nb"));
    }

    [Fact]
    public void RoundTripsVersionFileNames()
    {
        var name = ArchiveLayout.VersionFileName(12);

        Assert.Equal("Version 12.md", name);
        Assert.True(ArchiveLayout.TryParseVersionFileName(name, out var number));
        Assert.Equal(12, number);
    }

    [Theory]
    [InlineData("Version 1 [anthropic-claude].md")]
    [InlineData("notes.md")]
    [InlineData("Version.md")]
    [InlineData("Version 1.txt")]
    public void RejectsNonVersionFileNames(string fileName)
    {
        Assert.False(ArchiveLayout.TryParseVersionFileName(fileName, out _));
    }

    [Fact]
    public void BuildsDraftFileNameFromStampAndTitle()
    {
        var created = new DateTime(2026, 4, 11, 9, 3, 12, DateTimeKind.Utc);

        Assert.Equal("20260411T090312Z - A Night.md", ArchiveLayout.DraftFileName(created, "A Night"));
    }

    [Fact]
    public void BuildsDraftFileNameForUntitledDraft()
    {
        var created = new DateTime(2026, 4, 11, 9, 3, 12, DateTimeKind.Utc);

        Assert.Equal("20260411T090312Z - Untitled.md", ArchiveLayout.DraftFileName(created, null));
    }

    [Fact]
    public void AllocatorSuffixesCollisions()
    {
        var allocator = new NameAllocator();

        Assert.Equal("Same", allocator.Allocate("Same"));
        Assert.Equal("Same (2)", allocator.Allocate("Same"));
        Assert.Equal("Same (3)", allocator.Allocate("Same"));
        Assert.Equal("Other", allocator.Allocate("Other"));
    }

    [Fact]
    public void AllocatorTreatsCaseInsensitiveCollisionsAsCollisions()
    {
        var allocator = new NameAllocator();

        Assert.Equal("Same", allocator.Allocate("Same"));
        Assert.Equal("same (2)", allocator.Allocate("same"));
    }

    [Fact]
    public void SanitizeIsIdempotent()
    {
        var testCases = new[]
        {
            "name.-",
            "name.-.",
            "  padded  ",
            "trailing dots...",
            "-Leading dash kept",
            "///",
            "with/slash",
            "name\u00A0",      // trailing NO-BREAK SPACE
            "name\u2003",      // trailing EM SPACE
            "name\u3000",      // trailing IDEOGRAPHIC SPACE
            "name\u2028",      // trailing LINE SEPARATOR
            "name\u00A0.-",    // mixed with NO-BREAK SPACE
        };

        foreach (var input in testCases)
        {
            var first = ArchiveLayout.Sanitize(input);
            var second = ArchiveLayout.Sanitize(first);
            Assert.Equal(first, second);
        }
    }

    [Fact]
    public void StripTrailingUnicodeWhitespace()
    {
        // Trailing Unicode whitespace should be stripped just like ASCII space
        Assert.Equal("name", ArchiveLayout.Sanitize("name\u00A0"));   // NO-BREAK SPACE
        Assert.Equal("name", ArchiveLayout.Sanitize("name\u2003"));   // EM SPACE
        Assert.Equal("name", ArchiveLayout.Sanitize("name\u3000"));   // IDEOGRAPHIC SPACE
        Assert.Equal("name", ArchiveLayout.Sanitize("name\u2028"));   // LINE SEPARATOR

        // Mixed trailing whitespace, dots, and dashes
        Assert.Equal("name", ArchiveLayout.Sanitize("name\u00A0.-"));
        Assert.Equal("name", ArchiveLayout.Sanitize("name.-\u00A0"));
    }
}
