# Narration via Kokoro TTS Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add audio narration for stories and drafts. A user-configured Kokoro-FastAPI (Remsky) server synthesises per-bubble MP3s via a Fabulis-server proxy; the SwiftUI client gets play/pause, ±10s seek, and "Play from here" inside each story/draft view.

**Architecture:** Three layers. (1) Server: a `KokoroService` wraps the Kokoro HTTP API; `NarrationEndpoints` proxies it with markdown stripping and session auth; `SettingsEndpoints` gains Kokoro URL + voice + speed and exposes `narrationAvailable`. (2) Client API + DTOs grow narration methods. (3) Client UI: a `NarrationPlayer` (observable, AVAudioPlayer-backed, per-bubble prefetch) plus a `NarrationBar` and per-bubble "Play from here" context menu entries, with the playing bubble highlighted and scrolled into view.

**Tech Stack:** ASP.NET Core .NET 10 / EF Core / SQLite+SQLCipher (server), `Markdig` (new server dep), xUnit (new test project), SwiftUI / iOS 18.5+ / Mac Catalyst (client), `AVFoundation` (client narration), `URLSession`.

**Spec:** [docs/superpowers/specs/2026-05-26-narration-design.md](../specs/2026-05-26-narration-design.md)

---

## Testing notes (read before starting)

This codebase has **no existing test projects** for either side:

- **Server:** This plan adds one (`tests/Fabulis.Server.Tests/`) for the parts that are cleanly unit-testable: the markdown stripper (pure function) and `KokoroService` (HTTP-mocked). For the endpoint layer, we extract the small bits of validation/normalisation logic into pure helpers in `Data/NarrationValidation.cs` and test those — full WebApplicationFactory integration tests would require refactoring the SQLCipher/vault DI and aren't worth the scope for two thin endpoints. Endpoint-level behaviour is verified by manual smoke at the end of the server tasks.
- **Client:** `client/FabulisTests/FabulisTests.swift` is an empty boilerplate stub and no view-test infrastructure exists. Per the precedent in [2026-05-21-story-version-dropdown.md](2026-05-21-story-version-dropdown.md), the client verification gate is **clean compile + manual smoke**, not unit tests. The spec mentions a `NarrationPlayer` unit test; we're deferring it to keep this plan scoped. If we later add a client test target, that test fits naturally.

**Server test command** (used throughout server tasks):

```bash
dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj
```

**Server build command:**

```bash
dotnet build Fabulis.slnx
```

**Client build command** (adjust the simulator name to one that exists locally — run `xcrun simctl list devices available` to see options):

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected on success: a line ending in `** BUILD SUCCEEDED **`. If the scheme isn't found from the command line, open `client/Fabulis.xcodeproj` in Xcode and build with ⌘B.

---

## File Structure

### Server

- **Create:** `tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj` — new xUnit project, references `Fabulis.Server`.
- **Create:** `tests/Fabulis.Server.Tests/MarkdownStripperTests.cs`
- **Create:** `tests/Fabulis.Server.Tests/KokoroServiceTests.cs`
- **Create:** `tests/Fabulis.Server.Tests/NarrationValidationTests.cs`
- **Modify:** `Fabulis.slnx` — add the test project.
- **Modify:** `src/Fabulis.Server/Fabulis.Server.csproj` — add `Markdig` package.
- **Create:** `src/Fabulis.Server/Data/MarkdownStripper.cs` — pure static helper.
- **Create:** `src/Fabulis.Server/Data/KokoroService.cs` — Kokoro HTTP wrapper.
- **Create:** `src/Fabulis.Server/Data/NarrationValidation.cs` — pure validators/defaulters used by the endpoint.
- **Create:** `src/Fabulis.Server/Api/NarrationEndpoints.cs` — `/api/v1/narration/{voices,synthesize}`.
- **Modify:** `src/Fabulis.Server/Api/Dtos.cs` — add `NarrationVoice`, `VoicesResponse`, `SynthesizeRequest`; extend `SettingsDto` + `SettingsUpdateRequest`.
- **Modify:** `src/Fabulis.Server/Api/SettingsEndpoints.cs` — handle Kokoro URL, voice, speed, and `narrationAvailable`.
- **Modify:** `src/Fabulis.Server/Program.cs` — register `KokoroService` (Scoped) and a named `HttpClient` `"kokoro"`; map narration endpoints.

### Client

- **Modify:** `client/Fabulis/Models/APIDtos.swift` — extend `SettingsDto`; add `NarrationVoice`, `VoicesResponse`.
- **Modify:** `client/Fabulis/Services/FabulisAPIClient.swift` — new `narrationVoices()`, `synthesize(...)`, extended `updateSettings(...)`. Add a raw-bytes request helper alongside the existing JSON helpers.
- **Create:** `client/Fabulis/Services/NarrationPlayer.swift` — `@Observable` player.
- **Create:** `client/Fabulis/Views/Narration/NarrationBar.swift` — bottom controls bar.
- **Create:** `client/Fabulis/Views/Settings/NarrationVoicePickerView.swift` — voice picker.
- **Modify:** `client/Fabulis/Views/Settings/SettingsView.swift` — add "Narration" section.
- **Modify:** `client/Fabulis/Views/Story/StoryView.swift` — own player + bar + scroll-to-playing, pass params.
- **Modify:** `client/Fabulis/Views/Story/StoryMessageView.swift` — playing border + "Play from here" context menu.
- **Modify:** `client/Fabulis/Views/Draft/DraftView.swift` — own player + bar + scroll-to-playing, stop on mutation, pass params.
- **Modify:** `client/Fabulis/Views/Draft/DraftMessageView.swift` — playing border + "Play from here" context menu item.
- **Modify:** `client/Fabulis.xcodeproj/project.pbxproj` — add the three new Swift files (`NarrationPlayer.swift`, `NarrationBar.swift`, `NarrationVoicePickerView.swift`) to the `Fabulis` target.
- **Modify:** `BACKLOG.md` — add the "Background narration playback" entry once v1 ships.

---

## Task 1: Set up the server xUnit test project

**Files:**
- Create: `tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
- Create: `tests/Fabulis.Server.Tests/SmokeTests.cs`
- Modify: `Fabulis.slnx`

- [ ] **Step 1: Create the test project file**

Create `tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`:

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
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Fabulis.Server\Fabulis.Server.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add a trivial smoke test so the project compiles and runs**

Create `tests/Fabulis.Server.Tests/SmokeTests.cs`:

```csharp
namespace Fabulis.Server.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectRuns()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 3: Add the test project to the solution file**

Open `Fabulis.slnx` and add a `tests/` folder containing the new project. The whole file should read:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/Fabulis.Server/Fabulis.Server.csproj" />
    <Project Path="src/Fabulis.Cli/Fabulis.Cli.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 4: Verify the project builds**

Run: `dotnet build Fabulis.slnx`
Expected: `Build succeeded` with no errors.

- [ ] **Step 5: Verify the smoke test runs**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: `Passed: 1, Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add tests/Fabulis.Server.Tests/ Fabulis.slnx
git commit -m "Add Fabulis.Server.Tests xUnit project"
```

---

## Task 2: MarkdownStripper (TDD)

**Files:**
- Create: `tests/Fabulis.Server.Tests/MarkdownStripperTests.cs`
- Create: `src/Fabulis.Server/Data/MarkdownStripper.cs`
- Modify: `src/Fabulis.Server/Fabulis.Server.csproj`

- [ ] **Step 1: Add the Markdig package reference**

Edit `src/Fabulis.Server/Fabulis.Server.csproj` and add inside the existing `<ItemGroup>` that holds `PackageReference`s:

```xml
<PackageReference Include="Markdig" Version="0.40.0" />
```

The final `ItemGroup` should look like:

```xml
<ItemGroup>
  <PackageReference Include="Markdig" Version="0.40.0" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.5">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.5" />
  <PackageReference Include="SQLitePCLRaw.bundle_e_sqlcipher" Version="2.1.11" />
</ItemGroup>
```

Run: `dotnet restore Fabulis.slnx`
Expected: completes with no errors.

- [ ] **Step 2: Write the failing tests**

Create `tests/Fabulis.Server.Tests/MarkdownStripperTests.cs`:

```csharp
using Fabulis.Server.Data;

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
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter "FullyQualifiedName~MarkdownStripperTests"`
Expected: compilation fails because `MarkdownStripper` doesn't exist yet.

- [ ] **Step 4: Implement MarkdownStripper**

Create `src/Fabulis.Server/Data/MarkdownStripper.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Fabulis.Server.Data;

/// <summary>
/// Converts the markdown stored in story / draft messages into plain
/// text suitable for TTS synthesis. Removes emphasis/heading/link
/// syntax (keeping link text), drops fenced code blocks entirely
/// (they sound awful read aloud), strips HTML tags but keeps their
/// inner text, drops footnote references, and collapses whitespace.
/// </summary>
public static class MarkdownStripper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseFootnotes()
        .Build();

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HtmlTag = new(@"<[^>]+>", RegexOptions.Compiled);

    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var doc = Markdown.Parse(markdown, Pipeline);
        var sb = new StringBuilder();
        Render(doc, sb);

        var withoutHtml = HtmlTag.Replace(sb.ToString(), string.Empty);
        return WhitespaceRun.Replace(withoutHtml, " ").Trim();
    }

    private static void Render(MarkdownObject node, StringBuilder sb)
    {
        switch (node)
        {
            case FencedCodeBlock:
                return; // Drop entirely.
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case LiteralInline literal:
                sb.Append(literal.Content.ToString());
                break;
            case LineBreakInline:
                sb.Append(' ');
                break;
            case LinkInline link:
                foreach (var child in link) Render(child, sb);
                break;
            case FootnoteLink:
                return; // Drop footnote reference markers like [^1].
            case ContainerBlock container:
                foreach (var child in container) Render(child, sb);
                sb.Append(' ');
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                foreach (var child in leaf.Inline) Render(child, sb);
                sb.Append(' ');
                break;
            case ContainerInline inline:
                foreach (var child in inline) Render(child, sb);
                break;
        }
    }
}
```

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter "FullyQualifiedName~MarkdownStripperTests"`
Expected: all `MarkdownStripperTests` pass. If a particular row fails (footnotes, HTML, or a fenced-code edge case), adjust the renderer and re-run — the test table is the contract.

- [ ] **Step 6: Commit**

```bash
git add tests/Fabulis.Server.Tests/MarkdownStripperTests.cs \
        src/Fabulis.Server/Data/MarkdownStripper.cs \
        src/Fabulis.Server/Fabulis.Server.csproj
git commit -m "Add MarkdownStripper for TTS-ready plain text"
```

---

## Task 3: KokoroService (TDD with HttpMessageHandler stub)

**Files:**
- Create: `tests/Fabulis.Server.Tests/StubHttpMessageHandler.cs`
- Create: `tests/Fabulis.Server.Tests/KokoroServiceTests.cs`
- Create: `src/Fabulis.Server/Data/KokoroService.cs`

> **Important:** `KokoroService` reads `KokoroBaseUrl` from `AppSettings` (the DB). For tests we'd need a `FabulisDbContext` instance, which requires `VaultService`-managed SQLCipher setup. To stay testable without spinning that up, we make the URL lookup overridable via a constructor parameter (`Func<CancellationToken, Task<string?>>`) that production code wires to the DB and tests wire to a fake. The production-default ctor is what `Program.cs` picks up via DI.

- [ ] **Step 1: Add a reusable stub HTTP handler for tests**

Create `tests/Fabulis.Server.Tests/StubHttpMessageHandler.cs`:

```csharp
namespace Fabulis.Server.Tests;

/// <summary>
/// Records the last request and returns a programmed response. Use
/// for unit-testing classes that take an HttpClient (directly or
/// via IHttpClientFactory).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Responder { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        if (Responder is null)
            throw new InvalidOperationException("StubHttpMessageHandler.Responder not set.");
        return await Responder(request);
    }
}

/// <summary>
/// Adapts a single HttpClient into IHttpClientFactory for tests.
/// </summary>
public sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
```

- [ ] **Step 2: Write the failing KokoroService tests**

Create `tests/Fabulis.Server.Tests/KokoroServiceTests.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using Fabulis.Server.Data;

namespace Fabulis.Server.Tests;

public class KokoroServiceTests
{
    private static (KokoroService service, StubHttpMessageHandler stub) Build(
        string? baseUrl = "http://localhost:8880")
    {
        var stub = new StubHttpMessageHandler();
        var client = new HttpClient(stub);
        var factory = new FixedHttpClientFactory(client);
        var service = new KokoroService(factory, _ => Task.FromResult(baseUrl));
        return (service, stub);
    }

    [Fact]
    public async Task SynthesizeSendsExpectedRequest()
    {
        var (service, stub) = Build();
        stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0x49, 0x44, 0x33])
        });

        var bytes = await service.SynthesizeAsync("hello", "af_bella", 1.25, CancellationToken.None);

        Assert.NotNull(stub.LastRequest);
        Assert.Equal(HttpMethod.Post, stub.LastRequest!.Method);
        Assert.Equal("http://localhost:8880/v1/audio/speech", stub.LastRequest.RequestUri!.ToString());
        Assert.NotNull(stub.LastRequestBody);
        using var body = JsonDocument.Parse(stub.LastRequestBody!);
        Assert.Equal("kokoro", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("hello", body.RootElement.GetProperty("input").GetString());
        Assert.Equal("af_bella", body.RootElement.GetProperty("voice").GetString());
        Assert.Equal("mp3", body.RootElement.GetProperty("response_format").GetString());
        Assert.Equal(1.25, body.RootElement.GetProperty("speed").GetDouble());
        Assert.Equal(new byte[] { 0x49, 0x44, 0x33 }, bytes);
    }

    [Fact]
    public async Task SynthesizeThrowsKokoroUnavailableWhenUrlMissing()
    {
        var (service, _) = Build(baseUrl: null);
        await Assert.ThrowsAsync<KokoroUnavailableException>(() =>
            service.SynthesizeAsync("hi", "af_bella", 1.0, CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeThrowsKokoroUpstreamErrorOn5xx()
    {
        var (service, stub) = Build();
        stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("boom")
        });
        await Assert.ThrowsAsync<KokoroUpstreamException>(() =>
            service.SynthesizeAsync("hi", "af_bella", 1.0, CancellationToken.None));
    }

    [Fact]
    public async Task ListVoicesParsesKokoroResponse()
    {
        var (service, stub) = Build();
        stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"voices":["af_bella","am_michael","bf_emma"]}""",
                Encoding.UTF8, "application/json")
        });

        var voices = await service.ListVoicesAsync(CancellationToken.None);

        Assert.Equal(3, voices.Count);
        Assert.Equal("af_bella", voices[0].Id);
        Assert.Equal("Bella", voices[0].DisplayName);
        Assert.Equal("en-us-female", voices[0].Language);
        Assert.Equal("am_michael", voices[1].Id);
        Assert.Equal("en-us-male", voices[1].Language);
        Assert.Equal("en-gb-female", voices[2].Language);
    }

    [Fact]
    public async Task ListVoicesCachesFor5Minutes()
    {
        var (service, stub) = Build();
        var calls = 0;
        stub.Responder = _ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"voices":["af_bella"]}""", Encoding.UTF8, "application/json")
            });
        };

        await service.ListVoicesAsync(CancellationToken.None);
        await service.ListVoicesAsync(CancellationToken.None);

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ProbeReturnsTrueOn200()
    {
        var (service, stub) = Build();
        stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        Assert.True(await service.ProbeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProbeReturnsFalseOnError()
    {
        var (service, stub) = Build();
        stub.Responder = _ => throw new HttpRequestException("connection refused");
        Assert.False(await service.ProbeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ProbeReturnsFalseWhenUrlMissing()
    {
        var (service, _) = Build(baseUrl: null);
        Assert.False(await service.ProbeAsync(CancellationToken.None));
    }
}
```

- [ ] **Step 3: Run the tests to confirm they fail**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter "FullyQualifiedName~KokoroServiceTests"`
Expected: compilation fails because `KokoroService`, `KokoroVoice`, `KokoroUnavailableException`, and `KokoroUpstreamException` don't exist yet.

- [ ] **Step 4: Implement KokoroService**

Create `src/Fabulis.Server/Data/KokoroService.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public sealed record KokoroVoice(string Id, string DisplayName, string Language);

public sealed class KokoroUnavailableException(string message) : Exception(message);
public sealed class KokoroUpstreamException(string message, int upstreamStatus)
    : Exception(message)
{
    public int UpstreamStatus { get; } = upstreamStatus;
}

public class KokoroService
{
    private static readonly TimeSpan VoicesCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProbeCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<CancellationToken, Task<string?>> _baseUrlLookup;

    private readonly object _voicesGate = new();
    private DateTimeOffset _voicesCachedAt = DateTimeOffset.MinValue;
    private IReadOnlyList<KokoroVoice>? _voicesCache;

    private readonly object _probeGate = new();
    private DateTimeOffset _probeCachedAt = DateTimeOffset.MinValue;
    private bool _probeCache;

    /// <summary>Production ctor. Wires the URL lookup to AppSettings.</summary>
    public KokoroService(
        IHttpClientFactory httpClientFactory,
        IServiceProvider services)
        : this(httpClientFactory, ct => GetBaseUrlFromDbAsync(services, ct)) { }

    /// <summary>Testing ctor. Injects the URL lookup directly.</summary>
    public KokoroService(
        IHttpClientFactory httpClientFactory,
        Func<CancellationToken, Task<string?>> baseUrlLookup)
    {
        _httpClientFactory = httpClientFactory;
        _baseUrlLookup = baseUrlLookup;
    }

    private static async Task<string?> GetBaseUrlFromDbAsync(
        IServiceProvider services, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FabulisDbContext>();
        var setting = await db.AppSettings.FindAsync(["KokoroBaseUrl"], ct);
        var value = setting?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.TrimEnd('/');
    }

    /// <summary>
    /// Called by SettingsEndpoints when the user updates the Kokoro URL,
    /// so the next probe / voices fetch goes to the new server.
    /// </summary>
    public void InvalidateCaches()
    {
        lock (_voicesGate) { _voicesCache = null; _voicesCachedAt = DateTimeOffset.MinValue; }
        lock (_probeGate) { _probeCachedAt = DateTimeOffset.MinValue; }
    }

    public async Task<byte[]> SynthesizeAsync(string text, string voice, double speed, CancellationToken ct)
    {
        var baseUrl = await _baseUrlLookup(ct)
            ?? throw new KokoroUnavailableException("Kokoro base URL not configured");

        var client = _httpClientFactory.CreateClient("kokoro");
        var body = new
        {
            model = "kokoro",
            input = text,
            voice,
            response_format = "mp3",
            speed
        };

        try
        {
            using var response = await client.PostAsJsonAsync(
                $"{baseUrl}/v1/audio/speech", body, JsonOptions, ct);
            if ((int)response.StatusCode >= 500)
                throw new KokoroUpstreamException(
                    $"Kokoro returned {(int)response.StatusCode}", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (HttpRequestException ex) when (ex is not null)
        {
            throw new KokoroUnavailableException($"Could not reach Kokoro: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<KokoroVoice>> ListVoicesAsync(CancellationToken ct)
    {
        lock (_voicesGate)
        {
            if (_voicesCache is not null && DateTimeOffset.UtcNow - _voicesCachedAt < VoicesCacheTtl)
                return _voicesCache;
        }

        var baseUrl = await _baseUrlLookup(ct)
            ?? throw new KokoroUnavailableException("Kokoro base URL not configured");

        var client = _httpClientFactory.CreateClient("kokoro");
        List<KokoroVoice> normalised;
        try
        {
            var json = await client.GetFromJsonAsync<JsonElement>($"{baseUrl}/v1/voices", ct);
            var ids = new List<string>();
            if (json.TryGetProperty("voices", out var voicesEl) && voicesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in voicesEl.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is { } s)
                        ids.Add(s);
            }
            normalised = ids.Select(Normalise).ToList();
        }
        catch (HttpRequestException ex)
        {
            throw new KokoroUnavailableException($"Could not reach Kokoro: {ex.Message}");
        }

        lock (_voicesGate)
        {
            _voicesCache = normalised;
            _voicesCachedAt = DateTimeOffset.UtcNow;
            return _voicesCache;
        }
    }

    /// <summary>
    /// Kokoro voice ids follow the convention {language-region prefix}{f|m}_{name},
    /// e.g. "af_bella" = American Female Bella. We unpack that into a friendlier
    /// display label and a coarse language tag.
    /// </summary>
    internal static KokoroVoice Normalise(string id)
    {
        var underscore = id.IndexOf('_');
        if (underscore < 2 || underscore == id.Length - 1)
            return new KokoroVoice(id, id, "unknown");

        var prefix = id[..underscore];
        var name = id[(underscore + 1)..];
        var display = char.ToUpperInvariant(name[0]) + name[1..];

        var region = prefix[..^1] switch
        {
            "a" => "en-us",
            "b" => "en-gb",
            "j" => "ja",
            "z" => "zh",
            "e" => "es",
            "f" => "fr",
            "h" => "hi",
            "i" => "it",
            "p" => "pt-br",
            _ => "unknown"
        };
        var sex = prefix[^1] switch
        {
            'f' => "female",
            'm' => "male",
            _ => "unknown"
        };
        return new KokoroVoice(id, display, $"{region}-{sex}");
    }

    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        lock (_probeGate)
        {
            if (DateTimeOffset.UtcNow - _probeCachedAt < ProbeCacheTtl)
                return _probeCache;
        }

        var result = await ProbeUncachedAsync(ct);
        lock (_probeGate)
        {
            _probeCache = result;
            _probeCachedAt = DateTimeOffset.UtcNow;
            return result;
        }
    }

    private async Task<bool> ProbeUncachedAsync(CancellationToken ct)
    {
        string? baseUrl;
        try { baseUrl = await _baseUrlLookup(ct); }
        catch { return false; }
        if (baseUrl is null) return false;

        var client = _httpClientFactory.CreateClient("kokoro");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ProbeTimeout);

        try
        {
            using var response = await client.GetAsync($"{baseUrl}/health", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter "FullyQualifiedName~KokoroServiceTests"`
Expected: all `KokoroServiceTests` pass.

- [ ] **Step 6: Commit**

```bash
git add tests/Fabulis.Server.Tests/StubHttpMessageHandler.cs \
        tests/Fabulis.Server.Tests/KokoroServiceTests.cs \
        src/Fabulis.Server/Data/KokoroService.cs
git commit -m "Add KokoroService HTTP wrapper for narration"
```

---

## Task 4: NarrationValidation helpers (TDD)

**Files:**
- Create: `tests/Fabulis.Server.Tests/NarrationValidationTests.cs`
- Create: `src/Fabulis.Server/Data/NarrationValidation.cs`

This holds the small bits of validation/defaulting logic the endpoint uses, so they can be tested without spinning up the full pipeline.

- [ ] **Step 1: Write the failing tests**

Create `tests/Fabulis.Server.Tests/NarrationValidationTests.cs`:

```csharp
using Fabulis.Server.Data;

namespace Fabulis.Server.Tests;

public class NarrationValidationTests
{
    [Fact]
    public void NormalizesSpeedFromRequestThenSettingThenOne()
    {
        Assert.Equal(1.5, NarrationValidation.NormalizeSpeed(requested: 1.5, settingValue: "1.0"));
        Assert.Equal(1.0, NarrationValidation.NormalizeSpeed(requested: null, settingValue: "1.0"));
        Assert.Equal(1.0, NarrationValidation.NormalizeSpeed(requested: null, settingValue: null));
        Assert.Equal(1.0, NarrationValidation.NormalizeSpeed(requested: null, settingValue: "not a number"));
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(2.0, true)]
    [InlineData(1.25, true)]
    [InlineData(0.49, false)]
    [InlineData(2.01, false)]
    [InlineData(double.NaN, false)]
    public void ValidatesSpeedRange(double speed, bool ok)
    {
        Assert.Equal(ok, NarrationValidation.IsSpeedValid(speed));
    }

    [Fact]
    public void NormalizesVoiceFromRequestOverSetting()
    {
        Assert.Equal("af_bella", NarrationValidation.NormalizeVoice("af_bella", "am_michael"));
        Assert.Equal("am_michael", NarrationValidation.NormalizeVoice(null, "am_michael"));
        Assert.Null(NarrationValidation.NormalizeVoice(null, null));
        Assert.Null(NarrationValidation.NormalizeVoice("   ", null));
    }

    [Fact]
    public void NormalizesKokoroBaseUrl()
    {
        Assert.Equal("http://localhost:8880",
            NarrationValidation.NormalizeBaseUrl("http://localhost:8880/"));
        Assert.Equal("https://kokoro.local",
            NarrationValidation.NormalizeBaseUrl("https://kokoro.local"));
    }

    [Theory]
    [InlineData("http://localhost:8880", true)]
    [InlineData("https://kokoro.local:8443", true)]
    [InlineData("ftp://nope", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void ValidatesKokoroBaseUrl(string url, bool ok)
    {
        Assert.Equal(ok, NarrationValidation.IsBaseUrlValid(url));
    }

    [Fact]
    public void ValidatesTextLength()
    {
        Assert.True(NarrationValidation.IsTextLengthOk("short"));
        Assert.True(NarrationValidation.IsTextLengthOk(new string('x', 8000)));
        Assert.False(NarrationValidation.IsTextLengthOk(new string('x', 8001)));
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter "FullyQualifiedName~NarrationValidationTests"`
Expected: compilation fails because `NarrationValidation` doesn't exist.

- [ ] **Step 3: Implement the validators**

Create `src/Fabulis.Server/Data/NarrationValidation.cs`:

```csharp
using System.Globalization;

namespace Fabulis.Server.Data;

public static class NarrationValidation
{
    public const int MaxTextLength = 8000;
    public const double MinSpeed = 0.5;
    public const double MaxSpeed = 2.0;

    public static double NormalizeSpeed(double? requested, string? settingValue)
    {
        if (requested is { } r) return r;
        if (settingValue is not null &&
            double.TryParse(settingValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 1.0;
    }

    public static bool IsSpeedValid(double speed) =>
        !double.IsNaN(speed) && speed >= MinSpeed && speed <= MaxSpeed;

    public static string? NormalizeVoice(string? requested, string? settingValue)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested.Trim();
        if (!string.IsNullOrWhiteSpace(settingValue)) return settingValue.Trim();
        return null;
    }

    public static string NormalizeBaseUrl(string url) => url.TrimEnd('/');

    public static bool IsBaseUrlValid(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        return parsed.Scheme is "http" or "https";
    }

    public static bool IsTextLengthOk(string text) => text.Length <= MaxTextLength;
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj --filter "FullyQualifiedName~NarrationValidationTests"`
Expected: all `NarrationValidationTests` pass.

- [ ] **Step 5: Commit**

```bash
git add tests/Fabulis.Server.Tests/NarrationValidationTests.cs \
        src/Fabulis.Server/Data/NarrationValidation.cs
git commit -m "Add NarrationValidation helpers"
```

---

## Task 5: DTOs for narration and extended settings

**Files:**
- Modify: `src/Fabulis.Server/Api/Dtos.cs`

- [ ] **Step 1: Extend the DTOs file**

Open `src/Fabulis.Server/Api/Dtos.cs`. Replace the `settings` section that currently reads:

```csharp
// ---------- settings ----------
public sealed record SettingsDto(
    bool ApiKeyIsSet,
    string? AssistantModel,
    string AutoLockSelection); // "1"/"5"/"15"/"30"/"60"/"never"

public sealed record SettingsUpdateRequest(
    string? ApiKey,            // null = leave alone
    string? AssistantModel,    // null = leave alone
    string? AutoLockSelection); // null = leave alone, otherwise one of the legal strings
```

with:

```csharp
// ---------- settings ----------
public sealed record SettingsDto(
    bool ApiKeyIsSet,
    string? AssistantModel,
    string AutoLockSelection, // "1"/"5"/"15"/"30"/"60"/"never"
    bool KokoroBaseUrlIsSet,
    string? NarrationVoice,
    double NarrationSpeed,
    bool NarrationAvailable);

public sealed record SettingsUpdateRequest(
    string? ApiKey,             // null = leave alone
    string? AssistantModel,     // null = leave alone
    string? AutoLockSelection,  // null = leave alone, otherwise one of the legal strings
    string? KokoroBaseUrl,      // null = leave alone; empty string = clear
    string? NarrationVoice,     // null = leave alone
    double? NarrationSpeed);    // null = leave alone

// ---------- narration ----------
public sealed record NarrationVoiceDto(string Id, string DisplayName, string Language);
public sealed record VoicesResponse(IReadOnlyList<NarrationVoiceDto> Voices);
public sealed record SynthesizeRequest(string Text, string? Voice, double? Speed);
```

- [ ] **Step 2: Verify the project still builds**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds (the existing `SettingsEndpoints` code will compile because record constructors that gain parameters break call sites — but the only call site is inside `SettingsEndpoints.cs`, which we update in Task 6 before this matters).

If you get errors here from `SettingsEndpoints.cs` not knowing about the new params, that's expected — proceed to Task 6 to fix it. Otherwise:

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Api/Dtos.cs
git commit -m "Extend Settings DTOs and add narration DTOs"
```

> **Note:** This commit may leave the build red if `SettingsEndpoints` references the old constructor positions. We fix that in Task 6 next, so the working tree is restored to green before any merge.

---

## Task 6: Extend SettingsEndpoints with Kokoro fields

**Files:**
- Modify: `src/Fabulis.Server/Api/SettingsEndpoints.cs`

- [ ] **Step 1: Rewrite the file**

Replace the entire contents of `src/Fabulis.Server/Api/SettingsEndpoints.cs` with:

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class SettingsEndpoints
{
    private static readonly HashSet<string> LegalAutoLock =
        new(StringComparer.OrdinalIgnoreCase) { "1", "5", "15", "30", "60", "never" };

    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/settings").RequireSession();

        group.MapGet("", async (FabulisDbContext db, KokoroService kokoro, CancellationToken ct) =>
        {
            var apiKey = await db.AppSettings.FindAsync(["OpenRouterApiKey"], ct);
            var assistantModel = await db.AppSettings.FindAsync(["AssistantModel"], ct);
            var autoLock = await db.AppSettings.FindAsync(["AutoLockMinutes"], ct);
            var kokoroUrl = await db.AppSettings.FindAsync(["KokoroBaseUrl"], ct);
            var narrationVoice = await db.AppSettings.FindAsync(["NarrationVoice"], ct);
            var narrationSpeed = await db.AppSettings.FindAsync(["NarrationSpeed"], ct);

            var dto = new SettingsDto(
                ApiKeyIsSet: apiKey is not null && !string.IsNullOrEmpty(apiKey.Value),
                AssistantModel: assistantModel?.Value,
                AutoLockSelection: NormalizeAutoLock(autoLock?.Value),
                KokoroBaseUrlIsSet: kokoroUrl is not null && !string.IsNullOrWhiteSpace(kokoroUrl.Value),
                NarrationVoice: narrationVoice?.Value,
                NarrationSpeed: NarrationValidation.NormalizeSpeed(null, narrationSpeed?.Value),
                NarrationAvailable: await kokoro.ProbeAsync(ct));

            return Results.Ok(dto);
        });

        group.MapPut("", async (
            SettingsUpdateRequest body,
            FabulisDbContext db,
            VaultService vault,
            KokoroService kokoro) =>
        {
            if (body.ApiKey is { } apiKey && !string.IsNullOrWhiteSpace(apiKey))
                await UpsertAsync(db, "OpenRouterApiKey", apiKey.Trim());

            if (body.AssistantModel is { } model && !string.IsNullOrWhiteSpace(model))
                await UpsertAsync(db, "AssistantModel", model.Trim());

            if (body.AutoLockSelection is { } autoLock)
            {
                if (!LegalAutoLock.Contains(autoLock))
                    return Results.BadRequest(new { error = "autoLockSelection must be one of 1, 5, 15, 30, 60, or never" });

                await UpsertAsync(db, "AutoLockMinutes", autoLock);
                vault.ConfigureAutoLock(autoLock.Equals("never", StringComparison.OrdinalIgnoreCase) ? null : int.Parse(autoLock));
            }

            if (body.KokoroBaseUrl is { } urlInput)
            {
                var trimmed = urlInput.Trim();
                if (trimmed.Length == 0)
                {
                    await UpsertAsync(db, "KokoroBaseUrl", "");
                }
                else
                {
                    if (!NarrationValidation.IsBaseUrlValid(trimmed))
                        return Results.BadRequest(new { error = "kokoroBaseUrl must be a valid http(s) URL" });
                    await UpsertAsync(db, "KokoroBaseUrl", NarrationValidation.NormalizeBaseUrl(trimmed));
                }
                kokoro.InvalidateCaches();
            }

            if (body.NarrationVoice is { } voice && !string.IsNullOrWhiteSpace(voice))
                await UpsertAsync(db, "NarrationVoice", voice.Trim());

            if (body.NarrationSpeed is { } speed)
            {
                if (!NarrationValidation.IsSpeedValid(speed))
                    return Results.BadRequest(new { error = $"narrationSpeed must be between {NarrationValidation.MinSpeed} and {NarrationValidation.MaxSpeed}" });
                await UpsertAsync(db, "NarrationSpeed", speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }

    private static async Task UpsertAsync(FabulisDbContext db, string key, string value)
    {
        var existing = await db.AppSettings.FindAsync(key);
        if (existing is not null)
            existing.Value = value;
        else
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
    }

    private static string NormalizeAutoLock(string? raw)
    {
        if (string.Equals(raw, "never", StringComparison.OrdinalIgnoreCase))
            return "never";
        if (int.TryParse(raw, out var parsed) && LegalAutoLock.Contains(parsed.ToString()))
            return parsed.ToString();
        return "15";
    }
}
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds. If you get a missing-`KokoroService` symbol error from DI, ignore for now — Task 8 wires DI in `Program.cs`. The file should at least compile.

- [ ] **Step 3: Run the existing tests to confirm nothing regressed**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: same number of passing tests as before this task; no failures introduced.

- [ ] **Step 4: Commit**

```bash
git add src/Fabulis.Server/Api/SettingsEndpoints.cs
git commit -m "Extend SettingsEndpoints with Kokoro URL, voice, speed, availability"
```

---

## Task 7: NarrationEndpoints

**Files:**
- Create: `src/Fabulis.Server/Api/NarrationEndpoints.cs`

- [ ] **Step 1: Create the endpoints**

Create `src/Fabulis.Server/Api/NarrationEndpoints.cs`:

```csharp
using Fabulis.Server.Auth;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Api;

public static class NarrationEndpoints
{
    public static IEndpointRouteBuilder MapNarrationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/narration").RequireSession();

        group.MapGet("voices", async (KokoroService kokoro, CancellationToken ct) =>
        {
            try
            {
                var voices = await kokoro.ListVoicesAsync(ct);
                var dto = new VoicesResponse(
                    voices.Select(v => new NarrationVoiceDto(v.Id, v.DisplayName, v.Language))
                          .ToList());
                return Results.Ok(dto);
            }
            catch (KokoroUnavailableException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapPost("synthesize", async (
            SynthesizeRequest body,
            FabulisDbContext db,
            KokoroService kokoro,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { error = "text is required" });
            if (!NarrationValidation.IsTextLengthOk(body.Text))
                return Results.BadRequest(new { error = $"text exceeds {NarrationValidation.MaxTextLength} characters" });

            var stripped = MarkdownStripper.ToPlainText(body.Text);
            if (string.IsNullOrWhiteSpace(stripped))
                return Results.BadRequest(new { error = "text has no readable content after markdown stripping" });

            var voiceSetting = await db.AppSettings.FindAsync(["NarrationVoice"], ct);
            var speedSetting = await db.AppSettings.FindAsync(["NarrationSpeed"], ct);

            var voice = NarrationValidation.NormalizeVoice(body.Voice, voiceSetting?.Value);
            if (voice is null)
                return Results.BadRequest(new { error = "no voice configured" });

            var speed = NarrationValidation.NormalizeSpeed(body.Speed, speedSetting?.Value);
            if (!NarrationValidation.IsSpeedValid(speed))
                return Results.BadRequest(new { error = $"speed must be between {NarrationValidation.MinSpeed} and {NarrationValidation.MaxSpeed}" });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            try
            {
                var bytes = await kokoro.SynthesizeAsync(stripped, voice, speed, cts.Token);
                return Results.File(bytes, contentType: "audio/mpeg");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
            }
            catch (KokoroUnavailableException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            catch (KokoroUpstreamException)
            {
                return Results.StatusCode(StatusCodes.Status502BadGateway);
            }
        });

        return routes;
    }
}
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build Fabulis.slnx`
Expected: builds without errors.

- [ ] **Step 3: Commit**

```bash
git add src/Fabulis.Server/Api/NarrationEndpoints.cs
git commit -m "Add NarrationEndpoints (voices and synthesize)"
```

---

## Task 8: Wire DI and map endpoints in Program.cs

**Files:**
- Modify: `src/Fabulis.Server/Program.cs`

- [ ] **Step 1: Register KokoroService, the named HttpClient, and map the endpoint group**

`KokoroService` is **Singleton** even though `OpenRouterService` is Scoped. Reason: the voices and probe caches live on the instance, so a per-request lifetime would defeat them. The per-call DB lookup uses `IServiceProvider.CreateScope()` internally (already done in Task 3's `GetBaseUrlFromDbAsync`), so a singleton can still read scoped state safely. This is a deliberate, documented deviation from the OpenRouterService precedent.

Replace the entire contents of `src/Fabulis.Server/Program.cs` with:

```csharp
using Fabulis.Server.Api;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Fabulis.Server.Auth.SessionTokenStore>();
builder.Services.AddSingleton<VaultService>();
builder.Services.AddHostedService<AutoLockService>();
builder.Services.AddDbContext<FabulisDbContext>((sp, options) =>
{
    var vault = sp.GetRequiredService<VaultService>();
    if (vault.IsUnlocked)
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "fabulis.db");
        options.UseSqlite($"Data Source={dbPath};Password={vault.Password}");
    }
});

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("kokoro", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddScoped<OpenRouterService>();
builder.Services.AddSingleton<KokoroService>();
builder.Services.AddScoped<DraftService>();
builder.Services.AddSingleton<GenerationManager>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    if (path is null || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }
    var vault = context.RequestServices.GetRequiredService<VaultService>();
    vault.RecordActivity();
    await next();
});

var api = app.MapGroup("/api/v1").DisableAntiforgery();
api.MapAuthEndpoints();
api.MapLibraryEndpoints();
api.MapStoryEndpoints();
api.MapSettingsEndpoints();
api.MapStorytellerEndpoints();
api.MapDraftEndpoints();
api.MapModelEndpoints();
api.MapNarrationEndpoints();

app.Run();
```

> **Spec drift to update:** the spec's "Server" section says `KokoroService` is Scoped. Implementing this task as Singleton (above) is correct; update the spec line to read **Singleton (caches live on the instance)** as a follow-up doc edit when this lands.

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build Fabulis.slnx`
Expected: build succeeds.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 4: Manual smoke test the server (do this before moving on to the client)**

Start the server:

```bash
dotnet run --project src/Fabulis.Server
```

Then in another terminal:

```bash
# Unlock (replace with your vault password)
TOKEN=$(curl -s -X POST http://localhost:5288/api/v1/auth/unlock \
  -H 'Content-Type: application/json' \
  -d '{"password":"YOUR_VAULT_PASSWORD"}' | jq -r .token)

# GET settings — should now include kokoroBaseUrlIsSet, narrationVoice, narrationSpeed, narrationAvailable
curl -s http://localhost:5288/api/v1/settings -H "Authorization: Bearer $TOKEN" | jq .

# PUT Kokoro URL (replace with your Kokoro server URL; default port is 8880)
curl -s -X PUT http://localhost:5288/api/v1/settings \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"kokoroBaseUrl":"http://localhost:8880"}'

# GET voices
curl -s http://localhost:5288/api/v1/narration/voices -H "Authorization: Bearer $TOKEN" | jq .

# Synthesize and play (macOS afplay)
curl -s -X POST http://localhost:5288/api/v1/narration/synthesize \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"text":"Hello from Fabulis.","voice":"af_bella","speed":1.0}' \
  -o /tmp/test.mp3 && afplay /tmp/test.mp3

# Set the default voice + speed
curl -s -X PUT http://localhost:5288/api/v1/settings \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"narrationVoice":"af_bella","narrationSpeed":1.0}'

# Synthesize without explicit voice/speed (uses defaults from settings)
curl -s -X POST http://localhost:5288/api/v1/narration/synthesize \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"text":"This uses defaults."}' \
  -o /tmp/test2.mp3 && afplay /tmp/test2.mp3
```

Expected outcomes:
- `GET /settings` includes the new fields with sensible defaults (`kokoroBaseUrlIsSet: false`, `narrationSpeed: 1.0`, `narrationAvailable: false`) on first run.
- After setting the Kokoro URL, `narrationAvailable: true` and `GET /narration/voices` returns voices.
- Both synthesize calls return MP3 bytes you can hear.
- A request with `"text":"```\ncode\n```"` returns 400 (`text has no readable content after markdown stripping`).
- A request with `"speed":3.0` returns 400.

- [ ] **Step 5: Commit**

```bash
git add src/Fabulis.Server/Program.cs
git commit -m "Wire KokoroService DI and map NarrationEndpoints"
```

---

## Task 9: Client DTOs

**Files:**
- Modify: `client/Fabulis/Models/APIDtos.swift`

- [ ] **Step 1: Replace the `SettingsDto` struct and add narration DTOs**

Open `client/Fabulis/Models/APIDtos.swift`. Find the `SettingsDto` struct, which currently reads:

```swift
struct SettingsDto: Decodable, Sendable {
    let apiKeyIsSet: Bool
    let assistantModel: String?
    let autoLockSelection: String
}
```

Replace it with:

```swift
struct SettingsDto: Decodable, Sendable {
    let apiKeyIsSet: Bool
    let assistantModel: String?
    let autoLockSelection: String
    let kokoroBaseUrlIsSet: Bool
    let narrationVoice: String?
    let narrationSpeed: Double
    let narrationAvailable: Bool
}

struct NarrationVoice: Decodable, Identifiable, Sendable {
    let id: String
    let displayName: String
    let language: String
}

struct VoicesResponse: Decodable, Sendable {
    let voices: [NarrationVoice]
}
```

- [ ] **Step 2: Verify the client compiles**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected: `** BUILD SUCCEEDED **`. Any callers of `SettingsDto`'s synthesised init become broken at this point — that's fine because the only synthesised-init call sites are in `SettingsView`'s preview-style code (if any) and the API path uses `Decodable`, which doesn't rely on positional init. If there's a real call site breakage, fix the call site to provide the new fields with sensible defaults and re-run.

- [ ] **Step 3: Commit**

```bash
git add client/Fabulis/Models/APIDtos.swift
git commit -m "Add narration fields to SettingsDto and NarrationVoice DTOs"
```

---

## Task 10: Client API methods (voices, synthesize, settings PUT)

**Files:**
- Modify: `client/Fabulis/Services/FabulisAPIClient.swift`

- [ ] **Step 1: Find the existing settings helper and the request helpers**

Open `client/Fabulis/Services/FabulisAPIClient.swift`. Locate (use grep if needed):
- The `settings()` method that calls `/settings`.
- The `updateSettings(...)` method, if it exists (look for any function that PUTs `/settings`).
- The private `request<T>(...)` / `requestVoid(...)` helpers.

- [ ] **Step 2: Add a raw-bytes request helper**

Just above the existing `request<T>` private helper, add this method (which lives on the actor, so it can use the same `buildRequest` / `transport` plumbing):

```swift
// Returns raw response bytes for endpoints whose payload is not JSON
// (e.g. /narration/synthesize returns audio/mpeg).
private func requestBytes(
    _ method: String,
    path: String,
    body: (some Encodable)? = Optional<Empty>.none,
    authed: Bool
) async throws -> Data {
    let req = try await buildRequest(method: method, path: path, body: body, authed: authed)
    let (data, response) = try await transport(req)
    try validate(response: response, data: data)
    return data
}

private struct Empty: Encodable {}
```

> If `Empty` already exists in this file as a private nested struct, reuse it instead of declaring a new one — duplicate declarations will fail to compile.

- [ ] **Step 3: Add narration API methods at the bottom of the actor**

Just before the final closing `}` of the `FabulisAPIClient` actor, add:

```swift
// MARK: - Narration

func narrationVoices() async throws -> [NarrationVoice] {
    let resp: VoicesResponse = try await request("GET", path: "/narration/voices", authed: true)
    return resp.voices
}

func synthesize(text: String, voice: String?, speed: Double?) async throws -> Data {
    struct Body: Encodable {
        let text: String
        let voice: String?
        let speed: Double?
    }
    return try await requestBytes(
        "POST",
        path: "/narration/synthesize",
        body: Body(text: text, voice: voice, speed: speed),
        authed: true)
}
```

- [ ] **Step 4: Replace / add the updateSettings method**

Find the existing `updateSettings` (or the place where settings are PUT — look for the call to `/settings` with PUT). Replace its body / signature with one that accepts all the optional fields including the new narration ones:

```swift
func updateSettings(
    apiKey: String? = nil,
    assistantModel: String? = nil,
    autoLockSelection: String? = nil,
    kokoroBaseUrl: String? = nil,
    narrationVoice: String? = nil,
    narrationSpeed: Double? = nil
) async throws {
    struct Body: Encodable {
        let apiKey: String?
        let assistantModel: String?
        let autoLockSelection: String?
        let kokoroBaseUrl: String?
        let narrationVoice: String?
        let narrationSpeed: Double?
    }
    try await requestVoid(
        "PUT",
        path: "/settings",
        body: Body(
            apiKey: apiKey,
            assistantModel: assistantModel,
            autoLockSelection: autoLockSelection,
            kokoroBaseUrl: kokoroBaseUrl,
            narrationVoice: narrationVoice,
            narrationSpeed: narrationSpeed),
        authed: true)
}
```

If existing call sites (in `SettingsView`) pass positional args, they'll keep working since they used named labels for the original parameters. If anything breaks, update call sites to use the named labels with the original argument values.

- [ ] **Step 5: Verify the client compiles**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected: `** BUILD SUCCEEDED **`. Fix any call-site mismatches and re-run.

- [ ] **Step 6: Commit**

```bash
git add client/Fabulis/Services/FabulisAPIClient.swift
git commit -m "Add narration API methods and extended updateSettings"
```

---

## Task 11: NarrationPlayer

**Files:**
- Create: `client/Fabulis/Services/NarrationPlayer.swift`

- [ ] **Step 1: Create the player**

Create `client/Fabulis/Services/NarrationPlayer.swift`:

```swift
import AVFoundation
import Foundation
import Observation

/// Per-view audio player that synthesises and plays the response
/// bubbles in a story or draft. One bubble plays at a time; the
/// next bubble's audio is prefetched while the current one plays.
@MainActor
@Observable
final class NarrationPlayer: NSObject, AVAudioPlayerDelegate {

    enum State: Equatable {
        case idle
        case preparing(bubbleId: Int)
        case playing(bubbleId: Int)
        case paused(bubbleId: Int)
    }

    private(set) var state: State = .idle
    private(set) var currentTime: TimeInterval = 0
    private(set) var duration: TimeInterval = 0
    private(set) var lastError: String?

    /// Absolute 1-based index of the active bubble in the list passed
    /// to `start(...)`. nil when idle. Always references the originally
    /// supplied list, not a slice.
    var currentBubbleIndex: Int? {
        guard let id = currentBubbleId, let i = bubbles.firstIndex(where: { $0.id == id })
        else { return nil }
        return i + 1
    }
    var totalBubbles: Int { bubbles.count }
    var currentBubbleId: Int? {
        switch state {
        case .idle: return nil
        case .preparing(let id), .playing(let id), .paused(let id): return id
        }
    }

    private var bubbles: [(id: Int, text: String)] = []
    private var player: AVAudioPlayer?
    private var tempFileURL: URL?
    private var synthesisTask: Task<Void, Never>?
    private var prefetchTask: Task<Void, Never>?
    private var prefetch: (bubbleId: Int, data: Data)?
    private var timer: Timer?

    deinit {
        synthesisTask?.cancel()
        prefetchTask?.cancel()
        timer?.invalidate()
        if let url = tempFileURL { try? FileManager.default.removeItem(at: url) }
    }

    // MARK: - Public API

    func start(bubbles: [(id: Int, text: String)], from bubbleId: Int) {
        stop()
        self.bubbles = bubbles
        guard bubbles.contains(where: { $0.id == bubbleId }) else { return }
        configureAudioSession()
        loadAndPlay(bubbleId: bubbleId)
    }

    func togglePlayPause() {
        switch state {
        case .playing(let id):
            player?.pause()
            state = .paused(bubbleId: id)
            stopTimer()
        case .paused(let id):
            player?.play()
            state = .playing(bubbleId: id)
            startTimer()
        case .idle, .preparing:
            return
        }
    }

    func seek(by delta: TimeInterval) {
        guard let player else { return }
        let target = (player.currentTime + delta).clamped(to: 0...max(player.duration, 0))
        player.currentTime = target
        currentTime = target
    }

    func jumpTo(bubbleId: Int) {
        guard bubbles.contains(where: { $0.id == bubbleId }) else { return }
        cancelInFlight()
        loadAndPlay(bubbleId: bubbleId)
    }

    func stop() {
        cancelInFlight()
        player?.stop()
        player = nil
        if let url = tempFileURL { try? FileManager.default.removeItem(at: url) }
        tempFileURL = nil
        prefetch = nil
        currentTime = 0
        duration = 0
        state = .idle
        stopTimer()
        // Leave the audio session category set — flipping it on/off
        // around each session causes audible glitches and the category
        // is benign at .playback.
    }

    // MARK: - Internals

    private func cancelInFlight() {
        synthesisTask?.cancel()
        synthesisTask = nil
        prefetchTask?.cancel()
        prefetchTask = nil
        prefetch = nil
        lastError = nil
    }

    private func configureAudioSession() {
        #if canImport(UIKit)
        do {
            try AVAudioSession.sharedInstance().setCategory(.playback, mode: .spokenAudio)
            try AVAudioSession.sharedInstance().setActive(true)
        } catch {
            // Non-fatal — playback still works, just not ideally
            // (e.g. silent switch may mute on iOS).
        }
        #endif
    }

    private func loadAndPlay(bubbleId: Int) {
        state = .preparing(bubbleId: bubbleId)
        let text = bubbles.first(where: { $0.id == bubbleId })?.text ?? ""

        if let cached = prefetch, cached.bubbleId == bubbleId {
            prefetch = nil
            playData(cached.data, bubbleId: bubbleId)
            kickPrefetch(after: bubbleId)
            return
        }

        synthesisTask = Task { [weak self] in
            do {
                let data = try await FabulisAPIClient.shared.synthesize(text: text, voice: nil, speed: nil)
                try Task.checkCancellation()
                await MainActor.run {
                    self?.playData(data, bubbleId: bubbleId)
                    self?.kickPrefetch(after: bubbleId)
                }
            } catch is CancellationError {
                // Swallowed by caller.
            } catch {
                await MainActor.run {
                    self?.lastError = error.localizedDescription
                    self?.stop()
                }
            }
        }
    }

    private func playData(_ data: Data, bubbleId: Int) {
        do {
            let url = FileManager.default.temporaryDirectory
                .appendingPathComponent("narration-\(UUID().uuidString).mp3")
            try data.write(to: url, options: .atomic)
            if let old = tempFileURL { try? FileManager.default.removeItem(at: old) }
            tempFileURL = url

            let player = try AVAudioPlayer(contentsOf: url)
            player.delegate = self
            player.prepareToPlay()
            self.player = player
            duration = player.duration
            currentTime = 0
            state = .playing(bubbleId: bubbleId)
            player.play()
            startTimer()
        } catch {
            lastError = error.localizedDescription
            stop()
        }
    }

    private func kickPrefetch(after bubbleId: Int) {
        guard let idx = bubbles.firstIndex(where: { $0.id == bubbleId }), idx + 1 < bubbles.count
        else { return }
        let next = bubbles[idx + 1]
        prefetchTask = Task { [weak self] in
            do {
                let data = try await FabulisAPIClient.shared.synthesize(text: next.text, voice: nil, speed: nil)
                try Task.checkCancellation()
                await MainActor.run {
                    self?.prefetch = (bubbleId: next.id, data: data)
                }
            } catch {
                // Prefetch failures are silent; on natural advance the live
                // synthesis will re-fetch and surface the error then.
            }
        }
    }

    private func advanceToNext() {
        guard let id = currentBubbleId,
              let idx = bubbles.firstIndex(where: { $0.id == id }),
              idx + 1 < bubbles.count
        else { stop(); return }
        let next = bubbles[idx + 1]
        loadAndPlay(bubbleId: next.id)
    }

    private func startTimer() {
        stopTimer()
        timer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                if case .playing = self.state, let p = self.player {
                    self.currentTime = p.currentTime
                }
            }
        }
        if let timer { RunLoop.main.add(timer, forMode: .common) }
    }

    private func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    // MARK: - AVAudioPlayerDelegate

    nonisolated func audioPlayerDidFinishPlaying(_ player: AVAudioPlayer, successfully flag: Bool) {
        Task { @MainActor in
            self.advanceToNext()
        }
    }

    nonisolated func audioPlayerDecodeErrorDidOccur(_ player: AVAudioPlayer, error: Error?) {
        Task { @MainActor in
            self.lastError = error?.localizedDescription ?? "Audio decode failed"
            self.stop()
        }
    }
}

private extension Comparable {
    func clamped(to range: ClosedRange<Self>) -> Self {
        min(max(self, range.lowerBound), range.upperBound)
    }
}
```

- [ ] **Step 2: Add the file to the Xcode target**

Open `client/Fabulis.xcodeproj` in Xcode, drag `client/Fabulis/Services/NarrationPlayer.swift` into the project navigator under `Services/`, and make sure it's checked into the `Fabulis` target in the file inspector. (Equivalent: edit `project.pbxproj` manually with the standard `PBXFileReference` / `PBXBuildFile` entries — but doing it in Xcode is faster and less error-prone.)

- [ ] **Step 3: Verify the client compiles**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -30
```

Expected: `** BUILD SUCCEEDED **`. If Xcode complains about `import AVFoundation` not being available under Mac Catalyst, confirm by checking the build for the Catalyst destination too (`-destination 'platform=macOS,variant=Mac Catalyst'`). AVFoundation is available on both; this should just work.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Services/NarrationPlayer.swift client/Fabulis.xcodeproj/project.pbxproj
git commit -m "Add NarrationPlayer (AVAudioPlayer-backed, per-bubble prefetch)"
```

---

## Task 12: NarrationBar UI component

**Files:**
- Create: `client/Fabulis/Views/Narration/NarrationBar.swift`

- [ ] **Step 1: Create the bar**

Create the directory and file `client/Fabulis/Views/Narration/NarrationBar.swift`:

```swift
import SwiftUI

struct NarrationBar: View {
    let player: NarrationPlayer

    var body: some View {
        HStack(spacing: 16) {
            Button { player.seek(by: -10) } label: {
                Image(systemName: "gobackward.10")
            }
            .disabled(isPreparingOrIdle)

            Button { player.togglePlayPause() } label: {
                if case .preparing = player.state {
                    ProgressView().controlSize(.small)
                } else {
                    Image(systemName: isPlaying ? "pause.fill" : "play.fill")
                }
            }
            .disabled(isPreparingOrIdle && !isPlayable)

            Button { player.seek(by: 10) } label: {
                Image(systemName: "goforward.10")
            }
            .disabled(isPreparingOrIdle)

            Spacer()

            VStack(alignment: .trailing, spacing: 2) {
                if let idx = player.currentBubbleIndex {
                    Text("Bubble \(idx) / \(player.totalBubbles)")
                        .font(.caption.weight(.medium))
                }
                Text("\(format(player.currentTime)) / \(format(player.duration))")
                    .font(.caption2.monospacedDigit())
                    .foregroundStyle(.secondary)
            }

            Button { player.stop() } label: {
                Image(systemName: "xmark.circle.fill")
                    .foregroundStyle(.secondary)
            }
            .accessibilityLabel("Stop narration")
        }
        .font(.title3)
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.bar)
    }

    private var isPlaying: Bool {
        if case .playing = player.state { return true }
        return false
    }

    private var isPreparingOrIdle: Bool {
        switch player.state {
        case .preparing, .idle: return true
        default: return false
        }
    }

    private var isPlayable: Bool {
        switch player.state {
        case .playing, .paused: return true
        default: return false
        }
    }

    private func format(_ t: TimeInterval) -> String {
        guard t.isFinite, t >= 0 else { return "0:00" }
        let total = Int(t.rounded())
        return String(format: "%d:%02d", total / 60, total % 60)
    }
}
```

- [ ] **Step 2: Add the file to the Xcode target**

In Xcode, drag `client/Fabulis/Views/Narration/NarrationBar.swift` into the project navigator under a new `Narration/` group inside `Views/`, with the `Fabulis` target checked.

- [ ] **Step 3: Verify the client compiles**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected: `** BUILD SUCCEEDED **`.

- [ ] **Step 4: Commit**

```bash
git add client/Fabulis/Views/Narration/NarrationBar.swift client/Fabulis.xcodeproj/project.pbxproj
git commit -m "Add NarrationBar SwiftUI component"
```

---

## Task 13: NarrationVoicePickerView and Settings UI

**Files:**
- Create: `client/Fabulis/Views/Settings/NarrationVoicePickerView.swift`
- Modify: `client/Fabulis/Views/Settings/SettingsView.swift`

- [ ] **Step 1: Create the voice picker view**

Create `client/Fabulis/Views/Settings/NarrationVoicePickerView.swift`:

```swift
import SwiftUI

struct NarrationVoicePickerView: View {
    let currentVoice: String?
    let onPicked: (String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var voices: [NarrationVoice] = []
    @State private var isLoading = true
    @State private var errorMessage: String?

    private var grouped: [(String, [NarrationVoice])] {
        let groups = Dictionary(grouping: voices, by: \.language)
        return groups.keys.sorted().map { ($0, groups[$0]!.sorted { $0.displayName < $1.displayName }) }
    }

    var body: some View {
        Group {
            if isLoading {
                ProgressView()
            } else if let errorMessage {
                ContentUnavailableView("Couldn't load voices", systemImage: "exclamationmark.triangle",
                    description: Text(errorMessage))
            } else {
                List {
                    ForEach(grouped, id: \.0) { language, items in
                        Section(language) {
                            ForEach(items) { voice in
                                Button {
                                    onPicked(voice.id)
                                    dismiss()
                                } label: {
                                    HStack {
                                        Text(voice.displayName)
                                        Spacer()
                                        if voice.id == currentVoice {
                                            Image(systemName: "checkmark").foregroundStyle(.accent)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        .navigationTitle("Voice")
        .task { await load() }
    }

    private func load() async {
        do {
            voices = try await FabulisAPIClient.shared.narrationVoices()
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}
```

- [ ] **Step 2: Update SettingsView**

Open `client/Fabulis/Views/Settings/SettingsView.swift`. Add these `@State`s alongside the existing ones at the top of the struct:

```swift
@State private var kokoroUrlDraft: String = ""
@State private var isSavingKokoroUrl = false
@State private var kokoroUrlJustSaved = false
@State private var speedDraft: Double = 1.0
```

Inside `body`, add a new "Narration" `Section` between the existing "Assistant model" and "Storyteller" sections:

```swift
Section("Narration") {
    if let settings, settings.kokoroBaseUrlIsSet {
        Text("Server URL is set").foregroundStyle(.secondary)
    }
    TextField("http://localhost:8880", text: $kokoroUrlDraft)
        .textInputAutocapitalization(.never)
        .autocorrectionDisabled(true)
        .keyboardType(.URL)
    Button {
        Task { await saveKokoroUrl() }
    } label: {
        HStack {
            if isSavingKokoroUrl { ProgressView().controlSize(.mini) }
            Text("Save URL")
        }
    }
    .disabled(kokoroUrlDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || isSavingKokoroUrl)
    if kokoroUrlJustSaved {
        Text("Saved.").font(.caption).foregroundStyle(.green)
    }

    NavigationLink {
        NarrationVoicePickerView(currentVoice: settings?.narrationVoice) { picked in
            Task { await saveVoice(picked) }
        }
    } label: {
        HStack {
            Text("Voice")
            Spacer()
            Text(settings?.narrationVoice ?? "Not set").foregroundStyle(.secondary)
        }
    }
    .disabled(settings?.kokoroBaseUrlIsSet != true)

    HStack {
        Text("Speed")
        Spacer()
        Text(String(format: "%.2f×", speedDraft))
            .monospacedDigit().foregroundStyle(.secondary)
    }
    Slider(
        value: $speedDraft, in: 0.5...2.0, step: 0.25,
        onEditingChanged: { editing in
            if !editing { Task { await saveSpeed(speedDraft) } }
        }
    )

    if let settings, settings.kokoroBaseUrlIsSet, !settings.narrationAvailable {
        Text("Narration server unreachable.")
            .font(.caption).foregroundStyle(.orange)
    }
}
```

Then add the three save handlers at the bottom of the `SettingsView` struct (next to the existing `saveApiKey`, `saveModel`, `saveAutoLock`):

```swift
private func saveKokoroUrl() async {
    let trimmed = kokoroUrlDraft.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !trimmed.isEmpty else { return }
    isSavingKokoroUrl = true
    do {
        try await FabulisAPIClient.shared.updateSettings(kokoroBaseUrl: trimmed)
        settings = try await FabulisAPIClient.shared.settings()
        kokoroUrlDraft = ""
        kokoroUrlJustSaved = true
        Task { try? await Task.sleep(for: .seconds(3)); kokoroUrlJustSaved = false }
    } catch {
        errorMessage = error.localizedDescription
    }
    isSavingKokoroUrl = false
}

private func saveVoice(_ voice: String) async {
    do {
        try await FabulisAPIClient.shared.updateSettings(narrationVoice: voice)
        settings = try await FabulisAPIClient.shared.settings()
    } catch {
        errorMessage = error.localizedDescription
    }
}

private func saveSpeed(_ speed: Double) async {
    do {
        try await FabulisAPIClient.shared.updateSettings(narrationSpeed: speed)
        settings = try await FabulisAPIClient.shared.settings()
    } catch {
        errorMessage = error.localizedDescription
    }
}
```

Finally, in `load()`, after `settings = try await FabulisAPIClient.shared.settings()`, add:

```swift
if let settings { speedDraft = settings.narrationSpeed }
```

- [ ] **Step 3: Add the picker file to the Xcode target**

In Xcode, drag `client/Fabulis/Views/Settings/NarrationVoicePickerView.swift` into the project navigator under `Views/Settings/`, target `Fabulis`.

- [ ] **Step 4: Verify the client compiles**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected: `** BUILD SUCCEEDED **`.

- [ ] **Step 5: Manual smoke test**

1. Run the server (`dotnet run --project src/Fabulis.Server`) and the Kokoro server.
2. Launch the iOS Simulator build of the client, unlock the vault.
3. Open Settings → Narration:
   - Type the Kokoro URL (e.g. `http://localhost:8880`), tap Save URL.
   - "Saved." appears briefly. Voice row becomes enabled.
   - Tap Voice → list of voices loads grouped by language. Pick one.
   - The Voice row updates to show the picked voice id.
   - Drag the speed slider → on release, the setting saves silently. Re-open Settings and confirm the speed persists.
4. Edit the URL to a bogus value (e.g. `http://nope.local:9999`) and Save → eventually `Narration server unreachable.` should appear.
5. Reset to the working URL.

- [ ] **Step 6: Commit**

```bash
git add client/Fabulis/Views/Settings/NarrationVoicePickerView.swift \
        client/Fabulis/Views/Settings/SettingsView.swift \
        client/Fabulis.xcodeproj/project.pbxproj
git commit -m "Add Narration section to Settings (URL, voice, speed)"
```

---

## Task 14: Integrate narration into StoryView

**Files:**
- Modify: `client/Fabulis/Views/Story/StoryMessageView.swift`
- Modify: `client/Fabulis/Views/Story/StoryView.swift`

- [ ] **Step 1: Extend StoryMessageView with playing state + context menu**

Replace the entire contents of `client/Fabulis/Views/Story/StoryMessageView.swift` with:

```swift
import MarkdownUI
import SwiftUI

struct StoryMessageView: View {
    let message: StoryMessage
    var isCurrentlyPlaying: Bool = false
    var narrationAvailable: Bool = false
    var onPlayFromHere: (() -> Void)? = nil

    private var roleLabel: String {
        switch message.role {
        case .prompt: return "Prompt"
        case .response: return "Response"
        }
    }

    private var roleColor: Color {
        switch message.role {
        case .prompt: return .secondary
        case .response: return .accentColor
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(roleLabel.uppercased())
                .font(.caption2.bold())
                .foregroundStyle(roleColor)
            Markdown(message.content)
                .markdownTextStyle { FontSize(.em(1)) }
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(message.role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay {
            if isCurrentlyPlaying {
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(Color.accentColor, lineWidth: 2)
            }
        }
        .contextMenu {
            if narrationAvailable, message.role == .response, let onPlayFromHere {
                Button { onPlayFromHere() } label: {
                    Label("Play from here", systemImage: "play.fill")
                }
            }
        }
    }
}
```

- [ ] **Step 2: Update StoryView to own a player and render the bar**

Replace the entire contents of `client/Fabulis/Views/Story/StoryView.swift` with:

```swift
import SwiftUI

struct StoryView: View {
    let storyId: Int
    let fallbackTitle: String

    @State private var detail: StoryDetail?
    @State private var selectedVersion: Int?
    @State private var versionDetail: StoryVersionDetail?
    @State private var storyError: String?
    @State private var versionError: String?
    @State private var isLoadingStory = true
    @State private var isLoadingVersion = false
    @State private var narrationAvailable = false
    @State private var player = NarrationPlayer()

    var body: some View {
        Group {
            if let detail {
                if detail.versions.isEmpty {
                    ContentUnavailableView("No versions yet", systemImage: "doc.text",
                        description: Text("This story has no saved versions."))
                } else if let versionDetail {
                    ScrollViewReader { proxy in
                        ScrollView {
                            LazyVStack(alignment: .leading, spacing: 12) {
                                ForEach(versionDetail.messages) { message in
                                    StoryMessageView(
                                        message: message,
                                        isCurrentlyPlaying: player.currentBubbleId == message.id,
                                        narrationAvailable: narrationAvailable,
                                        onPlayFromHere: { startNarration(from: message.id) })
                                        .id(message.id)
                                }
                            }
                            .padding()
                        }
                        .onChange(of: player.currentBubbleId) { _, new in
                            if let new {
                                withAnimation { proxy.scrollTo(new, anchor: .center) }
                            }
                        }
                    }
                } else if isLoadingVersion {
                    ProgressView()
                } else if let versionError {
                    errorView("Couldn't load version", versionError) {
                        if let selectedVersion {
                            Task { await loadVersion(selectedVersion) }
                        }
                    }
                }
            } else if isLoadingStory {
                ProgressView()
            } else if let storyError {
                errorView("Couldn't load story", storyError) {
                    Task { await loadStory() }
                }
            }
        }
        .navigationTitle(detail?.title ?? fallbackTitle)
        .toolbar {
            if let detail, !detail.versions.isEmpty, let selectedVersion {
                ToolbarItem(placement: .topBarTrailing) {
                    Menu {
                        ForEach(detail.versions) { version in
                            Button {
                                select(version: version.versionNumber)
                            } label: {
                                if version.versionNumber == selectedVersion {
                                    Label("Version \(version.versionNumber)", systemImage: "checkmark")
                                } else {
                                    Text("Version \(version.versionNumber)")
                                }
                            }
                        }
                    } label: {
                        HStack(spacing: 2) {
                            Text("Version \(selectedVersion)")
                            Image(systemName: "chevron.down").font(.caption2)
                        }
                    }
                }
            }
        }
        .safeAreaInset(edge: .bottom) {
            if player.state != .idle {
                NarrationBar(player: player)
            }
        }
        .task {
            await loadStory()
            await loadNarrationAvailability()
        }
        .refreshable { await loadStory() }
        .onDisappear { player.stop() }
    }

    @ViewBuilder
    private func errorView(_ headline: String, _ message: String, retry: @escaping () -> Void) -> some View {
        VStack(spacing: 12) {
            Text(headline).font(.headline)
            Text(message).font(.caption).foregroundStyle(.secondary)
            Button("Retry", action: retry)
        }
        .padding()
    }

    private func select(version: Int) {
        guard version != selectedVersion else { return }
        player.stop()
        selectedVersion = version
        Task { await loadVersion(version) }
    }

    private func loadStory() async {
        isLoadingStory = true
        storyError = nil
        do {
            let storyDetail = try await FabulisAPIClient.shared.story(id: storyId)
            detail = storyDetail
            let versionNumbers = storyDetail.versions.map(\.versionNumber)
            let target = selectedVersion.flatMap { versionNumbers.contains($0) ? $0 : nil }
                ?? storyDetail.versions.first?.versionNumber
            if let target {
                selectedVersion = target
                await loadVersion(target)
            }
        } catch {
            storyError = error.localizedDescription
        }
        isLoadingStory = false
    }

    private func loadVersion(_ version: Int) async {
        isLoadingVersion = true
        versionError = nil
        versionDetail = nil
        do {
            let result = try await FabulisAPIClient.shared.storyVersion(storyId: storyId, version: version)
            guard version == selectedVersion else { return }
            versionDetail = result
        } catch {
            guard version == selectedVersion else { return }
            versionError = error.localizedDescription
        }
        guard version == selectedVersion else { return }
        isLoadingVersion = false
    }

    private func loadNarrationAvailability() async {
        if let s = try? await FabulisAPIClient.shared.settings() {
            narrationAvailable = s.narrationAvailable
        }
    }

    private func startNarration(from bubbleId: Int) {
        guard let versionDetail else { return }
        let responses = versionDetail.messages
            .filter { $0.role == .response }
            .map { (id: $0.id, text: $0.content) }
        player.start(bubbles: responses, from: bubbleId)
    }
}
```

- [ ] **Step 3: Verify the client compiles**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected: `** BUILD SUCCEEDED **`.

- [ ] **Step 4: Manual smoke test**

1. Open a story with multiple response bubbles in the simulator.
2. Long-press on a response bubble → "Play from here" appears.
3. Tap it. The narration bar slides up, the bubble gets an accent border, and the audio plays.
4. Tap pause → resume → back 10 → forward 10. All behave.
5. Let it play to the end of bubble 1. Bubble 2 begins; border moves; the scroll view centres on it.
6. Long-press a later bubble → Play from here → jumps cleanly. The bubble counter updates to the new absolute position.
7. Tap ✕ on the bar → narration stops, bar disappears.
8. Start narration, then navigate back → audio stops.
9. With Kokoro stopped, open a story → no "Play from here" item in the context menu.

- [ ] **Step 5: Commit**

```bash
git add client/Fabulis/Views/Story/StoryView.swift \
        client/Fabulis/Views/Story/StoryMessageView.swift
git commit -m "Integrate narration into StoryView"
```

---

## Task 15: Integrate narration into DraftView

**Files:**
- Modify: `client/Fabulis/Views/Draft/DraftMessageView.swift`
- Modify: `client/Fabulis/Views/Draft/DraftView.swift`

- [ ] **Step 1: Extend DraftMessageView with playing state**

Replace the entire contents of `client/Fabulis/Views/Draft/DraftMessageView.swift` with:

```swift
import MarkdownUI
import SwiftUI

struct DraftMessageView<Menu: View>: View {
    let role: MessageRole
    let content: String
    let isStreaming: Bool
    let isCurrentlyPlaying: Bool
    let menu: () -> Menu

    init(
        message: DraftMessageDto,
        isCurrentlyPlaying: Bool = false,
        @ViewBuilder menu: @escaping () -> Menu
    ) {
        self.role = message.role
        self.content = message.content
        self.isStreaming = false
        self.isCurrentlyPlaying = isCurrentlyPlaying
        self.menu = menu
    }

    init(streamingResponse content: String, @ViewBuilder menu: @escaping () -> Menu) {
        self.role = .response
        self.content = content
        self.isStreaming = true
        self.isCurrentlyPlaying = false
        self.menu = menu
    }

    private var roleLabel: String {
        switch role {
        case .prompt: return "Prompt"
        case .response: return "Response"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Text(roleLabel.uppercased())
                    .font(.caption2.bold())
                    .foregroundStyle(role == .response ? Color.accentColor : .secondary)
                if isStreaming { ProgressView().controlSize(.mini) }
            }
            Markdown(content)
                .markdownTextStyle { FontSize(.em(1)) }
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(role == .response ? Color.accentColor.opacity(0.06) : Color(.secondarySystemBackground))
        .clipShape(RoundedRectangle(cornerRadius: 10))
        .overlay {
            if isCurrentlyPlaying {
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(Color.accentColor, lineWidth: 2)
            }
        }
        .contextMenu { menu() }
    }
}

extension DraftMessageView where Menu == EmptyView {
    init(message: DraftMessageDto, isCurrentlyPlaying: Bool = false) {
        self.init(message: message, isCurrentlyPlaying: isCurrentlyPlaying, menu: { EmptyView() })
    }
    init(streamingResponse content: String) {
        self.init(streamingResponse: content, menu: { EmptyView() })
    }
}
```

- [ ] **Step 2: Update DraftView to own a player + bar + Play-from-here menu item**

Open `client/Fabulis/Views/Draft/DraftView.swift`. Make the following targeted edits:

**2a. Add `@State` for the player and availability.** Below the existing `@State private var editingMessage: DraftMessageDto?`, add:

```swift
@State private var narrationAvailable = false
@State private var player = NarrationPlayer()
```

**2b. Update the `ForEach` row to pass `isCurrentlyPlaying` and add the "Play from here" menu item.** Find the existing ForEach body inside `LazyVStack`:

```swift
ForEach(Array(draft.messages.enumerated()), id: \.element.id) { idx, msg in
    let isLast = idx == draft.messages.count - 1
    let isLastResponse = isLast && msg.role == .response
    DraftMessageView(message: msg) {
        Button {
            editingMessage = msg
        } label: { Label("Edit", systemImage: "pencil") }
        if isLastResponse {
            Button {
                Task { await regenerate() }
            } label: { Label("Regenerate", systemImage: "arrow.clockwise") }
        }
        Divider()
        Button(role: .destructive) {
            Task { await deleteMessage(msg.id) }
        } label: { Label("Delete and after", systemImage: "trash") }
    }
}
```

Replace it with:

```swift
ForEach(Array(draft.messages.enumerated()), id: \.element.id) { idx, msg in
    let isLast = idx == draft.messages.count - 1
    let isLastResponse = isLast && msg.role == .response
    DraftMessageView(
        message: msg,
        isCurrentlyPlaying: player.currentBubbleId == msg.id
    ) {
        if narrationAvailable, msg.role == .response, msg.id >= 0 {
            Button { startNarration(from: msg.id) } label: {
                Label("Play from here", systemImage: "play.fill")
            }
            Divider()
        }
        Button {
            editingMessage = msg
        } label: { Label("Edit", systemImage: "pencil") }
        if isLastResponse {
            Button {
                Task { await regenerate() }
            } label: { Label("Regenerate", systemImage: "arrow.clockwise") }
        }
        Divider()
        Button(role: .destructive) {
            Task { await deleteMessage(msg.id) }
        } label: { Label("Delete and after", systemImage: "trash") }
    }
    .id(msg.id)
}
```

**2c. Add the narration bar via safeAreaInset.** After the existing `.onDisappear { streamTask?.cancel() }` line, add:

```swift
.safeAreaInset(edge: .bottom) {
    if player.state != .idle {
        NarrationBar(player: player)
    }
}
.task { await loadNarrationAvailability() }
```

The existing `.onDisappear` should also stop the player. Change:

```swift
.onDisappear { streamTask?.cancel() }
```

to:

```swift
.onDisappear {
    streamTask?.cancel()
    player.stop()
}
```

**2d. Stop the player on any mutation.** At the very top of `submit()`, `editAndResubmit(messageId:content:)`, `regenerate()`, and `deleteMessage(_:)`, add:

```swift
player.stop()
```

For example, `submit()` becomes:

```swift
private func submit() async {
    player.stop()
    let pending = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !pending.isEmpty else { return }
    prompt = ""
    let stream = await FabulisAPIClient.shared.streamMessage(draftId: draftId, prompt: pending)
    runStream(inFlight: pending, initial: stream)
}
```

Apply the same first-line addition to the other three methods.

**2e. Add the helper functions** at the bottom of the `DraftView` struct (next to `deleteMessage`):

```swift
private func loadNarrationAvailability() async {
    if let s = try? await FabulisAPIClient.shared.settings() {
        narrationAvailable = s.narrationAvailable
    }
}

private func startNarration(from bubbleId: Int) {
    guard let draft else { return }
    let responses = draft.messages
        .filter { $0.role == .response && $0.id >= 0 }
        .map { (id: $0.id, text: $0.content) }
    player.start(bubbles: responses, from: bubbleId)
}
```

**2f. Scroll the playing bubble into view.** DraftView's existing `ScrollPosition` is anchored to `.bottom` for the streaming-aware behaviour, which doesn't compose well with jumping to a specific id. Wrap the existing `ScrollView` in a `ScrollViewReader` (no behaviour change for the existing logic) and use the proxy for the narration scroll.

Change:

```swift
ScrollView {
    LazyVStack(alignment: .leading, spacing: 12) {
        // ... existing contents ...
    }
    .padding()
}
.scrollPosition($scrollPosition, anchor: .bottom)
```

to:

```swift
ScrollViewReader { proxy in
    ScrollView {
        LazyVStack(alignment: .leading, spacing: 12) {
            // ... existing contents ...
        }
        .padding()
    }
    .scrollPosition($scrollPosition, anchor: .bottom)
    .onChange(of: player.currentBubbleId) { _, new in
        if let new {
            withAnimation { proxy.scrollTo(new, anchor: .center) }
        }
    }
}
```

The `.id(msg.id)` modifier added in step 2b is what `proxy.scrollTo(new, ...)` resolves against.

- [ ] **Step 3: Verify the client compiles**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -30
```

Expected: `** BUILD SUCCEEDED **`. Fix any compiler errors (most likely around the `.id(msg.id)` placement on synthetic-id rows where `msg.id == -1`/`-2`/`-3` — those are fine to give a `.id()` since they're unique within the list at any given time, but if SwiftUI warns about duplicates remove the modifier from synthetic rows).

- [ ] **Step 4: Manual smoke test**

1. Open a draft with several saved exchanges.
2. Long-press a response bubble → "Play from here" appears above Edit.
3. Tap it. Narration starts; bar appears; border on the bubble; the scroll view centres on each bubble as narration advances.
4. While paused, tap Edit on a message → narration stops, edit sheet appears as before.
5. While playing, tap Regenerate on the last response → narration stops, generation runs as before.
6. While playing, type a new prompt and submit → narration stops, new generation streams as before.
7. With Kokoro stopped, open a draft → no "Play from here" item in any context menu.
8. With a draft mid-stream (a response is streaming in), confirm the streaming bubble has no "Play from here" option (it's a synthetic id `-2`, filtered out).

- [ ] **Step 5: Commit**

```bash
git add client/Fabulis/Views/Draft/DraftView.swift \
        client/Fabulis/Views/Draft/DraftMessageView.swift
git commit -m "Integrate narration into DraftView"
```

---

## Task 16: BACKLOG.md and wrap-up

**Files:**
- Modify: `BACKLOG.md`

- [ ] **Step 1: Add the background-playback entry**

Open `BACKLOG.md`. Under `## Functional gaps`, after the existing entries, add:

```markdown
### Background narration playback

Narration stops when you leave the story or draft view. A global
mini-player + `AVAudioSession.Category.playback` survival across
backgrounding, plus `MPNowPlayingInfoCenter` /
`MPRemoteCommandCenter` wiring for lock-screen controls, would let
you keep listening while browsing the library or with the screen
off.

Originally deferred in the narration v1 spec at
`docs/superpowers/specs/2026-05-26-narration-design.md`.
```

- [ ] **Step 2: Run the full server test suite one more time**

Run: `dotnet test tests/Fabulis.Server.Tests/Fabulis.Server.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 3: Run a final client build**

Run:

```bash
xcodebuild -project client/Fabulis.xcodeproj -scheme Fabulis \
  -destination 'platform=iOS Simulator,name=iPhone 16' \
  build 2>&1 | tail -20
```

Expected: `** BUILD SUCCEEDED **`.

- [ ] **Step 4: Commit**

```bash
git add BACKLOG.md
git commit -m "Backlog: defer background narration playback"
```

- [ ] **Step 5: Push the branch and open a PR (optional, when ready)**

```bash
git push -u origin narration-spec
gh pr create --title "Narration via Kokoro TTS" \
  --body "Implements docs/superpowers/specs/2026-05-26-narration-design.md."
```

---

## Done

Verification gate before declaring complete:
- `dotnet test` shows all tests passing.
- `dotnet build Fabulis.slnx` succeeds.
- Client builds for the iOS simulator.
- Manual smoke list from Tasks 8, 13, 14, and 15 has been walked through.
- The committed `BACKLOG.md` has the background-playback entry.
