using System.Net;
using System.Text.Json;
using NUnit.Framework;
using Reqnroll;
using RestSharp;

namespace PlacementRunner.Specs.Steps;

// Small live demo showing how a Reqnroll test could call an external API with RestSharp.
[Binding]
public sealed class JsonPlaceholderDemoSteps
{
    private string _environmentFile = string.Empty;
    private string _baseUrlVariable = string.Empty;
    private string _resourceVariable = string.Empty;
    private string _secretsFile = string.Empty;
    private string _tokenVariable = string.Empty;
    private string _baseUrl = string.Empty;
    private string _resource = string.Empty;
    private string _token = string.Empty;
    private RestResponse? _response;

    [Given("the demo API environment file is \"(.*)\"")]
    public void GivenTheDemoApiEnvironmentFileIs(string environmentFile)
    {
        _environmentFile = environmentFile;
    }

    [Given("the demo API base URL variable is \"(.*)\"")]
    public void GivenTheDemoApiBaseUrlVariableIs(string baseUrlVariable)
    {
        _baseUrlVariable = baseUrlVariable;
    }

    [Given("the demo API resource variable is \"(.*)\"")]
    public void GivenTheDemoApiResourceVariableIs(string resourceVariable)
    {
        _resourceVariable = resourceVariable;
    }

    [Given("the demo API secrets file is \"(.*)\"")]
    public void GivenTheDemoApiSecretsFileIs(string secretsFile)
    {
        _secretsFile = secretsFile;
    }

    [Given("the demo API token variable is \"(.*)\"")]
    public void GivenTheDemoApiTokenVariableIs(string tokenVariable)
    {
        _tokenVariable = tokenVariable;
    }

    [When("I send the demo GET request with RestSharp")]
    public async Task WhenISendTheDemoGetRequestWithRestSharp()
    {
        LoadDemoApiVariables();

        var client = new RestClient(_baseUrl);
        var request = new RestRequest(_resource, Method.Get);
        request.AddHeader("Authorization", $"Bearer {_token}");

        _response = await client.ExecuteAsync(request);
    }

    [Then("the demo response status should be (.*)")]
    public void ThenTheDemoResponseStatusShouldBe(int expectedStatusCode)
    {
        AssertResponse();
        Assert.That((int)_response!.StatusCode, Is.EqualTo(expectedStatusCode));
    }

    [Then("the demo response should contain post id (.*)")]
    public void ThenTheDemoResponseShouldContainPostId(int expectedPostId)
    {
        AssertResponse();

        using var document = JsonDocument.Parse(_response!.Content!);
        var actualPostId = document.RootElement.GetProperty("id").GetInt32();

        Assert.That(actualPostId, Is.EqualTo(expectedPostId));
    }

    [Then("the demo response title should not be empty")]
    public void ThenTheDemoResponseTitleShouldNotBeEmpty()
    {
        AssertResponse();

        using var document = JsonDocument.Parse(_response!.Content!);
        var title = document.RootElement.GetProperty("title").GetString();

        Assert.That(title, Is.Not.Null.And.Not.Empty);
    }

    private void AssertResponse()
    {
        Assert.That(_response, Is.Not.Null, "The RestSharp request did not return a response.");
        Assert.That(_response!.ResponseStatus, Is.EqualTo(ResponseStatus.Completed), "The RestSharp request did not complete successfully.");
        Assert.That(_response.Content, Is.Not.Null.And.Not.Empty, "The API response body was empty.");
    }

    private void LoadDemoApiVariables()
    {
        var repoRoot = FindRepoRoot();
        var environmentPath = Path.Combine(repoRoot, "test-config", _environmentFile);
        var secretsPath = Path.Combine(repoRoot, "test-secrets", _secretsFile);

        Assert.That(File.Exists(environmentPath), Is.True, $"Environment file was not found: {environmentPath}");
        Assert.That(File.Exists(secretsPath), Is.True, $"Secrets file was not found: {secretsPath}");

        var environmentVariables = LoadVariables(environmentPath);
        var secretVariables = LoadVariables(secretsPath);

        Assert.That(environmentVariables.TryGetValue(_baseUrlVariable, out var resolvedBaseUrl), Is.True, $"Missing variable: {_baseUrlVariable}");
        Assert.That(environmentVariables.TryGetValue(_resourceVariable, out var resolvedResource), Is.True, $"Missing variable: {_resourceVariable}");
        Assert.That(secretVariables.TryGetValue(_tokenVariable, out var resolvedToken), Is.True, $"Missing variable: {_tokenVariable}");

        _baseUrl = resolvedBaseUrl!;
        _resource = resolvedResource!;
        _token = resolvedToken!;
    }

    private static Dictionary<string, string> LoadVariables(string jsonPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var values = document.RootElement.GetProperty("values").EnumerateArray();

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in values)
        {
            var key = item.GetProperty("key").GetString();
            var value = item.GetProperty("value").GetString();

            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                variables[key] = value;
            }
        }

        return variables;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "postman-runner-spike", "external");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root containing postman-runner-spike/external.");
    }
}
