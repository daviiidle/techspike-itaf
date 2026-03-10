using System.Text;
using System.Text.Json;
using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

public sealed class AuthorizationService
{
    public PostmanAuth? LoadExternalAuth(string authorizationConfigPath)
    {
        if (!File.Exists(authorizationConfigPath))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(authorizationConfigPath));
        return ParseAuthObject(doc.RootElement);
    }

    public AppliedAuthResult ApplyAuth(
        ParsedPostmanCollection collection,
        ParsedPostmanRequest request,
        IReadOnlyDictionary<string, string> variables,
        PostmanAuth? externalAuth,
        IReadOnlyDictionary<string, string> currentHeaders,
        IReadOnlyList<PostmanKeyValueItem> currentQuery,
        VariableResolver variableResolver)
    {
        var effectiveAuth = ResolveEffectiveAuth(request.Auth, collection.Auth, externalAuth);
        if (effectiveAuth is null)
        {
            return new AppliedAuthResult
            {
                AuthTypeApplied = "none",
                Headers = new Dictionary<string, string>(currentHeaders, StringComparer.OrdinalIgnoreCase),
                Query = currentQuery.ToList()
            };
        }

        var headers = new Dictionary<string, string>(currentHeaders, StringComparer.OrdinalIgnoreCase);
        var query = currentQuery.Select(CloneItem).ToList();
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var authType = NormalizeType(effectiveAuth.Type);

        switch (authType)
        {
            case "noauth":
            case "none":
                return new AppliedAuthResult
                {
                    AuthTypeApplied = "noauth",
                    Headers = headers,
                    Query = query
                };

            case "bearer":
                {
                    var token = ResolveAuthValue(effectiveAuth.Parameters, "token", variables, variableResolver, unresolved);
                    headers["Authorization"] = $"Bearer {token}";
                    break;
                }

            case "basic":
                {
                    var username = ResolveAuthValue(effectiveAuth.Parameters, "username", variables, variableResolver, unresolved);
                    var password = ResolveAuthValue(effectiveAuth.Parameters, "password", variables, variableResolver, unresolved);
                    var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                    headers["Authorization"] = $"Basic {encoded}";
                    break;
                }

            case "apikey":
                {
                    var key = ResolveAuthValue(effectiveAuth.Parameters, "key", variables, variableResolver, unresolved);
                    var value = ResolveAuthValue(effectiveAuth.Parameters, "value", variables, variableResolver, unresolved);
                    var location = ResolveAuthValue(effectiveAuth.Parameters, "in", variables, variableResolver, unresolved);

                    if (string.Equals(location, "query", StringComparison.OrdinalIgnoreCase))
                    {
                        query.Add(new PostmanKeyValueItem { Key = key, Value = value });
                    }
                    else
                    {
                        headers[key] = value;
                    }

                    break;
                }

            default:
                return new AppliedAuthResult
                {
                    AuthTypeApplied = authType,
                    Headers = headers,
                    Query = query,
                    UnsupportedMessage = $"Unsupported auth type '{effectiveAuth.Type}'."
                };
        }

        return new AppliedAuthResult
        {
            AuthTypeApplied = authType,
            Headers = headers,
            Query = query,
            UnresolvedVariables = unresolved.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public static PostmanAuth? ParseAuthObject(JsonElement authElement)
    {
        if (authElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = authElement.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString() ?? string.Empty
            : string.Empty;

        var auth = new PostmanAuth { Type = type };
        if (!string.IsNullOrWhiteSpace(type) &&
            authElement.TryGetProperty(type, out var typedValues) &&
            typedValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in typedValues.EnumerateArray())
            {
                var key = entry.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
                var value = entry.TryGetProperty("value", out var valueElement) ? valueElement.ToString() : null;
                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                {
                    auth.Parameters[key] = value;
                }
            }
        }

        foreach (var property in authElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(property.Name, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            auth.Parameters[property.Name] = property.Value.ToString();
        }

        return auth;
    }

    private static PostmanAuth? ResolveEffectiveAuth(PostmanAuth? requestAuth, PostmanAuth? collectionAuth, PostmanAuth? externalAuth)
    {
        var requestType = NormalizeType(requestAuth?.Type);
        if (requestAuth is not null)
        {
            if (requestType is "noauth" or "none")
            {
                return new PostmanAuth { Type = "noauth" };
            }

            if (requestType is not "" and not "inherit")
            {
                return requestAuth;
            }
        }

        var collectionType = NormalizeType(collectionAuth?.Type);
        if (collectionAuth is not null && collectionType is not "" and not "inherit")
        {
            return collectionAuth;
        }

        var externalType = NormalizeType(externalAuth?.Type);
        if (externalAuth is not null && externalType is not "" and not "inherit")
        {
            return externalAuth;
        }

        return null;
    }

    private static string NormalizeType(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string ResolveAuthValue(
        IReadOnlyDictionary<string, string> parameters,
        string key,
        IReadOnlyDictionary<string, string> variables,
        VariableResolver variableResolver,
        HashSet<string> unresolved)
    {
        var template = parameters.TryGetValue(key, out var value) ? value : string.Empty;
        var resolved = variableResolver.Resolve(template, variables);
        foreach (var item in variableResolver.FindUnresolvedVariables(resolved))
        {
            unresolved.Add(item);
        }

        return resolved;
    }

    private static PostmanKeyValueItem CloneItem(PostmanKeyValueItem item)
    {
        return new PostmanKeyValueItem
        {
            Key = item.Key,
            Value = item.Value,
            Disabled = item.Disabled,
            Type = item.Type
        };
    }
}

public sealed class AppliedAuthResult
{
    public string AuthTypeApplied { get; set; } = "none";
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PostmanKeyValueItem> Query { get; set; } = [];
    public IReadOnlyList<string> UnresolvedVariables { get; set; } = Array.Empty<string>();
    public string UnsupportedMessage { get; set; } = string.Empty;
}
