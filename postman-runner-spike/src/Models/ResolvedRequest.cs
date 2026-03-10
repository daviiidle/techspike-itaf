namespace PostmanRunnerSpike.Models;

public sealed class ResolvedRequest
{
    public string CollectionName { get; set; } = string.Empty;
    public string RequestName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public string ResolvedUrl { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PostmanRequestBody? Body { get; set; }
    public string AuthTypeApplied { get; set; } = "none";
    public bool AllowInvalidCertificates { get; set; }
    public List<string> UnresolvedVariables { get; set; } = [];
    public List<PostmanEventScript> RequestEvents { get; set; } = [];
    public List<PostmanEventScript> CollectionEvents { get; set; } = [];
}
