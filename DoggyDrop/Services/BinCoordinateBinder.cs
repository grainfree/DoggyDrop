using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoggyDrop.Services;

// Map widgets emit decimal points; Slovenian users may type decimal commas. Never interpret either as thousands.
public sealed class BinCoordinateBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value != ValueProviderResult.None) context.ModelState.SetModelValue(context.ModelName, value);
        var text = value.FirstValue?.Trim();
        if (string.IsNullOrEmpty(text) && context.ModelType == typeof(double?)) context.Result = ModelBindingResult.Success(null);
        else if (!string.IsNullOrEmpty(text) && double.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                     CultureInfo.InvariantCulture, out var coordinate) && double.IsFinite(coordinate)) context.Result = ModelBindingResult.Success(coordinate);
        else context.ModelState.TryAddModelError(context.ModelName, "Vnesi veljavno koordinato brez ločil tisočic.");
        return Task.CompletedTask;
    }
}
