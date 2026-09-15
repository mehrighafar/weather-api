using Microsoft.AspNetCore.Mvc;
using Weather.Services;

namespace Weather.Controllers;

[ApiController]
[Route("api/weather")]
[Produces("application/json")]
public sealed class WeatherController(IWeatherService weatherService) : ControllerBase
{
    private readonly IWeatherService weatherService = weatherService;

    [HttpGet]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get([FromQuery] WeatherRequest request, CancellationToken cancellationToken)
    {
        var payload = await weatherService.GetAsync(request.Latitude, request.Longitude, request.Hourly, cancellationToken);
        return payload is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Weather service unavailable.",
                Detail = "No current or previously saved weather data is available."
            })
            : Content(payload, "application/json");
    }
}
