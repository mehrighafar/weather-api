namespace Weather.Clients;

public sealed class WeatherOpenmeteoServiceConfig
{
    public WeatherOpenmeteoEndpoints Endpoints { get; set; } = new();
}

public sealed class WeatherOpenmeteoEndpoints
{
    public string BaseUrl { get; set; } = string.Empty;
    public string GetForecast { get; set; } = "/v1/forecast";
}
