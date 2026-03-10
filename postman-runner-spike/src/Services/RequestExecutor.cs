using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

public sealed class RequestExecutor
{
    private readonly Func<bool, CookieContainer, HttpMessageHandler> _handlerFactory;

    public RequestExecutor()
        : this(CreateDefaultHandler)
    {
    }

    public RequestExecutor(HttpClient _)
        : this(CreateDefaultHandler)
    {
    }

    public RequestExecutor(Func<bool, CookieContainer, HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory;
    }

    public RequestExecutionContext CreateContext()
    {
        return new RequestExecutionContext(_handlerFactory);
    }

    public async Task<ExecutedRequestResponse> ExecuteAsync(
        ResolvedRequest request,
        RequestExecutionContext context,
        bool mockMode,
        CancellationToken cancellationToken = default)
    {
        if (mockMode)
        {
            return new ExecutedRequestResponse
            {
                Succeeded = true,
                StatusCode = (int)HttpStatusCode.OK,
                ResponseBody = "mock success",
                DurationMs = 0
            };
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var httpRequest = BuildRequestMessage(request);
            using var response = await context.GetClient(request.AllowInvalidCertificates)
                .SendAsync(httpRequest, cancellationToken);
            stopwatch.Stop();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseHeaders = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(
                    static header => header.Key,
                    static header => string.Join(", ", header.Value),
                    StringComparer.OrdinalIgnoreCase);

            return new ExecutedRequestResponse
            {
                Succeeded = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                ResponseBody = responseBody,
                ResponseHeaders = responseHeaders,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ExecutedRequestResponse
            {
                Succeeded = false,
                StatusCode = 0,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name
            };
        }
    }

    private static HttpRequestMessage BuildRequestMessage(ResolvedRequest request)
    {
        var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.ResolvedUrl);
        httpRequest.Content = BuildContent(request.Body, request.Headers);

        foreach (var header in request.Headers)
        {
            if (httpRequest.Content is not null &&
                httpRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                continue;
            }

            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return httpRequest;
    }

    private static HttpContent? BuildContent(PostmanRequestBody? body, IReadOnlyDictionary<string, string> headers)
    {
        if (body is null)
        {
            return null;
        }

        var mode = body.Mode.Trim().ToLowerInvariant();
        return mode switch
        {
            "raw" => BuildRawContent(body, headers),
            "urlencoded" => BuildUrlEncodedContent(body),
            "formdata" => BuildFormDataContent(body),
            _ => null
        };
    }

    private static HttpContent BuildRawContent(PostmanRequestBody body, IReadOnlyDictionary<string, string> headers)
    {
        var content = new StringContent(body.Raw ?? string.Empty, Encoding.UTF8);
        var contentType = ResolveContentType(body, headers);
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return content;
    }

    private static HttpContent BuildUrlEncodedContent(PostmanRequestBody body)
    {
        var pairs = body.UrlEncoded
            .Where(static item => !item.Disabled)
            .Select(static item => new KeyValuePair<string, string>(item.Key, item.Value ?? string.Empty));
        return new FormUrlEncodedContent(pairs);
    }

    private static HttpContent BuildFormDataContent(PostmanRequestBody body)
    {
        var content = new MultipartFormDataContent();
        foreach (var item in body.FormData.Where(static entry => !entry.Disabled))
        {
            if (string.Equals(item.Type, "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            content.Add(new StringContent(item.Value ?? string.Empty), item.Key);
        }

        return content;
    }

    private static string? ResolveContentType(PostmanRequestBody body, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.TryGetValue("Content-Type", out var contentType))
        {
            return contentType;
        }

        return body.RawLanguage?.Trim().ToLowerInvariant() switch
        {
            "json" => "application/json",
            "xml" => "application/xml",
            "html" => "text/html",
            "javascript" => "application/javascript",
            "text" => "text/plain",
            _ => null
        };
    }

    private static HttpMessageHandler CreateDefaultHandler(bool allowInvalidCertificates, CookieContainer cookies)
    {
        return new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = allowInvalidCertificates
                ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                : null
        };
    }
}

public sealed class RequestExecutionContext : IDisposable
{
    private readonly Func<bool, CookieContainer, HttpMessageHandler> _handlerFactory;
    private readonly CookieContainer _cookieContainer = new();
    private readonly Dictionary<bool, HttpClient> _clients = [];

    public RequestExecutionContext(Func<bool, CookieContainer, HttpMessageHandler> handlerFactory)
    {
        _handlerFactory = handlerFactory;
    }

    public HttpClient GetClient(bool allowInvalidCertificates)
    {
        if (_clients.TryGetValue(allowInvalidCertificates, out var client))
        {
            return client;
        }

        var handler = _handlerFactory(allowInvalidCertificates, _cookieContainer);
        if (handler is not HttpClientHandler)
        {
            handler = new CookieTrackingHandler(_cookieContainer, handler);
        }

        client = new HttpClient(handler, disposeHandler: true);
        _clients[allowInvalidCertificates] = client;
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
