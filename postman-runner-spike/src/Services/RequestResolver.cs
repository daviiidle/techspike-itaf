using PostmanRunnerSpike.Models;
using ExecutionContextModel = PostmanRunnerSpike.Models.ExecutionContext;

namespace PostmanRunnerSpike.Services;

public sealed class RequestResolver
{
    private readonly VariableResolver _variableResolver;
    private readonly AuthorizationService _authorizationService;

    public RequestResolver(VariableResolver variableResolver, AuthorizationService authorizationService)
    {
        _variableResolver = variableResolver;
        _authorizationService = authorizationService;
    }

    public ResolvedRequest Resolve(
        ParsedPostmanCollection collection,
        ParsedPostmanRequest request,
        ExecutionContextModel executionContext)
    {
        var variables = _variableResolver.MergeVariables(
            request.Variables,
            executionContext.CollectionVariables,
            executionContext.EnvironmentVariables);

        var resolvedHeaders = ResolveHeaders(request.Headers, variables);
        var resolvedQuery = ResolveQuery(request.Url.Query, variables);
        var resolvedBody = ResolveBody(request.Body, variables);

        var authResult = _authorizationService.ApplyAuth(
            request.Auth,
            executionContext.CollectionAuth,
            executionContext.ExternalAuthFallback,
            variables,
            resolvedHeaders,
            resolvedQuery,
            _variableResolver);

        var finalUrl = ResolveUrl(request.Url, authResult.Query, variables);
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddUnresolved(unresolved, finalUrl);
        foreach (var header in authResult.Headers)
        {
            AddUnresolved(unresolved, header.Key, header.Value);
        }

        foreach (var item in authResult.UnresolvedVariables)
        {
            unresolved.Add(item);
        }

        if (resolvedBody is not null)
        {
            AddUnresolved(unresolved, resolvedBody.Raw);
            foreach (var field in resolvedBody.UrlEncoded.Where(static item => !item.Disabled))
            {
                AddUnresolved(unresolved, field.Key, field.Value);
            }

            foreach (var field in resolvedBody.FormData.Where(static item => !item.Disabled))
            {
                AddUnresolved(unresolved, field.Key, field.Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(authResult.UnsupportedMessage))
        {
            unresolved.Add($"unsupported-auth:{authResult.AuthTypeApplied}");
        }

        return new ResolvedRequest
        {
            CollectionName = collection.Name,
            RequestName = request.Name,
            FolderPath = request.FolderPath,
            RequestPath = request.RequestPath,
            Method = request.Method,
            ResolvedUrl = finalUrl,
            Headers = authResult.Headers,
            Body = resolvedBody,
            AuthTypeApplied = authResult.AuthTypeApplied,
            StrictSsl = request.StrictSsl ?? executionContext.StrictSsl,
            UnresolvedVariables = unresolved.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToList(),
            RequestEvents = request.Events,
            CollectionEvents = collection.Events
        };
    }

    private Dictionary<string, string> ResolveHeaders(
        IEnumerable<PostmanKeyValueItem> headers,
        IReadOnlyDictionary<string, string> variables)
    {
        var resolvedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers.Where(static item => !item.Disabled))
        {
            resolvedHeaders[_variableResolver.Resolve(header.Key, variables)] =
                _variableResolver.Resolve(header.Value ?? string.Empty, variables);
        }

        return resolvedHeaders;
    }

    private List<PostmanKeyValueItem> ResolveQuery(
        IEnumerable<PostmanKeyValueItem> query,
        IReadOnlyDictionary<string, string> variables)
    {
        return query
            .Where(static item => !item.Disabled)
            .Select(item => new PostmanKeyValueItem
            {
                Key = _variableResolver.Resolve(item.Key, variables),
                Value = _variableResolver.Resolve(item.Value ?? string.Empty, variables),
                Type = item.Type
            })
            .ToList();
    }

    private PostmanRequestBody? ResolveBody(PostmanRequestBody? body, IReadOnlyDictionary<string, string> variables)
    {
        if (body is null)
        {
            return null;
        }

        return new PostmanRequestBody
        {
            Mode = body.Mode,
            Raw = body.Raw is null ? null : _variableResolver.Resolve(body.Raw, variables),
            RawLanguage = body.RawLanguage,
            UrlEncoded = body.UrlEncoded
                .Select(item => new PostmanKeyValueItem
                {
                    Key = _variableResolver.Resolve(item.Key, variables),
                    Value = _variableResolver.Resolve(item.Value ?? string.Empty, variables),
                    Disabled = item.Disabled,
                    Type = item.Type
                })
                .ToList(),
            FormData = body.FormData
                .Select(item => new PostmanKeyValueItem
                {
                    Key = _variableResolver.Resolve(item.Key, variables),
                    Value = _variableResolver.Resolve(item.Value ?? string.Empty, variables),
                    Disabled = item.Disabled,
                    Type = item.Type
                })
                .ToList()
        };
    }

    private string ResolveUrl(PostmanUrl url, IReadOnlyList<PostmanKeyValueItem> query, IReadOnlyDictionary<string, string> variables)
    {
        var raw = _variableResolver.Resolve(url.Raw, variables);
        if (!string.IsNullOrWhiteSpace(raw) && raw.Contains("://", StringComparison.Ordinal))
        {
            return AppendQuery(raw, query);
        }

        var protocol = _variableResolver.Resolve(url.Protocol ?? string.Empty, variables);
        var host = string.Join(".",
            url.Host
                .Select(segment => _variableResolver.Resolve(segment, variables))
                .Where(static item => !string.IsNullOrWhiteSpace(item)));
        var port = _variableResolver.Resolve(url.Port ?? string.Empty, variables);
        var pathSegments = new List<string>();

        foreach (var segment in url.Path)
        {
            var resolvedSegment = _variableResolver.Resolve(segment, variables);
            foreach (var nestedSegment in resolvedSegment.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                pathSegments.Add(Uri.EscapeDataString(nestedSegment));
            }
        }

        var baseUrl = string.Empty;
        if (!string.IsNullOrWhiteSpace(protocol))
        {
            baseUrl = $"{protocol}://";
        }

        baseUrl += host;
        if (!string.IsNullOrWhiteSpace(port))
        {
            baseUrl += $":{port}";
        }

        if (pathSegments.Count > 0)
        {
            baseUrl += $"/{string.Join("/", pathSegments)}";
        }

        return AppendQuery(baseUrl, query);
    }

    private static string AppendQuery(string baseUrl, IReadOnlyList<PostmanKeyValueItem> query)
    {
        var activeQuery = query.Where(static item => !item.Disabled).ToArray();
        if (activeQuery.Length == 0)
        {
            return baseUrl;
        }

        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var queryString = string.Join("&", activeQuery.Select(static item =>
            $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? string.Empty)}"));
        return $"{baseUrl}{separator}{queryString}";
    }

    private void AddUnresolved(HashSet<string> unresolved, params string?[] values)
    {
        foreach (var value in _variableResolver.FindUnresolvedVariables(values))
        {
            unresolved.Add(value);
        }
    }
}
