using System.Text.Json;
using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

// Reads a Postman collection file and extracts the first request.
public sealed class PostmanCollectionParser
{
    public ParsedPostmanRequest ParseFirstRequest(string collectionPath)
    {
        // Load and parse the JSON file.
        using var doc = JsonDocument.Parse(File.ReadAllText(collectionPath));
        var root = doc.RootElement;

        // Keep spike scope tiny: only first item in the collection.
        var item = root.GetProperty("item")[0];
        var request = item.GetProperty("request");

        // Pull the basic fields needed to run the request.
        var parsed = new ParsedPostmanRequest
        {
            Name = item.GetProperty("name").GetString() ?? "Unnamed Request",
            Method = request.GetProperty("method").GetString() ?? "GET",
            RawUrl = request.GetProperty("url").GetProperty("raw").GetString() ?? string.Empty
        };

        // Copy headers if they exist.
        if (request.TryGetProperty("header", out var headersElement) && headersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headersElement.EnumerateArray())
            {
                var key = header.TryGetProperty("key", out var k) ? k.GetString() : null;
                var value = header.TryGetProperty("value", out var v) ? v.GetString() : null;

                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                {
                    parsed.Headers[key] = value;
                }
            }
        }

        // Copy auth configuration if the collection defines one.
        if (request.TryGetProperty("auth", out var authElement) && authElement.ValueKind == JsonValueKind.Object)
        {
            parsed.AuthType = authElement.TryGetProperty("type", out var authType)
                ? authType.GetString() ?? string.Empty
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(parsed.AuthType) &&
                authElement.TryGetProperty(parsed.AuthType, out var authValuesElement) &&
                authValuesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var authValue in authValuesElement.EnumerateArray())
                {
                    var key = authValue.TryGetProperty("key", out var k) ? k.GetString() : null;
                    var value = authValue.TryGetProperty("value", out var v) ? v.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(key) && value is not null)
                    {
                        parsed.AuthConfiguration[key] = value;
                    }
                }
            }
        }

        // Copy raw body text if present.
        if (request.TryGetProperty("body", out var bodyElement) &&
            bodyElement.TryGetProperty("raw", out var rawBody))
        {
            parsed.Body = rawBody.GetString();
        }

        return parsed;
    }
}
