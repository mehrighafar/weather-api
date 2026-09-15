namespace Weather.Data;

public interface IWeatherRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AppendAsync(string queryKey, string payload, CancellationToken cancellationToken = default);
    Task<string?> GetLatestAsync(string queryKey, CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredAsync(DateTime receivedBeforeUtc, CancellationToken cancellationToken = default);
}
