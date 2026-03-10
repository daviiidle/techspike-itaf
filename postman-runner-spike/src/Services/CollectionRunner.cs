using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

public sealed class CollectionRunner
{
    private readonly PostmanCollectionParser _parser;
    private readonly VariableResolver _variableResolver;
    private readonly AuthorizationService _authorizationService;
    private readonly RequestExecutor _requestExecutor;
    private readonly AssertionEvaluator _assertionEvaluator;

    public CollectionRunner(
        PostmanCollectionParser parser,
        VariableResolver variableResolver,
        AuthorizationService authorizationService,
        RequestExecutor requestExecutor,
        AssertionEvaluator assertionEvaluator)
    {
        _parser = parser;
        _variableResolver = variableResolver;
        _authorizationService = authorizationService;
        _requestExecutor = requestExecutor;
        _assertionEvaluator = assertionEvaluator;
    }

    public CollectionRunner(
        PostmanCollectionParser parser,
        VariableResolver variableResolver,
        AuthorizationService authorizationService,
        RequestExecutor requestExecutor)
        : this(parser, variableResolver, authorizationService, requestExecutor, new AssertionEvaluator())
    {
    }

    public async Task<IReadOnlyList<ExecutionResult>> RunRepositoryAsync(
        string externalRootPath,
        string repositoryName,
        bool mockMode,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExecutionResult>();

        try
        {
            var repository = DiscoverRepository(externalRootPath, repositoryName);
            var environmentVariables = _variableResolver.LoadVariables(repository.EnvironmentPath);
            var externalAuth = _authorizationService.LoadExternalAuth(repository.AuthorizationPath);

            foreach (var collectionPath in repository.CollectionFiles)
            {
                results.AddRange(await RunCollectionAsync(
                    repositoryName,
                    collectionPath,
                    environmentVariables,
                    externalAuth,
                    mockMode,
                    cancellationToken));
            }

            return results;
        }
        catch (Exception ex)
        {
            results.Add(new ExecutionResult
            {
                RepositoryName = repositoryName,
                CollectionFileName = string.Empty,
                CollectionName = string.Empty,
                RequestName = "[RepositoryError]",
                Succeeded = false,
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name
            });

            return results;
        }
    }

    public async Task<IReadOnlyList<ExecutionResult>> RunAllRepositoriesAsync(
        string externalRootPath,
        bool mockMode,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(externalRootPath))
        {
            throw new DirectoryNotFoundException($"External root path not found: {externalRootPath}");
        }

        var allResults = new List<ExecutionResult>();
        var repositoryNames = Directory
            .GetDirectories(externalRootPath)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var repositoryName in repositoryNames)
        {
            var repositoryResults = await RunRepositoryAsync(
                externalRootPath,
                repositoryName!,
                mockMode,
                cancellationToken);

            allResults.AddRange(repositoryResults);
        }

        return allResults;
    }

    private async Task<IReadOnlyList<ExecutionResult>> RunCollectionAsync(
        string repositoryName,
        string collectionPath,
        IReadOnlyDictionary<string, string> environmentVariables,
        PostmanAuth? externalAuth,
        bool mockMode,
        CancellationToken cancellationToken)
    {
        var results = new List<ExecutionResult>();
        try
        {
            var collection = _parser.ParseCollection(collectionPath);
            using var executionContext = _requestExecutor.CreateContext();

            foreach (var request in collection.Requests)
            {
                var resolvedRequest = ResolveRequest(collection, request, environmentVariables, externalAuth);
                var result = BuildBaseResult(repositoryName, Path.GetFileName(collectionPath), resolvedRequest);

                if (resolvedRequest.UnresolvedVariables.Count > 0)
                {
                    result.Succeeded = false;
                    result.ErrorMessage = $"Unresolved variables: {string.Join(", ", resolvedRequest.UnresolvedVariables)}";
                    result.UnresolvedVariables = resolvedRequest.UnresolvedVariables;
                    result.AssertionResults = _assertionEvaluator.Evaluate(resolvedRequest, new ExecutedRequestResponse()).ToList();
                    results.Add(result);
                    continue;
                }

                var response = await _requestExecutor.ExecuteAsync(resolvedRequest, executionContext, mockMode, cancellationToken);
                result.StatusCode = response.StatusCode;
                result.ResponseBody = response.ResponseBody;
                result.ResponseHeaders = response.ResponseHeaders;
                result.DurationMs = response.DurationMs;
                result.Succeeded = response.Succeeded;
                result.ErrorMessage = response.ErrorMessage;
                result.ExceptionType = response.ExceptionType;
                result.AssertionResults = _assertionEvaluator.Evaluate(resolvedRequest, response).ToList();

                if (response.Succeeded && result.AssertionResults.Any(static item => string.Equals(item.Outcome, "failed", StringComparison.OrdinalIgnoreCase)))
                {
                    result.Succeeded = false;
                }

                results.Add(result);
            }
        }
        catch (Exception ex)
        {
            results.Add(new ExecutionResult
            {
                RepositoryName = repositoryName,
                CollectionFileName = Path.GetFileName(collectionPath),
                CollectionName = Path.GetFileNameWithoutExtension(collectionPath),
                RequestName = "[CollectionError]",
                Succeeded = false,
                ErrorMessage = ex.Message,
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name
            });
        }

        return results;
    }

    private ResolvedRequest ResolveRequest(
        ParsedPostmanCollection collection,
        ParsedPostmanRequest request,
        IReadOnlyDictionary<string, string> environmentVariables,
        PostmanAuth? externalAuth)
    {
        var variables = _variableResolver.MergeVariables(request.Variables, collection.Variables, environmentVariables);
        var resolvedHeaders = ResolveHeaders(request.Headers, variables);
        var resolvedQuery = ResolveQuery(request.Url.Query, variables);
        var resolvedBody = ResolveBody(request.Body, variables);
        var resolvedUrl = ResolveUrl(request.Url, resolvedQuery, variables);

        var authResult = _authorizationService.ApplyAuth(collection, request, variables, externalAuth, resolvedHeaders, resolvedQuery, _variableResolver);
        var finalUrl = ResolveUrl(request.Url, authResult.Query, variables);

        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddUnresolved(unresolved, finalUrl);
        foreach (var header in authResult.Headers)
        {
            AddUnresolved(unresolved, header.Key, header.Value);
        }

        if (resolvedBody is not null)
        {
            AddUnresolved(unresolved, resolvedBody.Raw);
            foreach (var field in resolvedBody.UrlEncoded)
            {
                if (!field.Disabled)
                {
                    AddUnresolved(unresolved, field.Key, field.Value);
                }
            }

            foreach (var field in resolvedBody.FormData)
            {
                if (!field.Disabled)
                {
                    AddUnresolved(unresolved, field.Key, field.Value);
                }
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
            AllowInvalidCertificates = (request.StrictSsl ?? collection.StrictSsl) == false,
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
            resolvedHeaders[header.Key] = _variableResolver.Resolve(header.Value ?? string.Empty, variables);
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
        var host = string.Join(".", url.Host.Select(segment => _variableResolver.Resolve(segment, variables)).Where(static item => !string.IsNullOrWhiteSpace(item)));
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

        var path = string.Join("/", pathSegments);

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

        if (!string.IsNullOrWhiteSpace(path))
        {
            baseUrl += $"/{path}";
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

    private static ExecutionResult BuildBaseResult(string repositoryName, string collectionFileName, ResolvedRequest request)
    {
        request.Headers.TryGetValue("Authorization", out var authorizationHeader);
        return new ExecutionResult
        {
            RepositoryName = repositoryName,
            CollectionFileName = collectionFileName,
            CollectionName = request.CollectionName,
            RequestName = request.RequestName,
            FolderPath = request.FolderPath,
            RequestPath = request.RequestPath,
            Method = request.Method,
            ResolvedUrl = request.ResolvedUrl,
            AuthTypeApplied = request.AuthTypeApplied,
            AuthorizationHeader = authorizationHeader ?? string.Empty
        };
    }

    private RepositoryDiscoveryResult DiscoverRepository(string externalRootPath, string repositoryName)
    {
        var repositoryPath = Path.Combine(externalRootPath, repositoryName);
        var collectionsPath = Path.Combine(repositoryPath, "tests", "collections");
        var dataPath = Path.Combine(repositoryPath, "tests", "data");

        if (!Directory.Exists(repositoryPath))
        {
            throw new DirectoryNotFoundException($"Repository path not found: {repositoryPath}");
        }

        if (!Directory.Exists(collectionsPath))
        {
            throw new DirectoryNotFoundException($"Collections path not found: {collectionsPath}");
        }

        return new RepositoryDiscoveryResult
        {
            CollectionFiles = Directory
                .GetFiles(collectionsPath, "*.postman_collection.json", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            EnvironmentPath = Path.Combine(dataPath, "environment.json"),
            AuthorizationPath = Path.Combine(dataPath, "collection_authorizationservice.json")
        };
    }

    private void AddUnresolved(HashSet<string> unresolved, params string?[] values)
    {
        foreach (var value in _variableResolver.FindUnresolvedVariables(values))
        {
            unresolved.Add(value);
        }
    }
}

internal sealed class RepositoryDiscoveryResult
{
    public string EnvironmentPath { get; set; } = string.Empty;
    public string AuthorizationPath { get; set; } = string.Empty;
    public IReadOnlyList<string> CollectionFiles { get; set; } = Array.Empty<string>();
}
