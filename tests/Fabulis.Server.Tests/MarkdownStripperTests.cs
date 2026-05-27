using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Server.Tests;

public class MarkdownStripperTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("**bold**", "bold")]
    [InlineData("*em*", "em")]
    [InlineData("_em_", "em")]
    [InlineData("# Heading", "Heading")]
    [InlineData("## Subhead\n\nBody.", "Subhead Body.")]
    [InlineData("[Anthropic](https://anthropic.com)", "Anthropic")]
    [InlineData("Inline `code` here", "Inline code here")]
    [InlineData("> a quote", "a quote")]
    [InlineData("Footnote ref[^1] tail", "Footnote ref tail")]
    [InlineData("Multiple   spaces\tand\nlines", "Multiple spaces and lines")]
    [InlineData("score < 10 and y > 5", "score < 10 and y > 5")]
    public void PreservesProseRemovesSyntax(string input, string expected)
    {
        Assert.Equal(expected, MarkdownStripper.ToPlainText(input));
    }

    [Fact]
    public void DropsFencedCodeBlocks()
    {
        var input = "Before\n\n```python\nprint(\"hi\")\n```\n\nAfter";
        Assert.Equal("Before After", MarkdownStripper.ToPlainText(input));
    }

    [Fact]
    public void StripsHtmlTagsButKeepsContents()
    {
        var input = "Hello <span class=\"x\">world</span>!";
        Assert.Equal("Hello world!", MarkdownStripper.ToPlainText(input));
    }

    [Fact]
    public void HandlesBoldInsideHeading()
    {
        Assert.Equal("Important note", MarkdownStripper.ToPlainText("# **Important** note"));
    }

    [Fact]
    public void HandlesLinkInsideList()
    {
        Assert.Equal("See docs", MarkdownStripper.ToPlainText("- See [docs](https://x)"));
    }

    [Fact]
    public void EmptyAndWhitespaceReturnEmpty()
    {
        Assert.Equal("", MarkdownStripper.ToPlainText(""));
        Assert.Equal("", MarkdownStripper.ToPlainText("   \n\t  "));
    }
}
