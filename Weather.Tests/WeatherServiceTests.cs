using Microsoft.Extensions.Logging.Abstractions;
using Weather;
using Weather.Data;
using Weather.Services;
using WeatherService = Weather.Services.WeatherService;
using Xunit;

public sealed class WeatherServiceTests
{
    [Fact]
    public async Task Returns_fresh_payload_unchanged()
    {
        const string payload = "{\"hourly\":{\"temperature_2m\":[21.5]}}";
        var repository = new FakeRepository();
        var service = CreateService(payload, repository, out var handler);

        var result = await service.GetAsync(52.52, 13.41, "temperature_2m,wind_speed_10m", CancellationToken.None);

        Assert.Equal(payload, result);
        Assert.Equal(payload, repository.SavedPayload);
        Assert.Equal("/v1/forecast?latitude=52.52&longitude=13.41&hourly=temperature_2m%2Cwind_speed_10m", handler.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Returns_last_snapshot_when_upstream_fails()
    {
        var repository = new FakeRepository { Snapshot = "{\"cached\":true}" };
        var service = CreateService(new HttpRequestException(), repository, out _);

        var result = await service.GetAsync(52.52, 13.41, null, CancellationToken.None);

        Assert.Equal(repository.Snapshot, result);
    }

    [Fact]
    public async Task Returns_null_when_upstream_and_snapshot_are_unavailable()
    {
        var service = CreateService(new HttpRequestException(), new FakeRepository(), out _);

        Assert.Null(await service.GetAsync(52.52, 13.41, null, CancellationToken.None));
    }

    private static WeatherService CreateService(object response, FakeRepository repository, out Handler handler)
    {
        handler = new Handler(response);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://test/") };
        return new WeatherService(client, repository, NullLogger<WeatherService>.Instance);
    }

    private sealed class Handler(object response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            if (response is Exception exception) throw exception;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent((string)response)
            });
        }
    }

    private sealed class FakeRepository : IWeatherRepository
    {
        public string? Snapshot { get; set; }
        public string? SavedPayload { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AppendAsync(string queryKey, string payload, CancellationToken cancellationToken = default) { SavedPayload = payload; Snapshot = payload; return Task.CompletedTask; }
        public Task<string?> GetLatestAsync(string queryKey, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
        public Task<int> CleanupExpiredAsync(DateTime receivedBeforeUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
