using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;

// AppHost-owned endpoints (dashboard / OTLP exporter / resource service)
// start at their defaults and walk up by 1 until a free port is found, so
// the first instance lands on 15036/19200/20056 and a second parallel
// AppHost picks 15037/19201/20057, etc. Set KALANDRA_PORT_OFFSET=<int> to
// pin to a specific offset instead.
//
// Reservation strategy: bind a TcpListener on each chosen port the moment
// we pick it, keep it bound through AppHost setup (so siblings probing
// those ports get SocketException and walk past), then release the
// listeners and let Aspire bind the dashboard in the same critical
// section. A named Mutex serializes the two narrow windows (probe-and-bind
// + release-and-handoff) across parallel AppHosts; the long middle phase
// runs without holding the lock.
const string PortMutexName = "Kalandra-Aspire-Ports";
using var portMutex = new Mutex(initiallyOwned: false, PortMutexName);

int dashboardPort, otlpPort, resourcePort;
string portSource;
TcpListener? dashboardListener, otlpListener, resourceListener;

AcquireMutex(portMutex);
try
{
    (dashboardPort, otlpPort, resourcePort, portSource,
        dashboardListener, otlpListener, resourceListener) = ResolveAppHostPorts();
}
finally
{
    portMutex.ReleaseMutex();
}

Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://localhost:{dashboardPort}");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", $"http://localhost:{otlpPort}");
Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", $"http://localhost:{resourcePort}");
Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");

Console.WriteLine($"Aspire ({portSource}):");
Console.WriteLine($"  Dashboard:        http://localhost:{dashboardPort}");
Console.WriteLine($"  OTLP exporter:    http://localhost:{otlpPort}");
Console.WriteLine($"  Resource service: http://localhost:{resourcePort}");
Console.WriteLine($"  API + frontend:   allocated by Aspire — see dashboard");

var builder = DistributedApplication.CreateBuilder(args);

// API and frontend ports are picked by dcp and discovered dynamically.
// WithReference(api) injects services__api__http__0 into the npm app so the
// Vite proxy in astro.config.mjs knows where Kestrel landed. WithHttpEndpoint
// passes the allocated frontend port to Astro via PORT, which astro.config.mjs
// reads.
//
// launchProfileName: null skips launchSettings.json entirely — the profile
// hardcodes http://localhost:5000, which two parallel AppHosts can't share.
// `npm run dev` and the e2e tests use `dotnet run` directly and still rely
// on :5000 from launchSettings; we only bypass it here. WithHttpEndpoint
// without a port lets Aspire allocate a free one.
var api = builder.AddProject<Projects.Kalandra_Api>("api", launchProfileName: null)
    .WithHttpEndpoint()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

builder.AddNpmApp("frontend", "../../frontend", "dev:claudePreview")
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WithExternalHttpEndpoints()
    .WaitFor(api);

// Temporal — community integration once we wire it in. Add the package
// CommunityToolkit.Aspire.Hosting.Temporal and uncomment:
//
//   var temporal = builder.AddTemporalServerContainer("temporal");
//   api.WithReference(temporal).WaitFor(temporal);
//
// Supabase containers are owned by the Supabase CLI (`npm run dev:infra`),
// not Aspire — that way `Ctrl+C`-ing the AppHost doesn't leak them. We
// surface the endpoints on the dashboard as external services for visibility
// and one-click access. Ports come from supabase/config.toml.
builder.AddExternalService("supabase-api", "http://127.0.0.1:54321");
builder.AddExternalService("supabase-studio", "http://127.0.0.1:54323");
builder.AddExternalService("supabase-mailpit", "http://127.0.0.1:54324");

var app = builder.Build();

// Hand-off window: re-acquire the mutex, release our bound listeners so the
// kernel will let Aspire have the ports, and start the app while still
// holding the lock. StartAsync returns once the dashboard is up, so the
// close-then-bind sequence is atomic against any sibling AppHost doing its
// own hand-off at the same instant.
//
// Block synchronously on StartAsync: Mutex has thread affinity, and an
// `await` here would let the continuation resume on a different thread, so
// ReleaseMutex would throw ApplicationException.
AcquireMutex(portMutex);
try
{
    dashboardListener?.Stop();
    otlpListener?.Stop();
    resourceListener?.Stop();
    dashboardListener = otlpListener = resourceListener = null;
    app.StartAsync().GetAwaiter().GetResult();
}
finally
{
    portMutex.ReleaseMutex();
}

await app.WaitForShutdownAsync();

// AbandonedMutexException means a previous AppHost crashed while holding
// the lock. The kernel hands ownership to us anyway — just swallow the
// exception and proceed.
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

static (int Dashboard, int Otlp, int Resource, string Source,
        TcpListener? DashboardListener, TcpListener? OtlpListener, TcpListener? ResourceListener)
    ResolveAppHostPorts()
{
    const int DashboardDefault = 15036;
    const int OtlpDefault = 19200;
    const int ResourceDefault = 20056;

    var offsetEnv = Environment.GetEnvironmentVariable("KALANDRA_PORT_OFFSET");
    if (!string.IsNullOrEmpty(offsetEnv))
    {
        if (!int.TryParse(offsetEnv, out var offset))
        {
            Console.Error.WriteLine($"KALANDRA_PORT_OFFSET must be an integer, got: {offsetEnv}");
            Environment.Exit(1);
        }
        return (DashboardDefault + offset, OtlpDefault + offset, ResourceDefault + offset,
            $"KALANDRA_PORT_OFFSET={offset}", null, null, null);
    }

    var (dashboard, dashboardListener) = ReserveFreePortFrom(DashboardDefault, "dashboard");
    var (otlp, otlpListener) = ReserveFreePortFrom(OtlpDefault, "otlp");
    var (resource, resourceListener) = ReserveFreePortFrom(ResourceDefault, "resource");
    var source = dashboard == DashboardDefault && otlp == OtlpDefault && resource == ResourceDefault
        ? "default ports"
        : "default ports, stepped past in-use";
    return (dashboard, otlp, resource, source, dashboardListener, otlpListener, resourceListener);
}

// Walks up from `start` and binds the first port it can grab. The returned
// listener stays bound — keep it alive until you want Aspire to take the
// port, then Stop() and let Aspire bind. The TCP bind itself is what
// reserves the port; the outer Mutex only serializes the probe phase and
// the close→Aspire-bind hand-off so two AppHosts can't race in those
// narrow windows.
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
