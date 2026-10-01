using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebApplication1.Model;
using WebApplication1.TokenService;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries =
        [
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        ];

        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }
    }
    [ApiController]
    [Route("api/github")]
    public class GitHubGraphQLProxyController : ControllerBase
    {
        private readonly GitHubAppTokenService _tokenService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public GitHubGraphQLProxyController(
            GitHubAppTokenService tokenService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _tokenService = tokenService;
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        /// <summary>
        /// Relays an incoming GraphQL query directly to GitHub GraphQL API using the App Installation Token.
        /// </summary>
        [HttpPost("graphql/{installationId}")]
        public async Task<IActionResult> RelayGraphQL(
            long installationId,
            [FromBody] GraphQLRequestDto requestDto)
        {
            if (string.IsNullOrWhiteSpace(requestDto.Query))
            {
                return BadRequest(new { error = "GraphQL query string must not be empty." });
            }

            string appId = _config["GitHubApp:AppId"]!;
            string privateKeyPem = _config["GitHubApp:PrivateKeyPem"]!;

            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(privateKeyPem))
            {
                return StatusCode(500, new { error = "GitHubApp settings are missing in appsettings.json." });
            }

            try
            {
                // 1. Get cached or fresh installation token
                string accessToken = await _tokenService.GetInstallationAccessTokenAsync(appId, privateKeyPem, installationId);

                // 2. Prepare payload and proxy request
                var client = _httpClientFactory.CreateClient();
                var outboundRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://api.github.com/graphql"
                );

                outboundRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                outboundRequest.Headers.UserAgent.ParseAdd("DotNetGitHubAppRelay");

                string jsonPayload = JsonSerializer.Serialize(requestDto, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                outboundRequest.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 3. Execute request against GitHub
                var response = await client.SendAsync(outboundRequest);

                // 4. Stream response body back to caller preserving status code
                var responseStream = await response.Content.ReadAsStreamAsync();
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";

                // Set the HTTP status code on the outgoing response, then return the FileStreamResult.
                Response.StatusCode = (int)response.StatusCode;
                return new FileStreamResult(responseStream, mediaType);
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(502, new { error = "Upstream request to GitHub failed.", details = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "An internal server error occurred.", details = ex.Message });
            }
        }
    }
}
