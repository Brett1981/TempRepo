// Data/Models/OAuthToken.cs
namespace Sage200Microservice.Data.Models
{
    public class OAuthToken
    {
        public int Id { get; set; }                    // keep a single row (Id=1) or per tenant
        public string Provider { get; set; } = "Sage"; // future-proofing
        public string Audience { get; set; } = "s200ukipd/sage200";

        // Encrypted (see repo 'OAuthTokenStore')
        public string? ProtectedRefreshToken { get; set; }

        // Optional: you can store access token too, but not required
        public DateTimeOffset? AccessTokenExpiresUtc { get; set; }

        public DateTimeOffset UpdatedUtc { get; set; }
        public string? Scope { get; set; }
    }
}
