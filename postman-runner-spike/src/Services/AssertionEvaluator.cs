using System.Text.Json;
using System.Text.RegularExpressions;
using PostmanRunnerSpike.Models;
using ExecutionContextModel = PostmanRunnerSpike.Models.ExecutionContext;

namespace PostmanRunnerSpike.Services;

public sealed class AssertionEvaluator
{
    private static readonly Regex StatusRegex = new(@"pm\.response\.to\.have\.status\((?<status>\d+)\)", RegexOptions.Compiled);
    private static readonly Regex IncludeRegex = new(@"pm\.expect\(pm\.response\.text\(\)\)\.to\.include\((?<value>.+?)\)", RegexOptions.Compiled);
    private static readonly Regex JsonAssignmentRegex = new(@"(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*pm\.response\.json\(\)\s*;?", RegexOptions.Compiled);
    private static readonly Regex EqualityRegex = new(@"pm\.expect\((?<path>.+?)\)\.to\.eql\((?<expected>.+?)\)", RegexOptions.Compiled);
    private static readonly Regex TestNameRegex = new(@"pm\.test\(\s*[""'](?<name>[^""']+)[""']", RegexOptions.Compiled);

    public IReadOnlyList<AssertionResult> Evaluate(
        ResolvedRequest request,
        ExecutedRequestResponse response,
        ExecutionContextModel? executionContext = null)
    {
        var results = new List<AssertionResult>();
        var jsonAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        JsonElement? jsonBody = null;

        foreach (var script in request.RequestEvents.Where(static item => string.Equals(item.Listen, "test", StringComparison.OrdinalIgnoreCase)))
        {
            var currentTestName = string.Empty;
            foreach (var rawLine in script.RawScript.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                var testNameMatch = TestNameRegex.Match(line);
                if (testNameMatch.Success)
                {
                    currentTestName = testNameMatch.Groups["name"].Value;
                    continue;
                }

                if (line is "});" or "}" or ");")
                {
                    currentTestName = string.Empty;
                    continue;
                }

                var statusMatch = StatusRegex.Match(line);
                if (statusMatch.Success)
                {
                    var expected = int.Parse(statusMatch.Groups["status"].Value);
                    results.Add(new AssertionResult
                    {
                        Name = NameOrDefault(currentTestName, $"status == {expected}"),
                        Outcome = response.StatusCode == expected ? "passed" : "failed",
                        Message = response.StatusCode == expected
                            ? $"Expected status {expected}."
                            : $"Expected status {expected} but got {response.StatusCode}."
                    });
                    continue;
                }

                var includeMatch = IncludeRegex.Match(line);
                if (includeMatch.Success)
                {
                    var expectedText = ParseLiteral(includeMatch.Groups["value"].Value);
                    var passed = response.ResponseBody.Contains(expectedText, StringComparison.Ordinal);
                    results.Add(new AssertionResult
                    {
                        Name = NameOrDefault(currentTestName, "response text includes value"),
                        Outcome = passed ? "passed" : "failed",
                        Message = passed
                            ? $"Response text contains '{expectedText}'."
                            : $"Response text did not contain '{expectedText}'."
                    });
                    continue;
                }

                var assignmentMatch = JsonAssignmentRegex.Match(line);
                if (assignmentMatch.Success)
                {
                    jsonAliases.Add(assignmentMatch.Groups["name"].Value);
                    if (jsonBody is null)
                    {
                        results.Add(ParseJsonAssertion(currentTestName, response, ref jsonBody));
                    }

                    continue;
                }

                if (line.Contains("pm.response.json()", StringComparison.Ordinal))
                {
                    if (jsonBody is null)
                    {
                        results.Add(ParseJsonAssertion(currentTestName, response, ref jsonBody));
                    }

                    if (!line.Contains("pm.expect(", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                var equalityMatch = EqualityRegex.Match(line);
                if (equalityMatch.Success)
                {
                    var actualPath = equalityMatch.Groups["path"].Value.Trim();
                    if (actualPath.StartsWith("pm.response.json()", StringComparison.Ordinal))
                    {
                        actualPath = actualPath["pm.response.json()".Length..].TrimStart('.');
                    }
                    else
                    {
                        foreach (var alias in jsonAliases)
                        {
                            if (actualPath.StartsWith($"{alias}.", StringComparison.OrdinalIgnoreCase))
                            {
                                actualPath = actualPath[(alias.Length + 1)..];
                                break;
                            }

                            if (string.Equals(actualPath, alias, StringComparison.OrdinalIgnoreCase))
                            {
                                actualPath = string.Empty;
                                break;
                            }
                        }
                    }

                    if (jsonBody is null)
                    {
                        results.Add(ParseJsonAssertion(currentTestName, response, ref jsonBody));
                    }

                    if (jsonBody is not null && TryResolveJsonPath(jsonBody.Value, actualPath, out var actualValue))
                    {
                        var expectedValue = ParseExpectedLiteral(equalityMatch.Groups["expected"].Value);
                        var passed = Equals(actualValue, expectedValue);
                        results.Add(new AssertionResult
                        {
                            Name = NameOrDefault(currentTestName, $"json path {actualPath} equals expected value"),
                            Outcome = passed ? "passed" : "failed",
                            Message = passed
                                ? $"Value at '{actualPath}' matched."
                                : $"Expected '{expectedValue}' at '{actualPath}' but got '{actualValue}'."
                        });
                    }
                    else
                    {
                        results.Add(new AssertionResult
                        {
                            Name = NameOrDefault(currentTestName, $"json path {actualPath}"),
                            Outcome = "failed",
                            Message = $"Could not resolve JSON path '{actualPath}'."
                        });
                    }

                    continue;
                }

                if (line.Contains("pm.", StringComparison.Ordinal))
                {
                    results.Add(new AssertionResult
                    {
                        Name = NameOrDefault(currentTestName, "unsupported assertion"),
                        Outcome = "unsupported",
                        Message = line
                    });
                }
            }
        }

        return results;
    }

    private static AssertionResult ParseJsonAssertion(string testName, ExecutedRequestResponse response, ref JsonElement? jsonBody)
    {
        try
        {
            using var document = JsonDocument.Parse(response.ResponseBody);
            jsonBody = document.RootElement.Clone();
            return new AssertionResult
            {
                Name = NameOrDefault(testName, "response.json()"),
                Outcome = "passed",
                Message = "Response body parsed as JSON."
            };
        }
        catch (Exception ex)
        {
            jsonBody = null;
            return new AssertionResult
            {
                Name = NameOrDefault(testName, "response.json()"),
                Outcome = "failed",
                Message = ex.Message
            };
        }
    }

    private static bool TryResolveJsonPath(JsonElement json, string path, out object? value)
    {
        var current = json;
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                {
                    value = null;
                    return false;
                }
            }
        }

        value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number when current.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => current.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => current.GetRawText()
        };
        return true;
    }

    private static object? ParseExpectedLiteral(string value)
    {
        var trimmed = value.Trim().TrimEnd(';').Trim();
        if (trimmed.StartsWith('"') || trimmed.StartsWith('\'') || trimmed.StartsWith('`'))
        {
            return ParseLiteral(trimmed);
        }

        if (bool.TryParse(trimmed, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(trimmed, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(trimmed, out var doubleValue))
        {
            return doubleValue;
        }

        return trimmed;
    }

    private static string ParseLiteral(string literal)
    {
        var trimmed = literal.Trim().TrimEnd(';').Trim();
        if (trimmed.Length >= 2)
        {
            var start = trimmed[0];
            var end = trimmed[^1];
            if ((start == '"' && end == '"') || (start == '\'' && end == '\'') || (start == '`' && end == '`'))
            {
                return trimmed[1..^1];
            }
        }

        return trimmed;
    }

    private static string NameOrDefault(string name, string fallback)
    {
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }
}
