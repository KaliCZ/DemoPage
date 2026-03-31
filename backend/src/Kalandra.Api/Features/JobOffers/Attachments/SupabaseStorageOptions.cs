namespace Kalandra.Api.Features.JobOffers.Attachments;

public class SupabaseStorageOptions
{
    public const string SectionName = "Storage";

    public string BucketName { get; set; } = "job-offer-attachments";

    public string ServiceKey { get; set; } = "";
}
