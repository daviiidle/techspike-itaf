using System.Text.Json;
using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

public sealed class PostmanCollectionParser
{
    public ParsedPostmanCollection ParseCollection(string collectionPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(collectionPath));
        var root = doc.RootElement;

        var collection = new ParsedPostmanCollection
        {
            Name = root.TryGetProperty("info", out var info) && info.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? Path.GetFileNameWithoutExtension(collectionPath)
                : Path.GetFileNameWithoutExtension(collectionPath),
            Variables = ParseVariables(root),
            Auth = root.TryGetProperty("auth", out var authElement)
                ? AuthorizationService.ParseAuthObject(authElement)
                : null,
            Events = ParseEvents(root),
            StrictSsl = ParseStrictSsl(root)
        };

        if (root.TryGetProperty("item", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            CollectRequests(items, collection.Requests, [], new Dictionary<string, string>(collection.Variables, StringComparer.OrdinalIgnoreCase), collection.StrictSsl);
        }

        return collection;
    }

    private static void CollectRequests(
        JsonElement items,
        List<ParsedPostmanRequest> requests,
        List<string> folderSegments,
        Dictionary<string, string> inheritedVariables,
        bool? inheritedStrictSsl)
    {
        foreach (var item in items.EnumerateArray())
        {
            var itemName = item.TryGetProperty("name", out var itemNameElement)
                ? itemNameElement.GetString() ?? "Unnamed Item"
                : "Unnamed Item";
            var itemVariables = Merge(inheritedVariables, ParseVariables(item));
            var itemStrictSsl = ParseStrictSsl(item) ?? inheritedStrictSsl;

            if (item.TryGetProperty("request", out var requestElement) && requestElement.ValueKind == JsonValueKind.Object)
            {
                var requestPathSegments = folderSegments.Append(itemName).ToArray();
                requests.Add(ParseRequest(item, requestElement, itemVariables, folderSegments, requestPathSegments, itemStrictSsl));
            }

            if (item.TryGetProperty("item", out var nestedItems) && nestedItems.ValueKind == JsonValueKind.Array)
            {
                var nestedFolders = folderSegments.Append(itemName).ToList();
                CollectRequests(nestedItems, requests, nestedFolders, itemVariables, itemStrictSsl);
            }
        }
    }

    private static ParsedPostmanRequest ParseRequest(
        JsonElement item,
        JsonElement requestElement,
        Dictionary<string, string> variables,
        IReadOnlyList<string> folderSegments,
        IReadOnlyList<string> requestPathSegments,
        bool? strictSsl)
    {
        var urlElement = requestElement.TryGetProperty("url", out var candidateUrl) ? candidateUrl : default;
        return new ParsedPostmanRequest
        {
            Name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "Unnamed Request" : "Unnamed Request",
            Method = requestElement.TryGetProperty("method", out var methodElement) ? methodElement.GetString() ?? "GET" : "GET",
            FolderPath = string.Join(" / ", folderSegments),
            RequestPath = string.Join(" / ", requestPathSegments),
            Variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase),
            Url = ParseUrl(urlElement),
            Headers = ParseKeyValueArray(requestElement, "header"),
            Auth = requestElement.TryGetProperty("auth", out var authElement)
                ? AuthorizationService.ParseAuthObject(authElement)
                : null,
            Body = ParseBody(requestElement),
            Events = ParseEvents(item),
            StrictSsl = ParseStrictSsl(requestElement) ?? ParseStrictSsl(item) ?? strictSsl
        };
    }

    private static Dictionary<string, string> ParseVariables(JsonElement element)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("variable", out var variableElement) || variableElement.ValueKind != JsonValueKind.Array)
        {
            return variables;
        }

        foreach (var item in variableElement.EnumerateArray())
        {
            var key = item.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
            var value = item.TryGetProperty("value", out var valueElement) ? valueElement.ToString() : null;
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                variables[key] = value;
            }
        }

        return variables;
    }

    private static List<PostmanEventScript> ParseEvents(JsonElement element)
    {
        var events = new List<PostmanEventScript>();
        if (!element.TryGetProperty("event", out var eventElement) || eventElement.ValueKind != JsonValueKind.Array)
        {
            return events;
        }

        foreach (var item in eventElement.EnumerateArray())
        {
            var listen = item.TryGetProperty("listen", out var listenElement) ? listenElement.GetString() ?? string.Empty : string.Empty;
            if (!item.TryGetProperty("script", out var scriptElement) || scriptElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var scriptType = scriptElement.TryGetProperty("type", out var scriptTypeElement) ? scriptTypeElement.GetString() ?? string.Empty : string.Empty;
            var rawScript = string.Empty;
            if (scriptElement.TryGetProperty("exec", out var execElement) && execElement.ValueKind == JsonValueKind.Array)
            {
                rawScript = string.Join(Environment.NewLine, execElement.EnumerateArray().Select(static line => line.GetString() ?? string.Empty));
            }
            else if (scriptElement.TryGetProperty("raw", out var rawElement))
            {
                rawScript = rawElement.ToString();
            }

            events.Add(new PostmanEventScript
            {
                Listen = listen,
                ScriptType = scriptType,
                RawScript = rawScript
            });
        }

        return events;
    }

    private static PostmanUrl ParseUrl(JsonElement urlElement)
    {
        if (urlElement.ValueKind == JsonValueKind.String)
        {
            return new PostmanUrl { Raw = urlElement.GetString() ?? string.Empty };
        }

        if (urlElement.ValueKind != JsonValueKind.Object)
        {
            return new PostmanUrl();
        }

        var url = new PostmanUrl
        {
            Raw = urlElement.TryGetProperty("raw", out var rawElement) ? rawElement.GetString() ?? string.Empty : string.Empty,
            Protocol = urlElement.TryGetProperty("protocol", out var protocolElement) ? protocolElement.GetString() : null,
            Port = urlElement.TryGetProperty("port", out var portElement) ? portElement.ToString() : null,
            Host = ParseSegments(urlElement, "host"),
            Path = ParseSegments(urlElement, "path"),
            Query = ParseKeyValueArray(urlElement, "query")
        };

        return url;
    }

    private static PostmanRequestBody? ParseBody(JsonElement requestElement)
    {
        if (!requestElement.TryGetProperty("body", out var bodyElement) || bodyElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var body = new PostmanRequestBody
        {
            Mode = bodyElement.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() ?? string.Empty : string.Empty,
            Raw = bodyElement.TryGetProperty("raw", out var rawElement) ? rawElement.GetString() : null,
            UrlEncoded = ParseKeyValueArray(bodyElement, "urlencoded"),
            FormData = ParseKeyValueArray(bodyElement, "formdata")
        };

        if (bodyElement.TryGetProperty("options", out var optionsElement) &&
            optionsElement.TryGetProperty("raw", out var rawOptionsElement) &&
            rawOptionsElement.TryGetProperty("language", out var languageElement))
        {
            body.RawLanguage = languageElement.GetString();
        }

        return body;
    }

    private static List<PostmanKeyValueItem> ParseKeyValueArray(JsonElement element, string propertyName)
    {
        var items = new List<PostmanKeyValueItem>();
        if (!element.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var item in values.EnumerateArray())
        {
            var key = item.TryGetProperty("key", out var keyElement) ? keyElement.GetString() ?? string.Empty : string.Empty;
            items.Add(new PostmanKeyValueItem
            {
                Key = key,
                Value = item.TryGetProperty("value", out var valueElement) ? valueElement.ToString() : null,
                Disabled = item.TryGetProperty("disabled", out var disabledElement) && disabledElement.ValueKind == JsonValueKind.True,
                Type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null
            });
        }

        return items;
    }

    private static List<string> ParseSegments(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var segmentElement))
        {
            return [];
        }

        return segmentElement.ValueKind switch
        {
            JsonValueKind.Array => segmentElement.EnumerateArray().Select(static item => item.ToString()).Where(static item => !string.IsNullOrWhiteSpace(item)).ToList(),
            JsonValueKind.String => [segmentElement.GetString() ?? string.Empty],
            _ => []
        };
    }

    private static bool? ParseStrictSsl(JsonElement element)
    {
        if (!element.TryGetProperty("protocolProfileBehavior", out var behaviorElement) || behaviorElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!behaviorElement.TryGetProperty("strictSSL", out var strictSslElement))
        {
            return null;
        }

        return strictSslElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Dictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> inheritedVariables,
        IReadOnlyDictionary<string, string> itemVariables)
    {
        var merged = new Dictionary<string, string>(inheritedVariables, StringComparer.OrdinalIgnoreCase);
        foreach (var variable in itemVariables)
        {
            merged[variable.Key] = variable.Value;
        }

        return merged;
    }
}
