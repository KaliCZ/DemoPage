using Microsoft.AspNetCore.Mvc;

namespace Kalandra.Api.Infrastructure;

/// <summary>The enum name becomes the error code the frontend looks up as an i18n key, so renaming a value is a breaking change.</summary>
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
