using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

public sealed class CollectionRunner
{
    private readonly PostmanCollectionParser _parser;
    private readonly VariableResolver _variableResolver;
    private readonly RequestResolver _requestResolver;
    private readonly AuthorizationService _authorizationService;
    private readonly RequestExecutor _requestExecutor;
    private readonly AssertionEvaluator _assertionEvaluator;

    public CollectionRunner(
        PostmanCollectionParser parser,
        VariableResolver variableResolver,
        RequestResolver requestResolver,
        AuthorizationService authorizationService,
        RequestExecutor requestExecutor,
        AssertionEvaluator assertionEvaluator)
    {
        _parser = parser;
        _variableResolver = variableResolver;
        _requestResolver = requestResolver;
        _authorizationService = authorizationService;
        _requestExecutor = requestExecutor;
        _assertionEvaluator = assertionEvaluator;
    }

    public CollectionRunner(
        PostmanCollectionParser parser,
        VariableResolver variableResolver,
        RequestResolver requestResolver,
        AuthorizationService authorizationService,
        RequestExecutor requestExecutor)
        : this(parser, variableResolver, requestResolver, authorizationService, requestExecutor, new AssertionEvaluator())
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
            using var executionContext = _requestExecutor.CreateExecutionContext(environmentVariables, collection, externalAuth);

            foreach (var request in collection.Requests)
            {
                var resolvedRequest = _requestResolver.Resolve(collection, request, executionContext);
                var result = BuildBaseResult(repositoryName, Path.GetFileName(collectionPath), resolvedRequest);

                if (resolvedRequest.UnresolvedVariables.Count > 0)
                {
                    result.Succeeded = false;
                    result.ErrorMessage = $"Unresolved variables: {string.Join(", ", resolvedRequest.UnresolvedVariables)}";
                    result.UnresolvedVariables = resolvedRequest.UnresolvedVariables;
                    result.AssertionResults = _assertionEvaluator.Evaluate(
                        resolvedRequest,
                        new ExecutedRequestResponse(),
                        executionContext).ToList();
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
                result.AssertionResults = _assertionEvaluator.Evaluate(resolvedRequest, response, executionContext).ToList();

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

}

internal sealed class RepositoryDiscoveryResult
{
    public string EnvironmentPath { get; set; } = string.Empty;
    public string AuthorizationPath { get; set; } = string.Empty;
    public IReadOnlyList<string> CollectionFiles { get; set; } = Array.Empty<string>();
}
