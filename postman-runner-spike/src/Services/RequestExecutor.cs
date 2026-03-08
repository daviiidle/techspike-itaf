using System.Net;
using System.Text;

namespace PostmanRunnerSpike.Services;

// Executes HTTP requests, with a safe mock mode for local spikes.
public sealed class RequestExecutor
{
    private readonly HttpClient _httpClient;

    public RequestExecutor(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(int StatusCode, string Content)> ExecuteAsync(
        string method,
        string url,
        IReadOnlyDictionary<string, string> headers,
        string? body,
        bool mockMode,
        CancellationToken cancellationToken = default)
    {
        // Mock mode avoids real network calls and returns fixed fake output.
        if (mockMode)
        {
            return ((int)HttpStatusCode.OK, "mock success");
        }

        // Build a normal HTTP request when mock mode is off.
        using var request = new HttpRequestMessage(new HttpMethod(method), url);

        // Add headers to either request headers or content headers.
        foreach (var header in headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                request.Content ??= new StringContent(string.Empty);
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // If body exists, send it as JSON text for simplicity.
        if (!string.IsNullOrWhiteSpace(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        // Send request and return status + response text.
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ((int)response.StatusCode, content);
    }
}
