using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Kalandra.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Supabase is shared machine-wide via its CLI; Aspire owns Marten's Postgres so each run gets an isolated DB.
DevInfrastructure.EnsureRunning();

// Prevents two aspires to bind into the same ports.
using var portMutex = new Mutex(initiallyOwned: false, "Kalandra-Aspire-Ports");
AcquireMutex(portMutex);

DistributedApplication app;
string dashboardUrl;
try
{
    var ports = PortReservation.Reserve();

    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://localhost:{ports.Dashboard.Port}");
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", $"http://localhost:{ports.Otlp.Port}");
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", $"http://localhost:{ports.OtlpHttp.Port}");
    Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", $"http://localhost:{ports.Resource.Port}");
    Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");
    // Loopback-only dev dashboard; allow the browser OTLP exporter to POST cross-origin from the Astro dev server.
    Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
    Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDORIGINS", "*");
    Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDHEADERS", "*");

    dashboardUrl = $"http://localhost:{ports.Dashboard.Port}";
    // Only announce non-default port assignments.
    if (ports.Source != "default ports")
    {
        Console.WriteLine($"Aspire ports: {ports.Source}");
    }

    var builder = DistributedApplication.CreateBuilder(args);

    // Per-worktree key: scopes the Postgres volume and the Docker Desktop group so parallel worktrees stay isolated.
    var repoRoot = DevInfrastructure.FindRepoRoot();
    var worktreeId = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(repoRoot))))[..8].ToLowerInvariant();

    // Docker Desktop collapses containers sharing this label into one row.
    var dockerGroup = $"DemoPage-Aspire-{worktreeId}";

    // Fixed local-dev password: Postgres bakes POSTGRES_PASSWORD into the volume on first init,
    // so a rotating value would break auth on every subsequent run.
    var pgPassword = builder.AddParameter("postgres-password", () => "kalandra-local-dev", secret: true);

    // connectionName matches the key in the API's appsettings.json.
    var postgres = builder.AddPostgres("postgres", password: pgPassword)
        .WithDataVolume($"kalandra-pgdata-{worktreeId}")
        .WithDockerGroup(dockerGroup);
    var kalandraDb = postgres.AddDatabase("kalandra");

    // launchProfileName: null bypasses launchSettings.json's :5000 so parallel AppHosts get distinct API ports.
    var api = builder.AddProject<Projects.Kalandra_Api>("api", launchProfileName: null)
        .WithReference(kalandraDb, connectionName: "DefaultConnection")
        .WaitFor(kalandraDb)
        .WithHttpEndpoint()
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithIconName("Server");

    var otlpTracesUrl = $"http://localhost:{ports.OtlpHttp.Port}/v1/traces";
    // No WaitFor(api): the UI comes up immediately rather than blocking on the API's own DB wait;
    // a fetch before the API is ready just errors and recovers on the next call.
    var web = builder.AddNpmApp("web", "../../frontend", "dev:claudePreview")
        .WithHttpEndpoint(env: "PORT")
        .WithReference(api)
        .WithEnvironment("PUBLIC_OTLP_TRACES_ENDPOINT", otlpTracesUrl)
        // Empty PUBLIC_API_URL forces relative fetches through Vite's /api proxy, overriding any stale .env.local.
        .WithEnvironment("PUBLIC_API_URL", "")
        .WithExternalHttpEndpoints()
        .WithIconName("Globe");

    // The API's /mcp tools list blog posts from the site's RSS feed — point them at the dev web server.
    api.WithEnvironment("BlogFeed__RssUrl", ReferenceExpression.Create($"{web.GetEndpoint("http")}/rss.xml"));

    // Display-only links to the CLI-managed Supabase stack; ports come from supabase/config.toml.
    builder.AddExternalService("supabase-api", "http://127.0.0.1:54321");
    builder.AddExternalService("supabase-storage", "http://127.0.0.1:54321/storage/v1/");
    builder.AddExternalService("supabase-studio", "http://127.0.0.1:54323");
    builder.AddExternalService("supabase-mailpit", "http://127.0.0.1:54324");

    app = builder.Build();

    ports.StopListeners();
    try
    {
        // Sync wait: Mutex has thread affinity, so an `await` could resume on a different thread and break ReleaseMutex.
        app.StartAsync().WaitAsync(TimeSpan.FromSeconds(40)).GetAwaiter().GetResult();
    }
    catch (TimeoutException)
    {
        Console.Error.WriteLine("AppHost startup timed out after 40s — a resource (likely Postgres) failed to become healthy. Check `docker ps` and the Aspire dashboard.");
        Environment.Exit(1);
    }
}
finally
{
    portMutex.ReleaseMutex();
}

// Print a clickable Web + Dashboard summary once the frontend is up; everything else is reachable from the dashboard.
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

// OSC 8 hyperlink escape; terminals without support fall back to showing the raw URL.
static string Hyperlink(string url) => $"\x1b]8;;{url}\x1b\\{url}\x1b]8;;\x1b\\";

// AbandonedMutexException = a prior AppHost crashed holding the lock; the OS still hands us ownership.
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
