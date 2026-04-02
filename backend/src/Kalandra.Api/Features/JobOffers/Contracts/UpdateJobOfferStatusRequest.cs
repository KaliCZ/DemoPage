using System.ComponentModel.DataAnnotations;
using Kalandra.JobOffers.Entities;

namespace Kalandra.Api.Features.JobOffers.Contracts;

public record UpdateJobOfferStatusRequest(
    [EnumDataType(typeof(JobOfferStatus))] JobOfferStatus Status,
    [MaxLength(2000)] string? AdminNotes);
