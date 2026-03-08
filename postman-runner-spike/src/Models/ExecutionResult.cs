namespace PostmanRunnerSpike.Models;

// Final output from running one request end-to-end.
public sealed class ExecutionResult
{
    // Repository folder the request came from.
    public string RepositoryName { get; set; } = string.Empty;
    // Collection file the request came from.
    public string CollectionFileName { get; set; } = string.Empty;
    // Name of the request that was executed.
    public string RequestName { get; set; } = string.Empty;
    // URL after variables were replaced.
    public string ResolvedUrl { get; set; } = string.Empty;
    // Authorization header that was prepared for the outgoing request.
    public string AuthorizationHeader { get; set; } = string.Empty;
    // Numeric HTTP status code (for example 200).
    public int StatusCode { get; set; }
    // Response text returned by the executor.
    public string ResponseBody { get; set; } = string.Empty;
}
