using System.Security.Claims;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Configuration;
using Kalandra.JobOffers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
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

var authConfig = SupabaseAuthConfig.AddSingleton(builder.Services, builder.Configuration);
SupabaseStorageConfig.AddSingleton(builder.Services, builder.Configuration);
TurnstileConfig.AddSingleton(builder.Services, builder.Configuration);

builder.Services.AddAppMarten(builder.Configuration, builder.Environment);
builder.Services.AddSupabaseAuth(authConfig);
builder.Services.AddAppCors(builder.Environment);
builder.Services.AddStorageServices();
builder.Services.AddTurnstile();
builder.Services.AddAuthAdminServices();
builder.Services.AddApiServices();
builder.Services.AddJobOffersDomain();
builder.Services.AddRateLimiter(options =>
{
    // Hire-me submissions: 2 per 30 minutes per authenticated user. When the
    // limit is hit, the client must re-render Turnstile in interactive mode and
    // resend the request with the X-Interactive-Captcha header to bypass.
    options.AddPolicy("hire-me-create", httpContext =>
    {
        if (httpContext.Request.Headers.ContainsKey("X-Interactive-Captcha"))
            return RateLimitPartition.GetNoLimiter("interactive-captcha");

        var partitionKey =
            httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 2,
                Window = TimeSpan.FromMinutes(30),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    });

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"captcha_required\"}", ct);
    };
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .AddCheck<CommitHashHealthCheck>("version");

var app = builder.Build();

// Yes, even for production.
app.UseSwagger();
app.UseSwaggerUI();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors("DefaultPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
