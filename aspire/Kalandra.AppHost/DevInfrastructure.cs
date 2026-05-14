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
// and removes "I forgot to npm install" / "I forgot to start the DB"
// footguns. Any entry point that runs the AppHost (npm run aspire, the
// IDE, plain `dotnet run`) gets a working stack with no prerequisites.
//
// We deliberately don't stop the containers on shutdown: Ctrl+C-ing the
// AppHost leaves them running so dev DB state survives across runs.
// `npm run dev:stop` is the explicit cleanup.
internal static class DevInfrastructure
{
    public static void EnsureRunning()
    {
        var repoRoot = FindRepoRoot();

        Console.WriteLine("Installing npm dependencies (root + frontend)...");
        RunNpm(repoRoot, "install");

        Console.WriteLine("Starting dev infrastructure (Postgres + Supabase)...");
        RunNpm(repoRoot, "run", "dev:infra");
    }

    private static void RunNpm(string repoRoot, params string[] args)
    {
        var npm = OperatingSystem.IsWindows() ? "npm.cmd" : "npm";
        var psi = new ProcessStartInfo(npm, args)
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {npm}");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine($"`npm {string.Join(" ", args)}` exited with code {process.ExitCode}");
            Environment.Exit(process.ExitCode);
        }
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
