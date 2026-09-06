using Fabulis.Cli.Archive;
using Xunit;

namespace Fabulis.Cli.Tests;

public class FrontMatterTests
{
    [Fact]
    public void ParsesFieldsAndBody()
    {
        var content = "---\nmodel: anthropic/claude-sonnet-4\ntemperature: 0.7\n---\n\nbody text\n";

        var (fields, body) = FrontMatter.Parse(content);

        Assert.Equal("anthropic/claude-sonnet-4", fields["model"]);
        Assert.Equal("0.7", fields["temperature"]);
        Assert.Equal("body text", body.Trim());
    }

    [Fact]
    public void ParseReturnsEmptyFieldsWhenNoFrontMatter()
    {
        var (fields, body) = FrontMatter.Parse("- just an item\n");

        Assert.Empty(fields);
        Assert.Equal("- just an item", body.Trim());
    }

    [Fact]
    public void OmitsNullValuedKeys()
    {
        var result = FrontMatter.Serialize(
            [new("model", "gpt"), new("topP", null), new("topK", "40")], "");

        Assert.Contains("model: gpt", result);
        Assert.Contains("topK: 40", result);
        Assert.DoesNotContain("topP", result);
    }

    [Theory]
    [InlineData("  leading space")]
    [InlineData("trailing space  ")]
    [InlineData("has: a colon space")]
    [InlineData("- starts with dash")]
    [InlineData("#starts with hash")]
    [InlineData("\"already quoted\"")]
    [InlineData("back\\slash")]
    public void RoundTripsValuesNeedingQuoting(string value)
    {
        var serialized = FrontMatter.Serialize([new("title", value)], "");

        var (fields, _) = FrontMatter.Parse(serialized);

        Assert.Equal(value, fields["title"]);
    }

    [Fact]
    public void RoundTripsPlainValueUnquoted()
    {
        var serialized = FrontMatter.Serialize([new("model", "anthropic/claude-sonnet-4")], "");

        Assert.Contains("model: anthropic/claude-sonnet-4\n", serialized);
    }

    [Fact]
    public void RoundTripsTimestampsAsUtc()
    {
        var when = new DateTime(2026, 4, 11, 9, 3, 12, DateTimeKind.Utc).AddTicks(1234567);

        var text = FrontMatter.FormatTimestamp(when);
        var parsed = FrontMatter.ParseTimestamp(text);

        Assert.EndsWith("Z", text);
        Assert.Equal(when, parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Fact]
    public void ParseTimestampReturnsNullForGarbage()
    {
        Assert.Null(FrontMatter.ParseTimestamp("not a date"));
        Assert.Null(FrontMatter.ParseTimestamp(null));
    }

    [Fact]
    public void FormatsDoublesInvariantly()
    {
        Assert.Equal("0.7", FrontMatter.FormatDouble(0.7));
    }

    [Fact]
    public void UnterminatedFrontMatterThrows()
    {
        Assert.Throws<InvalidDataException>(
            () => FrontMatter.Parse("---\nmodel: gpt\nno closing fence\n"));
    }

    [Fact]
    public void BodyIsPreservedVerbatimIncludingInternalDashes()
    {
        var content = "---\nk: v\n---\n\nfirst\n\n---\n\nsecond\n";

        var (_, body) = FrontMatter.Parse(content);

        Assert.Contains("\n---\n", body);
    }

    [Fact]
    public void RoundTripsValueWithEmbeddedNewline()
    {
        var value = "line1\n---";

        var serialized = FrontMatter.Serialize([new("title", value)], "");
        var (fields, _) = FrontMatter.Parse(serialized);

        Assert.Equal(value, fields["title"]);
    }

    [Fact]
    public void RoundTripsValueWithCarriageReturnNewline()
    {
        var value = "line1\r\nline2";

        var serialized = FrontMatter.Serialize([new("title", value)], "");
        var (fields, _) = FrontMatter.Parse(serialized);

        Assert.Equal(value, fields["title"]);
    }

    // Parse is static and cannot reach the importer's SeenPaths, so an
    // ignored line has to come back to the caller some other way: without
    // this, a hand-edited file that lost a field reads as fully understood
    // and --mirror deletes the row it no longer describes.
    [Fact]
    public void ReportsAnIgnoredLineWithNoColon()
    {
        var (fields, _) = FrontMatter.Parse("---\nmodel: gpt\ncreated\n---\n", out var malformed);

        Assert.True(malformed);
        Assert.Equal("gpt", fields["model"]);
        Assert.False(fields.ContainsKey("created"));
    }

    // The commonest slip: the colon after the key is deleted, leaving the
    // timestamp's own colon as the first one on the line. Parsed naively this
    // is a well-formed field named "created 2026-04-11T09".
    [Fact]
    public void ReportsAnIgnoredLineWhoseOnlyColonIsInsideTheValue()
    {
        var (fields, _) = FrontMatter.Parse(
            "---\ncreated 2026-04-11T09:03:12.0000000Z\n---\n", out var malformed);

        Assert.True(malformed);
        Assert.Empty(fields);
    }

    [Fact]
    public void ReportsNoMalformedLinesForAnOrdinaryFile()
    {
        FrontMatter.Parse("---\nmodel: gpt\ncreated: 2026-04-11T09:03:12.0000000Z\n---\n",
            out var malformed);

        Assert.False(malformed);
    }

    [Fact]
    public void RoundTripsLiteralBackslashNDistinctFromNewline()
    {
        var value = "line1\\nline2";

        var serialized = FrontMatter.Serialize([new("title", value)], "");
        var (fields, _) = FrontMatter.Parse(serialized);

        Assert.Equal(value, fields["title"]);
        Assert.DoesNotContain('\n', fields["title"]);
    }
}
