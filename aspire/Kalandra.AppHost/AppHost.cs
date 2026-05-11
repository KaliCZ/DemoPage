var builder = DistributedApplication.CreateBuilder(args);

// Backend API — pinned to :5000 with the proxy disabled so the existing Vite
// proxy in frontend/astro.config.mjs (which targets http://localhost:5000)
// keeps working without changes.
var api = builder.AddProject<Projects.Kalandra_Api>("api")
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5000;
        endpoint.IsProxied = false;
    });

// Frontend — Astro dev server on :4321. dev:claudePreview skips the browser
// pop-up and fails fast if the port is taken.
builder.AddNpmApp("frontend", "../../frontend", "dev:claudePreview")
    .WithHttpEndpoint(port: 4321, isProxied: false)
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
