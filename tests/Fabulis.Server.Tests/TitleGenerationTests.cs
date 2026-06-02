using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Server.Tests;

public class TitleGenerationTests
{
    [Fact]
    public void BuildStoryBodyJoinsOnlyResponsesInSortOrder()
    {
        var messages = new List<DraftMessage>
        {
            new() { Content = "second response", Role = MessageRole.Response, SortOrder = 3 },
            new() { Content = "the user prompt", Role = MessageRole.Prompt, SortOrder = 0 },
            new() { Content = "first response", Role = MessageRole.Response, SortOrder = 1 },
        };

        Assert.Equal("first response\n\nsecond response", TitleGeneration.BuildStoryBody(messages));
    }

    [Fact]
    public void BuildStoryBodyReturnsEmptyWhenNoResponses()
    {
        var messages = new List<DraftMessage>
        {
            new() { Content = "only a prompt", Role = MessageRole.Prompt, SortOrder = 0 },
        };

        Assert.Equal("", TitleGeneration.BuildStoryBody(messages));
    }

    [Theory]
    [InlineData("Hello World", "Hello World")]
    [InlineData("  Hello World  ", "Hello World")]
    [InlineData("\"Hello World\"", "Hello World")]
    [InlineData("'Hello World'", "Hello World")]
    [InlineData("“Hello World”", "Hello World")]
    [InlineData("Hello World\n\nextra commentary", "Hello World")]
    [InlineData("\n\n  \"The Quiet Hour\"  ", "The Quiet Hour")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void CleanTitleNormalizesModelOutput(string raw, string expected)
    {
        Assert.Equal(expected, TitleGeneration.CleanTitle(raw));
    }
}
