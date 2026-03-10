namespace PostmanRunnerSpike.Models;

public sealed class PostmanUrl
{
    public string Raw { get; set; } = string.Empty;
    public string? Protocol { get; set; }
    public List<string> Host { get; set; } = [];
    public string? Port { get; set; }
    public List<string> Path { get; set; } = [];
    public List<PostmanKeyValueItem> Query { get; set; } = [];
}
