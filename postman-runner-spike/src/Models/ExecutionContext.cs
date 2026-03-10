using System.Net;

namespace PostmanRunnerSpike.Models;

public sealed class ExecutionContext : IDisposable
{
    private readonly Func<bool, CookieContainer, HttpMessageHandler> _handlerFactory;
    private readonly Dictionary<bool, HttpClient> _clients = [];

    public ExecutionContext(
        IReadOnlyDictionary<string, string> environmentVariables,
        IReadOnlyDictionary<string, string> collectionVariables,
        PostmanAuth? collectionAuth,
        PostmanAuth? externalAuthFallback,
        bool strictSsl,
        Func<bool, CookieContainer, HttpMessageHandler> handlerFactory)
    {
        EnvironmentVariables = environmentVariables;
        CollectionVariables = collectionVariables;
        CollectionAuth = collectionAuth;
        ExternalAuthFallback = externalAuthFallback;
        StrictSsl = strictSsl;
        _handlerFactory = handlerFactory;
    }

    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }
    public IReadOnlyDictionary<string, string> CollectionVariables { get; }
    public PostmanAuth? CollectionAuth { get; }
    public PostmanAuth? ExternalAuthFallback { get; }
    public CookieContainer SharedCookies { get; } = new();
    public bool StrictSsl { get; }

    public HttpClient GetClient(bool strictSsl)
    {
        if (_clients.TryGetValue(strictSsl, out var client))
        {
            return client;
        }

        var handler = _handlerFactory(!strictSsl, SharedCookies);
        if (handler is not HttpClientHandler)
        {
            handler = new CookieTrackingHandler(SharedCookies, handler);
        }

        client = new HttpClient(handler, disposeHandler: true);
        _clients[strictSsl] = client;
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
    }
}

internal sealed class CookieTrackingHandler : DelegatingHandler
{
    private readonly CookieContainer _cookieContainer;

    public CookieTrackingHandler(CookieContainer cookieContainer, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _cookieContainer = cookieContainer;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cookieHeader = _cookieContainer.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            request.Headers.Remove("Cookie");
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders))
        {
            foreach (var header in setCookieHeaders)
            {
                _cookieContainer.SetCookies(request.RequestUri!, header);
            }
        }

        return response;
    }
}
