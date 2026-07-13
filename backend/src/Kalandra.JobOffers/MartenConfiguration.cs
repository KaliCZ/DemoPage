using JasperFx.Events.Projections;
using Kalandra.JobOffers.Entities;
using Kalandra.JobOffers.Notifications;
using Marten;

namespace Kalandra.JobOffers;

public static class MartenConfiguration
{
    /// <summary>
    /// Configures Marten projections and schema for the JobOffers domain.
    /// Call this from StoreOptions configuration.
    /// </summary>
    public static void ConfigureJobOffers(this StoreOptions options)
    {
        options.Projections.Snapshot<JobOffer>(SnapshotLifecycle.Inline);
        options.Schema.For<JobOffer>().Duplicate(j => j.Status);
        options.Schema.For<JobOfferNotificationSent>();
    }
}
