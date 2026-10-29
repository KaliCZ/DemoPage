using HealthChecks.UI.Client;
using Kalandra.Blog;
using Kalandra.Blog.Feed;
using Kalandra.Hosting;
using Kalandra.Infrastructure.Configuration;
using Kalandra.JobOffers;
using Kalandra.McpServer;
using Kalandra.McpServer.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

Observability.Add(builder, serviceName: "mcp");

builder.Services.AddProblemDetails();

var supabaseConfig = SupabaseConfig.AddSingleton(builder.Services, builder.Configuration, builder.Environment);
var mcpConfig = McpServerConfig.AddSingleton(builder.Services, builder.Configuration, builder.Environment);

// No daemon or subscriptions here: the API runs them, so a tool-written event is emailed exactly once.
builder.Services.AddAppMartenStore(builder.Configuration, builder.Environment, ownsSchema: false);
McpAuth.Add(builder.Services, supabaseConfig, mcpConfig);
builder.Services.AddMcpServices();
builder.Services.AddJobOffersDomain();
builder.Services.AddBlogDomain();
builder.Services.AddBlogFeed(builder.Configuration, builder.Environment);
builder.Services.AddMcpTools();
McpRateLimits.Add(builder.Services, builder.Environment);

// A background worker faulting must not stop the whole host.
builder.Services.Configure<HostOptions>(
    o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!, timeout: TimeSpan.FromSeconds(5), tags: ["ready"])
    .AddCheck<CommitHashHealthCheck>("version", tags: ["live", "ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

const string McpEndpoint = "/mcp";

McpAuth.Use(app);
McpRateLimits.Use(app);
McpAccountGate.Use(app, McpEndpoint);

// No RequireAuthorization: it would lock out the public tools, so McpAccountGate challenges per tool call instead.
app.MapMcp(McpEndpoint).RequireRateLimiting(McpRateLimitPolicies.Mcp);

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// The blue/green deploy gate skips external deps: a shared DB/Supabase outage breaks both slots and must not roll back a good build.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
