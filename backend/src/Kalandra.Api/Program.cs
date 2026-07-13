using HealthChecks.UI.Client;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Api.Infrastructure.DataProtection;
using Kalandra.Blog;
using Kalandra.Infrastructure.Configuration;
using Kalandra.JobOffers;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.OpenApi;
using StrongTypes.OpenApi.Swashbuckle;

var builder = WebApplication.CreateBuilder(args);

Observability.Add(builder);

builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // allowIntegerValues: false so a raw number for an enum field is rejected at
        // binding (a clean 400) instead of deserializing to an out-of-range value.
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddStrongTypes();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your Supabase access token"
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});

var supabaseConfig = SupabaseConfig.AddSingleton(builder.Services, builder.Configuration, builder.Environment);
TurnstileConfig.AddSingleton(builder.Services, builder.Configuration, builder.Environment);

builder.Services.AddAppMarten(builder.Configuration, builder.Environment);
builder.Services.AddAppDataProtection();
Auth.Add(builder.Services, supabaseConfig);
builder.Services.AddAppCors(builder.Environment);
builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();
builder.Services.AddUserInfoCache(builder.Configuration);
builder.Services.AddStorageServices();
builder.Services.AddTurnstile(builder.Environment);
builder.Services.AddAuthAdminServices();
builder.Services.AddApiServices();
builder.Services.AddJobOffersDomain();
JobOffersNotificationsConfig.AddSingleton(builder.Services, builder.Configuration, builder.Environment);
builder.Services.AddBlogDomain();
BlogNotificationsConfig.AddSingleton(builder.Services, builder.Configuration, builder.Environment);
builder.Services.AddEmailServices(builder.Configuration, builder.Environment);
RateLimits.Add(builder.Services, builder.Environment);

builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.EnableForHttps = true;
});

// A background worker faulting (e.g. the notification daemon) must not stop the whole API host.
builder.Services.Configure<HostOptions>(
    o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

// "live" = deploy-gate liveness (process up + build commit, no external deps); "ready" = full readiness.
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!, timeout: TimeSpan.FromSeconds(5), tags: ["ready"])
    .AddCheck<CommitHashHealthCheck>("version", tags: ["live", "ready"])
    .AddCheck<SupabaseAuthHealthCheck>("supabase-auth", tags: ["ready"])
    .AddCheck<SupabaseStorageHealthCheck>("supabase-storage", tags: ["ready"]);

var app = builder.Build();

app.UseResponseCompression();

// Yes, even for production.
app.UseSwagger();
app.UseSwaggerUI();

app.UseExceptionHandler();
app.UseStatusCodePages();
RobotsTag.Use(app);

app.UseCors("DefaultPolicy");
app.UseOutputCache();
Auth.Use(app);
RateLimits.Use(app);
app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Liveness for the blue/green deploy gate: process up + expected commit, no external deps — a
// shared DB/Supabase outage breaks both slots equally and must not roll back a good build.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
