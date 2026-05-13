using System.Net;
using System.Net.Sockets;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// AppHost-owned ports (dashboard, OTLP gRPC, OTLP HTTP, resource service)
// start at their defaults and walk up by 1 until free, so a second parallel
// AppHost lands one above the first. KALANDRA_PORT_OFFSET=<int> pins to a
// fixed offset instead. OTLP HTTP exists separately from gRPC because the
// browser exporter can only speak HTTP.
//
// To make port pickup race-free across parallel AppHosts, we bind a
// TcpListener on each chosen port immediately (siblings probing it then
// see SocketException and step past), and a named Mutex serializes only
// the two narrow critical sections: the initial probe-and-bind, and the
// later release-and-hand-off to Aspire.
using var portMutex = new Mutex(initiallyOwned: false, "Kalandra-Aspire-Ports");

int dashboardPort, otlpPort, otlpHttpPort, resourcePort;
string portSource;
TcpListener? dashboardListener, otlpListener, otlpHttpListener, resourceListener;

AcquireMutex(portMutex);
try
{
    (dashboardPort, otlpPort, otlpHttpPort, resourcePort, portSource,
        dashboardListener, otlpListener, otlpHttpListener, resourceListener) = ResolveAppHostPorts();
}
finally
{
    portMutex.ReleaseMutex();
}

Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://localhost:{dashboardPort}");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", $"http://localhost:{otlpPort}");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", $"http://localhost:{otlpHttpPort}");
Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", $"http://localhost:{resourcePort}");
Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");
// Disable dashboard auth + OTLP API-key auth and open CORS so the browser
// OTLP exporter can POST from the Astro dev origin without credentials.
// Dev-only; the AppHost is bound to loopback.
Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");
Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDORIGINS", "*");
Environment.SetEnvironmentVariable("DASHBOARD__OTLP__CORS__ALLOWEDHEADERS", "*");

var dashboardUrl = $"http://localhost:{dashboardPort}";
Console.WriteLine($"Aspire ({portSource}):");
Console.WriteLine($"  Dashboard: {Hyperlink(dashboardUrl)}");

var builder = DistributedApplication.CreateBuilder(args);

// Aspire allocates API and frontend ports dynamically. WithReference(api)
// injects services__api__http__0 into the npm app (read by astro.config.mjs
// for the Vite proxy); WithHttpEndpoint(env: "PORT") passes the frontend's
// allocated port to Astro via $PORT.
//
// launchProfileName: null bypasses launchSettings.json (which hardcodes
// :5000 and would block parallel AppHosts). `npm run dev` and the e2e
// tests still use that profile directly.
var api = builder.AddProject<Projects.Kalandra_Api>("api", launchProfileName: null)
    .WithHttpEndpoint()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithIconName("Server");

var otlpTracesUrl = $"http://localhost:{otlpHttpPort}/v1/traces";
builder.AddNpmApp("web", "../../frontend", "dev:claudePreview")
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WithEnvironment("PUBLIC_OTLP_TRACES_ENDPOINT", otlpTracesUrl)
    .WithExternalHttpEndpoints()
    .WithIconName("Globe")
    .WaitFor(api);

// Supabase containers are owned by the Supabase CLI (`npm run dev:infra`),
// not Aspire — Ctrl+C-ing the AppHost would otherwise leak them. Surface
// them on the dashboard as external services (display-only, no lifecycle).
// Ports come from supabase/config.toml.
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
    dashboardListener?.Stop();
    otlpListener?.Stop();
    otlpHttpListener?.Stop();
    resourceListener?.Stop();
    dashboardListener = otlpListener = otlpHttpListener = resourceListener = null;
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

static (int Dashboard, int Otlp, int OtlpHttp, int Resource, string Source,
        TcpListener? DashboardListener, TcpListener? OtlpListener, TcpListener? OtlpHttpListener, TcpListener? ResourceListener)
    ResolveAppHostPorts()
{
    const int DashboardDefault = 15036;
    const int OtlpDefault = 19200;
    const int OtlpHttpDefault = 19400;
    const int ResourceDefault = 20056;

    var offsetEnv = Environment.GetEnvironmentVariable("KALANDRA_PORT_OFFSET");
    if (!string.IsNullOrEmpty(offsetEnv))
    {
        if (!int.TryParse(offsetEnv, out var offset))
        {
            Console.Error.WriteLine($"KALANDRA_PORT_OFFSET must be an integer, got: {offsetEnv}");
            Environment.Exit(1);
        }
        // Bind-test each port at the requested offset so we fail loudly
        // here instead of crashing deep in Aspire startup. If any one is
        // taken, release the rest and exit with a clear message.
        var pinned = new (int Port, string Label)[]
        {
            (DashboardDefault + offset, "dashboard"),
            (OtlpDefault + offset, "otlp"),
            (OtlpHttpDefault + offset, "otlp-http"),
            (ResourceDefault + offset, "resource"),
        };
        var pinnedListeners = new TcpListener[pinned.Length];
        for (var i = 0; i < pinned.Length; i++)
        {
            try
            {
                pinnedListeners[i] = new TcpListener(IPAddress.Loopback, pinned[i].Port);
                pinnedListeners[i].Start();
            }
            catch (SocketException)
            {
                for (var j = 0; j < i; j++) pinnedListeners[j].Stop();
                Console.Error.WriteLine($"KALANDRA_PORT_OFFSET={offset}: {pinned[i].Label} port {pinned[i].Port} is already in use");
                Environment.Exit(1);
            }
        }
        return (pinned[0].Port, pinned[1].Port, pinned[2].Port, pinned[3].Port,
            $"KALANDRA_PORT_OFFSET={offset}", pinnedListeners[0], pinnedListeners[1], pinnedListeners[2], pinnedListeners[3]);
    }

    var (dashboard, dashboardListener) = ReserveFreePortFrom(DashboardDefault, "dashboard");
    var (otlp, otlpListener) = ReserveFreePortFrom(OtlpDefault, "otlp");
    var (otlpHttp, otlpHttpListener) = ReserveFreePortFrom(OtlpHttpDefault, "otlp-http");
    var (resource, resourceListener) = ReserveFreePortFrom(ResourceDefault, "resource");
    var source = dashboard == DashboardDefault && otlp == OtlpDefault && otlpHttp == OtlpHttpDefault && resource == ResourceDefault
        ? "default ports"
        : "default ports, stepped past in-use";
    return (dashboard, otlp, otlpHttp, resource, source, dashboardListener, otlpListener, otlpHttpListener, resourceListener);
}

// Bind the first free port at or above `start` and return the live
// listener — the caller keeps it bound (which reserves the port) until
// it's ready to hand off, then calls Stop() so Aspire can take over.
static (int Port, TcpListener Listener) ReserveFreePortFrom(int start, string label, int maxAttempts = 100)
{
    for (var port = start; port < start + maxAttempts; port++)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            return (port, listener);
        }
        catch (SocketException)
        {
            // Port in use — try the next one.
        }
    }
    throw new InvalidOperationException($"No free {label} port found in range {start}..{start + maxAttempts - 1}");
}
