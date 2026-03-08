namespace PostmanRunnerSpike.Models;

// Simple data object that holds one parsed request from a Postman collection.
public sealed class ParsedPostmanRequest
{
    // Human-friendly name shown in Postman.
    public string Name { get; set; } = string.Empty;
    // HTTP verb like GET/POST.
    public string Method { get; set; } = "GET";
    // URL exactly as stored in Postman, including {{variables}}.
    public string RawUrl { get; set; } = string.Empty;
    // Request headers as key/value pairs.
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Auth type declared inside the collection request, if present.
    public string AuthType { get; set; } = string.Empty;
    // Raw auth settings found inside the collection request.
    public Dictionary<string, string> AuthConfiguration { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Optional request body (mostly for POST/PUT).
    public string? Body { get; set; }
}
