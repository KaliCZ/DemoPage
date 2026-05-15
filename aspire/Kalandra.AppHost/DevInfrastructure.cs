using System.Diagnostics;

namespace Kalandra.AppHost;

// Brings up the dev prerequisites Aspire doesn't own: npm install and the Supabase CLI stack.
internal static class DevInfrastructure
{
    public static void EnsureRunning()
    {
        var repoRoot = FindRepoRoot();

        // Skip when launched under `npm run` — the parent npm already ran install.
        if (Environment.GetEnvironmentVariable("npm_lifecycle_event") is null)
        {
            Console.WriteLine("Installing npm dependencies (root + frontend)...");
            RunNpm(repoRoot, "install");
        }

        Console.WriteLine("Starting Supabase...");
        RunSupabase(repoRoot, "start");
    }

    // Works regardless of how the AppHost is launched, since the cwd isn't reliable across launchers.
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
        // Inherited npm_* vars from a parent `npm run` can misdirect the nested CLI; strip them.
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
        // Resolve the CLI directly from node_modules/.bin to avoid spawning a nested npm.
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

    // Returns an absolute npm path; on Windows, launching npm.cmd by name breaks `%~dp0` inside the script.
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
