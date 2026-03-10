namespace PostmanRunnerSpike.Models;

public sealed class PostmanEventScript
{
    public string Listen { get; set; } = string.Empty;
    public string ScriptType { get; set; } = string.Empty;
    public string RawScript { get; set; } = string.Empty;
}
