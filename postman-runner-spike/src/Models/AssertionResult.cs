namespace PostmanRunnerSpike.Models;

public sealed class AssertionResult
{
    public string Name { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
