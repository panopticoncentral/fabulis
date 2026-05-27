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
