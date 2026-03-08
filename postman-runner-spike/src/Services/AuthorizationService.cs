using System.Text.Json;

namespace PostmanRunnerSpike.Services;

// Builds auth headers from a small auth config file.
public sealed class AuthorizationService
{
    public Dictionary<string, string> BuildAuthHeaders(
        string authorizationConfigPath,
        EnvironmentResolver environmentResolver,
        IReadOnlyDictionary<string, string> variables)
    {
        // Read auth config JSON.
        using var doc = JsonDocument.Parse(File.ReadAllText(authorizationConfigPath));
        var root = doc.RootElement;

        var type = root.GetProperty("type").GetString() ?? string.Empty;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Allow auth to be explicitly disabled for public endpoints.
        if (string.Equals(type, "none", StringComparison.OrdinalIgnoreCase))
        {
            return headers;
        }

        // For this spike we only support bearer token auth.
        if (string.Equals(type, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            var tokenTemplate = root.GetProperty("token").GetString() ?? string.Empty;
            var token = environmentResolver.Resolve(tokenTemplate, variables);
            headers["Authorization"] = $"Bearer {token}";
        }

        return headers;
    }
}
