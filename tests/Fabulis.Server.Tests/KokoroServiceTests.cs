using System.Net;
using System.Text;
using System.Text.Json;
using Fabulis.Server.Data;
using Xunit;

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

        using var response = await service.SynthesizeStreamAsync("hello", "af_bella", 1.25, CancellationToken.None);

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
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x49, 0x44, 0x33 }, bytes);
    }

    [Fact]
    public async Task SynthesizeThrowsKokoroUnavailableWhenUrlMissing()
    {
        var (service, _) = Build(baseUrl: null);
        await Assert.ThrowsAsync<KokoroUnavailableException>(() =>
            service.SynthesizeStreamAsync("hi", "af_bella", 1.0, CancellationToken.None));
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
            service.SynthesizeStreamAsync("hi", "af_bella", 1.0, CancellationToken.None));
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

        Assert.Equal("http://localhost:8880/v1/audio/voices", stub.LastRequest!.RequestUri!.ToString());
        Assert.Equal(3, voices.Count);
        Assert.Equal("af_bella", voices[0].Id);
        Assert.Equal("Bella", voices[0].DisplayName);
        Assert.Equal("en-us-female", voices[0].Language);
        Assert.Equal("am_michael", voices[1].Id);
        Assert.Equal("en-us-male", voices[1].Language);
        Assert.Equal("en-gb-female", voices[2].Language);
    }

    [Fact]
    public async Task ListVoicesParsesObjectShapeResponse()
    {
        // kokoro-fastapi (Remsky) returns voices as objects, not bare strings.
        var (service, stub) = Build();
        stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"voices":[{"id":"af_bella","name":"af_bella"},{"id":"bm_george","name":"bm_george"}]}""",
                Encoding.UTF8, "application/json")
        });

        var voices = await service.ListVoicesAsync(CancellationToken.None);

        Assert.Equal(2, voices.Count);
        Assert.Equal("af_bella", voices[0].Id);
        Assert.Equal("en-us-female", voices[0].Language);
        Assert.Equal("bm_george", voices[1].Id);
        Assert.Equal("en-gb-male", voices[1].Language);
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

    [Fact]
    public async Task SynthesizeThrowsKokoroUpstreamErrorOn4xx()
    {
        var (service, stub) = Build();
        stub.Responder = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("invalid voice")
        });
        var ex = await Assert.ThrowsAsync<KokoroUpstreamException>(() =>
            service.SynthesizeStreamAsync("hi", "made_up_voice", 1.0, CancellationToken.None));
        Assert.Equal(400, ex.UpstreamStatus);
    }

    [Fact]
    public async Task InvalidateCachesForcesVoicesRefetch()
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
        Assert.Equal(1, calls);  // cached on second call

        service.InvalidateCaches();

        await service.ListVoicesAsync(CancellationToken.None);
        Assert.Equal(2, calls);  // refetched after invalidate
    }

    [Fact]
    public async Task ListVoicesThrowsKokoroUnavailableOnNetworkError()
    {
        var (service, stub) = Build();
        stub.Responder = _ => throw new HttpRequestException("connection refused");
        await Assert.ThrowsAsync<KokoroUnavailableException>(() =>
            service.ListVoicesAsync(CancellationToken.None));
    }
}
