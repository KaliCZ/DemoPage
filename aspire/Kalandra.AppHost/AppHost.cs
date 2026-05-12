using System.Net;
using System.Net.Sockets;

// AppHost-owned endpoints (dashboard / OTLP exporter / resource service)
// start at their defaults and walk up by 1 until a free port is found, so
// the first instance lands on 15036/19200/20056 and a second parallel
// AppHost picks 15037/19201/20057, etc. Set KALANDRA_PORT_OFFSET=<int> to
// pin to a specific offset instead.
var (dashboardPort, otlpPort, resourcePort, portSource) = ResolveAppHostPorts();

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
var api = builder.AddProject<Projects.Kalandra_Api>("api");

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
// Supabase stays outside Aspire. The CLI manages its own Docker containers
// and would leak them on AppHost shutdown (no automatic `supabase stop` hook),
// so we keep using `npm run dev:infra` to start Postgres + Supabase before
// launching the AppHost.

builder.Build().Run();

static (int Dashboard, int Otlp, int Resource, string Source) ResolveAppHostPorts()
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
        return (DashboardDefault + offset, OtlpDefault + offset, ResourceDefault + offset, $"KALANDRA_PORT_OFFSET={offset}");
    }

    var dashboard = FindFreePortFrom(DashboardDefault);
    var otlp = FindFreePortFrom(OtlpDefault);
    var resource = FindFreePortFrom(ResourceDefault);
    var source = dashboard == DashboardDefault && otlp == OtlpDefault && resource == ResourceDefault
        ? "default ports"
        : "default ports, stepped past in-use";
    return (dashboard, otlp, resource, source);
}

static int FindFreePortFrom(int start, int maxAttempts = 100)
{
    for (var port = start; port < start + maxAttempts; port++)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
            return port;
        }
        catch (SocketException)
        {
            // Port in use — try the next one.
        }
        finally
        {
            listener.Stop();
        }
    }
    throw new InvalidOperationException($"No free port found in range {start}..{start + maxAttempts - 1}");
}
