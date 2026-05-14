using Aspire.Hosting.ApplicationModel;
using Kalandra.AppHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Bring up dev infra (Postgres + Supabase) before anything else. These
// commands are idempotent, so any entry point — `npm run aspire`, the
// IDE's auto-generated AppHost config, or plain `dotnet run` — gets a
// working stack without external prerequisites.
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
// Disable dashboard auth + OTLP API-key auth and open CORS so the browser
// OTLP exporter can POST from the Astro dev origin without credentials.
// Dev-only; the AppHost is bound to loopback.
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDORIGINS", "*");
Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDHEADERS", "*");

var dashboardUrl = $"http://localhost:{ports.Dashboard.Port}";
Console.WriteLine($"Aspire ({ports.Source}):");
Console.WriteLine($"  Dashboard: {Hyperlink(dashboardUrl)}");

var builder = DistributedApplication.CreateBuilder(args);

// Aspire allocates API and frontend ports dynamically. WithReference(api)
// injects services__api__http__0 into the npm app (read by astro.config.mjs
// for the Vite proxy); WithHttpEndpoint(env: "PORT") passes the frontend's
// allocated port to Astro via $PORT.
//
// launchProfileName: null bypasses launchSettings.json (which hardcodes
// :5000 and would block parallel AppHosts). The e2e tests still use
// :5000 via `dotnet run` directly.
var api = builder.AddProject<Projects.Kalandra_Api>("api", launchProfileName: null)
    .WithHttpEndpoint()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithIconName("Server");

var otlpTracesUrl = $"http://localhost:{ports.OtlpHttp.Port}/v1/traces";
builder.AddNpmApp("web", "../../frontend", "dev:claudePreview")
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WithEnvironment("PUBLIC_OTLP_TRACES_ENDPOINT", otlpTracesUrl)
    .WithExternalHttpEndpoints()
    .WithIconName("Globe")
    .WaitFor(api);

// We start Supabase at boot (see DevInfrastructure) but don't own its
// lifecycle — Ctrl+C-ing the AppHost leaves the containers running so
// dev DB state survives across runs. Surface them on the dashboard as
// external services (display-only). Ports come from supabase/config.toml.
builder.AddExternalService("supabase-api", "http://127.0.0.1:54321");
builder.AddExternalService("supabase-storage", "http://127.0.0.1:54321/storage/v1/");
builder.AddExternalService("supabase-postgres", "postgresql://127.0.0.1:54322");
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

// Print each resource's external URL the moment it goes Running. Aspire
// also logs these, but interleaved with framework output; this gives a
// clean, clickable list at the top of the terminal.
var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var printed = new HashSet<string>();
_ = Task.Run(async () =>
{
    try
    {
        await foreach (var evt in notifications.WatchAsync(lifetime.ApplicationStopping))
        {
            if (evt.Snapshot.State?.Text != KnownResourceStates.Running) continue;
            var urls = evt.Snapshot.Urls.Where(u => !u.IsInternal).ToList();
            if (urls.Count == 0 || !printed.Add(evt.Resource.Name)) continue;
            foreach (var url in urls)
                Console.WriteLine($"  {evt.Resource.Name}: {Hyperlink(url.Url)}");
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
