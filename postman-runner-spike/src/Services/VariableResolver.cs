using System.Text.Json;
using System.Text.RegularExpressions;

namespace PostmanRunnerSpike.Services;

public class VariableResolver
{
    private static readonly Regex VariablePattern = new(@"\{\{\s*(?<name>[^}]+?)\s*\}\}", RegexOptions.Compiled);

    public Dictionary<string, string> LoadVariables(string environmentPath)
    {
        if (!File.Exists(environmentPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(environmentPath));
        if (!doc.RootElement.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values.EnumerateArray())
        {
            var key = item.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
            var value = item.TryGetProperty("value", out var valueElement) ? valueElement.ToString() : null;

            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                map[key] = value;
            }
        }

        return map;
    }

    public Dictionary<string, string> MergeVariables(params IReadOnlyDictionary<string, string>?[] scopes)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in scopes.Reverse())
        {
            if (scope is null)
            {
                continue;
            }

            foreach (var pair in scope)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    public string Resolve(string template, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var resolved = template;
        for (var i = 0; i < 10; i++)
        {
            var changed = false;
            resolved = VariablePattern.Replace(resolved, match =>
            {
                var name = match.Groups["name"].Value.Trim();
                if (!variables.TryGetValue(name, out var value))
                {
                    return match.Value;
                }

                changed = true;
                return value;
            });

            if (!changed)
            {
                break;
            }
        }

        return resolved;
    }

    public IReadOnlyList<string> FindUnresolvedVariables(params string?[] values)
    {
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (Match match in VariablePattern.Matches(value))
            {
                unresolved.Add(match.Groups["name"].Value.Trim());
            }
        }

        return unresolved.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
