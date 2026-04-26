using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Kalandra.Api.StrongTypesExtensions;

/// <summary>
/// Binds <see cref="NonEmptyString"/> and <see cref="NonEmptyString?"/> from
/// non-JSON value providers (form, query, route). JSON bodies are handled by
/// the <c>[JsonConverter]</c> that ships with the type.
/// An empty/whitespace value produces a ModelState error that ASP.NET Core
/// surfaces as an RFC 7807 400 response — matching the behaviour of the
/// <c>JsonConverter</c> on the JSON path.
/// </summary>
// Candidate to move into a future Kalicz.StrongTypes.AspNetCore package so
// every consumer gets this binding out of the box. Tracked at
// https://github.com/KaliCZ/StrongTypes/issues/75.
public class NonEmptyStringModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var type = context.Metadata.UnderlyingOrModelType;
        return type == typeof(NonEmptyString) ? new NonEmptyStringModelBinder() : null;
    }

    private sealed class NonEmptyStringModelBinder : IModelBinder
    {
        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            ArgumentNullException.ThrowIfNull(bindingContext);

            var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
            if (valueProviderResult == ValueProviderResult.None)
                return Task.CompletedTask;

            bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);

            var raw = valueProviderResult.FirstValue;
            if (raw.AsNonEmpty() is { } nonEmpty)
            {
                bindingContext.Result = ModelBindingResult.Success(nonEmpty);
                return Task.CompletedTask;
            }

            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"The {bindingContext.ModelMetadata.GetDisplayName()} field is required.");
            return Task.CompletedTask;
        }
    }
}
