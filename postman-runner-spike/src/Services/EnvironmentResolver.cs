using System.Text.Json;

namespace PostmanRunnerSpike.Services;

// Handles loading and replacing {{variables}} from a Postman environment file.
public sealed class EnvironmentResolver
{
    public Dictionary<string, string> LoadVariables(string environmentPath)
    {
        // Read the environment JSON and grab the "values" array.
        using var doc = JsonDocument.Parse(File.ReadAllText(environmentPath));
        var values = doc.RootElement.GetProperty("values");

        // Store variables in a case-insensitive map for easier lookup.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values.EnumerateArray())
        {
            var key = item.GetProperty("key").GetString();
            var value = item.GetProperty("value").GetString();

            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                map[key] = value;
            }
        }

        return map;
    }

    public string Resolve(string template, IReadOnlyDictionary<string, string> variables)
    {
        // Replace each {{Key}} with its value.
        var resolved = template;
        foreach (var pair in variables)
        {
            resolved = resolved.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return resolved;
    }
}
