using Weather;
using Weather.Clients;
using Weather.Data;
using Weather.Services;

DotEnv.Load();
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var weatherOpenmeteoConfig = new WeatherOpenmeteoServiceConfig
{
    Endpoints = new WeatherOpenmeteoEndpoints
    {
        BaseUrl = builder.Configuration["WeatherOpenmeteoServiceConfig:Endpoints:BaseUrl"] ?? string.Empty,
        GetForecast = builder.Configuration["WeatherOpenmeteoServiceConfi:Endpoints:GetForecast"] ?? "/v1/forecast"
    }
};
builder.Services.AddSingleton(weatherOpenmeteoConfig);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>((sp, client) =>
{
    var config = sp.GetRequiredService<WeatherOpenmeteoServiceConfig>();
    if (string.IsNullOrWhiteSpace(config.Endpoints.BaseUrl))
    {
        throw new InvalidOperationException("WeatherOpenmeteoServiceConfig:Endpoints:BaseUrl is required.");
    }

    client.BaseAddress = new Uri(config.Endpoints.BaseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(4);
});
builder.Services.AddSingleton<SqlWeatherRepository>();
builder.Services.AddSingleton<IWeatherRepository>(services => services.GetRequiredService<SqlWeatherRepository>());
builder.Services.AddSingleton<IWeatherService>(services => new WeatherService(
    services.GetRequiredService<IOpenMeteoClient>(),
    services.GetRequiredService<IWeatherRepository>(),
    services.GetRequiredService<ILogger<WeatherService>>()));
builder.Services.AddHostedService<WeatherSnapshotCleanupService>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

await app.Services.GetRequiredService<IWeatherRepository>().InitializeAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
