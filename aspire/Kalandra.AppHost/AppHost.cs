var builder = DistributedApplication.CreateBuilder(args);

// API and frontend ports are picked by Aspire/dcp and discovered dynamically.
// WithReference(api) injects services__api__http__0 into the npm app so the
// Vite proxy in astro.config.mjs knows where Kestrel landed. WithHttpEndpoint
// passes the allocated frontend port to Astro via PORT, which astro.config.mjs
// reads. Only the AppHost-owned ports (dashboard / OTLP / resource service)
// need a deterministic offset — see aspire/dev-aspire.mjs.
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
