using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Infrastructure;

/// <summary>
/// Adds the error enum value as a validation error on the given field
/// and returns an RFC 7807 ValidationProblem response. The enum name
/// becomes the error code the frontend uses for i18n key lookup.
/// </summary>
public static class ControllerExtensions
{
    extension(ControllerBase controller)
    {
        public ActionResult ValidationError<TError>(string field, TError error)
            where TError : struct, Enum
        {
            controller.ModelState.AddModelError(field, error.ToString());
            return controller.ValidationProblem();
        }
    }
}
