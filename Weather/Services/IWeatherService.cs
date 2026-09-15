namespace Weather.Services;

public interface IWeatherService
{
    Task<string?> GetAsync(double latitude, double longitude, string? hourly, CancellationToken requestAborted);
}
