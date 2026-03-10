namespace PostmanRunnerSpike.Models;

public sealed class ParsedPostmanCollection
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PostmanAuth? Auth { get; set; }
    public List<PostmanEventScript> Events { get; set; } = [];
    public List<ParsedPostmanRequest> Requests { get; set; } = [];
    public bool? StrictSsl { get; set; }
}
