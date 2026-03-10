using NUnit.Framework;
using PostmanRunnerSpike.Models;
using PostmanRunnerSpike.Services;
using Reqnroll;
using System.Text.Json;

namespace PlacementRunner.Specs.Steps;

// Reqnroll step definitions that drive the spike like an acceptance test.
[Binding]
public sealed class PlacementRunnerSteps
{
    private string _repositoryName = "mock-postman-repo";
    private string _collectionFile = string.Empty;
    private bool _mockMode;
    private IReadOnlyList<ExecutionResult> _results = Array.Empty<ExecutionResult>();

    [Given("the Postman runner targets repository \"(.*)\"")]
    public void GivenThePostmanRunnerTargetsRepository(string repositoryName)
    {
        _repositoryName = repositoryName;
    }

    [Given("collection \"(.*)\"")]
    public void GivenCollection(string fileName)
    {
        _collectionFile = fileName;
    }

    [Given("mock execution is enabled")]
    public void GivenMockExecutionIsEnabled()
    {
        _mockMode = true;
    }

    [Given("mock execution is disabled")]
    public void GivenMockExecutionIsDisabled()
    {
        _mockMode = false;
    }

    [When("I execute the repository with the Postman runner")]
    public async Task WhenIExecuteTheRepositoryWithThePostmanRunner()
    {
        var repoRoot = FindRepoRoot();
        var externalRoot = Path.Combine(repoRoot, "postman-runner-spike", "external");
        _results = await CreateRunner().RunRepositoryAsync(externalRoot, _repositoryName, _mockMode);
    }

    [Then("the collection result should contain request \"(.*)\"")]
    public void ThenTheCollectionResultShouldContainRequest(string expectedRequestName)
    {
        var result = GetResult();
        Assert.That(result.RequestName, Is.EqualTo(expectedRequestName));
    }

    [Then("the resolved URL should be \"(.*)\"")]
    public void ThenTheResolvedUrlShouldBe(string expectedUrl)
    {
        var result = GetResult();
        Assert.That(result.ResolvedUrl, Is.EqualTo(expectedUrl));
    }

    [Then("the status code should be (.*)")]
    public void ThenTheStatusCodeShouldBe(int expectedStatusCode)
    {
        var result = GetResult();
        Assert.That(result.StatusCode, Is.EqualTo(expectedStatusCode));
    }

    [Then("the response body should be \"(.*)\"")]
    public void ThenTheResponseBodyShouldBe(string expectedBody)
    {
        var result = GetResult();
        Assert.That(result.ResponseBody, Is.EqualTo(expectedBody));
    }

    [Then("the response body should contain \"(.*)\"")]
    public void ThenTheResponseBodyShouldContain(string expectedFragment)
    {
        var result = GetResult();
        Assert.That(result.ResponseBody, Does.Contain(expectedFragment));
    }

    [Then("the JSON response should contain integer property \"(.*)\" with value (.*)")]
    public void ThenTheJsonResponseShouldContainIntegerPropertyWithValue(string propertyName, int expectedValue)
    {
        var result = GetResult();
        using var document = JsonDocument.Parse(result.ResponseBody);
        var actualValue = document.RootElement.GetProperty(propertyName).GetInt32();

        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    [Then("the authorization header should be \"(.*)\"")]
    public void ThenTheAuthorizationHeaderShouldBe(string expectedAuthorizationHeader)
    {
        var result = GetResult();
        Assert.That(result.AuthorizationHeader, Is.EqualTo(expectedAuthorizationHeader));
    }

    [Then("the JSON response should contain boolean property \"(.*)\" with value (.*)")]
    public void ThenTheJsonResponseShouldContainBooleanPropertyWithValue(string propertyName, bool expectedValue)
    {
        var result = GetResult();
        using var document = JsonDocument.Parse(result.ResponseBody);
        var actualValue = document.RootElement.GetProperty(propertyName).GetBoolean();

        Assert.That(actualValue, Is.EqualTo(expectedValue));
    }

    private ExecutionResult GetResult()
    {
        Assert.That(_results, Is.Not.Empty, "Collection runner did not return any results.");
        var result = _results.FirstOrDefault(r =>
            string.Equals(r.CollectionFileName, _collectionFile, StringComparison.OrdinalIgnoreCase));

        Assert.That(result, Is.Not.Null, $"Collection runner did not return a result for collection '{_collectionFile}'.");
        return result!;
    }

    private static CollectionRunner CreateRunner()
    {
        var parser = new PostmanCollectionParser();
        var variableResolver = new EnvironmentResolver();
        var authorizationService = new AuthorizationService();
        var requestExecutor = new RequestExecutor(new HttpClient());
        var requestResolver = new RequestResolver(variableResolver, authorizationService);
        return new CollectionRunner(parser, variableResolver, requestResolver, authorizationService, requestExecutor);
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
