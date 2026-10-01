using System.Text.Json.Serialization;

namespace WebApplication1.Model
{
    public class GraphQLRequestDto
    {
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        [JsonPropertyName("variables")]
        public object? Variables { get; set; }

        [JsonPropertyName("operationName")]
        public string? OperationName { get; set; }
    }

    public class InstallationTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public DateTime ExpiresAt { get; set; }
    }
}
