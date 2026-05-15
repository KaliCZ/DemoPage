using System.Diagnostics;

namespace Kalandra.AppHost;

// Runs the dev-time prerequisites Aspire doesn't own before the distributed
// app builds: npm install and the Supabase CLI stack. Marten's Postgres is
// declared in AppHost.cs so each run gets its own isolated database;
// Supabase is shared machine-wide (the CLI manages a single instance,
// and auth + storage are stateless test fixtures).
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

        Console.WriteLine("Starting Supabase...");
        RunSupabase(repoRoot, "start");
    }

    // Walks up from the build output directory until package.json is found.
    // Robust to how the AppHost is launched (dotnet run from anywhere, IDE,
    // dotnet exec on a published binary). AppHost.cs uses the result to
    // derive a per-worktree hash for volume and container-group names.
    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "package.json")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException(
            $"Couldn't find repo root: no package.json above {AppContext.BaseDirectory}");
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

    private static void RunSupabase(string repoRoot, params string[] args)
    {
        // Supabase CLI is installed as an npm devDependency, so it lives
        // in node_modules/.bin. Resolving it directly (rather than going
        // through `npm run`) avoids spawning a nested npm process from
        // inside the AppHost, which we already saw cause env-pollution
        // failures earlier.
        var supabase = ResolveLocalBin(repoRoot, OperatingSystem.IsWindows() ? "supabase.cmd" : "supabase");
        var psi = new ProcessStartInfo(supabase, args)
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {supabase}");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"`supabase {string.Join(" ", args)}` exited with code {process.ExitCode}");
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

    private static string ResolveLocalBin(string repoRoot, string exe)
    {
        var path = Path.Combine(repoRoot, "node_modules", ".bin", exe);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Couldn't find {exe} at {path}. Run `npm install` from the repo root.");
        }
        return path;
    }
}
