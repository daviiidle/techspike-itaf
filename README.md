# .NET Tech Spike: Submodules, Postman Runner, and Reqnroll Demo Tests

This repo is a tech spike for two related ideas:

1. Managing external automation repositories as Git submodules.
2. Running a small .NET/Reqnroll automation demo that reads Postman-style assets and also supports a separate config-driven live API test.

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
      fake-postman-repo/
        tests/
          collections/
            FakeHealthCheck.postman_collection.json
          data/
            environment.json
            collection_authorizationservice.json
    src/
      ...
  test-config/
    demo_api_environment.json
  test-secrets/
    demo_api_secrets.json
  tests/
    PlacementRunner.Specs/
      Features/
        PlacementRunner.feature
        JsonPlaceholderDemo.feature
      Steps/
        PlacementRunnerSteps.cs
        JsonPlaceholderDemoSteps.cs
```

## Key Separation

There are two different sources of test data in this spike.

`postman-runner-spike/external/fake-postman-repo/tests/...`
- Holds fake Postman-style assets.
- Used by the collection runner mock demo.
- This represents data that could come from an external automation repository.

`test-config/...`
- Holds framework-level config that should stay outside the external fake repo.
- Used by the live RestSharp demo.
- This is the cleaner pattern for enterprise automation where environment values and secrets/config are managed separately.

`test-secrets/...`
- Holds framework-level secret values separate from normal config.
- Used by the live RestSharp demo for bearer-token style auth.
- This is where secret material should live instead of inside the external repo.

## What Exists Today

`postman-runner-spike/src`
- Minimal C# runner that parses a Postman collection, resolves environment variables, applies auth, and executes requests.
- Supports mock mode so no real HTTP call is required for the main spike flow.

`tests/PlacementRunner.Specs/Features/PlacementRunner.feature`
- Reqnroll feature that runs the collection runner against the fake Postman assets.
- Uses files from `postman-runner-spike/external/fake-postman-repo/tests/...`.

`tests/PlacementRunner.Specs/Features/JsonPlaceholderDemo.feature`
- Reqnroll feature that calls the public demo API at runtime using `RestSharp`.
- Reads its base URL and resource path from `test-config/demo_api_environment.json`.
- Reads its token from `test-secrets/demo_api_secrets.json`.

## Recommended Flow

1. Clone the repo with submodules.
2. Initialize/update submodules.
3. Run the spike app if you want to validate the core collection runner directly.
4. Run the Reqnroll spec project to validate both demo scenarios.
5. If submodule pointers changed, commit those changes in the parent repo.

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

Run submodule precondition and intentionally pull latest child repo changes:

```powershell
dotnet run --project .\tools\SubmoduleTool\SubmoduleTool.csproj -- precondition --pull-latest
```

Run the spike console app directly:

```powershell
dotnet run --project .\postman-runner-spike\src\postman-runner-spike.csproj
```

Run all Reqnroll tests:

```powershell
dotnet test .\tests\PlacementRunner.Specs\PlacementRunner.Specs.csproj -c Debug
```

Run only the Postman runner mock scenario:

```powershell
dotnet test .\tests\PlacementRunner.Specs\PlacementRunner.Specs.csproj -c Debug --filter "FullyQualifiedName~PlacementRunner"
```

Run only the live RestSharp demo scenario:

```powershell
dotnet test .\tests\PlacementRunner.Specs\PlacementRunner.Specs.csproj -c Debug --filter "FullyQualifiedName~JsonPlaceholder"
```

## Which Test Uses Which Data

`PlacementRunner.feature`
- Uses fake Postman assets under `postman-runner-spike/external/fake-postman-repo/tests/collections`
- Uses fake environment/auth files under `postman-runner-spike/external/fake-postman-repo/tests/data`
- Runs through the internal collection runner in mock mode
- This path still works and passes today

`JsonPlaceholderDemo.feature`
- Uses separate config from `test-config/demo_api_environment.json`
- Uses separate secret values from `test-secrets/demo_api_secrets.json`
- Uses `RestSharp` for a live HTTP GET against a public demo API
- Does not depend on fake Postman collection files
- This path also works and passes today

## Current Working State

Both demo paths are working.

Fake Postman repo path:
- Reads collection and fake environment/auth files from `postman-runner-spike/external/fake-postman-repo/tests/...`
- Covered by `PlacementRunner.feature`
- Passes

Separate framework config/secrets path:
- Reads environment config from `test-config/demo_api_environment.json`
- Reads token/secret data from `test-secrets/demo_api_secrets.json`
- Covered by `JsonPlaceholderDemo.feature`
- Passes

## Parent Repo vs Submodule Repo

Commit inside a submodule when:

- You changed files in that child repository.
- You need to push those changes to that child repo remote.

Commit in the parent repo when:

- You changed the main framework files.
- You changed docs, tests, or tools in this repo.
- You updated the commit pointer for one of the submodules.

If submodule pointers changed:

```powershell
git add external
git commit -m "Bump submodule pointers"
git push
```

## Why Submodules Instead of Nested Normal Repos

If you drop child repos into a parent folder as normal nested Git repos, parent Git commands become misleading:

- `git add .` in the parent will not track inner repo files the way people expect.
- `git commit` in the parent will not include child repo commits.
- `git push` in the parent does not push child repo changes.

Submodules solve that by making the parent repo track the exact child commit intentionally.

## Notes

- This is a tech spike, not production-ready automation.
- No real secrets are used.
- The mock collection runner test and the live RestSharp demo are intentionally separate examples.
- The live RestSharp demo still uses a fake token value, but the file location and loading pattern now mimic a real enterprise secret/config split.
- The Reqnroll spec project is included in the solution so Visual Studio can resolve step definitions correctly.
