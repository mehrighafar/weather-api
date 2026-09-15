namespace Weather.Clients;

public interface IOpenMeteoClient
{
    Task<string> GetForecastAsync(double latitude, double longitude, string? hourly, CancellationToken cancellationToken);
}
