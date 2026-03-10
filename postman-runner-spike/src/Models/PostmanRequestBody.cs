namespace PostmanRunnerSpike.Models;

public sealed class PostmanRequestBody
{
    public string Mode { get; set; } = string.Empty;
    public string? Raw { get; set; }
    public string? RawLanguage { get; set; }
    public List<PostmanKeyValueItem> UrlEncoded { get; set; } = [];
    public List<PostmanKeyValueItem> FormData { get; set; } = [];
}
