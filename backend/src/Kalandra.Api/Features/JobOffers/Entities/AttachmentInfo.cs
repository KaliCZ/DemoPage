using System.ComponentModel.DataAnnotations;

namespace Kalandra.Api.Features.JobOffers.Entities;

public record AttachmentInfo(
    [Required, MaxLength(255)] string FileName,
    [Required, MaxLength(500)] string StoragePath,
    [Range(1, long.MaxValue)] long FileSize,
    [Required, MaxLength(100)] string ContentType);
