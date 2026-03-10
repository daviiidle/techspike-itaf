namespace PostmanRunnerSpike.Models;

public sealed class PostmanKeyValueItem
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public bool Disabled { get; set; }
    public string? Type { get; set; }
}
