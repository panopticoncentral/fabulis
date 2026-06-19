using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Server.Tests;

public class StorySummaryTests
{
    [Fact]
    public void BuildVersionBodyJoinsOnlyResponsesInSortOrder()
    {
        var messages = new List<StoryMessage>
        {
            new() { Content = "second response", Role = MessageRole.Response, SortOrder = 3 },
            new() { Content = "the user prompt", Role = MessageRole.Prompt, SortOrder = 0 },
            new() { Content = "first response", Role = MessageRole.Response, SortOrder = 1 },
        };

        Assert.Equal("first response\n\nsecond response", StorySummary.BuildVersionBody(messages));
    }

    [Fact]
    public void BuildVersionBodyReturnsEmptyWhenNoResponses()
    {
        var messages = new List<StoryMessage>
        {
            new() { Content = "only a prompt", Role = MessageRole.Prompt, SortOrder = 0 },
        };

        Assert.Equal("", StorySummary.BuildVersionBody(messages));
    }

    [Fact]
    public void ComposeUserMessageReturnsContentOnlyWhenNoPriorSummary()
    {
        Assert.Equal("the story", StorySummary.ComposeUserMessage(null, "the story"));
        Assert.Equal("the story", StorySummary.ComposeUserMessage("   ", "the story"));
    }

    [Fact]
    public void ComposeUserMessageIncludesPriorSummaryWhenPresent()
    {
        var result = StorySummary.ComposeUserMessage("old summary", "new version text");

        Assert.Equal(
            "EXISTING SUMMARY:\nold summary\n\nNEW STORY CONTENT:\nnew version text",
            result);
    }

    [Theory]
    [InlineData("A tidy paragraph.", "A tidy paragraph.")]
    [InlineData("  leading and trailing  ", "leading and trailing")]
    [InlineData("line one\n\nline two", "line one line two")]
    [InlineData("line one\n  \nline two\n", "line one line two")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CleanSummaryCollapsesToSingleParagraph(string raw, string expected)
    {
        Assert.Equal(expected, StorySummary.CleanSummary(raw));
    }

    [Theory]
    [InlineData(null, 1, true)]   // never summarized
    [InlineData(0, 2, true)]      // stale: new version exists
    [InlineData(2, 2, false)]     // up to date
    [InlineData(3, 2, false)]     // defensive: ahead, treat as done
    public void NeedsWorkComparesSummarizedVersionToLatest(int? summarizedThrough, int latest, bool expected)
    {
        Assert.Equal(expected, StorySummary.NeedsWork(summarizedThrough, latest));
    }
}
