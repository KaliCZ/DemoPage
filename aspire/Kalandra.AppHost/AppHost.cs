var builder = DistributedApplication.CreateBuilder(args);

// Parallel envs across worktrees: scripts/dev-aspire.mjs computes per-worktree
// ports from KALANDRA_PORT_OFFSET and exports them. Defaults match the
// single-instance values so `dotnet run` from an IDE still works on its own.
var apiPort = int.TryParse(Environment.GetEnvironmentVariable("KALANDRA_API_PORT"), out var p1) ? p1 : 5000;
var frontendPort = int.TryParse(Environment.GetEnvironmentVariable("KALANDRA_FRONTEND_PORT"), out var p2) ? p2 : 4321;

// Backend API — proxy disabled so the Vite proxy in astro.config.mjs hits
// Kestrel directly on the configured port.
var api = builder.AddProject<Projects.Kalandra_Api>("api")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = apiPort;
        endpoint.IsProxied = false;
    });

// Frontend — Astro reads KALANDRA_API_PORT / KALANDRA_FRONTEND_PORT from env
// (see astro.config.mjs) for proxy upstream and bind port respectively.
builder.AddNpmApp("frontend", "../../frontend", "dev:claudePreview")
    .WithHttpEndpoint(port: frontendPort, isProxied: false)
    .WithEnvironment("KALANDRA_API_PORT", apiPort.ToString())
    .WithEnvironment("KALANDRA_FRONTEND_PORT", frontendPort.ToString())
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
