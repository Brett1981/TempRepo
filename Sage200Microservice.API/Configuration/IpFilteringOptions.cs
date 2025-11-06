namespace Sage200Microservice.API.Configuration
{
    /// <summary>
    /// Configuration options for IP filtering
    /// </summary>
    public class IpFilteringOptions
    {
        /// <summary>
        /// Gets or sets whether IP filtering is enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the default policy (allow or deny)
        /// </summary>
        public IpFilterPolicy DefaultPolicy { get; set; } = IpFilterPolicy.Allow;

        /// <summary>
        /// Gets or sets the list of IP addresses or CIDR ranges to allow
        /// </summary>
        public List<string> AllowedIps { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the list of IP addresses or CIDR ranges to deny
        /// </summary>
        public List<string> DeniedIps { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the list of client-specific IP restrictions
        /// </summary>
        public Dictionary<string, ClientIpRestriction> ClientRestrictions { get; set; } = new Dictionary<string, ClientIpRestriction>();

        /// <summary>
        /// Gets or sets the list of endpoints to exclude from IP filtering
        /// </summary>
        public List<string> ExcludedEndpoints { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the HTTP status code to return when an IP is denied
        /// </summary>
        public int HttpStatusCode { get; set; } = 403;

        /// <summary>
        /// Gets or sets the header to use for client identification
        /// </summary>
        public string ClientIdHeader { get; set; } = "X-Api-Key";

        /// <summary>
        /// Gets or sets the header to use for real IP address (when behind proxy)
        /// </summary>
        public string RealIpHeader { get; set; } = "X-Forwarded-For";

        /// <summary>
        /// Gets or sets whether to trust the X-Forwarded-For header
        /// </summary>
        public bool TrustXForwardedFor { get; set; } = false;
    }

    /// <summary>
    /// IP filter policy
    /// </summary>
    public enum IpFilterPolicy
    {
        /// <summary>
        /// Allow all IPs except those explicitly denied
        /// </summary>
        Allow,

        /// <summary>
        /// Deny all IPs except those explicitly allowed
        /// </summary>
        Deny
    }

    /// <summary>
    /// Client-specific IP restriction
    /// </summary>
    public class ClientIpRestriction
    {
        /// <summary>
        /// Gets or sets the client ID
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Gets or sets the list of IP addresses or CIDR ranges allowed for this client
        /// </summary>
        public List<string> AllowedIps { get; set; } = new List<string>();
    }
}