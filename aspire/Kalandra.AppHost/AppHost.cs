using System.Net;
using System.Net.Sockets;

// AppHost-owned endpoints (dashboard / OTLP exporter / resource service) get
// OS-allocated ports by default, so two AppHosts can run in parallel without
// clashing. Set KALANDRA_PORT_OFFSET=<int> to pin to a deterministic offset
// (dashboard 15036 / OTLP 19200 / resource 20056 + offset) when you want a
// bookmarkable dashboard URL.
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
    var offsetEnv = Environment.GetEnvironmentVariable("KALANDRA_PORT_OFFSET");
    if (!string.IsNullOrEmpty(offsetEnv))
    {
        if (!int.TryParse(offsetEnv, out var offset))
        {
            Console.Error.WriteLine($"KALANDRA_PORT_OFFSET must be an integer, got: {offsetEnv}");
            Environment.Exit(1);
        }
        return (15036 + offset, 19200 + offset, 20056 + offset, $"KALANDRA_PORT_OFFSET={offset}");
    }
    return (FindFreePort(), FindFreePort(), FindFreePort(), "OS-allocated");
}

static int FindFreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    finally
    {
        listener.Stop();
    }
}
