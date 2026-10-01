using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using WebApplication1.Model;

namespace WebApplication1.TokenService
{
    public class GitHubAppTokenService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;

        public GitHubAppTokenService(IHttpClientFactory httpClientFactory, IMemoryCache cache)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
        }

        /// <summary>
        /// Generates a signed JWT valid for 10 minutes.
        /// </summary>
        public string GenerateGitHubAppJwt(string appId, string privateKeyPem)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem.ToCharArray());

            var securityKey = new RsaSecurityKey(rsa);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

            var now = DateTimeOffset.UtcNow;

            // Subtracting 60 seconds from IssuedAt accounts for server clock skew
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = appId,
                IssuedAt = now.AddSeconds(-60).UtcDateTime,
                Expires = now.AddMinutes(10).UtcDateTime,
                SigningCredentials = credentials
            };

            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var token = handler.CreateToken(descriptor);

            return handler.WriteToken(token);
        }

        /// <summary>
        /// Fetches an Installation Access Token, utilizing IMemoryCache to avoid unnecessary GitHub API calls.
        /// </summary>
        public async Task<string> GetInstallationAccessTokenAsync(string appId, string privateKeyPem, long installationId)
        {
            string cacheKey = $"github_inst_token_{installationId}";

            if (_cache.TryGetValue(cacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
            {
                return cachedToken;
            }

            string jwt = GenerateGitHubAppJwt(appId, privateKeyPem);

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.github.com/app/installations/{installationId}/access_tokens"
            );

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.UserAgent.ParseAdd("DotNetGitHubAppRelay");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                string errBody = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"GitHub token request failed [{response.StatusCode}]: {errBody}");
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<InstallationTokenResponse>();

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.Token))
            {
                throw new InvalidOperationException("Failed to parse GitHub installation access token.");
            }

            // Cache token slightly shorter than its actual expiration (5 minutes buffer) to avoid edge-case expiry during requests
            var cacheExpiry = tokenResponse.ExpiresAt.AddMinutes(-5);
            if (cacheExpiry > DateTime.UtcNow)
            {
                _cache.Set(cacheKey, tokenResponse.Token, cacheExpiry);
            }

            return tokenResponse.Token;
        }
    }
}
