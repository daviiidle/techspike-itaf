namespace PostmanRunnerSpike.Models;

public sealed class ExecutedRequestResponse
{
    public bool Succeeded { get; set; }
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public Dictionary<string, string> ResponseHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public long DurationMs { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string ExceptionType { get; set; } = string.Empty;
}
