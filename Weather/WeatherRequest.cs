using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Weather;

public sealed class WeatherRequest : IValidatableObject
{
    [BindRequired]
    public required double Latitude { get; set; }

    [BindRequired]
    public required double Longitude { get; set; }
    public string? Hourly { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude is < -90 or > 90)
        {
            yield return new ValidationResult("latitude must be between -90 and 90.", new[] { nameof(Latitude) });
        }

        if (Longitude is < -180 or > 180)
        {
            yield return new ValidationResult("longitude must be between -180 and 180.", new[] { nameof(Longitude) });
        }
    }
}
