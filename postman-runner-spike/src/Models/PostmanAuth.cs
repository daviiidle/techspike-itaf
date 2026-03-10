namespace PostmanRunnerSpike.Models;

public sealed class PostmanAuth
{
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
