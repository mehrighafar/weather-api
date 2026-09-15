using Weather.Clients;
using Weather.Data;

namespace Weather.Services;

public sealed class WeatherService : IWeatherService
{
    private readonly IOpenMeteoClient openMeteoClient;
    private readonly IWeatherRepository repository;
    private readonly ILogger<WeatherService> logger;

    public WeatherService(HttpClient httpClient, IWeatherRepository repository, ILogger<WeatherService> logger)
        : this(new OpenMeteoClient(httpClient, new WeatherOpenmeteoServiceConfig()), repository, logger)
    {
    }

    public WeatherService(
        IOpenMeteoClient openMeteoClient,
        IWeatherRepository repository,
        ILogger<WeatherService> logger)
    {
        this.openMeteoClient = openMeteoClient;
        this.repository = repository;
        this.logger = logger;
    }

    public async Task<string?> GetAsync(double latitude, double longitude, string? hourly, CancellationToken requestAborted)
    {
        var hourlyText = string.IsNullOrWhiteSpace(hourly) ? null : hourly.Trim();
        var queryKey = $"{latitude}:{longitude}:hourly={hourlyText ?? "default"}";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var payload = await openMeteoClient.GetForecastAsync(latitude, longitude, hourlyText, deadline.Token);
            try
            {
                await repository.AppendAsync(queryKey, payload, deadline.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Fresh weather received but could not be persisted.");
            }

            return payload;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Weather service is unavailable; trying the latest snapshot.");
        }

        try
        {
            return await repository.GetLatestAsync(queryKey, requestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not read the latest weather snapshot.");
            return null;
        }
    }
}
