using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

// Orchestrates the full pipeline: discover -> parse -> resolve -> auth -> execute.
public sealed class CollectionRunner
{
    private readonly PostmanCollectionParser _parser;
    private readonly EnvironmentResolver _environmentResolver;
    private readonly AuthorizationService _authorizationService;
    private readonly RequestExecutor _requestExecutor;

    public CollectionRunner(
        PostmanCollectionParser parser,
        EnvironmentResolver environmentResolver,
        AuthorizationService authorizationService,
        RequestExecutor requestExecutor)
    {
        _parser = parser;
        _environmentResolver = environmentResolver;
        _authorizationService = authorizationService;
        _requestExecutor = requestExecutor;
    }

    public async Task<IReadOnlyList<ExecutionResult>> RunRepositoryAsync(
        string externalRootPath,
        string repositoryName,
        bool mockMode,
        CancellationToken cancellationToken = default)
    {
        var repositoryPath = Path.Combine(externalRootPath, repositoryName);
        var testsPath = Path.Combine(repositoryPath, "tests");
        var collectionsPath = Path.Combine(testsPath, "collections");
        var dataPath = Path.Combine(testsPath, "data");
        var environmentPath = Path.Combine(dataPath, "environment.json");
        var authorizationPath = Path.Combine(dataPath, "collection_authorizationservice.json");

        if (!Directory.Exists(repositoryPath))
        {
            throw new DirectoryNotFoundException($"Repository path not found: {repositoryPath}");
        }

        if (!Directory.Exists(collectionsPath))
        {
            throw new DirectoryNotFoundException($"Collections path not found: {collectionsPath}");
        }

        if (!File.Exists(environmentPath))
        {
            throw new FileNotFoundException("Environment file not found.", environmentPath);
        }

        if (!File.Exists(authorizationPath))
        {
            throw new FileNotFoundException("Authorization file not found.", authorizationPath);
        }

        var variables = _environmentResolver.LoadVariables(environmentPath);
        var authHeaders = _authorizationService.BuildAuthHeaders(authorizationPath, _environmentResolver, variables);
        var collectionFiles = Directory
            .GetFiles(collectionsPath, "*.postman_collection.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<ExecutionResult>();
        foreach (var collectionPath in collectionFiles)
        {
            var parsedRequests = _parser.ParseRequests(collectionPath);
            foreach (var parsed in parsedRequests)
            {
                var result = await ExecuteRequestAsync(
                    repositoryName,
                    Path.GetFileName(collectionPath),
                    parsed,
                    variables,
                    authHeaders,
                    mockMode,
                    cancellationToken);

                results.Add(result);
            }
        }

        return results;
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

    private async Task<ExecutionResult> ExecuteRequestAsync(
        string repositoryName,
        string collectionFileName,
        ParsedPostmanRequest parsed,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, string> authHeaders,
        bool mockMode,
        CancellationToken cancellationToken)
    {
        var resolvedUrl = _environmentResolver.Resolve(parsed.RawUrl, variables);

        var resolvedHeaders = _environmentResolver.ResolveHeaders(parsed.Headers, variables);
        foreach (var authHeader in authHeaders)
        {
            resolvedHeaders[authHeader.Key] = authHeader.Value;
        }

        var resolvedBody = parsed.Body is null ? null : _environmentResolver.Resolve(parsed.Body, variables);
        var (statusCode, content) = await _requestExecutor.ExecuteAsync(
            parsed.Method,
            resolvedUrl,
            resolvedHeaders,
            resolvedBody,
            mockMode,
            cancellationToken);

        return new ExecutionResult
        {
            RepositoryName = repositoryName,
            CollectionFileName = collectionFileName,
            RequestName = parsed.Name,
            ResolvedUrl = resolvedUrl,
            AuthorizationHeader = resolvedHeaders.TryGetValue("Authorization", out var authorizationHeader)
                ? authorizationHeader
                : string.Empty,
            StatusCode = statusCode,
            ResponseBody = content
        };
    }
}
