using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kalandra.Api.Features.JobOffers.Entities;

public class JobOfferConfiguration : IEntityTypeConfiguration<JobOffer>
{
    public void Configure(EntityTypeBuilder<JobOffer> builder)
    {
        builder.ToTable("job_offers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired().HasMaxLength(255);
        builder.Property(x => x.UserEmail).HasColumnName("user_email").IsRequired().HasMaxLength(255);

        builder.Property(x => x.CompanyName).HasColumnName("company_name").IsRequired().HasMaxLength(200);
        builder.Property(x => x.ContactName).HasColumnName("contact_name").IsRequired().HasMaxLength(200);
        builder.Property(x => x.ContactEmail).HasColumnName("contact_email").IsRequired().HasMaxLength(255);
        builder.Property(x => x.JobTitle).HasColumnName("job_title").IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasColumnName("description").IsRequired().HasMaxLength(5000);
        builder.Property(x => x.SalaryRange).HasColumnName("salary_range").HasMaxLength(100);
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(200);
        builder.Property(x => x.IsRemote).HasColumnName("is_remote");
        builder.Property(x => x.AdditionalNotes).HasColumnName("additional_notes").HasMaxLength(2000);

        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.AdminNotes).HasColumnName("admin_notes").HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_job_offers_user_id");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_job_offers_status");
    }
}
