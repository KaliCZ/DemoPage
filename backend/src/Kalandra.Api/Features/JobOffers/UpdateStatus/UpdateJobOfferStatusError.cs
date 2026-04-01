namespace Kalandra.Api.Features.JobOffers.UpdateStatus;

public enum UpdateJobOfferStatusError
{
    NotFound,
    AlreadyInStatus,
    InvalidTransition,
}
