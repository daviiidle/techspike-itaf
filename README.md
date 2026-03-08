# .NET Tech Spike: Managing ITAF/Postman Repositories as Git Submodules

This spike keeps external repositories inside one main framework repository by using Git submodules, with orchestration done in C# instead of PowerShell.

## Recommended End-to-End Flow

Yes, your flow is correct.

1. Pull/initialize submodules first.
2. Update docs/config (for example `README.md`) in the parent repo.
3. Run the runner/tests that use Reqnroll feature files + step definitions.
4. Commit parent changes (including any submodule pointer changes).

Commands:

```bash
# fresh clone
git clone --recurse-submodules <PARENT_REPO_URL>
cd <PARENT_REPO_FOLDER>

# ensure all submodules are present and pinned correctly
dotnet run --project ./tools/SubmoduleTool -- precondition

# optional: pull latest child-repo branches intentionally
dotnet run --project ./tools/SubmoduleTool -- precondition --pull-latest

# run spike app directly
dotnet run --project ./postman-runner-spike/src/postman-runner-spike.csproj

# run Reqnroll/NUnit acceptance test (feature + step defs)
dotnet test ./tests/PlacementRunner.Specs/PlacementRunner.Specs.csproj
```

If `--pull-latest` moves any submodules to newer commits, commit the updated pointers in the parent repo:

```bash
git add external
git commit -m "Bump submodule pointers after test update"
git push
```

## Why Submodules (and not nested repos as plain folders)

If you clone repos into a parent folder as-is (with their own `.git` directories), Git treats each nested repo as an independent repository boundary:

- `git add .` in the parent does not track child files the way you expect.
- `git commit` in the parent will not include inner repo file-level changes.
- `git push` from the parent does not push child repo commits.

Result: confusion and missing changes.

Submodules solve this by storing each child repo as a gitlink (a pointer to an exact commit) in the parent repo. The parent tracks exactly which commit each child repo must be at.

## Folder Structure (example)

```text
main-framework/
  README.md
  .gitmodules
  tools/
    SubmoduleTool/
      SubmoduleTool.csproj
      Program.cs
  external/
    postman-collection-a/   # submodule
    itaf-tests-b/           # submodule
```

## One-Time Setup in Parent Repo

Initialize parent repo:

```bash
git init
git add .
git commit -m "Initial tech spike scaffold"
```

Add submodules (example):

```bash
# Option A: explicit add commands
git submodule add https://github.com/your-org/postman-collection-a.git external/postman-collection-a
git submodule add https://github.com/your-org/itaf-tests-b.git external/itaf-tests-b

# Option B: use .NET helper + manifest
dotnet run --project ./tools/SubmoduleTool -- add-from-config ./submodules.example.json
```

Commit submodule registration:

```bash
git add .gitmodules external
git commit -m "Add ITAF/Postman repositories as submodules"
```

## Fresh Clone

Recommended single command:

```bash
git clone --recurse-submodules <PARENT_REPO_URL>
```

If already cloned without submodules:

```bash
git submodule update --init --recursive
```

## .NET Precondition Tool

Run this before local test runs or CI jobs:

```bash
# Init + update + status only
dotnet run --project ./tools/SubmoduleTool -- precondition

# Also pull latest changes in each submodule's current branch
dotnet run --project ./tools/SubmoduleTool -- precondition --pull-latest
```

## Example Commands You Requested

Fresh clone:

```bash
git clone --recurse-submodules <PARENT_REPO_URL>
```

Update all submodules:

```bash
dotnet run --project ./tools/SubmoduleTool -- init-update
```

Check status of all submodules:

```bash
dotnet run --project ./tools/SubmoduleTool -- status
```

Pull latest changes in all submodules:

```bash
dotnet run --project ./tools/SubmoduleTool -- pull
```

Commit updated submodule pointers in parent:

```bash
# after submodules moved to newer commits
git add external
git commit -m "Bump submodule pointers"
git push
```

## Parent Commit vs Submodule Commit

Commit inside submodule when:

- You changed files in that child repository.
- You need to push those child changes to its remote.

Commit in parent when:

- You changed which child commit should be used.
- You added/removed submodules.
- You changed parent files/tools/docs.

Typical flow when updating a child repo:

1. `cd external/postman-collection-a`
2. Commit + push child changes.
3. `cd ../../`
4. `git add external/postman-collection-a`
5. `git commit -m "Bump postman-collection-a submodule pointer"`
6. `git push`

## Local Dev Recommendation

- Run the precondition command before builds/tests.
- Default to pinned submodule commits for reproducibility.
- Only use `--pull-latest` when intentionally updating dependencies.

## CI Recommendation

- Always initialize submodules in checkout step.
- Use pinned submodule commits for deterministic builds.
- If CI pulls latest submodule branches, treat it as a separate non-deterministic lane.

GitHub Actions example:

```yaml
- uses: actions/checkout@v4
  with:
    submodules: recursive

- name: Submodule precondition
  run: dotnet run --project ./tools/SubmoduleTool -- precondition
```

## Tech Spike Scope

This is intentionally simple and not production-ready.

- No branch policy enforcement.
- No auth/token bootstrap logic.
- No automatic PR creation for submodule bumps.
