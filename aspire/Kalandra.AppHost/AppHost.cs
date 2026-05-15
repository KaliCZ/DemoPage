using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Kalandra.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Supabase (auth + storage) is owned by its CLI, not Aspire — the CLI
// keeps a single shared instance machine-wide. Aspire only references
// Supabase's endpoints as external services, so we bootstrap them here
// before the distributed app builds. Marten's Postgres goes the other
// way: Aspire owns the container so each run gets an isolated DB.
DevInfrastructure.EnsureRunning();

// AppHost-owned ports (dashboard, OTLP gRPC, OTLP HTTP, resource service)
// are reserved under a named Mutex so parallel AppHosts can't race for the
// same port. The mutex guards two narrow critical sections: the initial
// probe-and-bind below, and the later release-and-hand-off to Aspire.
using var portMutex = new Mutex(initiallyOwned: false, "Kalandra-Aspire-Ports");

AppHostPorts ports;
AcquireMutex(portMutex);
try
{
    ports = PortReservation.Reserve();
}
finally
{
    portMutex.ReleaseMutex();
}

Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://localhost:{ports.Dashboard.Port}");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", $"http://localhost:{ports.Otlp.Port}");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", $"http://localhost:{ports.OtlpHttp.Port}");
Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", $"http://localhost:{ports.Resource.Port}");
Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");
// Let the browser OTLP exporter POST cross-origin from the Astro dev
// server without credentials. Safe because the AppHost binds to loopback;
// would be unacceptable on any real network.
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDORIGINS", "*");
Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDHEADERS", "*");

var dashboardUrl = $"http://localhost:{ports.Dashboard.Port}";
// Stay quiet in the common case so Aspire's own boot logs aren't drowned
// out; surface the port-source label only when something unusual happened
// (offset pinning or stepped past a collision).
if (ports.Source != "default ports")
{
    Console.WriteLine($"Aspire ports: {ports.Source}");
}

var builder = DistributedApplication.CreateBuilder(args);

// Per-worktree namespace key (hash of the absolute repo path). Scopes the
// postgres data volume and the Docker Desktop group label, so parallel
// worktrees can't share state or collapse into one row in Docker Desktop.
var repoRoot = DevInfrastructure.FindRepoRoot();
var worktreeId = Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes(Path.GetFullPath(repoRoot))))[..8].ToLowerInvariant();

// Stamp every Aspire-managed container with com.docker.compose.project
// so Docker Desktop groups them under one collapsible row. Supabase is
// stamped separately via supabase/config.toml's project_id; the two
// stacks end up in distinct DemoPage-Supabase / DemoPage-Aspire-<id>
// rows so it's obvious at a glance which side owns each container.
var dockerGroup = $"DemoPage-Aspire-{worktreeId}";

// Aspire owns the Marten Postgres container. The data volume is scoped
// by worktreeId so parallel AppHosts get independent databases; same
// worktree reuses its volume across restarts. connectionName:
// "DefaultConnection" matches the key in the API's appsettings.json.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume($"kalandra-pgdata-{worktreeId}")
    .WithDockerGroup(dockerGroup);
var kalandraDb = postgres.AddDatabase("kalandra");

// launchProfileName: null bypasses launchSettings.json's hardcoded :5000
// so parallel AppHosts get distinct API ports. astro.config.mjs reads
// services__api__http__0 (injected by WithReference(api) below) to wire
// its Vite proxy. The e2e tests still rely on :5000 via `dotnet run`.
var api = builder.AddProject<Projects.Kalandra_Api>("api", launchProfileName: null)
    .WithReference(kalandraDb, connectionName: "DefaultConnection")
    .WaitFor(kalandraDb)
    .WithHttpEndpoint()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithIconName("Server");

var otlpTracesUrl = $"http://localhost:{ports.OtlpHttp.Port}/v1/traces";
builder.AddNpmApp("web", "../../frontend", "dev:claudePreview")
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WithEnvironment("PUBLIC_OTLP_TRACES_ENDPOINT", otlpTracesUrl)
    // Force the API URL empty so `fetch(apiUrl + "/api/...")` produces a
    // relative URL and goes through Vite's /api proxy → Aspire's API.
    // Vite's loadEnv merges process.env after .env* files, so this wins
    // over any stale PUBLIC_API_URL=https://api.kalandra.tech that a
    // contributor may have left in their gitignored .env.local from
    // testing against prod.
    .WithEnvironment("PUBLIC_API_URL", "")
    .WithExternalHttpEndpoints()
    .WithIconName("Globe")
    .WaitFor(api);

// Supabase services are started by the CLI (see DevInfrastructure) and
// shared machine-wide. Surface them on the dashboard as external services
// (display-only, no lifecycle). Ports come from supabase/config.toml.
builder.AddExternalService("supabase-api", "http://127.0.0.1:54321");
builder.AddExternalService("supabase-storage", "http://127.0.0.1:54321/storage/v1/");
builder.AddExternalService("supabase-studio", "http://127.0.0.1:54323");
builder.AddExternalService("supabase-mailpit", "http://127.0.0.1:54324");

var app = builder.Build();

// Hand-off: re-acquire the mutex, drop our placeholder listeners, and let
// Aspire bind the ports before another instance can race in. StartAsync
// is awaited synchronously because Mutex has thread affinity — an `await`
// could resume on a different thread and break ReleaseMutex.
AcquireMutex(portMutex);
try
{
    ports.StopListeners();
    app.StartAsync().GetAwaiter().GetResult();
}
finally
{
    portMutex.ReleaseMutex();
}

// Once the frontend goes Running, print Web + Dashboard as a clean,
// clickable summary at the bottom of the log. Aspire emits the same
// URLs inline with framework output, but they're easy to miss there.
// Everything else (API, OTLP, Supabase) is reachable from the dashboard.
var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
_ = Task.Run(async () =>
{
    try
    {
        await foreach (var evt in notifications.WatchAsync(lifetime.ApplicationStopping))
        {
            if (evt.Resource.Name != "web") continue;
            if (evt.Snapshot.State?.Text != KnownResourceStates.Running) continue;
            var url = evt.Snapshot.Urls.FirstOrDefault(u => !u.IsInternal);
            if (url is null) continue;
            Console.WriteLine($"  Web:    {Hyperlink(url.Url)}");
            Console.WriteLine($"  Aspire: {Hyperlink(dashboardUrl)}");
            return;
        }
    }
    catch (OperationCanceledException) { }
});

await app.WaitForShutdownAsync();

// OSC 8 hyperlink escape — Windows Terminal, VS Code, JetBrains, and most
// modern Unix terminals render this as a clickable link; others show the
// raw URL as the visible text, so nothing is lost.
static string Hyperlink(string url) => $"\x1b]8;;{url}\x1b\\{url}\x1b]8;;\x1b\\";

// AbandonedMutexException means a prior AppHost crashed holding the lock.
// The OS still hands ownership to us, so swallow it and continue.
static void AcquireMutex(Mutex mutex)
{
    try
    {
        mutex.WaitOne();
    }
    catch (AbandonedMutexException)
    {
    }
}
