using PostmanRunnerSpike.Services;

// Find the project root so this can run from build output folders too.
var root = FindSpikeRoot();
// Build absolute paths to the fake external Postman assets.
var collectionPath = Path.Combine(root, "external", "fake-postman-repo", "tests", "collections", "FakeHealthCheck.postman_collection.json");
var environmentPath = Path.Combine(root, "external", "fake-postman-repo", "tests", "data", "environment.json");
var authorizationPath = Path.Combine(root, "external", "fake-postman-repo", "tests", "data", "collection_authorizationservice.json");

// Wire up services manually for this simple spike.
var parser = new PostmanCollectionParser();
var environmentResolver = new EnvironmentResolver();
var authorizationService = new AuthorizationService();
var requestExecutor = new RequestExecutor(new HttpClient());
var runner = new CollectionRunner(parser, environmentResolver, authorizationService, requestExecutor);

// Run in mock mode so no real network calls are made.
var result = await runner.RunAsync(
    collectionPath,
    environmentPath,
    authorizationPath,
    mockMode: true);

// Print the same key fields someone would want to verify quickly.
Console.WriteLine($"Request name: {result.RequestName}");
Console.WriteLine($"Resolved URL: {result.ResolvedUrl}");
Console.WriteLine($"Status code: {result.StatusCode}");
Console.WriteLine($"Response body: {result.ResponseBody}");

static string FindSpikeRoot()
{
    // Start from the running binary folder and walk upward.
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null)
    {
        // This known folder is our marker that we found the spike root.
        var candidate = Path.Combine(current.FullName, "external", "fake-postman-repo");
        if (Directory.Exists(candidate))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate spike root containing external/fake-postman-repo.");
}
