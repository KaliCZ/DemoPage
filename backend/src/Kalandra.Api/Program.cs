using HealthChecks.UI.Client;
using Kalandra.Api.Infrastructure;
using Kalandra.Api.Infrastructure.Auth;
using Kalandra.Infrastructure.Configuration;
using Kalandra.JobOffers;
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

builder.Services.AddAppMarten(builder.Configuration, builder.Environment);
builder.Services.AddSupabaseAuth(authConfig);
builder.Services.AddAppCors(builder.Environment);
builder.Services.AddStorageServices();
builder.Services.AddAuthAdminServices();
builder.Services.AddApiServices();
builder.Services.AddJobOffersDomain();
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
app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.Run();
