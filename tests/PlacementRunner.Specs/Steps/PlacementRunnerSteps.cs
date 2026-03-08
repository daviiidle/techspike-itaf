using NUnit.Framework;
using PostmanRunnerSpike.Models;
using PostmanRunnerSpike.Services;
using Reqnroll;

namespace PlacementRunner.Specs.Steps;

// Reqnroll step definitions that drive the spike like an acceptance test.
[Binding]
public sealed class PlacementRunnerSteps
{
    // Inputs captured from Given steps.
    private string _fakeRepoFolder = "fake-postman-repo";
    private string _collectionFile = string.Empty;
    private string _environmentFile = string.Empty;
    private string _authorizationFile = string.Empty;
    private bool _mockMode;
    // Output captured from When step and asserted in Then steps.
    private ExecutionResult? _result;

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
    public void GivenEnvironmentFile(string fileName)
    {
        _environmentFile = fileName;
    }

    [Given("authorization file \"(.*)\"")]
    public void GivenAuthorizationFile(string fileName)
    {
        _authorizationFile = fileName;
    }

    [Given("mock mode is enabled")]
    public void GivenMockModeIsEnabled()
    {
        _mockMode = true;
    }

    [When("I run the collection runner")]
    public async Task WhenIRunTheCollectionRunner()
    {
        // Locate test assets under the fake external repo.
        var repoRoot = FindRepoRoot();
        var testsRoot = Path.Combine(repoRoot, "postman-runner-spike", "external", _fakeRepoFolder, "tests");

        var collectionPath = Path.Combine(testsRoot, "collections", _collectionFile);
        var environmentPath = Path.Combine(testsRoot, "data", _environmentFile);
        var authorizationPath = Path.Combine(testsRoot, "data", _authorizationFile);

        // Build runner stack directly for this spike.
        var parser = new PostmanCollectionParser();
        var environmentResolver = new EnvironmentResolver();
        var authorizationService = new AuthorizationService();
        var requestExecutor = new RequestExecutor(new HttpClient());
        var runner = new CollectionRunner(parser, environmentResolver, authorizationService, requestExecutor);

        // Run end-to-end and store result for assertions.
        _result = await runner.RunAsync(collectionPath, environmentPath, authorizationPath, _mockMode);
    }

    [Then("the request name should be \"(.*)\"")]
    public void ThenTheRequestNameShouldBe(string expectedRequestName)
    {
        AssertResult();
        Assert.That(_result!.RequestName, Is.EqualTo(expectedRequestName));
    }

    [Then("the resolved URL should be \"(.*)\"")]
    public void ThenTheResolvedUrlShouldBe(string expectedUrl)
    {
        AssertResult();
        Assert.That(_result!.ResolvedUrl, Is.EqualTo(expectedUrl));
    }

    [Then("the status code should be (.*)")]
    public void ThenTheStatusCodeShouldBe(int expectedStatusCode)
    {
        AssertResult();
        Assert.That(_result!.StatusCode, Is.EqualTo(expectedStatusCode));
    }

    [Then("the response body should be \"(.*)\"")]
    public void ThenTheResponseBodyShouldBe(string expectedBody)
    {
        AssertResult();
        Assert.That(_result!.ResponseBody, Is.EqualTo(expectedBody));
    }

    private void AssertResult()
    {
        // Guard to keep failure messages clear if When step failed.
        Assert.That(_result, Is.Not.Null, "Collection runner did not return a result.");
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
