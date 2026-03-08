using System.Diagnostics;
using System.Text.Json;

// Small CLI helper for day-to-day Git submodule maintenance.
return await SubmoduleTool.RunAsync(args);

static class SubmoduleTool
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        // Default to recursive behavior so nested submodules are covered.
        var recursive = !args.Contains("--no-recursive", StringComparer.OrdinalIgnoreCase);
        // Optional mode for intentionally updating child repos.
        var pullLatest = args.Contains("--pull-latest", StringComparer.OrdinalIgnoreCase);

        try
        {
            EnsureInGitRepo();

            var command = args[0].ToLowerInvariant();
            return command switch
            {
                "init-update" => await InitUpdateAsync(recursive),
                "status" => await StatusAsync(recursive),
                "pull" => await PullAsync(recursive),
                "precondition" => await PreconditionAsync(recursive, pullLatest),
                "add-from-config" => await AddFromConfigAsync(args),
                "help" or "--help" or "-h" => PrintHelpAndExit(),
                _ => PrintUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    static async Task<int> InitUpdateAsync(bool recursive)
    {
        // Ensures each configured submodule exists at the pinned commit.
        Console.WriteLine("=== Submodule init/update ===");
        return await RunGitAsync($"submodule update --init{RecursiveArg(recursive)}");
    }

    static async Task<int> StatusAsync(bool recursive)
    {
        // Show both pointer summary and detailed status per submodule.
        Console.WriteLine("=== Submodule status summary ===");
        var code = await RunGitAsync($"submodule status{RecursiveArg(recursive)}");
        if (code != 0) return code;

        Console.WriteLine();
        Console.WriteLine("=== git status in each submodule ===");
        return await RunGitAsync($"submodule foreach{RecursiveArg(recursive)} \"git status --short --branch\"");
    }

    static async Task<int> PullAsync(bool recursive)
    {
        // Fast-forward pulls only, to avoid implicit merge commits.
        Console.WriteLine("=== Pull latest in each submodule (ff-only) ===");
        return await RunGitAsync($"submodule foreach{RecursiveArg(recursive)} \"git pull --ff-only\"");
    }

    static async Task<int> PreconditionAsync(bool recursive, bool pullLatest)
    {
        // Main preflight flow used by local dev/CI scripts.
        var code = await InitUpdateAsync(recursive);
        if (code != 0) return code;

        Console.WriteLine();
        code = await StatusAsync(recursive);
        if (code != 0) return code;

        if (!pullLatest)
        {
            Console.WriteLine();
            Console.WriteLine("Submodule precondition completed.");
            return 0;
        }

        Console.WriteLine();
        code = await PullAsync(recursive);
        if (code != 0) return code;

        Console.WriteLine();
        code = await StatusAsync(recursive);
        if (code != 0) return code;
        Console.WriteLine();
        Console.WriteLine("If any submodule commit changed, commit updated pointers in the parent repo.");

        return 0;
    }

    static async Task<int> AddFromConfigAsync(string[] args)
    {
        // Reads a JSON manifest and adds missing submodules.
        if (args.Length < 2)
        {
            WriteError("Missing config path. Usage: add-from-config <path-to-json>");
            return 1;
        }

        var path = args[1];
        if (!File.Exists(path))
        {
            WriteError($"Config file not found: {path}");
            return 1;
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var config = await JsonSerializer.DeserializeAsync<SubmoduleConfig>(File.OpenRead(path), options);
        if (config?.Submodules is null || config.Submodules.Count == 0)
        {
            WriteError("Config must contain a non-empty 'submodules' array.");
            return 1;
        }

        foreach (var item in config.Submodules)
        {
            if (string.IsNullOrWhiteSpace(item.Url) || string.IsNullOrWhiteSpace(item.Path))
            {
                WriteError("Each submodule entry must have 'url' and 'path'.");
                return 1;
            }

            if (Directory.Exists(item.Path))
            {
                Console.WriteLine($"Skipping existing path: {item.Path}");
                continue;
            }

            Console.WriteLine($"Adding submodule {item.Url} -> {item.Path}");
            var code = await RunGitAsync($"submodule add {Escape(item.Url)} {Escape(item.Path)}");
            if (code != 0) return code;
        }

        Console.WriteLine("Done. Commit .gitmodules and submodule paths in the parent repo.");
        return 0;
    }

    static void EnsureInGitRepo()
    {
        // Guardrail to prevent confusing failures outside a repo.
        var code = RunGitAsync("rev-parse --is-inside-work-tree", printHeader: false).GetAwaiter().GetResult();
        if (code != 0)
        {
            throw new InvalidOperationException("Current directory is not inside a Git repository.");
        }
    }

    static async Task<int> RunGitAsync(string arguments, bool printHeader = true)
    {
        // Run git as a subprocess and stream output directly.
        if (printHeader)
        {
            Console.WriteLine($"> git {arguments}");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return process.ExitCode;
    }

    static string RecursiveArg(bool recursive) => recursive ? " --recursive" : string.Empty;

    static int PrintUnknownCommand(string command)
    {
        WriteError($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }

    static int PrintHelpAndExit()
    {
        PrintUsage();
        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine("SubmoduleTool commands:");
        Console.WriteLine("  init-update [--no-recursive]");
        Console.WriteLine("  status [--no-recursive]");
        Console.WriteLine("  pull [--no-recursive]");
        Console.WriteLine("  precondition [--pull-latest] [--no-recursive]");
        Console.WriteLine("  add-from-config <path-to-json>");
    }

    static void WriteError(string message)
    {
        Console.Error.WriteLine($"ERROR: {message}");
    }

    static string Escape(string value)
    {
        // Keep argument handling simple for this spike.
        if (value.Contains('"'))
        {
            throw new InvalidOperationException("Arguments containing double quotes are not supported in this spike.");
        }

        if (value.Contains(' ') || value.Contains('\t'))
        {
            return $"\"{value}\"";
        }

        return value;
    }
}

// Shape of submodule manifest JSON file.
sealed class SubmoduleConfig
{
    public List<SubmoduleEntry> Submodules { get; set; } = [];
}

// One submodule row in the manifest.
sealed class SubmoduleEntry
{
    public string Url { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
