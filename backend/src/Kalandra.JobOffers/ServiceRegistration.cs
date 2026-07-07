using Kalandra.JobOffers.Commands;
using Kalandra.JobOffers.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Kalandra.JobOffers;

public static class ServiceRegistration
{
    public static IServiceCollection AddJobOffersDomain(this IServiceCollection services)
    {
        // Command handlers
        services.AddScoped<CreateJobOfferHandler>();
        services.AddScoped<StoreJobOfferHandler>();
        services.AddScoped<EditJobOfferHandler>();
        services.AddScoped<CancelJobOfferHandler>();
        services.AddScoped<UpdateJobOfferStatusHandler>();
        services.AddScoped<AddCommentHandler>();
        services.AddScoped<StoreJobOfferCommentHandler>();

        // Query handlers
        services.AddScoped<GetJobOfferDetailHandler>();
        services.AddScoped<ListJobOffersHandler>();
        services.AddScoped<GetJobOfferHistoryHandler>();
        services.AddScoped<ListCommentsHandler>();
        services.AddScoped<GetAttachmentInfoHandler>();

        return services;
    }
}
