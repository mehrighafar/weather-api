namespace Weather.Services;

using Weather.Data;

public sealed class WeatherSnapshotCleanupService(
    IWeatherRepository repository,
    ILogger<WeatherSnapshotCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var deleted = await repository.CleanupExpiredAsync(DateTime.UtcNow - RetentionPeriod, stoppingToken);
                logger.LogInformation("Weather snapshot cleanup deleted {DeletedCount} expired snapshots.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Weather snapshot cleanup failed.");
            }
        }
    }
}
