using PostmanRunnerSpike.Services;

// Find the project root so this can run from build output folders too.
var root = FindSpikeRoot();
// Point the runner at the external repo root and let it discover collections from there.
var externalRoot = Path.Combine(root, "external");
var repositoryName = "auth-postman-repo";

// Wire up services manually for this simple spike.
var parser = new PostmanCollectionParser();
var environmentResolver = new EnvironmentResolver();
var authorizationService = new AuthorizationService();
var requestExecutor = new RequestExecutor(new HttpClient());
var runner = new CollectionRunner(parser, environmentResolver, authorizationService, requestExecutor);

// Run against the live public endpoint for this plumbing test.
var results = await runner.RunRepositoryAsync(
    externalRoot,
    repositoryName,
    mockMode: false);

if (results.Count == 0)
{
    throw new InvalidOperationException($"No executable requests were discovered in repository '{repositoryName}'.");
}

foreach (var result in results)
{
    // Print the same key fields someone would want to verify quickly.
    Console.WriteLine($"Request name: {result.RequestName}");
    Console.WriteLine($"Resolved URL: {result.ResolvedUrl}");
    Console.WriteLine($"Authorization header: {result.AuthorizationHeader}");
    Console.WriteLine($"Status code: {result.StatusCode}");
    Console.WriteLine($"Response body: {result.ResponseBody}");

    // Minimal plumbing validation for the public auth call.
    if (result.StatusCode != 200)
    {
        throw new InvalidOperationException($"Expected status code 200 but got {result.StatusCode}.");
    }

    if (!string.Equals(result.AuthorizationHeader, "Bearer spike-test-token", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected Authorization header 'Bearer spike-test-token' but got '{result.AuthorizationHeader}'.");
    }

    if (!result.ResponseBody.Contains("\"authenticated\": true", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Expected response body to contain '\"authenticated\": true'.");
    }
}

static string FindSpikeRoot()
{
    // Start from the running binary folder and walk upward.
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null)
    {
        // This known folder is our marker that we found the spike root.
        var candidate = Path.Combine(current.FullName, "external");
        if (Directory.Exists(candidate))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate spike root containing external.");
}
