using PostmanRunnerSpike.Models;
using PostmanRunnerSpike.Services;

var root = FindSpikeRoot();
var externalRoot = Path.Combine(root, "external");
var repositoryName = "mock-postman-repo";

var variableResolver = new VariableResolver();
var authorizationService = new AuthorizationService();
var runner = new CollectionRunner(
    new PostmanCollectionParser(),
    variableResolver,
    new RequestResolver(variableResolver, authorizationService),
    authorizationService,
    new RequestExecutor(),
    new AssertionEvaluator());

var results = await runner.RunRepositoryAsync(
    externalRoot,
    repositoryName,
    mockMode: true);

foreach (var result in results)
{
    Console.WriteLine($"{result.CollectionFileName} :: {result.RequestPath} :: {result.StatusCode} :: {result.Succeeded}");
    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
    {
        Console.WriteLine($"  error: {result.ErrorMessage}");
    }
}

static string FindSpikeRoot()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, "external");
        if (Directory.Exists(candidate))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate spike root containing external.");
}
