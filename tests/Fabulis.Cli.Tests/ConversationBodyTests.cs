using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fabulis.Library;
using Xunit;

namespace Fabulis.Cli.Tests;

public class ConversationBodyTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public sealed record Fixture(string Name, List<ConversationMessage> Messages, string Body);
    public sealed record FixtureFile(List<Fixture> Cases);

    public static IEnumerable<object[]> Fixtures() => JsonSerializer.Deserialize<FixtureFile>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "conversation-body.json")), Options)!
        .Cases.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void SharedFixturesPreserveExactContentAndCanonicalBytes(Fixture fixture)
    {
        Assert.Equal(fixture.Messages, ConversationBody.Read(fixture.Body));
        Assert.Equal(Encoding.UTF8.GetBytes(fixture.Body), Encoding.UTF8.GetBytes(ConversationBody.Write(fixture.Messages)));
    }

    [Fact]
    public void EveryWhitespaceEdgeAndEscapedMarkerRoundTrips()
    {
        string[] edges = ["", "\n", "\r", "\r\n", " \t\n\n", "\n\r\n"];
        string[] payloads = ["", "**Me:**", "**StoryTeller:**", "<!-- fabulis:end -->",
            "<!-- fabulis:message {} -->", "<!-- fabulis:item {} -->", "<!-- fabulis:section {} -->",
            "é e\u0301 🦊", "---", "ordinary\\text"];
        foreach (var before in edges)
        foreach (var after in edges)
        foreach (var payload in payloads)
        for (var slashCount = 0; slashCount < 4; slashCount++)
        {
            var message = new ConversationMessage(Id, Speaker.Response,
                before + new string('\\', slashCount) + payload + after, 9);
            Assert.Equal(message, Assert.Single(ConversationBody.Read(ConversationBody.Write([message]))));
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"id\":\"bad\",\"sortOrder\":0}")]
    [InlineData("{\"id\":\"00000000-0000-0000-0000-000000000000\",\"sortOrder\":0}")]
    [InlineData("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"sortOrder\":2147483648}")]
    [InlineData("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"sortOrder\":0.1}")]
    [InlineData("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"sortOrder\":0,\"sortOrder\":1}")]
    [InlineData("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"sortOrder\":0,\"extra\":true}")]
    public void InvalidMetadataIsRejected(string metadata) => Assert.Throws<InvalidDataException>(() =>
        ConversationBody.Read($"<!-- fabulis:message {metadata} -->\n**Me:**\n\nbody\n\n<!-- fabulis:end -->\n"));

    [Fact]
    public void TruncationAndStrayProseNeverSilentlyLoseContent()
    {
        var valid = ConversationBody.Write([new(Id, Speaker.Prompt, "body", 0)]);
        for (var length = 1; length < valid.Length; length++)
            Assert.Throws<InvalidDataException>(() => ConversationBody.Read(valid[..length]));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Read(valid + "stray prose"));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Read(valid + "\n"));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Read(valid.Replace("body", "**Me:**")));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Read(valid.Replace("**Me:**", "**Unknown:**")));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Read(valid.Replace("\n", "\r\n")));
    }

    [Fact]
    public void DuplicateAndMissingIdsAreRejectedInBothDirections()
    {
        var message = new ConversationMessage(Id, Speaker.Prompt, "body", 0);
        var body = ConversationBody.Write([message]);
        Assert.Throws<InvalidDataException>(() => ConversationBody.Read(body + "\n" + body));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Write([message, message]));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Write([message with { Id = Guid.Empty }]));
        Assert.Throws<InvalidDataException>(() => ConversationBody.Write([message with { Role = (Speaker)17 }]));
    }
}
