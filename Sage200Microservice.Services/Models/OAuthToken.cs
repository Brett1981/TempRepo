namespace Sage200Microservice.Services.Models
{
    /// <summary>
    /// Represents an OAuth 2.0 token response
    /// </summary>
    public class OAuthToken
    {
        /// <summary>
        /// The access token
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// The refresh token
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// The token type (usually "Bearer")
        /// </summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// The expiration time in seconds
        /// </summary>
        public int ExpiresIn { get; set; }

        /// <summary>
        /// The time when the token was acquired
        /// </summary>
        public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The scopes granted for this token
        /// </summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>
        /// Checks if the token is expired
        /// </summary>
        /// <returns> True if the token is expired </returns>
        public bool IsExpired()
        {
            // Add a 5-minute buffer to ensure we refresh before actual expiration
            return DateTime.UtcNow >= AcquiredAt.AddSeconds(ExpiresIn - 300);
        }
    }
}