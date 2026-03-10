namespace PostmanRunnerSpike.Models;

public sealed class ParsedPostmanRequest
{
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public string FolderPath { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PostmanUrl Url { get; set; } = new();
    public List<PostmanKeyValueItem> Headers { get; set; } = [];
    public PostmanAuth? Auth { get; set; }
    public PostmanRequestBody? Body { get; set; }
    public List<PostmanEventScript> Events { get; set; } = [];
    public bool? StrictSsl { get; set; }
}
