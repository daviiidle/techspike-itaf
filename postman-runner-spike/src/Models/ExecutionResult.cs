namespace PostmanRunnerSpike.Models;

public sealed class ExecutionResult
{
    public string RepositoryName { get; set; } = string.Empty;
    public string CollectionFileName { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string RequestName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string ResolvedUrl { get; set; } = string.Empty;
    public string AuthTypeApplied { get; set; } = "none";
    public string AuthorizationHeader { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public Dictionary<string, string> ResponseHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long DurationMs { get; set; }
    public bool Succeeded { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
    public List<string> UnresolvedVariables { get; set; } = [];
    public List<AssertionResult> AssertionResults { get; set; } = [];
}
