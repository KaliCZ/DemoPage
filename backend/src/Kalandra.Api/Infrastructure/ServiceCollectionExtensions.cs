using Kalandra.Api.Features.JobOffers.Attachments;
using Kalandra.Api.Features.JobOffers.Cancel;
using Kalandra.Api.Features.JobOffers.Comments;
using Kalandra.Api.Features.JobOffers.Create;
using Kalandra.Api.Features.JobOffers.Edit;
using Kalandra.Api.Features.JobOffers.Entities;
using Kalandra.Api.Features.JobOffers.GetDetail;
using Kalandra.Api.Features.JobOffers.History;
using Kalandra.Api.Features.JobOffers.List;
using Kalandra.Api.Features.JobOffers.UpdateStatus;
using Kalandra.Api.Infrastructure.Auth;
using Marten;
using Marten.Events.Projections;
using Weasel.Core;

namespace Kalandra.Api.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppMarten(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")!;

        services.AddMarten(options =>
        {
            options.Connection(connectionString);

            // Inline projection: Marten maintains the JobOffer document
            // automatically by applying events as they are appended
            options.Projections.Snapshot<JobOffer>(SnapshotLifecycle.Inline);

            // Use snake_case for database identifiers
            options.UseSystemTextJsonForSerialization();

            if (environment.IsDevelopment())
            {
                options.AutoCreateSchemaObjects = AutoCreate.All;
            }
            else
            {
                options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            }
        })
        .UseLightweightSessions();

        return services;
    }

    public static IServiceCollection AddAppCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options =>
        {
            options.AddPolicy("DefaultPolicy", policy =>
            {
                var origins = environment.IsDevelopment()
                    ? [.. allowedOrigins, "http://localhost:4321", "http://127.0.0.1:4321"]
                    : allowedOrigins;

                policy
                    .WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }

    public static IServiceCollection AddJobOfferAttachments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SupabaseStorageOptions>(configuration.GetSection(SupabaseStorageOptions.SectionName));
        services.AddHttpClient<IJobOfferAttachmentVerifier, SupabaseJobOfferAttachmentVerifier>();

        return services;
    }

    public static IServiceCollection AddJobOfferFeatures(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();

        services.AddTransient<CreateJobOfferHandler>();
        services.AddTransient<EditJobOfferHandler>();
        services.AddTransient<CancelJobOfferHandler>();
        services.AddTransient<UpdateJobOfferStatusHandler>();
        services.AddTransient<ListJobOffersHandler>();
        services.AddTransient<GetJobOfferDetailHandler>();
        services.AddTransient<JobOfferHistoryHandler>();
        services.AddTransient<CommentsHandler>();

        return services;
    }
}
