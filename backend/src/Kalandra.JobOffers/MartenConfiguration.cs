using Kalandra.JobOffers.Entities;
using Marten;
using Marten.Events.Projections;

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
    }
}
