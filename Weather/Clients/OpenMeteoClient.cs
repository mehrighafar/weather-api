using System.Globalization;

namespace Weather.Clients;

public sealed class OpenMeteoClient(
    HttpClient httpClient,
    WeatherOpenmeteoServiceConfig config) : IOpenMeteoClient
{
    public async Task<string> GetForecastAsync(
        double latitude,
        double longitude,
        string? hourly,
        CancellationToken cancellationToken)
    {
        var latitudeText = latitude.ToString(CultureInfo.InvariantCulture);
        var longitudeText = longitude.ToString(CultureInfo.InvariantCulture);
        var hourlyText = string.IsNullOrWhiteSpace(hourly) ? null : hourly.Trim();
        var path = $"{config.Endpoints.GetForecast}?latitude={latitudeText}&longitude={longitudeText}" +
            (hourlyText is null ? string.Empty : $"&hourly={Uri.EscapeDataString(hourlyText)}");

        using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
