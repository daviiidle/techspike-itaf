using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using NUnit.Framework;
using PostmanRunnerSpike.Models;
using PostmanRunnerSpike.Services;

namespace PlacementRunner.Specs;

[TestFixture]
public sealed class CollectionRunnerTests
{
    [Test]
    public async Task RunRepositoryAsync_preserves_order_applies_collection_auth_and_persists_cookies()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            WriteRepository(
                tempRoot,
                "repo-a",
                """
                {
                  "values": [
                    { "key": "ServerName", "value": "svc" },
                    { "key": "Port_Auto_PP_http", "value": "8443" },
                    { "key": "ApiKey", "value": "secret" }
                  ]
                }
                """,
                null,
                ("Regression.postman_collection.json",
                """
                {
                  "info": { "name": "Regression Pack" },
                  "variable": [
                    { "key": "BasePath", "value": "api/v1" }
                  ],
                  "auth": {
                    "type": "apikey",
                    "apikey": [
                      { "key": "key", "value": "x-api-key" },
                      { "key": "value", "value": "{{ApiKey}}" },
                      { "key": "in", "value": "query" }
                    ]
                  },
                  "item": [
                    {
                      "name": "Folder A",
                      "item": [
                        {
                          "name": "Create Case",
                          "variable": [
                            { "key": "RequestId", "value": "42" }
                          ],
                          "request": {
                            "method": "POST",
                            "header": [
                              { "key": "Content-Type", "value": "application/json" },
                              { "key": "X-Disabled", "value": "skip", "disabled": true }
                            ],
                            "auth": { "type": "inherit" },
                            "url": {
                              "protocol": "https",
                              "host": [ "{{ServerName}}", "spike", "local" ],
                              "port": "{{Port_Auto_PP_http}}",
                              "path": [ "{{BasePath}}", "CreateCaseModel" ],
                              "query": [
                                { "key": "trace", "value": "{{RequestId}}" },
                                { "key": "ignored", "value": "1", "disabled": true }
                              ]
                            },
                            "body": {
                              "mode": "raw",
                              "raw": "{ \"id\": \"{{RequestId}}\" }",
                              "options": {
                                "raw": { "language": "json" }
                              }
                            }
                          },
                          "event": [
                            {
                              "listen": "test",
                              "script": {
                                "exec": [
                                  "pm.test(\"accepted\", function () {",
                                  "pm.response.to.have.status(202);",
                                  "});",
                                  "const responseJson = pm.response.json();",
                                  "pm.expect(responseJson.data.status).to.eql(\"Accepted\");"
                                ]
                              }
                            }
                          ]
                        },
                        {
                          "name": "Health",
                          "request": {
                            "method": "GET",
                            "auth": { "type": "noauth" },
                            "url": {
                              "raw": "https://{{ServerName}}.spike.local:{{Port_Auto_PP_http}}/health"
                            }
                          }
                        }
                      ]
                    }
                  ]
                }
                """));

            var seenRequests = new List<HttpRequestMessage>();
            var requestExecutor = new RequestExecutor((allowInvalidCertificates, cookies) =>
                new RecordingHandler(async request =>
                {
                    seenRequests.Add(CloneRequest(request));
                    if (request.RequestUri!.AbsolutePath.EndsWith("/CreateCaseModel", StringComparison.Ordinal))
                    {
                        Assert.That(request.RequestUri!.Query, Is.EqualTo("?trace=42&x-api-key=secret"));
                        Assert.That(request.Headers.Contains("X-Disabled"), Is.False);
                        Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
                        Assert.That(await request.Content.ReadAsStringAsync(), Is.EqualTo("{ \"id\": \"42\" }"));

                        var response = new HttpResponseMessage(HttpStatusCode.Accepted)
                        {
                            Content = new StringContent("{\"data\":{\"status\":\"Accepted\"}}", Encoding.UTF8, "application/json")
                        };
                        response.Headers.Add("Set-Cookie", "session=abc; path=/");
                        return response;
                    }

                    Assert.That(request.RequestUri!.ToString(), Is.EqualTo("https://svc.spike.local:8443/health"));
                    Assert.That(request.RequestUri.Query, Is.EqualTo(string.Empty));
                    Assert.That(request.Headers.Contains("Cookie"), Is.True);
                    Assert.That(string.Join(";", request.Headers.GetValues("Cookie")), Does.Contain("session=abc"));
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("healthy")
                    };
                }));

            var results = await CreateRunner(requestExecutor).RunRepositoryAsync(tempRoot, "repo-a", mockMode: false);

            Assert.That(results.Select(static item => item.RequestPath), Is.EqualTo(new[]
            {
                "Folder A / Create Case",
                "Folder A / Health"
            }));

            Assert.Multiple(() =>
            {
                Assert.That(results[0].ResolvedUrl, Is.EqualTo("https://svc.spike.local:8443/api/v1/CreateCaseModel?trace=42&x-api-key=secret"));
                Assert.That(results[0].AuthTypeApplied, Is.EqualTo("apikey"));
                Assert.That(results[0].Succeeded, Is.True);
                Assert.That(results[0].AssertionResults.Any(static item => item.Outcome == "passed"), Is.True);
                Assert.That(results[1].AuthTypeApplied, Is.EqualTo("noauth"));
                Assert.That(results[1].Succeeded, Is.True);
                Assert.That(seenRequests, Has.Count.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public async Task RunRepositoryAsync_does_not_execute_unresolved_requests_and_continues()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            WriteRepository(
                tempRoot,
                "repo-b",
                """
                {
                  "values": [
                    { "key": "ServerName", "value": "localhost" },
                    { "key": "Port", "value": "8080" }
                  ]
                }
                """,
                null,
                ("Regression.postman_collection.json",
                """
                {
                  "info": { "name": "Unresolved Pack" },
                  "item": [
                    {
                      "name": "Broken Request",
                      "request": {
                        "method": "GET",
                        "url": {
                          "raw": "http://{{ServerName}}:{{MissingPort}}/broken"
                        }
                      }
                    },
                    {
                      "name": "Working Request",
                      "request": {
                        "method": "GET",
                        "url": {
                          "raw": "http://{{ServerName}}:{{Port}}/ok"
                        }
                      }
                    }
                  ]
                }
                """));

            var sentRequests = 0;
            var requestExecutor = new RequestExecutor((allowInvalidCertificates, cookies) =>
                new RecordingHandler(request =>
                {
                    sentRequests++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("ok")
                    });
                }));

            var results = await CreateRunner(requestExecutor).RunRepositoryAsync(tempRoot, "repo-b", mockMode: false);

            Assert.Multiple(() =>
            {
                Assert.That(results, Has.Count.EqualTo(2));
                Assert.That(results[0].Succeeded, Is.False);
                Assert.That(results[0].UnresolvedVariables, Is.EqualTo(new[] { "MissingPort" }));
                Assert.That(results[0].StatusCode, Is.EqualTo(0));
                Assert.That(results[1].Succeeded, Is.True);
                Assert.That(results[1].StatusCode, Is.EqualTo(200));
                Assert.That(sentRequests, Is.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public async Task RunRepositoryAsync_supports_external_auth_urlencoded_and_formdata_and_strict_ssl()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            WriteRepository(
                tempRoot,
                "repo-c",
                """
                {
                  "values": [
                    { "key": "ServerName", "value": "localhost" },
                    { "key": "Token", "value": "runner-token" }
                  ]
                }
                """,
                """
                {
                  "type": "bearer",
                  "token": "{{Token}}"
                }
                """,
                ("Bodies.postman_collection.json",
                """
                {
                  "info": { "name": "Bodies Pack" },
                  "item": [
                    {
                      "name": "Submit Form",
                      "protocolProfileBehavior": { "strictSSL": false },
                      "request": {
                        "method": "POST",
                        "header": [
                          { "key": "X-Sent", "value": "yes" }
                        ],
                        "url": {
                          "raw": "https://{{ServerName}}/submit"
                        },
                        "body": {
                          "mode": "urlencoded",
                          "urlencoded": [
                            { "key": "enabled", "value": "1" },
                            { "key": "disabled", "value": "2", "disabled": true }
                          ]
                        }
                      }
                    },
                    {
                      "name": "Upload Metadata",
                      "request": {
                        "method": "POST",
                        "auth": { "type": "inherit" },
                        "url": {
                          "raw": "https://{{ServerName}}/upload"
                        },
                        "body": {
                          "mode": "formdata",
                          "formdata": [
                            { "key": "name", "value": "spike", "type": "text" },
                            { "key": "ignore", "value": "x", "type": "text", "disabled": true }
                          ]
                        }
                      }
                    }
                  ]
                }
                """));

            var strictSslFlags = new List<bool>();
            var requestExecutor = new RequestExecutor((allowInvalidCertificates, cookies) =>
            {
                strictSslFlags.Add(allowInvalidCertificates);
                return new RecordingHandler(async request =>
                {
                    if (request.RequestUri!.AbsolutePath == "/submit")
                    {
                        Assert.That(request.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
                        Assert.That(request.Headers.Authorization.Parameter, Is.EqualTo("runner-token"));
                        var body = await request.Content!.ReadAsStringAsync();
                        Assert.That(body, Is.EqualTo("enabled=1"));
                    }
                    else
                    {
                        Assert.That(request.Content, Is.TypeOf<MultipartFormDataContent>());
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("ok")
                    };
                });
            });

            var results = await CreateRunner(requestExecutor).RunRepositoryAsync(tempRoot, "repo-c", mockMode: false);

            Assert.Multiple(() =>
            {
                Assert.That(results, Has.Count.EqualTo(2));
                Assert.That(results.All(static item => item.Succeeded), Is.True);
                Assert.That(results.All(static item => item.AuthorizationHeader == "Bearer runner-token"), Is.True);
                Assert.That(strictSslFlags, Is.EqualTo(new[] { true, false }));
            });
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static CollectionRunner CreateRunner(RequestExecutor requestExecutor)
    {
        return new CollectionRunner(
            new PostmanCollectionParser(),
            new VariableResolver(),
            new AuthorizationService(),
            requestExecutor,
            new AssertionEvaluator());
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "postman-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteRepository(
        string externalRoot,
        string repositoryName,
        string environmentJson,
        string? authJson,
        params (string FileName, string CollectionJson)[] collections)
    {
        var repositoryRoot = Path.Combine(externalRoot, repositoryName, "tests");
        var collectionsRoot = Path.Combine(repositoryRoot, "collections");
        var dataRoot = Path.Combine(repositoryRoot, "data");
        Directory.CreateDirectory(collectionsRoot);
        Directory.CreateDirectory(dataRoot);

        File.WriteAllText(Path.Combine(dataRoot, "environment.json"), environmentJson);
        if (authJson is not null)
        {
            File.WriteAllText(Path.Combine(dataRoot, "collection_authorizationservice.json"), authJson);
        }

        foreach (var (fileName, collectionJson) in collections)
        {
            File.WriteAllText(Path.Combine(collectionsRoot, fileName), collectionJson);
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var content = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            clone.Content = new StringContent(content);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
