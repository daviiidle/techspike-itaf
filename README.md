# .NET Tech Spike: Submodules, Postman Runner, and Reqnroll Demo Tests

This repo is a tech spike for two related ideas:

1. Managing external automation repositories as Git submodules.
2. Replacing Newman/Postman for our current internal static regression collections with a .NET runner.

## Current Layout

```text
.net tech spike/
  README.md
  .net tech spike.sln
  submodules.example.json
  tools/
    SubmoduleTool/
  postman-runner-spike/
    external/
      mock-postman-repo/
      public-api-postman-repo/
      auth-postman-repo/
    src/
      Models/
      Services/
      Program.cs
  test-config/
    demo_api_environment.json
  test-secrets/
    demo_api_secrets.json
  tests/
    PlacementRunner.Specs/
      CollectionRunnerTests.cs
      Features/
      Steps/
```

## What The Postman Runner Does Now

The spike is no longer limited to a single flat request plus a mandatory auth file. It now supports the static Postman/Newman features we actually use across internal repos:

- Recursive collection parsing with nested folders and authored item order preserved
- Request-level auth, collection-level auth, and optional external auth fallback
- `noauth`, `bearer`, `basic`, `apikey`, and inherit/unspecified auth handling
- Collection variables, request variables, and environment variables with case-insensitive lookup
- Variable resolution in URL, query, headers, auth, and body
- URL reconstruction from Postman URL objects when `url.raw` is absent or incomplete
- `raw`, `urlencoded`, and `formdata` request bodies
- Disabled headers, query params, and body fields being skipped
- Per-collection cookie persistence
- `protocolProfileBehavior.strictSSL = false`
- Lightweight Postman test assertion support for the common static patterns in our collections
- Mock mode that still parses, resolves, applies auth, and produces execution results without real network calls

Out of scope by design:

- Full JavaScript/Postman runtime
- `pm.environment.set(...)`
- `pm.collectionVariables.set(...)`
- `pm.variables.set(...)`
- `postman.setNextRequest(...)`
- `pm.execution.setNextRequest(...)`
- `pm.iterationData.get(...)`
- Dynamic script-driven request/header/url mutation
- Runtime-generated values such as `CryptoJS` and `Date.now()`

## Runtime Flow

At runtime the runner processes a repo in this order:

1. Discover the repo under `postman-runner-spike/external/<repo-name>`.
2. Read collections from `tests/collections/*.postman_collection.json`.
3. Read `tests/data/environment.json` if present.
4. Read `tests/data/collection_authorizationservice.json` if present.
5. Parse each collection recursively and preserve exact collection item order.
6. For each request, merge variables with precedence:
   request variables -> collection variables -> environment variables
7. Resolve URL, query params, headers, auth values, and body content.
8. Resolve auth with precedence:
   request auth -> collection auth -> external auth -> none
9. Detect unresolved `{{variables}}`.
10. If unresolved variables remain, do not execute the request. Record the failure and continue.
11. Execute the request with the collection-run cookie container and per-request SSL policy.
12. Evaluate supported Postman test assertions and store pass/fail/unsupported results.
13. Continue even if a request, collection, or repo fails.

## What Happens With Multiple Collections

If a repo contains more than one file in `tests/collections`, the runner executes all of them.

- Collection files are discovered with `*.postman_collection.json`
- They are executed in deterministic filename order
- Requests inside each collection run in the exact order authored in Postman
- Cookies are shared within one collection run, not across different collection files
- One bad request does not stop the collection
- One bad collection does not stop the repo
- One bad repo does not stop `RunAllRepositoriesAsync`

## Repo Conventions

Current discovery is intentionally simple and deterministic:

`tests/collections/*.postman_collection.json`
- Required for collection execution

`tests/data/environment.json`
- Optional
- Missing file means no environment variables are loaded

`tests/data/collection_authorizationservice.json`
- Optional
- Missing file means no external auth fallback is loaded

The runner does not hardcode service names, hostnames, path suffixes, or port variable names. URLs are fully collection-driven and variable-driven, so patterns such as:

`https://{{ServerName}}.spike.local:{{Port_Auto_PP_http}}/api/v1/CreateCaseModel`

and

`https://{{ServerName}}.spike.local:{{Port_Auto_NOS_http}}/api/v1/NetworkOutageData`

both work as long as the referenced variables resolve for that repo.

## Supported Assertions

The lightweight assertion evaluator currently supports these common patterns:

- `pm.response.to.have.status(...)`
- `pm.expect(pm.response.text()).to.include("...")`
- `pm.response.json()`
- Simple equality assertions on parsed JSON paths such as:
  `pm.expect(responseJson.data.status).to.eql("Accepted")`

If a test line uses a `pm.*` pattern the evaluator does not understand, it is recorded as `unsupported` rather than being silently ignored.

## Results Produced Per Request

Each executed or skipped request records:

- Repository name
- Collection file name
- Collection name
- Request name
- Folder path / request path
- HTTP method
- Resolved URL
- Auth type applied
- Authorization header if one was applied
- Status code
- Response body
- Response headers
- Duration in ms
- Success/failure
- Error message
- Exception type
- Unresolved variables
- Assertion results

## Key Code Paths

`postman-runner-spike/src/Services/PostmanCollectionParser.cs`
- Parses collections recursively, including collection variables, request variables, auth blocks, URL objects, bodies, and raw event scripts

`postman-runner-spike/src/Services/VariableResolver.cs`
- Loads environment variables and performs repeated `{{variable}}` replacement plus unresolved-variable detection

`postman-runner-spike/src/Services/AuthorizationService.cs`
- Applies auth precedence and resolves `bearer`, `basic`, `apikey`, and `noauth`

`postman-runner-spike/src/Services/CollectionRunner.cs`
- Orchestrates discovery, parsing, resolution, execution, assertions, and failure isolation

`postman-runner-spike/src/Services/RequestExecutor.cs`
- Builds request content, applies headers correctly, manages cookie persistence, and scopes certificate validation

`postman-runner-spike/src/Services/AssertionEvaluator.cs`
- Evaluates the supported static Postman assertion patterns

## Tests

The older Reqnroll demo files are still in the repo, but the main verification for the current Postman-runner behaviour is now the focused NUnit coverage in:

[tests/PlacementRunner.Specs/CollectionRunnerTests.cs](/mnt/c/Users/D/.net tech spike/tests/PlacementRunner.Specs/CollectionRunnerTests.cs)

That test suite covers:

- Nested collection parsing and authored execution order
- Collection auth inheritance and request-level `noauth`
- Optional external auth
- URL resolution with repo-specific port variable names
- Disabled header/query/body fields
- Raw JSON, `urlencoded`, and `formdata`
- Cookie persistence
- `strictSSL:false`
- Unresolved-variable skip behaviour
- Assertion evaluation

## Commands

Fresh clone:

```powershell
git clone --recurse-submodules <PARENT_REPO_URL>
cd "<PARENT_REPO_FOLDER>"
```

If already cloned without submodules:

```powershell
git submodule update --init --recursive
```

Run submodule precondition helper:

```powershell
dotnet run --project .\tools\SubmoduleTool\SubmoduleTool.csproj -- precondition
```

Run the spike console app directly:

```powershell
dotnet run --project .\postman-runner-spike\src\postman-runner-spike.csproj
```

Run the focused Postman-runner tests:

```powershell
dotnet test .\tests\PlacementRunner.Specs\PlacementRunner.Specs.csproj --filter "FullyQualifiedName~CollectionRunnerTests"
```

Run all tests:

```powershell
dotnet test .\tests\PlacementRunner.Specs\PlacementRunner.Specs.csproj
```

## Parent Repo vs Submodule Repo

Commit inside a submodule when:

- You changed files in that child repository
- You need to push those changes to that child repo remote

Commit in the parent repo when:

- You changed the main framework files
- You changed docs, tests, or tools in this repo
- You updated the commit pointer for one of the submodules

If submodule pointers changed:

```powershell
git add external
git commit -m "Bump submodule pointers"
git push
```

## Notes

- This is still a spike, not a production-ready framework
- The current runner is intentionally scoped to the static Postman/Newman features we actually use now
- The code is structured so a richer runtime could be added later without rewriting the parser and models from scratch
