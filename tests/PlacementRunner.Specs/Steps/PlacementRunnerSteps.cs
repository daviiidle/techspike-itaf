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
    // Inputs captured from Given steps.
    private string _fakeRepoFolder = "mock-postman-repo";
    private string _collectionFile = string.Empty;
    private bool _mockMode;
    // Output captured from When step and asserted in Then steps.
    private IReadOnlyList<ExecutionResult> _results = Array.Empty<ExecutionResult>();

    [Given("fake repo folder \"(.*)\"")]
    public void GivenFakeRepoFolder(string fakeRepoFolder)
    {
        _fakeRepoFolder = fakeRepoFolder;
    }

    [Given("collection file \"(.*)\"")]
    public void GivenCollectionFile(string fileName)
    {
        _collectionFile = fileName;
    }

    [Given("environment file \"(.*)\"")]
    public void GivenEnvironmentFile(string _)
    {
    }

    [Given("authorization file \"(.*)\"")]
    public void GivenAuthorizationFile(string _)
    {
    }

    [Given("mock mode is enabled")]
    public void GivenMockModeIsEnabled()
    {
        _mockMode = true;
    }

    [Given("mock mode is disabled")]
    public void GivenMockModeIsDisabled()
    {
        _mockMode = false;
    }

    [When("I run the collection runner")]
    public async Task WhenIRunTheCollectionRunner()
    {
        // Point the runner at the external repo root and let it discover collections on its own.
        var repoRoot = FindRepoRoot();
        var externalRoot = Path.Combine(repoRoot, "postman-runner-spike", "external");

        // Build runner stack directly for this spike.
        var parser = new PostmanCollectionParser();
        var environmentResolver = new EnvironmentResolver();
        var authorizationService = new AuthorizationService();
        var requestExecutor = new RequestExecutor(new HttpClient());
        var runner = new CollectionRunner(parser, environmentResolver, authorizationService, requestExecutor);

        // Run the discovered repo and store results for assertions.
        _results = await runner.RunRepositoryAsync(externalRoot, _fakeRepoFolder, _mockMode);
    }

    [Then("the request name should be \"(.*)\"")]
    public void ThenTheRequestNameShouldBe(string expectedRequestName)
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

    [Then("the JSON response body should contain id (.*)")]
    public void ThenTheJsonResponseBodyShouldContainId(int expectedId)
    {
        var result = GetResult();
        using var document = JsonDocument.Parse(result.ResponseBody);
        var actualId = document.RootElement.GetProperty("id").GetInt32();

        Assert.That(actualId, Is.EqualTo(expectedId));
    }

    [Then("the authorization header should be \"(.*)\"")]
    public void ThenTheAuthorizationHeaderShouldBe(string expectedAuthorizationHeader)
    {
        var result = GetResult();
        Assert.That(result.AuthorizationHeader, Is.EqualTo(expectedAuthorizationHeader));
    }

    [Then("the JSON response body should contain authenticated true")]
    public void ThenTheJsonResponseBodyShouldContainAuthenticatedTrue()
    {
        var result = GetResult();
        using var document = JsonDocument.Parse(result.ResponseBody);
        var authenticated = document.RootElement.GetProperty("authenticated").GetBoolean();

        Assert.That(authenticated, Is.True);
    }

    private ExecutionResult GetResult()
    {
        Assert.That(_results, Is.Not.Empty, "Collection runner did not return any results.");
        var result = _results.FirstOrDefault(r =>
            string.Equals(r.CollectionFileName, _collectionFile, StringComparison.OrdinalIgnoreCase));

        Assert.That(result, Is.Not.Null, $"Collection runner did not return a result for collection '{_collectionFile}'.");
        return result!;
    }

    private static string FindRepoRoot()
    {
        // Walk upward from test output until we find the repository root marker.
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
