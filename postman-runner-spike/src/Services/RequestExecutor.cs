using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PostmanRunnerSpike.Models;
using ExecutionContextModel = PostmanRunnerSpike.Models.ExecutionContext;

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

    public ExecutionContextModel CreateExecutionContext(
        IReadOnlyDictionary<string, string> environmentVariables,
        ParsedPostmanCollection collection,
        PostmanAuth? externalAuthFallback)
    {
        return new ExecutionContextModel(
            environmentVariables,
            collection.Variables,
            collection.Auth,
            externalAuthFallback,
            collection.StrictSsl ?? true,
            _handlerFactory);
    }

    public async Task<ExecutedRequestResponse> ExecuteAsync(
        ResolvedRequest request,
        ExecutionContextModel context,
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
            using var response = await context.GetClient(request.StrictSsl)
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
