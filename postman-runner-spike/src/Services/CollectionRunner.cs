using PostmanRunnerSpike.Models;

namespace PostmanRunnerSpike.Services;

// Orchestrates the full pipeline: parse -> resolve -> auth -> execute.
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

    public async Task<ExecutionResult> RunAsync(
        string collectionPath,
        string environmentPath,
        string authorizationPath,
        bool mockMode,
        CancellationToken cancellationToken = default)
    {
        // 1) Parse first request from collection.
        var parsed = _parser.ParseFirstRequest(collectionPath);
        // 2) Load environment variables.
        var variables = _environmentResolver.LoadVariables(environmentPath);

        // 3) Resolve URL variables.
        var resolvedUrl = _environmentResolver.Resolve(parsed.RawUrl, variables);

        // 4) Merge request headers with auth headers.
        var resolvedHeaders = new Dictionary<string, string>(parsed.Headers, StringComparer.OrdinalIgnoreCase);
        foreach (var authHeader in _authorizationService.BuildAuthHeaders(authorizationPath, _environmentResolver, variables))
        {
            resolvedHeaders[authHeader.Key] = authHeader.Value;
        }

        // 5) Resolve body variables if a body exists.
        var resolvedBody = parsed.Body is null ? null : _environmentResolver.Resolve(parsed.Body, variables);
        // 6) Execute request (or fake execution in mock mode).
        var (statusCode, content) = await _requestExecutor.ExecuteAsync(
            parsed.Method,
            resolvedUrl,
            resolvedHeaders,
            resolvedBody,
            mockMode,
            cancellationToken);

        // 7) Return clean execution output object.
        return new ExecutionResult
        {
            RequestName = parsed.Name,
            ResolvedUrl = resolvedUrl,
            StatusCode = statusCode,
            ResponseBody = content
        };
    }
}
