using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Cli.Tests;

public class ConversationFormatTests
{
    private static ConversationFormat.Turn Prompt(string text, int order = 0) =>
        new(MessageRole.Prompt, text, order);

    private static ConversationFormat.Turn Response(string text, int order = 1) =>
        new(MessageRole.Response, text, order);

    [Fact]
    public void RoundTripsTurns()
    {
        List<ConversationFormat.Turn> turns = [Prompt("Tell me a story."), Response("Once upon a time.")];

        var read = ConversationFormat.Read(ConversationFormat.Write(turns));

        Assert.Equal(2, read.Count);
        Assert.Equal(MessageRole.Prompt, read[0].Role);
        Assert.Equal("Tell me a story.", read[0].Content);
        Assert.Equal(MessageRole.Response, read[1].Role);
        Assert.Equal("Once upon a time.", read[1].Content);
        Assert.Equal([0, 1], read.Select(t => t.SortOrder));
    }

    [Fact]
    public void WritesExpectedLabels()
    {
        var text = ConversationFormat.Write([Prompt("hi"), Response("hello")]);

        Assert.Contains("**Me:**", text);
        Assert.Contains("**StoryTeller:**", text);
    }

    [Fact]
    public void EscapesContentThatLooksLikeADelimiter()
    {
        var turns = new List<ConversationFormat.Turn> { Prompt("**Me:**\nnot a real delimiter") };

        var written = ConversationFormat.Write(turns);
        var read = ConversationFormat.Read(written);

        Assert.Single(read);
        Assert.Equal("**Me:**\nnot a real delimiter", read[0].Content);
    }

    [Fact]
    public void EscapingIsIdempotentAcrossRepeatedRoundTrips()
    {
        var original = "\\**StoryTeller:**";
        var turns = new List<ConversationFormat.Turn> { Prompt(original) };

        var once = ConversationFormat.Read(ConversationFormat.Write(turns));
        var twice = ConversationFormat.Read(ConversationFormat.Write(once));

        Assert.Equal(original, once[0].Content);
        Assert.Equal(original, twice[0].Content);
    }

    [Fact]
    public void PreservesInternalBlankLinesButTrimsEdges()
    {
        var turns = new List<ConversationFormat.Turn> { Prompt("first\n\nsecond") };

        var read = ConversationFormat.Read(ConversationFormat.Write(turns));

        Assert.Equal("first\n\nsecond", read[0].Content);
    }

    [Fact]
    public void ReadsCrlfInput()
    {
        var text = "**Me:**\r\n\r\nhello\r\n\r\n**StoryTeller:**\r\n\r\nworld\r\n";

        var read = ConversationFormat.Read(text);

        Assert.Equal("hello", read[0].Content);
        Assert.Equal("world", read[1].Content);
    }

    [Fact]
    public void ReadReturnsEmptyForContentWithNoTurns()
    {
        Assert.Empty(ConversationFormat.Read("just some prose\n"));
    }
}

public class BlockListFormatTests
{
    [Fact]
    public void RoundTripsBlocks()
    {
        List<string> blocks = ["first message", "second message"];

        var read = BlockListFormat.Read(BlockListFormat.Write(blocks));

        Assert.Equal(blocks, read);
    }

    [Fact]
    public void EscapesSeparatorLinesInsideContent()
    {
        List<string> blocks = ["before\n---\nafter", "second"];

        var read = BlockListFormat.Read(BlockListFormat.Write(blocks));

        Assert.Equal(blocks, read);
    }

    [Fact]
    public void EscapingIsIdempotentAcrossRepeatedRoundTrips()
    {
        List<string> blocks = ["\\---"];

        var once = BlockListFormat.Read(BlockListFormat.Write(blocks));
        var twice = BlockListFormat.Read(BlockListFormat.Write(once));

        Assert.Equal(blocks, once);
        Assert.Equal(blocks, twice);
    }

    [Fact]
    public void RoundTripsSingleBlock()
    {
        List<string> blocks = ["only one"];

        Assert.Equal(blocks, BlockListFormat.Read(BlockListFormat.Write(blocks)));
    }

    [Fact]
    public void ReadReturnsEmptyForEmptyBody()
    {
        Assert.Empty(BlockListFormat.Read("\n"));
    }
}

public class ItemListFormatTests
{
    [Fact]
    public void RoundTripsSingleLineItems()
    {
        List<string> items = ["first item", "second item"];

        Assert.Equal(items, ItemListFormat.Read(ItemListFormat.Write(items)));
    }

    [Fact]
    public void RoundTripsMultiLineItemsUsingIndentedContinuations()
    {
        List<string> items = ["first line\nsecond line", "plain"];

        var written = ItemListFormat.Write(items);

        Assert.Contains("- first line\n  second line\n", written);
        Assert.Equal(items, ItemListFormat.Read(written));
    }

    [Fact]
    public void RoundTripsItemContainingItsOwnBulletLine()
    {
        List<string> items = ["intro\n- looks like a bullet"];

        Assert.Equal(items, ItemListFormat.Read(ItemListFormat.Write(items)));
    }

    [Fact]
    public void WritesEmptyStringForEmptyList()
    {
        Assert.Equal("", ItemListFormat.Write([]));
        Assert.Empty(ItemListFormat.Read(""));
    }

    [Fact]
    public void PreservesOrder()
    {
        List<string> items = ["c", "a", "b"];

        Assert.Equal(items, ItemListFormat.Read(ItemListFormat.Write(items)));
    }
}

public class BodySectionsTests
{
    [Fact]
    public void RoundTripsSections()
    {
        List<KeyValuePair<string, string>> sections =
        [
            new("System prompt", "You are a storyteller."),
            new("Titling prompt", "You write titles.\n\nBe brief."),
        ];

        var read = BodySections.Read(BodySections.Write(sections));

        Assert.Equal("You are a storyteller.", read["System prompt"]);
        Assert.Equal("You write titles.\n\nBe brief.", read["Titling prompt"]);
    }

    [Fact]
    public void ReadReturnsEmptyWhenNoSections()
    {
        Assert.Empty(BodySections.Read("no headings here\n"));
    }

    [Fact]
    public void RoundTripsSectionContentThatLooksLikeAHeading()
    {
        List<KeyValuePair<string, string>> sections =
        [
            new("System prompt", "You are a storyteller.\n\n## Style\n\nWrite vividly."),
            new("Titling prompt", "You write titles."),
        ];

        var read = BodySections.Read(BodySections.Write(sections));

        Assert.Equal(2, read.Count);
        Assert.Equal("You are a storyteller.\n\n## Style\n\nWrite vividly.", read["System prompt"]);
        Assert.Equal("You write titles.", read["Titling prompt"]);
        Assert.False(read.ContainsKey("Style"));
    }

    [Fact]
    public void EscapingIsIdempotentAcrossRepeatedRoundTrips()
    {
        var original = "\\## Already escaped";
        List<KeyValuePair<string, string>> sections = [new("Heading", original)];

        var once = BodySections.Read(BodySections.Write(sections));
        var twice = BodySections.Read(BodySections.Write(once));

        Assert.Equal(original, once["Heading"]);
        Assert.Equal(original, twice["Heading"]);
    }

    [Fact]
    public void TrimsHeadingWhitespaceSymmetrically()
    {
        var written = BodySections.Write([new KeyValuePair<string, string>("Heading  ", "x")]);

        Assert.Contains("## Heading\n", written);
        Assert.DoesNotContain("Heading  ", written);

        var read = BodySections.Read(written);

        Assert.Equal("x", read["Heading"]);
        Assert.False(read.ContainsKey("Heading  "));
    }

    [Fact]
    public void LinesResemblingButNotMatchingAHeadingAreKeptAsPlainContent()
    {
        var text = "#NoSpace\n### Deeper";
        List<KeyValuePair<string, string>> sections = [new("Heading", text)];

        var written = BodySections.Write(sections);
        var read = BodySections.Read(written);

        Assert.Single(read);
        Assert.Equal(text, read["Heading"]);
        Assert.DoesNotContain("\\#NoSpace", written);
        Assert.DoesNotContain("\\### Deeper", written);
    }
}
