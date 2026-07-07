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

var supabaseConfig = SupabaseConfig.AddSingleton(builder.Services, builder.Configuration);
TurnstileConfig.AddSingleton(builder.Services, builder.Configuration);

builder.Services.AddAppMarten(builder.Configuration, builder.Environment);
builder.Services.AddAppDataProtection();
Auth.Add(builder.Services, supabaseConfig);
builder.Services.AddAppCors(builder.Environment);
builder.Services.AddMemoryCache();
builder.Services.AddStorageServices();
builder.Services.AddTurnstile(builder.Environment);
builder.Services.AddAuthAdminServices();
builder.Services.AddApiServices();
builder.Services.AddJobOffersDomain();
builder.Services.AddBlogDomain();
BlogNotificationsConfig.AddSingleton(builder.Services, builder.Configuration);
builder.Services.AddEmailServices(builder.Configuration);
builder.Services.AddTemporal(builder.Configuration);
RateLimits.Add(builder.Services, builder.Environment);

builder.Services.AddResponseCompression(options =>
{
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.EnableForHttps = true;
});

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .AddCheck<CommitHashHealthCheck>("version")
    .AddCheck<SupabaseAuthHealthCheck>("supabase-auth")
    .AddCheck<SupabaseStorageHealthCheck>("supabase-storage");

var app = builder.Build();

app.UseResponseCompression();

// Yes, even for production.
app.UseSwagger();
app.UseSwaggerUI();

app.UseExceptionHandler();
app.UseStatusCodePages();
RobotsTag.Use(app);

app.UseCors("DefaultPolicy");
Auth.Use(app);
RateLimits.Use(app);
app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
