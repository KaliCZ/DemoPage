using System.Diagnostics;

namespace Kalandra.AppHost;

// Ensures every dev-time prerequisite is in place before Aspire builds:
//
//   - npm dependencies (root + frontend via the postinstall hook)
//   - Postgres via `docker compose`
//   - Supabase via the Supabase CLI
//
// All three commands are idempotent — calling them when nothing has
// changed is a fast no-op — so running this every AppHost boot is cheap
// and removes "I forgot to start the DB" footguns. Any entry point that
// runs the AppHost (IDE click, plain `dotnet run`) gets a working stack
// with no prerequisites.
//
// We deliberately don't stop the containers on shutdown: Ctrl+C-ing the
// AppHost leaves them running so dev DB state survives across runs.
// `npm run dev:stop` is the explicit cleanup.
internal static class DevInfrastructure
{
    public static void EnsureRunning()
    {
        var repoRoot = FindRepoRoot();

        // When invoked under `npm run`, the user is already managing npm
        // themselves (`npm run aspire` chains `npm install` ahead of us).
        // Skip the nested install in that case to avoid running npm from
        // inside another npm process.
        if (Environment.GetEnvironmentVariable("npm_lifecycle_event") is null)
        {
            Console.WriteLine("Installing npm dependencies (root + frontend)...");
            RunNpm(repoRoot, "install");
        }

        Console.WriteLine("Starting dev infrastructure (Postgres + Supabase)...");
        RunNpm(repoRoot, "run", "dev:infra");
    }

    private static void RunNpm(string repoRoot, params string[] args)
    {
        var npm = ResolveNpm();
        var psi = new ProcessStartInfo(npm, args)
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };
        // When the AppHost is launched via `npm run aspire`, the parent
        // npm injects npm_execpath / npm_config_* into our env. Forwarded
        // to a nested npm, those can point the child at the wrong CLI and
        // produce confusing "Cannot find module npm-prefix.js" failures.
        // Strip them so the child boots from a clean slate.
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("npm_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            psi.Environment.Remove(key);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {npm}");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"`npm {string.Join(" ", args)}` exited with code {process.ExitCode}");
            Environment.Exit(process.ExitCode);
        }
    }

    // Returns the absolute path to npm. Resolving it ourselves (instead of
    // letting Process.Start search PATH from a relative "npm.cmd") matters
    // on Windows: when cmd.exe runs a relative .cmd name, `%~dp0` inside
    // the script evaluates against the working directory rather than the
    // script's actual location, so npm.cmd ends up looking for npm-cli.js
    // next to the repo root and fails. Passing an absolute path fixes it.
    private static string ResolveNpm()
    {
        var exe = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            var candidate = Path.Combine(dir.Trim(), exe);
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException($"Couldn't find {exe} on PATH");
    }

    // Walks up from the build output directory until package.json is found.
    // Robust to how the AppHost is launched (dotnet run from anywhere, IDE,
    // dotnet exec on a published binary).
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "package.json")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException(
            $"Couldn't find repo root: no package.json above {AppContext.BaseDirectory}");
    }
}
