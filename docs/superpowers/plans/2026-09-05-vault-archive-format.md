# Vault Archive Format Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Fabulis.Cli` export and import the entire vault — prompts, one-liners, tropes, storytellers and settings included — in a versioned, hand-editable directory format that could eventually replace SQLite as the system of record.

**Architecture:** Format knowledge is extracted out of the two flat service classes into six pure-function helpers under `src/Fabulis.Cli/Archive/`, which are testable without a database. `VaultExporter` and `VaultImporter` then contain only traversal. Identity is the filesystem path — no row ids are written — so renaming a directory renames the thing it holds.

**Tech Stack:** .NET 10, EF Core + SQLite, xunit 2.9.3. No new package references; the YAML subset is hand-rolled.

**Spec:** `docs/superpowers/specs/2026-09-05-vault-archive-format-design.md`

## Global Constraints

- Target framework `net10.0`, `Nullable` and `ImplicitUsings` enabled, matching every existing project.
- **No new NuGet dependencies.** The front-matter subset is hand-rolled deliberately.
- Test project must NOT reference `Microsoft.Data.Sqlite` or `Microsoft.EntityFrameworkCore.Sqlite` directly — those come transitively from `Fabulis.Server`, and referencing the non-`.Core` packages drags the bundled `e_sqlite3` native library back in and breaks SQLCipher. Copy the `ItemGroup` from `tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj` verbatim.
- All timestamps written to disk are UTC in round-trip (`O`) format, e.g. `2026-04-11T09:03:12.1234567Z`.
- `formatVersion` is `1`. Import refuses any other value.
- Archive types are `public` (the test project references the CLI exe project; no `InternalsVisibleTo`).
- Every new file uses LF line endings, matching `4fbf4f4`.
- Existing archives are NOT supported. Legacy parsing is deleted, not preserved.

## File structure

| Path | Responsibility |
|---|---|
| `src/Fabulis.Cli/Archive/ArchiveLayout.cs` | Directory/file names, version + draft filename shapes, `Sanitize`, `NameAllocator` |
| `src/Fabulis.Cli/Archive/FrontMatter.cs` | The YAML scalar subset: parse, serialize, scalar formatting |
| `src/Fabulis.Cli/Archive/BodySections.cs` | `## Heading` body sections (storyteller prompts, multi-line settings) |
| `src/Fabulis.Cli/Archive/ConversationFormat.cs` | `**Me:**` / `**StoryTeller:**` turns, with delimiter escaping |
| `src/Fabulis.Cli/Archive/BlockListFormat.cs` | `---`-separated blocks (prompt messages), with separator escaping |
| `src/Fabulis.Cli/Archive/ItemListFormat.cs` | Bullet lists with indented continuations (one-liners, tropes) |
| `src/Fabulis.Cli/VaultExporter.cs` | Traversal: vault → directory tree |
| `src/Fabulis.Cli/VaultImporter.cs` | Traversal: directory tree → vault, merge and mirror |
| `src/Fabulis.Cli/Program.cs` | Flag parsing, console I/O, mirror confirmation |
| `src/Fabulis.Cli/SillyTavernConvertService.cs` | Updated to emit the new draft shape |
| `tests/Fabulis.Cli.Tests/` | New xunit project |

Deleted: `CategoryExportService.cs`, `CategoryImportService.cs`, `DraftMarkdownWriter.cs`.

`BodySections.cs` and `BlockListFormat.cs` are a refinement of the spec's four-file `Archive/` sketch: the spec folded section parsing into `FrontMatter` and left prompt separators unassigned. Six focused files beat two overloaded ones.

---

### Task 1: Test project + front matter

**Files:**
- Create: `tests/Fabulis.Cli.Tests/Fabulis.Cli.Tests.csproj`
- Create: `src/Fabulis.Cli/Archive/FrontMatter.cs`
- Modify: `Fabulis.slnx`
- Test: `tests/Fabulis.Cli.Tests/FrontMatterTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `Fabulis.Cli.Archive.FrontMatter.Parse(string content) -> (Dictionary<string,string> Fields, string Body)`
  - `FrontMatter.Serialize(IEnumerable<KeyValuePair<string,string?>> fields, string body) -> string`
  - `FrontMatter.FormatTimestamp(DateTime utc) -> string`
  - `FrontMatter.ParseTimestamp(string? raw) -> DateTime?`
  - `FrontMatter.FormatDouble(double value) -> string`

- [ ] **Step 1: Create the test project**

`tests/Fabulis.Cli.Tests/Fabulis.Cli.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <!-- EF Core SQLite and the SQLCipher provider come from the Fabulis.Cli
         reference below, which project-references Fabulis.Server. Referencing
         the non-.Core packages here would drag the bundled e_sqlite3 native
         library back in. -->
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Fabulis.Cli\Fabulis.Cli.csproj" />
  </ItemGroup>

</Project>
```

Add to `Fabulis.slnx` inside the existing `/tests/` folder element:

```xml
    <Project Path="tests/Fabulis.Cli.Tests/Fabulis.Cli.Tests.csproj" />
```

- [ ] **Step 2: Write the failing tests**

`tests/Fabulis.Cli.Tests/FrontMatterTests.cs`:

```csharp
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
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Cli.Tests`
Expected: compile failure — `FrontMatter` does not exist.

- [ ] **Step 4: Implement FrontMatter**

`src/Fabulis.Cli/Archive/FrontMatter.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Fabulis.Cli.Archive;

/// <summary>
/// A deliberately minimal YAML front-matter subset: a "---" fenced block of
/// flat "key: value" scalar lines at the very top of a markdown file. No
/// nesting, no lists, no anchors. Hand-rolled so the CLI takes no YAML
/// dependency; forward compatibility is guarded by the manifest's
/// formatVersion rather than by validating keys here.
/// </summary>
public static class FrontMatter
{
    private const string Fence = "---";

    public static (Dictionary<string, string> Fields, string Body) Parse(string content)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalized = content.Replace("\r\n", "\n");

        if (!normalized.StartsWith(Fence + "\n", StringComparison.Ordinal))
            return (fields, normalized);

        var lines = normalized.Split('\n');
        var end = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i] == Fence) { end = i; break; }
        }

        if (end < 0)
            throw new InvalidDataException(
                "Front matter opened with '---' but no closing '---' line was found.");

        for (var i = 1; i < end; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                Console.Error.WriteLine($"warn: ignoring malformed front-matter line: {line}");
                continue;
            }

            var key = line[..colon].Trim();
            var value = Unquote(line[(colon + 1)..].Trim());
            if (key.Length > 0)
                fields[key] = value;
        }

        var body = string.Join('\n', lines[(end + 1)..]);
        return (fields, body.TrimStart('\n'));
    }

    public static string Serialize(IEnumerable<KeyValuePair<string, string?>> fields, string body)
    {
        var sb = new StringBuilder();
        sb.Append(Fence).Append('\n');
        foreach (var (key, value) in fields)
        {
            if (value is null) continue;
            sb.Append(key).Append(": ").Append(Quote(value)).Append('\n');
        }
        sb.Append(Fence).Append('\n');

        if (body.Length > 0)
            sb.Append('\n').Append(body.TrimEnd('\n')).Append('\n');

        return sb.ToString();
    }

    private static string Quote(string value)
    {
        var needsQuoting =
            value.Length == 0 ||
            value != value.Trim() ||
            value.Contains(": ", StringComparison.Ordinal) ||
            value.Contains('\n') ||
            value.Contains('\\') ||
            "-#\"'[{".Contains(value[0]);

        if (!needsQuoting) return value;

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            return value;

        var inner = value[1..^1];
        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                i++;
                sb.Append(inner[i]);
            }
            else
            {
                sb.Append(inner[i]);
            }
        }
        return sb.ToString();
    }

    public static string FormatTimestamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    public static DateTime? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
            return null;

        return parsed.Kind == DateTimeKind.Utc
            ? parsed
            : DateTime.SpecifyKind(parsed.ToUniversalTime(), DateTimeKind.Utc);
    }

    public static string FormatDouble(double value) =>
        value.ToString("R", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Fabulis.Cli.Tests`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add Fabulis.slnx tests/Fabulis.Cli.Tests src/Fabulis.Cli/Archive/FrontMatter.cs
git commit -m "Add Fabulis.Cli test project and front-matter format"
```

---

### Task 2: Conversation, block-list and item-list formats

**Files:**
- Create: `src/Fabulis.Cli/Archive/ConversationFormat.cs`
- Create: `src/Fabulis.Cli/Archive/BlockListFormat.cs`
- Create: `src/Fabulis.Cli/Archive/ItemListFormat.cs`
- Create: `src/Fabulis.Cli/Archive/BodySections.cs`
- Test: `tests/Fabulis.Cli.Tests/TextFormatTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (these are independent).
- Produces:
  - `ConversationFormat.Turn(MessageRole Role, string Content, int SortOrder)` record
  - `ConversationFormat.Write(IEnumerable<Turn> turns) -> string`
  - `ConversationFormat.Read(string body) -> List<Turn>`
  - `ConversationFormat.TrimBlankEdges(List<string> lines) -> string` (internal, reused by the other two list formats)
  - `BlockListFormat.Write(IEnumerable<string> blocks) -> string`
  - `BlockListFormat.Read(string body) -> List<string>`
  - `ItemListFormat.Write(IEnumerable<string> items) -> string`
  - `ItemListFormat.Read(string content) -> List<string>`
  - `BodySections.Write(IEnumerable<KeyValuePair<string,string>> sections) -> string`
  - `BodySections.Read(string body) -> Dictionary<string,string>`

The escaping rule has the same shape in `ConversationFormat` and `BlockListFormat`: a content line that is a delimiter, optionally already preceded by backslashes, gains one more backslash on write and loses exactly one on read. That makes the escape idempotent under repeated round-trips, which the tests check explicitly.

- [ ] **Step 1: Write the failing tests**

`tests/Fabulis.Cli.Tests/TextFormatTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Cli.Tests`
Expected: compile failure — the four format types do not exist.

- [ ] **Step 3: Implement ConversationFormat**

`src/Fabulis.Cli/Archive/ConversationFormat.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using Fabulis.Server.Data;

namespace Fabulis.Cli.Archive;

/// <summary>
/// The "**Me:** / **StoryTeller:**" turn format shared by story versions and
/// drafts. A content line that would itself read as a delimiter is escaped
/// with a leading backslash, so a message quoting the format round-trips
/// intact — the pre-versioning format corrupted those silently.
/// </summary>
public static partial class ConversationFormat
{
    private const string PromptLabel = "**Me:**";
    private const string ResponseLabel = "**StoryTeller:**";

    public sealed record Turn(MessageRole Role, string Content, int SortOrder);

    // Group 1 captures leading backslashes: zero means a real delimiter, one
    // or more means escaped content.
    [GeneratedRegex(@"^(\\*)\*\*(Me|StoryTeller):\*\*[ \t]*$")]
    private static partial Regex DelimiterPattern();

    public static string Write(IEnumerable<Turn> turns)
    {
        var sb = new StringBuilder();
        foreach (var turn in turns.OrderBy(t => t.SortOrder))
        {
            sb.Append(turn.Role == MessageRole.Prompt ? PromptLabel : ResponseLabel).Append('\n');
            sb.Append('\n');
            foreach (var line in turn.Content.Replace("\r\n", "\n").Split('\n'))
                sb.Append(Escape(line)).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static List<Turn> Read(string body)
    {
        var turns = new List<Turn>();
        var lines = body.Replace("\r\n", "\n").Split('\n');

        MessageRole? role = null;
        var buffer = new List<string>();
        var sortOrder = 0;

        void Flush()
        {
            if (role is null) return;
            turns.Add(new Turn(role.Value, TrimBlankEdges(buffer), sortOrder++));
            buffer.Clear();
        }

        foreach (var line in lines)
        {
            var match = DelimiterPattern().Match(line);
            if (match.Success && match.Groups[1].Value.Length == 0)
            {
                Flush();
                role = match.Groups[2].Value == "Me" ? MessageRole.Prompt : MessageRole.Response;
            }
            else if (role is not null)
            {
                buffer.Add(Unescape(line));
            }
        }

        Flush();
        return turns;
    }

    private static string Escape(string line) =>
        DelimiterPattern().IsMatch(line) ? "\\" + line : line;

    private static string Unescape(string line)
    {
        var match = DelimiterPattern().Match(line);
        return match.Success && match.Groups[1].Value.Length > 0 ? line[1..] : line;
    }

    /// <summary>Drops blank leading and trailing lines, keeping internal ones.</summary>
    internal static string TrimBlankEdges(List<string> lines)
    {
        var start = 0;
        while (start < lines.Count && lines[start].Trim().Length == 0) start++;

        var end = lines.Count - 1;
        while (end >= start && lines[end].Trim().Length == 0) end--;

        return start > end ? string.Empty : string.Join('\n', lines[start..(end + 1)]);
    }
}
```

- [ ] **Step 4: Implement BlockListFormat**

`src/Fabulis.Cli/Archive/BlockListFormat.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace Fabulis.Cli.Archive;

/// <summary>
/// Ordered content blocks separated by a line that is exactly "---". Used for
/// PromptMessages, which carry no role — only content and sort order. Escaping
/// mirrors <see cref="ConversationFormat"/>.
/// </summary>
public static partial class BlockListFormat
{
    private const string Separator = "---";

    [GeneratedRegex(@"^(\\*)---[ \t]*$")]
    private static partial Regex SeparatorPattern();

    public static string Write(IEnumerable<string> blocks)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var block in blocks)
        {
            if (!first)
                sb.Append('\n').Append(Separator).Append('\n').Append('\n');
            first = false;

            foreach (var line in block.Replace("\r\n", "\n").Split('\n'))
                sb.Append(Escape(line)).Append('\n');
        }
        return sb.ToString();
    }

    public static List<string> Read(string body)
    {
        var blocks = new List<string>();
        var buffer = new List<string>();
        var sawContent = false;

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var match = SeparatorPattern().Match(line);
            if (match.Success && match.Groups[1].Value.Length == 0)
            {
                blocks.Add(ConversationFormat.TrimBlankEdges(buffer));
                buffer.Clear();
                sawContent = true;
            }
            else
            {
                if (line.Trim().Length > 0) sawContent = true;
                buffer.Add(Unescape(line));
            }
        }

        if (!sawContent) return [];

        blocks.Add(ConversationFormat.TrimBlankEdges(buffer));
        return blocks;
    }

    private static string Escape(string line) =>
        SeparatorPattern().IsMatch(line) ? "\\" + line : line;

    private static string Unescape(string line)
    {
        var match = SeparatorPattern().Match(line);
        return match.Success && match.Groups[1].Value.Length > 0 ? line[1..] : line;
    }
}
```

- [ ] **Step 5: Implement ItemListFormat**

`src/Fabulis.Cli/Archive/ItemListFormat.cs`:

```csharp
using System.Text;

namespace Fabulis.Cli.Archive;

/// <summary>
/// A markdown bullet list, one bullet per item, continuation lines indented
/// two spaces. The indent is what lets an item whose own text starts with
/// "- " round-trip without needing an escape rule.
/// </summary>
public static class ItemListFormat
{
    private const string Bullet = "- ";
    private const string Indent = "  ";

    public static string Write(IEnumerable<string> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            var lines = item.Replace("\r\n", "\n").Split('\n');
            sb.Append(Bullet).Append(lines[0]).Append('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) sb.Append('\n');
                else sb.Append(Indent).Append(lines[i]).Append('\n');
            }
        }
        return sb.ToString();
    }

    public static List<string> Read(string content)
    {
        var items = new List<string>();
        var buffer = new List<string>();
        var started = false;

        void Flush()
        {
            if (!started) return;
            items.Add(ConversationFormat.TrimBlankEdges(buffer));
            buffer.Clear();
        }

        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(Bullet, StringComparison.Ordinal))
            {
                Flush();
                started = true;
                buffer.Add(line[Bullet.Length..]);
            }
            else if (started)
            {
                buffer.Add(line.StartsWith(Indent, StringComparison.Ordinal)
                    ? line[Indent.Length..]
                    : line);
            }
        }

        Flush();
        return items;
    }
}
```

- [ ] **Step 6: Implement BodySections**

`src/Fabulis.Cli/Archive/BodySections.cs`:

```csharp
using System.Text;

namespace Fabulis.Cli.Archive;

/// <summary>
/// "## Heading" delimited body sections, for multi-line text that does not
/// belong in front matter: a storyteller's system and titling prompts, and any
/// AppSetting whose value contains a newline.
/// </summary>
public static class BodySections
{
    private const string HeadingPrefix = "## ";

    public static string Write(IEnumerable<KeyValuePair<string, string>> sections)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var (heading, text) in sections)
        {
            if (!first) sb.Append('\n');
            first = false;
            sb.Append(HeadingPrefix).Append(heading).Append('\n').Append('\n');
            sb.Append(text.Replace("\r\n", "\n").TrimEnd('\n')).Append('\n');
        }
        return sb.ToString();
    }

    public static Dictionary<string, string> Read(string body)
    {
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        string? heading = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (heading is null) return;
            sections[heading] = ConversationFormat.TrimBlankEdges(buffer);
            buffer.Clear();
        }

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(HeadingPrefix, StringComparison.Ordinal))
            {
                Flush();
                heading = line[HeadingPrefix.Length..].Trim();
            }
            else if (heading is not null)
            {
                buffer.Add(line);
            }
        }

        Flush();
        return sections;
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/Fabulis.Cli.Tests`
Expected: PASS, all four format test classes green.

- [ ] **Step 8: Commit**

```bash
git add src/Fabulis.Cli/Archive tests/Fabulis.Cli.Tests/TextFormatTests.cs
git commit -m "Add conversation, block-list, item-list and section formats"
```

---

### Task 3: Archive layout and name sanitization

**Files:**
- Create: `src/Fabulis.Cli/Archive/ArchiveLayout.cs`
- Test: `tests/Fabulis.Cli.Tests/ArchiveLayoutTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `ArchiveLayout.FormatVersion` (int, `1`) plus the directory/file name constants below
  - `ArchiveLayout.Sanitize(string name) -> string`
  - `ArchiveLayout.VersionFileName(int versionNumber) -> string`
  - `ArchiveLayout.TryParseVersionFileName(string fileName, out int versionNumber) -> bool`
  - `ArchiveLayout.DraftFileName(DateTime createdUtc, string? title) -> string`
  - `Fabulis.Cli.Archive.NameAllocator` with `Allocate(string desired) -> string`

- [ ] **Step 1: Write the failing tests**

`tests/Fabulis.Cli.Tests/ArchiveLayoutTests.cs`:

```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Cli.Tests --filter ArchiveLayoutTests`
Expected: compile failure — `ArchiveLayout` does not exist.

- [ ] **Step 3: Implement ArchiveLayout**

`src/Fabulis.Cli/Archive/ArchiveLayout.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Fabulis.Cli.Archive;

/// <summary>
/// Single source of truth for the shape of an archive directory. Identity is
/// the path: a category, story, prompt or storyteller is named by its
/// directory or file, and nothing on disk carries a row id.
/// </summary>
public static partial class ArchiveLayout
{
    public const int FormatVersion = 1;

    public const string ManifestFile = "fabulis.md";
    public const string SettingsFile = "settings.md";
    public const string StorytellersDir = "storytellers";
    public const string LibraryDir = "library";
    public const string DraftsDir = "drafts";
    public const string StoriesDir = "stories";
    public const string PromptsDir = "prompts";
    public const string OneLinersFile = "one-liners.md";
    public const string TropesFile = "tropes.md";

    public const string SystemPromptSection = "System prompt";
    public const string TitlingPromptSection = "Titling prompt";

    [GeneratedRegex(@"^Version (\d+)\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionFilePattern();

    /// <summary>
    /// Makes a user- or model-supplied string safe as a single path segment.
    /// A safety net for names already in the vault; the server rejects these
    /// characters going forward.
    /// </summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastWasDash = false;

        foreach (var ch in name)
        {
            if (ch == '/' || ch == '\\' || char.IsControl(ch))
            {
                // Suppress a dash at the very start, so a leading unsafe
                // character does not turn into "-name".
                if (sb.Length > 0 && !lastWasDash)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }
            else
            {
                sb.Append(ch);
                lastWasDash = false;
            }
        }

        var result = sb.ToString().Trim().TrimEnd('.').TrimEnd('-').Trim();
        return result.Length == 0 ? "Untitled" : result;
    }

    public static string VersionFileName(int versionNumber) =>
        $"Version {versionNumber.ToString(CultureInfo.InvariantCulture)}.md";

    public static bool TryParseVersionFileName(string fileName, out int versionNumber)
    {
        versionNumber = 0;
        var match = VersionFilePattern().Match(fileName);
        return match.Success
            && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out versionNumber);
    }

    public static string DraftFileName(DateTime createdUtc, string? title)
    {
        var stamp = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc)
            .ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var safeTitle = Sanitize(string.IsNullOrWhiteSpace(title) ? "Untitled" : title);
        return $"{stamp} - {safeTitle}.md";
    }
}

/// <summary>
/// Hands out unique names within one directory, appending " (2)", " (3)" on
/// collision — the convention SillyTavernConvertService already uses.
/// </summary>
public sealed class NameAllocator
{
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string Allocate(string desired)
    {
        if (_used.Add(desired)) return desired;

        for (var n = 2; ; n++)
        {
            var candidate = $"{desired} ({n.ToString(CultureInfo.InvariantCulture)})";
            if (_used.Add(candidate)) return candidate;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fabulis.Cli.Tests --filter ArchiveLayoutTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Cli/Archive/ArchiveLayout.cs tests/Fabulis.Cli.Tests/ArchiveLayoutTests.cs
git commit -m "Add archive layout constants and name sanitization"
```

---

### Task 4: VaultExporter

**Files:**
- Create: `src/Fabulis.Cli/VaultExporter.cs`
- Create: `tests/Fabulis.Cli.Tests/VaultFixture.cs`
- Delete: `src/Fabulis.Cli/CategoryExportService.cs`, `src/Fabulis.Cli/DraftMarkdownWriter.cs`
- Modify: `src/Fabulis.Cli/Program.cs` (export call site and summary line only; flags land in Task 7)
- Modify: `src/Fabulis.Cli/SillyTavernConvertService.cs` (it calls the deleted `DraftMarkdownWriter`)
- Test: `tests/Fabulis.Cli.Tests/VaultExporterTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces:
  - `Fabulis.Cli.VaultExporter.ExportAsync(FabulisDbContext db, string destinationPath, bool includeSecrets = false) -> Task<ExportResult>`
  - `Fabulis.Cli.ExportResult` with int properties `Categories`, `Stories`, `Versions`, `Drafts`, `Prompts`, `OneLiners`, `Tropes`, `Storytellers` and bool `SettingsWritten`
  - `Fabulis.Cli.Tests.VaultFixture` with `Db`, `TempDir()`, `SeedFullVaultAsync()`, `VaultFixture.Epoch`

Deleting `DraftMarkdownWriter.cs` breaks `SillyTavernConvertService`, so this task fixes that call site too. Keep the build green at every commit.

- [ ] **Step 1: Write the shared test fixture**

`tests/Fabulis.Cli.Tests/VaultFixture.cs`:

```csharp
using Fabulis.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli.Tests;

/// <summary>
/// An in-memory vault plus scratch directories, torn down together. The seed
/// deliberately includes one of every entity kind, an empty story, a
/// story-free category, and content that exercises the escaping rules — so
/// the round-trip test in Task 6 has teeth.
/// </summary>
public sealed class VaultFixture : IDisposable
{
    public static readonly DateTime Epoch = new(2026, 4, 11, 9, 3, 12, DateTimeKind.Utc);

    private readonly SqliteConnection _connection;
    private readonly List<string> _tempDirs = [];

    public FabulisDbContext Db { get; }

    public VaultFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<FabulisDbContext>()
            .UseSqlite(_connection)
            .Options;
        Db = new FabulisDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>A path that does not exist yet, deleted on dispose.</summary>
    public string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "fabulis-test-" + Guid.NewGuid().ToString("N"));
        _tempDirs.Add(path);
        return path;
    }

    public async Task SeedFullVaultAsync()
    {
        var storyteller = new Storyteller
        {
            Name = "Storyteller",
            Prompt = "You are a helpful storyteller.\n\nBe vivid.",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "anthropic/claude-sonnet-4",
            Temperature = 0.7,
            TopK = 40,
            ReasoningEffort = Fabulis.Server.Data.ReasoningEffort.Medium,
            CreatedAt = Epoch,
        };
        Db.Storytellers.Add(storyteller);

        var category = new Category { Name = "Fables", CreatedAt = Epoch };
        Db.Categories.Add(category);

        var story = new Story { Title = "The Fox", CreatedAt = Epoch, Category = category };
        var version = new StoryVersion
        {
            VersionNumber = 1,
            ModelName = "anthropic/claude-sonnet-4",
            CreatedAt = Epoch,
            Story = story,
        };
        // Content that an unescaped format would corrupt.
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Prompt,
            SortOrder = 0,
            Content = "Write about a fox.\n**Me:**\nstill the same message",
        });
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Response,
            SortOrder = 1,
            Content = "A fox went out.\n\n---\n\nIt returned.",
        });
        story.Versions.Add(version);
        category.Stories.Add(story);

        // A story with no versions, exercising the empty-directory case.
        category.Stories.Add(new Story { Title = "Empty Story", CreatedAt = Epoch, Category = category });

        // A category with no stories at all — dropped entirely by the old export.
        var listOnly = new Category { Name = "Openers", CreatedAt = Epoch };
        Db.Categories.Add(listOnly);

        var prompt = new Prompt
        {
            Title = "Cold open",
            CreatedAt = Epoch,
            UpdatedAt = Epoch,
            Category = category,
        };
        prompt.Messages.Add(new PromptMessage { Content = "Start in motion.", SortOrder = 0 });
        prompt.Messages.Add(new PromptMessage { Content = "Second block\n---\nwith a rule.", SortOrder = 1 });
        category.Prompts.Add(prompt);

        listOnly.OneLiners.Add(new OneLiner
        {
            Text = "She set fire to the document.", CreatedAt = Epoch, UpdatedAt = Epoch,
        });
        listOnly.OneLiners.Add(new OneLiner
        {
            Text = "Multi-line opener\n- with a bullet inside", CreatedAt = Epoch, UpdatedAt = Epoch,
        });
        listOnly.Tropes.Add(new Trope { Text = "Reluctant mentor", CreatedAt = Epoch, UpdatedAt = Epoch });

        var draft = new Draft
        {
            Storyteller = storyteller,
            Title = "A Night In The Fens",
            CreatedAt = Epoch,
            UpdatedAt = Epoch.AddHours(2),
        };
        draft.Messages.Add(new DraftMessage
        {
            Role = MessageRole.Prompt, Content = "Set the scene.", SortOrder = 0,
        });
        Db.Drafts.Add(draft);

        Db.AppSettings.Add(new AppSetting { Key = "AutoLockMinutes", Value = "15" });
        Db.AppSettings.Add(new AppSetting { Key = "KokoroBaseUrl", Value = "http://localhost:8880" });
        Db.AppSettings.Add(new AppSetting { Key = "SummaryPrompt", Value = "Summarize this.\n\nBe brief." });
        Db.AppSettings.Add(new AppSetting { Key = "OpenRouterApiKey", Value = "sk-secret-value" });

        await Db.SaveChangesAsync();
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; a leaked temp dir must not fail a test run.
            }
        }
    }
}
```

- [ ] **Step 2: Write the failing exporter tests**

`tests/Fabulis.Cli.Tests/VaultExporterTests.cs`:

```csharp
using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Xunit;

namespace Fabulis.Cli.Tests;

public class VaultExporterTests : IDisposable
{
    private readonly VaultFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private async Task<string> ExportSeededAsync(bool includeSecrets = false)
    {
        await _fixture.SeedFullVaultAsync();
        var dest = _fixture.TempDir();
        await new VaultExporter().ExportAsync(_fixture.Db, dest, includeSecrets);
        return dest;
    }

    [Fact]
    public async Task RefusesAnExistingDestination()
    {
        var dest = _fixture.TempDir();
        Directory.CreateDirectory(dest);

        await Assert.ThrowsAsync<IOException>(
            () => new VaultExporter().ExportAsync(_fixture.Db, dest));
    }

    [Fact]
    public async Task WritesManifestWithFormatVersion()
    {
        var dest = await ExportSeededAsync();

        var (fields, _) = FrontMatter.Parse(
            await File.ReadAllTextAsync(Path.Combine(dest, ArchiveLayout.ManifestFile)));

        Assert.Equal("1", fields["formatVersion"]);
        Assert.NotNull(FrontMatter.ParseTimestamp(fields["exportedAt"]));
    }

    [Fact]
    public async Task WritesNamespacedTopLevelDirectories()
    {
        var dest = await ExportSeededAsync();

        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.LibraryDir)));
        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.DraftsDir)));
        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.StorytellersDir)));
    }

    [Fact]
    public async Task ExportsCategoryHoldingNoStories()
    {
        var dest = await ExportSeededAsync();

        var openers = Path.Combine(dest, ArchiveLayout.LibraryDir, "Openers");
        Assert.True(Directory.Exists(openers));
        Assert.Equal(2, ItemListFormat.Read(
            await File.ReadAllTextAsync(Path.Combine(openers, ArchiveLayout.OneLinersFile))).Count);
        Assert.Single(ItemListFormat.Read(
            await File.ReadAllTextAsync(Path.Combine(openers, ArchiveLayout.TropesFile))));
    }

    [Fact]
    public async Task WritesEmptyListFilesRatherThanOmittingThem()
    {
        var dest = await ExportSeededAsync();

        var fables = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables");
        Assert.True(File.Exists(Path.Combine(fables, ArchiveLayout.OneLinersFile)));
        Assert.Empty(ItemListFormat.Read(
            await File.ReadAllTextAsync(Path.Combine(fables, ArchiveLayout.OneLinersFile))));
    }

    [Fact]
    public async Task WritesStoryVersionWithModelInFrontMatter()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", "Version 1.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal("anthropic/claude-sonnet-4", fields["model"]);
        Assert.Equal(VaultFixture.Epoch, FrontMatter.ParseTimestamp(fields["created"]));
        Assert.Equal(2, ConversationFormat.Read(body).Count);
    }

    [Fact]
    public async Task WritesStoryDirectoryEvenWithNoVersions()
    {
        var dest = await ExportSeededAsync();

        var dir = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "Empty Story");

        Assert.True(Directory.Exists(dir));
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public async Task WritesStorytellerTuningAndPrompts()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.StorytellersDir, "Storyteller.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));
        var sections = BodySections.Read(body);

        Assert.Equal("anthropic/claude-sonnet-4", fields["model"]);
        Assert.Equal("0.7", fields["temperature"]);
        Assert.Equal("40", fields["topK"]);
        Assert.Equal("Medium", fields["reasoningEffort"]);
        Assert.False(fields.ContainsKey("topP"));
        Assert.False(fields.ContainsKey("maxTokens"));
        Assert.StartsWith("You are a helpful storyteller.", sections[ArchiveLayout.SystemPromptSection]);
        Assert.Equal(Storyteller.DefaultTitlingPrompt, sections[ArchiveLayout.TitlingPromptSection]);
    }

    [Fact]
    public async Task WritesPromptMessagesAsBlocks()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.PromptsDir, "Cold open.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal(VaultFixture.Epoch, FrontMatter.ParseTimestamp(fields["created"]));
        Assert.Equal(2, BlockListFormat.Read(body).Count);
    }

    [Fact]
    public async Task WritesDraftWithAuthoritativeTitleInFrontMatter()
    {
        var dest = await ExportSeededAsync();

        var path = Path.Combine(dest, ArchiveLayout.DraftsDir,
            "20260411T090312Z - A Night In The Fens.md");
        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal("Storyteller", fields["storyteller"]);
        Assert.Equal("A Night In The Fens", fields["title"]);
        Assert.False(fields.ContainsKey("model"));
        Assert.Single(ConversationFormat.Read(body));
    }

    [Fact]
    public async Task RedactsApiKeyByDefault()
    {
        var dest = await ExportSeededAsync();

        var text = await File.ReadAllTextAsync(Path.Combine(dest, ArchiveLayout.SettingsFile));
        var (fields, body) = FrontMatter.Parse(text);

        Assert.DoesNotContain("sk-secret-value", text);
        Assert.False(fields.ContainsKey("OpenRouterApiKey"));
        Assert.Equal("15", fields["AutoLockMinutes"]);
        Assert.Equal("Summarize this.\n\nBe brief.", BodySections.Read(body)["SummaryPrompt"]);
    }

    [Fact]
    public async Task WritesApiKeyWhenSecretsRequested()
    {
        var dest = await ExportSeededAsync(includeSecrets: true);

        var (fields, _) = FrontMatter.Parse(
            await File.ReadAllTextAsync(Path.Combine(dest, ArchiveLayout.SettingsFile)));

        Assert.Equal("sk-secret-value", fields["OpenRouterApiKey"]);
    }

    [Fact]
    public async Task SanitizesUnsafeNamesAndCountsEverything()
    {
        await _fixture.SeedFullVaultAsync();
        _fixture.Db.Categories.Add(new Category { Name = "Slash/Name", CreatedAt = VaultFixture.Epoch });
        await _fixture.Db.SaveChangesAsync();
        var dest = _fixture.TempDir();

        var result = await new VaultExporter().ExportAsync(_fixture.Db, dest);

        Assert.True(Directory.Exists(Path.Combine(dest, ArchiveLayout.LibraryDir, "Slash-Name")));
        Assert.Equal(3, result.Categories);
        Assert.Equal(2, result.Stories);
        Assert.Equal(1, result.Versions);
        Assert.Equal(1, result.Drafts);
        Assert.Equal(1, result.Prompts);
        Assert.Equal(2, result.OneLiners);
        Assert.Equal(1, result.Tropes);
        Assert.Equal(1, result.Storytellers);
        Assert.True(result.SettingsWritten);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Cli.Tests --filter VaultExporterTests`
Expected: compile failure — `VaultExporter` does not exist.

- [ ] **Step 4: Implement VaultExporter**

`src/Fabulis.Cli/VaultExporter.cs`:

```csharp
using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli;

/// <summary>
/// Writes the whole vault to a directory tree. Traversal only — every
/// question about file shape lives under <c>Archive/</c>.
/// </summary>
public sealed class VaultExporter
{
    private const string ApiKeySetting = "OpenRouterApiKey";

    public async Task<ExportResult> ExportAsync(
        FabulisDbContext db, string destinationPath, bool includeSecrets = false)
    {
        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            throw new IOException($"Destination already exists: {destinationPath}");

        var result = new ExportResult();
        Directory.CreateDirectory(destinationPath);

        await WriteManifestAsync(destinationPath);
        await WriteSettingsAsync(db, destinationPath, includeSecrets, result);
        await WriteStorytellersAsync(db, destinationPath, result);
        await WriteLibraryAsync(db, destinationPath, result);
        await WriteDraftsAsync(db, destinationPath, result);

        return result;
    }

    private static async Task WriteManifestAsync(string root)
    {
        var body =
            "A Fabulis vault archive.\n\n" +
            $"- `{ArchiveLayout.LibraryDir}/` one directory per category, each holding " +
            $"`{ArchiveLayout.StoriesDir}/`, `{ArchiveLayout.PromptsDir}/`, " +
            $"`{ArchiveLayout.OneLinersFile}` and `{ArchiveLayout.TropesFile}`\n" +
            $"- `{ArchiveLayout.DraftsDir}/` unsaved drafts\n" +
            $"- `{ArchiveLayout.StorytellersDir}/` storyteller prompts and tuning\n" +
            $"- `{ArchiveLayout.SettingsFile}` app settings\n";

        var text = FrontMatter.Serialize(
            [
                new("formatVersion", ArchiveLayout.FormatVersion.ToString()),
                new("exportedAt", FrontMatter.FormatTimestamp(DateTime.UtcNow)),
            ],
            body);

        await File.WriteAllTextAsync(Path.Combine(root, ArchiveLayout.ManifestFile), text);
    }

    private static async Task WriteSettingsAsync(
        FabulisDbContext db, string root, bool includeSecrets, ExportResult result)
    {
        var settings = await db.AppSettings.OrderBy(s => s.Key).ToListAsync();

        var scalars = new List<KeyValuePair<string, string?>>();
        var sections = new List<KeyValuePair<string, string>>();

        foreach (var setting in settings)
        {
            if (setting.Key == ApiKeySetting && !includeSecrets)
                continue;

            // The mapping is generic rather than a hardcoded field list, so a
            // new AppSetting is covered without touching this code. Multi-line
            // values (SummaryPrompt today) go to a body section.
            if (setting.Value.Contains('\n'))
                sections.Add(new(setting.Key, setting.Value));
            else
                scalars.Add(new(setting.Key, setting.Value));
        }

        var text = FrontMatter.Serialize(scalars, BodySections.Write(sections));
        await File.WriteAllTextAsync(Path.Combine(root, ArchiveLayout.SettingsFile), text);
        result.SettingsWritten = true;
    }

    private static async Task WriteStorytellersAsync(
        FabulisDbContext db, string root, ExportResult result)
    {
        var dir = Path.Combine(root, ArchiveLayout.StorytellersDir);
        Directory.CreateDirectory(dir);

        var names = new NameAllocator();
        foreach (var s in await db.Storytellers.OrderBy(s => s.Id).ToListAsync())
        {
            var text = FrontMatter.Serialize(
                [
                    new("model", s.ModelName),
                    new("temperature", FrontMatter.FormatDouble(s.Temperature)),
                    new("topP", s.TopP is null ? null : FrontMatter.FormatDouble(s.TopP.Value)),
                    new("maxTokens", s.MaxTokens?.ToString()),
                    new("minP", s.MinP is null ? null : FrontMatter.FormatDouble(s.MinP.Value)),
                    new("topK", s.TopK?.ToString()),
                    new("topA", s.TopA is null ? null : FrontMatter.FormatDouble(s.TopA.Value)),
                    new("reasoningEffort", s.ReasoningEffort?.ToString()),
                    new("created", FrontMatter.FormatTimestamp(s.CreatedAt)),
                ],
                BodySections.Write(
                [
                    new(ArchiveLayout.SystemPromptSection, s.Prompt),
                    new(ArchiveLayout.TitlingPromptSection, s.TitlingPrompt),
                ]));

            var stem = names.Allocate(SafeName(s.Name));
            await File.WriteAllTextAsync(Path.Combine(dir, stem + ".md"), text);
            result.Storytellers++;
        }
    }

    private static async Task WriteLibraryAsync(
        FabulisDbContext db, string root, ExportResult result)
    {
        var libraryDir = Path.Combine(root, ArchiveLayout.LibraryDir);
        Directory.CreateDirectory(libraryDir);

        var categories = await db.Categories
            .Include(c => c.Stories).ThenInclude(s => s.Versions).ThenInclude(v => v.Messages)
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .Include(c => c.OneLiners)
            .Include(c => c.Tropes)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var categoryNames = new NameAllocator();

        foreach (var category in categories)
        {
            var categoryDir = Path.Combine(libraryDir, categoryNames.Allocate(SafeName(category.Name)));
            Directory.CreateDirectory(categoryDir);
            result.Categories++;

            await WriteStoriesAsync(categoryDir, category, result);
            await WritePromptsAsync(categoryDir, category, result);

            // Written even when empty, so an empty list and a missing file
            // stay distinguishable on import.
            await File.WriteAllTextAsync(
                Path.Combine(categoryDir, ArchiveLayout.OneLinersFile),
                ItemListFormat.Write(category.OneLiners.OrderBy(o => o.Id).Select(o => o.Text)));
            result.OneLiners += category.OneLiners.Count;

            await File.WriteAllTextAsync(
                Path.Combine(categoryDir, ArchiveLayout.TropesFile),
                ItemListFormat.Write(category.Tropes.OrderBy(t => t.Id).Select(t => t.Text)));
            result.Tropes += category.Tropes.Count;
        }
    }

    private static async Task WriteStoriesAsync(
        string categoryDir, Category category, ExportResult result)
    {
        var storiesDir = Path.Combine(categoryDir, ArchiveLayout.StoriesDir);
        Directory.CreateDirectory(storiesDir);

        var storyNames = new NameAllocator();
        foreach (var story in category.Stories.OrderBy(s => s.Title, StringComparer.Ordinal))
        {
            var storyDir = Path.Combine(storiesDir, storyNames.Allocate(SafeName(story.Title)));
            Directory.CreateDirectory(storyDir);
            result.Stories++;

            foreach (var version in story.Versions.OrderBy(v => v.VersionNumber))
            {
                var text = FrontMatter.Serialize(
                    [
                        new("model", version.ModelName),
                        new("created", FrontMatter.FormatTimestamp(version.CreatedAt)),
                    ],
                    ConversationFormat.Write(version.Messages.Select(
                        m => new ConversationFormat.Turn(m.Role, m.Content, m.SortOrder))));

                await File.WriteAllTextAsync(
                    Path.Combine(storyDir, ArchiveLayout.VersionFileName(version.VersionNumber)),
                    text);
                result.Versions++;
            }
        }
    }

    private static async Task WritePromptsAsync(
        string categoryDir, Category category, ExportResult result)
    {
        var promptsDir = Path.Combine(categoryDir, ArchiveLayout.PromptsDir);
        Directory.CreateDirectory(promptsDir);

        var promptNames = new NameAllocator();
        foreach (var prompt in category.Prompts.OrderBy(p => p.Title, StringComparer.Ordinal))
        {
            var text = FrontMatter.Serialize(
                [
                    new("created", FrontMatter.FormatTimestamp(prompt.CreatedAt)),
                    new("updated", FrontMatter.FormatTimestamp(prompt.UpdatedAt)),
                ],
                BlockListFormat.Write(
                    prompt.Messages.OrderBy(m => m.SortOrder).Select(m => m.Content)));

            var stem = promptNames.Allocate(SafeName(prompt.Title));
            await File.WriteAllTextAsync(Path.Combine(promptsDir, stem + ".md"), text);
            result.Prompts++;
        }
    }

    private static async Task WriteDraftsAsync(
        FabulisDbContext db, string root, ExportResult result)
    {
        var draftsDir = Path.Combine(root, ArchiveLayout.DraftsDir);
        Directory.CreateDirectory(draftsDir);

        var drafts = await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages)
            .OrderBy(d => d.Id)
            .ToListAsync();

        var fileNames = new NameAllocator();
        foreach (var draft in drafts)
        {
            var text = FrontMatter.Serialize(
                [
                    new("storyteller", draft.Storyteller?.Name ?? "(unknown)"),
                    new("title", draft.Title),
                    new("created", FrontMatter.FormatTimestamp(draft.CreatedAt)),
                    new("updated", FrontMatter.FormatTimestamp(draft.UpdatedAt)),
                ],
                ConversationFormat.Write(draft.Messages.Select(
                    m => new ConversationFormat.Turn(m.Role, m.Content, m.SortOrder))));

            var desired = ArchiveLayout.DraftFileName(draft.CreatedAt, draft.Title);
            var stem = fileNames.Allocate(Path.GetFileNameWithoutExtension(desired));
            await File.WriteAllTextAsync(Path.Combine(draftsDir, stem + ".md"), text);
            result.Drafts++;
        }
    }

    /// <summary>Sanitizes a path segment, warning when the name had to change.</summary>
    private static string SafeName(string name)
    {
        var safe = ArchiveLayout.Sanitize(name);
        if (!string.Equals(safe, name, StringComparison.Ordinal))
            Console.Error.WriteLine($"warn: wrote '{name}' to disk as '{safe}'");
        return safe;
    }
}

public class ExportResult
{
    public int Categories { get; set; }
    public int Stories { get; set; }
    public int Versions { get; set; }
    public int Drafts { get; set; }
    public int Prompts { get; set; }
    public int OneLiners { get; set; }
    public int Tropes { get; set; }
    public int Storytellers { get; set; }
    public bool SettingsWritten { get; set; }
}
```

- [ ] **Step 5: Delete the old exporter and repair the call sites**

```bash
git rm src/Fabulis.Cli/CategoryExportService.cs src/Fabulis.Cli/DraftMarkdownWriter.cs
```

In `Program.cs`, replace the export branch body with:

```csharp
var result = await new VaultExporter().ExportAsync(db, path);
Console.WriteLine(
    $"Exported: {result.Categories} categories, {result.Stories} stories, " +
    $"{result.Versions} versions, {result.Prompts} prompts, " +
    $"{result.OneLiners} one-liners, {result.Tropes} tropes, " +
    $"{result.Drafts} drafts, {result.Storytellers} storytellers");
return 0;
```

In `SillyTavernConvertService.cs`, add `using Fabulis.Cli.Archive;` and replace the
`DraftMarkdownWriter.FormatDraft(...)` call with the new draft shape. The
converter's `Model:` header is dropped, matching the format:

```csharp
var content = FrontMatter.Serialize(
    [
        new("storyteller", storytellerName),
        new("title", title),
        new("created", FrontMatter.FormatTimestamp(createdUtc)),
        new("updated", FrontMatter.FormatTimestamp(updatedUtc)),
    ],
    ConversationFormat.Write(messages.Select(
        m => new ConversationFormat.Turn(m.Role, m.Content, m.SortOrder))));
```

Also change the destination subdirectory it writes into from `_drafts` to
`ArchiveLayout.DraftsDir`, and update the `sillytavern` usage string in
`Program.cs`, which currently says `<destination>/_drafts/`.

- [ ] **Step 6: Run the build and the full CLI test suite**

Run: `dotnet build Fabulis.slnx && dotnet test tests/Fabulis.Cli.Tests`
Expected: build succeeds with no reference to `DraftMarkdownWriter`; all exporter tests PASS.

- [ ] **Step 7: Commit**

```bash
git add -A src/Fabulis.Cli tests/Fabulis.Cli.Tests
git commit -m "Replace CategoryExportService with full-vault VaultExporter"
```

---

### Task 5: VaultImporter (merge mode)

**Files:**
- Create: `src/Fabulis.Cli/VaultImporter.cs`
- Delete: `src/Fabulis.Cli/CategoryImportService.cs`
- Modify: `src/Fabulis.Cli/Program.cs` (import call site and summary line)
- Test: `tests/Fabulis.Cli.Tests/VaultImporterTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4, including `VaultFixture` and `VaultExporter`.
- Produces:
  - `Fabulis.Cli.VaultImporter.ImportAsync(FabulisDbContext db, string rootPath, ImportOptions? options = null) -> Task<ImportResult>`
  - `Fabulis.Cli.ImportOptions(bool Mirror = false, Func<MirrorPlan, bool>? ConfirmDeletions = null)` record — `Mirror` is accepted but ignored until Task 7
  - `Fabulis.Cli.ImportResult` with int properties `CategoriesCreated`, `StoriesCreated`, `VersionsCreated`, `PromptsCreated`, `OneLinersCreated`, `TropesCreated`, `DraftsCreated`, `StorytellersCreated`, `SettingsApplied`, plus `MirrorPlan? Deleted` and `bool Cancelled`
  - `Fabulis.Cli.MirrorPlan` record (defined here, populated in Task 7)

Merge semantics: create what is missing, skip what exists, never delete. Matching is by path identity — category name, story title plus version number, prompt title, storyteller name — and drafts dedupe on `(StorytellerId, Title, CreatedAt)` as they do today. `Story.CreatedAt` is derived as the earliest version `created`; `Category.CreatedAt` as the earliest of its stories' and prompts' timestamps. Both fall back to `DateTime.UtcNow` when there is nothing to derive from.

- [ ] **Step 1: Write the failing tests**

`tests/Fabulis.Cli.Tests/VaultImporterTests.cs`:

```csharp
using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Cli.Tests;

public class VaultImporterTests : IDisposable
{
    private readonly VaultFixture _source = new();
    private readonly VaultFixture _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    /// <summary>Seeds and exports the source vault, returning the archive path.</summary>
    private async Task<string> ArchiveAsync()
    {
        await _source.SeedFullVaultAsync();
        var dest = _source.TempDir();
        await new VaultExporter().ExportAsync(_source.Db, dest);
        return dest;
    }

    [Fact]
    public async Task RefusesAMissingDirectory()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new VaultImporter().ImportAsync(_target.Db, _target.TempDir()));
    }

    [Fact]
    public async Task RefusesAnUnknownFormatVersion()
    {
        var archive = await ArchiveAsync();
        var manifest = Path.Combine(archive, ArchiveLayout.ManifestFile);
        await File.WriteAllTextAsync(manifest,
            (await File.ReadAllTextAsync(manifest)).Replace("formatVersion: 1", "formatVersion: 99"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive));

        Assert.Contains("formatVersion", ex.Message);
    }

    [Fact]
    public async Task RefusesADirectoryWithNoManifest()
    {
        var archive = await ArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.ManifestFile));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive));
    }

    [Fact]
    public async Task ImportsEveryKind()
    {
        var archive = await ArchiveAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(2, result.CategoriesCreated);
        Assert.Equal(2, result.StoriesCreated);
        Assert.Equal(1, result.VersionsCreated);
        Assert.Equal(1, result.PromptsCreated);
        Assert.Equal(2, result.OneLinersCreated);
        Assert.Equal(1, result.TropesCreated);
        Assert.Equal(1, result.DraftsCreated);
        // EnsureCreated() builds the schema but does not run
        // SeedDefaultStorytellerIfMissingAsync, so the target vault starts with
        // no storytellers and the archived one is created.
        Assert.Equal(1, result.StorytellersCreated);
        // AutoLockMinutes, KokoroBaseUrl, SummaryPrompt -- the API key is redacted.
        Assert.Equal(3, result.SettingsApplied);
    }

    [Fact]
    public async Task ImportsCategoryHoldingOnlyLists()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var openers = await _target.Db.Categories
            .Include(c => c.OneLiners).Include(c => c.Tropes).Include(c => c.Stories)
            .SingleAsync(c => c.Name == "Openers");

        Assert.Equal(2, openers.OneLiners.Count);
        Assert.Single(openers.Tropes);
        Assert.Empty(openers.Stories);
    }

    [Fact]
    public async Task PreservesEscapedContentThroughImport()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var version = await _target.Db.StoryVersions
            .Include(v => v.Messages)
            .SingleAsync();
        var messages = version.Messages.OrderBy(m => m.SortOrder).ToList();

        Assert.Equal("Write about a fox.\n**Me:**\nstill the same message", messages[0].Content);
        Assert.Equal("A fox went out.\n\n---\n\nIt returned.", messages[1].Content);
    }

    [Fact]
    public async Task DerivesStoryAndCategoryTimestampsFromChildren()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var story = await _target.Db.Stories.SingleAsync(s => s.Title == "The Fox");
        var category = await _target.Db.Categories.SingleAsync(c => c.Name == "Fables");

        Assert.Equal(VaultFixture.Epoch, story.CreatedAt);
        Assert.Equal(VaultFixture.Epoch, category.CreatedAt);
    }

    [Fact]
    public async Task ImportsStorytellerTuningWhenNameIsNew()
    {
        var archive = await ArchiveAsync();
        var storytellerFile = Path.Combine(archive, ArchiveLayout.StorytellersDir, "Storyteller.md");
        File.Move(storytellerFile,
            Path.Combine(archive, ArchiveLayout.StorytellersDir, "Second Voice.md"));

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.Storytellers.SingleAsync(s => s.Name == "Second Voice");
        Assert.Equal(1, result.StorytellersCreated);
        Assert.Equal("anthropic/claude-sonnet-4", imported.ModelName);
        Assert.Equal(0.7, imported.Temperature);
        Assert.Equal(40, imported.TopK);
        Assert.Equal(ReasoningEffort.Medium, imported.ReasoningEffort);
        Assert.Null(imported.TopP);
        Assert.StartsWith("You are a helpful storyteller.", imported.Prompt);
        Assert.Equal(Storyteller.DefaultTitlingPrompt, imported.TitlingPrompt);
    }

    [Fact]
    public async Task SkipsDraftWhoseStorytellerIsUnknown()
    {
        var archive = await ArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir), recursive: true);
        _target.Db.Storytellers.RemoveRange(_target.Db.Storytellers);
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(0, result.DraftsCreated);
    }

    [Fact]
    public async Task LeavesExistingApiKeyIntactWhenArchiveRedactsIt()
    {
        var archive = await ArchiveAsync();
        _target.Db.AppSettings.Add(new AppSetting { Key = "OpenRouterApiKey", Value = "sk-existing" });
        await _target.Db.SaveChangesAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var key = await _target.Db.AppSettings.FindAsync("OpenRouterApiKey");
        Assert.Equal("sk-existing", key!.Value);
    }

    [Fact]
    public async Task AppliesMultiLineSettingFromBodySection()
    {
        var archive = await ArchiveAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var prompt = await _target.Db.AppSettings.FindAsync("SummaryPrompt");
        Assert.Equal("Summarize this.\n\nBe brief.", prompt!.Value);
    }

    [Fact]
    public async Task IsIdempotent()
    {
        var archive = await ArchiveAsync();
        var importer = new VaultImporter();

        await importer.ImportAsync(_target.Db, archive);
        var second = await importer.ImportAsync(_target.Db, archive);

        Assert.Equal(0, second.CategoriesCreated);
        Assert.Equal(0, second.StoriesCreated);
        Assert.Equal(0, second.VersionsCreated);
        Assert.Equal(0, second.PromptsCreated);
        Assert.Equal(0, second.OneLinersCreated);
        Assert.Equal(0, second.TropesCreated);
        Assert.Equal(0, second.DraftsCreated);
        Assert.Equal(2, await _target.Db.Categories.CountAsync());
        Assert.Equal(1, await _target.Db.StoryVersions.CountAsync());
    }

    [Fact]
    public async Task MergeNeverDeletes()
    {
        var archive = await ArchiveAsync();
        _target.Db.Categories.Add(new Category { Name = "Untouched", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Untouched"));
    }

    [Fact]
    public async Task MissingListFileIsANoOpUnderMerge()
    {
        var archive = await ArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Openers",
            ArchiveLayout.OneLinersFile));

        var result = await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(0, result.OneLinersCreated);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Cli.Tests --filter VaultImporterTests`
Expected: compile failure — `VaultImporter` does not exist.

- [ ] **Step 3: Implement VaultImporter**

`src/Fabulis.Cli/VaultImporter.cs`:

```csharp
using System.Globalization;
using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli;

public sealed record ImportOptions(bool Mirror = false, Func<MirrorPlan, bool>? ConfirmDeletions = null);

/// <summary>
/// What a mirror import would remove. Empty until Task 7 populates it.
/// One-liners and tropes are replaced wholesale per category rather than
/// itemized, so they do not appear here.
/// </summary>
public sealed record MirrorPlan(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Stories,
    IReadOnlyList<string> Versions,
    IReadOnlyList<string> Prompts,
    IReadOnlyList<string> Drafts,
    IReadOnlyList<string> Storytellers)
{
    public static readonly MirrorPlan Empty = new([], [], [], [], [], []);

    public int Count =>
        Categories.Count + Stories.Count + Versions.Count +
        Prompts.Count + Drafts.Count + Storytellers.Count;

    public bool IsEmpty => Count == 0;
}

/// <summary>
/// Reads a directory tree back into the vault. Traversal only; file shapes
/// live under <c>Archive/</c>. Identity is the path, so a renamed directory
/// reads as a different category, story or prompt.
/// </summary>
public sealed class VaultImporter
{
    private const string ApiKeySetting = "OpenRouterApiKey";

    public async Task<ImportResult> ImportAsync(
        FabulisDbContext db, string rootPath, ImportOptions? options = null)
    {
        options ??= new ImportOptions();

        var root = new DirectoryInfo(rootPath);
        if (!root.Exists)
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

        await VerifyManifestAsync(root);

        var result = new ImportResult();

        await ImportSettingsAsync(db, root, result);
        await ImportStorytellersAsync(db, root, result);
        await ImportLibraryAsync(db, root, result);
        await ImportDraftsAsync(db, root, result);

        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>
    /// Reads the manifest first. An unrecognized formatVersion is a hard error
    /// rather than a best-effort parse — that is what makes dropping legacy
    /// support safe going forward.
    /// </summary>
    private static async Task VerifyManifestAsync(DirectoryInfo root)
    {
        var manifestPath = Path.Combine(root.FullName, ArchiveLayout.ManifestFile);
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException(
                $"'{root.FullName}' is not a Fabulis archive: no {ArchiveLayout.ManifestFile} found.");

        var (fields, _) = FrontMatter.Parse(await File.ReadAllTextAsync(manifestPath));

        if (!fields.TryGetValue("formatVersion", out var raw) ||
            !int.TryParse(raw, CultureInfo.InvariantCulture, out var version))
            throw new InvalidOperationException(
                $"{ArchiveLayout.ManifestFile} has no readable formatVersion.");

        if (version != ArchiveLayout.FormatVersion)
            throw new InvalidOperationException(
                $"Archive formatVersion {version} is not supported by this build " +
                $"(expected {ArchiveLayout.FormatVersion}).");
    }

    private static async Task ImportSettingsAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result)
    {
        var path = Path.Combine(root.FullName, ArchiveLayout.SettingsFile);
        if (!File.Exists(path)) return;

        var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(path));

        var values = new Dictionary<string, string>(fields, StringComparer.Ordinal);
        foreach (var (key, text) in BodySections.Read(body))
            values[key] = text;

        foreach (var (key, value) in values)
        {
            // A redacted archive must never clear a working key.
            if (key == ApiKeySetting && string.IsNullOrEmpty(value)) continue;

            var existing = await db.AppSettings.FindAsync(key);
            if (existing is null)
                db.AppSettings.Add(new AppSetting { Key = key, Value = value });
            else
                existing.Value = value;

            result.SettingsApplied++;
        }
    }

    private static async Task ImportStorytellersAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result)
    {
        var dir = new DirectoryInfo(Path.Combine(root.FullName, ArchiveLayout.StorytellersDir));
        if (!dir.Exists) return;

        var byName = await db.Storytellers.ToDictionaryAsync(s => s.Name, s => s, StringComparer.Ordinal);

        foreach (var file in dir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file.Name);
            if (byName.ContainsKey(name)) continue;

            var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(file.FullName));
            var sections = BodySections.Read(body);

            var storyteller = new Storyteller
            {
                Name = name,
                ModelName = Scalar(fields, "model") ?? "",
                Prompt = sections.GetValueOrDefault(ArchiveLayout.SystemPromptSection, ""),
                TitlingPrompt = sections.GetValueOrDefault(
                    ArchiveLayout.TitlingPromptSection, Storyteller.DefaultTitlingPrompt),
                Temperature = Double(fields, "temperature") ?? 0.7,
                TopP = Double(fields, "topP"),
                MaxTokens = Int(fields, "maxTokens"),
                MinP = Double(fields, "minP"),
                TopK = Int(fields, "topK"),
                TopA = Double(fields, "topA"),
                ReasoningEffort = Enum.TryParse<ReasoningEffort>(
                    Scalar(fields, "reasoningEffort"), out var effort) ? effort : null,
                CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow,
            };

            db.Storytellers.Add(storyteller);
            byName[name] = storyteller;
            result.StorytellersCreated++;
        }

        await db.SaveChangesAsync();
    }

    private static async Task ImportLibraryAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result)
    {
        var libraryDir = new DirectoryInfo(Path.Combine(root.FullName, ArchiveLayout.LibraryDir));
        if (!libraryDir.Exists) return;

        foreach (var categoryDir in libraryDir.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            var category = await db.Categories
                .Include(c => c.Stories).ThenInclude(s => s.Versions)
                .Include(c => c.Prompts)
                .Include(c => c.OneLiners)
                .Include(c => c.Tropes)
                .FirstOrDefaultAsync(c => c.Name == categoryDir.Name);

            var isNew = category is null;
            if (category is null)
            {
                category = new Category { Name = categoryDir.Name, CreatedAt = DateTime.UtcNow };
                db.Categories.Add(category);
                result.CategoriesCreated++;
            }

            var childTimestamps = new List<DateTime>();

            await ImportStoriesAsync(categoryDir, category, result, childTimestamps);
            await ImportPromptsAsync(categoryDir, category, result, childTimestamps);
            ImportOneLiners(categoryDir, category, result);
            ImportTropes(categoryDir, category, result);

            // Category.CreatedAt is derived, not stored on disk.
            if (isNew && childTimestamps.Count > 0)
                category.CreatedAt = childTimestamps.Min();
        }
    }

    private static async Task ImportStoriesAsync(
        DirectoryInfo categoryDir, Category category, ImportResult result, List<DateTime> childTimestamps)
    {
        var storiesDir = new DirectoryInfo(Path.Combine(categoryDir.FullName, ArchiveLayout.StoriesDir));
        if (!storiesDir.Exists) return;

        foreach (var storyDir in storiesDir.GetDirectories().OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            var story = category.Stories.FirstOrDefault(s => s.Title == storyDir.Name);
            var isNew = story is null;
            if (story is null)
            {
                story = new Story { Title = storyDir.Name, CreatedAt = DateTime.UtcNow, Category = category };
                category.Stories.Add(story);
                result.StoriesCreated++;
            }

            var versionTimestamps = new List<DateTime>();

            foreach (var file in storyDir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (!ArchiveLayout.TryParseVersionFileName(file.Name, out var versionNumber))
                {
                    Console.Error.WriteLine($"warn: skipping unrecognized file: {file.FullName}");
                    continue;
                }

                var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(file.FullName));
                var created = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow;
                versionTimestamps.Add(created);

                if (story.Versions.Any(v => v.VersionNumber == versionNumber)) continue;

                var version = new StoryVersion
                {
                    VersionNumber = versionNumber,
                    ModelName = Scalar(fields, "model") ?? "(unknown)",
                    CreatedAt = created,
                    Story = story,
                };
                version.Messages.AddRange(ConversationFormat.Read(body).Select(t => new StoryMessage
                {
                    Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
                }));

                story.Versions.Add(version);
                result.VersionsCreated++;
            }

            // Story.CreatedAt is derived from its earliest version.
            if (isNew && versionTimestamps.Count > 0)
                story.CreatedAt = versionTimestamps.Min();

            childTimestamps.Add(story.CreatedAt);
        }
    }

    private static async Task ImportPromptsAsync(
        DirectoryInfo categoryDir, Category category, ImportResult result, List<DateTime> childTimestamps)
    {
        var promptsDir = new DirectoryInfo(Path.Combine(categoryDir.FullName, ArchiveLayout.PromptsDir));
        if (!promptsDir.Exists) return;

        foreach (var file in promptsDir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var title = Path.GetFileNameWithoutExtension(file.Name);
            var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(file.FullName));
            var created = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow;
            childTimestamps.Add(created);

            if (category.Prompts.Any(p => p.Title == title)) continue;

            var prompt = new Prompt
            {
                Title = title,
                CreatedAt = created,
                UpdatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? created,
                Category = category,
            };

            var sortOrder = 0;
            foreach (var block in BlockListFormat.Read(body))
                prompt.Messages.Add(new PromptMessage { Content = block, SortOrder = sortOrder++ });

            category.Prompts.Add(prompt);
            result.PromptsCreated++;
        }
    }

    private static void ImportOneLiners(DirectoryInfo categoryDir, Category category, ImportResult result)
    {
        var path = Path.Combine(categoryDir.FullName, ArchiveLayout.OneLinersFile);
        if (!File.Exists(path)) return;

        var existing = category.OneLiners.Select(o => o.Text).ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        foreach (var text in ItemListFormat.Read(File.ReadAllText(path)))
        {
            if (!existing.Add(text)) continue;
            category.OneLiners.Add(new OneLiner { Text = text, CreatedAt = now, UpdatedAt = now });
            result.OneLinersCreated++;
        }
    }

    private static void ImportTropes(DirectoryInfo categoryDir, Category category, ImportResult result)
    {
        var path = Path.Combine(categoryDir.FullName, ArchiveLayout.TropesFile);
        if (!File.Exists(path)) return;

        var existing = category.Tropes.Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        foreach (var text in ItemListFormat.Read(File.ReadAllText(path)))
        {
            if (!existing.Add(text)) continue;
            category.Tropes.Add(new Trope { Text = text, CreatedAt = now, UpdatedAt = now });
            result.TropesCreated++;
        }
    }

    private static async Task ImportDraftsAsync(
        FabulisDbContext db, DirectoryInfo root, ImportResult result)
    {
        var draftsDir = new DirectoryInfo(Path.Combine(root.FullName, ArchiveLayout.DraftsDir));
        if (!draftsDir.Exists) return;

        var storytellers = await db.Storytellers.ToDictionaryAsync(
            s => s.Name, s => s, StringComparer.Ordinal);

        foreach (var file in draftsDir.GetFiles("*.md").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var (fields, body) = FrontMatter.Parse(await File.ReadAllTextAsync(file.FullName));

            var storytellerName = Scalar(fields, "storyteller");
            if (string.IsNullOrWhiteSpace(storytellerName) ||
                !storytellers.TryGetValue(storytellerName, out var storyteller))
            {
                Console.Error.WriteLine(
                    $"warn: draft references unknown storyteller '{storytellerName}', skipping: {file.FullName}");
                continue;
            }

            // The filename is derived and sanitized; front matter is authoritative.
            var title = Scalar(fields, "title");
            var createdAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? DateTime.UtcNow;
            var updatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? createdAt;

            var exists = await db.Drafts.AnyAsync(d =>
                d.StorytellerID == storyteller.Id && d.Title == title && d.CreatedAt == createdAt);
            if (exists) continue;

            var draft = new Draft
            {
                StorytellerID = storyteller.Id,
                Storyteller = storyteller,
                Title = title,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
            };
            draft.Messages.AddRange(ConversationFormat.Read(body).Select(t => new DraftMessage
            {
                Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
            }));

            db.Drafts.Add(draft);
            result.DraftsCreated++;
        }
    }

    private static string? Scalar(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    private static double? Double(Dictionary<string, string> fields, string key) =>
        double.TryParse(Scalar(fields, key), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Int(Dictionary<string, string> fields, string key) =>
        int.TryParse(Scalar(fields, key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : null;
}

public class ImportResult
{
    public int CategoriesCreated { get; set; }
    public int StoriesCreated { get; set; }
    public int VersionsCreated { get; set; }
    public int PromptsCreated { get; set; }
    public int OneLinersCreated { get; set; }
    public int TropesCreated { get; set; }
    public int DraftsCreated { get; set; }
    public int StorytellersCreated { get; set; }
    public int SettingsApplied { get; set; }
    public MirrorPlan? Deleted { get; set; }
    public bool Cancelled { get; set; }
}
```

- [ ] **Step 4: Delete the old importer and fix the call site**

```bash
git rm src/Fabulis.Cli/CategoryImportService.cs
```

In `Program.cs`, replace the import branch body with:

```csharp
var result = await new VaultImporter().ImportAsync(db, path);
Console.WriteLine(
    $"Imported: {result.CategoriesCreated} categories, {result.StoriesCreated} stories, " +
    $"{result.VersionsCreated} versions, {result.PromptsCreated} prompts, " +
    $"{result.OneLinersCreated} one-liners, {result.TropesCreated} tropes, " +
    $"{result.DraftsCreated} drafts, {result.StorytellersCreated} storytellers, " +
    $"{result.SettingsApplied} settings");
return 0;
```

- [ ] **Step 5: Run the build and the full CLI test suite**

Run: `dotnet build Fabulis.slnx && dotnet test tests/Fabulis.Cli.Tests`
Expected: build succeeds, all importer and exporter tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src/Fabulis.Cli tests/Fabulis.Cli.Tests
git commit -m "Replace CategoryImportService with full-vault VaultImporter"
```

---

### Task 6: Round-trip equality

**Files:**
- Create: `tests/Fabulis.Cli.Tests/VaultSnapshot.cs`
- Create: `tests/Fabulis.Cli.Tests/RoundTripTests.cs`
- Modify: whichever of `VaultExporter.cs` / `VaultImporter.cs` the tests prove wrong

**Interfaces:**
- Consumes: Tasks 4 and 5.
- Produces: `Fabulis.Cli.Tests.VaultSnapshot.CaptureAsync(FabulisDbContext db) -> Task<string>`

This is the task that answers the question that started the project: *does export cover everything?* The snapshot is a canonical text dump of every content-bearing field. Anything a future feature adds to the vault and forgets to export makes this test fail — provided the snapshot is extended alongside the entity, which the comment in the file says explicitly.

The snapshot deliberately omits the four fields the spec documents as lossy: row ids, `Story`/`Category.CreatedAt`, story summaries, and one-liner/trope timestamps. A separate test asserts those are lossy on purpose, so nobody "fixes" the omission by weakening the comparison.

- [ ] **Step 1: Write the snapshot helper**

`tests/Fabulis.Cli.Tests/VaultSnapshot.cs`:

```csharp
using System.Globalization;
using System.Text;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli.Tests;

/// <summary>
/// A canonical text rendering of everything the archive format is supposed to
/// preserve. Two vaults with equal snapshots round-tripped losslessly.
///
/// WHEN YOU ADD AN ENTITY OR A CONTENT-BEARING COLUMN, ADD IT HERE. That is
/// what makes RoundTripTests fail when the exporter forgets it.
///
/// Deliberately excluded, per the spec's "Known lossy fields":
///   row ids; Story.CreatedAt and Category.CreatedAt (derived on import);
///   story summaries (regenerated); OneLiner/Trope timestamps.
/// </summary>
internal static class VaultSnapshot
{
    public static async Task<string> CaptureAsync(FabulisDbContext db)
    {
        var sb = new StringBuilder();

        sb.AppendLine("== storytellers ==");
        var storytellers = await db.Storytellers
            .OrderBy(s => s.Name).AsNoTracking().ToListAsync();
        foreach (var s in storytellers)
        {
            sb.AppendLine($"name={s.Name}");
            sb.AppendLine($"  model={s.ModelName}");
            sb.AppendLine($"  temperature={Num(s.Temperature)}");
            sb.AppendLine($"  topP={Num(s.TopP)}");
            sb.AppendLine($"  maxTokens={Num(s.MaxTokens)}");
            sb.AppendLine($"  minP={Num(s.MinP)}");
            sb.AppendLine($"  topK={Num(s.TopK)}");
            sb.AppendLine($"  topA={Num(s.TopA)}");
            sb.AppendLine($"  reasoningEffort={s.ReasoningEffort?.ToString() ?? "-"}");
            sb.AppendLine($"  created={Stamp(s.CreatedAt)}");
            sb.AppendLine($"  prompt={Block(s.Prompt)}");
            sb.AppendLine($"  titlingPrompt={Block(s.TitlingPrompt)}");
        }

        sb.AppendLine("== library ==");
        var categories = await db.Categories
            .Include(c => c.Stories).ThenInclude(st => st.Versions).ThenInclude(v => v.Messages)
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .Include(c => c.OneLiners)
            .Include(c => c.Tropes)
            .OrderBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync();

        foreach (var category in categories)
        {
            sb.AppendLine($"category={category.Name}");

            foreach (var story in category.Stories.OrderBy(s => s.Title, StringComparer.Ordinal))
            {
                sb.AppendLine($"  story={story.Title}");
                foreach (var version in story.Versions.OrderBy(v => v.VersionNumber))
                {
                    sb.AppendLine($"    version={version.VersionNumber}");
                    sb.AppendLine($"      model={version.ModelName}");
                    sb.AppendLine($"      created={Stamp(version.CreatedAt)}");
                    foreach (var m in version.Messages.OrderBy(m => m.SortOrder))
                        sb.AppendLine($"      [{m.SortOrder}] {m.Role}: {Block(m.Content)}");
                }
            }

            foreach (var prompt in category.Prompts.OrderBy(p => p.Title, StringComparer.Ordinal))
            {
                sb.AppendLine($"  prompt={prompt.Title}");
                sb.AppendLine($"    created={Stamp(prompt.CreatedAt)}");
                sb.AppendLine($"    updated={Stamp(prompt.UpdatedAt)}");
                foreach (var m in prompt.Messages.OrderBy(m => m.SortOrder))
                    sb.AppendLine($"    [{m.SortOrder}] {Block(m.Content)}");
            }

            // Order matters: it is the display order the file preserves.
            foreach (var o in category.OneLiners.OrderBy(o => o.Id))
                sb.AppendLine($"  oneLiner={Block(o.Text)}");
            foreach (var t in category.Tropes.OrderBy(t => t.Id))
                sb.AppendLine($"  trope={Block(t.Text)}");
        }

        sb.AppendLine("== drafts ==");
        var drafts = await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages)
            .AsNoTracking()
            .ToListAsync();

        foreach (var draft in drafts
            .OrderBy(d => d.Storyteller.Name, StringComparer.Ordinal)
            .ThenBy(d => d.CreatedAt)
            .ThenBy(d => d.Title, StringComparer.Ordinal))
        {
            sb.AppendLine($"draft={draft.Title ?? "-"}");
            sb.AppendLine($"  storyteller={draft.Storyteller.Name}");
            sb.AppendLine($"  created={Stamp(draft.CreatedAt)}");
            sb.AppendLine($"  updated={Stamp(draft.UpdatedAt)}");
            foreach (var m in draft.Messages.OrderBy(m => m.SortOrder))
                sb.AppendLine($"  [{m.SortOrder}] {m.Role}: {Block(m.Content)}");
        }

        sb.AppendLine("== settings ==");
        var settings = await db.AppSettings.OrderBy(s => s.Key).AsNoTracking().ToListAsync();
        foreach (var setting in settings)
        {
            // Redacted by default, so it is not part of the round-trip claim.
            if (setting.Key == "OpenRouterApiKey") continue;
            sb.AppendLine($"{setting.Key}={Block(setting.Value)}");
        }

        return sb.ToString();
    }

    private static string Stamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static string Num(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? "-";

    private static string Num(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    /// <summary>Renders multi-line text on one line so diffs stay readable.</summary>
    private static string Block(string text) =>
        text.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");
}
```

- [ ] **Step 2: Write the failing round-trip tests**

`tests/Fabulis.Cli.Tests/RoundTripTests.cs`:

```csharp
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Cli.Tests;

public class RoundTripTests : IDisposable
{
    private readonly VaultFixture _source = new();
    private readonly VaultFixture _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    private async Task<string> ExportSourceAsync(bool includeSecrets = false)
    {
        var dest = _source.TempDir();
        await new VaultExporter().ExportAsync(_source.Db, dest, includeSecrets);
        return dest;
    }

    [Fact]
    public async Task FullVaultSurvivesExportAndImport()
    {
        await _source.SeedFullVaultAsync();
        var archive = await ExportSourceAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive);

        Assert.Equal(
            await VaultSnapshot.CaptureAsync(_source.Db),
            await VaultSnapshot.CaptureAsync(_target.Db));
    }

    [Fact]
    public async Task ExportingTheImportedVaultProducesTheSameSnapshot()
    {
        await _source.SeedFullVaultAsync();
        var first = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, first);

        var second = _target.TempDir();
        await new VaultExporter().ExportAsync(_target.Db, second);

        using var third = new VaultFixture();
        await new VaultImporter().ImportAsync(third.Db, second);

        Assert.Equal(
            await VaultSnapshot.CaptureAsync(_target.Db),
            await VaultSnapshot.CaptureAsync(third.Db));
    }

    [Theory]
    [InlineData("**Me:**")]
    [InlineData("**StoryTeller:**")]
    [InlineData("---")]
    [InlineData("\\---")]
    [InlineData("- bullet at the start of a line")]
    [InlineData("## Looks like a section heading")]
    [InlineData("trailing whitespace   ")]
    [InlineData("unicode: caf\u00e9 \u2014 \u00fcber \u2603")]
    [InlineData("key: value")]
    public async Task AdversarialMessageContentSurvivesRoundTrip(string payload)
    {
        var storyteller = new Storyteller
        {
            Name = "Storyteller",
            Prompt = "p",
            TitlingPrompt = Storyteller.DefaultTitlingPrompt,
            ModelName = "m",
            CreatedAt = VaultFixture.Epoch,
        };
        var category = new Category { Name = "C", CreatedAt = VaultFixture.Epoch };
        var story = new Story { Title = "S", CreatedAt = VaultFixture.Epoch, Category = category };
        var version = new StoryVersion
        {
            VersionNumber = 1, ModelName = "m", CreatedAt = VaultFixture.Epoch, Story = story,
        };
        version.Messages.Add(new StoryMessage
        {
            Role = MessageRole.Prompt, SortOrder = 0, Content = $"before\n{payload}\nafter",
        });
        story.Versions.Add(version);
        category.Stories.Add(story);
        _source.Db.Storytellers.Add(storyteller);
        _source.Db.Categories.Add(category);
        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.StoryVersions.Include(v => v.Messages).SingleAsync();
        Assert.Equal($"before\n{payload}\nafter", imported.Messages.Single().Content);
    }

    [Fact]
    public async Task CrlfContentIsNormalizedToLfAndStaysStable()
    {
        await _source.SeedFullVaultAsync();
        var version = await _source.Db.StoryVersions.Include(v => v.Messages).FirstAsync();
        version.Messages.First().Content = "line one\r\nline two";
        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.StoryVersions
            .Include(v => v.Messages)
            .FirstAsync(v => v.VersionNumber == 1);
        Assert.Equal("line one\nline two",
            imported.Messages.OrderBy(m => m.SortOrder).First().Content);
    }

    [Fact]
    public async Task SecretsRoundTripOnlyWhenRequested()
    {
        await _source.SeedFullVaultAsync();
        var archive = await ExportSourceAsync(includeSecrets: true);

        await new VaultImporter().ImportAsync(_target.Db, archive);

        var key = await _target.Db.AppSettings.FindAsync("OpenRouterApiKey");
        Assert.Equal("sk-secret-value", key!.Value);
    }

    [Fact]
    public async Task DocumentedLossyFieldsAreActuallyLost()
    {
        await _source.SeedFullVaultAsync();
        var story = await _source.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        story.SummaryText = "A fox goes out and comes back.";
        story.SummaryStatus = SummaryStatus.Ready;
        story.SummarizedThroughVersion = 1;
        story.SummaryUpdatedAt = VaultFixture.Epoch;
        await _source.Db.SaveChangesAsync();

        var archive = await ExportSourceAsync();
        await new VaultImporter().ImportAsync(_target.Db, archive);

        var imported = await _target.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        Assert.Null(imported.SummaryText);
        Assert.Equal(SummaryStatus.None, imported.SummaryStatus);
        // Ids are not preserved either; identity is the path.
        Assert.NotEqual(0, imported.Id);
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `dotnet test tests/Fabulis.Cli.Tests --filter RoundTripTests`
Expected: initially FAIL. Read each snapshot diff and fix the exporter or importer — not the snapshot — unless the field is one of the four documented lossy ones.

- [ ] **Step 4: Fix whatever the round-trip surfaced, then re-run everything**

Run: `dotnet test tests/Fabulis.Cli.Tests`
Expected: PASS, every test class green.

- [ ] **Step 5: Commit**

```bash
git add tests/Fabulis.Cli.Tests src/Fabulis.Cli
git commit -m "Add vault round-trip equality tests"
```

---

### Task 7: Mirror import

**Files:**
- Modify: `src/Fabulis.Cli/VaultImporter.cs`
- Test: `tests/Fabulis.Cli.Tests/MirrorImportTests.cs`

**Interfaces:**
- Consumes: Task 5's `ImportOptions`, `MirrorPlan`, `ImportResult`.
- Produces: `ImportOptions.Mirror` and `ImportOptions.ConfirmDeletions` become live; `ImportResult.Deleted` is populated; `ImportResult.Cancelled` is set when the confirmation callback returns false.

Mirror makes the vault match the directory: it deletes rows absent from disk **and** updates rows whose content differs, which plain merge does not do. Console I/O stays out of the importer — `ConfirmDeletions` is a callback so the behavior is testable; `Program.cs` supplies the prompt in Task 8.

Deletion order is drafts, then storytellers, so a storyteller freed by a deleted draft can go. A storyteller still referenced by a surviving draft is an error, not a cascade. Settings are never deleted, since an absent key means "unspecified" rather than "removed". Summaries on surviving stories are left alone.

- [ ] **Step 1: Write the failing tests**

`tests/Fabulis.Cli.Tests/MirrorImportTests.cs`:

```csharp
using Fabulis.Cli.Archive;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fabulis.Cli.Tests;

public class MirrorImportTests : IDisposable
{
    private readonly VaultFixture _source = new();
    private readonly VaultFixture _target = new();

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }

    /// <summary>Seeds, exports, and imports once so both sides match.</summary>
    private async Task<string> SyncedArchiveAsync()
    {
        await _source.SeedFullVaultAsync();
        var archive = _source.TempDir();
        await new VaultExporter().ExportAsync(_source.Db, archive);
        await new VaultImporter().ImportAsync(_target.Db, archive);
        return archive;
    }

    private static ImportOptions Mirror(Func<MirrorPlan, bool>? confirm = null) =>
        new(Mirror: true, ConfirmDeletions: confirm ?? (_ => true));

    [Fact]
    public async Task DeletesCategoryAbsentFromDisk()
    {
        var archive = await SyncedArchiveAsync();
        _target.Db.Categories.Add(new Category { Name = "Extra", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.False(await _target.Db.Categories.AnyAsync(c => c.Name == "Extra"));
        Assert.Contains("Extra", result.Deleted!.Categories);
    }

    [Fact]
    public async Task DeletesStoryAndVersionAbsentFromDisk()
    {
        var archive = await SyncedArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "Empty Story"), recursive: true);
        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Fables",
            ArchiveLayout.StoriesDir, "The Fox", "Version 1.md"));

        var result = await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.False(await _target.Db.Stories.AnyAsync(s => s.Title == "Empty Story"));
        Assert.Equal(0, await _target.Db.StoryVersions.CountAsync());
        Assert.Contains("Empty Story", string.Join("|", result.Deleted!.Stories));
        Assert.Single(result.Deleted.Versions);
    }

    [Fact]
    public async Task ReplacesListItemsRatherThanMerging()
    {
        var archive = await SyncedArchiveAsync();
        var path = Path.Combine(archive, ArchiveLayout.LibraryDir, "Openers",
            ArchiveLayout.OneLinersFile);
        await File.WriteAllTextAsync(path, ItemListFormat.Write(["Only this one."]));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var openers = await _target.Db.Categories
            .Include(c => c.OneLiners).SingleAsync(c => c.Name == "Openers");
        Assert.Equal(["Only this one."], openers.OneLiners.Select(o => o.Text));
    }

    [Fact]
    public async Task MissingListFileEmptiesTheListUnderMirror()
    {
        var archive = await SyncedArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.LibraryDir, "Openers",
            ArchiveLayout.OneLinersFile));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var openers = await _target.Db.Categories
            .Include(c => c.OneLiners).SingleAsync(c => c.Name == "Openers");
        Assert.Empty(openers.OneLiners);
    }

    [Fact]
    public async Task UpdatesExistingRowsToMatchDisk()
    {
        var archive = await SyncedArchiveAsync();
        var storytellerFile = Path.Combine(archive, ArchiveLayout.StorytellersDir, "Storyteller.md");
        await File.WriteAllTextAsync(storytellerFile,
            (await File.ReadAllTextAsync(storytellerFile)).Replace("temperature: 0.7", "temperature: 1.1"));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var storyteller = await _target.Db.Storytellers.SingleAsync(s => s.Name == "Storyteller");
        Assert.Equal(1.1, storyteller.Temperature);
    }

    [Fact]
    public async Task RefusesToDeleteAStorytellerStillReferencedByASurvivingDraft()
    {
        var archive = await SyncedArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir), recursive: true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new VaultImporter().ImportAsync(_target.Db, archive, Mirror()));

        Assert.Contains("Storyteller", ex.Message);
        Assert.True(await _target.Db.Storytellers.AnyAsync());
    }

    [Fact]
    public async Task DeletesStorytellerOnceItsDraftsAreAlsoGone()
    {
        var archive = await SyncedArchiveAsync();
        Directory.Delete(Path.Combine(archive, ArchiveLayout.StorytellersDir), recursive: true);
        foreach (var file in Directory.GetFiles(Path.Combine(archive, ArchiveLayout.DraftsDir)))
            File.Delete(file);

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.Empty(await _target.Db.Storytellers.ToListAsync());
        Assert.Empty(await _target.Db.Drafts.ToListAsync());
    }

    [Fact]
    public async Task NeverDeletesSettings()
    {
        var archive = await SyncedArchiveAsync();
        File.Delete(Path.Combine(archive, ArchiveLayout.SettingsFile));

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        Assert.NotNull(await _target.Db.AppSettings.FindAsync("AutoLockMinutes"));
    }

    [Fact]
    public async Task LeavesSummariesOnSurvivingStories()
    {
        var archive = await SyncedArchiveAsync();
        var story = await _target.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        story.SummaryText = "kept";
        story.SummaryStatus = SummaryStatus.Ready;
        await _target.Db.SaveChangesAsync();

        await new VaultImporter().ImportAsync(_target.Db, archive, Mirror());

        var after = await _target.Db.Stories.FirstAsync(s => s.Title == "The Fox");
        Assert.Equal("kept", after.SummaryText);
    }

    [Fact]
    public async Task CancellingTheConfirmationAbortsWithoutDeleting()
    {
        var archive = await SyncedArchiveAsync();
        _target.Db.Categories.Add(new Category { Name = "Extra", CreatedAt = VaultFixture.Epoch });
        await _target.Db.SaveChangesAsync();

        var result = await new VaultImporter().ImportAsync(
            _target.Db, archive, Mirror(confirm: _ => false));

        Assert.True(result.Cancelled);
        Assert.True(await _target.Db.Categories.AnyAsync(c => c.Name == "Extra"));
    }

    [Fact]
    public async Task DoesNotPromptWhenThereIsNothingToDelete()
    {
        var archive = await SyncedArchiveAsync();
        var prompted = false;

        var result = await new VaultImporter().ImportAsync(
            _target.Db, archive, Mirror(confirm: _ => { prompted = true; return true; }));

        Assert.False(prompted);
        Assert.False(result.Cancelled);
        Assert.True(result.Deleted!.IsEmpty);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Cli.Tests --filter MirrorImportTests`
Expected: FAIL — `Mirror` is currently ignored, so nothing is deleted or updated.

- [ ] **Step 3: Implement mirror in VaultImporter**

Restructure `ImportAsync` into two passes. The first pass walks the directory exactly as it does now, additionally recording every path-identity it saw and (when mirroring) updating existing rows. The second pass computes and applies deletions.

```csharp
public async Task<ImportResult> ImportAsync(
    FabulisDbContext db, string rootPath, ImportOptions? options = null)
{
    options ??= new ImportOptions();

    var root = new DirectoryInfo(rootPath);
    if (!root.Exists)
        throw new DirectoryNotFoundException($"Directory not found: {rootPath}");

    await VerifyManifestAsync(root);

    var result = new ImportResult();
    var seen = new SeenPaths();

    await ImportSettingsAsync(db, root, result);
    await ImportStorytellersAsync(db, root, result, options.Mirror, seen);
    await ImportLibraryAsync(db, root, result, options.Mirror, seen);
    await ImportDraftsAsync(db, root, result, options.Mirror, seen);

    if (options.Mirror)
    {
        var plan = await BuildMirrorPlanAsync(db, seen);
        result.Deleted = plan;

        if (!plan.IsEmpty && options.ConfirmDeletions is not null && !options.ConfirmDeletions(plan))
        {
            result.Cancelled = true;
            db.ChangeTracker.Clear();
            return result;
        }

        await ApplyMirrorDeletionsAsync(db, seen);
    }

    await db.SaveChangesAsync();
    return result;
}

/// <summary>Path identities observed on disk during the first pass.</summary>
private sealed class SeenPaths
{
    public HashSet<string> Storytellers { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Categories { get; } = new(StringComparer.Ordinal);
    // Keyed "<category>/<story>" and "<category>/<story>/<version>".
    public HashSet<string> Stories { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Versions { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Prompts { get; } = new(StringComparer.Ordinal);
    // Keyed "<storytellerName>|<title>|<createdAt:O>", matching the dedupe key.
    public HashSet<string> Drafts { get; } = new(StringComparer.Ordinal);
}
```

Threading `mirror` through the per-kind methods means each `if (... already exists) continue;` becomes `if (exists) { if (mirror) UpdateFrom(...); continue; }`. The four update helpers:

```csharp
private static void UpdateStoryteller(Storyteller s, Dictionary<string, string> fields,
    Dictionary<string, string> sections)
{
    s.ModelName = Scalar(fields, "model") ?? s.ModelName;
    s.Prompt = sections.GetValueOrDefault(ArchiveLayout.SystemPromptSection, s.Prompt);
    s.TitlingPrompt = sections.GetValueOrDefault(ArchiveLayout.TitlingPromptSection, s.TitlingPrompt);
    s.Temperature = Double(fields, "temperature") ?? s.Temperature;
    s.TopP = Double(fields, "topP");
    s.MaxTokens = Int(fields, "maxTokens");
    s.MinP = Double(fields, "minP");
    s.TopK = Int(fields, "topK");
    s.TopA = Double(fields, "topA");
    s.ReasoningEffort = Enum.TryParse<ReasoningEffort>(
        Scalar(fields, "reasoningEffort"), out var effort) ? effort : null;
    s.CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? s.CreatedAt;
}

private static void UpdateVersion(StoryVersion version, Dictionary<string, string> fields, string body)
{
    version.ModelName = Scalar(fields, "model") ?? version.ModelName;
    version.CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? version.CreatedAt;
    version.Messages.Clear();
    version.Messages.AddRange(ConversationFormat.Read(body).Select(t => new StoryMessage
    {
        Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
    }));
}

private static void UpdatePrompt(Prompt prompt, Dictionary<string, string> fields, string body)
{
    prompt.CreatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "created")) ?? prompt.CreatedAt;
    prompt.UpdatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? prompt.UpdatedAt;
    prompt.Messages.Clear();
    var sortOrder = 0;
    foreach (var block in BlockListFormat.Read(body))
        prompt.Messages.Add(new PromptMessage { Content = block, SortOrder = sortOrder++ });
}

private static void UpdateDraft(Draft draft, Dictionary<string, string> fields, string body)
{
    draft.UpdatedAt = FrontMatter.ParseTimestamp(Scalar(fields, "updated")) ?? draft.UpdatedAt;
    draft.Messages.Clear();
    draft.Messages.AddRange(ConversationFormat.Read(body).Select(t => new DraftMessage
    {
        Role = t.Role, Content = t.Content, SortOrder = t.SortOrder,
    }));
}
```

Under mirror, the two list kinds replace rather than merge. In `ImportOneLiners`/`ImportTropes`, when `mirror` is true clear the collection first, and treat a missing file as an empty list rather than a no-op:

```csharp
private static void ImportOneLiners(
    DirectoryInfo categoryDir, Category category, ImportResult result, bool mirror)
{
    var path = Path.Combine(categoryDir.FullName, ArchiveLayout.OneLinersFile);
    var present = File.Exists(path);

    // Under merge a missing file means "unspecified"; under mirror it means
    // "empty list", because export always writes the file.
    if (!present && !mirror) return;

    if (mirror) category.OneLiners.Clear();

    var existing = category.OneLiners.Select(o => o.Text).ToHashSet(StringComparer.Ordinal);
    var now = DateTime.UtcNow;

    var items = present ? ItemListFormat.Read(File.ReadAllText(path)) : [];
    foreach (var text in items)
    {
        if (!existing.Add(text)) continue;
        category.OneLiners.Add(new OneLiner { Text = text, CreatedAt = now, UpdatedAt = now });
        result.OneLinersCreated++;
    }
}
```

Finally, the deletion pass:

```csharp
private static async Task<MirrorPlan> BuildMirrorPlanAsync(FabulisDbContext db, SeenPaths seen)
{
    var categories = new List<string>();
    var stories = new List<string>();
    var versions = new List<string>();
    var prompts = new List<string>();

    var all = await db.Categories
        .Include(c => c.Stories).ThenInclude(s => s.Versions)
        .Include(c => c.Prompts)
        .ToListAsync();

    foreach (var category in all)
    {
        if (!seen.Categories.Contains(category.Name))
        {
            categories.Add(category.Name);
            continue;
        }

        foreach (var story in category.Stories)
        {
            var storyKey = $"{category.Name}/{story.Title}";
            if (!seen.Stories.Contains(storyKey))
            {
                stories.Add(storyKey);
                continue;
            }

            foreach (var version in story.Versions)
            {
                if (!seen.Versions.Contains($"{storyKey}/{version.VersionNumber}"))
                    versions.Add($"{storyKey}/Version {version.VersionNumber}");
            }
        }

        foreach (var prompt in category.Prompts)
        {
            if (!seen.Prompts.Contains($"{category.Name}/{prompt.Title}"))
                prompts.Add($"{category.Name}/{prompt.Title}");
        }
    }

    var drafts = new List<string>();
    foreach (var draft in await db.Drafts.Include(d => d.Storyteller).ToListAsync())
    {
        if (!seen.Drafts.Contains(DraftKey(draft.Storyteller.Name, draft.Title, draft.CreatedAt)))
            drafts.Add(draft.Title ?? "(untitled)");
    }

    var storytellers = (await db.Storytellers.ToListAsync())
        .Where(s => !seen.Storytellers.Contains(s.Name))
        .Select(s => s.Name)
        .ToList();

    return new MirrorPlan(categories, stories, versions, prompts, drafts, storytellers);
}

internal static string DraftKey(string storytellerName, string? title, DateTime createdAt) =>
    $"{storytellerName}|{title}|{FrontMatter.FormatTimestamp(createdAt)}";

private static async Task ApplyMirrorDeletionsAsync(FabulisDbContext db, SeenPaths seen)
{
    var categories = await db.Categories
        .Include(c => c.Stories).ThenInclude(s => s.Versions)
        .Include(c => c.Prompts)
        .ToListAsync();

    foreach (var category in categories)
    {
        if (!seen.Categories.Contains(category.Name))
        {
            db.Categories.Remove(category);
            continue;
        }

        foreach (var story in category.Stories.ToList())
        {
            var storyKey = $"{category.Name}/{story.Title}";
            if (!seen.Stories.Contains(storyKey))
            {
                db.Stories.Remove(story);
                continue;
            }

            foreach (var version in story.Versions.ToList())
            {
                if (!seen.Versions.Contains($"{storyKey}/{version.VersionNumber}"))
                    db.StoryVersions.Remove(version);
            }
        }

        foreach (var prompt in category.Prompts.ToList())
        {
            if (!seen.Prompts.Contains($"{category.Name}/{prompt.Title}"))
                db.Prompts.Remove(prompt);
        }
    }

    // Drafts before storytellers, so a storyteller freed by a deleted draft
    // can go in the same run.
    var drafts = await db.Drafts.Include(d => d.Storyteller).ToListAsync();
    var survivingDrafts = new List<Draft>();
    foreach (var draft in drafts)
    {
        if (seen.Drafts.Contains(DraftKey(draft.Storyteller.Name, draft.Title, draft.CreatedAt)))
            survivingDrafts.Add(draft);
        else
            db.Drafts.Remove(draft);
    }

    foreach (var storyteller in await db.Storytellers.ToListAsync())
    {
        if (seen.Storytellers.Contains(storyteller.Name)) continue;

        if (survivingDrafts.Any(d => d.StorytellerID == storyteller.Id))
            throw new InvalidOperationException(
                $"Cannot delete storyteller '{storyteller.Name}': a draft in the archive still " +
                "references it. Remove that draft from the archive, or restore the storyteller file.");

        db.Storytellers.Remove(storyteller);
    }
}
```

Populate `seen` in the first pass: add `seen.Storytellers.Add(name)`, `seen.Categories.Add(categoryDir.Name)`, `seen.Stories.Add($"{categoryDir.Name}/{storyDir.Name}")`, `seen.Versions.Add($"{categoryDir.Name}/{storyDir.Name}/{versionNumber}")`, `seen.Prompts.Add($"{categoryDir.Name}/{title}")` and `seen.Drafts.Add(DraftKey(storytellerName, title, createdAt))` at the point each is read, whether or not the row already existed.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Fabulis.Cli.Tests`
Expected: PASS, including the merge tests from Task 5 which must still show no deletions.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Cli/VaultImporter.cs tests/Fabulis.Cli.Tests/MirrorImportTests.cs
git commit -m "Add --mirror import semantics to VaultImporter"
```

---

### Task 8: CLI flags and documentation

**Files:**
- Modify: `src/Fabulis.Cli/Program.cs`
- Modify: `src/Fabulis.Cli/README.md`
- Modify: `CLAUDE.md`
- Modify: `BACKLOG.md` (only if it holds an entry this work ships)

**Interfaces:**
- Consumes: `VaultExporter.ExportAsync(..., includeSecrets)`, `VaultImporter.ImportAsync(..., ImportOptions)`.
- Produces: no new types — this is the console surface.

`Program.cs` currently parses positional arguments only, which cannot express `--mirror`, `--include-secrets` or `--yes`.

- [ ] **Step 1: Add flag parsing**

Replace the `switch (command)` block's argument handling with a small parser that separates flags from positionals:

```csharp
static (List<string> Positionals, HashSet<string> Flags) ParseArgs(string[] args)
{
    var positionals = new List<string>();
    var flags = new HashSet<string>(StringComparer.Ordinal);

    foreach (var arg in args)
    {
        if (arg.StartsWith("--", StringComparison.Ordinal)) flags.Add(arg);
        else positionals.Add(arg);
    }

    return (positionals, flags);
}
```

Reject unknown flags rather than ignoring them, so a typo does not silently
lose `--include-secrets`:

`Program.cs` uses top-level statements, which allow local functions but not
field declarations, so the known-flag list is a local:

```csharp
string[] knownFlags = ["--mirror", "--include-secrets", "--yes"];

var unknown = flags.Except(knownFlags).ToList();
if (unknown.Count > 0)
{
    Console.Error.WriteLine($"error: unknown flag(s): {string.Join(", ", unknown)}");
    PrintUsage();
    return 1;
}
```

- [ ] **Step 2: Wire the flags into the export and import branches**

Export:

```csharp
var result = await new VaultExporter().ExportAsync(db, path, includeSecrets: flags.Contains("--include-secrets"));
```

Import, with the mirror confirmation living here rather than in the importer:

```csharp
var mirror = flags.Contains("--mirror");
var assumeYes = flags.Contains("--yes");

var options = new ImportOptions(
    Mirror: mirror,
    ConfirmDeletions: plan =>
    {
        if (assumeYes) return true;

        Console.WriteLine("--mirror will delete the following from the vault:");
        foreach (var name in plan.Categories) Console.WriteLine($"  category  {name}");
        foreach (var name in plan.Stories) Console.WriteLine($"  story     {name}");
        foreach (var name in plan.Versions) Console.WriteLine($"  version   {name}");
        foreach (var name in plan.Prompts) Console.WriteLine($"  prompt    {name}");
        foreach (var name in plan.Drafts) Console.WriteLine($"  draft     {name}");
        foreach (var name in plan.Storytellers) Console.WriteLine($"  storyteller {name}");
        Console.Write($"Delete {plan.Count} item(s)? [y/N] ");

        var answer = Console.ReadLine();
        return string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    });

var result = await new VaultImporter().ImportAsync(db, path, options);

if (result.Cancelled)
{
    Console.WriteLine("Cancelled; nothing was changed.");
    return 1;
}
```

- [ ] **Step 3: Update the usage text**

```csharp
Console.Error.WriteLine("  export <destination> [--include-secrets]");
Console.Error.WriteLine("      Write the whole vault to a directory tree (must not exist).");
Console.Error.WriteLine("      --include-secrets also writes the OpenRouter API key, which is");
Console.Error.WriteLine("      otherwise omitted.");
Console.Error.WriteLine();
Console.Error.WriteLine("  import <source> [--mirror] [--yes]");
Console.Error.WriteLine("      Read an archive directory into the vault. Additive and idempotent");
Console.Error.WriteLine("      by default. --mirror makes the vault match the directory exactly,");
Console.Error.WriteLine("      deleting what the archive does not contain, after confirmation.");
Console.Error.WriteLine("      --yes skips that confirmation.");
```

- [ ] **Step 4: Rewrite the README's format sections**

In `src/Fabulis.Cli/README.md`, replace the "Commands" and "On-disk format" sections. The whole shape-detection description goes: detection is now a check for `library/`. Specifically:

- Commands become `export <destination> [--include-secrets]`, `import <source> [--mirror] [--yes]`, `sillytavern <source> <destination>`.
- The single-category and drafts-folder import modes no longer exist — delete that whole detection-rule paragraph.
- The on-disk format block becomes the tree from the spec (`fabulis.md`, `settings.md`, `storytellers/`, `library/<Category>/{stories,prompts,one-liners.md,tropes.md}`, `drafts/`).
- Document front matter, the `---` prompt separator, the backslash escape rule, and `formatVersion: 1`.
- State plainly that archives written before this change are not readable, and that `--include-secrets` puts a credential in plaintext.
- Delete the paragraph claiming drafts with unknown storytellers are skipped "because the export does not capture the storyteller's system prompt or tuning" — it now does. Keep the skip behavior itself documented, since a hand-built archive can still reference a storyteller that is not there.
- Note under `sillytavern` that its output is written to `<destination>/drafts/` (not `_drafts/`).

- [ ] **Step 5: Update CLAUDE.md**

The "Bulk import / export" block gains the flags:

```bash
dotnet run --project src/Fabulis.Cli -- export <destination> [--include-secrets]
dotnet run --project src/Fabulis.Cli -- import <source> [--mirror] [--yes]
```

Add `tests/Fabulis.Cli.Tests/` to the project-structure list alongside the existing test project.

- [ ] **Step 6: Verify the CLI end to end against a real vault**

```bash
dotnet build Fabulis.slnx
```

Then, with the server stopped, run an export and a re-import into a scratch copy of the database. Confirm the printed counts are non-zero for every kind present, and that `grep -r "sk-" <destination>` finds nothing.

- [ ] **Step 7: Commit**

```bash
git add src/Fabulis.Cli CLAUDE.md BACKLOG.md
git commit -m "Add --mirror, --include-secrets and --yes flags with docs"
```

---

### Task 9: Server-side name validation

**Files:**
- Create: `src/Fabulis.Server/Api/NameValidation.cs`
- Modify: `src/Fabulis.Server/Api/LibraryEndpoints.cs:129-147` (category create and rename)
- Modify: `src/Fabulis.Server/Api/DraftEndpoints.cs:90-103` (new category name, new story title)
- Modify: `src/Fabulis.Server/Api/PromptEndpoints.cs:18-35` (create and update)
- Modify: `src/Fabulis.Server/Api/StorytellerEndpoints.cs:25-33` (update)
- Test: `tests/Fabulis.Server.Tests/NameValidationTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks — this is server-side and independent.
- Produces: `Fabulis.Server.Api.NameValidation.IsValidPathSegment(string? name) -> bool` and `NameValidation.Error(string field) -> string`

Path-as-identity is only sound if the four values that become path segments cannot contain a path separator. Category names, story titles, prompt titles and storyteller names are validated; draft titles are not, because a draft's authoritative title lives in front matter and its filename is derived.

Note `PromptEndpoints` POST does not currently validate `Title` at all — only PUT does. This task fixes that gap as well.

- [ ] **Step 1: Write the failing tests**

`tests/Fabulis.Server.Tests/NameValidationTests.cs`:

```csharp
using Fabulis.Server.Api;
using Xunit;

namespace Fabulis.Server.Tests;

public class NameValidationTests
{
    [Theory]
    [InlineData("Fables")]
    [InlineData("A Night In The Fens")]
    [InlineData("caf\u00e9 stories")]
    [InlineData("dashes-and_underscores")]
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

    [Fact]
    public void ErrorNamesTheOffendingField()
    {
        Assert.Contains("name", NameValidation.Error("name"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Fabulis.Server.Tests --filter NameValidationTests`
Expected: compile failure — `NameValidation` does not exist.

- [ ] **Step 3: Implement NameValidation**

`src/Fabulis.Server/Api/NameValidation.cs`:

```csharp
namespace Fabulis.Server.Api;

/// <summary>
/// Guards the four values that become path segments in a CLI archive
/// (category name, story title, prompt title, storyteller name). The archive
/// identifies rows by path, so a separator or control character in one of
/// these would make export lossy and import ambiguous.
/// </summary>
public static class NameValidation
{
    public static bool IsValidPathSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var trimmed = name.Trim();
        if (trimmed is "." or "..") return false;

        foreach (var ch in trimmed)
        {
            if (ch == '/' || ch == '\\' || char.IsControl(ch)) return false;
        }

        return true;
    }

    public static string Error(string field) =>
        $"{field} must not contain '/', '\\\\' or control characters";
}
```

- [ ] **Step 4: Apply validation at the five call sites**

In `LibraryEndpoints.cs`, in both `MapPost("/categories")` and `MapPut("/categories/{id:int}")`, replace the `IsNullOrWhiteSpace` guard with:

```csharp
if (string.IsNullOrWhiteSpace(body.Name))
    return Results.BadRequest(new { error = "name is required" });
if (!NameValidation.IsValidPathSegment(body.Name))
    return Results.BadRequest(new { error = NameValidation.Error("name") });
```

In `DraftEndpoints.cs`, guard `body.NewCategoryName` before the category is created, and `newStoryTitle` after it is trimmed:

```csharp
if (!NameValidation.IsValidPathSegment(body.NewCategoryName))
    return Results.BadRequest(new { error = NameValidation.Error("newCategoryName") });
```

```csharp
if (newStoryTitle is not null && !NameValidation.IsValidPathSegment(newStoryTitle))
    return Results.BadRequest(new { error = NameValidation.Error("newStoryTitle") });
```

In `PromptEndpoints.cs`, add to `MapPost("")` — which validates no title today — and to `MapPut("/{id:int}")`:

```csharp
if (!NameValidation.IsValidPathSegment(body.Title))
    return Results.BadRequest(new { error = NameValidation.Error("title") });
```

In `StorytellerEndpoints.cs`, extend the existing guard in `MapPut("")`:

```csharp
if (!NameValidation.IsValidPathSegment(body.Name))
    return Results.BadRequest(new { error = NameValidation.Error("name") });
```

Add `using Fabulis.Server.Api;` where the endpoint file does not already sit in that namespace.

- [ ] **Step 5: Run the full solution test suite**

Run: `dotnet build Fabulis.slnx && dotnet test Fabulis.slnx`
Expected: PASS across `Fabulis.Server.Tests` and `Fabulis.Cli.Tests`.

- [ ] **Step 6: Commit**

```bash
git add src/Fabulis.Server/Api tests/Fabulis.Server.Tests/NameValidationTests.cs
git commit -m "Reject path separators in names that become archive path segments"
```
